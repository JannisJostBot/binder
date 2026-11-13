using System.Runtime.InteropServices;

namespace Binder.Services;

/// <summary>Theme for the tray icon's Win32 popup menu (the panel's own menus are WinUI menu flyouts).</summary>
internal static partial class NativeMenu
{
    private static bool _checked;

    /// <summary>
    /// Win32 menus are light unless the process opts in. uxtheme's SetPreferredAppMode (ordinal 135, Windows 10
    /// 1903+) with AllowDark makes them follow the Windows app mode, as Explorer's tray menus do. Undocumented;
    /// if it's missing, menus simply stay light.
    /// </summary>
    public static void FollowSystemTheme()
    {
        if (_checked) return;
        _checked = true;
        if (Environment.OSVersion.Version.Build < 18362) return;
        var uxtheme = LoadLibraryExW("uxtheme.dll", IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (uxtheme == IntPtr.Zero) return;
        var setMode = GetProcAddress(uxtheme, 135);
        var flush = GetProcAddress(uxtheme, 136);
        if (setMode == IntPtr.Zero) return;
        unsafe
        {
            ((delegate* unmanaged<int, int>)setMode)(AllowDark);
            if (flush != IntPtr.Zero) ((delegate* unmanaged<void>)flush)();
        }
    }

    private const int AllowDark = 1;
    private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr LoadLibraryExW(string file, IntPtr reserved, uint flags);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetProcAddress(IntPtr module, nint ordinal);
}
