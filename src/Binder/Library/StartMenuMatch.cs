using Binder.Models;

namespace Binder.Library;

/// <summary>
/// Which games already have a Start menu entry of their own (spec §22 c, d): a Start shortcut whose store link,
/// exe or install folder is the game's. Pure; the entries come from <c>Services.StartMenuGames.ReadEntries</c>.
/// </summary>
public static class StartMenuMatch
{
    /// <summary>The ids of the games that Start search already finds. Xbox app games are Start apps themselves.</summary>
    public static HashSet<string> GamesWithEntry(IEnumerable<ShortcutFile> entries, IEnumerable<Game> games)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        // Built once, then each entry is a few lookups (never a search over the games per entry).
        var steam = new Dictionary<string, string>(StringComparer.Ordinal);
        var epic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ubisoft = new Dictionary<string, string>(StringComparer.Ordinal);
        var exes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            if (game.Source == GameSource.Xbox)
            {
                found.Add(game.Id);
                continue;
            }
            if (game.StoreId is { Length: > 0 } id)
            {
                if (game.Source == GameSource.Steam) steam.TryAdd(id, game.Id);
                else if (game.Source == GameSource.Epic) epic.TryAdd(id, game.Id);
                else if (game.Source == GameSource.Ubisoft) ubisoft.TryAdd(id, game.Id);
            }
            if (Full(game.ExePath) is { } exe && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exes.TryAdd(exe, game.Id);
            if (Full(game.ExeOverride) is { } over) exes.TryAdd(over, game.Id);
            if (Full(game.InstallDir) is { } dir) folders.TryAdd(Path.TrimEndingDirectorySeparator(dir), game.Id);
        }

        foreach (var entry in entries)
        {
            string? id = null;
            if (entry.SteamAppId is { } appId) steam.TryGetValue(appId, out id);
            else if (entry.EpicAppName is { } app) epic.TryGetValue(app, out id);
            else if (entry.UbisoftId is { } uplay) ubisoft.TryGetValue(uplay, out id);
            else if (Full(entry.TargetPath) is { } target)
            {
                if (!exes.TryGetValue(target, out id))
                    for (var dir = Path.GetDirectoryName(target); id is null && !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                        folders.TryGetValue(Path.TrimEndingDirectorySeparator(dir), out id);
            }
            if (id is not null) found.Add(id);
        }
        return found;
    }

    /// <summary>How many of <paramref name="games"/> Start search doesn't find.</summary>
    public static int CountMissing(IEnumerable<ShortcutFile> entries, IReadOnlyCollection<Game> games)
    {
        var found = GamesWithEntry(entries, games);
        return games.Count(g => !found.Contains(g.Id));
    }

    private static string? Full(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return null;
        try { return Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
