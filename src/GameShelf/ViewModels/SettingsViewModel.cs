using System.Globalization;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.Views;
using DockPosition = GameShelf.Models.DockPosition;

namespace GameShelf.ViewModels;

/// <summary>What a settings change affects, so the window applies only that part.</summary>
public enum SettingChange { Behavior, DisplayMode, Hotkey, TrayIcon, Shelf, Sources, Position, Screen, DesktopLayer, Autostart }

/// <summary>What the Settings page asks its owner (the panel window) to do. An interface, so none can be left unwired.</summary>
public interface ISettingsActions
{
    void GoBack();
    /// <summary>A setting was changed by the user (already written to <see cref="Settings"/>): save and apply it.</summary>
    void SettingChanged(SettingChange change);
    void Refresh();
    void Unhide(Game game);
    void ImportShortcuts();
    void RestoreShortcuts();
    void AddGame();
    void RunSetup();
    void OpenFreeSpace();
    void OpenGameMode();
}

/// <summary>
/// The Settings page's state. <see cref="Load"/> fills it without reporting changes; afterwards a property setter
/// writes to <see cref="Settings"/> and reports a change only when the value really differs, so filling the
/// controls never counts as the user changing something.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    // The order of the choices in SettingsView.xaml.
    private static readonly DisplayMode[] Modes = [DisplayMode.Flyout, DisplayMode.Desktop];
    private static readonly int[] Delays = [0, 5, 10, 30];
    private static readonly GridDensity[] Densities = [GridDensity.Small, GridDensity.Medium, GridDensity.Large, GridDensity.List];
    private static readonly DockPosition[] Positions = [DockPosition.Left, DockPosition.Right, DockPosition.Free];
    private static readonly Hotkey[] Hotkeys = Enum.GetValues<Hotkey>();
    private static readonly string[] HotkeyNames = [.. Hotkeys.Select(Format.HotkeyLabel)];

    private Settings _settings = new();
    private bool _hotkeyFree = true;
    private bool _autostartOn;
    private bool _refreshing;
    private int _importedShortcuts;
    private IReadOnlyList<Game> _games = [];
    private string[] _screenDevices = [];

    private readonly ISettingsActions _actions;

    public SettingsViewModel(ISettingsActions actions)
    {
        _actions = actions;
        BackCommand = new(actions.GoBack);
        RefreshCommand = new(actions.Refresh);
        ImportCommand = new(actions.ImportShortcuts);
        RestoreCommand = new(actions.RestoreShortcuts);
        AddGameCommand = new(actions.AddGame);
        SetupCommand = new(actions.RunSetup);
        FreeSpaceCommand = new(actions.OpenFreeSpace);
        GameModeCommand = new(actions.OpenGameMode);
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand AddGameCommand { get; }
    public RelayCommand SetupCommand { get; }
    public RelayCommand FreeSpaceCommand { get; }
    public RelayCommand GameModeCommand { get; }

    public void Unhide(Game game) => _actions.Unhide(game);

    /// <summary>Fills the page. <paramref name="autostartOn"/> comes from the registry, the source of truth.</summary>
    public void Load(Settings settings, IReadOnlyList<Game> games, bool autostartOn, bool refreshing, int importedShortcuts,
        IReadOnlyList<MonitorInfo> monitors, string? currentMonitor, bool hotkeyFree)
    {
        _settings = settings;
        _games = games;
        _autostartOn = autostartOn;
        _refreshing = refreshing;
        _importedShortcuts = importedShortcuts;
        _hotkeyFree = hotkeyFree;
        LoadScreens(monitors, currentMonitor);
        HiddenGames = [.. games.Where(g => g.Hidden).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)];
        OnAllPropertiesChanged();
    }

    /// <summary>Applies a user's change: writes it (done by the caller), refreshes what depends on it, tells the owner.</summary>
    private void Changed(SettingChange change, params string[] alsoChanged)
    {
        foreach (var name in alsoChanged) OnPropertyChanged(name);
        _actions.SettingChanged(change);
    }

    /// <summary>A two-way choice backed by a setting: notifies and reports only a real change.</summary>
    private void Set<T>(T current, T value, Action<T> write, SettingChange change, string[] alsoChanged, string name)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        write(value);
        OnPropertyChanged(name);
        Changed(change, alsoChanged);
    }

    private static int IndexOf<T>(T[] options, T value) => Array.IndexOf(options, value);

    /// <summary>The value at a selected index; null for "nothing selected" (-1), which never changes a setting.</summary>
    private static bool TryPick<T>(T[] options, int index, out T value)
    {
        value = index >= 0 && index < options.Length ? options[index] : default!;
        return index >= 0 && index < options.Length;
    }

    // --- Opening Game Shelf ------------------------------------------------------------------------------

    public int ModeIndex
    {
        get => IndexOf(Modes, _settings.DisplayMode);
        set
        {
            if (TryPick(Modes, value, out var mode))
                Set(_settings.DisplayMode, mode, v => _settings.DisplayMode = v, SettingChange.DisplayMode, [nameof(ModeText)], nameof(ModeIndex));
        }
    }

    public string ModeText => _settings.DisplayMode == DisplayMode.Desktop ? "On the desktop" : "Flyout from the notification area";

    public int HotkeyIndex
    {
        get => IndexOf(Hotkeys, _settings.Hotkey);
        set
        {
            if (TryPick(Hotkeys, value, out var hotkey))
                Set(_settings.Hotkey, hotkey, v => _settings.Hotkey = v, SettingChange.Hotkey, [nameof(HotkeyTaken), nameof(HotkeyToolTip)], nameof(HotkeyIndex));
        }
    }

    /// <summary>Only a problem gets a line on the card; the rest is the tooltip.</summary>
    public IReadOnlyList<string> HotkeyLabels => HotkeyNames;
    public bool HotkeyTaken => _settings.Hotkey != Hotkey.Off && !_hotkeyFree;
    public string? HotkeyToolTip => _settings.Hotkey == Hotkey.Off ? null
        : HotkeyTaken ? "Taken by another app: choose a different one" : "Free, not used by Windows or other apps";

    public bool TrayIcon
    {
        get => _settings.TrayIcon;
        set => Set(_settings.TrayIcon, value, v => _settings.TrayIcon = v, SettingChange.TrayIcon, [], nameof(TrayIcon));
    }

    public bool CloseOnClickAway
    {
        get => _settings.CloseOnClickAway;
        set => Set(_settings.CloseOnClickAway, value, v => _settings.CloseOnClickAway = v, SettingChange.Behavior, [], nameof(CloseOnClickAway));
    }

    private static readonly string[] StayReadyTexts = [nameof(StayReadyText), nameof(KeepLoadedDetail), nameof(KeepLoadedEnabled)];

    public bool StayReady
    {
        get => _settings.StayReady;
        set => Set(_settings.StayReady, value, v => _settings.StayReady = v, SettingChange.Behavior, StayReadyTexts, nameof(StayReady));
    }

    /// <summary>0 = "Open faster" (keep it loaded), 1 = "Use less memory".</summary>
    public int KeepLoadedIndex
    {
        get => _settings.KeepPanelLoaded ? 0 : 1;
        set
        {
            if (value is 0 or 1)
                Set(_settings.KeepPanelLoaded, value == 0, v => _settings.KeepPanelLoaded = v, SettingChange.Behavior, StayReadyTexts, nameof(KeepLoadedIndex));
        }
    }

    /// <summary>The choice only matters while it stays ready in the background.</summary>
    public bool KeepLoadedEnabled => _settings.StayReady;

    public string KeepLoadedDetail => !_settings.StayReady
        ? "Only while it stays ready in the background"
        : _settings.KeepPanelLoaded
            ? "Opens at once; stays loaded (up to ~200 MB, which Windows reclaims first). Games aren't affected: it exits while you play."
            : "Frees all but a few MB, so it opens a moment slower. Games aren't affected: it exits while you play.";

    public string StayReadyText => !_settings.StayReady ? "Off: closing it ends Game Shelf"
        : _settings.KeepPanelLoaded ? "Opens faster while it's closed"
        : "Uses less memory while it's closed";

    /// <summary>The registry is the source of truth; the owner reads it back after trying the change.</summary>
    public bool StartWithWindows
    {
        get => _autostartOn;
        set
        {
            if (value == _autostartOn) return;
            _autostartOn = value;
            _settings.StartWithWindows = value;
            OnPropertyChanged();
            Changed(SettingChange.Autostart);
        }
    }

    // --- When a game starts ------------------------------------------------------------------------------

    public int DelayIndex
    {
        get => IndexOf(Delays, _settings.HideDelaySeconds);
        set
        {
            if (TryPick(Delays, value, out var delay))
                Set(_settings.HideDelaySeconds, delay, v => _settings.HideDelaySeconds = v, SettingChange.Behavior, [], nameof(DelayIndex));
        }
    }

    public bool ComeBackOnClose
    {
        get => _settings.ComeBackOnClose;
        set => Set(_settings.ComeBackOnClose, value, v => _settings.ComeBackOnClose = v, SettingChange.Behavior, [], nameof(ComeBackOnClose));
    }

    public string GameModeText => _settings.GameModeEnabled ? "On" : "Off";

    // --- Shelf --------------------------------------------------------------------------------------------

    public int DensityIndex
    {
        get => IndexOf(Densities, _settings.GridDensity);
        set
        {
            if (TryPick(Densities, value, out var density))
                Set(_settings.GridDensity, density, v => _settings.GridDensity = v, SettingChange.Shelf, [], nameof(DensityIndex));
        }
    }

    public bool ShowWeeklySummary
    {
        get => _settings.ShowWeeklySummary;
        set => Set(_settings.ShowWeeklySummary, value, v => _settings.ShowWeeklySummary = v, SettingChange.Shelf, [], nameof(ShowWeeklySummary));
    }

    // --- Library ------------------------------------------------------------------------------------------

    /// <summary>The sources in the order of their check boxes; the count in <see cref="LibraryText"/> follows this list.</summary>
    private static readonly GameSource[] Sources =
    [
        GameSource.Steam, GameSource.Epic, GameSource.Gog, GameSource.Ea, GameSource.Ubisoft,
        GameSource.Xbox, GameSource.Rockstar, GameSource.Hytale, GameSource.Shortcut,
    ];

    private bool GetSource(GameSource source) => _settings.IsSourceEnabled(source);

    private void SetSource(GameSource source, bool on, string name) =>
        Set(GetSource(source), on, v => _settings.SetSourceEnabled(source, v), SettingChange.Sources, [nameof(LibraryText)], name);

    public bool SteamEnabled { get => GetSource(GameSource.Steam); set => SetSource(GameSource.Steam, value, nameof(SteamEnabled)); }
    public bool EpicEnabled { get => GetSource(GameSource.Epic); set => SetSource(GameSource.Epic, value, nameof(EpicEnabled)); }
    public bool GogEnabled { get => GetSource(GameSource.Gog); set => SetSource(GameSource.Gog, value, nameof(GogEnabled)); }
    public bool EaEnabled { get => GetSource(GameSource.Ea); set => SetSource(GameSource.Ea, value, nameof(EaEnabled)); }
    public bool UbisoftEnabled { get => GetSource(GameSource.Ubisoft); set => SetSource(GameSource.Ubisoft, value, nameof(UbisoftEnabled)); }
    public bool XboxEnabled { get => GetSource(GameSource.Xbox); set => SetSource(GameSource.Xbox, value, nameof(XboxEnabled)); }
    public bool RockstarEnabled { get => GetSource(GameSource.Rockstar); set => SetSource(GameSource.Rockstar, value, nameof(RockstarEnabled)); }
    public bool HytaleEnabled { get => GetSource(GameSource.Hytale); set => SetSource(GameSource.Hytale, value, nameof(HytaleEnabled)); }
    public bool ShortcutsEnabled { get => GetSource(GameSource.Shortcut); set => SetSource(GameSource.Shortcut, value, nameof(ShortcutsEnabled)); }

    public string LibraryText
    {
        get
        {
            if (_refreshing) return "Scanning your stores…";
            int shown = _games.Count(g => !g.Hidden && _settings.IsSourceEnabled(g.Source));
            int on = Sources.Count(GetSource);
            return $"{on} of {Sources.Length} on · {shown} games";
        }
    }

    public bool CanRefresh => !_refreshing;

    public string ShortcutsText => _importedShortcuts switch
    {
        0 => "None imported",
        1 => "1 imported",
        _ => $"{_importedShortcuts} imported",
    };

    public bool CanRestoreShortcuts => _importedShortcuts > 0;

    public IReadOnlyList<Game> HiddenGames { get; private set; } = [];
    public string HiddenCountText => HiddenGames.Count == 0 ? "None" : $"{HiddenGames.Count} hidden";
    public bool NoHiddenGames => HiddenGames.Count == 0;

    // --- Window -------------------------------------------------------------------------------------------

    public int PositionIndex
    {
        get => IndexOf(Positions, _settings.Position);
        set
        {
            if (TryPick(Positions, value, out var position))
                Set(_settings.Position, position, v => _settings.Position = v, SettingChange.Position, [nameof(PositionIsFree)], nameof(PositionIndex));
        }
    }

    public bool PositionIsFree => _settings.Position == DockPosition.Free;

    /// <summary>One entry per monitor, left to right; the choice only shows with more than one monitor.</summary>
    public IReadOnlyList<string> Screens { get; private set; } = [];
    public bool ShowScreens => Screens.Count > 1;
    private int _screenIndex = -1;

    public int ScreenIndex
    {
        get => _screenIndex;
        set
        {
            if (value == _screenIndex || value < 0 || value >= _screenDevices.Length) return;
            _screenIndex = value;
            _settings.Monitor = _screenDevices[value];
            OnPropertyChanged();
            Changed(SettingChange.Screen);
        }
    }

    private void LoadScreens(IReadOnlyList<MonitorInfo> monitors, string? current)
    {
        var ordered = monitors.Count < 2 ? [] : monitors.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToArray();
        Screens = [.. ordered.Select((m, i) =>
        {
            var number = (i + 1).ToString(CultureInfo.CurrentCulture);
            return $"{(m.IsPrimary ? number + " (main)" : number)} · {m.Bounds.Width} × {m.Bounds.Height}";
        })];
        _screenDevices = [.. ordered.Select(m => m.Device)];
        _screenIndex = Array.FindIndex(_screenDevices, d => string.Equals(d, current, StringComparison.OrdinalIgnoreCase));
    }

    public bool StayOnDesktop
    {
        get => _settings.StayOnDesktop;
        set => Set(_settings.StayOnDesktop, value, v => _settings.StayOnDesktop = v, SettingChange.DesktopLayer, [], nameof(StayOnDesktop));
    }
}
