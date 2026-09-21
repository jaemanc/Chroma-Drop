// PlayerAccount.cs — 게스트 계정(닉네임/국가) 로컬 보관.
// 서버 uid 는 Leaderboard 가 익명 인증으로 받아오고, 표시용 정보만 여기서 관리한다.

using System.Collections.Generic;
using UnityEngine;

public static class PlayerAccount
{
    const string KeyName = "acct_name";
    const string KeyCountry = "acct_country";

    /// <summary>표시 닉네임. 없으면 임의로 만들어 저장한다.</summary>
    public static string Name
    {
        get
        {
            var n = PlayerPrefs.GetString(KeyName, "");
            if (string.IsNullOrEmpty(n))
            {
                n = "Guest" + Random.Range(1000, 10000);
                PlayerPrefs.SetString(KeyName, n);
                PlayerPrefs.Save();
            }
            return n;
        }
        set { PlayerPrefs.SetString(KeyName, value); PlayerPrefs.Save(); }
    }

    /// <summary>ISO 3166-1 alpha-3 (KOR, USA). 사용자가 고른 값이 있으면 그것, 없으면 기기 로케일에서 추정.</summary>
    public static string Country
    {
        get
        {
            var c = PlayerPrefs.GetString(KeyCountry, "");
            return c.Length == 3 ? c : DetectCountry();   // 예전에 저장된 2글자 값은 버리고 다시 감지한다
        }
        set { PlayerPrefs.SetString(KeyCountry, value); PlayerPrefs.Save(); }
    }

    /// <summary>사용자가 직접 고른 적이 있는가 (자동 감지값과 구분).</summary>
    public static bool CountryIsManual { get { return PlayerPrefs.GetString(KeyCountry, "").Length == 3; } }

    /// <summary>기기 지역 설정에서 국가를 추정. 실패하면 언어로, 그것도 실패하면 "ZZZ".</summary>
    public static string DetectCountry()
    {
        try
        {
            var r = System.Globalization.RegionInfo.CurrentRegion;
            if (r != null)
            {
                var code = r.ThreeLetterISORegionName;
                if (!string.IsNullOrEmpty(code) && code.Length == 3) return code.ToUpperInvariant();
            }
        }
        catch (System.Exception) { /* 일부 플랫폼에서 지역 정보가 없다 */ }

        switch (Application.systemLanguage)
        {
            case SystemLanguage.Korean: return "KOR";
            case SystemLanguage.Japanese: return "JPN";
            case SystemLanguage.ChineseSimplified: return "CHN";
            case SystemLanguage.ChineseTraditional: return "TWN";
            case SystemLanguage.English: return "USA";
            case SystemLanguage.French: return "FRA";
            case SystemLanguage.German: return "DEU";
            case SystemLanguage.Spanish: return "ESP";
            case SystemLanguage.Portuguese: return "BRA";
            case SystemLanguage.Russian: return "RUS";
            case SystemLanguage.Italian: return "ITA";
            case SystemLanguage.Indonesian: return "IDN";
            case SystemLanguage.Thai: return "THA";
            case SystemLanguage.Vietnamese: return "VNM";
            case SystemLanguage.Turkish: return "TUR";
            default: return "ZZZ";
        }
    }

    /// <summary>국가 코드로 고유한 배지 색을 만든다 (국기 그림이 없는 나라를 대신한다).</summary>
    public static Color BadgeColor(string code)
    {
        if (string.IsNullOrEmpty(code)) code = "ZZZ";
        int h = 17;
        foreach (var c in code) h = h * 31 + c;
        float hue = (Mathf.Abs(h) % 360) / 360f;
        return Palette.HslToRgb(hue, 0.62, 0.46);
    }

    // 선택 목록은 따로 두지 않는다 — 국기 폴더에 있는 나라가 곧 고를 수 있는 나라다 (CountryFlags.Codes).

    static readonly Dictionary<string, string> Names = new Dictionary<string, string>
    {
        { "KOR", "Korea" }, { "JPN", "Japan" }, { "CHN", "China" }, { "TWN", "Taiwan" },
        { "USA", "USA" }, { "GBR", "UK" }, { "FRA", "France" }, { "DEU", "Germany" },
        { "BRA", "Brazil" }, { "IND", "India" }, { "ZZZ", "Unknown" },
    };

    /// <summary>표시용 국가명. 목록에 없으면 코드 그대로.</summary>
    public static string DisplayName(string code)
    {
        string n;
        return Names.TryGetValue(code ?? "", out n) ? n : (code ?? "ZZZ");
    }
}
