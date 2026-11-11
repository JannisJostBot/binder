using System.IO.Compression;

namespace GameShelf.Setup;

/// <summary>
/// The app embedded in this exe by publish.ps1: app.zip (the widget's folder plus the watcher) and payload.txt,
/// which lists every file as "sha256 length relative\path". Setup writes only the files listed there, checks
/// each one's hash after writing it, and nothing in the zip that isn't listed is ever extracted.
/// </summary>
internal sealed class Payload : IDisposable
{
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);

    private Payload(List<PayloadList.Entry> files, ZipArchive zip)
    {
        Files = files;
        _zip = zip;
        // Zip tools disagree on the separator (Windows PowerShell's writes "\"), so look entries up either way.
        foreach (var entry in zip.Entries) _entries.TryAdd(entry.FullName.Replace('/', '\\'), entry);
    }

    public List<PayloadList.Entry> Files { get; }
    public long TotalBytes => Files.Sum(f => f.Length);

    public Stream Open(PayloadList.Entry file) =>
        _entries.TryGetValue(file.Path, out var entry) ? entry.Open() : throw new SetupException(Text.MissingFromSetup(file.Path));

    public void Dispose() => _zip.Dispose();

    public static Payload? Read()
    {
        var assembly = typeof(Payload).Assembly;
        using var list = assembly.GetManifestResourceStream("payload.txt");
        var zipStream = assembly.GetManifestResourceStream("app.zip");
        if (list is null || zipStream is null)
        {
            zipStream?.Dispose();
            return null;
        }
        var files = PayloadList.Parse(new StreamReader(list), Locations.WidgetExe, Locations.WatcherExe);
        if (files is null)
        {
            zipStream.Dispose();
            return null;
        }
        return new Payload(files, new ZipArchive(zipStream, ZipArchiveMode.Read));
    }
}

/// <summary>
/// installed-files.txt in the install folder: what the last setup wrote there (relative paths), so an update
/// can remove files the new version no longer has and uninstall knows what to delete. Every entry is checked
/// again when it's read: a path that isn't plainly inside the install folder is ignored.
/// </summary>
internal static class InstalledFiles
{
    public const string ListName = "installed-files.txt";

    public static string ListPath => Path.Combine(Locations.InstallDir, ListName);

    public static bool IsSafe(string relative) => PayloadList.IsSafe(relative);

    /// <summary>The full path, or null when a folder on the way is a link (setup never writes or deletes through one).</summary>
    public static string? FullPath(string relative)
    {
        if (!IsSafe(relative)) return null;
        var full = Path.GetFullPath(Path.Combine(Locations.InstallDir, relative));
        if (!Locations.IsInside(full, Locations.InstallDir)) return null;
        for (var dir = Path.GetDirectoryName(full); dir is not null && Locations.IsInside(dir, Locations.InstallDir); dir = Path.GetDirectoryName(dir))
            if (Locations.IsReparsePoint(dir)) return null;
        return full;
    }

    /// <summary>The previous setup's list; with none (versions before the list existed) the files they wrote.</summary>
    public static List<string> Read()
    {
        try
        {
            if (!System.IO.File.Exists(ListPath) || Locations.IsReparsePoint(ListPath)) return [.. Locations.LegacyFiles];
            var info = new FileInfo(ListPath);
            if (info.Length > 1_000_000) return [.. Locations.LegacyFiles];
            return System.IO.File.ReadAllLines(ListPath).Select(l => l.Trim()).Where(IsSafe).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [.. Locations.LegacyFiles];
        }
    }

    public static void Write(IEnumerable<string> files) =>
        System.IO.File.WriteAllLines(ListPath, files);

    /// <summary>
    /// Deletes the listed files (never through links) and then the folders they leave empty, deepest first.
    /// <paramref name="delete"/> does the deleting, so uninstall can retry a file still in use.
    /// </summary>
    public static void Remove(IEnumerable<string> files, Action<string> delete)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in files)
        {
            if (FullPath(relative) is not { } full) continue;
            // A link in a file's place is removed itself, never followed.
            if (System.IO.File.Exists(full) || Locations.IsReparsePoint(full)) delete(full);
            for (var dir = Path.GetDirectoryName(full); dir is not null && Locations.IsInside(dir, Locations.InstallDir); dir = Path.GetDirectoryName(dir))
                dirs.Add(dir);
        }
        foreach (var dir in dirs.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Locations.IsReparsePoint(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
