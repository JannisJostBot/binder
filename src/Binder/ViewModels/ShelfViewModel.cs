using Binder.Library;
using Binder.Models;
using Binder.Views;

namespace Binder.ViewModels;

/// <summary>What the shelf asks its owner to do.</summary>
public interface IShelfActions
{
    void Play(Game game);
    void OpenDetails(Game game);
    /// <summary>Right-click menu actions other than Play and Details.</summary>
    void Act(Game game, GameAction action);
    void OpenSettings();
    void ImportShortcuts();
    void AddGame();
    void TogglePin();
    /// <summary>Game mode for all games turned on (true) or off; the owner saves it.</summary>
    void SetGameMode(bool on);
    void OpenGameMode();
    void OpenFreeSpace();
    void Refresh();
    /// <summary>A shelf choice kept in settings changed (sort order, favorites first, the top slot); the owner saves.</summary>
    void ViewSettingsChanged();
    /// <summary>Whether Game mode's admin helper is from an older version.</summary>
    bool HelperNeedsUpdate(IReadOnlyList<Game> games);
    /// <summary>Whether Game mode's changes are applied now (a game runs), not only switched on for the next one.</summary>
    bool GameModeRunning { get; }
    /// <summary>Updates the helper (UAC); the owner gives the shelf its data again afterwards.</summary>
    Task UpdateHelperAsync();
    /// <summary>Whether the launcher is installed, so its chip offers "Open …" (a few registry reads).</summary>
    bool CanOpenLauncher(GameSource source);
    /// <summary>Opens the launcher itself (spec §21).</summary>
    void OpenLauncher(GameSource source);
    /// <summary>"Update to 0.2.0" / "Restart to update" in the "…" menu (spec §25): downloads or installs, and shows Settings › About.</summary>
    void StartUpdate();
    /// <summary>Gives the game a number for its game shortcut (1–9), or none (null); spec §26.</summary>
    void SetGameKey(string gameId, int? number);
    /// <summary>Whether a year has enough play for Year in review (spec §28), read when the "…" menu opens.</summary>
    bool HasYearReview();
    /// <summary>Opens Year in review on <paramref name="year"/>, or the newest year.</summary>
    void OpenYearReview(int? year);
}

/// <summary>
/// The shelf (boards 1-7, 14, 16, 18): which games show and in what order, the filters, the top slot, the empty
/// state and the notices. Worked out when the data, the filter, the search text or the sort order changes, never on
/// a timer. The grid, its covers, menus and keyboard/controller focus are the view's.
/// </summary>
public sealed partial class ShelfViewModel : ObservableObject
{
    private readonly IShelfActions _actions;
    private Settings _settings = new();
    private List<Game> _games = [];
    private Dictionary<string, DateTimeOffset> _lastPlayed = [];
    private Dictionary<string, TimeSpan> _playtime = [];
    // The shelf in sort order. Sorting (culture-aware name compares) happens when the data or the sort order
    // changes, not on every keystroke: typing only filters this list, O(n), and keeps its order.
    private List<Game> _sorted = [];
    // Stats are recomputed only when the session list changes (it's replaced when a game ends).
    private IReadOnlyList<Session>? _statsFor;
    private int _statsCount = -1;
    private string _query = "";
    private bool _shownSearching;

    public ShelfViewModel(IShelfActions actions)
    {
        _actions = actions;
        SettingsCommand = new(actions.OpenSettings);
        PinCommand = new(actions.TogglePin);
        ImportCommand = new(actions.ImportShortcuts);
        AddGameCommand = new(actions.AddGame);
        ContinuePlayCommand = new(() => { if (ContinueGame is { } game) actions.Play(game); });
        OpenYearOfferCommand = new(() =>
        {
            int? year = _yearOffer?.Year;
            SetYearOffer(null); // once: back to the usual slot afterwards
            actions.OpenYearReview(year);
        });
        DismissYearOfferCommand = new(() => SetYearOffer(null));
    }

    public RelayCommand SettingsCommand { get; }
    public RelayCommand PinCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand AddGameCommand { get; }
    public RelayCommand ContinuePlayCommand { get; }
    public RelayCommand OpenYearOfferCommand { get; }
    public RelayCommand DismissYearOfferCommand { get; }

    public void Play(Game game) => _actions.Play(game);
    public void OpenDetails(Game game) => _actions.OpenDetails(game);
    /// <summary>The cover menu's "Shortcut" or a cover dropped on a number (spec §26).</summary>
    public void SetGameKey(Game game, int? number) => _actions.SetGameKey(game.Id, number);

    /// <summary>
    /// What each number 0–9 plays now, for the row of numbers shown while a cover is dragged (spec §26): 0 the last
    /// game played, 1–9 the user's; null where there's none.
    /// </summary>
    public Game?[] GamesOnKeys()
    {
        var byId = new Dictionary<string, Game>(_games.Count);
        foreach (var game in _games) byId.TryAdd(game.Id, game);
        var keys = new Game?[10];
        DateTimeOffset newest = DateTimeOffset.MinValue;
        foreach (var (id, when) in _lastPlayed)
            if (when > newest && byId.TryGetValue(id, out var game))
            {
                newest = when;
                keys[GameKeys.LastPlayed] = game;
            }
        foreach (int number in GameKeys.UserSlots)
            keys[number] = GameKeys.IdOn(_settings.GameKeySlots, number) is { } id && byId.TryGetValue(id, out var game) ? game : null;
        return keys;
    }

    /// <summary>A game shortcut as it's pressed with the current shortcut: "Win+Shift+G, 3".</summary>
    public string GameKeyLabel(int number) => Format.GameKeyLabel(_settings.Hotkey, number);
    public void Act(Game game, GameAction action) => _actions.Act(game, action);

    /// <summary>The library, sessions and settings, when the panel gets its data (it opens, a session ends).</summary>
    public void SetData(List<Game> games, IReadOnlyList<Session> sessions, Settings settings)
    {
        _settings = settings;
        _games = games.Where(g => !g.Hidden && settings.IsSourceEnabled(g.Source)).ToList();
        if (!ReferenceEquals(sessions, _statsFor) || sessions.Count != _statsCount)
        {
            (_lastPlayed, _playtime) = ShelfOrder.Stats(sessions);
            _statsFor = sessions;
            _statsCount = sessions.Count;
        }
        foreach (var game in _games)
            game.LastPlayedText = _lastPlayed.TryGetValue(game.Id, out var when) ? Format.RelativeDay(when) : Strings.Never;

        Density = settings.GridDensity;
        BuildChips();
        // Worked out when the panel gets its data, never on a timer.
        Greeting = Format.Greeting(DateTime.Now);
        // The footer teaches hide and return; after a few games from the shelf it has done its job.
        _footerHint = sessions.Count < FooterHintSessions;
        var hides = settings.HideDelaySeconds <= 0 ? Strings.FooterHides : Strings.Format(Strings.FooterHidesAfter, settings.HideDelaySeconds);
        FooterText = settings.ComeBackOnClose ? Strings.Format(Strings.FooterComesBack, hides) : hides;
        UpdateContinue();
        UpdateWeek(sessions);
        UpdateHelper(games);
        _sorted = Sort();
        Filter(force: true, notify: false);
        OnAllPropertiesChanged();
    }

    // ---- Layout ----------------------------------------------------------------------------------------------

    public GridDensity Density { get; private set; } = GridDensity.Medium;
    public bool ListMode => Density == GridDensity.List;
    /// <summary>Cover size: Small = 5 columns, Medium = 4, Large = 3, or the list (board 11).</summary>
    public int Columns => Density switch { GridDensity.Small => 5, GridDensity.Large => 3, GridDensity.List => 1, _ => 4 };

    public string Greeting { get; private set; } = "";

    // ---- Filtering and search --------------------------------------------------------------------------------

    private static readonly string[] FilterNames =
    [
        nameof(Placeholder), nameof(SortVisible), nameof(EmptyVisible), nameof(EmptyGlyph), nameof(EmptyText),
        nameof(EmptyActionsVisible), nameof(EmptyClearVisible), nameof(EmptyImportVisible), nameof(Searching),
        nameof(EnterGame),
    ];

    /// <summary>The games shown, in order. A new list only when what's shown changes (or the data did).</summary>
    public List<Game> Visible { get; private set; } = [];
    public string Query
    {
        get => _query;
        set
        {
            value ??= "";
            if (value == _query) return;
            _query = value;
            Filter(force: false, notify: true);
        }
    }

    public bool Searching => _query.Trim().Length > 0;

    /// <summary>
    /// "Search 72 games", or with Enter's game "Search 72 games · Enter plays Hades II" (spec §22 b), which also
    /// teaches the Enter key.
    /// </summary>
    public string Placeholder
    {
        get
        {
            var search = Strings.Plural(_sorted.Count, Strings.SearchCountOne, Strings.SearchCount);
            return !Searching && EnterGame is { } game ? Strings.Format(Strings.SearchEnterPlays, search, game.Name) : search;
        }
    }

    /// <summary>
    /// What Enter in the search box plays (spec §22 a, b): the top match while searching; with nothing typed the
    /// "Continue playing" (or "Welcome back") game, only while the top slot shows it. Null: Enter does nothing.
    /// </summary>
    public Game? EnterGame =>
        Searching ? (Visible.Count > 0 ? Visible[0] : null)
        : !_crashShown && SpotlightVisible && !ShowWeek && !YearOfferVisible ? ContinueGame : null;

    /// <summary>Enter in the search box: plays <see cref="EnterGame"/>. False when there's nothing to play.</summary>
    public bool PlayEnterGame()
    {
        if (EnterGame is not { } game) return false;
        _actions.Play(game);
        return true;
    }
    public bool SortVisible { get; private set; } = true;
    public bool EmptyVisible { get; private set; }
    public string EmptyGlyph { get; private set; } = "";
    public string EmptyText { get; private set; } = "";
    public bool EmptyActionsVisible { get; private set; }
    public bool EmptyClearVisible { get; private set; }
    public bool EmptyImportVisible { get; private set; } = true;

    private List<Game> Sort() => ShelfOrder.Sort(_games, _settings.SortBy, _settings.FavoritesFirst, _lastPlayed, _playtime);

    /// <summary>
    /// Filters the sorted shelf by filter and search text. <paramref name="force"/> hands the view a new list even
    /// when the same games match (their contents changed); otherwise an unchanged result keeps the tiles as they are.
    /// </summary>
    private void Filter(bool force, bool notify)
    {
        var query = _query.Trim();
        Func<Game, bool>? chip = CurrentFilter switch
        {
            "All" => null,
            "Favorites" => g => g.Favorite,
            "Recent" => g => _lastPlayed.ContainsKey(g.Id),
            "Other" => g => !Format.HasBadge(g.Source),
            _ when Enum.TryParse<GameSource>(CurrentFilter, out var source) => g => g.Source == source,
            _ => null,
        };
        var visible = new List<Game>(_sorted.Count);
        foreach (var game in _sorted)
        {
            if (chip is not null && !chip(game)) continue;
            visible.Add(game);
        }
        // While searching the best match comes first, so Enter plays it (spec §22 a). Otherwise "Recent" is its own
        // order (only played games, usually a few); everything else follows the sort menu.
        if (query.Length > 0) visible = GameSearch.Search(visible, query, _lastPlayed);
        else if (CurrentFilter == "Recent") visible.Sort((a, b) => _lastPlayed[b.Id].CompareTo(_lastPlayed[a.Id]));

        // The first tile's "Enter" hint comes and goes with searching: the tiles are drawn again then.
        bool searchingChanged = (query.Length > 0) != _shownSearching;
        _shownSearching = query.Length > 0;
        bool same = !force && !searchingChanged && visible.Count == Visible.Count && visible.SequenceEqual(Visible, ReferenceEqualityComparer.Instance);
        if (!same) Visible = visible;
        SortVisible = CurrentFilter != "Recent";

        EmptyVisible = Visible.Count == 0;
        bool noMatch = _games.Count > 0 && query.Length > 0;
        EmptyGlyph = _games.Count == 0 ? "" : query.Length > 0 ? "" : CurrentFilter == "Favorites" ? "" : "";
        EmptyActionsVisible = _games.Count == 0 || noMatch;
        EmptyClearVisible = noMatch;
        EmptyImportVisible = !noMatch;
        EmptyText =
            _games.Count == 0 ? Strings.EmptyNoGames :
            query.Length > 0 ? Strings.Format(Strings.EmptyNoMatch, query) :
            CurrentFilter == "Recent" ? Strings.EmptyRecent :
            CurrentFilter == "Favorites" ? Strings.EmptyFavorites :
            Strings.EmptyStore;

        UpdateSpotlight();
        if (!notify) return;
        if (!same) OnPropertyChanged(nameof(Visible));
        foreach (var name in FilterNames) OnPropertyChanged(name);
        RaiseSpotlight();
    }

    // ---- Filter chips ----------------------------------------------------------------------------------------

    // Launcher filters shown directly; the rest go under "More" (spec §7, §8).
    private const int VisibleLauncherChips = 3;
    private string _chipKey = "";
    private List<string> _allFilters = ["All", "Favorites", "Recent"];

    public string CurrentFilter { get; private set; } = "All";
    /// <summary>The launcher chips after All, Favorites and Recent.</summary>
    public string[] ShownChips { get; private set; } = [];
    /// <summary>The launchers under "More".</summary>
    public string[] MoreChips { get; private set; } = [];
    public bool MoreVisible => MoreChips.Length > 0;

    /// <summary>
    /// All, Favorites, Recent, then the launchers that have games (most games first), "Other" for manual games
    /// and shortcuts; launchers beyond the first few go under "More". Returns whether the chips changed.
    /// </summary>
    private bool BuildChips()
    {
        var launchers = LaunchersByGames(_games).Select(source => source.ToString()).ToList();
        if (_games.Any(g => !Format.HasBadge(g.Source))) launchers.Add("Other");

        // The current filter stays visible even if it would be under More.
        var shown = launchers.Take(VisibleLauncherChips).ToList();
        if (launchers.Contains(CurrentFilter) && !shown.Contains(CurrentFilter))
            shown[^1] = CurrentFilter;
        var more = launchers.Except(shown).ToList();
        _allFilters = ["All", "Favorites", "Recent", .. launchers];

        if (CurrentFilter != "All" && CurrentFilter is not ("Favorites" or "Recent") && !launchers.Contains(CurrentFilter)) CurrentFilter = "All";

        var key = string.Join(",", shown) + "|" + string.Join(",", more) + "|" + CurrentFilter;
        if (key == _chipKey) return false;
        _chipKey = key;
        ShownChips = [.. shown];
        MoreChips = [.. more];
        return true;
    }

    /// <summary>
    /// The launchers that have games on the shelf, in chip order (most games first). Also the tray menu's order.
    /// </summary>
    public static GameSource[] LaunchersByGames(IEnumerable<Game> shelf) =>
        [.. shelf.Where(g => Format.HasBadge(g.Source))
            .GroupBy(g => g.Source)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)];

    /// <summary>
    /// The chip menu's item (spec §21 a): "Open Steam" for a launcher chip whose launcher is installed, else null
    /// (All, Favorites, Recent, Other: no menu). Asked when the menu opens.
    /// </summary>
    public string? OpenLauncherLabel(string filter) =>
        LauncherOf(filter) is { } source && _actions.CanOpenLauncher(source) ? Strings.Format(Strings.OpenLauncher, Format.LauncherAppName(source)) : null;

    /// <summary>The chip menu's "Open …" was picked.</summary>
    public void OpenLauncher(string filter)
    {
        if (LauncherOf(filter) is { } source) _actions.OpenLauncher(source);
    }

    /// <summary>The launcher a chip filters by; null for All, Favorites, Recent and Other.</summary>
    public static GameSource? LauncherOf(string filter) =>
        filter.Length > 0 && char.IsAsciiLetter(filter[0]) && Enum.TryParse<GameSource>(filter, out var source) && Format.HasBadge(source)
            ? source
            : null;

    /// <summary>A chip or a launcher under More was picked.</summary>
    public void SelectFilter(string filter)
    {
        if (filter == CurrentFilter) return;
        CurrentFilter = filter;
        AfterFilterChange();
    }

    /// <summary>LB / RB: the previous or next filter, including the ones under More, wrapping around.</summary>
    public void CycleFilter(int step)
    {
        int at = _allFilters.IndexOf(CurrentFilter);
        CurrentFilter = _allFilters[((at < 0 ? 0 : at) + step + _allFilters.Count) % _allFilters.Count];
        AfterFilterChange();
    }

    private void AfterFilterChange()
    {
        bool chips = BuildChips();
        OnPropertyChanged(nameof(CurrentFilter));
        if (chips)
        {
            OnPropertyChanged(nameof(ShownChips));
            OnPropertyChanged(nameof(MoreVisible));
        }
        Filter(force: false, notify: true);
    }

    public static string ChipLabel(string filter) => filter switch
    {
        "All" => Strings.FilterAll,
        "Favorites" => Strings.FilterFavorites,
        "Recent" => Strings.FilterRecent,
        "Other" => Strings.SourceOther,
        nameof(GameSource.Gog) => "GOG",
        nameof(GameSource.Ea) => "EA",
        _ => filter,
    };

    // ---- Sort ------------------------------------------------------------------------------------------------

    public static readonly SortBy[] SortChoices = Enum.GetValues<SortBy>();

    public static string SortLabel(SortBy sort) => sort switch
    {
        SortBy.MostPlayed => Strings.SortMostPlayed,
        SortBy.Name => Strings.SortName,
        SortBy.RecentlyAdded => Strings.SortRecentlyAdded,
        SortBy.Size => Strings.SortSize,
        _ => Strings.SortRecentlyPlayed,
    };

    /// <summary>The sort button is an icon; its tooltip and screen-reader name say the current order.</summary>
    public string SortName => Strings.Format(Strings.SortCurrent, SortLabel(_settings.SortBy));
    public SortBy SortBy => _settings.SortBy;
    public bool FavoritesFirst => _settings.FavoritesFirst;

    public void SetSort(SortBy sort)
    {
        _settings.SortBy = sort;
        OnPropertyChanged(nameof(SortBy));
        OnPropertyChanged(nameof(SortName));
        Resort();
    }

    public void SetFavoritesFirst(bool on)
    {
        _settings.FavoritesFirst = on;
        OnPropertyChanged(nameof(FavoritesFirst));
        Resort();
    }

    private void Resort()
    {
        _sorted = Sort();
        Filter(force: true, notify: true);
        _actions.ViewSettingsChanged();
    }

    // ---- Top slot: Continue playing / This week / the crash card ---------------------------------------------

    private static readonly string[] SpotlightNames =
    [
        nameof(Placeholder), nameof(EnterGame), nameof(CrashVisible), nameof(SpotlightVisible), nameof(ShowWeek), nameof(YearOfferVisible), nameof(ContinueVisible), nameof(SwitchVisible),
    ];

    private bool _crashShown;

    private static readonly string[] ContinueNames =
    [
        nameof(ContinueGame), nameof(ContinueEyebrow), nameof(ContinueTitle), nameof(ContinueDetail),
        nameof(ContinueMilestone), nameof(MilestoneVisible), nameof(ContinuePlayName),
    ];

    /// <summary>The last played game (or the one "Welcome back" is for), worked out when the data changes.</summary>
    public Game? ContinueGame { get; private set; }
    /// <summary>"Continue playing", or "Welcome back" once after a session (§13 c).</summary>
    public string ContinueEyebrow { get; private set; } = Strings.ContinuePlaying;
    public string ContinueTitle { get; private set; } = "";
    public string ContinueDetail { get; private set; } = "";
    /// <summary>A quiet playtime milestone ("100 hours in …", §13 e), only in "Welcome back".</summary>
    public string ContinueMilestone { get; private set; } = "";
    public bool MilestoneVisible { get; private set; }
    public string ContinuePlayName { get; private set; } = Strings.Play;

    private WelcomeMoment? _welcome;

    /// <summary>
    /// "Welcome back" (proposals §13 c) in the top slot for this opening of the panel, or null for the normal
    /// slot. Chosen (and stored as shown) by the window each time the panel opens.
    /// </summary>
    public void SetWelcome(WelcomeMoment? moment)
    {
        if (_welcome is null && moment is null) return;
        _welcome = moment;
        UpdateContinue();
        UpdateSpotlight();
        foreach (var name in ContinueNames) OnPropertyChanged(name);
        RaiseSpotlight();
    }

    private YearOffer? _yearOffer;

    /// <summary>
    /// Year in review's one-time offer in December (spec §28, board 42), or null. Set by the window when the panel
    /// opens (and stored as offered there); Open or Dismiss takes it away, and the slot shows what it would otherwise.
    /// </summary>
    public void SetYearOffer(YearOffer? offer)
    {
        if (_yearOffer is null && offer is null) return;
        _yearOffer = offer;
        UpdateSpotlight();
        OnPropertyChanged(nameof(YearOfferTitle));
        OnPropertyChanged(nameof(YearOfferDetail));
        OnPropertyChanged(nameof(YearOffer)); // the view sets the covers
        RaiseSpotlight();
    }

    public YearOffer? YearOffer => _yearOffer;
    /// <summary>A game on the shelf by id, or null (the offer's covers).</summary>
    public Game? FindGame(string id) => _games.FirstOrDefault(g => g.Id == id);
    public bool YearOfferVisible { get; private set; }
    public string YearOfferTitle => _yearOffer?.Title ?? "";
    public string YearOfferDetail => _yearOffer?.Detail ?? "";

    /// <summary>The "…" menu's Year in review item, shown only when a year has enough play.</summary>
    public bool HasYearReview => _actions.HasYearReview();
    public void OpenYearReview() => _actions.OpenYearReview(null);

    /// <summary>"Welcome back" takes the slot for this opening, without the switch.</summary>
    public bool ShowsWelcome => _welcome is not null && ContinueGame?.Id == _welcome.GameId;

    public bool HasWeek { get; private set; }
    public string WeekTotal { get; private set; } = "";
    public string WeekDetail { get; private set; } = "";
    public string? WeekToolTip { get; private set; }
    public List<Bar> WeekBars { get; private set; } = [];

    public bool CrashVisible { get; private set; }
    public bool SpotlightVisible { get; private set; }
    public bool ShowWeek { get; private set; }
    public bool ContinueVisible => !ShowWeek && !YearOfferVisible;
    public bool SwitchVisible { get; private set; }

    /// <summary>The crash card takes the top slot (true) until it's dismissed or the game is played again.</summary>
    public void SetCrashShown(bool shown)
    {
        if (shown == _crashShown) return;
        _crashShown = shown;
        UpdateSpotlight();
        RaiseSpotlight();
    }

    public bool CrashShown => _crashShown;

    /// <summary>
    /// The top slot: "Continue playing" or "This week", whichever the user picked last; the switch only when both
    /// have something to show. Hidden while searching.
    /// </summary>
    private void UpdateSpotlight()
    {
        bool searching = Searching;
        YearOfferVisible = false;
        if (_crashShown)
        {
            CrashVisible = !searching;
            SpotlightVisible = false;
            return;
        }
        CrashVisible = false;
        // Year in review's offer (spec §28): after the crash card and Welcome back, before the usual slot.
        if (!searching && _yearOffer is not null && !ShowsWelcome)
        {
            YearOfferVisible = SpotlightVisible = true;
            ShowWeek = SwitchVisible = false;
            return;
        }
        bool hasContinue = ContinueGame is not null;
        SpotlightVisible = !searching && (hasContinue || HasWeek);
        if (!SpotlightVisible) return;
        bool welcome = ShowsWelcome;
        ShowWeek = !welcome && HasWeek && (!hasContinue || _settings.SpotlightShowsWeek);
        SwitchVisible = !welcome && hasContinue && HasWeek;
    }

    private void RaiseSpotlight()
    {
        foreach (var name in SpotlightNames) OnPropertyChanged(name);
    }

    /// <summary>The switch in the top slot's corner.</summary>
    public void ShowWeekCard(bool week)
    {
        if (week == _settings.SpotlightShowsWeek) return;
        _settings.SpotlightShowsWeek = week;
        UpdateSpotlight();
        RaiseSpotlight();
        _actions.ViewSettingsChanged();
    }

    private void UpdateContinue()
    {
        ContinueGame = _lastPlayed.Count == 0 ? null : _games
            .Where(g => _lastPlayed.ContainsKey(g.Id))
            .MaxBy(g => _lastPlayed[g.Id]);
        if (_welcome is not null && _games.FirstOrDefault(g => g.Id == _welcome.GameId) is { } welcomeGame) ContinueGame = welcomeGame;
        if (ContinueGame is null) return;
        ContinueTitle = ContinueGame.Name;
        if (ShowsWelcome)
        {
            ContinueEyebrow = Strings.WelcomeBack;
            ContinueDetail = Strings.Format(Strings.WelcomeDetail, Format.Duration(_welcome!.Length), Format.Playtime(_welcome.Week));
            ContinueMilestone = _welcome.ClosedLine
                ? Strings.WelcomeClosedLine
                : Strings.Format(Strings.WelcomeMilestone, _welcome.Milestone, ContinueGame.Name);
            MilestoneVisible = _welcome.Milestone > 0 || _welcome.ClosedLine;
        }
        else
        {
            ContinueEyebrow = Strings.ContinuePlaying;
            ContinueDetail = Strings.Format(Strings.Dot, Format.RelativeDay(_lastPlayed[ContinueGame.Id]), ContinueGame.SourceLabel);
            MilestoneVisible = false;
        }
        ContinuePlayName = Strings.Format(Strings.PlayGame, ContinueGame.Name);
    }

    /// <summary>
    /// "This week": total, how many games, the most played one and a bar per day, Monday to Sunday. Worked out
    /// when the shelf gets its data (the panel opening, a session ending), never on a timer.
    /// </summary>
    private void UpdateWeek(IReadOnlyList<Session> sessions)
    {
        HasWeek = false;
        if (!_settings.ShowWeeklySummary) return;
        var week = PlaytimeStats.Week(sessions, DateTime.Now);
        if (week.Total <= TimeSpan.Zero) return;
        HasWeek = true;

        WeekTotal = Format.Duration(week.Total);
        var games = Strings.Plural(week.GameCount, Strings.GameCountOne, Strings.GameCount);
        var top = week.TopGameId is { } id ? _games.FirstOrDefault(g => g.Id == id)?.Name : null;
        var detail = top is null || week.GameCount == 1 ? (top is null ? games : top) : Strings.Format(Strings.WeekGamesMost, games, top);
        WeekDetail = Strings.Format(Strings.Dot, Strings.ThisWeek, detail);
        WeekToolTip = top is null ? null : Strings.Format(Strings.WeekMostPlayed, top, Format.Duration(week.TopGameTime));
        WeekBars = week.Days.Select(d => new Bar(
            PlaytimeStats.DayLetter(d.Start),
            Strings.Format(Strings.LabelValue, PlaytimeStats.DayName(d.Start), d.Time > TimeSpan.Zero ? Format.Duration(d.Time) : Strings.NotPlayedLower),
            d.Time.TotalMinutes)).ToList();
    }

    // ---- Game mode helper notice and quick actions (§16) -----------------------------------------------------

    private bool _helperBarClosed; // closed with its ✕: not again while this panel lives

    public bool HelperNeedsUpdate { get; private set; }
    public bool HelperBarOpen => !_helperBarClosed && HelperNeedsUpdate;
    public bool HelperUpdating { get; private set; }
    public bool HelperUpdateEnabled => !HelperUpdating;
    public bool GameModeOn => _settings.GameModeEnabled;
    /// <summary>
    /// The header button: outlined while Game mode is ready (it turns on when a game starts), filled only while
    /// it's applied, so "on" doesn't read as "running all the time".
    /// </summary>
    public bool GameModeRunning { get; private set; }
    public string GameModeToolTip => GameModeRunning ? Strings.GameModeRunningToolTip : Strings.GameModeOnToolTip;

    private void UpdateHelper(IReadOnlyList<Game> games)
    {
        HelperNeedsUpdate = _actions.HelperNeedsUpdate(games);
        HelperUpdating = false;
        GameModeRunning = _settings.GameModeEnabled && _actions.GameModeRunning;
    }

    /// <summary>
    /// Game mode was switched on or off, applied or restored: only the header's Game mode button and the helper notice change, so only
    /// those are reported (a full <see cref="SetData"/> would lay the whole shelf out again).
    /// </summary>
    public void GameModeChanged(IReadOnlyList<Game> games)
    {
        UpdateHelper(games);
        OnPropertyChanged(nameof(GameModeOn));
        OnPropertyChanged(nameof(GameModeRunning));
        OnPropertyChanged(nameof(GameModeToolTip));
        OnPropertyChanged(nameof(HelperNeedsUpdate));
        OnPropertyChanged(nameof(HelperBarOpen));
        OnPropertyChanged(nameof(HelperUpdateEnabled));
    }

    public void CloseHelperBar()
    {
        if (_helperBarClosed) return;
        _helperBarClosed = true;
        OnPropertyChanged(nameof(HelperBarOpen));
    }

    public async Task UpdateHelperAsync()
    {
        HelperUpdating = true;
        OnPropertyChanged(nameof(HelperUpdateEnabled));
        try { await _actions.UpdateHelperAsync(); }
        finally
        {
            HelperUpdating = false;
            OnPropertyChanged(nameof(HelperUpdateEnabled));
        }
    }

    /// <summary>The Game mode toggle (menu item or Ctrl+G): settings to update first when the helper needs it.</summary>
    public void ToggleGameMode()
    {
        if (HelperNeedsUpdate) _actions.OpenGameMode();
        else _actions.SetGameMode(!GameModeOn);
    }

    public void SetGameMode(bool on) => _actions.SetGameMode(on);
    public void OpenGameMode() => _actions.OpenGameMode();
    public void OpenFreeSpace() => _actions.OpenFreeSpace();
    public void AddGame() => _actions.AddGame();
    public void RefreshLibrary() => _actions.Refresh();

    // ---- An update waiting (spec §25, board 28) ----------------------------------------------------------------

    /// <summary>
    /// The "…" menu's first item while an update waits for the user ("Update to 0.2.0", "Restart to update"), else
    /// null. Also puts the dot on the "…" button. The owner decides (Ask before installing, not skipped, not urgent).
    /// </summary>
    public string? UpdateMenuText { get; private set; }
    public bool UpdateDot => UpdateMenuText is not null;
    /// <summary>The "…" button's screen-reader name and tooltip: says when an update is waiting.</summary>
    public string ActionsName => UpdateDot ? Strings.MoreActionsUpdate : Strings.MoreActions;

    public void SetUpdate(string? menuText)
    {
        if (menuText == UpdateMenuText) return;
        UpdateMenuText = menuText;
        OnPropertyChanged(nameof(UpdateMenuText));
        OnPropertyChanged(nameof(UpdateDot));
        OnPropertyChanged(nameof(ActionsName));
    }

    public void StartUpdate() => _actions.StartUpdate();

    // ---- Flyout pin, footer and controller hints ---------------------------------------------------------------

    private const int FooterHintSessions = 3;
    private bool _footerHint, _padMode;

    public bool PinVisible { get; private set; }
    public bool? PinChecked { get; private set; } = false;
    public string PinLabel { get; private set; } = Strings.PinOpen;

    /// <summary>Shows the pin button in Flyout mode, pressed while pinned open.</summary>
    public void SetFlyout(bool flyout, bool pinned)
    {
        PinVisible = flyout;
        PinChecked = pinned;
        PinLabel = pinned ? Strings.Unpin : Strings.PinOpen;
        OnPropertyChanged(nameof(PinVisible));
        OnPropertyChanged(nameof(PinChecked));
        OnPropertyChanged(nameof(PinLabel));
    }

    public string FooterText { get; private set; } = "";
    public bool FooterVisible => _footerHint && !_padMode;

    /// <summary>Controller look on (a pad was used) or off (mouse or keyboard again): the button hints replace the footer.</summary>
    public bool PadMode
    {
        get => _padMode;
        set
        {
            if (!SetProperty(ref _padMode, value)) return;
            OnPropertyChanged(nameof(FooterVisible));
        }
    }

    /// <summary>B's hint: Close in the flyout (it closes it), hidden on the desktop (there's nothing to go back to).</summary>
    public bool BackHintVisible { get; private set; } = true;

    public void SetBackHint(bool flyout)
    {
        BackHintVisible = flyout;
        OnPropertyChanged(nameof(BackHintVisible));
    }
}
