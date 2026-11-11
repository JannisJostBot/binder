using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace GameShelf.Setup;

/// <summary>Start menu shortcut, written with the Shell's own ShellLink object.</summary>
internal static class ShellLink
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;

    public static void Create(string linkPath, string target, string workingDir, string description)
    {
        Native.CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED); // S_FALSE if already done: fine
        int hr = Native.CoCreateInstance(ClsidShellLink, IntPtr.Zero, CLSCTX_INPROC_SERVER, IidShellLinkW, out var pointer);
        if (hr < 0) throw new SetupException(Text.ShortcutFailed($"0x{hr:X8}"));
        try
        {
            var link = (IShellLinkW)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
            link.SetPath(target);
            link.SetWorkingDirectory(workingDir);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            ((IPersistFile)link).Save(linkPath, true);
        }
        catch (COMException ex)
        {
            throw new SetupException(Text.ShortcutFailed(ex.Message));
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }
}
