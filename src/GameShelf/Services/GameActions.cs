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

    /// <summary>
    /// Where a game is uninstalled (Free up space): what to open (a link or Settings page, or the launcher itself
    /// when <paramref name="Launcher"/> is set), and a line telling the user what to do there.
    /// </summary>
    public sealed record UninstallStep(string Target, string Hint, GameSource? Launcher = null);

    private const string AppsSettings = "ms-settings:appsfeatures";

    /// <summary>
    /// How a game is removed, always by its own launcher or Windows, never by us: Steam's uninstall link (Steam
    /// asks first), GOG Galaxy's page for the game, Settings › Apps for Xbox games, else the launcher itself.
    /// Settings › Apps when the launcher isn't installed. Links are built from validated ids only.
    /// </summary>
    public static UninstallStep Uninstall(Game game)
    {
        if (SteamCommand(game, "uninstall") is { } steam)
            return new(steam, $"Steam asks before it removes {game.Name}.");
        if (game.Source == GameSource.Gog && game.StoreId is { Length: > 0 and <= 20 } gogId && gogId.All(char.IsAsciiDigit) &&
            LauncherIcons.ProtocolHandler("goggalaxy") is not null)
            return new($"goggalaxy://openGameView/{gogId}", $"In GOG Galaxy, open the menu next to Play and choose Uninstall.");
        if (game.Source != GameSource.Xbox && LauncherTarget(game.Source) is { } launcher)
            return new(launcher, $"Find {game.Name} in your {Views.Format.LauncherAppName(game.Source)} library and choose Uninstall.", game.Source);
        return new(AppsSettings, $"Find {game.Name} in Settings › Apps and choose Uninstall.");
    }

    /// <summary>Opens the step's link, Settings page or launcher.</summary>
    public static void OpenUninstall(Game game)
    {
        var step = Uninstall(game);
        if (step.Launcher is { } source) OpenLauncher(source);
        else ShellOpen(step.Target);
    }

    // ---- Open a launcher (spec §21) ----------------------------------------------------------------------------

    /// <summary>
    /// What opening the launcher starts: the installed launcher's exe (an existing file ending in .exe, from
    /// <see cref="LauncherIcons.LauncherExe"/>), or the Xbox app's Start item. Null when it isn't installed.
    /// Registry reads, so only when a menu opens or an item is picked.
    /// </summary>
    public static string? LauncherTarget(GameSource source)
    {
        if (!Views.Format.HasBadge(source) || LauncherIcons.LauncherExe(source) is not { } target) return null;
        if (source == GameSource.Xbox) return target; // the shell:AppsFolder item, fixed in LauncherIcons
        return Path.IsPathFullyQualified(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target)
            ? target
            : null;
    }

    /// <summary>Whether the launcher is installed, so "Open …" is offered.</summary>
    public static bool CanOpenLauncher(GameSource source) => LauncherTarget(source) is not null;

    /// <summary>
    /// Opens the launcher itself with no arguments; Steam with its window (no -silent: the user asked for it).
    /// A running launcher brings its window to the front.
    /// </summary>
    public static void OpenLauncher(GameSource source)
    {
        if (LauncherTarget(source) is { } target) ShellOpen(target);
    }

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
