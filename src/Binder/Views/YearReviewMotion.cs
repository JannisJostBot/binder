using System.Diagnostics;
using System.Numerics;
using Binder.Library;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Binder.Views;

/// <summary>
/// What a Year in review card plays when it comes in (spec §28, board 41): each element's cue, when it starts and,
/// for the intro's fan, where it comes from. Filled by <see cref="YearReviewCards"/>, played once by
/// <see cref="YearReviewMotion"/>.
/// </summary>
internal sealed class CardMotion
{
    public sealed record Entry(UIElement Element, Cue Cue, int At, Vector2 From, float Degrees, bool Horizontal);

    public List<Entry> Entries { get; } = [];
    public List<Counter> Counters { get; } = [];

    public void Add(UIElement element, Cue cue, int at, Vector2 from = default, float degrees = 0, bool horizontal = false) =>
        Entries.Add(new Entry(element, cue, at, from, degrees, horizontal));
}

/// <summary>
/// A number that counts up (board 41's card 2, 3 and 6) with a light motion blur: two faint copies above and below
/// that spread with the count's speed and close as it lands. Not a blur effect (no Win2D): two more TextBlocks.
/// Narrator reads the final value from the start; the counting text is hidden from it.
/// </summary>
internal sealed class Counter(TextBlock main, TextBlock above, TextBlock below, double value, int at, double fontSize)
{
    public TextBlock Main => main;
    public double Value => value;
    public int At => at;

    /// <summary>The count at <paramref name="progress"/> (0–1, eased out), with the copies placed for its speed.</summary>
    public void Show(double progress)
    {
        double p = Math.Clamp(progress, 0, 1);
        double eased = 1 - Math.Pow(1 - p, 3);
        main.Text = Math.Round(value * eased).ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        above.Text = below.Text = main.Text;
        double speed = p is > 0 and < 1 ? Math.Pow(1 - p, 2) : 0; // the ease's slope: 1 at the start, 0 when it lands
        float d = (float)(speed * fontSize * 0.07);
        bool trail = d >= 0.5;
        above.Visibility = below.Visibility = trail ? Visibility.Visible : Visibility.Collapsed;
        if (!trail) return;
        above.Translation = new Vector3(0, -d, 0);
        below.Translation = new Vector3(0, d, 0);
        above.Opacity = below.Opacity = 0.3 * speed + 0.05;
    }
}

/// <summary>
/// Year in review's motion (spec §28; the brief's exception: only when the user moves to a card, at most about a
/// second, all Composition: offset, scale, rotation, opacity and an inset clip; no blur, no Win2D). Animation effects
/// off or high contrast: cards switch at once and show their final state, nothing is started. The count-up is the one
/// per-frame piece: a Rendering handler that exists only while a number counts.
/// </summary>
internal sealed class YearReviewMotion
{
    private readonly Compositor _compositor;
    private readonly float _slow;
    private readonly CompositionEasingFunction _gentle, _lively, _out;
    private readonly Stopwatch _clock = new();
    private List<(Counter Counter, int Start)>? _counting;

    public YearReviewMotion(UIElement host, float slow)
    {
        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _slow = Math.Max(1, slow);
        _gentle = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1));
        _lively = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.05f, 0.7f), new Vector2(0.1f, 1));
        _out = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0), new Vector2(1, 0.5f));
    }

    /// <summary>Whether anything moves: Windows' Animation effects on, and not high contrast.</summary>
    public static bool Enabled =>
        new Windows.UI.ViewManagement.UISettings().AnimationsEnabled && !new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;

    /// <summary>The old card slides 40 px toward where the user is going and fades; <paramref name="done"/> removes it.</summary>
    public void Out(UIElement card, bool forward, Action done)
    {
        var visual = Prepare(card);
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", Scalar(1, 0, YearMotion.CardOutMs, 0, _out));
        visual.StartAnimation("Translation", Vector(Vector2.Zero, new Vector2(forward ? -40 : 40, 0), YearMotion.CardOutMs, 0, _out));
        batch.End();
        batch.Completed += (_, _) => done();
    }

    /// <summary>The new card slides in from the side the user is going to, then its blocks come in, each once.</summary>
    public void In(UIElement card, CardMotion motion, bool forward, int delay, MotionKind kind)
    {
        var ease = kind == MotionKind.Lively ? _lively : _gentle;
        var visual = Prepare(card);
        visual.StartAnimation("Opacity", Scalar(0, 1, YearMotion.CardInMs, delay, ease));
        visual.StartAnimation("Translation", Vector(new Vector2(forward ? 40 : -40, 0), Vector2.Zero, YearMotion.CardInMs, delay, ease));
        foreach (var entry in motion.Entries) Start(entry, delay, ease);
        StartCounting(motion.Counters, delay);
    }

    /// <summary>Everything at its final state at once (Animation effects off, high contrast, or a resize).</summary>
    public static void Still(CardMotion motion)
    {
        foreach (var counter in motion.Counters) counter.Show(1);
    }

    /// <summary>Stops the count-up (the page closed or the card changed); numbers jump to their values.</summary>
    public void Stop()
    {
        if (_counting is null) return;
        CompositionTarget.Rendering -= OnRendering;
        foreach (var (counter, _) in _counting) counter.Show(1);
        _counting = null;
    }

    private void Start(CardMotion.Entry entry, int delay, CompositionEasingFunction ease)
    {
        var visual = Prepare(entry.Element);
        int at = delay + entry.At;
        int ms = YearMotion.Duration(entry.Cue);
        var size = entry.Element.ActualSize;
        switch (entry.Cue)
        {
            case Cue.Rise:
                visual.StartAnimation("Opacity", Scalar(0, 1, ms, at, ease));
                visual.StartAnimation("Translation", Vector(new Vector2(0, 16), Vector2.Zero, ms, at, ease));
                break;
            case Cue.Up:
                visual.StartAnimation("Opacity", Scalar(0, 1, ms, at, ease));
                visual.StartAnimation("Translation", Vector(new Vector2(0, 28), Vector2.Zero, ms, at, ease));
                break;
            case Cue.Fade:
                visual.StartAnimation("Opacity", Scalar(0, 1, ms, at, ease));
                break;
            case Cue.Pop:
                visual.CenterPoint = new Vector3(size.X / 2, size.Y / 2, 0);
                visual.StartAnimation("Opacity", Scalar(0, 1, ms, at, ease));
                visual.StartAnimation("Scale", Vector3(new Vector3(0.6f, 0.6f, 1), Vector3One, ms, at, ease));
                break;
            case Cue.Grow:
                // Bars grow from their baseline: up from the bottom, or to the right from the left edge.
                visual.CenterPoint = entry.Horizontal ? new Vector3(0, size.Y / 2, 0) : new Vector3(size.X / 2, size.Y, 0);
                var from = entry.Horizontal ? new Vector3(0, 1, 1) : new Vector3(1, 0, 1);
                visual.StartAnimation("Scale", Vector3(from, Vector3One, ms, at, ease));
                break;
            case Cue.Wipe:
                var clip = _compositor.CreateInsetClip();
                visual.Clip = clip;
                clip.StartAnimation("RightInset", Scalar(size.X, 0, ms, at, ease));
                break;
            case Cue.Fan:
                // From the stack in the middle (the logo's covers) out to its place, turning straight on the way.
                visual.CenterPoint = new Vector3(size.X / 2, size.Y / 2, 0);
                visual.StartAnimation("Translation", Vector(entry.From, Vector2.Zero, ms, at, ease));
                visual.StartAnimation("RotationAngleInDegrees", Scalar(entry.Degrees, 0, ms, at, ease));
                visual.StartAnimation("Scale", Vector3(new Vector3(0.92f, 0.92f, 1), Vector3One, ms, at, ease));
                break;
            case Cue.Drop:
                visual.CenterPoint = new Vector3(size.X / 2, size.Y / 2, 0);
                visual.StartAnimation("Opacity", Scalar(0, 1, ms, at, ease));
                visual.StartAnimation("Translation", Vector(new Vector2(0, -28), Vector2.Zero, ms, at, ease));
                visual.StartAnimation("RotationAngleInDegrees", Scalar(entry.Degrees == 0 ? -5 : entry.Degrees, 0, ms, at, ease));
                break;
        }
    }

    private void StartCounting(List<Counter> counters, int delay)
    {
        Stop();
        if (counters.Count == 0) return;
        foreach (var counter in counters) counter.Show(0);
        _counting = [.. counters.Select(c => (c, delay + c.At))];
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        if (_counting is null) return;
        double elapsed = _clock.Elapsed.TotalMilliseconds;
        bool running = false;
        foreach (var (counter, start) in _counting)
        {
            double p = (elapsed - start * _slow) / (YearMotion.CountMs * _slow);
            counter.Show(p);
            running |= p < 1;
        }
        if (running) return;
        CompositionTarget.Rendering -= OnRendering; // landed: nothing runs per frame any more
        _counting = null;
    }

    private static readonly Vector3 Vector3One = new(1, 1, 1);

    private static Visual Prepare(UIElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        return ElementCompositionPreview.GetElementVisual(element);
    }

    private ScalarKeyFrameAnimation Scalar(float from, float to, int ms, int delay, CompositionEasingFunction ease)
    {
        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, from);
        animation.InsertKeyFrame(1, to, ease);
        Time(animation, ms, delay);
        return animation;
    }

    private Vector3KeyFrameAnimation Vector(Vector2 from, Vector2 to, int ms, int delay, CompositionEasingFunction ease) =>
        Vector3(new Vector3(from, 0), new Vector3(to, 0), ms, delay, ease);

    private Vector3KeyFrameAnimation Vector3(Vector3 from, Vector3 to, int ms, int delay, CompositionEasingFunction ease)
    {
        var animation = _compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0, from);
        animation.InsertKeyFrame(1, to, ease);
        Time(animation, ms, delay);
        return animation;
    }

    private void Time(KeyFrameAnimation animation, int ms, int delay)
    {
        animation.Duration = TimeSpan.FromMilliseconds(Math.Max(1, ms) * _slow);
        animation.DelayTime = TimeSpan.FromMilliseconds(delay * _slow);
        // The start value holds during the delay, so nothing shows at its end state first.
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
    }
}
