using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Binder.Services;

// Kept out of the unsafe declaration: async methods can't be in an unsafe context.
internal static partial class ShellImage
{
    // ---- PNG cache (Windows' own codecs) ------------------------------------------------------------------

    public static async Task<IconPixels?> LoadPngAsync(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var file = File.OpenRead(path);
            using var stream = file.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return new IconPixels((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            return null;
        }
    }

    public static async Task SavePngAsync(IconPixels icon, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            using (var file = File.Create(tmp))
            using (var stream = file.AsRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)icon.Width,
                    (uint)icon.Height, 96, 96, icon.Bgra);
                await encoder.FlushAsync();
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException) { }
    }
}
