using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using GameShelf.Services;
using Microsoft.Win32;

namespace GameShelf;

/// <summary>
/// The hidden window that lives as long as the app (spec §5). It owns the notification-area icon, the global
/// shortcut, and receives commands from second instances. These messages are the only thing that wakes the
/// app while the panel is closed: there are no timers.
///
/// A hidden top-level window rather than a message-only one: only top-level windows can be found by class
/// name and get Explorer's "TaskbarCreated" broadcast (to re-add the icon after Explorer restarts).
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    public const string ClassName = "GameShelf.MessageWindow";
    public const int WM_COPYDATA = 0x004A;
    public static readonly IntPtr CopyDataCommand = new(0x47534846); // "GSHF"

    private const int WM_HOTKEY = 0x0312, WM_SETTINGCHANGE = 0x001A, WM_CONTEXTMENU = 0x007B, WM_NULL = 0;
    private const int TrayCallback = 0x8000 + 1; // WM_APP + 1
    private const int NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401, WM_LBUTTONDBLCLK = 0x0203;
    private long _swallowSelectUntil;
    private const int HotkeyId = 1;
    private const uint TrayId = 1;

    public event Action<string[]>? Command;
    /// <summary>The icon was clicked; true for the second click of a double click.</summary>
    public event Action<bool>? TrayClicked;
    public event Action? TrayMenuRequested;
    public event Action? HotkeyPressed;

    private readonly WndProc _wndProc; // kept alive: Windows holds a pointer to it
    private readonly uint _taskbarCreated;
    private IntPtr _hwnd;
    private IntPtr _trayIcon;
    private bool _trayShown;
    private string _tooltip = "Game Shelf";

    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;

    public MessageWindow()
    {
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _wndProc = Proc;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = ClassName,
        };
        if (RegisterClassExW(ref wc) == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
            throw new Win32Exception();
        _hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, ClassName, "Game Shelf", WS_POPUP, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception();
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    }

    public IntPtr Handle => _hwnd;

    // ---- Notification-area icon --------------------------------------------------------------------------

    public void ShowTrayIcon(bool show)
    {
        if (show == _trayShown) return;
        if (show) AddTrayIcon();
        else RemoveTrayIcon();
    }

    private void AddTrayIcon()
    {
        UpdateTrayImage();
        var data = TrayData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _trayShown = Shell_NotifyIconW(NIM_ADD, ref data);
        data.uVersion = NOTIFYICON_VERSION_4;
        if (_trayShown) Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    private void RemoveTrayIcon()
    {
        var data = TrayData(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _trayShown = false;
    }

    private bool _trayActive;

    /// <summary>The coloured logo while the flyout is open, the monochrome glyph while it's closed.</summary>
    public void SetTrayActive(bool active)
    {
        if (active == _trayActive) return;
        _trayActive = active;
        if (_trayShown) UpdateTrayImage();
    }

    /// <summary>
    /// The coloured logo while open; otherwise a white glyph on dark taskbars and a dark one on light taskbars,
    /// at the small-icon size for this DPI.
    /// </summary>
    private void UpdateTrayImage()
    {
        int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForWindow(_hwnd));
        var resource = _trayActive ? @"Assets\tray-active.ico"
            : TaskbarIsLight() ? @"Assets\tray-light.ico" : @"Assets\tray-dark.ico";
        var icon = IconFromResource(resource, size);
        if (icon == IntPtr.Zero) return;
        if (_trayIcon != IntPtr.Zero) DestroyIcon(_trayIcon);
        _trayIcon = icon;
        if (_trayShown)
        {
            var data = TrayData(NIF_ICON);
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
    }

    private NOTIFYICONDATAW TrayData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = TrayId,
        uFlags = flags,
        uCallbackMessage = TrayCallback,
        hIcon = _trayIcon,
        szTip = _tooltip,
    };

    private static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Makes an HICON from the closest-size entry of an .ico next to the exe (entries are PNG or DIB).</summary>
    private static IntPtr IconFromResource(string resource, int size)
    {
        byte[] ico;
        try { ico = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, resource)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return IntPtr.Zero; }
        if (ico.Length < 6) return IntPtr.Zero;
        int count = BitConverter.ToUInt16(ico, 4);
        int best = -1, bestDiff = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            if (entry + 16 > ico.Length) break;
            int width = ico[entry] == 0 ? 256 : ico[entry];
            int diff = width >= size ? width - size : (size - width) * 4; // prefer scaling down
            if (diff < bestDiff) { bestDiff = diff; best = entry; }
        }
        if (best < 0) return IntPtr.Zero;
        int length = BitConverter.ToInt32(ico, best + 8), offset = BitConverter.ToInt32(ico, best + 12);
        if (offset < 0 || length <= 0 || offset + length > ico.Length) return IntPtr.Zero;
        var bits = ico.AsSpan(offset, length).ToArray();
        return CreateIconFromResourceEx(bits, (uint)bits.Length, true, 0x00030000, size, size, 0);
    }

    /// <summary>The small native menu on right-click (Open, Settings, Quit). Returns the chosen id or 0.</summary>
    public int ShowTrayMenu(IReadOnlyList<(int Id, string Text)> items)
    {
        NativeMenu.FollowSystemTheme(); // dark in dark mode, like Windows' own tray menus
        var menu = CreatePopupMenu();
        try
        {
            foreach (var (id, text) in items)
                AppendMenuW(menu, id == 0 ? MF_SEPARATOR : MF_STRING, (UIntPtr)(uint)id, id == 0 ? null : text);
            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd); // so the menu closes when clicking elsewhere
            int chosen = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, point.X, point.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return chosen;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    // ---- Global shortcut ---------------------------------------------------------------------------------

    /// <summary>Registers the shortcut; false if another app already has it.</summary>
    public bool RegisterHotkey(uint modifiers, uint key)
    {
        UnregisterHotKey(_hwnd, HotkeyId);
        if (key == 0) return true; // shortcut turned off
        return RegisterHotKey(_hwnd, HotkeyId, modifiers | MOD_NOREPEAT, key);
    }

    // ---- Messages ----------------------------------------------------------------------------------------

    private IntPtr Proc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            return IntPtr.Zero;
        }
        if (msg == TrayCallback)
        {
            int ev = (int)(lParam.ToInt64() & 0xFFFF);
            // A quick second click arrives as a double click (on its mouse-down), and Windows may still send
            // NIN_SELECT for that same click when the button comes up: that one is swallowed, once.
            if (ev == WM_LBUTTONDBLCLK)
            {
                _swallowSelectUntil = Environment.TickCount64 + GetDoubleClickTime();
                TrayClicked?.Invoke(true);
            }
            else if (ev == NIN_SELECT && Environment.TickCount64 < _swallowSelectUntil)
            {
                _swallowSelectUntil = 0;
            }
            else if (ev is NIN_SELECT or NIN_KEYSELECT)
            {
                TrayClicked?.Invoke(false);
            }
            else if (ev == WM_CONTEXTMENU) TrayMenuRequested?.Invoke();
            return IntPtr.Zero;
        }
        if (msg == WM_COPYDATA && lParam != IntPtr.Zero)
        {
            var copy = Marshal.PtrToStructure<Program.COPYDATASTRUCT>(lParam);
            if (copy.dwData != CopyDataCommand || copy.cbData < 0 || copy.cbData > 64 * 1024) return IntPtr.Zero;
            var text = copy.cbData == 0 || copy.lpData == IntPtr.Zero ? "" : Marshal.PtrToStringUni(copy.lpData, copy.cbData / 2);
            var args = text.Length == 0 ? [] : text.Split('\0');
            // Handle after returning, so the sending instance isn't kept waiting.
            _dispatcher.TryEnqueue(() => Command?.Invoke(args));
            return new IntPtr(1);
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            if (_trayShown)
            {
                _trayShown = false;
                AddTrayIcon(); // Explorer restarted: the icon is gone, add it again
            }
            return IntPtr.Zero;
        }
        if (msg == WM_SETTINGCHANGE) UpdateTrayImage(); // light/dark taskbar may have changed
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        RemoveTrayIcon();
        if (_trayIcon != IntPtr.Zero) DestroyIcon(_trayIcon);
        UnregisterHotKey(_hwnd, HotkeyId);
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    // ---- Interop -----------------------------------------------------------------------------------------

    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const uint WS_POPUP = 0x80000000;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const int SM_CXSMICON = 49;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;

    private delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? module);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon,
        uint version, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
