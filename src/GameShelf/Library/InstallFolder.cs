namespace GameShelf.Library;

/// <summary>
/// Decides whether a folder may be treated as "the game's folder". The watcher counts every process started
/// from inside it as the game, so a folder like C:\, C:\Windows or Program Files must never qualify: the
/// widget would wait on unrelated programs and not come back. Used for store manifests, the GOG registry,
/// shortcut targets and manually added games, which are all untrusted or arbitrary input.
/// </summary>
public static class InstallFolder
{
    private static readonly Lazy<string[]> SharedFolders = new(() =>
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            profile,
            string.IsNullOrEmpty(profile) ? "" : Path.Combine(profile, "Downloads"),
        ];
        return folders.Where(f => !string.IsNullOrEmpty(f)).Select(Normalize).ToArray();
    });

    /// <summary>
    /// The normalized folder if it's a plausible game folder: a full path that exists, isn't a drive root,
    /// isn't inside Windows, and isn't one of the big shared folders itself (a folder inside them is fine).
    /// </summary>
    public static string? Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return null;
            full = Normalize(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || Same(Normalize(root), full)) return null;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && IsSameOrInside(full, Normalize(windows))) return null;

        foreach (var shared in SharedFolders.Value)
            if (Same(shared, full)) return null;

        return Directory.Exists(full) ? full : null;
    }

    /// <summary>True if <paramref name="path"/> is <paramref name="folder"/> or somewhere below it.</summary>
    public static bool IsSameOrInside(string path, string folder) =>
        Same(path, folder) ||
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
