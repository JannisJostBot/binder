using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GameShelf.Models;

namespace GameShelf.Services;

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

/// <summary>JSON persistence in %APPDATA%\GameShelf. Source-generated, no reflection.</summary>
public static class Storage
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameShelf");

    public static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameShelf");

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
        var old = Load(legacy, StorageJsonContext.Default.ListSession);
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

    private static T? Load<T>(string path, JsonTypeInfo<T> info) where T : class
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, info);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Missing or corrupt file: start fresh rather than crash.
            return null;
        }
    }

    /// <summary>Writes to a temp file and swaps it in, so a crash never leaves a half-written file.</summary>
    private static void Save<T>(string path, T value, JsonTypeInfo<T> info)
    {
        Directory.CreateDirectory(DataDir);
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, value, info);
        File.Move(tmp, path, overwrite: true);
    }
}
