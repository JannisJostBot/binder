using Microsoft.UI.Xaml.Media;

namespace Binder.Views;

/// <summary>
/// Runs callbacks before each frame (CompositionTarget.Rendering) while any are added, through one static handler.
/// WinUI keeps that static event's last handler after it's removed: a window's own method there kept the closed
/// window alive, with its views and covers, until another one subscribed (measured: ~20 MB per closed panel).
/// UI thread only.
/// </summary>
internal static class FrameTicks
{
    private static readonly List<Action> Callbacks = [];

    public static void Add(Action callback)
    {
        if (Callbacks.Count == 0) CompositionTarget.Rendering += OnRendering;
        Callbacks.Add(callback);
    }

    /// <summary>Removes <paramref name="callback"/> if it was added (a method group compares equal to the one added).</summary>
    public static void Remove(Action callback)
    {
        if (!Callbacks.Remove(callback) || Callbacks.Count > 0) return;
        CompositionTarget.Rendering -= OnRendering;
    }

    private static void OnRendering(object? sender, object e)
    {
        foreach (var callback in Callbacks.ToArray()) callback();
    }
}
