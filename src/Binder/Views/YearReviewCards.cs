using System.Globalization;
using System.Numerics;
using Binder.Library;
using Binder.Models;
using Binder.Services;
using Binder.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Binder.Views;

/// <summary>A card's colors: worked out from the year's theme (YearColors), or the system's in high contrast.</summary>
internal sealed record CardPalette(Brush Background, Brush Text, Brush Secondary, Brush Shape, Brush Muted, Brush Accent,
    Color AccentColor, bool HighContrast);

/// <summary>
/// Builds a Year in review card (spec §28, boards 41–43) from its <see cref="CardSpec"/>: each block kind is drawn
/// here, once, and registers how it comes in (<see cref="CardMotion"/>). Built when the card is shown, dropped when
/// it leaves. Text and names come from the spec; decorative parts are hidden from UI Automation, since the card's
/// sentence (its name, and Narrator's announcement) says what they show.
/// </summary>
internal sealed class YearReviewCards
{
    public const double Pad = 24;
    private const string DisplayFont = "Segoe UI Variable Display";

    private readonly CardSpec _card;
    private readonly YearReviewViewModel _vm;
    private readonly YearTheme _theme;
    private readonly CardPalette _p;
    private readonly CardMotion _motion = new();
    private readonly double _width;

    private YearReviewCards(CardSpec card, YearReviewViewModel vm, CardPalette palette, double width)
    {
        _card = card;
        _vm = vm;
        _theme = vm.Theme;
        _p = palette;
        _width = Math.Max(280, width - 2 * Pad);
    }

    /// <summary>The card's element and what it plays when it comes in.</summary>
    public static (Grid Card, CardMotion Motion) Build(CardSpec card, YearReviewViewModel vm, CardPalette palette, double width)
    {
        var builder = new YearReviewCards(card, vm, palette, width);
        return (builder.Build(), builder._motion);
    }

    private Grid Build()
    {
        var root = new Grid { Background = _p.Background, CornerRadius = new CornerRadius(8) };
        AutomationProperties.SetName(root, _card.Narration);
        if (!_p.HighContrast) root.Children.Add(Shapes());

        var behind = new Grid { Margin = new Thickness(Pad), VerticalAlignment = VerticalAlignment.Bottom };
        root.Children.Add(behind);

        var content = new Grid { Padding = new Thickness(Pad) };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var flow = new StackPanel { Spacing = 6 };
        var bottom = new StackPanel { Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        if (_card.Centered)
        {
            flow.HorizontalAlignment = HorizontalAlignment.Center;
            flow.VerticalAlignment = VerticalAlignment.Center;
        }
        Grid.SetRow(bottom, 1);
        content.Children.Add(flow);
        content.Children.Add(bottom);

        // A card taller than the panel (a small screen, large text) scrolls instead of cutting its bottom off.
        var scroller = new ScrollViewer
        {
            Content = content,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        scroller.SizeChanged += (_, e) => content.MinHeight = e.NewSize.Height;
        root.Children.Add(scroller);

        foreach (var block in _card.Blocks)
        {
            var target = block.Place switch
            {
                BlockPlace.Behind => (Panel)behind,
                BlockPlace.Bottom => bottom,
                _ => flow,
            };
            if (Block(block) is { } element) target.Children.Add(element);
        }
        if (_card.Kind == CardKind.Summary) bottom.Children.Add(SummaryActions());
        return root;
    }

    private FrameworkElement? Block(BlockSpec b) => b.Kind switch
    {
        BlockKind.Caption => Animated(Caption(b.Text ?? ""), b),
        BlockKind.Headline when b.List.Length > 0 => Digits(b),
        BlockKind.Headline => Animated(Text(b.Text ?? "", 26, FontWeights.SemiBold, _p.Text), b),
        BlockKind.Number => Number(b),
        BlockKind.Line => Animated(Text(b.Text ?? "", 15, FontWeights.Normal, _p.Text), b),
        BlockKind.Note => Animated(Text(b.Text ?? "", 12, FontWeights.Normal, _p.Secondary), b),
        BlockKind.Bars => b.Style == BlockStyle.Rows ? BarRows(b) : Bars(b),
        BlockKind.Ranks => b.Style == BlockStyle.Compact ? CompactRanks(b) : Ranks(b),
        BlockKind.Heat => Heat(b),
        BlockKind.Mosaic => Mosaic(b),
        BlockKind.Covers => Covers(b),
        BlockKind.Tiles => b.Style switch
        {
            BlockStyle.Record => RecordTile(b),
            BlockStyle.List => ListTile(b),
            _ => Tiles(b),
        },
        BlockKind.Share => Share(b),
        BlockKind.Timeline => Timeline(b),
        BlockKind.Hero => Hero(b),
        BlockKind.Dots => Dots(b),
        BlockKind.Play => Play(b),
        _ => null,
    };

    private T Animated<T>(T element, BlockSpec b) where T : UIElement
    {
        _motion.Add(element, YearMotion.CueFor(b.Kind, _theme), b.At);
        return element;
    }

    // ---- Text ------------------------------------------------------------------------------------------------

    private TextBlock Text(string text, double size, Windows.UI.Text.FontWeight weight, Brush brush) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = _card.Centered ? TextAlignment.Center : TextAlignment.Left,
        HorizontalAlignment = _card.Centered ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
    };

    private TextBlock Caption(string text)
    {
        var caption = Text(text, 13, FontWeights.SemiBold, _p.Secondary);
        caption.CharacterSpacing = 40;
        return caption;
    }

    private TextBlock Display(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = new FontFamily(DisplayFont),
        FontWeight = Weight(_theme.DisplayWeight),
        Foreground = _p.Text,
        LineHeight = size,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    private static Windows.UI.Text.FontWeight Weight(int weight) => weight switch
    {
        <= 300 => FontWeights.Light,
        <= 450 => FontWeights.Normal,
        <= 650 => FontWeights.SemiBold,
        _ => FontWeights.Bold,
    };

    /// <summary>The intro's year, its digits rising one after another.</summary>
    private FrameworkElement Digits(BlockSpec b)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(row, b.Text ?? "");
        for (int i = 0; i < b.List.Length; i++)
        {
            var digit = Display(b.List[i].Label, 104);
            AutomationProperties.SetAccessibilityView(digit, AccessibilityView.Raw);
            row.Children.Add(digit);
            _motion.Add(digit, YearMotion.CueFor(BlockKind.Number, _theme), YearMotion.ItemAt(b, _theme, i));
        }
        return row;
    }

    /// <summary>A number that counts up, its unit beside it; Narrator reads the final value.</summary>
    private FrameworkElement Number(BlockSpec b)
    {
        double size = _card.Kind switch
        {
            CardKind.Time => _card.Centered ? 132 : 112,
            CardKind.Top => 68,
            _ => 44,
        } * _theme.NumberScale;
        var main = Display("0", size);
        var above = Display("0", size);
        var below = Display("0", size);
        foreach (var ghost in new[] { above, below })
        {
            ghost.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAccessibilityView(ghost, AccessibilityView.Raw);
        }
        AutomationProperties.SetName(main, b.Detail ?? b.Value.ToString("N0", CultureInfo.CurrentCulture));
        var number = new Grid();
        number.Children.Add(above);
        number.Children.Add(below);
        number.Children.Add(main);
        var unit = Display(b.Text ?? "", Math.Max(18, size * 0.26));
        unit.Margin = new Thickness(10, 0, 0, size * 0.1);
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        AutomationProperties.SetAccessibilityView(unit, AccessibilityView.Raw);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = _card.Centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };
        row.Children.Add(number);
        row.Children.Add(unit);
        var counter = new Counter(main, above, below, b.Value, b.At, size);
        counter.Show(1);
        _motion.Counters.Add(counter);
        return Animated(row, b);
    }

    // ---- Charts ------------------------------------------------------------------------------------------------

    /// <summary>Month bars: the busiest in the accent; below the number, or behind everything (Grid theme).</summary>
    private FrameworkElement Bars(BlockSpec b)
    {
        bool behind = b.Place == BlockPlace.Behind;
        double plot = behind ? 300 : 132;
        double max = Math.Max(0.01, b.List.Max(i => i.Value));
        var panel = new StackPanel { Spacing = 8 };
        if (!behind) panel.Children.Add(Animated(Text(b.Text ?? "", 13, FontWeights.Normal, _p.Secondary), new BlockSpec(BlockKind.Note, b.At + 400)));
        var grid = new Grid { ColumnSpacing = behind ? 6 : 8, RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(plot) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AutomationProperties.SetAccessibilityView(grid, AccessibilityView.Raw);
        for (int i = 0; i < b.List.Length; i++)
        {
            var item = b.List[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bar = new Rectangle
            {
                Height = Math.Max(item.Value > 0 ? 4 : 2, item.Value / max * plot),
                VerticalAlignment = VerticalAlignment.Bottom,
                RadiusX = 3,
                RadiusY = 3,
                Fill = item.Highlight ? (behind ? _p.Muted : _p.Accent) : (behind ? _p.Shape : _p.Muted),
            };
            ToolTipService.SetToolTip(bar, item.Detail);
            Grid.SetColumn(bar, i);
            grid.Children.Add(bar);
            _motion.Add(bar, Cue.Grow, YearMotion.ItemAt(b, _theme, i));
            if (behind) continue;
            var label = Text(Initial(item.Label), 11, FontWeights.Normal, _p.Secondary);
            label.TextAlignment = TextAlignment.Center;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(label, i);
            Grid.SetRow(label, 1);
            grid.Children.Add(label);
        }
        panel.Children.Add(grid);
        if (behind) panel.Children.Insert(0, Animated(Text(b.Text ?? "", 13, FontWeights.Normal, _p.Secondary), new BlockSpec(BlockKind.Note, b.At + 400)));
        return panel;
    }

    private static string Initial(string month) =>
        month.Length == 0 ? "" : char.ToUpper(month[0], CultureInfo.CurrentCulture).ToString();

    /// <summary>The top three months as rows (Cover tint theme).</summary>
    private FrameworkElement BarRows(BlockSpec b)
    {
        var panel = new StackPanel { Spacing = 8 };
        double max = Math.Max(0.01, b.List.Max(i => i.Value));
        for (int i = 0; i < b.List.Length; i++)
        {
            var item = b.List[i];
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Text(item.Label, 12, FontWeights.Normal, _p.Secondary));
            var bar = Bar(item.Value / max, item.Highlight ? _p.Accent : _p.Muted, 8, b.At + i * 45);
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            var value = Text(item.Detail ?? "", 12, FontWeights.Normal, _p.Secondary);
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            AutomationProperties.SetName(row, item.Detail ?? "");
            panel.Children.Add(row);
        }
        panel.Children.Add(Animated(Text(b.Text ?? "", 13, FontWeights.Normal, _p.Secondary), new BlockSpec(BlockKind.Note, b.At + 400)));
        return panel;
    }

    /// <summary>A horizontal bar of <paramref name="share"/> (0–1) of the width, growing from the left.</summary>
    private Grid Bar(double share, Brush fill, double height, int at)
    {
        var track = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(height / 2), Background = _p.Shape };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, share), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - share), GridUnitType.Star) });
        var fillBar = new Rectangle { Fill = fill, RadiusX = height / 2, RadiusY = height / 2 };
        track.Children.Add(fillBar);
        AutomationProperties.SetAccessibilityView(track, AccessibilityView.Raw);
        _motion.Add(fillBar, Cue.Grow, at, horizontal: true);
        return track;
    }

    /// <summary>Ranked games: rank, cover, name and time, and a bar against the first.</summary>
    private FrameworkElement Ranks(BlockSpec b)
    {
        var panel = new StackPanel { Spacing = 14 };
        for (int i = 0; i < b.List.Length; i++)
        {
            var item = b.List[i];
            int at = YearMotion.ItemAt(b, _theme, i);
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rank = Display((i + 1).ToString(CultureInfo.CurrentCulture), 28);
            rank.Foreground = item.Highlight ? _p.Accent : _p.Secondary;
            rank.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(rank);
            var cover = Cover(item.GameId, 52, 70);
            Grid.SetColumn(cover, 1);
            row.Children.Add(cover);
            var info = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            var line = new Grid { ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Text(item.Label, 15, FontWeights.SemiBold, _p.Text);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Children.Add(name);
            var hours = Text(item.Detail ?? "", 13, FontWeights.Normal, _p.Secondary);
            Grid.SetColumn(hours, 1);
            line.Children.Add(hours);
            info.Children.Add(line);
            info.Children.Add(Bar(item.Value, item.Highlight ? _p.Accent : _p.Muted, 6, at));
            Grid.SetColumn(info, 2);
            row.Children.Add(info);
            AutomationProperties.SetName(row, Strings.Format(Strings.Dot, item.Label, item.Detail));
            _motion.Add(row, YearMotion.RowCue(_theme), at);
            panel.Children.Add(row);
        }
        return panel;
    }

    /// <summary>The summary's top five: plain rows of text.</summary>
    private FrameworkElement CompactRanks(BlockSpec b)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Caption(b.Text ?? ""));
        for (int i = 0; i < b.List.Length; i++)
        {
            var item = b.List[i];
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Text((i + 1).ToString(CultureInfo.CurrentCulture), 13, FontWeights.Normal, _p.Secondary));
            var name = Text(item.Label, 13, FontWeights.Normal, _p.Text);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var hours = Text(item.Detail ?? "", 13, FontWeights.Normal, _p.Secondary);
            Grid.SetColumn(hours, 2);
            row.Children.Add(hours);
            _motion.Add(row, YearMotion.RowCue(_theme), YearMotion.ItemAt(b, _theme, i));
            panel.Children.Add(row);
        }
        return panel;
    }

    /// <summary>The 7 × 24 hour grid, Monday first, filling in a wave.</summary>
    private FrameworkElement Heat(BlockSpec b)
    {
        const double cell = 14, gap = 3;
        var grid = new Grid { ColumnSpacing = gap, RowSpacing = gap, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAccessibilityView(grid, AccessibilityView.Raw);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        for (int h = 0; h < 24; h++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
        for (int d = 0; d < 8; d++) grid.RowDefinitions.Add(new RowDefinition { Height = d < 7 ? new GridLength(cell) : GridLength.Auto });
        var days = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        for (int d = 0; d < 7; d++)
        {
            var label = Text(days[(d + 1) % 7], 11, FontWeights.Normal, _p.Secondary);
            label.TextWrapping = TextWrapping.NoWrap;
            label.LineHeight = cell;
            label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            Grid.SetRow(label, d);
            grid.Children.Add(label);
            for (int h = 0; h < 24; h++)
            {
                double level = b.List.Length == 7 * 24 ? b.List[d * 24 + h].Value : 0;
                var square = new Rectangle { RadiusX = 3, RadiusY = 3, Fill = Level(level) };
                Grid.SetRow(square, d);
                Grid.SetColumn(square, h + 1);
                grid.Children.Add(square);
                _motion.Add(square, Cue.Pop, b.At + YearMotion.HeatDelay(d, h));
            }
        }
        foreach (int h in new[] { 0, 6, 12, 18 })
        {
            var hour = Text(h.ToString(CultureInfo.CurrentCulture), 11, FontWeights.Normal, _p.Secondary);
            hour.TextWrapping = TextWrapping.NoWrap;
            Grid.SetRow(hour, 7);
            Grid.SetColumn(hour, h + 1);
            Grid.SetColumnSpan(hour, 4);
            grid.Children.Add(hour);
        }
        return grid;
    }

    private Brush Level(double level)
    {
        if (level <= 0) return _p.Shape;
        if (_p.HighContrast) return level >= 3 ? _p.Accent : _p.Muted;
        byte alpha = level switch { >= 4 => 255, >= 3 => 190, >= 2 => 128, _ => 72 };
        return new SolidColorBrush(Color.FromArgb(alpha, _p.AccentColor.R, _p.AccentColor.G, _p.AccentColor.B));
    }

    // ---- Covers ------------------------------------------------------------------------------------------------

    /// <summary>A game's cover (store art, a custom cover or the generated one); a removed game's initial on a plain tile.</summary>
    private FrameworkElement Cover(string? gameId, double width, double height)
    {
        FrameworkElement cover;
        if (_vm.GameFor(gameId) is { } game)
        {
            cover = new CoverView { Width = width, Height = height, DecodeWidth = width, CornerRadius = new CornerRadius(6), Game = game };
        }
        else
        {
            var name = gameId is null ? "" : _vm.NameOf(gameId);
            cover = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(6),
                Background = _p.Muted,
                Child = new TextBlock
                {
                    Text = name.Length > 0 ? name[..1] : "",
                    FontSize = width * 0.3,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _p.Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }
        AutomationProperties.SetAccessibilityView(cover, AccessibilityView.Raw);
        return cover;
    }

    /// <summary>The year's covers: stacked like the logo, then fanned out into a mosaic (or dealt in).</summary>
    private FrameworkElement Mosaic(BlockSpec b)
    {
        const double w = 84, h = 112, gap = 12;
        int n = b.List.Length, columns = Math.Clamp(n, 1, 4), rows = (n + columns - 1) / columns;
        double width = columns * w + (columns - 1) * gap, height = rows * h + (rows - 1) * gap;
        var canvas = new Canvas { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 28) };
        AutomationProperties.SetAccessibilityView(canvas, AccessibilityView.Raw);
        var cue = YearMotion.CueFor(BlockKind.Mosaic, _theme);
        for (int i = 0; i < n; i++)
        {
            double x = i % columns * (w + gap), y = i / columns * (h + gap);
            var cover = Cover(b.List[i].GameId, w, h);
            Canvas.SetLeft(cover, x);
            Canvas.SetTop(cover, y);
            canvas.Children.Add(cover);
            var from = new Vector2((float)((width - w) / 2 - x), (float)((height - h) / 2 - y));
            float degrees = (i % 2 == 0 ? -1 : 1) * (2 + i % 4 * 2);
            _motion.Add(cover, cue, YearMotion.ItemAt(b, _theme, i), from, degrees);
        }
        return canvas;
    }

    /// <summary>New games' covers, dealt in.</summary>
    private FrameworkElement Covers(BlockSpec b)
    {
        const double w = 80, h = 106, gap = 10;
        int columns = Math.Max(1, (int)((_width + gap) / (w + gap)));
        var grid = new Grid { ColumnSpacing = gap, RowSpacing = gap, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAccessibilityView(grid, AccessibilityView.Raw);
        for (int c = 0; c < Math.Min(columns, b.List.Length); c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        for (int r = 0; r < (b.List.Length + columns - 1) / columns; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(h) });
        for (int i = 0; i < b.List.Length; i++)
        {
            var cover = Cover(b.List[i].GameId, w, h);
            Grid.SetColumn(cover, i % columns);
            Grid.SetRow(cover, i / columns);
            grid.Children.Add(cover);
            _motion.Add(cover, Cue.Drop, YearMotion.ItemAt(b, _theme, i));
        }
        return grid;
    }

    /// <summary>The game's banner and logo: across the top of "Most played", or the summary's header with the year.</summary>
    private FrameworkElement? Hero(BlockSpec b)
    {
        bool summary = _card.Kind == CardKind.Summary;
        var game = _vm.GameFor(b.GameId);
        var hero = new Grid
        {
            Height = summary ? 150 : 260,
            CornerRadius = new CornerRadius(summary ? 8 : 0),
            Margin = summary ? new Thickness(0, 0, 0, 10) : new Thickness(-Pad, -Pad, -Pad, 14),
            Background = _p.Muted,
        };
        if (!_p.HighContrast && game is not null)
        {
            hero.Children.Add(new CoverView { Kind = ArtKind.Hero, DecodeWidth = 560, CornerRadius = new CornerRadius(0), Game = game });
            hero.Children.Add(new Border { Background = (Brush)Application.Current.Resources["BannerScrimBrush"] });
        }
        if (summary)
        {
            var text = new StackPanel { Margin = new Thickness(16, 0, 16, 14), VerticalAlignment = VerticalAlignment.Bottom, Spacing = 2 };
            var year = Display(b.Text ?? "", 52);
            var line = Text(b.Detail ?? "", 13, FontWeights.Normal, _p.Text);
            if (!_p.HighContrast && game is not null)
                year.Foreground = line.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White); // over the scrim, as the shelf's banner
            text.Children.Add(year);
            text.Children.Add(line);
            hero.Children.Add(text);
        }
        else if (!_p.HighContrast && game is not null)
        {
            var logo = new BannerLogo
            {
                MaxLogoHeight = 72,
                Margin = new Thickness(Pad, 0, Pad, 18),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Game = game,
            };
            hero.Children.Add(logo);
            _motion.Add(logo, Cue.Rise, b.At + 240);
        }
        else if (!summary)
        {
            return null; // high contrast: no art, the name below says it
        }
        AutomationProperties.SetAccessibilityView(hero, summary ? AccessibilityView.Content : AccessibilityView.Raw);
        return Animated(hero, b);
    }

    // ---- Tiles ------------------------------------------------------------------------------------------------

    private Border Tile(UIElement child) => new()
    {
        Background = _p.Shape,
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14, 12, 14, 12),
        Child = child,
    };

    /// <summary>Small stat tiles: three in a row, or two by two on the summary.</summary>
    private FrameworkElement Tiles(BlockSpec b)
    {
        int columns = b.List.Length == 4 ? 2 : Math.Max(1, b.List.Length);
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < (b.List.Length + columns - 1) / columns; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < b.List.Length; i++)
        {
            var item = b.List[i];
            // The summary names first, then the value; elsewhere the value large, its name under it.
            bool summary = _card.Kind == CardKind.Summary;
            var value = summary ? Text(item.Label, 17, FontWeights.SemiBold, _p.Text) : Display(item.Label, 28);
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            value.TextWrapping = TextWrapping.NoWrap;
            var name = Text(item.Detail ?? "", 12, FontWeights.Normal, _p.Secondary);
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(summary ? name : value);
            stack.Children.Add(summary ? value : name);
            var tile = Tile(stack);
            AutomationProperties.SetName(tile, Strings.Format(Strings.LabelValue, item.Detail, item.Label));
            Grid.SetColumn(tile, i % columns);
            Grid.SetRow(tile, i / columns);
            grid.Children.Add(tile);
            _motion.Add(tile, YearMotion.CueFor(BlockKind.Tiles, _theme), YearMotion.ItemAt(b, _theme, i));
        }
        return grid;
    }

    /// <summary>One record: its name, the value large, and what it was.</summary>
    private FrameworkElement RecordTile(BlockSpec b)
    {
        var item = b.List[0];
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(Text(b.Text ?? "", 12, FontWeights.Normal, _p.Secondary));
        stack.Children.Add(Display(item.Label, 38));
        stack.Children.Add(Text(item.Detail ?? "", 14, FontWeights.Normal, _p.Text));
        var tile = Tile(stack);
        AutomationProperties.SetName(tile, Strings.Format(Strings.LabelValue, b.Text, Strings.Format(Strings.Dot, item.Label, item.Detail)));
        return Animated(tile, b);
    }

    /// <summary>A titled list, one line per item with a star (the milestones).</summary>
    private FrameworkElement ListTile(BlockSpec b)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Text(b.Text ?? "", 12, FontWeights.Normal, _p.Secondary));
        for (int i = 0; i < b.List.Length; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var star = new FontIcon { Glyph = "", FontSize = 14, Foreground = _p.Accent };
            AutomationProperties.SetAccessibilityView(star, AccessibilityView.Raw);
            row.Children.Add(star);
            row.Children.Add(Text(b.List[i].Label, 14, FontWeights.Normal, _p.Text));
            stack.Children.Add(row);
            _motion.Add(row, YearMotion.CueFor(BlockKind.Tiles, _theme), YearMotion.ItemAt(b, _theme, i));
        }
        return Tile(stack);
    }

    // ---- The rest ------------------------------------------------------------------------------------------------

    /// <summary>Launchers' shares: one bar revealed left to right, a row per launcher with its icon.</summary>
    private FrameworkElement Share(BlockSpec b)
    {
        var panel = new StackPanel { Spacing = 18, Margin = new Thickness(0, 14, 0, 0) };
        var bar = new Grid { Height = 36, CornerRadius = new CornerRadius(6), ColumnSpacing = 2 };
        AutomationProperties.SetAccessibilityView(bar, AccessibilityView.Raw);
        for (int i = 0; i < b.List.Length; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, b.List[i].Value), GridUnitType.Star) });
            var part = new Rectangle { Fill = i == 0 ? _p.Accent : Shade(i) };
            Grid.SetColumn(part, i);
            bar.Children.Add(part);
        }
        _motion.Add(bar, Cue.Wipe, b.At);
        panel.Children.Add(bar);
        var rows = new StackPanel { Spacing = 10 };
        for (int i = 0; i < b.List.Length; i++)
        {
            var item = b.List[i];
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            FrameworkElement icon = item.Source is { } source && Format.HasBadge(source)
                ? new LauncherBadge { Source = source, Margin = new Thickness(0) }
                : new FontIcon { Glyph = "", FontSize = 16, Foreground = _p.Secondary };
            icon.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
            row.Children.Add(icon);
            var name = Text(item.Label, 15, FontWeights.Normal, _p.Text);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var hours = Text(item.Detail ?? "", 13, FontWeights.Normal, _p.Secondary);
            hours.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hours, 2);
            row.Children.Add(hours);
            var percent = Text(Strings.Format(Strings.YirPercent, item.Value), 15, FontWeights.SemiBold, _p.Text);
            percent.TextAlignment = TextAlignment.Right;
            percent.HorizontalAlignment = HorizontalAlignment.Right;
            percent.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(percent, 3);
            row.Children.Add(percent);
            AutomationProperties.SetName(row, Strings.Format(Strings.Dot, item.Label, Strings.Format(Strings.YirPercent, item.Value)));
            rows.Children.Add(row);
            _motion.Add(row, YearMotion.RowCue(_theme), YearMotion.ItemAt(b, _theme, i));
        }
        panel.Children.Add(rows);
        return panel;
    }

    /// <summary>The share bar's other parts: the accent at falling strength, never a launcher's brand color.</summary>
    private Brush Shade(int i)
    {
        if (_p.HighContrast) return i % 2 == 1 ? _p.Muted : _p.Text;
        byte alpha = (byte)Math.Max(60, 200 - i * 35);
        return new SolidColorBrush(Color.FromArgb(alpha, _p.AccentColor.R, _p.AccentColor.G, _p.AccentColor.B));
    }

    /// <summary>A comeback: the game, a dashed break that draws across, then the day it came back.</summary>
    private FrameworkElement Timeline(BlockSpec b)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Text(b.Text ?? "", 12, FontWeights.Normal, _p.Secondary));
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Cover(b.GameId, 48, 64));
        var info = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(Text(b.GameId is null ? "" : _vm.NameOf(b.GameId), 15, FontWeights.SemiBold, _p.Text));
        var line = new Grid { Height = 12 };
        AutomationProperties.SetAccessibilityView(line, AccessibilityView.Raw);
        var dash = new Rectangle { Height = 2, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Fill = _p.Muted };
        var start = new Ellipse { Width = 12, Height = 12, Fill = _p.Accent, HorizontalAlignment = HorizontalAlignment.Left };
        var end = new Ellipse { Width = 12, Height = 12, Fill = _p.Accent, HorizontalAlignment = HorizontalAlignment.Right };
        line.Children.Add(dash);
        line.Children.Add(start);
        line.Children.Add(end);
        info.Children.Add(line);
        int lineAt = b.At + YearMotion.TimelineLineAt;
        _motion.Add(dash, Cue.Grow, lineAt, horizontal: true);
        _motion.Add(end, Cue.Pop, lineAt + YearMotion.Duration(Cue.Grow));
        var dates = new Grid();
        if (b.List.Length == 2)
        {
            dates.Children.Add(Text(b.List[0].Label, 12, FontWeights.Normal, _p.Secondary));
            var back = Text(b.List[1].Label, 12, FontWeights.Normal, _p.Secondary);
            back.HorizontalAlignment = HorizontalAlignment.Right;
            back.TextAlignment = TextAlignment.Right;
            dates.Children.Add(back);
        }
        info.Children.Add(dates);
        Grid.SetColumn(info, 1);
        row.Children.Add(info);
        stack.Children.Add(row);
        var tile = Tile(stack);
        return Animated(tile, b);
    }

    /// <summary>One dot per day of the streak (up to a month), then its dates.</summary>
    private FrameworkElement Dots(BlockSpec b)
    {
        const int perRow = 16;
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 6) };
        var dots = new Grid { ColumnSpacing = 6, RowSpacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAccessibilityView(dots, AccessibilityView.Raw);
        int n = b.List.Length;
        for (int c = 0; c < Math.Min(n, perRow); c++) dots.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        for (int r = 0; r < (n + perRow - 1) / perRow; r++) dots.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        for (int i = 0; i < n; i++)
        {
            var dot = new Ellipse { Fill = _p.Accent };
            Grid.SetColumn(dot, i % perRow);
            Grid.SetRow(dot, i / perRow);
            dots.Children.Add(dot);
            _motion.Add(dot, Cue.Pop, YearMotion.ItemAt(b, _theme, i));
        }
        panel.Children.Add(dots);
        panel.Children.Add(Animated(Text(b.Text ?? "", 14, FontWeights.Normal, _p.Text), new BlockSpec(BlockKind.Line, b.At)));
        return panel;
    }

    /// <summary>Play for the most played game; "Not installed" for a game that left the library.</summary>
    private FrameworkElement Play(BlockSpec b)
    {
        if (!_vm.CanPlay(b.GameId))
            return Animated(Text(Strings.YirNotInstalled, 13, FontWeights.Normal, _p.Secondary), b);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = "", FontSize = 12 });
        content.Children.Add(new TextBlock { Text = Strings.Play });
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Content = content,
            Padding = new Thickness(18, 6, 18, 6),
            Command = _vm.PlayCommand,
        };
        AutomationProperties.SetName(button, Strings.Format(Strings.PlayGame, _vm.NameOf(b.GameId!)));
        return Animated(button, b);
    }

    /// <summary>The summary's Watch again and the other years.</summary>
    private FrameworkElement SummaryActions()
    {
        var panel = new StackPanel { Spacing = 10 };
        var again = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        again.Children.Add(new FontIcon { Glyph = "", FontSize = 14 });
        again.Children.Add(new TextBlock { Text = Strings.YirWatchAgain });
        var watch = new Button { Content = again, Command = _vm.RestartCommand, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(watch, Strings.YirWatchAgain);
        panel.Children.Add(watch);
        var years = _vm.OtherYears;
        if (years.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var label = Text(Strings.YirOtherYears, 12, FontWeights.Normal, _p.Secondary);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            foreach (int year in years.Take(4))
            {
                var chip = new Button { Content = _vm.Label(year), Padding = new Thickness(12, 4, 12, 5) };
                chip.Click += (_, _) => _vm.PickYear(year);
                row.Children.Add(chip);
            }
            panel.Children.Add(row);
        }
        _motion.Add(panel, YearMotion.RowCue(_theme), 520);
        return panel;
    }

    /// <summary>The theme's shapes behind the card's content: circles, stripes or a dot grid. Static; nothing moves.</summary>
    private Canvas Shapes()
    {
        var canvas = new Canvas { IsHitTestVisible = false };
        AutomationProperties.SetAccessibilityView(canvas, AccessibilityView.Raw);
        var fill = _p.Shape;
        switch (_theme.Shapes)
        {
            case ShapeKind.Circles:
                Place(canvas, new Ellipse { Width = 340, Height = 340, Fill = fill }, _width - 150, -150);
                Place(canvas, new Ellipse { Width = 240, Height = 240, Fill = fill, Opacity = 0.7 }, -90, 520);
                break;
            case ShapeKind.Stripes:
                foreach (var (x, width) in new[] { (-60.0, 120.0), (150.0, 56.0), (300.0, 130.0) })
                {
                    var stripe = new Rectangle { Width = width, Height = 1400, Fill = fill, RenderTransform = new RotateTransform { Angle = 24 } };
                    Place(canvas, stripe, x, -300);
                }
                break;
            case ShapeKind.DotGrid:
                // One path of small dots: a single element, drawn once.
                var dots = new GeometryGroup();
                for (double y = 12; y < 1000; y += 22)
                    for (double x = 12; x < _width + 2 * Pad; x += 22)
                        dots.Children.Add(new EllipseGeometry { Center = new Windows.Foundation.Point(x, y), RadiusX = 1.5, RadiusY = 1.5 });
                canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = dots, Fill = fill });
                break;
        }
        return canvas;
    }

    private static void Place(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }
}
