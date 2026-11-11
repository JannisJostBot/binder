using GameShelf.Library;
using GameShelf.Models;
using GameShelf.ViewModels;

namespace GameShelf.Services;

/// <summary>
/// Settings › Advanced's "Check" (spec §14): runs one launcher's source once with the values in use and says what
/// it found, or which entry failed. Only when clicked; reads only what's on this PC (no downloads). Call off the UI
/// thread.
/// </summary>
public static class SourceCheck
{
    public static string Run(GameSource launcher)
    {
        try
        {
            return launcher switch
            {
                GameSource.Steam => Steam(),
                GameSource.Epic => !Directory.Exists(SourceDefaults.EpicManifests())
                    ? Missing(SourceEntry.EpicManifests)
                    : Found(EpicSource.Scan().Count),
                GameSource.Gog => Registry(SourceEntry.GogKey, GogSource.Scan),
                GameSource.Ubisoft => Registry(SourceEntry.UbisoftKey, UbisoftSource.Scan),
                GameSource.Rockstar => Registry(SourceEntry.RockstarKey, RockstarSource.Scan),
                _ => "",
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return e.Message;
        }
    }

    private static string Steam()
    {
        if (SteamSource.FindSteamPath() is null) return Missing(SourceEntry.SteamFolder);
        var games = SteamSource.Scan();
        if (SourceDefaults.SteamArtFolder() is not { } art || !Directory.Exists(art))
            return Strings.Format(Strings.Dot, Found(games.Count), Missing(SourceEntry.SteamArtFolder));
        int covers = games.Count(g => CoverCache.HasLocal(g, ArtKind.Cover));
        int banners = games.Count(g => CoverCache.HasLocal(g, ArtKind.Hero));
        int logos = games.Count(g => CoverCache.HasLocal(g, ArtKind.Logo));
        return Strings.Format(Strings.Dot, Found(games.Count), Strings.Format(Strings.CheckSteamArt, covers, banners, logos));
    }

    private static string Registry(SourceEntry entry, Func<List<Game>> scan)
    {
        using (var key = SourceDefaults.OpenKey(entry))
            if (key is null) return Missing(entry);
        return Found(scan().Count);
    }

    private static string Found(int count) => Strings.Plural(count, Strings.CheckFoundOne, Strings.CheckFound);

    private static string Missing(SourceEntry entry) => Strings.Format(Strings.EntryNotFound, SettingsViewModel.EntryLabel(entry));
}
