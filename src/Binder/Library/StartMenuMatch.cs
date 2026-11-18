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
        // An imported desktop shortcut is named as the launcher named it, and so is that launcher's Start entry
        // ("Anno 1800.url" in both, uplay:// inside); the stored copy's link isn't read here (pure code).
        var shortcutNames = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var game in games)
        {
            if (game.Source == GameSource.Xbox)
            {
                found.Add(game.Id);
                continue;
            }
            if (game.Source == GameSource.Shortcut && game.Name.Length > 0) shortcutNames.TryAdd(game.Name, game.Id);
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
            if (shortcutNames.TryGetValue(entry.Name, out var named)) found.Add(named);
        }
        return found;
    }

    /// <summary>How many of <paramref name="games"/> Start search doesn't find.</summary>
    public static int CountMissing(IEnumerable<ShortcutFile> entries, IReadOnlyCollection<Game> games)
    {
        var found = GamesWithEntry(entries, games);
        return games.Count(g => !found.Contains(g.Id));
    }

    /// <summary>
    /// Spec §22 c: the shortcuts Binder's Start folder should hold, one per game Start search doesn't find yet, by
    /// name ("Hades II.lnk", "Hades II (2).lnk" for a second game of that name), in name order.
    /// </summary>
    public static List<(string FileName, Game Game)> Plan(IEnumerable<Game> shelf, IReadOnlySet<string> withEntry)
    {
        var plan = new List<(string, Game)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in shelf.Where(g => !withEntry.Contains(g.Id) && IsPlayableId(g.Id)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Id, StringComparer.Ordinal))
        {
            var name = FileNameFor(game.Name, game.Id);
            var file = name + ".lnk";
            for (int n = 2; !taken.Add(file); n++) file = $"{name} ({n}).lnk";
            plan.Add((file, game));
        }
        return plan;
    }

    /// <summary>
    /// An id "--play" can carry as one plain argument (and that App.PlayArgument accepts): no quotes, backslashes,
    /// spaces or control characters, at most 256 characters. Library ids are store ids and hashes, so this holds.
    /// </summary>
    public static bool IsPlayableId(string id) =>
        id.Length is > 0 and <= 256 && !id.Any(c => c is '"' or '\\' || char.IsWhiteSpace(c) || char.IsControl(c));

    /// <summary>A safe file name from a game's name: no path characters, no leading or trailing dots and spaces, not a device name, at most 80 characters.</summary>
    public static string FileNameFor(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => c < 32 || Array.IndexOf(invalid, c) >= 0 || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? ' ' : c).ToArray();
        var clean = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (clean.Length > 80) clean = clean[..80].TrimEnd(' ', '.');
        if (clean.Length > 0 && !IsDeviceName(clean)) return clean;
        var id = new string(fallback.Where(char.IsAsciiLetterOrDigit).Take(40).ToArray()); // rare: a name of only symbols
        return id.Length > 0 && !IsDeviceName(id) ? id : "Binder";
    }

    /// <summary>CON, PRN, AUX, NUL, COM1–9, LPT1–9 (also with an extension): names Windows keeps for devices.</summary>
    private static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].Trim().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsAsciiDigit(stem[3]));
    }

    private static string? Full(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return null;
        try { return Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
