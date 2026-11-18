using Binder.Models;

namespace Binder.Library;

/// <summary>
/// Game shortcuts (spec §26, boards 35–37): the panel's shortcut, then a number, plays a game without opening the
/// panel. Numbers 1–9 are the user's (<see cref="Settings.GameKeySlots"/>, number → game id); 0 is always the last
/// game played and isn't stored. Pure logic, unit-tested.
/// </summary>
public static class GameKeys
{
    /// <summary>The number that plays the last game played.</summary>
    public const int LastPlayed = 0;

    /// <summary>How long the game's logo shows before it starts; Esc in that time cancels.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(1.5);

    /// <summary>The numbers a user can give a game, in the order they're listed.</summary>
    public static readonly int[] UserSlots = [1, 2, 3, 4, 5, 6, 7, 8, 9];

    /// <summary>
    /// The number of a virtual key: 0–9 on the number row or the number pad; null for any other key. With Shift held,
    /// Windows hands the number pad's keys over as Insert, End, the arrows and so on (as with NumLock off): those count
    /// too when not <paramref name="extended"/>, since the separate Insert, Home and arrow keys are extended keys.
    /// </summary>
    public static int? DigitOf(uint virtualKey, bool extended = false) => virtualKey switch
    {
        >= 0x30 and <= 0x39 => (int)(virtualKey - 0x30), // VK_0 … VK_9
        >= 0x60 and <= 0x69 => (int)(virtualKey - 0x60), // VK_NUMPAD0 … VK_NUMPAD9
        _ when extended => null,
        0x2D => 0, // VK_INSERT
        0x23 => 1, // VK_END
        0x28 => 2, // VK_DOWN
        0x22 => 3, // VK_NEXT
        0x25 => 4, // VK_LEFT
        0x0C => 5, // VK_CLEAR
        0x27 => 6, // VK_RIGHT
        0x24 => 7, // VK_HOME
        0x26 => 8, // VK_UP
        0x21 => 9, // VK_PRIOR
        _ => null,
    };

    private static string Key(int slot) => ((char)('0' + slot)).ToString();

    /// <summary>The game id on a user number (1–9), or null. Anything else in the settings file is ignored.</summary>
    public static string? IdOn(IReadOnlyDictionary<string, string>? slots, int slot) =>
        slots is not null && slot is >= 1 and <= 9 && slots.TryGetValue(Key(slot), out var id) && id.Length > 0 ? id : null;

    /// <summary>The user number (1–9) a game has, or null.</summary>
    public static int? SlotOf(IReadOnlyDictionary<string, string>? slots, string gameId)
    {
        foreach (int slot in UserSlots)
            if (IdOn(slots, slot) == gameId) return slot;
        return null;
    }

    /// <summary>
    /// Gives <paramref name="gameId"/> the number <paramref name="slot"/> (1–9), or takes its number away (null). A game
    /// has at most one number: it moves from its old one, and a game that had the new number loses it. Returns that
    /// game's id (it has none now), else null.
    /// </summary>
    public static string? Assign(Dictionary<string, string> slots, string gameId, int? slot)
    {
        if (slot is not null and not (>= 1 and <= 9)) throw new ArgumentOutOfRangeException(nameof(slot));
        foreach (var key in slots.Where(p => p.Value == gameId).Select(p => p.Key).ToList()) slots.Remove(key);
        if (slot is not { } n) return null;
        var previous = IdOn(slots, n);
        slots[Key(n)] = gameId;
        return previous;
    }

    /// <summary>
    /// Drops numbers of games that aren't in the library any more, and entries that aren't a number 1–9 (a hand-edited
    /// file). True when something was dropped.
    /// </summary>
    public static bool Prune(Dictionary<string, string> slots, IEnumerable<string> libraryIds)
    {
        var ids = libraryIds as IReadOnlySet<string> ?? new HashSet<string>(libraryIds);
        var drop = slots.Where(p => p.Key.Length != 1 || p.Key[0] is < '1' or > '9' || p.Value is not { Length: > 0 } || !ids.Contains(p.Value))
            .Select(p => p.Key).ToList();
        foreach (var key in drop) slots.Remove(key);
        return drop.Count > 0;
    }

    /// <summary>The game of the newest session among <paramref name="shelf"/>, or null when none was played.</summary>
    public static Game? LastPlayedGame(IReadOnlyList<Game> shelf, IEnumerable<Session> sessions)
    {
        var byId = new Dictionary<string, Game>(shelf.Count);
        foreach (var game in shelf) byId.TryAdd(game.Id, game);
        Game? last = null;
        DateTimeOffset when = DateTimeOffset.MinValue;
        foreach (var s in sessions)
            if (s.End > when && byId.TryGetValue(s.GameId, out var game))
            {
                when = s.End;
                last = game;
            }
        return last;
    }

    /// <summary>
    /// The game a number plays: on 0 the last game played, else the one the user gave it. Only games on
    /// <paramref name="shelf"/> (not hidden, their launcher on); null when there's none.
    /// </summary>
    public static Game? Resolve(int slot, IReadOnlyList<Game> shelf, IReadOnlyDictionary<string, string>? slots, IEnumerable<Session> sessions)
    {
        if (slot == LastPlayed) return LastPlayedGame(shelf, sessions);
        if (IdOn(slots, slot) is not { } id) return null;
        foreach (var game in shelf)
            if (game.Id == id) return game;
        return null;
    }

    /// <summary>The numbers 1–9 that play a game on <paramref name="shelf"/>, in order (the held shortcut's list).</summary>
    public static List<(int Number, Game Game)> Given(IReadOnlyList<Game> shelf, IReadOnlyDictionary<string, string>? slots)
    {
        var given = new List<(int, Game)>();
        if (slots is null || slots.Count == 0) return given;
        var byId = new Dictionary<string, Game>(shelf.Count);
        foreach (var game in shelf) byId.TryAdd(game.Id, game);
        foreach (int number in UserSlots)
            if (IdOn(slots, number) is { } id && byId.TryGetValue(id, out var game)) given.Add((number, game));
        return given;
    }
}
