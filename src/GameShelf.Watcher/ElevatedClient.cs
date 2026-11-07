using System.Runtime.InteropServices;
using GameShelf.Shared;

namespace GameShelf.Watcher;

/// <summary>
/// The watcher's side of Game mode's admin part (spec §11): writes the request, runs one of this user's two
/// scheduled tasks (System32\schtasks /run) and waits on the named event the helper sets when it's done. Nothing
/// runs unless "Allow…" was done and an admin item is on.
/// </summary>
internal static class ElevatedClient
{
    public const int ApplyWaitMs = 60_000, RestoreWaitMs = 90_000;
    private const uint CREATE_NO_WINDOW = 0x08000000;

    /// <summary>Runs the task with this request; true once the helper says it's done, false if it didn't in time.</summary>
    public static bool Run(string task, ElevatedRequest request, int waitMs)
    {
        try
        {
            Directory.CreateDirectory(GameModeInfo.Dir);
            File.WriteAllText(GameModeInfo.RequestPath, request.Encode());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GameMode.Log("Admin part: couldn't write the request: " + e.Message);
            return false;
        }
        var ev = Native.CreateEventW(IntPtr.Zero, true, false, ElevatedPart.EventPrefix + request.Event);
        if (ev == IntPtr.Zero) return false;
        try
        {
            var schtasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
            int code = RunHidden(schtasks, ["/run", "/tn", task]);
            if (code != 0)
            {
                GameMode.Log($"Admin part: couldn't run the task (schtasks {code}); use Allow… in Settings again");
                return false;
            }
            return Native.WaitForSingleObject(ev, (uint)waitMs) == Native.WAIT_OBJECT_0;
        }
        finally
        {
            Native.CloseHandle(ev);
        }
    }

    /// <summary>Starts a console tool without a window and waits up to 15 s for it; its exit code, or -1.</summary>
    private static int RunHidden(string exe, string[] args)
    {
        if (!Program.StartProcess(exe, args, CREATE_NO_WINDOW, out var process)) return -1;
        try
        {
            if (Native.WaitForSingleObject(process, 15_000) != Native.WAIT_OBJECT_0) return -1;
            return Native.GetExitCodeProcess(process, out var code) ? (int)code : -1;
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }
}

internal static partial class Native
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateEventW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr CreateEventW(IntPtr security, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string name);
}
