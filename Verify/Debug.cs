// Debug.cs — 디버그 모드. 임의 스테이지를 즉시 열고, 목표 진행도와
// 연결 영역·사장 영역을 콘솔에 그린다.
//
//   mono debug.exe <stageId> [--play N]

using System;
using System.Collections.Generic;
using ChromaDrop.Engine;
using Stream = ChromaDrop.Engine.Stream;

static class DebugTool
{
    static void Main(string[] args)
    {
        int stageId = args.Length > 0 ? int.Parse(args[0]) : 1;
        int plays = 0;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--play") plays = int.Parse(args[i + 1]);

        var rep = StageJson.Load();
        if (rep.Errors.Count > 0)
        {
            foreach (var e in rep.Errors) Console.WriteLine("ERROR " + e);
            Environment.Exit(1);
        }

        StageRow row = null;
        foreach (var s in rep.Stages) if (s.StageId == stageId) row = s;
        if (row == null) { Console.WriteLine("스테이지 " + stageId + " 없음"); Environment.Exit(1); }

        var topo = TopologyGen.Build(row.TopologyMode, row.GridSize);
        var spec = new EngineSpec
        {
            PaletteSize = row.PaletteSize,
            MinGroupSize = row.MinGroupSize,
            ChainReaction = true,
            InitialFillRatio = row.InitialFillRatio,
            FillPattern = row.FillPattern == "bottom_up" ? FillKind.BottomUp : FillKind.Random,
            Refill = new RefillSpec
            {
                Mode = row.RefillMode == "none" ? RefillKind.None
                     : row.RefillMode == "drip" ? RefillKind.Drip : RefillKind.Instant,
                BlocksPerClear = row.BlocksPerClear,
            },
            ColorWeights = row.ColorWeights,
        };
        PlaceObstacles(spec, topo, row);

        int seed = 0;
        foreach (var s in rep.Stages) if (s.StageId == stageId) seed = stageId;
        var eng = new GameEngine(topo, spec, new Rng(seed, Stream.Board), new Rng(seed, Stream.Refill));

        Console.WriteLine("STAGE " + stageId + "  topology=" + topo.Name
                          + "  cells=" + topo.Count + "  palette=" + row.PaletteSize
                          + "  minGroup=" + row.MinGroupSize + "  moves=" + row.Moves);
        Console.WriteLine("조작 가능 칸 = " + topo.Count + " - " + ObstacleCount(spec)
                          + " = " + (topo.Count - ObstacleCount(spec)));

        Regions(eng, row);
        Objectives(row, new int[row.PaletteSize], 0, 0, 0);

        if (plays > 0) Play(eng, row, plays);
    }

    static int ObstacleCount(EngineSpec spec)
    {
        int n = 0;
        foreach (var o in spec.Obstacles) n += o.CellIds.Count;
        return n;
    }

    static void PlaceObstacles(EngineSpec spec, Topology topo, StageRow row)
    {
        int want = (int)Math.Round(topo.Count * row.ObstacleRatioPermille / 1000.0);
        if (want <= 0) return;

        var rng = new Rng(row.StageId, Stream.Board);
        var brick = new ObstacleGroup { Type = "brick", Cell = CellState.Brick, HitsToBreak = 2 };
        var frozen = new ObstacleGroup { Type = "frozen", Cell = CellState.Frozen };
        var locked = new ObstacleGroup { Type = "locked", Cell = CellState.Locked };

        var used = new HashSet<int>();
        for (int t = 0; t < want * 40 && used.Count < want; t++)
        {
            int id = rng.Next(topo.Count);
            if (topo.Cells[id].IsSpawn || !used.Add(id)) continue;
            int pick = used.Count % 5;
            if (pick < 3) brick.CellIds.Add(id);
            else if (pick == 3) frozen.CellIds.Add(id);
            else locked.CellIds.Add(id);
        }
        if (brick.CellIds.Count > 0) spec.Obstacles.Add(brick);
        if (frozen.CellIds.Count > 0) spec.Obstacles.Add(frozen);
        if (locked.CellIds.Count > 0) spec.Obstacles.Add(locked);
    }

    static void Regions(GameEngine eng, StageRow row)
    {
        var seen = new bool[eng.Count];
        var regions = new List<List<int>>();
        for (int i = 0; i < eng.Count; i++)
        {
            if (seen[i] || eng.Get(i) == CellState.Locked) continue;
            var r = ItemSystem.RegionOf(eng, i);
            foreach (int id in r) seen[id] = true;
            regions.Add(r);
        }

        Console.WriteLine("연결 영역 " + regions.Count + "개");
        int need = row.MinGroupSize;
        for (int i = 0; i < regions.Count; i++)
        {
            bool dead = regions[i].Count < need;
            bool refillable = false;
            foreach (int id in regions[i]) if (eng.Topo.Cells[id].IsSpawn) refillable = true;
            Console.WriteLine("  영역 " + (i + 1) + ": " + regions[i].Count + "칸 · "
                              + (dead ? "dead zone" : refillable ? "정상" : "고립(소모전)"));
        }
    }

    static void Objectives(StageRow row, int[] byColor, int cleared, int bricks, int score)
    {
        Console.WriteLine("목표:");
        foreach (var o in row.Objectives)
        {
            int cur = o.Type == "clear_count" ? cleared
                    : o.Type == "clear_color" ? (o.ColorIndex >= 0 && o.ColorIndex < byColor.Length ? byColor[o.ColorIndex] : 0)
                    : o.Type == "break_obstacle" ? bricks
                    : score;
            string name = o.Type == "clear_color" ? ("color" + o.ColorIndex) : o.Type;
            Console.WriteLine("  " + name + " " + cur + "/" + o.Target);
        }
    }

    static void Play(GameEngine eng, StageRow row, int turns)
    {
        var byColor = new int[row.PaletteSize];
        int cleared = 0, bricks = 0, score = 0;
        var rng = new Rng(row.StageId, Stream.Spawn);

        for (int t = 0; t < turns; t++)
        {
            var res = eng.ResolveAll();
            if (res.Cleared.Count == 0)
            {
                // 소거할 게 없으면 임의의 칸 색을 바꿔 진행을 흉내낸다 (디버그 전용)
                int id = rng.Next(eng.Count);
                if (CellState.IsColor(eng.Get(id))) eng.Set(id, rng.Next(row.PaletteSize));
                continue;
            }
            cleared += res.Cleared.Count;
            bricks += res.BricksBroken;
            score += res.Score;
            for (int i = 0; i < byColor.Length && i < res.ClearedByColor.Length; i++)
                byColor[i] += res.ClearedByColor[i];
        }
        Console.WriteLine();
        Console.WriteLine(turns + "턴 후");
        Objectives(row, byColor, cleared, bricks, score);
    }
}
