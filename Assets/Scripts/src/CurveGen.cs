// CurveGen.cs — curve.config.json 으로 stages.json 을 만든다.
// 한 스테이지에서 동시에 움직이는 축을 제한한다. 파생값(moves)은 목표가 바뀔 때만 따라 움직인다.
// 수동 조정한 스테이지(locked:true)는 원본을 그대로 보존한다.
//
// 이 파일에 스테이지 번호·목표 수치·색상값 리터럴은 없다. 전부 설정에서 온다.

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ChromaDrop.Engine
{
    public sealed class CurveResult
    {
        public string Json = "";
        public readonly List<string> Log = new List<string>();
        public readonly List<string> Errors = new List<string>();
    }

    public static class CurveGen
    {
        sealed class AxisPlan
        {
            public string Name;
            public double From, To;
            public int Start, End;
            public bool Integer;
            public readonly Dictionary<int, double> Slots = new Dictionary<int, double>();
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>스테이지 번호가 시작하는 값. 난이도 수치가 아니라 번호 체계의 기준점이다.
        /// 이 스테이지에는 모든 축이 시작값을 갖고, 축 변화는 그다음부터 배정된다.</summary>
        const int BaseStage = 1;

        public static CurveResult Generate(string curveJson, string existingStagesJson)
        {
            var res = new CurveResult();
            var cfg = MiniJson.Parse(curveJson) as Dictionary<string, object>;
            if (cfg == null) { res.Errors.Add("curve.config.json 파싱 실패"); return res; }

            int count = (int)N(cfg, "stageCount");
            int baseSeed = (int)N(cfg, "baseSeed");
            if (count < 1) { res.Errors.Add("stageCount 없음"); return res; }

            var grid = M(cfg, "grid");
            int size = (int)N(grid, "size");

            var topo = M(cfg, "topology");
            int forcedSquareUntil = (int)N(topo, "forcedSquareUntil");
            var allowed = SL(topo, "allowed");
            var topoWeights = DL(topo, "weights");

            var axes = PlanAxes(M(cfg, "axes"), res);
            if (res.Errors.Count > 0) return res;

            var locked = LockedStages(existingStagesJson);

            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": 1,\n  \"stages\": [\n");

            for (int id = BaseStage; id <= count; id++)
            {
                if (id > BaseStage) sb.Append(",\n");
                if (locked.ContainsKey(id))
                {
                    res.Log.Add("stage " + id + ": locked 보존");
                    sb.Append(locked[id]);
                    continue;
                }
                sb.Append(BuildStage(id, count, size, baseSeed, forcedSquareUntil, allowed,
                                     topoWeights, axes, cfg, res));
            }
            sb.Append("\n  ]\n}\n");
            res.Json = sb.ToString();
            return res;
        }

        // ---------- 축 배정 ----------
        // 각 축의 '값이 바뀌는 스테이지'가 서로 겹치지 않게 배치한다.
        static List<AxisPlan> PlanAxes(Dictionary<string, object> m, CurveResult res)
        {
            var plans = new List<AxisPlan>();
            if (m == null) { res.Errors.Add("axes 없음"); return plans; }

            var names = new List<string>(m.Keys);
            names.Sort(System.StringComparer.Ordinal);   // 결정성
            foreach (var name in names)
            {
                var a = M(m, name);
                plans.Add(new AxisPlan
                {
                    Name = name,
                    From = N(a, "from"),
                    To = N(a, "to"),
                    Start = (int)N(a, "startStage"),
                    End = (int)N(a, "endStage"),
                    Integer = IsInteger(name),
                });
            }

            // 배정 순서가 결과를 좌우하므로 두 단계로 나눈다.
            // 1) 정수 축은 필요한 변화 횟수가 정해져 있으니 좁은 구간부터 먼저 자리를 잡는다.
            // 2) 실수 축은 남은 자리를 나눠 갖고, 받은 자리 수에 맞춰 값을 나눈다.
            var taken = new HashSet<int>();

            var ints = new List<AxisPlan>();
            var floats = new List<AxisPlan>();
            foreach (var p in plans) (p.Integer ? ints : floats).Add(p);

            ints.Sort((x, y) =>
            {
                int wx = x.End - x.Start + 1, wy = y.End - y.Start + 1;
                int nx = NeededSteps(x), ny = NeededSteps(y);
                double rx = nx == 0 ? double.MaxValue : wx / (double)nx;
                double ry = ny == 0 ? double.MaxValue : wy / (double)ny;
                int c = rx.CompareTo(ry);
                return c != 0 ? c : System.StringComparer.Ordinal.Compare(x.Name, y.Name);
            });

            foreach (var p in ints)
            {
                var steps = Steps(p, NeededSteps(p));
                p.Slots[BaseStage] = steps[0];
                int placed = 0;
                for (int stage = p.Start; stage <= p.End && placed < steps.Count - 1; stage++)
                {
                    if (stage <= BaseStage || taken.Contains(stage)) continue;
                    taken.Add(stage);
                    p.Slots[stage] = steps[++placed];
                }
                if (placed < steps.Count - 1)
                    res.Errors.Add("축 '" + p.Name + "' 변화 " + (steps.Count - 1)
                                   + "단계를 " + p.Start + "~" + p.End + " 안에 못 넣는다");
            }

            foreach (var p in floats)
            {
                var free = new List<int>();
                for (int stage = p.Start; stage <= p.End; stage++)
                    if (stage > BaseStage && !taken.Contains(stage)) free.Add(stage);

                var steps = Steps(p, free.Count);
                p.Slots[BaseStage] = steps[0];
                for (int i = 1; i < steps.Count && i - 1 < free.Count; i++)
                {
                    taken.Add(free[i - 1]);
                    p.Slots[free[i - 1]] = steps[i];
                }
                if (steps.Count <= 1 && System.Math.Abs(p.To - p.From) > 1e-12)
                    res.Errors.Add("축 '" + p.Name + "' 이 쓸 스테이지가 없다 ("
                                   + p.Start + "~" + p.End + ")");
            }

            return plans;
        }

        /// <summary>정수 축이 거쳐야 하는 변화 횟수.</summary>
        static int NeededSteps(AxisPlan p)
        {
            return (int)System.Math.Abs(System.Math.Round(p.To - p.From));
        }

        static bool IsInteger(string name)
        {
            return name == "blocksPerClear" || name == "paletteSize" || name == "minGroupSize"
                || name == "rerollCount" || name == "objectiveCount";
        }

        /// <summary>변화 횟수 n 에 맞춰 From→To 를 나눈 값들. 중복은 접는다.</summary>
        static List<double> Steps(AxisPlan p, int n)
        {
            var list = new List<double>();
            if (System.Math.Abs(p.To - p.From) < 1e-12 || n < 1) { list.Add(p.From); return list; }

            for (int i = 0; i <= n; i++)
            {
                double v = p.From + (p.To - p.From) * i / n;
                if (p.Integer) v = System.Math.Round(v);
                if (list.Count == 0 || System.Math.Abs(list[list.Count - 1] - v) > 1e-12) list.Add(v);
            }
            return list;
        }

        static double Held(List<AxisPlan> plans, string name, int id)
        {
            foreach (var p in plans)
            {
                if (p.Name != name) continue;
                double v = 0; int best = -1;
                foreach (var kv in p.Slots)
                    if (kv.Key <= id && kv.Key > best) { best = kv.Key; v = kv.Value; }
                return v;
            }
            return 0;
        }

        static bool ChangedAt(List<AxisPlan> plans, int id)
        {
            foreach (var p in plans) if (p.Slots.ContainsKey(id) && id > BaseStage) return true;
            return false;
        }

        // ---------- 한 스테이지 ----------
        static string BuildStage(int id, int count, int size, int baseSeed, int forcedSquareUntil,
                                 List<string> allowed, double[] topoWeights, List<AxisPlan> axes,
                                 Dictionary<string, object> cfg, CurveResult res)
        {
            double fill = Held(axes, "initialFillRatio", id);
            int blocks = (int)Held(axes, "blocksPerClear", id);
            double obsRatio = Held(axes, "obstacleRatio", id);
            int paletteSize = (int)Held(axes, "paletteSize", id);
            int minGroup = (int)Held(axes, "minGroupSize", id);
            int reroll = (int)Held(axes, "rerollCount", id);
            int objCount = (int)Held(axes, "objectiveCount", id);

            // 목표는 스테이지 번호에 비례한다. moves 는 목표에서 파생되므로 별도 축이 아니다.
            var tgt = M(cfg, "objectiveTarget");
            double t = count > BaseStage ? (id - BaseStage) / (double)(count - BaseStage) : 0;
            int target = (int)System.Math.Round(N(tgt, "from") + (N(tgt, "to") - N(tgt, "from")) * t);

            var slackCfg = M(cfg, "moveSlack");
            double slack = N(slackCfg, "from") + (N(slackCfg, "to") - N(slackCfg, "from")) * t;

            int seed = baseSeed + id;
            bool forceSquare = id <= forcedSquareUntil;

            // 토폴로지 보정: 이웃이 많을수록 어렵다
            var mods = M(cfg, "topologyModifiers");
            string resolved = ResolveTopology(seed, forceSquare, allowed, topoWeights);
            int neighbors = NeighborCount(resolved);
            int extra = neighbors - (int)N(mods, "neighborBaseline");
            if (extra < 0) extra = 0;

            // 토폴로지 보정은 moves 에만 건다.
            // palette/minGroup 까지 흔들면 토폴로지가 바뀌는 스테이지마다 축이 셋씩 움직여
            // 난이도 급등의 원인을 짚을 수 없게 된다.
            int effPalette = paletteSize;
            int effMinGroup = minGroup;

            int perMove = effMinGroup * effMinGroup * 3;
            int moves = (int)System.Math.Ceiling(target * slack / perMove);
            moves += extra * (int)N(mods, "movesPerExtraNeighbor");
            if (moves < 5) moves = 5;

            var rf = M(cfg, "refillModeByStage");
            string mode = id >= (int)N(rf, "noneFrom") ? "none"
                        : id <= (int)N(rf, "instantUntil") ? "instant" : "drip";

            var hits = M(cfg, "brickHitsToBreak");
            int hitsToBreak = (int)System.Math.Round(N(hits, "from") + (N(hits, "to") - N(hits, "from")) * t);
            if (hitsToBreak < 1) hitsToBreak = 1;

            res.Log.Add("stage " + id + ": " + resolved + " palette " + effPalette
                        + " group " + effMinGroup + " fill " + fill.ToString("0.00", Inv)
                        + " obs " + obsRatio.ToString("0.00", Inv) + " target " + target
                        + " moves " + moves + (ChangedAt(axes, id) ? " *축변화" : ""));

            var sb = new StringBuilder();
            sb.Append("    {\n");
            sb.Append("      \"stageId\": ").Append(id).Append(",\n");
            sb.Append("      \"seed\": ").Append(seed).Append(",\n");
            sb.Append("      \"locked\": false,\n");

            sb.Append("      \"topology\": { \"mode\": ")
              .Append(forceSquare ? "\"fixed\"" : "\"random\"")
              .Append(", \"allowed\": [");
            var list = forceSquare ? new List<string> { TopologyGen.Square } : allowed;
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(list[i]).Append('"');
            }
            sb.Append("], \"weights\": [");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                double w = forceSquare ? 1.0 : (i < topoWeights.Length ? topoWeights[i] : 0);
                sb.Append(w.ToString("0.00", Inv));
            }
            sb.Append("] },\n");

            var pal = M(cfg, "palette");
            var satR = DL(pal, "satRange");
            var litR = DL(pal, "lightRange");
            sb.Append("      \"palette\": { \"size\": ").Append(effPalette)
              .Append(", \"hueSpread\": ").Append(N(pal, "hueSpread").ToString("0.##", Inv))
              .Append(", \"satRange\": [").Append(satR[0].ToString("0.##", Inv)).Append(", ")
              .Append(satR[1].ToString("0.##", Inv)).Append("]")
              .Append(", \"lightRange\": [").Append(litR[0].ToString("0.##", Inv)).Append(", ")
              .Append(litR[1].ToString("0.##", Inv)).Append("]")
              .Append(", \"minDeltaE\": ").Append(N(pal, "minDeltaE").ToString("0.##", Inv))
              .Append(", \"minContrast\": ").Append(N(pal, "minContrast").ToString("0.##", Inv))
              .Append(" },\n");

            sb.Append("      \"grid\": { \"size\": ").Append(size)
              .Append(", \"initialFillRatio\": ").Append(fill.ToString("0.00", Inv))
              .Append(", \"fillPattern\": \"").Append(fill >= 1.0 ? "random" : "bottom_up")
              .Append("\" },\n");

            sb.Append("      \"pieces\": { \"shapes\": [\"auto\"], \"colorsPerPiece\": 1, \"weights\": [], \"rerollCount\": ")
              .Append(reroll).Append(" },\n");

            sb.Append("      \"refill\": { \"mode\": \"").Append(mode)
              .Append("\", \"blocksPerClear\": ").Append(mode == "drip" ? blocks : 0)
              .Append(", \"delayMs\": 150, \"colorWeights\": [");
            for (int i = 0; i < effPalette; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append((1.0 / effPalette).ToString("0.0000", Inv));
            }
            sb.Append("] },\n");

            sb.Append("      \"obstaclePlacement\": { \"ratio\": ").Append(obsRatio.ToString("0.00", Inv))
              .Append(", \"pattern\": \"").Append(ObstaclePattern(seed))
              .Append("\", \"avoidSpawnCells\": true, \"hitsToBreak\": ").Append(hitsToBreak)
              .Append(", \"mix\": ");
            var mix = M(cfg, "obstacleMix");
            sb.Append("{ \"brick\": ").Append(N(mix, "brick").ToString("0.##", Inv))
              .Append(", \"frozen\": ").Append(N(mix, "frozen").ToString("0.##", Inv))
              .Append(", \"locked\": ").Append(N(mix, "locked").ToString("0.##", Inv)).Append(" } },\n");

            var items = M(cfg, "items");
            sb.Append("      \"items\": { \"available\": [");
            var av = SL(items, "available");
            for (int i = 0; i < av.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(av[i]).Append('"');
            }
            sb.Append("], \"itemCostsMove\": ").Append(N(items, "itemCostsMove") != 0 ? "true" : "false")
              .Append(", \"burstRadius\": ").Append((int)N(items, "burstRadius"))
              .Append(", \"ringRadius\": ").Append((int)N(items, "ringRadius"))
              .Append(", \"dropFromMatch\": {} },\n");

            sb.Append("      \"objectives\": [\n");
            sb.Append("        { \"type\": \"clear_count\", \"target\": ").Append(target).Append(" }");
            // 리필이 없는 판(소모전)에는 색 목표를 걸 수 없다.
            // 신규 블록이 안 들어오므로 처음 깔린 개수가 상한이고, 그걸 넘으면 영영 못 채운다.
            if (objCount >= 2 && mode != "none")
                sb.Append(",\n        { \"type\": \"clear_color\", \"colorIndex\": ")
                  .Append((id - BaseStage) % effPalette)
                  .Append(", \"target\": ").Append(System.Math.Max(1, target / (effPalette + 1))).Append(" }");
            if (objCount >= 3)
                sb.Append(",\n        { \"type\": \"clear_group_size\", \"groupSize\": ")
                  .Append(effMinGroup + 1)
                  .Append(", \"target\": ").Append(System.Math.Max(1, moves / 4)).Append(" }");
            sb.Append("\n      ],\n");
            sb.Append("      \"objectiveMode\": \"all\",\n");
            sb.Append("      \"limits\": { \"moves\": ").Append(moves).Append(", \"timeSeconds\": 0 },\n");
            sb.Append("      \"matchRule\": { \"minGroupSize\": ").Append(effMinGroup)
              .Append(", \"chainReaction\": true }\n");
            sb.Append("    }");
            return sb.ToString();
        }

        static string ObstaclePattern(int seed)
        {
            string[] p = { "scattered", "wall", "cluster", "ring" };
            return p[new Rng(seed, Stream.Topology).Next(p.Length)];
        }

        public static string ResolveTopology(int seed, bool forceSquare,
                                             List<string> allowed, double[] weights)
        {
            if (forceSquare || allowed == null || allowed.Count == 0) return TopologyGen.Square;
            if (allowed.Count == 1) return allowed[0];
            int i = new Rng(seed, Stream.Topology).Weighted(weights);
            if (i < 0 || i >= allowed.Count) i = 0;
            return allowed[i];
        }

        public static int NeighborCount(string kind)
        {
            if (kind == TopologyGen.Triangle) return 3;
            if (kind == TopologyGen.Hex) return 6;
            return 4;
        }

        // ---------- locked 보존 ----------
        static Dictionary<int, string> LockedStages(string stagesJson)
        {
            var map = new Dictionary<int, string>();
            if (string.IsNullOrEmpty(stagesJson)) return map;

            // 원문을 그대로 보존해야 손으로 넣은 값이 왜곡되지 않는다.
            int i = 0;
            while (true)
            {
                int start = stagesJson.IndexOf("    {", i);
                if (start < 0) break;
                int depth = 0, j = start;
                for (; j < stagesJson.Length; j++)
                {
                    if (stagesJson[j] == '{') depth++;
                    else if (stagesJson[j] == '}') { depth--; if (depth == 0) break; }
                }
                if (j >= stagesJson.Length) break;
                string chunk = stagesJson.Substring(start, j - start + 1);
                i = j + 1;

                if (chunk.IndexOf("\"locked\": true") < 0) continue;
                var m = MiniJson.Parse(chunk) as Dictionary<string, object>;
                if (m == null) continue;
                map[(int)N(m, "stageId")] = chunk;
            }
            return map;
        }

        // ---------- 읽기 헬퍼 ----------
        static Dictionary<string, object> M(Dictionary<string, object> m, string k)
        {
            object v;
            return m != null && m.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }
        static double N(Dictionary<string, object> m, string k)
        {
            object v;
            if (m != null && m.TryGetValue(k, out v))
            {
                if (v is double) return (double)v;
                if (v is bool) return (bool)v ? 1 : 0;
            }
            return 0;
        }
        static List<string> SL(Dictionary<string, object> m, string k)
        {
            var outp = new List<string>();
            object v;
            if (m == null || !m.TryGetValue(k, out v)) return outp;
            var l = v as List<object>;
            if (l == null) return outp;
            foreach (var o in l) if (o is string) outp.Add((string)o);
            return outp;
        }
        static double[] DL(Dictionary<string, object> m, string k)
        {
            object v;
            if (m == null || !m.TryGetValue(k, out v)) return new double[0];
            var l = v as List<object>;
            if (l == null) return new double[0];
            var outp = new double[l.Count];
            for (int i = 0; i < l.Count; i++) outp[i] = l[i] is double ? (double)l[i] : 0;
            return outp;
        }
    }
}
