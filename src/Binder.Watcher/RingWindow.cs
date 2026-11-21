using System.Runtime.InteropServices;

namespace Binder.Watcher;

/// <summary>
/// The ring of "End a stuck game" (board 40): a disc with an X whose outline fills while the shortcut is held,
/// and a label under it; or a pill with a short note. One top-most layered window of the watcher's own (it never
/// takes focus and lets clicks through), drawn with GDI+ (System32), which is loaded only when it first shows.
/// It exists only while it shows; <see cref="Hide"/> destroys it and shuts GDI+ down again. Follows the Windows
/// app mode and high contrast, sized for the monitor's DPI.
/// </summary>
internal static unsafe partial class RingWindow
{
    // Sizes in DIPs, as on board 40.
    private const float Disc = 168, Track = 140, TrackWidth = 4, SegmentWidth = 12, SegmentGap = 16, Cross = 44, CrossWidth = 4,
        Gap = 14, LabelHeight = 32, LabelPad = 14, PillHeight = 48, TextSize = 13, PillTextSize = 14, Margin = 6;
    private const int WidthDip = 640, HeightDip = (int)(Disc + Gap + LabelHeight + 2 * Margin);

    private static IntPtr _hwnd, _gdiplus, _dc, _dib, _oldBitmap, _bitmap, _graphics, _bits;
    private static int _width, _height, _x, _y, _workLeft, _workRight, _taskbarTop;
    /// <summary>The gap between a note and the taskbar, as the Game mode toast's (its shadow room, 32 DIPs).</summary>
    private const float NoteGap = 32;
    private static float _scale = 1;
    private static Colors _colors;

    /// <summary>Windows' "Animation effects": off fills the ring in quarters and skips the drain.</summary>
    public static bool Animations
    {
        get
        {
            int on = 1;
            return !Native.SystemParametersInfoW(Native.SPI_GETCLIENTAREAANIMATION, 0, &on, 0) || on != 0;
        }
    }

    /// <summary>The ring at <paramref name="progress"/> (0–1), its label "main · sub"; grey when cancelled.</summary>
    public static void ShowRing(double progress, bool cancelled, string main, string sub, IntPtr near)
    {
        if (!Ready(near)) return;
        var c = _colors;
        Native.GdipGraphicsClear(_graphics, 0);
        float s = _scale, cx = _width / 2f, top = Margin * s;
        float d = Disc * s;
        FillEllipse(c.Surface, cx - d / 2, top, d, d);
        DrawEllipse(c.Border, c.HighContrast ? 2 * s : s, cx - d / 2 + s / 2, top + s / 2, d - s, d - s);
        // Four arcs, clockwise from the top: each grows from the thin track to a thick colored stroke in its quarter.
        float t = Track * s, tx = cx - t / 2, ty = top + (d - t) / 2;
        uint full = cancelled ? c.Cancelled : c.Ring;
        for (int i = 0; i < EndHold.Segments; i++)
        {
            double f = EndHold.SegmentFill(progress, i);
            float width = (float)(TrackWidth + (SegmentWidth - TrackWidth) * f) * s;
            var pen = Pen(Blend(c.Track, full, f), width, round: true);
            Native.GdipDrawArc(_graphics, pen, tx, ty, t, t, -90 + i * 90 + SegmentGap / 2, 90 - SegmentGap);
            Native.GdipDeletePen(pen);
        }
        float x = Cross * s * 0.3f, ccy = top + d / 2;
        var cross = Pen(cancelled ? c.Sub : c.Text, CrossWidth * s, round: true);
        Native.GdipDrawLine(_graphics, cross, cx - x, ccy - x, cx + x, ccy + x);
        Native.GdipDrawLine(_graphics, cross, cx + x, ccy - x, cx - x, ccy + x);
        Native.GdipDeletePen(cross);

        Label(main, sub, TextSize, LabelHeight, top + d + Gap * s, dot: false);
        Present();
    }

    /// <summary>
    /// A pill "main · sub" with an info mark, where Binder's other pills show (the Game mode toast): centred above
    /// the taskbar, also an auto-hide one, of the monitor of <paramref name="near"/>.
    /// </summary>
    public static void ShowNote(string main, string sub, IntPtr near)
    {
        if (!Ready(near)) return;
        Native.GdipGraphicsClear(_graphics, 0);
        float s = _scale;
        // The pill sits at the bottom of the surface, its lower edge NoteGap above the taskbar.
        Label(main, sub, PillTextSize, PillHeight, _height - (Margin + PillHeight) * s, dot: true);
        _x = (_workLeft + _workRight - _width) / 2;
        _y = _taskbarTop - (int)(NoteGap * s) - _height + (int)(Margin * s);
        Present();
    }

    public static void Hide()
    {
        if (_hwnd != IntPtr.Zero) Native.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
        if (_graphics != IntPtr.Zero) Native.GdipDeleteGraphics(_graphics);
        if (_bitmap != IntPtr.Zero) Native.GdipDisposeImage(_bitmap);
        _graphics = _bitmap = IntPtr.Zero;
        if (_dc != IntPtr.Zero)
        {
            Native.SelectObject(_dc, _oldBitmap);
            Native.DeleteDC(_dc);
        }
        if (_dib != IntPtr.Zero) Native.DeleteObject(_dib);
        _dc = _dib = _bits = IntPtr.Zero;
        if (_gdiplus == IntPtr.Zero) return;
        Native.GdiplusShutdown(_gdiplus);
        _gdiplus = IntPtr.Zero;
        // GDI+, its fonts and the surface stay paged in (several MB) otherwise: hand them back to Windows now, as
        // after detection. Only the next hold touches them again.
        Footprint.Trim();
    }

    // ---- Drawing helpers --------------------------------------------------------------------------------

    private static void Label(string main, string sub, float size, float height, float top, bool dot)
    {
        var c = _colors;
        float s = _scale;
        var family = Family();
        Native.GdipCreateFont(family, size * s, 1 /* bold */, 2 /* pixels */, out var bold);
        Native.GdipCreateFont(family, size * s, 0, 2, out var regular);
        Native.GdipDeleteFontFamily(family);
        Native.GdipCreateStringFormat(0x1000 /* no wrap */, 0, out var format);
        string rest = " · " + sub;
        float mainWidth = Measure(main, bold, format), restWidth = Measure(rest, regular, format);
        float h = height * s, pad = (dot ? 10 : LabelPad) * s, dotSize = dot ? 28 * s : 0, dotGap = dot ? 12 * s : 0;
        float w = Math.Min(_width - 2 * Margin * s, pad + dotSize + dotGap + mainWidth + restWidth + (dot ? 20 : LabelPad) * s);
        float left = (_width - w) / 2;

        var path = Pill(left, top, w, h);
        var fill = Brush(c.Surface);
        Native.GdipFillPath(_graphics, fill, path);
        Native.GdipDeleteBrush(fill);
        var border = Pen(c.Border, c.HighContrast ? 2 * s : s, round: false);
        Native.GdipDrawPath(_graphics, border, path);
        Native.GdipDeletePen(border);
        Native.GdipDeletePath(path);

        float x = left + pad;
        if (dot)
        {
            float dy = top + (h - dotSize) / 2;
            FillEllipse(c.Dot, x, dy, dotSize, dotSize);
            var mark = Pen(c.Text, 2 * s, round: true);
            float mx = x + dotSize / 2;
            DrawEllipse(c.Text, 1.6f * s, mx - 8 * s, dy + dotSize / 2 - 8 * s, 16 * s, 16 * s);
            Native.GdipDrawLine(_graphics, mark, mx, dy + dotSize / 2 - 1 * s, mx, dy + dotSize / 2 + 4 * s);
            Native.GdipDrawLine(_graphics, mark, mx, dy + dotSize / 2 - 4.4f * s, mx, dy + dotSize / 2 - 4.3f * s);
            Native.GdipDeletePen(mark);
            x += dotSize + dotGap;
        }
        float textTop = top + (h - size * s * 1.36f) / 2;
        Text(main, bold, format, c.Text, x, textTop, mainWidth + 2);
        Text(rest, regular, format, c.Sub, x + mainWidth, textTop, Math.Max(0, left + w - pad - x - mainWidth));
        Native.GdipDeleteStringFormat(format);
        Native.GdipDeleteFont(bold);
        Native.GdipDeleteFont(regular);
    }

    private static IntPtr Family()
    {
        if (Native.GdipCreateFontFamilyFromName("Segoe UI Variable Text", IntPtr.Zero, out var family) == 0) return family;
        Native.GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out family);
        return family;
    }

    private static float Measure(string text, IntPtr font, IntPtr format)
    {
        var layout = new Native.RectF(0, 0, 4000, 400);
        Native.GdipMeasureString(_graphics, text, text.Length, font, ref layout, format, out var box, out _, out _);
        return box.Width;
    }

    private static void Text(string text, IntPtr font, IntPtr format, uint color, float x, float y, float width)
    {
        var brush = Brush(color);
        var rect = new Native.RectF(x, y, width, _height);
        Native.GdipDrawString(_graphics, text, text.Length, font, ref rect, format, brush);
        Native.GdipDeleteBrush(brush);
    }

    private static IntPtr Pill(float x, float y, float w, float h)
    {
        Native.GdipCreatePath(0, out var path);
        Native.GdipAddPathArc(path, x, y, h, h, 90, 180);
        Native.GdipAddPathArc(path, x + w - h, y, h, h, 270, 180);
        Native.GdipClosePathFigure(path);
        return path;
    }

    /// <summary>A color between <paramref name="from"/> and <paramref name="to"/> (ARGB, 0–1).</summary>
    private static uint Blend(uint from, uint to, double f)
    {
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            double a = (from >> shift) & 0xFF, b = (to >> shift) & 0xFF;
            result |= (uint)Math.Round(a + (b - a) * f) << shift;
        }
        return result;
    }

    private static IntPtr Brush(uint argb)
    {
        Native.GdipCreateSolidFill(argb, out var brush);
        return brush;
    }

    private static IntPtr Pen(uint argb, float width, bool round)
    {
        Native.GdipCreatePen1(argb, width, 2, out var pen);
        if (round) Native.GdipSetPenLineCap197819(pen, 2, 2, 0);
        return pen;
    }

    private static void FillEllipse(uint argb, float x, float y, float w, float h)
    {
        var brush = Brush(argb);
        Native.GdipFillEllipse(_graphics, brush, x, y, w, h);
        Native.GdipDeleteBrush(brush);
    }

    private static void DrawEllipse(uint argb, float width, float x, float y, float w, float h)
    {
        var pen = Pen(argb, width, round: false);
        Native.GdipDrawEllipse(_graphics, pen, x, y, w, h);
        Native.GdipDeletePen(pen);
    }

    // ---- The window -------------------------------------------------------------------------------------

    /// <summary>Creates the window and its surface the first time it shows (on the monitor of <paramref name="near"/>).</summary>
    private static bool Ready(IntPtr near)
    {
        if (_hwnd != IntPtr.Zero) return true;
        try
        {
            // Physical pixels on every monitor.
            Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            var monitor = Native.MonitorFromWindow(near, Native.MONITOR_DEFAULTTOPRIMARY);
            var info = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
            if (!Native.GetMonitorInfoW(monitor, &info)) return false;
            _scale = Native.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96f : 1;
            _width = (int)(WidthDip * _scale);
            _height = (int)(HeightDip * _scale);
            _x = (info.rcMonitor.Left + info.rcMonitor.Right - _width) / 2;
            _y = (info.rcMonitor.Top + info.rcMonitor.Bottom - _height) / 2;
            _workLeft = info.rcWork.Left;
            _workRight = info.rcWork.Right;
            _taskbarTop = info.rcWork.Bottom - AutoHideTaskbarHeight(info.rcMonitor);
            _colors = Colors.Now();

            var input = new Native.GdiplusStartupInput { GdiplusVersion = 1 };
            if (Native.GdiplusStartup(out _gdiplus, &input, IntPtr.Zero) != 0) { _gdiplus = IntPtr.Zero; return false; }

            _dc = Native.CreateCompatibleDC(IntPtr.Zero);
            var bmi = new Native.BITMAPINFOHEADER { biSize = (uint)sizeof(Native.BITMAPINFOHEADER), biWidth = _width, biHeight = -_height, biPlanes = 1, biBitCount = 32 };
            _dib = Native.CreateDIBSection(_dc, &bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero) { Hide(); return false; }
            _oldBitmap = Native.SelectObject(_dc, _dib);
            // Premultiplied BGRA top-down, as UpdateLayeredWindow wants: GDI+ draws straight into the DIB.
            if (Native.GdipCreateBitmapFromScan0(_width, _height, _width * 4, Native.PixelFormat32bppPARGB, _bits, out _bitmap) != 0 ||
                Native.GdipGetImageGraphicsContext(_bitmap, out _graphics) != 0) { Hide(); return false; }
            Native.GdipSetSmoothingMode(_graphics, 4);   // anti-alias
            Native.GdipSetPixelOffsetMode(_graphics, 4); // high quality: edges on half pixels, no 0.5 px shift
            Native.GdipSetTextRenderingHint(_graphics, 4); // anti-alias (grayscale: the surface has alpha)

            var instance = Native.GetModuleHandleW(null);
            fixed (char* name = "Binder.Watcher.Ring")
            {
                var wc = new Native.WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                    lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, nuint, nint, nint>)&Proc,
                    hInstance = instance,
                    lpszClassName = name,
                };
                Native.RegisterClassExW(&wc); // already registered from an earlier hold: fine
                _hwnd = Native.CreateWindowExW(Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW |
                    Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT, name, null, Native.WS_POPUP, _x, _y, _width, _height,
                    IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            }
            if (_hwnd == IntPtr.Zero) { Hide(); return false; }
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Hide();
            return false;
        }
    }

    /// <summary>
    /// An auto-hide taskbar at the bottom of this monitor reserves no work area but slides up over it: its height,
    /// else 0 (the widget's Monitors.AutoHideTaskbars, bottom edge only).
    /// </summary>
    private static int AutoHideTaskbarHeight(Native.RECT monitor)
    {
        var data = new Native.APPBARDATA { cbSize = (uint)sizeof(Native.APPBARDATA), uEdge = Native.ABE_BOTTOM, rc = monitor };
        var bar = Native.SHAppBarMessage(Native.ABM_GETAUTOHIDEBAREX, &data);
        if (bar == IntPtr.Zero || !Native.GetWindowRect(bar, out var r)) return 0;
        int size = r.Bottom - r.Top;
        // A hidden taskbar keeps its full size; ignore anything implausible.
        return size > 0 && size <= (monitor.Bottom - monitor.Top) / 3 ? size : 0;
    }

    private static void Present()
    {
        Native.GdipFlush(_graphics, 1);
        var screen = Native.GetDC(IntPtr.Zero);
        var position = new Native.POINT { X = _x, Y = _y };
        var size = new Native.SIZE { Cx = _width, Cy = _height };
        var source = new Native.POINT();
        var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        Native.UpdateLayeredWindow(_hwnd, screen, &position, &size, _dc, &source, 0, &blend, Native.ULW_ALPHA);
        Native.ReleaseDC(IntPtr.Zero, screen);
        Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint Proc(IntPtr hwnd, uint msg, nuint wParam, nint lParam) => Native.DefWindowProcW(hwnd, msg, wParam, lParam);

    /// <summary>The ring's colors (ARGB): board 40's dark and light, or the system's high contrast colors.</summary>
    private readonly record struct Colors(uint Surface, uint Border, uint Track, uint Ring, uint Cancelled, uint Text, uint Sub, uint Dot, bool HighContrast)
    {
        public static Colors Now()
        {
            var hc = new Native.HIGHCONTRASTW { cbSize = (uint)sizeof(Native.HIGHCONTRASTW) };
            if (Native.SystemParametersInfoW(Native.SPI_GETHIGHCONTRAST, hc.cbSize, &hc, 0) && (hc.dwFlags & 1) != 0)
            {
                uint back = Sys(5), text = Sys(8), highlight = Sys(13);
                return new(back, text, back, highlight, text, text, text, back, true);
            }
            return LightApps()
                ? new(0xFFF3F3F3, 0xFFDADADA, 0xFFDADADA, 0xFFC42B1C, 0xFF8A8A8A, 0xFF1B1B1B, 0xFF5C5C5C, 0xFFE3E3E3, false)
                : new(0xFF2C2C30, 0xFF3E3E45, 0xFF3C3C43, 0xFFFF99A4, 0xFF8A8A92, 0xFFF3F3F5, 0xFFA9A9B2, 0xFF38383F, false);
        }

        private static uint Sys(int index)
        {
            uint bgr = Native.GetSysColor(index);
            return 0xFF000000 | ((bgr & 0xFF) << 16) | (bgr & 0xFF00) | ((bgr >> 16) & 0xFF);
        }

        private static bool LightApps()
        {
            uint value = 0, size = sizeof(uint);
            return Native.RegGetValueW(Native.HKEY_CURRENT_USER, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", Native.RRF_RT_REG_DWORD, IntPtr.Zero, &value, &size) == 0 && value != 0;
        }
    }
}

internal static unsafe partial class Native
{
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_LAYERED = 0x80000, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint ULW_ALPHA = 2;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint SPI_GETHIGHCONTRAST = 0x42, SPI_GETCLIENTAREAANIMATION = 0x1042;
    public const int PixelFormat32bppPARGB = 0x000E200B;
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
    public static readonly IntPtr HKEY_CURRENT_USER = unchecked((nint)0x80000001);
    public const uint RRF_RT_REG_DWORD = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct RectF(float x, float y, float width, float height)
    {
        public float X = x, Y = y, Width = width, Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread, SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int Cx, Cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    public const uint ABM_GETAUTOHIDEBAREX = 0x0B, ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage, uEdge;
        public RECT rc;
        public nint lParam;
    }

    [LibraryImport("shell32.dll")]
    public static partial IntPtr SHAppBarMessage(uint message, APPBARDATA* data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIGHCONTRASTW
    {
        public uint cbSize, dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    // user32
    [LibraryImport("user32.dll")]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll")]
    public static partial IntPtr CreateWindowExW(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hwnd, int command);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(IntPtr hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr screenDc, POINT* position, SIZE* size, IntPtr sourceDc,
        POINT* sourcePosition, uint key, BLENDFUNCTION* blend, uint flags);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(IntPtr monitor, MONITORINFO* info);

    [LibraryImport("user32.dll")]
    public static partial IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoW(uint action, uint param, void* value, uint winIni);

    [LibraryImport("user32.dll")]
    public static partial uint GetSysColor(int index);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandleW(string? name);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegGetValueW(IntPtr key, string subKey, string value, uint flags, IntPtr type, void* data, uint* size);

    // gdi32
    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateCompatibleDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateDIBSection(IntPtr dc, BITMAPINFOHEADER* info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr obj);

    // gdiplus (flat API)
    [LibraryImport("gdiplus.dll")]
    public static partial int GdiplusStartup(out IntPtr token, GdiplusStartupInput* input, IntPtr output);

    [LibraryImport("gdiplus.dll")]
    public static partial void GdiplusShutdown(IntPtr token);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, IntPtr scan0, out IntPtr bitmap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteGraphics(IntPtr graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDisposeImage(IntPtr image);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFlush(IntPtr graphics, int intention);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetSmoothingMode(IntPtr graphics, int mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetTextRenderingHint(IntPtr graphics, int hint);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPixelOffsetMode(IntPtr graphics, int mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGraphicsClear(IntPtr graphics, uint color);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateSolidFill(uint color, out IntPtr brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteBrush(IntPtr brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePen1(uint color, float width, int unit, out IntPtr pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenLineCap197819(IntPtr pen, int startCap, int endCap, int dashCap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePen(IntPtr pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillEllipse(IntPtr graphics, IntPtr brush, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawEllipse(IntPtr graphics, IntPtr pen, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawArc(IntPtr graphics, IntPtr pen, float x, float y, float width, float height, float start, float sweep);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawLine(IntPtr graphics, IntPtr pen, float x1, float y1, float x2, float y2);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePath(int fillMode, out IntPtr path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathArc(IntPtr path, float x, float y, float width, float height, float start, float sweep);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipClosePathFigure(IntPtr path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillPath(IntPtr graphics, IntPtr brush, IntPtr path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawPath(IntPtr graphics, IntPtr pen, IntPtr path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePath(IntPtr path);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipCreateFontFamilyFromName(string name, IntPtr collection, out IntPtr family);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteFontFamily(IntPtr family);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateFont(IntPtr family, float size, int style, int unit, out IntPtr font);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteFont(IntPtr font);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateStringFormat(int flags, ushort language, out IntPtr format);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteStringFormat(IntPtr format);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipMeasureString(IntPtr graphics, string text, int length, IntPtr font, ref RectF layout, IntPtr format,
        out RectF box, out int characters, out int lines);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipDrawString(IntPtr graphics, string text, int length, IntPtr font, ref RectF layout, IntPtr format, IntPtr brush);
}
