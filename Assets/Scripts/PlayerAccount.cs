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

    /// <summary>ISO 3166-1 alpha-2. 사용자가 고른 값이 있으면 그것, 없으면 기기 로케일에서 추정.</summary>
    public static string Country
    {
        get
        {
            var c = PlayerPrefs.GetString(KeyCountry, "");
            return string.IsNullOrEmpty(c) ? DetectCountry() : c;
        }
        set { PlayerPrefs.SetString(KeyCountry, value); PlayerPrefs.Save(); }
    }

    /// <summary>사용자가 직접 고른 적이 있는가 (자동 감지값과 구분).</summary>
    public static bool CountryIsManual { get { return !string.IsNullOrEmpty(PlayerPrefs.GetString(KeyCountry, "")); } }

    /// <summary>기기 지역 설정에서 국가를 추정. 실패하면 언어로, 그것도 실패하면 "ZZ".</summary>
    public static string DetectCountry()
    {
        try
        {
            var r = System.Globalization.RegionInfo.CurrentRegion;
            if (r != null)
            {
                var code = r.TwoLetterISORegionName;
                if (!string.IsNullOrEmpty(code) && code.Length == 2) return code.ToUpperInvariant();
            }
        }
        catch (System.Exception) { /* 일부 플랫폼에서 지역 정보가 없다 */ }

        switch (Application.systemLanguage)
        {
            case SystemLanguage.Korean: return "KR";
            case SystemLanguage.Japanese: return "JP";
            case SystemLanguage.ChineseSimplified: return "CN";
            case SystemLanguage.ChineseTraditional: return "TW";
            case SystemLanguage.English: return "US";
            case SystemLanguage.French: return "FR";
            case SystemLanguage.German: return "DE";
            case SystemLanguage.Spanish: return "ES";
            case SystemLanguage.Portuguese: return "BR";
            case SystemLanguage.Russian: return "RU";
            case SystemLanguage.Italian: return "IT";
            case SystemLanguage.Indonesian: return "ID";
            case SystemLanguage.Thai: return "TH";
            case SystemLanguage.Vietnamese: return "VN";
            case SystemLanguage.Turkish: return "TR";
            default: return "ZZ";
        }
    }

    /// <summary>국가 코드로 고유한 배지 색을 만든다 (국기 이미지 대신 쓰는 2글자 배지용).</summary>
    public static Color BadgeColor(string code)
    {
        if (string.IsNullOrEmpty(code)) code = "ZZ";
        int h = 17;
        foreach (var c in code) h = h * 31 + c;
        float hue = (Mathf.Abs(h) % 360) / 360f;
        return Palette.HslToRgb(hue, 0.62, 0.46);
    }

    /// <summary>국가 선택 목록. 전 세계를 다 넣지 않고 자주 쓰는 곳만 둔다 — 없으면 자동 감지값이 그대로 쓰인다.</summary>
    public static readonly string[] PickList = {
        "KR", "JP", "CN", "TW", "HK", "SG", "TH", "VN", "ID", "MY", "PH", "IN",
        "US", "CA", "MX", "BR", "AR", "CL", "CO", "PE",
        "GB", "FR", "DE", "IT", "ES", "PT", "NL", "BE", "SE", "NO", "FI", "DK",
        "PL", "CZ", "AT", "CH", "IE", "GR", "RO", "HU", "UA", "RU", "TR",
        "AU", "NZ", "ZA", "EG", "NG", "KE", "MA", "SA", "AE", "IL", "PK", "BD", "ZZ",
    };

    static readonly Dictionary<string, string> Names = new Dictionary<string, string>
    {
        { "KR", "Korea" }, { "JP", "Japan" }, { "CN", "China" }, { "TW", "Taiwan" },
        { "US", "USA" }, { "GB", "UK" }, { "FR", "France" }, { "DE", "Germany" },
        { "BR", "Brazil" }, { "IN", "India" }, { "ZZ", "Unknown" },
    };

    /// <summary>표시용 국가명. 목록에 없으면 코드 그대로.</summary>
    public static string DisplayName(string code)
    {
        string n;
        return Names.TryGetValue(code ?? "", out n) ? n : (code ?? "ZZ");
    }
}
