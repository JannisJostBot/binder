using System.Runtime.InteropServices;

namespace GameShelf.Services;

/// <summary>
/// The soft chime after Game mode put Windows back: the user's own Windows notification sound (Settings › System ›
/// Sound, so "No sounds" stays silent), at the system sounds volume. Skipped while Windows holds notifications back
/// (full-screen app or game, presentation mode, quiet time).
/// </summary>
internal static partial class Sound
{
    private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_ALIAS = 0x10000, SND_SYSTEM = 0x200000;
    private const int QUNS_ACCEPTS_NOTIFICATIONS = 5;

    public static void PlayRestored()
    {
        if (SHQueryUserNotificationState(out int state) >= 0 && state != QUNS_ACCEPTS_NOTIFICATIONS) return;
        PlaySoundW("Notification.Default", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT | SND_SYSTEM);
    }

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(string sound, IntPtr module, uint flags);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}
