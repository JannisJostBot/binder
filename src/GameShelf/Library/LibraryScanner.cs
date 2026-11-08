using GameShelf.Models;

namespace GameShelf.Library;

/// <summary>Runs the enabled sources and merges results with the stored library.</summary>
public static class LibraryScanner
{
    /// <summary>Every launcher Game Shelf reads, in the order Settings lists them.</summary>
    public static readonly GameSource[] StoreSources =
        [GameSource.Steam, GameSource.Epic, GameSource.Gog, GameSource.Ea, GameSource.Ubisoft, GameSource.Xbox, GameSource.Rockstar];

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
        _ => [],
    };

    public static List<Game> Rescan(List<Game> stored, Settings settings)
    {
        var scanned = new List<Game>();
        var scannedSources = new HashSet<GameSource>();

        foreach (var source in StoreSources)
        {
            if (!settings.IsSourceEnabled(source)) continue;
            scanned.AddRange(ScanOne(source));
            scannedSources.Add(source);
        }

        var byId = new Dictionary<string, Game>(stored.Count);
        foreach (var game in stored) byId.TryAdd(game.Id, game);
        var now = DateTimeOffset.Now;
        foreach (var game in scanned)
        {
            if (byId.TryGetValue(game.Id, out var old)) game.CopyUserData(old);
            else game.AddedAt = now; // first time we see it
        }

        // Keep entries the scan doesn't own (manual adds, imported shortcuts, disabled sources).
        foreach (var game in stored)
            if (!scannedSources.Contains(game.Source)) scanned.Add(game);
        ShortcutImporter.RemoveDuplicates(scanned); // shortcuts to a store game's own exe, from earlier imports

        scanned.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return scanned;
    }
}
