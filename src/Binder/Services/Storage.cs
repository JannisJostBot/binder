using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Binder.Models;

namespace Binder.Services;

[JsonSourceGenerationOptions(
    WriteIndented = true,
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

    public static Settings LoadSettings() => Load(SettingsPath, StorageJsonContext.Default.Settings) ?? new Settings();
    public static void SaveSettings(Settings s) => Save(SettingsPath, s, StorageJsonContext.Default.Settings);

    /// <summary>All sessions, from every year file, oldest year first (moves an old sessions.json over first).</summary>
    public static List<Session> LoadSessions()
    {
        MigrateLegacySessions(DataDir);
        return LoadSessions(DataDir);
    }

    internal static List<Session> LoadSessions(string dir)
    {
        var sessions = new List<Session>();
        foreach (var (_, path) in Shared.SessionLog.YearFiles(dir))
            ReadSessionLines(path, sessions);
        return sessions;
    }

    /// <summary>
    /// Reads one year file. Lines that don't parse (cut off by a power loss, edited by hand) or have no game are
    /// skipped; an oversized file is skipped whole. Shares the file with a watcher that is appending.
    /// </summary>
    private static void ReadSessionLines(string path, List<Session> into)
    {
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > Shared.SessionLog.MaxFileBytes) return;
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        ReadOnlySpan<byte> rest = bytes;
        if (rest.StartsWith("﻿"u8)) rest = rest[3..];
        while (!rest.IsEmpty)
        {
            int end = rest.IndexOf((byte)'\n');
            var line = end < 0 ? rest : rest[..end];
            rest = end < 0 ? default : rest[(end + 1)..];
            line = line.TrimEnd("\r"u8).Trim(" \t"u8);
            if (line.IsEmpty || line.Length > Shared.SessionLog.MaxLineBytes) continue;
            try
            {
                if (JsonSerializer.Deserialize(line, SessionLineContext.Default.Session) is { GameId.Length: > 0 } session)
                    into.Add(session);
            }
            catch (JsonException)
            {
                // A broken line: skip it, keep the rest.
            }
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

    public static List<ImportedShortcut> LoadImportedShortcuts() =>
        Load(ImportedPath, StorageJsonContext.Default.ListImportedShortcut) ?? [];
    public static void SaveImportedShortcuts(List<ImportedShortcut> list) =>
        Save(ImportedPath, list, StorageJsonContext.Default.ListImportedShortcut);

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
