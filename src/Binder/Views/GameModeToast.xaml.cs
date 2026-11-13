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

    public GameModeToast(Kind kind, RestoreResult result)
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
            _announce = LineText;
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
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            Surface.BorderThickness = new Thickness(2); // system colors, a stronger edge and no shadow
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
