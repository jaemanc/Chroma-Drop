// GameEngine.cs — 규칙 엔진. 보드는 그래프이고, 엔진은 x/y/width/height/column 을 모른다.
// 소거는 '인접 같은 색 연결 그룹'이다. 선/대각 매칭은 없다.
// 중력은 fallTarget 을 따르는 한 방향 고정이다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public static class CellState
    {
        public const int Empty = -1;
        public const int Brick = -2;
        public const int Locked = -3;
        public const int Frozen = -4;

        public static bool IsColor(int v) { return v >= 0; }
        public static bool IsObstacle(int v) { return v == Brick || v == Locked || v == Frozen; }
        /// <summary>낙하를 물리적으로 막는가.</summary>
        public static bool Blocks(int v) { return IsObstacle(v); }
    }

    public sealed class ClearResult
    {
        public readonly List<int> Cleared = new List<int>();
        public int BricksBroken;
        public int FrozenThawed;
        public int Score;
        public int Chain;
        public int[] ClearedByColor = new int[0];
        public int LargestGroup;
        /// <summary>이번에 새로 들어오거나 내려온 칸. 연출이 여기만 튀게 한다.</summary>
        public readonly List<int> Refilled = new List<int>();
    }

    public sealed class GameEngine
    {
        public readonly Topology Topo;
        public readonly int PaletteSize;
        public readonly int MinGroupSize;

        readonly int[] cells;
        readonly int[] brickHp;
        readonly RefillSpec refill;
        readonly double[] colorWeights;
        readonly Rng refillRng;
        readonly bool chainReaction;

        public GameEngine(Topology topo, EngineSpec spec, Rng boardRng, Rng refillRng)
        {
            Topo = topo;
            PaletteSize = spec.PaletteSize;
            MinGroupSize = spec.MinGroupSize;
            refill = spec.Refill;
            colorWeights = spec.ColorWeights;
            chainReaction = spec.ChainReaction;
            this.refillRng = refillRng;

            cells = new int[topo.Count];
            brickHp = new int[topo.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = CellState.Empty;

            foreach (var o in spec.Obstacles)
                foreach (int id in o.CellIds)
                {
                    if (id < 0 || id >= cells.Length) continue;
                    cells[id] = o.Cell;
                    brickHp[id] = o.Cell == CellState.Brick ? o.HitsToBreak : 0;
                }

            FillInitial(spec, boardRng);
        }

        public int Get(int id) { return cells[id]; }
        public void Set(int id, int v) { cells[id] = v; }
        public int BrickHp(int id) { return cells[id] == CellState.Brick ? brickHp[id] : 0; }
        public int Count { get { return cells.Length; } }

        // ---------- 초기 배치 ----------

        void FillInitial(EngineSpec spec, Rng rng)
        {
            var open = new List<int>();
            for (int i = 0; i < cells.Length; i++)
                if (!CellState.IsObstacle(cells[i])) open.Add(i);

            var order = new List<int>(open);
            if (spec.FillPattern == FillKind.BottomUp)
            {
                // '아래부터' 는 좌표가 아니라 낙하 깊이로 정한다.
                // 낙하 사슬을 더 못 내려가는 칸일수록 아래다.
                var depth = FallDepth();
                order.Sort((x, y) =>
                {
                    int c = depth[x].CompareTo(depty(depth, y));
                    return c != 0 ? c : x.CompareTo(y);
                });
            }
            else Shuffle(order, rng);

            int keep = (int)(open.Count * spec.InitialFillRatio);
            for (int i = 0; i < order.Count; i++) cells[order[i]] = CellState.Empty;

            // 채우면서 바로 소거되는 그룹이 생기지 않게 색을 고른다.
            // 채운 뒤 고치는 방식은 이웃이 많은 격자에서 수렴하지 않는다.
            for (int i = 0; i < keep; i++) Recolor(order[i], rng);

            // 그래도 남는 위반은 그 그룹의 칸을 다시 칠해 푼다.
            // 이웃 수가 많은 격자에서도 적정 색칠이 존재하므로 몇 바퀴면 사라진다.
            int passes = PaletteSize * 4;
            while (passes-- > 0)
            {
                var g = FirstGroup();
                if (g == null) break;
                foreach (int id in g) Recolor(id, rng);
            }
        }

        /// <summary>같은 색 이웃이 가장 적은 색으로 칠한다. 동점이면 만들어지는 덩어리가 작은 쪽.</summary>
        void Recolor(int id, Rng rng)
        {
            int start = rng.Next(PaletteSize);
            int best = start, bestAdj = int.MaxValue, bestSize = int.MaxValue;

            for (int k = 0; k < PaletteSize; k++)
            {
                int color = (start + k) % PaletteSize;
                int adj = 0;
                foreach (int nb in Topo.Cells[id].Neighbors)
                    if (nb >= 0 && cells[nb] == color) adj++;

                cells[id] = color;
                int size = ComponentSize(id, color);

                if (adj < bestAdj || (adj == bestAdj && size < bestSize))
                {
                    bestAdj = adj; bestSize = size; best = color;
                }
                if (size < MinGroupSize && adj == 0) break;
            }
            cells[id] = best;
        }

        /// <summary>이 칸에서 낙하 사슬을 몇 번이나 더 내려갈 수 있는가. 0 이면 바닥이다.</summary>
        int[] FallDepth()
        {
            var depth = new int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                int d = 0, cur = i, guard = cells.Length;
                while (guard-- > 0)
                {
                    int nxt = Topo.Cells[cur].FallTarget;
                    if (nxt < 0) break;
                    cur = nxt; d++;
                }
                depth[i] = d;
            }
            return depth;
        }

        static int depty(int[] depth, int i) { return depth[i]; }

        /// <summary>이미 채워진 칸만 보고 센 연결 성분 크기.</summary>
        int ComponentSize(int id, int color)
        {
            var seen = new HashSet<int> { id };
            var stack = new Stack<int>();
            stack.Push(id);
            int n = 0;
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                n++;
                foreach (int nb in Topo.Cells[cur].Neighbors)
                {
                    if (nb < 0 || seen.Contains(nb)) continue;
                    if (cells[nb] != color) continue;
                    seen.Add(nb);
                    stack.Push(nb);
                }
            }
            return n;
        }

        static void Shuffle(List<int> list, Rng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        // ---------- 그룹 ----------

        /// <summary>같은 색으로 이어진 그룹. minGroupSize 미만이면 null.</summary>
        public List<int> GroupAt(int id)
        {
            if (!CellState.IsColor(cells[id])) return null;
            int color = cells[id];
            var seen = new HashSet<int> { id };
            var stack = new Stack<int>();
            var acc = new List<int>();
            stack.Push(id);

            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                acc.Add(cur);
                foreach (int n in Topo.Cells[cur].Neighbors)
                {
                    if (n < 0 || seen.Contains(n)) continue;
                    if (cells[n] != color) continue;
                    seen.Add(n);
                    stack.Push(n);
                }
            }
            return acc.Count >= MinGroupSize ? acc : null;
        }

        List<int> FirstGroup()
        {
            for (int i = 0; i < cells.Length; i++)
            {
                var g = GroupAt(i);
                if (g != null) return g;
            }
            return null;
        }

        /// <summary>이 칸들에 이 색을 놓으면 어느 칸이 사라지는지 미리 계산한다.
        /// 보드는 바꾸지 않는다. 실제로 놓아 보고 되돌리므로 판정이 어긋날 수 없다.</summary>
        public List<int> PreviewPlace(IList<int> cells, int color)
        {
            var saved = new int[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                saved[i] = this.cells[cells[i]];
                this.cells[cells[i]] = color;
            }

            var acc = new List<int>();
            var seen = new HashSet<int>();
            foreach (var g in AllGroups())
                foreach (int id in g) if (seen.Add(id)) acc.Add(id);

            for (int i = 0; i < cells.Count; i++) this.cells[cells[i]] = saved[i];
            return acc;
        }

        /// <summary>지금 이 순간 소거될 칸 전부. 보드는 바꾸지 않는다.</summary>
        public List<int> Clearable()
        {
            var acc = new List<int>();
            var seen = new HashSet<int>();
            foreach (var g in AllGroups())
                foreach (int id in g) if (seen.Add(id)) acc.Add(id);
            return acc;
        }

        /// <summary>지금 소거 가능한 그룹이 하나라도 있는가.</summary>
        public bool HasClearableGroup() { return FirstGroup() != null; }

        // ---------- 소거 ----------

        /// <summary>모든 소거 가능 그룹을 없애고 중력·리필까지 돌린다 (연쇄 끝까지).</summary>
        public ClearResult ResolveAll()
        {
            var res = new ClearResult { ClearedByColor = new int[PaletteSize] };
            int guard = cells.Length * 4;

            while (guard-- > 0)
            {
                var step = ResolveOnce();
                if (step.Cleared.Count == 0) break;

                res.Chain++;
                res.Score += step.Score;
                res.BricksBroken += step.BricksBroken;
                res.FrozenThawed += step.FrozenThawed;
                if (step.LargestGroup > res.LargestGroup) res.LargestGroup = step.LargestGroup;
                res.Cleared.AddRange(step.Cleared);
                res.Refilled.AddRange(step.Refilled);
                for (int i = 0; i < res.ClearedByColor.Length && i < step.ClearedByColor.Length; i++)
                    res.ClearedByColor[i] += step.ClearedByColor[i];

                if (!chainReaction) break;
            }
            return res;
        }

        /// <summary>연쇄 한 단계만 처리한다. 화면에 단계별로 보여주려면 이걸 쓴다.</summary>
        public ClearResult ResolveOnce()
        {
            var res = new ClearResult { ClearedByColor = new int[PaletteSize], Chain = 1 };

            var groups = AllGroups();
            if (groups.Count == 0) { res.Chain = 0; return res; }

            var doomed = new List<int>();
            foreach (var g in groups)
            {
                if (g.Count > res.LargestGroup) res.LargestGroup = g.Count;
                doomed.AddRange(g);
            }

            ClearCells(doomed, res);

            // 낙하·리필 전후를 비교해 '새로 채워진 칸' 을 뽑는다
            var before = new int[cells.Length];
            for (int i = 0; i < cells.Length; i++) before[i] = cells[i];

            ApplyGravity();
            Refill();

            for (int i = 0; i < cells.Length; i++)
                if (before[i] != cells[i] && CellState.IsColor(cells[i])) res.Refilled.Add(i);

            return res;
        }

        List<List<int>> AllGroups()
        {
            var found = new List<List<int>>();
            var seen = new bool[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                if (seen[i] || !CellState.IsColor(cells[i])) continue;
                var g = GroupAt(i);
                if (g == null) { seen[i] = true; continue; }
                foreach (int id in g) seen[id] = true;
                found.Add(g);
            }
            return found;
        }

        /// <summary>지정한 칸들을 없앤다. 인접 장애물에 손상을 준다.</summary>
        public void ClearCells(IList<int> ids, ClearResult res)
        {
            var damaged = new HashSet<int>();
            foreach (int id in ids)
            {
                int was = cells[id];
                if (CellState.IsColor(was))
                {
                    if (was < res.ClearedByColor.Length) res.ClearedByColor[was]++;
                    res.Score += Score.PerCell;
                }
                else if (was == CellState.Brick) res.BricksBroken++;
                if (was == CellState.Locked) continue;    // locked 는 없앨 수 없다

                cells[id] = CellState.Empty;
                brickHp[id] = 0;
                res.Cleared.Add(id);
            }

            foreach (int id in ids)
                foreach (int n in Topo.Cells[id].Neighbors)
                {
                    if (n < 0 || !damaged.Add(n)) continue;
                    if (cells[n] == CellState.Frozen)
                    {
                        cells[n] = NextColor();
                        res.FrozenThawed++;
                    }
                    else if (cells[n] == CellState.Brick && --brickHp[n] <= 0)
                    {
                        cells[n] = CellState.Empty;
                        brickHp[n] = 0;
                        res.BricksBroken++;
                        res.Cleared.Add(n);
                    }
                }
        }

        // ---------- 중력 ----------

        /// <summary>fallTarget 을 따라 한 칸씩 내린다. 방향은 상수다.</summary>
        public void ApplyGravity()
        {
            bool moved = true;
            int guard = cells.Length + 1;
            while (moved && guard-- > 0)
            {
                moved = false;
                for (int i = 0; i < cells.Length; i++)
                {
                    if (!CellState.IsColor(cells[i])) continue;
                    int below = Topo.Cells[i].FallTarget;
                    if (below < 0 || cells[below] != CellState.Empty) continue;
                    cells[below] = cells[i];
                    cells[i] = CellState.Empty;
                    moved = true;
                }
            }
        }

        // ---------- 리필 ----------

        /// <summary>스폰 칸으로만 신규 블록이 들어온다. 위가 막힌 영역에는 못 들어온다.</summary>
        public int Refill()
        {
            if (refill.Mode == RefillKind.None) return 0;
            int budget = refill.Mode == RefillKind.Instant ? cells.Length : refill.BlocksPerClear;
            int placed = 0;
            int guard = cells.Length * 4;

            while (placed < budget && guard-- > 0)
            {
                bool any = false;
                for (int i = 0; i < cells.Length && placed < budget; i++)
                {
                    if (!Topo.Cells[i].IsSpawn || cells[i] != CellState.Empty) continue;
                    cells[i] = NextColor();
                    placed++;
                    any = true;
                }
                if (!any) break;
                ApplyGravity();
            }
            return placed;
        }

        int NextColor()
        {
            if (colorWeights == null || colorWeights.Length != PaletteSize)
                return refillRng.Next(PaletteSize);
            return refillRng.Weighted(colorWeights);
        }
    }

    public static class Score
    {
        public const int PerCell = 10;
    }
}
