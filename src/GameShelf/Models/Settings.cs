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
    public bool XboxEnabled { get; set; } = true;
    public bool RockstarEnabled { get; set; } = true;
    public bool HytaleEnabled { get; set; } = true;
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
    /// <summary>
    /// With <see cref="StayReady"/>: keep the closed flyout loaded (hidden) so it opens at once, instead of
    /// freeing it down to a few MB. Either way the process exits while a game runs.
    /// </summary>
    public bool KeepPanelLoaded { get; set; }
    public bool CloseOnClickAway { get; set; } = true;
    public Hotkey Hotkey { get; set; } = Hotkey.WinShiftG;
    public bool SetupDone { get; set; }

    public bool StayOnDesktop { get; set; } = true;
    public bool StartWithWindows { get; set; }

    /// <summary>Game mode (spec §11): optimize Windows while a game runs, put back when it closes. Opt-in.</summary>
    public bool GameModeEnabled { get; set; }
    /// <summary>Which Game mode items are on (ids from <see cref="Shared.GameModeInfo.Items"/>).</summary>
    public List<string> GameModeItems { get; set; } = [.. Shared.GameModeInfo.DefaultItems];
    /// <summary>Apps (full exe paths) asked to close while a game runs and reopened after.</summary>
    public List<string> GameModeCloseApps { get; set; } = [];
    /// <summary>Show "Restored successfully" when the game closes (problems are always shown).</summary>
    public bool ShowRestoreToast { get; set; } = true;
    /// <summary>Play the Windows notification sound with "Windows is back to normal" (not when something failed).</summary>
    public bool RestoreSound { get; set; } = true;
    /// <summary>Exe names the user added to "kept smooth" (the game and installed voice chat apps always are).</summary>
    public List<string> KeepSmoothApps { get; set; } = [];
    /// <summary>Exe names whose uploads are slowed down while a game runs.</summary>
    public List<string> ThrottledUploadApps { get; set; } = [.. Shared.ElevatedPart.DefaultThrottledApps];
    /// <summary>Their upload limit in Mbit/s: 1, 5 or 20.</summary>
    public int UploadLimitMbps { get; set; } = Shared.ElevatedPart.DefaultUploadLimit;

    /// <summary>Whether games from this source show on the shelf. Manually added games always do.</summary>
    public bool IsSourceEnabled(GameSource source) => source switch
    {
        GameSource.Steam => SteamEnabled,
        GameSource.Epic => EpicEnabled,
        GameSource.Gog => GogEnabled,
        GameSource.Ea => EaEnabled,
        GameSource.Ubisoft => UbisoftEnabled,
        GameSource.Xbox => XboxEnabled,
        GameSource.Rockstar => RockstarEnabled,
        GameSource.Hytale => HytaleEnabled,
        GameSource.Shortcut => ShortcutsEnabled,
        _ => true,
    };

    public void SetSourceEnabled(GameSource source, bool on)
    {
        switch (source)
        {
            case GameSource.Steam: SteamEnabled = on; break;
            case GameSource.Epic: EpicEnabled = on; break;
            case GameSource.Gog: GogEnabled = on; break;
            case GameSource.Ea: EaEnabled = on; break;
            case GameSource.Ubisoft: UbisoftEnabled = on; break;
            case GameSource.Xbox: XboxEnabled = on; break;
            case GameSource.Rockstar: RockstarEnabled = on; break;
            case GameSource.Hytale: HytaleEnabled = on; break;
            case GameSource.Shortcut: ShortcutsEnabled = on; break;
        }
    }
}
