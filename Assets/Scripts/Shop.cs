// Shop.cs — 상점 카탈로그.
// 아이템 목록 자체는 스테이지 설정(items.available)이 정한다.
// 여기에는 '무엇을 파는가' 가 아니라 '얼마에 파는가' 만 둔다.

using UnityEngine;

public struct ShopEntry
{
    public string Id;          // 엔진 아이템 id (line / burst / ring / color / cross)
    public string Name;
    public string Desc;
    public int Price;
    public Color Tint;
}

public static class Shop
{
    /// <summary>광고 한 번 보고 받는 코인.</summary>
    public const int AdReward = 50;

    public static readonly ShopEntry[] Items =
    {
        new ShopEntry {
            Id = "line", Name = "LINE", Price = 90,
            Desc = "Clears a whole line along an axis you pick.",
            Tint = Palette.Hex(0x7FCFC0),
        },
        new ShopEntry {
            Id = "burst", Name = "BURST", Price = 120,
            Desc = "Clears everything within a few steps.",
            Tint = Palette.Hex(0xE4795A),
        },
        new ShopEntry {
            Id = "ring", Name = "RING", Price = 110,
            Desc = "Clears a ring at an exact distance.",
            Tint = Palette.Hex(0x8B84D6),
        },
        new ShopEntry {
            Id = "color", Name = "COLOR", Price = 200,
            Desc = "Clears one colour across the region.",
            Tint = Palette.Hex(0xF0C64D),
        },
        new ShopEntry {
            Id = "cross", Name = "CROSS", Price = 180,
            Desc = "Clears every axis at once.",
            Tint = Palette.Hex(0x9B8FE0),
        },
    };

    public static ShopEntry? Find(string id)
    {
        foreach (var e in Items) if (e.Id == id) return e;
        return null;
    }
}
