// DEBUG BUILDS ONLY (see App.Dev.cs): left out of Release builds. Nothing here is saved.
using Binder.Models;
using Binder.ViewModels;

namespace Binder.Dev;

/// <summary>
/// Made-up years for Year in review (spec §28) in the debug panel: three years of sessions, the first starting on
/// September 14, for the library's own games (so real covers, banners and logos show), topped up with made-up
/// games when the library is small. The same every time (a fixed seed), until "now", which the panel sets to
/// December 20 so this year is "so far". Each year has its own favourite, games arrive over time, one comes back
/// after a long break, and one session has a part after the PC slept.
/// </summary>
internal static class YearReviewMock
{
    private static readonly string[] MadeUpNames =
    [
        "Lantern Keep", "Saltwater Run", "Orbit Gardens", "Northbound", "Paper Kingdoms", "Ember Lake",
        "Static Bloom", "Night Ferry", "Copper Hollow", "Tidebreak", "Quiet Engines", "Moss & Iron",
    ];

    // Launchers for the made-up games, so the "By launcher" card has something to show.
    private static readonly GameSource[] Sources = [GameSource.Steam, GameSource.Epic, GameSource.Gog, GameSource.Manual, GameSource.Epic, GameSource.Xbox];

    public static YearData Data(YearData real, DateTime now, bool removeTop)
    {
        var games = real.Games.Values.Where(g => !g.Hidden && !real.Removed.Contains(g.Id)).Take(14).ToList();
        for (int i = 0; games.Count < 10 && i < MadeUpNames.Length; i++)
            games.Add(new Game { Id = "dev-yir:" + i, Name = MadeUpNames[i], Source = Sources[i % Sources.Length] }); // no store id: nothing is downloaded

        var random = new Random(2026);
        var sessions = new List<Session>();
        int n = games.Count, first = now.Year - 2;
        var start = new DateTime(first, 9, 14);
        int comeback = n - 1; // played in the first autumn, then not again until late this year
        bool slept = false;
        for (var day = start; day < now.Date.AddDays(1); day = day.AddDays(1))
        {
            int k = day.Year - first;
            bool weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (random.NextDouble() > (weekend ? 0.62 : 0.42)) continue;
            int count = random.NextDouble() < 0.25 ? 2 : 1;
            for (int s = 0; s < count; s++)
            {
                int game = Pick(random, n, k, day, comeback, now);
                double h = random.NextDouble();
                int hour = h < 0.6 ? 19 + random.Next(4) : h < 0.75 ? 22 + random.Next(3) : h < 0.9 ? 14 + random.Next(4) : 9 + random.Next(3);
                var begin = day.AddHours(hour).AddMinutes(random.Next(60)).AddHours(s * 3); // 24 is the next day's midnight
                var length = TimeSpan.FromMinutes(20 + Math.Pow(random.NextDouble(), 2) * 280);
                var end = begin + length;
                if (end > now) continue;
                sessions.Add(Session(games[game].Id, begin, end));
                if (!slept && k == 2 && length > TimeSpan.FromHours(2))
                {
                    // The PC slept for three hours, then the game went on: one session in two parts.
                    sessions.Add(Session(games[game].Id, end.AddHours(3), end.AddHours(4), resumed: true));
                    slept = true;
                }
            }
        }
        var map = games.ToDictionary(g => g.Id);
        if (removeTop && sessions.Count > 0)
        {
            // The year's most played game was uninstalled and Binder kept no settings for it: "A removed game".
            var top = sessions.Where(x => x.End.Year == now.Year).GroupBy(x => x.GameId)
                .MaxBy(g => g.Sum(x => (x.End - x.Start).Ticks))?.Key;
            if (top is not null) map.Remove(top);
        }
        return new YearData(sessions, map, new HashSet<string>());
    }

    /// <summary>Year k's favourite is a different game; games arrive over the years; the comeback game sits out.</summary>
    private static int Pick(Random random, int n, int k, DateTime day, int comeback, DateTime now)
    {
        int available = Math.Min(n, 4 + 3 * k + day.Month / 4);
        var weights = new double[n];
        for (int i = 0; i < available; i++) weights[(i + 2 * k) % n] = 1 / Math.Pow(i + 1, 1.1);
        bool comebackPlays = k == 0 && day.Month >= 10 || k == 2 && day >= now.AddDays(-25);
        weights[comeback] = comebackPlays ? 0.6 : 0;
        double pick = random.NextDouble() * weights.Sum();
        for (int i = 0; i < n; i++)
        {
            pick -= weights[i];
            if (pick <= 0 && weights[i] > 0) return i;
        }
        return 0;
    }

    private static Session Session(string id, DateTime start, DateTime end, bool resumed = false) =>
        new() { GameId = id, Start = new DateTimeOffset(start), End = new DateTimeOffset(end), Resumed = resumed };
}
