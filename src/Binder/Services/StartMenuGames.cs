using Binder.Library;

namespace Binder.Services;

/// <summary>
/// The Start menu (spec §22 c, d). Reads the user's and the shared Start menu shortcuts (never runs them) to tell
/// which games Start search already finds.
/// </summary>
public static class StartMenuGames
{
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
