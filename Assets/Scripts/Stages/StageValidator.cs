// StageValidator.cs — 플레이 가능성 검증. 설정만 보고 판단할 수 있는 것과
// 실제 보드를 만들어야 알 수 있는 것을 나눠서 본다.
//
// 여기에도 스테이지 번호나 목표 수치 리터럴은 없다. 판단 기준은 설정에서 파생된 값뿐이다.
// UnityEngine 에 의존하지 않는다 — 오프라인 검사 도구로도 쓸 수 있어야 하기 때문이다.

using System.Collections.Generic;

namespace ColorMatcher.Core
{
    public class StageValidation
    {
        public int StageId;
        public bool Ok { get { return Errors.Count == 0; } }
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Info = new List<string>();

        public int PlayableCells;
        public int UsableCells;              // 사장 영역을 뺀 칸 수
        public List<Region> Regions = new List<Region>();
    }

    public static class StageValidator
    {
        /// <summary>한 수에 최대 몇 칸이나 없앨 수 있는지의 낙관적 상한.
        /// 목표가 산술적으로 불가능한지 거르는 데만 쓴다 — 실제 기대값이 아니다.</summary>
        public static int OptimisticClearPerMove(StageConfig cfg)
        {
            // 가장 큰 정사각 매칭 + 연쇄로 얼마쯤 더 터진다고 낙관적으로 잡는다.
            int side = cfg.MinMatch < cfg.Width ? cfg.MinMatch + 1 : cfg.MinMatch;
            int square = side * side;
            return square * 3;
        }

        /// <summary>설정만으로 판단할 수 있는 것들.</summary>
        public static StageValidation Check(StageConfig cfg)
        {
            var v = new StageValidation { StageId = cfg.StageId };
            v.PlayableCells = cfg.PlayableCells;
            v.Info.Add("조작 가능 칸 = " + cfg.Width * cfg.Height + " - " + cfg.ObstacleCells
                       + " = " + cfg.PlayableCells);

            if (cfg.PlayableCells < cfg.MinMatch * cfg.MinMatch)
                v.Errors.Add("조작 가능 칸이 최소 매칭(" + cfg.MinMatch + "x" + cfg.MinMatch + ")보다 적다");

            // 좌표 중복 — 같은 칸에 두 장애물을 놓으면 뒤엣것만 남아 설계 의도와 달라진다
            var seen = new HashSet<int>();
            foreach (var o in cfg.Obstacles)
                foreach (var p in o.Positions)
                    if (!seen.Add(p.X * 1000 + p.Y))
                        v.Warnings.Add("장애물 좌표가 겹친다 (" + p.X + "," + p.Y + ")");

            CheckObjectiveReachability(cfg, v);
            return v;
        }

        /// <summary>실제 보드를 만들어야 알 수 있는 것들 — 분단, 사장 영역, 고립 영역.</summary>
        public static void CheckBoard(StageConfig cfg, Board b, StageValidation v)
        {
            v.Regions = BoardTopology.Regions(b);
            v.UsableCells = 0;

            for (int i = 0; i < v.Regions.Count; i++)
            {
                var r = v.Regions[i];
                string tag = r.DeadZone ? "dead zone" : (r.Refillable ? "정상" : "고립(소모전)");
                v.Info.Add("영역 " + (i + 1) + ": " + r.Size + "칸 · " + tag);
                if (!r.DeadZone) v.UsableCells += r.Size;
            }
            if (v.Regions.Count > 1)
                v.Info.Add("보드가 " + v.Regions.Count + "개 영역으로 분단됨 — 의도된 설계 수단이다");

            if (v.UsableCells < cfg.MinMatch * cfg.MinMatch)
                v.Errors.Add("사장 영역을 빼고 나면 매칭이 성립할 칸이 없다");

            // 색 목표: 신규로도 안 나오고 판에도 부족하면 영영 못 채운다
            var census = b.ColorCensus();
            foreach (var o in cfg.Objectives)
            {
                if (o.Type != ObjectiveType.ClearColor) continue;
                bool spawnable = cfg.ColorWeights != null
                                 && o.ColorIndex < cfg.ColorWeights.Length
                                 && cfg.ColorWeights[o.ColorIndex] > 0
                                 && cfg.Refill.Mode != RefillMode.None;
                int onBoard = o.ColorIndex < census.Length ? census[o.ColorIndex] : 0;
                if (!spawnable && onBoard < o.Target)
                    v.Errors.Add("'" + o.ColorName + "' 목표 " + o.Target + "개인데 신규로 안 나오고"
                                 + " 판에는 " + onBoard + "개뿐이다");
            }

            // 장애물 목표: 판에 있는 수보다 많이 부술 수는 없다
            foreach (var o in cfg.Objectives)
            {
                if (o.Type != ObjectiveType.BreakObstacle) continue;
                int cell = o.ObstacleType == ObstacleKind.Brick ? Board.Obstacle : Board.Frozen;
                int have = b.CountCells(cell);
                if (have < o.Target)
                    v.Errors.Add(o.ObstacleType + " 목표 " + o.Target + "개인데 판에는 " + have + "개뿐이다");
            }
        }

        static void CheckObjectiveReachability(StageConfig cfg, StageValidation v)
        {
            if (cfg.Moves <= 0) return;   // 시간 제한 판은 수로 상한을 못 잡는다

            int ceiling = cfg.Moves * OptimisticClearPerMove(cfg);
            foreach (var o in cfg.Objectives)
            {
                switch (o.Type)
                {
                    case ObjectiveType.ClearCount:
                    case ObjectiveType.ClearColor:
                        if (o.Target > ceiling)
                            v.Errors.Add("목표 " + o.Target + " 은 " + cfg.Moves + "수로 도달할 수 없다"
                                         + " (낙관적 상한 " + ceiling + ")");
                        break;
                    case ObjectiveType.ReachScore:
                        int scoreCeiling = ceiling * Board.BaseTileScore * 4;
                        if (o.Target > scoreCeiling)
                            v.Errors.Add("점수 목표 " + o.Target + " 은 " + cfg.Moves + "수로 도달할 수 없다"
                                         + " (낙관적 상한 " + scoreCeiling + ")");
                        break;
                    case ObjectiveType.BreakObstacle:
                        if (o.Target > cfg.ObstacleCells)
                            v.Errors.Add(o.ObstacleType + " 목표 " + o.Target
                                         + " 이 배치된 장애물 수 " + cfg.ObstacleCells + " 보다 많다");
                        break;
                }
            }
        }
    }
}
