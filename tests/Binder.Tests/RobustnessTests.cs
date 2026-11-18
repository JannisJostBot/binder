using Binder.Library;
using Binder.Models;
using Binder.Services;

namespace Binder.Tests;

static partial class Tests
{
    /// <summary>
    /// Races and crashes: a library scan meeting changes made while it ran, saves that survive a crash, shortcut
    /// imports that can't lose a shortcut, Game mode's restore edge cases.
    /// </summary>
    static void RobustnessTests(string root)
    {
        var dir = Path.Combine(root, "robustness");
        Directory.CreateDirectory(dir);
        ReconcileTests();
        DurableSaveTests(dir);
        UnrecordedShortcutTests(dir);
        RepeatGuardTests();
        SleepSplitTests();
        EdgeCaseTests(root);
    }

    /// <summary>A scan's result against the library as it is when the scan ends.</summary>
    static void ReconcileTests()
    {
        var steam = new Game { Id = "steam:1", Name = "Alpha", Source = GameSource.Steam };
        var manual = new Game { Id = "manual:m", Name = "Manual", Source = GameSource.Manual };
        var gone = new Game { Id = "manual:g", Name = "Gone", Source = GameSource.Manual };
        var before = new HashSet<string> { steam.Id, manual.Id, gone.Id };

        // The scan's result: a new instance for the store game, the manual games as they were.
        var scannedSteam = new Game { Id = steam.Id, Name = "Alpha", Source = GameSource.Steam };
        var scanned = new List<Game> { scannedSteam, gone, manual };

        // Meanwhile on the UI thread: Alpha became a favorite, "Gone" was removed, a game was added.
        steam.Favorite = true;
        var added = new Game { Id = "manual:new", Name = "Beta", Source = GameSource.Manual };
        var current = new List<Game> { steam, manual, added };

        var result = LibraryScanner.Reconcile(scanned, before, current);
        Check(result.Any(g => g.Id == added.Id), "scan: a game added while it ran stays on the shelf");
        Check(result.All(g => g.Id != gone.Id), "scan: a game removed while it ran stays removed");
        Check(result.Single(g => g.Id == steam.Id).Favorite, "scan: a setting changed while it ran is kept");
        Check(result.Select(g => g.Id).Distinct().Count() == result.Count, "scan: no game twice");
        Check(result.Select(g => g.Name).SequenceEqual(["Alpha", "Beta", "Manual"]), "scan: still sorted by name");

        // A store game the scan found for the first time is kept, even though it isn't in the library yet.
        var fresh = new Game { Id = "steam:2", Name = "Fresh", Source = GameSource.Steam };
        var withFresh = LibraryScanner.Reconcile([fresh], new HashSet<string>(), []);
        Check(withFresh.Count == 1 && withFresh[0] == fresh, "scan: newly found games are added");
    }

    /// <summary>Data files: a damaged one never becomes an empty library or default settings.</summary>
    static void DurableSaveTests(string dir)
    {
        var path = Path.Combine(dir, "settings.json");
        var info = StorageJsonContext.Default.Settings;
        Storage.Save(path, new Settings { HideDelaySeconds = 7 }, info);
        Storage.Save(path, new Settings { HideDelaySeconds = 9 }, info);
        Check(Storage.Load(path, info)?.HideDelaySeconds == 9 && File.Exists(path + ".bak") && !File.Exists(path + ".tmp"),
            "save: the new file is in place, the one before kept as .bak");

        File.WriteAllBytes(path, new byte[64]); // what a power loss can leave: the right size, all zeros
        var loaded = Storage.Load(path, info);
        Check(loaded?.HideDelaySeconds == 7, "save: a damaged file falls back to the copy before");
        Check(File.Exists(path + ".bad"), "save: the damaged file is kept aside as .bad");

        File.WriteAllText(path, "{");
        File.Delete(path + ".bak");
        Check(Storage.Load(path, info) is null, "save: damaged and no backup: nothing (the caller starts fresh)");
        Check(Storage.Load(Path.Combine(dir, "missing.json"), info) is null, "save: a missing file is just missing");
    }

    /// <summary>Shortcuts in our folder that no record names still go back; a manual add's own copy doesn't.</summary>
    static void UnrecordedShortcutTests(string dir)
    {
        var folder = Path.Combine(dir, "Shortcuts");
        Directory.CreateDirectory(folder);
        ShortcutImporter.FolderForTests = folder;
        try
        {
            var recorded = Path.Combine(folder, "Recorded.lnk");
            var lost = Path.Combine(folder, "Lost.url");
            var manualCopy = Path.Combine(folder, "Dropped.lnk");
            foreach (var f in new[] { recorded, lost, manualCopy, Path.Combine(folder, "notes.txt") }) File.WriteAllText(f, "");
            var records = new List<ImportedShortcut> { new() { StoredPath = recorded, OriginalPath = @"C:\Users\x\Desktop\Recorded.lnk" } };
            var games = new List<Game> { new() { Id = "manual:d", Source = GameSource.Manual, ExePath = manualCopy } };
            var found = ShortcutImporter.Unrecorded(records, games);
            Check(found.Count == 1 && Path.GetFileName(found[0].StoredPath) == "Lost.url" && found[0].OriginalPath == "",
                "shortcuts: one in our folder without a record still goes back (to the user's desktop)");
        }
        finally
        {
            ShortcutImporter.FolderForTests = null;
        }
    }

    /// <summary>Play twice in a row starts the game once.</summary>
    static void RepeatGuardTests()
    {
        var guard = new RepeatGuard(TimeSpan.FromSeconds(5));
        Check(!guard.IsRepeat("a", 1000), "play: the first press starts the game");
        guard.Started("a", 1000);
        Check(guard.IsRepeat("a", 1300), "play: a second press right after is ignored");
        Check(!guard.IsRepeat("b", 1300), "play: another game starts at once");
        Check(!guard.IsRepeat("a", 6000), "play: the same game again after a few seconds starts");
    }

    /// <summary>Time the PC slept during a game isn't playtime: the session is saved in its awake parts.</summary>
    static void SleepSplitTests()
    {
        var t0 = new DateTimeOffset(2026, 11, 12, 20, 0, 0, TimeSpan.FromHours(1));
        var parts = Binder.Watcher.SystemEvents.Awake(t0, t0.AddHours(10), [(t0.AddHours(1), t0.AddHours(9))]);
        Check(parts.Count == 2 && parts[0] == (t0, t0.AddHours(1)) && parts[1] == (t0.AddHours(9), t0.AddHours(10)),
            "sessions: a night asleep mid-game leaves two hours played, in two parts");
        Check(Binder.Watcher.SystemEvents.Awake(t0, t0.AddHours(2), []).Count == 1, "sessions: no sleep, one session");
        var edge = Binder.Watcher.SystemEvents.Awake(t0, t0.AddHours(2), [(t0.AddHours(-1), t0.AddMinutes(30)), (t0.AddHours(1.5), t0.AddHours(3))]);
        Check(edge.Count == 1 && edge[0] == (t0.AddMinutes(30), t0.AddHours(1.5)), "sessions: sleeps over the start or the end are cut off");
    }
}
