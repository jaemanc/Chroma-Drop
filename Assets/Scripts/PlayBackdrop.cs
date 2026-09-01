// PlayBackdrop.cs — 플레이 화면 배경을 런타임에 그린다 (숲 느낌).
// 하늘 그라디언트 + 해 + 겹친 언덕 + 나무(기둥/수관) + 반짝임. 전부 코드라 이미지 파일이 없다.
//
// 좌표는 레퍼런스(390x844)를 그대로 쓰고 텍스처 크기에 맞춰 배율만 곱한다.

using UnityEngine;

public static class PlayBackdrop
{
    const float RefW = 390f, RefH = 844f;

    // 하늘: 위는 옅은 하늘, 아래로 갈수록 연둣빛
    static readonly Color SkyTop = Palette.Hex(0xE7F1EC);
    static readonly Color SkyMid = Palette.Hex(0xDCEDE0);
    static readonly Color SkyLow = Palette.Hex(0xCBE4CF);

    static readonly Color Sun       = Palette.Hex(0xF6EEBC);
    static readonly Color HillFar   = Palette.Hex(0xB6D9BC);   // 먼 언덕
    static readonly Color HillNear  = Palette.Hex(0x9BCBA6);   // 가까운 언덕
    static readonly Color Trunk     = Palette.Hex(0x8A6A4B);
    static readonly Color LeafLight = Palette.Hex(0x8FC79B);
    static readonly Color LeafMid   = Palette.Hex(0x74B586);
    static readonly Color LeafDark  = Palette.Hex(0x5C9E70);
    static readonly Color Spark     = Palette.Hex(0xE8C35C);

    // 나무: x, 밑동 y, 높이, 수관 반지름
    static readonly float[][] Trees =
    {
        new[] {  46f, 340f, 120f, 62f },
        new[] { 128f, 300f,  88f, 44f },
        new[] { 262f, 320f, 104f, 54f },
        new[] { 344f, 292f,  80f, 40f },
        new[] { 196f, 268f,  64f, 34f },
    };

    public static Sprite Make(int w, int h)
    {
        var tex = new Texture2D(w, h) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        float sx = w / RefW, sy = h / RefH;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // 텍스처는 아래가 0 이므로 레퍼런스(위가 0) 좌표로 뒤집어 쓴다
                float rx = (x + 0.5f) / sx, ry = (h - 1 - y + 0.5f) / sy;
                float t = Mathf.Clamp01(ry / RefH);

                Color c = t < 0.5f ? Color.Lerp(SkyTop, SkyMid, t / 0.5f)
                                   : Color.Lerp(SkyMid, SkyLow, (t - 0.5f) / 0.5f);

                c = Over(c, Sun, 0.75f * Disc(rx, ry, 96f, 78f, 150f));

                // 겹친 언덕 — 아래쪽에 깔려 지평선을 만든다
                c = Over(c, HillFar, 0.85f * Disc(rx, ry, 70f, 470f, 190f));
                c = Over(c, HillFar, 0.85f * Disc(rx, ry, 330f, 452f, 165f));
                c = Over(c, HillNear, 0.90f * Disc(rx, ry, 200f, 560f, 250f));
                c = Over(c, HillNear, 0.90f * Disc(rx, ry, -20f, 600f, 210f));

                // 나무 — 기둥 위에 수관 세 겹
                foreach (var tr in Trees)
                {
                    float tx = tr[0], baseY = tr[1], hgt = tr[2], rad = tr[3];
                    c = Over(c, Trunk, 0.55f * Rect(rx, ry, tx, baseY - hgt * 0.35f, rad * 0.13f, hgt * 0.35f));
                    float topY = baseY - hgt;
                    c = Over(c, LeafDark, 0.85f * Disc(rx, ry, tx, topY + rad * 0.55f, rad));
                    c = Over(c, LeafMid, 0.85f * Disc(rx, ry, tx - rad * 0.38f, topY + rad * 0.10f, rad * 0.74f));
                    c = Over(c, LeafLight, 0.85f * Disc(rx, ry, tx + rad * 0.34f, topY - rad * 0.06f, rad * 0.62f));
                }

                // 반짝임
                c = Over(c, Spark, 0.75f * Star(rx, ry, 30.5f, 210f, 4.5f));
                c = Over(c, Spark, 0.75f * Star(rx, ry, 300f, 150f, 3.5f));
                c = Over(c, Spark, 0.75f * Star(rx, ry, 220f, 96f, 3.5f));

                px[y * w + x] = c;
            }

        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    static Color Over(Color under, Color top, float a)
    {
        return a <= 0f ? under : Color.Lerp(under, top, Mathf.Clamp01(a));
    }

    static float Disc(float x, float y, float cx, float cy, float r)
    {
        return Mathf.Clamp01(r - Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)));
    }

    static float Rect(float x, float y, float cx, float cy, float hw, float hh)
    {
        return Mathf.Clamp01(hw - Mathf.Abs(x - cx)) * Mathf.Clamp01(hh - Mathf.Abs(y - cy));
    }

    /// <summary>4각 별 (아스트로이드) — 반짝임.</summary>
    static float Star(float x, float y, float cx, float cy, float r)
    {
        float dx = Mathf.Abs(x - cx) / r, dy = Mathf.Abs(y - cy) / r;
        if (dx > 1f || dy > 1f) return 0f;
        return Mathf.Clamp01((1.35f - (Mathf.Sqrt(dx) + Mathf.Sqrt(dy))) * 3f);
    }
}
