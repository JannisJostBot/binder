using System.Runtime.InteropServices.WindowsRuntime;
using Binder.Services;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Binder.Views;

internal static class Bitmaps
{
    /// <summary>A UI bitmap from icon pixels (premultiplied BGRA). UI thread only.</summary>
    public static WriteableBitmap FromPixels(IconPixels icon)
    {
        var bitmap = new WriteableBitmap(icon.Width, icon.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(icon.Bgra, 0, Math.Min(icon.Bgra.Length, (int)stream.Length));
        bitmap.Invalidate();
        return bitmap;
    }
}
