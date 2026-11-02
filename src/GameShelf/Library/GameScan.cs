using GameShelf.Models;

namespace GameShelf.Library;

/// <summary>What a look through a game's install folder found.</summary>
public sealed record GameScanResult(GameEngine Engine, List<AntiCheatKind> AntiCheats, string Stamp = "");

/// <summary>
/// Detects a game's engine and anti-cheat from file and folder names in its install folder. Bounded (depth and
/// entry count), never follows links or junctions (library folders can be on drives others can write to), reads
/// no file contents. Runs off the UI thread when the detail view opens; the result is cached on the game until
/// its files change (<see cref="Stamp"/>).
/// </summary>
public static class GameScan
{
    public const int MaxDepth = 4;
    public const int MaxEntries = 20_000;

    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Changes when the game updates: its size on disk, the folder's and the exe's write times. Null when there is
    /// no folder to look at.
    /// </summary>
    public static string? Stamp(Game game)
    {
        var dir = game.InstallDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        try
        {
            long exeTime = LaunchOptions.DirectExe(game) is { } exe && File.Exists(exe) ? File.GetLastWriteTimeUtc(exe).Ticks : 0;
            return $"2:{game.SizeOnDisk}:{Directory.GetLastWriteTimeUtc(dir).Ticks}:{exeTime}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static GameScanResult Scan(string folder)
    {
        bool unityPlayer = false, unityData = false, unreal = false, source = false, source2 = false, godot = false;
        var found = new List<AntiCheatKind>();
        void Found(AntiCheatKind kind) { if (!found.Contains(kind)) found.Add(kind); }

        if (folder.Contains("Riot Games", StringComparison.OrdinalIgnoreCase)) Found(AntiCheatKind.Vanguard);

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((folder, 0));
        int entries = 0;
        while (queue.Count > 0 && entries < MaxEntries)
        {
            var (dir, depth) = queue.Dequeue();
            bool dirUnityPlayer = false, dirUnityData = false;
            var exes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var packs = new List<string>();
            IEnumerable<FileSystemInfo> items;
            try
            {
                items = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Options);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }
            try
            {
                foreach (var item in items)
                {
                    if (++entries > MaxEntries) break;
                    var name = item.Name.ToLowerInvariant();
                    if (item is DirectoryInfo)
                    {
                        if (name.EndsWith("_data")) dirUnityData = true;
                        // Unreal ships Engine\Binaries; CryEngine games also have an Engine folder, without it.
                        if (depth == 1 && name == "binaries" && string.Equals(Path.GetFileName(dir), "Engine", StringComparison.OrdinalIgnoreCase)) unreal = true;
                        switch (name)
                        {
                            case "easyanticheat" or "easyanticheat_eos": Found(AntiCheatKind.EasyAntiCheat); break;
                            case "battleye": Found(AntiCheatKind.BattlEye); break;
                            case "eaanticheat": Found(AntiCheatKind.Javelin); break;
                            case "denuvo anti-cheat": Found(AntiCheatKind.DenuvoAntiCheat); break;
                            case "gameguard": Found(AntiCheatKind.GameGuard); break;
                            case "xigncode": Found(AntiCheatKind.Xigncode); break;
                            case "anticheatexpert": Found(AntiCheatKind.Ace); break;
                        }
                        if (depth < MaxDepth) queue.Enqueue((item.FullName, depth + 1));
                        continue;
                    }

                    if (name == "unityplayer.dll") dirUnityPlayer = true;
                    else if (name.EndsWith("-win64-shipping.exe")) unreal = true;
                    else if (name == "gameinfo.txt") source = true;
                    else if (name == "gameinfo.gi") source2 = true;
                    else if (name.EndsWith(".pck")) packs.Add(Path.GetFileNameWithoutExtension(name));
                    if (name.EndsWith(".exe")) exes.Add(Path.GetFileNameWithoutExtension(name));

                    if (name is "easyanticheat_eos_setup.exe" or "easyanticheat_setup.exe" or "start_protected_game.exe" or "easyanticheat_x64.dll")
                        Found(AntiCheatKind.EasyAntiCheat);
                    else if (name is "beservice_x64.exe" or "beservice.exe" or "beclient_x64.dll" || name.EndsWith("_be.exe"))
                        Found(AntiCheatKind.BattlEye);
                    else if (name.StartsWith("eaanticheat.gameservicelauncher") || name.StartsWith("eaanticheat.installer"))
                        Found(AntiCheatKind.Javelin);
                    else if (name.StartsWith("denuvo-anti-cheat"))
                        Found(AntiCheatKind.DenuvoAntiCheat);
                    else if (name is "gamemon.des" or "gamemon64.des" or "gameguard.des")
                        Found(AntiCheatKind.GameGuard);
                    else if (name is "x3.xem" or "xxd-0.xem")
                        Found(AntiCheatKind.Xigncode);
                    else if (name is "hoyokprotect.sys" or "mhypbase.dll")
                        Found(AntiCheatKind.HoYoProtect);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a folder vanished or became unreadable mid-scan; keep what we have
            }
            if (dirUnityPlayer && dirUnityData) { unityPlayer = true; unityData = true; }
            if (packs.Any(exes.Contains)) godot = true; // a .pck next to the exe of the same name
        }

        var engine = unityPlayer && unityData ? GameEngine.Unity
            : unreal ? GameEngine.Unreal
            : source2 ? GameEngine.Source2
            : source ? GameEngine.Source
            : godot ? GameEngine.Godot
            : GameEngine.Unknown;
        return new GameScanResult(engine, found);
    }

    /// <summary>The cached result when it's still current, else a fresh scan. Off the UI thread.</summary>
    public static GameScanResult? Get(Game game)
    {
        var stamp = Stamp(game);
        if (stamp is null) return null;
        if (stamp == game.ScanStamp && game.Engine is { } engine)
            return new GameScanResult(engine, game.AntiCheats ?? [], stamp);
        return Scan(game.InstallDir!) with { Stamp = stamp };
    }

    /// <summary>Stores a fresh result on the game (UI thread). True when something changed and the library needs saving.</summary>
    public static bool Store(Game game, GameScanResult result)
    {
        if (result.Stamp.Length == 0 || result.Stamp == game.ScanStamp) return false;
        game.ScanStamp = result.Stamp;
        game.Engine = result.Engine;
        game.AntiCheats = result.AntiCheats.Count > 0 ? result.AntiCheats : null;
        return true;
    }
}