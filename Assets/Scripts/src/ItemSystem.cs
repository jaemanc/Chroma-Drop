// ItemSystem.cs — 공간 아이템. 가로/세로를 따로 만들지 않는다. line 하나 + 축 인덱스다.
// 반경은 그래프 홉 수다. 좌표 거리를 쓰지 않는다.
// 프리뷰와 실행은 같은 Affected() 를 호출한다 — 둘이 어긋날 수 없다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public enum ItemEffect { Line, Burst, Ring, Color, Cross }
    public enum AxisMode { PlayerChoice, Random, Fixed }

    public sealed class SpatialItem
    {
        public string Id;
        public ItemEffect Effect;
        public AxisMode AxisMode;
        public int FixedAxis;
        public int Radius;                 // burst/ring 홉 수
        public int MaxCells;               // 0 이면 제한 없음
        public string[] BlockedBy;         // 이 칸을 만나면 진행이 멈춘다
        public string[] Damages;           // 이 칸에 손상을 준다
    }

    public sealed class MetaItem
    {
        public string Id;                  // reroll | add_moves | add_time
        public int Amount;
    }

    public static class ItemSystem
    {
        public static bool IsBlockedBy(SpatialItem item, int cellValue)
        {
            if (item.BlockedBy == null) return false;
            string name = NameOf(cellValue);
            if (name == null) return false;
            foreach (var b in item.BlockedBy) if (b == name) return true;
            return false;
        }

        public static bool CanDamage(SpatialItem item, int cellValue)
        {
            if (CellState.IsColor(cellValue)) return true;
            if (item.Damages == null) return false;
            string name = NameOf(cellValue);
            if (name == null) return false;
            foreach (var d in item.Damages) if (d == name) return true;
            return false;
        }

        public static string NameOf(int cell)
        {
            if (cell == CellState.Brick) return "brick";
            if (cell == CellState.Locked) return "locked";
            if (cell == CellState.Frozen) return "frozen";
            return null;
        }

        /// <summary>이 아이템이 실제로 영향을 주는 칸. 프리뷰와 실행이 공유한다.</summary>
        public static List<int> Affected(GameEngine eng, SpatialItem item, int originId, int axis)
        {
            var acc = new List<int>();
            var seen = new HashSet<int>();

            switch (item.Effect)
            {
                case ItemEffect.Line:
                    Walk(eng, item, originId, axis, acc, seen);
                    break;

                case ItemEffect.Cross:
                    for (int a = 0; a < eng.Topo.Axes.Length; a++)
                        Walk(eng, item, originId, a, acc, seen);
                    break;

                case ItemEffect.Burst:
                    Hops(eng, item, originId, acc, seen, item.Radius, false);
                    break;

                case ItemEffect.Ring:
                    Hops(eng, item, originId, acc, seen, item.Radius, true);
                    break;

                case ItemEffect.Color:
                    {
                        int target = eng.Get(originId);
                        if (!CellState.IsColor(target)) break;
                        // 분단된 보드에서는 기준 칸이 속한 영역 안에서만 작동한다
                        var region = RegionOf(eng, originId);
                        foreach (int id in region)
                            if (eng.Get(id) == target && seen.Add(id)) acc.Add(id);
                        break;
                    }
            }

            if (item.MaxCells > 0 && acc.Count > item.MaxCells) acc.RemoveRange(item.MaxCells, acc.Count - item.MaxCells);
            return acc;
        }

        /// <summary>축을 따라 양방향으로 뻗는다. blockedBy 칸에서 멈춘다.</summary>
        static void Walk(GameEngine eng, SpatialItem item, int origin, int axis,
                         List<int> acc, HashSet<int> seen)
        {
            if (seen.Add(origin)) acc.Add(origin);
            for (int dir = 0; dir < 2; dir++)
            {
                int cur = origin;
                int guard = eng.Count;
                while (guard-- > 0)
                {
                    int nxt = eng.Topo.Step(cur, axis, dir == 0);
                    if (nxt < 0) break;
                    if (IsBlockedBy(item, eng.Get(nxt))) break;   // locked 에서 정지
                    if (seen.Add(nxt)) acc.Add(nxt);
                    cur = nxt;
                }
            }
        }

        /// <summary>그래프 홉 수 기반 BFS. ringOnly 면 정확히 radius 홉인 칸만.</summary>
        static void Hops(GameEngine eng, SpatialItem item, int origin,
                         List<int> acc, HashSet<int> seen, int radius, bool ringOnly)
        {
            var dist = new Dictionary<int, int> { { origin, 0 } };
            var q = new Queue<int>();
            q.Enqueue(origin);

            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int d = dist[cur];
                bool take = ringOnly ? d == radius : d <= radius;
                if (take && seen.Add(cur)) acc.Add(cur);
                if (d >= radius) continue;

                foreach (int n in eng.Topo.Cells[cur].Neighbors)
                {
                    if (n < 0 || dist.ContainsKey(n)) continue;
                    if (IsBlockedBy(item, eng.Get(n))) continue;   // 막는 칸은 통과도 못 한다
                    dist[n] = d + 1;
                    q.Enqueue(n);
                }
            }
        }

        /// <summary>이 칸이 속한 연결 영역 (locked 로 갈린 영역 단위).</summary>
        public static List<int> RegionOf(GameEngine eng, int origin)
        {
            var seen = new HashSet<int> { origin };
            var q = new Queue<int>();
            var acc = new List<int>();
            q.Enqueue(origin);

            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                acc.Add(cur);
                foreach (int n in eng.Topo.Cells[cur].Neighbors)
                {
                    if (n < 0 || !seen.Add(n)) continue;
                    if (eng.Get(n) == CellState.Locked) { seen.Remove(n); continue; }
                    q.Enqueue(n);
                }
            }
            return acc;
        }

        /// <summary>아이템 발동. 소거는 minGroupSize 를 무시한다.</summary>
        public static ClearResult Fire(GameEngine eng, SpatialItem item, int originId, int axis)
        {
            var cells = Affected(eng, item, originId, axis);
            var res = new ClearResult { ClearedByColor = new int[eng.PaletteSize] };

            var target = new List<int>();
            foreach (int id in cells)
                if (CanDamage(item, eng.Get(id))) target.Add(id);

            eng.ClearCells(target, res);
            eng.ApplyGravity();
            eng.Refill();
            return res;
        }
    }
}
