// Wallet.cs — 코인과 아이템 보유량. PlayerPrefs 에 로컬 저장한다.
// 서버에 올리지 않으므로 기기를 바꾸면 초기화된다. 랭킹 점수와 달리 조작 가치가 낮아
// 지금은 로컬로 충분하다고 봤다.

using UnityEngine;

public enum ShopItem { BombPiece, Hammer, Rainbow, Shuffle }

/// <summary>타일 겉모습. 규칙에는 영향이 없다.</summary>
public enum TileSkin { Glossy, Gem, Crayon, Crystal, Jewel }

public static class Wallet
{
    public static int Coins
    {
        get { return TestPayments.Balance; }
        private set
        {
            TestPayments.Balance = value;
        }
    }

    public static void AddCoins(int n)
    {
        if (n <= 0) return;
        Coins = checked(Coins + n);
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
        return TestPayments.OwnsSkin(s);
    }

    public static void UnlockSkin(TileSkin s)
    {
        TestPayments.UnlockSkin(s);
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

    public static int Count(ShopItem it) { return TestPayments.Count(it); }

    public static bool BuyItem(ShopItem it) { return TestPayments.BuyItem(it); }
    public static bool BuySkin(TileSkin skin) { return TestPayments.BuySkin(skin); }

    public static void Add(ShopItem it, int n)
    {
        TestPayments.AddItem(it, n);
    }

    /// <summary>하나 써서 소모한다. 없으면 false.</summary>
    public static bool Use(ShopItem it)
    {
        return TestPayments.UseItem(it);
    }
}
