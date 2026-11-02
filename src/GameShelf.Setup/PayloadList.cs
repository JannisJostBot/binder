namespace GameShelf.Setup;

/// <summary>
/// payload.txt and installed-files.txt checks, with no other dependencies (the unit tests compile this file).
/// </summary>
internal static class PayloadList
{
    public const int MaxFiles = 2000;
    private const int MaxDepth = 8;

    public sealed record Entry(string Path, string Sha256, long Length);

    /// <summary>A relative path of plain names: no root, drive, "..", stream or device syntax.</summary>
    public static bool IsSafe(string relative)
    {
        if (relative.Length is 0 or > 200 || relative.Contains('/') || System.IO.Path.IsPathRooted(relative)) return false;
        var parts = relative.Split('\\');
        if (parts.Length > MaxDepth) return false;
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')) return false;
            if (part.Any(c => c < 32 || c is ':' or '*' or '?' or '"' or '<' or '>' or '|')) return false;
        }
        return true;
    }

    /// <summary>
    /// Parses "sha256 length relative\path" lines; null when anything is off (a bad line, an unsafe or repeated
    /// path, too many files, or one of <paramref name="required"/> missing).
    /// </summary>
    public static List<Entry>? Parse(TextReader reader, params string[] required)
    {
        var files = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit) ||
                !long.TryParse(parts[1], out var length) || length < 0)
                return null;
            if (!IsSafe(parts[2]) || !seen.Add(parts[2]) || files.Count >= MaxFiles) return null;
            files.Add(new Entry(parts[2], parts[0], length));
        }
        return required.All(seen.Contains) ? files : null;
    }
}
