// TopologyGen.cs — 토폴로지 3종 생성. fallTarget 계산은 각 생성기가 책임진다.
// 중력은 위→아래 고정 상수다. 설정으로 바꿀 수 없다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public static class TopologyGen
    {
        public const string Square = "square";
        public const string Triangle = "triangle";
        public const string Hex = "hex";

        public static readonly string[] Kinds = { Square, Triangle, Hex };

        public static Topology Build(string kind, int size)
        {
            switch (kind)
            {
                case Triangle: return BuildTriangle(size);
                case Hex: return BuildHex(size);
                default: return BuildSquare(size);
            }
        }

        // ---------- square : 이웃 4, 축 2 ----------
        // neighbors: 0=우 1=좌 2=상 3=하
        static Topology BuildSquare(int n)
        {
            var t = new Topology
            {
                Name = Square,
                NeighborCount = 4,
                Axes = new[]
                {
                    new Axis { Index = 0, Label = "row", Forward = 0, Backward = 1 },
                    new Axis { Index = 1, Label = "col", Forward = 2, Backward = 3 },
                },
                Cells = new Cell[n * n],
            };

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int id = y * n + x;
                    var c = new Cell { Id = id, Neighbors = new int[4] };
                    c.Neighbors[0] = x + 1 < n ? id + 1 : -1;
                    c.Neighbors[1] = x - 1 >= 0 ? id - 1 : -1;
                    c.Neighbors[2] = y + 1 < n ? id + n : -1;
                    c.Neighbors[3] = y - 1 >= 0 ? id - n : -1;
                    c.FallTarget = c.Neighbors[3];                 // 바로 아래
                    c.IsSpawn = y == n - 1;                        // 맨 윗줄로 들어온다
                    c.AxisForward = new[] { c.Neighbors[0], c.Neighbors[2] };
                    c.AxisBackward = new[] { c.Neighbors[1], c.Neighbors[3] };
                    c.Center = new Vec2(x + 0.5, y + 0.5);
                    c.Poly = new[]
                    {
                        new Vec2(x, y), new Vec2(x + 1, y), new Vec2(x + 1, y + 1), new Vec2(x, y + 1)
                    };
                    t.Cells[id] = c;
                }
            return t;
        }

        // ---------- triangle : 이웃 3, 축 3 ----------
        // 위향(▲)/아래향(▼)이 교대한다. 삼각형은 셀당 이웃이 3개뿐이라
        // 한 변을 두 축이 나눠 쓴다 (3축 x 2방향 = 6, 변은 3개).
        // 축은 '두 방향을 번갈아 밟는' 규칙으로 정의된다.
        static Topology BuildTriangle(int n)
        {
            int cols = n * 2;
            double h = System.Math.Sqrt(3.0) / 2.0;

            var t = new Topology
            {
                Name = Triangle,
                NeighborCount = 3,
                Axes = new[]
                {
                    new Axis { Index = 0, Label = "horizontal", Forward = 1, Backward = 0 },
                    new Axis { Index = 1, Label = "diagonal-down-right", Forward = 2, Backward = 0 },
                    new Axis { Index = 2, Label = "diagonal-down-left", Forward = 2, Backward = 1 },
                },
                Cells = new Cell[n * cols],
            };

            for (int row = 0; row < n; row++)
                for (int col = 0; col < cols; col++)
                {
                    int id = row * cols + col;
                    bool up = ((row + col) % 2) == 0;
                    var c = new Cell { Id = id, Neighbors = new int[3] };

                    int left = col - 1 >= 0 ? id - 1 : -1;
                    int right = col + 1 < cols ? id + 1 : -1;
                    // ▲ 는 아래 줄(row-1)과, ▼ 는 위 줄(row+1)과 가로변을 맞댄다
                    int vert = up ? (row - 1 >= 0 ? id - cols : -1)
                                  : (row + 1 < n ? id + cols : -1);

                    c.Neighbors[0] = left;
                    c.Neighbors[1] = right;
                    c.Neighbors[2] = vert;

                    // 중력은 위→아래 고정이다. ▲ 는 아래 줄로 떨어지고,
                    // ▼ 는 같은 줄의 이웃 ▲ 가 기하적으로 더 아래이므로 그리로 떨어진다.
                    // 그래서 삼각 격자에서는 낙하 경로가 한 줄씩 옆으로 밀린다.
                    c.FallTarget = up ? vert : left;
                    c.IsSpawn = row == n - 1;

                    // 축별 이동. ▲ 와 ▼ 가 서로 다른 변을 쓰기 때문에 방향별 매핑을 나눈다.
                    // ▲: right(+0.5,+h/3) left(-0.5,+h/3) vert(0,-2h/3)
                    // ▼: right(+0.5,-h/3) left(-0.5,-h/3) vert(0,+2h/3)
                    // 축0 가로   = ▲right → ▼right      (2보 합 (+1, 0))
                    // 축1 우상향 = ▲right → ▼vert       (2보 합 (+0.5, +h))
                    // 축2 좌상향 = ▲left  → ▼vert       (2보 합 (-0.5, +h))
                    if (up)
                    {
                        c.AxisForward = new[] { right, right, left };
                        c.AxisBackward = new[] { left, vert, vert };
                    }
                    else
                    {
                        c.AxisForward = new[] { right, vert, vert };
                        c.AxisBackward = new[] { left, left, right };
                    }

                    double bx = col * 0.5, y0 = row * h;
                    c.Poly = up
                        ? new[] { new Vec2(bx, y0), new Vec2(bx + 1, y0), new Vec2(bx + 0.5, y0 + h) }
                        : new[] { new Vec2(bx, y0 + h), new Vec2(bx + 1, y0 + h), new Vec2(bx + 0.5, y0) };
                    c.Center = Centroid(c.Poly);
                    t.Cells[id] = c;
                }
            return t;
        }

        // ---------- hex : 이웃 6, 축 3 ----------
        // flat-top 고정. 축 좌표(q, r) 로 저장하면 세 축이 모두 직선이 된다.
        // 중심 = (1.5q, h*(r + q/2)). 수평축은 없다.
        static Topology BuildHex(int n)
        {
            double h = System.Math.Sqrt(3.0) / 2.0;

            var t = new Topology
            {
                Name = Hex,
                NeighborCount = 6,
                Axes = new[]
                {
                    new Axis { Index = 0, Label = "vertical", Forward = 0, Backward = 1 },
                    new Axis { Index = 1, Label = "diagonal-a", Forward = 2, Backward = 3 },
                    new Axis { Index = 2, Label = "diagonal-b", Forward = 4, Backward = 5 },
                },
                Cells = new Cell[n * n],
            };

            // 축 좌표(q, r) 로 이웃을 정의하되, 저장은 열마다 r 을 밀어 직사각형이 되게 한다.
            // 그냥 q,r 을 0..n 으로 두면 판이 마름모가 되어 구석이 크게 빈다.
            for (int q = 0; q < n; q++)
                for (int rr = 0; rr < n; rr++)
                {
                    int r = rr - (q >> 1);
                    int id = q * n + rr;
                    var c = new Cell { Id = id, Neighbors = new int[6] };

                    c.Neighbors[0] = Idx(n, q, r + 1);       // 위        (0,+1)
                    c.Neighbors[1] = Idx(n, q, r - 1);       // 아래      (0,-1)
                    c.Neighbors[2] = Idx(n, q + 1, r);       // 우상      (+1, 0)
                    c.Neighbors[3] = Idx(n, q - 1, r);       // 좌하      (-1, 0)
                    c.Neighbors[4] = Idx(n, q + 1, r - 1);   // 우하      (+1,-1)
                    c.Neighbors[5] = Idx(n, q - 1, r + 1);   // 좌상      (-1,+1)

                    c.FallTarget = c.Neighbors[1];           // 같은 열 아래
                    c.IsSpawn = rr == n - 1;
                    c.AxisForward = new[] { c.Neighbors[0], c.Neighbors[2], c.Neighbors[4] };
                    c.AxisBackward = new[] { c.Neighbors[1], c.Neighbors[3], c.Neighbors[5] };

                    double cx = 1.5 * q;
                    double cy = h * (2 * r + q);
                    c.Center = new Vec2(cx, cy);
                    // flat-top 정육각형. 반지름 1 — 중심에서 여섯 꼭짓점까지 같다.
                    var poly = new Vec2[6];
                    for (int i = 0; i < 6; i++)
                    {
                        double ang = System.Math.PI / 180.0 * (60 * i);
                        poly[i] = new Vec2(cx + System.Math.Cos(ang), cy + System.Math.Sin(ang));
                    }
                    c.Poly = poly;
                    t.Cells[id] = c;
                }
            return t;
        }

        /// <summary>축 좌표 → 저장 인덱스. 열마다 r 을 밀어 직사각형 판을 만든다.</summary>
        static int Idx(int n, int q, int r)
        {
            if (q < 0 || q >= n) return -1;
            int rr = r + (q >> 1);
            return rr >= 0 && rr < n ? q * n + rr : -1;
        }

        public static Vec2 Centroid(Vec2[] poly)
        {
            double x = 0, y = 0;
            foreach (var p in poly) { x += p.X; y += p.Y; }
            return new Vec2(x / poly.Length, y / poly.Length);
        }

        /// <summary>셀의 내접원 반지름 — 중심에서 각 변까지의 최소 거리.
        /// '직선인가' 판정의 허용 오차 기준으로 쓴다 (임의 상수가 아니다).</summary>
        public static double Inradius(Cell c)
        {
            double best = double.MaxValue;
            for (int i = 0; i < c.Poly.Length; i++)
            {
                var a = c.Poly[i];
                var b = c.Poly[(i + 1) % c.Poly.Length];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len = System.Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-12) continue;
                double d = System.Math.Abs((c.Center.X - a.X) * dy - (c.Center.Y - a.Y) * dx) / len;
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>fallTarget 을 따라가다 순환이 생기는지. 있으면 그 칸 목록을 돌려준다.</summary>
        public static List<int> FallCycles(Topology t)
        {
            var bad = new List<int>();
            for (int i = 0; i < t.Count; i++)
            {
                int cur = i, guard = t.Count + 1;
                var seen = new HashSet<int>();
                while (cur >= 0 && guard-- > 0)
                {
                    if (!seen.Add(cur)) { bad.Add(i); break; }
                    cur = t.Cells[cur].FallTarget;
                }
                if (guard <= 0) bad.Add(i);
            }
            return bad;
        }

        /// <summary>스폰 칸에서 fallTarget 을 거꾸로 타고 닿지 못하는 칸.</summary>
        public static List<int> Unreachable(Topology t)
        {
            var reach = new bool[t.Count];
            var q = new Queue<int>();
            for (int i = 0; i < t.Count; i++)
                if (t.Cells[i].IsSpawn) { reach[i] = true; q.Enqueue(i); }

            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int nxt = t.Cells[cur].FallTarget;
                if (nxt >= 0 && !reach[nxt]) { reach[nxt] = true; q.Enqueue(nxt); }
            }

            var list = new List<int>();
            for (int i = 0; i < t.Count; i++) if (!reach[i]) list.Add(i);
            return list;
        }
    }
}
