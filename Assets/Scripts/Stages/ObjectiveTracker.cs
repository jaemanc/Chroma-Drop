// ObjectiveTracker.cs — 목표 진행도. 설정이 준 목표 배열을 그대로 해석한다.
// 목표 종류를 늘리려면 여기 switch 에 한 갈래를 더한다. 스테이지 번호는 등장하지 않는다.

using System.Collections.Generic;

namespace ColorMatcher.Core
{
    /// <summary>목표 하나의 현재 상태.</summary>
    public struct ObjectiveProgress
    {
        public Objective Objective;
        public int Current;
        public int Target;
        public bool Done { get { return Current >= Target; } }
        public string Label;          // UI 표시용 (예: "coral 7/12")
    }

    public class ObjectiveTracker
    {
        readonly List<Objective> objectives;
        readonly ObjectiveMode mode;
        readonly int[] current;

        public ObjectiveTracker(IList<Objective> objectives, ObjectiveMode mode)
        {
            this.objectives = new List<Objective>(objectives);
            this.mode = mode;
            current = new int[this.objectives.Count];
        }

        public int Count { get { return objectives.Count; } }

        /// <summary>한 수의 결과를 반영한다. 점수는 누적값을 그대로 넘긴다.</summary>
        public void Apply(ResolveResult res, int totalScore)
        {
            for (int i = 0; i < objectives.Count; i++)
            {
                var o = objectives[i];
                switch (o.Type)
                {
                    case ObjectiveType.ClearCount:
                        current[i] += res.TilesDestroyed;
                        break;
                    case ObjectiveType.ClearColor:
                        if (o.ColorIndex >= 0 && o.ColorIndex < res.ClearedByColor.Length)
                            current[i] += res.ClearedByColor[o.ColorIndex];
                        break;
                    case ObjectiveType.BreakObstacle:
                        if (o.ObstacleType == ObstacleKind.Brick) current[i] += res.BricksBroken;
                        else if (o.ObstacleType == ObstacleKind.Frozen) current[i] += res.FrozenThawed;
                        break;
                    case ObjectiveType.ReachScore:
                        current[i] = totalScore;     // 누적값이라 더하지 않고 덮는다
                        break;
                }
            }
        }

        public bool Cleared
        {
            get
            {
                if (objectives.Count == 0) return false;
                for (int i = 0; i < objectives.Count; i++)
                {
                    bool done = current[i] >= objectives[i].Target;
                    if (mode == ObjectiveMode.All && !done) return false;
                    if (mode == ObjectiveMode.Any && done) return true;
                }
                return mode == ObjectiveMode.All;
            }
        }

        public List<ObjectiveProgress> Snapshot()
        {
            var list = new List<ObjectiveProgress>(objectives.Count);
            for (int i = 0; i < objectives.Count; i++)
            {
                var o = objectives[i];
                int cur = current[i] > o.Target ? o.Target : current[i];
                list.Add(new ObjectiveProgress
                {
                    Objective = o,
                    Current = cur,
                    Target = o.Target,
                    Label = LabelFor(o) + " " + cur + "/" + o.Target,
                });
            }
            return list;
        }

        static string LabelFor(Objective o)
        {
            switch (o.Type)
            {
                case ObjectiveType.ClearColor: return o.ColorName;
                case ObjectiveType.BreakObstacle: return o.ObstacleType;
                case ObjectiveType.ReachScore: return "score";
                default: return "blocks";
            }
        }
    }
}
