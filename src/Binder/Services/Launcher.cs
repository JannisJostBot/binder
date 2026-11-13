using System.ComponentModel;
using System.Diagnostics;
using Binder.Library;
using Binder.Models;

namespace Binder.Services;

public static class Launcher
{
    /// <summary>How long the watcher looks for the game process before giving up.</summary>
    public const int DetectionTimeoutSeconds = 180;

    public static string WatcherPath => Path.Combine(AppContext.BaseDirectory, "Binder.Watcher.exe");

    /// <summary>Starts the game via its store URI or its exe. Returns an error message, or null on success.</summary>
    public static string? StartGame(Game game)
    {
        var args = LaunchOptions.Split(game.LaunchArgs);
        ProcessStartInfo psi;
        if (LaunchOptions.ValidOverride(game) is { } overrideExe)
            psi = DirectStart(overrideExe, args);
        else if (LaunchOptions.RouteFor(game) == LaunchOptions.Route.Steam &&
                 LaunchOptions.SteamLink(game.StoreId, args) is { } steamLink)
            psi = SteamStart(steamLink);
        else if (args.Count > 0 && LaunchOptions.DirectExe(game) is { } exe && File.Exists(exe))
            psi = DirectStart(exe, args);
        else if (game.Source == GameSource.Rockstar && RockstarSource.IsTitleId(game.StoreId ?? ""))
        {
            // Through the Rockstar Games Launcher, which handles sign-in and updates, like the store links.
            if (RockstarSource.LauncherExe() is not { } rockstar) return Strings.Format(Strings.LauncherNotFound, "Rockstar Games Launcher");
            psi = DirectStart(rockstar, ["-launchTitle=" + game.StoreId]);
        }
        else if (game.Source == GameSource.Hytale)
        {
            // Through the Hytale Launcher: the client needs its sign-in. Play there starts the game.
            if (HytaleSource.LauncherExe() is not { } hytale) return Strings.Format(Strings.LauncherNotFound, "Hytale Launcher");
            psi = DirectStart(hytale, []);
        }
        else if (!string.IsNullOrEmpty(game.LaunchUri))
            psi = new ProcessStartInfo(game.LaunchUri) { UseShellExecute = true };
        else if (!string.IsNullOrEmpty(game.ExePath) && File.Exists(game.ExePath))
            psi = new ProcessStartInfo(game.ExePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(game.ExePath)! };
        else
            return Strings.ExeNotFound;

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
    /// Opens a Steam link. When Steam isn't running yet, starts steam.exe with -silent so Steam comes up in the
    /// notification area instead of opening its main window first. When it is running, the link goes to it
    /// as before (no new window appears then), and if steam.exe can't be found the link is opened normally.
    /// </summary>
    private static ProcessStartInfo SteamStart(string link)
    {
        if (!SteamSource.IsRunning() && SteamSource.SteamExe() is { } steam)
        {
            var psi = new ProcessStartInfo(steam) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(steam)! };
            psi.ArgumentList.Add("-silent");
            psi.ArgumentList.Add(link);
            return psi;
        }
        return new ProcessStartInfo(link) { UseShellExecute = true };
    }

    /// <summary>Runs an exe from its own folder with arguments passed one by one (quoted by .NET, never re-parsed by a shell).</summary>
    private static ProcessStartInfo DirectStart(string exe, List<string> args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return psi;
    }

    /// <summary>What the game starts as, for the "Starts as" preview.</summary>
    public static string Describe(Game game)
    {
        var args = LaunchOptions.Split(game.LaunchArgs);
        var joined = LaunchOptions.Join(args);
        if (LaunchOptions.ValidOverride(game) is { } overrideExe) return (Path.GetFileName(overrideExe) + " " + joined).Trim();
        return LaunchOptions.RouteFor(game) switch
        {
            LaunchOptions.Route.Steam => args.Count > 0 ? Strings.Format(Strings.SteamWith, joined) : "Steam",
            LaunchOptions.Route.Exe => (Path.GetFileName(LaunchOptions.DirectExe(game)) + " " + joined).Trim(),
            _ => game.Source == GameSource.Shortcut ? Strings.ItsShortcut : Strings.Format(Strings.TheSourceLauncher, game.SourceLabel),
        };
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
        if (game.RestartOnCrash)
        {
            // Brought back after a crash even with "Come back" off, to restart it (spec §17).
            psi.ArgumentList.Add("--restart");
            psi.ArgumentList.Add("1");
        }
        GameModeService.AddWatcherArguments(psi.ArgumentList, game, App.Settings);

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
