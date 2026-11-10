using GameShelf.Models;
using GameShelf.Shared;
using GameShelf.Views;

namespace GameShelf.ViewModels;

/// <summary>What Windows looks like now, read by the owner when the page opens or after Allow/Remove (never watched).</summary>
public sealed record GameModeStatus(
    bool Allowed,
    bool Outdated,
    bool CanAllow,
    string PowerNow,
    bool PowerFastest,
    bool GameBarRecording,
    string[] VoiceApps,
    IReadOnlyDictionary<string, ServiceState.Kind> Services,
    WindowsGamingStatus Windows);

/// <summary>Windows' own gaming settings and OneDrive, as they are now (changed only in Windows Settings).</summary>
public sealed record WindowsGamingStatus(
    bool GameModeOn,
    bool HasWindowedOptimizations,
    bool WindowedOptimizationsOn,
    bool OneDriveRunning,
    bool DocumentsInOneDrive);

/// <summary>The last restore and whether one can run now.</summary>
public sealed record RestoreStatus((DateTimeOffset When, RestoreResult Result)? Last, bool HasLog, bool CanRestoreNow, bool Pending);

/// <summary>An app with a window, to pick from a menu.</summary>
public sealed record AppChoice(string Path, string Name);

/// <summary>One app closed (or calmed: efficiency mode) while playing, as its row shows it.</summary>
public sealed record CloseAppRow(string Path, string Name, string Detail, bool Calm);

/// <summary>One service the helper can pause, as its row shows it.</summary>
public sealed record ServiceRow(string Id, string Name, string Detail, bool IsOn);

/// <summary>What the Game mode page asks its owner to do, and what it reads from Windows. An interface, so none can be left unwired.</summary>
public interface IGameModeActions
{
    void GoBack();
    /// <summary>A setting was changed by the user (already written to <see cref="Settings"/>): save it.</summary>
    void SettingsChanged();
    GameModeStatus ReadStatus();
    RestoreStatus ReadRestore();
    /// <summary>The apps with a window right now (read on click), the user's games left out.</summary>
    IReadOnlyList<AppChoice> RunningApps();
    /// <summary>The full paths of the running programs, to tell which closed apps run now.</summary>
    IReadOnlySet<string> RunningPaths();
    string AppName(string path);
    bool AppExists(string path);
    /// <summary>Installs or updates the helper (one UAC prompt); its exit code.</summary>
    Task<int> AllowAsync();
    Task RemoveAsync();
    /// <summary>Starts putting everything back; false when it can't now.</summary>
    bool RestoreNow();
    void OpenLog();
    void PlayRestoredSound();
    /// <summary>Opens a page of Windows Settings (an <c>ms-settings:</c> URI).</summary>
    void OpenWindowsSettings(string uri);
}

/// <summary>
/// Settings › Game mode (spec §11, board 5). <see cref="Show"/> fills it without reporting changes; afterwards a
/// switch or list change writes to <see cref="Settings"/> and asks the owner to save, only when it really changed.
/// The state of Windows (power mode, Game Bar, services, running apps, the last restore) is read once when the page
/// opens or after a restore, never watched.
/// </summary>
public sealed partial class GameModeViewModel : ObservableObject
{
    private static readonly Dictionary<string, string> ServiceDetails = new()
    {
        [GameModeInfo.WindowsSearch] = Strings.ServiceSearchDetail,
        [GameModeInfo.SysMain] = Strings.ServiceSysMainDetail,
        [GameModeInfo.Telemetry] = Strings.ServiceTelemetryDetail,
        [GameModeInfo.PrintSpooler] = Strings.ServiceSpoolerDetail,
    };

    private readonly IGameModeActions _actions;
    private Settings _settings = new();
    private GameModeStatus _status = new(false, false, false, "", false, false, [], new Dictionary<string, ServiceState.Kind>(),
        new(false, false, false, false, false));
    private RestoreStatus _restore = new(null, false, false, false);
    private bool _fromShelf;
    private bool _allowRunning, _removeRunning, _restoring;
    private int? _allowFailed;

    public GameModeViewModel(IGameModeActions actions)
    {
        _actions = actions;
        BackCommand = new(actions.GoBack);
        LogCommand = new(actions.OpenLog);
        RestoreCommand = new(Restore);
        OpenGameModeSettingsCommand = new(() => actions.OpenWindowsSettings("ms-settings:gaming-gamemode"));
        OpenGraphicsSettingsCommand = new(() => actions.OpenWindowsSettings("ms-settings:display-advancedgraphics"));
        OpenNotificationSettingsCommand = new(() => actions.OpenWindowsSettings("ms-settings:notifications"));
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand LogCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand OpenGameModeSettingsCommand { get; }
    public RelayCommand OpenGraphicsSettingsCommand { get; }
    public RelayCommand OpenNotificationSettingsCommand { get; }

    /// <summary>Fills the page and reads the state of Windows once.</summary>
    public void Show(Settings settings, bool fromShelf)
    {
        _settings = settings;
        _fromShelf = fromShelf;
        _status = _actions.ReadStatus();
        _allowFailed = null;
        _restoring = false;
        _restore = _actions.ReadRestore();
        FillApps();
        FillServices();
        OnAllPropertiesChanged();
    }

    /// <summary>A restore just finished (the toast is up): show its result and whether anything is still pending.</summary>
    public void UpdateLastRestore()
    {
        _restoring = false;
        _restore = _actions.ReadRestore();
        OnPropertiesChanged(RestoreNames);
    }

    private void OnPropertiesChanged(string[] names)
    {
        foreach (var name in names) OnPropertyChanged(name);
    }

    private void Save() => _actions.SettingsChanged();

    /// <summary>Back goes to the shelf (opened from its "…" menu) or to Settings; the button says which.</summary>
    public string BackName => _fromShelf ? Strings.BackToShelf : Strings.BackToSettings;
    public string BackToolTip => Strings.Format(Strings.WithEsc, BackName);

    // ---- Switches -----------------------------------------------------------------------------------------

    public bool MasterOn
    {
        get => _settings.GameModeEnabled;
        set
        {
            if (_settings.GameModeEnabled == value) return;
            _settings.GameModeEnabled = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool IsItemOn(string id) => _settings.GameModeItems.Contains(id);

    /// <summary>Turns one of Game mode's items on or off (the switches, and the services' rows).</summary>
    public void SetItem(string id, bool on, string? name = null)
    {
        if (IsItemOn(id) == on) return;
        _settings.GameModeItems.Remove(id);
        if (on) _settings.GameModeItems.Add(id);
        if (name is not null) OnPropertyChanged(name);
        Save();
    }

    public bool PowerOn { get => IsItemOn(GameModeInfo.Power); set => SetItem(GameModeInfo.Power, value, nameof(PowerOn)); }
    public bool PriorityOn { get => IsItemOn(GameModeInfo.Priority); set => SetItem(GameModeInfo.Priority, value, nameof(PriorityOn)); }
    public bool GameBarOn { get => IsItemOn(GameModeInfo.GameBar); set => SetItem(GameModeInfo.GameBar, value, nameof(GameBarOn)); }
    public bool OneDriveOn { get => IsItemOn(GameModeInfo.OneDrive); set => SetItem(GameModeInfo.OneDrive, value, nameof(OneDriveOn)); }
    public bool SmoothOn { get => IsItemOn(GameModeInfo.Smooth); set => SetItem(GameModeInfo.Smooth, value, nameof(SmoothOn)); }
    public bool UploadsOn { get => IsItemOn(GameModeInfo.Uploads); set => SetItem(GameModeInfo.Uploads, value, nameof(UploadsOn)); }
    public bool UpdatesOn { get => IsItemOn(GameModeInfo.Updates); set => SetItem(GameModeInfo.Updates, value, nameof(UpdatesOn)); }

    public bool ToastOn
    {
        get => _settings.ShowRestoreToast;
        set
        {
            if (_settings.ShowRestoreToast == value) return;
            _settings.ShowRestoreToast = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SoundEnabled)); // the sound comes with the confirmation
            Save();
        }
    }

    public bool SoundEnabled => _settings.ShowRestoreToast;

    public bool SoundOn
    {
        get => _settings.RestoreSound;
        set
        {
            if (_settings.RestoreSound == value) return;
            _settings.RestoreSound = value;
            OnPropertyChanged();
            if (value) _actions.PlayRestoredSound(); // so the user hears what they turned on
            Save();
        }
    }

    /// <summary>The upload limit, in the order of the ComboBox's items (1, 5, 20 Mbit/s); an unknown value shows the default.</summary>
    public int LimitIndex
    {
        get
        {
            int index = Array.IndexOf(ElevatedPart.UploadLimits, _settings.UploadLimitMbps);
            return index >= 0 ? index : Array.IndexOf(ElevatedPart.UploadLimits, ElevatedPart.DefaultUploadLimit);
        }
        set
        {
            if (value < 0 || value >= ElevatedPart.UploadLimits.Length || ElevatedPart.UploadLimits[value] == _settings.UploadLimitMbps) return;
            _settings.UploadLimitMbps = ElevatedPart.UploadLimits[value];
            OnPropertyChanged();
            Save();
        }
    }

    // ---- What Windows does now ----------------------------------------------------------------------------

    public string PowerText => _status.PowerFastest
        ? Strings.Format(Strings.GmPowerFastest, _status.PowerNow)
        : Strings.Format(Strings.GmPowerSwitch, _status.PowerNow);

    public string GameBarText => Strings.Format(Strings.GmGameBarText, Strings.LowerFirst(_status.GameBarRecording ? Strings.On : Strings.Off));

    public string OneDriveText => _status.Windows.DocumentsInOneDrive
        ? Strings.GmOneDriveDocuments
        : Strings.Format(Strings.GmOneDriveText, _status.Windows.OneDriveRunning ? Strings.GmRunningLower : Strings.GmNotRunningLower);

    // ---- Windows' own settings (read when the page opens; changed only in Windows Settings) ----------------

    public string WinGameModeText => _status.Windows.GameModeOn
        ? Strings.GmWinGameModeOn
        : Strings.GmWinGameModeOff;
    public string WinGameModeState => _status.Windows.GameModeOn ? Strings.On : Strings.Off;

    /// <summary>"Optimizations for windowed games" exists on Windows 11 only.</summary>
    public bool WindowedVisible => _status.Windows.HasWindowedOptimizations;
    public string WindowedText => _status.Windows.WindowedOptimizationsOn
        ? Strings.GmWindowedOn
        : Strings.GmWindowedOff;
    public string WindowedState => _status.Windows.WindowedOptimizationsOn ? Strings.On : Strings.Off;

    public string UpdatesText => Strings.Format(Strings.GmUpdatesText, ElevatedPart.UpdatePercent);

    public string SmoothText => Strings.Format(Strings.GmSmoothText, Strings.List([Strings.GmTheGame, .. _status.VoiceApps]));

    // ---- The admin part -----------------------------------------------------------------------------------

    private static readonly string[] AdminNames =
    [
        nameof(HelperAllowed), nameof(AllowBarOpen), nameof(AllowMessage), nameof(AllowWarning), nameof(AllowText),
        nameof(ShowAllow), nameof(AllowEnabled), nameof(RemoveEnabled), nameof(Services), nameof(SmoothText),
        nameof(SmoothApps), nameof(UploadApps),
    ];

    /// <summary>The helper is installed: the services and network items can be used.</summary>
    public bool HelperAllowed => _status.Allowed;
    private bool Outdated => _status.Allowed && _status.Outdated;
    public bool AllowBarOpen => !_status.Allowed || Outdated;
    public bool AllowWarning => _allowFailed is not null;
    public string AllowMessage => _allowFailed is { } code
        ? Strings.Format(Strings.GmAllowFailed, code, @"ProgramData\GameShelf\elevated.log")
        : Outdated
            ? Strings.GmHelperOutdated
            : _status.CanAllow
                ? Strings.GmHelperExplain
                : Strings.GmHelperNeedsAdmin;
    public string AllowText => Outdated ? Strings.UpdateEllipsis : Strings.AllowEllipsis;
    public bool ShowAllow => _status.CanAllow;
    public bool AllowEnabled => !_allowRunning;
    public bool RemoveEnabled => !_removeRunning;

    /// <summary>Installs (or updates) the helper; Windows asks once. Declining the prompt (1223) isn't an error.</summary>
    public async Task AllowAsync()
    {
        _allowRunning = true;
        _allowFailed = null;
        OnPropertyChanged(nameof(AllowEnabled));
        int code;
        try
        {
            code = await _actions.AllowAsync();
        }
        finally
        {
            _allowRunning = false;
        }
        _status = _actions.ReadStatus();
        if (code is not (0 or 1223)) _allowFailed = code;
        FillServices();
        OnPropertiesChanged(AdminNames);
    }

    public async Task RemoveAsync()
    {
        _removeRunning = true;
        OnPropertyChanged(nameof(RemoveEnabled));
        try
        {
            await _actions.RemoveAsync();
        }
        finally
        {
            _removeRunning = false;
        }
        _allowFailed = null;
        _status = _actions.ReadStatus();
        FillServices();
        OnPropertiesChanged(AdminNames);
    }

    /// <summary>The helper's services with their state now; drawn by the view.</summary>
    public ServiceRow[] Services { get; private set; } = [];

    private void FillServices() =>
        Services = [.. ElevatedPart.Services.Select(s => new ServiceRow(s.Id, GameModeText.ItemName(s.Name),
            Strings.Format(Strings.GmServiceNow, ServiceDetails[s.Id], (_status.Services.TryGetValue(s.Id, out var state) ? state : ServiceState.Kind.Missing) switch
            {
                ServiceState.Kind.Running => Strings.GmRunningLower,
                ServiceState.Kind.Stopped => Strings.GmServiceStopped,
                ServiceState.Kind.Disabled => Strings.GmServiceDisabled,
                _ => Strings.GmServiceMissing,
            }),
            IsItemOn(s.Id)))];

    // ---- App lists ----------------------------------------------------------------------------------------

    /// <summary>The apps closed or calmed while playing.</summary>
    public CloseAppRow[] CloseApps { get; private set; } = [];

    private IReadOnlySet<string> _running = new HashSet<string>();

    private void FillApps()
    {
        _running = _settings.GameModeCloseApps.Count > 0 ? _actions.RunningPaths() : new HashSet<string>();
        CloseApps = [.. _settings.GameModeCloseApps.Select(path => Row(path, IsCalmed(path)))];
    }

    private bool IsCalmed(string path) => _settings.GameModeCalmApps.Contains(path, StringComparer.OrdinalIgnoreCase);

    private CloseAppRow Row(string path, bool calm) => new(path, _actions.AppName(path),
        !_actions.AppExists(path) ? Strings.GmAppNotFound
        : !_running.Contains(path) ? Strings.GmAppNotRunning
        : calm ? Strings.GmAppCalmed
        : Strings.GmAppClosed,
        calm);

    /// <summary>
    /// Close the app (reopened after) or Calm it (efficiency mode, it keeps running). Returns the row as it is now;
    /// the list itself isn't redrawn, so the choice keeps the focus.
    /// </summary>
    public CloseAppRow SetCalm(string path, bool calm)
    {
        int index = Array.FindIndex(CloseApps, r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (IsCalmed(path) != calm)
        {
            _settings.GameModeCalmApps.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (calm) _settings.GameModeCalmApps.Add(path);
            Save();
        }
        var row = Row(path, calm);
        if (index >= 0) CloseApps[index] = row;
        return row;
    }

    /// <summary>Apps with a window that aren't closed while playing yet.</summary>
    public IReadOnlyList<AppChoice> AppsToClose() =>
        [.. _actions.RunningApps().Where(a => !_settings.GameModeCloseApps.Contains(a.Path, StringComparer.OrdinalIgnoreCase))];

    public void AddCloseApp(string path)
    {
        _settings.GameModeCloseApps.Add(path);
        Save();
        FillApps();
        OnPropertyChanged(nameof(CloseApps));
    }

    public void RemoveCloseApp(string path)
    {
        _settings.GameModeCloseApps.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _settings.GameModeCalmApps.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Save();
        FillApps();
        OnPropertyChanged(nameof(CloseApps));
    }

    /// <summary>The apps kept smooth, and those whose uploads are slowed: exe names, as QoS matches them.</summary>
    public string[] SmoothApps => [.. _settings.KeepSmoothApps];
    public string[] UploadApps => [.. _settings.ThrottledUploadApps];

    /// <summary>Running apps with a window that can be added to a name list (plain exe names only, not already in it).</summary>
    public IReadOnlyList<AppChoice> AppsFor(bool smooth)
    {
        var names = smooth ? _settings.KeepSmoothApps : _settings.ThrottledUploadApps;
        return [.. _actions.RunningApps().Where(a => ElevatedPart.IsAppName(Path.GetFileName(a.Path)) &&
                                                      !names.Contains(Path.GetFileName(a.Path), StringComparer.OrdinalIgnoreCase))];
    }

    public void AddName(bool smooth, string path)
    {
        (smooth ? _settings.KeepSmoothApps : _settings.ThrottledUploadApps).Add(Path.GetFileName(path));
        Save();
        OnPropertyChanged(smooth ? nameof(SmoothApps) : nameof(UploadApps));
    }

    public void RemoveName(bool smooth, string exe)
    {
        (smooth ? _settings.KeepSmoothApps : _settings.ThrottledUploadApps).RemoveAll(n => string.Equals(n, exe, StringComparison.OrdinalIgnoreCase));
        Save();
        OnPropertyChanged(smooth ? nameof(SmoothApps) : nameof(UploadApps));
    }

    // ---- The last restore ---------------------------------------------------------------------------------

    private static readonly string[] RestoreNames =
        [nameof(LastTitle), nameof(LastDetail), nameof(LastOk), nameof(HasLog), nameof(CanRestore), nameof(RestoreText)];

    public string LastTitle => _restore.Last is { } last ? Strings.Format(Strings.GmLastRestore, Format.SessionStart(last.When)) : Strings.GmNothingRestored;

    public string LastDetail => _restore.Last is not { } last ? Strings.GmUndoneOnClose
        : last.Result.Success
            ? Strings.Format(last.Result.Total == 1 ? Strings.GmPutBackOfOne : Strings.GmPutBackOf, last.Result.Restored, last.Result.Total)
            : Strings.Format(Strings.GmPutBackOfFailed, last.Result.Restored, last.Result.Total, Strings.List([.. last.Result.Failed.Select(GameModeText.ItemName)]));

    /// <summary>True: all put back; false: something couldn't be; null: nothing restored yet.</summary>
    public bool? LastOk => _restore.Last?.Result.Success;

    public bool HasLog => _restore.HasLog;
    public bool CanRestore => !_restoring && _restore.CanRestoreNow;
    public string RestoreText => _restoring ? Strings.Restoring // the result comes back with the toast (UpdateLastRestore)
        : _restore.CanRestoreNow ? Strings.RestoreNow
        : _restore.Pending ? Strings.Format(Strings.Dot, Strings.RestoreNow, Strings.GmGameRunning)
        : Strings.Format(Strings.Dot, Strings.RestoreNow, Strings.GmNothingPending);

    private void Restore()
    {
        if (!_actions.RestoreNow()) return;
        _restoring = true;
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(RestoreText));
    }
}
