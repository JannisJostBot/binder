using System.Text;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.Shared;

namespace GameShelf.Tests;

static partial class Tests
{
    /// <summary>
    /// Sessions in one JSON Lines file per year: names, the watcher's append, New Year, broken lines and the move
    /// from the old sessions.json.
    /// </summary>
    static void SessionLogTests(string root)
    {
        var dir = Path.Combine(root, "session-log");
        Directory.CreateDirectory(dir);
        var cet = TimeSpan.FromHours(1);

        // File names.
        Check(SessionLog.FileName(2026) == "sessions-2026.jsonl" && SessionLog.FileName(1980) == "sessions-2000.jsonl",
            "sessions: one file per year, a wrong clock clamped into range");
        Check(SessionLog.TryYear("sessions-2027.jsonl", out int y) && y == 2027 &&
              !SessionLog.TryYear("sessions-27.jsonl", out _) && !SessionLog.TryYear("sessions-20a6.jsonl", out _) &&
              !SessionLog.TryYear("sessions-1999.jsonl", out _) && !SessionLog.TryYear("sessions.json", out _) &&
              !SessionLog.TryYear("sessions-2026.jsonl.tmp", out _), "sessions: only sessions-YYYY.jsonl names are read");

        // Appending: a session over New Year goes into the year it ended, as one record.
        var newYearsEve = new DateTimeOffset(2026, 12, 31, 23, 10, 0, cet);
        var line = Encoding.UTF8.GetBytes(
            $"{{\"GameId\":\"steam:570\",\"Start\":\"{newYearsEve:O}\",\"End\":\"{newYearsEve.AddHours(2):O}\",\"ExitCode\":0}}");
        SessionLog.AppendLine(SessionLog.PathFor(dir, newYearsEve.AddHours(2)), line);
        Check(File.Exists(Path.Combine(dir, "sessions-2027.jsonl")) && !File.Exists(Path.Combine(dir, "sessions-2026.jsonl")),
            "sessions: a session over New Year is stored once, in the year it ended");

        // A line cut off by a power loss: skipped, and the next append starts on its own line.
        var file2027 = Path.Combine(dir, "sessions-2027.jsonl");
        File.AppendAllText(file2027, "{\"GameId\":\"steam:730\",\"Sta");
        var after = new DateTimeOffset(2027, 1, 2, 18, 0, 0, cet);
        SessionLog.AppendLine(file2027, Encoding.UTF8.GetBytes(
            $"{{\"GameId\":\"steam:730\",\"Start\":\"{after:O}\",\"End\":\"{after.AddHours(1):O}\",\"Future\":{{\"x\":1}},\"CrashReporter\":true}}"));
        var loaded = Storage.LoadSessions(dir);
        Check(loaded.Count == 2 && loaded[0].GameId == "steam:570" && loaded[0].Start == newYearsEve &&
              loaded[0].End == newYearsEve.AddHours(2) && loaded[0].ExitCode == 0 &&
              loaded[1] is { GameId: "steam:730", CrashReporter: true, ExitCode: null },
            "sessions: a cut-off line is skipped, the next one still reads, unknown fields are ignored");
        Check(File.ReadAllText(file2027).Split('\n').Length == 4, "sessions: the append after a cut-off line starts a new line");

        // Lines without a game, blank lines, Windows line ends and a BOM (edited in Notepad).
        File.WriteAllText(Path.Combine(dir, "sessions-2025.jsonl"),
            "﻿{\"GameId\":\"gog:1\",\"Start\":\"2025-05-01T10:00:00+02:00\",\"End\":\"2025-05-01T11:00:00+02:00\"}\r\n\r\n" +
            "{\"GameId\":\"\",\"Start\":\"2025-05-02T10:00:00+02:00\",\"End\":\"2025-05-02T11:00:00+02:00\"}\r\n" +
            "[1,2]\r\n", new UTF8Encoding(false));
        loaded = Storage.LoadSessions(dir);
        Check(loaded.Count == 3 && loaded[0].GameId == "gog:1", "sessions: older years first; empty game ids and non-objects skipped");
        File.WriteAllText(Path.Combine(dir, "sessions-2024.jsonl"), new string('x', SessionLog.MaxLineBytes + 1) + "\n");
        Check(Storage.LoadSessions(dir).Count == 3, "sessions: an oversized line is skipped");

        // Moving the old single file over: by the year each ended, without duplicates, the old file kept as .bak.
        var legacyDir = Path.Combine(root, "session-legacy");
        Directory.CreateDirectory(legacyDir);
        var legacy = Path.Combine(legacyDir, "sessions.json");
        File.WriteAllText(legacy, """
            [
              { "GameId": "steam:570", "Start": "2026-03-01T20:00:00+01:00", "End": "2026-03-01T22:00:00+01:00", "ExitCode": 0 },
              { "GameId": "steam:730", "Start": "2026-12-31T23:30:00+01:00", "End": "2027-01-01T00:40:00+01:00", "ExitCode": 3221225477, "Exe": "cs2.exe" }
            ]
            """);
        Storage.MigrateLegacySessions(legacyDir);
        var moved = Storage.LoadSessions(legacyDir);
        Check(!File.Exists(legacy) && File.Exists(legacy + ".bak") && moved.Count == 2 &&
              File.ReadAllLines(Path.Combine(legacyDir, "sessions-2026.jsonl")).Length == 1 &&
              moved[1] is { GameId: "steam:730", ExitCode: 3221225477, Exe: "cs2.exe" },
            "sessions: the old sessions.json moves into year files and is kept as .bak");
        var compact = File.ReadAllText(Path.Combine(legacyDir, "sessions-2026.jsonl"));
        Check(!compact.Contains("CrashReporter") && !compact.Contains("Exe") && !compact.Contains("  "),
            "sessions: moved lines are compact and leave defaults out");

        // Run again with a new old file holding one known and one new session (an interrupted or repeated move).
        File.WriteAllText(legacy, """
            [
              { "GameId": "steam:570", "Start": "2026-03-01T20:00:00+01:00", "End": "2026-03-01T22:00:00+01:00", "ExitCode": 0 },
              { "GameId": "steam:570", "Start": "2026-03-02T20:00:00+01:00", "End": "2026-03-02T21:00:00+01:00", "ExitCode": 0 }
            ]
            """);
        Storage.MigrateLegacySessions(legacyDir);
        Check(Storage.LoadSessions(legacyDir).Count == 3 && !File.Exists(legacy) && File.Exists(legacy + ".bak"),
            "sessions: a second move adds only new sessions and keeps the first backup");

        // A broken old file stays where it is (nothing lost, tried again next start).
        File.WriteAllText(legacy, "[ { \"GameId\": ");
        Storage.MigrateLegacySessions(legacyDir);
        Check(File.Exists(legacy) && Storage.LoadSessions(legacyDir).Count == 3, "sessions: an unreadable old file is left alone");
    }
}
