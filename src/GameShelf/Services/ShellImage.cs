using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GameShelf.Services;

/// <summary>An icon's pixels: premultiplied BGRA, top-down rows. Thread-agnostic, unlike UI bitmaps.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>
/// A file's icon at a given size, the same image Explorer shows. Only reads the file's icon resource; nothing
/// is run. Source-generated COM and P/Invoke, so it works in the Native AOT build.
/// </summary>
internal static unsafe partial class ShellImage
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Runs the extraction on its own short-lived STA thread: Shell objects expect one.</summary>
    public static Task<IconPixels?> GetIconAsync(string path, int size)
    {
        var done = new TaskCompletionSource<IconPixels?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(GetIcon(path, size)); }
            catch (Exception e) when (e is COMException or ArgumentException or UnauthorizedAccessException or IOException)
            {
                done.SetResult(null);
            }
        })
        { IsBackground = true, Name = "Shell icon" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static IconPixels? GetIcon(string path, int size)
    {
        if (!File.Exists(path)) return null;
        // Programs and .ico files: read the icon resource directly. The Shell can hand back its generic
        // "application" icon for some big game exes (Battlefield V) although they have a proper 256 px icon.
        var ext = Path.GetExtension(path);
        if ((ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
             ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)) && ExtractIcon(path, size) is { } direct)
            return direct;

        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, in iid, out var unknown) < 0 || unknown == IntPtr.Zero) return null;
        try
        {
            var factory = (IShellItemImageFactory)Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY, out var hbitmap) != 0 || hbitmap == IntPtr.Zero)
                return null;
            try { return ToPixels(hbitmap, premultiplied: true); }
            finally { DeleteObject(hbitmap); }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>The file's first icon at the size closest to <paramref name="size"/>, or null.</summary>
    private static IconPixels? ExtractIcon(string path, int size)
    {
        IntPtr icon;
        uint id;
        if (PrivateExtractIconsW(path, 0, size, size, &icon, &id, 1, 0) != 1 || icon == IntPtr.Zero) return null;
        try
        {
            if (!GetIconInfo(icon, out var info)) return null;
            try { return info.hbmColor == IntPtr.Zero ? null : ToPixels(info.hbmColor, premultiplied: false); }
            finally
            {
                if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
                if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    /// <summary>Copies the 32-bit DIB with its alpha channel, as premultiplied BGRA.</summary>
    private static IconPixels? ToPixels(IntPtr hbitmap, bool premultiplied)
    {
        BITMAP bm;
        if (GetObjectW(hbitmap, sizeof(BITMAP), &bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return null;
        int width = bm.bmWidth, height = Math.Abs(bm.bmHeight);
        var info = new BITMAPINFOHEADER
        {
            biSize = sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            fixed (byte* bits = pixels)
                if (GetDIBits(dc, hbitmap, 0, (uint)height, bits, &info, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
        // The Shell hands out premultiplied alpha; an icon's color bitmap has straight alpha. An old icon with no
        // alpha at all (every byte zero) is opaque: its mask isn't used here, so show it whole, not invisible.
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length && !anyAlpha; i += 4) anyAlpha = pixels[i] != 0;
        if (!anyAlpha)
        {
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }
        else if (!premultiplied)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int a = pixels[i + 3];
                if (a == 255) continue;
                pixels[i] = (byte)(pixels[i] * a / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
            }
        }
        return new IconPixels(width, height, pixels);
    }

    // ---- Interop -----------------------------------------------------------------------------------------

    private const int SIIGBF_ICONONLY = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [GeneratedComInterface, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    internal partial interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr hbitmap);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, IntPtr bindContext, in Guid iid, out IntPtr item);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(IntPtr handle, int size, void* bitmap);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte* bits, BITMAPINFOHEADER* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PrivateExtractIconsW(string file, int index, int cx, int cy, IntPtr* icons, uint* ids, uint count, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
