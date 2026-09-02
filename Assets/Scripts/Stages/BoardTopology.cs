// BoardTopology.cs — 보드의 연결 구조 분석. locked 로 갈린 영역, 사장 영역,
// 신규 블록이 못 들어오는 고립 영역을 찾는다. 에러가 아니라 정보다.

using System.Collections.Generic;

namespace ColorMatcher.Core
{
    /// <summary>locked 로 갈린 영역 하나.</summary>
    public class Region
    {
        public List<Point> Cells = new List<Point>();
        public bool DeadZone;        // 칸 수가 minMatch^2 미만 — 영영 매칭이 안 되는 영역
        public bool Refillable;      // 어느 한 칸이라도 열 위쪽까지 뚫려 있는가
        public int Size { get { return Cells.Count; } }
    }

    public static class BoardTopology
    {
        /// <summary>장애물이 아닌 칸들을 4방향으로 이어 영역을 나눈다.</summary>
        public static List<Region> Regions(Board b)
        {
            int w = b.W, h = b.H;
            var seen = new bool[w, h];
            var list = new List<Region>();
            int need = b.MinMatch * b.MinMatch;

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (seen[x, y] || !Passable(b, x, y)) continue;

                    var r = new Region();
                    var q = new Queue<Point>();
                    q.Enqueue(new Point(x, y));
                    seen[x, y] = true;

                    while (q.Count > 0)
                    {
                        var p = q.Dequeue();
                        r.Cells.Add(p);
                        if (ColumnOpenAbove(b, p.X, p.Y)) r.Refillable = true;

                        TryPush(b, seen, q, p.X + 1, p.Y);
                        TryPush(b, seen, q, p.X - 1, p.Y);
                        TryPush(b, seen, q, p.X, p.Y + 1);
                        TryPush(b, seen, q, p.X, p.Y - 1);
                    }

                    r.DeadZone = r.Size < need;
                    list.Add(r);
                }

            list.Sort((a, c) => c.Size.CompareTo(a.Size));
            return list;
        }

        /// <summary>사장 영역을 뺀, 실제로 쓸 수 있는 칸 수.</summary>
        public static int UsableCells(Board b)
        {
            int n = 0;
            foreach (var r in Regions(b)) if (!r.DeadZone) n += r.Size;
            return n;
        }

        static void TryPush(Board b, bool[,] seen, Queue<Point> q, int x, int y)
        {
            if (x < 0 || y < 0 || x >= b.W || y >= b.H) return;
            if (seen[x, y] || !Passable(b, x, y)) return;
            seen[x, y] = true;
            q.Enqueue(new Point(x, y));
        }

        /// <summary>블록이 있을 수 있는 칸인가. 장애물은 영역을 가른다.</summary>
        static bool Passable(Board b, int x, int y)
        {
            int t = b.GetTile(x, y);
            return t == Board.Empty || t >= 0;
        }

        /// <summary>이 칸 위쪽이 열 최상단까지 뚫려 있는가 — 신규 블록이 들어올 수 있는가.</summary>
        static bool ColumnOpenAbove(Board b, int x, int y)
        {
            for (int k = y + 1; k < b.H; k++)
                if (!Passable(b, x, k)) return false;
            return true;
        }
    }
}
