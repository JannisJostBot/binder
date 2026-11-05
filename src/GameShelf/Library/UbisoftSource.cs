using System.Security;
using GameShelf.Models;
using Microsoft.Win32;

namespace GameShelf.Library;

/// <summary>
/// Finds games installed with Ubisoft Connect: its Installs key gives each game's id and folder, and the
/// "Uplay Install {id}" entry in Settings → Apps gives its name, size and icon.
/// </summary>
public static class UbisoftSource
{
    private const string InstallsKey = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
    private const string UninstallKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<Game> Scan()
    {
        var games = new List<Game>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(InstallsKey);
            if (root is null) return games;
            using var uninstall = Registry.LocalMachine.OpenSubKey(UninstallKey);
            var seen = new HashSet<string>();
            foreach (var id in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(id);
                if (key is null) continue;
                using var entry = uninstall?.OpenSubKey("Uplay Install " + id);
                var game = Read(id,
                    key.GetValue("InstallDir") as string,
                    entry?.GetValue("DisplayName") as string,
                    entry?.GetValue("EstimatedSize") is int kb ? kb : 0,
                    entry?.GetValue("DisplayIcon") as string);
                if (game is not null && seen.Add(game.Id)) games.Add(game);
            }
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
        return games;
    }

    /// <summary>Builds a game from one install entry; null for anything incomplete or unsafe.</summary>
    public static Game? Read(string id, string? installDir, string? name, int sizeKb, string? icon)
    {
        // The id goes into a uplay:// link: digits only.
        if (id.Length is 0 or > 10 || !id.All(char.IsAsciiDigit)) return null;
        var folder = InstallFolder.Validate(installDir);
        if (folder is null) return null;
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(folder);

        // Ubisoft Connect keeps each game's icon as an .ico in its own data folder.
        string? iconPath = icon is { Length: > 0 } && icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) &&
                           Path.IsPathFullyQualified(icon) && File.Exists(icon) ? icon : null;

        return new Game
        {
            Id = "ubisoft:" + id,
            Source = GameSource.Ubisoft,
            StoreId = id,
            Name = name.Trim(),
            InstallDir = folder,
            LaunchUri = $"uplay://launch/{id}/0",
            SizeOnDisk = Math.Max(0, sizeKb) * 1024L,
            IconPath = iconPath,
        };
    }
}
