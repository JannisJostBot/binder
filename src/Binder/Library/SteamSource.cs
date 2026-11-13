using Binder.Models;
using Microsoft.Win32;

namespace Binder.Library;

/// <summary>Finds installed Steam games from libraryfolders.vdf and appmanifest_*.acf files.</summary>
public static class SteamSource
{
    // Tools that ship as Steam "apps" but aren't games.
    private static readonly HashSet<string> ExcludedAppIds =
    [
        "228980",  // Steamworks Common Redistributables
        "250820",  // SteamVR
        "1070560", // Steam Linux Runtime
        "1391110", // Steam Linux Runtime - Soldier
        "1628350", // Steam Linux Runtime - Sniper
    ];

    private static readonly string[] ExcludedNameParts =
    [
        "Proton", "Steam Linux Runtime", "Steamworks", "Redistributable", " SDK", "Dedicated Server",
    ];

    /// <summary>Steam's folder: the one set in Settings › Advanced, else the one Steam records (SourceDefaults).</summary>
    public static string? FindSteamPath() => SourceDefaults.SteamFolder();

    /// <summary>Steam's own steam.exe in the Steam folder, or null when it isn't there.</summary>
    public static string? SteamExe()
    {
        if (FindSteamPath() is not { } dir) return null;
        var exe = Path.Combine(dir, "steam.exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>
    /// True when Steam is running: Steam keeps its process id in ActiveProcess\pid (0 when closed). The id is
    /// checked against a live steam process, since a crashed Steam leaves a stale one behind. One registry read
    /// at launch time, no polling.
    /// </summary>
    public static bool IsRunning()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
        if (key?.GetValue("pid") is not int pid || pid <= 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public static List<Game> Scan()
    {
        var games = new List<Game>();
        var steamPath = FindSteamPath();
        if (steamPath is null) return games;

        var seen = new HashSet<string>();
        foreach (var library in GetLibraryFolders(steamPath))
        {
            var steamapps = Path.Combine(library, "steamapps");
            try
            {
                if (!Directory.Exists(steamapps)) continue; // e.g. an unplugged drive
                foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    var game = ReadManifest(manifest, steamapps);
                    // Steam keeps the manifest of a game whose folder was deleted by hand: that game is gone.
                    if (game is null || GameFiles.Check(game.InstallDir) == PathState.Gone) continue;
                    if (seen.Add(game.StoreId!)) games.Add(game);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        foreach (var game in games) game.IconPath = IconFor(steamPath, game.StoreId!);
        return games;
    }

    private static readonly string[] UninstallKeys =
    [
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <summary>
    /// The game's icon, for its generated cover and the Free up space map (Steam games have store art, not an exe
    /// we know): the .ico Steam names in the game's Settings › Apps entry ("Steam App {appid}", up to 256 px), else
    /// the small icon in Steam's library cache. Only files inside Steam's own folders count.
    /// </summary>
    public static string? IconFor(string steamPath, string appId)
    {
        if (!IsAppId(appId)) return null;
        var gamesDir = Path.Combine(steamPath, "steam", "games");
        foreach (var path in UninstallKeys)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"{path}\Steam App {appId}");
                if (key?.GetValue("DisplayIcon") is string icon && icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) &&
                    Path.IsPathFullyQualified(icon) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(icon)), gamesDir, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(icon))
                    return Path.GetFullPath(icon);
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { }
        }
        try
        {
            // librarycache\{appid}\{sha1}.jpg (current Steam), or librarycache\{appid}_icon.jpg (older).
            var cache = SourceDefaults.Override(SourceEntry.SteamArtFolder) ?? Path.Combine(steamPath, SourceDefaults.DefaultArtSubfolder);
            var folder = Path.Combine(cache, appId);
            if (Directory.Exists(folder))
                foreach (var file in Directory.EnumerateFiles(folder, "*.jpg"))
                    if (Path.GetFileNameWithoutExtension(file) is { Length: 40 } name && name.All(char.IsAsciiHexDigit)) return file;
            var old = Path.Combine(cache, appId + "_icon.jpg");
            return File.Exists(old) ? old : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsAppId(string value) =>
        value.Length is > 0 and <= 10 && value.All(char.IsAsciiDigit);

    /// <summary>A single folder name: no separators, drive letters or "..", so it stays under steamapps\common.</summary>
    private static bool IsPlainFolderName(string value) =>
        value is not ("." or "..") &&
        value.Trim().Length > 0 &&
        value.IndexOfAny(['\\', '/', ':']) < 0 &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static List<string> GetLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };
        try
        {
            var vdf = Vdf.ParseFile(Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"));
            var root = vdf.Obj("libraryfolders") ?? vdf.Obj("LibraryFolders");
            if (root is null) return folders;
            foreach (var (key, value) in root)
            {
                if (!int.TryParse(key, out _)) continue;
                // New format: "0" { "path" "..." }. Old format: "1" "D:\\SteamLibrary".
                var path = value is VdfObject o ? o.Str("path") : value as string;
                if (!string.IsNullOrEmpty(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    folders.Add(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { }
        return folders;
    }

    private static Game? ReadManifest(string manifestPath, string steamapps)
    {
        VdfObject? app;
        try { app = Vdf.ParseFile(manifestPath).Obj("AppState"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { return null; }
        if (app is null) return null;

        var appId = app.Str("appid");
        var name = app.Str("name");
        var installDir = app.Str("installdir");
        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir)) return null;
        // The appid goes into a steam:// URI, a URL and cache paths; the installdir decides which processes the
        // watcher waits on. A crafted manifest must not inject URI text or point the game at another folder.
        if (!IsAppId(appId) || !IsPlainFolderName(installDir)) return null;
        if (ExcludedAppIds.Contains(appId)) return null;
        foreach (var part in ExcludedNameParts)
            if (name.Contains(part, StringComparison.OrdinalIgnoreCase)) return null;

        // StateFlags bit 4 = fully installed (also set while an update is pending).
        if (int.TryParse(app.Str("StateFlags"), out var flags) && (flags & 4) == 0) return null;

        long.TryParse(app.Str("SizeOnDisk"), out var size);
        // Unix seconds; 0 when never played. Free up space counts games played through Steam directly too.
        DateTimeOffset? lastPlayed = long.TryParse(app.Str("LastPlayed"), out var seconds) && seconds is > 0 and < 32503680000
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        return new Game
        {
            Id = "steam:" + appId,
            Source = GameSource.Steam,
            StoreId = appId,
            Name = name,
            InstallDir = Path.Combine(steamapps, "common", installDir),
            LaunchUri = "steam://rungameid/" + appId,
            SizeOnDisk = size,
            StoreLastPlayed = lastPlayed,
        };
    }
}
