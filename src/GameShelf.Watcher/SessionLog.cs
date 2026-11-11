namespace GameShelf.Shared;

/// <summary>
/// Where play sessions are stored: one JSON Lines file per year in %APPDATA%\GameShelf
/// (<c>sessions-2026.jsonl</c>), one compact JSON object per line. Compiled into the watcher (which appends) and the
/// widget (which reads, see <c>Storage.LoadSessions</c>).
/// <list type="bullet">
/// <item>A session goes into the file of the year it <b>ended</b> (local time, as recorded), so the watcher only
/// ever appends to the current year's file and past years never change. A session over New Year stays one record;
/// whoever needs per-year totals clips it at Jan 1.</item>
/// <item>The file name is only where to look: readers load every year file and go by the timestamps.</item>
/// <item>Appending writes one line and never reads the file, so the watcher's cost doesn't grow with the history.
/// A line cut off by a power loss is skipped by the reader, and the next append starts on a new line.</item>
/// <item>New fields are optional; readers ignore fields they don't know. A breaking change gets a new file name.</item>
/// </list>
/// </summary>
public static class SessionLog
{
    /// <summary>The single file used before year files (an indented JSON array); moved over by the widget.</summary>
    public const string LegacyFile = "sessions.json";

    // A wrong system clock still lands in a file the reader accepts.
    public const int MinYear = 2000;
    public const int MaxYear = 2999;

    /// <summary>Bounds for the reader: a year file and one line (a session is ~150 bytes).</summary>
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const int MaxLineBytes = 16 * 1024;

    private const string Prefix = "sessions-";
    private const string Extension = ".jsonl";

    public static string FileName(int year) => $"{Prefix}{Math.Clamp(year, MinYear, MaxYear)}{Extension}";

    /// <summary>The file a session that ended at <paramref name="end"/> belongs in.</summary>
    public static string PathFor(string dataDir, DateTimeOffset end) => Path.Combine(dataDir, FileName(end.Year));

    /// <summary>The year of a <c>sessions-YYYY.jsonl</c> file name; false for any other name.</summary>
    public static bool TryYear(string fileName, out int year)
    {
        year = 0;
        if (fileName.Length != Prefix.Length + 4 + Extension.Length ||
            !fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            return false;
        var digits = fileName.AsSpan(Prefix.Length, 4);
        foreach (var c in digits)
            if (c is < '0' or > '9') return false;
        year = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        return year is >= MinYear and <= MaxYear;
    }

    /// <summary>The year files in <paramref name="dataDir"/>, oldest first.</summary>
    public static List<(int Year, string Path)> YearFiles(string dataDir)
    {
        var files = new List<(int Year, string Path)>();
        if (!Directory.Exists(dataDir)) return files;
        foreach (var path in Directory.EnumerateFiles(dataDir, Prefix + "*" + Extension))
            if (TryYear(Path.GetFileName(path), out int year))
                files.Add((year, path));
        files.Sort((a, b) => a.Year.CompareTo(b.Year));
        return files;
    }

    /// <summary>
    /// Appends one JSON object (no line breaks inside) as a line. Starts on a new line if the file's last line was
    /// cut off. Throws IOException while another writer holds the file; callers retry.
    /// </summary>
    public static void AppendLine(string path, ReadOnlySpan<byte> json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            bool endsWithNewline = stream.ReadByte() == '\n';
            stream.Seek(0, SeekOrigin.End);
            if (!endsWithNewline) stream.WriteByte((byte)'\n');
        }
        stream.Write(json);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }
}
