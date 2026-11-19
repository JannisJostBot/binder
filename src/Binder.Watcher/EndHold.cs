using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Binder.Shared;

namespace Binder.Watcher;

/// <summary>
/// "End a stuck game" (spec §28, board 40): while the game runs, holding Binder's shortcut for 2 s ends it.
/// Runs on the system events thread (SystemEvents.cs), whose hidden window takes the shortcut
/// (<c>RegisterHotKey</c>, not a keyboard hook) once the game is found. Nothing runs until it's pressed: then a
/// 30 ms timer reads the keys and draws the ring (<see cref="RingWindow"/>) only while it's held, and stops when
/// it's let go. With "When it stops responding" (the default) it ends a game only while Windows calls its window
/// hung or Windows Error Reporting holds it, checked when the ring would appear and again when it's full.
/// Ends only the game's own processes (the ones the watcher follows, by id and start time) and a WerFault.exe from
/// System32 that holds one of them; never anything else.
/// </summary>
internal static unsafe partial class EndHold
{
    public enum Mode { Off, WhenNotResponding, Always }

    /// <summary>The texts the ring and its notes show, already translated and with the game's name (the widget's).</summary>
    public sealed record Labels(string Ending, string LetGo, string Cancelled, string StillRunning, string Responding,
        string RespondingAgain, string NothingEnded)
    {
        public static readonly Labels Default = new("Ending the game", "let go to cancel", "Cancelled", "The game is still running",
            "The game is responding", "The game is responding again", "nothing was ended");
    }

    public const int DelayMs = 300, HoldMs = 2000, DrainMs = 300, CancelledMs = 1000, NoteMs = 2200, FrameMs = 30;
    private const int HotkeyId = 1, TimerId = 1;

    public static Mode Setting { get; set; } = Mode.Off;
    public static uint Modifiers { get; set; }
    public static uint Key { get; set; }
    public static Labels Text { get; set; } = Labels.Default;

    // ---- What the arguments say (pure, unit-tested) --------------------------------------------------------

    /// <summary>"--end hung|always": anything else is off.</summary>
    internal static Mode ParseMode(string value) => value switch
    {
        "hung" => Mode.WhenNotResponding,
        "always" => Mode.Always,
        _ => Mode.Off,
    };

    /// <summary>
    /// "--end-key &lt;modifiers&gt;,&lt;key&gt;" in decimal: MOD_ALT/CONTROL/SHIFT/WIN only (at least one), a letter
    /// or digit key. False for anything else.
    /// </summary>
    internal static bool ParseKey(string value, out uint modifiers, out uint key)
    {
        modifiers = key = 0;
        var parts = value.Split(',');
        if (parts.Length != 2 || !uint.TryParse(parts[0], out var m) || !uint.TryParse(parts[1], out var k)) return false;
        if (m == 0 || (m & ~0xFu) != 0) return false;
        if (k is not (>= '0' and <= '9' or >= 'A' and <= 'Z')) return false;
        modifiers = m;
        key = k;
        return true;
    }

    public const int MaxLabel = 120, MaxLabels = 2048;

    /// <summary>"--end-text": the seven <see cref="Labels"/> one per line. Null when the count, a length or a character is off.</summary>
    internal static Labels? ParseLabels(string value)
    {
        if (value.Length > MaxLabels) return null;
        var lines = value.Split('\n');
        if (lines.Length != 7) return null;
        foreach (var line in lines)
            if (line.Length is 0 or > MaxLabel || line.Any(char.IsControl)) return null;
        return new Labels(lines[0], lines[1], lines[2], lines[3], lines[4], lines[5], lines[6]);
    }

    /// <summary>The ring's fill: smooth, or (Animation effects off) in quarters.</summary>
    internal static double Shown(double progress, bool animations) =>
        Math.Clamp(animations ? progress : Math.Floor(progress * 4) / 4, 0, 1);

    /// <summary>
    /// The process id a Windows Error Reporting command line is about ("WerFault.exe -u -p 1234 -s 560"), or 0.
    /// </summary>
    internal static uint ReportedPid(string commandLine)
    {
        var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i++)
            if (parts[i] is "-p" or "/p" && uint.TryParse(parts[i + 1], out var pid)) return pid;
        return 0;
    }

    // ---- The game, from the main thread ------------------------------------------------------------------

    private static (uint Pid, long Created)[] _game = [];

    /// <summary>The game's processes, set by the main thread before each wait on them (and after a restart).</summary>
    public static void Follow(List<IntPtr> handles)
    {
        var game = new List<(uint, long)>(handles.Count);
        foreach (var h in handles)
        {
            uint pid = Native.GetProcessId(h);
            if (pid != 0) game.Add((pid, Native.CreationTime(h)));
        }
        Volatile.Write(ref _game, [.. game]);
    }

    private static volatile bool _ended, _wasHung, _wasReporting;

    /// <summary>The user ended the game with the hold (read after the wait on the game returned).</summary>
    public static bool Ended => _ended;
    /// <summary>When it was ended, Windows called its window hung.</summary>
    public static bool WasHung => _wasHung;
    /// <summary>When it was ended, Windows Error Reporting was holding it (it had crashed).</summary>
    public static bool WasReporting => _wasReporting;

    // ---- On the system events thread ---------------------------------------------------------------------

    private enum Phase { Idle, Armed, Holding, Draining, Note, Done }

    private static IntPtr _hwnd;
    private static Phase _phase;
    private static long _pressed, _holdFrom, _drainFrom;
    private static double _progress, _drainStart;
    private static bool _hasKey;
    private static IntPtr _widget; // a widget that has the shortcut: taken again once it exits
    private static readonly List<IntPtr> Minimized = [];

    /// <summary>The handle the thread's loop waits on besides messages (a widget holding the shortcut), or 0.</summary>
    public static IntPtr WaitHandle => _widget;

    /// <summary>The hidden window exists: take the shortcut (the game has been found).</summary>
    public static void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        if (Setting != Mode.Off && Key != 0) TakeKey();
    }

    private static void TakeKey()
    {
        if (_hasKey) return;
        if (Native.RegisterHotKey(_hwnd, HotkeyId, Modifiers | Native.MOD_NOREPEAT, Key))
        {
            _hasKey = true;
            return;
        }
        // The widget still has it (the countdown, Keep open): taken once it exits. Anything else that has it keeps it.
        if (FindWidget() is var widget && widget != IntPtr.Zero) Watch(widget);
    }

    /// <summary>The widget that held the shortcut has exited.</summary>
    public static void WidgetGone()
    {
        Watch(IntPtr.Zero);
        TakeKey();
    }

    private static void Watch(IntPtr widget)
    {
        if (_widget != IntPtr.Zero) Native.CloseHandle(_widget);
        _widget = widget;
    }

    /// <summary>The hidden window's messages for this feature; true when handled.</summary>
    public static bool Handle(uint msg, nuint wParam)
    {
        switch (msg)
        {
            case Native.WM_HOTKEY when wParam == HotkeyId:
                Begin();
                return true;
            case Native.WM_TIMER when wParam == TimerId:
                Tick();
                return true;
            case WatcherNames.ReleaseKey:
                Release((uint)wParam);
                return true;
        }
        return false;
    }

    /// <summary>A widget starting meanwhile wants its shortcut: let go now, take it again once that widget is gone.</summary>
    private static void Release(uint widgetPid)
    {
        if (_phase is not (Phase.Idle or Phase.Done)) Cancel();
        if (_hasKey) Native.UnregisterHotKey(_hwnd, HotkeyId);
        _hasKey = false;
        var h = Native.OpenProcess(Native.SYNCHRONIZE | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, widgetPid);
        if (h == IntPtr.Zero) return;
        if (IsWidget(h)) Watch(h);
        else Native.CloseHandle(h);
    }

    private static void Begin()
    {
        if (_phase != Phase.Idle || Setting == Mode.Off) return;
        // A WM_HOTKEY posted by another program, without the keys really held, does nothing.
        if (!KeysDown()) return;
        _pressed = Environment.TickCount64;
        _phase = Phase.Armed;
        Native.SetTimer(_hwnd, TimerId, FrameMs, IntPtr.Zero);
    }

    private static void Tick()
    {
        long now = Environment.TickCount64;
        switch (_phase)
        {
            case Phase.Armed:
                if (!KeysDown()) { Stop(); return; } // a tap: nothing was shown, nothing happens
                if (now - _pressed < DelayMs) return;
                var seen = Look();
                if (Setting == Mode.WhenNotResponding && !seen.Stuck)
                {
                    Note(Text.Responding);
                    return;
                }
                _phase = Phase.Holding;
                _holdFrom = now;
                _progress = 0;
                MinimizeIfExclusive(seen.Windows);
                RingWindow.ShowRing(0, false, Text.Ending, Text.LetGo, Windowed(seen.Windows));
                return;
            case Phase.Holding:
                if (!KeysDown())
                {
                    if (!RingWindow.Animations) { Cancelled(); return; }
                    _phase = Phase.Draining;
                    _drainFrom = now;
                    _drainStart = _progress;
                    return;
                }
                _progress = (double)(now - _holdFrom) / HoldMs;
                if (_progress >= 1) { Finish(); return; }
                RingWindow.ShowRing(Shown(_progress, RingWindow.Animations), false, Text.Ending, Text.LetGo, IntPtr.Zero);
                return;
            case Phase.Draining:
                double left = _drainStart * (1 - (double)(now - _drainFrom) / DrainMs);
                if (left <= 0) { Cancelled(); return; }
                RingWindow.ShowRing(left, true, Text.Cancelled, Text.StillRunning, IntPtr.Zero);
                return;
            case Phase.Note:
                Stop();
                return;
        }
    }

    private static void Finish()
    {
        var seen = Look();
        if (Setting == Mode.WhenNotResponding && !seen.Stuck)
        {
            RestoreMinimized();
            Note(Text.RespondingAgain); // it recovered during the hold
            return;
        }
        // Set before the game ends: the main thread reads them as soon as its wait on the game returns.
        _wasHung = seen.Hung;
        _wasReporting = seen.Reporters.Count > 0;
        _ended = true;
        Native.KillTimer(_hwnd, TimerId);
        RingWindow.Hide();
        // Windows Error Reporting first: it holds the crashed game until its dump is written.
        foreach (var pid in seen.Reporters) EndReporter(pid);
        bool any = false;
        foreach (var (pid, created) in Volatile.Read(ref _game)) any |= EndGameProcess(pid, created);
        Minimized.Clear();
        if (any)
        {
            _phase = Phase.Done;
            return;
        }
        // Nothing could be ended (protected by its anti-cheat, or gone already): not "ended by you" when it closes.
        _ended = _wasHung = _wasReporting = false;
        _phase = Phase.Idle;
    }

    /// <summary>"Cancelled · [game] is still running" for a second, after letting go early.</summary>
    private static void Cancelled()
    {
        RestoreMinimized();
        _phase = Phase.Note;
        RingWindow.ShowRing(0, true, Text.Cancelled, Text.StillRunning, IntPtr.Zero);
        Native.SetTimer(_hwnd, TimerId, CancelledMs, IntPtr.Zero);
    }

    /// <summary>A pill instead of the ring ("[game] is responding · nothing was ended"), then gone.</summary>
    private static void Note(string text)
    {
        _phase = Phase.Note;
        RingWindow.ShowNote(text, Text.NothingEnded);
        Native.SetTimer(_hwnd, TimerId, NoteMs, IntPtr.Zero);
    }

    private static void Cancel()
    {
        RestoreMinimized();
        Stop();
    }

    private static void Stop()
    {
        Native.KillTimer(_hwnd, TimerId);
        RingWindow.Hide();
        _phase = Phase.Idle;
    }

    // ---- Keys --------------------------------------------------------------------------------------------

    private static bool Down(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>The shortcut's key and every one of its modifiers are held right now.</summary>
    private static bool KeysDown()
    {
        if (!Down((int)Key)) return false;
        if ((Modifiers & Native.MOD_ALT) != 0 && !Down(0x12)) return false;
        if ((Modifiers & Native.MOD_CONTROL) != 0 && !Down(0x11)) return false;
        if ((Modifiers & Native.MOD_SHIFT) != 0 && !Down(0x10)) return false;
        if ((Modifiers & Native.MOD_WIN) != 0 && !Down(0x5B) && !Down(0x5C)) return false;
        return true;
    }

    // ---- What Windows says about the game ----------------------------------------------------------------

    private sealed record Seen(List<IntPtr> Windows, bool Hung, List<uint> Reporters)
    {
        public bool Stuck => Hung || Reporters.Count > 0;
    }

    private static HashSet<uint>? _lookPids;
    private static List<IntPtr>? _lookWindows;

    private static Seen Look()
    {
        var game = Volatile.Read(ref _game);
        var pids = new HashSet<uint>(game.Length);
        foreach (var (pid, _) in game) pids.Add(pid);
        _lookPids = pids;
        _lookWindows = [];
        Native.EnumWindows(&OnWindow, IntPtr.Zero);
        var windows = _lookWindows;
        _lookWindows = null;
        bool hung = false;
        foreach (var w in windows) hung |= Native.IsHungAppWindow(w);
        return new Seen(windows, hung, Reporters(pids));
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(IntPtr hwnd, IntPtr lParam)
    {
        try
        {
            if (_lookWindows is { Count: < 64 } list && Native.IsWindowVisible(hwnd) && Native.GetWindow(hwnd, Native.GW_OWNER) == IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(hwnd, out var pid);
                if (_lookPids!.Contains(pid)) list.Add(hwnd);
            }
        }
        catch (Exception) { } // never across into Windows
        return 1;
    }

    /// <summary>Windows Error Reporting processes (System32's WerFault.exe) holding one of the game's processes.</summary>
    private static List<uint> Reporters(HashSet<uint> game)
    {
        var found = new List<uint>();
        if (game.Count == 0) return found;
        var snapshot = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
        if (snapshot == Native.InvalidHandle) return found;
        try
        {
            var entry = new Native.PROCESSENTRY32W { dwSize = (uint)sizeof(Native.PROCESSENTRY32W) };
            for (bool more = Native.Process32FirstW(snapshot, &entry); more && found.Count < 8; more = Native.Process32NextW(snapshot, &entry))
            {
                var name = new string(entry.szExeFile);
                if (!name.Equals("WerFault.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, entry.th32ProcessID);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (!IsSystemReporter(h)) continue;
                    if (game.Contains(entry.th32ParentProcessID) || game.Contains(ReportedPid(CommandLine(h)))) found.Add(entry.th32ProcessID);
                }
                finally
                {
                    Native.CloseHandle(h);
                }
            }
        }
        finally
        {
            Native.CloseHandle(snapshot);
        }
        return found;
    }

    private static string? ImagePath(IntPtr process)
    {
        var path = new char[1024];
        uint len = (uint)path.Length;
        return Native.QueryFullProcessImageNameW(process, 0, path, ref len) ? new string(path, 0, (int)len) : null;
    }

    private static readonly string ReporterPath = Path.Combine(Environment.SystemDirectory, "WerFault.exe");

    private static bool IsSystemReporter(IntPtr process) =>
        string.Equals(ImagePath(process), ReporterPath, StringComparison.OrdinalIgnoreCase);

    private static bool IsWidget(IntPtr process) =>
        string.Equals(ImagePath(process), Program.WidgetPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>A process's command line (Windows 8.1 and later), or "" when Windows doesn't say.</summary>
    private static string CommandLine(IntPtr process)
    {
        const int Size = 8192;
        var buffer = stackalloc byte[Size];
        if (Native.NtQueryInformationProcess(process, Native.ProcessCommandLineInformation, buffer, Size, out _) != 0) return "";
        var text = (Native.UNICODE_STRING*)buffer;
        if (text->Buffer == IntPtr.Zero || text->Length == 0) return "";
        // The text lies inside the same buffer, right after the header.
        long start = (long)text->Buffer - (long)buffer;
        if (start < sizeof(Native.UNICODE_STRING) || start + text->Length > Size) return "";
        return new string((char*)text->Buffer, 0, text->Length / 2);
    }

    /// <summary>The widget's process (Binder.exe from this folder), opened to wait on, or 0.</summary>
    private static IntPtr FindWidget()
    {
        var pids = new uint[4096];
        if (!Native.EnumProcesses(pids, (uint)(pids.Length * sizeof(uint)), out var bytes)) return IntPtr.Zero;
        for (int i = 0; i < bytes / sizeof(uint); i++)
        {
            var h = Native.OpenProcess(Native.SYNCHRONIZE | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pids[i]);
            if (h == IntPtr.Zero) continue;
            if (IsWidget(h)) return h;
            Native.CloseHandle(h);
        }
        return IntPtr.Zero;
    }

    // ---- Ending -----------------------------------------------------------------------------------------

    /// <summary>Ends one of the game's processes: the same one the watcher found (id and start time), never a reused id.</summary>
    private static bool EndGameProcess(uint pid, long created)
    {
        var h = Native.OpenProcess(Native.PROCESS_TERMINATE | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false; // already gone, or protected (its anti-cheat): the watcher keeps waiting
        bool ended = created != 0 && Native.CreationTime(h) == created && Native.TerminateProcess(h, EndedExitCode);
        Native.CloseHandle(h);
        return ended;
    }

    private static void EndReporter(uint pid)
    {
        var h = Native.OpenProcess(Native.PROCESS_TERMINATE | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return;
        if (IsSystemReporter(h)) Native.TerminateProcess(h, EndedExitCode);
        Native.CloseHandle(h);
    }

    /// <summary>The exit code an ended game gets, as Task Manager's End task gives: not a failure code.</summary>
    public const uint EndedExitCode = 1;

    // ---- Exclusive fullscreen ----------------------------------------------------------------------------

    /// <summary>
    /// A game in exclusive fullscreen owns the screen, and the ring would never show: it's minimized while the
    /// ring shows and restored if the hold is let go.
    /// </summary>
    private static void MinimizeIfExclusive(List<IntPtr> windows)
    {
        if (Native.SHQueryUserNotificationState(out int state) != 0 || state != Native.QUNS_RUNNING_D3D_FULL_SCREEN) return;
        foreach (var w in windows)
            if (Native.ShowWindowAsync(w, Native.SW_MINIMIZE)) Minimized.Add(w);
    }

    private static void RestoreMinimized()
    {
        foreach (var w in Minimized) Native.ShowWindowAsync(w, Native.SW_RESTORE);
        Minimized.Clear();
    }

    /// <summary>The window whose monitor the ring shows on: the game's first, else the one in front.</summary>
    private static IntPtr Windowed(List<IntPtr> windows) => windows.Count > 0 ? windows[0] : Native.GetForegroundWindow();
}

internal static unsafe partial class Native
{
    public const uint WM_HOTKEY = 0x0312, WM_TIMER = 0x0113;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    public const uint PROCESS_TERMINATE = 0x0001;
    public const int SW_MINIMIZE = 6, SW_RESTORE = 9;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const uint TH32CS_SNAPPROCESS = 2;
    public const int ProcessCommandLineInformation = 60;
    public static readonly IntPtr InvalidHandle = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length, MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hwnd, int id);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(IntPtr hwnd, nuint id, uint ms, IntPtr callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(IntPtr hwnd, nuint id);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsHungAppWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindowAsync(IntPtr hwnd, int command);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(IntPtr process, uint exitCode);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32FirstW(IntPtr snapshot, PROCESSENTRY32W* entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32NextW(IntPtr snapshot, PROCESSENTRY32W* entry);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(IntPtr process, int infoClass, void* info, int size, out int returned);
}
