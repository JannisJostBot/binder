namespace GameShelf.Models;

public enum DockPosition { Right, Left, Free }

/// <summary>Flyout (default): opens from the tray icon or shortcut, hides when you click away. Desktop: always there, behind windows.</summary>
public enum DisplayMode { Flyout, Desktop }

/// <summary>The global shortcut that opens the panel.</summary>
public enum Hotkey { WinShiftG, WinShiftJ, CtrlAltG, Off }

public enum SortBy { RecentlyPlayed, MostPlayed, Name, RecentlyAdded, Size }

/// <summary>Cover size on the shelf: Small = 5 columns, Medium = 4, Large = 3, or a list.</summary>
public enum GridDensity { Medium, Small, Large, List }

public sealed class Settings
{
    public int HideDelaySeconds { get; set; } = 10;
    public bool ComeBackOnClose { get; set; } = true;

    public bool SteamEnabled { get; set; } = true;
    public bool EpicEnabled { get; set; } = true;
    public bool GogEnabled { get; set; } = true;
    public bool EaEnabled { get; set; } = true;
    public bool UbisoftEnabled { get; set; } = true;
    public bool ShortcutsEnabled { get; set; } = true;

    public DockPosition Position { get; set; } = DockPosition.Right;

    /// <summary>Monitor device name (e.g. \\.\DISPLAY2) to dock on; null or unknown = the primary monitor.</summary>
    public string? Monitor { get; set; }

    /// <summary>Top-left corner in Free position, in physical screen pixels.</summary>
    public double? FreeLeft { get; set; }
    public double? FreeTop { get; set; }

    public SortBy SortBy { get; set; } = SortBy.RecentlyPlayed;
    public bool FavoritesFirst { get; set; } = true;
    public GridDensity GridDensity { get; set; } = GridDensity.Medium;
    /// <summary>The "This week" card on the shelf: playtime per day from our own sessions.</summary>
    public bool ShowWeeklySummary { get; set; } = true;
    /// <summary>The shelf's top slot shows "This week" instead of "Continue playing" (the last choice is kept).</summary>
    public bool SpotlightShowsWeek { get; set; }

    public DisplayMode DisplayMode { get; set; } = DisplayMode.Flyout;
    public bool TrayIcon { get; set; } = true;
    /// <summary>Stay running (tiny, in efficiency mode) after the flyout closes, so it opens instantly.</summary>
    public bool StayReady { get; set; } = true;
    public bool CloseOnClickAway { get; set; } = true;
    public Hotkey Hotkey { get; set; } = Hotkey.WinShiftG;
    public bool SetupDone { get; set; }

    public bool StayOnDesktop { get; set; } = true;
    public bool StartWithWindows { get; set; }

    /// <summary>Whether games from this source show on the shelf. Manually added games always do.</summary>
    public bool IsSourceEnabled(GameSource source) => source switch
    {
        GameSource.Steam => SteamEnabled,
        GameSource.Epic => EpicEnabled,
        GameSource.Gog => GogEnabled,
        GameSource.Ea => EaEnabled,
        GameSource.Ubisoft => UbisoftEnabled,
        GameSource.Shortcut => ShortcutsEnabled,
        _ => true,
    };
}
