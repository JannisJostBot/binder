using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;

// Our P/Invokes only target Windows system DLLs. Load them from System32 only, never from the app folder:
// if the exe is run from e.g. Downloads, a planted dwmapi.dll there must not be picked up.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Binder.Services;

/// <summary>Win32 window tweaks for the borderless widget.</summary>
internal static unsafe partial class NativeWindow
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOREDRAW = 0x0008, SWP_NOACTIVATE = 0x0010,
        SWP_ASYNCWINDOWPOS = 0x4000;

    /// <summary>The window (HWND) a XAML element is shown in, or zero.</summary>
    public static IntPtr HandleOf(UIElement element) =>
        element.XamlRoot?.ContentIslandEnvironment is { } environment
            ? Win32Interop.GetWindowFromWindowId(environment.AppWindowId)
            : IntPtr.Zero;

    /// <summary>Moves and sizes the window in physical pixels, without touching its z-order.</summary>
    public static void SetBounds(IntPtr hwnd, PixelRect rect) =>
        SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>Moves the window without resizing or redrawing it (the flyout slide).</summary>
    public static void MoveTo(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOREDRAW);

    /// <summary>Like <see cref="MoveTo"/>, but callable from another thread without waiting for the window's thread.</summary>
    public static void MoveToAsync(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOREDRAW | SWP_ASYNCWINDOWPOS);

    /// <summary>Blocks until the compositor presents its next frame (the display's refresh). 0 on success.</summary>
    public static int WaitForCompositorFrame() => DwmFlush();

    /// <summary>The window's rectangle in physical pixels.</summary>
    public static PixelRect GetBounds(IntPtr hwnd) =>
        GetWindowRect(hwnd, out var r) ? new PixelRect(r.Left, r.Top, r.Right, r.Bottom) : default;

    /// <summary>Keeps the window out of the taskbar and Alt+Tab.</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        long style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
    }

    private const int DWMWA_CLOAK = 13;

    /// <summary>
    /// Hides (or shows again) a window that is otherwise shown and active: DWM keeps composing it, so its
    /// content is ready when it appears. Used until the first frame is drawn, which would otherwise flash black.
    /// </summary>
    public static void Cloak(IntPtr hwnd, bool cloak)
    {
        int value = cloak ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, &value, sizeof(int));
    }

    /// <summary>Windows 11 rounded corners. False on Windows 10, which has square windows.</summary>
    public static bool TryApplyRoundedCorners(IntPtr hwnd)
    {
        int corner = DWMWCP_ROUND;
        return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int)) == 0;
    }

    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DONOTROUND = 1;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    /// <summary>
    /// A window with no frame of Windows' own, whose content draws its whole shape on a transparent background
    /// (the Game mode toast): no non-client area at all (so no border line, whatever style WinUI's presenter keeps
    /// giving it), no DWM corners or border, and the DWM frame extended over the window so what the content leaves
    /// empty shows what's behind it. The subclass lives as long as the window. Harmless on Windows 10.
    /// </summary>
    public static WindowSubclass MakeFrameless(IntPtr hwnd)
    {
        var subclass = new WindowSubclass(hwnd);
        subclass.Add(static (IntPtr _, uint msg, IntPtr wParam, IntPtr _, ref bool handled) =>
        {
            // The client area is the whole window rectangle: nothing is left for a frame.
            if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero) handled = true;
            return IntPtr.Zero;
        });
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        int corner = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        uint none = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, &none, sizeof(uint));
        var all = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, &all);
        return subclass;
    }

    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint SWP_NOMOVE = 0x0002, SWP_FRAMECHANGED = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, MARGINS* margins);

    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const nint HTCAPTION = 2;

    /// <summary>Lets the user drag the window with the mouse button that is down now (like a title bar). Returns when released.</summary>
    public static void DragMove(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessageW(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }

    public static bool BringToFront(IntPtr hwnd) => SetForegroundWindow(hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmFlush();

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, void* value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    private static partial long GetWindowLongPtrW(IntPtr hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial long SetWindowLongPtrW(IntPtr hwnd, int index, long value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hwnd, uint msg, nint wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);
}

/// <summary>
/// Sees a window's messages before WinUI does (comctl32 window subclassing): display and setting changes, and
/// the desktop layer's z-order. Handlers run on the window's thread; nothing polls.
/// </summary>
internal sealed unsafe partial class WindowSubclass : IDisposable
{
    public delegate IntPtr Handler(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled);

    private const uint WM_NCDESTROY = 0x0082;
    private readonly IntPtr _hwnd;
    private readonly List<Handler> _handlers = [];
    private GCHandle _self;

    public WindowSubclass(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _self = GCHandle.Alloc(this);
        SetWindowSubclass(hwnd, &Proc, 1, (nuint)(nint)GCHandle.ToIntPtr(_self));
    }

    public void Add(Handler handler) => _handlers.Add(handler);

    public void Remove(Handler handler) => _handlers.Remove(handler);

    [UnmanagedCallersOnly]
    private static IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint refData)
    {
        var self = GCHandle.FromIntPtr((IntPtr)(nint)refData).Target as WindowSubclass;
        if (self is not null)
        {
            try
            {
                foreach (var handler in self._handlers.ToArray())
                {
                    bool handled = false;
                    var result = handler(hwnd, msg, wParam, lParam, ref handled);
                    if (handled) return result;
                }
            }
            catch (Exception e)
            {
                ErrorLog.Write(e, "window"); // an exception must not cross into native code
            }
            if (msg == WM_NCDESTROY) self.Dispose();
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_self.IsAllocated) return;
        RemoveWindowSubclass(_hwnd, &Proc, 1);
        _self.Free();
    }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(IntPtr hwnd, delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, nuint, nuint, IntPtr> proc,
        nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(IntPtr hwnd, delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, nuint, nuint, IntPtr> proc,
        nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
