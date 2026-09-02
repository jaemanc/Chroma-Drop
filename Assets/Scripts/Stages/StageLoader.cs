// StageLoader.cs — stages.json 을 읽고, 스키마 규칙에 맞는지 검사하고, 설정 객체로 바꾼다.
//
// 이 파일에는 스테이지 번호도 목표 수치도 리터럴로 들어가지 않는다.
// 값은 전부 설정 파일에서 온다. 여기 있는 숫자는 '형식이 성립하는 최소 조건' 뿐이다.
//
// 읽는 순서:
//   1) persistentDataPath/stages/stages.json   — 빌드 후에도 교체 가능한 자리
//   2) StreamingAssets/stages/stages.json      — 프로젝트에 들어 있는 기본값
// 1 이 있으면 1 을 쓴다. 밸런싱 담당자는 1 에 파일만 놓으면 되고 빌드가 필요 없다.

using System.Collections.Generic;
using System.IO;
using ColorMatcher.Core;
using UnityEngine;

public class StageLoadReport
{
    public bool Ok { get { return Errors.Count == 0; } }
    public string SourcePath = "";
    public readonly List<string> Errors = new List<string>();
    public readonly List<string> Warnings = new List<string>();
    public readonly List<StageConfig> Stages = new List<StageConfig>();
}

public static class StageLoader
{
    public const string FolderName = "stages";
    public const string StagesFile = "stages.json";
    public const int SupportedVersion = 1;

    static StageLoadReport cached;

    /// <summary>빌드 후에도 사람이 고칠 수 있는 자리. 여기 파일이 있으면 우선한다.</summary>
    public static string OverridePath
    {
        get { return Path.Combine(Path.Combine(Application.persistentDataPath, FolderName), StagesFile); }
    }

    public static string BuiltInPath
    {
        get { return Path.Combine(Path.Combine(Application.streamingAssetsPath, FolderName), StagesFile); }
    }

    public static StageLoadReport Report { get { if (cached == null) Load(); return cached; } }
    public static IList<StageConfig> All { get { return Report.Stages; } }
    public static int Count { get { return Report.Stages.Count; } }

    /// <summary>다시 읽는다. 게임 재시작 없이 반영하고 싶을 때 쓴다.</summary>
    public static StageLoadReport Reload() { cached = null; return Report; }

    public static StageConfig Get(int stageId)
    {
        foreach (var s in All) if (s.StageId == stageId) return s;
        return null;
    }

    static StageLoadReport Load()
    {
        cached = new StageLoadReport();

        string text = null;
        if (File.Exists(OverridePath)) { text = File.ReadAllText(OverridePath); cached.SourcePath = OverridePath; }
        else if (File.Exists(BuiltInPath)) { text = File.ReadAllText(BuiltInPath); cached.SourcePath = BuiltInPath; }

        if (text == null)
        {
            cached.Errors.Add("stages.json 을 찾지 못했다: " + BuiltInPath);
            return cached;
        }

        var root = Json.AsMap(Json.Parse(text));
        if (root == null) { cached.Errors.Add("stages.json 을 JSON 으로 읽을 수 없다"); return cached; }

        long version = Json.Num(root, "version", -1);
        if (version != SupportedVersion)
        {
            cached.Errors.Add("지원하지 않는 스키마 버전 " + version + " (지원: " + SupportedVersion + ")");
            return cached;
        }

        var arr = Json.AsList(root.ContainsKey("stages") ? root["stages"] : null);
        if (arr == null) { cached.Errors.Add("stages 배열이 없다"); return cached; }

        var seenIds = new HashSet<int>();
        for (int i = 0; i < arr.Count; i++)
        {
            var m = Json.AsMap(arr[i]);
            if (m == null) { cached.Errors.Add("stages[" + i + "] 가 객체가 아니다"); continue; }

            var cfg = ParseStage(m, cached);
            if (cfg == null) continue;
            if (!seenIds.Add(cfg.StageId))
                cached.Errors.Add("stageId " + cfg.StageId + " 가 중복이다");
            cached.Stages.Add(cfg);
        }

        cached.Stages.Sort((a, b) => a.StageId.CompareTo(b.StageId));
        for (int i = 0; i < cached.Stages.Count; i++)
            if (cached.Stages[i].StageId != i + 1)
                cached.Warnings.Add("stageId 가 1부터 연속이 아니다: " + cached.Stages[i].StageId + " (" + (i + 1) + " 번째)");

        return cached;
    }

    static StageConfig ParseStage(Dictionary<string, object> m, StageLoadReport rep)
    {
        var cfg = new StageConfig();
        cfg.StageId = (int)Json.Num(m, "stageId", 0);
        if (cfg.StageId < 1) { rep.Errors.Add("stageId 가 없거나 1 미만이다"); return null; }
        string at = "stage " + cfg.StageId + ": ";
        cfg.Locked = Json.Bool(m, "locked", false);

        // ---- grid ----
        var grid = Json.AsMap(m.ContainsKey("grid") ? m["grid"] : null);
        if (grid == null) { rep.Errors.Add(at + "grid 가 없다"); return null; }
        cfg.Width = (int)Json.Num(grid, "width", 0);
        cfg.Height = (int)Json.Num(grid, "height", 0);
        if (cfg.Width < 4 || cfg.Height < 4) { rep.Errors.Add(at + "grid 크기가 너무 작다"); return null; }
        cfg.InitialFillRatio = Json.Real(grid, "initialFillRatio", 1.0);
        if (cfg.InitialFillRatio <= 0 || cfg.InitialFillRatio > 1.0)
        { rep.Errors.Add(at + "initialFillRatio 는 0 초과 1 이하여야 한다"); return null; }

        switch (Json.Str(grid, "fillPattern", "random"))
        {
            case "bottom_up": cfg.FillPattern = FillPattern.BottomUp; break;
            case "random": cfg.FillPattern = FillPattern.Random; break;
            case "preset": cfg.FillPattern = FillPattern.Preset; break;
            default: rep.Errors.Add(at + "알 수 없는 fillPattern"); return null;
        }

        // ---- matchRule ----
        var mr = Json.AsMap(m.ContainsKey("matchRule") ? m["matchRule"] : null);
        if (mr == null) { rep.Errors.Add(at + "matchRule 이 없다"); return null; }
        cfg.MinMatch = (int)Json.Num(mr, "minMatchLength", 0);
        if (cfg.MinMatch < 2) { rep.Errors.Add(at + "minMatchLength 는 2 이상이어야 한다"); return null; }
        if (cfg.MinMatch > cfg.Width || cfg.MinMatch > cfg.Height)
        { rep.Errors.Add(at + "minMatchLength 가 보드보다 크다"); return null; }
        if (Json.Bool(mr, "allowDiagonal", false))
        { rep.Errors.Add(at + "이 게임에는 대각선 매칭이 없다. allowDiagonal 은 false 여야 한다"); return null; }

        // ---- refill ----
        var rf = Json.AsMap(m.ContainsKey("refill") ? m["refill"] : null);
        if (rf == null) { rep.Errors.Add(at + "refill 이 없다"); return null; }
        var pol = new RefillPolicy();
        bool enabled = Json.Bool(rf, "enabled", true);
        switch (Json.Str(rf, "mode", "instant"))
        {
            case "instant": pol.Mode = RefillMode.Instant; break;
            case "drip": pol.Mode = RefillMode.Drip; break;
            case "none": pol.Mode = RefillMode.None; break;
            default: rep.Errors.Add(at + "알 수 없는 refill.mode"); return null;
        }
        if (!enabled) pol.Mode = RefillMode.None;
        pol.BlocksPerClear = (int)Json.Num(rf, "blocksPerClear", 0);
        pol.DelayMs = (int)Json.Num(rf, "delayMs", 0);
        if (pol.Mode == RefillMode.Drip && pol.BlocksPerClear < 1)
        { rep.Errors.Add(at + "drip 인데 blocksPerClear 가 0 이다 — none 과 구분되지 않는다"); return null; }
        cfg.Refill = pol;

        // ---- colorWeights ----
        var cw = Json.AsMap(rf.ContainsKey("colorWeights") ? rf["colorWeights"] : null);
        if (cw != null && cw.Count > 0)
        {
            var names = new List<string>(cw.Keys);
            names.Sort(System.StringComparer.Ordinal);   // 순서를 결정적으로 고정한다
            cfg.ColorNames = names.ToArray();
            cfg.ColorWeights = new double[names.Count];
            for (int i = 0; i < names.Count; i++) cfg.ColorWeights[i] = Json.Real(cw, names[i], 0);
        }

        // ---- obstacles ----
        var obs = Json.AsList(m.ContainsKey("obstacles") ? m["obstacles"] : null);
        if (obs != null)
            foreach (var item in obs)
            {
                var om = Json.AsMap(item);
                if (om == null) continue;
                string type = Json.Str(om, "type", "");
                int cell;
                if (!ObstacleKind.TryCell(type, out cell))
                { rep.Errors.Add(at + "알 수 없는 장애물 type '" + type + "'"); return null; }

                var spec = new ObstacleSpec { Type = type, Cell = cell };
                spec.HitsToBreak = (int)Json.Num(om, "hitsToBreak", 1);
                if (cell == Board.Obstacle && spec.HitsToBreak < 1)
                { rep.Errors.Add(at + "brick 의 hitsToBreak 는 1 이상이어야 한다"); return null; }

                var ps = Json.AsList(om.ContainsKey("positions") ? om["positions"] : null);
                if (ps != null)
                    foreach (var p in ps)
                    {
                        var pair = Json.AsList(p);
                        if (pair == null || pair.Count < 2) continue;
                        int x = (int)(double)pair[0], y = (int)(double)pair[1];
                        if (x < 0 || y < 0 || x >= cfg.Width || y >= cfg.Height)
                        { rep.Errors.Add(at + "장애물 좌표가 보드 밖이다 (" + x + "," + y + ")"); return null; }
                        spec.Positions.Add(new Point(x, y));
                    }
                cfg.Obstacles.Add(spec);
            }

        // ---- objectives ----
        var objs = Json.AsList(m.ContainsKey("objectives") ? m["objectives"] : null);
        if (objs == null || objs.Count == 0) { rep.Errors.Add(at + "objectives 가 비었다"); return null; }
        foreach (var item in objs)
        {
            var om = Json.AsMap(item);
            if (om == null) continue;
            var ob = new Objective();
            switch (Json.Str(om, "type", ""))
            {
                case "clear_count": ob.Type = ObjectiveType.ClearCount; break;
                case "clear_color": ob.Type = ObjectiveType.ClearColor; break;
                case "break_obstacle": ob.Type = ObjectiveType.BreakObstacle; break;
                case "reach_score": ob.Type = ObjectiveType.ReachScore; break;
                default: rep.Errors.Add(at + "알 수 없는 objective type"); return null;
            }
            ob.Target = (int)Json.Num(om, "target", 0);
            if (ob.Target < 1) { rep.Errors.Add(at + "objective target 은 1 이상이어야 한다"); return null; }

            if (ob.Type == ObjectiveType.ClearColor)
            {
                ob.ColorName = Json.Str(om, "color", null);
                if (ob.ColorName == null) { rep.Errors.Add(at + "clear_color 에 color 가 없다"); return null; }
                ob.ColorIndex = IndexOfColor(cfg, ob.ColorName);
                if (ob.ColorIndex < 0)
                { rep.Errors.Add(at + "color '" + ob.ColorName + "' 가 colorWeights 에 없다"); return null; }
            }
            if (ob.Type == ObjectiveType.BreakObstacle)
            {
                ob.ObstacleType = Json.Str(om, "obstacleType", null);
                if (ob.ObstacleType == ObstacleKind.Locked)
                { rep.Errors.Add(at + "locked 는 부술 수 없어 목표가 될 수 없다"); return null; }
                if (ob.ObstacleType != ObstacleKind.Brick && ob.ObstacleType != ObstacleKind.Frozen)
                { rep.Errors.Add(at + "break_obstacle 의 obstacleType 이 잘못됐다"); return null; }
            }
            cfg.Objectives.Add(ob);
        }
        cfg.ObjectiveMode = Json.Str(m, "objectiveMode", "all") == "any"
                          ? ObjectiveMode.Any : ObjectiveMode.All;

        // ---- limits ----
        var lim = Json.AsMap(m.ContainsKey("limits") ? m["limits"] : null);
        cfg.Moves = Json.Has(lim, "moves") ? (int)Json.Num(lim, "moves", 0) : 0;
        cfg.TimeSeconds = Json.Has(lim, "timeSeconds") ? (int)Json.Num(lim, "timeSeconds", 0) : 0;
        if (cfg.Moves <= 0 && cfg.TimeSeconds <= 0)
        { rep.Errors.Add(at + "moves 와 timeSeconds 가 둘 다 없다 — 실패할 방법이 없는 판이다"); return null; }

        return cfg;
    }

    static int IndexOfColor(StageConfig cfg, string name)
    {
        if (cfg.ColorNames == null) return -1;
        for (int i = 0; i < cfg.ColorNames.Length; i++)
            if (cfg.ColorNames[i] == name) return i;
        return -1;
    }
}
