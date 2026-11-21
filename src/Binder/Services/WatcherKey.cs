using System.Runtime.InteropServices;
using Binder.Shared;

namespace Binder.Services;

/// <summary>
/// "End a stuck game" (spec §28): while a game runs, its watcher holds Binder's shortcut. A widget that starts
/// meanwhile (Start search, a second game) asks every watcher to let go of it, so the shortcut opens the panel
/// again; the watcher takes it back once this widget has exited.
/// </summary>
internal static partial class WatcherKey
{
    private const uint SMTO_ABORTIFHUNG = 0x0002, TimeoutMs = 500;

    /// <summary>Asks each watcher's window to release the shortcut; true if any answered.</summary>
    public static bool Release()
    {
        bool any = false;
        var pid = (nuint)Environment.ProcessId;
        var window = IntPtr.Zero;
        for (int i = 0; i < 16; i++)
        {
            window = FindWindowExW(IntPtr.Zero, window, WatcherNames.WindowClass, null);
            if (window == IntPtr.Zero) break;
            any |= SendMessageTimeoutW(window, WatcherNames.ReleaseKey, pid, 0, SMTO_ABORTIFHUNG, TimeoutMs, out _) != 0;
        }
        return any;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string? windowName);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageTimeoutW(IntPtr hwnd, uint msg, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
