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
            args.Add("--close");
            args.Add(app);
        }
        args.Add("--toast");
        args.Add(settings.ShowRestoreToast ? "1" : "0");
    }

    private static bool IsPartOf(string path, Game game) =>
        Launcher.WatchFolder(game) is { } folder && LaunchOptions.IsInside(path, folder);

    /// <summary>Changes are applied and not put back yet (a game is running, or a session didn't end normally).</summary>
    public static bool Pending => File.Exists(GameModeInfo.JournalPath);

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
