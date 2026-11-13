using System.Runtime.InteropServices;

namespace Binder.Services;

/// <summary>Tells Explorer a folder changed, so desktop icons update right away instead of after a delay.</summary>
internal static partial class ShellNotify
{
    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_PATHW = 0x0005;

    public static void DesktopsChanged()
    {
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                 })
        {
            if (!string.IsNullOrEmpty(folder)) SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, folder, IntPtr.Zero);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);
}
