using System;

namespace UnityEngine
{
    public static class Application { public static bool isEditor; }
    public static class Debug
    {
        public static bool isDebugBuild;
        public static void LogWarning(object message) { }
    }
}

public static class TestPayments
{
    public static int Orders;
    public static bool Buy(CoinPack pack) { Orders++; return true; }
}

class ReleaseServicesTests
{
    static void Main()
    {
        CheckPurchase(false, false, false);
        CheckPurchase(true, false, true);
        CheckPurchase(false, true, true);
        Console.WriteLine("출시 구매 차단·개발 구매 유지 검사 통과");
    }

    static void CheckPurchase(bool editor, bool development, bool allowed)
    {
        UnityEngine.Application.isEditor = editor;
        UnityEngine.Debug.isDebugBuild = development;
        int before = TestPayments.Orders, callbacks = 0;
        if (Iap.Ready != allowed) throw new Exception("구매 가능 상태 불일치");
        Iap.Buy(Iap.Packs[0], success => {
            callbacks++;
            if (success != allowed) throw new Exception("구매 결과 불일치");
        });
        if (callbacks != 1 || TestPayments.Orders != before + (allowed ? 1 : 0))
            throw new Exception("출시 주문 생성 또는 중복 콜백");
    }
}
