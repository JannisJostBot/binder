using System.Globalization;

namespace GameShelf.Views;

/// <summary>Human-readable dates and durations shared by the shelf and detail views.</summary>
public static class Format
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>The shelf's header: "Good morning" (5–12), "Good afternoon" (12–18), else "Good evening".</summary>
    public static string Greeting(DateTime now) => now.Hour switch
    {
        >= 5 and < 12 => Strings.GoodMorning,
        >= 12 and < 18 => Strings.GoodAfternoon,
        _ => Strings.GoodEvening,
    };

    /// <summary>"Today", "Yesterday", "3 days ago", or a short date.</summary>
    public static string RelativeDay(DateTimeOffset when)
    {
        int days = (DateTime.Today - when.LocalDateTime.Date).Days;
        return days switch
        {
            <= 0 => Strings.Today,
            1 => Strings.Yesterday,
            < 7 => Strings.Format(Strings.DaysAgo, days),
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
            <= 0 => Strings.Today,
            1 => Strings.Yesterday,
            < 7 => local.ToString("ddd", Culture),
            _ => local.ToString("M", Culture),
        };
        return Strings.Format(Strings.DayAndTime, day, local.ToString("t", Culture));
    }

    /// <summary>A single session's length: "< 1 min", "35 min", "2 h 05 min".</summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return Strings.UnderAMinute;
        if (span.TotalHours < 1) return Strings.Format(Strings.Minutes, (int)span.TotalMinutes);
        return Strings.Format(Strings.HoursMinutes, (int)span.TotalHours, span.Minutes.ToString("00", Culture));
    }

    /// <summary>Total playtime, coarser for big numbers: "35 min", "4 h 20 min", "42 h".</summary>
    public static string Playtime(TimeSpan total)
    {
        if (total.TotalHours < 1) return Strings.Format(Strings.Minutes, Math.Max(1, (int)total.TotalMinutes));
        if (total.TotalHours < 10) return Strings.Format(Strings.HoursMinutes, (int)total.TotalHours, total.Minutes);
        return Strings.Format(Strings.Hours, (int)total.TotalHours);
    }

    /// <summary>A hide delay: "Instantly" or "10 s".</summary>
    public static string Delay(int seconds) => seconds <= 0 ? Strings.Instantly : Strings.Format(Strings.Seconds, seconds);

    /// <summary>Manually added games and imported shortcuts get no launcher badge.</summary>
    public static bool HasBadge(Models.GameSource source) => source is not (Models.GameSource.Manual or Models.GameSource.Shortcut);

    /// <summary>The launcher's name as its badge reads it out: "Epic Games", "Xbox app".</summary>
    public static string LauncherName(Models.GameSource source) => source switch
    {
        Models.GameSource.Steam => "Steam",
        Models.GameSource.Epic => "Epic Games",
        Models.GameSource.Gog => "GOG",
        Models.GameSource.Ea => "EA app",
        Models.GameSource.Ubisoft => "Ubisoft Connect",
        Models.GameSource.Xbox => Strings.XboxApp,
        Models.GameSource.Rockstar => "Rockstar Games Launcher",
        Models.GameSource.Hytale => "Hytale Launcher",
        _ => source.ToString(),
    };

    /// <summary>The global shortcut as Windows writes it: "Win+Shift+G".</summary>
    public static string HotkeyLabel(Models.Hotkey hotkey) => hotkey switch
    {
        Models.Hotkey.WinShiftG => "Win+Shift+G",
        Models.Hotkey.WinShiftJ => "Win+Shift+J",
        Models.Hotkey.CtrlAltG => "Ctrl+Alt+G",
        _ => Strings.Off,
    };
}
