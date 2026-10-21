// Shop.cs — 상점 카탈로그. 아이템 정의를 한곳에 모은다.
//
// 고른 기준: 기존 규칙만 재사용해서 새 게임 로직을 만들지 않는 것.
// 장애물 부수기·폭탄 설치 같은 건 보드를 직접 건드려야 해서 코어 수술이 필요하다.

using UnityEngine;

public struct ShopEntry
{
    public ShopItem Item;
    public string Name;
    public string Desc;
    public int Price;
    public Color Tint;
    public bool MovesOnly;     // 횟수 모드에서만 쓸 수 있는가
}

public static class Shop
{
    /// <summary>광고 한 번 보고 받는 코인.</summary>
    public const int AdReward = 50;

    // ---- 판이 끝날 때 주는 코인 ----
    // 아이템이 60~180, 스킨이 300 코인이다. 한 판에 아이템 하나를 바로 살 만큼 주면
    // 상점이 의미가 없어지므로 몇 판 모아야 하나를 사는 정도로 잡았다. 미검증 추정치.
    public const int ScorePerCoin = 400;    // 점수 400 당 1코인
    public const int BlocksPerCoin = 60;    // 부순 블록 60개당 1코인
    public const int MaxCoinsPerGame = 40;  // 한 판 상한

    /// <summary>판이 끝날 때 주는 코인. 점수와 부순 블록 수를 함께 본다 (버림, 상한 있음).</summary>
    public static int CoinsFor(int score, int blocksBroken)
    {
        if (score <= 0 && blocksBroken <= 0) return 0;
        int n = Mathf.Max(0, score) / ScorePerCoin + Mathf.Max(0, blocksBroken) / BlocksPerCoin;
        return Mathf.Clamp(n, 0, MaxCoinsPerGame);
    }

    public static readonly ShopEntry[] Items =
    {
        new ShopEntry {
            Item = ShopItem.BombPiece, Name = "BOMB", Price = 120,
            Desc = "Swap your piece for a block that blows up 5x5.",
            Tint = Palette.Hex(0xFF9A7C),   // example.html .bomb-button 그라데이션의 중간톤
        },
        new ShopEntry {
            Item = ShopItem.Hammer, Name = "HAMMER", Price = 80,
            Desc = "Smash any one block you tap.",
            Tint = Palette.Hex(0x7FB4FF),
        },
        new ShopEntry {
            Item = ShopItem.Rainbow, Name = "RAINBOW", Price = 180,
            Desc = "Clear every block of the color you tap.",
            Tint = Palette.Hex(0xC98BEA),
        },
        new ShopEntry {
            Item = ShopItem.Shuffle, Name = "SHUFFLE", Price = 60,
            Desc = "Shuffle the board without using a move.",
            Tint = Palette.Hex(0x62D2A8),
        },
    };

    /// <summary>타일 스킨 — 한 번 사면 계속 쓴다.</summary>
    public struct SkinEntry
    {
        public TileSkin Skin;
        public string Name;
        public int Price;
    }

    public static readonly SkinEntry[] Skins =
    {
        new SkinEntry { Skin = TileSkin.Glossy,  Name = "GLOSSY",  Price = 0 },
        new SkinEntry { Skin = TileSkin.Gem,     Name = "GEM",     Price = 300 },
        new SkinEntry { Skin = TileSkin.Crayon,  Name = "CRAYON",  Price = 300 },
        new SkinEntry { Skin = TileSkin.Crystal, Name = "CRYSTAL", Price = 300 },
        new SkinEntry { Skin = TileSkin.Jewel,   Name = "JEWEL",   Price = 300 },
    };

    public static ShopEntry Get(ShopItem it)
    {
        foreach (var e in Items) if (e.Item == it) return e;
        return Items[0];
    }
}
