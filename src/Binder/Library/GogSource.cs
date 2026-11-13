using System.Security;
using Binder.Models;
using Microsoft.Win32;

namespace Binder.Library;

/// <summary>Finds installed GOG games from the registry entries GOG's installers write.</summary>
public static class GogSource
{

    public static List<Game> Scan()
    {
        var games = new List<Game>();
        try
        {
            using var root = SourceDefaults.OpenKey(SourceEntry.GogKey);
            if (root is null) return games;
            var seen = new HashSet<string>();
            foreach (var keyName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(keyName);
                if (key is null) continue;
                var game = Read(keyName,
                    key.GetValue("gameID") as string,
                    key.GetValue("gameName") as string,
                    key.GetValue("path") as string,
                    key.GetValue("exe") as string);
                if (game is not null && seen.Add(game.Id)) games.Add(game);
            }
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
        return games;
    }

    /// <summary>
    /// Builds a game from one registry entry. The exe is launched directly, so it must be an existing .exe
    /// inside a valid install folder; anything else is rejected.
    /// </summary>
    public static Game? Read(string keyName, string? gameId, string? name, string? path, string? exe)
    {
        var id = string.IsNullOrEmpty(gameId) ? keyName : gameId;
        if (id.Length is 0 or > 20 || !id.All(char.IsAsciiDigit)) return null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(exe)) return null;

        var folder = InstallFolder.Validate(path);
        if (folder is null) return null;

        string full;
        try
        {
            full = Path.GetFullPath(Path.IsPathFullyQualified(exe) ? exe : Path.Combine(folder, exe));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        if (!InstallFolder.IsSameOrInside(full, folder) || Same(full, folder)) return null;
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;

        return new Game
        {
            Id = "gog:" + id,
            Source = GameSource.Gog,
            StoreId = id,
            Name = name.Trim(),
            InstallDir = folder,
            ExePath = full,
        };
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
