// BoardRegions.cs — locked 로 갈린 연결 영역. 분단은 버그가 아니라 설계 수단이다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public sealed class BoardRegion
    {
        public readonly List<int> Cells = new List<int>();
        public bool DeadZone;      // minGroupSize 미만 — 영영 소거가 안 되는 영역
        public bool Refillable;    // 스폰 칸을 포함하는가
    }

    public static class BoardRegions
    {
        public static List<BoardRegion> All(GameEngine eng)
        {
            var seen = new bool[eng.Count];
            var list = new List<BoardRegion>();

            for (int i = 0; i < eng.Count; i++)
            {
                if (seen[i] || eng.Get(i) == CellState.Locked) continue;

                var r = new BoardRegion();
                var q = new Queue<int>();
                q.Enqueue(i); seen[i] = true;

                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    r.Cells.Add(cur);
                    if (eng.Topo.Cells[cur].IsSpawn) r.Refillable = true;
                    foreach (int n in eng.Topo.Cells[cur].Neighbors)
                    {
                        if (n < 0 || seen[n]) continue;
                        if (eng.Get(n) == CellState.Locked) continue;
                        seen[n] = true;
                        q.Enqueue(n);
                    }
                }
                r.DeadZone = r.Cells.Count < eng.MinGroupSize;
                list.Add(r);
            }
            list.Sort((a, b) => b.Cells.Count.CompareTo(a.Cells.Count));
            return list;
        }

        /// <summary>사장 영역을 뺀 실제로 쓸 수 있는 칸 수.</summary>
        public static int Usable(GameEngine eng)
        {
            int n = 0;
            foreach (var r in All(eng)) if (!r.DeadZone) n += r.Cells.Count;
            return n;
        }
    }
}
