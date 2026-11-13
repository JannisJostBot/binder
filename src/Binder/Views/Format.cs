using System.Globalization;

namespace Binder.Views;

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

    /// <summary>"Unreal Engine 5.4", "Unity 6", or the engine's name alone when its version isn't known.</summary>
    public static string EngineLabel(Library.GameEngine engine, string? version) => version is null
        ? Library.LaunchOptions.EngineName(engine)
        : Strings.Format(Strings.EngineWithVersion, Library.LaunchOptions.EngineName(engine), version);

    /// <summary>
    /// The engine tag's icon: a Segoe Fluent glyph that suits the engine's name, not its logo (all of them also in
    /// Segoe MDL2 Assets, for Windows 10). The controller for engines without one of their own.
    /// </summary>
    public static string EngineGlyph(Library.GameEngine engine) => engine switch
    {
        Library.GameEngine.Unity => "",                                  // a cube
        Library.GameEngine.Unreal => "",                                 // a lightning bolt
        Library.GameEngine.Source or Library.GameEngine.Source2 => "",   // braces, as in source code
        Library.GameEngine.Godot => "",                                  // a robot
        Library.GameEngine.Frostbite or Library.GameEngine.Snowdrop => "", // a thermometer: cold
        Library.GameEngine.ReEngine => "",                               // a heartbeat
        Library.GameEngine.Anvil => "",                                  // a wrench: tools at the anvil
        Library.GameEngine.Dunia => "",                                  // a globe: open worlds
        Library.GameEngine.CryEngine => "",                              // an eye
        Library.GameEngine.IdTech => "",                                 // a flame
        Library.GameEngine.Glacier => "",                                // peaks
        Library.GameEngine.Insomniac => "",                              // a moon: up at night
        Library.GameEngine.Illusion => "",                               // sparkles
        Library.GameEngine.GameMaker => "",                              // a brush
        Library.GameEngine.Love => "",                                   // a heart
        Library.GameEngine.RenPy => "",                                  // a speech bubble: visual novels
        Library.GameEngine.RpgMaker => "",                               // a shield
        _ => "",                                                         // a controller
    };

    /// <summary>The launcher app's own name, for "Open …" and the uninstall hints: "Epic Games Launcher", "GOG Galaxy".</summary>
    public static string LauncherAppName(Models.GameSource source) => source switch
    {
        Models.GameSource.Steam => "Steam",
        Models.GameSource.Epic => "Epic Games Launcher",
        Models.GameSource.Gog => "GOG Galaxy",
        Models.GameSource.Ea => "EA app",
        Models.GameSource.Ubisoft => "Ubisoft Connect",
        Models.GameSource.Xbox => Strings.XboxApp,
        Models.GameSource.Rockstar => "Rockstar Games Launcher",
        Models.GameSource.Hytale => "Hytale Launcher",
        _ => Strings.TheLauncher,
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
