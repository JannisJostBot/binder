using System.Text.Json.Serialization;
using GameShelf.Library;

namespace GameShelf.Models;

public enum GameSource { Steam, Epic, Gog, Ea, Ubisoft, Xbox, Rockstar, Hytale, Shortcut, Manual }

public sealed class Game
{
    /// <summary>Stable id, e.g. "steam:1158160". Used to link sessions and user overrides.</summary>
    public string Id { get; set; } = "";
    public GameSource Source { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Store-specific id (Steam appid, Epic AppName, GOG game id, EA content id, Ubisoft game id, Xbox package family name, Rockstar title id, "hytale").</summary>
    public string? StoreId { get; set; }
    public string? InstallDir { get; set; }

    /// <summary>Store URI (steam://, com.epicgames.launcher://) or null when launched by exe.</summary>
    public string? LaunchUri { get; set; }
    public string? ExePath { get; set; }
    public long SizeOnDisk { get; set; }

    /// <summary>When the store last started the game (Steam's LastPlayed), for games played without Game Shelf.</summary>
    public DateTimeOffset? StoreLastPlayed { get; set; }

    /// <summary>The program whose icon makes the generated cover (when there's no store art).</summary>
    public string? IconPath { get; set; }

    /// <summary>When the game first showed up in the library; null for games from before this was tracked.</summary>
    public DateTimeOffset? AddedAt { get; set; }

    /// <summary>Cached background color of the generated icon cover ("#RRGGBB"), computed once from the icon.</summary>
    public string? CoverTint { get; set; }

    // User data — preserved across rescans.
    public bool Favorite { get; set; }
    public bool Hidden { get; set; }
    public int? HideDelaySeconds { get; set; }
    public string? ProcessName { get; set; }
    public string? CustomCover { get; set; }

    /// <summary>Arguments the game starts with (milestone 10), as typed: split by the Windows command-line rules.</summary>
    public string? LaunchArgs { get; set; }

    /// <summary>"Start with": another exe inside the install folder to run instead of the normal launch.</summary>
    public string? ExeOverride { get; set; }

    /// <summary>Ask to close conflicting programs (cheat tools, debuggers) before this game starts.</summary>
    public bool AntiCheatReminder { get; set; }

    /// <summary>Game mode for this game: null = the default from Settings, else always on or off.</summary>
    public bool? GameMode { get; set; }

    // Install-folder scan (engine and anti-cheat), cached until the game's files change (see GameScan).
    public string? ScanStamp { get; set; }
    public GameEngine? Engine { get; set; }
    public List<AntiCheatKind>? AntiCheats { get; set; }

    /// <summary>The highest playtime milestone (hours) already shown in "Welcome back" (proposals §13 e).</summary>
    public int MilestoneShownHours { get; set; }

    [JsonIgnore]
    public string SizeText => FormatSize(SizeOnDisk);

    /// <summary>Short store name for badges and filters.</summary>
    [JsonIgnore]
    public string SourceLabel => Source switch
    {
        GameSource.Steam => "Steam",
        GameSource.Epic => "Epic",
        GameSource.Gog => "GOG",
        GameSource.Ea => "EA",
        GameSource.Ubisoft => "Ubisoft",
        GameSource.Xbox => "Xbox",
        GameSource.Rockstar => "Rockstar",
        GameSource.Hytale => "Hytale",
        _ => Strings.SourceOther,
    };

    /// <summary>Copies the user-owned fields from a previously stored entry.</summary>
    public void CopyUserData(Game from)
    {
        Favorite = from.Favorite;
        Hidden = from.Hidden;
        HideDelaySeconds = from.HideDelaySeconds;
        ProcessName = from.ProcessName;
        CustomCover = from.CustomCover;
        LaunchArgs = from.LaunchArgs;
        ExeOverride = from.ExeOverride;
        AntiCheatReminder = from.AntiCheatReminder;
        GameMode = from.GameMode;
        MilestoneShownHours = from.MilestoneShownHours;
        if (ScanStamp is null)
        {
            ScanStamp = from.ScanStamp;
            Engine = from.Engine;
            AntiCheats = from.AntiCheats;
        }
        AddedAt = from.AddedAt;
        // The cached icon and its color belong to the picture they were read from (an Xbox game's exe icon
        // before its tile logo was used, say); a different one has to be read again.
        if (string.Equals(IconSource, from.IconSource, StringComparison.OrdinalIgnoreCase)) CoverTint ??= from.CoverTint;
        else IconChanged = true;
    }

    /// <summary>A rescan found a different <see cref="IconSource"/> than the stored entry: its cached icon is stale.</summary>
    [JsonIgnore]
    public bool IconChanged { get; set; }

    /// <summary>The program to take the icon from: <see cref="IconPath"/>, else the exe the game runs as.</summary>
    [JsonIgnore]
    public string? IconSource => IconPath ??
        (ExePath is { } exe && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : null);

    /// <summary>"Today", "3 days ago", … for the list view; set by the shelf, not stored.</summary>
    [JsonIgnore]
    public string LastPlayedText { get; set; } = "";

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        const double GB = 1024d * 1024 * 1024, MB = 1024d * 1024;
        return bytes >= GB ? Strings.Format(Strings.SizeGb, (bytes / GB).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture))
            : Strings.Format(Strings.SizeMb, (bytes / MB).ToString("0", System.Globalization.CultureInfo.CurrentCulture));
    }
}
