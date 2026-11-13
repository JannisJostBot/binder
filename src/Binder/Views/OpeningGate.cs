namespace Binder.Views;

/// <summary>
/// Holds a library refresh while the flyout slides in: rebuilding the grid is a big layout pass. Covers don't
/// wait: they appear as they load, from the top down (see CoverQueue). UI thread only.
/// </summary>
public static class OpeningGate
{
    private static TaskCompletionSource? _gate;

    public static bool IsHolding => _gate is not null;

    /// <summary>Completes when the slide has ended (right away when nothing slides).</summary>
    public static Task WhenOpen => _gate?.Task ?? Task.CompletedTask;

    public static void Hold() => _gate ??= new TaskCompletionSource();

    public static void Release()
    {
        var gate = _gate;
        _gate = null;
        gate?.TrySetResult();
    }
}
