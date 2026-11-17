using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Binder.Models;

namespace Binder.Services;

[JsonSourceGenerationOptions(
    WriteIndented = false, // compact: smaller files, read faster at start
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<Game>))]
[JsonSerializable(typeof(List<Session>))]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(List<ImportedShortcut>))]
[JsonSerializable(typeof(List<CrashRecord>))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;

/// <summary>One session per line in the year files (<see cref="Shared.SessionLog"/>): compact, defaults left out,
/// like the watcher writes them.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(Session))]
internal sealed partial class SessionLineContext : JsonSerializerContext;

/// <summary>JSON persistence in %APPDATA%\Binder. Source-generated, no reflection.</summary>
public static class Storage
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Binder");

    public static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Binder");

    private static string LibraryPath => Path.Combine(DataDir, "library.json");
    private static string SettingsPath => Path.Combine(DataDir, "settings.json");
    private static string ImportedPath => Path.Combine(DataDir, "imported-shortcuts.json");
    private static string CrashesPath => Path.Combine(DataDir, "crashes.json");
    private static string RemovedPath => Path.Combine(DataDir, "removed-games.json");

    public static List<Game> LoadLibrary() => Load(LibraryPath, StorageJsonContext.Default.ListGame) ?? [];
    public static void SaveLibrary(List<Game> games) => Save(LibraryPath, games, StorageJsonContext.Default.ListGame);

    /// <summary>
    /// Whether two libraries would be saved as the same file: a rescan that found nothing new then needs no save
    /// and no redraw. A few hundred games serialize in about a millisecond.
    /// </summary>
    public static bool SameLibrary(List<Game> a, List<Game> b) =>
        a.Count == b.Count &&
        JsonSerializer.SerializeToUtf8Bytes(a, StorageJsonContext.Default.ListGame).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(b, StorageJsonContext.Default.ListGame));

    public static Settings LoadSettings() => Load(SettingsPath, StorageJsonContext.Default.Settings) ?? new Settings();
    public static void SaveSettings(Settings s) => Save(SettingsPath, s, StorageJsonContext.Default.Settings);

    /// <summary>All sessions, from every year file, oldest year first (moves an old sessions.json over first).</summary>
    public static List<Session> LoadSessions()
    {
        MigrateLegacySessions(DataDir);
        SessionsRead.Clear();
        return LoadSessions(DataDir, SessionsRead);
    }

    /// <summary>How far each year file was read by <see cref="LoadSessions()"/> (bytes), for <see cref="ReloadSessions"/>.</summary>
    private static readonly Dictionary<string, long> SessionsRead = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// After a game ended: <paramref name="known"/> plus the sessions appended since the last read, reading only the
    /// new end of each year file (the watcher only ever appends). A file that got shorter or went away means it was
    /// changed some other way: everything is read again. Returns a new list (a check off the UI thread may still be
    /// reading the old one).
    /// </summary>
    public static List<Session> ReloadSessions(List<Session> known) => ReloadSessions(DataDir, known, SessionsRead);

    internal static List<Session> ReloadSessions(string dir, List<Session> known, Dictionary<string, long> read)
    {
        var files = Shared.SessionLog.YearFiles(dir);
        var present = new HashSet<string>(files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        bool rewritten = read.Count == 0 || read.Keys.Any(p => !present.Contains(p));
        if (!rewritten)
        {
            foreach (var (_, path) in files)
            {
                if (!read.TryGetValue(path, out long from)) continue;
                try
                {
                    if (new FileInfo(path).Length < from) rewritten = true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        if (rewritten)
        {
            read.Clear();
            return LoadSessions(dir, read);
        }
        var sessions = new List<Session>(known);
        foreach (var (_, path) in files)
            read[path] = ReadSessionLines(path, sessions, read.GetValueOrDefault(path));
        return sessions;
    }

    internal static List<Session> LoadSessions(string dir, Dictionary<string, long>? read = null)
    {
        var sessions = new List<Session>();
        foreach (var (_, path) in Shared.SessionLog.YearFiles(dir))
        {
            long end = ReadSessionLines(path, sessions, 0);
            if (read is not null) read[path] = end;
        }
        return sessions;
    }

    /// <summary>
    /// Reads one year file from byte <paramref name="from"/> on. Lines that don't parse (cut off by a power loss,
    /// edited by hand) or have no game are skipped; an oversized file is skipped whole. Shares the file with a
    /// watcher that is appending. Returns how far it's read: past the last complete line, or the end when the last
    /// line has no line break yet but parsed (so a line still being written is read again next time, once).
    /// </summary>
    private static long ReadSessionLines(string path, List<Session> into, long from)
    {
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > Shared.SessionLog.MaxFileBytes) return from;
            if (from < 0 || from > stream.Length) from = 0;
            stream.Seek(from, SeekOrigin.Begin);
            bytes = new byte[stream.Length - from];
            stream.ReadExactly(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return from;
        }

        ReadOnlySpan<byte> rest = bytes;
        long consumed = from;
        if (from == 0 && rest.StartsWith("\uFEFF"u8))
        {
            rest = rest[3..];
            consumed += 3;
        }
        while (!rest.IsEmpty)
        {
            int end = rest.IndexOf((byte)'\n');
            var line = end < 0 ? rest : rest[..end];
            rest = end < 0 ? default : rest[(end + 1)..];
            bool parsed = ReadSessionLine(line, into);
            if (end >= 0 || parsed) consumed += end < 0 ? line.Length : end + 1;
        }
        return consumed;
    }

    /// <summary>One line; false when it isn't a session (yet).</summary>
    private static bool ReadSessionLine(ReadOnlySpan<byte> line, List<Session> into)
    {
        line = line.TrimEnd("\r"u8).Trim(" \t"u8);
        if (line.IsEmpty || line.Length > Shared.SessionLog.MaxLineBytes) return false;
        try
        {
            if (JsonSerializer.Deserialize(line, SessionLineContext.Default.Session) is not { GameId.Length: > 0 } session) return false;
            into.Add(session);
            return true;
        }
        catch (JsonException)
        {
            return false; // a broken line: skip it, keep the rest
        }
    }

    /// <summary>
    /// Moves the sessions of the old single file (before year files) into the year files, by the year each ended,
    /// leaving out any already there (same game and start, so a run cut off half-way can simply run again). Then
    /// keeps the old file as sessions.json.bak. An unreadable old file stays where it is and is tried next start.
    /// </summary>
    internal static void MigrateLegacySessions(string dir)
    {
        var legacy = Path.Combine(dir, Shared.SessionLog.LegacyFile);
        if (!File.Exists(legacy)) return;
        // Read directly, not through Load: that would move a damaged file to .bad and fall back to a .bak from an
        // earlier move, and the old file must stay where it is instead.
        List<Session>? old;
        try
        {
            using var stream = File.OpenRead(legacy);
            old = JsonSerializer.Deserialize(stream, StorageJsonContext.Default.ListSession);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (old is null) return;
        try
        {
            var known = new HashSet<(string, long)>(LoadSessions(dir).Select(s => (s.GameId, s.Start.UtcTicks)));
            foreach (var session in old)
            {
                if (session.GameId.Length == 0 || !known.Add((session.GameId, session.Start.UtcTicks))) continue;
                var line = JsonSerializer.SerializeToUtf8Bytes(session, SessionLineContext.Default.Session);
                Shared.SessionLog.AppendLine(Shared.SessionLog.PathFor(dir, session.End), line);
            }
            var backup = legacy + ".bak";
            // A backup from an earlier move already holds the older sessions; this file's are all in the year files.
            if (File.Exists(backup)) File.Delete(legacy);
            else File.Move(legacy, backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Held by another program: what was written is skipped next time, the rest follows then.
        }
    }

    /// <summary>The kept settings of games that left the library (<see cref="Library.RemovedGames"/>).</summary>
    public static List<Game> LoadRemovedGames() => Load(RemovedPath, StorageJsonContext.Default.ListGame) ?? [];
    public static void SaveRemovedGames(List<Game> games) => Save(RemovedPath, games, StorageJsonContext.Default.ListGame);

    public static List<CrashRecord> LoadCrashes() => Load(CrashesPath, StorageJsonContext.Default.ListCrashRecord) ?? [];
    public static void SaveCrashes(List<CrashRecord> list) => Save(CrashesPath, list, StorageJsonContext.Default.ListCrashRecord);

    public static List<ImportedShortcut> LoadImportedShortcuts()
    {
        var list = Load(ImportedPath, StorageJsonContext.Default.ListImportedShortcut) ?? [];
        _importedCount = list.Count;
        return list;
    }

    public static void SaveImportedShortcuts(List<ImportedShortcut> list)
    {
        _importedCount = -1; // read again if the save fails half-way
        Save(ImportedPath, list, StorageJsonContext.Default.ListImportedShortcut);
        _importedCount = list.Count;
    }

    /// <summary>How many shortcuts were imported (the Settings card's line), read from the file only once: every
    /// change goes through <see cref="SaveImportedShortcuts"/>.</summary>
    public static int ImportedShortcutCount() => _importedCount >= 0 ? _importedCount : LoadImportedShortcuts().Count;

    private static volatile int _importedCount = -1;

    /// <summary>
    /// Reads a data file. A damaged one (cut short by a power loss, edited by hand) is kept aside as ".bad" and the
    /// copy from the save before is used (".bak", see <see cref="Save"/>), so a broken file never turns into an empty
    /// library or default settings that the next save would make permanent. A file another program holds for a
    /// moment is tried again briefly. Null only when there's no file, or neither copy can be read.
    /// </summary>
    internal static T? Load<T>(string path, JsonTypeInfo<T> info) where T : class
    {
        if (TryRead(path, info, out var value)) return value;
        if (!File.Exists(path) && !File.Exists(path + BackupSuffix)) return null;
        if (TryRead(path + BackupSuffix, info, out var backup))
        {
            ErrorLog.Note($"{Path.GetFileName(path)} couldn't be read; the copy from the save before was used");
            return backup;
        }
        return null;
    }

    private const string BackupSuffix = ".bak";

    private static bool TryRead<T>(string path, JsonTypeInfo<T> info, out T? value) where T : class
    {
        value = null;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = File.OpenRead(path);
                value = JsonSerializer.Deserialize(stream, info);
                return value is not null;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return false;
            }
            catch (JsonException)
            {
                // Damaged: keep it for a look, out of the way of the next save.
                try { File.Move(path, path + ".bad", overwrite: true); }
                catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
                ErrorLog.Note($"{Path.GetFileName(path)} is damaged; kept as {Path.GetFileName(path)}.bad");
                return false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 10) return false; // held by another program (a virus scanner, a sync tool)
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Writes to a temp file, flushes it to the disk, then swaps it in and keeps the previous file as ".bak". A crash
    /// or power loss at any point leaves either the old or the new file complete, never a half-written or empty one.
    /// </summary>
    internal static void Save<T>(string path, T value, JsonTypeInfo<T> info)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, value, info);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(tmp, path, path + BackupSuffix, ignoreMetadataErrors: true);
        else File.Move(tmp, path);
    }
}
