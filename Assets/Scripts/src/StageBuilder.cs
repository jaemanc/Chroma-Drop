// StageBuilder.cs — 스테이지 정의 → 실제 판(토폴로지 + 엔진 설정).
// 장애물 자리는 설정의 배치 규칙(pattern)과 시드로 정한다. 좌표를 코드에 박지 않는다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public sealed class StageInstance
    {
        public StageDef Def;
        public Topology Topo;
        public GameEngine Engine;
        public PaletteResult Palette;
        public ObjectiveTracker Objectives;
        public TurnController Turn;
        public List<SpatialItem> Items = new List<SpatialItem>();
        public readonly List<string> Log = new List<string>();
    }

    public static class StageBuilder
    {
        public static StageInstance Build(StageDef def, Rgb background)
        {
            var inst = new StageInstance { Def = def };

            string kind = def.ResolveTopology();
            inst.Topo = TopologyGen.Build(kind, def.GridSize);

            inst.Palette = PaletteGen.Generate(def.Palette.ToSpec(), background,
                                               new Rng(def.Seed, Stream.Palette));
            foreach (var w in inst.Palette.Warnings) inst.Log.Add("palette: " + w);

            var spec = new EngineSpec
            {
                PaletteSize = def.Palette.Size,
                MinGroupSize = def.MinGroupSize,
                ChainReaction = def.ChainReaction,
                InitialFillRatio = def.InitialFillRatio,
                FillPattern = def.FillPattern == "bottom_up" ? FillKind.BottomUp
                            : def.FillPattern == "preset" ? FillKind.Preset : FillKind.Random,
                Refill = new RefillSpec
                {
                    Mode = def.RefillMode == "none" ? RefillKind.None
                         : def.RefillMode == "drip" ? RefillKind.Drip : RefillKind.Instant,
                    BlocksPerClear = def.BlocksPerClear,
                    DelayMs = def.DelayMs,
                },
                ColorWeights = def.ColorWeights,
            };
            PlaceObstacles(spec, inst.Topo, def);

            inst.Engine = new GameEngine(inst.Topo, spec,
                                         new Rng(def.Seed, Stream.Board),
                                         new Rng(def.Seed, Stream.Refill));

            inst.Objectives = new ObjectiveTracker(def);
            inst.Turn = new TurnController(inst.Engine, PieceShapes.For(inst.Topo),
                                           new Rng(def.Seed, Stream.Spawn), def.RerollCount);
            inst.Items = BuildItems(def, inst.Topo);

            Describe(inst);
            return inst;
        }

        /// <summary>배치 규칙에 따라 장애물 자리를 고른다.</summary>
        static void PlaceObstacles(EngineSpec spec, Topology topo, StageDef def)
        {
            int want = (int)System.Math.Round(topo.Count * def.ObstacleRatio);
            if (want <= 0) return;

            var rng = new Rng(def.Seed, Stream.Board);
            var chosen = Pick(topo, def, rng, want);
            if (chosen.Count == 0) return;

            double total = def.MixBrick + def.MixFrozen + def.MixLocked;
            if (total <= 0) total = 1;
            int nBrick = (int)(chosen.Count * def.MixBrick / total);
            int nFrozen = (int)(chosen.Count * def.MixFrozen / total);

            var brick = new ObstacleGroup { Type = "brick", Cell = CellState.Brick, HitsToBreak = def.HitsToBreak };
            var frozen = new ObstacleGroup { Type = "frozen", Cell = CellState.Frozen };
            var locked = new ObstacleGroup { Type = "locked", Cell = CellState.Locked };

            for (int i = 0; i < chosen.Count; i++)
            {
                if (i < nBrick) brick.CellIds.Add(chosen[i]);
                else if (i < nBrick + nFrozen) frozen.CellIds.Add(chosen[i]);
                else locked.CellIds.Add(chosen[i]);
            }
            if (brick.CellIds.Count > 0) spec.Obstacles.Add(brick);
            if (frozen.CellIds.Count > 0) spec.Obstacles.Add(frozen);
            if (locked.CellIds.Count > 0) spec.Obstacles.Add(locked);
        }

        static List<int> Pick(Topology topo, StageDef def, Rng rng, int want)
        {
            var ok = new List<int>();
            for (int i = 0; i < topo.Count; i++)
            {
                if (def.AvoidSpawnCells && topo.Cells[i].IsSpawn) continue;
                ok.Add(i);
            }
            if (ok.Count == 0) return ok;

            var chosen = new List<int>();
            var used = new HashSet<int>();

            switch (def.ObstaclePattern)
            {
                case "wall":
                    {
                        // 한 축을 따라 벽을 세운다. 보드가 갈릴 수 있고, 그건 의도된 설계다.
                        int start = ok[rng.Next(ok.Count)];
                        int axis = rng.Next(topo.Axes.Length);
                        foreach (int id in topo.Line(start, axis))
                        {
                            if (chosen.Count >= want) break;
                            if (def.AvoidSpawnCells && topo.Cells[id].IsSpawn) continue;
                            if (used.Add(id)) chosen.Add(id);
                        }
                        break;
                    }
                case "cluster":
                    {
                        // 한 점에서 홉 단위로 퍼뜨린다.
                        int start = ok[rng.Next(ok.Count)];
                        var q = new Queue<int>();
                        q.Enqueue(start); used.Add(start);
                        while (q.Count > 0 && chosen.Count < want)
                        {
                            int cur = q.Dequeue();
                            if (!(def.AvoidSpawnCells && topo.Cells[cur].IsSpawn)) chosen.Add(cur);
                            foreach (int n in topo.Cells[cur].Neighbors)
                                if (n >= 0 && used.Add(n)) q.Enqueue(n);
                        }
                        break;
                    }
                case "ring":
                    {
                        // 기준 칸에서 정확히 r 홉 떨어진 칸들.
                        int start = ok[rng.Next(ok.Count)];
                        var dist = new Dictionary<int, int> { { start, 0 } };
                        var q = new Queue<int>();
                        q.Enqueue(start);
                        int radius = 2;
                        while (q.Count > 0)
                        {
                            int cur = q.Dequeue();
                            int d = dist[cur];
                            if (d == radius && chosen.Count < want
                                && !(def.AvoidSpawnCells && topo.Cells[cur].IsSpawn)) chosen.Add(cur);
                            if (d >= radius) continue;
                            foreach (int n in topo.Cells[cur].Neighbors)
                                if (n >= 0 && !dist.ContainsKey(n)) { dist[n] = d + 1; q.Enqueue(n); }
                        }
                        break;
                    }
                default:
                    {
                        for (int t = 0; t < want * 40 && chosen.Count < want; t++)
                        {
                            int id = ok[rng.Next(ok.Count)];
                            if (used.Add(id)) chosen.Add(id);
                        }
                        break;
                    }
            }
            return chosen;
        }

        static List<SpatialItem> BuildItems(StageDef def, Topology topo)
        {
            var list = new List<SpatialItem>();
            foreach (var name in def.ItemsAvailable)
            {
                ItemEffect effect;
                switch (name)
                {
                    case "line": effect = ItemEffect.Line; break;
                    case "burst": effect = ItemEffect.Burst; break;
                    case "ring": effect = ItemEffect.Ring; break;
                    case "color": effect = ItemEffect.Color; break;
                    case "cross": effect = ItemEffect.Cross; break;
                    default: continue;
                }
                list.Add(new SpatialItem
                {
                    Id = name,
                    Effect = effect,
                    AxisMode = effect == ItemEffect.Line ? AxisMode.PlayerChoice : AxisMode.Fixed,
                    Radius = effect == ItemEffect.Ring ? def.RingRadius
                           : ItemBalance.BurstRadius(topo, def.BurstRadius),
                    MaxCells = effect == ItemEffect.Line ? ItemBalance.LineMaxCells(topo) : 0,
                    BlockedBy = new[] { "locked" },
                    Damages = new[] { "brick", "frozen" },
                });
            }
            return list;
        }

        /// <summary>판을 열 때 남기는 정보. 에러가 아니다.</summary>
        static void Describe(StageInstance inst)
        {
            int obstacles = 0;
            for (int i = 0; i < inst.Engine.Count; i++)
                if (CellState.IsObstacle(inst.Engine.Get(i))) obstacles++;

            inst.Log.Add("조작 가능 칸 = " + inst.Topo.Count + " - " + obstacles
                         + " = " + (inst.Topo.Count - obstacles));

            var regions = BoardRegions.All(inst.Engine);
            if (regions.Count > 1) inst.Log.Add("보드가 " + regions.Count + "개 영역으로 분단됨");
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                string tag = r.DeadZone ? "dead zone" : r.Refillable ? "정상" : "고립(소모전)";
                inst.Log.Add("영역 " + (i + 1) + ": " + r.Cells.Count + "칸 · " + tag);
            }
        }
    }
}
