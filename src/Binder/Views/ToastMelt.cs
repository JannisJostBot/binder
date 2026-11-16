using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Effects;
using Windows.UI;

namespace Binder.Views;

/// <summary>
/// The Game mode pill's changes melting in (canvas board 33, "Melt in", the board's default tweaks). Each change
/// falls onto the pill as a drop of the pill's own surface and melts into it; a faint light spreads inside from
/// where it entered. The pill and the drops are blurred together and cut at two alpha levels (edge, then fill), a
/// blur and color-matrix effect from Win2D on a visual surface, so drop and pill flow into one smooth outline.
/// "Back to normal" plays the other way, in order: left to right on an even beat, each change swells gently out
/// of the pill where it went in and floats back up to where it fell from; when all are back in their row they
/// pop one after another, each leaving a thin ring that softens as it spreads.
/// Everything is started once, with its delays worked out up front, and runs on the compositor. Landings (or
/// arrivals) are reported through scoped batches (no timer). The visuals are removed once all is done.
/// </summary>
internal sealed class ToastMelt
{
    // The board's defaults: stagger 130 ms, spring 0.6, variation 0.5, stickiness 0.5.
    private const float Radius = 14, FallHeight = 96, SinkDepth = 24, Stagger = 130, Variation = 0.5f, Stickiness = 0.5f;
    private const float MeltMs = 320, FirstDropMs = 380;
    private const float EmergeMs = 440, RiseMs = 720, RowWaitMs = 350, PopGapMs = 110, PopMs = 190, RingMs = 800;
    private const float Pad = 24, Top = FallHeight + Radius + 26; // room around the pill for the drops and the blur

    /// <summary>
    /// One change. Coming in: it starts falling at Start and lands at Land. Going back: it starts to swell out at
    /// Start, lets go at Free, is back in its place at Land, and pops at Pop.
    /// </summary>
    private readonly record struct Drop(float X, float Start, float FallMs, float Land, float Free = 0, float Pop = 0);

    private readonly Compositor _compositor;
    private readonly UIElement _host;
    private readonly Canvas _glyphHost;
    private readonly bool _back; // "Back to normal": the changes go back out
    private readonly float _k; // 1, or 4 for slow motion
    private readonly Vector2 _size;
    private readonly Color _fill, _stroke, _glow;
    private readonly Brush _glyphBrush;
    private readonly Drop[] _drops;
    private readonly List<Action> _start = [];
    private ContainerVisual? _root;
    private CompositionEffectBrush? _effect;

    /// <summary>Landing times in ms from <see cref="Start"/>, earliest first (slow motion included).</summary>
    public float[] Lands { get; }

    /// <summary>When everything is over and the visuals go (ms from <see cref="Start"/>).</summary>
    public float Done { get; }

    /// <summary>When the needle or the check should move: the last change is in, or the last bubble has popped.</summary>
    public float Settled { get; }

    /// <summary>Going back: when the check draws, as the first change starts to leave (the toast's timer still
    /// goes by <see cref="Settled"/>).</summary>
    public float CheckAt { get; }

    /// <summary>A drop landed: its number (0 = first to land) and how many drops there are.</summary>
    public event Action<int, int>? Landed;

    /// <summary>All drops have merged and the visuals are gone: the XAML surface can show again.</summary>
    public event Action? Finished;

    private static readonly string[] Glyphs = ["", "", "", "", ""]; // quiet hours, power, pause, close, cloud

    public ToastMelt(UIElement host, Canvas glyphHost, bool back, float slow, int drops, Vector2 pillSize,
        Color fill, Color stroke, Color glow, Brush glyphBrush)
    {
        _host = host;
        _glyphHost = glyphHost;
        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _back = back;
        _k = slow;
        _size = pillSize;
        _fill = fill;
        _stroke = stroke;
        _glow = glow;
        _glyphBrush = glyphBrush;
        _drops = back ? PlanBack(Math.Clamp(drops, 1, 5), pillSize.X) : Plan(Math.Clamp(drops, 1, 5), pillSize.X);
        Lands = [.. _drops.Select(d => d.Land * _k).Order()];
        if (back)
        {
            float lastPop = _drops.Max(d => d.Pop);
            Settled = (lastPop + PopMs) * _k;
            CheckAt = _drops.Min(d => d.Start) * _k; // the check draws as the first change starts to leave
            Done = (lastPop + 100 + RingMs + 60) * _k;
        }
        else
        {
            Settled = (_drops.Max(d => d.Land) + 80) * _k;
            Done = (_drops.Max(d => d.Land) + MeltMs + 160) * _k;
        }
    }

    // ---- The plan: where and when each drop falls (board 33's numbers) ------------------------------------

    private static double Rnd(int i, int n)
    {
        double x = Math.Sin(i * 12.9898 + n * 78.233) * 43758.5453;
        return x - Math.Floor(x);
    }

    private static double Spread(int i, int n) => Rnd(i, n) * 2 - 1;

    private static Drop[] Plan(int count, float width)
    {
        // An uneven beat that speeds up a little, and an order that isn't left to right.
        var starts = new float[count];
        double t = FirstDropMs;
        for (int i = 0; i < count; i++)
        {
            starts[i] = (float)t;
            t += Stagger * (1 - 0.08 * i) * (1 + 0.4 * Variation * Spread(i, 1));
        }
        int[] order = count == 5 ? [1, 3, 0, 4, 2] : [.. Enumerable.Range(0, count).OrderBy(i => Rnd(i, 9))];
        var drops = new Drop[count];
        for (int i = 0; i < count; i++)
        {
            double frac = count == 1 ? 0.5 : 0.2 + 0.64 * i / (count - 1) + Variation * 0.035 * Spread(i, 2);
            float fall = (float)(500 + 80 * Variation * Spread(i, 4));
            float start = starts[Array.IndexOf(order, i)];
            drops[i] = new Drop((float)(frac * width), start, fall, start + fall);
        }
        return drops;
    }

    /// <summary>Going back: in order, left to right, on an even beat; all wait in their row, then pop in turn.</summary>
    private static Drop[] PlanBack(int count, float width)
    {
        var drops = new Drop[count];
        float allBack = 300 + (count - 1) * Stagger + EmergeMs + RiseMs + RowWaitMs;
        for (int i = 0; i < count; i++)
        {
            double frac = count == 1 ? 0.5 : 0.2 + 0.64 * i / (count - 1) + Variation * 0.035 * Spread(i, 2);
            float start = 300 + i * Stagger;
            float free = start + EmergeMs;
            drops[i] = new Drop((float)(frac * width), start, 0, free + RiseMs, free, allBack + i * PopGapMs);
        }
        return drops;
    }

    // ---- Easing -------------------------------------------------------------------------------------------

    private static readonly Vector4 Gravity = new(.55f, .085f, .68f, .53f);
    private static readonly Vector4 SinkIn = new(.2f, .6f, .4f, 1);
    private static readonly Vector4 Squash = new(.3f, .7f, .5f, 1);
    private static readonly Vector4 GlowOut = new(.2f, .7f, .3f, 1);
    private static readonly Vector4 EmergeEase = new(.45f, 0, .25f, 1); // a gentle swell, no yank
    private static readonly Vector4 RiseEase = new(.35f, 0, .25f, 1);
    private static readonly Vector4 RingOut = new(.2f, .6f, .35f, 1);

    private CompositionEasingFunction Ease(Vector4 b) =>
        _compositor.CreateCubicBezierEasingFunction(new Vector2(b.X, b.Y), new Vector2(b.Z, b.W));

    private TimeSpan Ms(float ms) => TimeSpan.FromMilliseconds(Math.Max(1, ms * _k));

    // ---- Building and starting ----------------------------------------------------------------------------

    /// <summary>Creates the visuals in their first state (the pill alone; drops hidden). Call before the window shows.</summary>
    public void Build()
    {
        _root = _compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(_host, _root);

        BuildWin2D();
        BuildGlows();
        BuildGlyphs();
        if (_back) BuildRings();
    }

    /// <summary>Starts every animation at once; they run on the compositor from here.</summary>
    public void Start()
    {
        foreach (var start in _start) start();
        _start.Clear();
        for (int i = 0; i < Lands.Length; i++)
        {
            int index = i;
            After(Lands[i], () => Landed?.Invoke(index, Lands.Length));
        }
        After(Done, Remove);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread after <paramref name="ms"/> (a scoped batch, no timer).</summary>
    private void After(float ms, Action action)
    {
        var clock = _compositor.CreatePropertySet();
        clock.InsertScalar("t", 0);
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var run = _compositor.CreateScalarKeyFrameAnimation();
        run.InsertKeyFrame(1, 1);
        run.Duration = TimeSpan.FromMilliseconds(Math.Max(1, ms));
        clock.StartAnimation("t", run);
        batch.End();
        batch.Completed += (_, _) =>
        {
            action();
            clock.Dispose();
        };
    }

    /// <summary>Takes the melt visuals away (the toast's own surface is shown again by the owner).</summary>
    public void Remove()
    {
        if (_root is null) return;
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _glyphHost.Children.Clear();
        _effect?.Dispose();
        _effect = null;
        _root.Dispose();
        _root = null;
        Finished?.Invoke();
    }

    /// <summary>The drop's fall, then its sink into the pill (Offset of a shape).</summary>
    private Vector2KeyFrameAnimation FallAndSink(Drop d)
    {
        var a = _compositor.CreateVector2KeyFrameAnimation();
        if (_back)
        {
            // Out of the pill where it went in, then up to its place in the row.
            float span = EmergeMs + RiseMs;
            a.InsertKeyFrame(0, new Vector2(d.X, SinkDepth));
            a.InsertKeyFrame(EmergeMs / span, new Vector2(d.X, 0), Ease(EmergeEase));
            a.InsertKeyFrame(1, new Vector2(d.X, -FallHeight), Ease(RiseEase));
            a.Duration = Ms(span);
            a.DelayTime = Ms(d.Start);
            a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            return a;
        }
        float total = d.FallMs + MeltMs;
        a.InsertKeyFrame(0, new Vector2(d.X, -FallHeight));
        a.InsertKeyFrame(d.FallMs / total, new Vector2(d.X, 0), Ease(Gravity));
        a.InsertKeyFrame(1, new Vector2(d.X, SinkDepth), Ease(SinkIn));
        a.Duration = Ms(total);
        a.DelayTime = Ms(d.Start);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        return a;
    }

    /// <summary>The drop appears small, then squashes and shrinks as it melts in (Scale of a shape).</summary>
    private Vector2KeyFrameAnimation GrowAndMelt(Drop d)
    {
        var a = _compositor.CreateVector2KeyFrameAnimation();
        if (_back)
        {
            // Un-melts as it swells out, keeps its size in the row, then pops: a small swell and gone.
            float span = d.Pop + PopMs - d.Start;
            float swell = d.Pop + PopMs * 0.55f - d.Start;
            a.InsertKeyFrame(0, new Vector2(0.25f));
            a.InsertKeyFrame(EmergeMs * 0.65f / span, new Vector2(0.86f, 1.18f), Ease(Squash));
            a.InsertKeyFrame((EmergeMs + 60) / span, Vector2.One, Ease(Squash));
            a.InsertKeyFrame((d.Pop - d.Start) / span, Vector2.One);
            a.InsertKeyFrame(swell / span, new Vector2(1.22f), Ease(new Vector4(0, 0, .58f, 1)));
            a.InsertKeyFrame((swell + 1) / span, Vector2.Zero);
            a.InsertKeyFrame(1, Vector2.Zero);
            a.Duration = Ms(span);
            a.DelayTime = Ms(d.Start);
            return a;
        }
        float total = d.FallMs + MeltMs + 60;
        a.InsertKeyFrame(0, new Vector2(0.3f));
        a.InsertKeyFrame(180 / total, Vector2.One, Ease(new Vector4(0, 0, .58f, 1)));
        a.InsertKeyFrame(d.FallMs / total, Vector2.One);
        a.InsertKeyFrame((d.FallMs + 0.35f * (MeltMs + 60)) / total, new Vector2(0.86f, 1.18f), Ease(Squash));
        a.InsertKeyFrame(1, new Vector2(0.25f), Ease(Squash));
        a.Duration = Ms(total);
        a.DelayTime = Ms(d.Start); // the shape stays at scale 0 (hidden) until then
        return a;
    }

    private CompositionSpriteShape DropShape(Drop d, float radius, CompositionBrush? fill, CompositionBrush? stroke)
    {
        var geometry = _compositor.CreateEllipseGeometry();
        geometry.Radius = new Vector2(radius);
        var shape = _compositor.CreateSpriteShape(geometry);
        shape.FillBrush = fill;
        if (stroke is not null)
        {
            shape.StrokeBrush = stroke;
            shape.StrokeThickness = 2;
            shape.IsStrokeNonScaling = true;
        }
        shape.Offset = new Vector2(d.X, _back ? SinkDepth : -FallHeight);
        shape.Scale = Vector2.Zero;
        _start.Add(() =>
        {
            shape.StartAnimation("Offset", FallAndSink(d));
            shape.StartAnimation("Scale", GrowAndMelt(d));
        });
        return shape;
    }

    /// <summary>A shape visual covering the pill and the room above it, with its shapes in pill coordinates.</summary>
    private (ShapeVisual Visual, CompositionContainerShape Shapes) Area()
    {
        var visual = _compositor.CreateShapeVisual();
        visual.Size = new Vector2(_size.X + 2 * Pad, _size.Y + Top + Pad);
        visual.Offset = new Vector3(-Pad, -Top, 0);
        var shapes = _compositor.CreateContainerShape();
        shapes.Offset = new Vector2(Pad, Top);
        visual.Shapes.Add(shapes);
        return (visual, shapes);
    }

    // ---- Win2D: blur and two alpha cuts --------------------------------------------------------------------

    private void BuildWin2D()
    {
        // The source: the pill and the drops as solid shapes, 1 px inside their final edge. It stays in the tree
        // under the effect (a visual surface captures a live visual), drawn in the fill color, where the effect
        // covers it exactly.
        var (source, shapes) = Area();
        var fill = _compositor.CreateColorBrush(_fill);
        var pill = _compositor.CreateRoundedRectangleGeometry();
        pill.Offset = new Vector2(1);
        pill.Size = _size - new Vector2(2);
        pill.CornerRadius = new Vector2(_size.Y / 2 - 1);
        var pillShape = _compositor.CreateSpriteShape(pill);
        pillShape.FillBrush = fill;
        shapes.Shapes.Add(pillShape);
        foreach (var d in _drops) shapes.Shapes.Add(DropShape(d, Radius - 1, fill, null));
        _root!.Children.InsertAtTop(source);

        var surface = _compositor.CreateVisualSurface();
        surface.SourceVisual = source;
        surface.SourceSize = source.Size;
        var surfaceBrush = _compositor.CreateSurfaceBrush(surface);
        surfaceBrush.Stretch = CompositionStretch.None;
        surfaceBrush.HorizontalAlignmentRatio = 0;
        surfaceBrush.VerticalAlignmentRatio = 0;

        // Blur, then cut: the edge where the blurred alpha passes 0.434 (1 px outside the source's straight edges),
        // the fill from 0.5 (on them). Steep cuts keep the outline crisp; the blur makes drop and pill flow together.
        float sigma = 3.5f + 5 * Stickiness;
        float AlphaBias(float level) => 0.5f - 26 * level;
        // Two steps: the effect premultiplies before it clamps, so a color set in the same matrix as the steep alpha
        // would come out multiplied by up to 26 (white). First the alpha alone, clamped; then the color on it.
        IGraphicsEffectSource Cut(Color color, float level) => new ColorMatrixEffect
        {
            Source = new ColorMatrixEffect
            {
                Source = new GaussianBlurEffect
                {
                    Source = new CompositionEffectSourceParameter("src"),
                    BlurAmount = sigma,
                    BorderMode = EffectBorderMode.Hard,
                },
                ColorMatrix = new Matrix5x4 { M44 = 26, M54 = AlphaBias(level) },
                ClampOutput = true,
            },
            ColorMatrix = new Matrix5x4
            {
                M44 = 1,
                M51 = color.R / 255f, M52 = color.G / 255f, M53 = color.B / 255f,
            },
            ClampOutput = true,
        };
        var graph = new CompositeEffect { Mode = CanvasComposite.SourceOver };
        graph.Sources.Add(Cut(_stroke, 0.434f));
        graph.Sources.Add(Cut(_fill, 0.5f));
        _effect = _compositor.CreateEffectFactory(graph).CreateBrush();
        _effect.SetSourceParameter("src", surfaceBrush);

        var sprite = _compositor.CreateSpriteVisual();
        sprite.Brush = _effect;
        sprite.Size = source.Size;
        sprite.Offset = source.Offset;
        _root.Children.InsertAtTop(sprite);
    }

    // ---- The light that spreads inside, and the icons that ride on the drops --------------------------------

    private void BuildGlows()
    {
        var clipGeometry = _compositor.CreateRoundedRectangleGeometry();
        clipGeometry.Size = _size;
        clipGeometry.CornerRadius = new Vector2(_size.Y / 2);
        var layer = _compositor.CreateContainerVisual();
        layer.Size = _size;
        layer.Clip = _compositor.CreateGeometricClip(clipGeometry);
        _root!.Children.InsertAtTop(layer);
        // What's above the pill's top edge: the bubbles' part of the glow (inside the pill, the pill's own shows).
        var above = _compositor.CreateContainerVisual();
        above.Offset = new Vector3(-Pad, -Top, 0);
        above.Size = new Vector2(_size.X + 2 * Pad, Top);
        above.Clip = _compositor.CreateInsetClip();
        _root.Children.InsertAtTop(above);

        foreach (var d in _drops)
        {
            var brush = _compositor.CreateRadialGradientBrush();
            brush.EllipseCenter = new Vector2(0.5f);
            brush.EllipseRadius = new Vector2(0.5f);
            brush.ColorStops.Add(_compositor.CreateColorGradientStop(0, Color.FromArgb(87, _glow.R, _glow.G, _glow.B)));
            brush.ColorStops.Add(_compositor.CreateColorGradientStop(0.68f, Color.FromArgb(0, _glow.R, _glow.G, _glow.B)));
            var glow = _compositor.CreateSpriteVisual();
            glow.Brush = brush;
            glow.Size = new Vector2(88);
            glow.CenterPoint = new Vector3(44, 44, 0);
            glow.Offset = new Vector3(d.X - 44, -44, 0);
            glow.Opacity = 0;
            layer.Children.InsertAtTop(glow);

            var scale = _compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0, new Vector3(0.2f, 0.2f, 1));
            scale.InsertKeyFrame(1, new Vector3(2.4f, 2.4f, 1), Ease(GlowOut));
            var fade = _compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(0.12f, 1, Ease(GlowOut));
            fade.InsertKeyFrame(1, 0, Ease(GlowOut));
            foreach (var a in new KeyFrameAnimation[] { scale, fade })
            {
                a.Duration = Ms(_back ? 700 : 650);
                a.DelayTime = Ms(_back ? d.Start : d.Land + 30);
            }
            // The light also fills the part of the change that's outside the pill while it's joined to it, so it
            // doesn't stop at the pill's edge: the same glow, clipped to the bubble as it moves and grows or shrinks
            // (going back, gone as the bubble lets go).
            var circle = _compositor.CreateEllipseGeometry();
            circle.Radius = new Vector2(Radius);
            var bubbleClip = _compositor.CreateGeometricClip(circle);
            bubbleClip.Offset = new Vector2(d.X, _back ? SinkDepth : -FallHeight);
            bubbleClip.Scale = Vector2.Zero;
            var bubble = _compositor.CreateContainerVisual();
            bubble.Offset = new Vector3(Pad, Top, 0); // back to the pill's coordinates
            bubble.Size = _size;
            bubble.Clip = bubbleClip;
            above.Children.InsertAtTop(bubble);
            var inBubble = _compositor.CreateSpriteVisual();
            inBubble.Brush = brush;
            inBubble.Size = glow.Size;
            inBubble.CenterPoint = glow.CenterPoint;
            inBubble.Offset = glow.Offset;
            inBubble.Opacity = 0;
            bubble.Children.InsertAtTop(inBubble);
            ScalarKeyFrameAnimation? letGo = null;
            if (_back)
            {
                float span = EmergeMs + 60;
                letGo = _compositor.CreateScalarKeyFrameAnimation();
                letGo.InsertKeyFrame(0, 1);
                letGo.InsertKeyFrame((EmergeMs - 120) / span, 1);
                letGo.InsertKeyFrame(1, 0);
                letGo.Duration = Ms(span);
                letGo.DelayTime = Ms(d.Start);
            }
            _start.Add(() =>
            {
                glow.StartAnimation("Scale", scale);
                glow.StartAnimation("Opacity", fade);
                inBubble.StartAnimation("Scale", scale);
                inBubble.StartAnimation("Opacity", fade);
                bubbleClip.StartAnimation("Offset", FallAndSink(d));
                bubbleClip.StartAnimation("Scale", GrowAndMelt(d));
                if (letGo is not null) bubble.StartAnimation("Opacity", letGo);
            });
        }
    }

    private void BuildGlyphs()
    {
        for (int i = 0; i < _drops.Length; i++)
        {
            var d = _drops[i];
            var icon = new FontIcon
            {
                Glyph = Glyphs[i % Glyphs.Length],
                FontSize = 14,
                Width = 14,
                Height = 14,
                Foreground = _glyphBrush,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(icon, d.X - 7);
            Canvas.SetTop(icon, -7);
            _glyphHost.Children.Add(icon);
            ElementCompositionPreview.SetIsTranslationEnabled(icon, true);
            var visual = ElementCompositionPreview.GetElementVisual(icon);
            visual.Opacity = 0;
            visual.CenterPoint = new Vector3(7, 7, 0);
            visual.Properties.InsertVector3("Translation", new Vector3(0, _back ? SinkDepth : -FallHeight, 0));

            if (_back)
            {
                // Rides out with its drop, shows as it clears the edge, and pops with it.
                float span = EmergeMs + RiseMs;
                var up = _compositor.CreateVector3KeyFrameAnimation();
                up.InsertKeyFrame(0, new Vector3(0, SinkDepth, 0));
                up.InsertKeyFrame(EmergeMs / span, Vector3.Zero, Ease(EmergeEase));
                up.InsertKeyFrame(1, new Vector3(0, -FallHeight, 0), Ease(RiseEase));
                up.Duration = Ms(span);
                up.DelayTime = Ms(d.Start);
                up.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

                float showAt = d.Start + EmergeMs * 0.5f;
                float life2 = d.Pop + PopMs - showAt;
                float popAt = d.Pop + PopMs * 0.55f - showAt;
                var seen = _compositor.CreateScalarKeyFrameAnimation();
                seen.InsertKeyFrame(0, 0);
                seen.InsertKeyFrame(260 / life2, 1, Ease(new Vector4(0, 0, .58f, 1)));
                seen.InsertKeyFrame(popAt / life2, 1);
                seen.InsertKeyFrame((popAt + 1) / life2, 0);
                seen.InsertKeyFrame(1, 0);
                seen.Duration = Ms(life2);
                seen.DelayTime = Ms(showAt);

                var size = _compositor.CreateVector3KeyFrameAnimation();
                size.InsertKeyFrame(0, new Vector3(0.5f, 0.5f, 1));
                size.InsertKeyFrame(260 / life2, Vector3.One, Ease(new Vector4(0, 0, .58f, 1)));
                size.InsertKeyFrame((d.Pop - showAt) / life2, Vector3.One);
                size.InsertKeyFrame(popAt / life2, new Vector3(1.2f, 1.2f, 1), Ease(new Vector4(0, 0, .58f, 1)));
                size.InsertKeyFrame(1, new Vector3(1.2f, 1.2f, 1));
                size.Duration = Ms(life2);
                size.DelayTime = Ms(showAt);
                size.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

                _start.Add(() =>
                {
                    visual.StartAnimation("Translation", up);
                    visual.StartAnimation("Opacity", seen);
                    visual.StartAnimation("Scale", size);
                });
                continue;
            }

            var move = _compositor.CreateVector3KeyFrameAnimation();
            float total = d.FallMs + MeltMs;
            move.InsertKeyFrame(0, new Vector3(0, -FallHeight, 0));
            move.InsertKeyFrame(d.FallMs / total, Vector3.Zero, Ease(Gravity));
            move.InsertKeyFrame(1, new Vector3(0, SinkDepth, 0), Ease(SinkIn));
            move.Duration = Ms(total);
            move.DelayTime = Ms(d.Start);
            move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            float life = d.FallMs + 220;
            var fade = _compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(200 / life, 1, Ease(new Vector4(0, 0, .58f, 1)));
            fade.InsertKeyFrame(d.FallMs / life, 1);
            fade.InsertKeyFrame(1, 0, Ease(new Vector4(.42f, 0, 1, 1)));
            fade.Duration = Ms(life);
            fade.DelayTime = Ms(d.Start);

            var shrink = _compositor.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(0, new Vector3(0.4f, 0.4f, 1));
            shrink.InsertKeyFrame(200 / life, Vector3.One, Ease(new Vector4(0, 0, .58f, 1)));
            shrink.InsertKeyFrame(d.FallMs / life, Vector3.One);
            shrink.InsertKeyFrame(1, new Vector3(0.5f, 0.5f, 1), Ease(new Vector4(.42f, 0, 1, 1)));
            shrink.Duration = Ms(life);
            shrink.DelayTime = Ms(d.Start);
            shrink.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            _start.Add(() =>
            {
                visual.StartAnimation("Translation", move);
                visual.StartAnimation("Opacity", fade);
                visual.StartAnimation("Scale", shrink);
            });
        }
    }
    /// <summary>
    /// The ring each bubble leaves when it pops: thin and sharp, then wider and softer as it spreads and fades.
    /// A ring-shaped radial gradient with a Gaussian cross-section that widens (no blur needed, so it's the same
    /// in both modes).
    /// </summary>
    private void BuildRings()
    {
        // Drawn at twice the size it starts at, so growing it never stretches the gradient.
        const float size = 128, line = 0.47f;
        // The line's cross-section is a Gaussian, sampled finely: a few straight-line stops show as bands.
        const int steps = 8;
        foreach (var d in _drops)
        {
            var brush = _compositor.CreateRadialGradientBrush();
            brush.EllipseCenter = new Vector2(0.5f);
            brush.EllipseRadius = new Vector2(0.5f);
            var stops = new List<(CompositionColorGradientStop Stop, float U)>();
            for (int i = -steps; i <= steps; i++)
            {
                float u = (float)i / steps; // -1 inside .. 1 outside, in half-widths
                float alpha = i == -steps || i == steps ? 0 : MathF.Exp(-4.5f * u * u);
                var stop = _compositor.CreateColorGradientStop(0,
                    Color.FromArgb((byte)MathF.Round(alpha * _glow.A), _glow.R, _glow.G, _glow.B));
                brush.ColorStops.Add(stop);
                stops.Add((stop, u));
            }
            var ring = _compositor.CreateSpriteVisual();
            ring.Brush = brush;
            ring.Size = new Vector2(size);
            ring.CenterPoint = new Vector3(size / 2, size / 2, 0);
            ring.Offset = new Vector3(d.X - size / 2, -FallHeight - size / 2, 0);
            ring.Opacity = 0;
            ring.Scale = new Vector3(0.525f, 0.525f, 1);
            _root!.Children.InsertAtTop(ring);

            float at = d.Pop + 100;
            var grow = _compositor.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(0, new Vector3(0.525f, 0.525f, 1));
            grow.InsertKeyFrame(1, new Vector3(1.05f, 1.05f, 1), Ease(RingOut));
            var fade = _compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(0.08f, 0.85f);
            fade.InsertKeyFrame(0.45f, 0.5f);
            fade.InsertKeyFrame(1, 0, Ease(RingOut));
            var animations = new List<(CompositionObject Target, string Property, KeyFrameAnimation Animation)>
            {
                (ring, "Scale", grow),
                (ring, "Opacity", fade),
            };
            // Softening: every stop moves out from the line together, so the profile stays a smooth bell as it
            // widens (more inward than outward is the old look: 0.07 → 0.29 inside, 0.05 → 0.33 outside).
            foreach (var (stop, u) in stops)
            {
                float from = line + u * (u < 0 ? 0.07f : 0.05f), to = line + u * (u < 0 ? 0.29f : 0.33f);
                stop.Offset = from;
                var soften = _compositor.CreateScalarKeyFrameAnimation();
                soften.InsertKeyFrame(0, from);
                soften.InsertKeyFrame(1, to, Ease(RingOut));
                animations.Add((stop, "Offset", soften));
            }
            foreach (var (_, _, a) in animations)
            {
                a.Duration = Ms(RingMs);
                a.DelayTime = Ms(at);
            }
            _start.Add(() =>
            {
                foreach (var (target, property, a) in animations) target.StartAnimation(property, a);
            });
        }
    }
}
