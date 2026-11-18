using Binder.Library;
using Binder.Models;
using Binder.Services;

namespace Binder.Tests;

static partial class Tests
{
    /// <summary>
    /// Edge cases along whole use cases: games on a drive that isn't there, launcher shortcuts, Steam updates, sleep
    /// around a game's end, the kept settings during a scan, files held by another program, copied shortcuts.
    /// </summary>
    static void EdgeCaseTests(string root)
    {
        var dir = Path.Combine(root, "edge-cases");
        Directory.CreateDirectory(dir);

        // The countdown's end asks the game's watcher whether the game has closed: no watcher, running, closed.
        var endedId = "test:" + Guid.NewGuid().ToString("N");
        bool? noWatcher = Binder.Shared.WatcherNames.GameEnded(endedId), running, closed;
        using (var ended = new EventWaitHandle(false, EventResetMode.ManualReset, Binder.Shared.WatcherNames.Ended(endedId)))
        {
            running = Binder.Shared.WatcherNames.GameEnded(endedId);
            ended.Set();
            closed = Binder.Shared.WatcherNames.GameEnded(endedId);
        }
        Check(noWatcher is null && running == false && closed == true &&
              Binder.Shared.WatcherNames.Mutex(endedId) != Binder.Shared.WatcherNames.Ended(endedId),
            "watcher: the game-ended signal reads as no watcher, running, then closed");

        // Sleep: the last part carries how the game ended, so it's kept however short; a short middle part isn't.
        var t0 = new DateTimeOffset(2026, 11, 12, 20, 0, 0, TimeSpan.FromHours(1));
        var quick = Binder.Watcher.SystemEvents.Awake(t0, t0.AddMilliseconds(500), []);
        Check(quick.Count == 1 && quick[0] == (t0, t0.AddMilliseconds(500)), "sessions: a game that ends within a second is still saved");
        var afterWake = Binder.Watcher.SystemEvents.Awake(t0, t0.AddHours(2).AddMilliseconds(300), [(t0.AddHours(1), t0.AddHours(2))]);
        Check(afterWake.Count == 2 && afterWake[1] == (t0.AddHours(2), t0.AddHours(2).AddMilliseconds(300)),
            "sessions: a game that ends right after the PC wakes keeps its last part");
        var shortStart = Binder.Watcher.SystemEvents.Awake(t0, t0.AddHours(2), [(t0.AddMilliseconds(300), t0.AddHours(1))]);
        Check(shortStart.Count == 1 && shortStart[0] == (t0.AddHours(1), t0.AddHours(2)), "sessions: a part under a second before a sleep is left out");

        // A part after a sleep didn't start with the game: never "closed right after starting".
        var resumed = new Session { GameId = "steam:1", Start = t0, End = t0.AddSeconds(2), ExitCode = 1, Resumed = true };
        Check(!CrashAnalysis.NeedsCheck(resumed), "crash check: a short part after a sleep with an ordinary exit code isn't checked");
        var crashedAfterWake = CrashAnalysis.Analyze(
            new Session { GameId = "steam:1", Start = t0, End = t0.AddSeconds(2), ExitCode = 0xC0000005, Resumed = true }, null, []);
        Check(crashedAfterWake.Ending == GameEnding.ClosedUnexpectedly && CrashAnalysis.IsRestartable(crashedAfterWake),
            "crash check: a crash right after the PC woke is a crash of a running game, and may be restarted");
        var quickFail = CrashAnalysis.Analyze(new Session { GameId = "steam:1", Start = t0, End = t0.AddSeconds(2), ExitCode = 0xC0000005 }, null, []);
        Check(quickFail.Ending == GameEnding.StartFailed, "crash check: a game that fails within seconds of starting still says so");

        // A rescan never drops a store game whose drive isn't there.
        var offline = new Game { Id = "steam:20", Source = GameSource.Steam, Name = "Offline", Favorite = true };
        var uninstalled = new Game { Id = "steam:21", Source = GameSource.Steam, Name = "Uninstalled" };
        var merged = LibraryScanner.Merge([offline, uninstalled], [], [GameSource.Steam], [], DateTimeOffset.Now, _ => false, g => g.Id == "steam:20");
        Check(merged.Games.Contains(offline) && merged.Dropped.SequenceEqual([uninstalled]),
            "scan: a game on an unplugged drive stays, an uninstalled one leaves");
        Check(!GameFiles.DriveMissing(Path.GetFullPath(dir)) && !GameFiles.DriveMissing(null) && !GameFiles.DriveMissing("relative\\path"),
            "a reachable drive (or no path) isn't offline");

        // Kept settings changed while a scan ran aren't lost when the scan's list is used.
        var a = new Game { Id = "manual:a", Favorite = true };
        var b = new Game { Id = "manual:b", Favorite = true };
        var c = new Game { Id = "manual:c", Favorite = true };
        var d = new Game { Id = "manual:d", Favorite = true };
        List<Game> before = [a, b];
        List<Game> scan = [a, c];     // the scan gave b back (found again) and kept c (dropped)
        List<Game> current = [b, d];  // meanwhile: a was added back, d left the shelf on Play
        LibraryScanner.ReconcileRemoved(scan, before, current);
        Check(scan.Count == 2 && scan.Contains(c) && scan.Contains(d), "scan: kept settings added or taken back meanwhile are kept that way");
        List<Game> unchanged = [a];
        LibraryScanner.ReconcileRemoved(unchanged, before, before);
        Check(unchanged.SequenceEqual([a]), "scan: nothing changed meanwhile, the scan's list stands");

        // Launcher shortcuts: the launcher keeps running after the game, so its folder is never watched.
        var riot = Path.Combine(dir, "Riot Client", "RiotClientServices.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(riot)!);
        File.WriteAllBytes(riot, [0]);
        var league = ShortcutImporter.CreateGame(new ShortcutFile(Path.Combine(dir, "League.lnk"), "League", riot, null), Path.Combine(dir, "League.lnk"), GameSource.Shortcut);
        Check(ShortcutImporter.IsLauncher(riot) && league.InstallDir is null && league.ProcessName is null,
            "a shortcut through a launcher watches nothing of the launcher's (Process to watch says what)");
        var own = Path.Combine(dir, "Game", "game.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllBytes(own, [0]);
        var game = ShortcutImporter.CreateGame(new ShortcutFile(Path.Combine(dir, "Game.lnk"), "Game", own, null), Path.Combine(dir, "Game.lnk"), GameSource.Shortcut);
        Check(!ShortcutImporter.IsLauncher(own) && game.InstallDir is not null, "a shortcut to the game's own exe watches its folder");
        if (OperatingSystem.IsWindows())
        {
            Check(ShortcutImporter.IsLauncher(@"C:\Program Files\Rockstar Games\Launcher\Launcher.exe") &&
                  !ShortcutImporter.IsLauncher(@"D:\Games\Some Game\Launcher.exe"), "Rockstar's Launcher.exe by its folder, not every Launcher.exe");
            Check(ShortcutImporter.IsWindowsProgram(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")),
                "explorer.exe is a Windows program");
        }

        // Steam clears "fully installed" while it updates a game: an installed game stays, a first install doesn't.
        var depots = new VdfObject { ["InstalledDepots"] = new VdfObject { ["571"] = new VdfObject { ["manifest"] = "1" } } };
        // 1030 (installed + update required + update started) was read from a real update on 2026-11-18.
        Check(SteamSource.IsInstalled(4, new VdfObject()) && SteamSource.IsInstalled(1026, depots) &&
              SteamSource.IsInstalled(1030, depots) &&
              !SteamSource.IsInstalled(1026, new VdfObject()) && !SteamSource.IsInstalled(2052, depots),
            "Steam: updating counts as installed, a first install and an uninstall don't");

        // A data file held by another program: the copy from before is read, and it isn't saved over this run.
        var held = Path.Combine(dir, "held-settings.json");
        var info = StorageJsonContext.Default.Settings;
        Storage.Save(held, new Settings { HideDelaySeconds = 7 }, info);
        Storage.Save(held, new Settings { HideDelaySeconds = 9 }, info);
        using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check(Storage.Load(held, info)?.HideDelaySeconds == 7, "save: a held file falls back to the copy before");
        }
        bool refused;
        try
        {
            Storage.Save(held, new Settings { HideDelaySeconds = 1 }, info);
            refused = false;
        }
        catch (IOException)
        {
            refused = true;
        }
        Check(refused && Storage.Load(held, info)?.HideDelaySeconds == 9, "save: a file that couldn't be read isn't saved over with older data");

        // A copied shortcut whose original the user deleted since doesn't come back.
        var desktop = Directory.CreateDirectory(Path.Combine(dir, "Desktop")).FullName;
        var stored = Directory.CreateDirectory(Path.Combine(dir, "Stored")).FullName;
        ShortcutImporter.FolderForTests = stored;
        ShortcutImporter.DesktopsForTests = [desktop];
        try
        {
            File.WriteAllText(Path.Combine(stored, "Copy.lnk"), "c");
            var failed = ShortcutImporter.Restore([
                new ImportedShortcut { StoredPath = Path.Combine(stored, "Copy.lnk"), OriginalPath = Path.Combine(desktop, "Copy.lnk"), Copied = true },
            ]);
            Check(failed.Count == 0 && !File.Exists(Path.Combine(desktop, "Copy.lnk")) && !File.Exists(Path.Combine(stored, "Copy.lnk")),
                "restore: our copy of a shortcut the user deleted since goes, and doesn't reappear");
        }
        finally
        {
            ShortcutImporter.FolderForTests = null;
            ShortcutImporter.DesktopsForTests = null;
        }
    }
}
