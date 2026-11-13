using Binder.Library;
using Binder.Models;

namespace Binder.Services;

/// <summary>
/// Keeps crashes.json (milestone 11): each session that ended with an error is checked against the Windows logs
/// once, when the widget next runs, and the result kept, so the detail view and its pattern line never read the
/// logs again. Sessions that ended normally aren't checked at all.
/// </summary>
internal static class CrashStore
{
    private const int MaxChecksPerRun = 20;
    private const int MaxRecords = 500;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30); // the logs roll over; older ones can't be checked

    /// <summary>Checks new sessions and returns the updated list (saved if it changed). Off the UI thread: it reads the logs.</summary>
    public static List<CrashRecord> Update(IReadOnlyList<CrashRecord> known, IReadOnlyList<Session> sessions, IReadOnlyList<Game> games)
    {
        var records = known.ToList();
        var checkedKeys = records.Select(Key).ToHashSet();
        var since = DateTimeOffset.Now - MaxAge;
        var todo = sessions
            .Where(s => s.End > since && CrashAnalysis.NeedsCheck(s) && !checkedKeys.Contains(Key(s)))
            .OrderByDescending(s => s.End)
            .Take(MaxChecksPerRun)
            .ToList();
        if (todo.Count == 0) return records;

        foreach (var session in todo)
        {
            var game = games.FirstOrDefault(g => g.Id == session.GameId);
            var folder = game is null ? null : GameActions.InstallFolder(game);
            var events = CrashLog.Read(session.Start - TimeSpan.FromSeconds(5), session.End + CrashAnalysis.EventSlack);
            records.Add(CrashAnalysis.Analyze(session, folder, events));
        }
        records = records.OrderBy(r => r.End).TakeLast(MaxRecords).ToList();
        Save(records);
        return records;
    }

    private static readonly Lock SaveLock = new();

    /// <summary>The background check and a dismissal on the UI thread may save at once: one at a time.</summary>
    private static void Save(List<CrashRecord> records)
    {
        lock (SaveLock)
        {
            try { Storage.SaveCrashes(records); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // checked again next run
        }
    }

    public static CrashRecord? For(IReadOnlyList<CrashRecord> records, Session s) => CrashAnalysis.For(records, s);

    /// <summary>
    /// The newest problem whose card hasn't been seen yet (last 7 days), unless that game was played again since.
    /// </summary>
    public static CrashRecord? Unseen(IReadOnlyList<CrashRecord> records, IReadOnlyList<Session> sessions)
    {
        var since = DateTimeOffset.Now.AddDays(-7);
        var candidates = records.Where(r => r.IsProblem && !r.Seen && r.End > since).ToList();
        if (candidates.Count == 0) return null; // the usual case: no pass over the sessions at all
        // Each game's latest session start, in one pass, instead of searching the sessions per record.
        var latest = new Dictionary<string, DateTimeOffset>();
        foreach (var s in sessions)
            if (!latest.TryGetValue(s.GameId, out var t) || s.Start > t) latest[s.GameId] = s.Start;
        return candidates.Where(r => !(latest.TryGetValue(r.GameId, out var t) && t > r.Start)).MaxBy(r => r.End);
    }

    public static void MarkSeen(List<CrashRecord> records, CrashRecord record)
    {
        if (record.Seen) return;
        record.Seen = true;
        Save(records);
    }

    private static (string, long) Key(Session s) => (s.GameId, s.Start.UtcTicks);
    private static (string, long) Key(CrashRecord r) => (r.GameId, r.Start.UtcTicks);
}
