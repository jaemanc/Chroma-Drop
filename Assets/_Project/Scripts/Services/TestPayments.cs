using System;
using System.Collections.Generic;
using UnityEngine;

public enum TestPurchaseResult { Success, Cancelled, Failed }

// 코인·아이템·스킨과 더미 주문을 한 JSON 스냅샷으로 저장한다. Firebase 지급은 아니다.
public static class TestPayments
{
    public const string StorageKey = "chromadrop_test_wallet_v1";
    public static TestPurchaseResult NextResult = TestPurchaseResult.Success;
    public static string Message { get; private set; }

    [Serializable]
    public class Payment
    {
        public string transactionId, productId, store = "dummy", status = "pending", createdAtUtc;
        public string uid = "dummy-local-player", currency = "KRW";
        public int coins, amountWon;
    }

    [Serializable]
    class State
    {
        public int coins;
        public int[] inventory;
        public bool[] skins;
        public List<Payment> pending = new List<Payment>();
        public List<Payment> pay = new List<Payment>();
    }

    static string WalletKey { get { return TestServices.Enabled ? StorageKey : "chromadrop_wallet_v2"; } }

    static State Load()
    {
        var json = PlayerPrefs.GetString(WalletKey, "");
        var state = json.Length == 0 ? new State { coins = PlayerPrefs.GetInt("wallet_coins", 0) } : JsonUtility.FromJson<State>(json);
        if (state == null || state.coins < 0 || state.pay == null || state.pending == null)
            throw new InvalidOperationException("테스트 지갑 기록이 손상됐습니다.");
        // 기존 보유량은 최초 저장 시 함께 옮기고 이후에는 이 스냅샷만 읽는다.
        if (state.inventory == null)
        {
            state.inventory = new int[Enum.GetValues(typeof(ShopItem)).Length];
            for (int i = 0; i < state.inventory.Length; i++) state.inventory[i] = PlayerPrefs.GetInt("inv_" + i, 0);
        }
        if (state.skins == null)
        {
            state.skins = new bool[Enum.GetValues(typeof(TileSkin)).Length];
            for (int i = 0; i < state.skins.Length; i++) state.skins[i] = i == 0 || PlayerPrefs.GetInt("skin_" + i, 0) == 1;
        }
        if (state.inventory.Length != Enum.GetValues(typeof(ShopItem)).Length ||
            state.skins.Length != Enum.GetValues(typeof(TileSkin)).Length || Array.Exists(state.inventory, n => n < 0))
            throw new InvalidOperationException("지갑 보유량 기록이 손상됐습니다.");
        return state;
    }

    static void Save(State state)
    {
        string key = WalletKey;
        bool existed = PlayerPrefs.HasKey(key);
        string before = PlayerPrefs.GetString(key, "");
        try
        {
            PlayerPrefs.SetString(key, JsonUtility.ToJson(state));
            PlayerPrefs.Save();
        }
        catch
        {
            // 저장 실패 시 메모리 캐시만 지급된 상태로 남지 않게 한다.
            if (existed) PlayerPrefs.SetString(key, before); else PlayerPrefs.DeleteKey(key);
            throw;
        }
    }

    public static int Balance
    {
        get { return Load().coins; }
        set { var state = Load(); state.coins = Math.Max(0, value); Save(state); }
    }

    public static int Count(ShopItem item) { return Load().inventory[(int)item]; }
    public static bool OwnsSkin(TileSkin skin)
    {
        return Enum.IsDefined(typeof(TileSkin), skin) && (skin == TileSkin.Glossy || Load().skins[(int)skin]);
    }

    public static void AddItem(ShopItem item, int amount)
    {
        var state = Load();
        state.inventory[(int)item] = Math.Max(0, checked(state.inventory[(int)item] + amount));
        Save(state);
    }

    public static bool UseItem(ShopItem item)
    {
        var state = Load();
        if (state.inventory[(int)item] == 0) return false;
        state.inventory[(int)item]--;
        Save(state);
        return true;
    }

    public static void UnlockSkin(TileSkin skin)
    {
        var state = Load(); state.skins[(int)skin] = true; Save(state);
    }

    public static bool BuyItem(ShopItem item)
    {
        var state = Load();
        foreach (var entry in Shop.Items)
        {
            if (entry.Item != item) continue;
            if (entry.Price < 0 || state.coins < entry.Price || state.inventory[(int)item] == int.MaxValue) return false;
            state.coins -= entry.Price;
            state.inventory[(int)item]++;
            Save(state);
            return true;
        }
        return false;
    }

    public static bool BuySkin(TileSkin skin)
    {
        var state = Load();
        foreach (var entry in Shop.Skins)
        {
            if (entry.Skin != skin) continue;
            if (state.skins[(int)skin]) return true;
            if (entry.Price < 0 || state.coins < entry.Price) return false;
            state.coins -= entry.Price;
            state.skins[(int)skin] = true;
            Save(state);
            return true;
        }
        return false;
    }

    public static int RecordCount { get { return Load().pay.Count; } }
    public static bool HasPending { get { return Load().pending.Count > 0; } }

    public static bool Buy(CoinPack pack)
    {
        try { return BuyCore(pack); }
        catch (Exception)
        {
            Message = "구매 저장 실패 · 지급 완료로 처리하지 않았습니다. 주문 재시도를 확인해 주세요.";
            return false;
        }
    }

    static bool BuyCore(CoinPack pack)
    {
        if (!TestServices.Enabled) return false;
        var outcome = NextResult;
        NextResult = TestPurchaseResult.Success;
        if (outcome == TestPurchaseResult.Cancelled) { Message = "테스트 구매 취소 · 지급 없음"; return false; }
        var state = Load();
        if (state.pending.Count > 0) { Message = "미완료 주문이 있습니다. 주문 재시도를 눌러주세요."; return false; }
        CoinPack? catalog = null;
        foreach (var item in Iap.Packs) if (item.ProductId == pack.ProductId) catalog = item;
        if (!catalog.HasValue) { Message = "등록되지 않은 테스트 상품"; return false; }
        var product = catalog.Value;
        state.pending.Add(new Payment {
            transactionId = Guid.NewGuid().ToString("N"), productId = product.ProductId,
            coins = product.Coins, amountWon = product.Won, createdAtUtc = DateTime.UtcNow.ToString("O")
        });
        Save(state);
        if (outcome == TestPurchaseResult.Failed) { Message = "지급 실패 테스트 · 주문 재시도 가능"; return false; }
        return Fulfill(state, state.pending[0]);
    }

    public static bool Retry()
    {
        try { return RetryCore(); }
        catch (Exception)
        {
            Message = "주문 복구 실패 · 기존 기록을 보존했습니다.";
            return false;
        }
    }

    static bool RetryCore()
    {
        if (!TestServices.Enabled) return false;
        var state = Load();
        var order = state.pending.Count > 0 ? state.pending[0]
            : (state.pay.Count > 0 ? state.pay[state.pay.Count - 1] : null);
        if (order == null) { Message = "재시도할 테스트 주문이 없습니다."; return false; }
        return Fulfill(state, order);
    }

    static bool Fulfill(State state, Payment order)
    {
        if (order == null || string.IsNullOrEmpty(order.transactionId) || order.coins <= 0 ||
            !Array.Exists(Iap.Packs, p => p.ProductId == order.productId && p.Coins == order.coins && p.Won == order.amountWon))
        { Message = "주문 정보 오류 · 기존 기록을 보존했습니다."; return false; }
        if (state.pay.Exists(p => p.transactionId == order.transactionId))
        {
            if (state.pending.RemoveAll(p => p.transactionId == order.transactionId) > 0) Save(state);
            Message = "이미 지급한 주문 · 추가 지급 없음"; return true;
        }
        if (state.coins > int.MaxValue - order.coins) { Message = "지갑 한도 초과 · 지급 없음"; return false; }
        state.coins += order.coins;
        order.status = "granted";
        state.pay.Add(order);
        state.pending.RemoveAll(p => p.transactionId == order.transactionId);
        Save(state);
        Message = "테스트 지급 +" + order.coins + " 코인 · 기록 " + state.pay.Count + "건";
        return true;
    }
}
