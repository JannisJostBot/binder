using System.Text.Json.Serialization;

namespace GameShelf.Models;

public enum GameSource { Steam, Epic, Gog, Ea, Ubisoft, Shortcut, Manual }

public sealed class Game
{
    /// <summary>Stable id, e.g. "steam:1158160". Used to link sessions and user overrides.</summary>
    public string Id { get; set; } = "";
    public GameSource Source { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Store-specific id (Steam appid, Epic AppName, GOG game id, EA content id, Ubisoft game id).</summary>
    public string? StoreId { get; set; }
    public string? InstallDir { get; set; }

    /// <summary>Store URI (steam://, com.epicgames.launcher://) or null when launched by exe.</summary>
    public string? LaunchUri { get; set; }
    public string? ExePath { get; set; }
    public long SizeOnDisk { get; set; }

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
        _ => "Other",
    };

    /// <summary>Copies the user-owned fields from a previously stored entry.</summary>
    public void CopyUserData(Game from)
    {
        Favorite = from.Favorite;
        Hidden = from.Hidden;
        HideDelaySeconds = from.HideDelaySeconds;
        ProcessName = from.ProcessName;
        CustomCover = from.CustomCover;
        AddedAt = from.AddedAt;
        CoverTint ??= from.CoverTint;
    }

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
        return bytes >= GB ? $"{bytes / GB:0.#} GB" : $"{bytes / MB:0} MB";
    }
}
