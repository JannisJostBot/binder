using System.ComponentModel;
using System.Diagnostics;
using Binder.Shared;

namespace Binder.Setup;

/// <summary>
/// Game mode's admin part (spec §11), if "Allow…" was done for this user: uninstalling Binder removes it too,
/// through the copy in Program Files (it removes this user's tasks, and itself with the last user). That needs
/// admin rights, so Windows asks; a silent uninstall leaves it (it does nothing without Binder).
/// </summary>
internal static class GameModeHelper
{
    public static bool IsAllowed => ElevatedPart.AllowedVersion(ElevatedPart.CurrentUserSid()) is not null;

    /// <summary>Removes it if it was allowed; false if it was left in place (declined, silent, or it failed).</summary>
    public static bool Remove(bool silent)
    {
        var sid = ElevatedPart.CurrentUserSid();
        if (sid is null || ElevatedPart.AllowedVersion(sid) is null) return true;
        if (silent)
        {
            Locations.Log("Game mode helper left in place (silent uninstall)");
            return false;
        }
        try
        {
            var info = new ProcessStartInfo(ElevatedPart.InstalledExe) { UseShellExecute = true, Verb = "runas" };
            info.ArgumentList.Add("--uninstall");
            info.ArgumentList.Add("--user");
            info.ArgumentList.Add(sid);
            using var process = Process.Start(info);
            if (process is null || !process.WaitForExit(120_000)) return false;
            Locations.Log($"Game mode helper removed ({process.ExitCode})");
            return process.ExitCode == 0;
        }
        catch (Win32Exception e)
        {
            Locations.Log($"Game mode helper left in place ({(e.NativeErrorCode == 1223 ? "declined" : e.Message)})");
            return false;
        }
    }
}
