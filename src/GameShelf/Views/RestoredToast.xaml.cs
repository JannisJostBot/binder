using GameShelf.Services;
using GameShelf.Shared;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>
/// "Restored successfully" (gone after 3 s) or "Restored 4 of 5 · … couldn't be put back" with Details (stays
/// until dismissed), after Game mode put Windows back (board 4). Shown without taking focus, centered above
/// the taskbar of the main screen. Its one timer exists only while a success message is up.
/// </summary>
public sealed partial class RestoredToast : Window
{
    private static readonly TimeSpan SuccessTimeout = TimeSpan.FromSeconds(3);
    private const int Height = 52;
    private const int MarginAboveTaskbar = 24;

    private readonly IntPtr _hwnd;
    private readonly bool _success;
    private DispatcherQueueTimer? _timer;

    public RestoredToast(RestoreResult result)
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _success = result.Success;

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

        Message.Text = result.Summary();
        if (!_success)
        {
            Icon.Glyph = ""; // warning
            Icon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            DetailsButton.Visibility = GameModeService.HasLog ? Visibility.Visible : Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
        }
        Root.SizeChanged += OnFirstLayout;
        Closed += (_, _) => _timer?.Stop();
    }

    /// <summary>Shows it without activating it (the user may be back in another app already).</summary>
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
        NativeWindow.Cloak(_hwnd, false);

        // Screen readers announce it like Windows' own notifications.
        FrameworkElementAutomationPeer.CreatePeerForElement(Message)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

        if (!_success) return;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = SuccessTimeout;
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
