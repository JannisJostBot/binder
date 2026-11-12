using GameShelf.Models;

namespace GameShelf.Library;

/// <summary>One installed game the planner may suggest removing, with what the plan weighs.</summary>
public sealed record SpaceGame(Game Game, long Size, TimeSpan Playtime, DateTimeOffset? LastPlayed, bool Recent)
{
    public bool Favorite => Game.Favorite;
    public bool SizeKnown => Size > 0;
}

/// <summary>A drive a new game could go on: its root ("D:\"), label and space, read when the page opens.</summary>
public sealed record DriveSpace(string Root, string Label, long Total, long Free);

/// <summary>What one drive would need: the games to remove (in the order they were picked) and what that frees.</summary>
public sealed record DrivePlan(DriveSpace Drive, IReadOnlyList<SpaceGame> Games, long Missing, IReadOnlyList<SpaceGame> Picked)
{
    public long Freed => Picked.Sum(g => g.Size);
    public bool FitsAlready => Missing <= 0;
    public bool Enough => Freed >= Missing;
    public TimeSpan Playtime => Picked.Aggregate(TimeSpan.Zero, (sum, g) => sum + g.Playtime);
}

/// <summary>A treemap tile in the map's own units.</summary>
public readonly record struct TileRect(double X, double Y, double W, double H);

/// <summary>
/// Free up space (spec §12, board 17): which installed games to remove, on which drive, so a new game fits.
/// Pure, so it's unit-tested. Game Shelf never deletes anything itself; the plan only suggests.
/// </summary>
public static class SpacePlanner
{
    /// <summary>Games played within this many days only go when the others aren't enough.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(14);

    public const long GB = 1024L * 1024 * 1024;

    /// <summary>The drive root a game is installed on ("D:\"), or null for games no launcher can uninstall.</summary>
    public static string? DriveOf(Game game)
    {
        if (game.Source is GameSource.Manual or GameSource.Shortcut || string.IsNullOrEmpty(game.InstallDir)) return null;
        try
        {
            var root = Path.GetPathRoot(game.InstallDir);
            return string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal) ? null : root.ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The games that count, grouped by drive root. Hidden games count (they still use space); manual games,
    /// shortcuts and games running right now don't. Last played is the later of our sessions and the store's.
    /// </summary>
    public static Dictionary<string, List<SpaceGame>> ByDrive(IEnumerable<Game> games,
        IReadOnlyDictionary<string, TimeSpan> playtime, IReadOnlyDictionary<string, DateTimeOffset> lastPlayed,
        DateTimeOffset now, IReadOnlySet<string> running)
    {
        var drives = new Dictionary<string, List<SpaceGame>>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            if (DriveOf(game) is not { } root || running.Contains(game.Id)) continue;
            DateTimeOffset? last = lastPlayed.TryGetValue(game.Id, out var ours) ? ours : null;
            if (game.StoreLastPlayed is { } store && (last is null || store > last)) last = store;
            var entry = new SpaceGame(game, Math.Max(0, game.SizeOnDisk), playtime.GetValueOrDefault(game.Id), last,
                Recent: last is { } l && now - l <= RecentWindow);
            if (!drives.TryGetValue(root, out var list)) drives[root] = list = [];
            list.Add(entry);
        }
        return drives;
    }

    /// <summary>Playtime bands; lower ones go first: never played, under 1 h, under 5 h, under 20 h, more.</summary>
    public static int Band(TimeSpan playtime) => playtime.TotalHours switch
    {
        <= 0 => 0,
        < 1 => 1,
        < 5 => 2,
        < 20 => 3,
        _ => 4,
    };

    /// <summary>
    /// The games to remove so <paramref name="missing"/> bytes are freed. Tiers in order (everything else, then
    /// games played in the last 14 days, then favorites), a later tier only when the earlier aren't enough.
    /// Within a tier, lower playtime bands first; within a band the closest fit: the smallest game that covers
    /// what's still missing, else the largest, then repeat. Finally drop picks that aren't needed, biggest first.
    /// Games without a known size are never picked.
    /// </summary>
    public static List<SpaceGame> Plan(IEnumerable<SpaceGame> games, long missing)
    {
        var picked = new List<SpaceGame>();
        if (missing <= 0) return picked;
        var known = games.Where(g => g.SizeKnown).ToList();
        long left = missing;

        void Take(IEnumerable<SpaceGame> tier)
        {
            foreach (var band in tier.GroupBy(g => Band(g.Playtime)).OrderBy(b => b.Key))
            {
                var pool = band.ToList();
                while (left > 0 && pool.Count > 0)
                {
                    // The smallest that covers what's left, else the largest: one pass each (no sort per pick).
                    var next = pool.Where(g => g.Size >= left).MinBy(g => g.Size) ?? pool.MaxBy(g => g.Size)!;
                    picked.Add(next);
                    left -= next.Size;
                    pool.Remove(next);
                }
                if (left <= 0) return;
            }
        }

        Take(known.Where(g => !g.Favorite && !g.Recent));
        if (left > 0) Take(known.Where(g => !g.Favorite && g.Recent));
        if (left > 0) Take(known.Where(g => g.Favorite));

        long total = picked.Sum(g => g.Size);
        foreach (var g in picked.OrderByDescending(g => g.Size).ToList())
        {
            if (total - g.Size >= missing)
            {
                total -= g.Size;
                picked.Remove(g);
            }
        }
        return picked;
    }

    /// <summary>The plan for one drive and a new game of <paramref name="need"/> bytes.</summary>
    public static DrivePlan PlanDrive(DriveSpace drive, IReadOnlyList<SpaceGame> games, long need)
    {
        long missing = need - drive.Free;
        return new DrivePlan(drive, games, missing, Plan(games, missing));
    }

    /// <summary>
    /// The best drive: one where the game fits already (the most room left), else a plan that frees enough with
    /// the least playtime removed, then the fewest games, then the smallest overshoot. -1 when there are no drives.
    /// </summary>
    public static int Best(IReadOnlyList<DrivePlan> plans)
    {
        int best = -1;
        for (int i = 0; i < plans.Count; i++)
            if (best < 0 || Better(plans[i], plans[best])) best = i;
        return best;
    }

    private static bool Better(DrivePlan a, DrivePlan b)
    {
        if (a.FitsAlready != b.FitsAlready) return a.FitsAlready;
        if (a.FitsAlready) return a.Missing < b.Missing; // more room left afterwards
        if (a.Enough != b.Enough) return a.Enough;
        if (!a.Enough) return a.Missing - a.Freed < b.Missing - b.Freed; // neither frees enough: the closest
        if (a.Playtime != b.Playtime) return a.Playtime < b.Playtime;
        if (a.Picked.Count != b.Picked.Count) return a.Picked.Count < b.Picked.Count;
        return a.Freed - a.Missing < b.Freed - b.Missing;
    }

    /// <summary>
    /// A squarified treemap (Bruls, Huizing, van Wijk) of <paramref name="values"/> in a w × h area. The values
    /// should be sorted largest first; the tiles come back in the same order. Zero or negative values get none.
    /// </summary>
    public static List<TileRect> Squarify(IReadOnlyList<double> values, double width, double height)
    {
        var result = new TileRect[values.Count];
        double total = values.Where(v => v > 0).Sum();
        if (total <= 0 || width <= 0 || height <= 0) return [.. result];

        double scale = width * height / total;
        double x = 0, y = 0, w = width, h = height;
        var row = new List<(int Index, double Area)>();

        double Worst(List<(int Index, double Area)> r, double side)
        {
            double sum = 0, max = 0, min = double.MaxValue;
            foreach (var (_, a) in r)
            {
                sum += a;
                max = Math.Max(max, a);
                min = Math.Min(min, a);
            }
            return Math.Max(side * side * max / (sum * sum), sum * sum / (side * side * min));
        }

        void Place(List<(int Index, double Area)> r)
        {
            double sum = r.Sum(n => n.Area);
            if (w >= h)
            {
                double cw = sum / h, cy = y;
                foreach (var (i, a) in r)
                {
                    double nh = a / cw;
                    result[i] = new TileRect(x, cy, cw, nh);
                    cy += nh;
                }
                x += cw;
                w -= cw;
            }
            else
            {
                double rh = sum / w, cx = x;
                foreach (var (i, a) in r)
                {
                    double nw = a / rh;
                    result[i] = new TileRect(cx, y, nw, rh);
                    cx += nw;
                }
                y += rh;
                h -= rh;
            }
        }

        for (int i = 0; i < values.Count;)
        {
            if (values[i] <= 0) { i++; continue; }
            var node = (i, values[i] * scale);
            double side = Math.Min(w, h);
            if (row.Count == 0 || Worst([.. row, node], side) <= Worst(row, side))
            {
                row.Add(node);
                i++;
            }
            else
            {
                Place(row);
                row.Clear();
            }
        }
        if (row.Count > 0) Place(row);
        return [.. result];
    }
}
