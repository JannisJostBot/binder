using System.ComponentModel;
using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.ViewModels;
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
/// Free up space (spec §12, board 17). The plan and what each control does live in <see cref="FreeSpaceViewModel"/>
/// (bound with x:Bind); this file draws the drive choices, the treemap of the chosen drive with the games' icons, and
/// the list of games to remove. Built when shown, static while shown (no timers); <see cref="Release"/> drops it all.
/// </summary>
public sealed partial class FreeSpaceView : UserControl
{
    private const double Gap = 2;

    public FreeSpaceViewModel? ViewModel { get; private set; }

    private bool _updating;
    private readonly Dictionary<string, Border> _iconBoxes = [];
    // Icons loaded while the page is open (null: the game has none), and those on their way.
    private readonly Dictionary<string, IconPixels?> _icons = [];
    private readonly HashSet<string> _iconLoading = [];

    public FreeSpaceView()
    {
        InitializeComponent();
        AutomationProperties.SetName(this, Strings.FreeUpSpace);
    }

    /// <summary>Connects the page to its owner once, before it's first shown.</summary>
    public void Initialize(IFreeSpaceActions actions)
    {
        if (ViewModel is not null) throw new InvalidOperationException("The Free up space page is already initialized.");
        ViewModel = new FreeSpaceViewModel(actions);
        ViewModel.PropertyChanged += OnViewModelChanged;
        Bindings.Update();
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
        ViewModel?.Release();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = ViewModel!;
        switch (e.PropertyName)
        {
            case null or "":
                _updating = true;
                NeedBox.Text = vm.NeedText;
                _updating = false;
                ShowDrives();
                ShowMap();
                ShowSummary();
                break;
            case nameof(FreeSpaceViewModel.Drives):
                ShowDrives();
                break;
            case nameof(FreeSpaceViewModel.SelectedDrive):
                MapDriveTag.Content = vm.SelectedDrive is { } root ? DriveTag(root) : null;
                break;
            case nameof(FreeSpaceViewModel.CurrentPlan):
                ShowMap();
                break;
            case nameof(FreeSpaceViewModel.Picked):
                ShowSummary();
                break;
        }
    }

    private void OnNeedTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args) =>
        args.Cancel = args.NewText.Any(c => !char.IsAsciiDigit(c));

    private void OnNeedChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updating) ViewModel?.SetNeed(NeedBox.Text);
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
        var vm = ViewModel!;
        _updating = true;
        // Reset the index first: setting the same index again on new items wouldn't check any of them.
        DriveChoice.SelectedIndex = -1;
        DriveChoice.Items.Clear();
        int selected = -1;
        foreach (var drive in vm.Drives)
        {
            var item = DriveItem(drive);
            if (drive.Root == vm.SelectedDrive)
            {
                selected = DriveChoice.Items.Count;
                item.IsChecked = true;
            }
            DriveChoice.Items.Add(item);
        }
        DriveChoice.SelectedIndex = selected;
        MapDriveTag.Content = vm.CurrentPlan is { } current ? DriveTag(current.Drive.Root) : null;
        _updating = false;
    }

    private static RadioButton DriveItem(DriveRow drive)
    {
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(DriveTag(drive.Root));
        var label = new TextBlock { Text = drive.Label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        head.Children.Add(label);
        if (drive.Best)
        {
            var badge = new Border
            {
                Padding = new Thickness(8, 1, 8, 2),
                CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Resource<Brush>("AccentFillColorDefaultBrush"),
                Child = new TextBlock
                {
                    Text = Strings.BestChoiceTag,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Resource<Brush>("TextOnAccentFillColorPrimaryBrush"),
                },
            };
            Grid.SetColumn(badge, 2);
            head.Children.Add(badge);
        }

        var text = new TextBlock
        {
            Text = drive.Text,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        var content = new StackPanel { Width = 196, Spacing = 6, Margin = new Thickness(0, 0, 0, 4) };
        content.Children.Add(head);
        content.Children.Add(new ProgressBar { Value = drive.UsedPercent, Maximum = 100, IsHitTestVisible = false, IsTabStop = false });
        content.Children.Add(text);

        var item = new RadioButton { Content = content, Tag = drive.Root };
        AutomationProperties.SetName(item, drive.Name);
        return item;
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
        if (!_updating && DriveChoice.SelectedItem is RadioButton { Tag: string root }) ViewModel?.PickDrive(root);
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
        if (ViewModel?.CurrentPlan is not { } plan || width <= 0) return;

        var chosen = ViewModel.Chosen(plan).Select(g => g.Game.Id).ToHashSet();
        var games = plan.Games.Where(g => g.SizeKnown).OrderByDescending(g => g.Size).ToList();
        MapEmptyText.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (games.Count == 0) return;

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
        RequestIcons(games);
    }

    private static FrameworkElement FreeTile(long free, TileRect r)
    {
        double w = Math.Max(0, r.W - Gap), h = Math.Max(0, r.H - Gap);
        var grid = new Grid { Width = w, Height = h };
        AutomationProperties.SetName(grid, Strings.Format(Strings.FreeNowName, FreeSpaceViewModel.Gb(free)));
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
            text.Children.Add(new TextBlock { Text = Strings.FreeNow, FontSize = 12, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = Strings.Format(Strings.SizeGb, FreeSpaceViewModel.Gb(free)), FontSize = 11, Foreground = Resource<Brush>("TextFillColorSecondaryBrush") });
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
                    Text = Strings.Format(Strings.Dot, Strings.Format(Strings.SizeGb, FreeSpaceViewModel.Gb(g.Size)), FreeSpaceViewModel.Played(g)),
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

        var label = FreeSpaceViewModel.TileName(g);
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
            int focus = Map.Children.IndexOf(tile);
            ViewModel!.ToggleGame(game.Id, on); // redraws the map and the summary
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
        var view = ViewModel!.State;
        bool tintChanged = false;
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(missing.Select(async game =>
        {
            await gate.WaitAsync();
            try
            {
                var hadTint = game.CoverTint;
                var art = await CoverCache.GetFallbackAsync(game); // continues on the UI thread
                if (view != ViewModel.State) return; // the page closed or reopened meanwhile
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
        if (view != ViewModel.State || ViewModel.CurrentPlan is null) return;
        if (tintChanged) ShowMap(); // first time: tiles take their icons' colors
        ShowSummary(); // the picked list's rows, built before the icons arrived
    }

    // ---- Summary ---------------------------------------------------------------------------------------------

    private void ShowSummary()
    {
        var vm = ViewModel!;
        PickedList.Children.Clear();
        StatusText.Foreground = Resource<Brush>(vm.StatusOk ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush");
        foreach (var g in vm.Picked) PickedList.Children.Add(PickedRow(g));
    }

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
            Text = FreeSpaceViewModel.Why(g),
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(why, 2);
        row.Children.Add(why);
        var size = new TextBlock { Text = Strings.Format(Strings.SizeGb, FreeSpaceViewModel.Gb(g.Size)), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(size, 3);
        row.Children.Add(size);
        AutomationProperties.SetName(row, Strings.Format(Strings.SpaceTileName, g.Game.Name, FreeSpaceViewModel.Gb(g.Size), FreeSpaceViewModel.Why(g)));
        return row;
    }

    /// <summary>Closes the (i) tip, so it doesn't stay on screen when the page changes or the panel hides.</summary>
    public void CloseTips() => RulesTip.IsOpen = false;

    private void OnRulesClick(object sender, RoutedEventArgs e) => RulesTip.IsOpen = !RulesTip.IsOpen;

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
