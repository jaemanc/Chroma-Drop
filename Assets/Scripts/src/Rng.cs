// Rng.cs — 시드 스트림. 스테이지당 seed 1개에서 용도별 스트림을 갈라 쓴다.
// 한 스트림을 얼마나 소비하든 다른 스트림 결과는 변하지 않는다.

namespace ChromaDrop.Engine
{
    public enum Stream { Topology, Palette, Board, Spawn, Refill }

    /// <summary>결정적 난수. System.Random 에 의존하지 않는다 (런타임 구현 차이 배제).</summary>
    public sealed class Rng
    {
        ulong state;

        public Rng(int seed, Stream stream)
        {
            // 스트림마다 서로 다른 상수로 섞어 독립된 수열을 만든다.
            ulong s = (ulong)(uint)seed;
            ulong k = 0x9E3779B97F4A7C15UL * (ulong)((int)stream + 1);
            state = Mix(s ^ k);
            if (state == 0) state = k | 1UL;
        }

        static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }

        /// <summary>0 이상 max 미만.</summary>
        public int Next(int max)
        {
            if (max <= 1) return 0;
            return (int)(NextULong() % (ulong)max);
        }

        public int Range(int min, int max) { return min + Next(max - min); }

        /// <summary>0 이상 1 미만.</summary>
        public double NextDouble() { return (NextULong() >> 11) * (1.0 / 9007199254740992.0); }

        /// <summary>가중치 추첨. 합이 0 이면 균등.</summary>
        public int Weighted(double[] weights)
        {
            if (weights == null || weights.Length == 0) return 0;
            double total = 0;
            for (int i = 0; i < weights.Length; i++) total += weights[i];
            if (total <= 0) return Next(weights.Length);

            double r = NextDouble() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                r -= weights[i];
                if (r <= 0) return i;
            }
            return weights.Length - 1;
        }
    }
}
