using System.Runtime.InteropServices;
using Binder.Shared;

// All interop below is blittable; no runtime marshalling stubs needed.
[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]
// System DLLs only, from System32.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Binder.Elevated;

/// <summary>
/// Game mode's admin part (milestone 14, spec §11). Does nothing unless it runs elevated.
///
///   --install --user &lt;sid&gt;     "Allow…": copy to Program Files, register the tasks (the one UAC prompt)
///   --uninstall --user &lt;sid&gt;   remove this user's tasks; the last user's removal deletes the helper too
///   --apply                    the "Binder Apply" task: pause services, network priority, Windows Update limit
///   --restore                  the "Binder Restore" task (also at logon): put all of that back
///
/// Exit codes: 0 done, 1 something wasn't put back, 2 no valid request, 3 not elevated, 4/5 refused, 6 busy (another run held the
/// lock for 2 minutes), 10+ install failed.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (!Native.IsElevated()) return 3;
        Native.CoInitializeEx(IntPtr.Zero, Native.COINIT_MULTITHREADED);
        Native.CoInitializeSecurity(IntPtr.Zero, -1, IntPtr.Zero, IntPtr.Zero, Native.RPC_C_AUTHN_LEVEL_DEFAULT,
            Native.RPC_C_IMP_LEVEL_IMPERSONATE, IntPtr.Zero, Native.EOAC_NONE, IntPtr.Zero);

        switch (args)
        {
            case ["--install", "--user", var sid] when IsKnownUser(sid):
                return Install.Run(sid);
            case ["--uninstall", "--user", var sid] when IsKnownUser(sid):
                return Install.Remove(sid);
            case ["--apply"] when ElevatedPart.CurrentUserSid() is { } me:
                return Helper.Apply(me);
            case ["--restore"] when ElevatedPart.CurrentUserSid() is { } me:
                return Helper.RestoreTask(me);
            default:
                return 2;
        }
    }

    /// <summary>A user SID in the right shape that Windows can parse (the user may be another account than the admin who approved).</summary>
    private static bool IsKnownUser(string sid)
    {
        if (!ElevatedPart.IsUserSid(sid) || !Native.ConvertStringSidToSidW(sid, out var binary)) return false;
        Native.LocalFree(binary);
        return true;
    }
}
