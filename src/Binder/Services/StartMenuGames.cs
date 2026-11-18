using Binder.Library;
using Binder.Models;

namespace Binder.Services;

/// <summary>
/// The Start menu (spec §22 c, d). Reads the user's and the shared Start menu shortcuts (never runs them) to tell
/// which games Start search already finds, and, with "Add my games to Start search" on, keeps Binder's own folder
/// of game shortcuts ("Binder games") in step with the shelf. Only .lnk files directly in that folder are ever
/// written or deleted, never through links; other Start entries are never touched.
/// </summary>
public static class StartMenuGames
{
    /// <summary>
    /// Brings our folder in step with the shelf: one shortcut per game Start search doesn't find, each running
    /// "Binder.exe --play &lt;id&gt;". Nothing is written when <paramref name="lastStamp"/> says it's already so.
    /// Returns the new stamp (keep it in settings), or null when the folder couldn't be written. Off the UI thread.
    /// </summary>
    public static string? Sync(IReadOnlyList<Game> shelf, string? lastStamp)
    {
        lock (Gate) return SyncLocked(shelf, lastStamp);
    }

    // A rescan and the setting's switch may both start a run: one at a time on the folder.
    private static readonly Lock Gate = new();

    private static string? SyncLocked(IReadOnlyList<Game> shelf, string? lastStamp)
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        var plan = StartMenuMatch.Plan(shelf, StartMenuMatch.GamesWithEntry(ReadEntries(), shelf));
        var icons = plan.ConvertAll(p => IconFor(p.Game, exe));

        var stamp = new System.Text.StringBuilder(exe).Append('\n');
        for (int i = 0; i < plan.Count; i++) stamp.Append(plan[i].FileName).Append('|').Append(plan[i].Game.Id).Append('|').Append(icons[i]).Append('\n');
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stamp.ToString())));
        var folder = OwnFolder;
        if (hash == lastStamp && Directory.Exists(folder)) return hash;

        try
        {
            if (Directory.Exists(folder))
            {
                if (IsLink(folder)) return null; // not ours: leave it alone
                DeleteOurShortcuts(folder);
            }
            else Directory.CreateDirectory(folder);
            for (int i = 0; i < plan.Count; i++)
            {
                var (file, game) = plan[i];
                Library.ShellLink.Write(Path.Combine(folder, file), exe, "--play " + game.Id, game.Name, icons[i]); // a plain id (Plan)
            }
            return hash;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            ErrorLog.Write(e, "Start menu games");
            return null;
        }
    }

    /// <summary>"Add my games to Start search" turned off, or uninstall: removes our shortcuts and then the folder if it's empty.</summary>
    public static void Remove()
    {
        lock (Gate) RemoveLocked();
    }

    private static void RemoveLocked()
    {
        var folder = OwnFolder;
        try
        {
            if (!Directory.Exists(folder) || IsLink(folder)) return;
            DeleteOurShortcuts(folder);
            if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(e, "Start menu games");
        }
    }

    /// <summary>Only .lnk files directly in the folder, never links themselves.</summary>
    private static void DeleteOurShortcuts(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*.lnk"))
            if (!IsLink(file)) File.Delete(file);
    }

    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    /// <summary>
    /// The game's own icon where Binder knows one (an .exe or .ico it found, or for an imported shortcut its
    /// IconFile or target), else Binder's. Shortcuts are only read, never resolved or run.
    /// </summary>
    private static string IconFor(Game game, string binderExe)
    {
        foreach (var candidate in new[] { game.IconPath, game.ExePath })
        {
            if (candidate is not { } path || !Path.IsPathFullyQualified(path)) continue;
            if (IsIconFile(path)) return path;
            try
            {
                var inner = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".url" => ShortcutFile.ReadUrlValue(path, "IconFile"),
                    ".lnk" => Library.ShellLink.ReadTarget(path),
                    _ => null,
                };
                if (inner is not null && Path.IsPathFullyQualified(inner) && IsIconFile(inner)) return inner;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or
                                          System.Runtime.InteropServices.COMException or InvalidCastException) { }
        }
        return binderExe;

        static bool IsIconFile(string path) =>
            (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) &&
            File.Exists(path);
    }


    // Bounded: Start menus are small, but a broken or huge one mustn't stall setup.
    private const int MaxDepth = 4, MaxFiles = 3000;

    /// <summary>Our own folder of game shortcuts in the user's Start menu (spec §22 c); never counted as a game's own entry.</summary>
    public static string OwnFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Binder games");

    /// <summary>
    /// The .lnk and .url files in the user's and the shared Start menu Programs folders (not ours), read with
    /// <see cref="ShortcutFile"/>, never run. Links to folders are not followed. Run off the UI thread.
    /// </summary>
    public static List<ShortcutFile> ReadEntries()
    {
        var found = new List<ShortcutFile>();
        int files = 0;
        var own = Path.TrimEndingDirectorySeparator(OwnFolder);
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                 })
        {
            if (!string.IsNullOrEmpty(root)) Walk(root, 0);
        }
        return found;

        void Walk(string folder, int depth)
        {
            if (files >= MaxFiles || string.Equals(Path.TrimEndingDirectorySeparator(folder), own, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (++files > MaxFiles) return;
                    var ext = Path.GetExtension(file);
                    if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".url", StringComparison.OrdinalIgnoreCase)) continue;
                    if (ShortcutFile.Read(file) is { CanBeGame: true } shortcut) found.Add(shortcut);
                }
                if (depth >= MaxDepth) return;
                foreach (var sub in Directory.EnumerateDirectories(folder))
                {
                    if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    Walk(sub, depth + 1);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
