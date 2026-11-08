using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>
/// A see-through window background, so a window can be shaped by its XAML content alone (the Game mode toast's
/// pill with fully round ends and its own soft shadow). WinUI windows have no redirection surface, so whatever
/// the content leaves transparent shows the desktop. The backdrop brush has to come from Windows.UI.Composition,
/// whose compositor needs a system dispatcher queue on the UI thread; both are made once and kept.
/// </summary>
public sealed partial class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? s_compositor;
    private static IntPtr s_queueController;
    private Windows.UI.Composition.CompositionColorBrush? _brush;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (s_compositor is null)
        {
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null && s_queueController == IntPtr.Zero)
            {
                var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2 /* current */ };
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out s_queueController));
            }
            s_compositor = new Windows.UI.Composition.Compositor();
        }
        _brush = s_compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        connectedTarget.SystemBackdrop = _brush;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
        _brush?.Dispose();
        _brush = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
}
