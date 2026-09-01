// Wallet.cs — 코인과 아이템 보유량. PlayerPrefs 에 로컬 저장한다.
// 서버에 올리지 않으므로 기기를 바꾸면 초기화된다. 랭킹 점수와 달리 조작 가치가 낮아
// 지금은 로컬로 충분하다고 봤다.

using UnityEngine;

public enum ShopItem { BombPiece, BigPiece }

/// <summary>타일 겉모습. 규칙에는 영향이 없다.</summary>
public enum TileSkin { Glossy, Gem, Crayon }

public static class Wallet
{
    const string KeyCoins = "wallet_coins";
    const string KeySeeded = "dev_seeded";

    // ⚠ 개발용. 처음 한 번 코인을 넉넉히 넣어 사면서 테스트할 수 있게 한다.
    //    스토어에 올리기 전에 이 정적 생성자와 DevGrant, GameUI.TapCoins 를 제거할 것.
    static Wallet()
    {
        if (PlayerPrefs.GetInt(KeySeeded, 0) != 0) return;
        PlayerPrefs.SetInt(KeySeeded, 1);
        PlayerPrefs.SetInt(KeyCoins, PlayerPrefs.GetInt(KeyCoins, 0) + DevGrant);
        PlayerPrefs.Save();
    }

    public static int Coins
    {
        get { return PlayerPrefs.GetInt(KeyCoins, 0); }
        private set { PlayerPrefs.SetInt(KeyCoins, Mathf.Max(0, value)); PlayerPrefs.Save(); }
    }

    /// <summary>⚠ 개발용. 상점 코인 칩을 5번 두드리면 호출된다.
    /// 실제로 사면서 테스트할 수 있도록 코인을 넉넉히 넣는다.
    /// 스토어에 올리기 전에 이 메서드와 호출부(GameUI.TapCoins)를 제거할 것.</summary>
    public const int DevGrant = 100000;

    public static void AddCoins(int n)
    {
        if (n <= 0) return;
        Coins = Coins + n;
    }

    /// <summary>코인이 모자라면 아무것도 하지 않고 false.</summary>
    public static bool SpendCoins(int n)
    {
        if (n < 0 || Coins < n) return false;
        Coins = Coins - n;
        return true;
    }

    // ---- 타일 스킨 (영구 해금 + 착용) ----
    const string KeySkin = "skin_equipped";

    /// <summary>기본 스킨은 처음부터 갖고 있다.</summary>
    public static bool OwnsSkin(TileSkin s)
    {
        return s == TileSkin.Glossy || PlayerPrefs.GetInt("skin_" + (int)s, 0) == 1;
    }

    public static void UnlockSkin(TileSkin s)
    {
        PlayerPrefs.SetInt("skin_" + (int)s, 1);
        PlayerPrefs.Save();
    }

    /// <summary>착용 중인 스킨. 갖고 있지 않으면 기본으로 돌아간다.</summary>
    public static TileSkin Skin
    {
        get
        {
            var s = (TileSkin)PlayerPrefs.GetInt(KeySkin, 0);
            return OwnsSkin(s) ? s : TileSkin.Glossy;
        }
        set
        {
            if (!OwnsSkin(value)) return;
            PlayerPrefs.SetInt(KeySkin, (int)value);
            PlayerPrefs.Save();
        }
    }

    static string Key(ShopItem it) { return "inv_" + (int)it; }

    public static int Count(ShopItem it) { return PlayerPrefs.GetInt(Key(it), 0); }

    public static void Add(ShopItem it, int n)
    {
        PlayerPrefs.SetInt(Key(it), Mathf.Max(0, Count(it) + n));
        PlayerPrefs.Save();
    }

    /// <summary>하나 써서 소모한다. 없으면 false.</summary>
    public static bool Use(ShopItem it)
    {
        int n = Count(it);
        if (n <= 0) return false;
        PlayerPrefs.SetInt(Key(it), n - 1);
        PlayerPrefs.Save();
        return true;
    }
}
