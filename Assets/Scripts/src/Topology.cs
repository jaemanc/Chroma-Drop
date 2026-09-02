// Topology.cs — 보드를 그래프로 정의한다. 엔진은 좌표를 모르고 이웃 인덱스만 안다.
// 좌표(poly)는 렌더링과 검증에만 쓰인다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public struct Vec2
    {
        public double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }
    }

    /// <summary>축 하나. neighbors 배열의 인덱스로 앞/뒤를 가리킨다.</summary>
    public sealed class Axis
    {
        public int Index;
        public string Label;
        public int Forward;    // neighbors[] 인덱스
        public int Backward;
    }

    public sealed class Cell
    {
        public int Id;
        public int[] Neighbors;      // 없으면 -1
        public int FallTarget;       // 중력이 끌어내리는 칸. 없으면 -1
        public bool IsSpawn;         // 신규 블록이 진입하는 칸
        public Vec2[] Poly;          // 렌더용 다각형
        public Vec2 Center;
        public int[] AxisForward;    // 축별 앞 이웃 (삼각형처럼 방향이 갈리는 토폴로지 대응)
        public int[] AxisBackward;
    }

    public sealed class Topology
    {
        public string Name;
        public int NeighborCount;
        public Axis[] Axes;
        public Cell[] Cells;

        public int Count { get { return Cells.Length; } }

        /// <summary>축을 따라 한 칸 이동. 없으면 -1.</summary>
        public int Step(int cellId, int axis, bool forward)
        {
            var c = Cells[cellId];
            var table = forward ? c.AxisForward : c.AxisBackward;
            if (table == null || axis >= table.Length) return -1;
            int n = table[axis];
            return n;
        }

        /// <summary>축을 따라 끝까지 훑은 칸 목록 (기준 칸 포함).</summary>
        public List<int> Line(int cellId, int axis)
        {
            var list = new List<int> { cellId };
            for (int dir = 0; dir < 2; dir++)
            {
                int cur = cellId;
                var guard = Cells.Length;
                while (guard-- > 0)
                {
                    int nxt = Step(cur, axis, dir == 0);
                    if (nxt < 0) break;
                    list.Add(nxt);
                    cur = nxt;
                }
            }
            return list;
        }
    }
}
