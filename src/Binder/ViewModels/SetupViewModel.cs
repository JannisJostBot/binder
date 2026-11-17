using Binder.Library;
using Binder.Models;
using Binder.Views;

namespace Binder.ViewModels;

/// <summary>One launcher in setup step 1; its tick is kept until step 1's Next applies it.</summary>
public sealed class SetupLauncherRow(GameSource source, string name, string count, bool enabled)
{
    public GameSource Source { get; } = source;
    public string Name { get; } = name;
    public string Count { get; } = count;
    public bool Enabled { get; set; } = enabled;
}

/// <summary>Step 4: Game mode off, on with only the items that need no admin rights, or on with all (the helper allowed).</summary>
public enum SetupGameMode { Off, NoAdmin, All }

/// <summary>The setup's answers, applied by the window.</summary>
public sealed record SetupChoices(DisplayMode Mode, bool StartWithWindows, SetupGameMode GameMode = SetupGameMode.Off);

/// <summary>One line of step 4's "what it changes" list; <see cref="NeedsAdmin"/> lines get the shield.</summary>
public sealed record SetupGameModeItem(string Text, bool NeedsAdmin);

/// <summary>What first-run setup asks its owner to do.</summary>
public interface ISetupActions
{
    /// <summary>Every launcher Binder reads, in its fixed order.</summary>
    IReadOnlyList<GameSource> Launchers { get; }
    /// <summary>"Skip setup": keep the defaults.</summary>
    void Skip();
    /// <summary>Step 2 needs the desktop's shortcuts (scanned off the UI thread; they come back through <see cref="SetupViewModel.SetShortcuts"/>).</summary>
    void LoadShortcuts();
    /// <summary>Moves the picked shortcuts onto the shelf; <see cref="SetupViewModel.ImportDone"/> goes on to step 3.</summary>
    void Import(IReadOnlyList<ShortcutFile> picked);
    void Finish(SetupChoices choices);
    /// <summary>This account can allow Game mode's admin helper (it's in the Administrators group).</summary>
    bool CanAllowHelper { get; }
    /// <summary>The helper is installed for this user and current (allowing it again would ask for nothing).</summary>
    bool HelperReady { get; }
    /// <summary>
    /// Installs or updates the helper, as Settings › Game mode › Allow… does (one UAC prompt). Returns its exit code:
    /// 0 done, 1223 the prompt was declined, anything else failed.
    /// </summary>
    Task<int> AllowHelperAsync();
}

/// <summary>
/// First-run setup, four steps (spec §6, board 10): which launchers, tidy the desktop (the shortcut picker,
/// embedded in the view), how it opens, Game mode (with its admin helper, one UAC prompt). Shown once; Settings can
/// run it again.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly ISetupActions _actions;
    private Settings _settings = new();
    private bool _shortcutsLoaded;
    private IReadOnlyList<ShortcutFile> _selected = [];

    public SetupViewModel(ISetupActions actions)
    {
        _actions = actions;
        SkipCommand = new(actions.Skip);
        NextCommand = new(Next);
        BackToStep1Command = new(() => ShowStep(1));
        NotNowCommand = new(() => ShowStep(3));
        BackToStep2Command = new(() => ShowStep(2));
        MoveCommand = new(Move);
        ToStep4Command = new(() => ShowStep(4));
        BackToStep3Command = new(() => ShowStep(3));
        DoneCommand = new(() => _ = DoneAsync());
    }

    public RelayCommand SkipCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand BackToStep1Command { get; }
    public RelayCommand NotNowCommand { get; }
    public RelayCommand BackToStep2Command { get; }
    public RelayCommand MoveCommand { get; }
    public RelayCommand ToStep4Command { get; }
    public RelayCommand BackToStep3Command { get; }
    public RelayCommand DoneCommand { get; }

    /// <summary>Starts at step 1 with the library as found so far (the scan may still be running).</summary>
    public void Start(Settings settings, IReadOnlyList<Game> games, bool startWithWindows)
    {
        _moving = false;
        _settings = settings;
        _shortcutsLoaded = false;
        Launchers = []; // a new run starts from the settings' ticks
        _foundCounts = null;
        _startEntries = null;
        NotInStartText = "";
        Shortcuts = [];
        _selected = [];
        ImportTitle = ImportDetail = "";
        UpdateLibrary(games);
        Flyout = settings.DisplayMode == DisplayMode.Flyout;
        Desktop = settings.DisplayMode == DisplayMode.Desktop;
        StartWithWindows = startWithWindows;
        HotkeyText = Format.HotkeyLabel(settings.Hotkey);
        // Step 4 opens on Off while Game mode is off (owner, 2026-11-17): turning it on, and above all the UAC
        // prompt, is the user's own choice. A run with Game mode already on starts from what's set.
        _allowing = false;
        _allowFailed = null;
        _allowDeclined = false;
        _gameMode = !settings.GameModeEnabled ? SetupGameMode.Off
            : _actions.HelperReady ? SetupGameMode.All : SetupGameMode.NoAdmin;
        GameModeItems = [.. BuildGameModeItems()];
        FillSelection();
        Step = 0; // so step 1 counts as a change
        ShowStep(1);
        OnAllPropertiesChanged();
    }

    // ---- Steps -----------------------------------------------------------------------------------------------

    private static readonly string[] StepNames = [nameof(Step), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4)];

    public int Step { get; private set; }
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;

    private void ShowStep(int step)
    {
        if (step == Step) return;
        Step = step;
        foreach (var name in StepNames) OnPropertyChanged(name);
        if (step == 2 && !_shortcutsLoaded) _actions.LoadShortcuts();
    }

    // ---- Step 1: launchers -----------------------------------------------------------------------------------

    // Games per launcher: from the library until the window's count of every launcher (on or off) arrives.
    private Dictionary<GameSource, int> _libraryCounts = [];
    private IReadOnlyDictionary<GameSource, int>? _foundCounts;

    private static readonly string[] LauncherNames = [nameof(Launchers), nameof(FoundTitle), nameof(NothingFound)];

    public SetupLauncherRow[] Launchers { get; private set; } = [];
    public string FoundTitle { get; private set; } = "";
    public bool NothingFound { get; private set; }

    /// <summary>The library as found so far. Called again when the scan finishes.</summary>
    public void UpdateLibrary(IReadOnlyList<Game> games)
    {
        _libraryCounts = games.Where(g => Format.HasBadge(g.Source))
            .GroupBy(g => g.Source)
            .ToDictionary(group => group.Key, group => group.Count());
        _shelf = [.. games.Where(g => !g.Hidden)];
        BuildLaunchers();
        CountNotInStart();
    }

    // ---- Step 1's "N of them aren't in Start search" (spec §22 d) ----------------------------------------------

    private Game[] _shelf = [];
    private IReadOnlyList<ShortcutFile>? _startEntries;

    /// <summary>"23 of them aren't in Start search", or empty (none, or not counted yet).</summary>
    public string NotInStartText { get; private set; } = "";
    public bool HasNotInStart => NotInStartText.Length > 0;

    /// <summary>The Start menu's shortcuts, read once per run by the owner (never stored).</summary>
    public void SetStartEntries(IReadOnlyList<ShortcutFile> entries)
    {
        _startEntries = entries;
        CountNotInStart();
    }

    private void CountNotInStart()
    {
        var text = _startEntries is null ? "" : StartMenuMatch.CountMissing(_startEntries, _shelf) is > 0 and var missing
            ? Strings.Plural(missing, Strings.NotInStartOne, Strings.NotInStart)
            : "";
        if (text == NotInStartText) return;
        NotInStartText = text;
        OnPropertyChanged(nameof(NotInStartText));
        OnPropertyChanged(nameof(HasNotInStart));
    }

    /// <summary>Every launcher's games on this PC, also the ones turned off.</summary>
    public void SetFoundCounts(IReadOnlyDictionary<GameSource, int> counts)
    {
        _foundCounts = counts;
        BuildLaunchers();
    }

    /// <summary>
    /// Every launcher Binder reads: the ones with games first (most first), then the ones with none found,
    /// so none is left unmentioned. A tick changed here is kept when the counts come in.
    /// </summary>
    private void BuildLaunchers()
    {
        var previous = Launchers.ToDictionary(row => row.Source, row => row.Enabled);
        int CountOf(GameSource source) =>
            _foundCounts?.GetValueOrDefault(source) is > 0 and var found ? found : _libraryCounts.GetValueOrDefault(source);
        var counts = _actions.Launchers
            .Select(source => (Source: source, Count: CountOf(source)))
            .OrderByDescending(entry => entry.Count)
            .ToList();
        Launchers = [.. counts.Select(entry => new SetupLauncherRow(entry.Source, Format.LauncherName(entry.Source),
            entry.Count == 0 ? Strings.NoneFound : Strings.Plural(entry.Count, Strings.GameCountOne, Strings.GameCount),
            previous.TryGetValue(entry.Source, out bool ticked) ? ticked : _settings.IsSourceEnabled(entry.Source)))];
        int total = counts.Sum(entry => entry.Count);
        NothingFound = total == 0;
        FoundTitle = total == 0 ? Strings.SetupTitleEmpty : Strings.Plural(total, Strings.SetupFoundOne, Strings.SetupFound);
        foreach (var name in LauncherNames) OnPropertyChanged(name);
    }

    private void Next()
    {
        foreach (var row in Launchers) _settings.SetSourceEnabled(row.Source, row.Enabled);
        ShowStep(2);
    }

    // ---- Step 2: desktop shortcuts ---------------------------------------------------------------------------

    private static readonly string[] SelectionNames = [nameof(MoveText), nameof(CanMove), nameof(SelectionText)];

    /// <summary>The desktop's shortcuts for the picker, and the library they're matched against.</summary>
    public ShortcutFile[] Shortcuts { get; private set; } = [];
    public IReadOnlyList<Game> Library { get; private set; } = [];
    public string MoveText { get; private set; } = Strings.Move;
    public bool CanMove { get; private set; }
    public string SelectionText { get; private set; } = "";

    public void SetShortcuts(IReadOnlyList<ShortcutFile> shortcuts, IReadOnlyList<Game> library)
    {
        _shortcutsLoaded = true;
        Library = library;
        Shortcuts = [.. shortcuts];
        OnPropertyChanged(nameof(Shortcuts));
    }

    /// <summary>The picker's ticked shortcuts, whenever they change.</summary>
    public void SetSelection(IReadOnlyList<ShortcutFile> selected)
    {
        _selected = selected;
        FillSelection();
        foreach (var name in SelectionNames) OnPropertyChanged(name);
    }

    private void FillSelection()
    {
        int selected = _selected.Count;
        MoveText = selected == 0 ? Strings.Move : Strings.Format(Strings.MoveCount, selected);
        CanMove = selected > 0;
        SelectionText = selected == 0 ? Strings.NothingSelected : Strings.Plural(selected, Strings.SelectedCountOne, Strings.SelectedCount);
    }

    // A move runs until ImportDone (step 2's merge animation plays in between): a second Move (Enter on the still
    // focused button) mustn't move the same shortcuts again.
    private bool _moving;

    private void Move()
    {
        if (_moving || _selected.Count == 0) return;
        _moving = true;
        _actions.Import(_selected);
    }

    private static readonly string[] ImportNames = [nameof(ImportTitle), nameof(ImportDetail), nameof(HasImportResult)];

    /// <summary>What the move did, shown at the top of step 3 (not as the panel's notice, which would cover Done).</summary>
    public string ImportTitle { get; private set; } = "";
    public string ImportDetail { get; private set; } = "";
    public bool HasImportResult => ImportTitle.Length > 0;

    /// <summary>After the picked shortcuts were moved: on to the last step, which shows the result.</summary>
    public void ImportDone(string title, string detail)
    {
        _moving = false;
        ImportTitle = title;
        ImportDetail = detail;
        foreach (var name in ImportNames) OnPropertyChanged(name);
        ShowStep(3);
    }

    // ---- Step 3: how it opens --------------------------------------------------------------------------------

    private bool? _flyout, _desktop;
    private bool _startWithWindows;

    // bool? because a RadioButton's IsChecked is.
    public bool? Flyout { get => _flyout; set => SetProperty(ref _flyout, value); }
    public bool? Desktop { get => _desktop; set => SetProperty(ref _desktop, value); }
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }
    public string HotkeyText { get; private set; } = "";

    // ---- Step 4: Game mode ------------------------------------------------------------------------------------

    private static readonly string[] GameModeNames =
    [
        nameof(GameModeOff), nameof(GameModeNoAdmin), nameof(GameModeAll), nameof(DoneText), nameof(DoneNeedsAdmin),
        nameof(DoneEnabled), nameof(AllowDeclined), nameof(AllowFailed), nameof(AllowError), nameof(ShowAllOption),
        nameof(AllText), nameof(ShowNeedsAdmin),
    ];

    private SetupGameMode _gameMode;
    private bool _allowing, _allowDeclined;
    private int? _allowFailed;

    public SetupGameMode GameMode
    {
        get => _gameMode;
        set
        {
            if (_gameMode == value) return;
            _gameMode = value;
            _allowDeclined = false; // a message about the last try no longer fits another choice
            _allowFailed = null;
            OnPropertiesChanged(GameModeNames);
        }
    }

    // bool? because a RadioButton's IsChecked is; unchecking one (another got checked) changes nothing.
    public bool? GameModeOff { get => _gameMode == SetupGameMode.Off; set { if (value == true) GameMode = SetupGameMode.Off; } }
    public bool? GameModeNoAdmin { get => _gameMode == SetupGameMode.NoAdmin; set { if (value == true) GameMode = SetupGameMode.NoAdmin; } }
    public bool? GameModeAll { get => _gameMode == SetupGameMode.All; set { if (value == true) GameMode = SetupGameMode.All; } }

    /// <summary>"All items" only where the helper can be allowed (an administrator account); else a note says why.</summary>
    public bool ShowAllOption => _actions.CanAllowHelper;
    public bool ShowNeedsAdmin => !_actions.CanAllowHelper;
    /// <summary>All items' second line: Windows asks once, or nothing to ask (the helper is already there).</summary>
    public string AllText => _actions.HelperReady ? Strings.SetupGmAllReady : Strings.SetupGmAllText;

    /// <summary>What Game mode changes while a game runs: its default items, the admin ones marked.</summary>
    public SetupGameModeItem[] GameModeItems { get; private set; } = [];

    private static IEnumerable<SetupGameModeItem> BuildGameModeItems()
    {
        bool services = false;
        foreach (var id in Shared.GameModeInfo.DefaultItems)
        {
            bool admin = Shared.GameModeInfo.IsElevatedItem(id);
            string? text = id switch
            {
                Shared.GameModeInfo.Power => Strings.GmPowerTitle,
                Shared.GameModeInfo.Priority => Strings.GmPriorityTitle,
                Shared.GameModeInfo.Smooth => Strings.GmSmoothTitle,
                Shared.GameModeInfo.Uploads => Strings.GmUploadsTitle,
                Shared.GameModeInfo.Updates => Strings.GmUpdatesTitle,
                _ when Shared.ElevatedPart.ServiceFor(id) is not null && !services => Strings.GmServicesTitle,
                _ => null, // the other services share the one line
            };
            if (Shared.ElevatedPart.ServiceFor(id) is not null) services = true;
            if (text is not null) yield return new SetupGameModeItem(text, admin);
        }
    }

    /// <summary>"Allow and finish…" (with the shield) when Done asks Windows; else "Done".</summary>
    public string DoneText => NeedsPrompt ? Strings.SetupAllowAndFinish : Strings.Done;
    public bool DoneNeedsAdmin => NeedsPrompt;
    public bool DoneEnabled => !_allowing;
    private bool NeedsPrompt => _gameMode == SetupGameMode.All && _actions.CanAllowHelper && !_actions.HelperReady;

    /// <summary>After a declined prompt: nothing was turned on; choose again or try again.</summary>
    public bool AllowDeclined => _allowDeclined;
    /// <summary>After a failed install: the error, with its code and the helper's log.</summary>
    public bool AllowFailed => _allowFailed is not null;
    public string AllowError => _allowFailed is { } code
        ? Strings.Format(Strings.GmAllowFailed, code, @"ProgramData\Binder\elevated.log")
        : "";

    private void OnPropertiesChanged(string[] names)
    {
        foreach (var name in names) OnPropertyChanged(name);
    }

    private async Task DoneAsync()
    {
        if (_allowing) return; // Enter again while Windows asks
        var choice = _gameMode;
        if (choice == SetupGameMode.All && !_actions.CanAllowHelper) choice = SetupGameMode.NoAdmin;
        if (choice == SetupGameMode.All && !_actions.HelperReady)
        {
            _allowing = true;
            _allowDeclined = false;
            _allowFailed = null;
            OnPropertiesChanged(GameModeNames);
            int code;
            try
            {
                code = await _actions.AllowHelperAsync();
            }
            finally
            {
                _allowing = false;
            }
            if (code != 0)
            {
                // Stay on the step: the user didn't get what they picked, so nothing is turned on.
                if (code == 1223) _allowDeclined = true;
                else _allowFailed = code;
                OnPropertiesChanged(GameModeNames);
                return;
            }
        }
        _actions.Finish(new SetupChoices(Desktop == true ? DisplayMode.Desktop : DisplayMode.Flyout, StartWithWindows, choice));
    }
}
