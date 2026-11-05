using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace GameShelf.Services;

/// <summary>
/// The standard Windows "Open" dialog (spec: "Use the standard Windows file picker"). GetOpenFileName rather than
/// WinRT's FileOpenPicker: it can return a shortcut itself instead of its target (OFN_NODEREFERENCELINKS), which
/// "Add a game" needs to keep a shortcut's arguments.
/// </summary>
internal static unsafe partial class FilePicker
{
    /// <summary>
    /// Shows the dialog over <paramref name="owner"/>'s window. <paramref name="filter"/> is pairs of
    /// "Name|*.ext;*.ext". Returns the picked path, or null if cancelled.
    /// </summary>
    public static string? Open(UIElement owner, string title, string filter, string? initialDir = null, bool keepShortcuts = false)
    {
        var buffer = new char[1024];
        // "Name\0pattern\0...\0\0"
        var filterText = filter.Replace('|', '\0') + "\0\0";
        App.ModalDepth++; // the flyout must not close on click-away while the dialog is up
        try
        {
            fixed (char* file = buffer)
            fixed (char* filters = filterText)
            fixed (char* caption = title)
            fixed (char* dir = initialDir)
            {
                var ofn = new OPENFILENAMEW
                {
                    lStructSize = sizeof(OPENFILENAMEW),
                    hwndOwner = NativeWindow.HandleOf(owner),
                    lpstrFilter = filters,
                    lpstrFile = file,
                    nMaxFile = buffer.Length,
                    lpstrTitle = caption,
                    lpstrInitialDir = dir,
                    Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR |
                            (keepShortcuts ? OFN_NODEREFERENCELINKS : 0),
                };
                if (!GetOpenFileNameW(&ofn)) return null;
            }
        }
        finally
        {
            App.ModalDepth--;
        }
        int end = Array.IndexOf(buffer, '\0');
        return end > 0 ? new string(buffer, 0, end) : null;
    }

    private const uint OFN_EXPLORER = 0x00080000, OFN_FILEMUSTEXIST = 0x00001000, OFN_PATHMUSTEXIST = 0x00000800,
        OFN_NOCHANGEDIR = 0x00000008, OFN_NODEREFERENCELINKS = 0x00100000;

    [StructLayout(LayoutKind.Sequential)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public IntPtr hwndOwner, hInstance;
        public char* lpstrFilter, lpstrCustomFilter;
        public int nMaxCustFilter, nFilterIndex;
        public char* lpstrFile;
        public int nMaxFile;
        public char* lpstrFileTitle;
        public int nMaxFileTitle;
        public char* lpstrInitialDir, lpstrTitle;
        public uint Flags;
        public ushort nFileOffset, nFileExtension;
        public char* lpstrDefExt;
        public IntPtr lCustData, lpfnHook;
        public char* lpTemplateName;
        public IntPtr pvReserved;
        public uint dwReserved, FlagsEx;
    }

    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileNameW(OPENFILENAMEW* ofn);
}
