using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.Views;
using Microsoft.UI.Dispatching;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI.ViewManagement;

namespace GameShelf;

/// <summary>
/// The panel: a borderless WinUI window docked to the screen edge, with Acrylic behind it. In Flyout mode it's
/// created on open and closed for good on close; on the desktop it stays.
/// </summary>
public sealed partial class MainWindow : Window
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

        Shelf.PlayRequested += Play;
        Shelf.OpenRequested += ShowDetail;
        Shelf.ActionRequested += OnGameAction;
        Detail.BackRequested += ShowShelf;
        Detail.ActionRequested += OnGameAction;
        Detail.GameEdited += _ =>
        {
            SaveLibrary();
            Shelf.SetData(_games, _sessions, App.Settings); // e.g. a new cover shows on the tile too
        };
        Shelf.SettingsRequested += ShowSettings;
        Shelf.SortChanged += SaveSettings;
        Shelf.ImportRequested += () => _ = ShowImportAsync();
        Shelf.AddGameRequested += AddGame;
        SettingsPage.BackRequested += ShowShelf;
        SettingsPage.Changed += OnSettingChanged;
        SettingsPage.RefreshRequested += () => _ = RescanAsync();
        SettingsPage.UnhideRequested += Unhide;
        SettingsPage.ImportRequested += () => _ = ShowImportAsync();
        SettingsPage.RestoreRequested += RestoreShortcuts;
        SettingsPage.AddGameRequested += AddGame;
        SettingsPage.SetupRequested += ShowSetup;
        ImportPage.Cancelled += ShowSettings;
        ImportPage.ImportRequested += ImportShortcuts;
        SetupPage.Skipped += () => FinishSetup(null);
        SetupPage.Finished += choices => FinishSetup(choices);
        SetupPage.ShortcutsNeeded += () => _ = LoadSetupShortcutsAsync();
        SetupPage.ImportRequested += picked =>
        {
            ShowImportResult(StoreShortcuts(picked));
            SetupPage.ImportDone();
        };
        Shelf.PinRequested += () =>
        {
            App.Pinned = !App.Pinned;
            Shelf.SetFlyout(IsFlyout, App.Pinned);
        };
        Shelf.SetFlyout(IsFlyout, App.Pinned);
        Shelf.SetData(_games, _sessions, App.Settings);
        if (!App.Settings.SetupDone) ShowSetup();

        // The launch countdown runs in App (it survives the flyout closing); show it while we're open.
        App.Current.CountdownTick += OnCountdownTick;
        if (App.Current.CountdownRunning) ShowCountdown("Starting your game");

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
        // Hidden until WinUI has drawn its first frame: a new window is otherwise black for a moment.
        NativeWindow.Cloak(_hwnd, true);
        Activate();
        CompositionTarget.Rendering += OnFirstFrame;
    }

    private int _framesBeforeShowing = 2;

    /// <summary>
    /// The first frame is drawn (Rendering runs before each frame, so the second call means one is done): show
    /// the window, start the slide and, once per run, the library scan.
    /// </summary>
    private void OnFirstFrame(object? sender, object e)
    {
        if (--_framesBeforeShowing > 0) return;
        CompositionTarget.Rendering -= OnFirstFrame;
        NativeWindow.Cloak(_hwnd, false);
        _startSlide?.Invoke();
        _startSlide = null;
        if (!App.State.Scanned) _ = RescanAsync(); // once per run, not on every flyout open
    }

    // ---- Flyout (spec §5, board 6) -----------------------------------------------------------------------

    private const double SlideDistance = 40; // effective pixels, scaled to the monitor
    private const double SlideMilliseconds = 300;
    private volatile bool _slideCancelled;
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
        _startSlide = () =>
        {
            // One step per display refresh: DwmFlush waits for the compositor's next frame (240 per second on a
            // 240 Hz screen), which a dispatcher timer can't match. The thread ends with the slide.
            var thread = new Thread(() =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (!_slideCancelled)
                {
                    double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / SlideMilliseconds);
                    double eased = Decelerate(t);
                    NativeWindow.MoveToAsync(hwnd, target.Left + (int)Math.Round(dx * (1 - eased)), target.Top);
                    if (t >= 1) break;
                    if (NativeWindow.WaitForCompositorFrame() != 0) Thread.Sleep(4);
                }
                _dispatcher.TryEnqueue(OpeningGate.Release);
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
        AppWindow.Hide();
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, Close);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!IsClosing)
        {
            IsClosing = true;
            Closing?.Invoke();
        }
        _slideCancelled = true;
        OpeningGate.Release();
        CompositionTarget.Rendering -= OnFirstFrame;
        App.Current.CountdownTick -= OnCountdownTick;
        _undoTimer?.Stop();
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

    private const uint WM_SETTINGCHANGE = 0x001A, WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0;
    private bool _placementPending;

    /// <summary>
    /// Taskbar moved, resized or switched to auto-hide (Windows broadcasts a setting change), resolution,
    /// scaling or monitors changed: place the panel again. Event-driven, nothing polls.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WM_SETTINGCHANGE or WM_DISPLAYCHANGE or WM_DPICHANGED) SchedulePlacement();
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
            if (current.TryGetValue(game.Id, out var old) && !ReferenceEquals(old, game)) game.CopyUserData(old);

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
        Shelf.SetData(_games, _sessions, App.Settings);
        RefreshSettingsPage();
        if (SetupPage.Visibility == Visibility.Visible) SetupPage.UpdateLibrary(_games);
        if (Detail.Visibility != Visibility.Visible || Detail.Game is not { } shown) return;

        var game = FindGame(shown.Id);
        if (game is null || game.Hidden || !App.Settings.IsSourceEnabled(game.Source)) ShowShelf();
        else Detail.Show(game, _sessions, App.Settings);
    }

    private void RefreshSettingsPage()
    {
        if (SettingsPage.Visibility != Visibility.Visible) return;
        var bounds = NativeWindow.GetBounds(_hwnd);
        var current = Monitors.FromPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        SettingsPage.Show(App.Settings, _games, Autostart.IsEnabled(), _scanning,
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
    }

    /// <summary>First-run setup (board 10). Also reachable from Settings → "Run first-time setup again".</summary>
    private void ShowSetup()
    {
        SetupPage.Start(App.Settings, _games, Autostart.IsEnabled());
        ShowPage(SetupPage);
    }

    private async Task LoadSetupShortcutsAsync()
    {
        var found = await Task.Run(ShortcutFile.ScanDesktops);
        SetupPage.SetShortcuts(found, _games);
    }

    /// <summary>Applies the setup's answers. <paramref name="choices"/> is null for "Skip setup" (keep the defaults).</summary>
    private void FinishSetup(SetupChoices? choices)
    {
        var settings = App.Settings;
        bool modeChanged = false;
        if (choices is not null)
        {
            modeChanged = settings.DisplayMode != choices.Mode;
            settings.DisplayMode = choices.Mode;
            if (choices.StartWithWindows != Autostart.IsEnabled() && !Autostart.TrySet(choices.StartWithWindows))
                ShowToast("Couldn't change Start with Windows", "Windows didn't allow the change.", "OK");
            settings.StartWithWindows = Autostart.IsEnabled();
        }
        settings.SetupDone = true;
        SaveSettings();
        if (modeChanged)
        {
            App.Current.ApplyDisplayMode(); // this window closes; the new one opens in the chosen mode
            return;
        }
        RefreshViews(); // launchers may have been unticked in step 1
        ShowShelf();
        Shelf.FocusSearch();
    }

    private void ShowDetail(Game game)
    {
        Detail.Show(game, _sessions, App.Settings);
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
        else Shelf.FocusSearch();
    }

    private void OnSettingChanged(SettingChange change)
    {
        var settings = App.Settings;
        switch (change)
        {
            case SettingChange.DisplayMode:
                SaveSettings();
                App.Current.ApplyDisplayMode(); // rebuilds the panel window in the new mode; this one closes
                return;
            case SettingChange.Hotkey:
                App.Current.ApplyHotkey();
                break;
            case SettingChange.TrayIcon:
                App.Current.ApplyTrayIcon();
                break;
            case SettingChange.Position:
                if (settings.Position == DockPosition.Free) RememberFreePosition(); // start where it is now
                PlaceOnScreen();
                break;
            case SettingChange.Screen:
                if (settings.Position == DockPosition.Free) settings.Position = DockPosition.Right; // dock on the new screen
                PlaceOnScreen();
                break;
            case SettingChange.DesktopLayer:
                if (_desktopLayer is not null) _desktopLayer.Enabled = settings.StayOnDesktop;
                break;
            case SettingChange.Autostart:
                if (!Autostart.TrySet(settings.StartWithWindows))
                    ShowToast("Couldn't change Start with Windows", "Windows didn't allow the change.", "OK");
                settings.StartWithWindows = Autostart.IsEnabled();
                break;
            case SettingChange.Sources:
                SaveSettings();
                RefreshViews();
                _ = RescanAsync(); // a re-enabled store may have new games
                return;
        }
        SaveSettings();
        RefreshViews();
    }

    /// <summary>Scans the desktops (off the UI thread; reading .lnk files can touch slow drives) and shows the picker.</summary>
    private async Task ShowImportAsync()
    {
        var found = await Task.Run(ShortcutFile.ScanDesktops);
        ImportPage.Show(found, _games);
        ShowPage(ImportPage);
        ImportPage.UpdateLayout();
        ImportPage.FocusFirst();
    }

    /// <summary>Moves the picked shortcuts into our folder and adds the ones that aren't already on the shelf.</summary>
    private void ImportShortcuts(IReadOnlyList<ShortcutFile> picked)
    {
        var result = StoreShortcuts(picked);
        ShowShelf();
        ShowImportResult(result);
    }

    private readonly record struct ImportResult(int Imported, int Copied, int Failed);

    private ImportResult StoreShortcuts(IReadOnlyList<ShortcutFile> picked)
    {
        var records = Storage.LoadImportedShortcuts();
        int imported = 0, copied = 0, failed = 0;
        foreach (var shortcut in picked)
        {
            var existing = ShortcutImporter.ExistingFor(shortcut, _games);
            var record = ShortcutImporter.Store(shortcut, move: true);
            if (record is null)
            {
                failed++;
                continue;
            }
            records.Add(record);
            imported++;
            if (record.Copied) copied++;
            if (existing is not null) continue; // already on the shelf: the shortcut is only tidied away

            var game = ShortcutImporter.CreateGame(shortcut, record.StoredPath, GameSource.Shortcut);
            if (FindGame(game.Id) is { } same) same.ExePath = record.StoredPath;
            else _games.Add(game);
        }

        TrySave(() => Storage.SaveImportedShortcuts(records));
        SaveLibrary();
        RefreshViews();
        ShellNotify.DesktopsChanged();
        return new ImportResult(imported, copied, failed);
    }

    private void ShowImportResult(ImportResult result)
    {
        var (imported, copied, failed) = result;
        var detail = copied > 0
            ? $"{copied} stayed on the shared desktop too: changing it needs administrator rights."
            : "You can put them back from Settings.";
        if (failed > 0) detail += $" {failed} couldn't be imported.";
        ShowToast(imported == 1 ? "Imported 1 shortcut" : $"Imported {imported} shortcuts", detail, "OK");
    }

    /// <summary>Puts every imported shortcut back on the desktop and removes the games made from them.</summary>
    private void RestoreShortcuts()
    {
        var (restored, failed) = ShortcutImporter.RestoreAll(_games);
        SaveLibrary();
        RefreshViews();
        ShowToast(failed == 0 ? "Shortcuts are back on your desktop" : "Some shortcuts couldn't be restored",
            failed == 0 ? $"{restored} restored." : $"{failed} are still in Game Shelf's folder.", "OK");
    }

    /// <summary>"Add a game": pick an .exe or a shortcut with the standard Windows file picker.</summary>
    private void AddGame()
    {
        var path = FilePicker.Open(Root, "Add a game", "Games and shortcuts (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url",
            keepShortcuts: true); // keep the shortcut itself, with its arguments
        if (path is not null) AddGameFromPath(path, showDetails: true);
    }

    private static bool IsAddable(string path) =>
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    private static async Task<List<string>> DroppedFilesAsync(DataPackageView data)
    {
        if (!data.Contains(StandardDataFormats.StorageItems)) return [];
        var items = await data.GetStorageItemsAsync();
        return items.OfType<StorageFile>().Select(f => f.Path).Where(p => IsAddable(p) && File.Exists(p)).ToList();
    }

    /// <summary>Drag an .exe or shortcut onto the widget to add it.</summary>
    private async void OnDragOver(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            e.AcceptedOperation = (await DroppedFilesAsync(e.DataView)).Count > 0 ? DataPackageOperation.Link : DataPackageOperation.None;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        List<string> addable;
        try { addable = await DroppedFilesAsync(e.DataView); }
        finally { deferral.Complete(); }
        foreach (var file in addable) AddGameFromPath(file, showDetails: addable.Count == 1);
    }

    /// <summary>Adds an .exe or shortcut the user picked or dropped; the user's file stays where it is.</summary>
    private void AddGameFromPath(string path, bool showDetails)
    {
        Game? game;
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            game = ShortcutImporter.CreateFromExe(path);
        }
        else if (ShortcutFile.Read(path) is { } shortcut)
        {
            if (ShortcutImporter.ExistingFor(shortcut, _games) is { } existing)
            {
                ShowDetail(existing);
                ShowToast($"{existing.Name} is already on the shelf", "It comes from your " + existing.SourceLabel + " library.", "OK");
                return;
            }
            // Keep the user's file where it is; we launch our own copy.
            var stored = ShortcutImporter.Store(shortcut, move: false);
            game = stored is null ? null : ShortcutImporter.CreateGame(shortcut, stored.StoredPath, GameSource.Manual);
        }
        else
        {
            game = null;
        }
        if (game is null)
        {
            ShowToast("Couldn't add that file", "It isn't a program or a shortcut Game Shelf can read.", "OK");
            return;
        }

        if (FindGame(game.Id) is { } known)
        {
            known.Hidden = false;
            game = known;
        }
        else
        {
            _games.Add(game);
        }
        SaveLibrary();
        RefreshViews();
        if (showDetails) ShowDetail(game);
        else ShowToast($"Added {game.Name}", "It's on the shelf now.", "OK");
    }

    private static void TrySave(Action save)
    {
        try { save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void Unhide(Game game)
    {
        if (_toastAction is not null) CloseToast(); // a pending "Hid … · Undo" no longer applies
        game.Hidden = false;
        SaveLibrary();
        RefreshViews();
    }

    private void OnGameAction(Game game, GameAction action)
    {
        switch (action)
        {
            case GameAction.Play:
                Play(game);
                break;
            case GameAction.Details:
                ShowDetail(game);
                break;
            case GameAction.OpenFolder:
                if (GameActions.InstallFolder(game) is { } folder) GameActions.ShellOpen(folder);
                break;
            case GameAction.StorePage:
                if (GameActions.StoreUrl(game) is { } url) GameActions.ShellOpen(url);
                break;
            case GameAction.VerifyFiles:
                if (GameActions.SteamCommand(game, "validate") is { } validate) GameActions.ShellOpen(validate);
                break;
            case GameAction.Uninstall:
                if (GameActions.SteamCommand(game, "uninstall") is { } uninstall) GameActions.ShellOpen(uninstall);
                break;
            case GameAction.ToggleFavorite:
                game.Favorite = !game.Favorite;
                SaveLibrary();
                RefreshViews();
                break;
            case GameAction.Hide:
                HideGame(game);
                break;
        }
    }

    /// <summary>Hides a game from the shelf, with an Undo in the toast.</summary>
    private void HideGame(Game game)
    {
        game.Hidden = true;
        SaveLibrary();
        RefreshViews();

        var id = game.Id;
        ShowToast($"Hid {game.Name}", "It won't show on the shelf anymore.", "Undo", () =>
        {
            if (FindGame(id) is not { } restored) return;
            restored.Hidden = false;
            SaveLibrary();
            RefreshViews();
        });
    }

    /// <summary>
    /// Esc that no control used: back from a page to the shelf, else close the flyout. The search box clears
    /// itself first (see ShelfView).
    /// </summary>
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (Detail.Visibility == Visibility.Visible || SettingsPage.Visibility == Visibility.Visible) ShowShelf();
        else if (ImportPage.Visibility == Visibility.Visible) ImportPage.CancelByEscape();
        else if (IsFlyout) App.Current.ClosePanel(this);
        else return;
        e.Handled = true;
    }

    /// <summary>Type-to-search: printable keys typed anywhere outside a text box go to the search box.</summary>
    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (e.Handled || SetupPage.Visibility == Visibility.Visible) return; // no shelf to search behind the setup
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
        if (focused is TextBox || char.IsControl(e.Character)) return;
        // Space activates a focused button, item or switch; leave it alone.
        if (e.Character == ' ' && focused is ButtonBase or SelectorItem or ToggleSwitch) return;
        ShowShelf(); // typing on the detail view goes back to the grid and searches
        Shelf.TypeIntoSearch(e.Character.ToString());
        e.Handled = true;
    }

    private void Play(Game game)
    {
        App.Current.CancelCountdown();

        if (Launcher.StartGame(game) is { } launchError)
        {
            ShowToast($"Couldn't start {game.Name}", launchError, "OK");
            return;
        }
        if (!Launcher.CanWatch(game))
        {
            // Nothing would tell us when it closes (e.g. a web link shortcut), so hiding would be for good.
            ShowToast($"Started {game.Name}",
                "Staying open: Game Shelf can't tell when this game closes. Choose its program in the game's details to hide automatically.",
                "OK");
            return;
        }
        if (Launcher.StartWatcher(game, App.Settings.ComeBackOnClose) is { } watcherError)
        {
            // Without a watcher nothing would bring the widget back, so stay open.
            ShowToast($"Started {game.Name}", "Staying open: the watcher couldn't start. " + watcherError, "OK");
            return;
        }

        int delay = game.HideDelaySeconds ?? App.Settings.HideDelaySeconds;
        if (delay <= 0)
        {
            App.Current.Quit(); // exit completely: during the game only the watcher remains
            return;
        }

        // The countdown runs in App, so it keeps going if the flyout closes (the game takes the foreground).
        App.Current.StartCountdown(delay);
        ShowCountdown($"Starting {game.Name}");
    }

    private bool _toastIsCountdown;

    private void ShowCountdown(string title)
    {
        ShowToast(title, CountdownText(App.Current.CountdownRemaining), "Keep open");
        _toastIsCountdown = true;
    }

    private void OnCountdownTick(int remaining)
    {
        if (_toastIsCountdown && Toast.Visibility == Visibility.Visible) ToastBar.Message = CountdownText(remaining);
    }

    private static string CountdownText(int remaining) => $"Hiding in {remaining} s";

    private void OnToastButton(object sender, RoutedEventArgs e)
    {
        // "Keep open" cancels the countdown; "OK" dismisses a notice; "Undo" also runs its action.
        if (_toastIsCountdown) App.Current.CancelCountdown();
        var action = _toastAction;
        CloseToast();
        action?.Invoke();
    }

    private void CloseToast()
    {
        _undoTimer?.Stop();
        _undoTimer = null;
        _toastAction = null;
        _toastIsCountdown = false;
        Toast.Visibility = Visibility.Collapsed;
    }

    private void ShowToast(string title, string detail, string button, Action? action = null)
    {
        _undoTimer?.Stop();
        _undoTimer = null;
        _toastAction = action;
        _toastIsCountdown = false;
        if (action is not null)
        {
            _undoTimer = _dispatcher.CreateTimer();
            _undoTimer.Interval = UndoTimeout;
            _undoTimer.IsRepeating = false;
            _undoTimer.Tick += (_, _) =>
            {
                if (!_toastIsCountdown) CloseToast(); // never cut off a launch countdown
            };
            _undoTimer.Start();
        }
        ToastBar.Title = title;
        ToastBar.Message = detail;
        ToastButton.Content = button;
        Toast.Visibility = Visibility.Visible;
    }

    /// <summary>After an unexpected error was caught and logged.</summary>
    public void ShowError() =>
        ShowToast("Something went wrong", $"Game Shelf kept running. Details are in {ErrorLog.FilePath}", "OK");

    public void ShowNotDetected(string gameId)
    {
        var name = _games.FirstOrDefault(g => g.Id == gameId)?.Name ?? "The game";
        ShowToast("Couldn't detect the game",
            $"{name} didn't appear to start, or runs from outside its install folder. Set its process in the game's settings.",
            "OK");
    }

    /// <summary>A game ended while we were still open (e.g. after "Keep open"): show the new session.</summary>
    public void Returned()
    {
        if (App.Current.CountdownRunning)
        {
            // The game ended before the countdown did; don't hide now.
            App.Current.CancelCountdown();
            CloseToast();
        }
        RefreshViews();
    }
}
