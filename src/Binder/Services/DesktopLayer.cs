using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Binder.Services;

/// <summary>
/// Keeps the widget on the desktop layer: below every other window, like desktop icons.
///
/// Every z-order change of our window (activation, clicks, Show) is redirected to the bottom in
/// WM_WINDOWPOSCHANGING, so clicking the widget never pulls it over other apps. When the desktop itself
/// comes to the foreground (Win+D, clicking the wallpaper), the widget is raised once so it isn't left
/// behind the desktop; the next app that comes forward sends it back down.
///
/// Costs nothing while idle: a message hook on our own window and an out-of-context WinEvent hook that
/// only fires when the foreground window changes. No timers, no polling.
/// </summary>
internal sealed class DesktopLayer : IDisposable
{
    private const uint WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_BOTTOM = new(1), HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);

    /// <summary>Win+D raises the shell's desktop layer after the foreground changes; raise again once it settled.</summary>
    private static readonly TimeSpan RaiseAgainAfter = TimeSpan.FromMilliseconds(400);
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    private readonly IntPtr _hwnd;
    private readonly WindowSubclass _subclass;
    private readonly WindowSubclass.Handler _handler;
    private readonly DispatcherQueue _dispatcher;
    private readonly WinEventProc _onForeground; // kept in a field: the hook holds a pointer to it
    private IntPtr _hook;
    private bool _enabled;
    private bool _desktopForeground;
    private bool _raising;
    private bool _peeking; // raised by the shortcut until another app comes forward
    private DispatcherQueueTimer? _raiseAgain; // exists only for a moment after the desktop came to the front

    public DesktopLayer(IntPtr hwnd, WindowSubclass subclass)
    {
        _hwnd = hwnd;
        _subclass = subclass;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _onForeground = OnForegroundChanged;
        _handler = WndProc;
        subclass.Add(_handler);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value)
            {
                _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground,
                    0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                _desktopForeground = IsDesktop(GetForegroundWindow());
                if (!_desktopForeground) SendToBack();
            }
            else
            {
                Unhook();
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // While the desktop is in front (after Win+D), leave our z-order alone so we stay visible on it.
        if (msg == WM_WINDOWPOSCHANGING && _enabled && !_raising && !_peeking && !_desktopForeground)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & SWP_NOZORDER) == 0 && pos.hwndInsertAfter != HWND_BOTTOM)
            {
                pos.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint eventThread, uint eventTime)
    {
        bool desktop = IsDesktop(hwnd);
        if (_peeking && !desktop)
        {
            _peeking = false;
            SendToBack(); // another app came forward: back behind it
        }
        if (desktop == _desktopForeground) return;
        _desktopForeground = desktop;

        if (desktop)
        {
            // Win+D or a click on the wallpaper: come up so the desktop doesn't cover us. The shell may still
            // be raising its own layer, so do it now and once more when that has settled.
            RaiseAboveDesktop();
            _raiseAgain?.Stop();
            _raiseAgain = _dispatcher.CreateTimer();
            _raiseAgain.Interval = RaiseAgainAfter;
            _raiseAgain.IsRepeating = false;
            _raiseAgain.Tick += (_, _) =>
            {
                _raiseAgain = null;
                if (_desktopForeground) RaiseAboveDesktop();
            };
            _raiseAgain.Start();
        }
        else
        {
            _raiseAgain?.Stop();
            _raiseAgain = null;
            SendToBack(); // an app came forward: go back behind it
        }
    }

    /// <summary>The shortcut in "On the desktop" mode: come in front of other windows until the next app comes forward.</summary>
    public void Peek()
    {
        if (!_enabled) return;
        _peeking = true;
        RaiseAboveDesktop();
    }

    /// <summary>
    /// Topmost then not topmost: lands at the top of the normal windows, above the shell's desktop layer,
    /// without staying "always on top".
    /// </summary>
    private void RaiseAboveDesktop()
    {
        _raising = true;
        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, flags);
        SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        _raising = false;
    }

    private void SendToBack() =>
        SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>The shell's desktop windows: Progman, or the WorkerW that hosts the icons and wallpaper.</summary>
    private static bool IsDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var buffer = new char[16];
        int length = GetClassNameW(hwnd, buffer, buffer.Length);
        var name = new string(buffer, 0, Math.Max(0, length));
        return name is "Progman" or "WorkerW";
    }

    private void Unhook()
    {
        _raiseAgain?.Stop();
        _raiseAgain = null;
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose()
    {
        Unhook();
        _subclass.Remove(_handler);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, [Out] char[] className, int maxCount);
}
