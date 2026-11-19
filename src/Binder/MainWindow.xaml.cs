using Binder.Library;
using Binder.Models;
using Binder.Services;
using Binder.ViewModels;
using Binder.Views;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Binder;

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
        Footprint.TrackPanel(this); // counted in --memory-report
        // Set here, not with x:Bind: an x:Bind on a Window subscribes to its Activated event (see OnClosed).
        KeepOpenButton.Content = Strings.KeepOpen;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(KeepOpenButton, Strings.KeepOpenToolTip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(KeepOpenButton, Strings.KeepOpenName);
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        Shelf.Initialize(this); // its actions: MainWindow.Shelf.cs
        // The other pages are built and initialized on their first visit (MainWindow.Pages.cs).
        Shelf.ViewModel!.SetFlyout(IsFlyout, App.Pinned);
        Shelf.ViewModel!.SetData(_games, _sessions, App.Settings);
        ListenForUpdates(); // its part: MainWindow.Updates.cs
        PickWelcome();
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
        FrameTicks.Add(OnFirstFrame);
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
    private void OnFirstFrame()
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

        FrameTicks.Remove(OnFirstFrame);
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
        if (App.State.ScanDue) _ = RescanAsync(); // the first open, then only after an hour (AppState.ScanDue)
        App.Current.PanelShown(); // the update check, at most once a day (spec §25)
    }

    /// <summary>Scans the stores off the UI thread and merges the result. Runs at the first open, on Refresh, and on an open over an hour after the last scan.</summary>
    private async Task RescanAsync()
    {
        if (App.State.Scanning) return;
        App.State.Scanning = true;
        RefreshSettingsPage();

        // The scan works on its own copy: the library may change on the UI thread while it runs.
        var stored = _games.ToList();
        var before = stored.Select(g => g.Id).ToHashSet();
        var settings = App.Settings;
        var removed = new List<Game>(App.State.RemovedGames); // the scan's own copy
        LibraryScanner.RescanResult result;
        try
        {
            result = await Task.Run(() => LibraryScanner.Rescan(stored, removed, settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        finally
        {
            App.State.Scanning = false; // also after an unexpected error: Refresh must keep working
            if (!IsClosing) RefreshSettingsPage();
        }
        ForgetGames(result.Dropped, removed);

        // Bring the result up to date with what changed while the scan ran: settings (a favorite set meanwhile isn't
        // lost), games removed meanwhile stay removed, games added meanwhile stay.
        var games = LibraryScanner.Reconcile(result.Games, before, _games);
        bool iconChanged = false;
        foreach (var game in games)
        {
            if (!game.IconChanged) continue;
            iconChanged = true;
            CoverCache.ForgetIcon(game);
        }

        App.State.Scanned = true;
        // The usual case: nothing changed. Keep the games already shown (their objects, so nothing on screen goes
        // stale), with no save and no redraw of the grid. Start search is still brought in line (spec §22 c: a
        // launcher may have added its own Start entries). Not when library.json is gone (it was damaged and its .bak
        // used): then this save puts it back.
        if (!iconChanged && result.Dropped.Count == 0 && Storage.SameLibrary(games, _games) && Storage.LibraryFileExists())
        {
            if (App.Settings.StartMenuGames) SyncStartMenu();
            return;
        }
        _games = games;
        // Spec §22 c: after a rescan always (a launcher may have added its own Start entries), so not through
        // SaveLibrary's check, which would sync a second time.
        _startMenuShelfKey = StartMenuShelfKey();
        SaveLibrary();
        if (App.Settings.StartMenuGames) SyncStartMenu();
        if (IsClosing)
        {
            App.Current.LibraryRescanned(); // the panel was closed and maybe reopened meanwhile: that one shows it
            return;
        }
        await ShowRescanned();
    }

    /// <summary>Shows the library after a scan that a closed panel had started.</summary>
    public async Task ShowRescanned()
    {
        if (IsClosing) return;
        if (OpeningGate.IsHolding) await OpeningGate.WhenOpen; // don't rebuild the grid mid-slide
        RefreshViews();
    }

    /// <summary>
    /// Games that left the library: keeps their settings (<see cref="RemovedGames"/>, in <paramref name="removed"/>,
    /// which becomes the kept list) and deletes their cached art; a custom cover goes only when its settings do.
    /// </summary>
    private static void ForgetGames(List<Game> dropped, List<Game> removed)
    {
        bool changed = removed.Count != App.State.RemovedGames.Count || dropped.Count > 0;
        foreach (var game in RemovedGames.Remember(removed, dropped)) CoverCache.Forget(game, customToo: true);
        foreach (var game in dropped) CoverCache.Forget(game, customToo: !game.HasUserData);
        App.State.SetRemovedGames(removed);
        ForgetGameKeys(dropped); // their numbers are free again
        if (!changed) return; // nothing restored or added
        try { Storage.SaveRemovedGames(removed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void SaveLibrary()
    {
        try { Storage.SaveLibrary(_games); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        // An import, its Undo, Add a game, Hide or a rename changes what Start should list (spec §22 c); other edits
        // (favorites, per-game settings) leave the shelf's games as they were and don't read the Start menu.
        if (App.Settings.StartMenuGames && StartMenuShelfKey() != _startMenuShelfKey) SyncStartMenu();
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
        if (Shown(SetupSlot)) SetupPage.ViewModel!.UpdateLibrary(_games);
        if (!Shown(DetailSlot) || Detail.Game is not { } shown) return;

        var game = FindGame(shown.Id);
        if (game is null || game.Hidden || !App.Settings.IsSourceEnabled(game.Source)) ShowShelf();
        else Detail.ViewModel!.Show(game, _sessions, App.Settings, App.State.Crashes);
    }

    private void RefreshSettingsPage()
    {
        if (!Shown(SettingsSlot)) return;
        var bounds = NativeWindow.GetBounds(_hwnd);
        var current = Monitors.FromPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        SettingsPage.ViewModel!.SetUpdate(App.CurrentUpdate);
        SettingsPage.ViewModel!.Load(App.Settings, _games, Autostart.IsEnabled(), App.State.Scanning,
            Storage.ImportedShortcutCount(), Monitors.All(), current.Device, App.HotkeyFree);
    }

    /// <summary>Closes the pages' (i) tips: they are popups of their own and would stay on screen.</summary>
    private void CloseTips()
    {
        SettingsSlot?.CloseTips();
        FreeSpaceSlot?.CloseTips();
    }

    private Game? FindGame(string id) => _games.FirstOrDefault(g => g.Id == id);

    /// <summary>Shows one page of the panel (shelf, game details or settings) and collapses the others.</summary>
    private void ShowPage(UIElement page)
    {
        CloseTips();
        Shelf.Visibility = page == Shelf ? Visibility.Visible : Visibility.Collapsed;
        // Pages not built yet stay unbuilt: only the one being shown is loaded (Show… asks for it first).
        SetShown(DetailSlot, page);
        SetShown(SettingsSlot, page);
        SetShown(ImportSlot, page);
        SetShown(SetupSlot, page);
        SetShown(GameModeSlot, page);
        if (page != FreeSpaceSlot && FreeSpaceSlot is { Visibility: Visibility.Visible } freeSpace)
        {
            freeSpace.Release();
            App.State.FreeSpace = null;
        }
        SetShown(FreeSpaceSlot, page);
    }

    private void ShowDetail(Game game)
    {
        if (RemoveIfGone(game)) return;
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
        var shown = Shown(DetailSlot) ? Detail.Game : null;
        ShowPage(Shelf);
        Shelf.UpdateLayout();
        if (shown is not null) Shelf.FocusGame(shown);
        else if (_padMode) Shelf.FocusFirstGame(); // a search box is no use without a keyboard
        else Shelf.FocusSearch();
    }
}
