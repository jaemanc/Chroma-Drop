// Palette.cs — 색상 팔레트 (게임 흐름과 분리된 순수 유틸).
// 판을 시작할 때 아래 고정 세트 중 하나를 시드로 뽑아 쓴다.
// 예전에는 색상환을 균등분할해 매번 새 색을 만들었는데, 조합에 따라 두 색이
// 비슷하게 나오는 경우가 있어 검증된 세트를 박아두는 쪽으로 바꿨다.

using UnityEngine;

public static class Palette
{
    /// <summary>고정 팔레트 세트. 각 세트는 서로 구분되는 4색이다.</summary>
    public static readonly Color[][] Sets =
    {
        // 1. 색맹 안전 — 적록색약에서도 구분되는 조합
        new[] { Hex(0x0072B2), Hex(0xE69F00), Hex(0xCC79A7), Hex(0x009E73) },
        // 2. 선셋 — 웜 2 · 쿨 2
        new[] { Hex(0xFF6B5B), Hex(0xFFC24B), Hex(0x6C5CE7), Hex(0x35D0E0) },
        // 3. 캔디 — 그린 제외
        new[] { Hex(0xFF4D8D), Hex(0xFFD60A), Hex(0x8338EC), Hex(0x3A86FF) },
        // 4. 쿨 아날로거스 + 악센트 1
        new[] { Hex(0x1B4B8F), Hex(0x35B0C4), Hex(0xA06BE0), Hex(0xFF8A00) },
    };

    public static readonly string[] SetNames = { "색맹 안전", "선셋", "캔디", "쿨 아날로거스" };

    // 배경이 숲 파스텔이라 원색 그대로면 블록만 튄다. 회색 쪽으로 당기고 살짝 밝힌다.
    public const float Desaturate = 0.1f;   // 0 = 원색, 1 = 무채색
    public const float Soften     = 0.10f;   // 흰색 쪽으로

    static Color Mute(Color c)
    {
        float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        var m = Color.Lerp(c, new Color(l, l, l), Desaturate);
        return Color.Lerp(m, Color.white, Soften);
    }

    /// <summary>세트를 하나 뽑아 앞에서 n색을 돌려준다.
    /// n 이 세트 길이보다 크면 색이 반복되므로, 게임 색 수는 세트 길이 이하로 유지할 것.</summary>
    public static Color[] Generate(int n, System.Random r)
    {
        var set = Sets[r.Next(Sets.Length)];
        var outp = new Color[n];
        for (int i = 0; i < n; i++) outp[i] = Mute(set[i % set.Length]);
        return outp;
    }

    /// <summary>0xRRGGBB → Color</summary>
    public static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f,
                         ((rgb >> 8) & 0xFF) / 255f,
                         (rgb & 0xFF) / 255f);
    }

    /// <summary>국가 배지처럼 임의의 색이 필요한 곳에서 쓴다.</summary>
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
