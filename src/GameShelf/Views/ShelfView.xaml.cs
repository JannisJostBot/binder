using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace GameShelf.Views;

public sealed partial class ShelfView : UserControl
{
    // Each item's 6 px padding on both sides.
    private const double ItemPadding = 12;

    // The grid's width: 560 - 2 × 20 plus the items' padding, until it's measured (the window border takes a
    // pixel or so, and a column that doesn't fit exactly wraps).
    private double _gridWidth = 532;

    /// <summary>A shelf choice kept in settings changed (sort order, favorites first, the top slot); the owner saves.</summary>
    public event Action? ViewSettingsChanged;

    public event Action<Game>? PlayRequested;
    public event Action<Game>? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ImportRequested;
    public event Action? PinRequested;
    public event Action? AddGameRequested;
    /// <summary>Right-click menu actions other than Play and Details.</summary>
    public event Action<Game, GameAction>? ActionRequested;

    private List<Game> _games = [];
    private Dictionary<string, DateTimeOffset> _lastPlayed = [];
    private Dictionary<string, TimeSpan> _playtime = [];
    private List<Game> _visible = [];
    // The shelf in sort order. Sorting (culture-aware name compares) happens when the data or the sort order
    // changes, not on every keystroke: typing only filters this list, O(n), and keeps its order.
    private List<Game> _sorted = [];
    // Stats are recomputed only when the session list changes (it's replaced when a game ends).
    private IReadOnlyList<Session>? _statsFor;
    private int _statsCount = -1;
    private string _filter = "All";
    private Settings _settings = new();
    private bool _listMode;
    private int _columns;
    private double _tileWidth = 121, _tileHeight = 161;
    private string _chipKey = "";
    private bool _buildingChips;

    // Launcher filters shown directly; the rest go under "More" (spec §7, §8).
    private const int VisibleLauncherChips = 3;
    private Game? _continueGame;

    public ShelfView()
    {
        InitializeComponent();
        Tiles.Loaded += (_, _) => ApplyPanel();
        Tiles.SizeChanged += (_, e) =>
        {
            // Collapsed (another page is open) it measures 0 wide: keep the last real width.
            if (e.NewSize.Width < 200 || Math.Abs(e.NewSize.Width - _gridWidth) < 0.5) return;
            _gridWidth = e.NewSize.Width;
            if (_listMode) return;
            SizeTiles();
            Refresh(force: true); // shown tiles take the new size
        };
    }

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
            game.LastPlayedText = _lastPlayed.TryGetValue(game.Id, out var when) ? Format.RelativeDay(when) : "Never";

        ApplyDensity(settings.GridDensity);
        BuildChips();
        BuildSortMenu();

        // Worked out when the panel gets its data (it opens, a session ends), never on a timer.
        GreetingText.Text = Format.Greeting(DateTime.Now);
        // The footer teaches hide and return; after a few games from the shelf it has done its job.
        Footer.Visibility = sessions.Count < FooterHintSessions ? Visibility.Visible : Visibility.Collapsed;
        var hides = settings.HideDelaySeconds <= 0 ? "Hides when a game starts" : $"Hides {settings.HideDelaySeconds} s after a game starts";
        FooterText.Text = settings.ComeBackOnClose ? hides + " · comes back when it closes" : hides;
        UpdateContinueCard();
        UpdateWeekCard(sessions);
        UpdateHelperBar(games);
        Resort();
    }

    // ---- Game mode helper notice ---------------------------------------------------------------------------

    /// <summary>The Game mode helper needs updating; the owner runs it (UAC) and calls <see cref="SetData"/> again.</summary>
    public event Func<Task>? HelperUpdateRequested;

    private bool _helperBarClosed; // closed with its ✕: not again while this panel lives
    private bool _helperNeedsUpdate;

    /// <summary>The notice, and the "…" button's dot and name (Game mode on or off).</summary>
    private void UpdateHelperBar(List<Game> games)
    {
        _helperNeedsUpdate = GameModeService.HelperNeedsUpdate(_settings, games);
        HelperBar.IsOpen = !_helperBarClosed && _helperNeedsUpdate;
        HelperUpdateButton.IsEnabled = true;
        bool on = _settings.GameModeEnabled;
        GameModeDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(ActionsButton, on ? "More actions, Game mode on" : "More actions");
    }

    private async void OnHelperUpdateClick(object sender, RoutedEventArgs e)
    {
        if (HelperUpdateRequested is null) return;
        HelperUpdateButton.IsEnabled = false;
        await HelperUpdateRequested();
        HelperUpdateButton.IsEnabled = true;
    }

    private void OnHelperBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason == InfoBarCloseReason.CloseButton) _helperBarClosed = true;
    }

    // ---- Quick actions (§16) -------------------------------------------------------------------------------

    /// <summary>Game mode for all games turned on (true) or off; the owner saves it.</summary>
    public event Action<bool>? GameModeToggled;
    public event Action? GameModeSettingsRequested;
    public event Action? FreeSpaceRequested;
    public event Action? RefreshRequested;

    /// <summary>Built each time it opens, so it holds nothing while closed and always shows the current state.</summary>
    private void OnActionsMenuOpening(object? sender, object e)
    {
        ActionsMenu.Items.Clear();
        if (_helperNeedsUpdate)
        {
            ActionsMenu.Items.Add(MenuItem("Game mode (needs update)", "", () => GameModeSettingsRequested?.Invoke(), "Ctrl+G"));
        }
        else
        {
            var toggle = new ToggleMenuFlyoutItem
            {
                Text = "Game mode",
                IsChecked = _settings.GameModeEnabled,
                Icon = new FontIcon { Glyph = "" },
                KeyboardAcceleratorTextOverride = "Ctrl+G",
            };
            toggle.Click += (_, _) => GameModeToggled?.Invoke(toggle.IsChecked);
            ActionsMenu.Items.Add(toggle);
        }
        ActionsMenu.Items.Add(MenuItem("Game mode settings…", "", () => GameModeSettingsRequested?.Invoke()));
        ActionsMenu.Items.Add(new MenuFlyoutSeparator());
        ActionsMenu.Items.Add(MenuItem("Free up space…", "", () => FreeSpaceRequested?.Invoke()));
        ActionsMenu.Items.Add(MenuItem("Add a game…", "", () => AddGameRequested?.Invoke()));
        ActionsMenu.Items.Add(MenuItem("Refresh library", "", () => RefreshRequested?.Invoke(), "F5"));
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action, string? shortcut = null)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        if (shortcut is not null) item.KeyboardAcceleratorTextOverride = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    private void OnGameModeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true; // not the button's own click, which would open the menu
        if (_helperNeedsUpdate) GameModeSettingsRequested?.Invoke();
        else GameModeToggled?.Invoke(!_settings.GameModeEnabled);
    }

    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RefreshRequested?.Invoke();
    }

    /// <summary>Sorts once for the current data and sort order, then shows the filtered result.</summary>
    private void Resort()
    {
        _sorted = ShelfOrder.Sort(_games, _settings.SortBy, _settings.FavoritesFirst, _lastPlayed, _playtime);
        Refresh(force: true);
    }

    public void FocusSearch() => SearchBox.Focus(FocusState.Programmatic);

    /// <summary>Type-to-search: adds the typed text to the search box and moves focus there.</summary>
    public void TypeIntoSearch(string text)
    {
        SearchBox.Text += text;
        FocusSearch();
        // AutoSuggestBox has no caret API; its inner TextBox does.
        if (FindDescendant<TextBox>(SearchBox) is { } box) box.SelectionStart = box.Text.Length;
    }

    /// <summary>
    /// Filters the sorted shelf by filter and search text. <paramref name="force"/> re-renders even when the same
    /// games match (their contents changed); otherwise an unchanged result keeps the realized tiles as they are.
    /// </summary>
    private void Refresh(bool force = false)
    {
        var query = SearchBox.Text.Trim();
        Func<Game, bool>? chip = _filter switch
        {
            "All" => null,
            "Favorites" => g => g.Favorite,
            "Recent" => g => _lastPlayed.ContainsKey(g.Id),
            "Other" => g => !LauncherIcons.HasBadge(g.Source),
            _ when Enum.TryParse<GameSource>(_filter, out var source) => g => g.Source == source,
            _ => null,
        };
        var visible = new List<Game>(_sorted.Count);
        foreach (var game in _sorted)
        {
            if (chip is not null && !chip(game)) continue;
            if (query.Length > 0 && !game.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
            visible.Add(game);
        }
        // "Recent" is its own order (only played games, usually a few); everything else follows the sort menu.
        if (_filter == "Recent") visible.Sort((a, b) => _lastPlayed[b.Id].CompareTo(_lastPlayed[a.Id]));

        bool same = !force && visible.Count == _visible.Count && visible.SequenceEqual(_visible, ReferenceEqualityComparer.Instance);
        _visible = visible;
        if (!same)
        {
            Tiles.ItemsSource = _listMode ? null : _visible;
            Rows.ItemsSource = _listMode ? _visible : null;
        }
        SearchBox.PlaceholderText = _sorted.Count == 1 ? "Search 1 game" : $"Search {_sorted.Count} games";
        SortButton.Visibility = _filter == "Recent" ? Visibility.Collapsed : Visibility.Visible;

        EmptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool noMatch = _games.Count > 0 && query.Length > 0;
        EmptyIcon.Glyph = _games.Count == 0 ? "\uE7FC" : query.Length > 0 ? "\uE721" : _filter == "Favorites" ? "\uE734" : "\uE81C";
        EmptyActions.Visibility = _games.Count == 0 || noMatch ? Visibility.Visible : Visibility.Collapsed;
        EmptyClearButton.Visibility = noMatch ? Visibility.Visible : Visibility.Collapsed;
        EmptyImportButton.Visibility = noMatch ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Text =
            _games.Count == 0 ? "No games found yet. Games from Steam, Epic, GOG, EA, Ubisoft, Xbox and Rockstar show up here automatically, or bring in your own." :
            query.Length > 0 ? $"No games match “{query}”. Check the spelling, or add the game if it isn't installed through a launcher." :
            _filter == "Recent" ? "Games you play from the shelf show up here." :
            _filter == "Favorites" ? "No favorites yet. Star a game in its details or its right-click menu." :
            "No games from this store.";

        UpdateSpotlight(searching: query.Length > 0);
    }

    private const int FooterHintSessions = 3;
    private bool _updatingSpotlight;

    /// <summary>
    /// The top slot: "Continue playing" or "This week", whichever the user picked last; the switch only when both
    /// have something to show. Hidden while searching. The hero art loads only while its pane is shown.
    /// </summary>
    private void UpdateSpotlight(bool searching)
    {
        if (_crashShown)
        {
            Crash.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
            Spotlight.Visibility = Visibility.Collapsed;
            return;
        }
        bool hasContinue = _continueGame is not null;
        Spotlight.Visibility = !searching && (hasContinue || _hasWeek) ? Visibility.Visible : Visibility.Collapsed;
        if (Spotlight.Visibility == Visibility.Collapsed) return;

        bool showWeek = _hasWeek && (!hasContinue || _settings.SpotlightShowsWeek);
        WeekPane.Visibility = showWeek ? Visibility.Visible : Visibility.Collapsed;
        ContinuePane.Visibility = showWeek ? Visibility.Collapsed : Visibility.Visible;
        SpotlightSwitchSurface.Visibility = hasContinue && _hasWeek ? Visibility.Visible : Visibility.Collapsed;
        var item = showWeek ? WeekItem : ContinueItem;
        if (!ReferenceEquals(SpotlightSwitch.SelectedItem, item))
        {
            _updatingSpotlight = true;
            SpotlightSwitch.SelectedItem = item;
            _updatingSpotlight = false;
        }
        if (!showWeek && !ReferenceEquals(ContinueArt.Game, _continueGame)) ContinueArt.Game = _continueGame;
    }

    private bool _crashShown;

    /// <summary>The crash card instead of the top slot, until <see cref="HideCrash"/> (dismissed or played again).</summary>
    public void ShowCrash(Game game, CrashRecord record, IReadOnlyList<Session> sessions, IReadOnlyList<CrashRecord> records)
    {
        Crash.Show(game, record, sessions, records);
        _crashShown = true;
        UpdateSpotlight(searching: SearchBox.Text.Length > 0);
    }

    public void HideCrash()
    {
        if (!_crashShown) return;
        _crashShown = false;
        Crash.Visibility = Visibility.Collapsed;
        UpdateSpotlight(searching: SearchBox.Text.Length > 0);
    }

    public CrashRecord? ShownCrash => _crashShown ? Crash.Record : null;

    public event Action<Game>? CrashPlayRequested
    {
        add => Crash.PlayRequested += value;
        remove => Crash.PlayRequested -= value;
    }

    public event Action<CrashRecord>? CrashDismissed
    {
        add => Crash.Dismissed += value;
        remove => Crash.Dismissed -= value;
    }

    private void OnSpotlightChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingSpotlight || sender.SelectedItem is null) return;
        bool week = ReferenceEquals(sender.SelectedItem, WeekItem);
        if (week == _settings.SpotlightShowsWeek) return;
        _settings.SpotlightShowsWeek = week;
        UpdateSpotlight(searching: false);
        ViewSettingsChanged?.Invoke();
    }

    private void OnEmptyClearClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        FocusSearch();
    }

    private bool _hasWeek;

    /// <summary>
    /// "This week" in the top slot: total, how many games, the most played one and a bar per day, Monday to Sunday. Worked out
    /// when the shelf gets its data (the panel opening, a session ending), never on a timer.
    /// </summary>
    private void UpdateWeekCard(IReadOnlyList<Session> sessions)
    {
        _hasWeek = false;
        if (!_settings.ShowWeeklySummary) return;
        var week = PlaytimeStats.Week(sessions, DateTime.Now);
        if (week.Total <= TimeSpan.Zero) return;
        _hasWeek = true;

        WeekTotal.Text = Format.Duration(week.Total);
        var games = week.GameCount == 1 ? "1 game" : $"{week.GameCount} games";
        var top = week.TopGameId is { } id ? _games.FirstOrDefault(g => g.Id == id)?.Name : null;
        var detail = top is null || week.GameCount == 1 ? (top is null ? games : top) : $"{games} · most: {top}";
        WeekDetail.Text = "This week · " + detail;
        ToolTipService.SetToolTip(WeekDetail, top is null ? null : $"Most played: {top}, {Format.Duration(week.TopGameTime)}");
        WeekChart.Show(
            week.Days.Select(d => new Bar(
                PlaytimeStats.DayLetter(d.Start),
                $"{PlaytimeStats.DayName(d.Start)}: {(d.Time > TimeSpan.Zero ? Format.Duration(d.Time) : "not played")}",
                d.Time.TotalMinutes)).ToList(),
            "Playtime this week");
    }

    /// <summary>The last played game, worked out when the data changes (not per keystroke).</summary>
    private void UpdateContinueCard()
    {
        _continueGame = _lastPlayed.Count == 0 ? null : _games
            .Where(g => _lastPlayed.ContainsKey(g.Id))
            .MaxBy(g => _lastPlayed[g.Id]);
        if (_continueGame is null) return;
        // The art itself is set by UpdateSpotlight, only while the banner is shown.
        if (ReferenceEquals(ContinueArt.Game, _continueGame)) ContinueArt.Refresh(); // its art may have changed
        ContinueTitle.Text = _continueGame.Name;
        ContinueDetail.Text = $"{Format.RelativeDay(_lastPlayed[_continueGame.Id])} · {_continueGame.SourceLabel}";
        AutomationProperties.SetName(ContinuePlay, "Play " + _continueGame.Name);
    }

    /// <summary>
    /// How many items fit in the grid (or list) as laid out now, rounded up to whole rows: the covers that must
    /// be drawn before the panel shows. 0 when the shelf isn't laid out (another page is open).
    /// </summary>
    public int VisibleItemCount()
    {
        if (Visibility != Visibility.Visible) return 0;
        if (_listMode)
            return Rows.ActualHeight > 0 ? (int)Math.Ceiling(Rows.ActualHeight / ListRowHeight) : 0;
        if (Tiles.ActualHeight <= 0) return 0;
        double rowHeight = _tileHeight + ItemPadding + 28; // as SizeTiles sets ItemHeight
        return _columns * (int)Math.Ceiling(Tiles.ActualHeight / rowHeight);
    }

    /// <summary>A list row's height, near enough for <see cref="VisibleItemCount"/> (rounds the count up).</summary>
    private const double ListRowHeight = 52;

    /// <summary>Cover size: Small = 5 columns, Medium = 4, Large = 3, or the list (board 11).</summary>
    private void ApplyDensity(GridDensity density)
    {
        bool listMode = density == GridDensity.List;
        int columns = density switch { GridDensity.Small => 5, GridDensity.Large => 3, GridDensity.List => 1, _ => 4 };
        if (columns == _columns && listMode == _listMode) return;
        _columns = columns;
        _listMode = listMode;
        Tiles.Visibility = listMode ? Visibility.Collapsed : Visibility.Visible;
        Rows.Visibility = listMode ? Visibility.Visible : Visibility.Collapsed;
        if (listMode) return;

        SizeTiles();
    }

    private void SizeTiles()
    {
        // A little slack: at 125 or 150 % scaling the items round up to whole pixels, and a row that comes out
        // exactly as wide as the grid wraps its last item.
        _tileWidth = Math.Floor((_gridWidth - 2) / _columns) - ItemPadding;
        _tileHeight = Math.Round(_tileWidth * 4 / 3);
        ApplyPanel();
    }

    /// <summary>Item size of the cover grid's panel (exists once the grid is loaded).</summary>
    private void ApplyPanel()
    {
        if (_listMode || Tiles.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        panel.ItemWidth = _tileWidth + ItemPadding;
        panel.ItemHeight = _tileHeight + ItemPadding + 28; // cover, padding, title
        panel.MaximumRowsOrColumns = _columns;
    }

    private void OnTileContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not Game game) return;
        if (args.ItemContainer.ContentTemplateRoot is GameTile tile)
        {
            tile.DetailsRequested ??= g => OpenRequested?.Invoke(g);
            tile.Show(game, _tileWidth, _tileHeight, args.ItemIndex);
        }
        Describe(args.ItemContainer, game);
        args.Handled = true;
    }

    private void OnRowContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not Game game) return;
        if (args.ItemContainer.ContentTemplateRoot is GameRow row)
        {
            row.DetailsRequested ??= g => OpenRequested?.Invoke(g);
            row.Show(game, args.ItemIndex);
        }
        Describe(args.ItemContainer, game);
        args.Handled = true;
    }

    // No tooltip on the whole item: the hover "Play" label already says what a click does, and once one tooltip is
    // open WinUI shows the next at once, so they'd follow the mouse across the grid. Cut-off titles get one (GameTile).
    private static void Describe(SelectorItem container, Game game) => AutomationProperties.SetName(container, game.Name);

    /// <summary>
    /// All, Favorites, Recent, then the launchers that have games (most games first), "Other" for manual games
    /// and shortcuts; launchers beyond the first few go under "More". Rebuilt only when that set changes.
    /// </summary>
    private void BuildChips()
    {
        var launchers = _games.Where(g => LauncherIcons.HasBadge(g.Source))
            .GroupBy(g => g.Source)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key.ToString())
            .ToList();
        if (_games.Any(g => !LauncherIcons.HasBadge(g.Source))) launchers.Add("Other");

        // The current filter stays visible even if it would be under More.
        var shown = launchers.Take(VisibleLauncherChips).ToList();
        if (launchers.Contains(_filter) && !shown.Contains(_filter))
            shown[^1] = _filter;
        var more = launchers.Except(shown).ToList();

        if (_filter != "All" && _filter is not ("Favorites" or "Recent") && !launchers.Contains(_filter)) _filter = "All";

        var key = string.Join(",", shown) + "|" + string.Join(",", more) + "|" + _filter;
        if (key == _chipKey) return;
        _chipKey = key;

        _buildingChips = true;
        try
        {
            Filters.Items.Clear();
            AddChip("All", "All");
            AddChip("Favorites", "Favorites");
            AddChip("Recent", "Recent");
            foreach (var launcher in shown) AddChip(launcher, ChipLabel(launcher));
        }
        finally
        {
            _buildingChips = false;
        }

        MoreMenu.Items.Clear();
        foreach (var launcher in more)
            GameMenu.AddItem(MoreMenu, ChipLabel(launcher), null, () =>
            {
                _filter = launcher;
                BuildChips();
                Refresh();
            });
        MoreFilters.Visibility = more.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddChip(string filter, string label)
    {
        var item = new SelectorBarItem { Text = label, Tag = filter };
        Filters.Items.Add(item);
        if (_filter == filter) Filters.SelectedItem = item;
    }

    private void OnFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_buildingChips || sender.SelectedItem is not { Tag: string filter } || filter == _filter) return;
        _filter = filter;
        BuildChips();
        Refresh();
    }

    private static string ChipLabel(string filter) => filter switch
    {
        nameof(GameSource.Gog) => "GOG",
        nameof(GameSource.Ea) => "EA",
        _ => filter,
    };

    private static string SortLabel(SortBy sort) => sort switch
    {
        SortBy.MostPlayed => "Most played",
        SortBy.Name => "Name (A–Z)",
        SortBy.RecentlyAdded => "Recently added",
        SortBy.Size => "Size on disk",
        _ => "Recently played",
    };

    /// <summary>The sort button is an icon; its tooltip and screen-reader name say the current order.</summary>
    private void ShowSort(SortBy sort)
    {
        var label = "Sort: " + SortLabel(sort);
        ToolTipService.SetToolTip(SortButton, label);
        AutomationProperties.SetName(SortButton, label);
    }

    /// <summary>Sort menu (board 7): five orders as radio items, then "Favorites first".</summary>
    private void BuildSortMenu()
    {
        ShowSort(_settings.SortBy);
        SortMenu.Items.Clear();
        foreach (var sort in Enum.GetValues<SortBy>())
            GameMenu.AddChoice(SortMenu, SortLabel(sort), "Sort", _settings.SortBy == sort, () =>
            {
                _settings.SortBy = sort;
                ShowSort(sort);
                Resort();
                ViewSettingsChanged?.Invoke();
            });
        SortMenu.Items.Add(new MenuFlyoutSeparator());
        var favoritesFirst = new ToggleMenuFlyoutItem { Text = "Favorites first", IsChecked = _settings.FavoritesFirst };
        favoritesFirst.Click += (_, _) =>
        {
            _settings.FavoritesFirst = favoritesFirst.IsChecked;
            Resort();
            ViewSettingsChanged?.Invoke();
        };
        SortMenu.Items.Add(favoritesFirst);
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Refresh();

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down or VirtualKey.Enter when _visible.Count > 0:
                FocusIndex(0);
                e.Handled = true;
                break;
            case VirtualKey.Escape when SearchBox.Text.Length > 0:
                SearchBox.Text = "";
                e.Handled = true;
                break;
        }
    }

    /// <summary>Clicking a cover or row plays it, like Enter and like the desktop shortcuts it replaces.</summary>
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game game) PlayRequested?.Invoke(game);
    }

    /// <summary>The game menu on right-click, the Menu key or Shift+F10 on a cover or row.</summary>
    private void OnItemContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not ListViewBase list || FindContainer(args.OriginalSource as DependencyObject) is not { } container ||
            list.ItemFromContainer(container) is not Game game) return;
        args.Handled = true;
        Windows.Foundation.Point? at = args.TryGetPosition(container, out var point) ? point : null;
        GameMenu.Show(container, at, game, includePlayAndDetails: true, action =>
        {
            switch (action)
            {
                case GameAction.Play: PlayRequested?.Invoke(game); break;
                case GameAction.Details: OpenRequested?.Invoke(game); break;
                default: ActionRequested?.Invoke(game, action); break;
            }
        });
    }

    private static SelectorItem? FindContainer(DependencyObject? element)
    {
        while (element is not null and not SelectorItem) element = VisualTreeHelper.GetParent(element);
        return element as SelectorItem;
    }

    /// <summary>
    /// Puts focus back on a game when coming back from its detail view. Programmatic, so WinUI draws the focus
    /// ring only if the last input was the keyboard (Esc), not after clicking Back with the mouse.
    /// </summary>
    public void FocusGame(Game game)
    {
        int index = _visible.FindIndex(g => g.Id == game.Id);
        if (index >= 0) FocusIndex(index, FocusState.Programmatic);
        else FocusSearch();
    }

    private void FocusIndex(int index, FocusState how = FocusState.Keyboard)
    {
        ListViewBase list = _listMode ? Rows : Tiles;
        list.ScrollIntoView(_visible[index]);
        list.UpdateLayout();
        if (list.ContainerFromIndex(index) is Control container) container.Focus(how);
    }

    /// <summary>
    /// Enter plays the focused game and Space opens its details (handled here, before the grid would turn Space
    /// into a click); Up from the first row returns to search. Arrows are the grid's own.
    /// </summary>
    private void OnGridKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not ListViewBase list || FindContainer(e.OriginalSource as DependencyObject) is not { } container) return;
        int i = list.IndexFromContainer(container);
        if (i < 0 || i >= _visible.Count) return;
        int columns = _listMode ? 1 : _columns;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                PlayRequested?.Invoke(_visible[i]);
                e.Handled = true;
                break;
            case VirtualKey.Space:
                OpenRequested?.Invoke(_visible[i]);
                e.Handled = true;
                break;
            case VirtualKey.Up when i < columns:
                FocusSearch();
                e.Handled = true;
                break;
        }
    }

    private void OnContinuePlayClick(object sender, RoutedEventArgs e)
    {
        if (_continueGame is not null) PlayRequested?.Invoke(_continueGame);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnPinClick(object sender, RoutedEventArgs e) => PinRequested?.Invoke();

    /// <summary>Shows the pin button in Flyout mode, pressed while pinned open.</summary>
    public void SetFlyout(bool flyout, bool pinned)
    {
        PinButton.Visibility = flyout ? Visibility.Visible : Visibility.Collapsed;
        PinButton.IsChecked = pinned;
        var label = pinned ? "Unpin (closes when you click elsewhere)" : "Pin open";
        ToolTipService.SetToolTip(PinButton, label);
        AutomationProperties.SetName(PinButton, label);
    }

    private void OnEmptyImportClick(object sender, RoutedEventArgs e) => ImportRequested?.Invoke();

    private void OnEmptyAddClick(object sender, RoutedEventArgs e) => AddGameRequested?.Invoke();

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
