namespace Binder.Services;

/// <summary>
/// The "back to normal" sound (board 25, "Lullaby", §19 a), made in memory each time it plays: two soft felt-key
/// notes falling a major third (F♯4, then D4 with a low D3 under it), each a sine with a quiet second harmonic and a
/// light shimmer (a copy a fraction of a hertz off, 15 % of the pair), through a low-pass closing from 1.8 to 1 kHz,
/// a short room and a 120 Hz high-pass. 2 s, about −34 LUFS. At night (<see cref="Render"/>'s flag) it's 6 dB quieter
/// and darker. The same signal chain as the board's Web Audio version, sample by sample, so it sounds like what the
/// owner picked. Pure code, no Windows calls: it returns a 16-bit stereo WAV. A few ms of work on a background
/// thread and ~2 MB of short-lived buffers; nothing stays behind.
/// </summary>
internal static class Chime
{
    public const int SampleRate = 22050; // everything is low-passed under 2.5 kHz, so this is plenty
    public const double Seconds = 2.0;

    /// <summary>One voice: a sine (with partials) that swells in with a sin² curve and fades exponentially.</summary>
    private readonly record struct Voice(double Frequency, double Pan, double Level, double At, double Attack,
                                         double Release, double Beat, double Depth, double Harmonic);

    // The board's numbers (board 25, "lullaby").
    private static readonly Voice[] Voices =
    [
        new(146.83, 0, 0.018, 0.25, 0.15, 0.5, 0.4, 0.15, 0.15),     // D3, joins under the second note
        new(369.99, 0.15, 0.022, 0, 0.1, 0.3, 0.5, 0.15, 0.12),      // F♯4
        new(293.66, -0.15, 0.024, 0.25, 0.1, 0.45, 0.45, 0.15, 0.12) // D4
    ];
    private const double Master = 1.135, HighPass = 120, CutFrom = 1800, CutTo = 1000;
    private const double RoomSeconds = 1.2, RoomWet = 0.3, RoomPreDelay = 0.02, RoomCut = 2500;
    private const double FadeSeconds = 0.5;
    private const double BoardRate = 48000; // the room's noise filter was tuned in a browser running at 48 kHz

    // At night (22:00–7:00): 6 dB quieter, every filter 30 % lower, less room, a higher high-pass.
    private const double NightMaster = 0.5, NightDark = 0.7, NightWet = 0.6, NightHighPass = 140;

    /// <summary>Night hours for the sound: 22:00 to 7:00 by the local clock (read once when it plays).</summary>
    public static bool IsNight(DateTime now) => now.Hour >= 22 || now.Hour < 7;

    public static byte[] RenderWav(bool night = false)
    {
        var (left, right) = Render(night);
        return ToWav(left, right);
    }

    public static (float[] Left, float[] Right) Render(bool night = false)
    {
        double dark = night ? NightDark : 1;
        int n = (int)(Seconds * SampleRate);
        var dryL = new float[n];
        var dryR = new float[n];
        foreach (var voice in Voices) Swell(voice, dryL, dryR);

        // The filter every voice goes through: 1800 Hz closing to 1000 Hz over the whole sound.
        var lpL = new Biquad();
        var lpR = new Biquad();
        for (int k = 0; k < n; k++)
        {
            if ((k & 15) == 0)
            {
                double t = (double)k / SampleRate;
                lpL.SetLowpass(dark * CutFrom * Math.Pow(CutTo / CutFrom, Math.Min(1, t / Seconds)), 0.2);
                lpR.CopyCoefficients(lpL);
            }
            dryL[k] = lpL.Process(dryL[k]);
            dryR[k] = lpR.Process(dryR[k]);
        }

        var outL = (float[])dryL.Clone();
        var outR = (float[])dryR.Clone();
        Room(dryL, dryR, outL, outR, RoomCut * dark, RoomWet * (night ? NightWet : 1));

        // Master level with a linear fade over the last half second, then the high-pass.
        var hpL = new Biquad();
        var hpR = new Biquad();
        hpL.SetHighpass(night ? Math.Max(HighPass, NightHighPass) : HighPass, 0.7);
        hpR.CopyCoefficients(hpL);
        double master = Master * (night ? NightMaster : 1);
        int fade = (int)(FadeSeconds * SampleRate);
        for (int k = 0; k < n; k++)
        {
            double g = k < n - fade ? master : master * (n - k) / fade;
            outL[k] = hpL.Process(outL[k] * g);
            outR[k] = hpR.Process(outR[k] * g);
        }
        return (outL, outR);
    }

    /// <summary>One voice: the note and its second harmonic, each a sine plus a copy <c>Beat</c> Hz higher at
    /// <c>Depth</c> of the pair (a slow, light shimmer); a sin² rise over <c>Attack</c>, then an exponential fade
    /// with time constant <c>Release</c> (Web Audio's setTargetAtTime), stopping after 7 of them; panned with Web
    /// Audio's equal-power panner.</summary>
    private static void Swell(Voice v, float[] left, float[] right)
    {
        int start = (int)Math.Round(v.At * SampleRate);
        int length = Math.Min((int)((v.Attack + 7 * v.Release) * SampleRate), left.Length - start);
        double x = (v.Pan + 1) / 2;
        double gl = Math.Cos(x * Math.PI / 2), gr = Math.Sin(x * Math.PI / 2);
        double w = 2 * Math.PI / SampleRate;
        double w1 = w * v.Frequency, w1b = w * (v.Frequency + v.Beat);
        double w2 = w * 2 * v.Frequency, w2b = w * (2 * v.Frequency + v.Beat);
        double still = 1 - v.Depth;
        for (int k = 0; k < length; k++)
        {
            double t = (double)k / SampleRate;
            double env;
            if (t < v.Attack)
            {
                double rise = Math.Sin(Math.PI / 2 * t / v.Attack);
                env = v.Level * rise * rise;
            }
            else env = v.Level * Math.Exp(-(t - v.Attack) / v.Release);
            double s = still * Math.Sin(w1 * k) + v.Depth * Math.Sin(w1b * k)
                       + v.Harmonic * (still * Math.Sin(w2 * k) + v.Depth * Math.Sin(w2b * k));
            s *= env;
            left[start + k] += (float)(s * gl);
            right[start + k] += (float)(s * gr);
        }
    }

    /// <summary>The room: the board's impulse response (noise per ear, fading to -60 dB over 1.2 s and darkening as
    /// it fades), normalized the way Web Audio's ConvolverNode does, 20 ms after the dry sound, through a low-pass.
    /// Convolved with one FFT pair for both ears.</summary>
    private static void Room(float[] inL, float[] inR, float[] outL, float[] outR, double cut, double wet)
    {
        int n = outL.Length;
        int irLength = (int)(RoomSeconds * SampleRate);
        int size = 1;
        while (size < n + irLength) size <<= 1;

        // Input: left in the real part, right in the imaginary part; the response likewise.
        var zr = new float[size];
        var zi = new float[size];
        Array.Copy(inL, zr, n);
        Array.Copy(inR, zi, n);
        var hr = new float[size];
        var hi = new float[size];
        var random = new Lcg(23);
        double power = 0;
        for (int channel = 0; channel < 2; channel++)
        {
            var h = channel == 0 ? hr : hi;
            double y = 0;
            for (int k = 0; k < irLength; k++)
            {
                double t = (double)k / SampleRate;
                double a = 0.25 * Math.Exp(-t * 1.6) + 0.015;
                a = 1 - Math.Pow(1 - a, BoardRate / SampleRate); // the same cutoff in Hz at this rate
                y += a * (random.Next() * 2 - 1 - y);
                double v = y * Math.Exp(-6.9 * t / RoomSeconds) * Math.Min(1, t / 0.08);
                h[k] = (float)v;
                power += v * v;
            }
        }
        // Web Audio normalizes a convolver's response: 1 / RMS, then -58 dB, then 44100 / sample rate.
        power = Math.Max(Math.Sqrt(power / (2.0 * irLength)), 0.000125);
        float scale = (float)(1 / power * 0.00125 * (44100.0 / SampleRate) * wet);

        Fft(zr, zi, inverse: false);
        Fft(hr, hi, inverse: false);
        // Separate the two packed spectra (conjugate symmetry), multiply ear by ear, pack the result again.
        for (int k = 0; k <= size / 2; k++)
        {
            int j = (size - k) & (size - 1);
            var (ak, bk) = Multiply(zr[k], zi[k], zr[j], zi[j], hr[k], hi[k], hr[j], hi[j]);
            var (aj, bj) = Multiply(zr[j], zi[j], zr[k], zi[k], hr[j], hi[j], hr[k], hi[k]);
            zr[k] = (float)(ak.Re - bk.Im); zi[k] = (float)(ak.Im + bk.Re);
            zr[j] = (float)(aj.Re - bj.Im); zi[j] = (float)(aj.Im + bj.Re);
        }
        Fft(zr, zi, inverse: true);

        var cutL = new Biquad();
        var cutR = new Biquad();
        cutL.SetLowpass(cut, 1);
        cutR.SetLowpass(cut, 1);
        int pre = (int)Math.Round(RoomPreDelay * SampleRate);
        for (int k = 0; k + pre < n; k++)
        {
            outL[k + pre] += cutL.Process(zr[k] * scale);
            outR[k + pre] += cutR.Process(zi[k] * scale);
        }
    }

    /// <summary>The board's seeded noise (a 32-bit linear congruential generator), so the room is the same one.</summary>
    private struct Lcg(uint seed)
    {
        private uint _s = seed;

        public double Next()
        {
            _s = unchecked(_s * 1664525 + 1013904223);
            return _s / 4294967296.0;
        }
    }

    private readonly record struct Complex(double Re, double Im);

    /// <summary>From the packed spectra at bins k and N-k: (left input × left response, right × right) at bin k.</summary>
    private static (Complex Left, Complex Right) Multiply(float zr, float zi, float zjr, float zji,
                                                         float hr, float hi, float hjr, float hji)
    {
        // X_L = (Z[k] + conj Z[N-k]) / 2, X_R = (Z[k] - conj Z[N-k]) / 2i.
        double xlr = (zr + zjr) / 2, xli = (zi - zji) / 2;
        double xrr = (zi + zji) / 2, xri = -(zr - zjr) / 2;
        double hlr = (hr + hjr) / 2, hli = (hi - hji) / 2;
        double hrr = (hi + hji) / 2, hri = -(hr - hjr) / 2;
        return (new Complex(xlr * hlr - xli * hli, xlr * hli + xli * hlr),
                new Complex(xrr * hrr - xri * hri, xrr * hri + xri * hrr));
    }

    /// <summary>In-place radix-2 FFT; the inverse is scaled by 1/N.</summary>
    private static void Fft(float[] re, float[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = 2 * Math.PI / length * (inverse ? 1 : -1);
            int half = length >> 1;
            for (int m = 0; m < half; m++)
            {
                double wr = Math.Cos(angle * m), wi = Math.Sin(angle * m);
                for (int i = m; i < n; i += length)
                {
                    int j = i + half;
                    double tr = re[j] * wr - im[j] * wi, ti = re[j] * wi + im[j] * wr;
                    re[j] = (float)(re[i] - tr); im[j] = (float)(im[i] - ti);
                    re[i] = (float)(re[i] + tr); im[i] = (float)(im[i] + ti);
                }
            }
        }
        if (!inverse) return;
        float scale = 1f / n;
        for (int i = 0; i < n; i++) { re[i] *= scale; im[i] *= scale; }
    }

    /// <summary>Web Audio's low-pass and high-pass biquads (Q in dB).</summary>
    private sealed class Biquad
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

        public void SetHighpass(double frequency, double qDb)
        {
            double w = 2 * Math.PI * frequency / SampleRate;
            double alpha = Math.Sin(w) / (2 * Math.Pow(10, qDb / 20));
            double cos = Math.Cos(w), a0 = 1 + alpha;
            _b0 = (1 + cos) / 2 / a0;
            _b1 = -(1 + cos) / a0;
            _b2 = _b0;
            _a1 = -2 * cos / a0;
            _a2 = (1 - alpha) / a0;
        }

        public void CopyCoefficients(Biquad other)
        {
            (_b0, _b1, _b2, _a1, _a2) = (other._b0, other._b1, other._b2, other._a1, other._a2);
        }

        public float Process(double x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return (float)y;
        }
    }

    public static byte[] ToWav(float[] left, float[] right)
    {
        int frames = left.Length, dataSize = frames * 4;
        var wav = new byte[44 + dataSize];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BitConverter.TryWriteBytes(span[4..], 36 + dataSize);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BitConverter.TryWriteBytes(span[16..], 16);           // fmt chunk size
        BitConverter.TryWriteBytes(span[20..], (short)1);     // PCM
        BitConverter.TryWriteBytes(span[22..], (short)2);     // stereo
        BitConverter.TryWriteBytes(span[24..], SampleRate);
        BitConverter.TryWriteBytes(span[28..], SampleRate * 4);
        BitConverter.TryWriteBytes(span[32..], (short)4);     // block align
        BitConverter.TryWriteBytes(span[34..], (short)16);    // bits per sample
        "data"u8.CopyTo(span[36..]);
        BitConverter.TryWriteBytes(span[40..], dataSize);
        for (int k = 0; k < frames; k++)
        {
            BitConverter.TryWriteBytes(span[(44 + k * 4)..], ToPcm(left[k]));
            BitConverter.TryWriteBytes(span[(46 + k * 4)..], ToPcm(right[k]));
        }
        return wav;
    }

    private static short ToPcm(float v) => (short)Math.Clamp(Math.Round(v * 32767.0), -32768, 32767);
}
