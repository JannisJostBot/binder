using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Binder.Views;

/// <summary>
/// What to do when a window closes, run once, after which the handler removes itself. A handler left on a Window's
/// own event that reaches back to the window (a lambda that names it, or the window's own method) keeps the closed
/// window alive, with everything it showed: WinUI's Window doesn't let the GC see through that cycle (measured with
/// the panel, ~20 MB per close). The same goes for an x:Bind in a Window's XAML, which adds a handler to Activated.
/// </summary>
internal static class WindowClosed
{
    public static void Once(Window window, Action onClosed)
    {
        TypedEventHandler<object, WindowEventArgs>? handler = null;
        handler = (sender, _) =>
        {
            ((Window)sender).Closed -= handler;
            onClosed();
        };
        window.Closed += handler;
    }
}
