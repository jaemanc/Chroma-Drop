// Palette.cs — 색상 팔레트 생성 유틸 (게임 흐름과 분리된 순수 함수).
// 색상환 균등분할 + 지터로 서로 구분되는 n색을 만들고 HSL→RGB로 변환한다.

using UnityEngine;

public static class Palette
{
    /// <summary>색상환을 n등분하고 살짝 지터를 주어 서로 구분되는 팔레트를 생성.</summary>
    public static Color[] Generate(int n, System.Random r)
    {
        var outp = new Color[n];
        double baseH = r.NextDouble() * 360;
        for (int i = 0; i < n; i++)
        {
            double h = (baseH + i * (360.0 / n) + (r.NextDouble() * 36 - 18)) % 360;
            double sat = 0.68 + r.NextDouble() * 0.17;
            double lit = 0.52 + r.NextDouble() * 0.10;
            outp[i] = HslToRgb(h / 360.0, sat, lit);
        }
        return outp;
    }

    public static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        return new Color((float)r, (float)g, (float)b);
    }

    static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
