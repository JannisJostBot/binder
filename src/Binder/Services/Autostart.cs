using System.Security;
using Microsoft.Win32;

namespace Binder.Services;

/// <summary>
/// "Start with Windows" through the standard per-user Run key. The registry value is the source of truth,
/// so a change made elsewhere (e.g. removing the entry) shows correctly in settings.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Binder";

    /// <summary>
    /// The exe path in quotes (an unquoted path with spaces could start the wrong program), plus --background:
    /// at sign-in the flyout waits in the tray instead of opening.
    /// </summary>
    private static string? Command => Environment.ProcessPath is { } exe ? "\"" + exe + "\" --background" : null;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Adds or removes the Run entry. Returns false if the registry couldn't be written.</summary>
    public static bool TrySet(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (!enabled) key.DeleteValue(ValueName, throwOnMissingValue: false);
            else if (Command is { } command) key.SetValue(ValueName, command);
            else return false;
            return true;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>If autostart is on but points at an old location (the app was moved), point it here.</summary>
    public static void UpdatePathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is string current && Command is { } command &&
                !string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                TrySet(true);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
    }
}
