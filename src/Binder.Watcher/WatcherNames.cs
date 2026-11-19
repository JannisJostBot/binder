using System.Security.Cryptography;
using System.Text;

namespace Binder.Shared;

/// <summary>
/// The names one game's watcher and the widget share. Compiled into the watcher and the widget.
/// </summary>
public static class WatcherNames
{
    private static string Hash(string gameId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gameId)), 0, 16);

    /// <summary>Held by the game's watcher until its session is saved: one watcher per game.</summary>
    public static string Mutex(string gameId) => @"Local\Binder.Watcher." + Hash(gameId);

    /// <summary>
    /// Set by the game's watcher once every process of the game has exited (while it looks for a restart, saves
    /// and brings the widget back); reset when the game comes back. Gone with the watcher.
    /// </summary>
    public static string Ended(string gameId) => @"Local\Binder.Watcher.Ended." + Hash(gameId);

    /// <summary>
    /// The class of each watcher's hidden window (SystemEvents.cs). The widget finds them by it to take Binder's
    /// shortcut back (<see cref="ReleaseKey"/>): during a game the watcher holds it for "End a stuck game".
    /// </summary>
    public const string WindowClass = "Binder.Watcher.SystemEvents";

    /// <summary>
    /// Sent by the widget to a watcher's window (wParam: the widget's process id) when it can't register its
    /// shortcut: the watcher lets go of it and takes it again once that widget has exited.
    /// </summary>
    public const uint ReleaseKey = 0x8000 + 0x42; // WM_APP + 0x42

    /// <summary>
    /// For the widget: true when the game's watcher says the game has ended, false while it runs (or hasn't been
    /// found yet), null when no watcher for it is running.
    /// </summary>
    public static bool? GameEnded(string gameId)
    {
        try
        {
            using var ended = EventWaitHandle.OpenExisting(Ended(gameId));
            return ended.WaitOne(0);
        }
        catch (Exception e) when (e is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
