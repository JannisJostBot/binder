using Binder.Models;

namespace Binder.Library;

/// <summary>What a look through a game's install folder found.</summary>
public sealed record GameScanResult(GameEngine Engine, List<AntiCheatKind> AntiCheats, string Stamp = "", string? EngineVersion = null);

/// <summary>
/// Detects a game's engine and anti-cheat from file and folder names in its install folder. Bounded (depth and
/// entry count), never follows links or junctions (library folders can be on drives others can write to). The only
/// file contents read are for the engine version (<see cref="EngineVersion"/>): Unity's from UnityPlayer.dll's file
/// details, Unreal's from its version string inside the shipping exe (one bounded, read-only pass). Runs off the UI
/// thread when the detail view opens; the result is cached on the game until its files change (<see cref="Stamp"/>).
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
            return $"4:{game.SizeOnDisk}:{Directory.GetLastWriteTimeUtc(dir).Ticks}:{exeTime}";
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
        var engines = new HashSet<GameEngine>(); // the engines with markers of their own (see Detect)
        bool toc = false, dag = false;
        string? unityDll = null, shippingExe = null; // where the engine version is read from
        void Found(AntiCheatKind kind) { if (!found.Contains(kind)) found.Add(kind); }

        if (folder.Contains("Riot Games", StringComparison.OrdinalIgnoreCase)) Found(AntiCheatKind.Vanguard);

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((folder, 0));
        int entries = 0;
        while (queue.Count > 0 && entries < MaxEntries)
        {
            var (dir, depth) = queue.Dequeue();
            var dirName = Path.GetFileName(dir).ToLowerInvariant();
            bool dirUnityPlayer = false, dirUnityData = false;
            string? dirUnityDll = null;
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
                        if (depth == 0 && name == "sds_retail") engines.Add(GameEngine.Illusion);
                        if (depth == 0 && name == "renpy") engines.Add(GameEngine.RenPy);
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

                    if (name == "unityplayer.dll") { dirUnityPlayer = true; dirUnityDll = item.FullName; }
                    else if (name.EndsWith("-win64-shipping.exe")) { unreal = true; shippingExe ??= item.FullName; }
                    else if (name.EndsWith("-wingdk-shipping.exe")) shippingExe ??= item.FullName; // Xbox app builds
                    else if (name == "gameinfo.txt") source = true;
                    else if (name == "gameinfo.gi") source2 = true;
                    else if (name.EndsWith(".pck")) packs.Add(Path.GetFileNameWithoutExtension(name));
                    if (name.EndsWith(".exe")) exes.Add(Path.GetFileNameWithoutExtension(name));
                    if (depth == 0 && name == "toc") toc = true;
                    if (depth == 0 && name == "dag") dag = true;
                    if (Detect(name, dirName, depth, dir) is { } engineFound) engines.Add(engineFound);

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
            if (dirUnityPlayer && dirUnityData) { unityPlayer = true; unityData = true; unityDll ??= dirUnityDll; }
            if (packs.Any(exes.Contains)) godot = true; // a .pck next to the exe of the same name
        }

        var engine = unityPlayer && unityData ? GameEngine.Unity
            : unreal ? GameEngine.Unreal
            : source2 ? GameEngine.Source2
            : source ? GameEngine.Source
            : godot ? GameEngine.Godot
            : GameEngine.Unknown;
        if (toc && dag) engines.Add(GameEngine.Insomniac);
        if (engine == GameEngine.Unknown)
            engine = Enum.GetValues<GameEngine>().FirstOrDefault(engines.Contains); // declaration order breaks ties
        string? version = engine switch
        {
            GameEngine.Unity when unityDll is not null => UnityVersion(FileVersionOf(unityDll)),
            GameEngine.Unreal when shippingExe is not null => UnrealVersion(shippingExe),
            _ => null,
        };
        return new GameScanResult(engine, found, EngineVersion: version);
    }

    // ---- Engine versions --------------------------------------------------------------------------------------

    /// <summary>The most an Unreal exe is read for its version string (real shipping exes are 70–250 MB).</summary>
    public const long MaxExeBytes = 512L * 1024 * 1024;

    private static string? FileVersionOf(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Unity's version from UnityPlayer.dll's file version: "2019.4.40.16762411" is "2019.4"; Unity 6 numbers itself
    /// 6000.x, so "6000.0.52…" is "6" and "6000.1…" is "6.1". Null for anything else.
    /// </summary>
    public static string? UnityVersion(string? fileVersion)
    {
        if (fileVersion is null) return null;
        var parts = fileVersion.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int minor)
            || minor > 99) return null;
        if (major >= 6000 && major < 7000) // Unity 6 and later: 6000.x is 6.x
        {
            int name = major / 1000;
            return minor == 0 ? $"{name}" : $"{name}.{minor}";
        }
        return major is >= 3 and <= 2100 ? $"{major}.{minor}" : null;
    }

    /// <summary>
    /// Unreal's version from the shipping exe: its build string "++UE5+Release-5.4" (ASCII or UTF-16) gives "5.4";
    /// without one, "UE5" / "UE4" in the exe's file version gives "5" / "4". Null when neither is there (many
    /// games build from a branch of their own).
    /// </summary>
    public static string? UnrealVersion(string exe)
    {
        try
        {
            var info = new FileInfo(exe);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > MaxExeBytes) return null;
            using (var stream = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
                if (FindUnrealVersion(stream, MaxExeBytes) is { } found) return found;
            var fileVersion = FileVersionOf(exe) ?? "";
            return fileVersion.StartsWith("UE5", StringComparison.Ordinal) ? "5"
                : fileVersion.StartsWith("UE4", StringComparison.Ordinal) ? "4"
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly byte[] UnrealAscii = "++UE"u8.ToArray();
    private static readonly byte[] UnrealUtf16 = [(byte)'+', 0, (byte)'+', 0, (byte)'U', 0, (byte)'E', 0];

    /// <summary>
    /// Looks for "++UE4+Release-4.27" / "++UE5+Release-5.4" in at most <paramref name="maxBytes"/> of the stream,
    /// in ASCII or UTF-16, and returns "4.27" / "5.4". Reads in 1 MB blocks with an overlap, so a string split
    /// across two blocks is still found.
    /// </summary>
    public static string? FindUnrealVersion(Stream stream, long maxBytes)
    {
        const int Block = 1 << 20, Overlap = 128;
        var buffer = new byte[Block + Overlap];
        int kept = 0;
        long total = 0;
        while (total < maxBytes)
        {
            int read = stream.Read(buffer, kept, (int)Math.Min(Block, maxBytes - total));
            if (read <= 0) break;
            total += read;
            var span = buffer.AsSpan(0, kept + read);
            if ((Match(span, UnrealAscii, 1) ?? Match(span, UnrealUtf16, 2)) is { } found) return found;
            kept = Math.Min(Overlap, span.Length);
            span[^kept..].CopyTo(buffer);
        }
        return null;
    }

    /// <summary>Each "++UE" in <paramref name="span"/> (characters <paramref name="width"/> bytes wide): "N+Release-N.N".</summary>
    private static string? Match(ReadOnlySpan<byte> span, ReadOnlySpan<byte> marker, int width)
    {
        int at = 0;
        while (true)
        {
            int hit = span[at..].IndexOf(marker);
            if (hit < 0) return null;
            int start = at + hit + marker.Length;
            if (ReadVersion(span, start, width) is { } version) return version;
            at = at + hit + 1;
        }
    }

    /// <summary>The <paramref name="index"/>th character after <paramref name="start"/>, or NUL past the end or when not plain ASCII.</summary>
    private static char CharAt(ReadOnlySpan<byte> span, int start, int width, int index)
    {
        int pos = start + index * width;
        if (pos + width > span.Length) return '\0';
        if (width == 2 && span[pos + 1] != 0) return '\0';
        return (char)span[pos];
    }

    /// <summary>After "++UE": the digit 4 or 5, "+Release-", then that major, a dot and up to two digits.</summary>
    private static string? ReadVersion(ReadOnlySpan<byte> span, int start, int width)
    {
        char major = CharAt(span, start, width, 0);
        if (major is not ('4' or '5')) return null;
        const string Middle = "+Release-";
        for (int i = 0; i < Middle.Length; i++)
            if (CharAt(span, start, width, 1 + i) != Middle[i]) return null;
        int p = 1 + Middle.Length;
        if (CharAt(span, start, width, p) != major || CharAt(span, start, width, p + 1) != '.') return null;
        char d1 = CharAt(span, start, width, p + 2), d2 = CharAt(span, start, width, p + 3);
        if (!char.IsAsciiDigit(d1)) return null;
        return char.IsAsciiDigit(d2) ? $"{major}.{d1}{d2}" : $"{major}.{d1}";
    }

    /// <summary>
    /// Engines recognized from one file, by names their games ship (checked on real installs): Frostbite's
    /// engine DLLs, RE Engine's chunk packs, Anvil's .forge archives, Snowdrop's .sdfdata, Dunia's .fat files
    /// under data_final, CryEngine's Engine\*.pak, id Tech's base\*.resources, Glacier's Runtime\*.rpkg,
    /// GameMaker's data.win, LÖVE's love.dll and RPG Maker's core script.
    /// </summary>
    private static GameEngine? Detect(string name, string dirName, int depth, string dir)
    {
        if (depth == 0)
        {
            if (name.StartsWith("engine.buildinfo") || name.StartsWith("engine.render.")) return GameEngine.Frostbite;
            if (name == "re_chunk_000.pak") return GameEngine.ReEngine;
            if (name.EndsWith(".forge")) return GameEngine.Anvil;
            if (name == "data.win") return GameEngine.GameMaker;
            if (name == "love.dll") return GameEngine.Love;
        }
        if (depth == 1)
        {
            if (dirName == "engine" && name is "engine.pak" or "shaders.pak") return GameEngine.CryEngine;
            if (dirName == "base" && name.EndsWith(".resources")) return GameEngine.IdTech;
            if (dirName == "runtime" && name.EndsWith(".rpkg")) return GameEngine.Glacier;
        }
        if (name.EndsWith(".sdfdata") || name.EndsWith(".sdftoc")) return GameEngine.Snowdrop;
        if (name.EndsWith(".fat") && dir.Contains(@"\data_final\", StringComparison.OrdinalIgnoreCase)) return GameEngine.Dunia;
        if (name is "rpg_core.js" or "rmmz_core.js") return GameEngine.RpgMaker;
        return null;
    }

    /// <summary>The cached result when it's still current, else a fresh scan. Off the UI thread.</summary>
    public static GameScanResult? Get(Game game)
    {
        var stamp = Stamp(game);
        if (stamp is null) return null;
        if (stamp == game.ScanStamp && game.Engine is { } engine)
            return new GameScanResult(engine, game.AntiCheats ?? [], stamp, game.EngineVersion);
        return Scan(game.InstallDir!) with { Stamp = stamp };
    }

    /// <summary>Stores a fresh result on the game (UI thread). True when something changed and the library needs saving.</summary>
    public static bool Store(Game game, GameScanResult result)
    {
        if (result.Stamp.Length == 0 || result.Stamp == game.ScanStamp) return false;
        game.ScanStamp = result.Stamp;
        game.Engine = result.Engine;
        game.EngineVersion = result.EngineVersion;
        game.AntiCheats = result.AntiCheats.Count > 0 ? result.AntiCheats : null;
        return true;
    }
}