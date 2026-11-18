using Binder.Library;
using Binder.Models;
using Binder.Views;

namespace Binder.ViewModels;

/// <summary>A program an anti-cheat may refuse to run next to (the same shape as <c>PcChecks.Conflict</c>).</summary>
public sealed record DetailConflict(string Name, int[] ProcessIds);

/// <summary>What a look at the game's folder and this PC found (<see cref="IDetailActions.ScanAsync"/>).</summary>
public sealed record DetailScan(
    GameScanResult? Result,
    (PcRequirement Requirement, bool? Met)[] Requirements,
    DetailConflict[] Conflicts,
    string[] DriverTools);

/// <summary>
/// What the detail page asks its owner (the panel window) to do, and the few facts it needs from Windows (disk,
/// running programs, the launcher). An interface, so none can be left unwired.
/// </summary>
public interface IDetailActions
{
    void GoBack();
    void Act(Game game, GameAction action);
    /// <summary>A per-game setting changed (already written to the game): save the library.</summary>
    void GameEdited(Game game);
    void CrashSeen(CrashRecord record);
    void CloseConflict(DetailConflict conflict);
    void OpenLink(string url);

    /// <summary>The game's folder if it exists on disk, else null.</summary>
    string? InstallFolder(Game game);
    bool HasStorePage(Game game);
    /// <summary>What the game starts as, for the "Starts as" line.</summary>
    string DescribeStart(Game game);
    /// <summary>Copies the image in as the game's cover; false when it couldn't (the cover stays).</summary>
    bool ImportCover(Game game, string path);
    void ClearCover(Game game);
    /// <summary>Engine, anti-cheat and the PC checks, off the UI thread; null when the look failed.</summary>
    Task<DetailScan?> ScanAsync(Game game);
    /// <summary>The games in the library, to name the ones that have the other numbers (spec §26).</summary>
    IReadOnlyList<Game> Library { get; }
    /// <summary>Gives the game a number for its game shortcut (1–9), or none (null); the owner saves and shows it again.</summary>
    void SetGameKey(string gameId, int? number);
}

/// <summary>One recent session as its row shows it.</summary>
public sealed record SessionRowInfo(Session Session, CrashRecord? Crash, bool Problem, bool KnownNormal, string MarkLabel,
    string Start, string Length, string DetailsName);

/// <summary>A suggested launch option with its check box state.</summary>
public sealed class SuggestionChoice(LaunchSuggestion suggestion, bool isOn)
{
    public LaunchSuggestion Suggestion { get; } = suggestion;
    public bool IsOn { get; set; } = isOn;
    public string Name => Strings.Format(Strings.OptionWithArgs, Suggestion.Label, Suggestion.Args);
}

/// <summary>One anti-cheat requirement: the setting's name, its state, whether that's what the anti-cheat needs.</summary>
public sealed record RequirementInfo(string Name, string Status, bool? Met, string? HelpUrl);

/// <summary>
/// The detail page's state for one game. <see cref="Show"/> fills it without reporting changes; afterwards a user's
/// change writes to the game and tells the owner (<see cref="IDetailActions.GameEdited"/>). Everything is computed
/// when the page opens or the user changes something; nothing updates in the background.
/// </summary>
public sealed partial class DetailViewModel : ObservableObject
{
    private const int RecentSessionCount = 5;
    private static readonly int[] DelayChoices = [0, 5, 10, 30];

    private readonly IDetailActions _actions;
    private Settings _settings = new();
    private string? _installFolder;
    private DetailScan? _scan;
    private bool _showMonths;

    public DetailViewModel(IDetailActions actions)
    {
        _actions = actions;
        BackCommand = new(actions.GoBack);
        PlayCommand = new(() => Act(GameAction.Play));
        FolderCommand = new(() => Act(GameAction.OpenFolder));
        StoreCommand = new(() => Act(GameAction.StorePage));
        HideCommand = new(() => Act(GameAction.Hide));
        FavoriteCommand = new(() => Act(GameAction.ToggleFavorite));
        AutoProcessCommand = new(AutoProcess);
        DefaultCoverCommand = new(DefaultCover);
        AutoStartWithCommand = new(AutoStartWith);
        TestLaunchCommand = new(TestLaunch);
        ResetLaunchCommand = new(ResetLaunch);
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand PlayCommand { get; }
    public RelayCommand FolderCommand { get; }
    public RelayCommand StoreCommand { get; }
    public RelayCommand HideCommand { get; }
    public RelayCommand FavoriteCommand { get; }
    public RelayCommand AutoProcessCommand { get; }
    public RelayCommand DefaultCoverCommand { get; }
    public RelayCommand AutoStartWithCommand { get; }
    public RelayCommand TestLaunchCommand { get; }
    public RelayCommand ResetLaunchCommand { get; }

    /// <summary>A different game is about to show (the view scrolls to the top and closes its cards).</summary>
    public event Action? NewGameShown;
    /// <summary>The cover image changed (the view reloads it).</summary>
    public event Action? CoverReplaced;

    /// <summary>The game being shown, or null.</summary>
    public Game? Game { get; private set; }

    /// <summary>All sessions and crash records, for a session's crash card.</summary>
    public IReadOnlyList<Session> AllSessions { get; private set; } = [];
    public IReadOnlyList<CrashRecord> Crashes { get; private set; } = [];

    public void Act(GameAction action)
    {
        if (Game is { } game) _actions.Act(game, action);
    }

    public void CrashSeen(CrashRecord record) => _actions.CrashSeen(record);
    public void CloseConflict(DetailConflict conflict) => _actions.CloseConflict(conflict);
    public void OpenLink(string url) => _actions.OpenLink(url);

    /// <summary>Fills the page for <paramref name="game"/> (again, after a change elsewhere), then looks at its files.</summary>
    public void Show(Game game, IReadOnlyList<Session> sessions, Settings settings, IReadOnlyList<CrashRecord> crashes)
    {
        bool sameGame = ReferenceEquals(Game, game) || Game?.Id == game.Id;
        Game = game;
        _settings = settings;
        AllSessions = sessions;
        Crashes = crashes;
        _installFolder = _actions.InstallFolder(game);
        HasStorePage = _actions.HasStorePage(game);
        if (!sameGame)
        {
            _scan = null;
            AntiCheatTouched = false;
        }

        FillStats();
        FillChoices();
        FillLaunchOptions();
        FillAntiCheat();
        if (!sameGame) NewGameShown?.Invoke();
        OnAllPropertiesChanged();
        _ = ScanAsync(game);
    }

    /// <summary>
    /// Engine and anti-cheat from the install folder (cached on the game until it updates), then the PC checks and
    /// running programs. The result is dropped if another game is on show by then.
    /// </summary>
    private async Task ScanAsync(Game game)
    {
        var info = await _actions.ScanAsync(game);
        if (info is null || Game is null || Game.Id != game.Id) return;
        ApplyScan(game, info);
    }

    /// <summary>Takes a finished scan for <paramref name="game"/> (the one on show).</summary>
    public void ApplyScan(Game game, DetailScan info)
    {
        _scan = info;
        if (info.Result is { } result && GameScan.Store(game, result)) _actions.GameEdited(game);
        FillLaunchOptions();
        FillAntiCheat();
        OnPropertiesChanged(LaunchNames);
        OnPropertiesChanged(AntiCheatProps);
    }

    private void OnPropertiesChanged(string[] names)
    {
        foreach (var name in names) OnPropertyChanged(name);
    }

    // ---- Header -------------------------------------------------------------------------------------------

    public string Title => Game?.Name ?? "";
    public string Subtitle => Game is null ? "" : Format.HasBadge(Game.Source) ? Format.LauncherName(Game.Source) : Game.SourceLabel;
    public string PlayName => Strings.Format(Strings.PlayGame, Title);
    public bool IsFavorite => Game?.Favorite == true;
    public string FavoriteLabel => IsFavorite ? Strings.RemoveFavorite : Strings.AddFavorite;
    public bool HasFolder => _installFolder is not null;
    public string FolderToolTip => HasFolder ? Strings.OpenInstallFolder : Strings.InstallFolderNotFound;
    public bool HasStorePage { get; private set; }
    /// <summary>Where a file picker starts.</summary>
    public string? InstallFolder => _installFolder;

    // ---- Stats and activity -------------------------------------------------------------------------------

    private List<Session> _mine = [];

    /// <summary>Shown for playtime and size on disk while there is none.</summary>
    private const string NoValue = "--";

    public string PlaytimeText { get; private set; } = NoValue;
    public string LastPlayedText { get; private set; } = Strings.Never;
    public string SizeText => Game is { SizeOnDisk: > 0 } game ? game.SizeText : NoValue;
    public SessionRowInfo[] Sessions { get; private set; } = [];
    public bool HasSessions => _mine.Count > 0;
    public bool NoSessions => _mine.Count == 0;
    public string CrashPattern { get; private set; } = "";
    public bool HasCrashPattern => CrashPattern.Length > 0;

    private void FillStats()
    {
        var game = Game!;
        _mine = AllSessions
            .Where(s => s.GameId == game.Id && s.End > s.Start)
            .OrderByDescending(s => s.End)
            .ToList();
        var total = TimeSpan.FromTicks(_mine.Sum(s => (s.End - s.Start).Ticks));
        PlaytimeText = _mine.Count == 0 ? NoValue : Format.Playtime(total);
        LastPlayedText = _mine.Count == 0 ? Strings.Never : Format.RelativeDay(_mine[0].End);
        Sessions = [.. _mine.Take(RecentSessionCount).Select(s => SessionRow(s, CrashFor(s)))];
        CrashPattern = CrashAnalysis.Pattern(_mine.Take(10).Select(CrashFor).ToList()) ?? "";
        FillChart();
    }

    private CrashRecord? CrashFor(Session s) => CrashAnalysis.For(Crashes, s);

    /// <summary>
    /// A warning and a Details link when it crashed, a check when Binder knows it ended normally (sessions
    /// from before crash checks get neither), when it started and how long it lasted.
    /// </summary>
    private static SessionRowInfo SessionRow(Session session, CrashRecord? crash)
    {
        bool problem = crash is { IsProblem: true };
        bool knownNormal = !problem && session.ExitCode is not null;
        var label = !problem ? (knownNormal ? Strings.SessionClosedNormally : "") : crash!.Ending switch
        {
            GameEnding.StartFailed => Strings.SessionStartFailed,
            GameEnding.ClosedUnexpectedly => Strings.SessionClosedUnexpectedly,
            _ => Strings.SessionCrashed,
        };
        var start = Format.SessionStart(session.Start);
        return new(session, crash, problem, knownNormal, label, start, Format.Duration(session.End - session.Start),
            Strings.Format(Strings.SessionDetailsName, start));
    }

    /// <summary>Months (12) instead of weeks (8) in the playtime chart.</summary>
    public bool ShowMonths
    {
        get => _showMonths;
        set
        {
            if (!SetProperty(ref _showMonths, value) || _mine.Count == 0) return;
            FillChart();
            OnPropertiesChanged(ChartNames);
        }
    }

    private static readonly string[] ChartNames = [nameof(PeriodTotal), nameof(PeriodDetail), nameof(ChartBars)];

    public string PeriodTotal { get; private set; } = "";
    public string PeriodDetail { get; private set; } = "";
    /// <summary>Hours per week for 8 weeks, or per month for 12; the view draws them.</summary>
    public Bar[] ChartBars { get; private set; } = [];
    public string ChartName => _showMonths ? Strings.ChartMonthsName : Strings.ChartWeeksName;

    private void FillChart()
    {
        if (_mine.Count == 0)
        {
            ChartBars = [];
            return;
        }
        bool months = _showMonths;
        var buckets = months ? PlaytimeStats.Months(_mine, DateTime.Now) : PlaytimeStats.Weeks(_mine, DateTime.Now);
        var period = TimeSpan.FromTicks(buckets.Sum(b => b.Time.Ticks));
        var current = buckets[^1].Time;
        PeriodTotal = period > TimeSpan.Zero ? Format.Duration(period) : Strings.NotPlayed;
        var now = current > TimeSpan.Zero ? Format.Duration(current) : Strings.NoneYet;
        PeriodDetail = Strings.Format(months ? Strings.PeriodMonths : Strings.PeriodWeeks, now);
        ChartBars = [.. buckets.Select(b => new Bar(
            months ? PlaytimeStats.MonthLabel(b.Start) : PlaytimeStats.WeekLabel(b.Start),
            Strings.Format(Strings.LabelValue, months ? PlaytimeStats.MonthName(b.Start) : Strings.Format(Strings.WeekOf, PlaytimeStats.WeekLabel(b.Start)),
                b.Time > TimeSpan.Zero ? Format.Duration(b.Time) : Strings.NotPlayedLower),
            b.Time.TotalMinutes))];
    }

    // ---- This game: hide delay, game mode, process, cover -------------------------------------------------

    private static readonly string[] ChoiceNames =
    [
        nameof(HideDelayChoices), nameof(SelectedHideDelay), nameof(GameModeChoices), nameof(SelectedGameMode),
        nameof(ProcessText), nameof(ProcessToolTip), nameof(HasCustomProcess), nameof(CoverText), nameof(HasCustomCover),
        nameof(GameKeyLabels), nameof(SelectedGameKey), nameof(GameKeyText), nameof(HasGameKey),
    ];

    /// <summary>"Default (10 s)", then the choices. A new array only when its text changes (the selection is bound by item).</summary>
    public string[] HideDelayChoices { get; private set; } = [];
    public string[] GameModeChoices { get; private set; } = [];

    private void FillChoices()
    {
        string[] delays = [Strings.Format(Strings.DefaultChoice, Format.Delay(_settings.HideDelaySeconds)), .. DelayChoices.Select(Format.Delay)];
        if (!delays.SequenceEqual(HideDelayChoices)) HideDelayChoices = delays;
        string[] modes = [Strings.Format(Strings.DefaultChoice, Strings.LowerFirst(_settings.GameModeEnabled ? Strings.On : Strings.Off)), Strings.On, Strings.Off];
        if (!modes.SequenceEqual(GameModeChoices)) GameModeChoices = modes;
        _gameKeyChoices = Game is null ? [] : GameKeyChoices.For(Game, _settings.GameKeySlots, _actions.Library);
        string[] keys = [.. _gameKeyChoices.Select(c => c.Label)];
        if (!keys.SequenceEqual(GameKeyLabels)) GameKeyLabels = keys;
    }

    // ---- Game shortcut (spec §26) ----

    private List<GameKeyChoice> _gameKeyChoices = [];

    /// <summary>None, then "1 · Hades II" … "9 · Free". A new array only when its text changes (the selection is bound by item).</summary>
    public string[] GameKeyLabels { get; private set; } = [];

    /// <summary>The game's number; picking one gives it to this game (one that had it has none then).</summary>
    public string? SelectedGameKey
    {
        get => _gameKeyChoices.FirstOrDefault(c => c.IsCurrent)?.Label;
        set
        {
            int index = value is null ? -1 : Array.IndexOf(GameKeyLabels, value);
            if (Game is not { } game || index < 0 || index >= _gameKeyChoices.Count || _gameKeyChoices[index].IsCurrent) return;
            _actions.SetGameKey(game.Id, _gameKeyChoices[index].Number);
        }
    }

    /// <summary>The second line of the shortcut card: "Win+Shift+G, 3", or nothing without a number.</summary>
    public string GameKeyText => Game is not null && GameKeys.SlotOf(_settings.GameKeySlots, Game.Id) is { } number
        ? Format.GameKeyLabel(_settings.Hotkey, number) : "";
    public bool HasGameKey => GameKeyText.Length > 0;

    /// <summary>The game's hide delay; an unknown value shows as the default.</summary>
    public string? SelectedHideDelay
    {
        get
        {
            if (Game is null || HideDelayChoices.Length == 0) return null;
            int index = Game.HideDelaySeconds is { } seconds ? Array.IndexOf(DelayChoices, seconds) + 1 : 0;
            return HideDelayChoices[Math.Max(0, index)];
        }
        set
        {
            int index = value is null ? -1 : Array.IndexOf(HideDelayChoices, value);
            if (Game is not { } game || index < 0) return;
            int? seconds = index == 0 ? null : DelayChoices[index - 1];
            if (value == SelectedHideDelay) return;
            game.HideDelaySeconds = seconds;
            Edited(game);
        }
    }

    public string? SelectedGameMode
    {
        get => Game is null || GameModeChoices.Length == 0 ? null : GameModeChoices[Game.GameMode switch { null => 0, true => 1, false => 2 }];
        set
        {
            int index = value is null ? -1 : Array.IndexOf(GameModeChoices, value);
            if (Game is not { } game || index < 0 || value == SelectedGameMode) return;
            game.GameMode = index switch { 1 => true, 2 => false, _ => null };
            Edited(game);
        }
    }

    /// <summary>"Restart if it crashes" (spec §17).</summary>
    public bool RestartOnCrash
    {
        get => Game?.RestartOnCrash == true;
        set
        {
            if (Game is not { } game || game.RestartOnCrash == value) return;
            game.RestartOnCrash = value;
            OnPropertyChanged();
            _actions.GameEdited(game);
        }
    }

    public string ProcessText => Game is null ? "" : Game.ProcessName ?? (Game.InstallDir is not null
        ? Strings.ProcessAutoFolder
        : Strings.ProcessAuto);
    public string? ProcessToolTip => Game?.ProcessName is null ? Game?.InstallDir : null;
    public bool HasCustomProcess => Game?.ProcessName is not null;

    /// <summary>Lets the user point at the game's real exe (picked by the view), for launchers the folder match can't follow.</summary>
    public void SetProcess(string path)
    {
        if (Game is not { } game) return;
        game.ProcessName = Path.GetFileName(path);
        Edited(game);
    }

    private void AutoProcess()
    {
        if (Game is not { } game) return;
        game.ProcessName = null;
        Edited(game);
    }

    public string CoverText => Game is null ? "" : Game.CustomCover is not null ? Strings.CoverCustom
        : Game.Source == GameSource.Steam ? Strings.CoverSteam
        : Strings.CoverGenerated;
    public bool HasCustomCover => Game?.CustomCover is not null;

    public void SetCover(string path)
    {
        if (Game is not { } game || !_actions.ImportCover(game, path)) return; // couldn't copy: keep the current cover
        Edited(game);
        CoverReplaced?.Invoke();
    }

    private void DefaultCover()
    {
        if (Game is not { } game) return;
        _actions.ClearCover(game);
        Edited(game);
        CoverReplaced?.Invoke();
    }

    private void Edited(Game game)
    {
        FillChoices();
        OnPropertiesChanged(ChoiceNames);
        _actions.GameEdited(game);
    }

    // ---- Launch options -----------------------------------------------------------------------------------

    private static readonly string[] LaunchNames =
    [
        nameof(CanPickStartWith), nameof(StartWithText), nameof(StartWithToolTip), nameof(HasStartWith),
        nameof(EngineKnown), nameof(EngineGlyph), nameof(EngineTag), nameof(EngineTagName), nameof(EngineText),
        nameof(Suggestions), nameof(HasSuggestions), nameof(LaunchSupported), nameof(ExtraArgs),
    ];

    private static readonly string[] StartsAsNames =
    [
        nameof(StartsAs), nameof(LaunchNote), nameof(HasLaunchNote), nameof(CanResetLaunch), nameof(LaunchSummary), nameof(CanTestLaunch),
    ];

    private bool _canPick;
    private string _extraArgs = "";
    private string? _note;

    public bool CanPickStartWith => _canPick;
    public string StartWithText => Game is null ? ""
        : LaunchOptions.ValidOverride(Game) is { } exe ? Path.GetFileName(exe)
        : _canPick ? Strings.Automatic : Strings.AutomaticNoFolder;
    public string? StartWithToolTip => Game is null ? null : LaunchOptions.ValidOverride(Game);
    public bool HasStartWith => Game?.ExeOverride is not null;

    private GameEngine Engine => _scan?.Result?.Engine ?? Game?.Engine ?? GameEngine.Unknown;
    private string? EngineVersion => _scan?.Result is { } result ? result.EngineVersion : Game?.EngineVersion;
    public bool EngineKnown => Engine != GameEngine.Unknown;
    /// <summary>The tag's icon: one that suits the engine's name (<see cref="Format.EngineGlyph"/>).</summary>
    public string EngineGlyph => Format.EngineGlyph(Engine);
    public string EngineTag => EngineKnown ? Format.EngineLabel(Engine, EngineVersion)
        : _scan is null && Game is { Engine: null, InstallDir: not null } ? Strings.Checking : Strings.EngineUnknown;
    public string EngineTagName => Strings.Format(Strings.EngineName, EngineTag);
    public string EngineText => LaunchOptions.Suggestions(Engine).Count > 0 ? Strings.SuggestedOptions : Strings.NoSuggestions;

    public SuggestionChoice[] Suggestions { get; private set; } = [];
    public bool HasSuggestions => Suggestions.Length > 0;
    public bool LaunchSupported => Game is not null && LaunchOptions.RouteFor(Game) != LaunchOptions.Route.NotSupported;

    /// <summary>The arguments beyond the ticked suggestions. The view writes them while the user types (saved by <see cref="CommitExtraArgs"/>).</summary>
    public string ExtraArgs
    {
        get => _extraArgs;
        set
        {
            if (value == _extraArgs) return;
            _extraArgs = value;
            ComposeArgs();
            UpdateStartsAs(); // saved when the field loses focus, not per keystroke
        }
    }

    /// <summary>The field lost focus: save what was typed.</summary>
    public void CommitExtraArgs()
    {
        if (Game is { } game) _actions.GameEdited(game);
    }

    private void FillLaunchOptions()
    {
        if (Game is not { } game) return;
        _canPick = game.InstallDir is not null && _actions.InstallFolder(game) is not null;
        var tokens = LaunchOptions.Split(game.LaunchArgs);
        var choices = new List<SuggestionChoice>();
        foreach (var suggestion in LaunchOptions.Suggestions(Engine))
        {
            bool on = LaunchOptions.IndexOf(tokens, suggestion.Tokens) >= 0;
            if (on) tokens = LaunchOptions.Without(tokens, suggestion);
            choices.Add(new(suggestion, on));
        }
        Suggestions = [.. choices];
        _extraArgs = LaunchOptions.Join(tokens);
        UpdateStartsAs();
    }

    /// <summary>Ticks or unticks a suggestion (unticking the others of its group), and saves.</summary>
    public void SetSuggestion(int index, bool on)
    {
        if (Game is not { } game || index < 0 || index >= Suggestions.Length) return;
        var clicked = Suggestions[index];
        clicked.IsOn = on;
        if (on && clicked.Suggestion.Group is { } group)
            foreach (var other in Suggestions)
                if (other != clicked && other.Suggestion.Group == group) other.IsOn = false;
        ComposeArgs();
        UpdateStartsAs();
        _actions.GameEdited(game);
    }

    /// <summary>The arguments as the check boxes and the field show them: ticked suggestions first, then the extras.</summary>
    private void ComposeArgs()
    {
        if (Game is not { } game) return;
        var tokens = new List<string>();
        foreach (var choice in Suggestions)
            if (choice.IsOn) tokens.AddRange(choice.Suggestion.Tokens);
        tokens.AddRange(LaunchOptions.Split(_extraArgs));
        var joined = LaunchOptions.Join(tokens);
        game.LaunchArgs = joined.Length == 0 ? null : joined.Length > LaunchOptions.MaxLength ? joined[..LaunchOptions.MaxLength] : joined;
    }

    public string StartsAs { get; private set; } = "";
    public string LaunchNote => _note ?? "";
    public bool HasLaunchNote => _note is not null;
    public bool CanResetLaunch => Game is not null && (Game.LaunchArgs is not null || Game.ExeOverride is not null);
    public bool CanTestLaunch => LaunchSupported;
    /// <summary>The closed Launch options card's line: the engine, then what's changed (arguments, the program it starts with).</summary>
    public string LaunchSummary { get; private set; } = "";

    private void UpdateStartsAs()
    {
        if (Game is not { } game) return;
        StartsAs = Strings.Format(Strings.StartsAs, _actions.DescribeStart(game));
        _note = LaunchOptions.RouteFor(game) switch
        {
            LaunchOptions.Route.Steam when !string.IsNullOrWhiteSpace(game.LaunchArgs) =>
                Strings.NoteSteamArgs,
            LaunchOptions.Route.NotSupported when game.Source == GameSource.Epic =>
                Strings.NoteEpicArgs,
            LaunchOptions.Route.NotSupported when game.Source == GameSource.Shortcut =>
                Strings.NoteShortcutArgs,
            LaunchOptions.Route.NotSupported =>
                Strings.Format(Strings.NoteLauncherArgs, game.SourceLabel),
            _ => null,
        };

        var parts = new List<string>(3);
        if (Engine != GameEngine.Unknown) parts.Add(Format.EngineLabel(Engine, EngineVersion));
        bool changed = false;
        if (!string.IsNullOrWhiteSpace(game.LaunchArgs)) { parts.Add(game.LaunchArgs.Trim()); changed = true; }
        if (LaunchOptions.ValidOverride(game) is { } exe) { parts.Add(Strings.Format(Strings.StartsExe, Path.GetFileName(exe))); changed = true; }
        if (!changed) parts.Add(Strings.Default);
        LaunchSummary = string.Join(" · ", parts);
        OnPropertiesChanged(StartsAsNames);
    }

    /// <summary>"Start with": another exe (picked by the view), only from inside the game's install folder.</summary>
    public void SetStartWith(string path)
    {
        if (Game is not { } game || game.InstallDir is null) return;
        if (!LaunchOptions.IsInside(path, game.InstallDir) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            _note = Strings.NoteInsideFolder;
            OnPropertyChanged(nameof(LaunchNote));
            OnPropertyChanged(nameof(HasLaunchNote));
            return;
        }
        game.ExeOverride = Path.GetFullPath(path);
        SaveLaunchOptions(game);
    }

    private void AutoStartWith()
    {
        if (Game is not { } game) return;
        game.ExeOverride = null;
        SaveLaunchOptions(game);
    }

    private void TestLaunch()
    {
        if (Game is not { } game) return;
        ComposeArgs();
        _actions.GameEdited(game);
        Act(GameAction.TestLaunch);
    }

    private void ResetLaunch()
    {
        if (Game is not { } game) return;
        game.LaunchArgs = null;
        game.ExeOverride = null;
        _extraArgs = "";
        SaveLaunchOptions(game);
    }

    private void SaveLaunchOptions(Game game)
    {
        _actions.GameEdited(game);
        FillLaunchOptions();
        OnPropertiesChanged(LaunchNames);
    }

    // ---- Anti-cheat ---------------------------------------------------------------------------------------

    private static readonly string[] AntiCheatProps =
    [
        nameof(HasAntiCheat), nameof(AntiCheatNames), nameof(AntiCheatReminder), nameof(Checking), nameof(Checked), nameof(Requirements),
        nameof(Conflicts), nameof(DriverToolNotes), nameof(AntiCheatIssues), nameof(AntiCheatOk), nameof(AntiCheatLabel),
        nameof(AntiCheatLabelName), nameof(AntiCheatState), nameof(AntiCheatCardName),
    ];

    private IReadOnlyList<AntiCheatKind> Kinds => _scan?.Result?.AntiCheats ?? Game?.AntiCheats ?? [];

    public bool HasAntiCheat => Kinds.Count > 0;
    public string AntiCheatNames => Strings.List([.. Kinds.Select(AntiCheat.Name)]);
    /// <summary>The PC checks haven't finished for this game yet.</summary>
    public bool Checking => _scan is null;
    public bool Checked => _scan is not null;
    public RequirementInfo[] Requirements { get; private set; } = [];
    public (DetailConflict Conflict, string Text)[] Conflicts { get; private set; } = [];
    public string[] DriverToolNotes { get; private set; } = [];
    public int AntiCheatIssues { get; private set; }
    public bool AntiCheatOk => AntiCheatIssues == 0;
    public string AntiCheatLabel => AntiCheatIssues == 0 ? AntiCheatNames : Strings.Format(Strings.Dot, AntiCheatNames, Issues(AntiCheatIssues));
    public string AntiCheatLabelName => Strings.Format(Strings.AntiCheatShowDetails, AntiCheatLabel);
    public string AntiCheatState => _scan is null ? Strings.Checking : AntiCheatIssues == 0 ? Strings.AntiCheatOk : Issues(AntiCheatIssues);
    public string AntiCheatCardName => Strings.Format(Strings.AntiCheatCardName, AntiCheatNames, AntiCheatState);

    /// <summary>
    /// The user opened or closed the anti-cheat card for this game, so it no longer opens by itself (board 21:
    /// closed when all is well, open when something needs attention).
    /// </summary>
    public bool AntiCheatTouched { get; set; }

    private static string Issues(int count) => Strings.Plural(count, Strings.IssueCountOne, Strings.IssueCount);

    public bool AntiCheatReminder
    {
        get => Game?.AntiCheatReminder == true;
        set
        {
            if (Game is not { } game || game.AntiCheatReminder == value) return;
            game.AntiCheatReminder = value;
            OnPropertyChanged();
            _actions.GameEdited(game);
        }
    }

    private void FillAntiCheat()
    {
        Requirements = [];
        Conflicts = [];
        DriverToolNotes = [];
        AntiCheatIssues = 0;
        if (!HasAntiCheat || _scan is null) return;

        var names = AntiCheatNames;
        Requirements = [.. _scan.Requirements.Select(r => Requirement(r.Requirement, r.Met))];
        Conflicts = [.. _scan.Conflicts.Select(c => (c, Strings.Format(Strings.ConflictRunning, c.Name, names)))];
        DriverToolNotes = [.. _scan.DriverTools.Select(tool =>
            Strings.Format(Strings.DriverToolLoaded, tool))];
        AntiCheatIssues = Requirements.Count(r => r.Met == false) + Conflicts.Length;
    }

    private static RequirementInfo Requirement(PcRequirement requirement, bool? met)
    {
        // The tag states the setting's state; its color says whether that's what the anti-cheat needs.
        string status = (requirement, met) switch
        {
            (_, null) => Strings.CouldntCheck,
            (PcRequirement.NoTestSigning or PcRequirement.NoKernelDebugging, true) => Strings.Off,
            (PcRequirement.NoTestSigning or PcRequirement.NoKernelDebugging, false) => Strings.On,
            (PcRequirement.Tpm20, true) => Strings.Found,
            (PcRequirement.Tpm20, false) => Strings.NotFound,
            (PcRequirement.VanguardLoaded, true) => Strings.Running,
            (PcRequirement.VanguardLoaded, false) => Strings.RestartNeeded,
            (_, true) => Strings.On,
            (_, false) => Strings.Off,
        };
        string name = requirement switch
        {
            // The tag says whether it's on, so the row names the setting itself.
            PcRequirement.NoTestSigning => Strings.TestSigning,
            PcRequirement.NoKernelDebugging => Strings.KernelDebugging,
            PcRequirement.VanguardLoaded => Strings.VanguardDriver,
            _ => AntiCheat.RequirementName(requirement),
        };
        return new(name, status, met, met == false ? AntiCheat.HelpUrl(requirement) : null);
    }
}
