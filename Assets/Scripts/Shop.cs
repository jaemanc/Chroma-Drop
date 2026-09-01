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

    public static readonly ShopEntry[] Items =
    {
        new ShopEntry {
            Item = ShopItem.Reroll, Name = "REROLL", Price = 30,
            Desc = "Swap the current piece. Costs no move.",
            Tint = Palette.Hex(0x8B84D6),
        },
        new ShopEntry {
            Item = ShopItem.AddTime, Name = "+5 SEC", Price = 20,
            Desc = "Add 5 seconds to the clock.",
            Tint = Palette.Hex(0x7FCFC0),
        },
        new ShopEntry {
            Item = ShopItem.ExtraMoves, Name = "+3 MOVES", Price = 60,
            Desc = "Three more chances. Moves mode only.",
            Tint = Palette.Hex(0xE4C05A), MovesOnly = true,
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
        new SkinEntry { Skin = TileSkin.Glossy, Name = "GLOSSY", Price = 0 },
        new SkinEntry { Skin = TileSkin.Gem,    Name = "GEM",    Price = 300 },
        new SkinEntry { Skin = TileSkin.Crayon, Name = "CRAYON", Price = 300 },
    };

    public static ShopEntry Get(ShopItem it)
    {
        foreach (var e in Items) if (e.Item == it) return e;
        return Items[0];
    }
}
