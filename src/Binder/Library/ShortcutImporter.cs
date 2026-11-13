using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Binder.Models;
using Binder.Services;

namespace Binder.Library;

/// <summary>
/// Turns desktop shortcuts and picked files into library entries. Imported shortcuts are moved (never
/// deleted) into our own folder, so the desktop gets tidy and "Restore to desktop" can put them back.
/// </summary>
public static class ShortcutImporter
{
    public static string Folder => FolderForTests ?? Path.Combine(Storage.DataDir, "Shortcuts");

    /// <summary>Unit tests only: a scratch folder instead of the user's.</summary>
    internal static string? FolderForTests { get; set; }

    /// <summary>Unit tests only: scratch folders instead of the user's and the Public desktop (the first is "the user's").</summary>
    internal static string[]? DesktopsForTests { get; set; }

    /// <summary>
    /// The library entry a shortcut duplicates, or null: a store link (steam://rungameid/570 → steam:570), or a
    /// shortcut to a store game's own exe (the EA app's "Battlefield 6.lnk" → bf6.exe). Other programs in a game's
    /// folder (a mod loader, a benchmark) stay games of their own.
    /// </summary>
    public static Game? ExistingFor(ShortcutFile shortcut, IReadOnlyList<Game> library)
    {
        string? id = shortcut.SteamAppId is { } appId ? "steam:" + appId
            : shortcut.EpicAppName is { } app ? "epic:" + app
            : shortcut.UbisoftId is { } ubisoft ? "ubisoft:" + ubisoft
            : null;
        if (id is not null) return library.FirstOrDefault(g => g.Id == id);
        return shortcut.TargetPath is { } target ? StoreGameWithExe(target, library) : null;
    }

    /// <summary>
    /// Takes out shortcut games that duplicate a store game (their target is its exe; <see cref="CreateGame"/>
    /// keeps the target as the icon path), left by imports made before <see cref="ExistingFor"/> knew them. A
    /// favorite carries over; the shortcut file stays in our folder, so "Restore to desktop" still puts it back.
    /// True if any were taken out.
    /// </summary>
    public static bool RemoveDuplicates(List<Game> games)
    {
        // Store games by their exe (the first one wins, as before), so each shortcut is one lookup.
        var storeByExe = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in games)
            if (IsStoreGame(g) && g.ExePath is { } exe) storeByExe.TryAdd(exe, g);
        int removed = games.RemoveAll(g =>
        {
            if (g.Source != GameSource.Shortcut || g.IconPath is not { } target || !storeByExe.TryGetValue(target, out var store)) return false;
            store.Favorite |= g.Favorite;
            return true;
        });
        return removed > 0;
    }

    private static bool IsStoreGame(Game g) => g.Source is not (GameSource.Shortcut or GameSource.Manual);

    private static Game? StoreGameWithExe(string exe, IReadOnlyList<Game> library) =>
        library.FirstOrDefault(g => IsStoreGame(g) && string.Equals(g.ExePath, exe, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pre-ticked in the picker: store links, and programs inside a known game's folder.</summary>
    public static bool LooksLikeGame(ShortcutFile shortcut, IReadOnlyList<Game> library)
    {
        if (shortcut.SteamAppId is not null || shortcut.EpicAppName is not null || shortcut.UbisoftId is not null) return true;
        if (shortcut.TargetPath is not { } target) return false;
        return library.Any(g => g.InstallDir is { } dir && InstallFolder.IsSameOrInside(target, dir));
    }

    /// <summary>
    /// Moves the shortcut into our folder; if that isn't allowed (the shared Public desktop needs admin
    /// rights) it's copied instead and the original stays. Returns null if neither worked.
    /// <para>With <paramref name="records"/> (an import), the record is added and saved <b>before</b> the file moves,
    /// so a crash or a failed save can never leave a moved shortcut that nothing knows how to put back. Without it
    /// (a shortcut dropped onto the panel), only a copy is made and the user's file stays.</para>
    /// </summary>
    public static ImportedShortcut? Store(ShortcutFile shortcut, bool move, List<ImportedShortcut>? records = null)
    {
        Directory.CreateDirectory(Folder);
        var target = UniquePath(Folder, Path.GetFileName(shortcut.Path));
        var record = new ImportedShortcut { StoredPath = target, OriginalPath = shortcut.Path, Copied = !move };
        if (records is not null)
        {
            records.Add(record);
            if (!TrySaveRecords(records))
            {
                records.Remove(record);
                return null; // nothing moved: the shortcut stays on the desktop
            }
        }
        if (move)
        {
            try
            {
                File.Move(shortcut.Path, target);
                return record;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            record.Copied = true;
        }
        try
        {
            File.Copy(shortcut.Path, target);
            if (records is not null) TrySaveRecords(records); // now "copied": the original stays where it is
            return record;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (records is not null)
            {
                records.Remove(record);
                TrySaveRecords(records);
            }
            return null;
        }
    }

    private static bool TrySaveRecords(List<ImportedShortcut> records)
    {
        try
        {
            Storage.SaveImportedShortcuts(records);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Shortcuts in our folder that no record names (left by an older version's interrupted import, or a lost
    /// imported-shortcuts.json): records for them, to go back to the user's desktop. A copy a manually added game
    /// launches (<paramref name="games"/>) isn't one: the user's own file never left.
    /// </summary>
    internal static List<ImportedShortcut> Unrecorded(IEnumerable<ImportedShortcut> records, IEnumerable<Game> games)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
            if (!string.IsNullOrEmpty(r.StoredPath) && Path.IsPathFullyQualified(r.StoredPath)) known.Add(Path.GetFullPath(r.StoredPath));
        foreach (var g in games)
            if (g.Source == GameSource.Manual && g.ExePath is { } exe && Path.IsPathFullyQualified(exe)) known.Add(Path.GetFullPath(exe));
        var found = new List<ImportedShortcut>();
        if (!Directory.Exists(Folder)) return found;
        try
        {
            foreach (var file in Directory.EnumerateFiles(Folder))
                if (StoredShortcut(file) is { } stored && !known.Contains(stored))
                    found.Add(new ImportedShortcut { StoredPath = stored, OriginalPath = "" });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return found;
    }

    /// <summary>A game that launches the stored shortcut file (keeping its arguments and working folder).</summary>
    public static Game CreateGame(ShortcutFile shortcut, string storedPath, GameSource source)
    {
        var game = new Game
        {
            Id = IdPrefix(source) + Hash(shortcut.Path),
            Source = source,
            Name = shortcut.Name,
            ExePath = storedPath,
            AddedAt = DateTimeOffset.Now,
        };
        if (shortcut.TargetPath is { } target)
        {
            game.IconPath = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : null;
            // Watch the target's folder; if that's a shared folder (e.g. System32), watch the program by name.
            game.InstallDir = InstallFolder.Validate(Path.GetDirectoryName(target));
            if (game.InstallDir is null) game.ProcessName = Path.GetFileName(target);
        }
        return game;
    }

    /// <summary>A game for an .exe the user picked.</summary>
    public static Game CreateFromExe(string exe)
    {
        var folder = InstallFolder.Validate(Path.GetDirectoryName(exe));
        return new Game
        {
            Id = "manual:" + Hash(exe),
            Source = GameSource.Manual,
            Name = FriendlyName(exe),
            ExePath = exe,
            InstallDir = folder,
            ProcessName = folder is null ? Path.GetFileName(exe) : null,
            AddedAt = DateTimeOffset.Now,
        };
    }

    /// <summary>
    /// Settings → "Restore shortcuts to desktop" and uninstall: puts every imported shortcut back, removes the
    /// games made from the restored ones from <paramref name="games"/>, and saves the list of the ones left.
    /// The caller saves the library. Returns how many went back and how many couldn't.
    /// </summary>
    public static (int Restored, int Failed) RestoreAll(List<Game> games)
    {
        var records = Storage.LoadImportedShortcuts();
        records.AddRange(Unrecorded(records, games));
        if (records.Count == 0) return (0, 0);
        var failed = Restore(records);
        var restored = records.Except(failed).Select(r => r.StoredPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        games.RemoveAll(g => g.Source == GameSource.Shortcut && g.ExePath is { } path && restored.Contains(path));
        try { Storage.SaveImportedShortcuts(failed); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        ShellNotify.DesktopsChanged();
        return (restored.Count, failed.Count);
    }

    /// <summary>
    /// Puts imported shortcuts back where they came from (next to an existing file of the same name if
    /// needed). Returns the records that couldn't be restored.
    /// </summary>
    public static List<ImportedShortcut> Restore(IEnumerable<ImportedShortcut> records)
    {
        var failed = new List<ImportedShortcut>();
        foreach (var record in records)
        {
            try
            {
                // The paths come from imported-shortcuts.json: only a shortcut in our own folder is moved, and only
                // back onto a desktop (the one it came from if that's still a desktop, else the user's).
                if (StoredShortcut(record.StoredPath) is not { } stored) continue;
                if (record.Copied && File.Exists(record.OriginalPath))
                {
                    File.Delete(stored); // only a copy of ours; the original never left
                    continue;
                }
                var original = RestoreTarget(record.OriginalPath, Path.GetFileName(stored));
                if (original is null)
                {
                    failed.Add(record);
                    continue;
                }
                var folder = Path.GetDirectoryName(original)!;
                var target = File.Exists(original) ? UniquePath(folder, Path.GetFileName(original)) : original;
                File.Move(stored, target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                failed.Add(record);
            }
        }
        return failed;
    }

    /// <summary>The stored shortcut's full path if it's a .lnk or .url file (not a link) directly in our folder, else null.</summary>
    internal static string? StoredShortcut(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return null;
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase) ||
            !IsShortcutName(Path.GetFileName(full)) || !File.Exists(full) ||
            File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)) return null;
        return full;
    }

    /// <summary>
    /// Where a shortcut goes back to: its original place if that's directly on the user's or the Public desktop
    /// and a .lnk/.url name, else the user's desktop under its stored name. Null when there's no desktop.
    /// </summary>
    internal static string? RestoreTarget(string? original, string storedName)
    {
        var desktops = (DesktopsForTests ?? new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        }).Where(d => !string.IsNullOrEmpty(d)).Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d))).ToArray();
        if (!string.IsNullOrEmpty(original) && Path.IsPathFullyQualified(original))
        {
            var full = Path.GetFullPath(original);
            var dir = Path.GetDirectoryName(full);
            if (IsShortcutName(Path.GetFileName(full)) && desktops.Any(d => string.Equals(d, dir, StringComparison.OrdinalIgnoreCase)) &&
                Directory.Exists(dir))
                return full;
        }
        return desktops.Length > 0 && Directory.Exists(desktops[0]) ? Path.Combine(desktops[0], storedName) : null;
    }

    private static bool IsShortcutName(string name) =>
        name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Game.lnk", or "Game (2).lnk" if that's taken.</summary>
    public static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{stem} ({i}){ext}");
        return path;
    }

    /// <summary>The product name from the exe's version info, else its file name.</summary>
    public static string FriendlyName(string exe)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            foreach (var candidate in new[] { info.ProductName, info.FileDescription })
            {
                var name = candidate?.Trim();
                if (!string.IsNullOrEmpty(name) && name.Length <= 80) return name;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return Path.GetFileNameWithoutExtension(exe);
    }

    private static string IdPrefix(GameSource source) => source == GameSource.Manual ? "manual:" : "shortcut:";

    /// <summary>Stable short id from a path: same file, same id.</summary>
    private static string Hash(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..16];
}
