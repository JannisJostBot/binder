using Binder.Models;

namespace Binder.Library;

/// <summary>One game's time in the year.</summary>
public sealed record GameTime(string GameId, TimeSpan Time, int Sessions);

/// <summary>The year's longest session (its Resumed parts joined), from its local start.</summary>
public sealed record LongestSession(string GameId, DateTime Start, TimeSpan Length);

/// <summary>The most days in a row with play, first and last day included.</summary>
public sealed record Streak(int Days, DateTime First, DateTime Last);

/// <summary>A playtime milestone (<see cref="Moments.MilestoneHours"/>) a game passed in the year.</summary>
public sealed record YearMilestone(string GameId, int Hours);

/// <summary>A game played again after a long break: the last day before it and the day it came back.</summary>
public sealed record Comeback(string GameId, DateTime LastPlayed, DateTime Back, int Months);

/// <summary>A launcher's share of the year (<see cref="GameSource.Manual"/> stands for every game without one).</summary>
public sealed record LauncherShare(GameSource Source, TimeSpan Time, int Percent);

/// <summary>When most of the playing happened, as card 5's headline says it.</summary>
public enum WhenKind { Evenings, WeekdayEvenings, Weekends, LateNights, Afternoons, Mornings }

/// <summary>
/// Year in review (spec §28, boards 41–43): every number a card can show, for one year (local time, January 1 to
/// December 31), from Binder's own sessions. Built in one pass over the sessions when the page opens (or another
/// year is picked), never on a timer; knows nothing about cards or looks. A session over New Year is clipped at
/// midnight; a session's Resumed parts count as one session; sessions under <see cref="Moments.MinSession"/> and
/// hidden games don't count.
/// </summary>
public sealed record YearFacts(
    int Year,
    bool SoFar,
    DateTime? Since,
    TimeSpan Total,
    int Days,
    int Sessions,
    TimeSpan[] Months,
    double[] Hours,
    GameTime[] Top,
    LongestSession? Longest,
    Streak? Streak,
    YearMilestone[] Milestones,
    string[] NewIds,
    Comeback? Back,
    LauncherShare[] Launchers,
    TimeSpan? LastYear,
    WhenKind When,
    int EveningPercent,
    int WeekendPercent,
    int LatePercent,
    DateTime? TopFirst,
    DateTime? TopLast)
{
    /// <summary>A year is offered and listed with at least this many sessions…</summary>
    public const int MinSessions = 10;
    /// <summary>…and this much playtime.</summary>
    public static readonly TimeSpan MinTime = TimeSpan.FromHours(5);
    /// <summary>A comeback needs a break of at least this long.</summary>
    public static readonly TimeSpan MinBreak = TimeSpan.FromDays(90);
    /// <summary>The current year is offered "so far" from this month on (December).</summary>
    public const int SoFarFromMonth = 12;

    public int Games => Top.Length;

    /// <summary>The cells of <see cref="Hours"/>: Monday first, 24 hours a day.</summary>
    public static int Cell(DayOfWeek day, int hour) => ((int)day + 6) % 7 * 24 + hour;

    /// <summary>
    /// The years with enough play to tell (<see cref="MinSessions"/>, <see cref="MinTime"/>), newest first. The
    /// current year counts from December 1; later years (a wrong clock) never. One pass, totals only.
    /// </summary>
    public static int[] Years(IReadOnlyList<Session> sessions, Func<string, bool> counts, DateTime now)
    {
        var time = new Dictionary<int, TimeSpan>();
        var count = new Dictionary<int, int>();
        foreach (var session in Joined(sessions, counts))
        {
            var seen = new HashSet<int>(2);
            foreach (var (start, end) in session.Parts)
                for (int year = start.Year; year <= end.Year; year++)
                {
                    var part = Overlap(start, end, new DateTime(year, 1, 1), new DateTime(year + 1, 1, 1));
                    if (part <= TimeSpan.Zero) continue;
                    time[year] = time.GetValueOrDefault(year) + part;
                    if (seen.Add(year)) count[year] = count.GetValueOrDefault(year) + 1; // over New Year: one in each
                }
        }
        return [.. time.Keys
            .Where(y => (y < now.Year || y == now.Year && now.Month >= SoFarFromMonth) &&
                        count.GetValueOrDefault(y) >= MinSessions && time[y] >= MinTime)
            .OrderByDescending(y => y)];
    }

    /// <summary>
    /// The year's facts. <paramref name="counts"/> says whether a game counts (not hidden); <paramref name="sourceOf"/>
    /// gives its launcher (null for a game Binder no longer knows, counted as other).
    /// </summary>
    public static YearFacts Build(IReadOnlyList<Session> sessions, int year, Func<string, bool> counts,
        Func<string, GameSource?> sourceOf, DateTime now)
    {
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);
        var joined = Joined(sessions, counts);

        var total = TimeSpan.Zero;
        var lastYear = TimeSpan.Zero;
        bool playedBefore = false;
        var months = new TimeSpan[12];
        var hourMinutes = new double[7 * 24];
        var perGame = new Dictionary<string, (TimeSpan Time, int Sessions, DateTime First, DateTime Last)>();
        var before = new Dictionary<string, TimeSpan>(); // playtime before the year, for milestones
        var days = new HashSet<DateTime>();
        var firstEver = new Dictionary<string, DateTime>();
        var lastEnd = new Dictionary<string, DateTime>(); // per game, the end of its previous session (sorted pass)
        LongestSession? longest = null;
        Comeback? back = null;
        DateTime? since = null;

        foreach (var session in joined) // sorted by start
        {
            var (gameId, start, end) = (session.GameId, session.Start, session.End);
            firstEver.TryAdd(gameId, start);
            if (lastEnd.TryGetValue(gameId, out var previous) && start >= from && start < to && start - previous >= MinBreak &&
                (back is null || start - previous > back.Back - back.LastPlayed))
                back = new Comeback(gameId, previous.Date, start.Date, (int)((start - previous).TotalDays / 30.44));
            if (!lastEnd.TryGetValue(gameId, out var last) || end > last) lastEnd[gameId] = end;

            var inYear = TimeSpan.Zero;
            DateTime? first = null, lastPart = null;
            foreach (var (partStart, partEnd) in session.Parts)
            {
                if (partStart < from)
                {
                    playedBefore = true;
                    before[gameId] = before.GetValueOrDefault(gameId) + Overlap(partStart, partEnd, DateTime.MinValue, from);
                    lastYear += Overlap(partStart, partEnd, from.AddYears(-1), from);
                }
                var clippedStart = partStart < from ? from : partStart;
                var clippedEnd = partEnd > to ? to : partEnd;
                if (clippedEnd <= clippedStart) continue;
                inYear += clippedEnd - clippedStart;
                first ??= clippedStart;
                lastPart = clippedEnd;
                // Months, days and the hour grid: step through the part hour by hour (sessions are hours long).
                for (var at = clippedStart; at < clippedEnd;)
                {
                    var next = new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0).AddHours(1);
                    if (next > clippedEnd) next = clippedEnd;
                    var part = next - at;
                    months[at.Month - 1] += part;
                    hourMinutes[Cell(at.DayOfWeek, at.Hour)] += part.TotalMinutes;
                    days.Add(at.Date);
                    at = next;
                }
            }
            if (first is not { } firstInYear || lastPart is not { } lastInYear) continue;

            since ??= firstInYear;
            total += inYear;
            (TimeSpan Time, int Sessions, DateTime First, DateTime Last) game =
                perGame.TryGetValue(gameId, out var known) ? known : (TimeSpan.Zero, 0, firstInYear, lastInYear);
            perGame[gameId] = (game.Time + inYear, game.Sessions + 1, game.First < firstInYear ? game.First : firstInYear,
                game.Last > lastInYear ? game.Last : lastInYear);
            if (longest is null || inYear > longest.Length) longest = new LongestSession(gameId, firstInYear, inYear);
        }

        var top = perGame.OrderByDescending(p => p.Value.Time).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new GameTime(p.Key, p.Value.Time, p.Value.Sessions)).ToArray();

        var milestones = new List<YearMilestone>();
        foreach (var game in top)
        {
            var earlier = before.GetValueOrDefault(game.GameId);
            var after = earlier + game.Time;
            int passed = 0;
            foreach (var hours in Moments.MilestoneHours)
                if (earlier.TotalHours < hours && after.TotalHours >= hours) passed = hours;
            if (passed > 0) milestones.Add(new YearMilestone(game.GameId, passed));
        }

        // New this year: first played in it, except in the first year with Binder (then everything is new).
        string[] newIds = playedBefore
            ? [.. top.Select(t => t.GameId).Where(id => firstEver[id] >= from).OrderBy(id => firstEver[id])]
            : [];

        var launchers = top.GroupBy(t => Launcher(sourceOf(t.GameId)))
            .Select(g => (Source: g.Key, Time: TimeSpan.FromTicks(g.Sum(t => t.Time.Ticks))))
            .OrderByDescending(l => l.Time).ToList();
        var percents = Percents(launchers.Select(l => l.Time.TotalMinutes).ToArray());
        var shares = launchers.Select((l, i) => new LauncherShare(l.Source, l.Time, percents[i])).ToArray();

        double minutes = Math.Max(1, total.TotalMinutes);
        double Share(Func<int, int, bool> where)
        {
            double sum = 0;
            for (int d = 0; d < 7; d++)
                for (int h = 0; h < 24; h++)
                    if (where(d, h)) sum += hourMinutes[d * 24 + h];
            return sum / minutes;
        }
        double evening = Share((_, h) => h is >= 18 and < 22), weekend = Share((d, _) => d >= 5), late = Share((_, h) => h < 5),
            afternoon = Share((_, h) => h is >= 12 and < 18), morning = Share((_, h) => h is >= 5 and < 12),
            night = Share((_, h) => h >= 22);
        var when = late >= 0.3 ? WhenKind.LateNights
            : weekend >= 0.5 ? WhenKind.Weekends
            : evening + night >= Math.Max(afternoon, morning) ? (weekend < 0.3 ? WhenKind.WeekdayEvenings : WhenKind.Evenings)
            : afternoon >= morning ? WhenKind.Afternoons : WhenKind.Mornings;

        var topGame = top.Length > 0 ? perGame[top[0].GameId] : default;
        return new YearFacts(
            year,
            SoFar: now.Year == year,
            Since: playedBefore ? null : since?.Date,
            total,
            days.Count,
            perGame.Values.Sum(g => g.Sessions),
            months,
            Levels(hourMinutes),
            top,
            longest,
            LongestStreak(days),
            [.. milestones.OrderByDescending(m => m.Hours).Take(3)],
            newIds,
            back,
            shares,
            lastYear > TimeSpan.Zero ? lastYear : null,
            when,
            (int)Math.Round(evening * 100),
            (int)Math.Round(weekend * 100),
            (int)Math.Round(late * 100),
            top.Length > 0 ? topGame.First.Date : null,
            top.Length > 0 ? topGame.Last.Date : null);
    }

    /// <summary>The busiest month (0–11).</summary>
    public int BusiestMonth
    {
        get
        {
            int best = 0;
            for (int i = 1; i < 12; i++)
                if (Months[i] > Months[best]) best = i;
            return best;
        }
    }

    /// <summary>
    /// Sessions in local time, sorted by start, with each Resumed part (after the PC slept) joined to the session
    /// before it of the same game, only games that count and sessions of <see cref="Moments.MinSession"/> or more.
    /// </summary>
    private static List<JoinedSession> Joined(IReadOnlyList<Session> sessions, Func<string, bool> counts)
    {
        var sorted = sessions.Where(s => s.End > s.Start && counts(s.GameId)).OrderBy(s => s.Start).ToList();
        var joined = new List<JoinedSession>(sorted.Count);
        var open = new Dictionary<string, JoinedSession>(); // a game's last session in the list
        foreach (var s in sorted)
        {
            var part = (s.Start.LocalDateTime, s.End.LocalDateTime);
            if (s.Resumed && open.TryGetValue(s.GameId, out var session))
            {
                session.Parts.Add(part); // the time asleep between the parts isn't playtime
                continue;
            }
            session = new JoinedSession(s.GameId, [part]);
            open[s.GameId] = session;
            joined.Add(session);
        }
        return [.. joined.Where(j => j.Played >= Moments.MinSession)];
    }

    /// <summary>A session with its parts (more than one after the PC slept), in local time.</summary>
    private sealed record JoinedSession(string GameId, List<(DateTime Start, DateTime End)> Parts)
    {
        public DateTime Start => Parts[0].Start;
        public DateTime End => Parts[^1].End;
        public TimeSpan Played => TimeSpan.FromTicks(Parts.Sum(p => (p.End - p.Start).Ticks));
    }

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime from, DateTime to)
    {
        var a = start > from ? start : from;
        var b = end < to ? end : to;
        return b > a ? b - a : TimeSpan.Zero;
    }

    private static GameSource Launcher(GameSource? source) =>
        source is null or GameSource.Shortcut or GameSource.Manual ? GameSource.Manual : source.Value;

    /// <summary>Whole percents that add up to 100 (largest remainder), for the launcher shares.</summary>
    public static int[] Percents(double[] values)
    {
        double sum = values.Sum();
        if (values.Length == 0 || sum <= 0) return new int[values.Length];
        var exact = values.Select(v => v * 100 / sum).ToArray();
        var whole = exact.Select(e => (int)Math.Floor(e)).ToArray();
        int left = 100 - whole.Sum();
        foreach (var i in Enumerable.Range(0, values.Length).OrderByDescending(i => exact[i] - whole[i]).ThenBy(i => i).Take(left)) whole[i]++;
        return whole;
    }

    /// <summary>The hour grid as levels 0–4: 0 nothing, then quarters of the busiest hour.</summary>
    private static double[] Levels(double[] minutes)
    {
        double max = minutes.Max();
        return [.. minutes.Select(m => m <= 0 || max <= 0 ? 0 : Math.Clamp(Math.Ceiling(m / max * 4), 1, 4))];
    }

    private static Streak? LongestStreak(HashSet<DateTime> days)
    {
        Streak? best = null;
        foreach (var day in days)
        {
            if (days.Contains(day.AddDays(-1))) continue; // not the first day of a run
            var last = day;
            while (days.Contains(last.AddDays(1))) last = last.AddDays(1);
            int length = (int)(last - day).TotalDays + 1;
            if (length >= 2 && (best is null || length > best.Days || length == best.Days && day > best.First))
                best = new Streak(length, day, last);
        }
        return best;
    }
}
