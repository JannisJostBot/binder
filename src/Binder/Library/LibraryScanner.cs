using Binder.Models;

namespace Binder.Library;

/// <summary>Runs the enabled sources and merges results with the stored library.</summary>
public static class LibraryScanner
{
    /// <summary>Every launcher Binder reads, in the order Settings lists them.</summary>
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
        return Merge(stored, scanned, scannedSources, removed, DateTimeOffset.Now, GameFiles.IsGone, GameFiles.IsOffline);
    }

    /// <summary>
    /// The merge, without the scan (unit-tested). A scanned store's games are what it found now; its stored games
    /// it didn't find are dropped, unless their drive isn't there (<paramref name="isOffline"/>: an unplugged drive
    /// or an offline share never removes anything). Manual adds and shortcuts stay unless their program is gone;
    /// games of stores turned off stay as they are.
    /// </summary>
    public static RescanResult Merge(List<Game> stored, List<Game> scanned, HashSet<GameSource> scannedSources,
        List<Game> removed, DateTimeOffset now, Func<Game, bool> isGone, Func<Game, bool>? isOffline = null)
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
                if (found.Contains(game.Id)) continue;
                if (isOffline?.Invoke(game) == true) scanned.Add(game); // its drive isn't there: kept as it was
                else dropped.Add(game);
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

    /// <summary>
    /// The same for the kept settings of removed games: the scan updated its own copy of the list
    /// (<paramref name="scanned"/>, made from <paramref name="before"/>), while the panel may have changed the list
    /// meanwhile (<paramref name="current"/>: a game added back took its entry, a game found gone added one). Those
    /// changes are applied to the scan's list too. Entries are compared by reference (each change makes a new list
    /// with the same entries). Unit-tested.
    /// </summary>
    public static void ReconcileRemoved(List<Game> scanned, IReadOnlyList<Game> before, IReadOnlyList<Game> current)
    {
        if (ReferenceEquals(before, current)) return;
        var was = new HashSet<Game>(before, ReferenceEqualityComparer.Instance);
        var now = new HashSet<Game>(current, ReferenceEqualityComparer.Instance);
        scanned.RemoveAll(g => was.Contains(g) && !now.Contains(g)); // taken back meanwhile
        foreach (var g in current)
        {
            if (was.Contains(g)) continue;
            scanned.RemoveAll(r => r.Id == g.Id); // kept meanwhile: the newer entry wins
            scanned.Add(g);
        }
    }

    /// <summary>
    /// A scan runs for a while on a copy of the library (<paramref name="before"/>: the ids it started from). Brings
    /// its result up to date with the library as it is now (<paramref name="current"/>): settings changed meanwhile
    /// are copied over, a game removed meanwhile stays removed, and one added meanwhile (Add a game, drag and drop,
    /// a shortcut import) is kept. Unit-tested.
    /// </summary>
    public static List<Game> Reconcile(List<Game> scanned, IReadOnlySet<string> before, IReadOnlyList<Game> current)
    {
        var now = new Dictionary<string, Game>(current.Count);
        foreach (var game in current) now.TryAdd(game.Id, game);
        var result = new List<Game>(scanned.Count);
        var ids = new HashSet<string>();
        foreach (var game in scanned)
        {
            if (now.TryGetValue(game.Id, out var old))
            {
                if (!ReferenceEquals(old, game)) game.CopyUserData(old);
            }
            else if (before.Contains(game.Id))
            {
                continue; // removed while the scan ran
            }
            if (ids.Add(game.Id)) result.Add(game);
        }
        bool added = false;
        foreach (var game in current)
        {
            if (before.Contains(game.Id) || !ids.Add(game.Id)) continue;
            result.Add(game); // added while the scan ran
            added = true;
        }
        if (added) result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }
}
