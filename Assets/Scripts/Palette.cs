// Palette.cs — 색상 팔레트 (게임 흐름과 분리된 순수 유틸).
// 판을 시작할 때 아래 고정 세트 중 하나를 시드로 뽑아 쓴다.
// 예전에는 색상환을 균등분할해 매번 새 색을 만들었는데, 조합에 따라 두 색이
// 비슷하게 나오는 경우가 있어 검증된 세트를 박아두는 쪽으로 바꿨다.

using UnityEngine;

public static class Palette
{
    /// <summary>보드 아트에서 뽑은 네 색 — 초록·파랑·핑크·노랑.
    /// 블록 그림과 순서가 같아야 한다 (Resources/tiles/jelly_0..3).
    /// 노랑은 참고 아트의 별 색(H48)을 가져오고, 명도만 눌러 다른 세 색과 같은 띠에 넣었다 —
    /// 원본 밝기(V=1.0)로는 혼자 튄다.</summary>
    public static readonly Color[][] Sets =
    {
        new[] { Hex(0x4FD541), Hex(0x17B8FD), Hex(0xFE62AA), Hex(0xD7B736) },
    };

    public static readonly string[] SetNames = { "보드 아트" };

    // 흰색을 많이 섞으면 타일끼리 서로 뿌예져 경계가 사라진다.
    // 채도는 조금만 빼고 명도를 중간 띠에 묶어 색을 살린다.
    public const float Desaturate = 0.04f;   // 0 = 원색, 1 = 무채색 — 참고 UI 만큼 쨍하게 낮췄다
    public const float WhiteMix   = 0.02f;   // 흰색을 섞는 정도 — 과하면 뿌예진다

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
    /// n 이 세트 길이보다 크면 색이 반복되므로, 게임 색 수는 세트 길이 이하로 유지할 것.
    /// 블록은 아트 그림을 그대로 쓰므로 여기 색은 파티클·고스트 표시에 쓰인다 —
    /// 그림과 같은 색이어야 해서 명도를 누르지 않는다.</summary>
    public static Color[] Generate(int n, System.Random r)
    {
        var set = Sets[r.Next(Sets.Length)];
        var outp = new Color[n];
        for (int i = 0; i < n; i++) outp[i] = set[i % set.Length];
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
