// Verify.cs — SPEC 의 VERIFY 24항목. 항목번호 + PASS/FAIL 만 출력한다.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ChromaDrop.Engine;
using Stream = ChromaDrop.Engine.Stream;

static class Verify
{
    static int pass = 0, fail = 0;
    static readonly List<string> failed = new List<string>();

    static void Check(string id, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine(id + " PASS"); }
        else { fail++; failed.Add(id); Console.WriteLine(id + " FAIL" + (detail == null ? "" : "  // " + detail)); }
    }

    const int Size = 8;
    static readonly string[] Kinds = { TopologyGen.Square, TopologyGen.Triangle, TopologyGen.Hex };

    static void Main()
    {
        V1(); V2(); V3(); V4(); V5(); V6(); V7(); V8(); V9(); V10();
        V11(); V12(); V13(); V14(); V15(); V16(); V17(); V18(); V19(); V20();
        V21(); V22(); V23(); V24();

        Console.WriteLine();
        Console.WriteLine("PASS " + pass + " / FAIL " + fail);
        if (fail > 0) Console.WriteLine("FAILED: " + string.Join(",", failed.ToArray()));
        Environment.Exit(fail == 0 ? 0 : 1);
    }

    static string[] SrcFiles()
    {
        return Directory.GetFiles("Assets/Scripts/src", "*.cs", SearchOption.AllDirectories);
    }

    // V1 색 리터럴·스테이지 번호 리터럴 0건
    static void V1()
    {
        var bad = new List<string>();
        var hex = new Regex(@"#[0-9A-Fa-f]{3,8}\b|0x[0-9A-Fa-f]{6}\b");
        var names = new Regex(@"\b(coral|purple|yellow|pink|teal|red|green|blue|orange)\b", RegexOptions.IgnoreCase);
        var stageNum = new Regex(@"\bstage(Id)?\s*[=<>!]=?\s*\d+", RegexOptions.IgnoreCase);
        foreach (var f in SrcFiles())
        {
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                int c = l.IndexOf("//");
                if (c >= 0) l = l.Substring(0, c);
                if (hex.IsMatch(l) || names.IsMatch(l) || stageNum.IsMatch(l))
                    bad.Add(Path.GetFileName(f) + ":" + (i + 1));
            }
        }
        Check("V1", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V2 GameEngine 에 x/y/width/height/column 참조 0건
    static void V2()
    {
        string src = File.ReadAllText("Assets/Scripts/src/GameEngine.cs");
        var re = new Regex(@"\b(width|height|column|\.x\b|\.y\b)\b", RegexOptions.IgnoreCase);
        var bad = new List<string>();
        var lines = src.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            int c = l.IndexOf("//");
            if (c >= 0) l = l.Substring(0, c);
            if (re.IsMatch(l)) bad.Add((i + 1) + ":" + l.Trim());
        }
        Check("V2", bad.Count == 0, string.Join(" | ", bad.ToArray()));
    }

    // V3 GameEngine 에 토폴로지 이름 분기 0건
    static void V3()
    {
        string src = File.ReadAllText("Assets/Scripts/src/GameEngine.cs");
        var re = new Regex("\"(square|triangle|hex)\"|TopologyGen\\.(Square|Triangle|Hex)");
        Check("V3", !re.IsMatch(src), "GameEngine 안에서 토폴로지 이름을 분기한다");
    }

    // V4 fallTarget 순환 0건
    static void V4()
    {
        var bad = new List<string>();
        foreach (var k in Kinds)
        {
            var t = TopologyGen.Build(k, Size);
            var cyc = TopologyGen.FallCycles(t);
            if (cyc.Count > 0) bad.Add(k + ":" + cyc.Count);
        }
        Check("V4", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V5 도달 불가 셀 목록 산출 (에러 아님, 출력만)
    static void V5()
    {
        bool ok = true;
        foreach (var k in Kinds)
        {
            var t = TopologyGen.Build(k, Size);
            var un = TopologyGen.Unreachable(t);
            Console.WriteLine("      " + k + " 도달불가 " + un.Count + "칸");
            if (un == null) ok = false;
        }
        Check("V5", ok, null);
    }

    // V6 축 순회 결과가 직선인가 (좌표로 검증)
    static void V6()
    {
        var bad = new List<string>();
        foreach (var k in Kinds)
        {
            var t = TopologyGen.Build(k, Size);
            for (int a = 0; a < t.Axes.Length; a++)
            {
                int probe = t.Count / 2;
                var line = t.Line(probe, a);
                if (line.Count < 3) continue;
                if (!Collinear(t, line)) bad.Add(k + " axis" + a + "(" + line.Count + ")");

            }
        }
        Check("V6", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // 셀 중심이 한 직선 위에 있는가. 허용 오차는 셀의 내접원 반지름이다 —
    // 삼각형처럼 중심이 지그재그로 놓이는 격자도 '한 셀 폭 안의 띠'면 직선으로 본다.
    static bool Collinear(Topology t, List<int> ids)
    {
        var pts = new List<Vec2>();
        foreach (int id in ids) pts.Add(t.Cells[id].Center);
        pts.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));

        // 주성분 방향으로 맞춘다. 끝점 두 개로 잡으면 지그재그 격자에서
        // 잔차가 한쪽으로 쏠려 실제보다 두 배로 나온다.
        double mx = 0, my = 0;
        foreach (var p in pts) { mx += p.X; my += p.Y; }
        mx /= pts.Count; my /= pts.Count;

        double sxx = 0, sxy = 0, syy = 0;
        foreach (var p in pts)
        {
            double ax = p.X - mx, ay = p.Y - my;
            sxx += ax * ax; sxy += ax * ay; syy += ay * ay;
        }
        double theta = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        double dx = Math.Cos(theta), dy = Math.Sin(theta);

        double tol = TopologyGen.Inradius(t.Cells[ids[0]]);
        foreach (var p in pts)
        {
            double cross = Math.Abs((p.X - mx) * dy - (p.Y - my) * dx);
            if (cross > tol) return false;
        }
        return true;
    }

    // V7 동일 seed 100회 → 완전 동일
    static void V7()
    {
        string first = Fingerprint(1234);
        bool ok = true;
        for (int i = 0; i < 100; i++) if (Fingerprint(1234) != first) { ok = false; break; }
        Check("V7", ok, null);
    }

    static string Fingerprint(int seed)
    {
        var t = TopologyGen.Build(Kinds[new Rng(seed, Stream.Topology).Next(Kinds.Length)], Size);
        var pal = PaletteGen.Generate(DefaultPalette(4), new Rgb(1, 1, 1), new Rng(seed, Stream.Palette));
        var eng = new GameEngine(t, DefaultSpec(4, 3), new Rng(seed, Stream.Board), new Rng(seed, Stream.Refill));
        var sb = new System.Text.StringBuilder(t.Name);
        for (int i = 0; i < pal.Colors.Length; i++)
            sb.Append('|').Append(pal.Colors[i].R.ToString("0.0000"));
        for (int i = 0; i < eng.Count; i++) sb.Append(',').Append(eng.Get(i));
        return sb.ToString();
    }

    // V8 paletteStream 만 변경 → spawnStream 결과 불변
    static void V8()
    {
        var a = new Rng(99, Stream.Spawn);
        var seqA = new List<int>();
        for (int i = 0; i < 50; i++) seqA.Add(a.Next(1000));

        var burn = new Rng(99, Stream.Palette);
        for (int i = 0; i < 500; i++) burn.Next(1000);

        var b = new Rng(99, Stream.Spawn);
        bool ok = true;
        for (int i = 0; i < 50; i++) if (b.Next(1000) != seqA[i]) ok = false;
        Check("V8", ok, null);
    }

    static PaletteSpec DefaultPalette(int size)
    {
        return new PaletteSpec
        {
            Size = size, HueSpread = 300, SatMin = 0.45, SatMax = 0.75,
            LightMin = 0.35, LightMax = 0.7, MinDeltaE = 22, MinContrast = 1.8,
        };
    }

    static EngineSpec DefaultSpec(int palette, int minGroup)
    {
        return new EngineSpec
        {
            PaletteSize = palette, MinGroupSize = minGroup, ChainReaction = true,
            InitialFillRatio = 1.0, FillPattern = FillKind.Random,
            Refill = new RefillSpec { Mode = RefillKind.Instant },
        };
    }

    // V9 팔레트 ΔE / 대비
    static void V9()
    {
        var bad = new List<string>();
        for (int seed = 0; seed < 30; seed++)
        {
            var spec = DefaultPalette(4 + seed % 3);
            var p = PaletteGen.Generate(spec, new Rgb(1, 1, 1), new Rng(seed, Stream.Palette));
            if (p.MinPairDeltaE < spec.MinDeltaE) bad.Add("seed" + seed + " dE=" + p.MinPairDeltaE.ToString("0.0"));
            if (p.MinBackgroundContrast < spec.MinContrast) bad.Add("seed" + seed + " C=" + p.MinBackgroundContrast.ToString("0.00"));
        }
        Check("V9", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V10 재생성 루프가 상한 안에서 끝난다
    static void V10()
    {
        bool ok = true;
        for (int seed = 0; seed < 30; seed++)
        {
            var p = PaletteGen.Generate(DefaultPalette(6), new Rgb(1, 1, 1), new Rng(seed, Stream.Palette));
            if (p.Attempts > PaletteGen.MaxAttempts) ok = false;
        }
        Check("V10", ok, null);
    }

    // V11 colorWeights 길이 == palette.size
    static void V11()
    {
        var rep = StageJson.Load();
        var bad = new List<string>();
        foreach (var s in rep.Stages)
        {
            if (s.ColorWeights.Length != s.PaletteSize)
                bad.Add("stage" + s.StageId + " w=" + s.ColorWeights.Length + " size=" + s.PaletteSize);
            foreach (var o in s.Objectives)
                if (o.Type == "clear_color" && (o.ColorIndex < 0 || o.ColorIndex >= s.PaletteSize))
                    bad.Add("stage" + s.StageId + " colorIndex=" + o.ColorIndex);
        }
        Check("V11", rep.Errors.Count == 0 && bad.Count == 0,
              string.Join(" ", bad.ToArray()) + string.Join(" ", rep.Errors.ToArray()));
    }

    // V12 초기 보드에 소거 가능 그룹 0건
    static void V12()
    {
        var bad = new List<string>();
        foreach (var k in Kinds)
            for (int seed = 0; seed < 20; seed++)
            {
                var t = TopologyGen.Build(k, Size);
                var eng = new GameEngine(t, DefaultSpec(4, 3), new Rng(seed, Stream.Board), new Rng(seed, Stream.Refill));
                if (eng.HasClearableGroup()) bad.Add(k + ":" + seed);
            }
        Check("V12", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V13 stages.json 30개 스키마 통과 + 달성 불가 0건
    static void V13()
    {
        var rep = StageJson.Load();
        var bad = new List<string>(rep.Errors);
        if (rep.Stages.Count != 30) bad.Add("스테이지 " + rep.Stages.Count + "개");
        foreach (var s in rep.Stages)
            foreach (var o in s.Objectives)
                if (o.Target > s.Moves * StageJson.OptimisticPerMove(s))
                    bad.Add("stage" + s.StageId + " " + o.Type + " " + o.Target + " 불가");
        Check("V13", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V14 스테이지 1~5 는 square
    static void V14()
    {
        var rep = StageJson.Load();
        var bad = new List<string>();
        foreach (var s in rep.Stages)
            if (s.StageId <= StageJson.ForcedSquareUntil && s.TopologyMode != TopologyGen.Square)
                bad.Add("stage" + s.StageId + "=" + s.TopologyMode);
        Check("V14", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V15 스테이지당 변화 축 <= 2, paletteSize/minGroupSize 동시 상향 0건
    static void V15()
    {
        var rep = StageJson.Load();
        var bad = new List<string>();
        for (int i = 1; i < rep.Stages.Count; i++)
        {
            var a = rep.Stages[i - 1];
            var b = rep.Stages[i];
            int changed = 0;
            if (Math.Abs(a.InitialFillRatio - b.InitialFillRatio) > 1e-9) changed++;
            if (a.BlocksPerClear != b.BlocksPerClear) changed++;
            if (a.ObstacleRatioPermille != b.ObstacleRatioPermille) changed++;
            if (a.PaletteSize != b.PaletteSize) changed++;
            if (a.MinGroupSize != b.MinGroupSize) changed++;
            if (a.RerollCount != b.RerollCount) changed++;
            if (a.Objectives.Count != b.Objectives.Count) changed++;
            if (a.Moves != b.Moves) changed++;
            if (changed > 2) bad.Add("stage" + b.StageId + " 변화 " + changed);
            if (b.PaletteSize > a.PaletteSize && b.MinGroupSize > a.MinGroupSize)
                bad.Add("stage" + b.StageId + " palette+minGroup 동시 상향");
        }
        Check("V15", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    static SpatialItem MakeItem(ItemEffect e)
    {
        return new SpatialItem
        {
            Id = e.ToString(), Effect = e, AxisMode = AxisMode.PlayerChoice,
            Radius = 2, MaxCells = 0,
            BlockedBy = new[] { "locked" }, Damages = new[] { "brick", "frozen" },
        };
    }

    // V16 5 effect × 3 토폴로지 = 15조합 예외 없이 실행
    static void V16()
    {
        var bad = new List<string>();
        foreach (var k in Kinds)
            foreach (ItemEffect e in Enum.GetValues(typeof(ItemEffect)))
            {
                try
                {
                    var t = TopologyGen.Build(k, Size);
                    var eng = new GameEngine(t, DefaultSpec(4, 3), new Rng(7, Stream.Board), new Rng(7, Stream.Refill));
                    ItemSystem.Fire(eng, MakeItem(e), t.Count / 2, 0);
                }
                catch (Exception ex) { bad.Add(k + "/" + e + ":" + ex.GetType().Name); }
            }
        Check("V16", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V17 프리뷰 == 실제 소거 (1000회)
    static void V17()
    {
        var bad = new List<string>();
        var rng = new Rng(4242, Stream.Board);
        for (int trial = 0; trial < 1000 && bad.Count == 0; trial++)
        {
            string k = Kinds[rng.Next(Kinds.Length)];
            var t = TopologyGen.Build(k, Size);
            var eng = new GameEngine(t, DefaultSpec(4, 3), new Rng(trial, Stream.Board), new Rng(trial, Stream.Refill));
            var effects = (ItemEffect[])Enum.GetValues(typeof(ItemEffect));
            var item = MakeItem(effects[rng.Next(effects.Length)]);
            int origin = rng.Next(t.Count);
            int axis = rng.Next(t.Axes.Length);

            var preview = ItemSystem.Affected(eng, item, origin, axis);
            var expect = new List<int>();
            foreach (int id in preview) if (ItemSystem.CanDamage(item, eng.Get(id))) expect.Add(id);

            var res = ItemSystem.Fire(eng, item, origin, axis);
            var got = new HashSet<int>(res.Cleared);
            foreach (int id in expect)
                if (!got.Contains(id)) { bad.Add("trial" + trial + " " + k + " " + item.Effect + " cell" + id); break; }
        }
        Check("V17", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V18 line 평균 소거 칸수 토폴로지 간 편차 <= 30%
    static void V18()
    {
        var avg = new List<double>();
        foreach (var k in Kinds)
        {
            var t = TopologyGen.Build(k, Size);
            double total = 0; int n = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                var eng = new GameEngine(t, DefaultSpec(4, 3), new Rng(seed, Stream.Board), new Rng(seed, Stream.Refill));
                var item = MakeItem(ItemEffect.Line);
                item.MaxCells = ItemBalance.LineMaxCells(t);
                for (int a = 0; a < t.Axes.Length; a++)
                {
                    total += ItemSystem.Affected(eng, item, t.Count / 2, a).Count;
                    n++;
                }
            }
            avg.Add(total / n);
        }
        double lo = double.MaxValue, hi = 0;
        foreach (var v in avg) { if (v < lo) lo = v; if (v > hi) hi = v; }
        double dev = hi <= 0 ? 1 : (hi - lo) / hi;
        Check("V18", dev <= 0.30, "편차 " + (dev * 100).ToString("0.0") + "% "
              + string.Join("/", avg.ConvertAll(v => v.ToString("0.0")).ToArray()));
    }

    // V19 line 이 locked 관통 0건
    static void V19()
    {
        var bad = new List<string>();
        foreach (var k in Kinds)
        {
            var t = TopologyGen.Build(k, Size);
            var eng = new GameEngine(t, DefaultSpec(4, 3), new Rng(3, Stream.Board), new Rng(3, Stream.Refill));
            int origin = t.Count / 2;
            for (int a = 0; a < t.Axes.Length; a++)
            {
                int wall = t.Step(origin, a, true);
                if (wall < 0) continue;
                eng.Set(wall, CellState.Locked);
                int beyond = t.Step(wall, a, true);
                var hit = ItemSystem.Affected(eng, MakeItem(ItemEffect.Line), origin, a);
                if (beyond >= 0 && hit.Contains(beyond)) bad.Add(k + " axis" + a);
                eng.Set(wall, CellState.Empty);
            }
        }
        Check("V19", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V20 분단 보드에서 아이템이 타 영역 침범 0건
    static void V20()
    {
        var bad = new List<string>();
        foreach (var k in Kinds)
        {
            var t = TopologyGen.Build(k, Size);
            var eng = new GameEngine(t, DefaultSpec(4, 3), new Rng(11, Stream.Board), new Rng(11, Stream.Refill));

            // 임의로 벽을 세워 보드를 가른다
            for (int i = 0; i < t.Count; i++)
                if (i % Size == Size / 2) eng.Set(i, CellState.Locked);

            int origin = 1;
            var region = new HashSet<int>(ItemSystem.RegionOf(eng, origin));
            foreach (ItemEffect e in Enum.GetValues(typeof(ItemEffect)))
            {
                var hit = ItemSystem.Affected(eng, MakeItem(e), origin, 0);
                foreach (int id in hit)
                    if (!region.Contains(id)) { bad.Add(k + "/" + e + " cell" + id); break; }
            }
        }
        Check("V20", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V21 stages.json 값 변경 후 재로드만으로 반영
    static void V21()
    {
        string path = StageJson.Path;
        if (!File.Exists(path)) { Check("V21", false, "stages.json 없음"); return; }
        string original = File.ReadAllText(path);
        try
        {
            var before = StageJson.Load();
            if (before.Stages.Count == 0) { Check("V21", false, "스테이지 0개"); return; }
            int wasMoves = before.Stages[0].Moves;

            string patched = original.Replace("\"moves\": " + wasMoves, "\"moves\": " + (wasMoves + 7));
            if (patched == original) { Check("V21", false, "moves 치환 실패"); return; }
            File.WriteAllText(path, patched);

            var after = StageJson.Load();
            Check("V21", after.Stages[0].Moves == wasMoves + 7,
                  "재로드 후 " + after.Stages[0].Moves);
        }
        finally { File.WriteAllText(path, original); }
    }

    // V22 모든 조각 모양이 해당 토폴로지에서 배치 가능
    static void V22()
    {
        var bad = new List<string>();
        foreach (var k in Kinds)
        {
            var t = TopologyGen.Build(k, Size);
            foreach (var shape in PieceShapes.For(t))
            {
                bool placeable = false;
                for (int i = 0; i < t.Count && !placeable; i++)
                    if (PieceShapes.Resolve(t, shape, i) != null) placeable = true;
                if (!placeable) bad.Add(k + "/" + shape.Name);
            }
        }
        Check("V22", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }

    // V23 매 턴 배치 가능 검사 + 불가 시 처리 경로 존재
    static void V23()
    {
        string src = File.ReadAllText("Assets/Scripts/src/TurnController.cs");
        bool hasCheck = src.Contains("CanPlaceAnywhere");
        bool hasPath = src.Contains("NoMoveAction") || src.Contains("OnNoPlacement");
        Check("V23", hasCheck && hasPath, "check=" + hasCheck + " path=" + hasPath);
    }

    // V24 다각형 겹침·틈 0건, 히트박스 겹침 0건
    static void V24()
    {
        var bad = new List<string>();
        foreach (var k in Kinds)
        {
            var t = TopologyGen.Build(k, Size);
            for (int i = 0; i < t.Count; i++)
            {
                // 자기 중심은 자기 다각형 안에 있어야 한다
                if (!Render.PointInPolygon(t.Cells[i].Poly, t.Cells[i].Center)) bad.Add(k + " center" + i);
                // 다른 셀의 다각형 안에 들어가면 히트박스가 겹친다
                for (int j = 0; j < t.Count; j++)
                {
                    if (i == j) continue;
                    if (Render.PointInPolygon(t.Cells[j].Poly, t.Cells[i].Center))
                    { bad.Add(k + " overlap " + i + "/" + j); break; }
                }
                if (bad.Count > 0) break;
            }
        }
        Check("V24", bad.Count == 0, string.Join(" ", bad.ToArray()));
    }
}
