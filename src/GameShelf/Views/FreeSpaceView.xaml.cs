using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace GameShelf.Views;

/// <summary>
/// What the Free up space page was doing, kept in <see cref="App.State"/> (memory only) while it's open: opening
/// a launcher to uninstall a game takes the focus and closes the flyout, and reopening it continues here.
/// </summary>
public sealed class FreeSpaceState
{
    public long Need { get; set; }
    public string? Drive { get; set; }
    /// <summary>The drive was chosen (by the user, or opened from a game's details); otherwise it follows "Best choice".</summary>
    public bool DrivePicked { get; set; }
    /// <summary>Tiles the user clicked: game id → removed (true) or kept (false), over the plan's own choice.</summary>
    public Dictionary<string, bool> Overrides { get; } = [];
    /// <summary>The games being uninstalled one at a time, and which one is next (-1: not started).</summary>
    public List<string> Queue { get; } = [];
    public int Step { get; set; } = -1;
    /// <summary>Where Back goes: the game whose details opened the page, else Settings.</summary>
    public string? ReturnGameId { get; set; }

    public bool Uninstalling => Step >= 0 && Step < Queue.Count;
}

/// <summary>
/// Free up space (spec §12, board 17): the new game's size in, a plan per drive out, shown as a treemap of the
/// chosen drive whose tiles can be added or left out, then each removal handed to the game's own launcher.
/// Built when opened, static while shown (no timers); everything is released when the page closes.
/// </summary>
public sealed partial class FreeSpaceView : UserControl
{
    public event Action? BackRequested;
    public event Action? CheckAgainRequested;

    private const double Gap = 2;

    private FreeSpaceState _state = new();
    private List<DriveSpace> _drives = [];
    private Dictionary<string, List<SpaceGame>> _byDrive = [];
    private List<DrivePlan> _plans = [];
    private int _best = -1;
    private bool _updating;
    private readonly Dictionary<string, Border> _iconBoxes = [];
    // Icons loaded while the page is open (null: the game has none), and those on their way.
    private readonly Dictionary<string, IconPixels?> _icons = [];
    private readonly HashSet<string> _iconLoading = [];

    public FreeSpaceView()
    {
        InitializeComponent();
    }

    public FreeSpaceState State => _state;

    /// <summary>
    /// Shows the page for <paramref name="state"/>, reading drive space, the games on each drive and which games
    /// are running right now. Called on open, on "Check again" and when the panel reopens mid-uninstall.
    /// </summary>
    public void Show(FreeSpaceState state, IReadOnlyList<Game> games, IReadOnlyList<Session> sessions)
    {
        _state = state;
        var (lastPlayed, playtime) = ShelfOrder.Stats(sessions);
        var installed = games.Where(g => SpacePlanner.DriveOf(g) is not null).ToList();
        _byDrive = SpacePlanner.ByDrive(installed, playtime, lastPlayed, DateTimeOffset.Now, RunningGames.Ids(installed));
        _drives = ReadDrives();

        if (_state.Drive is null || !_drives.Any(d => d.Root == _state.Drive))
        {
            _state.DrivePicked = false;
            // Until there's a size to plan for: the drive with the most game data.
            _state.Drive = _drives.OrderByDescending(d => GamesOn(d.Root).Sum(g => g.Size)).Select(d => d.Root).FirstOrDefault();
        }

        _updating = true;
        NeedBox.Text = _state.Need > 0 ? (_state.Need / SpacePlanner.GB).ToString() : "";
        _updating = false;
        BackButton.SetValue(ToolTipService.ToolTipProperty, _state.ReturnGameId is null ? "Back to settings (Esc)" : "Back to the game (Esc)");
        Recalculate();
    }

    public void FocusFirst() => NeedBox.Focus(FocusState.Programmatic);

    /// <summary>Drops the treemap, icons and data (the page closed).</summary>
    public void Release()
    {
        Map.Children.Clear();
        PickedList.Children.Clear();
        DriveChoice.Items.Clear();
        _iconBoxes.Clear();
        _icons.Clear();
        _byDrive = [];
        _plans = [];
        _drives = [];
    }

    private List<SpaceGame> GamesOn(string root) => _byDrive.TryGetValue(root, out var list) ? list : [];

    /// <summary>Fixed drives that are ready, read now (no watchers).</summary>
    private static List<DriveSpace> ReadDrives()
    {
        var drives = new List<DriveSpace>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                drives.Add(new DriveSpace(drive.RootDirectory.FullName.ToUpperInvariant(), label, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return drives.OrderBy(d => d.Root, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---- The plan --------------------------------------------------------------------------------------------

    private void Recalculate()
    {
        _plans = _drives.Select(d => SpacePlanner.PlanDrive(d, GamesOn(d.Root), _state.Need)).ToList();
        _best = _state.Need > 0 ? SpacePlanner.Best(_plans) : -1;
        if (!_state.DrivePicked && _best >= 0) _state.Drive = _plans[_best].Drive.Root;
        ShowDrives();
        ShowMap();
        ShowSummary();
    }

    private DrivePlan? Current => _plans.FirstOrDefault(p => p.Drive.Root == _state.Drive);

    /// <summary>The plan's picks with the user's tile clicks applied, in the plan's order then the added ones.</summary>
    private List<SpaceGame> Chosen(DrivePlan plan)
    {
        var chosen = plan.Picked.Where(g => !_state.Overrides.TryGetValue(g.Game.Id, out var on) || on).ToList();
        foreach (var g in plan.Games)
            if (_state.Overrides.TryGetValue(g.Game.Id, out var on) && on && !chosen.Contains(g)) chosen.Add(g);
        return chosen;
    }

    private void OnNeedTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args) =>
        args.Cancel = args.NewText.Any(c => !char.IsAsciiDigit(c));

    private void OnNeedChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        long gb = long.TryParse(NeedBox.Text, out var value) ? value : 0;
        _state.Need = gb * SpacePlanner.GB;
        ResetChoice();
        Recalculate(); // cheap: a few dozen games per drive
    }

    /// <summary>A new size or drive starts from the plan again: clicked tiles and an unfinished uninstall no longer apply.</summary>
    private void ResetChoice()
    {
        _state.Overrides.Clear();
        _state.Queue.Clear();
        _state.Step = -1;
    }

    // The TextBox's clear button would sit on top of the "GB" unit; the field is short enough without it.
    private void OnNeedBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (FindChild<Button>(NeedBox, "DeleteButton") is { } clear)
        {
            // Its visual states toggle Visibility, so make it invisible and inert in ways they don't touch.
            clear.Width = 0;
            clear.MinWidth = 0;
            clear.Margin = new Thickness(0);
            clear.Opacity = 0;
            clear.IsHitTestVisible = false;
            clear.IsTabStop = false;
            AutomationProperties.SetAccessibilityView(clear, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        }
    }

    private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name) return element;
            if (FindChild<T>(child, name) is { } found) return found;
        }
        return null;
    }

    // ---- Drives ----------------------------------------------------------------------------------------------

    private void ShowDrives()
    {
        _updating = true;
        DriveChoice.Items.Clear();
        int selected = -1;
        for (int i = 0; i < _plans.Count; i++)
        {
            var plan = _plans[i];
            DriveChoice.Items.Add(DriveItem(plan, i == _best));
            if (plan.Drive.Root == _state.Drive) selected = i;
        }
        DriveChoice.SelectedIndex = selected;
        MapDriveTag.Content = Current is { } current ? DriveTag(current.Drive.Root) : null;
        _updating = false;
    }

    private RadioButton DriveItem(DrivePlan plan, bool best)
    {
        var drive = plan.Drive;
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(DriveTag(drive.Root));
        var label = new TextBlock { Text = drive.Label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        head.Children.Add(label);
        if (best)
        {
            var badge = new Border
            {
                Padding = new Thickness(8, 1, 8, 2),
                CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Resource<Brush>("AccentFillColorDefaultBrush"),
                Child = new TextBlock
                {
                    Text = "Best choice",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Resource<Brush>("TextOnAccentFillColorPrimaryBrush"),
                },
            };
            Grid.SetColumn(badge, 2);
            head.Children.Add(badge);
        }

        double used = drive.Total > 0 ? (drive.Total - drive.Free) * 100.0 / drive.Total : 0;
        var status = $"{Gb(drive.Free)} GB free of {Gb(drive.Total)} GB";
        var outcome = Outcome(plan);
        var text = new TextBlock
        {
            Text = outcome.Length > 0 ? $"{status} · {outcome}" : status,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        var content = new StackPanel { Width = 196, Spacing = 6, Margin = new Thickness(0, 0, 0, 4) };
        content.Children.Add(head);
        content.Children.Add(new ProgressBar { Value = used, Maximum = 100, IsHitTestVisible = false, IsTabStop = false });
        content.Children.Add(text);

        var item = new RadioButton { Content = content, Tag = drive.Root };
        AutomationProperties.SetName(item,
            $"Drive {drive.Root.TrimEnd('\\')} {drive.Label}, {status}" + (outcome.Length > 0 ? ", " + outcome : "") + (best ? ", best choice" : ""));
        return item;
    }

    private static string Outcome(DrivePlan plan)
    {
        if (plan.Missing <= -plan.Drive.Free) return ""; // no size typed yet
        if (plan.FitsAlready) return "fits already";
        if (!plan.Enough) return plan.Picked.Count == 0 ? "not enough space" : "not enough even after removing games";
        return plan.Picked.Count == 1 ? "1 game to remove" : $"{plan.Picked.Count} games to remove";
    }

    /// <summary>A drive letter as a small tag with the drive glyph, so "D:" reads as a drive.</summary>
    private static Border DriveTag(string root) => new()
    {
        Padding = new Thickness(6, 1, 7, 2),
        CornerRadius = new CornerRadius(4),
        VerticalAlignment = VerticalAlignment.Center,
        Background = Resource<Brush>("ControlFillColorSecondaryBrush"),
        BorderBrush = Resource<Brush>("ControlStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                new FontIcon { Glyph = "", FontSize = 12 },
                new TextBlock { Text = root.TrimEnd('\\'), FontWeight = FontWeights.SemiBold, FontSize = 13 },
            },
        },
    };

    private void OnDriveChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || DriveChoice.SelectedItem is not RadioButton { Tag: string root } || root == _state.Drive) return;
        _state.Drive = root;
        _state.DrivePicked = true;
        ResetChoice();
        MapDriveTag.Content = DriveTag(root);
        ShowMap();
        ShowSummary();
    }

    // ---- Treemap ---------------------------------------------------------------------------------------------

    private void OnMapSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 1) ShowMap();
    }

    private void ShowMap()
    {
        Map.Children.Clear();
        _iconBoxes.Clear();
        double width = MapHost.ActualWidth, height = MapHost.ActualHeight;
        if (Current is not { } plan || width <= 0) return;

        var chosen = Chosen(plan).Select(g => g.Game.Id).ToHashSet();
        var games = plan.Games.Where(g => g.SizeKnown).OrderByDescending(g => g.Size).ToList();
        MapEmptyText.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MapEmptyText.Text = plan.Games.Count == 0
            ? "No games from your launchers are installed on this drive."
            : "None of the games on this drive has a known size.";
        if (games.Count == 0) { ShowUnknown(plan); return; }

        // The free space is a tile too, so it's clear how much is there already.
        var values = games.Select(g => (double)g.Size).ToList();
        int freeIndex = values.FindIndex(v => v < plan.Drive.Free);
        if (freeIndex < 0) freeIndex = values.Count;
        values.Insert(freeIndex, Math.Max(0, plan.Drive.Free));
        var rects = SpacePlanner.Squarify(values, width, height);

        for (int i = 0, g = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (r.W <= 0 || r.H <= 0) { if (i != freeIndex) g++; continue; }
            var tile = i == freeIndex ? FreeTile(plan.Drive.Free, r) : GameTileFor(games[g++], chosen, r);
            Canvas.SetLeft(tile, r.X + Gap / 2);
            Canvas.SetTop(tile, r.Y + Gap / 2);
            Map.Children.Add(tile);
        }
        ShowUnknown(plan);
        RequestIcons(games);
    }

    private FrameworkElement FreeTile(long free, TileRect r)
    {
        double w = Math.Max(0, r.W - Gap), h = Math.Max(0, r.H - Gap);
        var grid = new Grid { Width = w, Height = h };
        AutomationProperties.SetName(grid, $"Free now, {Gb(free)} GB");
        grid.Children.Add(new Rectangle
        {
            RadiusX = 6,
            RadiusY = 6,
            Stroke = Resource<Brush>("TextFillColorTertiaryBrush"),
            StrokeThickness = 1,
            StrokeDashArray = [3, 3],
        });
        if (w >= 70 && h >= 40)
        {
            var text = new StackPanel { Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Bottom };
            text.Children.Add(new TextBlock { Text = "Free now", FontSize = 12, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = $"{Gb(free)} GB", FontSize = 11, Foreground = Resource<Brush>("TextFillColorSecondaryBrush") });
            grid.Children.Add(text);
        }
        return grid;
    }

    private ToggleButton GameTileFor(SpaceGame g, HashSet<string> chosen, TileRect r)
    {
        var game = g.Game;
        bool on = chosen.Contains(game.Id);
        double w = Math.Max(0, r.W - Gap), h = Math.Max(0, r.H - Gap);
        // Every tile with room for one gets the game's picture: big tiles with the text under it, short wide ones
        // with the text beside it, small ones centered. Only slivers under 20 px are just the color.
        double side = Math.Min(w, h);
        bool big = w >= 70 && h >= 84;
        bool beside = !big && w >= 110 && h >= 34;
        double iconSize = big ? (w > 150 && h > 110 ? 40 : 30) : beside ? Math.Min(28, h - 12) : Math.Min(32, side - 8);
        bool showIcon = iconSize >= 14;
        var tint = TileBrush(game);
        var white = new SolidColorBrush(Colors.White);

        var content = new Grid();
        if (showIcon)
        {
            var box = IconBox(game, iconSize);
            box.HorizontalAlignment = big || beside ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            box.VerticalAlignment = big ? VerticalAlignment.Top : VerticalAlignment.Center;
            _iconBoxes[game.Id] = box;
            content.Children.Add(box);
        }
        if (big || beside)
        {
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (g.Favorite) title.Children.Add(new FontIcon { Glyph = "", FontSize = 10, Foreground = Resource<Brush>("FavoriteBrush") });
            else if (g.Recent) title.Children.Add(new FontIcon { Glyph = "", FontSize = 10 });
            title.Children.Add(new TextBlock
            {
                Text = game.Name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = Math.Max(0, big ? w - 34 : w - iconSize - 46),
            });
            var text = new StackPanel
            {
                VerticalAlignment = big ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                Margin = new Thickness(big || !showIcon ? 0 : iconSize + 8, 0, on && !big ? 18 : 0, 0),
                Spacing = 1,
            };
            text.Children.Add(title);
            if (big || h >= 50)
            {
                text.Children.Add(new TextBlock
                {
                    Text = $"{Gb(g.Size)} GB · {Played(g)}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 255, 255, 255)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            content.Children.Add(text);
        }
        if (on && side >= 30)
        {
            double mark = big ? 20 : 16;
            content.Children.Add(new Border
            {
                Width = mark,
                Height = mark,
                CornerRadius = new CornerRadius(mark / 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Resource<Brush>("AccentFillColorDefaultBrush"),
                Child = new FontIcon { Glyph = "", FontSize = 11, Foreground = Resource<Brush>("TextOnAccentFillColorPrimaryBrush") },
            });
        }

        var label = $"{game.Name}, {Gb(g.Size)} GB, {Why(g).ToLowerInvariant()}" +
                    (g.Favorite ? ", favorite" : "") + (g.Recent ? ", played in the last 14 days" : "");
        var tile = new ToggleButton
        {
            Width = w,
            Height = h,
            Padding = new Thickness(big || beside ? 8 : 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Opacity = on || chosen.Count == 0 ? 1 : 0.55, // "kept" only means something next to suggested ones
            Content = content,
        };
        // Its own colors in every state: the game's tint, an accent outline when it's suggested.
        var edge = on ? Resource<Brush>("AccentFillColorDefaultBrush") : new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Checked", "CheckedPointerOver", "CheckedPressed" })
        {
            tile.Resources["ToggleButtonBackground" + state] = tint;
            tile.Resources["ToggleButtonBorderBrush" + state] = edge;
            tile.Resources["ToggleButtonForeground" + state] = white;
        }
        tile.IsChecked = on;
        AutomationProperties.SetName(tile, label);
        ToolTipService.SetToolTip(tile, label);
        tile.Click += (_, _) =>
        {
            _state.Overrides[game.Id] = !on;
            _state.Queue.Clear();
            _state.Step = -1;
            int focus = Map.Children.IndexOf(tile);
            ShowMap();
            ShowSummary();
            if (focus >= 0 && focus < Map.Children.Count && Map.Children[focus] is Control again) again.Focus(FocusState.Programmatic);
        };
        return tile;
    }

    private static Brush TileBrush(Game game)
    {
        var hex = game.CoverTint ?? CoverColors.MonogramTone(game.Name);
        if (hex is ['#', ..] && hex.Length == 7 && uint.TryParse(hex[1..], System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        return new SolidColorBrush(Color.FromArgb(255, 0x44, 0x48, 0x4F));
    }

    /// <summary>A tile's picture: the game's icon when it's loaded, else its initials until it is.</summary>
    private Border IconBox(Game game, double size)
    {
        var box = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size > 30 ? 8 : size > 20 ? 6 : 4),
            Background = new SolidColorBrush(Color.FromArgb(0x29, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)),
            BorderThickness = new Thickness(1),
        };
        SetIcon(box, game, _icons.GetValueOrDefault(game.Id));
        return box;
    }

    private static void SetIcon(Border box, Game game, IconPixels? icon)
    {
        double size = box.Width;
        box.Child = icon is not null
            ? new Image { Source = CoverCache.IconImage(game, icon), Margin = new Thickness(size > 30 ? 6 : size > 20 ? 4 : 2) }
            : new TextBlock
            {
                Text = CoverColors.Initials(game.Name) is { Length: > 1 } initials && size < 24 ? initials[..1] : CoverColors.Initials(game.Name),
                FontSize = size > 30 ? 15 : size >= 24 ? 11 : 9,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
    }

    /// <summary>
    /// Loads the icons of the drive's games (the generated covers' icons, cached on disk after the first time),
    /// a few at a time, and puts each into its tile as it arrives. Kept for the page's life, so the map's
    /// rebuilds (typing, clicking tiles, other drives) show them at once.
    /// </summary>
    private async void RequestIcons(List<SpaceGame> games)
    {
        var missing = games.Where(g => !_icons.ContainsKey(g.Game.Id) && _iconLoading.Add(g.Game.Id)).Select(g => g.Game).ToList();
        if (missing.Count == 0) return;
        var view = _state;
        bool tintChanged = false;
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(missing.Select(async game =>
        {
            await gate.WaitAsync();
            try
            {
                var hadTint = game.CoverTint;
                var art = await CoverCache.GetFallbackAsync(game); // continues on the UI thread
                if (view != _state) return; // the page closed or reopened meanwhile
                if (hadTint != game.CoverTint) tintChanged = true;
                _icons[game.Id] = art.Icon;
                if (_iconBoxes.TryGetValue(game.Id, out var box)) SetIcon(box, game, art.Icon);
            }
            finally
            {
                gate.Release();
                _iconLoading.Remove(game.Id);
            }
        }));
        if (view != _state || _plans.Count == 0) return;
        if (tintChanged) ShowMap(); // first time: tiles take their icons' colors
        ShowSummary(); // the picked list's rows, built before the icons arrived
    }

    private void ShowUnknown(DrivePlan plan)
    {
        var unknown = plan.Games.Where(g => !g.SizeKnown).Select(g => g.Game.Name).ToList();
        UnknownText.Visibility = unknown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnknownText.Text = unknown.Count == 0 ? "" :
            $"Not counted, their size isn't known: {string.Join(", ", unknown.Take(6))}" + (unknown.Count > 6 ? $" and {unknown.Count - 6} more" : "");
    }

    // ---- Summary and uninstall -------------------------------------------------------------------------------

    private void ShowSummary()
    {
        PickedList.Children.Clear();
        if (Current is not { } plan || _state.Need <= 0)
        {
            SummaryCard.Visibility = Visibility.Collapsed;
            return;
        }
        SummaryCard.Visibility = Visibility.Visible;

        var chosen = plan.FitsAlready ? [] : Chosen(plan);
        long freed = chosen.Sum(g => g.Size);
        if (plan.FitsAlready)
        {
            HeadlineText.Text = "It fits already";
            StatusText.Text = $"{Gb(plan.Drive.Free - _state.Need)} GB free afterwards";
            StatusText.Foreground = Resource<Brush>("SystemFillColorSuccessBrush");
        }
        else if (chosen.Count == 0 && plan.Picked.Count == 0)
        {
            HeadlineText.Text = $"{Gb(plan.Missing)} GB missing";
            StatusText.Text = "No games here to remove";
            StatusText.Foreground = Resource<Brush>("SystemFillColorCautionBrush");
        }
        else
        {
            HeadlineText.Text = $"Frees {Gb(freed)} GB of the {Gb(plan.Missing)} GB missing";
            bool enough = freed >= plan.Missing;
            StatusText.Text = enough ? $"{Gb(plan.Drive.Free + freed - _state.Need)} GB free afterwards" : $"{Gb(plan.Missing - freed)} GB still missing";
            StatusText.Foreground = Resource<Brush>(enough ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush");
        }

        foreach (var g in chosen) PickedList.Children.Add(PickedRow(g));

        if (_state.Uninstalling)
        {
            int n = _state.Queue.Count;
            var next = FindChosen(chosen, _state.Queue[_state.Step]);
            StepBar.Visibility = Visibility.Visible;
            StepBar.Title = $"{_state.Step + 1} of {n}: {next?.Game.Name}";
            StepBar.Message = next is null ? "" : GameActions.Uninstall(next.Game).Hint;
            UninstallButton.Content = _state.Step + 1 < n ? "Next game" : "Done";
            UninstallButton.IsEnabled = true;
        }
        else
        {
            StepBar.Visibility = Visibility.Collapsed;
            UninstallButton.Content = chosen.Count switch
            {
                0 => "Nothing to uninstall",
                1 => "Uninstall 1 game…",
                _ => $"Uninstall {chosen.Count} games…",
            };
            UninstallButton.IsEnabled = chosen.Count > 0;
        }
    }

    private static SpaceGame? FindChosen(List<SpaceGame> chosen, string id) => chosen.FirstOrDefault(g => g.Game.Id == id);

    private Grid PickedRow(SpaceGame g)
    {
        var row = new Grid
        {
            ColumnSpacing = 10,
            MinHeight = 40,
            BorderBrush = Resource<Brush>("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

        // The game's icon on its tile color, like on the map.
        var picture = IconBox(g.Game, 28);
        picture.Background = TileBrush(g.Game);
        picture.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(picture);
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (g.Favorite) name.Children.Add(new FontIcon { Glyph = "", FontSize = 12, Foreground = Resource<Brush>("FavoriteBrush") });
        else if (g.Recent) name.Children.Add(new FontIcon { Glyph = "", FontSize = 12 });
        name.Children.Add(new TextBlock { Text = g.Game.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var why = new TextBlock
        {
            Text = Why(g),
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(why, 2);
        row.Children.Add(why);
        var size = new TextBlock { Text = $"{Gb(g.Size)} GB", TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(size, 3);
        row.Children.Add(size);
        AutomationProperties.SetName(row, $"{g.Game.Name}, {Gb(g.Size)} GB, {Why(g)}");
        return row;
    }

    /// <summary>
    /// Why a game was picked: "3 h played · Yesterday", "Never played", or "Last played 8 months ago" when only the
    /// store knows it was played (Steam's LastPlayed, with no playtime of ours).
    /// </summary>
    private static string Why(SpaceGame g)
    {
        if (g.Playtime > TimeSpan.Zero)
            return Format.Playtime(g.Playtime) + " played" + (g.LastPlayed is { } when ? " · " + Ago(when) : "");
        return g.LastPlayed is { } last ? "Last played " + Ago(last).ToLowerInvariant() : "Never played";
    }

    /// <summary>The short form on a tile: "3 h", "never played", "a year ago".</summary>
    private static string Played(SpaceGame g) =>
        g.Playtime > TimeSpan.Zero ? Format.Playtime(g.Playtime) : g.LastPlayed is { } last ? Ago(last).ToLowerInvariant() : "never played";

    private static string Ago(DateTimeOffset when)
    {
        int days = (DateTime.Today - when.LocalDateTime.Date).Days;
        if (days < 7) return Format.RelativeDay(when);
        if (days < 30) return days < 14 ? "Last week" : $"{days / 7} weeks ago";
        int months = days / 30;
        if (months < 12) return months == 1 ? "A month ago" : $"{months} months ago";
        int years = days / 365;
        return years <= 1 ? "A year ago" : $"{years} years ago";
    }

    private void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (Current is not { } plan) return;
        var chosen = Chosen(plan);
        if (!_state.Uninstalling)
        {
            if (chosen.Count == 0) return;
            _state.Queue.Clear();
            _state.Queue.AddRange(chosen.Select(g => g.Game.Id));
            _state.Step = 0;
        }
        else if (++_state.Step >= _state.Queue.Count)
        {
            // All handed over: see what the launchers removed.
            _state.Queue.Clear();
            _state.Step = -1;
            CheckAgainRequested?.Invoke();
            return;
        }
        ShowSummary();
        if (FindChosen(chosen, _state.Queue[_state.Step]) is { } next) GameActions.ShellOpen(GameActions.Uninstall(next.Game).Target);
    }

    private void OnCheckAgainClick(object sender, RoutedEventArgs e) => CheckAgainRequested?.Invoke();

    private void OnRulesClick(object sender, RoutedEventArgs e) => RulesTip.IsOpen = !RulesTip.IsOpen;

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private static string Gb(long bytes)
    {
        double gb = bytes / (double)SpacePlanner.GB;
        return gb >= 10 || gb == 0 ? $"{Math.Round(gb):0}" : $"{gb:0.#}";
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
