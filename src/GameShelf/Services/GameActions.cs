using System.ComponentModel;
using System.Diagnostics;
using GameShelf.Models;

namespace GameShelf.Services;

/// <summary>Small shell actions on a game: its install folder and store page.</summary>
public static class GameActions
{
    /// <summary>The game's folder if it exists on disk, else null.</summary>
    public static string? InstallFolder(Game game)
    {
        var dir = Launcher.WatchFolder(game);
        return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
    }

    /// <summary>The game's store page, or null when we don't know one.</summary>
    public static string? StoreUrl(Game game) => game.Source switch
    {
        GameSource.Steam when game.StoreId is { } appId => $"https://store.steampowered.com/app/{appId}",
        _ => null,
    };

    /// <summary>A Steam client command for the game (validate, uninstall), or null for non-Steam games.</summary>
    public static string? SteamCommand(Game game, string command) =>
        game.Source == GameSource.Steam && game.StoreId is { } appId && appId.All(char.IsAsciiDigit)
            ? $"steam://{command}/{appId}"
            : null;

    /// <summary>Where a game is uninstalled (Free up space): what to open, and a line telling the user what to do there.</summary>
    public sealed record UninstallStep(string Target, string Hint);

    private const string AppsSettings = "ms-settings:appsfeatures";

    /// <summary>
    /// How a game is removed, always by its own launcher or Windows, never by us: Steam's uninstall link (Steam
    /// asks first), GOG Galaxy's page for the game, Settings › Apps for Xbox games, else the launcher itself.
    /// Settings › Apps when the launcher isn't installed. Links are built from validated ids only.
    /// </summary>
    public static UninstallStep Uninstall(Game game)
    {
        if (SteamCommand(game, "uninstall") is { } steam)
            return new(steam, Strings.Format(Strings.UninstallHintSteam, game.Name));
        if (game.Source == GameSource.Gog && game.StoreId is { Length: > 0 and <= 20 } gogId && gogId.All(char.IsAsciiDigit) &&
            LauncherIcons.ProtocolHandler("goggalaxy") is not null)
            return new($"goggalaxy://openGameView/{gogId}", Strings.UninstallHintGog);
        if (game.Source != GameSource.Xbox && LauncherIcons.LauncherExe(game.Source) is { } launcher)
            return new(launcher, Strings.Format(Strings.UninstallHintLauncher, game.Name, LauncherName(game.Source)));
        return new(AppsSettings, Strings.Format(Strings.UninstallHintApps, game.Name));
    }

    private static string LauncherName(GameSource source) => source switch
    {
        GameSource.Epic => "Epic Games Launcher",
        GameSource.Ea => "EA app",
        GameSource.Ubisoft => "Ubisoft Connect",
        GameSource.Rockstar => "Rockstar Games Launcher",
        GameSource.Hytale => "Hytale Launcher",
        GameSource.Gog => "GOG Galaxy",
        _ => Strings.TheLauncher,
    };

    /// <summary>Opens a folder in Explorer or a URL in the default browser.</summary>
    public static void ShellOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // Nothing sensible to do; the user can try again.
        }
    }
}
