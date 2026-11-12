using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GameShelf.Watcher;

/// <summary>
/// What Windows tells top-level windows and the watcher, windowless, would otherwise never hear: the PC is shutting
/// down or the user signs out (save the session before the process is ended), and sleep (the time asleep isn't
/// playtime). One hidden tool window on its own thread, blocked in GetMessage: no polling, no CPU while the game runs.
/// </summary>
internal static unsafe partial class SystemEvents
{
    private static readonly Lock Sync = new();
    private static readonly List<(DateTimeOffset From, DateTimeOffset To)> SleepSpans = [];
    private static DateTimeOffset? _asleepSince;
    private static Action? _onEnding;
    private static volatile bool _ending;

    /// <summary>Windows is shutting down or the user is signing out.</summary>
    public static bool Ending => _ending;

    /// <summary>
    /// Starts listening. <paramref name="onEnding"/> runs once, on the listening thread, when the session ends
    /// (Windows allows a few seconds there): it saves the session.
    /// </summary>
    public static void Listen(Action onEnding)
    {
        _onEnding = onEnding;
        var thread = new Thread(Run, 64 * 1024) { IsBackground = true, Name = "System events" };
        thread.Start();
    }

    /// <summary>The times the PC slept since <see cref="Listen"/> (a sleep still going on ends now).</summary>
    public static List<(DateTimeOffset From, DateTimeOffset To)> Sleeps(DateTimeOffset now)
    {
        lock (Sync)
        {
            var list = new List<(DateTimeOffset, DateTimeOffset)>(SleepSpans);
            if (_asleepSince is { } since) list.Add((since, now));
            return list;
        }
    }

    /// <summary>
    /// A session cut at the times the PC slept: the parts the game was really running, in order. Parts under a
    /// second are left out. Pure logic, unit-tested.
    /// </summary>
    internal static List<(DateTimeOffset Start, DateTimeOffset End)> Awake(DateTimeOffset start, DateTimeOffset end,
        IEnumerable<(DateTimeOffset From, DateTimeOffset To)> sleeps)
    {
        var parts = new List<(DateTimeOffset, DateTimeOffset)>();
        var from = start;
        foreach (var (sleepFrom, sleepTo) in sleeps.OrderBy(s => s.From))
        {
            if (sleepTo <= from || sleepFrom >= end) continue;
            if (sleepFrom - from >= TimeSpan.FromSeconds(1)) parts.Add((from, sleepFrom));
            from = sleepTo > from ? sleepTo : from;
        }
        if (end - from >= TimeSpan.FromSeconds(1)) parts.Add((from, end));
        return parts;
    }

    private const uint WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016, WM_POWERBROADCAST = 0x0218;
    private const nuint PBT_APMSUSPEND = 0x4, PBT_APMRESUMESUSPEND = 0x7, PBT_APMRESUMEAUTOMATIC = 0x12;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    private static void Run()
    {
        try
        {
            var instance = GetModuleHandleW(null);
            fixed (char* name = "GameShelf.Watcher.SystemEvents")
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, nuint, nint, nint>)&WndProc,
                    hInstance = instance,
                    lpszClassName = name,
                };
                if (RegisterClassExW(&wc) == 0) return;
                // Top-level (a message-only window doesn't get these broadcasts), never shown, not in Alt+Tab.
                var hwnd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, name, null, WS_POPUP, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) return;
            }
            MSG msg;
            while (GetMessageW(&msg, IntPtr.Zero, 0, 0) > 0) DispatchMessageW(&msg);
        }
        catch (Exception e)
        {
            GameMode.Log("System events: " + e.Message);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(IntPtr hwnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_QUERYENDSESSION:
                    _ending = true; // from now on the main thread saves at once instead of waiting for a restart
                    return 1;
                case WM_ENDSESSION:
                    if (wParam != 0)
                    {
                        _ending = true;
                        Interlocked.Exchange(ref _onEnding, null)?.Invoke();
                    }
                    else
                    {
                        _ending = false; // the shutdown was cancelled
                    }
                    return 0;
                case WM_POWERBROADCAST:
                    lock (Sync)
                    {
                        if (wParam == PBT_APMSUSPEND) _asleepSince ??= DateTimeOffset.Now;
                        else if ((wParam == PBT_APMRESUMEAUTOMATIC || wParam == PBT_APMRESUMESUSPEND) && _asleepSince is { } since)
                        {
                            SleepSpans.Add((since, DateTimeOffset.Now));
                            _asleepSince = null;
                        }
                    }
                    return 1;
            }
        }
        catch (Exception)
        {
            // Never let an exception cross into Windows.
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX, ptY;
        public uint lPrivate;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreateWindowExW(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(MSG* msg, IntPtr hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(MSG* msg);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(IntPtr hwnd, uint msg, nuint wParam, nint lParam);
}
