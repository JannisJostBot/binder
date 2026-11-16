using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Binder.Views;

/// <summary>
/// Setup step 2, as on the website's launcher list (site/chips.js): the shortcuts that were just moved onto the
/// shelf fly into Binder's logo. Each visible row becomes a stand-in that flies, one after another, to one of the
/// logo's three covers and turns into it on the way (size, corners, angle, blue); as the last one lands the logo
/// itself shows on top and its play mark pops in. Rows beyond the first seven, or scrolled out of view, and the rows
/// that weren't moved fade while the pills form.
/// Everything is started at once and runs on the compositor; a scoped batch calls back when it's over (about
/// 1.5 s), no timer. Animation effects off or high contrast: no animation, the callback runs at once.
/// </summary>
internal static class LogoMerge
{
    private const int MaxFlying = 7;
    private const float LogoPx = 80, Unit = LogoPx / 48; // logo.svg's 48-unit box, drawn at 80 px
    // First each row becomes a pill around its name (PillMs), a beat (PauseMs), then the pills fly one after another;
    // the logo then stays long enough to take in (HoldMs) and fades before step 3.
    private const float PillMs = 320, PauseMs = 140, FlyMs = 600, StaggerMs = 70, HoldMs = 1100, LogoOutMs = 250;

    /// <summary>A cover of the logo: its centre from the logo's centre (px), its angle and its blue.</summary>
    private readonly record struct Cover(Vector2 Centre, float Degrees, Color Tint);

    // Back to front, as in logo.svg: rect (11,8) turned -14° around (21,40); (17,8) turned 10° around (27,40);
    // (14,10) straight. The tint is the middle of each cover's gradient (chips.js uses the same).
    private static readonly Cover[] Covers =
    [
        Make(21, 22, 21, 40, -14, Color.FromArgb(255, 0xBE, 0xE0, 0xF7)),
        Make(27, 22, 27, 40, 10, Color.FromArgb(255, 0x7F, 0xC4, 0xF0)),
        Make(24, 24, 24, 24, 0, Color.FromArgb(255, 0x3F, 0xA2, 0xE8)),
    ];

    private static Cover Make(float cx, float cy, float ox, float oy, float degrees, Color tint)
    {
        double a = degrees * Math.PI / 180, x = cx - ox, y = cy - oy;
        double rx = ox + x * Math.Cos(a) - y * Math.Sin(a), ry = oy + x * Math.Sin(a) + y * Math.Cos(a);
        return new Cover(new Vector2((float)(rx - 24) * Unit, (float)(ry - 24) * Unit), degrees, tint);
    }

    public static void Play(Canvas layer, FrameworkElement area, IReadOnlyList<FrameworkElement> rows,
        IReadOnlyList<FrameworkElement> others, Action then)
    {
        if (rows.Count == 0 || layer.ActualWidth <= 0 || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled ||
            new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
        {
            then();
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(layer).Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.65f, 0), new Vector2(.35f, 1));

        // Which rows fly: those in view, in list order, at most seven. The rest fade where they are.
        var view = Bounds(area, layer);
        var flying = new List<(FrameworkElement Row, Rect Box)>();
        var fading = new List<FrameworkElement>(others); // the rows left on the desktop go too, as the pills form
        foreach (var row in rows)
        {
            var box = Bounds(row, layer);
            bool inView = box.Height > 0 && box.Top >= view.Top - 1 && box.Bottom <= view.Bottom + 1;
            if (inView && flying.Count < MaxFlying) flying.Add((row, box));
            else fading.Add(row);
        }
        if (flying.Count == 0)
        {
            then();
            return;
        }

        // Stand-ins, their names over them, the logo on top: three layers in the overlay.
        var standInHost = new Canvas();
        var labels = new Canvas { IsHitTestVisible = false };
        var logo = BuildLogo(out var play);
        layer.Children.Add(standInHost);
        layer.Children.Add(labels);
        layer.Children.Add(logo);
        var centre = new Vector2((float)(view.X + view.Width / 2), (float)(view.Y + view.Height / 2));
        Canvas.SetLeft(logo, centre.X - LogoPx / 2);
        Canvas.SetTop(logo, centre.Y - LogoPx / 2);

        var shapes = compositor.CreateShapeVisual();
        shapes.Size = new Vector2((float)layer.ActualWidth, (float)layer.ActualHeight);
        ElementCompositionPreview.SetElementChildVisual(standInHost, shapes);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var resources = Application.Current.Resources;
        var start = Color.FromArgb(0x26, 0x80, 0x80, 0x80); // a faint chip where the row was
        var chip = ((SolidColorBrush)resources["ControlSolidFillColorDefaultBrush"]).Color;
        var edge = ((SolidColorBrush)resources["ControlStrokeColorDefaultBrush"]).Color;
        var noEdge = Color.FromArgb(0, edge.R, edge.G, edge.B);
        var coverSize = new Vector2(20, 28) * Unit;
        float flyFrom = PillMs + PauseMs;
        float lastLand = flyFrom + (flying.Count - 1) * StaggerMs + FlyMs;

        for (int i = 0; i < flying.Count; i++)
        {
            var (row, box) = flying[i];
            // The last rows go to the front cover, so the front is the last to land, on top.
            var cover = Covers[Math.Min(2, i * 3 / flying.Count)];
            float leave = flyFrom + i * StaggerMs, end = leave + FlyMs;
            var rowSize = new Vector2((float)box.Width, (float)box.Height);
            var rowCentre = new Vector2((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2));

            // The pill: around the row's name (a copy of it on top, where the name is, so the row can go), like
            // the website's launcher chips.
            var (pillCentre, pillSize, label) = Pill(row, box, layer);
            if (label is not null) labels.Children.Add(label);

            var geometry = compositor.CreateRoundedRectangleGeometry();
            geometry.Size = rowSize;
            geometry.Offset = -rowSize / 2;
            geometry.CornerRadius = new Vector2(4);
            var brush = compositor.CreateColorBrush(start);
            var stroke = compositor.CreateColorBrush(noEdge);
            var sprite = compositor.CreateSpriteShape(geometry);
            sprite.FillBrush = brush;
            sprite.StrokeBrush = stroke;
            sprite.StrokeThickness = 1;
            var holder = compositor.CreateContainerShape();
            holder.Offset = rowCentre;
            holder.Shapes.Add(sprite);
            shapes.Shapes.Add(holder);

            // One path per property (a second animation would replace the first): row → pill, wait, → cover.
            var pillRadius = new Vector2(pillSize.Y / 2);
            Start(holder, "Offset", V2Path(compositor, end, ease, (PillMs, pillCentre), (leave, pillCentre), (end, centre + cover.Centre)));
            Start(holder, "RotationAngleInDegrees", ScalarPath(compositor, end, ease, (leave, 0), (end, cover.Degrees)));
            Start(geometry, "Size", V2Path(compositor, end, ease, (PillMs, pillSize), (leave, pillSize), (end, coverSize)));
            Start(geometry, "Offset", V2Path(compositor, end, ease, (PillMs, -pillSize / 2), (leave, -pillSize / 2), (end, -coverSize / 2)));
            Start(geometry, "CornerRadius", V2Path(compositor, end, ease, (PillMs, pillRadius), (leave, pillRadius), (end, new Vector2(3 * Unit))));
            brush.StartAnimation("Color", ColorPath(compositor, leave + 550, (PillMs, chip), (leave + 100, chip), (leave + 550, cover.Tint)));
            stroke.StartAnimation("Color", ColorPath(compositor, leave + 200, (PillMs, edge), (leave, edge), (leave + 200, noEdge)));

            // The row goes as its pill forms (its name stays, as the copy); the name goes as the pill leaves.
            ElementCompositionPreview.GetElementVisual(row).StartAnimation("Opacity", Scalar(compositor, 0, 150, 0, null));
            if (label is not null)
                ElementCompositionPreview.GetElementVisual(label).StartAnimation("Opacity", Scalar(compositor, 0, 120, leave, null));
        }
        foreach (var row in fading)
            ElementCompositionPreview.GetElementVisual(row).StartAnimation("Opacity", Scalar(compositor, 0, PillMs, 0, null));

        // The logo shows on top as the last cover lands; the stand-ins under it go; the play mark pops in.
        var logoVisual = ElementCompositionPreview.GetElementVisual(logo);
        logoVisual.Opacity = 0;
        var standIns = ElementCompositionPreview.GetElementVisual(standInHost);
        standIns.StartAnimation("Opacity", Scalar(compositor, 0, 200, lastLand + 170, null));
        var playVisual = ElementCompositionPreview.GetElementVisual(play);
        playVisual.CenterPoint = new Vector3(25, 24, 0);
        playVisual.Scale = new Vector3(0, 0, 1);
        var pop = compositor.CreateVector3KeyFrameAnimation();
        pop.InsertKeyFrame(1, Vector3.One, compositor.CreateCubicBezierEasingFunction(new Vector2(.34f, 1.56f), new Vector2(.64f, 1)));
        pop.Duration = TimeSpan.FromMilliseconds(400);
        pop.DelayTime = TimeSpan.FromMilliseconds(lastLand + 170);
        playVisual.StartAnimation("Scale", pop);

        // Time to take it in, then the logo fades and step 3 comes.
        float logoOut = lastLand + 170 + 400 + HoldMs;
        var logoPath = compositor.CreateScalarKeyFrameAnimation();
        float logoTotal = logoOut + LogoOutMs - (lastLand - 80);
        logoPath.InsertKeyFrame(300 / logoTotal, 1);
        logoPath.InsertKeyFrame((logoOut - (lastLand - 80)) / logoTotal, 1);
        logoPath.InsertKeyFrame(1, 0);
        logoPath.Duration = TimeSpan.FromMilliseconds(logoTotal);
        logoPath.DelayTime = TimeSpan.FromMilliseconds(lastLand - 80);
        logoVisual.StartAnimation("Opacity", logoPath);
        var hold = compositor.CreatePropertySet();
        hold.InsertScalar("t", 0);
        hold.StartAnimation("t", Scalar(compositor, 1, logoOut + LogoOutMs, 0, null));
        batch.End();
        batch.Completed += (_, _) =>
        {
            hold.Dispose();
            then();
        };
    }

    /// <summary>Takes the overlay away and gives the rows back their opacity (step 2 can be shown again).</summary>
    public static void Clear(Canvas layer, IReadOnlyList<FrameworkElement> rows)
    {
        foreach (var child in layer.Children)
            if (child is Canvas host) ElementCompositionPreview.SetElementChildVisual(host, null);
        layer.Children.Clear();
        foreach (var row in rows)
        {
            var visual = ElementCompositionPreview.GetElementVisual(row);
            visual.StopAnimation("Opacity");
            visual.Opacity = 1;
        }
    }

    /// <summary>
    /// Where a row's pill goes: around its name (the first text in a CheckBox's content), with a copy of the name
    /// laid over the row's own. Without a name, a pill the row's height at its left.
    /// </summary>
    private static (Vector2 Centre, Vector2 Size, TextBlock? Label) Pill(FrameworkElement row, Rect box, UIElement layer)
    {
        const float padX = 12, padY = 5;
        if (row is ContentControl { Content: Panel { Children: [TextBlock name, ..] } })
        {
            var at = Bounds(name, layer);
            var label = new TextBlock
            {
                Text = name.Text, FontSize = name.FontSize, FontFamily = name.FontFamily, FontWeight = name.FontWeight,
                Foreground = name.Foreground, TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = Math.Max(0, box.Right - at.X - 2 * padX), IsHitTestVisible = false,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, at.X);
            Canvas.SetTop(label, at.Y);
            var size = new Vector2((float)label.DesiredSize.Width + 2 * padX, (float)at.Height + 2 * padY);
            var centre = new Vector2((float)at.X - padX + size.X / 2, (float)(at.Y + at.Height / 2));
            return (centre, size, label);
        }
        float h = (float)Math.Min(box.Height, 32);
        var fallback = new Vector2((float)Math.Min(box.Width, 160), h);
        return (new Vector2((float)box.X + fallback.X / 2, (float)(box.Y + box.Height / 2)), fallback, null);
    }

    private static Vector2KeyFrameAnimation V2Path(Compositor c, float total, CompositionEasingFunction ease, params (float At, Vector2 Value)[] keys)
    {
        var a = c.CreateVector2KeyFrameAnimation();
        foreach (var (at, value) in keys) a.InsertKeyFrame(at / total, value, ease);
        a.Duration = TimeSpan.FromMilliseconds(total);
        return a;
    }

    private static ScalarKeyFrameAnimation ScalarPath(Compositor c, float total, CompositionEasingFunction ease, params (float At, float Value)[] keys)
    {
        var a = c.CreateScalarKeyFrameAnimation();
        foreach (var (at, value) in keys) a.InsertKeyFrame(at / total, value, ease);
        a.Duration = TimeSpan.FromMilliseconds(total);
        return a;
    }

    private static ColorKeyFrameAnimation ColorPath(Compositor c, float total, params (float At, Color Value)[] keys)
    {
        var a = c.CreateColorKeyFrameAnimation();
        a.InterpolationColorSpace = CompositionColorSpace.Rgb; // straight from grey to blue, not round the hue wheel
        foreach (var (at, value) in keys) a.InsertKeyFrame(at / total, value);
        a.Duration = TimeSpan.FromMilliseconds(total);
        return a;
    }

    private static Rect Bounds(FrameworkElement element, UIElement to) =>
        element.TransformToVisual(to).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static void Start(CompositionObject target, string property, CompositionAnimation animation) =>
        target.StartAnimation(property, animation);

    private static Vector2KeyFrameAnimation V2(Compositor c, Vector2 to, float ms, float delay, CompositionEasingFunction ease)
    {
        var a = c.CreateVector2KeyFrameAnimation();
        a.InsertKeyFrame(1, to, ease);
        a.Duration = TimeSpan.FromMilliseconds(ms);
        a.DelayTime = TimeSpan.FromMilliseconds(delay);
        return a;
    }

    private static ScalarKeyFrameAnimation Scalar(Compositor c, float to, float ms, float delay, CompositionEasingFunction? ease)
    {
        var a = c.CreateScalarKeyFrameAnimation();
        if (ease is null) a.InsertKeyFrame(1, to);
        else a.InsertKeyFrame(1, to, ease);
        a.Duration = TimeSpan.FromMilliseconds(Math.Max(1, ms));
        a.DelayTime = TimeSpan.FromMilliseconds(Math.Max(0, delay));
        return a;
    }

    /// <summary>Binder's logo (site/logo.svg) at <see cref="LogoPx"/>: three fanned covers and the play mark.</summary>
    private static Canvas BuildLogo(out Microsoft.UI.Xaml.Shapes.Path play)
    {
        var box = new Canvas { Width = 48, Height = 48, RenderTransform = new ScaleTransform { ScaleX = Unit, ScaleY = Unit } };
        box.Children.Add(CoverShape(11, 8, -14, 0xE3F5FF, 0x9CCDEF));
        box.Children.Add(CoverShape(17, 8, 10, 0xB4E6FF, 0x4AA6E6));
        box.Children.Add(CoverShape(14, 10, 0, 0x7BD8FF, 0x0C78D2));
        var white = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        var figure = new PathFigure { StartPoint = new Point(21, 18.5), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment { Point = new Point(21, 29.5) });
        figure.Segments.Add(new LineSegment { Point = new Point(29.5, 24) });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        play = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry, Fill = white, Stroke = white, StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round,
        };
        box.Children.Add(play);
        return new Canvas { Width = LogoPx, Height = LogoPx, IsHitTestVisible = false, Children = { box } };
    }

    /// <summary>One 20 × 28 cover with corner radius 3, at (x, y), turned around the logo's pivot below it.</summary>
    private static Rectangle CoverShape(double x, double y, double degrees, int from, int to)
    {
        static Color Rgb(int v) => Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        fill.GradientStops.Add(new GradientStop { Offset = 0, Color = Rgb(from) });
        fill.GradientStops.Add(new GradientStop { Offset = 1, Color = Rgb(to) });
        var rect = new Rectangle { Width = 20, Height = 28, RadiusX = 3, RadiusY = 3, Fill = fill };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        // logo.svg turns each cover around (x + 10, 40), i.e. 10 across and 32 down from its own corner.
        if (degrees != 0) rect.RenderTransform = new RotateTransform { Angle = degrees, CenterX = 10, CenterY = 32 };
        return rect;
    }
}
