using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Views;

namespace GameShelf.ViewModels;

/// <summary>One launcher in setup step 1; its tick is kept until step 1's Next applies it.</summary>
public sealed class SetupLauncherRow(GameSource source, string name, string count, bool enabled)
{
    public GameSource Source { get; } = source;
    public string Name { get; } = name;
    public string Count { get; } = count;
    public bool Enabled { get; set; } = enabled;
}

/// <summary>The setup's answers, applied by the window.</summary>
public sealed record SetupChoices(DisplayMode Mode, bool StartWithWindows);

/// <summary>What first-run setup asks its owner to do.</summary>
public interface ISetupActions
{
    /// <summary>Every launcher Game Shelf reads, in its fixed order.</summary>
    IReadOnlyList<GameSource> Launchers { get; }
    /// <summary>"Skip setup": keep the defaults.</summary>
    void Skip();
    /// <summary>Step 2 needs the desktop's shortcuts (scanned off the UI thread; they come back through <see cref="SetupViewModel.SetShortcuts"/>).</summary>
    void LoadShortcuts();
    /// <summary>Moves the picked shortcuts onto the shelf; <see cref="SetupViewModel.ImportDone"/> goes on to step 3.</summary>
    void Import(IReadOnlyList<ShortcutFile> picked);
    void Finish(SetupChoices choices);
}

/// <summary>
/// First-run setup, three steps (spec §6, board 10): which launchers, tidy the desktop (the shortcut picker,
/// embedded in the view), how it opens. Shown once; Settings can run it again.
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
        DoneCommand = new(Done);
    }

    public RelayCommand SkipCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand BackToStep1Command { get; }
    public RelayCommand NotNowCommand { get; }
    public RelayCommand BackToStep2Command { get; }
    public RelayCommand MoveCommand { get; }
    public RelayCommand DoneCommand { get; }

    /// <summary>Starts at step 1 with the library as found so far (the scan may still be running).</summary>
    public void Start(Settings settings, IReadOnlyList<Game> games, bool startWithWindows)
    {
        _settings = settings;
        _shortcutsLoaded = false;
        Launchers = []; // a new run starts from the settings' ticks
        _foundCounts = null;
        Shortcuts = [];
        _selected = [];
        UpdateLibrary(games);
        Flyout = settings.DisplayMode == DisplayMode.Flyout;
        Desktop = settings.DisplayMode == DisplayMode.Desktop;
        StartWithWindows = startWithWindows;
        HotkeyText = Format.HotkeyLabel(settings.Hotkey);
        FillSelection();
        Step = 0; // so step 1 counts as a change
        ShowStep(1);
        OnAllPropertiesChanged();
    }

    // ---- Steps -----------------------------------------------------------------------------------------------

    private static readonly string[] StepNames = [nameof(Step), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3)];

    public int Step { get; private set; }
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;

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
        BuildLaunchers();
    }

    /// <summary>Every launcher's games on this PC, also the ones turned off.</summary>
    public void SetFoundCounts(IReadOnlyDictionary<GameSource, int> counts)
    {
        _foundCounts = counts;
        BuildLaunchers();
    }

    /// <summary>
    /// Every launcher Game Shelf reads: the ones with games first (most first), then the ones with none found,
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
            entry.Count switch { 0 => "None found", 1 => "1 game", var n => $"{n} games" },
            previous.TryGetValue(entry.Source, out bool ticked) ? ticked : _settings.IsSourceEnabled(entry.Source)))];
        int total = counts.Sum(entry => entry.Count);
        NothingFound = total == 0;
        FoundTitle = total == 0 ? "Let's set up your shelf" : total == 1 ? "We found 1 game" : $"We found {total} games";
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
    public string MoveText { get; private set; } = "Move";
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
        MoveText = selected == 0 ? "Move" : $"Move {selected}";
        CanMove = selected > 0;
        SelectionText = selected == 0 ? "Nothing selected" : selected == 1 ? "1 selected" : $"{selected} selected";
    }

    private void Move()
    {
        if (_selected.Count > 0) _actions.Import(_selected);
    }

    /// <summary>After the picked shortcuts were moved: on to the last step.</summary>
    public void ImportDone() => ShowStep(3);

    // ---- Step 3: how it opens --------------------------------------------------------------------------------

    private bool? _flyout, _desktop;
    private bool _startWithWindows;

    // bool? because a RadioButton's IsChecked is.
    public bool? Flyout { get => _flyout; set => SetProperty(ref _flyout, value); }
    public bool? Desktop { get => _desktop; set => SetProperty(ref _desktop, value); }
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }
    public string HotkeyText { get; private set; } = "";

    private void Done() =>
        _actions.Finish(new SetupChoices(Desktop == true ? DisplayMode.Desktop : DisplayMode.Flyout, StartWithWindows));
}
