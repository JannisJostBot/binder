using Binder.Models;

namespace Binder.Library;

/// <summary>What "Welcome back" shows (proposals §13 c, e): the session just played and, if passed, a milestone.</summary>
/// <paramref name="ClosedLine"/>: say once that Binder was closed the whole time (spec §22 d).
public sealed record WelcomeMoment(string GameId, DateTimeOffset SessionStart, TimeSpan Length, TimeSpan Week, int Milestone, bool ClosedLine = false);

/// <summary>
/// "Welcome back" after a session and quiet playtime milestones (proposals §13 c and e). Pure: the caller stores
/// what was shown (<see cref="Settings.WelcomeBackSessionStart"/>, <see cref="Game.MilestoneShownHours"/>).
/// </summary>
public static class Moments
{
    public static readonly int[] MilestoneHours = [10, 50, 100, 250, 500, 1000];

    /// <summary>Shorter sessions get no "Welcome back": the game likely didn't start properly.</summary>
    public static readonly TimeSpan MinSession = TimeSpan.FromMinutes(2);

    /// <summary>The highest milestone a playtime has reached, or 0.</summary>
    public static int Reached(TimeSpan playtime)
    {
        int reached = 0;
        foreach (var hours in MilestoneHours)
            if (playtime.TotalHours >= hours) reached = hours;
        return reached;
    }

    /// <summary>A game's playtime from our own sessions (as in the Detail view).</summary>
    public static TimeSpan Playtime(IEnumerable<Session> sessions, string gameId)
    {
        var total = TimeSpan.Zero;
        foreach (var s in sessions)
            if (s.GameId == gameId && s.End > s.Start) total += s.End - s.Start;
        return total;
    }

    public static Session? LastSession(IReadOnlyList<Session> sessions) =>
        sessions.Count == 0 ? null : sessions.MaxBy(s => s.End);

    /// <summary>
    /// The moment to show, or null: the last session, if it's newer than the one already shown and at least
    /// <see cref="MinSession"/> long. Its milestone is the highest one passed that wasn't shown yet (0 = none).
    /// </summary>
    public static WelcomeMoment? Pick(IReadOnlyList<Session> sessions, DateTimeOffset shownFor, Func<string, int> milestoneShown, DateTime now)
    {
        if (LastSession(sessions) is not { } last || last.Start <= shownFor) return null;
        var length = last.End - last.Start;
        if (length < MinSession) return null;
        int reached = Reached(Playtime(sessions, last.GameId));
        int milestone = reached > milestoneShown(last.GameId) ? reached : 0;
        return new WelcomeMoment(last.GameId, last.Start, length, PlaytimeStats.Week(sessions, now).Total, milestone);
    }

    /// <summary>
    /// The first run on an existing library: the milestones already passed count as shown, so long-time users
    /// aren't greeted with old ones. Returns the start of the last session (nothing to welcome back from yet).
    /// </summary>
    public static DateTimeOffset Seed(IReadOnlyList<Session> sessions, IEnumerable<Game> games)
    {
        var playtime = ShelfOrder.Stats(sessions).Playtime; // one pass over the sessions, not one per game
        foreach (var game in games)
            game.MilestoneShownHours = Math.Max(game.MilestoneShownHours, Reached(playtime.GetValueOrDefault(game.Id)));
        return LastSession(sessions)?.Start ?? DateTimeOffset.MinValue;
    }
}
