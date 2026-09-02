// StageValidator.cs — 플레이 가능성 검증. 설정만 보고 알 수 있는 것과
// 실제 판을 만들어야 알 수 있는 것을 나눠 본다. 분단·사장 영역은 에러가 아니라 정보다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public sealed class StageCheck
    {
        public int StageId;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Info = new List<string>();
        public bool Ok { get { return Errors.Count == 0; } }
    }

    public static class StageValidator
    {
        /// <summary>한 수에 없앨 수 있는 칸 수의 낙관적 상한.
        /// 목표가 산술적으로 불가능한지 거르는 데만 쓴다 — 기대값이 아니다.</summary>
        public static int OptimisticClearPerMove(StageDef def)
        {
            int g = def.MinGroupSize < 2 ? 2 : def.MinGroupSize;
            return g * g * 3;
        }

        public static StageCheck Check(StageDef def, Rgb background)
        {
            var c = new StageCheck { StageId = def.StageId };

            if (def.Moves > 0)
            {
                int ceiling = def.Moves * OptimisticClearPerMove(def);
                foreach (var o in def.Objectives)
                {
                    if (o.Type == "clear_count" || o.Type == "clear_color")
                    {
                        if (o.Target > ceiling)
                            c.Errors.Add(o.Type + " 목표 " + o.Target + " 은 " + def.Moves
                                         + "수로 도달할 수 없다 (상한 " + ceiling + ")");
                    }
                    else if (o.Type == "reach_score")
                    {
                        int s = ceiling * Score.PerCell;
                        if (o.Target > s)
                            c.Errors.Add("점수 목표 " + o.Target + " 은 도달할 수 없다 (상한 " + s + ")");
                    }
                }
            }

            var inst = StageBuilder.Build(def, background);
            foreach (var l in inst.Log) c.Info.Add(l);

            int usable = BoardRegions.Usable(inst.Engine);
            if (usable < def.MinGroupSize)
                c.Errors.Add("사장 영역을 빼면 소거가 성립할 칸이 없다");

            // 색 목표: 신규로도 안 나오고 판에도 부족하면 영영 못 채운다
            var census = new int[def.Palette.Size];
            for (int i = 0; i < inst.Engine.Count; i++)
            {
                int v = inst.Engine.Get(i);
                if (v >= 0 && v < census.Length) census[v]++;
            }
            foreach (var o in def.Objectives)
            {
                if (o.Type != "clear_color") continue;
                bool spawnable = def.RefillMode != "none"
                              && o.ColorIndex < def.ColorWeights.Length
                              && def.ColorWeights[o.ColorIndex] > 0;
                if (!spawnable && census[o.ColorIndex] < o.Target)
                    c.Errors.Add("색" + o.ColorIndex + " 목표 " + o.Target
                                 + "인데 신규로 안 나오고 판에는 " + census[o.ColorIndex] + "개뿐이다");
            }

            // 장애물 목표: 판에 있는 수보다 많이 부술 수 없다
            foreach (var o in def.Objectives)
            {
                if (o.Type != "break_obstacle") continue;
                int cell = o.ObstacleType == "frozen" ? CellState.Frozen : CellState.Brick;
                int have = 0;
                for (int i = 0; i < inst.Engine.Count; i++) if (inst.Engine.Get(i) == cell) have++;
                if (have < o.Target)
                    c.Errors.Add(o.ObstacleType + " 목표 " + o.Target + "인데 판에는 " + have + "개뿐이다");
            }

            // 시작 판에 소거 가능한 그룹이 있으면 안 된다
            if (inst.Engine.HasClearableGroup())
                c.Errors.Add("시작 보드에 이미 소거되는 그룹이 있다");

            // 조각을 놓을 데가 있어야 한다
            if (!inst.Turn.CanPlaceAnywhere())
                c.Warnings.Add("첫 조각을 놓을 자리가 없다 — 리롤로 처리된다");

            foreach (var w in inst.Palette.Warnings) c.Warnings.Add(w);
            return c;
        }
    }
}
