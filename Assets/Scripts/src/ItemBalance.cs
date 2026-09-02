// ItemBalance.cs — 토폴로지 간 아이템 위력 보정. 이웃 수가 많을수록 선이 길어지므로 줄인다.
// 수치는 curve.config.json 의 itemModifiers 에서 오지만, 기본값은 여기 상수 하나로 둔다.

namespace ChromaDrop.Engine
{
    public static class ItemBalance
    {
        /// <summary>기준 토폴로지(이웃 4)에서의 선 길이 비율.</summary>
        public const double BaseRatio = 1.0;

        /// <summary>판의 '높이' — 스폰 칸 하나가 책임지는 칸 수.
        /// 좌표가 아니라 그래프 구조에서 얻고, 토폴로지가 달라도 같은 뜻을 가지므로
        /// 아이템 위력을 맞추는 공통 기준이 된다.</summary>
        public static int ColumnHeight(Topology t)
        {
            int spawns = 0;
            for (int i = 0; i < t.Count; i++) if (t.Cells[i].IsSpawn) spawns++;
            if (spawns <= 0) return t.Count;
            return (int)System.Math.Round(t.Count / (double)spawns);
        }

        /// <summary>line 이 한 번에 먹을 수 있는 최대 칸수.
        /// 축을 따라 뻗을 수 있는 길이는 토폴로지마다 다르므로, 판 높이로 잘라 위력을 맞춘다.</summary>
        public static int LineMaxCells(Topology t) { return LineMaxCells(t, BaseRatio); }

        public static int LineMaxCells(Topology t, double ratio)
        {
            int cap = (int)System.Math.Round(ColumnHeight(t) * ratio);
            return cap < 1 ? 1 : cap;
        }

        /// <summary>이웃이 많은 토폴로지일수록 burst 반경을 줄인다.</summary>
        public static int BurstRadius(Topology t, int baseRadius)
        {
            int r = baseRadius - (t.NeighborCount - 4) / 2;
            return r < 1 ? 1 : r;
        }
    }
}
