// Wallet.cs — 코인과 아이템 보유량. PlayerPrefs 에 로컬 저장한다.
// 서버에 올리지 않으므로 기기를 바꾸면 초기화된다. 랭킹 점수와 달리 조작 가치가 낮아
// 지금은 로컬로 충분하다고 봤다.

using UnityEngine;

public enum ShopItem { Reroll, AddTime, ExtraMoves }

public static class Wallet
{
    const string KeyCoins = "wallet_coins";

    public static int Coins
    {
        get { return PlayerPrefs.GetInt(KeyCoins, 0); }
        private set { PlayerPrefs.SetInt(KeyCoins, Mathf.Max(0, value)); PlayerPrefs.Save(); }
    }

    public static void AddCoins(int n)
    {
        if (n <= 0) return;
        Coins = Coins + n;
    }

    /// <summary>코인이 모자라면 아무것도 하지 않고 false.</summary>
    public static bool SpendCoins(int n)
    {
        if (n <= 0 || Coins < n) return false;
        Coins = Coins - n;
        return true;
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
