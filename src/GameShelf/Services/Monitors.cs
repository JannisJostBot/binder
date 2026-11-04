using System.Runtime.InteropServices;

namespace GameShelf.Services;

/// <summary>One display, in physical pixels.</summary>
public sealed record MonitorInfo(IntPtr Handle, string Device, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary, double Scale);

/// <summary>Monitors and the taskbars on them. Only queried when placing the panel, never polled.</summary>
internal static class Monitors
{
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            if (Get(handle) is { } info) list.Add(info);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>The monitor with this device name, else the primary one.</summary>
    public static MonitorInfo Find(string? device)
    {
        var all = All();
        return all.FirstOrDefault(m => device is not null && string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(m => m.IsPrimary)
               ?? all.First();
    }

    /// <summary>The monitor a point is on (or the nearest one).</summary>
    public static MonitorInfo FromPoint(int x, int y) =>
        Get(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST)) ?? Find(null);

    /// <summary>
    /// Thickness of auto-hide taskbars on each edge of the monitor. They don't shrink the work area, so
    /// without this the taskbar would slide over the panel when it pops up.
    /// </summary>
    public static PixelEdges AutoHideTaskbars(MonitorInfo monitor)
    {
        int Thickness(uint edge)
        {
            var data = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                uEdge = edge,
                rc = new RECT { Left = monitor.Bounds.Left, Top = monitor.Bounds.Top, Right = monitor.Bounds.Right, Bottom = monitor.Bounds.Bottom },
            };
            var bar = SHAppBarMessage(ABM_GETAUTOHIDEBAREX, ref data);
            if (bar == IntPtr.Zero || !GetWindowRect(bar, out var r)) return 0;
            int size = edge is ABE_LEFT or ABE_RIGHT ? r.Right - r.Left : r.Bottom - r.Top;
            // A hidden taskbar keeps its full size; ignore anything implausible.
            int limit = (edge is ABE_LEFT or ABE_RIGHT ? monitor.Bounds.Width : monitor.Bounds.Height) / 3;
            return size > 0 && size <= limit ? size : 0;
        }
        return new PixelEdges(Thickness(ABE_LEFT), Thickness(ABE_TOP), Thickness(ABE_RIGHT), Thickness(ABE_BOTTOM));
    }

    private static MonitorInfo? Get(IntPtr handle)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (handle == IntPtr.Zero || !GetMonitorInfoW(handle, ref info)) return null;
        double scale = GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        return new MonitorInfo(handle, info.szDevice, ToRect(info.rcMonitor), ToRect(info.rcWork),
            (info.dwFlags & MONITORINFOF_PRIMARY) != 0, scale);
    }

    private static PixelRect ToRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 1;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint ABM_GETAUTOHIDEBAREX = 0x0000000B;
    private const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
