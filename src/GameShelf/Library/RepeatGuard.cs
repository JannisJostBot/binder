namespace GameShelf.Library;

/// <summary>
/// Play pressed again for the game that was just started (a double-click on a cover or on Continue playing, a held
/// Enter): ignored for a few seconds, so the game doesn't start twice and two watchers don't each record the same
/// session. Another game, or the same one after the window, starts normally. Pure logic, unit-tested.
/// </summary>
public sealed class RepeatGuard(TimeSpan window)
{
    private string? _id;
    private long _at;

    /// <summary>True when <paramref name="id"/> was started less than the window ago (<paramref name="now"/> in ms).</summary>
    public bool IsRepeat(string id, long now) => _id == id && now - _at < (long)window.TotalMilliseconds;

    /// <summary>The game started (only after it really did, so a failed start can be tried again at once).</summary>
    public void Started(string id, long now)
    {
        _id = id;
        _at = now;
    }
}
