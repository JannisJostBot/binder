using GameShelf.Models;

namespace GameShelf.Library;

/// <summary>Runs the enabled sources and merges results with the stored library.</summary>
public static class LibraryScanner
{
    public static List<Game> Rescan(List<Game> stored, Settings settings)
    {
        var scanned = new List<Game>();
        var scannedSources = new HashSet<GameSource>();

        if (settings.SteamEnabled)
        {
            scanned.AddRange(SteamSource.Scan());
            scannedSources.Add(GameSource.Steam);
        }
        if (settings.EpicEnabled)
        {
            scanned.AddRange(EpicSource.Scan());
            scannedSources.Add(GameSource.Epic);
        }
        if (settings.GogEnabled)
        {
            scanned.AddRange(GogSource.Scan());
            scannedSources.Add(GameSource.Gog);
        }
        if (settings.EaEnabled)
        {
            scanned.AddRange(EaSource.Scan());
            scannedSources.Add(GameSource.Ea);
        }
        if (settings.UbisoftEnabled)
        {
            scanned.AddRange(UbisoftSource.Scan());
            scannedSources.Add(GameSource.Ubisoft);
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

        scanned.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return scanned;
    }
}
