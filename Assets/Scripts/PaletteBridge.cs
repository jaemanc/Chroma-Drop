// PaletteBridge.cs — 엔진이 만든 색(Rgb)을 유니티 Color 로 옮긴다.
// 색은 절차 생성이므로 여기에도 색 리터럴을 두지 않는다.

using ChromaDrop.Engine;
using UnityEngine;

public static class PaletteBridge
{
    public static Color[] ToUnity(PaletteResult p)
    {
        if (p == null || p.Colors == null) return new Color[0];
        var outp = new Color[p.Colors.Length];
        for (int i = 0; i < p.Colors.Length; i++)
            outp[i] = new Color((float)p.Colors[i].R, (float)p.Colors[i].G, (float)p.Colors[i].B);
        return outp;
    }

    public static Rgb FromUnity(Color c) { return new Rgb(c.r, c.g, c.b); }
}
