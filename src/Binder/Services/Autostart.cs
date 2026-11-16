using System.Security;
using Microsoft.Win32;

namespace Binder.Services;

/// <summary>
/// "Start with Windows" through the standard per-user Run key. The registry is the source of truth, so a change
/// made elsewhere shows correctly in settings: the entry removed, or turned off in Task Manager or Settings › Apps ›
/// Startup. Windows doesn't remove the entry for that; it marks it disabled under Explorer\StartupApproved\Run.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Binder";

    /// <summary>
    /// The exe path in quotes (an unquoted path with spaces could start the wrong program), plus --background:
    /// at sign-in the flyout waits in the tray instead of opening.
    /// </summary>
    private static string? Command => Environment.ProcessPath is { } exe ? "\"" + exe + "\" --background" : null;

    /// <summary>On when the Run entry is there and Windows hasn't turned it off.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is not string) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return !IsTurnedOff(approved?.GetValue(ValueName) as byte[]);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Windows' StartupApproved value for a Run entry: 12 bytes, the first one a state, then when it was turned
    /// off. An odd state (0x03, 0x07) means turned off in Task Manager or Settings; an even one (0x02, 0x06), or
    /// no value at all, means on. Anything else that's there is read as on, as Windows does.
    /// </summary>
    public static bool IsTurnedOff(byte[]? approved) => approved is { Length: > 0 } && (approved[0] & 1) == 1;

    /// <summary>
    /// Adds or removes the Run entry. Turning it on also lifts a "turned off" Windows recorded (the user asked
    /// for it here, so it should really start); turning it off also removes that record. Returns false if the
    /// registry couldn't be written.
    /// </summary>
    public static bool TrySet(bool enabled)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (!enabled) key.DeleteValue(ValueName, throwOnMissingValue: false);
                else if (Command is { } command) key.SetValue(ValueName, command);
                else return false;
            }
            // Only our own value; with none, Windows treats the entry as on.
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// If there's a Run entry but it points at an old location (the app was moved), point it here. Only the path:
    /// an entry the user turned off in Windows stays off.
    /// </summary>
    public static void UpdatePathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string current && Command is { } command &&
                !string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, command);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
    }
}
