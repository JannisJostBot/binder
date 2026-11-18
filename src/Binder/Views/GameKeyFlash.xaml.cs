using System.Numerics;
using System.Runtime.InteropServices;
using Binder.Models;
using Binder.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Binder.Views;

/// <summary>
/// Game shortcuts' flash (spec §26, board 35, A): the game's banner and logo, centered above the taskbar of the
/// screen with the mouse, for the wait before the game starts; a bar empties over the wait, Esc cancels. Shown
/// without taking focus, so the app in front keeps the keyboard. A number without a game gets the plain pill. Its
/// owner (<c>App.GameKeys</c>) runs the wait; this window only shows it. No timer here except the one that closes
/// a message after a moment.
/// </summary>
public sealed partial class GameKeyFlash : Window
{
    private static readonly TimeSpan CancelledFor = TimeSpan.FromSeconds(1.2);
    private static readonly TimeSpan NoticeFor = TimeSpan.FromSeconds(2.5);

    private readonly IntPtr _hwnd;
    private readonly Game? _game;
    private readonly bool _animate;
    private readonly bool _highContrast;
    private TimeSpan? _closeAfter;
    private DispatcherQueueTimer? _timer;
    private Storyboard? _drain;
    private FrameworkElement _announce;

    // The shadow under the surface, drawn once (none in high contrast).
    private LayerVisual? _shadowLayer;
    private ShapeVisual? _shadowShape;
    private CompositionRoundedRectangleGeometry? _shadowGeometry;

    /// <summary>A game about to start: its art, "Starting · Esc to cancel", and the keys that were pressed.</summary>
    public GameKeyFlash(Game game, int number, string letter)
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        (_animate, _highContrast) = SetUpWindow();
        _game = game;
        _announce = LineText;
        LineTitle.Text = Strings.Starting;
        LineDetail.Text = " · " + Strings.EscToCancel;
        AutomationProperties.SetName(LineText, Strings.Format(Strings.GameKeyStartingName, game.Name));
        LetterKey.Text = letter;
        NumberKey.Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        NameText.Text = game.Name;
        Logo.LogoChanged += hasLogo => NamePanel.Visibility = hasLogo ? Visibility.Collapsed : Visibility.Visible;
        Root.SizeChanged += OnFirstLayout;
        Closed += (_, _) =>
        {
            _timer?.Stop();
            _drain?.Stop();
            Logo.Game = null;
        };
    }

    /// <summary>A number without a game ("Nothing on 4", "No game played yet"): the pill, gone after a moment.</summary>
    public GameKeyFlash(int number, string title, string? detail)
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        (_animate, _highContrast) = SetUpWindow();
        Card.Visibility = Visibility.Collapsed;
        Pill.Visibility = Visibility.Visible;
        Surface.CornerRadius = new CornerRadius(24);
        PillKey.Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PillTitle.Text = title;
        PillDetail.Text = detail is null ? "" : " · " + detail;
        AutomationProperties.SetName(PillText, detail is null ? title : Strings.Format(Strings.Sentences, title, detail));
        _announce = PillText;
        _closeAfter = NoticeFor;
        Root.SizeChanged += OnFirstLayout;
        Closed += (_, _) => _timer?.Stop();
    }

    private (bool Animate, bool HighContrast) SetUpWindow()
    {
        // As Game mode's pill: a frameless popup, always on top, out of the taskbar and Alt+Tab, see-through around
        // the XAML surface, which draws the card, its round corners and its shadow.
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        NativeWindow.MakeToolWindow(_hwnd);
        NativeWindow.MakeFrameless(_hwnd);
        SystemBackdrop = new TransparentBackdrop();
        bool highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        if (highContrast)
        {
            Surface.BorderThickness = new Thickness(2); // system colors, a stronger edge, no art and no shadow
            Card.Margin = Pill.Margin = new Thickness(2);
        }
        else
            Surface.SizeChanged += (_, _) => UpdateShadow();
        return (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled, highContrast);
    }

    /// <summary>Shows it without activating it, on the screen with the mouse.</summary>
    public void ShowQuietly()
    {
        var work = Screen().WorkArea;
        NativeWindow.SetBounds(_hwnd, new PixelRect(work.Left, work.Top, work.Left + 800, work.Top + 400));
        NativeWindow.Cloak(_hwnd, true);
        AppWindow.Show(activateWindow: false);
        if (_game is not null && !_highContrast) _ = LoadArtAsync(_game);
        else if (_game is not null) NameText.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    private static MonitorInfo Screen() => GetCursorPos(out var p) ? Monitors.FromPoint(p.X, p.Y) : Monitors.Find(null);

    /// <summary>The bar empties over <paramref name="wait"/> (on the compositor; it shows time, so also with animations off).</summary>
    public void StartWait(TimeSpan wait)
    {
        var drain = new DoubleAnimation { From = 1, To = 0, Duration = wait };
        Storyboard.SetTarget(drain, BarScale);
        Storyboard.SetTargetProperty(drain, "ScaleX");
        _drain = new Storyboard();
        _drain.Children.Add(drain);
        _drain.Begin();
    }

    /// <summary>Esc: the logo greys out, the line says nothing started, and it closes after a moment.</summary>
    public void ShowCancelled()
    {
        if (_game is null) return;
        _drain?.Stop();
        Track.Visibility = Visibility.Collapsed;
        CancelDot.Visibility = Visibility.Visible;
        Banner.Opacity = 0.35;
        LineTitle.Text = Strings.Cancelled;
        LineDetail.Text = " · " + Strings.Format(Strings.GameDidntStart, _game.Name);
        AutomationProperties.SetName(LineText, Strings.Format(Strings.Sentences, Strings.Cancelled, Strings.Format(Strings.GameDidntStart, _game.Name)));
        Announce();
        CloseAfter(CancelledFor);
    }

    /// <summary>The cover's art: the banner and Steam's logo, else the cover beside the name, on the cover's own color.</summary>
    private async Task LoadArtAsync(Game game)
    {
        double scale = Root.XamlRoot?.RasterizationScale ?? 1;
        var fallback = CoverCache.TryGetFallback(game, ArtKind.Cover) ?? await CoverCache.GetFallbackAsync(game);
        Banner.Background = new SolidColorBrush(FallbackArt.Parse(fallback.Tint));
        Logo.Game = game; // Steam's transparent logo; the name shows until (unless) it's there
        int width = (int)Math.Ceiling(360 * scale);
        var hero = CoverCache.TryGetCached(game, width, ArtKind.Hero) ?? await CoverCache.LoadAsync(game, width, ArtKind.Hero);
        if (hero is not null)
        {
            Hero.Source = hero;
            return;
        }
        int coverWidth = (int)Math.Ceiling(66 * scale);
        if ((CoverCache.TryGetCached(game, coverWidth) ?? await CoverCache.LoadAsync(game, coverWidth)) is { } cover)
        {
            CoverImage.Source = cover;
            CoverBox.Visibility = Visibility.Visible;
        }
    }

    private void OnFirstLayout(object sender, SizeChangedEventArgs e)
    {
        if (Root.ActualWidth <= 0 || Root.XamlRoot is not { } xamlRoot) return;
        Root.SizeChanged -= OnFirstLayout;

        double scale = xamlRoot.RasterizationScale;
        int width = (int)Math.Ceiling(Root.ActualWidth * scale);
        int height = (int)Math.Ceiling(Root.ActualHeight * scale);
        // Above the taskbar (also an auto-hide one), like Game mode's pill: the shadow room below is the gap.
        var monitor = Screen();
        var work = monitor.WorkArea;
        int bottom = work.Bottom - Monitors.AutoHideTaskbars(monitor).Bottom;
        int left = work.Left + (work.Width - width) / 2;
        int top = bottom - height;
        NativeWindow.SetBounds(_hwnd, new PixelRect(left, top, left + width, top + height));

        if (_animate)
        {
            // A short rise and fade in; nothing moves after it except the bar.
            var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(160) };
            Storyboard.SetTarget(fade, Body);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var rise = new DoubleAnimation
            {
                From = 8, To = 0, Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(rise, BodyShift);
            Storyboard.SetTargetProperty(rise, "Y");
            var show = new Storyboard();
            show.Children.Add(fade);
            show.Children.Add(rise);
            Body.Opacity = 0;
            NativeWindow.Cloak(_hwnd, false);
            show.Begin();
        }
        else NativeWindow.Cloak(_hwnd, false);

        Announce();
        if (_closeAfter is { } after) CloseAfter(after);
    }

    /// <summary>Screen readers hear it like Windows' own notifications.</summary>
    private void Announce() =>
        FrameworkElementAutomationPeer.CreatePeerForElement(_announce)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

    private void CloseAfter(TimeSpan after)
    {
        _timer?.Stop();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = after;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Close();
        _timer.Start();
    }

    private void UpdateShadow()
    {
        var size = new Vector2((float)Surface.ActualWidth, (float)Surface.ActualHeight);
        if (size.X <= 0) return;
        if (_shadowLayer is null)
        {
            var compositor = ElementCompositionPreview.GetElementVisual(ShadowHost).Compositor;
            _shadowGeometry = compositor.CreateRoundedRectangleGeometry();
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
        _shadowGeometry!.CornerRadius = new Vector2((float)Surface.CornerRadius.TopLeft);
        _shadowGeometry.Size = size;
        _shadowShape!.Size = size;
        _shadowLayer.Size = size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);
}
