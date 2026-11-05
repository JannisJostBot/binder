using System.Globalization;

namespace GameShelf.Views;

/// <summary>Human-readable dates and durations shared by the shelf and detail views.</summary>
public static class Format
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>The shelf's header: "Good morning" (5–12), "Good afternoon" (12–18), else "Good evening".</summary>
    public static string Greeting(DateTime now) => now.Hour switch
    {
        >= 5 and < 12 => "Good morning",
        >= 12 and < 18 => "Good afternoon",
        _ => "Good evening",
    };

    /// <summary>"Today", "Yesterday", "3 days ago", or a short date.</summary>
    public static string RelativeDay(DateTimeOffset when)
    {
        int days = (DateTime.Today - when.LocalDateTime.Date).Days;
        return days switch
        {
            <= 0 => "Today",
            1 => "Yesterday",
            < 7 => $"{days} days ago",
            _ => when.LocalDateTime.ToString("M", Culture),
        };
    }

    /// <summary>When a session started: "Yesterday, 20:14", "Thu, 18:40", or "12 March, 18:40".</summary>
    public static string SessionStart(DateTimeOffset when)
    {
        var local = when.LocalDateTime;
        int days = (DateTime.Today - local.Date).Days;
        var day = days switch
        {
            <= 0 => "Today",
            1 => "Yesterday",
            < 7 => local.ToString("ddd", Culture),
            _ => local.ToString("M", Culture),
        };
        return $"{day}, {local.ToString("t", Culture)}";
    }

    /// <summary>A single session's length: "< 1 min", "35 min", "2 h 05 min".</summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return "< 1 min";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min";
        return $"{(int)span.TotalHours} h {span.Minutes:00} min";
    }

    /// <summary>Total playtime, coarser for big numbers: "35 min", "4 h 20 min", "42 h".</summary>
    public static string Playtime(TimeSpan total)
    {
        if (total.TotalHours < 1) return $"{Math.Max(1, (int)total.TotalMinutes)} min";
        if (total.TotalHours < 10) return $"{(int)total.TotalHours} h {total.Minutes} min";
        return $"{(int)total.TotalHours} h";
    }

    /// <summary>A hide delay: "Instantly" or "10 s".</summary>
    public static string Delay(int seconds) => seconds <= 0 ? "Instantly" : $"{seconds} s";
}
