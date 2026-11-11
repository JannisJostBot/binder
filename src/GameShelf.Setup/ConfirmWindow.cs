using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GameShelf.Setup;

/// <summary>
/// The install / update screen, laid out like a classic Windows setup window: the app's icon and a title, one line
/// on what happens, the full disclaimer in a scrollable box with the checkbox right under it, where it's installed,
/// and the buttons in the grey band at the bottom. A Win32 dialog built from an in-memory template, standard
/// controls only: the dialog manager does Tab, Enter, Esc, Alt+letter and DPI scaling. The install button stays
/// disabled until the checkbox is ticked.
/// </summary>
internal static unsafe partial class ConfirmWindow
{
    public sealed record Content(string Title, string Summary, string Disclaimer, string Note, string Button);

    private const int IdIcon = 200, IdTitle = 201, IdSummary = 202, IdDisclaimer = 203, IdAccept = 204, IdNote = 205, IdRead = 206;
    private const int IdInstall = 1; // IDOK: Enter presses it (when enabled)
    private const int IdCancel = 2;  // IDCANCEL: Esc and the close box

    // Layout in dialog units (Segoe UI 9 pt: about 1.75 px across and 2 px down at 100 %).
    private const short Width = 300, Height = 232, Margin = 10, BandTop = 204, ButtonHeight = 14;

    // The dialog procedure runs on this thread only, while Show is in it.
    private static Content? _content;
    private static IntPtr _titleFont;
    private static IntPtr _icon;

    /// <summary>True for install, false for Cancel; null if the window couldn't be created (use a task dialog then).</summary>
    public static bool? Show(Content content)
    {
        _content = content;
        try
        {
            var template = BuildTemplate(content.Button);
            fixed (byte* p = template)
            {
                var result = DialogBoxIndirectParamW(Native.GetModuleHandle(null), p, IntPtr.Zero, &DialogProc, IntPtr.Zero);
                if (result <= 0) // -1 or 0: the window wasn't created
                {
                    Locations.Log($"setup window failed: {Marshal.GetLastPInvokeError()}");
                    return null;
                }
                return result == IdInstall;
            }
        }
        finally
        {
            _content = null;
            if (_titleFont != IntPtr.Zero) DeleteObject(_titleFont);
            if (_icon != IntPtr.Zero) DestroyIcon(_icon);
            _titleFont = _icon = IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr DialogProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_INITDIALOG:
                    Init(hwnd);
                    return 0; // focus was set here
                case WM_COMMAND:
                    int id = (int)(wParam & 0xFFFF);
                    if (id == IdAccept)
                        EnableWindow(GetDlgItem(hwnd, IdInstall), IsDlgButtonChecked(hwnd, IdAccept) == BST_CHECKED);
                    else if (id == IdInstall && IsDlgButtonChecked(hwnd, IdAccept) == BST_CHECKED)
                        EndDialog(hwnd, IdInstall);
                    else if (id == IdCancel)
                        EndDialog(hwnd, IdCancel);
                    return 1;
                case WM_CTLCOLORDLG:
                    return GetSysColorBrush(COLOR_WINDOW);
                case WM_CTLCOLORSTATIC: // labels, the checkbox and the read-only disclaimer box
                    SetBkColor(wParam, GetSysColor(COLOR_WINDOW));
                    SetTextColor(wParam, GetSysColor(COLOR_WINDOWTEXT));
                    return GetSysColorBrush(COLOR_WINDOW);
                case WM_CTLCOLORBTN: // around the push buttons: the band's colour
                    return GetSysColorBrush(COLOR_BTNFACE);
                case WM_PAINT:
                    PaintBand(hwnd);
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Locations.Log("setup window: " + ex);
        }
        return 0;
    }

    private static void Init(IntPtr hwnd)
    {
        var content = _content!;
        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;

        // The app's icon, at the size Windows uses for a dialog's main icon (32 px at 100 %).
        int iconSize = (int)(32 * dpi / 96);
        try
        {
            if (LoadIconWithScaleDown(Native.GetModuleHandle(null), AppIconId, iconSize, iconSize, out _icon) >= 0)
            {
                SendMessageW(GetDlgItem(hwnd, IdIcon), STM_SETICON, _icon, IntPtr.Zero);
                SendMessageW(hwnd, WM_SETICON, ICON_BIG, _icon);
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Common Controls 6 isn't active: no icon, the rest still works.
        }

        // The title in the task dialogs' "main instruction" size (12 pt), semibold like Windows 11's headings.
        fixed (char* face = "Segoe UI")
            _titleFont = CreateFontW(-(int)(12 * dpi / 72), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, face);
        if (_titleFont != IntPtr.Zero) SendMessageW(GetDlgItem(hwnd, IdTitle), WM_SETFONT, _titleFont, 1);

        SetText(hwnd, IdTitle, content.Title);
        SetText(hwnd, IdSummary, content.Summary);
        SetText(hwnd, IdDisclaimer, content.Disclaimer.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        SetText(hwnd, IdNote, content.Note);

        // Read from the top, with a little room inside the box's edge.
        var box = GetDlgItem(hwnd, IdDisclaimer);
        int inset = (int)(6 * dpi / 96);
        SendMessageW(box, EM_SETMARGINS, EC_LEFTMARGIN | EC_RIGHTMARGIN, (inset & 0xFFFF) | (inset << 16));
        SendMessageW(box, EM_SETSEL, 0, 0);
        SendMessageW(box, WM_VSCROLL, SB_TOP, 0);

        // Like Windows' own dialogs: access-key underlines and the focus box only once the keyboard is used.
        SendMessageW(hwnd, WM_CHANGEUISTATE, UIS_INITIALIZE | (UISF_HIDEFOCUS | UISF_HIDEACCEL) << 16, 0);
        SetFocus(GetDlgItem(hwnd, IdAccept));
    }

    private static void SetText(IntPtr hwnd, int id, string text)
    {
        fixed (char* p = text) SetWindowTextW(GetDlgItem(hwnd, id), p);
    }

    /// <summary>The grey band behind the buttons with a line above it, as in Windows' own dialogs.</summary>
    private static void PaintBand(IntPtr hwnd)
    {
        PAINTSTRUCT ps;
        var dc = BeginPaint(hwnd, &ps);
        RECT client, band = new() { left = 0, top = BandTop, right = Width, bottom = Height };
        GetClientRect(hwnd, &client);
        MapDialogRect(hwnd, &band);
        band.left = 0;
        band.right = client.right;
        band.bottom = client.bottom;
        FillRect(dc, &band, GetSysColorBrush(COLOR_BTNFACE));
        var line = band with { bottom = band.top + 1 };
        FillRect(dc, &line, GetSysColorBrush(COLOR_3DLIGHT));
        EndPaint(hwnd, &ps);
    }

    // ---- The template (DLGTEMPLATEEX and DLGITEMTEMPLATEEX) ---------------------------------------------

    private static byte[] BuildTemplate(string button)
    {
        var t = new TemplateWriter();
        const short contentWidth = Width - 2 * Margin;
        const short textLeft = Margin + 30;
        // Wider for longer labels ("Replace it with 0.1.3"): about 4 units a character plus padding.
        short installWidth = (short)Math.Clamp(button.Length * 4 + 16, 50, 110);
        const short cancelWidth = 50, buttonTop = BandTop + 8;

        var items = new List<Action>
        {
            () => t.Item(SS_ICON, Margin, Margin, 21, 20, IdIcon, Static, ""),
            () => t.Item(SS_LEFT | SS_NOPREFIX, textLeft, Margin, Width - textLeft - Margin, 14, IdTitle, Static, ""),
            () => t.Item(SS_LEFT | SS_NOPREFIX, textLeft, Margin + 17, Width - textLeft - Margin, 18, IdSummary, Static, ""),
            () => t.Item(SS_LEFT, Margin, 50, contentWidth, 9, IdRead, Static, Text.ReadDisclaimer),
            () => t.Item(ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL | WS_VSCROLL | WS_TABSTOP, Margin, 61, contentWidth, 98,
                IdDisclaimer, Edit, "", WS_EX_CLIENTEDGE),
            () => t.Item(BS_AUTOCHECKBOX | WS_TABSTOP, Margin, 164, contentWidth, 12, IdAccept, Button,
                Text.AcceptDisclaimerMnemonic),
            () => t.Item(SS_LEFT | SS_NOPREFIX, Margin, 181, contentWidth, 18, IdNote, Static, ""),
            () => t.Item(BS_DEFPUSHBUTTON | WS_TABSTOP | WS_DISABLED, (short)(Width - Margin - cancelWidth - 6 - installWidth), buttonTop,
                installWidth, ButtonHeight, IdInstall, Button, "&" + button),
            () => t.Item(BS_PUSHBUTTON | WS_TABSTOP, Width - Margin - cancelWidth, buttonTop, cancelWidth, ButtonHeight, IdCancel, Button, Text.Cancel),
        };

        // Header: dlgVer, signature, helpID, exStyle, style, item count, x, y, cx, cy, no menu, default class.
        t.Word(1); t.Word(0xFFFF); t.Dword(0); t.Dword(WS_EX_APPWINDOW);
        t.Dword(DS_SETFONT | DS_MODALFRAME | DS_CENTER | WS_POPUP | WS_CAPTION | WS_SYSMENU);
        t.Word((ushort)items.Count); t.Short(0); t.Short(0); t.Short(Width); t.Short(Height);
        t.Word(0); t.Word(0); t.String(Dialogs.Title);
        t.Word(9); t.Word(400); t.Byte(0); t.Byte(1); t.String("Segoe UI"); // point size, weight, italic, charset, face
        foreach (var item in items) item();
        return t.ToArray();
    }

    private const ushort Button = 0x0080, Edit = 0x0081, Static = 0x0082;

    private sealed class TemplateWriter
    {
        private readonly MemoryStream _stream = new();
        private readonly BinaryWriter _w;
        public TemplateWriter() => _w = new BinaryWriter(_stream);
        public void Byte(byte v) => _w.Write(v);
        public void Word(ushort v) => _w.Write(v);
        public void Short(short v) => _w.Write(v);
        public void Dword(uint v) => _w.Write(v);
        public void String(string s)
        {
            foreach (char c in s) _w.Write((ushort)c);
            _w.Write((ushort)0);
        }

        private void Align()
        {
            while (_stream.Position % 4 != 0) _w.Write((byte)0);
        }

        public void Item(uint style, short x, short y, short cx, short cy, int id, ushort cls, string text, uint exStyle = 0)
        {
            Align();
            Dword(0); Dword(exStyle); Dword(style | WS_CHILD | WS_VISIBLE);
            Short(x); Short(y); Short(cx); Short(cy); Dword((uint)id);
            Word(0xFFFF); Word(cls); // a predefined class
            String(text);
            Word(0); // no creation data
        }

        public byte[] ToArray()
        {
            _w.Flush();
            return _stream.ToArray();
        }
    }

    // ---- Win32 ---------------------------------------------------------------------------------------

    private const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_DISABLED = 0x08000000;
    private const uint WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_VSCROLL = 0x00200000, WS_TABSTOP = 0x00010000;
    private const uint WS_EX_APPWINDOW = 0x00040000, WS_EX_CLIENTEDGE = 0x00000200;
    private const uint DS_SETFONT = 0x40, DS_MODALFRAME = 0x80, DS_CENTER = 0x0800;
    private const uint SS_LEFT = 0x0, SS_ICON = 0x3, SS_NOPREFIX = 0x80;
    private const uint ES_MULTILINE = 0x4, ES_AUTOVSCROLL = 0x40, ES_READONLY = 0x800;
    private const uint BS_PUSHBUTTON = 0x0, BS_DEFPUSHBUTTON = 0x1, BS_AUTOCHECKBOX = 0x3;
    private const uint WM_INITDIALOG = 0x0110, WM_COMMAND = 0x0111, WM_PAINT = 0x000F, WM_SETFONT = 0x0030, WM_SETICON = 0x0080;
    private const uint WM_CTLCOLORDLG = 0x0136, WM_CTLCOLORSTATIC = 0x0138, WM_CTLCOLORBTN = 0x0135;
    private const uint STM_SETICON = 0x0170, EM_SETSEL = 0x00B1, EM_SETMARGINS = 0x00D3, WM_VSCROLL = 0x0115, WM_CHANGEUISTATE = 0x0127;
    private const int EC_LEFTMARGIN = 0x1, EC_RIGHTMARGIN = 0x2, SB_TOP = 6, UIS_INITIALIZE = 3, UISF_HIDEFOCUS = 0x1, UISF_HIDEACCEL = 0x2;
    private const int BST_CHECKED = 1, COLOR_WINDOW = 5, COLOR_WINDOWTEXT = 8, COLOR_BTNFACE = 15, COLOR_3DLIGHT = 22;
    private static readonly IntPtr ICON_BIG = 1;
    private static readonly IntPtr AppIconId = 32512; // the SDK's resource id for <ApplicationIcon>

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore, fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint DialogBoxIndirectParamW(IntPtr instance, byte* template, IntPtr parent,
        delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr> proc, IntPtr param);

    [LibraryImport("user32.dll")] private static partial int EndDialog(IntPtr hwnd, nint result);
    [LibraryImport("user32.dll")] private static partial IntPtr GetDlgItem(IntPtr hwnd, int id);
    [LibraryImport("user32.dll")] private static partial uint IsDlgButtonChecked(IntPtr hwnd, int id);
    [LibraryImport("user32.dll")] private static partial int EnableWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);
    [LibraryImport("user32.dll")] private static partial IntPtr SetFocus(IntPtr hwnd);
    [LibraryImport("user32.dll")] private static partial int SetWindowTextW(IntPtr hwnd, char* text);
    [LibraryImport("user32.dll")] private static partial IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(IntPtr hwnd);
    [LibraryImport("user32.dll")] private static partial IntPtr GetSysColorBrush(int index);
    [LibraryImport("user32.dll")] private static partial uint GetSysColor(int index);
    [LibraryImport("user32.dll")] private static partial IntPtr BeginPaint(IntPtr hwnd, PAINTSTRUCT* ps);
    [LibraryImport("user32.dll")] private static partial int EndPaint(IntPtr hwnd, PAINTSTRUCT* ps);
    [LibraryImport("user32.dll")] private static partial int FillRect(IntPtr dc, RECT* rect, IntPtr brush);
    [LibraryImport("user32.dll")] private static partial int GetClientRect(IntPtr hwnd, RECT* rect);
    [LibraryImport("user32.dll")] private static partial int MapDialogRect(IntPtr hwnd, RECT* rect);
    [LibraryImport("user32.dll")] private static partial int DestroyIcon(IntPtr icon);
    [LibraryImport("gdi32.dll")] private static partial uint SetBkColor(IntPtr dc, uint color);
    [LibraryImport("gdi32.dll")] private static partial uint SetTextColor(IntPtr dc, uint color);
    [LibraryImport("gdi32.dll")] private static partial int DeleteObject(IntPtr obj);
    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic,
        uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, char* face);
    [LibraryImport("comctl32.dll")]
    private static partial int LoadIconWithScaleDown(IntPtr instance, IntPtr name, int cx, int cy, out IntPtr icon);
}
