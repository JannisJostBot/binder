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

    /// <summary>The sort order or "favorites first" changed; the owner saves the settings.</summary>
    public event Action? SortChanged;

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
        ContinueCover.ImageChanged += hasImage =>
            ContinuePlaceholder.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
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

        var hides = settings.HideDelaySeconds <= 0 ? "Hides when a game starts" : $"Hides {settings.HideDelaySeconds} s after a game starts";
        FooterText.Text = settings.ComeBackOnClose ? hides + " · comes back when it closes" : hides;
        UpdateContinueCard();
        UpdateWeekCard(sessions);
        Resort();
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
        CountText.Text = $"{_games.Count} installed";
        VisibleCountText.Text = _visible.Count == 1 ? "1 game" : $"{_visible.Count} games";
        SortButton.Visibility = _filter == "Recent" ? Visibility.Collapsed : Visibility.Visible;

        EmptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyActions.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text =
            _games.Count == 0 ? "No games found yet. Games from Steam, Epic, GOG, EA and Ubisoft show up here automatically, or bring in your own." :
            query.Length > 0 ? $"No games match “{query}”." :
            _filter == "Recent" ? "Games you play from the shelf show up here." :
            _filter == "Favorites" ? "No favorites yet. Star a game in its details or its right-click menu." :
            "No games from this store.";

        // Hidden while searching.
        ContinueCard.Visibility = _continueGame is not null && query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        WeekCard.Visibility = _hasWeek && query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _hasWeek;

    /// <summary>
    /// "This week": total, how many games, the most played one and a bar per day, Monday to Sunday. Worked out
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
        WeekDetail.Text = top is null || week.GameCount == 1 ? (top is null ? games : top) : $"{games} · most: {top}";
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
        if (ReferenceEquals(ContinueCover.Game, _continueGame)) ContinueCover.Refresh(); // cover may have changed
        else ContinueCover.Game = _continueGame;
        ContinueTitle.Text = _continueGame.Name;
        ContinuePlaceholder.Text = _continueGame.Name; // shown until (or unless) the cover loads
        ContinuePlaceholder.Visibility = ContinueCover.HasImage ? Visibility.Collapsed : Visibility.Visible;
        ContinueDetail.Text = $"{Format.RelativeDay(_lastPlayed[_continueGame.Id])} · {_continueGame.SourceLabel}";
        AutomationProperties.SetName(ContinuePlay, "Play " + _continueGame.Name);
    }

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
            tile.PlayRequested ??= g => PlayRequested?.Invoke(g);
            tile.Show(game, _tileWidth, _tileHeight, args.ItemIndex);
        }
        Describe(args.ItemContainer, game);
        args.Handled = true;
    }

    private void OnRowContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not Game game) return;
        if (args.ItemContainer.ContentTemplateRoot is GameRow row) row.Show(game, args.ItemIndex);
        Describe(args.ItemContainer, game);
        args.Handled = true;
    }

    private static void Describe(SelectorItem container, Game game)
    {
        AutomationProperties.SetName(container, game.Name);
        ToolTipService.SetToolTip(container, game.Name);
    }

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

    /// <summary>Sort menu (board 7): five orders as radio items, then "Favorites first".</summary>
    private void BuildSortMenu()
    {
        SortText.Text = SortLabel(_settings.SortBy);
        SortMenu.Items.Clear();
        foreach (var sort in Enum.GetValues<SortBy>())
            GameMenu.AddChoice(SortMenu, SortLabel(sort), "Sort", _settings.SortBy == sort, () =>
            {
                _settings.SortBy = sort;
                SortText.Text = SortLabel(sort);
                Resort();
                SortChanged?.Invoke();
            });
        SortMenu.Items.Add(new MenuFlyoutSeparator());
        var favoritesFirst = new ToggleMenuFlyoutItem { Text = "Favorites first", IsChecked = _settings.FavoritesFirst };
        favoritesFirst.Click += (_, _) =>
        {
            _settings.FavoritesFirst = favoritesFirst.IsChecked;
            Resort();
            SortChanged?.Invoke();
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

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game game) OpenRequested?.Invoke(game);
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

    /// <summary>Puts keyboard focus on a game (e.g. when coming back from its detail view).</summary>
    public void FocusGame(Game game)
    {
        int index = _visible.FindIndex(g => g.Id == game.Id);
        if (index >= 0) FocusIndex(index);
        else FocusSearch();
    }

    private void FocusIndex(int index)
    {
        ListViewBase list = _listMode ? Rows : Tiles;
        list.ScrollIntoView(_visible[index]);
        list.UpdateLayout();
        if (list.ContainerFromIndex(index) is Control container) container.Focus(FocusState.Keyboard);
    }

    /// <summary>Enter plays the focused game; Up from the first row returns to search. Arrows are the grid's own.</summary>
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
