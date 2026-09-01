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

    // 흰색을 많이 섞으면 타일끼리 서로 뿌예져 경계가 사라진다.
    // 채도는 조금만 빼고 명도를 중간 띠에 묶어 색을 살린다.
    public const float Desaturate = 0.12f;   // 0 = 원색, 1 = 무채색
    public const float WhiteMix   = 0.06f;   // 흰색을 섞는 정도 — 과하면 뿌예진다

    // 명도 위계: 배경 75~90% / 보드 서피스 95~100% / 타일 55~70%.
    // 상한 70% 는 타일영역 배경(#EAF6F2, 명도 0.95)과 25% 차이를 지키기 위한 한계다.
    public const float TileLumMin = 0.55f;
    public const float TileLumMax = 0.70f;

    static float Lum(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }

    static Color Mute(Color c)
    {
        float l = Lum(c);
        c = Color.Lerp(c, new Color(l, l, l), Desaturate);
        c = Color.Lerp(c, Color.white, WhiteMix);

        // 명도를 정해진 띠 안으로 밀어 넣는다. 흰/검 쪽으로 섞으므로 색상은 유지된다.
        l = Lum(c);
        float target = Mathf.Clamp(l, TileLumMin, TileLumMax);
        if (target > l && l < 1f) c = Color.Lerp(c, Color.white, (target - l) / (1f - l));
        else if (target < l && l > 0f) c = Color.Lerp(c, Color.black, (l - target) / l);
        return c;
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
