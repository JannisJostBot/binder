namespace GameShelf.Views;

/// <summary>
/// Covers load a few at a time, the topmost waiting tile first, so the grid fills from the top down instead of
/// every visible tile reading and decoding at once in whatever order the grid created them. A tile that was
/// recycled for another game before its turn is skipped. UI thread only.
/// </summary>
public static class CoverQueue
{
    private const int MaxLoading = 3;

    private sealed record Request(Func<bool> IsStale, TaskCompletionSource<IDisposable?> Turn);

    private static readonly PriorityQueue<Request, (int Order, long Sequence)> Waiting = new();
    private static long _sequence;
    private static int _loading;

    /// <summary>
    /// Waits for a loading slot; dispose the result to free it. Null when the request went stale while waiting.
    /// Lower <paramref name="order"/> goes first (the grid's item index; the detail view and continue card use -1).
    /// </summary>
    public static Task<IDisposable?> WaitAsync(int order, Func<bool> isStale)
    {
        if (_loading < MaxLoading && Waiting.Count == 0)
        {
            _loading++;
            return Task.FromResult<IDisposable?>(new Slot());
        }
        var turn = new TaskCompletionSource<IDisposable?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Waiting.Enqueue(new Request(isStale, turn), (order, _sequence++));
        return turn.Task;
    }

    private static void Next()
    {
        while (_loading < MaxLoading && Waiting.TryDequeue(out var request, out _))
        {
            if (request.IsStale())
            {
                request.Turn.SetResult(null);
                continue;
            }
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
