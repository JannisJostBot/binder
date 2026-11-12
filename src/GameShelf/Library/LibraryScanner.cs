using GameShelf.Models;

namespace GameShelf.Library;

/// <summary>Runs the enabled sources and merges results with the stored library.</summary>
public static class LibraryScanner
{
    /// <summary>Every launcher Game Shelf reads, in the order Settings lists them.</summary>
    public static readonly GameSource[] StoreSources =
        [GameSource.Steam, GameSource.Epic, GameSource.Gog, GameSource.Ea, GameSource.Ubisoft, GameSource.Xbox, GameSource.Rockstar, GameSource.Hytale];

    /// <summary>
    /// First-run setup: how many games each launcher has on this PC, also for launchers that are turned off (the
    /// regular rescan skips those, so their games aren't in the library to count). Only when setup opens.
    /// </summary>
    public static Dictionary<GameSource, int> CountAll()
    {
        var counts = new Dictionary<GameSource, int>();
        foreach (var source in StoreSources)
        {
            try
            {
                counts[source] = ScanOne(source).Count;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                counts[source] = 0;
            }
        }
        return counts;
    }

    private static List<Game> ScanOne(GameSource source) => source switch
    {
        GameSource.Steam => SteamSource.Scan(),
        GameSource.Epic => EpicSource.Scan(),
        GameSource.Gog => GogSource.Scan(),
        GameSource.Ea => EaSource.Scan(),
        GameSource.Ubisoft => UbisoftSource.Scan(),
        GameSource.Xbox => XboxSource.Scan(),
        GameSource.Rockstar => RockstarSource.Scan(),
        GameSource.Hytale => HytaleSource.Scan(),
        _ => [],
    };

    /// <summary>A rescan's library, and the games that left it (uninstalled or deleted).</summary>
    public sealed record RescanResult(List<Game> Games, List<Game> Dropped);

    /// <summary>
    /// Scans the enabled stores and merges them with the stored library. <paramref name="removed"/> (the kept
    /// settings of games that left, <see cref="RemovedGames"/>) is updated: a game that's back gets its settings
    /// again. Off the UI thread.
    /// </summary>
    public static RescanResult Rescan(List<Game> stored, List<Game> removed, Settings settings)
    {
        var scanned = new List<Game>();
        var scannedSources = new HashSet<GameSource>();

        foreach (var source in StoreSources)
        {
            if (!settings.IsSourceEnabled(source)) continue;
            scanned.AddRange(ScanOne(source));
            scannedSources.Add(source);
        }
        return Merge(stored, scanned, scannedSources, removed, DateTimeOffset.Now, GameFiles.IsGone);
    }

    /// <summary>
    /// The merge, without the scan (unit-tested). A scanned store's games are what it found now; its stored games
    /// it didn't find are dropped. Manual adds and shortcuts stay unless their program is gone; games of stores
    /// turned off stay as they are.
    /// </summary>
    public static RescanResult Merge(List<Game> stored, List<Game> scanned, HashSet<GameSource> scannedSources,
        List<Game> removed, DateTimeOffset now, Func<Game, bool> isGone)
    {
        var byId = new Dictionary<string, Game>(stored.Count);
        foreach (var game in stored) byId.TryAdd(game.Id, game);
        foreach (var game in scanned)
        {
            if (byId.TryGetValue(game.Id, out var old)) game.CopyUserData(old);
            else
            {
                game.AddedAt = now; // first time we see it (or again, after it was uninstalled)
                RemovedGames.Restore(removed, game);
            }
        }

        var found = new HashSet<string>(scanned.Select(g => g.Id));
        var dropped = new List<Game>();
        foreach (var game in stored)
        {
            if (scannedSources.Contains(game.Source))
            {
                if (!found.Contains(game.Id)) dropped.Add(game);
            }
            else if ((game.Source is GameSource.Manual or GameSource.Shortcut) && isGone(game))
                dropped.Add(game);
            else
                scanned.Add(game); // manual adds, imported shortcuts, stores turned off
        }
        ShortcutImporter.RemoveDuplicates(scanned); // shortcuts to a store game's own exe, from earlier imports

        scanned.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return new RescanResult(scanned, dropped);
    }
}
