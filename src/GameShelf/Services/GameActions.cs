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
