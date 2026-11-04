using System.Diagnostics;
using System.Runtime.InteropServices;
using GameShelf.Library;
using GameShelf.Models;

namespace GameShelf.Services;

/// <summary>
/// Which games are running right now: a process whose exe is inside the game's install folder, as the watcher
/// matches them. Read once when asked (Free up space opening or "Check again"), never watched.
/// </summary>
internal static unsafe partial class RunningGames
{
    public static HashSet<string> Ids(IEnumerable<Game> games)
    {
        var running = new HashSet<string>();
        var folders = games.Where(g => !string.IsNullOrEmpty(g.InstallDir)).ToList();
        if (folders.Count == 0) return running;

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
                    var path = new string(buffer, 0, size);
                    foreach (var game in folders)
                        if (LaunchOptions.IsInside(path, game.InstallDir!)) running.Add(game.Id);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }
        return running;
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
