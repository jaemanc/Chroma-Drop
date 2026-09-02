// PaletteGen.cs — 절차 팔레트. 코드에 색 리터럴을 두지 않는다.
// 색은 이름이 없고 인덱스 0..size-1 로만 다룬다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public struct Rgb
    {
        public double R, G, B;
        public Rgb(double r, double g, double b) { R = r; G = g; B = b; }
    }

    public sealed class PaletteSpec
    {
        public int Size;
        public double HueSpread;
        public double SatMin, SatMax;
        public double LightMin, LightMax;
        public double MinDeltaE;
        public double MinContrast;
    }

    public sealed class PaletteResult
    {
        public Rgb[] Colors;
        public int Attempts;
        public readonly List<string> Warnings = new List<string>();
        public double MinPairDeltaE;
        public double MinBackgroundContrast;
    }

    public static class PaletteGen
    {
        public const int MaxAttempts = 64;

        public static PaletteResult Generate(PaletteSpec spec, Rgb background, Rng rng)
        {
            var res = new PaletteResult();
            double spread = spec.HueSpread;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                res.Attempts = attempt;
                var cols = new Rgb[spec.Size];
                double baseHue = rng.NextDouble() * 360.0;

                for (int i = 0; i < spec.Size; i++)
                {
                    double h = baseHue + spread * i / spec.Size;
                    double s = spec.SatMin + (spec.SatMax - spec.SatMin) * rng.NextDouble();
                    double l = spec.LightMin + (spec.LightMax - spec.LightMin) * (i / (double)spec.Size);
                    cols[i] = HslToRgb(h % 360.0, s, l);
                }

                double minPair = double.MaxValue, minContrast = double.MaxValue;
                for (int i = 0; i < spec.Size; i++)
                {
                    double c = Contrast(cols[i], background);
                    if (c < minContrast) minContrast = c;
                    for (int j = i + 1; j < spec.Size; j++)
                    {
                        double d = DeltaE(cols[i], cols[j]);
                        if (d < minPair) minPair = d;
                    }
                }

                res.Colors = cols;
                res.MinPairDeltaE = minPair;
                res.MinBackgroundContrast = minContrast;

                if (minPair >= spec.MinDeltaE && minContrast >= spec.MinContrast) break;

                // 실패하면 색상환을 더 벌린다. 상한을 두어 무한 루프를 막는다.
                spread = spread < 360.0 ? spread * 1.15 + 8.0 : 360.0;
                if (spread > 360.0) spread = 360.0;
            }

            if (res.MinPairDeltaE < spec.MinDeltaE)
                res.Warnings.Add("ΔE " + res.MinPairDeltaE.ToString("0.0") + " < " + spec.MinDeltaE);
            if (res.MinBackgroundContrast < spec.MinContrast)
                res.Warnings.Add("대비 " + res.MinBackgroundContrast.ToString("0.00") + " < " + spec.MinContrast);

            CheckColorBlind(res, spec);
            return res;
        }

        /// <summary>적록·청황 색맹 시뮬에서 색이 뭉치면 경고만 남긴다 (실패로 보지 않는다).</summary>
        static void CheckColorBlind(PaletteResult res, PaletteSpec spec)
        {
            string[] kinds = { "적록", "청황" };
            for (int k = 0; k < 2; k++)
            {
                double worst = double.MaxValue;
                for (int i = 0; i < res.Colors.Length; i++)
                    for (int j = i + 1; j < res.Colors.Length; j++)
                    {
                        double d = DeltaE(Simulate(res.Colors[i], k), Simulate(res.Colors[j], k));
                        if (d < worst) worst = d;
                    }
                if (worst < spec.MinDeltaE)
                    res.Warnings.Add(kinds[k] + " 색맹 ΔE " + worst.ToString("0.0") + " < " + spec.MinDeltaE);
            }
        }

        static Rgb Simulate(Rgb c, int kind)
        {
            // 매우 단순한 근사. 정밀 시뮬이 목적이 아니라 '뭉치는지' 만 본다.
            if (kind == 0) { double m = (c.R + c.G) * 0.5; return new Rgb(m, m, c.B); }
            double n = (c.G + c.B) * 0.5; return new Rgb(c.R, n, n);
        }

        // ---------- 색 공간 ----------

        public static Rgb HslToRgb(double h, double s, double l)
        {
            double c = (1 - System.Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - System.Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return new Rgb(r + m, g + m, b + m);
        }

        static double Linear(double v)
        {
            return v <= 0.04045 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        public static double Luminance(Rgb c)
        {
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        public static double Contrast(Rgb a, Rgb b)
        {
            double la = Luminance(a), lb = Luminance(b);
            double hi = la > lb ? la : lb, lo = la > lb ? lb : la;
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>CIE76 ΔE. 정밀도보다 결정성과 단순함을 택했다.</summary>
        public static double DeltaE(Rgb a, Rgb b)
        {
            double l1, a1, b1, l2, a2, b2;
            ToLab(a, out l1, out a1, out b1);
            ToLab(b, out l2, out a2, out b2);
            double dl = l1 - l2, da = a1 - a2, db = b1 - b2;
            return System.Math.Sqrt(dl * dl + da * da + db * db);
        }

        static void ToLab(Rgb c, out double L, out double A, out double B)
        {
            double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
            double x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
            double y = (r * 0.2126 + g * 0.7152 + b * 0.0722);
            double z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;
            x = F(x); y = F(y); z = F(z);
            L = 116 * y - 16;
            A = 500 * (x - y);
            B = 200 * (y - z);
        }

        static double F(double t)
        {
            return t > 0.008856 ? System.Math.Pow(t, 1.0 / 3.0) : (7.787 * t + 16.0 / 116.0);
        }
    }
}
