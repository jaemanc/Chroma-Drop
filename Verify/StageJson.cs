// StageJson.cs — VERIFY 전용 stages.json 리더. 런타임 로더와 같은 파일을 읽는다.
// (런타임 StageLoader 는 UnityEngine 에 묶여 있어 콘솔에서 못 쓰므로 최소 리더를 따로 둔다.)

using System;
using System.Collections.Generic;
using System.Globalization;
using ChromaDrop.Engine;
using System.IO;

class StageObjective
{
    public string Type;
    public int Target;
    public int ColorIndex = -1;
    public string ObstacleType;
}

class StageRow
{
    public int StageId;
    public bool Locked;
    public string TopologyMode;
    public int GridSize;
    public double InitialFillRatio;
    public string FillPattern;
    public int PaletteSize;
    public double[] ColorWeights = new double[0];
    public string RefillMode;
    public int BlocksPerClear;
    public int ObstacleRatioPermille;
    public int RerollCount;
    public int MinGroupSize;
    public int Moves;
    public int TimeSeconds;
    public readonly List<StageObjective> Objectives = new List<StageObjective>();
}

class StageReport
{
    public readonly List<string> Errors = new List<string>();
    public readonly List<StageRow> Stages = new List<StageRow>();
}

static class StageJson
{
    public const string Path = "stages/stages.json";
    public const int ForcedSquareUntil = 5;

    public static int OptimisticPerMove(StageRow s)
    {
        int group = s.MinGroupSize < 2 ? 2 : s.MinGroupSize;
        return group * group * 3;
    }

    public static StageReport Load()
    {
        var rep = new StageReport();
        if (!File.Exists(Path)) { rep.Errors.Add("stages.json 없음: " + Path); return rep; }

        var root = MiniJson.Parse(File.ReadAllText(Path)) as Dictionary<string, object>;
        if (root == null) { rep.Errors.Add("JSON 파싱 실패"); return rep; }

        var arr = Get(root, "stages") as List<object>;
        if (arr == null) { rep.Errors.Add("stages 배열 없음"); return rep; }

        foreach (var item in arr)
        {
            var m = item as Dictionary<string, object>;
            if (m == null) continue;
            var s = new StageRow();
            s.StageId = (int)Num(m, "stageId");
            s.Locked = Get(m, "locked") is bool && (bool)Get(m, "locked");

            int seed = (int)Num(m, "seed");
            var topo = Get(m, "topology") as Dictionary<string, object>;
            var allowed = new List<string>();
            var al = Get(topo, "allowed") as List<object>;
            if (al != null) foreach (var o in al) if (o is string) allowed.Add((string)o);
            var wl = Get(topo, "weights") as List<object>;
            var weights = new double[wl == null ? 0 : wl.Count];
            for (int i = 0; i < weights.Length; i++) weights[i] = Convert.ToDouble(wl[i]);
            // 설정에 적힌 대로 실제로 뽑아 본다 — 저장된 값을 믿지 않는다
            s.TopologyMode = CurveGen.ResolveTopology(seed, Str(topo, "mode") == "fixed", allowed, weights);

            var grid = Get(m, "grid") as Dictionary<string, object>;
            s.GridSize = (int)Num(grid, "size");
            s.InitialFillRatio = Num(grid, "initialFillRatio");
            s.FillPattern = Str(grid, "fillPattern");

            var pal = Get(m, "palette") as Dictionary<string, object>;
            s.PaletteSize = (int)Num(pal, "size");

            var rf = Get(m, "refill") as Dictionary<string, object>;
            s.RefillMode = Str(rf, "mode");
            s.BlocksPerClear = (int)Num(rf, "blocksPerClear");
            var cw = Get(rf, "colorWeights") as List<object>;
            if (cw != null)
            {
                s.ColorWeights = new double[cw.Count];
                for (int i = 0; i < cw.Count; i++) s.ColorWeights[i] = Convert.ToDouble(cw[i]);
            }

            var op = Get(m, "obstaclePlacement") as Dictionary<string, object>;
            s.ObstacleRatioPermille = (int)Math.Round(Num(op, "ratio") * 1000);

            var pieces = Get(m, "pieces") as Dictionary<string, object>;
            s.RerollCount = (int)Num(pieces, "rerollCount");

            var mr = Get(m, "matchRule") as Dictionary<string, object>;
            s.MinGroupSize = (int)Num(mr, "minGroupSize");

            var lim = Get(m, "limits") as Dictionary<string, object>;
            s.Moves = (int)Num(lim, "moves");
            s.TimeSeconds = (int)Num(lim, "timeSeconds");

            var objs = Get(m, "objectives") as List<object>;
            if (objs != null)
                foreach (var o in objs)
                {
                    var om = o as Dictionary<string, object>;
                    if (om == null) continue;
                    s.Objectives.Add(new StageObjective
                    {
                        Type = Str(om, "type"),
                        Target = (int)Num(om, "target"),
                        ColorIndex = Get(om, "colorIndex") == null ? -1 : (int)Num(om, "colorIndex"),
                        ObstacleType = Str(om, "obstacleType"),
                    });
                }

            if (s.StageId < 1) rep.Errors.Add("stageId 없음");
            if (s.PaletteSize < 2) rep.Errors.Add("stage" + s.StageId + " palette.size 이상");
            if (s.MinGroupSize < 2) rep.Errors.Add("stage" + s.StageId + " minGroupSize 이상");
            if (s.Moves <= 0 && s.TimeSeconds <= 0) rep.Errors.Add("stage" + s.StageId + " 제한 없음");
            if (s.Objectives.Count == 0) rep.Errors.Add("stage" + s.StageId + " objectives 비었음");

            rep.Stages.Add(s);
        }
        rep.Stages.Sort((a, b) => a.StageId.CompareTo(b.StageId));
        return rep;
    }

    static object Get(Dictionary<string, object> m, string k)
    {
        object v;
        return m != null && m.TryGetValue(k, out v) ? v : null;
    }
    static double Num(Dictionary<string, object> m, string k)
    {
        object v = Get(m, k);
        return v is double ? (double)v : 0;
    }
    static string Str(Dictionary<string, object> m, string k)
    {
        return Get(m, k) as string;
    }
}
