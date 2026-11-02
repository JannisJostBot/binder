using System.Reflection;
using System.Text;
using System.Text.Json;
using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.Views;

namespace GameShelf.Tests;

/// <summary>
/// Security and logic tests for the non-UI code. Private helpers are reached by reflection so the app code
/// doesn't need test-only visibility changes. Exit code = number of failures.
/// </summary>
static class Tests
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

    static int Main()
    {
        // VDF depth limit: a deeply nested file throws FormatException instead of overflowing the stack.
        var deep = "\"a\"" + string.Concat(Enumerable.Repeat(" { \"k\"", 100000)) + string.Concat(Enumerable.Repeat("}", 100000));
        try { Vdf.Parse(deep); Check(false, "deep VDF rejected"); } catch (FormatException) { Check(true, "deep VDF rejected"); }
        var normal = Vdf.Parse("\"AppState\" { \"appid\" \"570\" \"name\" \"Dota 2\" \"x\" { \"y\" { \"z\" \"1\" } } }");
        Check(normal.Obj("AppState")?.Str("appid") == "570", "normal VDF still parses");

        // Manifest validation (private ReadManifest via reflection).
        var read = typeof(SteamSource).GetMethod("ReadManifest", BindingFlags.NonPublic | BindingFlags.Static)!;
        var dir = Directory.CreateTempSubdirectory().FullName;
        Game? Manifest(string appid, string installdir)
        {
            var p = Path.Combine(dir, "appmanifest_1.acf");
            File.WriteAllText(p, $"\"AppState\" {{ \"appid\" \"{appid}\" \"name\" \"Game\" \"installdir\" \"{installdir}\" \"StateFlags\" \"4\" }}");
            return (Game?)read.Invoke(null, [p, Path.Combine(dir, "steamapps")]);
        }
        Check(Manifest("570", "dota 2 beta") is { LaunchUri: "steam://rungameid/570" }, "valid manifest accepted");
        Check(Manifest("570 -applaunch evil", "x") is null, "appid with extra text rejected");
        Check(Manifest("../../etc", "x") is null, "appid path traversal rejected");
        Check(Manifest("570", "..") is null, "installdir .. rejected");
        Check(Manifest("570", "C:\\\\Windows") is null, "absolute installdir rejected");
        Check(Manifest("570", "..\\\\..\\\\Windows") is null, "installdir with separators rejected");
        var big = Path.Combine(dir, "appmanifest_2.acf");
        File.WriteAllText(big, new string(' ', (int)Vdf.MaxFileBytes + 1));
        Check(read.Invoke(null, [big, Path.Combine(dir, "steamapps")]) is null, "oversized manifest rejected");

        // Watcher argument quoting round-trips through the CommandLineToArgvW rules.
        var append = typeof(GameShelf.Watcher.Program).Assembly.GetType("GameShelf.Watcher.Program")!
            .GetMethod("AppendArgument", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var arg in new[] { "steam:570", "a\"b", "C:\\dir\\", "x\\\\\"y", "", "two words", "end\\\\" })
        {
            var sb = new StringBuilder();
            append.Invoke(null, [sb, arg]);
            var parsed = ParseArgv(sb.ToString() + " next");
            Check(parsed.Count == 2 && parsed[0] == arg && parsed[1] == "next", $"quote round-trip {arg} -> {sb}");
        }
        // Formatting used by the detail view.
        Check(Format.Duration(TimeSpan.FromSeconds(30)) == "< 1 min", "duration under a minute");
        Check(Format.Duration(TimeSpan.FromMinutes(155)) == "2 h 35 min", "duration with hours");
        Check(Format.Playtime(TimeSpan.FromHours(42.5)) == "42 h", "playtime over 10 h");
        Check(Format.Delay(0) == "Instantly" && Format.Delay(10) == "10 s", "hide delay labels");

        SourceTests(dir);
        PlacementTests();
        ShelfTests();
        PayloadTests();

        Directory.Delete(dir, recursive: true);
        Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
        return fails;
    }

    /// <summary>Milestone 8: generated cover colors and initials, sort orders.</summary>
    static void ShelfTests()
    {
        Check(CoverColors.Initials("The Lantern Tide") == "LT", "initials skip a leading The");
        Check(CoverColors.Initials("Northwatch") == "N", "one-word title gives one initial");
        Check(CoverColors.Initials("half-life: alyx") == "HL", "initials are upper case, split on punctuation");
        Check(CoverColors.MonogramTone("Quiet Harbor") == CoverColors.MonogramTone("quiet harbor") &&
              CoverColors.MonogramTones.Contains(CoverColors.MonogramTone("Quiet Harbor")), "monogram tone is stable");

        byte[] Solid(byte r, byte g, byte b, byte a, int size = 32)
        {
            var px = new byte[size * size * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a; }
            return px;
        }
        (double H, double S, double L) Hsl(string hex)
        {
            double r = Convert.ToInt32(hex[1..3], 16) / 255.0, g = Convert.ToInt32(hex[3..5], 16) / 255.0, b = Convert.ToInt32(hex[5..7], 16) / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
            double s = d == 0 ? 0 : l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = d == 0 ? 0 : max == r ? ((g - b) / d + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return (h * 60, s, l);
        }
        var red = CoverColors.IconTint(Solid(230, 40, 40, 255), 32, 32);
        var (h, s, l) = red is null ? (0.0, 0.0, 0.0) : Hsl(red);
        Check(red is not null && (h < 15 || h > 345) && s <= 0.46 && l >= 0.21 && l <= 0.31,
            $"tint keeps the icon's hue, muted and dark ({red}: {h:0}°, {s:P0}, {l:P0})");
        // White text on the darkest allowed tint and on the lightest must pass 4.5:1.
        double Lum(string hex)
        {
            double C(int v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
            return 0.2126 * C(Convert.ToInt32(hex[1..3], 16)) + 0.7152 * C(Convert.ToInt32(hex[3..5], 16)) + 0.0722 * C(Convert.ToInt32(hex[5..7], 16));
        }
        var yellow = CoverColors.IconTint(Solid(250, 230, 40, 255), 32, 32)!;
        Check(1.05 / (Lum(yellow) + 0.05) >= 4.5, $"white text on a tint from a bright icon passes 4.5:1 ({yellow})");
        Check(CoverColors.IconTint(Solid(0, 0, 0, 0), 32, 32) is null, "fully transparent icon has no tint");
        var gray = CoverColors.IconTint(Solid(128, 128, 128, 255), 32, 32);
        Check(gray is not null && Hsl(gray).S < 0.05, $"gray icon gives a neutral tint ({gray})");
        Check(CoverColors.MonogramTones.All(t => 1.05 / (Lum(t) + 0.05) >= 4.5), "white text passes 4.5:1 on every monogram tone");

        // Sort orders.
        var now = DateTimeOffset.Now;
        var a = new Game { Id = "a", Name = "Äpfel", SizeOnDisk = 5, AddedAt = now.AddDays(-3) };
        var b = new Game { Id = "b", Name = "Banana", SizeOnDisk = 50, AddedAt = now.AddDays(-1), Favorite = true };
        var z = new Game { Id = "z", Name = "Zebra", SizeOnDisk = 10 };
        var games = new[] { z, b, a };
        var (last, playtime) = ShelfOrder.Stats([
            new Session { GameId = "a", Start = now.AddHours(-5), End = now.AddHours(-1) },
            new Session { GameId = "z", Start = now.AddDays(-2).AddHours(-1), End = now.AddDays(-2) },
        ]);
        string Order(SortBy sort, bool favFirst) => string.Concat(ShelfOrder.Sort(games, sort, favFirst, last, playtime).Select(g => g.Id));
        Check(Order(SortBy.RecentlyPlayed, false) == "azb", "recently played: most recent first, never played last");
        Check(Order(SortBy.MostPlayed, false) == "azb", "most played");
        Check(Order(SortBy.Name, false) == "abz", "name sort places Ä with A (culture-aware)");
        Check(Order(SortBy.RecentlyAdded, false) == "baz", "recently added, unknown dates last");
        Check(Order(SortBy.Size, false) == "bza", "size on disk, biggest first");
        Check(Order(SortBy.Name, true) == "baz", "favorites first, then the chosen order");
    }

    /// <summary>Milestone 7: where the panel goes, in physical pixels.</summary>
    static void PlacementTests()
    {
        var none = new PixelEdges(0, 0, 0, 0);
        var work = new PixelRect(0, 0, 1920, 1032); // 1080p with a 48 px taskbar that reserves space
        Check(Placement.Compute(work, none, 1.0, DockPosition.Right, null) == new PixelRect(1332, 28, 1892, 1004),
            "docked right at 100 %");
        Check(Placement.Compute(work, none, 1.0, DockPosition.Left, null) == new PixelRect(28, 28, 588, 1004),
            "docked left at 100 %");
        Check(Placement.Compute(work, none, 1.5, DockPosition.Right, null) == new PixelRect(1038, 42, 1878, 990),
            "width and margins scale at 150 %");

        // Auto-hide taskbar at the bottom: the work area is the whole screen, but the bar's 48 px stay free.
        var full = new PixelRect(0, 0, 1920, 1080);
        var rect = Placement.Compute(full, new PixelEdges(0, 0, 0, 48), 1.0, DockPosition.Right, null);
        Check(rect.Bottom == 1080 - 48 - 28, $"auto-hide taskbar at the bottom left free (bottom {rect.Bottom})");
        rect = Placement.Compute(full, new PixelEdges(0, 0, 48, 0), 1.0, DockPosition.Right, null);
        Check(rect.Right == 1920 - 48 - 28, $"auto-hide taskbar on the right left free (right {rect.Right})");

        // A second monitor left of the primary one has negative coordinates.
        var left = new PixelRect(-1920, 0, 0, 1080);
        Check(Placement.Compute(left, none, 1.0, DockPosition.Right, null) == new PixelRect(-588, 28, -28, 1052),
            "docked right on a monitor at negative coordinates");

        // Free position saved off-screen (e.g. that monitor was unplugged) is pulled back onto the screen.
        rect = Placement.Compute(work, none, 1.0, DockPosition.Free, (5000, -300));
        Check(rect.Left == 1920 - 560 && rect.Top == 0, $"free position kept on screen ({rect.Left}, {rect.Top})");
        rect = Placement.Compute(work, none, 1.0, DockPosition.Free, (300, 20));
        Check(rect.Left == 300 && rect.Top == 20, "free position inside the screen kept");

        // Small screen: never taller than the usable area.
        rect = Placement.Compute(new PixelRect(0, 0, 1024, 380), none, 1.0, DockPosition.Right, null);
        Check(rect.Height == 380 && rect.Top == 0, $"small screen: full height ({rect.Height})");
    }

    /// <summary>Milestone 6: folder safety, Epic and GOG validation, shortcut parsing, restoring shortcuts.</summary>
    static void SourceTests(string dir)
    {
        // Install folders the watcher may treat as "the game's folder".
        var gameDir = Directory.CreateDirectory(Path.Combine(dir, "Games", "My Game")).FullName;
        Check(InstallFolder.Validate(gameDir) == gameDir, "game folder accepted");
        Check(InstallFolder.Validate(Path.GetPathRoot(gameDir)) is null, "drive root rejected");
        Check(InstallFolder.Validate("Games\\My Game") is null && InstallFolder.Validate("") is null, "relative or empty path rejected");
        Check(InstallFolder.Validate(Path.Combine(dir, "missing")) is null, "missing folder rejected");
        Check(InstallFolder.Validate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) is null, "user profile itself rejected");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0)
        {
            Check(InstallFolder.Validate(windows) is null, "Windows folder rejected");
            Check(InstallFolder.Validate(Environment.SystemDirectory) is null, "System32 rejected");
            Check(InstallFolder.Validate(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) is null, "Program Files itself rejected");
        }

        // Epic manifests.
        string Epic(string app, string ns, string location, string extra = "") =>
            $"{{ \"DisplayName\": \"Fake Epic\", \"InstallLocation\": \"{JsonEncodedText.Encode(location)}\", " +
            $"\"AppName\": \"{app}\", \"CatalogNamespace\": \"{ns}\", \"CatalogItemId\": \"abc123\", \"InstallSize\": 1024{extra} }}";
        Game? ReadEpic(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return EpicSource.Read(doc.RootElement);
        }
        var epic = ReadEpic(Epic("FakeApp", "fakens", gameDir));
        Check(epic is { Id: "epic:FakeApp", LaunchUri: "com.epicgames.launcher://apps/fakens%3Aabc123%3AFakeApp?action=launch&silent=true" },
            "valid Epic manifest accepted");
        Check(ReadEpic(Epic("Fake&evil=1", "fakens", gameDir)) is null, "Epic AppName with URI characters rejected");
        Check(ReadEpic(Epic("FakeApp", "ns/../x", gameDir)) is null, "Epic namespace with separators rejected");
        Check(ReadEpic(Epic("FakeApp", "fakens", Path.GetPathRoot(gameDir)!)) is null, "Epic install at a drive root rejected");
        Check(ReadEpic(Epic("FakeApp", "fakens", gameDir, ", \"AppCategories\": [\"engines\"]")) is null, "Epic engine (not a game) skipped");
        Check(ReadEpic(Epic("FakeApp", "fakens", gameDir, ", \"AppCategories\": [\"public\", \"games\"]")) is not null, "Epic game category accepted");
        Check(ReadEpic(Epic("FakeApp", "fakens", gameDir, ", \"bIsIncompleteInstall\": true")) is null, "incomplete Epic install skipped");
        Check(ReadEpic(Epic("EelTexturePack", "fakens", gameDir, ", \"MainGameAppName\": \"Eel\"")) is null, "Epic add-on (HD texture pack) skipped");
        Check(ReadEpic(Epic("FakeApp", "fakens", gameDir, ", \"MainGameAppName\": \"FakeApp\"")) is not null, "Epic game naming itself as main game accepted");
        var epicFile = Path.Combine(dir, "big.item");
        File.WriteAllText(epicFile, new string(' ', (int)EpicSource.MaxManifestBytes + 1));
        Check(EpicSource.ReadFile(epicFile) is null, "oversized Epic manifest rejected");
        File.WriteAllText(epicFile, Epic("FakeApp", "fakens", gameDir), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Check(EpicSource.ReadFile(epicFile) is not null, "Epic manifest with a UTF-8 BOM read");
        File.WriteAllText(epicFile, string.Concat(Enumerable.Repeat("[", 1000)));
        Check(EpicSource.ReadFile(epicFile) is null, "deeply nested Epic manifest rejected");

        // GOG registry entries.
        var exe = Path.Combine(gameDir, "game.exe");
        File.WriteAllText(exe, "");
        var outside = Path.Combine(dir, "outside.exe");
        File.WriteAllText(outside, "");
        Check(GogSource.Read("1207658924", null, "Fake GOG", gameDir, exe) is { Id: "gog:1207658924" } g && g.ExePath == exe,
            "valid GOG entry accepted");
        Check(GogSource.Read("1207658924", null, "Fake GOG", gameDir, "game.exe") is { ExePath: var rel } && rel == exe, "relative GOG exe resolved");
        Check(GogSource.Read("1207658924", null, "Fake GOG", gameDir, outside) is null, "GOG exe outside the install folder rejected");
        Check(GogSource.Read("1207658924", null, "Fake GOG", gameDir, Path.Combine("..", "..", "outside.exe")) is null, "GOG exe path traversal rejected");
        Check(GogSource.Read("12x", null, "Fake GOG", gameDir, exe) is null, "GOG id that isn't digits rejected");
        // EA app entries (Settings → Apps) and their installerdata.xml.
        Check(EaSource.IsEaGame("\"C:\\Program Files\\Common Files\\EAInstaller\\Fake\\Cleanup.exe\" uninstall_game -autologging"), "EA uninstaller recognized");
        Check(!EaSource.IsEaGame("MsiExec.exe /X{C2622085-ABD2-49E5-8AB9-D3D6A642C091}"), "EA app's own uninstaller isn't a game");
        Check(EaSource.Read("{GUID}", "Fake™ 6", gameDir, $"\"{exe}\"", 1024, "16426154") is
            { Id: "ea:16426154", Name: "Fake 6", LaunchUri: "origin2://game/launch?offerIds=16426154&autoDownload=1", SizeOnDisk: 1048576 } ea &&
            ea.ExePath == exe, "valid EA entry accepted, ™ removed");
        Check(EaSource.Read("{GUID}", "Fake", gameDir, exe + ",0", 0, null) is { Id: "ea:GUID", LaunchUri: null } noId && noId.ExePath == exe,
            "EA entry without a content id starts its exe");
        Check(EaSource.Read("{GUID}", "Fake", gameDir, $"\"{outside}\"", 0, null) is null, "EA exe outside the install folder rejected");
        Check(EaSource.Read("{GUID}", "Fake", gameDir, null, 0, "1&evil=1") is null, "EA content id with link characters rejected");
        var installer = Directory.CreateDirectory(Path.Combine(gameDir, "__Installer")).FullName;
        File.WriteAllText(Path.Combine(installer, "installerdata.xml"),
            "<?xml version='1.0'?><DiPManifest><contentIDs>\n  <contentID>16274575</contentID>\n</contentIDs></DiPManifest>");
        Check(EaSource.ReadContentId(gameDir) == "16274575", "EA content id read from installerdata.xml");
        Directory.Delete(installer, recursive: true);

        // Ubisoft Connect installs.
        Check(UbisoftSource.Read("4553", gameDir.Replace('\\', '/') + "/", "Fake Ubi", 2048, null) is
            { Id: "ubisoft:4553", LaunchUri: "uplay://launch/4553/0", SizeOnDisk: 2097152 }, "valid Ubisoft install accepted (forward slashes)");
        Check(UbisoftSource.Read("4553", gameDir, null, 0, null) is { Name: "My Game" }, "Ubisoft name falls back to the folder");
        Check(UbisoftSource.Read("1081", "", "Fake Ubi", 0, null) is null, "Ubisoft install without a folder skipped");
        Check(UbisoftSource.Read("45/53", gameDir, "Fake Ubi", 0, null) is null, "Ubisoft id that isn't digits rejected");
        Check(UbisoftSource.Read("4553", gameDir, "Fake Ubi", 0, exe) is { IconPath: null }, "Ubisoft icon must be an .ico");

        // Shortcut links.
        ShortcutFile Url(string url) => new(Path.Combine(dir, "x.url"), "x", null, url);
        Check(Url("steam://rungameid/570").SteamAppId == "570", "Steam link parsed");
        Check(Url("uplay://launch/4553/0").UbisoftId == "4553", "Ubisoft link parsed");
        Check(Url("uplay://launch/45x/0").UbisoftId is null, "Ubisoft link with a bad id rejected");
        Check(Url("steam://rungameid/570 -evil").SteamAppId is null, "Steam link with extra text rejected");
        Check(Url("com.epicgames.launcher://apps/ns%3Aitem%3AFakeApp?action=launch&silent=true").EpicAppName == "FakeApp", "Epic link parsed");
        Check(Url("com.epicgames.launcher://apps/Fortnite?action=launch").EpicAppName == "Fortnite", "short Epic link parsed");
        Check(new ShortcutFile("a.lnk", "a", "C:\\Docs\\notes.txt", null).CanBeGame == false, "document shortcut isn't offered");
        var urlFile = Path.Combine(dir, "Dota 2.url");
        File.WriteAllText(urlFile, "[{000214A0-0000-0000-C000-000000000046}]\r\nProp3=19,0\r\n[InternetShortcut]\r\nIDList=\r\nURL=steam://rungameid/570\r\nIconIndex=0\r\n");
        Check(ShortcutFile.ReadUrlFile(urlFile) == "steam://rungameid/570", ".url file read");

        // Restoring a shortcut puts it back, next to a same-named file if needed.
        var desktop = Directory.CreateDirectory(Path.Combine(dir, "Desktop")).FullName;
        var stored = Directory.CreateDirectory(Path.Combine(dir, "Stored")).FullName;
        File.WriteAllText(Path.Combine(stored, "A.lnk"), "a");
        File.WriteAllText(Path.Combine(stored, "B.lnk"), "b");
        File.WriteAllText(Path.Combine(desktop, "B.lnk"), "someone else's");
        var failed = ShortcutImporter.Restore([
            new ImportedShortcut { StoredPath = Path.Combine(stored, "A.lnk"), OriginalPath = Path.Combine(desktop, "A.lnk") },
            new ImportedShortcut { StoredPath = Path.Combine(stored, "B.lnk"), OriginalPath = Path.Combine(desktop, "B.lnk") },
        ]);
        Check(failed.Count == 0 && File.ReadAllText(Path.Combine(desktop, "A.lnk")) == "a", "shortcut restored to the desktop");
        Check(File.ReadAllText(Path.Combine(desktop, "B.lnk")) == "someone else's" && File.ReadAllText(Path.Combine(desktop, "B (2).lnk")) == "b",
            "restore doesn't overwrite an existing file");
    }

    // Reference parser for the documented CommandLineToArgvW / MSVC argv rules (arguments after argv[0]).
    static List<string> ParseArgv(string s)
    {
        var args = new List<string>(); var cur = new StringBuilder(); bool inQ = false, any = false; int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\')
            {
                int n = 0; while (i < s.Length && s[i] == '\\') { n++; i++; }
                if (i < s.Length && s[i] == '"') { cur.Append('\\', n / 2); if (n % 2 == 1) { cur.Append('"'); i++; } }
                else cur.Append('\\', n);
                any = true; continue;
            }
            if (c == '"') { inQ = !inQ; any = true; i++; continue; }
            if ((c == ' ' || c == '\t') && !inQ) { if (any) { args.Add(cur.ToString()); cur.Clear(); any = false; } i++; continue; }
            cur.Append(c); any = true; i++;
        }
        if (any) args.Add(cur.ToString());
        return args;
    }

    /// <summary>The installer's file list: only plain relative paths inside the install folder are accepted.</summary>
    static void PayloadTests()
    {
        foreach (var bad in new[] { @"..\evil.exe", @"C:\Windows\x.exe", @"\x.exe", "a/b.exe", @"a\..\b.exe", "x.exe:stream",
                     "con.", "x ", "", "a\\\\b.exe", @"a\b\c\d\e\f\g\h\i.exe" })
            Check(!GameShelf.Setup.PayloadList.IsSafe(bad), $"unsafe install path rejected: '{bad}'");
        foreach (var good in new[] { "GameShelf.exe", @"Microsoft.UI.Xaml\Assets\NoiseAsset_256x256_PNG.png", @"Assets\tray-dark.ico" })
            Check(GameShelf.Setup.PayloadList.IsSafe(good), $"install path accepted: '{good}'");

        var hash = new string('a', 64);
        string List(params string[] lines) => string.Join("\n", lines);
        var ok = GameShelf.Setup.PayloadList.Parse(new StringReader(List($"{hash} 10 GameShelf.exe", $"{hash} 5 GameShelf.Watcher.exe",
            $"{hash} 7 Assets\\app.ico")), "GameShelf.exe", "GameShelf.Watcher.exe");
        Check(ok is { Count: 3 } && ok[2].Path == @"Assets\app.ico" && ok[0].Length == 10, "payload list parsed");
        Check(GameShelf.Setup.PayloadList.Parse(new StringReader(List($"{hash} 10 GameShelf.exe")), "GameShelf.exe", "GameShelf.Watcher.exe") is null,
            "payload without the watcher rejected");
        Check(GameShelf.Setup.PayloadList.Parse(new StringReader(List($"{hash} 10 GameShelf.exe", $"{hash} 1 ..\\x.exe")), "GameShelf.exe") is null,
            "payload with a path outside the folder rejected");
        Check(GameShelf.Setup.PayloadList.Parse(new StringReader(List($"{hash} 10 GameShelf.exe", $"{hash} 1 gameshelf.EXE")), "GameShelf.exe") is null,
            "payload with a repeated path rejected");
        Check(GameShelf.Setup.PayloadList.Parse(new StringReader(List($"{new string('g', 64)} 10 GameShelf.exe")), "GameShelf.exe") is null,
            "payload with a malformed hash rejected");
    }
}
