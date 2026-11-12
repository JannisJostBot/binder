using GameShelf.Models;

namespace GameShelf.Library;

/// <summary>What a look at a path found: there, certainly gone, or no way to tell (drive unplugged, share offline,
/// no access).</summary>
public enum PathState { Present, Gone, Unknown }

/// <summary>
/// Whether a game's files are still on the PC. Called only on a user action (Play, Details) and during a rescan;
/// one attribute read per game, never a folder walk. A game counts as gone only when its drive is there and its
/// folder or program isn't: an unplugged drive or an offline share never removes anything.
/// </summary>
public static class GameFiles
{
    public static PathState Check(string? path)
    {
        if (string.IsNullOrEmpty(path)) return PathState.Unknown;
        try
        {
            if (!Path.IsPathFullyQualified(path) || Path.GetPathRoot(path) is not { Length: > 0 } root) return PathState.Unknown;
            File.GetAttributes(root); // throws when the drive or share isn't there
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return PathState.Unknown;
        }
        try
        {
            File.GetAttributes(path);
            return PathState.Present;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return PathState.Gone;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return PathState.Unknown;
        }
    }

    /// <summary>
    /// True when the game was uninstalled or deleted: for a manual add or a shortcut its program (else its folder),
    /// for a store game its install folder. False when it's there or can't be told.
    /// </summary>
    public static bool IsGone(Game game)
    {
        if (game.Source is GameSource.Manual or GameSource.Shortcut)
        {
            // ExePath is the program (an added exe) or our stored copy of the shortcut; the shortcut's target is
            // its icon program or, failing that, its folder.
            if (Check(game.ExePath) == PathState.Gone) return true;
            if ((Exe(game.ExePath) ?? Exe(game.IconPath)) is { } program) return Check(program) == PathState.Gone;
        }
        return Check(game.InstallDir) == PathState.Gone;
    }

    private static string? Exe(string? path) =>
        path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
}

/// <summary>
/// The settings of games that left the library (uninstalled, deleted, on a drive that's unplugged), so a game that
/// comes back gets its favorite, launch options, Game mode choice and custom cover again. Kept in
/// <c>removed-games.json</c>, never shown. Only games with settings of their own are kept, the newest
/// <see cref="Max"/>.
/// </summary>
public static class RemovedGames
{
    public const int Max = 200;

    /// <summary>
    /// Adds the dropped games that have settings (replacing an older entry of the same id). Returns the entries
    /// pushed out by the limit, whose custom cover copies the caller deletes.
    /// </summary>
    public static List<Game> Remember(List<Game> removed, IEnumerable<Game> dropped)
    {
        foreach (var game in dropped)
        {
            if (!game.HasUserData) continue;
            removed.RemoveAll(g => g.Id == game.Id);
            removed.Add(game);
        }
        var evicted = new List<Game>();
        if (removed.Count > Max)
        {
            evicted.AddRange(removed.Take(removed.Count - Max));
            removed.RemoveRange(0, removed.Count - Max);
        }
        return evicted;
    }

    /// <summary>A game that shows up again: copies its kept settings onto it and forgets them. True if there were any.</summary>
    public static bool Restore(List<Game> removed, Game game)
    {
        int i = removed.FindIndex(g => g.Id == game.Id);
        if (i < 0) return false;
        var addedAt = game.AddedAt;
        game.CopyUserData(removed[i]);
        game.AddedAt = addedAt; // installed again: it's new on the shelf
        removed.RemoveAt(i);
        return true;
    }
}
