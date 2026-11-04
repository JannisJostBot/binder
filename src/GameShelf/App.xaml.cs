using System.Runtime.InteropServices;
using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GameShelf;

/// <summary>The library and play history, kept while the panel window comes and goes.</summary>
public sealed class AppState
{
    public List<Game> Games { get; set; } = [];
    public List<Session> Sessions { get; set; } = [];
    /// <summary>Crash checks of sessions that ended with an error (crashes.json, milestone 11).</summary>
    public List<CrashRecord> Crashes { get; set; } = [];

    /// <summary>The stores are scanned once per run (and on Refresh), not every time the flyout opens.</summary>
    public bool Scanned { get; set; }

    /// <summary>Free up space while it's open; mid-uninstall it survives the flyout closing (memory only).</summary>
    public Views.FreeSpaceState? FreeSpace { get; set; }
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
        Autostart.UpdatePathIfEnabled(); // the app may have been moved, or the entry predates --background
        State.Games = Storage.LoadLibrary();
        State.Sessions = Storage.LoadSessions();
        State.Crashes = Storage.LoadCrashes();

        _messages = new MessageWindow();
        _messages.Command += HandleCommand;
        _messages.TrayClicked += OnTrayClicked;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.HotkeyPressed += TogglePanel;
        _messages.ShowTrayIcon(Settings.TrayIcon);
        ApplyHotkey();

        HandleCommand(StartArgs, firstStart: true);
    }

    /// <summary>Arguments from this start, or forwarded from a second start (taskbar pin, Start menu, watcher).</summary>
    private void HandleCommand(string[] args) => HandleCommand(args, firstStart: false);

    private void HandleCommand(string[] args, bool firstStart)
    {
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
        var notice = StartupNotice.Parse(args);
        bool background = args.Contains("--background");

        if (notice is { NotDetected: false })
        {
            // A game ended (the watcher brought us back): pick up the session it wrote.
            State.Sessions = Storage.LoadSessions();
            if (Settings.DisplayMode == DisplayMode.Desktop) OpenPanel();
            else _panel?.Returned();
            if (firstStart && _panel is null) Footprint.Shrink();
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
        else if (background && firstStart) Footprint.Shrink(); // started with Windows: wait in the tray
        else if (firstStart) OpenPanel(); // started from the Start menu or a pin: show it
        else TogglePanel();
        if (firstStart) _ = CheckCrashesAsync(returnedGame: null); // sessions that ended while we weren't running
    }

    /// <summary>
    /// Checks sessions that ended with an error against the Windows logs (milestone 11), off the UI thread, once
    /// each. A problem with the game that just ended opens the panel with its crash card; any other unseen one
    /// shows the next time the panel is open.
    /// </summary>
    private async Task CheckCrashesAsync(string? returnedGame)
    {
        var known = State.Crashes;
        var sessions = State.Sessions;
        var games = State.Games;
        try
        {
            State.Crashes = await Task.Run(() => CrashStore.Update(known, sessions, games));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ExternalException)
        {
            ErrorLog.Write(e, "crash check");
            return;
        }
        if (_quitting) return;
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
        Footprint.Wake();
        if (_panel is null)
        {
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
                _panel.Closing += () => _messages?.SetTrayActive(false);
                _panel.Open();
            }
            // The desktop panel is always there, so only the flyout lights the tray icon up.
            _messages?.SetTrayActive(Settings.DisplayMode == DisplayMode.Flyout);
        }
        _panel.BringForward();
    }

    /// <summary>
    /// Closes the flyout <paramref name="panel"/> if it's still the current one (a late click-away check from a
    /// window that already closed must not close its successor). The desktop panel isn't closed this way.
    /// </summary>
    public void ClosePanel(MainWindow panel, bool byClickAway = false)
    {
        if (_panel is null || _panel != panel || _panel.IsClosing || Settings.DisplayMode == DisplayMode.Desktop) return;
        if (byClickAway) _closedByClickAwayAt = DateTime.UtcNow;
        if (Settings.StayReady && Settings.KeepPanelLoaded && _panel.HideKept())
        {
            // Open faster: the panel stays loaded, hidden; only efficiency mode while it waits.
            _kept = _panel;
            _panel = null;
            Footprint.Rest();
            return;
        }
        _panel.CloseAnimated();
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

    public static string HotkeyLabel(Hotkey hotkey) => hotkey switch
    {
        Hotkey.WinShiftG => "Win+Shift+G",
        Hotkey.WinShiftJ => "Win+Shift+J",
        Hotkey.CtrlAltG => "Ctrl+Alt+G",
        _ => "Off",
    };

    private void ShowTrayMenu()
    {
        if (_messages is null) return;
        const int open = 1, settings = 2, quit = 3;
        int chosen = _messages.ShowTrayMenu([(open, "Open Game Shelf"), (settings, "Settings"), (0, ""), (quit, "Quit")]);
        switch (chosen)
        {
            case open:
                OpenPanel();
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

    public void Quit()
    {
        _quitting = true;
        CancelCountdown();
        _panel?.Close();
        DropKept();
        _messages?.Dispose(); // removes the tray icon right away instead of leaving a ghost until hovered
        _messages = null;
        Exit();
    }

    // ---- Launch countdown (spec: hide N s after a game starts) -------------------------------------------
    // Lives here, not in the window: in Flyout mode the window may close while it counts down.

    public event Action<int>? CountdownTick;
    public int CountdownRemaining { get; private set; }
    public bool CountdownRunning => _countdown is not null;
    private DispatcherQueueTimer? _countdown;

    public void StartCountdown(int seconds)
    {
        CancelCountdown();
        CountdownRemaining = seconds;
        _countdown = _dispatcher.CreateTimer();
        _countdown.Interval = TimeSpan.FromSeconds(1);
        _countdown.Tick += (_, _) =>
        {
            if (--CountdownRemaining <= 0)
            {
                CancelCountdown();
                // Exit completely: during the game only the watcher remains.
                Quit();
                return;
            }
            CountdownTick?.Invoke(CountdownRemaining);
        };
        _countdown.Start();
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
}
