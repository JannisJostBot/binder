namespace GameShelf.Services;

/// <summary>
/// The "back to normal" sound (board 23, sound 1 "Open fourths"), made in memory each time it plays: soft FM keys on
/// A2, D3, G3 and C4 rolled from left to right, a low-pass that closes as they fade, a dark ping-pong echo and a
/// large hall whose tail dissolves over about 6 s. The same signal chain as the board's Web Audio version, sample
/// by sample, so it sounds like what the owner picked. Pure code, no Windows calls: it returns a 16-bit stereo WAV.
/// About 0.1 s of work on a background thread and ~2 MB of short-lived buffers; nothing stays behind.
/// </summary>
internal static class Chime
{
    public const int SampleRate = 22050; // everything is low-passed under 1.4 kHz, so this is plenty
    public const double Seconds = 8.5;

    // The board's numbers.
    private static readonly double[] Notes = [110.0, 146.83, 196.0, 261.63];
    private static readonly double[] Pans = [-0.35, -0.12, 0.12, 0.35];
    private static readonly double[] Levels = [1, 0.9, 0.7, 0.4];
    private const double Roll = 0.065, Attack = 0.03, Decay = 2.6, NoteGain = 0.085, GlideCents = -10;
    private const double CutFrom = 1100, CutTo = 320, CutTime = 2.2;
    private const double EchoSend = 0.1, EchoCut = 500, EchoDelay = 0.29, EchoFeedback = 0.38, EchoPan = 0.7;
    private const double HallSeconds = 6, HallWet = 0.55, HallPreDelay = 0.045, HallCut = 1400;
    private const double Master = 0.6;
    private const double BoardRate = 48000; // the hall's noise filter was tuned in a browser running at 48 kHz

    public static byte[] RenderWav()
    {
        var (left, right) = Render();
        return ToWav(left, right);
    }

    public static (float[] Left, float[] Right) Render()
    {
        int n = (int)(Seconds * SampleRate);
        var dryL = new float[n];
        var dryR = new float[n];
        for (int i = 0; i < Notes.Length; i++)
            Note(Notes[i], i * Roll, Pans[i], NoteGain * Levels[i], dryL, dryR);

        // The filter every note goes through: 1100 Hz closing to 320 Hz over 2.2 s.
        var lpL = new Biquad();
        var lpR = new Biquad();
        for (int k = 0; k < n; k++)
        {
            if ((k & 15) == 0)
            {
                double t = (double)k / SampleRate;
                double cut = t < CutTime ? CutFrom * Math.Pow(CutTo / CutFrom, t / CutTime) : CutTo;
                lpL.SetLowpass(cut, 0.2);
                lpR.CopyCoefficients(lpL);
            }
            dryL[k] = lpL.Process(dryL[k]);
            dryR[k] = lpR.Process(dryR[k]);
        }

        var outL = (float[])dryL.Clone();
        var outR = (float[])dryR.Clone();
        Echo(dryL, dryR, outL, outR);
        Hall(dryL, dryR, outL, outR);

        // Master level, and a gentle fade over the last second (the hall is near -60 dB there anyway).
        int fade = SampleRate;
        for (int k = 0; k < n; k++)
        {
            double g = Master;
            if (k > n - fade) g *= 0.5 + 0.5 * Math.Cos(Math.PI * (k - (n - fade)) / fade);
            outL[k] = (float)(outL[k] * g);
            outR[k] = (float)(outR[k] * g);
        }
        return (outL, outR);
    }

    /// <summary>One note: an FM sine whose brightness fades within ~0.1 s plus a slightly detuned sine, gliding up
    /// 10 cents into pitch, a 30 ms onset and an exponential fade, panned (Web Audio's equal-power panner).</summary>
    private static void Note(double f, double at, double pan, double gain, float[] left, float[] right)
    {
        const double fmIndex = 0.45, fmTau = 0.07, glideTau = 0.05, detuneCents = 5;
        double tau = Decay / 4.5;
        int start = (int)Math.Round(at * SampleRate);
        int length = Math.Min((int)((Attack + Decay + 0.4) * SampleRate), left.Length - start);
        double x = (pan + 1) / 2;
        double gl = Math.Cos(x * Math.PI / 2), gr = Math.Sin(x * Math.PI / 2);
        double f0 = f * Math.Pow(2, GlideCents / 1200);
        double detune = Math.Pow(2, detuneCents / 1200);
        double dt = 1.0 / SampleRate, w = 2 * Math.PI * dt;
        double phase1 = 0, phase2 = 0, phaseMod = 0;
        for (int k = 0; k < length; k++)
        {
            double t = k * dt;
            double baseF = f + (f0 - f) * Math.Exp(-t / glideTau);
            double mod = f * fmIndex * Math.Exp(-t / fmTau) * Math.Sin(phaseMod);
            double env = t < Attack ? t / Attack : Math.Exp(-(t - Attack) / tau);
            double s = env * gain * (0.9 * Math.Sin(phase1) + 0.3 * Math.Sin(phase2));
            left[start + k] += (float)(s * gl);
            right[start + k] += (float)(s * gr);
            phaseMod += w * f;
            phase1 += w * (baseF + mod);
            phase2 += w * baseF * detune;
        }
    }

    /// <summary>Ping-pong echo: through a 500 Hz low-pass into a left delay, which feeds a right delay, which feeds
    /// the left one again, each pass at 0.38.</summary>
    private static void Echo(float[] inL, float[] inR, float[] outL, float[] outR)
    {
        int n = inL.Length, d = (int)Math.Round(EchoDelay * SampleRate);
        var dark = new[] { new Biquad(), new Biquad() };
        dark[0].SetLowpass(EchoCut, 1);
        dark[1].SetLowpass(EchoCut, 1);
        var leftIn = new float[2 * n];  // interleaved per channel: [k * 2 + channel]
        var rightIn = new float[2 * n];
        // Web Audio's panner on a stereo signal: towards the left keeps the left and folds part of the right in,
        // towards the right the other way round.
        double xl = -EchoPan + 1, xr = EchoPan;
        double leftGainL = Math.Cos(xl * Math.PI / 2), leftGainR = Math.Sin(xl * Math.PI / 2);
        double rightGainL = Math.Cos(xr * Math.PI / 2), rightGainR = Math.Sin(xr * Math.PI / 2);
        for (int k = 0; k < n; k++)
        {
            float lOutL = k >= d ? leftIn[(k - d) * 2] : 0, lOutR = k >= d ? leftIn[(k - d) * 2 + 1] : 0;
            float rOutL = k >= d ? rightIn[(k - d) * 2] : 0, rOutR = k >= d ? rightIn[(k - d) * 2 + 1] : 0;
            leftIn[k * 2] = dark[0].Process(inL[k] * EchoSend) + (float)(EchoFeedback * rOutL);
            leftIn[k * 2 + 1] = dark[1].Process(inR[k] * EchoSend) + (float)(EchoFeedback * rOutR);
            rightIn[k * 2] = (float)(EchoFeedback * lOutL);
            rightIn[k * 2 + 1] = (float)(EchoFeedback * lOutR);
            outL[k] += (float)(lOutL + lOutR * leftGainL + rOutL * rightGainL);
            outR[k] += (float)(lOutR * leftGainR + rOutR + rOutL * rightGainR);
        }
    }

    /// <summary>The hall: the board's impulse response (noise per ear, fading to -60 dB over 6 s and darkening as
    /// it fades), normalized the way Web Audio's ConvolverNode does, 45 ms after the dry sound, through a 1400 Hz
    /// low-pass. Convolved with one FFT pair for both ears.</summary>
    private static void Hall(float[] inL, float[] inR, float[] outL, float[] outR)
    {
        int n = outL.Length;
        int irLength = (int)(HallSeconds * SampleRate);
        int inputLength = Math.Min(n, (int)((Notes.Length * Roll + Attack + Decay + 0.6) * SampleRate)); // silent after
        int size = 1;
        while (size < inputLength + irLength) size <<= 1;

        // Input: left in the real part, right in the imaginary part; the response likewise.
        var zr = new float[size];
        var zi = new float[size];
        Array.Copy(inL, zr, inputLength);
        Array.Copy(inR, zi, inputLength);
        var hr = new float[size];
        var hi = new float[size];
        var random = new Random(23);
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
                y += a * (random.NextDouble() * 2 - 1 - y);
                double v = y * Math.Exp(-6.9 * t / HallSeconds) * Math.Min(1, t / 0.08);
                h[k] = (float)v;
                power += v * v;
            }
        }
        // Web Audio normalizes a convolver's response: 1 / RMS, then -58 dB, then 44100 / sample rate.
        power = Math.Max(Math.Sqrt(power / (2.0 * irLength)), 0.000125);
        float scale = (float)(1 / power * 0.00125 * (44100.0 / SampleRate) * HallWet);

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
        cutL.SetLowpass(HallCut, 1);
        cutR.SetLowpass(HallCut, 1);
        int pre = (int)Math.Round(HallPreDelay * SampleRate);
        for (int k = 0; k + pre < n; k++)
        {
            outL[k + pre] += cutL.Process(zr[k] * scale);
            outR[k + pre] += cutR.Process(zi[k] * scale);
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

    /// <summary>Web Audio's low-pass biquad (Q in dB).</summary>
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
