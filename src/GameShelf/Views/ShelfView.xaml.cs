using System.ComponentModel;
using GameShelf.Models;
using GameShelf.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace GameShelf.Views;

/// <summary>
/// The shelf. What shows and in what order is <see cref="ShelfViewModel"/>'s; the view sizes and fills the cover
/// grid, builds the menus and filter chips, loads the top slot's art and moves keyboard and controller focus.
/// </summary>
public sealed partial class ShelfView : UserControl
{
    // Each item's 6 px padding on both sides.
    private const double ItemPadding = 12;

    // The grid's width: 560 - 2 × 20 plus the items' padding, until it's measured. Only a starting value: the
    // window's border takes a few pixels (about 15 at 150 %), so the grid gets no items before it's measured, or
    // its first layout wraps a column and then slides the covers into place.
    private double _gridWidth = 532;
    private bool _measured;

    private bool _listMode;
    private int _columns;
    private double _tileWidth = 121, _tileHeight = 161;
    private bool _buildingChips, _updatingSpotlight;

    public ShelfViewModel? ViewModel { get; private set; }

    private List<Game> Visible => ViewModel?.Visible ?? [];

    public ShelfView()
    {
        InitializeComponent();
        AutomationProperties.SetName(this, Strings.ShelfName);
        ContinueLogo.LogoChanged += hasLogo => ContinueTitle.Visibility = hasLogo ? Visibility.Collapsed : Visibility.Visible;
        Tiles.Loaded += (_, _) => ApplyPanel();
        Tiles.SizeChanged += (_, e) =>
        {            // Collapsed (another page is open) it measures 0 wide: keep the last real width.
            if (e.NewSize.Width < 200) return;
            bool first = !_measured;
            _measured = true;
            if (!first && Math.Abs(e.NewSize.Width - _gridWidth) < 0.5) return;
            _gridWidth = e.NewSize.Width;
            if (_listMode) return;
            SizeTiles();
            // Shown tiles take the new size: setting the same list again would keep them as they are.
            Tiles.ItemsSource = null;
            ShowItems();
        };
    }

    /// <summary>Connects the page to its owner once, before it's first shown.</summary>
    public void Initialize(IShelfActions actions)
    {
        if (ViewModel is not null) throw new InvalidOperationException("The shelf is already initialized.");
        ViewModel = new ShelfViewModel(actions);
        ViewModel.PropertyChanged += OnViewModelChanged;
        Bindings.Update();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = ViewModel!;
        switch (e.PropertyName)
        {
            case null or "":
                // New data: everything again.
                ApplyDensity(vm.Columns, vm.ListMode);
                BuildChips();
                BuildSortMenu();
                WeekChart.Show(vm.WeekBars, Strings.PlaytimeThisWeek);
                ToolTipService.SetToolTip(WeekDetail, vm.WeekToolTip);
                // The banner's art may have changed for the same game.
                if (vm.ContinueGame is not null && ReferenceEquals(ContinueArt.Game, vm.ContinueGame)) ContinueArt.Refresh();
                ShowItems();
                ShowSpotlight();
                break;
            case nameof(ShelfViewModel.Visible):
                ShowItems();
                break;
            case nameof(ShelfViewModel.ShownChips): // also when only the selected filter changed
                BuildChips();
                break;
            case nameof(ShelfViewModel.SwitchVisible): // the last of the top slot's names
                ShowSpotlight();
                break;
            case nameof(ShelfViewModel.SortBy):
            case nameof(ShelfViewModel.FavoritesFirst):
                BuildSortMenu();
                break;
        }
    }

    /// <summary>
    /// Hands the visible games to the grid or the list (only the one showing has items). The grid waits for its
    /// first measured width (its SizeChanged calls this again).
    /// </summary>
    private void ShowItems()
    {
        Tiles.ItemsSource = _listMode || !_measured ? null : Visible;
        Rows.ItemsSource = _listMode ? Visible : null;
    }

    // ---- Quick actions (§16) -------------------------------------------------------------------------------

    private async void OnHelperUpdateClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.UpdateHelperAsync();
    }

    private void OnHelperBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason == InfoBarCloseReason.CloseButton) ViewModel?.CloseHelperBar();
    }

    /// <summary>Built each time it opens, so it holds nothing while closed and always shows the current state.</summary>
    private void OnActionsMenuOpening(object? sender, object e)
    {
        var vm = ViewModel!;
        ActionsMenu.Items.Clear();
        if (vm.HelperNeedsUpdate)
        {
            ActionsMenu.Items.Add(MenuItem(Strings.GameModeNeedsUpdateMenu, "", vm.OpenGameMode, "Ctrl+G"));
        }
        else
        {
            var toggle = new ToggleMenuFlyoutItem
            {
                Text = Strings.GameMode,
                IsChecked = vm.GameModeOn,
                Icon = new FontIcon { Glyph = "" },
                KeyboardAcceleratorTextOverride = "Ctrl+G",
            };
            toggle.Click += (_, _) => vm.SetGameMode(toggle.IsChecked);
            ActionsMenu.Items.Add(toggle);
        }
        ActionsMenu.Items.Add(MenuItem(Strings.GameModeSettingsMenu, "", vm.OpenGameMode));
        ActionsMenu.Items.Add(new MenuFlyoutSeparator());
        ActionsMenu.Items.Add(MenuItem(Strings.FreeUpSpaceMenu, "", vm.OpenFreeSpace));
        ActionsMenu.Items.Add(MenuItem(Strings.AddGameMenu, "", vm.AddGame));
        ActionsMenu.Items.Add(MenuItem(Strings.RefreshLibrary, "", vm.RefreshLibrary, "F5"));
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
        ViewModel?.ToggleGameMode();
    }

    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel?.RefreshLibrary();
    }

    // ---- Search ----------------------------------------------------------------------------------------------

    public void FocusSearch() => SearchBox.Focus(FocusState.Programmatic);

    /// <summary>Type-to-search: adds the typed text to the search box and moves focus there.</summary>
    public void TypeIntoSearch(string text)
    {
        SearchBox.Text += text;
        FocusSearch();
        // AutoSuggestBox has no caret API; its inner TextBox does.
        if (FindDescendant<TextBox>(SearchBox) is { } box) box.SelectionStart = box.Text.Length;
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (ViewModel is { } vm) vm.Query = SearchBox.Text;
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down or VirtualKey.Enter when Visible.Count > 0:
                FocusIndex(0);
                e.Handled = true;
                break;
            case VirtualKey.Escape when SearchBox.Text.Length > 0:
                SearchBox.Text = "";
                e.Handled = true;
                break;
        }
    }

    private void OnEmptyClearClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        FocusSearch();
    }

    // ---- Top slot ----------------------------------------------------------------------------------------------

    /// <summary>The switch's selection and the banner's art, which loads only while its pane is shown.</summary>
    private void ShowSpotlight()
    {
        var vm = ViewModel!;
        if (!vm.SpotlightVisible) return;
        var item = vm.ShowWeek ? WeekItem : ContinueItem;
        if (!ReferenceEquals(SpotlightSwitch.SelectedItem, item))
        {
            _updatingSpotlight = true;
            SpotlightSwitch.SelectedItem = item;
            _updatingSpotlight = false;
        }
        if (!vm.ShowWeek && !ReferenceEquals(ContinueArt.Game, vm.ContinueGame)) ContinueArt.Game = vm.ContinueGame;
        var logoGame = vm.ShowWeek ? null : vm.ContinueGame; // loaded only while the banner shows
        // No title-to-logo swap: the title waits until the logo lookup answers.
        if (logoGame is not null && !ReferenceEquals(ContinueLogo.Game, logoGame)) ContinueTitle.Visibility = Visibility.Collapsed;
        ContinueLogo.Game = logoGame;
    }

    private void OnSpotlightChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingSpotlight || sender.SelectedItem is null) return;
        ViewModel?.ShowWeekCard(ReferenceEquals(sender.SelectedItem, WeekItem));
    }

    /// <summary>The crash card instead of the top slot, until <see cref="HideCrash"/> (dismissed or played again).</summary>
    public void ShowCrash(Game game, CrashRecord record, IReadOnlyList<Session> sessions, IReadOnlyList<CrashRecord> records)
    {
        Crash.Show(game, record, sessions, records);
        ViewModel!.SetCrashShown(true);
    }

    public void HideCrash() => ViewModel!.SetCrashShown(false);

    public CrashRecord? ShownCrash => ViewModel?.CrashShown == true ? Crash.Record : null;

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

    // ---- Grid layout -------------------------------------------------------------------------------------------

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

    private void ApplyDensity(int columns, bool listMode)
    {
        if (columns == _columns && listMode == _listMode) return;
        _columns = columns;
        _listMode = listMode;
        // Set here, before sizing: the grid's panel only takes its item size while the grid is shown.
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
            tile.DetailsRequested ??= g => ViewModel?.OpenDetails(g);
            tile.Show(game, _tileWidth, _tileHeight, args.ItemIndex);
            if (_padMode && args.ItemContainer.FocusState != FocusState.Unfocused) tile.SetPadFocus(true);
        }
        args.ItemContainer.UseSystemFocusVisuals = !_padMode;
        Describe(args.ItemContainer, game);
        args.Handled = true;
    }

    private void OnRowContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not Game game) return;
        if (args.ItemContainer.ContentTemplateRoot is GameRow row)
        {
            row.DetailsRequested ??= g => ViewModel?.OpenDetails(g);
            row.Show(game, args.ItemIndex);
        }
        Describe(args.ItemContainer, game);
        args.Handled = true;
    }

    // No tooltip on the whole item: the hover "Play" label already says what a click does, and once one tooltip is
    // open WinUI shows the next at once, so they'd follow the mouse across the grid. Cut-off titles get one (GameTile).
    private static void Describe(SelectorItem container, Game game) => AutomationProperties.SetName(container, game.Name);

    // ---- Filter chips and the sort menu --------------------------------------------------------------------

    private void BuildChips()
    {
        var vm = ViewModel!;
        _buildingChips = true;
        try
        {
            Filters.Items.Clear();
            AddChip("All", ShelfViewModel.ChipLabel("All"));
            AddChip("Favorites", ShelfViewModel.ChipLabel("Favorites"));
            AddChip("Recent", ShelfViewModel.ChipLabel("Recent"));
            foreach (var launcher in vm.ShownChips) AddChip(launcher, ShelfViewModel.ChipLabel(launcher));
        }
        finally
        {
            _buildingChips = false;
        }

        MoreMenu.Items.Clear();
        foreach (var launcher in vm.MoreChips)
            GameMenu.AddItem(MoreMenu, ShelfViewModel.ChipLabel(launcher), null, () => vm.SelectFilter(launcher));
    }

    private void AddChip(string filter, string label)
    {
        var item = new SelectorBarItem { Text = label, Tag = filter };
        // A launcher chip opens its launcher from a menu (spec §21 a); a normal click still only filters.
        if (ViewModel!.OpenLauncherLabel(filter) is not null && ShelfViewModel.LauncherOf(filter) is { } source)
        {
            AutomationProperties.SetHelpText(item, Strings.Format(Strings.ChipOpenLauncherHelp, Format.LauncherAppName(source)));
            item.ContextRequested += OnChipContextRequested;
        }
        Filters.Items.Add(item);
        if (ViewModel!.CurrentFilter == filter) Filters.SelectedItem = item;
    }

    /// <summary>Right-click, Shift+F10 or the Menu key on a launcher chip: "Open Steam", built when it opens.</summary>
    private void OnChipContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        if (sender is not SelectorBarItem { Tag: string filter } chip || ViewModel is not { } vm ||
            vm.OpenLauncherLabel(filter) is not { } label || ShelfViewModel.LauncherOf(filter) is not { } source) return;
        var item = new MenuFlyoutItem { Text = label };
        if (LauncherBadge.LoadedImage(source) is { } image) item.Icon = new ImageIcon { Source = image };
        item.Click += (_, _) => vm.OpenLauncher(filter);
        var menu = new MenuFlyout();
        menu.Items.Add(item);
        if (args.TryGetPosition(chip, out var point)) menu.ShowAt(chip, point);
        else menu.ShowAt(chip);
    }

    private void OnFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_buildingChips || sender.SelectedItem is not { Tag: string filter }) return;
        ViewModel?.SelectFilter(filter);
    }

    /// <summary>Sort menu (board 7): five orders as radio items, then "Favorites first".</summary>
    private void BuildSortMenu()
    {
        var vm = ViewModel!;
        SortMenu.Items.Clear();
        foreach (var sort in ShelfViewModel.SortChoices)
            GameMenu.AddChoice(SortMenu, ShelfViewModel.SortLabel(sort), "Sort", vm.SortBy == sort, () => vm.SetSort(sort));
        SortMenu.Items.Add(new MenuFlyoutSeparator());
        var favoritesFirst = new ToggleMenuFlyoutItem { Text = Strings.FavoritesFirst, IsChecked = vm.FavoritesFirst };
        favoritesFirst.Click += (_, _) => vm.SetFavoritesFirst(favoritesFirst.IsChecked);
        SortMenu.Items.Add(favoritesFirst);
    }

    // ---- Playing, menus, focus ---------------------------------------------------------------------------------

    /// <summary>Clicking a cover or row plays it, like Enter and like the desktop shortcuts it replaces.</summary>
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game game) ViewModel?.Play(game);
    }

    /// <summary>The game menu on right-click, the Menu key or Shift+F10 on a cover or row.</summary>
    private void OnItemContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not ListViewBase list || FindContainer(args.OriginalSource as DependencyObject) is not { } container ||
            list.ItemFromContainer(container) is not Game game || ViewModel is not { } vm) return;
        args.Handled = true;
        Windows.Foundation.Point? at = args.TryGetPosition(container, out var point) ? point : null;
        GameMenu.Show(container, at, game, includePlayAndDetails: true, action =>
        {
            switch (action)
            {
                case GameAction.Play: vm.Play(game); break;
                case GameAction.Details: vm.OpenDetails(game); break;
                default: vm.Act(game, action); break;
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
    public void FocusGame(Game game, FocusState how = FocusState.Programmatic)
    {
        int index = Visible.FindIndex(g => g.Id == game.Id);
        if (index >= 0) FocusIndex(index, how);
        else FocusSearch();
    }

    // ---- Controller (spec §4, board 3) ----

    private bool _padMode;

    /// <summary>
    /// Controller look on (a pad was used) or off (mouse or keyboard again): the button hints replace the footer,
    /// LB and RB show beside the filters, and the focused cover gets a bold ring instead of the thin system one.
    /// </summary>
    public void SetControllerMode(bool on)
    {
        if (_padMode == on) return;
        _padMode = on;
        ViewModel!.PadMode = on;
        for (int i = 0; i < Visible.Count; i++)
        {
            if (Tiles.ContainerFromIndex(i) is not SelectorItem container) continue;
            container.UseSystemFocusVisuals = !on;
            if (container.ContentTemplateRoot is GameTile tile)
                tile.SetPadFocus(on && container.FocusState != FocusState.Unfocused);
        }
    }

    private void OnTilesGotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not SelectorItem { ContentTemplateRoot: GameTile tile } item) return;
        if (_padMode) tile.SetPadFocus(true);
        else if (item.FocusState == FocusState.Keyboard) tile.SetKeyFocus(true); // not after a click or in code
    }

    private void OnTilesLostFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not SelectorItem { ContentTemplateRoot: GameTile tile }) return;
        tile.SetPadFocus(false);
        tile.SetKeyFocus(false);
    }

    /// <summary>The game whose cover or row has focus, if any.</summary>
    public Game? GameAt(DependencyObject? focused) =>
        FindContainer(focused) is { } container && ItemsControl.ItemsControlFromItemContainer(container) is ListViewBase list &&
        (list == Tiles || list == Rows) && list.ItemFromContainer(container) is Game game ? game : null;

    /// <summary>
    /// A controller move inside the grid, by index so it scrolls and works past the realized tiles. True if it
    /// was handled (also at a side edge, where focus stays); false to leave the grid upwards or when focus isn't in it.
    /// </summary>
    public bool MoveFocus(DependencyObject? focused, FocusNavigationDirection direction)
    {
        if (FindContainer(focused) is not { } container || ItemsControl.ItemsControlFromItemContainer(container) is not ListViewBase list ||
            (list != Tiles && list != Rows)) return false;
        int i = list.IndexFromContainer(container), count = Visible.Count;
        if (i < 0 || i >= count) return false;
        int columns = _listMode ? 1 : Math.Max(1, _columns);
        int target = direction switch
        {
            FocusNavigationDirection.Left => i % columns > 0 ? i - 1 : -1,
            FocusNavigationDirection.Right => i % columns < columns - 1 && i + 1 < count ? i + 1 : -1,
            FocusNavigationDirection.Up => i - columns,
            // Down from the row above a short last row lands on its last cover.
            FocusNavigationDirection.Down => i + columns < count ? i + columns : i / columns < (count - 1) / columns ? count - 1 : -1,
            _ => -1,
        };
        if (direction == FocusNavigationDirection.Up && target < 0) return false;
        if (target >= 0) FocusIndex(target);
        return true;
    }

    public void FocusFirstGame()
    {
        if (Visible.Count > 0) FocusIndex(0);
    }

    /// <summary>The scroll viewer of the grid or list that's showing, for the right stick.</summary>
    public ScrollViewer? GridScroller => FindDescendant<ScrollViewer>(_listMode ? Rows : Tiles);

    private void FocusIndex(int index, FocusState how = FocusState.Keyboard)
    {
        ListViewBase list = _listMode ? Rows : Tiles;
        list.ScrollIntoView(Visible[index]);
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
        var visible = Visible;
        if (i < 0 || i >= visible.Count) return;
        int columns = _listMode ? 1 : _columns;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                ViewModel?.Play(visible[i]);
                e.Handled = true;
                break;
            case VirtualKey.Space:
                ViewModel?.OpenDetails(visible[i]);
                e.Handled = true;
                break;
            case VirtualKey.Up when i < columns:
                FocusSearch();
                e.Handled = true;
                break;
        }
    }

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
