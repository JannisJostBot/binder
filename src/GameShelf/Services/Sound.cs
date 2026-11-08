using System.Runtime.InteropServices;

namespace GameShelf.Services;

/// <summary>
/// The calm sound after Game mode put Windows back (board 23, <see cref="Chime"/>): made in memory, played at the
/// system sounds volume (so Windows' own sound settings still apply). Skipped while the user is away, presenting
/// or another game holds the screen in exclusive full screen (<see cref="Holds"/>), and logged when it is. PlaySound reads the sound from
/// memory while it plays, so the buffer is native memory that stays until <see cref="Release"/>.
/// </summary>
internal static unsafe partial class Sound
{
    private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4, SND_SYSTEM = 0x200000;
    private const int QUNS_NOT_PRESENT = 1, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    /// <summary>
    /// Whether to stay quiet: only while the user is away or locked, presenting, or a game still holds the screen
    /// in exclusive full screen. "Busy" (any full-screen window) is not enough: right after a game closes Windows
    /// often still reports it, or does for a full-screen launcher or browser, so the sound hardly ever played.
    /// </summary>
    private static bool Holds(int state) => state is QUNS_NOT_PRESENT or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;

    private static string StateName(int state) => state switch
    {
        QUNS_NOT_PRESENT => "away or locked",
        QUNS_RUNNING_D3D_FULL_SCREEN => "a full-screen game",
        _ => "presentation mode",
    };

    /// <summary>A line in the Game mode log, next to the restore it belongs to.</summary>
    private static void Log(string text)
    {
        try
        {
            File.AppendAllText(GameShelf.Shared.GameModeInfo.LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}\r\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static readonly Lock Gate = new();
    private static void* _playing;

    /// <summary>Makes and starts the sound (call it off the UI thread, ~0.1 s of work). Returns how long it plays,
    /// or null when it doesn't (Windows is holding notifications back, or there's no audio device).</summary>
    public static TimeSpan? PlayRestored()
    {
        if (SHQueryUserNotificationState(out int state) >= 0 && Holds(state))
        {
            Log($"Sound: not played, Windows is holding notifications back ({StateName(state)})");
            return null;
        }
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
