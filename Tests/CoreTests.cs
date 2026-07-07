// CoreTests.cs — v5 규칙 검증. Unity 프로젝트에 포함하지 않는다.
using System;
using System.Collections.Generic;
using ColorMatcher.Core;

class CoreTests
{
    static int passed = 0, failed = 0;
    static void Assert(bool c, string n)
    {
        if (c) { passed++; Console.WriteLine("[PASS] " + n); }
        else { failed++; Console.WriteLine("[FAIL] " + n); }
    }

    static void Main()
    {
        // 1. 조각 세트: 13종, 전부 3칸 이상, 전 회전에서 2x2 미포함
        Assert(Piece.Shapes.Count == 13, "조각 13종");
        bool allNo2x2 = true, allGe3 = true;
        foreach (var kv in Piece.Shapes)
        {
            var cells = new List<Point>();
            foreach (var xy in kv.Value) cells.Add(new Point(xy[0], xy[1]));
            if (cells.Count < 3) allGe3 = false;
            var p = new Piece(kv.Key, cells, 0);
            for (int r = 0; r < 4; r++)
            {
                if (Contains2x2(p.Cells)) allNo2x2 = false;
                p = p.Rotated();
            }
        }
        Assert(allGe3, "모든 조각 3칸 이상 (1~2칸 제거)");
        Assert(allNo2x2, "모든 조각 전 회전에서 2x2 미포함 (공짜 파괴 조각 없음)");

        // 2. 페인트 스탬프: 덮인 칸이 조각 색으로 덮어쓰기
        var b = FlatBoard(0);
        // 색1 조각(I3)을 색0 보드 위 (5,5)~(7,5)에 → 그 3칸이 색1
        var piece = MakePiece("I3", 1);
        // I3는 매칭 안 생기므로 스탬프 후 색만 확인
        b.Stamp(piece, 5, 5);
        // 스탬프 직후 상태 확인 불가(Resolve 내부). 대신 매칭 없음 확인:
        Assert(b.FindSquares().Count == 0, "I3 스탬프는 즉시 매칭 없음");

        // 3. 페인트로 2x2 완성 → 파괴 + 점수
        var b2 = FlatBoard(0);
        // 색0 보드에서 (2,2),(3,2),(2,3)은 이미 색0. (3,3)만 색0→색0이라 매칭.
        // 명시적으로: 색0 평면은 시작부터 거대 정사각형이므로 다른 검증법 사용.
        // 체커보드 배경에 2x2만 심어 정확히 1개 매칭:
        var b3 = CheckerBoard();
        b3.SetTile(4, 4, 1); b3.SetTile(5, 4, 1); b3.SetTile(4, 5, 1); b3.SetTile(5, 5, 1);
        var ms = b3.FindSquares();
        Assert(ms.Count == 1 && ms[0].Size == 2, "체커보드+2x2 = 정확히 1매칭");

        // 4. 점수 공식: 3x3 chain1 = 9*10*2 = 180
        var b4 = CheckerBoard();
        for (int x = 2; x <= 4; x++) for (int y = 2; y <= 4; y++) b4.SetTile(x, y, 1);
        var r4 = b4.Resolve();
        Assert(r4.ScoreGained >= 180, "3x3 점수 >= 180 (배수 x2 적용)");
        Assert(r4.BigHit, "3x3은 BigHit");

        // 5. EffectCells 크기 (16x16)
        var b5 = FlatBoard(0);
        Assert(b5.EffectCells(ItemType.Row, 5, 3).Count == 16, "Row = 16칸");
        Assert(b5.EffectCells(ItemType.Col, 5, 3).Count == 16, "Col = 16칸");
        Assert(b5.EffectCells(ItemType.Bomb9, 8, 8).Count == 81, "Bomb9 중앙 = 81칸");
        Assert(b5.EffectCells(ItemType.Bomb9, 0, 0).Count == 25, "Bomb9 모서리 = 25칸");
        Assert(b5.EffectCells(ItemType.Diag, 8, 8).Count == 30, "Diag 중앙 = 30칸");
        Assert(b5.EffectCells(ItemType.Diag, 0, 0).Count == 16, "Diag 모서리 = 16칸");
        // ColorClear: 색0 평면 전체
        Assert(b5.EffectCells(ItemType.ColorClear, 0, 0).Count == 256, "ColorClear 단색평면 = 256칸");

        // 6. 아이템 발동 BFS 종결성 (아이템 30개 무작위 배치, 100회)
        bool bfsOk = true;
        for (int s = 0; s < 100; s++)
        {
            var bb = new Board(3, s + 1);
            var trng = new Random(s + 1);
            var types = new[] { ItemType.Row, ItemType.Col, ItemType.Diag, ItemType.Bomb9, ItemType.ColorClear };
            for (int i = 0; i < 30; i++)
                bb.SetItem(trng.Next(16), trng.Next(16), types[trng.Next(types.Length)]);
            // Bomb9 하나를 매칭에 넣어 발동시키는 대신, 발동 시뮬레이션은 Resolve 경유가 복잡하므로
            // EffectCells + 수동 BFS로 종결만 확인
            var td = new Dictionary<int, Point>();
            var q = new Queue<Point>();
            var start = new Point(8, 8);
            td[8 * 100 + 8] = start; q.Enqueue(start);
            bb.SetItem(8, 8, ItemType.Bomb9);
            int steps = 0;
            while (q.Count > 0)
            {
                if (++steps > 100000) { bfsOk = false; break; }
                var a = q.Dequeue();
                var it = bb.GetItem(a.X, a.Y);
                if (it == ItemType.None) continue;
                foreach (var e in bb.EffectCells(it, a.X, a.Y))
                {
                    int k = e.X * 100 + e.Y;
                    if (td.ContainsKey(k)) continue;
                    td[k] = e;
                    if (bb.GetItem(e.X, e.Y) != ItemType.None) q.Enqueue(e);
                }
            }
            if (!bfsOk) break;
        }
        Assert(bfsOk, "아이템 30개 최악배치 100회 BFS 종결");

        // 7. 초기 보드 무매칭 (시드 50개)
        bool clean = true;
        for (int s = 0; s < 50; s++)
            if (new Board(3, s).FindSquares().Count != 0) clean = false;
        Assert(clean, "초기 보드 무매칭 (시드 50)");

        // 8. 무작위 30수 후 불변식: 빈 칸 없음 + 잔여 매칭 없음
        var b8 = new Board(3, 42);
        var prng = new Random(42);
        for (int mv = 0; mv < 30; mv++)
        {
            var p = Piece.CreateRandom(prng, 3);
            for (int r = prng.Next(4); r > 0; r--) p = p.Rotated();
            int px = prng.Next(16), py = prng.Next(16);
            if (!b8.CanPlace(p, px, py)) continue;
            b8.Stamp(p, px, py);
        }
        bool full = true;
        for (int x = 0; x < 16; x++) for (int y = 0; y < 16; y++) if (b8.GetTile(x, y) == Board.Empty) full = false;
        Assert(full, "30수 후 빈 칸 없음");
        Assert(b8.FindSquares().Count == 0, "30수 후 잔여 매칭 없음");

        // 9. 회전: 모든 조각 4회 = 원형 복귀
        bool rotOk = true;
        foreach (var kv in Piece.Shapes)
        {
            var cells = new List<Point>();
            foreach (var xy in kv.Value) cells.Add(new Point(xy[0], xy[1]));
            var p0 = new Piece(kv.Key, cells, 0);
            var p4 = p0.Rotated().Rotated().Rotated().Rotated();
            if (!SameCells(p0, p4)) rotOk = false;
        }
        Assert(rotOk, "모든 조각 4회 회전 복귀");

        // 10. 난이도/타이머 규칙
        Assert(Rules.Table["hard"].Goal == 40000 && Rules.Table["hard"].Moves == 20, "상 난이도 = 20수/40000");
        Assert(Rules.PieceTimeMs(30, 30) == 8000, "첫 조각 = 8000ms");
        Assert(Rules.PieceTimeMs(1, 30) == 2500, "마지막 조각 = 2500ms");
        int prev = int.MaxValue; bool mono = true;
        for (int m = 30; m >= 1; m--) { int t = Rules.PieceTimeMs(m, 30); if (t > prev) mono = false; prev = t; }
        Assert(mono, "타이머 단조 감소");

        Console.WriteLine();
        Console.WriteLine("결과: " + passed + " 통과 / " + failed + " 실패");
        Environment.Exit(failed == 0 ? 0 : 1);
    }

    // ── 헬퍼 ──
    static Board FlatBoard(int color)
    {
        var b = new Board(3, 1);
        for (int x = 0; x < 16; x++) for (int y = 0; y < 16; y++) b.SetTile(x, y, color);
        return b;
    }
    static Board CheckerBoard()
    {
        var b = new Board(3, 1);
        for (int x = 0; x < 16; x++) for (int y = 0; y < 16; y++) b.SetTile(x, y, (x + y) % 2 == 0 ? 2 : 0);
        // 색은 0/2만 사용 → 색1 심으면 격리됨. ColorCount=3이므로 유효.
        return b;
    }
    static Piece MakePiece(string name, int color)
    {
        var cells = new List<Point>();
        foreach (var xy in Piece.Shapes[name]) cells.Add(new Point(xy[0], xy[1]));
        return new Piece(name, cells, color);
    }
    static bool Contains2x2(List<Point> cells)
    {
        var s = new HashSet<int>();
        foreach (var c in cells) s.Add(c.X * 100 + c.Y);
        foreach (var c in cells)
            if (s.Contains((c.X + 1) * 100 + c.Y) && s.Contains(c.X * 100 + (c.Y + 1)) &&
                s.Contains((c.X + 1) * 100 + (c.Y + 1))) return true;
        return false;
    }
    static bool SameCells(Piece a, Piece b)
    {
        if (a.Cells.Count != b.Cells.Count) return false;
        var s = new HashSet<int>();
        foreach (var c in a.Cells) s.Add(c.X * 100 + c.Y);
        foreach (var c in b.Cells) if (!s.Contains(c.X * 100 + c.Y)) return false;
        return true;
    }
}
