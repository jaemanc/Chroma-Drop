// PieceShapes.cs — 조각 모양을 그래프 위에서 정의한다.
// 좌표가 아니라 '기준 칸에서의 이웃 이동 경로'로 표현하므로 토폴로지에 독립적이다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    /// <summary>기준 칸에서 (축, 방향) 을 밟아 도달하는 칸들의 모임.</summary>
    public sealed class PieceShape
    {
        public string Name;
        public int[][] Steps;   // 각 칸: [축, 방향(0=forward,1=backward)] 쌍의 나열
    }

    public static class PieceShapes
    {
        /// <summary>이 토폴로지에서 쓸 수 있는 조각 모양. 축 수에 따라 자동 생성한다.</summary>
        public static List<PieceShape> For(Topology t)
        {
            var list = new List<PieceShape>();
            list.Add(new PieceShape { Name = "single", Steps = new int[][] { new int[0] } });

            for (int a = 0; a < t.Axes.Length; a++)
            {
                list.Add(new PieceShape
                {
                    Name = "pair-" + t.Axes[a].Label,
                    Steps = new[] { new int[0], new[] { a, 0 } },
                });
                list.Add(new PieceShape
                {
                    Name = "triple-" + t.Axes[a].Label,
                    Steps = new[] { new int[0], new[] { a, 0 }, new[] { a, 0, a, 0 } },
                });
            }

            if (t.Axes.Length >= 2)
                list.Add(new PieceShape
                {
                    Name = "corner",
                    Steps = new[] { new int[0], new[] { 0, 0 }, new[] { 1, 0 } },
                });
            return list;
        }

        /// <summary>기준 칸에 이 모양을 놓았을 때의 칸 목록. 하나라도 벗어나면 null.</summary>
        public static List<int> Resolve(Topology t, PieceShape shape, int originId)
        {
            var acc = new List<int>();
            foreach (var path in shape.Steps)
            {
                int cur = originId;
                for (int i = 0; i + 1 < path.Length; i += 2)
                {
                    cur = t.Step(cur, path[i], path[i + 1] == 0);
                    if (cur < 0) return null;
                }
                if (acc.Contains(cur)) return null;
                acc.Add(cur);
            }
            return acc;
        }
    }
}
