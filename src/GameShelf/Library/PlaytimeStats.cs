using System.Globalization;
using GameShelf.Models;

namespace GameShelf.Library;

/// <summary>Playtime in one period: a day, a week or a month (local time).</summary>
public sealed record PlaytimeBucket(DateTime Start, DateTime End, TimeSpan Time);

/// <summary>The "This week" card: Monday to Sunday of the current week.</summary>
public sealed record WeekSummary(IReadOnlyList<PlaytimeBucket> Days, TimeSpan Total, int GameCount, string? TopGameId, TimeSpan TopGameTime);

/// <summary>
/// Playtime charts from our own sessions only. Computed when a view opens, never on a timer. A session that
/// spans midnight (or a week or month boundary) is split between the periods it covers.
/// </summary>
public static class PlaytimeStats
{
    /// <summary>Monday 00:00 of the week <paramref name="day"/> is in.</summary>
    public static DateTime WeekStart(DateTime day) => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static WeekSummary Week(IEnumerable<Session> sessions, DateTime now)
    {
        var monday = WeekStart(now);
        var days = Enumerable.Range(0, 7).Select(i => monday.AddDays(i)).ToList();
        var perDay = new TimeSpan[7];
        var perGame = new Dictionary<string, TimeSpan>();
        foreach (var session in sessions)
        {
            var (start, end) = Local(session);
            for (int i = 0; i < 7; i++)
            {
                var time = Overlap(start, end, days[i], days[i].AddDays(1));
                if (time <= TimeSpan.Zero) continue;
                perDay[i] += time;
                perGame[session.GameId] = perGame.GetValueOrDefault(session.GameId) + time;
            }
        }
        var top = perGame.Count == 0 ? default : perGame.MaxBy(p => p.Value);
        return new WeekSummary(
            days.Select((d, i) => new PlaytimeBucket(d, d.AddDays(1), perDay[i])).ToList(),
            TimeSpan.FromTicks(perDay.Sum(t => t.Ticks)),
            perGame.Count,
            top.Key,
            top.Value);
    }

    /// <summary>The last <paramref name="count"/> weeks (Monday to Sunday), oldest first; the last one is this week.</summary>
    public static List<PlaytimeBucket> Weeks(IEnumerable<Session> sessions, DateTime now, int count = 8)
    {
        var thisWeek = WeekStart(now);
        var starts = Enumerable.Range(0, count).Select(i => thisWeek.AddDays(-7 * (count - 1 - i))).ToList();
        return Buckets(sessions, starts, s => s.AddDays(7));
    }

    /// <summary>The last <paramref name="count"/> calendar months, oldest first; the last one is this month.</summary>
    public static List<PlaytimeBucket> Months(IEnumerable<Session> sessions, DateTime now, int count = 12)
    {
        var thisMonth = new DateTime(now.Year, now.Month, 1);
        var starts = Enumerable.Range(0, count).Select(i => thisMonth.AddMonths(-(count - 1 - i))).ToList();
        return Buckets(sessions, starts, s => s.AddMonths(1));
    }

    private static List<PlaytimeBucket> Buckets(IEnumerable<Session> sessions, List<DateTime> starts, Func<DateTime, DateTime> next)
    {
        var times = new TimeSpan[starts.Count];
        foreach (var session in sessions)
        {
            var (start, end) = Local(session);
            for (int i = 0; i < starts.Count; i++) times[i] += Overlap(start, end, starts[i], next(starts[i]));
        }
        return starts.Select((s, i) => new PlaytimeBucket(s, next(s), times[i])).ToList();
    }

    private static (DateTime Start, DateTime End) Local(Session session) =>
        (session.Start.LocalDateTime, session.End.LocalDateTime);

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime from, DateTime to)
    {
        var a = start > from ? start : from;
        var b = end < to ? end : to;
        return b > a ? b - a : TimeSpan.Zero;
    }

    // Labels: month and day names in the user's format, like the rest of Windows.
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static string DayLetter(DateTime day) => day.ToString("ddd", Culture)[..1].ToUpper(Culture);
    public static string DayName(DateTime day) => day.ToString("dddd", Culture);
    /// <summary>"Sep 14", "14. Sept.", "14 sept.": the culture's month-day pattern with the short month name.</summary>
    public static string WeekLabel(DateTime monday) =>
        monday.ToString(Culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM"), Culture);
    public static string MonthLabel(DateTime month) => month.ToString("MMM", Culture);
    public static string MonthName(DateTime month) => month.ToString(Culture.DateTimeFormat.YearMonthPattern, Culture);
}
