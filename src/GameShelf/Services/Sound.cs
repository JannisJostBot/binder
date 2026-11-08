using System.Runtime.InteropServices;

namespace GameShelf.Services;

/// <summary>
/// The calm sound after Game mode put Windows back (board 23, <see cref="Chime"/>): made in memory, played at the
/// system sounds volume (so Windows' own sound settings still apply). Skipped while Windows holds notifications
/// back (full-screen app or game, presentation mode, Do not disturb, quiet time). PlaySound reads the sound from
/// memory while it plays, so the buffer is native memory that stays until <see cref="Release"/>.
/// </summary>
internal static unsafe partial class Sound
{
    private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4, SND_SYSTEM = 0x200000;
    private const int QUNS_ACCEPTS_NOTIFICATIONS = 5;

    private static readonly Lock Gate = new();
    private static void* _playing;

    /// <summary>Makes and starts the sound (call it off the UI thread, ~0.1 s of work). Returns how long it plays,
    /// or null when it doesn't (Windows is holding notifications back, or there's no audio device).</summary>
    public static TimeSpan? PlayRestored()
    {
        if (SHQueryUserNotificationState(out int state) >= 0 && state != QUNS_ACCEPTS_NOTIFICATIONS) return null;
        var wav = Chime.RenderWav();
        void* buffer = NativeMemory.Alloc((nuint)wav.Length);
        wav.CopyTo(new Span<byte>(buffer, wav.Length));
        lock (Gate)
        {
            bool started = PlaySoundW(buffer, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT | SND_SYSTEM);
            // Starting a sound stops the one before it, so its buffer is free now.
            if (_playing is not null) NativeMemory.Free(_playing);
            _playing = started ? buffer : null;
            if (!started) NativeMemory.Free(buffer);
            return started ? TimeSpan.FromSeconds(Chime.Seconds) : null;
        }
    }

    /// <summary>Stops the sound if it's still playing and frees its buffer.</summary>
    public static void Release()
    {
        lock (Gate)
        {
            if (_playing is null) return;
            PlaySoundW(null, IntPtr.Zero, 0);
            NativeMemory.Free(_playing);
            _playing = null;
        }
    }

    [LibraryImport("winmm.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(void* sound, IntPtr module, uint flags);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}
