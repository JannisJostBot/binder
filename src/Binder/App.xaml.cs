using System.Runtime.InteropServices;
using Binder.Models;
using Binder.Services;
using Binder.Shared;
using Binder.ViewModels;
using Binder.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Binder;

/// <summary>The library and play history, kept while the panel window comes and goes.</summary>
public sealed class AppState
{
    public List<Game> Games { get; set; } = [];
    public List<Session> Sessions { get; set; } = [];
    /// <summary>Crash checks of sessions that ended with an error (crashes.json, milestone 11).</summary>
    public List<CrashRecord> Crashes { get; set; } = [];

    /// <summary>
    /// The game whose end started this process (the watcher's "--returned" at a first start): Binder was closed the
    /// whole time it ran. Welcome back says so once (spec §22 d).
    /// </summary>
    public string? ClosedDuringGame { get; set; }

    /// <summary>
    /// The stores are scanned when the panel first opens, on Refresh, and when it opens again more than
    /// <see cref="RescanAfter"/> after the last scan (games uninstalled meanwhile leave the shelf), not every time.
    /// </summary>
    public bool Scanned
    {
        get => _scanned;
        set { _scanned = value; if (value) _scannedAt = Environment.TickCount64; }
    }
    private bool _scanned;
    private long _scannedAt;

    public static readonly TimeSpan RescanAfter = TimeSpan.FromHours(1);

    /// <summary>Whether opening the panel should scan the stores.</summary>
    public bool ScanDue => !_scanned || Environment.TickCount64 - _scannedAt > (long)RescanAfter.TotalMilliseconds;

    /// <summary>The kept settings of games that left the library (removed-games.json), read on first use.</summary>
    public List<Game> RemovedGames => _removed ??= Storage.LoadRemovedGames();
    private List<Game>? _removed;
    public void SetRemovedGames(List<Game> list) => _removed = list;

    /// <summary>
    /// A library scan is running. App-wide, not per window: a panel closed and reopened mid-scan must not start a
    /// second scan alongside the first.
    /// </summary>
    public bool Scanning { get; set; }

    /// <summary>Free up space while it's open; mid-uninstall it survives the flyout closing (memory only).</summary>
    public ViewModels.FreeSpaceState? FreeSpace { get; set; }
}

/// <summary>
/// Owns the app's lifetime (spec §5): the hidden message window (tray icon, shortcut, commands from second
/// instances), the panel window, which in Flyout mode is created on open and fully closed on close, and the
/// launch countdown, which keeps running after the flyout closes.
/// </summary>
public sealed partial class App : Application
{
    public static Settings Settings { get; private set; } = new();
    public static AppState State { get; } = new();

    /// <summary>The command line of this start (set by Program before the app starts).</summary>
    internal static string[] StartArgs { get; set; } = [];

    /// <summary>Open file dialogs and menus: the flyout must not close on click-away while one is up.</summary>
    public static int ModalDepth { get; set; }

    /// <summary>Pin button in the flyout header: keep it open when clicking elsewhere. Not saved.</summary>
    public static bool Pinned { get; set; }

    public static new App Current => (App)Application.Current;

    // UI errors are caught and shown as a toast; a burst of them (e.g. an error on every frame) isn't, so a
    // broken state ends instead of spinning the CPU.
    private const int MaxErrorsPerBurst = 5;
    private static readonly TimeSpan ErrorBurstWindow = TimeSpan.FromSeconds(10);
    private readonly Queue<DateTime> _recentErrors = new();

    private DispatcherQueue _dispatcher = null!;
    private MessageWindow? _messages;
    private MainWindow? _panel;
    private MainWindow? _kept; // the closed flyout, hidden but loaded ("Open faster")
    private bool _quitting;

    // Clicking the tray icon first deactivates (and closes) the flyout, then reports the click: don't reopen.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);
    private DateTime _closedByClickAwayAt;
    private bool _reopenAfterClose;

    public App()
    {
        InitializeComponent();
        // The app lives in the tray between panels: closing the last window doesn't end it, Quit does.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        UnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception, "UI");
            var now = DateTime.UtcNow;
            _recentErrors.Enqueue(now);
            while (_recentErrors.Count > 0 && now - _recentErrors.Peek() > ErrorBurstWindow) _recentErrors.Dequeue();
            if (_recentErrors.Count > MaxErrorsPerBurst) return; // let it end
            args.Handled = true;
            _panel?.ShowError();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ErrorLog.Write(args.ExceptionObject as Exception, "background");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write(args.Exception, "task");
            args.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Settings = Storage.LoadSettings();
        Library.SourceDefaults.Use(Settings.SourceOverrides); // before any scan or cover lookup
        InitUpdates(updated: StartArgs.Contains("--updated")); // a known update, or the pill after one (spec §25)
        Autostart.UpdatePathIfEnabled(); // the app may have been moved, or the entry predates --background
        State.Games = Storage.LoadLibrary();
        State.Sessions = Storage.LoadSessions();
        State.Crashes = Storage.LoadCrashes();
        GameModeService.RecoverIfNeeded(); // Game mode changes left by a session that didn't end normally

        _messages = new MessageWindow();
        _messages.Command += HandleCommand;
        _messages.TrayClicked += OnTrayClicked;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.HotkeyPressed += TogglePanel;
        _messages.ShowTrayIcon(Settings.TrayIcon);
        ApplyHotkey();
        InitGameKeys(); // a number after the shortcut plays a game (spec §26)

        HandleCommand(StartArgs, firstStart: true);
    }

    /// <summary>The debug panel's way in; only Debug builds have a body for it (src/Binder/Dev/App.Dev.cs).</summary>
    partial void DevCommand(string[] args, ref bool handled);

    /// <summary>Arguments from this start, or forwarded from a second start (taskbar pin, Start menu, watcher).</summary>
    private void HandleCommand(string[] args) => HandleCommand(args, firstStart: false);

    private void HandleCommand(string[] args, bool firstStart)
    {
        // Debug builds only: "--dev" opens the debug panel (src/Binder/Dev, left out of Release builds). In a release
        // this partial method has no body, so the compiler removes the call and its arguments entirely.
        bool handledByDev = false;
        DevCommand(args, ref handledByDev);
        if (handledByDev) return;

        if (args.Contains("--memory-report"))
        {
            Footprint.WriteReport(_panel is null ? "closed" : "open");
            return;
        }
        if (args.Contains("--quit"))
        {
            Quit(); // the installer is about to replace or remove us
            return;
        }
        if (PlayArgument(args) is { } playId)
        {
            PlayFromStart(playId, firstStart); // a Start menu shortcut (spec §22 c)
            return;
        }

        var notice = StartupNotice.Parse(args);
        bool background = args.Contains("--background");
        var restored = StartupNotice.Restored(args);

        if (notice is null && restored is not null)
        {
            // Game mode was put back: with "Come back" off (first start) only the toast shows, then we exit. With it
            // on, the watcher's "--returned" was started first but this start can still win the race to become the
            // instance: then it's forwarded here next, so stay.
            _startedForRestore = firstStart && Settings.ComeBackOnClose;
            ShowRestored(restored, exitWhenClosed: firstStart && !Settings.ComeBackOnClose);
            return;
        }
        // "--crashed": the watcher brought us back only for a possible restart ("Come back" off, spec §17).
        _crashedOnly = firstStart && notice is { NotDetected: false } && args.Contains("--crashed");
        if (restored is not null) ShowRestored(restored, exitWhenClosed: _crashedOnly);

        if (notice is null && StartupNotice.Applied(args) is { } applied)
        {
            // Game mode is on (from the watcher, while the countdown runs or the panel was kept open). Program
            // doesn't start the widget for this alone, so this is always a forwarded start: no panel toggle.
            _panel?.GameModeStateChanged(); // the shelf's button shows it running
            if (Shared.RestoreResult.Parse(applied) is { } result) ShowGameModeToast(Views.GameModeToast.Kind.Applied, result);
            return;
        }

        if (notice is { NotDetected: false })
        {
            // A game ended (the watcher brought us back): pick up the session it wrote.
            State.Sessions = Storage.ReloadSessions(State.Sessions); // only what was appended since
            bool cameBack = firstStart || _startedForRestore; // Binder wasn't running while the game was
            _startedForRestore = false;
            if (cameBack) State.ClosedDuringGame = notice.GameId;
            if (!_crashedOnly) // else only the restart pill (or the crash card), after the check
            {
                // A panel that was up shows the new session (and on the desktop, one opened now does that itself).
                bool shown = _panel is not null;
                if (Settings.DisplayMode == DisplayMode.Desktop) OpenPanel();
                if (shown) _panel?.Returned(notice.GameId);
            }
            // The game ended before its countdown did: stay, also while the flyout is closed (the game took the
            // foreground), so the crash check and Welcome back aren't cut off.
            if (CountdownRunning && CountdownGameId == notice.GameId) CancelCountdown();
            if (cameBack && _panel is null) Footprint.Shrink();
            _ = CheckCrashesAsync(returnedGame: notice.GameId);
            return;
        }
        if (notice is { NotDetected: true })
        {
            OpenPanel();
            _panel?.ShowNotDetected(notice.GameId);
            return;
        }

        if (Settings.DisplayMode == DisplayMode.Desktop || !Settings.SetupDone) OpenPanel();
        else if (background && firstStart)
        {
            if (TryInstallOnClose()) return; // "Install automatically": a downloaded update installs at sign-in
            Footprint.Shrink(); // started with Windows: wait in the tray
        }
        else if (firstStart) OpenPanel(); // started from the Start menu or a pin: show it
        else TogglePanel();
        if (firstStart) _ = CheckCrashesAsync(returnedGame: null); // sessions that ended while we weren't running
    }

    private Views.GameModeToast? _gameModeToast;

    /// <summary>This process started for Game mode's result while "Come back" is on: the "--returned" follows.</summary>
    private bool _startedForRestore;

    /// <summary>The debug panel's slow motion for the toast; only Debug builds have a body for it (App.Dev.cs).</summary>
    partial void DevToastSpeed(ref float slow);

    /// <summary>Game mode's "Restored" toast (board 4), and the Game mode page's last result if it's open.</summary>
    private void ShowRestored(string encoded, bool exitWhenClosed)
    {
        _panel?.GameModeStateChanged();
        if (Shared.RestoreResult.Parse(encoded) is not { } result)
        {
            if (exitWhenClosed) Quit();
            return;
        }
        ShowGameModeToast(Views.GameModeToast.Kind.Restored, result, exitWhenClosed);
        if (result.Success && Settings.RestoreSound) PlayRestoredSound(); // a calm sound, never for a problem
    }

    private int _sound; // which sound is playing (a newer one replaces it); 0 = none
    private bool _quitAfterSound;

    /// <summary>
    /// Game mode's "back to normal" sound (board 23). It's made on a background thread; the process stays until it
    /// has played (the toast may close first), then its buffer is freed. The Game mode page plays it too when its
    /// switch is turned on, so it can be heard.
    /// </summary>
    public async void PlayRestoredSound()
    {
        int id = ++_sound;
        try
        {
            if (await Task.Run(Services.Sound.PlayRestored) is { } length) await Task.Delay(length);
        }
        catch (Exception e) when (e is OutOfMemoryException or ExternalException)
        {
            ErrorLog.Write(e, "restored sound");
        }
        if (id != _sound) return; // a newer one is playing; it frees the buffer
        _sound = 0;
        Services.Sound.Release();
        if (_quitAfterSound && !_quitting) Quit();
    }

    /// <summary>One Game mode toast at a time; a newer one replaces it.</summary>
    private void ShowGameModeToast(Views.GameModeToast.Kind kind, Shared.RestoreResult result, bool exitWhenClosed = false)
    {
        _gameModeToast?.Close();
        float slow = 1;
        DevToastSpeed(ref slow);
        var toast = new Views.GameModeToast(kind, result, slow);
        Views.WindowClosed.Once(toast, () =>
        {
            if (ReferenceEquals(_gameModeToast, toast)) _gameModeToast = null;
            if (exitWhenClosed) QuitIfIdle();
        });
        _gameModeToast = toast;
        toast.ShowQuietly();
    }

    /// <summary>
    /// Checks sessions that ended with an error against the Windows logs (milestone 11), off the UI thread, once
    /// each. A crash of the game that just ended restarts it when it has "Restart if it crashes" (spec §17), else
    /// opens the panel with its crash card; any other unseen one shows the next time the panel is open.
    /// </summary>
    private async Task CheckCrashesAsync(string? returnedGame)
    {
        // One check at a time, in order: two games ending close together must not check from the same list and
        // overwrite each other's results.
        var previous = _crashCheck;
        var done = new TaskCompletionSource();
        _crashCheck = done.Task;
        _crashChecks++;
        try
        {
            await previous;
            await CheckCrashesNowAsync(returnedGame);
        }
        catch (Exception e)
        {
            // A background check: whatever went wrong, it's logged and the app carries on (and can still exit).
            ErrorLog.Write(e, "crash check");
        }
        finally
        {
            _crashChecks--;
            done.SetResult();
        }
        if (_crashedOnly) QuitIfIdle(); // nothing to restart or show: gone again, as with "Come back" off
    }

    private Task _crashCheck = Task.CompletedTask;
    /// <summary>Crash checks running or waiting their turn.</summary>
    private int _crashChecks;

    private async Task CheckCrashesNowAsync(string? returnedGame)
    {
        // Copies: the library and the records change on the UI thread while the check reads them.
        var known = State.Crashes.ToList();
        var sessions = State.Sessions;
        var games = State.Games.ToList();
        State.Crashes = await Task.Run(() => CrashStore.Update(known, sessions, games));
        if (_quitting) return;
        var last = returnedGame is null ? null : State.Sessions.Where(s => s.GameId == returnedGame).MaxBy(s => s.Start);
        if (last is not null && TryRestart(CrashStore.For(State.Crashes, last), returnedGame)) return;
        var unseen = CrashStore.Unseen(State.Crashes, State.Sessions);
        if (unseen is not null && unseen.GameId == returnedGame) OpenPanel();
        _panel?.CrashesChecked(unseen);
    }

    /// <summary>
    /// A click toggles the panel. The second click of a double click only ever opens it: after a first click
    /// that closed it, a quick second one brings it back, and a habitual double-click leaves it open.
    /// </summary>
    private void OnTrayClicked(bool doubleClick)
    {
        if (!doubleClick || Settings.DisplayMode == DisplayMode.Desktop)
        {
            TogglePanel();
            return;
        }
        if (_panel is { IsClosing: true }) _reopenAfterClose = true;
        else if (_panel is null) OpenPanel();
    }

    public void TogglePanel()
    {
        if (Settings.DisplayMode == DisplayMode.Desktop)
        {
            if (_panel is null) OpenPanel();
            else _panel.Peek(); // bring the desktop panel in front of other windows for a moment
            return;
        }
        if (_panel is { IsClosing: true })
        {
            _reopenAfterClose = true; // pressed again while it was still closing: open once it's gone
            return;
        }
        if (_panel is not null)
        {
            ClosePanel(_panel);
            return;
        }
        // The click on the tray icon that just closed the panel (by taking focus from it) must not reopen it.
        // Only that one click: the guard is used up, so a deliberate quick second click opens it again.
        if (DateTime.UtcNow - _closedByClickAwayAt < ReopenGuard)
        {
            _closedByClickAwayAt = default;
            return;
        }
        OpenPanel();
    }

    public void OpenPanel()
    {
        _reopenAfterClose = false;
        CloseFlash(); // a game shortcut's wait gives way to the panel
        Footprint.Wake();
        if (_panel is null)
        {
            CoverCache.PanelOpened(); // covers that couldn't be downloaded last time (offline) are tried again
            if (_kept is { } kept)
            {
                _kept = null;
                _panel = kept;
                kept.Reopen();
            }
            else
            {
                _panel = new MainWindow();
                _panel.Closed += OnPanelClosed;
                _panel.Closing += () =>
                {
                    _messages?.SetTrayActive(false);
                    CancelUpdateDownload(); // nothing downloads while the panel is closed
                };
                _panel.Open();
            }
            // The desktop panel is always there, so only the flyout lights the tray icon up.
            _messages?.SetTrayActive(Settings.DisplayMode == DisplayMode.Flyout);
        }
        _panel.BringForward();
        RestartGivesWayToPanel(); // opened while a restart counted down: the crash card instead
    }

    /// <summary>
    /// Closes the flyout <paramref name="panel"/> if it's still the current one (a late click-away check from a
    /// window that already closed must not close its successor). The desktop panel isn't closed this way.
    /// </summary>
    public void ClosePanel(MainWindow panel, bool byClickAway = false)
    {
        if (_panel is null || _panel != panel || _panel.IsClosing || Settings.DisplayMode == DisplayMode.Desktop) return;
        if (byClickAway) _closedByClickAwayAt = DateTime.UtcNow;
        // An update that installs by itself goes in when the panel closes: closed for real, not kept hidden.
        if (Settings.StayReady && Settings.KeepPanelLoaded && !InstallDueOnClose && _panel.HideKept())
        {
            // Open faster: the panel stays loaded, hidden; only efficiency mode while it waits.
            CancelUpdateDownload();
            _kept = _panel;
            _panel = null;
            Footprint.Rest();
            return;
        }
        _panel.CloseAnimated();
    }

    /// <summary>A scan started by a panel that has closed since finished: the open panel, if any, shows its result.</summary>
    public void LibraryRescanned()
    {
        if (_panel is { IsClosing: false } panel) _ = panel.ShowRescanned();
    }

    /// <summary>Closes the kept (hidden) flyout, if any, so the next open builds a new one.</summary>
    private void DropKept()
    {
        var kept = _kept;
        _kept = null;
        kept?.Close();
    }

    private void OnPanelClosed(object sender, WindowEventArgs e)
    {
        ((Window)sender).Closed -= OnPanelClosed;
        if (ReferenceEquals(sender, _kept)) _kept = null;
        if (!ReferenceEquals(sender, _panel)) return;
        _panel = null;
        if (_quitting) return;
        if (_reopenAfterClose)
        {
            _reopenAfterClose = false;
            OpenPanel();
            return;
        }
        if (Settings.DisplayMode == DisplayMode.Flyout && TryInstallOnClose()) return; // starts the setup and exits
        if (Settings.DisplayMode == DisplayMode.Flyout && Settings.StayReady)
        {
            // Stay ready at the lowest footprint: views and covers are gone with the window; compact and trim
            // once WinUI has finished tearing the window down (one shot, not a recurring timer).
            CoverCache.ReleaseMemory();
            ScheduleShrink();
        }
        else
        {
            Quit();
        }
    }

    private DispatcherQueueTimer? _shrinkTimer;

    private void ScheduleShrink()
    {
        if (_shrinkTimer is null)
        {
            _shrinkTimer = _dispatcher.CreateTimer();
            _shrinkTimer.Interval = TimeSpan.FromSeconds(1);
            _shrinkTimer.IsRepeating = false;
            _shrinkTimer.Tick += (_, _) =>
            {
                if (_panel is null && _kept is null) Footprint.Shrink();
            };
        }
        _shrinkTimer.Stop();
        _shrinkTimer.Start();
    }

    /// <summary>Flyout ⇄ On the desktop: rebuild the panel window in the new mode.</summary>
    public void ApplyDisplayMode()
    {
        DropKept();
        if (_panel is not null)
        {
            var panel = _panel;
            panel.Closed -= OnPanelClosed;
            _panel = null;
            panel.Close();
        }
        OpenPanel();
    }

    public void ApplyTrayIcon() => _messages?.ShowTrayIcon(Settings.TrayIcon);

    /// <summary>Registers the chosen shortcut. Returns false if another app already uses it.</summary>
    public bool ApplyHotkey()
    {
        var (modifiers, key) = HotkeyKeys(Settings.Hotkey);
        HotkeyFree = _messages?.RegisterHotkey(modifiers, key) ?? false;
        ApplyGameKeys(); // they follow the shortcut (none while it's off)
        return HotkeyFree;
    }

    /// <summary>Whether the current shortcut was registered (false: another app has it).</summary>
    public static bool HotkeyFree { get; private set; }

    public static (uint Modifiers, uint Key) HotkeyKeys(Hotkey hotkey) => hotkey switch
    {
        Hotkey.WinShiftG => (MessageWindow.MOD_WIN | MessageWindow.MOD_SHIFT, 'G'),
        Hotkey.WinShiftJ => (MessageWindow.MOD_WIN | MessageWindow.MOD_SHIFT, 'J'),
        Hotkey.CtrlAltG => (MessageWindow.MOD_CONTROL | MessageWindow.MOD_ALT, 'G'),
        _ => (0, 0),
    };

    private void ShowTrayMenu()
    {
        if (_messages is null) return;
        const int open = 1, settings = 2, quit = 3, gameMode = 4, freeSpace = 5, update = 6;
        // Read when the menu opens (one small file), not kept up to date while it's closed.
        bool needsUpdate = GameModeService.HelperNeedsUpdate(Settings, State.Games);
        // The launchers that have games on the shelf and are installed, in chip order (spec §21 b). The game list
        // is in memory; only their paths are read, now.
        var launchers = ShelfViewModel.LaunchersByGames(State.Games.Where(g => !g.Hidden && Settings.IsSourceEnabled(g.Source)))
            .Where(GameActions.CanOpenLauncher)
            .ToArray();
        const int firstLauncher = 100;
        List<(int Id, string Text, bool Checked)> items = [];
        if (UpdateMenuText is { } updateText) items.AddRange([(update, updateText, false), (0, "", false)]); // spec §25
        items.AddRange(
        [
            (open, Strings.OpenBinder, false), (0, "", false),
            (gameMode, needsUpdate ? Strings.GameModeNeedsUpdateMenu : Strings.GameModeTray, Settings.GameModeEnabled),
            (freeSpace, Strings.FreeUpSpaceMenu, false), (0, "", false),
        ]);
        if (launchers.Length > 0)
        {
            for (int i = 0; i < launchers.Length; i++)
                items.Add((firstLauncher + i, Strings.Format(Strings.OpenLauncher, Format.LauncherAppName(launchers[i])), false));
            items.Add((0, "", false));
        }
        items.Add((settings, Strings.Settings, false));
        items.Add((quit, Strings.Quit, false));
        int chosen = _messages.ShowTrayMenu(items);
        if (chosen >= firstLauncher && chosen - firstLauncher < launchers.Length)
        {
            GameActions.OpenLauncher(launchers[chosen - firstLauncher]);
            return;
        }
        switch (chosen)
        {
            case open:
                OpenPanel();
                break;
            case update:
                OpenPanel();
                _panel?.StartUpdateFromMenu();
                break;
            case gameMode when needsUpdate:
                OpenPanel();
                _panel?.OpenGameMode();
                break;
            case gameMode:
                SetGameMode(!Settings.GameModeEnabled);
                break;
            case freeSpace:
                OpenPanel();
                _panel?.OpenFreeSpace();
                break;
            case settings:
                OpenPanel();
                _panel?.OpenSettings();
                break;
            case quit:
                Quit();
                break;
        }
    }

    /// <summary>The tray menu's Game mode item: through the panel if it's loaded, else straight to settings.json.</summary>
    private void SetGameMode(bool on)
    {
        if (_panel is { IsClosing: false } panel)
        {
            panel.SetGameMode(on, toast: false); // the check mark in the menu is the confirmation
            return;
        }
        Settings.GameModeEnabled = on;
        try { Storage.SaveSettings(Settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Quit()
    {
        _quitting = true;
        CancelCountdown();
        CloseFlash();
        _panel?.Close();
        _gameModeToast?.Close();
        _restartToast?.Close();
        DropKept();
        _messages?.Dispose(); // removes the tray icon right away instead of leaving a ghost until hovered
        _messages = null;
        Exit();
    }

    // ---- Launch countdown (spec: hide N s after a game starts) -------------------------------------------
    // Lives here, not in the window: in Flyout mode the window may close while it counts down.

    public event Action<int>? CountdownTick;
    public int CountdownRemaining { get; private set; }
    /// <summary>The countdown's length, for its progress bar.</summary>
    public int CountdownTotal { get; private set; }
    /// <summary>"Starting [Game]", kept for a flyout that reopens while it counts.</summary>
    public string CountdownTitle { get; private set; } = "";
    public bool CountdownRunning => _countdown is not null;
    /// <summary>The game the countdown is for (another game ending meanwhile doesn't stop it).</summary>
    public string? CountdownGameId { get; private set; }
    private DispatcherQueueTimer? _countdown;
    /// <summary>How long the countdown's end waits for the watcher's return of a game that just closed.</summary>
    private const int ReturnWaitSeconds = 8;

    public void StartCountdown(int seconds, string title, string gameId)
    {
        CancelCountdown();
        CountdownGameId = gameId;
        CountdownRemaining = seconds;
        CountdownTotal = seconds;
        CountdownTitle = title;
        var timer = _countdown = _dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        int waited = 0; // seconds waited after the end for a game that closed meanwhile
        timer.Tick += (_, _) =>
        {
            if (CountdownRemaining > 0 && --CountdownRemaining > 0)
            {
                CountdownTick?.Invoke(CountdownRemaining);
                return;
            }
            CountdownRemaining = 0;
            // Exit completely: during the game only the watcher remains. Commands forwarded just now run first
            // (they're queued ahead): the watcher's "--returned" for a game that closed at once cancels this.
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (!ReferenceEquals(_countdown, timer)) return; // cancelled meanwhile
                // The game closed just before (its watcher looks for a restart for a few seconds, then brings us
                // back): wait for that "--returned" instead of quitting. A game that came back, or no "--returned"
                // in time ("Come back" off), ends the wait. A game that hasn't started yet doesn't start it.
                var ended = WatcherNames.GameEnded(gameId);
                if (waited == 0 ? ended == true : ended != false && waited < ReturnWaitSeconds)
                {
                    waited++;
                    return;
                }
                CancelCountdown();
                Quit();
            });
        };
        timer.Start();
    }

    public void CancelCountdown()
    {
        _countdown?.Stop();
        _countdown = null;
    }
}

/// <summary>Why the widget was started, from the watcher's relaunch arguments.</summary>
public sealed record StartupNotice(bool NotDetected, string GameId)
{
    public static StartupNotice? Parse(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--not-detected") return new StartupNotice(true, args[1]);
        if (args.Length >= 2 && args[0] == "--returned") return new StartupNotice(false, args[1]);
        return null;
    }

    /// <summary>Game mode's restore result ("--restored 5/5"), on its own or after the notice.</summary>
    public static string? Restored(string[] args)
    {
        int i = Array.IndexOf(args, "--restored");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>What Game mode changed when the game started ("--applied 3/3").</summary>
    public static string? Applied(string[] args)
    {
        int i = Array.IndexOf(args, "--applied");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
