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
static partial class Tests
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
        Check(Manifest("570", "dota 2 beta") is { LaunchUri: "steam://rungameid/570", StoreLastPlayed: null }, "valid manifest accepted");
        var played = Path.Combine(dir, "appmanifest_3.acf");
        File.WriteAllText(played, "\"AppState\" { \"appid\" \"570\" \"name\" \"Game\" \"installdir\" \"x\" \"StateFlags\" \"4\" \"LastPlayed\" \"1753800705\" }");
        Check(read.Invoke(null, [played, Path.Combine(dir, "steamapps")]) is Game { StoreLastPlayed: { } lp } && lp.ToUnixTimeSeconds() == 1753800705,
            "Steam LastPlayed read (0 means never)");
        Check(Manifest("570 -applaunch evil", "x") is null, "appid with extra text rejected");
        Check(Manifest("../../etc", "x") is null, "appid path traversal rejected");
        Check(Manifest("570", "..") is null, "installdir .. rejected");
        Check(Manifest("570", "C:\\\\Windows") is null, "absolute installdir rejected");
        Check(Manifest("570", "..\\\\..\\\\Windows") is null, "installdir with separators rejected");
        var cacheIcon = Path.Combine(dir, "appcache", "librarycache", "999999991", new string('a', 40) + ".jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheIcon)!);
        File.WriteAllBytes(cacheIcon, [0xFF, 0xD8]);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(cacheIcon)!, "header.jpg"), [0xFF, 0xD8]);
        Check(SteamSource.IconFor(dir, "999999991") == cacheIcon && SteamSource.IconFor(dir, "999999992") is null &&
              SteamSource.IconFor(dir, "..\\x") is null, "Steam icon from the library cache, validated appid only");
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
        Check(Format.Greeting(new DateTime(2026, 1, 1, 4, 59, 0)) == "Good evening" &&
              Format.Greeting(new DateTime(2026, 1, 1, 5, 0, 0)) == "Good morning" &&
              Format.Greeting(new DateTime(2026, 1, 1, 12, 0, 0)) == "Good afternoon" &&
              Format.Greeting(new DateTime(2026, 1, 1, 18, 0, 0)) == "Good evening", "greeting by time of day");
        Check(Format.Duration(TimeSpan.FromMinutes(155)) == "2 h 35 min", "duration with hours");
        Check(Format.Playtime(TimeSpan.FromHours(42.5)) == "42 h", "playtime over 10 h");
        Check(Format.Delay(0) == "Instantly" && Format.Delay(10) == "10 s", "hide delay labels");

        SourceTests(dir);
        PlacementTests();
        ShelfTests();
        PayloadTests();
        ChimeTests();
        LaunchTests(dir);
        PlaytimeTests();
        CrashTests();
        SpaceTests();
        GameModeTests();
        SettingsViewModelTests();
        DetailViewModelTests();
        GameModeViewModelTests();
        FreeSpaceViewModelTests();
        SetupViewModelTests();

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

        // Xbox app (milestone 12): .GamingRoot files, package records and a game folder.
        byte[] GamingRoot(uint count, params string[] names) =>
            [.. "RGBX"u8, .. BitConverter.GetBytes(count), .. names.SelectMany(n => Encoding.Unicode.GetBytes(n + "\0"))];
        Check(XboxSource.ParseGamingRoot(GamingRoot(1, "XboxGames")).SequenceEqual(["XboxGames"]), ".GamingRoot parsed");
        Check(XboxSource.ParseGamingRoot(GamingRoot(2, "Games", @"..\Windows")).SequenceEqual(["Games"]), ".GamingRoot can't point outside the drive root");
        Check(XboxSource.ParseGamingRoot(GamingRoot(1, @"C:\Windows")).Count == 0 && XboxSource.ParseGamingRoot("XXXX\u0001"u8.ToArray()).Count == 0,
            ".GamingRoot with a full path or a bad header rejected");
        Check(XboxSource.FamilyOf("Microsoft.624F8B84B80_3.688.109.0_x64__8wekyb3d8bbwe") == ("Microsoft.624F8B84B80", "8wekyb3d8bbwe"), "package family from a full name");
        Check(XboxSource.FamilyOf("Bad_Name_x64__8wekyb3d8bbwe_extra") is null && XboxSource.FamilyOf("Microsoft.App_1.0.0.0_x64__NOTVALID!") is null,
            "malformed package names rejected");
        var xboxGame = Directory.CreateDirectory(Path.Combine(dir, "XboxGames", "Forza Horizon 5")).FullName;
        var xboxContent = Directory.CreateDirectory(Path.Combine(xboxGame, "Content")).FullName;
        File.WriteAllBytes(Path.Combine(xboxContent, "ForzaHorizon5.exe"), [0x4D, 0x5A]);
        const string XboxManifest = """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
              <Identity Name="Microsoft.624F8B84B80" Publisher="CN=Microsoft Corporation" Version="3.688.109.0" ProcessorArchitecture="x64" />
              <Properties><DisplayName>ms-resource:ApplicationDisplayName</DisplayName></Properties>
              <Applications><Application Id="Forzahorizon5" Executable="GameLaunchHelper.exe"><uap:VisualElements DisplayName="x" /></Application></Applications>
            </Package>
            """;
        const string XboxConfig = """
            <Game configVersion="0"><ExecutableList><Executable Name="ForzaHorizon5.exe" Id="Forzahorizon5" /></ExecutableList>
            <ShellVisuals DefaultDisplayName="Forza Horizon 5 &amp; More" PublisherDisplayName="Microsoft Studios" /></Game>
            """;
        var registered = new Dictionary<string, string> { ["Microsoft.624F8B84B80"] = "8wekyb3d8bbwe" };
        var forza = XboxSource.Read(xboxGame, XboxManifest, XboxConfig, registered);
        Check(forza is { Id: "xbox:Microsoft.624F8B84B80", Source: GameSource.Xbox, Name: "Forza Horizon 5 & More",
                         LaunchUri: @"shell:AppsFolder\Microsoft.624F8B84B80_8wekyb3d8bbwe!Forzahorizon5" } &&
              forza.InstallDir == xboxGame && forza.IconPath == Path.Combine(xboxContent, "ForzaHorizon5.exe"),
            "Xbox game: launch link from the registered family, name from MicrosoftGame.config, icon from its exe");
        File.WriteAllBytes(Path.Combine(xboxContent, "Logo.png"), [0x89, 0x50]);
        Check(XboxSource.Read(xboxGame, XboxManifest, XboxConfig.Replace("PublisherDisplayName", "Square150x150Logo=\"Logo.png\" PublisherDisplayName"), registered) is
                  { IconPath: var logo } && logo == Path.Combine(xboxContent, "Logo.png"), "Xbox cover picture: the tile logo before the exe");
        File.Delete(Path.Combine(xboxContent, "Logo.png"));
        var rescanned = new Game { Id = "xbox:a", IconPath = @"C:\g\Content\Logo.png" };
        rescanned.CopyUserData(new Game { Id = "xbox:a", IconPath = @"C:\g\Content\Game.exe", CoverTint = "#102030", Favorite = true });
        Check(rescanned is { IconChanged: true, CoverTint: null, Favorite: true }, "new icon source: cached icon and its color dropped, user data kept");
        var same = new Game { Id = "xbox:a", IconPath = @"C:\g\Content\Logo.png" };
        same.CopyUserData(new Game { Id = "xbox:a", IconPath = @"c:\G\Content\logo.png", CoverTint = "#102030" });
        Check(same is { IconChanged: false, CoverTint: "#102030" }, "same icon source: cached color kept");
        Check(XboxSource.Read(xboxGame, XboxManifest, null, registered) is { Name: "Forza Horizon 5", IconPath: null },
            "Xbox name falls back to the folder when the manifest only has a resource reference");
        Check(XboxSource.Read(xboxGame, XboxManifest, XboxConfig, new Dictionary<string, string>()) is null, "Xbox game not registered for this user skipped");
        Check(XboxSource.Read(xboxGame, XboxManifest.Replace("Forzahorizon5\"", "Forza!evil\""), XboxConfig, registered) is null, "Xbox app id with link characters rejected");
        Check(XboxSource.Read(xboxGame, XboxManifest, XboxConfig.Replace("ForzaHorizon5.exe\" Id", @"..\..\evil.exe"" Id"), registered) is { IconPath: null },
            "Xbox exe outside the Content folder not used");

        // Rockstar Games Launcher (milestone 12): its per-game uninstall entries.
        Check(RockstarSource.TitleOf("\"C:\\Program Files\\Rockstar Games\\Launcher\\uninstall.exe\" -uninstall=gta5") == "gta5", "Rockstar title from its uninstaller");
        Check(RockstarSource.TitleOf("\"C:\\Program Files\\Rockstar Games\\Launcher\\uninstall.exe\" -enableFullMode -uninstall=launcher") is null,
            "the Rockstar launcher's own entry isn't a game");
        Check(RockstarSource.TitleOf("\"C:\\Other\\uninstall.exe\" -uninstall=gta5") is null &&
              RockstarSource.TitleOf("\"C:\\Program Files\\Rockstar Games\\Launcher\\uninstall.exe\" -uninstall=gta5&calc") is null,
            "other uninstallers and odd titles rejected");
        Check(RockstarSource.Read("rdr2", "Red Dead Redemption 2", gameDir, $"\"{exe}\",0", 4096) is
                  { Id: "rockstar:rdr2", Source: GameSource.Rockstar, StoreId: "rdr2", LaunchUri: null, SizeOnDisk: 4194304 } rdr && rdr.IconPath == exe,
            "valid Rockstar entry accepted, icon from its exe");
        Check(RockstarSource.Read("rdr2", "Fake", gameDir, $"\"{outside}\"", 0) is { IconPath: null }, "Rockstar icon outside the install folder not used");
        Check(RockstarSource.Read("rdr2", "Fake", "", null, 0) is null && RockstarSource.Read("rd r2", "Fake", gameDir, null, 0) is null,
            "Rockstar entry without a folder or with a bad title rejected");

        // Hytale Launcher: the game counts once its client is downloaded; the install folder holds client and runtime.
        var hytale = Path.Combine(dir, "Hytale");
        Directory.CreateDirectory(Path.Combine(hytale, "install"));
        Check(HytaleSource.Read(hytale) is null, "Hytale without its client isn't a game yet");
        var hytaleClient = Path.Combine(hytale, "install", "release", "package", "game", "latest", "Client", "HytaleClient.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(hytaleClient)!);
        File.WriteAllBytes(hytaleClient, new byte[1000]);
        Check(HytaleSource.Read(hytale) is { Id: "hytale:hytale", Source: GameSource.Hytale, Name: "Hytale", SizeOnDisk: 1000 } ht &&
              ht.InstallDir == Path.Combine(hytale, "install") && ht.IconPath == hytaleClient,
            "Hytale found with its install folder, size and client icon");
        var hytaleBeta = Path.Combine(hytale, "install", "pre-release", "package", "game", "latest", "Client", "HytaleClient.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(hytaleBeta)!);
        File.WriteAllBytes(hytaleBeta, []);
        File.Delete(hytaleClient);
        Check(HytaleSource.Client(Path.Combine(hytale, "install")) == hytaleBeta, "Hytale on another patchline found");
        Check(HytaleSource.Read("") is null && HytaleSource.Read("C:\\") is null, "Hytale with no or an unsafe folder rejected");

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

        // A shortcut to a store game's own exe (EA's desktop .lnk) is that game; another exe in its folder isn't.
        var bf = new Game { Id = "ea:1", Source = GameSource.Ea, Name = "Battlefield 6", ExePath = @"A:\Games\Battlefield 6\bf6.exe", InstallDir = @"A:\Games\Battlefield 6" };
        var lib = new List<Game>
        {
            bf,
            new() { Id = "shortcut:a", Source = GameSource.Shortcut, Name = "Battlefield 6", IconPath = @"A:\GAMES\Battlefield 6\BF6.exe", Favorite = true },
            new() { Id = "shortcut:b", Source = GameSource.Shortcut, Name = "BF6 benchmark", IconPath = @"A:\Games\Battlefield 6\bench.exe" },
            new() { Id = "shortcut:c", Source = GameSource.Shortcut, Name = "Some link" },
        };
        Check(ShortcutImporter.RemoveDuplicates(lib) && lib.Select(g => g.Id).SequenceEqual(["ea:1", "shortcut:b", "shortcut:c"]) && bf.Favorite,
            "shortcut duplicating a store game's exe taken out, its favorite kept");
        Check(!ShortcutImporter.RemoveDuplicates(lib), "nothing more to take out");

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
    /// <summary>Milestone 10: launch arguments, Steam links, "Start with" checks, engine and anti-cheat detection.</summary>
    static void LaunchTests(string dir)
    {
        // Splitting follows the same rules a game's C runtime uses (checked against ParseArgv).
        foreach (var text in new[] { "-novid -windowed", "\"two words\" -x", "a\\\"b c", "C:\\dir\\ next", "x\\\\\"y z\"", "  -a   -b  " })
        {
            var mine = LaunchOptions.Split(text);
            var expected = ParseArgv(text);
            Check(mine.SequenceEqual(expected), $"split matches argv rules: {text} -> [{string.Join("|", mine)}]");
            Check(LaunchOptions.Split(LaunchOptions.Join(mine)).SequenceEqual(mine), $"join round-trips: {text}");
        }
        Check(LaunchOptions.Split("").Count == 0 && LaunchOptions.Split(null).Count == 0, "empty arguments split to nothing");

        // Steam link: digits-only appid, arguments percent-encoded so they can't change the link.
        Check(LaunchOptions.SteamRunUri("570", ["-novid", "-w", "1280"]) == "steam://run/570//-novid%20-w%201280/", "steam run link");
        Check(LaunchOptions.SteamRunUri("570", ["/x", "a?b#c"])!.StartsWith("steam://run/570//%2Fx%20a%3Fb%23c/"), "slashes and ? # are encoded");
        Check(LaunchOptions.SteamRunUri("570/../1", ["-x"]) is null && LaunchOptions.SteamRunUri(null, []) is null, "bad appid gives no link");
        Check(LaunchOptions.SteamLink("570", []) == "steam://rungameid/570", "steam link without arguments");
        Check(LaunchOptions.SteamLink("570", ["-novid"]) == "steam://run/570//-novid/", "steam link with arguments");
        Check(LaunchOptions.SteamLink("570 -silent", []) is null && LaunchOptions.SteamLink(null, []) is null, "bad appid gives no steam link");

        // Suggestions: ticking and unticking multi-word options.
        Check(LaunchOptions.Suggestions(GameEngine.IdTech)[0].Tokens is ["+com_skipIntroVideo", "1"], "id Tech console variable kept as two arguments");
        Check(LaunchOptions.Suggestions(GameEngine.Glacier) is [{ Args: "-skip_launcher" }], "Glacier skips its launcher");
        var windowed = LaunchOptions.Suggestions(GameEngine.Unity).First(s => s.Args == "-screen-fullscreen 0");
        var tokens = LaunchOptions.Split("-popupwindow -screen-fullscreen 0 -foo");
        Check(LaunchOptions.IndexOf(tokens, windowed.Tokens) == 1, "multi-word suggestion found");
        Check(LaunchOptions.Without(tokens, windowed).SequenceEqual(["-popupwindow", "-foo"]), "multi-word suggestion removed");

        // "Start with" must stay inside the install folder.
        var game = Path.Combine(dir, "ScanGame");
        Directory.CreateDirectory(game);
        Check(LaunchOptions.IsInside(Path.Combine(game, "bin", "x.exe"), game), "exe inside the folder accepted");
        Check(!LaunchOptions.IsInside(Path.Combine(game, "..", "x.exe"), game), "exe outside via .. rejected");
        Check(!LaunchOptions.IsInside(game + "Evil\\x.exe", game), "sibling folder with the same prefix rejected");
        Check(!LaunchOptions.IsInside("x.exe", game), "relative path rejected");
        File.WriteAllText(Path.Combine(game, "game.exe"), "");
        var outside = Path.Combine(dir, "outside.exe");
        File.WriteAllText(outside, "");
        var g = new Game { Id = "manual:t", Source = GameSource.Manual, InstallDir = game, ExePath = Path.Combine(game, "game.exe"), ExeOverride = outside };
        Check(LaunchOptions.ValidOverride(g) is null && LaunchOptions.DirectExe(g) == g.ExePath, "override outside the folder ignored");
        g.ExeOverride = Path.Combine(game, "game.exe");
        Check(LaunchOptions.ValidOverride(g) == g.ExeOverride, "override inside the folder used");
        var epic = new Game { Id = "epic:x", Source = GameSource.Epic, LaunchUri = "com.epicgames.launcher://apps/x", InstallDir = game };
        Check(LaunchOptions.RouteFor(epic) == LaunchOptions.Route.NotSupported, "Epic games take no arguments from us");
        var steam = new Game { Id = "steam:570", Source = GameSource.Steam, StoreId = "570", LaunchUri = "steam://rungameid/570" };
        Check(LaunchOptions.RouteFor(steam) == LaunchOptions.Route.Steam, "Steam games use the run link");

        // Engine and anti-cheat detection from file names.
        GameScanResult ScanOf(string name, params string[] paths)
        {
            var root = Path.Combine(dir, name);
            foreach (var path in paths)
            {
                var full = Path.Combine(root, path);
                if (path.EndsWith('\\')) Directory.CreateDirectory(full);
                else { Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, ""); }
            }
            return GameScan.Scan(root);
        }
        Check(ScanOf("u", "UnityPlayer.dll", "Game_Data\\", "Game.exe").Engine == GameEngine.Unity, "Unity detected");
        Check(ScanOf("u2", "UnityPlayer.dll", "Game.exe").Engine == GameEngine.Unknown, "UnityPlayer.dll alone isn't Unity");
        Check(ScanOf("ue", "Game\\Binaries\\Win64\\Game-Win64-Shipping.exe").Engine == GameEngine.Unreal, "Unreal detected");
        Check(ScanOf("ue2", "Engine\\Binaries\\ThirdParty\\", "Game.exe").Engine == GameEngine.Unreal, "Unreal detected from Engine\\Binaries");
        Check(ScanOf("cry", "Engine\\Shaders.pak", "Bin\\Win64\\Game.exe").Engine == GameEngine.CryEngine, "CryEngine's Engine folder isn't Unreal");
        Check(ScanOf("s2", "game\\dota\\gameinfo.gi").Engine == GameEngine.Source2, "Source 2 detected");
        Check(ScanOf("s1", "hl2\\gameinfo.txt").Engine == GameEngine.Source, "Source detected");
        Check(ScanOf("gd", "Game.exe", "Game.pck").Engine == GameEngine.Godot, "Godot detected");
        Check(ScanOf("gd2", "Other.exe", "Game.pck").Engine == GameEngine.Unknown, "a .pck without its exe isn't Godot");
        Check(ScanOf("fb", "bf6.exe", "Engine.Render.Core2.PlatformPcDx12.retail.dll").Engine == GameEngine.Frostbite, "Frostbite detected");
        Check(ScanOf("re", "re9.exe", "re_chunk_000.pak").Engine == GameEngine.ReEngine, "RE Engine detected");
        Check(ScanOf("cry2", "Engine\\Engine.pak", "Bin\\Win64\\Game.exe").Engine == GameEngine.CryEngine, "CryEngine detected");
        Check(ScanOf("gm", "Game.exe", "data.win").Engine == GameEngine.GameMaker, "GameMaker detected");
        Check(ScanOf("sd", "rogue\\sdf\\pc\\data\\sdf-A-0000.sdfdata").Engine == GameEngine.Snowdrop, "Snowdrop detected three folders down");
        Check(ScanOf("ins", "toc", "Game.exe").Engine == GameEngine.Unknown, "a lone 'toc' file isn't the Insomniac engine");
        Check(ScanOf("mix", "UnityPlayer.dll", "Game_Data\\", "data.win").Engine == GameEngine.Unity, "Unity wins over a stray data.win");
        var eac = ScanOf("eac", "EasyAntiCheat\\", "start_protected_game.exe");
        Check(eac.AntiCheats.SequenceEqual([AntiCheatKind.EasyAntiCheat]), "Easy Anti-Cheat detected once");
        Check(ScanOf("be", "Game_BE.exe").AntiCheats.Contains(AntiCheatKind.BattlEye), "BattlEye detected from *_BE.exe");
        Check(ScanOf("none", "Game.exe").AntiCheats.Count == 0, "no anti-cheat in a plain game");
        Check(ScanOf("deep", "a\\b\\c\\d\\e\\f\\EasyAntiCheat\\").AntiCheats.Count == 0, "scan depth is capped");

        // A junction inside the game folder is never followed.
        var target = Path.Combine(dir, "junction-target");
        Directory.CreateDirectory(Path.Combine(target, "BattlEye"));
        var junctionRoot = Path.Combine(dir, "junction-game");
        Directory.CreateDirectory(junctionRoot);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(junctionRoot, "link"), target);
            Check(GameScan.Scan(junctionRoot).AntiCheats.Count == 0, "links inside the game folder aren't followed");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("SKIP link test (no symlink rights)");
        }

        // Requirements: Vanguard needs TPM and Secure Boot on Windows 11; Battlefield 6 adds Secure Boot.
        var vanguard = AntiCheat.Requirements([AntiCheatKind.Vanguard], "VALORANT", windows11: true);
        Check(vanguard.Contains(PcRequirement.Tpm20) && vanguard.Contains(PcRequirement.SecureBoot), "Vanguard needs TPM 2.0 and Secure Boot on Windows 11");
        Check(!AntiCheat.Requirements([AntiCheatKind.EasyAntiCheat], "Some Game", true).Contains(PcRequirement.SecureBoot), "EAC baseline has no Secure Boot");
        Check(AntiCheat.Requirements([AntiCheatKind.Javelin], "Battlefield 6", true).Contains(PcRequirement.SecureBoot), "Battlefield 6 exception adds Secure Boot");
        Check(AntiCheat.Requirements([], "Battlefield 6", true).Count == 0, "no anti-cheat, no requirements");
    }
    /// <summary>Milestone 11: exit codes, classification, plain-words advice, the copied texts' privacy, event parsing.</summary>
    /// <summary>Milestone 15: Free up space, which games to remove and on which drive.</summary>
    static void SpaceTests()
    {
        const long G = SpacePlanner.GB;
        var now = DateTimeOffset.Now;
        SpaceGame Make(string id, long gb, double hours = 0, bool recent = false, bool fav = false) =>
            new(new Game { Id = id, Name = id, Favorite = fav, SizeOnDisk = gb * G }, gb * G, TimeSpan.FromHours(hours),
                recent ? now.AddDays(-2) : null, recent);
        string Ids(IEnumerable<SpaceGame> games) => string.Join(",", games.Select(g => g.Game.Id));

        Check(SpacePlanner.Band(TimeSpan.Zero) == 0 && SpacePlanner.Band(TimeSpan.FromMinutes(30)) == 1 &&
              SpacePlanner.Band(TimeSpan.FromHours(4)) == 2 && SpacePlanner.Band(TimeSpan.FromHours(19)) == 3 &&
              SpacePlanner.Band(TimeSpan.FromHours(20)) == 4, "playtime bands");

        // The design board's drive: 120 GB needed, 42 GB free, so 78 GB missing.
        var d = new List<SpaceGame>
        {
            Make("g1", 70, 2), Make("g2", 25), Make("g3", 180, 30, recent: true), Make("g4", 60), Make("g5", 150, 120, fav: true),
            Make("g6", 85, 6), Make("g7", 110, 12), Make("g8", 20), Make("g9", 15, 25), Make("g10", 120, 45, fav: true),
        };
        // Never played first: g4 (60) covers nothing alone, so the largest; then 18 GB left, g8 (20) is the closest fit.
        Check(Ids(SpacePlanner.Plan(d, 78 * G)) == "g4,g8", "never-played games first, closest fit");
        Check(Ids(SpacePlanner.Plan(d, 20 * G)) == "g8", "the smallest game that covers it");
        Check(SpacePlanner.Plan(d, 0).Count == 0 && SpacePlanner.Plan(d, -5 * G).Count == 0, "nothing to remove when it fits");
        var keepFavorites = SpacePlanner.Plan(d, 500 * G);
        Check(!keepFavorites.Any(g => g.Favorite) && keepFavorites.Any(g => g.Recent), "recent games before favorites");
        Check(SpacePlanner.Plan(d, 900 * G).Any(g => g.Favorite), "favorites only when nothing else is enough");
        // Two small never-played games go first, then z covers it alone: x and y aren't needed after all.
        Check(Ids(SpacePlanner.Plan([Make("x", 10), Make("y", 10), Make("z", 100, 0.5)], 100 * G)) == "z",
              "picks that aren't needed after all are dropped, biggest first");
        Check(SpacePlanner.Plan([Make("big", 50), new(new Game { Id = "u", Name = "u" }, 0, TimeSpan.Zero, null, false)], 60 * G)
              .All(g => g.SizeKnown), "games without a known size are never picked");

        var full = new DriveSpace(@"D:\", "Games", 931 * G, 42 * G);
        var roomy = new DriveSpace(@"E:\", "Big", 2000 * G, 500 * G);
        var tight = new DriveSpace(@"C:\", "System", 476 * G, 18 * G);
        var plans = new List<DrivePlan>
        {
            SpacePlanner.PlanDrive(tight, [Make("c1", 45, 3), Make("c2", 30)], 120 * G),
            SpacePlanner.PlanDrive(full, d, 120 * G),
        };
        Check(SpacePlanner.Best(plans) == 1, "best drive: one that frees enough, with the least playtime removed");
        plans.Add(SpacePlanner.PlanDrive(roomy, [], 120 * G));
        Check(SpacePlanner.Best(plans) == 2 && plans[2].FitsAlready, "a drive where it fits already wins");

        var games = new List<Game>
        {
            new() { Id = "s", Name = "s", Source = GameSource.Steam, InstallDir = @"d:\Steam\common\s", SizeOnDisk = 5 * G,
                    StoreLastPlayed = now.AddDays(-3) },
            new() { Id = "m", Name = "m", Source = GameSource.Manual, InstallDir = @"D:\Games\m", SizeOnDisk = 5 * G },
            new() { Id = "r", Name = "r", Source = GameSource.Epic, InstallDir = @"D:\Epic\r", SizeOnDisk = 5 * G },
            new() { Id = "h", Name = "h", Source = GameSource.Gog, InstallDir = @"C:\GOG\h", Hidden = true },
        };
        var byDrive = SpacePlanner.ByDrive(games, new Dictionary<string, TimeSpan>(), new Dictionary<string, DateTimeOffset>(), now,
            new HashSet<string> { "r" });
        Check(byDrive.Count == 2 && Ids(byDrive[@"D:\"]) == "s" && Ids(byDrive[@"C:\"]) == "h",
              "by drive: manual games and running games left out, hidden ones kept");
        Check(byDrive[@"D:\"][0].Recent, "a game played through Steam lately counts as recent");

        var tiles = SpacePlanner.Squarify([6, 6, 4, 3, 2, 2, 1], 600, 400);
        double area = tiles.Sum(t => t.W * t.H);
        Check(Math.Abs(area - 240000) < 1 && tiles.All(t => t.X >= -0.01 && t.Y >= -0.01 && t.X + t.W <= 600.01 && t.Y + t.H <= 400.01),
              "treemap fills the area exactly");
        Check(Math.Abs(tiles[0].W * tiles[0].H - 60000) < 1, "treemap tile area follows its size");
        Check(tiles.Max(t => Math.Max(t.W / t.H, t.H / t.W)) < 3.5, "treemap tiles stay roughly square");
    }

    static void CrashTests()
    {
        // Watcher: which exit code tells the most, and which processes are crash reporters.
        Check(GameShelf.Watcher.Program.MostTelling([0, 1, 0xC0000005, 0]) == 2, "a failure code beats 0 and 1");
        Check(GameShelf.Watcher.Program.MostTelling([0, 1]) == 1, "a nonzero code beats 0");
        Check(GameShelf.Watcher.Program.MostTelling([]) == -1, "no processes, no exit code");
        Check(GameShelf.Watcher.Program.IsCrashReporter("CrashReportClient.exe") && GameShelf.Watcher.Program.IsCrashReporter("UnityCrashHandler64.exe")
              && GameShelf.Watcher.Program.IsCrashReporter("CrashSender1403.exe"), "engine crash reporters recognized");
        Check(!GameShelf.Watcher.Program.IsCrashReporter("Game-Win64-Shipping.exe"), "the game itself isn't a crash reporter");

        const string folder = @"C:\Users\Alice\Games\Space Game";
        var start = new DateTimeOffset(2026, 11, 4, 20, 0, 0, TimeSpan.FromHours(1));
        Session S(uint? code, double minutes, bool reporter = false) => new()
        {
            GameId = "steam:1", Start = start, End = start.AddMinutes(minutes), ExitCode = code, Exe = "SpaceGame.exe", CrashReporter = reporter,
        };
        WindowsEvent Crash(string app, string module, string code, string? modulePath = null, double atMinutes = 72) =>
            new(1000, "Application Error", start.AddMinutes(atMinutes), new Dictionary<string, string>
            {
                ["AppName"] = Path.GetFileName(app), ["AppPath"] = app, ["ModuleName"] = module,
                ["ModulePath"] = modulePath ?? @"C:\Windows\System32\" + module, ["ExceptionCode"] = code,
            });
        var gameExe = folder + @"\SpaceGame.exe";

        // 0xDEAD (57005): a deliberate exit on a fatal error, not a normal quit.
        Check(CrashAnalysis.NeedsCheck(S(0xDEAD, 94)) && CrashAnalysis.Analyze(S(0xDEAD, 94), folder, []).Ending == GameEnding.ClosedUnexpectedly,
            "exit code 0xDEAD counts as closed unexpectedly");
        Check(!CrashAnalysis.NeedsCheck(S(1, 94)), "a plain exit code 1 isn't checked");

        var nvidia = CrashAnalysis.Analyze(S(0xC0000005, 72), folder, [Crash(gameExe, "nvwgf2umx.dll", "c0000005")]);
        Check(nvidia.Ending == GameEnding.Crashed && nvidia.Culprit == CrashCulprit.NvidiaDriver && nvidia.ErrorCode == 0xC0000005,
            "a crash in nvwgf2umx.dll is the NVIDIA driver");
        Check(!CrashAnalysis.OffersAiPrompt(nvidia) && CrashAnalysis.Explanation(nvidia).Contains("NVIDIA"), "known culprits get advice, not the AI prompt");
        Check(CrashAnalysis.Title("Space Game", nvidia) == "Space Game crashed after 1 h 12 min", "crash title with the play time");

        var other = CrashAnalysis.Analyze(S(0xC0000005, 72), folder, [Crash(@"C:\Program Files\Other\Other.exe", "nvwgf2umx.dll", "c0000005")]);
        Check(other.Ending == GameEnding.ClosedUnexpectedly && other.Culprit == CrashCulprit.Unknown, "another app's crash event isn't ours");
        var late = CrashAnalysis.Analyze(S(0xC0000005, 72), folder, [Crash(gameExe, "nvwgf2umx.dll", "c0000005", atMinutes: 200)]);
        Check(late.Ending == GameEnding.ClosedUnexpectedly, "an event long after the session isn't ours");

        Check(CrashAnalysis.Analyze(S(1, 30), folder, []).Ending == GameEnding.Normal, "exit code 1 alone is a normal ending");
        Check(CrashAnalysis.Analyze(S(0xC000013A, 30), folder, []).Ending == GameEnding.Normal, "a closed console window isn't a crash");
        Check(CrashAnalysis.Analyze(S(0xC0000409, 30, reporter: true), folder, []).Ending == GameEnding.Crashed, "failure code plus crash reporter is a crash");
        var unexpected = CrashAnalysis.Analyze(S(0xC0000409, 30), folder, []);
        Check(unexpected.Ending == GameEnding.ClosedUnexpectedly && CrashAnalysis.OffersAiPrompt(unexpected), "a failure code alone is softer, with the AI prompt");

        var runtime = CrashAnalysis.Analyze(S(0xC0000135, 0.05), folder, []);
        Check(runtime.Ending == GameEnding.StartFailed && runtime.Culprit == CrashCulprit.MissingRuntime && CrashAnalysis.OffersRedist(runtime),
            "a missing DLL right after starting points to the Visual C++ Redistributable");
        Check(CrashAnalysis.Title("Space Game", runtime) == "Space Game closed right after starting", "start-failure title");

        var reshade = CrashAnalysis.Analyze(S(0xC0000005, 72), folder, [Crash(gameExe, "dxgi.dll", "c0000005", folder + @"\dxgi.dll")]);
        Check(reshade.Culprit == CrashCulprit.Overlay, "a dxgi.dll in the game folder is a mod");
        var systemDxgi = CrashAnalysis.Analyze(S(0xC0000005, 72), folder, [Crash(gameExe, "dxgi.dll", "c0000005")]);
        Check(systemDxgi.Culprit == CrashCulprit.Unknown, "Windows' own dxgi.dll isn't blamed on a mod");
        var steamOverlay = CrashAnalysis.Analyze(S(0xC0000005, 72), folder, [Crash(gameExe, "GameOverlayRenderer64.dll", "c0000005")]);
        Check(steamOverlay.Culprit == CrashCulprit.Overlay, "the Steam overlay is an overlay");
        var reset = CrashAnalysis.Analyze(S(0x887A0006, 72), folder,
            [new WindowsEvent(4101, "Display", start.AddMinutes(71), new Dictionary<string, string>())]);
        Check(reset.Culprit == CrashCulprit.GraphicsDevice && reset.DriverReset && CrashAnalysis.AlsoAtThatTime(reset)!.Contains("display driver"),
            "a display driver reset points to the graphics card");
        var byName = CrashAnalysis.Analyze(S(0xC0000005, 72), null, [Crash(gameExe, "vcruntime140.dll", "c0000005")]);
        Check(byName.Culprit == CrashCulprit.CppRuntime, "without an install folder the exe name matches");

        Check(CrashAnalysis.NeedsCheck(S(0xC0000005, 60)) && CrashAnalysis.NeedsCheck(S(1, 0.02)) && !CrashAnalysis.NeedsCheck(S(0, 60))
              && !CrashAnalysis.NeedsCheck(S(1, 60)) && !CrashAnalysis.NeedsCheck(S(null, 60)), "only sessions that ended with an error are checked");

        Check(CrashAnalysis.Pattern([nvidia, null, reset]) == "2 crashes in the last 3 sessions, both in the graphics driver.", "pattern line names a shared cause");
        Check(CrashAnalysis.Pattern([nvidia, null, steamOverlay]) == "2 crashes in the last 3 sessions.", "pattern line without a shared cause");
        Check(CrashAnalysis.Pattern([nvidia, null, null]) is null, "one crash is no pattern");

        // The copied texts: no user name, no paths, the facts that matter.
        var pc = new PcFacts("Windows 11 Pro 24H2", ["NVIDIA GeForce RTX 5090 with driver 566.36"], 32);
        var prompt = CrashAnalysis.AiPrompt("Space Game", unexpected, pc, 2, 10);
        var report = CrashAnalysis.BugReport("Space Game", "Steam", reshade, pc, 1, 4);
        foreach (var (text, name) in new[] { (prompt, "AI prompt"), (report, "bug report") })
            Check(!text.Contains("Alice") && !text.Contains(@":\") && !text.Contains(@"\\"), $"{name} has no user name or paths");
        Check(prompt.Contains("0xC0000409") && prompt.Contains("SpaceGame.exe") && prompt.Contains("32 GB") && prompt.Contains("2 times in my last 10 sessions")
              && prompt.EndsWith("starting with the easiest fix?"), "AI prompt has the error, file, PC and how often");
        Check(report.StartsWith("Game: Space Game (Steam version)") && report.Contains("in dxgi.dll") && report.Contains("first time in the last 4 sessions")
              && report.EndsWith("No personal data or file paths included.)"), "bug report has the game, store, error file and how often");

        Check(CrashAnalysis.DriverVersion("NVIDIA GeForce RTX 5090", "32.0.15.6636") == "566.36", "NVIDIA driver version as NVIDIA shows it");
        Check(CrashAnalysis.DriverVersion("AMD Radeon(TM) Graphics", "31.0.24033.1003") == "31.0.24033.1003", "other driver versions unchanged");

        // Event XML as EvtRender gives it (single quotes, entities), and bad input.
        const string xml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Application Error' Guid='{a0e9b465}'/>" +
            "<EventID>1000</EventID><TimeCreated SystemTime='2026-11-04T17:08:04.7863801Z'/></System><EventData>" +
            "<Data Name='AppName'>R&amp;D.exe</Data><Data Name='ExceptionCode'>c000027b</Data><Data Name='PackageFullName'></Data><Data Name='Empty'/></EventData></Event>";
        var parsed = CrashAnalysis.ParseEventXml(xml);
        Check(parsed is { Id: 1000, Provider: "Application Error" } && parsed.Data["AppName"] == "R&D.exe" && parsed.Data["ExceptionCode"] == "c000027b"
              && parsed.Data["Empty"] == "" && parsed.Time == new DateTimeOffset(2026, 11, 4, 17, 8, 4, TimeSpan.Zero).AddTicks(7863801),
            "event XML parsed: provider, id, time, named values, entities");
        Check(CrashAnalysis.ParseEventXml("<Event><System>") is null && CrashAnalysis.ParseEventXml("") is null
              && CrashAnalysis.ParseEventXml(new string('x', 70000)) is null, "malformed or oversized event XML rejected");
    }

    /// <summary>Milestone 10: "This week" and the playtime chart, split at midnight, week and month boundaries.</summary>
    static void PlaytimeTests()
    {
        Session S(string id, DateTime start, double hours) =>
            new() { GameId = id, Start = new DateTimeOffset(start), End = new DateTimeOffset(start.AddHours(hours)) };
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Local); // a Wednesday
        Check(PlaytimeStats.WeekStart(now) == new DateTime(2026, 11, 2), "weeks start on Monday");
        Check(PlaytimeStats.WeekStart(new DateTime(2026, 11, 8, 23, 0, 0)) == new DateTime(2026, 11, 2), "Sunday belongs to the week before");

        var sessions = new List<Session>
        {
            S("a", new DateTime(2026, 11, 2, 23, 0, 0, DateTimeKind.Local), 2),   // Mon 23:00 to Tue 01:00
            S("b", new DateTime(2026, 11, 3, 18, 0, 0, DateTimeKind.Local), 0.5),
            S("a", new DateTime(2026, 11, 1, 23, 30, 0, DateTimeKind.Local), 1),  // Sun 23:30 last week, 30 min into Monday
            S("c", new DateTime(2026, 10, 20, 20, 0, 0, DateTimeKind.Local), 3),  // two weeks ago
        };
        var week = PlaytimeStats.Week(sessions, now);
        Check(week.Days[0].Time == TimeSpan.FromMinutes(90), $"Monday gets its share of a session over midnight ({week.Days[0].Time})");
        Check(week.Days[1].Time == TimeSpan.FromMinutes(90), $"Tuesday gets the rest plus its own session ({week.Days[1].Time})");
        Check(week.Total == TimeSpan.FromHours(3) && week.GameCount == 2 && week.TopGameId == "a", "week total, game count and most played");
        Check(PlaytimeStats.Week([], now).Total == TimeSpan.Zero && PlaytimeStats.Week([], now).TopGameId is null, "empty week");

        var weeks = PlaytimeStats.Weeks(sessions, now);
        Check(weeks.Count == 8 && weeks[^1].Start == new DateTime(2026, 11, 2), "8 weeks, the last one is this week");
        Check(weeks[^1].Time == TimeSpan.FromHours(3) && weeks[^2].Time == TimeSpan.FromMinutes(30) && weeks[^3].Time == TimeSpan.FromHours(3),
            "weekly buckets split at the week boundary");
        var months = PlaytimeStats.Months(sessions, now);
        Check(months.Count == 12 && months[^1].Start == new DateTime(2026, 11, 1) && months[0].Start == new DateTime(2025, 12, 1), "12 months ending this month");
        Check(months[^1].Time == TimeSpan.FromHours(3.5) && months[^2].Time == TimeSpan.FromHours(3), "monthly buckets");
        Check(PlaytimeStats.WeekLabel(new DateTime(2026, 9, 14)) == "Sep 14" && PlaytimeStats.DayLetter(new DateTime(2026, 11, 2)) == "M",
            "chart labels are English whatever the locale");
    }
    /// <summary>Milestone 14: Game mode's restore result, journal and the apps it may close.</summary>
    static void GameModeTests()
    {
        var ok = new GameShelf.Shared.RestoreResult(3, 3, []);
        var partial = new GameShelf.Shared.RestoreResult(2, 3, ["Spotify"]);
        Check(ok.Encode() == "3/3" && partial.Encode() == "2/3|Spotify", "restore result encoding");
        Check(GameShelf.Shared.RestoreResult.Parse("2/3|Spotify") is { Restored: 2, Total: 3, Failed: ["Spotify"] }, "restore result round-trip");
        Check(GameShelf.Shared.RestoreResult.Parse("4/3") is null && GameShelf.Shared.RestoreResult.Parse("x/3") is null &&
              GameShelf.Shared.RestoreResult.Parse("") is null && GameShelf.Shared.RestoreResult.Parse("1") is null &&
              GameShelf.Shared.RestoreResult.Parse(new string('1', 3000)) is null, "malformed restore results rejected");
        Check(ok.Summary() == "Restored successfully" && partial.Summary() == "Restored 2 of 3 · Spotify couldn't be put back" &&
              new GameShelf.Shared.RestoreResult(1, 3, ["Power mode", "Spotify"]).Summary() == "Restored 1 of 3 · Power mode and 1 more couldn't be put back",
            "toast texts");
        Check(GameShelf.Watcher.Program.ToastArgument(ok, toast: true) == "3/3" && GameShelf.Watcher.Program.ToastArgument(ok, toast: false) is null &&
              GameShelf.Watcher.Program.ToastArgument(partial, toast: false) == "2/3|Spotify" &&
              GameShelf.Watcher.Program.ToastArgument(new GameShelf.Shared.RestoreResult(0, 0, []), toast: true) is null &&
              GameShelf.Watcher.Program.ToastArgument(null, toast: true) is null,
            "toast only when something changed; problems shown even with the confirmation off");
        Check(ok.ToastText(applied: false) == ("Windows is back to normal", "Game mode put back 3 changes") &&
              partial.ToastText(applied: false) == ("Restored 2 of 3", "Spotify couldn't be put back") &&
              new GameShelf.Shared.RestoreResult(1, 3, ["Power mode", "Spotify"]).ToastText(applied: false).Detail == "Power mode and Spotify couldn't be put back" &&
              new GameShelf.Shared.RestoreResult(1, 4, ["A", "B", "C"]).ToastText(applied: false).Detail == "A and 2 more couldn't be put back",
            "restored toast's two lines");
        Check(ok.ToastText(applied: true) == ("Game mode is on", "3 changes · put back when the game closes") &&
              new GameShelf.Shared.RestoreResult(1, 1, []).ToastText(applied: true).Detail == "1 change · put back when the game closes" &&
              new GameShelf.Shared.RestoreResult(0, 0, []).ToastText(applied: true) == ("Game mode is on", "Windows was already set up for gaming") &&
              partial.ToastText(applied: true) == ("Game mode is on", "Spotify couldn't be changed") &&
              new GameShelf.Shared.RestoreResult(0, 1, ["Services and network"]).ToastText(applied: true).Title == "Game mode couldn't change Windows",
            "applied toast's two lines");
        Check(GameShelf.Watcher.Program.AppliedArgument(new GameShelf.Shared.RestoreResult(0, 0, []), toast: true) == "0/0" &&
              GameShelf.Watcher.Program.AppliedArgument(ok, toast: false) is null &&
              GameShelf.Watcher.Program.AppliedArgument(partial, toast: false) == "2/3|Spotify" &&
              GameShelf.Watcher.Program.AppliedArgument(null, toast: true) is null,
            "applied toast also when nothing needed changing; failures shown even with the confirmation off");
        var tally = new GameShelf.Elevated.Helper.Tally();
        tally.Add(true, "Windows Search");
        tally.Add(null, "SysMain");
        tally.Add(false, "Network priority");
        Check(tally.Result() is { Restored: 1, Total: 2, Failed: ["Network priority"] }, "helper counts what it applied, not what it left alone");

        var app = OperatingSystem.IsWindows() ? @"C:\Program Files\Spotify\Spotify.exe" : "/opt/spotify/Spotify.exe";
        var journal = new GameShelf.Watcher.GameMode.Journal
        {
            GameId = "steam:570",
            Started = new DateTimeOffset(2026, 11, 7, 21, 0, 0, TimeSpan.FromHours(1)),
            Power = new GameShelf.Watcher.GameMode.PowerChange("mode", Guid.Empty, GameShelf.Shared.PowerMode.BestPerformance),
            GameBarOff = true,
        };
        journal.ClosedApps.Add(app);
        var back = GameShelf.Watcher.GameMode.Journal.Parse(journal.ToJson());
        Check(back.GameId == "steam:570" && back.Started == journal.Started && back.Power == journal.Power && back.GameBarOff &&
              back.ClosedApps.SequenceEqual([app]), "journal round-trip");
        var bare = GameShelf.Watcher.GameMode.Journal.Parse(Encoding.UTF8.GetBytes("{\"GameId\":\"a\",\"Started\":\"2026-11-07T21:00:00+01:00\"}"));
        Check(bare.Power is null && !bare.GameBarOff && bare.ClosedApps.Count == 0, "journal with nothing changed");
        try
        {
            GameShelf.Watcher.GameMode.Journal.Parse(Encoding.UTF8.GetBytes(
                "{\"GameId\":\"a\",\"Started\":\"2026-11-07T21:00:00+01:00\",\"Power\":{\"Kind\":\"service\",\"Before\":\"" + Guid.Empty + "\",\"Set\":\"" + Guid.Empty + "\"}}"));
            Check(false, "unknown power change in the journal rejected");
        }
        catch (FormatException) { Check(true, "unknown power change in the journal rejected"); }
        var hostile = GameShelf.Watcher.GameMode.Journal.Parse(Encoding.UTF8.GetBytes(
            "{\"GameId\":\"a\",\"Started\":\"2026-11-07T21:00:00+01:00\",\"ClosedApps\":[\"cmd.exe\",\"C:\\\\x\\\\..\\\\y.exe\",\"C:\\\\Program Files\\\\WindowsApps\\\\a.exe\",\"C:\\\\a.bat\"]}"));
        Check(hostile.ClosedApps.Count == 0, "journal apps that aren't plain full exe paths are never reopened");

        Check(!GameShelf.Watcher.GameMode.IsClosableApp("notepad.exe") && !GameShelf.Watcher.GameMode.IsClosableApp(@"C:\Apps\run.bat") &&
              !GameShelf.Watcher.GameMode.IsClosableApp(Path.Combine(AppContext.BaseDirectory, "GameShelf.exe")),
            "relative paths, non-exes and Game Shelf itself aren't closed");
        if (OperatingSystem.IsWindows())
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Check(!GameShelf.Watcher.GameMode.IsClosableApp(Path.Combine(windows, "explorer.exe")) &&
                  GameShelf.Watcher.GameMode.IsClosableApp(@"C:\Program Files\Spotify\Spotify.exe"), "Windows' own programs are never closed");
        }

        Check(GameShelf.Shared.PowerMode.IsFastest(GameShelf.Shared.PowerMode.BalancedPlan, GameShelf.Shared.PowerMode.BestPerformance) &&
              GameShelf.Shared.PowerMode.IsFastest(GameShelf.Shared.PowerMode.HighPerformancePlan, null) &&
              !GameShelf.Shared.PowerMode.IsFastest(GameShelf.Shared.PowerMode.BalancedPlan, Guid.Empty),
            "already at best performance is left alone");
        var settings = new Settings();
        // Spec §11 table: everything on by default except Print Spooler and Game Bar recording (the admin part
        // added services and network to what used to be power and priority only).
        Check(!settings.GameModeEnabled &&
              settings.GameModeItems.SequenceEqual(["power", "priority", "wsearch", "sysmain", "diagtrack", "smooth", "uploads", "updates"]) &&
              settings.ShowRestoreToast && settings.UploadLimitMbps == 5 &&
              settings.ThrottledUploadApps.SequenceEqual(["OneDrive.exe", "GoogleDriveFS.exe", "Dropbox.exe"]) && settings.KeepSmoothApps.Count == 0,
            "Game mode is opt-in; defaults as in the spec");
        ElevatedTests();
    }

    static void ElevatedTests()
    {
        var ok = new GameShelf.Shared.RestoreResult(2, 2, []);
        Check(ok.Plus(new GameShelf.Shared.RestoreResult(3, 4, ["Print Spooler"])) is { Restored: 5, Total: 6, Failed: ["Print Spooler"] } &&
              ok.Plus(null) == ok, "the watcher's and the helper's results add up");

        var request = new GameShelf.Shared.ElevatedRequest
        {
            Event = new string('a', 32),
            Session = "0123456789abcdef0123456789abcdef",
            UploadLimitMbps = 20,
            LimitUpdates = true,
        };
        request.Services.AddRange(["wsearch", "spooler"]);
        request.Smooth.AddRange(["Discord.exe", "Game (x64).exe"]);
        request.Throttle.AddRange(["OneDrive.exe", "discord.exe"]);
        var back = GameShelf.Shared.ElevatedRequest.Parse(request.Encode());
        Check(back is { UploadLimitMbps: 20, LimitUpdates: true } && back.Event == request.Event && back.Session == request.Session &&
              back.Services.SequenceEqual(["wsearch", "spooler"]) && back.Smooth.SequenceEqual(["Discord.exe", "Game (x64).exe"]) &&
              back.Throttle.SequenceEqual(["OneDrive.exe"]), "request round-trip; an app kept smooth is never slowed down");

        var hostile = GameShelf.Shared.ElevatedRequest.Parse(
            "event=" + new string('b', 32) + "\nsession=" + new string('c', 32) +
            "\nservices=wsearch,wuauserv,WinDefend,../x,spooler\nsmooth=C:\\Windows\\x.exe,a'b.exe,..\\y.exe,x.bat,ok.exe,\"q.exe\nthrottle=a/b.exe,%TEMP%.exe\nlimit=5\n");
        Check(hostile is not null && hostile.Services.SequenceEqual(["wsearch", "spooler"]) && hostile.Smooth.SequenceEqual(["ok.exe"]) &&
              hostile.Throttle.Count == 0, "only allowlisted services and plain exe names are kept");
        var system = GameShelf.Shared.ElevatedRequest.Parse(
            "event=" + new string('b', 32) + "\nsession=" + new string('c', 32) +
            "\nsmooth=game.exe,svchost.exe\nthrottle=SVCHOST.EXE,MsMpEng.exe,OneDrive.exe\nlimit=1\n");
        Check(system is not null && system.Smooth.SequenceEqual(["game.exe"]) && system.Throttle.SequenceEqual(["OneDrive.exe"]),
            "Windows and security processes are never throttled or re-marked");
        Check(GameShelf.Shared.ElevatedRequest.Parse("event=xyz\nsession=" + new string('c', 32)) is null &&
              GameShelf.Shared.ElevatedRequest.Parse("event=" + new string('B', 32) + "\nsession=" + new string('c', 32)) is null &&
              GameShelf.Shared.ElevatedRequest.Parse("event=" + new string('b', 32) + "\nsession=" + new string('c', 32) + "\nlimit=1000") is null &&
              GameShelf.Shared.ElevatedRequest.Parse(new string('x', 9000)) is null, "malformed requests rejected");
        Check(GameShelf.Shared.ElevatedPart.IsUserSid("S-1-5-21-1004336348-1177238915-682003330-1001") &&
              !GameShelf.Shared.ElevatedPart.IsUserSid("S-1-5-18") && !GameShelf.Shared.ElevatedPart.IsUserSid("S-1-5-21-1-") &&
              !GameShelf.Shared.ElevatedPart.IsUserSid("S-1-5-21-1 /f") && !GameShelf.Shared.ElevatedPart.IsUserSid("S-1-5-21-1\"x"),
            "only user SIDs name tasks and files");
        Check(!GameShelf.Shared.ElevatedPart.IsOutdatedMarker(GameShelf.Shared.ElevatedPart.HelperVersion.ToString()) &&
              !GameShelf.Shared.ElevatedPart.IsOutdatedMarker("1\r\n", current: 1) && !GameShelf.Shared.ElevatedPart.IsOutdatedMarker("7", current: 7),
            "a marker with this helper version is current, whatever the app version");
        Check(GameShelf.Shared.ElevatedPart.IsOutdatedMarker("1", current: 2) && GameShelf.Shared.ElevatedPart.IsOutdatedMarker("3", current: 2),
            "another helper version needs an update");
        Check(GameShelf.Shared.ElevatedPart.IsOutdatedMarker("0.1.3", current: 1) && GameShelf.Shared.ElevatedPart.IsOutdatedMarker("0.1.2") &&
              GameShelf.Shared.ElevatedPart.IsOutdatedMarker("") && GameShelf.Shared.ElevatedPart.IsOutdatedMarker("-1", current: -1) &&
              GameShelf.Shared.ElevatedPart.IsOutdatedMarker("+1", current: 1) && GameShelf.Shared.ElevatedPart.IsOutdatedMarker("1x", current: 1),
            "old app-version markers and junk update once");

        var sid = "S-1-5-21-1004336348-1177238915-682003330-1001";
        var journal = new GameShelf.Elevated.Journal { Sid = sid, Session = request.Session, Started = new DateTimeOffset(2026, 11, 7, 22, 0, 0, TimeSpan.FromHours(1)) };
        journal.Services.AddRange(["wsearch", "sysmain"]);
        journal.Policies.Add("GameShelf smooth Discord.exe");
        journal.Updates = new GameShelf.Elevated.UpdateChange(null, 10, KeyCreated: true);
        var j = GameShelf.Elevated.Journal.Parse(journal.ToJson());
        Check(j.Sid == sid && j.Session == request.Session && j.Started == journal.Started && j.Services.SequenceEqual(["wsearch", "sysmain"]) &&
              j.Policies.SequenceEqual(["GameShelf smooth Discord.exe"]) && j.Updates == journal.Updates, "helper journal round-trip");
        var bad = GameShelf.Elevated.Journal.Parse(Encoding.UTF8.GetBytes(
            "{\"Sid\":\"" + sid + "\",\"Session\":\"" + request.Session + "\",\"Started\":\"2026-11-07T22:00:00+01:00\"," +
            "\"Services\":[\"wsearch\",\"WinDefend\",\"wsearch\"],\"Policies\":[\"Someone else's\",\"GameShelf upload OneDrive.exe\"]}"));
        Check(bad.Services.SequenceEqual(["wsearch"]) && bad.Policies.SequenceEqual(["GameShelf upload OneDrive.exe"]) && bad.Updates is null,
            "the helper only ever restarts allowlisted services and removes its own policies");
        try
        {
            GameShelf.Elevated.Journal.Parse(Encoding.UTF8.GetBytes(
                "{\"Sid\":\"S-1-5-18\",\"Session\":\"" + request.Session + "\",\"Started\":\"2026-11-07T22:00:00+01:00\",\"Services\":[],\"Policies\":[]}"));
            Check(false, "a helper journal without a user SID is rejected");
        }
        catch (FormatException) { Check(true, "a helper journal without a user SID is rejected"); }
    }

    static void ChimeTests()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (left, right) = GameShelf.Services.Chime.Render();
        watch.Stop();
        int rate = GameShelf.Services.Chime.SampleRate;
        Check(GameShelf.Services.Chime.Seconds == 2.0, "sound is 2 s (Lullaby)");
        Check(left.Length == (int)(GameShelf.Services.Chime.Seconds * rate) && right.Length == left.Length, "sound has its length");
        float Peak(float[] l, float[] r)
        {
            float p = 0;
            foreach (var v in l) p = Math.Max(p, Math.Abs(v));
            foreach (var v in r) p = Math.Max(p, Math.Abs(v));
            return p;
        }
        float peak = Peak(left, right);
        Check(peak is > 0.02f and < 0.2f, $"sound is quiet but audible, never clipping (peak {peak:0.000})");
        double Rms(float[] x, double from, double to)
        {
            double sum = 0; int a = (int)(from * rate), b = (int)(to * rate);
            for (int k = a; k < b; k++) sum += x[k] * x[k];
            return Math.Sqrt(sum / (b - a));
        }
        // F#4 first, D4 and D3 from 0.25 s: the second note is the loudest part, then it fades.
        double first = Rms(left, 0.05, 0.25), second = Rms(left, 0.3, 0.6), late = Rms(left, 1.3, 1.5);
        Check(first > 0 && second > first * 0.8, $"two notes, the second at least as full ({first:0.0000}, {second:0.0000})");
        Check(late < second * 0.3 && late > second * 0.001, $"the notes fade and the room carries on ({late:0.00000})");
        Check(Rms(left, 1.97, 2.0) < second * 0.01, "sound ends in silence");
        Check(Math.Abs(Rms(left, 0.05, 1.5) - Rms(right, 0.05, 1.5)) < Rms(left, 0.05, 1.5) * 0.5, "sound is balanced left and right");
        var (nightL, nightR) = GameShelf.Services.Chime.Render(night: true);
        double ratio = Rms(nightL, 0.05, 1.5) / Rms(left, 0.05, 1.5);
        Check(ratio is > 0.35 and < 0.6 && Peak(nightL, nightR) < peak, $"the night version is about 6 dB quieter ({20 * Math.Log10(ratio):0.0} dB)");
        Check(GameShelf.Services.Chime.IsNight(new DateTime(2026, 11, 8, 22, 0, 0)) && GameShelf.Services.Chime.IsNight(new DateTime(2026, 11, 8, 6, 59, 0))
              && !GameShelf.Services.Chime.IsNight(new DateTime(2026, 11, 8, 7, 0, 0)) && !GameShelf.Services.Chime.IsNight(new DateTime(2026, 11, 8, 21, 59, 0)),
            "night is 22:00 to 7:00");
        var wav = GameShelf.Services.Chime.ToWav(left, right);
        Check(wav.Length == 44 + left.Length * 4 && Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && Encoding.ASCII.GetString(wav, 36, 4) == "data",
            "sound is a PCM WAV");
        Check(wav.Length < 200_000, $"sound is small ({wav.Length / 1024} KB)");
        Check(watch.ElapsedMilliseconds < 1500, $"sound is made quickly ({watch.ElapsedMilliseconds} ms)");
        if (Environment.GetEnvironmentVariable("GAMESHELF_CHIME_WAV") is { Length: > 0 } path)
        {
            File.WriteAllBytes(path, wav);
            File.WriteAllBytes(Path.ChangeExtension(path, null) + "-night.wav", GameShelf.Services.Chime.ToWav(nightL, nightR));
        }
    }

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
