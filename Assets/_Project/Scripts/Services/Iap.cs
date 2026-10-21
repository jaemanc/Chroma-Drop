// Iap.cs — 코인 결제 상품 목록과 구매 진입점. 지금은 스토어 SDK 가 없어 자리만 잡아 둔다.
//
// 상품 ID·가격을 바꾸는 곳은 이 파일 하나다. Unity IAP 를 넣은 뒤
// 실제 결제는 스토어 SDK와 서버 검증을 연결한 뒤에 활성화한다.
// 가격은 스토어(App Store Connect / Play Console)에 등록한 값이 진짜다 — 여기 원화는 표시용이다.

using System;
using UnityEngine;

public struct CoinPack
{
    public string ProductId;   // 스토어에 등록할 상품 ID
    public int Coins;
    public int Won;            // 표시용 가격 (원)
}

public static class Iap
{
    /// <summary>가장 싼 상품. 이보다 싼 상품은 만들지 않는다.</summary>
    public const int MinWon = 2000;

    // 코인 양은 미검증 추정치다. 아이템이 60~180 코인, 스킨이 300 코인이고
    // 한 판에서 버는 코인이 수십 개라는 것을 기준으로 잡았다.
    public static readonly CoinPack[] Packs =
    {
        new CoinPack { ProductId = "chromadrop.coins.300",  Coins = 300,  Won = 2000 },
        new CoinPack { ProductId = "chromadrop.coins.850",  Coins = 850,  Won = 5000 },
        new CoinPack { ProductId = "chromadrop.coins.1800", Coins = 1800, Won = 10000 },
    };

    /// <summary>현재 구매 흐름을 사용할 수 있는가. 지금은 더미 모드만 지원한다.</summary>
    public static bool Ready
    {
        get { return TestServices.Enabled; }
    }

    /// <summary>코인을 산다. done(true) 면 결제가 끝나 코인이 들어간 것이다.
    /// 테스트 모드에서는 로컬 주문과 지급을 함께 기록하며 실제 청구는 없다.</summary>
    public static void Buy(CoinPack pack, Action<bool> done)
    {
        if (TestServices.Enabled)
        {
            bool ok = TestPayments.Buy(pack);
            if (done != null) done(ok);
            return;
        }
#if ChromaDrop_IAP
        // TODO: UnityEngine.Purchasing 으로 pack.ProductId 구매 → 영수증 검증 → Wallet.AddCoins.
        Debug.LogWarning("[iap] ChromaDrop_IAP 가 켜져 있지만 SDK 연결 코드가 비어 있다.");
        if (done != null) done(false);
#else
        Debug.LogWarning("[iap] 결제 SDK 가 없다: " + pack.ProductId);
        if (done != null) done(false);
#endif
    }
}
