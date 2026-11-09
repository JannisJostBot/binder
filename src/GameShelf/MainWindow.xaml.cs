using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.ViewModels;
using GameShelf.Views;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GameShelf;

/// <summary>
/// The panel: a borderless WinUI window docked to the screen edge, with Acrylic behind it. In Flyout mode it's
/// created on open and closed for good on close; on the desktop it stays.
/// </summary>
public sealed partial class MainWindow : Window, ISettingsActions, IDetailActions, IGameModeActions, IFreeSpaceActions, ISetupActions, IShelfActions
{
    // The library lives in App.State, so it survives the flyout window being closed and rebuilt.
    private List<Game> _games { get => App.State.Games; set => App.State.Games = value; }
    private List<Session> _sessions { get => App.State.Sessions; set => App.State.Sessions = value; }

    private static bool IsFlyout => App.Settings.DisplayMode == DisplayMode.Flyout;

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _dispatcher;
    private readonly WindowSubclass _subclass;
    private DesktopLayer? _desktopLayer;
    private bool _scanning;
    private bool _active, _wasActive;

    // What the toast's button does besides closing it (e.g. "Undo"); null for plain notices.
    private Action? _toastAction;

    // An Undo offer goes away by itself. The timer exists only while such a toast is showing.
    private static readonly TimeSpan UndoTimeout = TimeSpan.FromSeconds(10);
    private DispatcherQueueTimer? _undoTimer;

    /// <summary>Raised once when the panel starts closing (it hides first, then closes a moment later).</summary>
    public event Action? Closing;

    public MainWindow()
    {
        InitializeComponent();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        Shelf.Initialize(this); // its actions: MainWindow.Shelf.cs
        Detail.Initialize(this); // its actions: MainWindow.Detail.cs
        SettingsPage.Initialize(this); // its actions: MainWindow.Settings.cs
        GameModePage.Initialize(this); // its actions: MainWindow.GameMode.cs
        FreeSpacePage.Initialize(this); // its actions: MainWindow.FreeSpace.cs
        ImportPage.Cancelled += ShowSettings;
        ImportPage.ImportRequested += ImportShortcuts;
        SetupPage.Initialize(this); // its actions: MainWindow.Setup.cs
        Shelf.ViewModel!.SetFlyout(IsFlyout, App.Pinned);
        Shelf.ViewModel!.SetData(_games, _sessions, App.Settings);
        Shelf.CrashPlayRequested += game =>
        {
            CrashSeen();
            Play(game);
        };
        Shelf.CrashDismissed += _ => CrashSeen();
        if (CrashStore.Unseen(App.State.Crashes, _sessions) is { } crash) ShowCrash(crash);
        if (!App.Settings.SetupDone) ShowSetup();
        else if (App.State.FreeSpace is { Uninstalling: true } freeSpace) ShowFreeSpace(freeSpace); // back from a launcher

        // The launch countdown runs in App (it survives the flyout closing); show it while we're open.
        App.Current.CountdownTick += OnCountdownTick;
        if (App.Current.CountdownRunning) ShowCountdown();

        // Controller (spec §4): read only while this window is active and a pad is connected.
        _pad = new Gamepad(_dispatcher);
        _pad.Moved += OnPadMoved;
        _pad.Pressed += OnPadPressed;
        _pad.Scrolled += OnPadScrolled;
        Shelf.ViewModel!.SetBackHint(IsFlyout);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnAnyKeyDown), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnAnyPointerPressed), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnAnyPointerPressed), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnAnyPointerMoved), handledEventsToo: true);

        Activated += OnActivated;
        Closed += OnClosed;

        // Borderless (Windows still draws the thin border, rounded corners and shadow), not resizable, not in
        // the taskbar or Alt+Tab. Like Windows' own flyouts the flyout is on top: if Windows doesn't let it take
        // focus (it may refuse after a tray click), it still opens in front instead of behind the current window.
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = IsFlyout;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        NativeWindow.MakeToolWindow(_hwnd);
        NativeWindow.TryApplyRoundedCorners(_hwnd);

        if (PanelBackdrop.IsSupported) SystemBackdrop = new PanelBackdrop();
        else Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

        _subclass = new WindowSubclass(_hwnd);
        _subclass.Add(WndProc);
        // The desktop layer only applies to the "On the desktop" mode; the flyout opens on top.
        _desktopLayer = new DesktopLayer(_hwnd, _subclass) { Enabled = !IsFlyout && App.Settings.StayOnDesktop };
    }

    /// <summary>Places the window and shows it (sliding in, in Flyout mode).</summary>
    public void Open()
    {
        PlaceOnScreen();
        if (IsFlyout) PrepareSlide();
        // Hidden until WinUI has drawn the panel with the covers in view: a new window is otherwise black for a
        // moment, and covers would pop in one by one while it slides.
        NativeWindow.Cloak(_hwnd, true);
        CoverQueue.HoldFrom(0); // no grid covers until the first layout says which ones are in view
        Activate();
        CompositionTarget.Rendering += OnFirstFrame;
    }

    /// <summary>Longest the panel waits for the covers in view before it shows anyway (slow disk, huge art).</summary>
    private const double MaxCoverWaitMilliseconds = 250;

    private int _framesBeforeShowing = 2;
    private int _framesAfterCovers = -1;
    private System.Diagnostics.Stopwatch? _coverWait;
    private bool _shown; // uncloaked at least once

    /// <summary>
    /// Runs before each frame while the panel is hidden. After the first frame (the layout is known) only the
    /// covers in view load; once they're decoded and drawn, or after <see cref="MaxCoverWaitMilliseconds"/>, the
    /// window shows complete, the slide starts and, once per run, the library scan. Covers below the fold load
    /// after the slide.
    /// </summary>
    private void OnFirstFrame(object? sender, object e)
    {
        if (--_framesBeforeShowing > 0) return;
        if (_coverWait is null)
        {
            CoverQueue.HoldFrom(Shelf.VisibleItemCount());
            _coverWait = System.Diagnostics.Stopwatch.StartNew();
        }
        // Covers decoded: give them two frames to be drawn before showing.
        if (_framesAfterCovers < 0 && CoverQueue.VisibleDone) _framesAfterCovers = 2;
        bool drawn = _framesAfterCovers >= 0 && --_framesAfterCovers <= 0;
        if (!drawn && _coverWait.Elapsed.TotalMilliseconds < MaxCoverWaitMilliseconds) return;

        CompositionTarget.Rendering -= OnFirstFrame;
        NativeWindow.Cloak(_hwnd, false);
        _shown = true;
        if (_startSlide is { } slide)
        {
            _startSlide = null;
            slide(); // releases the held covers when it ends
        }
        else
        {
            CoverQueue.ReleaseHold();
        }
        if (!App.State.Scanned) _ = RescanAsync(); // once per run, not on every flyout open
    }

    /// <summary>Scans the stores off the UI thread and merges the result. Runs at startup and on Refresh.</summary>
    private async Task RescanAsync()
    {
        if (_scanning) return;
        _scanning = true;
        RefreshSettingsPage();

        var stored = _games;
        var settings = App.Settings;
        List<Game> games;
        try
        {
            games = await Task.Run(() => LibraryScanner.Rescan(stored, settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _scanning = false;
            RefreshSettingsPage();
            return;
        }
        _scanning = false;

        // The scan copied user data from the list as it was when the scan started. Re-apply it from the
        // current entries, so a favorite or setting changed while the scan ran isn't lost.
        var current = new Dictionary<string, Game>(_games.Count);
        foreach (var game in _games) current.TryAdd(game.Id, game);
        foreach (var game in games)
        {
            if (current.TryGetValue(game.Id, out var old) && !ReferenceEquals(old, game)) game.CopyUserData(old);
            if (game.IconChanged) CoverCache.ForgetIcon(game);
        }

        _games = games;
        App.State.Scanned = true;
        SaveLibrary();
        if (IsClosing) return;
        if (OpeningGate.IsHolding) await OpeningGate.WhenOpen; // don't rebuild the grid mid-slide
        RefreshViews();
    }

    private void SaveLibrary()
    {
        try { Storage.SaveLibrary(_games); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void SaveSettings()
    {
        try { Storage.SaveSettings(App.Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Re-renders the shelf and whichever page is open (with the current instance of its game).</summary>
    private void RefreshViews()
    {
        Shelf.ViewModel!.SetData(_games, _sessions, App.Settings);
        RefreshSettingsPage();
        if (SetupPage.Visibility == Visibility.Visible) SetupPage.ViewModel!.UpdateLibrary(_games);
        if (Detail.Visibility != Visibility.Visible || Detail.Game is not { } shown) return;

        var game = FindGame(shown.Id);
        if (game is null || game.Hidden || !App.Settings.IsSourceEnabled(game.Source)) ShowShelf();
        else Detail.ViewModel!.Show(game, _sessions, App.Settings, App.State.Crashes);
    }

    private void RefreshSettingsPage()
    {
        if (SettingsPage.Visibility != Visibility.Visible) return;
        var bounds = NativeWindow.GetBounds(_hwnd);
        var current = Monitors.FromPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        SettingsPage.ViewModel!.Load(App.Settings, _games, Autostart.IsEnabled(), _scanning,
            Storage.LoadImportedShortcuts().Count, Monitors.All(), current.Device, App.HotkeyFree);
    }

    private Game? FindGame(string id) => _games.FirstOrDefault(g => g.Id == id);

    /// <summary>Shows one page of the panel (shelf, game details or settings) and collapses the others.</summary>
    private void ShowPage(UIElement page)
    {
        Shelf.Visibility = page == Shelf ? Visibility.Visible : Visibility.Collapsed;
        Detail.Visibility = page == Detail ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;
        ImportPage.Visibility = page == ImportPage ? Visibility.Visible : Visibility.Collapsed;
        SetupPage.Visibility = page == SetupPage ? Visibility.Visible : Visibility.Collapsed;
        GameModePage.Visibility = page == GameModePage ? Visibility.Visible : Visibility.Collapsed;
        if (page != FreeSpacePage && FreeSpacePage.Visibility == Visibility.Visible)
        {
            FreeSpacePage.Release();
            App.State.FreeSpace = null;
        }
        FreeSpacePage.Visibility = page == FreeSpacePage ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowDetail(Game game)
    {
        Detail.ViewModel!.Show(game, _sessions, App.Settings, App.State.Crashes);
        ShowPage(Detail);
        Detail.UpdateLayout();
        Detail.FocusPlay();
    }

    private void ShowSettings()
    {
        ShowPage(SettingsPage);
        RefreshSettingsPage();
        SettingsPage.UpdateLayout();
        SettingsPage.FocusFirst();
    }

    private void ShowShelf()
    {
        if (Shelf.Visibility == Visibility.Visible) return;
        var shown = Detail.Visibility == Visibility.Visible ? Detail.Game : null;
        ShowPage(Shelf);
        Shelf.UpdateLayout();
        if (shown is not null) Shelf.FocusGame(shown);
        else if (_padMode) Shelf.FocusFirstGame(); // a search box is no use without a keyboard
        else Shelf.FocusSearch();
    }
}
