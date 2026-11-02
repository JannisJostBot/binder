using GameShelf.Models;
using Microsoft.Win32;

namespace GameShelf.Library;

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

    public static string? FindSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamPath") is not string path || path.Length == 0) return null;
        path = Path.GetFullPath(path.Replace('/', '\\'));
        return Directory.Exists(path) ? path : null;
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
                    if (game is not null && seen.Add(game.StoreId!)) games.Add(game);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return games;
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
        return new Game
        {
            Id = "steam:" + appId,
            Source = GameSource.Steam,
            StoreId = appId,
            Name = name,
            InstallDir = Path.Combine(steamapps, "common", installDir),
            LaunchUri = "steam://rungameid/" + appId,
            SizeOnDisk = size,
        };
    }
}
