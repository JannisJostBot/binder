using Binder.Models;
using Binder.Services;
using Binder.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Binder;

/// <summary>The window itself: the flyout's slide, activation, closing and hiding, and where it sits on screen.</summary>
public sealed partial class MainWindow
{
    // ---- Flyout (spec §5, board 6) -----------------------------------------------------------------------

    private const double SlideDistance = 40; // effective pixels, scaled to the monitor
    private const double SlideMilliseconds = 300;
    private volatile int _slideGeneration; // bumped to stop a running slide (closing, or hiding to open again)
    private Action? _startSlide;

    /// <summary>
    /// Slides in 40 effective px over 300 ms with Fluent's entrance curve, by moving the window (not redrawing
    /// the content). Skipped when "Animation effects" is off, or when another monitor sits on that edge (it would
    /// show there mid-slide). The clock starts with the first rendered frame, so none of it happens unseen.
    /// </summary>
    private void PrepareSlide()
    {
        var target = NativeWindow.GetBounds(_hwnd);
        bool right = App.Settings.Position != DockPosition.Left;
        int distance = (int)Math.Round(SlideDistance * Monitors.FromPoint(target.Left, target.Top).Scale);
        int dx = right ? distance : -distance;
        int probeX = right ? target.Right + distance : target.Left - distance - 1;
        bool neighbor = Monitors.All().Any(m => m.Bounds.Contains(probeX, target.Top + target.Height / 2)) &&
                        !Monitors.FromPoint(target.Left, target.Top).Bounds.Contains(probeX, target.Top + target.Height / 2);
        if (!new UISettings().AnimationsEnabled || neighbor) return;

        NativeWindow.MoveTo(_hwnd, target.Left + dx, target.Top);
        OpeningGate.Hold(); // a library rescan that finishes mid-slide waits for it to end (see OpeningGate)
        var hwnd = _hwnd;
        int generation = _slideGeneration;
        _startSlide = () =>
        {
            // One step per display refresh: DwmFlush waits for the compositor's next frame (240 per second on a
            // 240 Hz screen), which a dispatcher timer can't match. The thread ends with the slide.
            var thread = new Thread(() =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (generation == _slideGeneration)
                {
                    double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / SlideMilliseconds);
                    double eased = Decelerate(t);
                    NativeWindow.MoveToAsync(hwnd, target.Left + (int)Math.Round(dx * (1 - eased)), target.Top);
                    if (t >= 1) break;
                    if (NativeWindow.WaitForCompositorFrame() != 0) Thread.Sleep(4);
                }
                _dispatcher.TryEnqueue(() =>
                {
                    OpeningGate.Release();
                    CoverQueue.ReleaseHold(); // the covers below the fold
                });
            }) { IsBackground = true, Name = "Flyout slide" };
            thread.Start();
        };
    }

    /// <summary>
    /// Fluent's entrance curve, cubic-bezier(0.1, 0.9, 0.2, 1): quick to start, then a long, soft settle, as
    /// Windows' own flyouts move. Solves the curve's x for <paramref name="t"/> (Newton, then bisection) and
    /// returns its y.
    /// </summary>
    internal static double Decelerate(double t)
    {
        const double x1 = 0.1, y1 = 0.9, x2 = 0.2, y2 = 1.0;
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        static double Bezier(double s, double a, double b) => 3 * a * s * (1 - s) * (1 - s) + 3 * b * s * s * (1 - s) + s * s * s;
        static double Slope(double s, double a, double b) => 3 * a * (1 - s) * (1 - s) + 6 * (b - a) * s * (1 - s) + 3 * (1 - b) * s * s;
        double u = t;
        for (int i = 0; i < 8; i++)
        {
            double error = Bezier(u, x1, x2) - t, slope = Slope(u, x1, x2);
            if (Math.Abs(error) < 1e-6) return Bezier(u, y1, y2);
            if (Math.Abs(slope) < 1e-6) break;
            u = Math.Clamp(u - error / slope, 0, 1);
        }
        double lo = 0, hi = 1;
        for (int i = 0; i < 30; i++)
        {
            u = (lo + hi) / 2;
            if (Bezier(u, x1, x2) < t) lo = u; else hi = u;
        }
        return Bezier(u, y1, y2);
    }

    /// <summary>Click-away closes the flyout, unless it's pinned or a dialog or menu of ours is open.</summary>
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        _active = args.WindowActivationState != WindowActivationState.Deactivated;
        if (_active) _wasActive = true;
        if (_active && !IsClosing) _pad.Activate();
        else _pad.Deactivate();
        // Windows may refuse focus to a flyout opened from the background: it's then "deactivated" from the
        // start, but nobody clicked away from it yet.
        if (_active || !_wasActive || !IsFlyout || !App.Settings.CloseOnClickAway || App.Pinned || App.ModalDepth > 0) return;
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_active && App.ModalDepth == 0 && !App.Pinned) App.Current.ClosePanel(this, byClickAway: true);
        });
    }

    /// <summary>Set once closing starts: the window is hidden first and only fully closed a moment later.</summary>
    public bool IsClosing { get; private set; }

    /// <summary>Hides the window now and closes it once the hide is on screen.</summary>
    public void CloseAnimated()
    {
        if (IsClosing) return;
        IsClosing = true;
        Closing?.Invoke();
        CloseTips();
        _pad.Deactivate();
        AppWindow.Hide();
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, Close);
    }

    /// <summary>
    /// "Open faster": hides the panel but keeps it loaded, covers and all, for <see cref="Reopen"/>. False (and
    /// nothing done) if it hasn't been on screen yet; close it instead then.
    /// </summary>
    public bool HideKept()
    {
        if (IsClosing || !_shown) return false;
        IsClosing = true;
        Closing?.Invoke();
        _slideGeneration++;
        CloseTips();
        _pad.Deactivate();
        LeavePadMode(); // it comes back like a new panel would
        AppWindow.Hide();
        _wasActive = false;
        // It comes back like a new panel would: on the grid, not on the last game or settings page.
        if (Shelf.Visibility != Visibility.Visible && !Shown(SetupSlot)) ShowShelf();
        return true;
    }

    /// <summary>Shows a panel kept by <see cref="HideKept"/>: already drawn, so it only needs placing and sliding in.</summary>
    public void Reopen()
    {
        IsClosing = false;
        PickWelcome();
        PlaceOnScreen();
        if (IsFlyout) PrepareSlide();
        Activate();
        if (_startSlide is { } slide)
        {
            _startSlide = null;
            slide();
        }
        App.Current.PanelShown(); // the update check, at most once a day (spec §25)
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!IsClosing)
        {
            IsClosing = true;
            Closing?.Invoke();
        }
        // A handler on the window's own events that reaches back to it keeps the closed window alive (WinUI's
        // Window doesn't let the GC see through that cycle), and with it its views and covers: so each one is
        // removed here, and MainWindow.xaml has no x:Bind (that adds one to Activated).
        Activated -= OnActivated;
        Closed -= OnClosed;
        App.Current.UpdateChanged -= PushUpdate;
        CloseTips();
        _slideGeneration++;
        OpeningGate.Release();
        CoverQueue.ReleaseHold();
        FrameTicks.Remove(OnFirstFrame);
        App.Current.CountdownTick -= OnCountdownTick;
        _undoTimer?.Stop();
        _pad.Dispose();
        _desktopLayer?.Dispose();
        _desktopLayer = null;
    }

    /// <summary>Opened (or toggled to) from the tray, the shortcut or another start: come to the front.</summary>
    public void BringForward()
    {
        Activate();
        NativeWindow.BringToFront(_hwnd);
        if (IsFlyout && Shelf.Visibility == Visibility.Visible) Shelf.FocusSearch();
    }

    /// <summary>"On the desktop" mode: the shortcut brings the panel in front of other windows for a moment.</summary>
    public void Peek()
    {
        _desktopLayer?.Peek();
        Activate();
    }

    public void OpenSettings() => ShowSettings();

    private const uint WM_SETTINGCHANGE = 0x001A, WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0, WM_DEVICECHANGE = 0x0219;
    private bool _placementPending;

    /// <summary>
    /// Taskbar moved, resized or switched to auto-hide (Windows broadcasts a setting change), resolution,
    /// scaling or monitors changed: place the panel again. Event-driven, nothing polls.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WM_SETTINGCHANGE or WM_DISPLAYCHANGE or WM_DPICHANGED) SchedulePlacement();
        // A device was added or removed (sent to every top-level window): a controller may have connected.
        if (msg == WM_DEVICECHANGE) _pad.DevicesChanged();
        return IntPtr.Zero;
    }

    /// <summary>Coalesces bursts of change messages into one placement once they're handled.</summary>
    private void SchedulePlacement()
    {
        if (_placementPending) return;
        _placementPending = true;
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _placementPending = false;
            PlaceOnScreen();
        });
    }

    /// <summary>
    /// Docks the panel to the chosen monitor's edge, or keeps it at its free position. Leaves room for
    /// taskbars, including auto-hide ones, which otherwise slide over the panel.
    /// </summary>
    private void PlaceOnScreen()
    {
        var settings = App.Settings;
        (int X, int Y)? free = !IsFlyout && settings.Position == DockPosition.Free && settings.FreeLeft is { } x && settings.FreeTop is { } y
            ? ((int)x, (int)y)
            : null;
        var monitor = free is { } point ? Monitors.FromPoint(point.X, point.Y) : Monitors.Find(settings.Monitor);
        // The flyout fills the work area's height with 12 px margins on the docked side (spec §5).
        var position = IsFlyout && settings.Position == DockPosition.Free ? DockPosition.Right : settings.Position;
        var rect = Placement.Compute(monitor.WorkArea, Monitors.AutoHideTaskbars(monitor), monitor.Scale,
            position, IsFlyout ? null : free, IsFlyout ? Placement.FlyoutMargin : Placement.EdgeMargin);
        if (rect != NativeWindow.GetBounds(_hwnd)) NativeWindow.SetBounds(_hwnd, rect);
    }

    /// <summary>In Free position, dragging an empty spot of the panel moves it (buttons and tiles handle their own presses).</summary>
    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsFlyout || App.Settings.Position != DockPosition.Free || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
        NativeWindow.DragMove(_hwnd); // returns when the button is released
        RememberFreePosition();
        PlaceOnScreen(); // dropped partly off-screen or on another monitor: fit it there
        SaveSettings();
    }

    private void RememberFreePosition()
    {
        var bounds = NativeWindow.GetBounds(_hwnd);
        App.Settings.FreeLeft = bounds.Left;
        App.Settings.FreeTop = bounds.Top;
        App.Settings.Monitor = Monitors.FromPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2).Device;
    }
}
