// ObjectiveTracker.cs — 목표 진행도. 설정이 준 목표 배열을 그대로 해석한다.
// 아이템으로 없앤 칸은 clear_group_size 에 세지 않는다 (minGroupSize 를 무시하고 터지므로).

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public struct ObjectiveProgress
    {
        public ObjectiveDef Def;
        public int Current;
        public int Target;
        public bool Done { get { return Current >= Target; } }
        public string Label;
    }

    public sealed class ObjectiveTracker
    {
        readonly List<ObjectiveDef> defs;
        readonly ObjectiveMode mode;
        readonly int[] current;

        public ObjectiveTracker(StageDef stage)
        {
            defs = new List<ObjectiveDef>(stage.Objectives);
            mode = stage.Mode;
            current = new int[defs.Count];
        }

        public int Count { get { return defs.Count; } }

        /// <summary>한 수의 결과를 반영한다. fromItem 이면 그룹 크기 목표에는 세지 않는다.</summary>
        public void Apply(ClearResult res, int totalScore, bool fromItem)
        {
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                switch (d.Type)
                {
                    case "clear_count":
                        current[i] += res.Cleared.Count;
                        break;
                    case "clear_color":
                        if (d.ColorIndex >= 0 && d.ColorIndex < res.ClearedByColor.Length)
                            current[i] += res.ClearedByColor[d.ColorIndex];
                        break;
                    case "break_obstacle":
                        if (d.ObstacleType == "frozen") current[i] += res.FrozenThawed;
                        else current[i] += res.BricksBroken;
                        break;
                    case "reach_score":
                        current[i] = totalScore;
                        break;
                    case "clear_group_size":
                        if (!fromItem && res.LargestGroup >= d.GroupSize) current[i]++;
                        break;
                }
            }
        }

        public bool Cleared
        {
            get
            {
                if (defs.Count == 0) return false;
                for (int i = 0; i < defs.Count; i++)
                {
                    bool done = current[i] >= defs[i].Target;
                    if (mode == ObjectiveMode.All && !done) return false;
                    if (mode == ObjectiveMode.Any && done) return true;
                }
                return mode == ObjectiveMode.All;
            }
        }

        public List<ObjectiveProgress> Snapshot()
        {
            var list = new List<ObjectiveProgress>(defs.Count);
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                int cur = current[i] > d.Target ? d.Target : current[i];
                list.Add(new ObjectiveProgress
                {
                    Def = d, Current = cur, Target = d.Target,
                    Label = LabelFor(d) + " " + cur + "/" + d.Target,
                });
            }
            return list;
        }

        static string LabelFor(ObjectiveDef d)
        {
            switch (d.Type)
            {
                case "clear_color": return "C" + d.ColorIndex;
                case "break_obstacle": return d.ObstacleType ?? "brick";
                case "reach_score": return "score";
                case "clear_group_size": return d.GroupSize + "+";
                default: return "blocks";
            }
        }
    }
}
