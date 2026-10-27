using System;

namespace ColorMatcher.Core
{
    [Serializable]
    public class HeartState
    {
        public int count = HeartClock.Capacity;
        public long nextUtc;
    }

    // 시간은 UTC 초다. 앱이 꺼져 있던 동안의 충전도 같은 계산을 사용한다.
    public static class HeartClock
    {
        public const int Capacity = 20;
        public const int Interval = 600;

        public static void Recover(HeartState state, long now)
        {
            if (state == null || state.count < 0 || state.count > 30 || state.nextUtc < 0)
                throw new InvalidOperationException("하트 저장 기록이 손상됐습니다.");
            // 이전 최대 30개 저장값은 새 상한으로 옮긴다. 그 밖의 손상값은 위에서 거부한다.
            state.count = Math.Min(state.count, Capacity);
            if (state.count == Capacity) { state.nextUtc = 0; return; }
            if (state.nextUtc == 0) state.nextUtc = now + Interval;
            if (now < state.nextUtc) return;
            long gained = Math.Min(Capacity - state.count, 1 + (now - state.nextUtc) / Interval);
            state.count += (int)gained;
            state.nextUtc = state.count == Capacity ? 0 : state.nextUtc + gained * Interval;
        }

        public static bool Spend(HeartState state, long now)
        {
            Recover(state, now);
            if (state.count == 0) return false;
            if (state.count == Capacity) state.nextUtc = now + Interval;
            state.count--;
            return true;
        }

        public static void Add(HeartState state, int amount, long now)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException("amount");
            Recover(state, now);
            state.count += Math.Min(Capacity - state.count, amount);
            if (state.count == Capacity) state.nextUtc = 0;
        }
    }
}
