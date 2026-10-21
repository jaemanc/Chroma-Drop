using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum UiFontRole { Title, Menu, Body, Numbers, Effects, KoreanAccent }

public static class UiTypography
{
    static UiThemeAssets assets;
    static Font fallback;
    static readonly TMP_FontAsset[] tmp = new TMP_FontAsset[6];

    public static UiThemeAssets Assets
    {
        get { return assets != null ? assets : assets = Resources.Load<UiThemeAssets>("ui-theme"); }
    }

    public static Font FontFor(UiFontRole role)
    {
        var set = Assets;
        Font font = null;
        if (set != null)
        {
            switch (role)
            {
                case UiFontRole.Title: font = set.titleFont; break;
                case UiFontRole.Menu: font = set.menuFont; break;
                case UiFontRole.Numbers: font = set.numberFont; break;
                case UiFontRole.Effects: font = set.effectFont; break;
                case UiFontRole.KoreanAccent: font = set.koreanAccentFont; break;
                default: font = set.bodyFont; break;
            }
            if (font == null) font = set.bodyFont;
        }
        if (font != null) return font;
        if (fallback == null) fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return fallback;
    }

    public static TMP_FontAsset TmpFor(UiFontRole role)
    {
        int index = (int)role;
        var font = tmp[index];
        if (font == null)
        {
            font = TMP_FontAsset.CreateFontAsset(FontFor(role));
            font.name = "ChromaDrop-" + role;
            tmp[index] = font;
        }
        // 라틴 폰트의 강조 문구는 Jua, 본문은 Pretendard로 한글을 표시한다.
        if (role != UiFontRole.Body)
        {
            if (font.fallbackFontAssetTable == null)
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            // 씬 전환으로 대체 폰트만 해제돼도 현재 인스턴스로 다시 연결한다.
            font.fallbackFontAssetTable.Clear();
            if (role == UiFontRole.Title || role == UiFontRole.Menu || role == UiFontRole.Effects)
                font.fallbackFontAssetTable.Add(TmpFor(UiFontRole.KoreanAccent));
            font.fallbackFontAssetTable.Add(TmpFor(UiFontRole.Body));
        }
        return font;
    }

    public static void Apply(Text text, UiFontRole role)
    {
        if (role != UiFontRole.Body)
            foreach (char c in text.text)
                if ((c >= '\u1100' && c <= '\u11ff') || (c >= '\u3130' && c <= '\u318f') || (c >= '\uac00' && c <= '\ud7a3'))
                { role = role == UiFontRole.Numbers ? UiFontRole.Body : UiFontRole.KoreanAccent; break; }
        text.font = FontFor(role);
        text.fontStyle = FontStyle.Normal;
    }

    public static void Apply(TMP_Text text, UiFontRole role)
    {
        text.font = TmpFor(role);
        text.fontStyle = FontStyles.Normal;
    }
}
