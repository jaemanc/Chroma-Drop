// CurveGenerator.cs — curve.config.json 의 파라미터로 stages.json 을 생성한다.
//
// 규칙: 한 스테이지에서 두 축이 동시에 움직이면 난이도 급등의 원인을 못 찾는다.
// 그래서 각 축의 '값이 바뀌는 스테이지'를 서로 겹치지 않게 배정한다.
// locked: true 인 스테이지는 손으로 조정한 것이므로 그대로 보존한다.
//
// 이 파일에도 스테이지 번호·목표 수치 리터럴은 없다. 전부 curve.config.json 에서 온다.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ColorMatcher.Core;
using UnityEngine;

public class CurveAxis
{
    public string Name;
    public double From, To;
    public int StartStage, EndStage;

    /// <summary>이 축이 거치는 값들. 중복은 접는다.</summary>
    public List<double> Steps(bool integer)
    {
        int n = integer ? Mathf.Max(1, Mathf.Abs(Mathf.RoundToInt((float)(To - From)))) : 8;
        return Build(n, integer);
    }

    /// <summary>주어진 단계 수에 맞춰 나눈 값들.</summary>
    public List<double> StepsCount(int n) { return Build(Mathf.Max(1, n), false); }

    List<double> Build(int n, bool integer)
    {
        var list = new List<double>();
        if (System.Math.Abs(To - From) < 1e-9) { list.Add(From); return list; }
        for (int i = 0; i <= n; i++)
        {
            double v = From + (To - From) * i / n;
            if (integer) v = System.Math.Round(v);
            if (list.Count == 0 || System.Math.Abs(list[list.Count - 1] - v) > 1e-9) list.Add(v);
        }
        return list;
    }
}

public class CurveReport
{
    public readonly List<string> Lines = new List<string>();
    public readonly List<string> Errors = new List<string>();
    public string Json = "";
}

public static class CurveGenerator
{
    public const string CurveFile = "curve.config.json";

    public static string CurvePath
    {
        get { return Path.Combine(Path.Combine(Application.streamingAssetsPath, StageLoader.FolderName), CurveFile); }
    }

    /// <summary>curve.config.json 을 읽어 stages.json 내용을 만든다. 파일로 쓰지는 않는다.</summary>
    public static CurveReport Generate(IList<StageConfig> keepLocked)
    {
        var rep = new CurveReport();
        if (!File.Exists(CurvePath)) { rep.Errors.Add("curve.config.json 이 없다: " + CurvePath); return rep; }

        var root = Json.AsMap(Json.Parse(File.ReadAllText(CurvePath)));
        if (root == null) { rep.Errors.Add("curve.config.json 을 읽을 수 없다"); return rep; }

        int count = (int)Json.Num(root, "stageCount", 0);
        if (count < 1) { rep.Errors.Add("stageCount 가 없다"); return rep; }

        var colors = new List<string>();
        var carr = Json.AsList(root.ContainsKey("colors") ? root["colors"] : null);
        if (carr != null) foreach (var c in carr) if (c is string) colors.Add((string)c);
        if (colors.Count == 0) { rep.Errors.Add("colors 가 비었다"); return rep; }

        var grid = Json.AsMap(root.ContainsKey("grid") ? root["grid"] : null);
        int gw = (int)Json.Num(grid, "width", 0), gh = (int)Json.Num(grid, "height", 0);
        if (gw < 4 || gh < 4) { rep.Errors.Add("grid 가 없다"); return rep; }

        var axes = ReadAxes(Json.AsMap(root.ContainsKey("axes") ? root["axes"] : null));
        if (axes.Count == 0) { rep.Errors.Add("axes 가 비었다"); return rep; }

        // 축마다 '값이 바뀌는 스테이지'를 배정한다. 정수 축을 먼저 놓고
        // 남은 자리를 실수 축이 나눠 갖는다 — 실수 축이 구간을 독점하지 않게.
        var takenBy = new Dictionary<int, string>();
        var schedule = new Dictionary<string, Dictionary<int, double>>();

        var ints = new List<CurveAxis>();
        var floats = new List<CurveAxis>();
        foreach (var ax in axes) (IsIntegerAxis(ax.Name) ? ints : floats).Add(ax);

        foreach (var ax in ints)
        {
            var steps = ax.Steps(true);
            var slots = new Dictionary<int, double> { { 1, steps[0] } };
            int placed = 0;
            for (int stage = ax.StartStage; stage <= ax.EndStage && placed < steps.Count - 1; stage++)
            {
                if (stage <= 1 || takenBy.ContainsKey(stage)) continue;
                takenBy[stage] = ax.Name;
                slots[stage] = steps[++placed];
            }
            if (placed < steps.Count - 1)
                rep.Errors.Add("축 '" + ax.Name + "' 의 변화 " + (steps.Count - 1)
                               + "단계를 구간 " + ax.StartStage + "~" + ax.EndStage + " 안에 배치할 수 없다");
            schedule[ax.Name] = slots;
        }

        foreach (var ax in floats)
        {
            var free = new List<int>();
            for (int stage = ax.StartStage; stage <= ax.EndStage; stage++)
                if (stage > 1 && !takenBy.ContainsKey(stage)) free.Add(stage);

            var steps = ax.StepsCount(free.Count);
            var slots = new Dictionary<int, double> { { 1, steps[0] } };
            for (int i = 1; i < steps.Count && i - 1 < free.Count; i++)
            {
                takenBy[free[i - 1]] = ax.Name;
                slots[free[i - 1]] = steps[i];
            }
            schedule[ax.Name] = slots;
        }

        if (rep.Errors.Count > 0) return rep;

        var obstacleMix = Json.AsMap(root.ContainsKey("obstacleMix") ? root["obstacleMix"] : null);
        var brickHits = ReadAxis("brickHitsToBreak", Json.AsMap(root.ContainsKey("brickHitsToBreak") ? root["brickHitsToBreak"] : null));
        var refillMode = Json.AsMap(root.ContainsKey("refillModeByStage") ? root["refillModeByStage"] : null);
        int seed = (int)Json.Num(root, "seed", 0);

        var locked = new Dictionary<int, StageConfig>();
        if (keepLocked != null) foreach (var s in keepLocked) if (s.Locked) locked[s.StageId] = s;

        var sb = new StringBuilder();
        sb.Append("{\n  \"version\": ").Append(StageLoader.SupportedVersion).Append(",\n  \"stages\": [\n");

        var rng = new System.Random(seed);
        for (int id = 1; id <= count; id++)
        {
            if (id > 1) sb.Append(",\n");

            if (locked.ContainsKey(id))
            {
                rep.Lines.Add("stage " + id + ": locked — 손으로 조정한 값을 보존한다");
                sb.Append(Reserialize(locked[id], colors));
                continue;
            }

            double fill = Held(schedule, "initialFillRatio", id);
            int blocks = (int)Held(schedule, "blocksPerClear", id);
            double obsRatio = Held(schedule, "obstacleRatio", id);
            int objCount = (int)Held(schedule, "objectiveCount", id);
            double objTarget = Held(schedule, "objectiveTarget", id);
            double slack = Held(schedule, "moveSlack", id);
            int minMatch = (int)Held(schedule, "minMatchLength", id);
            int hits = brickHits == null ? 1 : (int)System.Math.Round(Lerp(brickHits, id));

            string changed = takenBy.ContainsKey(id) ? takenBy[id] : "-";
            rep.Lines.Add("stage " + id + ": 변화축 " + changed
                          + " | fill " + fill.ToString("0.00", CultureInfo.InvariantCulture)
                          + " drip " + blocks + " obs " + obsRatio.ToString("0.00", CultureInfo.InvariantCulture)
                          + " obj " + objCount + "x" + (int)objTarget
                          + " match " + minMatch);

            sb.Append(BuildStage(id, gw, gh, fill, blocks, obsRatio, objCount, (int)objTarget,
                                 slack, minMatch, hits, colors, obstacleMix, refillMode, rng));
        }
        sb.Append("\n  ]\n}\n");
        rep.Json = sb.ToString();
        return rep;
    }

    // ---------- 한 스테이지 ----------

    static string BuildStage(int id, int w, int h, double fill, int blocks, double obsRatio,
                             int objCount, int objTarget, double slack, int minMatch, int hits,
                             List<string> colors, Dictionary<string, object> mix,
                             Dictionary<string, object> refillMode, System.Random rng)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        // 장애물 자리 — 가장자리를 피해 결정적으로 흩는다
        int obsCells = (int)(w * h * obsRatio);
        var slots = new List<Point>();
        var used = new HashSet<int>();
        for (int t = 0; t < obsCells * 60 && slots.Count < obsCells; t++)
        {
            int x = 1 + rng.Next(w - 2), y = 1 + rng.Next(h - 2);
            if (!used.Add(x * 1000 + y)) continue;
            slots.Add(new Point(x, y));
        }

        double mBrick = mix == null ? 1 : Json.Real(mix, ObstacleKind.Brick, 1);
        double mFrozen = mix == null ? 0 : Json.Real(mix, ObstacleKind.Frozen, 0);
        double mLocked = mix == null ? 0 : Json.Real(mix, ObstacleKind.Locked, 0);
        double mTotal = mBrick + mFrozen + mLocked;
        int nBrick = mTotal <= 0 ? slots.Count : (int)(slots.Count * mBrick / mTotal);
        int nFrozen = mTotal <= 0 ? 0 : (int)(slots.Count * mFrozen / mTotal);

        var brick = slots.GetRange(0, System.Math.Min(nBrick, slots.Count));
        var frozen = slots.GetRange(brick.Count, System.Math.Min(nFrozen, slots.Count - brick.Count));
        var lockedCells = slots.GetRange(brick.Count + frozen.Count, slots.Count - brick.Count - frozen.Count);

        string mode = "instant";
        if (refillMode != null)
        {
            int instantUntil = (int)Json.Num(refillMode, "instantUntil", 0);
            int noneFrom = (int)Json.Num(refillMode, "noneFrom", int.MaxValue);
            if (id >= noneFrom) mode = "none";
            else if (id > instantUntil) mode = "drip";
        }

        sb.Append("    {\n");
        sb.Append("      \"stageId\": ").Append(id).Append(",\n");
        sb.Append("      \"grid\": { \"width\": ").Append(w).Append(", \"height\": ").Append(h);
        sb.Append(", \"initialFillRatio\": ").Append(fill.ToString("0.00", inv));
        sb.Append(", \"fillPattern\": \"").Append(fill >= 1.0 ? "random" : "bottom_up").Append("\"");
        sb.Append(", \"presetLayout\": null },\n");

        sb.Append("      \"refill\": { \"enabled\": ").Append(mode == "none" ? "false" : "true");
        sb.Append(", \"mode\": \"").Append(mode).Append("\"");
        sb.Append(", \"blocksPerClear\": ").Append(mode == "drip" ? blocks : 0);
        sb.Append(", \"delayMs\": 150, \"colorWeights\": { ");
        for (int i = 0; i < colors.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(Json.Quote(colors[i])).Append(": ")
              .Append((1.0 / colors.Count).ToString("0.00", inv));
        }
        sb.Append(" } },\n");

        sb.Append("      \"obstacles\": [");
        bool first = true;
        first = AppendObstacle(sb, ObstacleKind.Brick, brick, hits, first);
        first = AppendObstacle(sb, ObstacleKind.Frozen, frozen, 0, first);
        first = AppendObstacle(sb, ObstacleKind.Locked, lockedCells, 0, first);
        sb.Append(first ? "]" : "\n      ]").Append(",\n");

        // 목표 — 개수만큼 종류를 늘린다. 종류 순서는 고정이라 결과가 결정적이다.
        sb.Append("      \"objectives\": [\n");
        sb.Append("        { \"type\": \"clear_count\", \"target\": ").Append(objTarget).Append(" }");
        // 리필이 없는 판에서는 색 목표를 걸 수 없다 — 신규 블록이 안 들어오므로
        // 판에 처음 깔린 개수가 상한이고, 그걸 넘는 목표는 영영 못 채운다.
        if (objCount >= 2 && colors.Count > 0 && mode != "none")
        {
            string color = colors[(id - 1) % colors.Count];
            int colorTarget = System.Math.Max(1, objTarget / (colors.Count + 1));
            sb.Append(",\n        { \"type\": \"clear_color\", \"color\": ").Append(Json.Quote(color))
              .Append(", \"target\": ").Append(colorTarget).Append(" }");
        }
        if (objCount >= 3)
        {
            int breakTarget = System.Math.Max(1, brick.Count / 2);
            if (brick.Count > 0)
                sb.Append(",\n        { \"type\": \"break_obstacle\", \"obstacleType\": \"brick\", \"target\": ")
                  .Append(breakTarget).Append(" }");
        }
        sb.Append("\n      ],\n");
        sb.Append("      \"objectiveMode\": \"all\",\n");

        int moves = (int)System.Math.Ceiling(objTarget * slack
                    / System.Math.Max(1, StageValidator.OptimisticClearPerMove(
                        new StageConfig { MinMatch = minMatch, Width = w, Height = h })) * 3);
        sb.Append("      \"limits\": { \"moves\": ").Append(System.Math.Max(5, moves))
          .Append(", \"timeSeconds\": null },\n");
        sb.Append("      \"matchRule\": { \"minMatchLength\": ").Append(minMatch)
          .Append(", \"allowDiagonal\": false }\n");
        sb.Append("    }");
        return sb.ToString();
    }

    static bool AppendObstacle(StringBuilder sb, string type, List<Point> cells, int hits, bool first)
    {
        if (cells.Count == 0) return first;
        sb.Append(first ? "\n" : ",\n");
        sb.Append("        { \"type\": \"").Append(type).Append("\", \"positions\": [");
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("[").Append(cells[i].X).Append(", ").Append(cells[i].Y).Append("]");
        }
        sb.Append("]");
        if (type == ObstacleKind.Brick) sb.Append(", \"hitsToBreak\": ").Append(hits);
        sb.Append(" }");
        return false;
    }

    // ---------- 축 읽기 ----------

    static List<CurveAxis> ReadAxes(Dictionary<string, object> m)
    {
        var list = new List<CurveAxis>();
        if (m == null) return list;
        // 순서를 고정해야 배정 결과가 결정적이다
        var names = new List<string>(m.Keys);
        names.Sort(System.StringComparer.Ordinal);
        foreach (var name in names)
        {
            var ax = ReadAxis(name, Json.AsMap(m[name]));
            if (ax != null) list.Add(ax);
        }
        return list;
    }

    static CurveAxis ReadAxis(string name, Dictionary<string, object> m)
    {
        if (m == null) return null;
        return new CurveAxis
        {
            Name = name,
            From = Json.Real(m, "from", 0),
            To = Json.Real(m, "to", 0),
            StartStage = (int)Json.Num(m, "startStage", 1),
            EndStage = (int)Json.Num(m, "endStage", 1),
        };
    }

    static bool IsIntegerAxis(string name)
    {
        // objectiveTarget 은 폭이 넓어 스테이지마다 조금씩 오른다 — 계단 축으로 두지 않는다.
        return name == "blocksPerClear" || name == "objectiveCount" || name == "minMatchLength";
    }

    /// <summary>배정된 변화 시점 중 이 스테이지 이하에서 가장 최근 값 (계단 유지).</summary>
    static double Held(Dictionary<string, Dictionary<int, double>> schedule, string axis, int id)
    {
        Dictionary<int, double> slots;
        if (!schedule.TryGetValue(axis, out slots)) return 0;
        double v = 0;
        int best = -1;
        foreach (var kv in slots)
            if (kv.Key <= id && kv.Key > best) { best = kv.Key; v = kv.Value; }
        return v;
    }

    static double Lerp(CurveAxis ax, int id)
    {
        if (id <= ax.StartStage) return ax.From;
        if (id >= ax.EndStage) return ax.To;
        double t = (id - ax.StartStage) / (double)(ax.EndStage - ax.StartStage);
        return ax.From + (ax.To - ax.From) * t;
    }

    /// <summary>locked 스테이지를 원본 그대로 다시 쓴다.</summary>
    static string Reserialize(StageConfig cfg, List<string> colors)
    {
        // 손으로 고친 판이므로 원본 JSON 을 그대로 보존하는 게 맞다.
        // 로더가 이미 파싱한 뒤라 원문이 없으므로, 같은 의미의 JSON 을 다시 만든다.
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append("    {\n      \"stageId\": ").Append(cfg.StageId).Append(",\n");
        sb.Append("      \"locked\": true,\n");
        sb.Append("      \"grid\": { \"width\": ").Append(cfg.Width).Append(", \"height\": ").Append(cfg.Height)
          .Append(", \"initialFillRatio\": ").Append(cfg.InitialFillRatio.ToString("0.00", inv))
          .Append(", \"fillPattern\": \"").Append(cfg.FillPattern == FillPattern.BottomUp ? "bottom_up" : "random")
          .Append("\", \"presetLayout\": null },\n");
        sb.Append("      \"refill\": { \"enabled\": ").Append(cfg.Refill.Mode == RefillMode.None ? "false" : "true")
          .Append(", \"mode\": \"").Append(cfg.Refill.Mode == RefillMode.Instant ? "instant"
                                        : cfg.Refill.Mode == RefillMode.Drip ? "drip" : "none")
          .Append("\", \"blocksPerClear\": ").Append(cfg.Refill.BlocksPerClear)
          .Append(", \"delayMs\": ").Append(cfg.Refill.DelayMs).Append(", \"colorWeights\": { ");
        for (int i = 0; i < colors.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            double wgt = cfg.ColorWeights != null && i < cfg.ColorWeights.Length
                       ? cfg.ColorWeights[i] : 1.0 / colors.Count;
            sb.Append(Json.Quote(colors[i])).Append(": ").Append(wgt.ToString("0.00", inv));
        }
        sb.Append(" } },\n      \"obstacles\": [");
        bool first = true;
        foreach (var o in cfg.Obstacles) first = AppendObstacle(sb, o.Type, o.Positions, o.HitsToBreak, first);
        sb.Append(first ? "]" : "\n      ]").Append(",\n      \"objectives\": [\n");
        for (int i = 0; i < cfg.Objectives.Count; i++)
        {
            var ob = cfg.Objectives[i];
            if (i > 0) sb.Append(",\n");
            sb.Append("        { \"type\": \"").Append(TypeName(ob.Type)).Append("\"");
            if (ob.Type == ObjectiveType.ClearColor) sb.Append(", \"color\": ").Append(Json.Quote(ob.ColorName));
            if (ob.Type == ObjectiveType.BreakObstacle) sb.Append(", \"obstacleType\": ").Append(Json.Quote(ob.ObstacleType));
            sb.Append(", \"target\": ").Append(ob.Target).Append(" }");
        }
        sb.Append("\n      ],\n      \"objectiveMode\": \"")
          .Append(cfg.ObjectiveMode == ObjectiveMode.Any ? "any" : "all").Append("\",\n");
        sb.Append("      \"limits\": { \"moves\": ").Append(cfg.Moves > 0 ? cfg.Moves.ToString() : "null")
          .Append(", \"timeSeconds\": ").Append(cfg.TimeSeconds > 0 ? cfg.TimeSeconds.ToString() : "null").Append(" },\n");
        sb.Append("      \"matchRule\": { \"minMatchLength\": ").Append(cfg.MinMatch)
          .Append(", \"allowDiagonal\": false }\n    }");
        return sb.ToString();
    }

    static string TypeName(ObjectiveType t)
    {
        switch (t)
        {
            case ObjectiveType.ClearColor: return "clear_color";
            case ObjectiveType.BreakObstacle: return "break_obstacle";
            case ObjectiveType.ReachScore: return "reach_score";
            default: return "clear_count";
        }
    }
}
