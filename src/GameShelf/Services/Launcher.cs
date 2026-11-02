using System.ComponentModel;
using System.Diagnostics;
using GameShelf.Models;

namespace GameShelf.Services;

public static class Launcher
{
    /// <summary>How long the watcher looks for the game process before giving up.</summary>
    public const int DetectionTimeoutSeconds = 180;

    public static string WatcherPath => Path.Combine(AppContext.BaseDirectory, "GameShelf.Watcher.exe");

    /// <summary>Starts the game via its store URI or its exe. Returns an error message, or null on success.</summary>
    public static string? StartGame(Game game)
    {
        ProcessStartInfo psi;
        if (!string.IsNullOrEmpty(game.LaunchUri))
            psi = new ProcessStartInfo(game.LaunchUri) { UseShellExecute = true };
        else if (!string.IsNullOrEmpty(game.ExePath) && File.Exists(game.ExePath))
            psi = new ProcessStartInfo(game.ExePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(game.ExePath)! };
        else
            return "The game's executable wasn't found.";

        try
        {
            Process.Start(psi)?.Dispose();
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// The folder the watcher matches processes in: the install folder, or the exe's own folder. Never the
    /// folder of a .lnk/.url (that's our shortcuts folder, which no game runs from).
    /// </summary>
    public static string? WatchFolder(Game game) =>
        game.InstallDir ??
        (game.ExePath is { } exe && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(exe) : null);

    /// <summary>True when the watcher can tell that the game closed (a folder or a process name to watch).</summary>
    public static bool CanWatch(Game game) => WatchFolder(game) is not null || !string.IsNullOrEmpty(game.ProcessName);

    /// <summary>Starts the watcher for a launched game. Returns an error message, or null on success.</summary>
    public static string? StartWatcher(Game game, bool comeBack)
    {
        var dir = WatchFolder(game);
        var psi = new ProcessStartInfo(WatcherPath) { UseShellExecute = false };
        psi.ArgumentList.Add("--game");
        psi.ArgumentList.Add(game.Id);
        if (!string.IsNullOrEmpty(dir))
        {
            psi.ArgumentList.Add("--dir");
            psi.ArgumentList.Add(dir);
        }
        if (!string.IsNullOrEmpty(game.ProcessName))
        {
            psi.ArgumentList.Add("--process");
            psi.ArgumentList.Add(game.ProcessName);
        }
        psi.ArgumentList.Add("--timeout");
        psi.ArgumentList.Add(DetectionTimeoutSeconds.ToString());
        psi.ArgumentList.Add("--return");
        psi.ArgumentList.Add(comeBack ? "1" : "0");

        try
        {
            Process.Start(psi)?.Dispose();
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return e.Message;
        }
    }
}
