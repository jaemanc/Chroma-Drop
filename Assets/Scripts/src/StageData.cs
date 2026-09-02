// StageData.cs — stages.json 을 읽어 스테이지 정의로 바꾼다.
// UnityEngine 비의존 — 게임과 콘솔 검증 도구가 같은 코드를 쓴다.
// 이 파일에 스테이지 번호·목표 수치·색상값 리터럴은 없다.

using System;
using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public sealed class ObjectiveDef
    {
        public string Type;
        public int Target;
        public int ColorIndex = -1;
        public int GroupSize;
        public string ObstacleType;
    }

    public sealed class PaletteDef
    {
        public int Size;
        public double HueSpread;
        public double SatMin, SatMax;
        public double LightMin, LightMax;
        public double MinDeltaE, MinContrast;

        public PaletteSpec ToSpec()
        {
            return new PaletteSpec
            {
                Size = Size, HueSpread = HueSpread,
                SatMin = SatMin, SatMax = SatMax,
                LightMin = LightMin, LightMax = LightMax,
                MinDeltaE = MinDeltaE, MinContrast = MinContrast,
            };
        }
    }

    public sealed class StageDef
    {
        public int StageId;
        public int Seed;
        public bool Locked;

        public string TopologyMode;
        public List<string> TopologyAllowed = new List<string>();
        public double[] TopologyWeights = new double[0];

        public PaletteDef Palette = new PaletteDef();

        public int GridSize;
        public double InitialFillRatio;
        public string FillPattern;

        public int RerollCount;

        public string RefillMode;
        public int BlocksPerClear;
        public int DelayMs;
        public double[] ColorWeights = new double[0];

        public double ObstacleRatio;
        public string ObstaclePattern;
        public bool AvoidSpawnCells;
        public int HitsToBreak;
        public double MixBrick, MixFrozen, MixLocked;

        public List<string> ItemsAvailable = new List<string>();
        public bool ItemCostsMove;
        public int BurstRadius, RingRadius;

        public readonly List<ObjectiveDef> Objectives = new List<ObjectiveDef>();
        public string ObjectiveMode;

        public int Moves, TimeSeconds;
        public int MinGroupSize;
        public bool ChainReaction;

        /// <summary>설정이 정한 대로 이 판의 토폴로지를 뽑는다.</summary>
        public string ResolveTopology()
        {
            if (TopologyMode == "fixed" || TopologyAllowed.Count <= 1)
                return TopologyAllowed.Count > 0 ? TopologyAllowed[0] : TopologyGen.Square;
            int i = new Rng(Seed, Stream.Topology).Weighted(TopologyWeights);
            return i >= 0 && i < TopologyAllowed.Count ? TopologyAllowed[i] : TopologyAllowed[0];
        }

        public ObjectiveMode Mode
        {
            get { return ObjectiveMode == "any" ? Engine.ObjectiveMode.Any : Engine.ObjectiveMode.All; }
        }
    }

    public enum ObjectiveMode { All, Any }

    public sealed class StageSet
    {
        public string SourcePath = "";
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<StageDef> Stages = new List<StageDef>();

        public bool Ok { get { return Errors.Count == 0; } }
        public int Count { get { return Stages.Count; } }

        public StageDef Get(int stageId)
        {
            foreach (var s in Stages) if (s.StageId == stageId) return s;
            return null;
        }
    }

    public static class StageData
    {
        public const int SupportedVersion = 1;

        /// <summary>스테이지 번호가 시작하는 값. 난이도 수치가 아니라 번호 체계의 기준점이다.</summary>
        public const int FirstStageId = 1;

        public static StageSet Parse(string json)
        {
            var set = new StageSet();
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) { set.Errors.Add("JSON 파싱 실패"); return set; }

            long version = (long)Num(root, "version", -1);
            if (version != SupportedVersion)
            {
                set.Errors.Add("지원하지 않는 스키마 버전 " + version);
                return set;
            }

            var arr = Get(root, "stages") as List<object>;
            if (arr == null) { set.Errors.Add("stages 배열 없음"); return set; }

            var seen = new HashSet<int>();
            foreach (var item in arr)
            {
                var m = item as Dictionary<string, object>;
                if (m == null) continue;
                var s = ParseOne(m, set);
                if (s == null) continue;
                if (!seen.Add(s.StageId)) set.Errors.Add("stageId " + s.StageId + " 중복");
                set.Stages.Add(s);
            }
            set.Stages.Sort((a, b) => a.StageId.CompareTo(b.StageId));
            return set;
        }

        static StageDef ParseOne(Dictionary<string, object> m, StageSet set)
        {
            var s = new StageDef();
            s.StageId = (int)Num(m, "stageId", 0);
            if (s.StageId < FirstStageId) { set.Errors.Add("stageId 없음"); return null; }
            string at = "stage " + s.StageId + ": ";

            s.Seed = (int)Num(m, "seed", s.StageId);
            s.Locked = Bool(m, "locked", false);

            var topo = Map(m, "topology");
            s.TopologyMode = Str(topo, "mode", "fixed");
            s.TopologyAllowed = StrList(topo, "allowed");
            s.TopologyWeights = NumList(topo, "weights");
            if (s.TopologyAllowed.Count == 0) { set.Errors.Add(at + "topology.allowed 비었음"); return null; }
            foreach (var k in s.TopologyAllowed)
                if (Array.IndexOf(TopologyGen.Kinds, k) < 0)
                { set.Errors.Add(at + "알 수 없는 토폴로지 '" + k + "'"); return null; }

            var pal = Map(m, "palette");
            var sat = NumList(pal, "satRange");
            var lit = NumList(pal, "lightRange");
            s.Palette = new PaletteDef
            {
                Size = (int)Num(pal, "size", 0),
                HueSpread = Num(pal, "hueSpread", 0),
                SatMin = sat.Length > 0 ? sat[0] : 0,
                SatMax = sat.Length > 1 ? sat[1] : 1,
                LightMin = lit.Length > 0 ? lit[0] : 0,
                LightMax = lit.Length > 1 ? lit[1] : 1,
                MinDeltaE = Num(pal, "minDeltaE", 0),
                MinContrast = Num(pal, "minContrast", 0),
            };
            if (s.Palette.Size < 2) { set.Errors.Add(at + "palette.size 는 2 이상"); return null; }

            var grid = Map(m, "grid");
            s.GridSize = (int)Num(grid, "size", 0);
            s.InitialFillRatio = Num(grid, "initialFillRatio", 1.0);
            s.FillPattern = Str(grid, "fillPattern", "random");
            if (s.GridSize < 3) { set.Errors.Add(at + "grid.size 가 너무 작다"); return null; }
            if (s.InitialFillRatio <= 0 || s.InitialFillRatio > 1.0)
            { set.Errors.Add(at + "initialFillRatio 범위 밖"); return null; }

            var pieces = Map(m, "pieces");
            s.RerollCount = (int)Num(pieces, "rerollCount", 0);

            var rf = Map(m, "refill");
            s.RefillMode = Str(rf, "mode", "instant");
            s.BlocksPerClear = (int)Num(rf, "blocksPerClear", 0);
            s.DelayMs = (int)Num(rf, "delayMs", 0);
            s.ColorWeights = NumList(rf, "colorWeights");
            if (s.RefillMode != "none" && s.RefillMode != "drip" && s.RefillMode != "instant")
            { set.Errors.Add(at + "알 수 없는 refill.mode"); return null; }
            if (s.RefillMode == "drip" && s.BlocksPerClear < 1)
            { set.Errors.Add(at + "drip 인데 blocksPerClear 가 0"); return null; }
            if (s.ColorWeights.Length != s.Palette.Size)
            { set.Errors.Add(at + "colorWeights 길이가 palette.size 와 다르다"); return null; }

            var op = Map(m, "obstaclePlacement");
            s.ObstacleRatio = Num(op, "ratio", 0);
            s.ObstaclePattern = Str(op, "pattern", "scattered");
            s.AvoidSpawnCells = Bool(op, "avoidSpawnCells", true);
            s.HitsToBreak = (int)Num(op, "hitsToBreak", 1);
            var mix = Map(op, "mix");
            s.MixBrick = Num(mix, "brick", 1);
            s.MixFrozen = Num(mix, "frozen", 0);
            s.MixLocked = Num(mix, "locked", 0);

            var items = Map(m, "items");
            s.ItemsAvailable = StrList(items, "available");
            s.ItemCostsMove = Bool(items, "itemCostsMove", false);
            s.BurstRadius = (int)Num(items, "burstRadius", 1);
            s.RingRadius = (int)Num(items, "ringRadius", 1);

            var objs = Get(m, "objectives") as List<object>;
            if (objs == null || objs.Count == 0) { set.Errors.Add(at + "objectives 비었음"); return null; }
            foreach (var o in objs)
            {
                var om = o as Dictionary<string, object>;
                if (om == null) continue;
                var ob = new ObjectiveDef
                {
                    Type = Str(om, "type", ""),
                    Target = (int)Num(om, "target", 0),
                    ColorIndex = (int)Num(om, "colorIndex", -1),
                    GroupSize = (int)Num(om, "groupSize", 0),
                    ObstacleType = Str(om, "obstacleType", null),
                };
                if (ob.Target < 1) { set.Errors.Add(at + "objective target 은 1 이상"); return null; }
                switch (ob.Type)
                {
                    case "clear_count": break;
                    case "clear_color":
                        if (ob.ColorIndex < 0 || ob.ColorIndex >= s.Palette.Size)
                        { set.Errors.Add(at + "colorIndex 가 palette 범위 밖"); return null; }
                        break;
                    case "break_obstacle":
                        if (ob.ObstacleType == "locked")
                        { set.Errors.Add(at + "locked 는 부술 수 없어 목표가 될 수 없다"); return null; }
                        break;
                    case "clear_group_size":
                        if (ob.GroupSize < 2) { set.Errors.Add(at + "groupSize 는 2 이상"); return null; }
                        break;
                    default: set.Errors.Add(at + "알 수 없는 objective type"); return null;
                }
                s.Objectives.Add(ob);
            }
            s.ObjectiveMode = Str(m, "objectiveMode", "all");

            var lim = Map(m, "limits");
            s.Moves = (int)Num(lim, "moves", 0);
            s.TimeSeconds = (int)Num(lim, "timeSeconds", 0);
            if (s.Moves <= 0 && s.TimeSeconds <= 0)
            { set.Errors.Add(at + "moves 와 timeSeconds 가 둘 다 없다"); return null; }

            var mr = Map(m, "matchRule");
            s.MinGroupSize = (int)Num(mr, "minGroupSize", 0);
            s.ChainReaction = Bool(mr, "chainReaction", true);
            if (s.MinGroupSize < 2) { set.Errors.Add(at + "minGroupSize 는 2 이상"); return null; }

            return s;
        }

        // ---------- 읽기 헬퍼 ----------
        static object Get(Dictionary<string, object> m, string k)
        {
            object v;
            return m != null && m.TryGetValue(k, out v) ? v : null;
        }
        static Dictionary<string, object> Map(Dictionary<string, object> m, string k)
        {
            return Get(m, k) as Dictionary<string, object>;
        }
        static double Num(Dictionary<string, object> m, string k, double fallback)
        {
            object v = Get(m, k);
            return v is double ? (double)v : fallback;
        }
        static bool Bool(Dictionary<string, object> m, string k, bool fallback)
        {
            object v = Get(m, k);
            return v is bool ? (bool)v : fallback;
        }
        static string Str(Dictionary<string, object> m, string k, string fallback)
        {
            return Get(m, k) as string ?? fallback;
        }
        static List<string> StrList(Dictionary<string, object> m, string k)
        {
            var outp = new List<string>();
            var l = Get(m, k) as List<object>;
            if (l != null) foreach (var o in l) if (o is string) outp.Add((string)o);
            return outp;
        }
        static double[] NumList(Dictionary<string, object> m, string k)
        {
            var l = Get(m, k) as List<object>;
            if (l == null) return new double[0];
            var outp = new double[l.Count];
            for (int i = 0; i < l.Count; i++) outp[i] = l[i] is double ? (double)l[i] : 0;
            return outp;
        }
    }
}
