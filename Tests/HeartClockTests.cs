using System;
using ColorMatcher.Core;

public static class HeartClockTests
{
    static void Check(bool value) { if (!value) throw new Exception("하트 충전 회귀 검사 실패"); }
    public static void Main()
    {
        var state = new HeartState();
        Check(state.count == 20);
        Check(HeartClock.Spend(state, 1000));
        Check(state.count == 19 && state.nextUtc == 1600);
        HeartClock.Recover(state, 1599); Check(state.count == 19);
        HeartClock.Recover(state, 1600); Check(state.count == 20 && state.nextUtc == 0);
        state = new HeartState { count = 0, nextUtc = 1600 };
        HeartClock.Recover(state, 2800); Check(state.count == 3 && state.nextUtc == 3400);
        Check(HeartClock.Spend(state, 2801)); Check(state.count == 2 && state.nextUtc == 3400);
        HeartClock.Recover(state, 100000); Check(state.count == 20 && state.nextUtc == 0);
        state = new HeartState { count = 0, nextUtc = 1600 };
        Check(!HeartClock.Spend(state, 1000)); Check(state.count == 0 && state.nextUtc == 1600);
        HeartClock.Add(state, 1, 1000); Check(state.count == 1 && state.nextUtc == 1600);
        HeartClock.Add(state, int.MaxValue, 1000); Check(state.count == 20 && state.nextUtc == 0);
        HeartClock.Spend(state, 2000); Check(state.nextUtc == 2600);
        HeartClock.Recover(state, 1900); Check(state.count == 19 && state.nextUtc == 2600);
        state = new HeartState { count = 30, nextUtc = 1600 };
        HeartClock.Recover(state, 1000); Check(state.count == 20 && state.nextUtc == 0);
        state = new HeartState { count = 12, nextUtc = 1600 };
        HeartClock.Recover(state, 1000); Check(state.count == 12 && state.nextUtc == 1600);
        bool rejected = false;
        try { HeartClock.Recover(new HeartState { count = 31 }, 1000); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected);
        Console.WriteLine("하트: 충전 경계·오프라인 충전·상한·소비·광고 보상·시계 역행 검사 통과");
    }
}
