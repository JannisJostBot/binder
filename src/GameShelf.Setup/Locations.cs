using System.Diagnostics;
using Microsoft.Win32;

namespace GameShelf.Setup;

/// <summary>
/// Every place setup writes to. All are fixed, computed from the user's known folders, never read back from the
/// registry or a data file, so a tampered uninstall entry can't point the uninstaller at someone else's files.
/// </summary>
internal static class Locations
{
    public static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static readonly string ProgramsRoot = Path.Combine(LocalAppData, "Programs");
    public static readonly string InstallDir = Path.Combine(ProgramsRoot, "GameShelf");

    public const string WidgetExe = "GameShelf.exe";
    public const string WatcherExe = "GameShelf.Watcher.exe";
    public const string UninstallerExe = "Uninstall.exe";

    /// <summary>What versions before installed-files.txt put into <see cref="InstallDir"/> (see <see cref="InstalledFiles"/>).</summary>
    public static readonly string[] LegacyFiles = [WidgetExe, WatcherExe, UninstallerExe];

    public static string Widget => Path.Combine(InstallDir, WidgetExe);

    public static readonly string StartMenuLink =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Game Shelf.lnk");

    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GameShelf";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = "Game Shelf";

    /// <summary>The widget's data (settings, library, sessions, imported shortcuts) and its cache (covers, logs).</summary>
    public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameShelf");
    public static readonly string CacheDir = Path.Combine(LocalAppData, "GameShelf");
    public static string ImportedShortcutsDir => Path.Combine(DataDir, "Shortcuts");

    public static readonly string LogFile = Path.Combine(Path.GetTempPath(), "GameShelfSetup.log");

    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public static bool IsInside(string? path, string dir) =>
        path is not null && path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>The version in Settings → Apps, or null when not installed.</summary>
    public static Version? InstalledVersion()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key?.GetValue("DisplayVersion") is string text && Version.TryParse(text, out var v) ? v : null;
    }

    public static void Log(string line)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>The installed widget and watcher while they run.</summary>
internal static class RunningApp
{
    private const string MessageWindowClass = "GameShelf.MessageWindow";
    private static readonly IntPtr CopyDataCommand = new(0x47534846); // "GSHF", as in the widget

    /// <summary>Asks a running widget to quit (as its tray menu's Quit does) and waits. True when none is left.</summary>
    public static unsafe bool QuitWidget()
    {
        var hwnd = Native.FindWindow(MessageWindowClass, null);
        if (hwnd != IntPtr.Zero)
        {
            Locations.Log("asking the running widget to quit");
            fixed (char* text = "--quit")
            {
                var copy = new Native.COPYDATASTRUCT { dwData = CopyDataCommand, cbData = 6 * sizeof(char), lpData = (IntPtr)text };
                Native.SendMessageTimeout(hwnd, Native.WM_COPYDATA, IntPtr.Zero, ref copy, Native.SMTO_ABORTIFHUNG, 5000, out _);
            }
        }
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (Ours(Locations.WidgetExe).Count == 0 && Native.FindWindow(MessageWindowClass, null) == IntPtr.Zero) return true;
            Thread.Sleep(200);
        }
        // Still there: a hung copy of our own exe. Ending it only loses the open panel.
        foreach (var process in Ours(Locations.WidgetExe)) TryKill(process);
        return Ours(Locations.WidgetExe).Count == 0;
    }

    /// <summary>A watcher from the install folder: a game is running and its playtime is being tracked.</summary>
    public static bool WatcherRunning() => Ours(Locations.WatcherExe).Count > 0;

    public static void StopWatcher()
    {
        foreach (var process in Ours(Locations.WatcherExe)) TryKill(process);
    }

    /// <summary>
    /// Processes started from our install folder only: never touch a copy running from elsewhere. That includes
    /// one still running from its ".old" file, which an update moved aside while it ran.
    /// </summary>
    private static List<Process> Ours(string exeName)
    {
        var result = new List<Process>();
        var installed = Path.Combine(Locations.InstallDir, exeName);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName)))
        {
            var path = Native.ProcessPath(process.Id);
            if (path is not null && (string.Equals(path, installed, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(path, installed + ".old", StringComparison.OrdinalIgnoreCase)))
                result.Add(process);
            else
                process.Dispose();
        }
        return result;
    }

    private static void TryKill(Process process)
    {
        try
        {
            Locations.Log($"ending {process.ProcessName} ({process.Id})");
            process.Kill();
            process.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally
        {
            process.Dispose();
        }
    }
}
