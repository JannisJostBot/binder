using Binder.Services;
using System.Numerics;
using Binder.Shared;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Binder.ViewModels;

namespace Binder.Views;

/// <summary>
/// Game mode's toast (board 23), shown without taking focus, centered above the taskbar of the main screen: a
/// one-line pill, "Game mode on · 5 changes" once it applied (while the widget is still up) and "Back to normal ·
/// 5 changes put back" once it put everything back. Something that couldn't be changed or put back gets a card
/// with the names and the log; after a restore it stays until dismissed. The dot pops in and draws its check
/// once; the one timer exists only while a message that closes by itself is up.
/// </summary>
public sealed partial class GameModeToast : Window
{
    public enum Kind { Applied, Restored }

    private static readonly TimeSpan SuccessTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan PartialTimeout = TimeSpan.FromSeconds(6);

    private readonly IntPtr _hwnd;
    private readonly TimeSpan? _timeout;
    private DispatcherQueueTimer? _timer;
    private readonly FrameworkElement _announce;
    private readonly FrameworkElement _dot;
    private readonly RestartViewModel? _restart;
    private Storyboard? _restartBar;

    // Test branch (ToastMelt): the changes fall onto the pill and melt in, the count rolling up as each lands.
    private const double MeltRoom = 136; // room above the pill for the drops (and, going back, their rings)
    private readonly MeltMode _melt;
    private readonly float _slow = 1;
    private readonly int _meltCount;
    private readonly bool _applied;
    private ToastMelt? _melting;
    private Storyboard? _drawCheck;
    private bool _highContrast;

    /// <summary>A message that waits for the user (something couldn't be put back): nothing should replace it.</summary>
    public bool WaitsForUser => _timeout is null && _restart is null;

    /// <summary>The restart pill after a crash (spec §17); it counts down once shown, and its owner closes it.</summary>
    public GameModeToast(RestartViewModel restart)
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetUpWindow();
        _restart = restart;
        Pill.Visibility = Visibility.Collapsed;
        RestartPill.Visibility = Visibility.Visible;
        RestartTitle.Text = restart.Title;
        RestartCancel.Command = restart.CancelCommand;
        UpdateRestart();
        restart.PropertyChanged += OnRestartChanged;
        Root.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Escape && e.Key != Windows.System.VirtualKey.GamepadB) return;
            e.Handled = true;
            restart.Cancel();
        };
        _announce = RestartText;
        _dot = RestartDot;
        Root.SizeChanged += OnFirstLayout;
        Closed += (_, _) =>
        {
            restart.PropertyChanged -= OnRestartChanged;
            _timer?.Stop();
            _restartBar?.Stop();
            ShowStoryboard.Stop();
        };
    }

    private void OnRestartChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RestartViewModel.Remaining)) UpdateRestart();
    }

    private void UpdateRestart()
    {
        RestartDetail.Text = " · " + _restart!.Detail;
        AutomationProperties.SetName(RestartText, _restart.Name);
        if (_restartBar is null) RestartScale.ScaleX = _restart.Progress; // animations off: a step a second
    }

    /// <summary>
    /// Starts the restart countdown: a tick a second for the text, and the bar draining in one smooth, linear move
    /// (compositor-run; with animations off it steps with the text instead).
    /// </summary>
    private void StartRestartCountdown(bool animate)
    {
        var restart = _restart!;
        if (animate)
        {
            var drain = new DoubleAnimation { From = restart.Progress, To = 0, Duration = TimeSpan.FromSeconds(restart.Remaining) };
            Storyboard.SetTarget(drain, RestartScale);
            Storyboard.SetTargetProperty(drain, "ScaleX");
            _restartBar = new Storyboard();
            _restartBar.Children.Add(drain);
            _restartBar.Begin();
        }
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            restart.Tick();
            if (restart.Ended) _timer?.Stop();
        };
        _timer.Start();
    }

    public GameModeToast(Kind kind, RestoreResult result, MeltMode melt = MeltMode.Off, float slow = 1)
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetUpWindow();
        _dot = Dot;

        bool applied = kind == Kind.Applied;
        // A restore that missed something waits for the user; an apply that did is only a heads-up.
        _timeout = result.Success ? SuccessTimeout : applied ? PartialTimeout : null;
        if (result.Success)
        {
            string changes = Strings.Plural(result.Restored, Strings.GmChangeCountOne, Strings.GmChangeCount);
            var (title, detail) = applied
                ? (Strings.GmPillOn, result.Restored == 0 ? Strings.GmPillAlreadySet : changes)
                : (Strings.GmPillBack, Strings.Format(Strings.GmPillPutBack, changes));
            LineTitle.Text = title;
            LineDetail.Text = " · " + detail;
            var (longTitle, longDetail) = GameModeText.Toast(result, applied);
            AutomationProperties.SetName(LineText, Strings.Format(Strings.Sentences, longTitle, longDetail));
            _applied = applied;
            if (melt != MeltMode.Off && result.Restored > 0 && !_highContrast && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            {
                _melt = melt;
                _slow = slow;
                _meltCount = result.Restored;
                LineText.Visibility = Visibility.Collapsed;
                MeltLine.Visibility = Visibility.Visible;
                MeltTitle.Text = title;
                MeltSep.Text = "·";
                MeltDetail.Text = detail; // laid out with the final count, so the pill keeps its width
                AutomationProperties.SetName(MeltLine, AutomationProperties.GetName(LineText));
                if (applied)
                {
                    OnGlyph.Visibility = Visibility.Collapsed;
                    Gauge.Visibility = Visibility.Visible;
                }
                Root.Padding = new Thickness(Root.Padding.Left, Root.Padding.Top + MeltRoom, Root.Padding.Right, Root.Padding.Bottom);
            }
            if (!applied)
            {
                // Done: a quiet neutral dot with a green check, not the accent.
                var resources = Application.Current.Resources;
                OnGlyph.Visibility = Visibility.Collapsed;
                Mark.Visibility = Visibility.Visible;
                DotFill.Fill = (Brush)resources["ControlFillColorSecondaryBrush"];
                DotFill.Stroke = (Brush)resources["ControlStrokeColorDefaultBrush"];
                Mark.Stroke = (Brush)resources["SystemFillColorSuccessBrush"];
            }
            _announce = _melt != MeltMode.Off ? MeltLine : LineText;
        }
        else
        {
            var (title, detail) = GameModeText.Toast(result, applied);
            bool hasLog = GameModeService.HasLog;
            TitleText.Text = title;
            DetailText.Text = Strings.Format(hasLog ? Strings.GmDetailWithLog : Strings.Sentence, detail);
            AutomationProperties.SetName(TitleText, Strings.Format(Strings.Sentences, title, DetailText.Text));
            DetailsButton.Visibility = hasLog ? Visibility.Visible : Visibility.Collapsed;
            Pill.Visibility = Visibility.Collapsed;
            Card.Visibility = Visibility.Visible;
            _announce = TitleText;
        }
        Root.SizeChanged += OnFirstLayout;
        Closed += (_, _) =>
        {
            _timer?.Stop();
            ShowStoryboard.Stop();
            _drawCheck?.Stop();
            CompositionTarget.Rendering -= StartMeltOnFirstFrame;
            _melting?.Remove();
        };
    }

    private void SetUpWindow()
    {
        // The context-menu kind is a plain popup: no frame at all (the borderless overlapped one keeps a thin
        // dialog frame that WinUI puts back whenever it's taken away).
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        NativeWindow.MakeToolWindow(_hwnd);
        // See-through and frameless: the XAML surface draws the whole pill, its round ends and its shadow.
        NativeWindow.MakeFrameless(_hwnd);
        SystemBackdrop = new TransparentBackdrop();
        _highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        if (_highContrast)
        {
            Surface.BorderThickness = new Thickness(2); // system colors, a stronger edge and no shadow
            Inner.Margin = new Thickness(2);
        }
        else
            Surface.SizeChanged += (_, _) => UpdateShadow();
    }

    /// <summary>Shows it without activating it (the user may be in another app, or the game starting).</summary>
    public void ShowQuietly()
    {
        // Laid out off to the side, hidden, then sized to its content and moved into place (OnFirstLayout).
        var work = Monitors.Find(null).WorkArea;
        NativeWindow.SetBounds(_hwnd, new PixelRect(work.Left, work.Top, work.Left + 1200, work.Top + 600)); // room for the card and its shadow
        NativeWindow.Cloak(_hwnd, true);
        AppWindow.Show(activateWindow: false);
    }

    private void OnFirstLayout(object sender, SizeChangedEventArgs e)
    {
        if (Root.ActualWidth <= 0 || Root.XamlRoot is not { } xamlRoot) return;
        Root.SizeChanged -= OnFirstLayout;

        double scale = xamlRoot.RasterizationScale;
        int width = (int)Math.Ceiling(Root.ActualWidth * scale);
        int height = (int)Math.Ceiling(Root.ActualHeight * scale);
        // Above the taskbar, also an auto-hide one (it reserves no work area but slides up over the bottom).
        var monitor = Monitors.Find(null);
        var work = monitor.WorkArea;
        int bottom = work.Bottom - Monitors.AutoHideTaskbars(monitor).Bottom;
        int left = work.Left + (work.Width - width) / 2;
        // The window's bottom edge sits on the taskbar: the shadow room below the pill (Root's padding) is the gap.
        int top = bottom - height;
        NativeWindow.SetBounds(_hwnd, new PixelRect(left, top, left + width, top + height));

        if (_melt != MeltMode.Off)
        {
            PlayMelt();
            return;
        }

        // The storyboard's first frame (From values) is in place before the window shows; with animations off
        // the XAML's values are already the final state.
        bool animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        if (animate)
        {
            Body.Opacity = 0;
            Mark.StrokeDashOffset = 8;
            OnGlyph.Glyph = ""; // the gauge starts at "off"
        }
        if (animate) PopDot();
        NativeWindow.Cloak(_hwnd, false);
        if (animate) ShowStoryboard.Begin();

        // Screen readers announce it like Windows' own notifications.
        FrameworkElementAutomationPeer.CreatePeerForElement(_announce)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

        if (_restart is not null)
        {
            StartRestartCountdown(animate);
            return;
        }

        if (_timeout is not { } timeout) return;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = timeout;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Close();
        _timer.Start();
    }

    /// <summary>
    /// The dot pops in from small with a slight overshoot. Done on the compositor, not with a XAML ScaleTransform:
    /// XAML would rasterize the dot at its small starting size and stretch that up (pixelated); here it's drawn
    /// at full size and only shrunk. Runs once; nothing is left running.
    /// </summary>
    private void PopDot()
    {
        var visual = ElementCompositionPreview.GetElementVisual(_dot);
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3((float)_dot.Width / 2, (float)_dot.Height / 2, 0);
        var pop = compositor.CreateVector3KeyFrameAnimation();
        pop.InsertKeyFrame(0, new Vector3(0.2f, 0.2f, 1));
        pop.InsertKeyFrame(1, Vector3.One, compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.5f), new Vector2(0.64f, 1)));
        pop.Duration = TimeSpan.FromMilliseconds(400);
        pop.DelayTime = TimeSpan.FromMilliseconds(100);
        pop.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Scale", pop);
    }

    // ---- Test branch: the changes melt in (ToastMelt, canvas board 33) --------------------------------------

    private static Visual VisualOf(UIElement element, bool translation = false)
    {
        if (translation) ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        return ElementCompositionPreview.GetElementVisual(element);
    }

    private CompositionEasingFunction Ease(Compositor c, float x1, float y1, float x2, float y2) =>
        c.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2));

    private float Ms(float ms) => ms * _slow;

    /// <summary>
    /// Builds the melt in its first frame (the pill drawn by the melt, the drops hidden, the line's count hidden),
    /// shows the window and starts everything once that frame is on screen.
    /// </summary>
    private void PlayMelt()
    {
        MeltDetail.MinWidth = MeltDetail.ActualWidth; // keeps the final width while the count rolls up
        var glow = _applied ? ((SolidColorBrush)DotFill.Fill).Color : ((SolidColorBrush)Mark.Stroke).Color;
        _melting = new ToastMelt(MeltHost, GlyphHost, _melt, back: !_applied, _slow, Math.Min(_meltCount, 5),
            new Vector2((float)Surface.ActualWidth, (float)Surface.ActualHeight),
            ((SolidColorBrush)Surface.Background).Color, ((SolidColorBrush)Surface.BorderBrush).Color, glow,
            (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]);
        _melting.Build();
        _melting.Landed += OnMeltLanded;
        _melting.Finished += () => Surface.Opacity = 1;
        Surface.Opacity = 0; // the melt draws the pill until the last drop has merged

        var body = VisualOf(Body, translation: true);
        body.Opacity = 0;
        body.Properties.InsertVector3("Translation", new Vector3(0, 96, 0));
        VisualOf(MeltSep).Opacity = 0;
        VisualOf(MeltDetail, translation: true).Opacity = 0;
        var dot = VisualOf(Dot);
        dot.CenterPoint = new Vector3(14, 14, 0);
        dot.Scale = new Vector3(0.6f, 0.6f, 1);
        if (_applied)
        {
            var needle = VisualOf(Needle);
            needle.CenterPoint = new Vector3(12, 17, 0);
            needle.RotationAngleInDegrees = -90;
        }
        else Mark.StrokeDashOffset = 8;

        NativeWindow.Cloak(_hwnd, false);
        FrameworkElementAutomationPeer.CreatePeerForElement(_announce)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        CompositionTarget.Rendering += StartMeltOnFirstFrame;
    }

    private void StartMeltOnFirstFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= StartMeltOnFirstFrame;
        if (_melting is not { } melt) return;
        var body = VisualOf(Body);
        var c = body.Compositor;
        // Coming in, the pill answers each landing; going back, the changes arrive above it, so it stays still.
        float[] lands = _applied ? melt.Lands : [];
        float last = melt.Settled;

        // Up from the taskbar (board 31, C), then a small give under each drop as it lands.
        var rise = new List<(float, float, CompositionEasingFunction?)>();
        Add(rise, 0, 96, null);
        Add(rise, Ms(367), 0, Ease(c, .1f, .9f, .2f, 1));
        for (int i = 0; i < lands.Length; i++)
        {
            float next = i + 1 < lands.Length ? lands[i + 1] : float.MaxValue;
            Add(rise, lands[i], 0, null);
            Add(rise, lands[i] + Ms(90), 2, Ease(c, 0, 0, .58f, 1));
            Add(rise, Math.Min(lands[i] + Ms(360), next), 0, Ease(c, .3f, 1.72f, .5f, 1));
        }
        body.StartAnimation("Translation", Timeline(c, rise, v => new Vector3(0, v, 0)));
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(Ms(100));
        body.StartAnimation("Opacity", fade);

        // The dot settles, then bumps as each drop lands.
        var bump = new List<(float, float, CompositionEasingFunction?)>();
        Add(bump, 0, 0.6f, null);
        Add(bump, Ms(150), 0.6f, null);
        Add(bump, Ms(510), 1, Ease(c, .34f, 1.56f, .64f, 1));
        for (int i = 0; i < lands.Length; i++)
        {
            float next = i + 1 < lands.Length ? lands[i + 1] : float.MaxValue;
            Add(bump, lands[i], 1, null);
            Add(bump, lands[i] + Ms(100), 1.18f, Ease(c, .2f, .8f, .4f, 1));
            Add(bump, Math.Min(lands[i] + Ms(420), next), 1, Ease(c, .34f, 1.84f, .64f, 1));
        }
        VisualOf(Dot).StartAnimation("Scale", Timeline(c, bump, v => new Vector3(v, v, 1)));

        // Once the last change is in: the needle sweeps up, or the check draws itself.
        if (_applied)
        {
            var sweep = c.CreateScalarKeyFrameAnimation();
            sweep.InsertKeyFrame(0, -90);
            sweep.InsertKeyFrame(1, 40, Ease(c, .34f, 1.56f, .64f, 1));
            sweep.Duration = TimeSpan.FromMilliseconds(Ms(700));
            sweep.DelayTime = TimeSpan.FromMilliseconds(last);
            VisualOf(Needle).StartAnimation("RotationAngleInDegrees", sweep);
        }
        else
        {
            var draw = new DoubleAnimation
            {
                From = 8, To = 0, EnableDependentAnimation = true,
                Duration = TimeSpan.FromMilliseconds(Ms(400)),
                BeginTime = TimeSpan.FromMilliseconds(last),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(draw, Mark);
            Storyboard.SetTargetProperty(draw, "StrokeDashOffset");
            _drawCheck = new Storyboard();
            _drawCheck.Children.Add(draw);
            _drawCheck.Begin();
        }

        melt.Start();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(last) + SuccessTimeout;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => LeaveAndClose();
        _timer.Start();
    }

    /// <summary>A keyframe, unless it would come before the one already there (landings close together).</summary>
    private static void Add(List<(float Ms, float Value, CompositionEasingFunction? Ease)> points, float ms, float value, CompositionEasingFunction? ease)
    {
        if (points.Count == 0 || ms > points[^1].Ms + 1) points.Add((ms, value, ease));
    }

    private static Vector3KeyFrameAnimation Timeline(Compositor c, List<(float Ms, float Value, CompositionEasingFunction? Ease)> points, Func<float, Vector3> map)
    {
        var a = c.CreateVector3KeyFrameAnimation();
        float end = Math.Max(1, points[^1].Ms);
        foreach (var (ms, value, ease) in points)
        {
            if (ease is null) a.InsertKeyFrame(ms / end, map(value));
            else a.InsertKeyFrame(ms / end, map(value), ease);
        }
        a.Duration = TimeSpan.FromMilliseconds(end);
        return a;
    }

    /// <summary>A change landed: the count rolls up to its share of the total.</summary>
    private void OnMeltLanded(int index, int of)
    {
        int shown = (int)Math.Round(_meltCount * (index + 1) / (double)of);
        string changes = Strings.Plural(shown, Strings.GmChangeCountOne, Strings.GmChangeCount);
        MeltDetail.Text = _applied ? changes : Strings.Format(Strings.GmPillPutBack, changes);

        var detail = VisualOf(MeltDetail);
        var c = detail.Compositor;
        var roll = c.CreateVector3KeyFrameAnimation();
        roll.InsertKeyFrame(0, new Vector3(0, 9, 0));
        roll.InsertKeyFrame(1, Vector3.Zero, Ease(c, .3f, 1.5f, .5f, 1));
        roll.Duration = TimeSpan.FromMilliseconds(Ms(280));
        detail.StartAnimation("Translation", roll);
        var show = c.CreateScalarKeyFrameAnimation();
        show.InsertKeyFrame(0, 0);
        show.InsertKeyFrame(1, 1, Ease(c, .3f, 1.5f, .5f, 1));
        show.Duration = TimeSpan.FromMilliseconds(Ms(280));
        detail.StartAnimation("Opacity", show);
        if (index == 0)
        {
            var sep = c.CreateScalarKeyFrameAnimation();
            sep.InsertKeyFrame(1, 1);
            sep.Duration = TimeSpan.FromMilliseconds(Ms(200));
            VisualOf(MeltSep).StartAnimation("Opacity", sep);
        }
    }

    /// <summary>Back down behind the taskbar, then closed.</summary>
    private void LeaveAndClose()
    {
        var body = VisualOf(Body);
        var c = body.Compositor;
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var down = c.CreateVector3KeyFrameAnimation();
        down.InsertKeyFrame(1, new Vector3(0, 96, 0), Ease(c, .7f, 0, 1, .5f));
        down.Duration = TimeSpan.FromMilliseconds(Ms(200));
        body.StartAnimation("Translation", down);
        batch.End();
        batch.Completed += (_, _) => Close();
    }

    private LayerVisual? _shadowLayer;
    private ShapeVisual? _shadowShape;
    private CompositionRoundedRectangleGeometry? _shadowGeometry;

    /// <summary>
    /// Board 23's soft shadow (0 8 24, 40 % black): a drop shadow cast by an opaque rounded rectangle the shape of
    /// the surface, hidden behind it. Built once; only resized if the surface's size changes.
    /// </summary>
    private void UpdateShadow()
    {
        var size = new Vector2((float)Surface.ActualWidth, (float)Surface.ActualHeight);
        if (size.X <= 0) return;
        if (_shadowLayer is null)
        {
            var compositor = ElementCompositionPreview.GetElementVisual(ShadowHost).Compositor;
            _shadowGeometry = compositor.CreateRoundedRectangleGeometry();
            _shadowGeometry.CornerRadius = new Vector2(24);
            var shape = compositor.CreateSpriteShape(_shadowGeometry);
            shape.FillBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
            _shadowShape = compositor.CreateShapeVisual();
            _shadowShape.Shapes.Add(shape);
            var shadow = compositor.CreateDropShadow();
            shadow.BlurRadius = 24;
            shadow.Offset = new Vector3(0, 8, 0);
            shadow.Color = Windows.UI.Color.FromArgb(102, 0, 0, 0);
            _shadowLayer = compositor.CreateLayerVisual();
            _shadowLayer.Shadow = shadow;
            _shadowLayer.Children.InsertAtTop(_shadowShape);
            ElementCompositionPreview.SetElementChildVisual(ShadowHost, _shadowLayer);
        }
        _shadowGeometry!.Size = size;
        _shadowShape!.Size = size;
        _shadowLayer.Size = size;
    }

    private void OnDetailsClick(object sender, RoutedEventArgs e)
    {
        GameModeService.OpenLog();
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
