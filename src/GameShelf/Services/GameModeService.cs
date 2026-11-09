using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Shared;
using Microsoft.Win32;

namespace GameShelf.Services;

/// <summary>
/// The widget's side of Game mode (milestone 14, spec §11). The watcher applies and restores the changes,
/// since the widget is gone while the game runs; this decides what it's asked to do, starts recovery when a
/// session didn't end normally, and reads the state for the Settings page. Nothing here runs unless asked.
/// </summary>
internal static unsafe partial class GameModeService
{
    public static bool IsOn(Game game, Settings settings) => game.GameMode ?? settings.GameModeEnabled;

    /// <summary>True when this chosen app is calmed (efficiency mode) instead of closed.</summary>
    public static bool IsCalmed(string path, Settings settings) =>
        settings.GameModeCalmApps.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds the watcher's Game mode arguments, when it's on for this game and something is chosen.</summary>
    public static void AddWatcherArguments(Collection<string> args, Game game, Settings settings)
    {
        if (!IsOn(game, settings)) return;
        var items = settings.GameModeItems.Where(GameModeInfo.IsKnownItem).Distinct().ToList();
        // Never the game itself, nor anything we couldn't reopen.
        var apps = settings.GameModeCloseApps.Where(p => IsClosable(p) && !IsPartOf(p, game)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0 && apps.Count == 0) return;
        args.Add("--mode");
        args.Add(string.Join(',', items));
        foreach (var app in apps)
        {
            args.Add(IsCalmed(app, settings) ? "--calm" : "--close");
            args.Add(app);
        }
        args.Add("--toast");
        args.Add(settings.ShowRestoreToast ? "1" : "0");

        // The admin part's apps (exe names); the watcher passes them on only if "Allow…" was done.
        if (items.Contains(GameModeInfo.Smooth))
        {
            foreach (var exe in SmoothApps(settings))
            {
                args.Add("--smooth");
                args.Add(exe);
            }
        }
        if (items.Contains(GameModeInfo.Uploads))
        {
            foreach (var exe in settings.ThrottledUploadApps.Where(ElevatedPart.IsAppName).Distinct(StringComparer.OrdinalIgnoreCase).Take(32))
            {
                args.Add("--throttle");
                args.Add(exe);
            }
            args.Add("--limit");
            args.Add(settings.UploadLimitMbps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The installed voice chat apps' exes plus the user's own "kept smooth" ones.</summary>
    public static List<string> SmoothApps(Settings settings) =>
        ElevatedPart.InstalledVoiceApps().SelectMany(a => a.Exes)
            .Concat(settings.KeepSmoothApps.Where(ElevatedPart.IsAppName))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();

    private static bool IsPartOf(string path, Game game) =>
        Launcher.WatchFolder(game) is { } folder && LaunchOptions.IsInside(path, folder);

    /// <summary>Changes are applied and not put back yet (a game is running, or a session didn't end normally).</summary>
    public static bool Pending => File.Exists(GameModeInfo.JournalPath) || ElevatedPending;

    /// <summary>The admin part's journal is there and "Allow…" was done for this user (its Restore task can run).</summary>
    private static bool ElevatedPending => File.Exists(ElevatedPart.JournalPath) && Allowed;

    // ---- The admin part ------------------------------------------------------------------------------------

    private static string? s_sid;
    private static string? Sid => s_sid ??= ElevatedPart.CurrentUserSid();

    /// <summary>"Allow…" was done for this user and the helper is installed.</summary>
    public static bool Allowed => ElevatedPart.AllowedVersion(Sid) is not null;

    /// <summary>The installed helper is another helper version than this widget's (Settings offers "Update…").</summary>
    public static bool AllowedButOutdated => ElevatedPart.AllowedVersion(Sid) is { } v && ElevatedPart.IsOutdatedMarker(v);

    /// <summary>
    /// For the shelf's notice: Game mode is on (for all games or one) with items that need the helper, and the
    /// helper is another helper version, so it has to be updated (one UAC prompt) before they work again.
    /// </summary>
    public static bool HelperNeedsUpdate(Settings settings, IEnumerable<Game> games) =>
        settings.GameModeItems.Any(GameModeInfo.IsElevatedItem) &&
        (settings.GameModeEnabled || games.Any(g => g.GameMode == true)) &&
        AllowedButOutdated;

    /// <summary>
    /// Only accounts in the Administrators group can approve it for themselves (a standard account's tasks would
    /// run without admin rights, so they'd do nothing).
    /// </summary>
    public static bool CanAllow
    {
        get
        {
            const int TokenElevationType = 18; // 1 default, 2 full, 3 limited (an admin under UAC)
            int type = 0;
            if (!GetTokenInformation(-4, TokenElevationType, &type, sizeof(int), out _)) return false;
            return type is 2 or 3;
        }
    }

    /// <summary>
    /// "Allow…" (the one UAC prompt, the owner-approved exception): runs the helper shipped next to the widget
    /// elevated; it copies itself to Program Files and registers this user's tasks. Returns its exit code, 1223
    /// if the prompt was declined, or -1.
    /// </summary>
    public static Task<int> AllowAsync() =>
        RunElevatedAsync(Path.Combine(AppContext.BaseDirectory, ElevatedPart.ExeName), "--install");

    /// <summary>Removes this user's tasks (and the helper, with the last user) through the installed copy.</summary>
    public static Task<int> RemoveAsync() => RunElevatedAsync(ElevatedPart.InstalledExe, "--uninstall");

    private static Task<int> RunElevatedAsync(string exe, string action)
    {
        if (Sid is not { } sid || !File.Exists(exe)) return Task.FromResult(-1);
        return Task.Run(() =>
        {
            fixed (char* verb = "runas")
            fixed (char* file = exe)
            fixed (char* parameters = $"{action} --user {sid}")
            {
                var info = new SHELLEXECUTEINFOW
                {
                    cbSize = sizeof(SHELLEXECUTEINFOW),
                    fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC,
                    lpVerb = verb,
                    lpFile = file,
                    lpParameters = parameters,
                };
                if (!ShellExecuteExW(&info)) return Marshal.GetLastPInvokeError() == 1223 ? 1223 : -1;
                if (info.hProcess == IntPtr.Zero) return -1;
                WaitForSingleObject(info.hProcess, 120_000);
                int code = GetExitCodeProcess(info.hProcess, out uint exit) ? (int)exit : -1;
                CloseHandle(info.hProcess);
                return code;
            }
        });
    }

    private const uint SEE_MASK_NOCLOSEPROCESS = 0x40, SEE_MASK_NOASYNC = 0x100;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHELLEXECUTEINFOW
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public char* lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public char* lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor, hProcess;
    }

    [LibraryImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellExecuteExW(SHELLEXECUTEINFOW* info);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int infoClass, void* info, int length, out int returned);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(IntPtr handle, uint ms);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(IntPtr process, out uint code);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    /// <summary>A service's state now, for its row on the Settings page.</summary>
    public static ServiceState.Kind ServiceStateOf(string id) =>
        ElevatedPart.ServiceFor(id) is { } s ? ServiceState.Of(s.Service) : ServiceState.Kind.Missing;

    /// <summary>A watcher is running a Game mode session right now (it holds the mutex).</summary>
    private static bool SessionRunning()
    {
        if (!Mutex.TryOpenExisting(GameModeInfo.MutexName, out var mutex)) return false;
        mutex.Dispose();
        return true;
    }

    /// <summary>
    /// On start: changes left by a session that didn't end normally (the PC lost power, the watcher was ended)
    /// are put back, unless a watcher still runs that session. One file check when there's nothing to do.
    /// </summary>
    public static void RecoverIfNeeded()
    {
        if (Pending && !SessionRunning()) StartRestore();
    }

    /// <summary>"Restore now": the watcher puts back what's pending and reports with --restored.</summary>
    public static bool RestoreNow() => !SessionRunning() && StartRestore();

    /// <summary>Whether "Restore now" can do anything: changes are pending and no game is being played.</summary>
    public static bool CanRestoreNow => Pending && !SessionRunning();

    private static bool StartRestore()
    {
        try
        {
            var psi = new ProcessStartInfo(Launcher.WatcherPath) { UseShellExecute = false };
            psi.ArgumentList.Add("--restore");
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            ErrorLog.Write(e, "game mode restore");
            return false;
        }
    }

    public static void OpenLog()
    {
        if (File.Exists(GameModeInfo.LogPath)) GameActions.ShellOpen(GameModeInfo.LogPath);
    }

    public static bool HasLog => File.Exists(GameModeInfo.LogPath);

    // ---- State for the Settings page (read when it opens) -----------------------------------------------

    private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

    /// <summary>Game Bar's "Record what happened" (background recording) is on now.</summary>
    public static bool GameBarRecording()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GameDvrKey);
            return key?.GetValue("HistoricalCaptureEnabled") is int value && value != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>An app Game mode can close and reopen: a plain exe outside Windows, Store apps and Game Shelf.</summary>
    public static bool IsClosable(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && LaunchOptions.IsInside(path, windows)) return false;
        // Store apps can't be reopened from their exe path.
        if (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return false;
        return !LaunchOptions.IsInside(path, AppContext.BaseDirectory);
    }

    /// <summary>An app's name as Windows shows it (its file description), else its file name.</summary>
    public static string AppName(string path)
    {
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return Path.GetFileNameWithoutExtension(path);
    }

    public sealed record RunningApp(string Path, string Name);

    /// <summary>
    /// Apps with a visible window right now that Game mode could close, for "Add a running app" (read on
    /// click). Games on the shelf are left out.
    /// </summary>
    public static List<RunningApp> RunningApps(IEnumerable<Game> games)
    {
        var pids = VisibleWindowProcesses();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pid in pids)
            if (RunningGames.ImagePath(pid) is { } path && IsClosable(path)) paths.Add(path);
        var gameFolders = games.Select(Launcher.WatchFolder).OfType<string>().ToList();
        return paths
            .Where(p => !gameFolders.Any(folder => LaunchOptions.IsInside(p, folder)))
            .Select(p => new RunningApp(p, AppName(p)))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Exe paths of every process running now (for "Running" next to the chosen apps).</summary>
    public static HashSet<string> RunningPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
                if (RunningGames.ImagePath((uint)process.Id) is { } path) paths.Add(path);
        }
        return paths;
    }

    [ThreadStatic] private static HashSet<uint>? t_windowPids;

    private static HashSet<uint> VisibleWindowProcesses()
    {
        t_windowPids = [];
        try
        {
            EnumWindows(&OnWindow, IntPtr.Zero);
            t_windowPids.Remove((uint)Environment.ProcessId);
            return t_windowPids;
        }
        finally
        {
            t_windowPids = null;
        }
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(IntPtr hwnd, IntPtr lParam)
    {
        // Top-level windows the user can see and close: visible, not owned by another window, with a title.
        if (IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero && GetWindowTextLengthW(hwnd) > 0)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            t_windowPids?.Add(pid);
        }
        return 1;
    }

    private const uint GW_OWNER = 4;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindow(IntPtr hwnd, uint command);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
