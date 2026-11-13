using System.Text;

namespace Binder.Library;

/// <summary>
/// Colors and initials for generated covers (design board 2). Pure functions on plain data, so they're
/// unit-tested and give the same result on every run.
/// </summary>
public static class CoverColors
{
    /// <summary>The 8 muted monogram tones. White text on each passes 4.5:1.</summary>
    public static readonly string[] MonogramTones =
    [
        "#4E3A5C", "#2F4F45", "#3A4258", "#5A3B33", "#2C4A5E", "#4A4A2C", "#5A2F4A", "#3B5A33",
    ];

    /// <summary>
    /// The main color of an icon as a muted dark tint: the most frequent saturated hue of a 16×16
    /// downsample, clamped to HSL lightness 22–30 % and saturation ≤ 45 % so white text stays readable.
    /// </summary>
    /// <param name="bgra">Pixels, 4 bytes each (B, G, R, A), premultiplied or straight alpha.</param>
    /// <returns>"#RRGGBB", or null when there are no visible pixels.</returns>
    public static string? IconTint(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return null;

        const int grid = 16, hueBins = 12;
        var binWeight = new double[hueBins];
        var binR = new double[hueBins];
        var binG = new double[hueBins];
        var binB = new double[hueBins];
        double grayR = 0, grayG = 0, grayB = 0, grayWeight = 0;

        for (int gy = 0; gy < grid; gy++)
        for (int gx = 0; gx < grid; gx++)
        {
            // Average the block of source pixels behind this cell of the 16×16 downsample.
            int x0 = gx * width / grid, x1 = Math.Max(x0 + 1, (gx + 1) * width / grid);
            int y0 = gy * height / grid, y1 = Math.Max(y0 + 1, (gy + 1) * height / grid);
            double r = 0, g = 0, b = 0, a = 0;
            for (int y = y0; y < y1 && y < height; y++)
            for (int x = x0; x < x1 && x < width; x++)
            {
                int i = (y * width + x) * 4;
                double alpha = bgra[i + 3] / 255.0;
                if (alpha < 0.5) continue; // mostly transparent: not part of the icon's color
                // Treat as straight alpha; for premultiplied input this slightly darkens edges only.
                b += bgra[i]; g += bgra[i + 1]; r += bgra[i + 2]; a += 1;
            }
            if (a == 0) continue;
            r /= a; g /= a; b /= a;

            var (h, s, l) = ToHsl(r / 255, g / 255, b / 255);
            if (s >= 0.25 && l is > 0.15 and < 0.9)
            {
                int bin = (int)(h * hueBins) % hueBins;
                double weight = a * s; // more saturated cells count more
                binWeight[bin] += weight;
                binR[bin] += r * weight; binG[bin] += g * weight; binB[bin] += b * weight;
            }
            else
            {
                grayR += r * a; grayG += g * a; grayB += b * a; grayWeight += a;
            }
        }

        int best = Array.IndexOf(binWeight, binWeight.Max());
        double cr, cg, cb;
        if (binWeight[best] > 0)
        {
            cr = binR[best] / binWeight[best]; cg = binG[best] / binWeight[best]; cb = binB[best] / binWeight[best];
        }
        else if (grayWeight > 0)
        {
            cr = grayR / grayWeight; cg = grayG / grayWeight; cb = grayB / grayWeight;
        }
        else
        {
            return null;
        }

        var (hue, sat, light) = ToHsl(cr / 255, cg / 255, cb / 255);
        sat = Math.Min(sat, 0.45);
        light = Math.Clamp(light, 0.22, 0.30);
        var (fr, fg, fb) = FromHsl(hue, sat, light);
        return $"#{(int)Math.Round(fr * 255):X2}{(int)Math.Round(fg * 255):X2}{(int)Math.Round(fb * 255):X2}";
    }

    /// <summary>First letters of the first two words, skipping a leading "The": "The Lantern Tide" → "LT".</summary>
    public static string Initials(string title)
    {
        var words = title.Split([' ', '-', '_', ':', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .ToList();
        if (words.Count > 1 && words[0].Equals("The", StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
        var initials = new StringBuilder();
        foreach (var word in words.Take(2)) initials.Append(char.ToUpper(word[0]));
        return initials.Length > 0 ? initials.ToString() : "?";
    }

    /// <summary>A monogram tone picked by a stable hash of the title (string.GetHashCode changes per run).</summary>
    public static string MonogramTone(string title)
    {
        uint hash = 2166136261; // FNV-1a
        foreach (char c in title.ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }
        return MonogramTones[hash % (uint)MonogramTones.Length];
    }

    private static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    private static (double R, double G, double B) FromHsl(double h, double s, double l)
    {
        if (s < 1e-9) return (l, l, l);
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        return (Hue(p, q, h + 1.0 / 3), Hue(p, q, h), Hue(p, q, h - 1.0 / 3));
    }
}
