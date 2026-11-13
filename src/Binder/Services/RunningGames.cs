using System.Diagnostics;
using System.Runtime.InteropServices;
using Binder.Library;
using Binder.Models;

namespace Binder.Services;

/// <summary>
/// Which games are running right now: a process whose exe is inside the game's install folder, as the watcher
/// matches them. Read once when asked (Free up space opening or "Check again"), never watched.
/// </summary>
internal static unsafe partial class RunningGames
{
    public static HashSet<string> Ids(IEnumerable<Game> games)
    {
        var running = new HashSet<string>();
        // Game ids by install folder (full, no trailing separator): a process is then matched by looking up each
        // folder above its exe, O(depth) per process instead of comparing it with every game's folder.
        var byFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            if (string.IsNullOrEmpty(game.InstallDir) || !Path.IsPathFullyQualified(game.InstallDir)) continue;
            string folder;
            try { folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.InstallDir)); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (!byFolder.TryGetValue(folder, out var ids)) byFolder[folder] = ids = [];
            ids.Add(game.Id);
        }
        if (byFolder.Count == 0) return running;

        var buffer = stackalloc char[1024];
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)process.Id);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    int size = 1024;
                    if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size)) continue;
                    // Every folder above the exe (strictly inside, as LaunchOptions.IsInside).
                    for (var dir = Path.GetDirectoryName(new string(buffer, 0, size)); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                        if (byFolder.TryGetValue(dir, out var ids)) running.UnionWith(ids);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }
        return running;
    }

    /// <summary>The full exe path of a running process, or null (ended, or not ours to query).</summary>
    public static string? ImagePath(uint processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = stackalloc char[1024];
            int size = 1024;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* name, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
