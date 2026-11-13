using Binder.Models;

namespace Binder.Library;

/// <summary>The shelf's sort orders (design board 7). Pure, so it's unit-tested.</summary>
public static class ShelfOrder
{
    public static List<Game> Sort(IEnumerable<Game> games, SortBy sortBy, bool favoritesFirst,
        IReadOnlyDictionary<string, DateTimeOffset> lastPlayed, IReadOnlyDictionary<string, TimeSpan> playtime)
    {
        // Culture-aware, so umlauts and accents sort where people expect them.
        var byName = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);

        IOrderedEnumerable<Game> ordered = favoritesFirst
            ? games.OrderByDescending(g => g.Favorite)
            : games.OrderBy(_ => 0);

        ordered = sortBy switch
        {
            SortBy.MostPlayed => ordered.ThenByDescending(g => playtime.GetValueOrDefault(g.Id)),
            SortBy.Name => ordered.ThenBy(g => g.Name, byName),
            SortBy.RecentlyAdded => ordered.ThenByDescending(g => g.AddedAt ?? DateTimeOffset.MinValue),
            SortBy.Size => ordered.ThenByDescending(g => g.SizeOnDisk),
            _ => ordered.ThenByDescending(g => lastPlayed.TryGetValue(g.Id, out var t) ? t : DateTimeOffset.MinValue),
        };
        // Ties (never played, same size, …) fall back to the name.
        return ordered.ThenBy(g => g.Name, byName).ToList();
    }

    /// <summary>Total playtime and last played time per game id, from the session log.</summary>
    public static (Dictionary<string, DateTimeOffset> LastPlayed, Dictionary<string, TimeSpan> Playtime) Stats(
        IEnumerable<Session> sessions)
    {
        var last = new Dictionary<string, DateTimeOffset>();
        var total = new Dictionary<string, TimeSpan>();
        foreach (var s in sessions)
        {
            if (!last.TryGetValue(s.GameId, out var t) || s.End > t) last[s.GameId] = s.End;
            if (s.End > s.Start) total[s.GameId] = total.GetValueOrDefault(s.GameId) + (s.End - s.Start);
        }
        return (last, total);
    }
}
