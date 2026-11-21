using System.Runtime.InteropServices;

namespace Binder.Watcher;

/// <summary>
/// The sound of the hold (spec §28, board 44, "Rising hum" with "Top note"), for games the ring can't be seen over.
/// A quiet low hum swells in as the ring appears, and each of the ring's four parts adds a voice above it as it starts
/// filling (D2, D3, A3, D4) while a low-pass opens from 280 Hz to 2.4 kHz. Full: the voices stop growing and ring out,
/// and a soft high D (D5) blooms on top. Let go early: the hum sinks away from where it is (<see cref="Cancel"/>).
/// The board's Web Audio numbers, sample by sample, in mono. Rendered in native memory when the ring appears (a few
/// ms, about 220 KB, no managed arrays) and played as one buffer through waveOut (winmm.dll), so letting go fades the
/// samples not yet played in place: no second sound, no switch, no click. Freed when it's over.
/// </summary>
internal static unsafe partial class HoldSound
{
    public const int SampleRate = 22050; // the hum is low-passed under 2.4 kHz and the top note is 587 Hz
    public const double FullAt = EndHold.HoldMs / 1000.0, Part = FullAt / EndHold.Segments, Seconds = FullAt + 2.5;
    /// <summary>Letting go: how far ahead of what's playing the fade starts (what Windows may already have taken), and how long it takes.</summary>
    public const double FadeAhead = 0.08, FadeSeconds = 0.35;

    private readonly record struct Voice(double Frequency, double At, double Level, double Attack, double Harmonic, bool Filtered, double Release);

    // board 44: the hum's voices (one per part of the ring), then the top note when it's full.
    private static readonly Voice[] Voices =
    [
        new(73.42, 0 * Part, 0.14, 0.25, 0.3, true, 0.35),
        new(146.83, 1 * Part, 0.09, 0.25, 0.3, true, 0.35),
        new(220.00, 2 * Part, 0.07, 0.25, 0.3, true, 0.35),
        new(293.66, 3 * Part, 0.06, 0.25, 0.3, true, 0.35),
        new(587.33, FullAt, 0.075, 0.08, 0.1, false, 0.55),
    ];
    // The board's output gain, and Web Audio's equal-power pan of a centred mono voice (each ear gets cos 45°).
    private const double Master = 0.9 * 0.70710678, CutFrom = 280, CutTo = 2400, CutQ = 0.5, EndFade = 0.3;

    /// <summary>Renders the whole hold into <paramref name="into"/>, its last <see cref="EndFade"/> s fading to silence. Pure, unit-tested.</summary>
    internal static void Render(Span<short> into)
    {
        double cut = -1;
        Biquad lp = default;
        int total = into.Length;
        for (int k = 0; k < total; k++)
        {
            double t = (double)k / SampleRate;
            double c = t >= FullAt ? CutTo : CutFrom * Math.Pow(CutTo / CutFrom, t / FullAt);
            if (Math.Abs(c - cut) > 1) { lp.SetLowpass(c, CutQ); cut = c; }
            double hum = 0, top = 0;
            foreach (var v in Voices)
            {
                double s = Tone(v, t);
                if (v.Filtered) hum += s; else top += s;
            }
            double g = Master;
            if (total - k < EndFade * SampleRate) g *= (total - k) / (EndFade * SampleRate);
            into[k] = (short)Math.Clamp(Math.Round((lp.Process(hum) + top) * g * 32767), -32768, 32767);
        }
    }

    /// <summary>
    /// Fades <paramref name="samples"/> out from <paramref name="from"/> over <paramref name="length"/> samples (a
    /// raised cosine: it leaves the level smoothly, so nothing clicks) and silences the rest. Pure, unit-tested.
    /// </summary>
    internal static void FadeOut(Span<short> samples, int from, int length)
    {
        for (int k = Math.Max(from, 0); k < samples.Length; k++)
        {
            int i = k - from;
            samples[k] = i >= length ? (short)0 : (short)Math.Round(samples[k] * 0.5 * (1 + Math.Cos(Math.PI * i / length)));
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

    // ---- Playing (the system events thread starts and fades it; the main thread waits for its end) ------------

    private static readonly Lock Gate = new();
    private static IntPtr _device;
    private static byte* _block; // the WAVEHDR, then the samples: native memory, read by Windows while it plays
    private static int _count;
    private static long _endsAt;

    /// <summary>The ring appeared: the whole hold, its ending included.</summary>
    public static void Start()
    {
        Release();
        int count = (int)(Seconds * SampleRate);
        var block = (byte*)NativeMemory.AllocZeroed((nuint)(sizeof(WAVEHDR) + count * 2));
        var header = (WAVEHDR*)block;
        var samples = (short*)(block + sizeof(WAVEHDR));
        Render(new Span<short>(samples, count));
        var format = new WAVEFORMATEX
        {
            wFormatTag = 1, nChannels = 1, nSamplesPerSec = SampleRate, nAvgBytesPerSec = SampleRate * 2, nBlockAlign = 2, wBitsPerSample = 16,
        };
        IntPtr device;
        if (waveOutOpen(&device, WAVE_MAPPER, &format, 0, 0, CALLBACK_NULL) != 0)
        {
            NativeMemory.Free(block); // no audio device: the ring alone
            return;
        }
        header->lpData = (IntPtr)samples;
        header->dwBufferLength = (uint)(count * 2);
        if (waveOutPrepareHeader(device, header, (uint)sizeof(WAVEHDR)) != 0 || waveOutWrite(device, header, (uint)sizeof(WAVEHDR)) != 0)
        {
            waveOutReset(device);
            waveOutUnprepareHeader(device, header, (uint)sizeof(WAVEHDR));
            waveOutClose(device);
            NativeMemory.Free(block);
            return;
        }
        lock (Gate)
        {
            _device = device;
            _block = block;
            _count = count;
            _endsAt = Environment.TickCount64 + (long)(Seconds * 1000);
        }
    }

    /// <summary>Let go (or nothing was ended): the hum sinks away from where it's playing.</summary>
    public static void Cancel()
    {
        lock (Gate)
        {
            if (_device == IntPtr.Zero) return;
            var time = new MMTIME { wType = TIME_SAMPLES };
            long played = waveOutGetPosition(_device, &time, (uint)sizeof(MMTIME)) == 0 && time.wType == TIME_SAMPLES ? time.u : 0;
            int from = (int)Math.Min(_count, played + (long)(FadeAhead * SampleRate));
            int length = (int)(FadeSeconds * SampleRate);
            FadeOut(new Span<short>(_block + sizeof(WAVEHDR), _count), from, length);
            _endsAt = Math.Min(_endsAt, Environment.TickCount64 + (long)((FadeAhead + FadeSeconds) * 1000) + 50);
        }
    }

    /// <summary>Stops it if it's still playing and frees it.</summary>
    public static void Release()
    {
        lock (Gate)
        {
            if (_device == IntPtr.Zero) return;
            waveOutReset(_device);
            waveOutUnprepareHeader(_device, (WAVEHDR*)_block, (uint)sizeof(WAVEHDR));
            waveOutClose(_device);
            NativeMemory.Free(_block);
            _device = IntPtr.Zero;
            _block = null;
            _count = 0;
        }
    }

    /// <summary>Before the watcher exits: lets the ending ring out (a sleep, at most the sound's length), then frees it.</summary>
    public static void WaitUntilDone()
    {
        long left;
        lock (Gate) left = _device == IntPtr.Zero ? 0 : _endsAt - Environment.TickCount64;
        if (left > 0) Thread.Sleep((int)Math.Min(left, (long)(Seconds * 1000)));
        Release();
    }

    private const uint WAVE_MAPPER = uint.MaxValue, CALLBACK_NULL = 0, TIME_SAMPLES = 2;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength, dwBytesRecorded;
        public nuint dwUser;
        public uint dwFlags, dwLoops;
        public IntPtr lpNext;
        public nuint reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MMTIME
    {
        public uint wType;
        public uint u;
        private uint _rest; // the union's larger members (SMPTE)
    }

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutOpen(IntPtr* device, uint deviceId, WAVEFORMATEX* format, nuint callback, nuint instance, uint flags);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutPrepareHeader(IntPtr device, WAVEHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutWrite(IntPtr device, WAVEHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutGetPosition(IntPtr device, MMTIME* time, uint size);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutReset(IntPtr device);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutUnprepareHeader(IntPtr device, WAVEHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutClose(IntPtr device);
}
