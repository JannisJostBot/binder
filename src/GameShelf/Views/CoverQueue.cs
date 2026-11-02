namespace GameShelf.Views;

/// <summary>
/// Covers load a few at a time, the topmost waiting tile first, so the grid fills from the top down instead of
/// every visible tile reading and decoding at once in whatever order the grid created them. A tile that was
/// recycled for another game before its turn is skipped. UI thread only.
/// <para>
/// While the panel opens, only the covers in view load (<see cref="HoldFrom"/>): the window stays hidden until
/// they're drawn (<see cref="VisibleDone"/>), so it appears complete, and the ones below the fold follow once it's
/// on screen (<see cref="ReleaseHold"/>).
/// </para>
/// </summary>
public static class CoverQueue
{
    private const int MaxLoading = 3;

    private sealed record Request(Func<bool> IsStale, TaskCompletionSource<IDisposable?> Turn);

    private static readonly PriorityQueue<Request, (int Order, long Sequence)> Waiting = new();
    private static long _sequence;
    private static int _loading;
    private static int _holdFrom = int.MaxValue;

    /// <summary>
    /// Waits for a loading slot; dispose the result to free it. Null when the request went stale while waiting.
    /// Lower <paramref name="order"/> goes first (the grid's item index; the detail view and continue card use -1).
    /// </summary>
    public static Task<IDisposable?> WaitAsync(int order, Func<bool> isStale)
    {
        if (_loading < MaxLoading && Waiting.Count == 0 && order < _holdFrom)
        {
            _loading++;
            return Task.FromResult<IDisposable?>(new Slot());
        }
        var turn = new TaskCompletionSource<IDisposable?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Waiting.Enqueue(new Request(isStale, turn), (order, _sequence++));
        return turn.Task;
    }

    /// <summary>Covers from grid index <paramref name="order"/> on wait until <see cref="ReleaseHold"/> (the panel is opening).</summary>
    public static void HoldFrom(int order)
    {
        _holdFrom = Math.Max(0, order);
        Next();
    }

    /// <summary>The panel is on screen: load the rest.</summary>
    public static void ReleaseHold()
    {
        if (_holdFrom == int.MaxValue) return;
        _holdFrom = int.MaxValue;
        Next();
    }

    /// <summary>True when nothing in view is still loading or waiting (only held covers, if any, remain).</summary>
    public static bool VisibleDone
    {
        get
        {
            if (_loading > 0) return false;
            // Tiles recycled while waiting no longer count.
            while (Waiting.TryPeek(out var request, out _) && request.IsStale()) Waiting.Dequeue().Turn.SetResult(null);
            if (!Waiting.TryPeek(out _, out var next)) return true;
            return next.Order >= _holdFrom;
        }
    }

    private static void Next()
    {
        while (_loading < MaxLoading && Waiting.TryPeek(out var request, out var priority))
        {
            if (request.IsStale())
            {
                Waiting.Dequeue();
                request.Turn.SetResult(null);
                continue;
            }
            if (priority.Order >= _holdFrom) break; // below the fold while opening
            Waiting.Dequeue();
            _loading++;
            request.Turn.SetResult(new Slot());
        }
    }

    private sealed class Slot : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _loading--;
            Next();
        }
    }
}
