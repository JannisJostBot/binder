using Binder.Library;
using Binder.Models;
using Binder.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Binder.Views;

/// <summary>
/// Covers for games without store art (spec §2, board 2): the game's icon on its own muted color, or a
/// monogram on one of 8 tones, over a soft diagonal gradient. Laid out in design units (a 120 × 160 cover, a
/// 560 × 200 banner) inside a Viewbox, so it's sharp at every cover size.
/// </summary>
public static class FallbackArt
{
    private const double CoverW = 120, CoverH = 160, BannerW = 560, BannerH = 200;

    /// <summary>The cover's background (the gradient) and its content (glow, icon or initials, title).</summary>
    public static (Brush Background, UIElement Content) Cover(Game game, FallbackArtInfo art)
    {
        var tint = Parse(art.Tint);
        var canvas = new Grid { Width = CoverW, Height = CoverH };
        canvas.Children.Add(new Border { Background = Glow(new Point(0.22, 0.12)) });
        // Faint inner edge, so dark tints don't melt into the panel.
        canvas.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        });

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        if (art.Icon is { } icon)
        {
            stack.Margin = new Thickness(0, 34, 0, 0);
            stack.Children.Add(new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
                Child = new Image { Source = CoverCache.IconImage(game, icon), Width = 44, Height = 44 },
            });
        }
        else
        {
            stack.Margin = new Thickness(0, 38, 0, 0);
            stack.Children.Add(Text(CoverColors.Initials(game.Name), 38, display: true, maxLines: 1, CoverW - 16));
        }
        var title = Text(game.Name, 11.5, display: false, maxLines: 2, CoverW - 20);
        title.Margin = new Thickness(0, art.Icon is null ? 4 : 10, 0, 0);
        stack.Children.Add(title);
        canvas.Children.Add(stack);
        return (Gradient(tint), Scaled(canvas));
    }

    /// <summary>The detail banner: the same tint, with the icon (or initials) large and faint on the right.</summary>
    public static (Brush Background, UIElement Content) Banner(Game game, FallbackArtInfo art)
    {
        var canvas = new Grid { Width = BannerW, Height = BannerH };
        canvas.Children.Add(new Border { Background = Glow(new Point(0.15, 0.1)) });
        UIElement mark = art.Icon is { } icon
            ? new Image { Source = CoverCache.IconImage(game, icon), Width = 220, Height = 220, Margin = new Thickness(0, 0, -20, -20) }
            : Text(CoverColors.Initials(game.Name), 150, display: true, maxLines: 1, 260);
        var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.18 };
        holder.Children.Add(mark);
        canvas.Children.Add(holder);
        return (Gradient(Parse(art.Tint)), Scaled(canvas));
    }

    private static Viewbox Scaled(Grid canvas) => new() { Stretch = Stretch.UniformToFill, Child = canvas, IsHitTestVisible = false };

    private static TextBlock Text(string text, double size, bool display, int maxLines, double width) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        FontFamily = new FontFamily(display ? "Segoe UI Variable Display" : "Segoe UI Variable Text"),
        Foreground = new SolidColorBrush(Colors.White),
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextWrapping = maxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = maxLines,
        Width = width,
    };

    /// <summary>
    /// A diagonal gradient around the tint: a little lighter with the hue turned one way at the top left, a
    /// little darker with the hue turned the other way at the bottom right. The middle stays the tint, so white
    /// text keeps the contrast the tones were chosen for.
    /// </summary>
    private static Brush Gradient(Color tint) => new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop { Color = Shift(tint, 18, 0.07), Offset = 0 },
            new GradientStop { Color = tint, Offset = 0.5 },
            new GradientStop { Color = Shift(tint, -14, -0.08), Offset = 1 },
        },
    };

    /// <summary>A soft white highlight, like light falling on the top of the cover.</summary>
    private static Brush Glow(Point center) => new RadialGradientBrush
    {
        Center = center,
        GradientOrigin = center,
        RadiusX = 0.9,
        RadiusY = 0.7,
        GradientStops =
        {
            new GradientStop { Color = Color.FromArgb(0x26, 255, 255, 255), Offset = 0 },
            new GradientStop { Color = Color.FromArgb(0, 255, 255, 255), Offset = 1 },
        },
    };

    /// <summary>The color with its hue turned by <paramref name="hueDegrees"/> and its lightness changed (HSL).</summary>
    internal static Color Shift(Color color, double hueDegrees, double lightness)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0, s = 0, l = (max + min) / 2, d = max - min;
        if (d > 0)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
        }
        h = ((h + hueDegrees) % 360 + 360) % 360;
        l = Math.Clamp(l + lightness, 0, 1);

        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        (double r1, double g1, double b1) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromArgb(255, (byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    /// <summary>"#RRGGBB" to a color; the first monogram tone if it isn't one.</summary>
    internal static Color Parse(string hex)
    {
        if (hex is ['#', ..] && hex.Length == 7 && uint.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return Color.FromArgb(255, 0x3A, 0x4A, 0x6B);
    }
}
