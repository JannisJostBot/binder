using System.Runtime.InteropServices;

namespace Binder.Watcher;

/// <summary>
/// The sound of the hold (spec §28, board 44, "Rising hum" with "Top note"), for games the ring can't be seen over.
/// A quiet low hum swells in as the ring appears, and each of the ring's four parts adds a voice above it as it starts
/// filling (D2, D3, A3, D4, every half second) while a low-pass opens from 280 Hz to 2.4 kHz. Full: the voices stop
/// growing and ring out, and a soft high D (D5) blooms on top. Let go early: the hum sinks away (<see cref="Cancel"/>).
/// The board's Web Audio numbers, sample by sample, in mono. Made in native memory when it starts (a few ms, about
/// 200 KB, no managed arrays) and played at the system sounds volume with PlaySound (winmm.dll); freed when it's
/// over or replaced.
/// </summary>
internal static unsafe partial class HoldSound
{
    public const int SampleRate = 22050; // the hum is low-passed under 2.4 kHz and the top note is 587 Hz
    public const double Seconds = 4.5, FullAt = 2.0, CancelSeconds = 0.6;

    private readonly record struct Voice(double Frequency, double At, double Level, double Attack, double Harmonic, bool Filtered, double Release);

    // board 44: the hum's voices (one per part of the ring), then the top note when it's full.
    private static readonly Voice[] Voices =
    [
        new(73.42, 0.0, 0.14, 0.25, 0.3, true, 0.35),
        new(146.83, 0.5, 0.09, 0.25, 0.3, true, 0.35),
        new(220.00, 1.0, 0.07, 0.25, 0.3, true, 0.35),
        new(293.66, 1.5, 0.06, 0.25, 0.3, true, 0.35),
        new(587.33, FullAt, 0.075, 0.08, 0.1, false, 0.55),
    ];
    // The board's output gain, and Web Audio's equal-power pan of a centred mono voice (each ear gets cos 45°).
    private const double Master = 0.9 * 0.70710678, CutFrom = 280, CutTo = 2400, CutQ = 0.5;
    private const double EndFade = 0.3, CancelTau = 0.1, Warm = 0.05;

    /// <summary>
    /// Renders <paramref name="into"/>.Length samples starting at <paramref name="from"/> s into the hold. With a
    /// <paramref name="fadeTau"/>, everything fades out from there (the sound when it's let go early); otherwise the
    /// last <see cref="EndFade"/> s of a full-length render fade to silence. Pure, unit-tested.
    /// </summary>
    internal static void Render(double from, double fadeTau, Span<short> into)
    {
        double cut = -1;
        Biquad lp = default;
        int warm = from > 0 ? (int)(Warm * SampleRate) : 0; // the filter settles before the first sample kept
        int total = into.Length;
        for (int k = -warm; k < total; k++)
        {
            double t = from + (double)k / SampleRate;
            double c = t <= 0 ? CutFrom : t >= FullAt ? CutTo : CutFrom * Math.Pow(CutTo / CutFrom, t / FullAt);
            if (Math.Abs(c - cut) > 1) { lp.SetLowpass(c, CutQ); cut = c; }
            double hum = 0, top = 0;
            foreach (var v in Voices)
            {
                double s = Tone(v, t);
                if (v.Filtered) hum += s; else top += s;
            }
            double y = lp.Process(hum) + top;
            if (k < 0) continue;
            double g = Master;
            if (fadeTau > 0) g *= Math.Exp(-(double)k / SampleRate / fadeTau);
            else if (total - k < EndFade * SampleRate) g *= (total - k) / (EndFade * SampleRate);
            into[k] = (short)Math.Clamp(Math.Round(y * g * 32767), -32768, 32767);
        }
    }

    /// <summary>One voice: a sine with a little second harmonic, a linear rise over its attack, held until the ring is
    /// full, then an exponential ring-out (Web Audio's setTargetAtTime). The top note decays right after its rise.</summary>
    private static double Tone(Voice v, double t)
    {
        double d = t - v.At;
        if (d < 0) return 0;
        double env;
        if (d < v.Attack) env = v.Level * d / v.Attack;
        else if (v.Filtered && t < FullAt) env = v.Level;
        else env = v.Level * Math.Exp(-(v.Filtered ? t - FullAt : d - v.Attack) / v.Release);
        if (env < 1e-5) return 0;
        double w = 2 * Math.PI * v.Frequency * t;
        return env * (Math.Sin(w) + v.Harmonic * Math.Sin(2 * w));
    }

    /// <summary>Web Audio's low-pass biquad (Q in dB).</summary>
    private struct Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        public void SetLowpass(double frequency, double qDb)
        {
            double w = 2 * Math.PI * frequency / SampleRate;
            double alpha = Math.Sin(w) / (2 * Math.Pow(10, qDb / 20));
            double cos = Math.Cos(w), a0 = 1 + alpha;
            _b0 = (1 - cos) / 2 / a0;
            _b1 = (1 - cos) / a0;
            _b2 = _b0;
            _a1 = -2 * cos / a0;
            _a2 = (1 - alpha) / a0;
        }

        public double Process(double x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return y;
        }
    }

    // ---- Playing (the system events thread starts and stops it; the main thread waits for its end) -----------

    private static readonly Lock Gate = new();
    private static byte* _playing;
    private static long _endsAt;

    /// <summary>The ring appeared: the whole hold, its ending included.</summary>
    public static void Start() => Play(0, Seconds, 0);

    /// <summary>Let go (or nothing was ended) at <paramref name="progress"/>: the hum from there, sinking away.</summary>
    public static void Cancel(double progress)
    {
        lock (Gate) if (_playing is null) return;
        Play(Math.Clamp(progress, 0, 1) * FullAt * 0.999, CancelSeconds, CancelTau);
    }

    private static void Play(double from, double seconds, double fadeTau)
    {
        int samples = (int)(seconds * SampleRate), size = 44 + samples * 2;
        var buffer = (byte*)NativeMemory.Alloc((nuint)size);
        WriteHeader(new Span<byte>(buffer, 44), samples);
        Render(from, fadeTau, new Span<short>(buffer + 44, samples));
        lock (Gate)
        {
            // Starting a sound stops the one before it, so its buffer is free once this one has started.
            bool started = PlaySoundW(buffer, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT | SND_SYSTEM);
            if (_playing is not null) NativeMemory.Free(_playing);
            _playing = started ? buffer : null;
            if (!started) NativeMemory.Free(buffer);
            _endsAt = Environment.TickCount64 + (long)(seconds * 1000);
        }
    }

    /// <summary>Stops it if it's still playing and frees its buffer.</summary>
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

    /// <summary>Before the watcher exits: lets the ending ring out (a sleep, at most the sound's length), then frees it.</summary>
    public static void WaitUntilDone()
    {
        long left;
        lock (Gate) left = _playing is null ? 0 : _endsAt - Environment.TickCount64;
        if (left > 0) Thread.Sleep((int)Math.Min(left, (long)(Seconds * 1000)));
        Release();
    }

    internal static void WriteHeader(Span<byte> span, int samples)
    {
        int dataSize = samples * 2;
        "RIFF"u8.CopyTo(span);
        BitConverter.TryWriteBytes(span[4..], 36 + dataSize);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BitConverter.TryWriteBytes(span[16..], 16);             // fmt chunk size
        BitConverter.TryWriteBytes(span[20..], (short)1);       // PCM
        BitConverter.TryWriteBytes(span[22..], (short)1);       // mono
        BitConverter.TryWriteBytes(span[24..], SampleRate);
        BitConverter.TryWriteBytes(span[28..], SampleRate * 2); // bytes a second
        BitConverter.TryWriteBytes(span[32..], (short)2);       // block align
        BitConverter.TryWriteBytes(span[34..], (short)16);      // bits per sample
        "data"u8.CopyTo(span[36..]);
        BitConverter.TryWriteBytes(span[40..], dataSize);
    }

    private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4, SND_SYSTEM = 0x200000;

    [LibraryImport("winmm.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(void* sound, IntPtr module, uint flags);
}
