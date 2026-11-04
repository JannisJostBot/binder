using System.Security;
using GameShelf.Models;
using Microsoft.Win32;

namespace GameShelf.Library;

/// <summary>
/// Finds games installed with the Rockstar Games Launcher (milestone 12). Each one has an entry in Settings → Apps
/// whose uninstaller is the launcher's own, "…\Rockstar Games\Launcher\uninstall.exe" -uninstall={title}; the title
/// id is what the launcher starts it by (Launcher.exe -launchTitle={title}). Copies bought on Steam or Epic are
/// left to those sources (they have no such entry).
/// </summary>
public static class RockstarSource
{
    private static readonly string[] UninstallKeys =
    [
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    private const string LauncherKey = @"SOFTWARE\WOW6432Node\Rockstar Games\Launcher";

    public static List<Game> Scan()
    {
        var games = new List<Game>();
        var seen = new HashSet<string>();
        foreach (var path in UninstallKeys)
        {
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(path);
                if (root is null) continue;
                foreach (var keyName in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(keyName);
                    if (key?.GetValue("UninstallString") is not string uninstall || TitleOf(uninstall) is not { } title) continue;
                    var game = Read(title,
                        key.GetValue("DisplayName") as string,
                        key.GetValue("InstallLocation") as string,
                        key.GetValue("DisplayIcon") as string,
                        key.GetValue("EstimatedSize") is int kb ? kb : 0);
                    if (game is not null && seen.Add(game.Id)) games.Add(game);
                }
            }
            catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return games;
    }

    /// <summary>
    /// The title id from the launcher's per-game uninstaller ("…\Rockstar Games\Launcher\uninstall.exe"
    /// -uninstall=gta5); null for anything else, including the launcher's own entry (-uninstall=launcher).
    /// </summary>
    public static string? TitleOf(string uninstallString)
    {
        if (!uninstallString.Contains(@"Rockstar Games\Launcher\uninstall.exe", StringComparison.OrdinalIgnoreCase)) return null;
        const string Marker = "-uninstall=";
        int at = uninstallString.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var title = uninstallString[(at + Marker.Length)..].Trim().Trim('"');
        int space = title.IndexOf(' ');
        if (space >= 0) title = title[..space];
        return IsTitleId(title) && !title.Equals("launcher", StringComparison.OrdinalIgnoreCase) ? title : null;
    }

    /// <summary>Title ids: 1–32 letters, digits and "_" (e.g. gta5, rdr2, lanoire).</summary>
    public static bool IsTitleId(string s) => s.Length is > 0 and <= 32 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>Builds a game from one Settings → Apps entry; null for anything incomplete or unsafe.</summary>
    public static Game? Read(string title, string? name, string? location, string? displayIcon, int sizeKb)
    {
        if (!IsTitleId(title) || string.IsNullOrWhiteSpace(name)) return null;
        var folder = InstallFolder.Validate(location);
        if (folder is null) return null;

        // DisplayIcon is the game's exe ("…\GTA5.exe,0"): used for its icon only, and only inside its folder.
        string? icon = null;
        if (displayIcon is { Length: > 0 })
        {
            var path = displayIcon.Trim().Trim('"');
            int comma = path.LastIndexOf(',');
            if (comma > 0 && path[(comma + 1)..].Trim().All(c => char.IsAsciiDigit(c) || c == '-')) path = path[..comma].Trim().Trim('"');
            try
            {
                if (Path.IsPathFullyQualified(path))
                {
                    var full = Path.GetFullPath(path);
                    if (InstallFolder.IsSameOrInside(full, folder) && full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                        icon = full;
                }
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        }

        return new Game
        {
            Id = "rockstar:" + title.ToLowerInvariant(),
            Source = GameSource.Rockstar,
            StoreId = title,
            Name = name.Trim(),
            InstallDir = folder,
            SizeOnDisk = Math.Max(0, sizeKb) * 1024L,
            IconPath = icon,
        };
    }

    /// <summary>The installed Rockstar Games Launcher (Launcher.exe), or null.</summary>
    public static string? LauncherExe()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(LauncherKey);
            if (key?.GetValue("InstallFolder") is not string folder || !Path.IsPathFullyQualified(folder)) return null;
            var exe = Path.Combine(folder, "Launcher.exe");
            return File.Exists(exe) ? exe : null;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
