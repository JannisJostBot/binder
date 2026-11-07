using GameShelf.Services;
using GameShelf.Shared;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>
/// Game mode's toast (board 4), shown without taking focus, centered above the taskbar of the main screen:
/// "Game mode is on" once it applied (while the widget is still up), "Windows is back to normal" once it put
/// everything back. A restore that missed something stays until dismissed and offers Details. The badge pops in
/// and draws its checkmark once; its one timer exists only while a message that closes by itself is up.
/// </summary>
public sealed partial class GameModeToast : Window
{
    public enum Kind { Applied, Restored }

    private static readonly TimeSpan SuccessTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan PartialTimeout = TimeSpan.FromSeconds(6);
    private const int Height = 64;
    private const int MarginAboveTaskbar = 24;

    private readonly IntPtr _hwnd;
    private readonly TimeSpan? _timeout;
    private DispatcherQueueTimer? _timer;

    public GameModeToast(Kind kind, RestoreResult result)
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        NativeWindow.MakeToolWindow(_hwnd);
        NativeWindow.TryApplyRoundedCorners(_hwnd);
        if (PanelBackdrop.IsSupported) SystemBackdrop = new PanelBackdrop();
        else Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

        var (title, detail) = result.ToastText(applied: kind == Kind.Applied);
        TitleText.Text = title;
        DetailText.Text = detail;
        AutomationProperties.SetName(TitleText, title + ". " + detail);
        ToolTipService.SetToolTip(Body, detail); // the detail may be cut short

        // A restore that missed something waits for the user; an apply that did is only a heads-up.
        _timeout = result.Success ? SuccessTimeout : kind == Kind.Applied ? PartialTimeout : null;
        if (!result.Success)
        {
            BadgeFill.Fill = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            Mark.Visibility = Visibility.Collapsed;
            Warning.Visibility = Visibility.Visible;
            DetailsButton.Visibility = GameModeService.HasLog ? Visibility.Visible : Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
        }
        Root.SizeChanged += OnFirstLayout;
        Closed += (_, _) =>
        {
            _timer?.Stop();
            ShowStoryboard.Stop();
        };
    }

    /// <summary>Shows it without activating it (the user may be in another app, or the game starting).</summary>
    public void ShowQuietly()
    {
        // Laid out off to the side, hidden, then sized to its content and moved into place (OnFirstLayout).
        var work = Monitors.Find(null).WorkArea;
        NativeWindow.SetBounds(_hwnd, new PixelRect(work.Left, work.Top, work.Left + 900, work.Top + 120));
        NativeWindow.Cloak(_hwnd, true);
        AppWindow.Show(activateWindow: false);
    }

    private void OnFirstLayout(object sender, SizeChangedEventArgs e)
    {
        if (Root.ActualWidth <= 0 || Root.XamlRoot is not { } xamlRoot) return;
        Root.SizeChanged -= OnFirstLayout;

        double scale = xamlRoot.RasterizationScale;
        int width = (int)Math.Ceiling(Root.ActualWidth * scale);
        int height = (int)Math.Ceiling(Height * scale);
        // Above the taskbar, also an auto-hide one (it reserves no work area but slides up over the bottom).
        var monitor = Monitors.Find(null);
        var work = monitor.WorkArea;
        int bottom = work.Bottom - Monitors.AutoHideTaskbars(monitor).Bottom;
        int left = work.Left + (work.Width - width) / 2;
        int top = bottom - height - (int)Math.Round(MarginAboveTaskbar * scale);
        NativeWindow.SetBounds(_hwnd, new PixelRect(left, top, left + width, top + height));

        // The storyboard's first frame (From values) is in place before the window shows; with animations off
        // the XAML's values are already the final state.
        bool animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        if (animate)
        {
            Badge.Opacity = 0;
            Body.Opacity = 0;
            Mark.StrokeDashOffset = 9;
        }
        NativeWindow.Cloak(_hwnd, false);
        if (animate) ShowStoryboard.Begin();

        // Screen readers announce it like Windows' own notifications.
        FrameworkElementAutomationPeer.CreatePeerForElement(TitleText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

        if (_timeout is not { } timeout) return;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = timeout;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Close();
        _timer.Start();
    }

    private void OnDetailsClick(object sender, RoutedEventArgs e)
    {
        GameModeService.OpenLog();
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
