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
    private const int MarginAboveTaskbar = 24;

    private readonly IntPtr _hwnd;
    private readonly TimeSpan? _timeout;
    private DispatcherQueueTimer? _timer;
    private readonly FrameworkElement _announce;

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

        bool applied = kind == Kind.Applied;
        // A restore that missed something waits for the user; an apply that did is only a heads-up.
        _timeout = result.Success ? SuccessTimeout : applied ? PartialTimeout : null;
        if (result.Success)
        {
            string changes = result.Restored == 1 ? "1 change" : $"{result.Restored} changes";
            var (title, detail) = applied
                ? ("Game mode on", result.Restored == 0 ? "already set up" : changes)
                : ("Back to normal", changes + " put back");
            LineTitle.Text = title;
            LineDetail.Text = " · " + detail;
            AutomationProperties.SetName(LineText, applied
                ? (result.Restored == 0 ? "Game mode is on. Windows was already set up for gaming" : $"Game mode is on. {changes}, put back when the game closes")
                : $"Windows is back to normal. Game mode put back {changes}");
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
            var (title, detail) = result.ToastText(applied);
            bool hasLog = GameModeService.HasLog;
            TitleText.Text = title;
            DetailText.Text = detail + (hasLog ? ". The log says what happened." : ".");
            AutomationProperties.SetName(TitleText, title + ". " + DetailText.Text);
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
        int height = (int)Math.Ceiling(Root.ActualHeight * scale);
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
            Body.Opacity = 0;
            DotScale.ScaleX = DotScale.ScaleY = 0.4;
            Mark.StrokeDashOffset = 8;
        }
        NativeWindow.Cloak(_hwnd, false);
        if (animate) ShowStoryboard.Begin();

        // Screen readers announce it like Windows' own notifications.
        FrameworkElementAutomationPeer.CreatePeerForElement(_announce)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

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
