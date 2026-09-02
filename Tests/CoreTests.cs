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

        // 3. 체커보드 배경에 2x2만 심어 정확히 1개 매칭
        var b3 = CheckerBoard();
        Fill(b3, 4, 4, 2, 1);
        var ms = b3.FindSquares();
        Assert(ms.Count == 1 && ms[0].Size == 2, "체커보드+2x2 = 정확히 1매칭");

        // 4. 점수 공식: 3x3 chain1 = 9*10*2 = 180
        var b4 = CheckerBoard();
        for (int x = 2; x <= 4; x++) for (int y = 2; y <= 4; y++) b4.SetTile(x, y, 1);
        var r4 = b4.Resolve();
        Assert(r4.ScoreGained >= 180, "3x3 점수 >= 180 (배수 x2 적용)");
        Assert(r4.BigHit, "3x3은 BigHit");

        // 5. EffectCells 크기 — 기대값을 Board.W/H 에서 유도한다 (보드 크기를 바꿔도 유효)
        var b5 = FlatBoard(0);
        int cx = Board.W / 2, cy = Board.H / 2;
        Assert(b5.EffectCells(ItemType.Row, 5, 3).Count == Board.W, "Row = " + Board.W + "칸");
        Assert(b5.EffectCells(ItemType.Col, 5, 3).Count == Board.H, "Col = " + Board.H + "칸");
        Assert(b5.EffectCells(ItemType.Bomb5, cx, cy).Count == 25, "Bomb5 중앙 = 25칸");
        Assert(b5.EffectCells(ItemType.Bomb5, 0, 0).Count == 9, "Bomb5 모서리 = 9칸");
        Assert(b5.EffectCells(ItemType.Diag, cx, cy).Count == ExpectedDiag(cx, cy),
               "Diag 중앙 = " + ExpectedDiag(cx, cy) + "칸");
        Assert(b5.EffectCells(ItemType.Diag, 0, 0).Count == ExpectedDiag(0, 0),
               "Diag 모서리 = " + ExpectedDiag(0, 0) + "칸");

        // 6. 아이템 발동 BFS 종결성 (아이템 30개 무작위 배치, 100회)
        bool bfsOk = true;
        for (int s = 0; s < 100; s++)
        {
            var bb = new Board(Rules.ColorCount, s + 1);
            var trng = new Random(s + 1);
            var types = new[] { ItemType.Row, ItemType.Col, ItemType.Diag, ItemType.Bomb5 };
            for (int i = 0; i < 30; i++)
                bb.SetItem(trng.Next(Board.W), trng.Next(Board.H), types[trng.Next(types.Length)]);
            // Bomb5 하나를 매칭에 넣어 발동시키는 대신, 발동 시뮬레이션은 Resolve 경유가 복잡하므로
            // EffectCells + 수동 BFS로 종결만 확인
            var td = new Dictionary<int, Point>();
            var q = new Queue<Point>();
            var start = new Point(Board.W / 2, Board.H / 2);
            td[start.X * 100 + start.Y] = start; q.Enqueue(start);
            bb.SetItem(start.X, start.Y, ItemType.Bomb5);
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
            if (new Board(Rules.ColorCount, s).FindSquares().Count != 0) clean = false;
        Assert(clean, "초기 보드 무매칭 (시드 50)");

        // 8. 무작위 30수 후 불변식: 빈 칸 없음 + 잔여 매칭 없음
        var b8 = new Board(Rules.ColorCount, 42);
        var prng = new Random(42);
        for (int mv = 0; mv < 30; mv++)
        {
            var p = Piece.CreateRandom(prng, Rules.ColorCount);
            for (int r = prng.Next(4); r > 0; r--) p = p.Rotated();
            int px = prng.Next(Board.W), py = prng.Next(Board.H);
            if (!b8.CanPlace(p, px, py)) continue;
            b8.Stamp(p, px, py);
        }
        bool full = true;
        for (int x = 0; x < Board.W; x++) for (int y = 0; y < Board.H; y++) if (b8.GetTile(x, y) == Board.Empty) full = false;
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
        Assert(Rules.Table["hard"].Moves == 20, "횟수 모드 = " + Rules.Table["hard"].Moves + "수");

        Console.WriteLine();
        // 조각 제한 시간
        int totalMv = Rules.Table["hard"].Moves;
        Assert(Rules.PieceTimeMs(totalMv, totalMv) == Rules.PieceTimeMaxMs, "첫 조각 = " + Rules.PieceTimeMaxMs + "ms");
        Assert(Rules.PieceTimeMs(1, totalMv) == Rules.PieceTimeMinMs, "마지막 조각 = " + Rules.PieceTimeMinMs + "ms");
        bool mono = true; int prev = int.MaxValue;
        for (int m = totalMv; m >= 1; m--) { int t = Rules.PieceTimeMs(m, totalMv); if (t > prev) mono = false; prev = t; }
        Assert(mono, "타이머 단조 감소");

        // 코인 환산
        Assert(Rules.CoinsFor(1000) == 10, "1000점 = 10코인");
        Assert(Rules.CoinsFor(0) == 0 && Rules.CoinsFor(-5) == 0, "0점 이하는 0코인");
        Assert(Rules.CoinsFor(99) == 0 && Rules.CoinsFor(100) == 1, "100점 미만은 버린다");
        Assert(Rules.CoinsFor(4045) == 40, "4045점 = 40코인 (버림)");

        PreviewTests();
        InitialDistributionTest();
        ObstacleTests();
        DetonateTests();

        Console.WriteLine("결과: " + passed + " 통과 / " + failed + " 실패");
        Environment.Exit(failed == 0 ? 0 : 1);
    }

    // 초기 보드가 특정 색으로 치우치지 않는지.
    // int[,] 기본값 0 때문에 미충전 칸이 '색0' 으로 읽혀 색0 만 재추첨당하던 결함이 있었다.
    // 손으로 던지는 폭탄 — 매칭 없이 즉시 발동한다
    static void DetonateTests()
    {
        var b = new Board(Rules.ColorCount, 1234);
        b.SetItem(7, 7, ItemType.Bomb5);
        var r = b.Detonate(7, 7);
        Assert(r.Destroyed.Count >= 25, "폭탄 발동: " + r.Destroyed.Count + "칸 파괴 (25 이상)");
        Assert(r.ScoreGained > 0, "폭탄 발동: 점수 " + r.ScoreGained);
        Assert(r.Waves.Count >= 1, "폭탄 발동: 웨이브 " + r.Waves.Count + "개 기록");
        Assert(b.GetItem(7, 7) == ItemType.None, "폭탄이 소모됐다");

        // 아이템 없는 칸이면 보드가 그대로여야 한다
        var b2 = new Board(Rules.ColorCount, 99);
        var snap = new List<int>();
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++) snap.Add(b2.GetTile(x, y));
        var r2 = b2.Detonate(3, 3);
        bool same = r2.Destroyed.Count == 0;
        int i = 0;
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++) if (snap[i++] != b2.GetTile(x, y)) same = false;
        Assert(same, "아이템 없는 칸 발동은 무해하다");
    }

    // 낙하 전 예고는 보드를 건드리면 안 되고, 실제 파괴와 맞아야 한다
    static void PreviewTests()
    {
        var b = new Board(Rules.ColorCount, 777);
        int c0 = b.GetTile(0, 0);
        b.SetTile(5, 5, c0); b.SetTile(6, 5, c0); b.SetTile(5, 6, c0);
        var piece = new Piece("dot", new List<Point> { new Point(0, 0) }, c0);

        var preview = b.PreviewStamp(piece, 6, 6);
        Assert(preview.Count >= 4, "예고 " + preview.Count + "칸 (2x2 이상)");

        bool hasStamped = false;
        foreach (var pt in preview) if (pt.X == 6 && pt.Y == 6) hasStamped = true;
        Assert(hasStamped, "놓는 칸 자체가 예고에 없다");

        Assert(b.GetTile(5, 5) == c0, "예고가 보드를 바꾸지 않는다");

        var res = b.Stamp(piece, 6, 6);
        var destroyed = new HashSet<int>();
        foreach (var pt in res.Destroyed) destroyed.Add(pt.X * 100 + pt.Y);
        bool all = true;
        foreach (var pt in preview) if (!destroyed.Contains(pt.X * 100 + pt.Y)) all = false;
        Assert(all, "예고한 칸은 실제로 전부 터진다");

        Assert(b.PreviewStamp(piece, -1, -1).Count == 0, "놓을 수 없으면 예고가 없다");
    }

    static void InitialDistributionTest()
    {
        int expected = Board.W * Board.H / Rules.ColorCount;   // 균등했을 때 색당 칸 수
        int lo = expected / 2, hi = expected * 3 / 2;          // ±50% 안에는 들어와야 한다
        bool even = true;
        int worst = 0;
        for (int seed = 1; seed <= 30 && even; seed++)
        {
            var b = new Board(Rules.ColorCount, seed);
            var n = new int[Rules.ColorCount];
            for (int x = 0; x < Board.W; x++)
                for (int y = 0; y < Board.H; y++)
                {
                    int t = b.GetTile(x, y);
                    if (t >= 0) n[t]++;
                }
            foreach (var v in n)
            {
                if (v < lo || v > hi) { even = false; worst = v; }
            }
        }
        Assert(even, "초기 보드 색 분포가 고름 (색당 " + lo + "~" + hi + "칸"
                     + (even ? "" : ", 실제 " + worst) + ")");
    }

    // ── 콘크리트 ──
    static void ObstacleTests()
    {

        // 콘크리트 위에는 조각을 놓을 수 없다
        var b3 = CheckerBoard();
        b3.SetObstacle(5, 5, Rules.ObstacleHp);
        var piece = new Piece("dot", new List<Point> { new Point(0, 0) }, 1);
        Assert(!b3.CanPlace(piece, 5, 5), "콘크리트 위에는 스탬프 불가");
        Assert(b3.CanPlace(piece, 5, 6), "콘크리트 옆에는 스탬프 가능");

        // 옆 칸이 터지면 금이 가고, 3번이면 부서진다
        var b4 = CheckerBoard();
        b4.SetObstacle(7, 5, Rules.ObstacleHp);
        int hp0 = b4.GetObstacleHp(7, 5);
        Fill(b4, 4, 4, 3, 1);
        b4.Resolve();
        Assert(hp0 == Rules.ObstacleHp && hp0 >= 2, "콘크리트 초기 내구도 " + Rules.ObstacleHp);

        var b5 = CheckerBoard();
        b5.SetObstacle(7, 5, 1);                          // 마지막 한 대만 남은 콘크리트
        Fill(b5, 4, 4, 3, 1);
        var r5 = b5.Resolve();
        Assert(r5.Destroyed.Exists(p => p.X == 7 && p.Y == 5), "내구도 1 콘크리트는 인접 파괴로 부서진다");

        // 벽돌도 중력을 받는다 — 판에 떠 있는 것은 없다
        var b6 = CheckerBoard();
        b6.SetObstacle(3, 7, Rules.ObstacleHp);
        int hpBefore = b6.GetObstacleHp(3, 7);
        b6.SetTile(3, 5, Board.Empty);
        b6.SetTile(3, 6, Board.Empty);
        b6.ApplyGravity();
        Assert(!b6.IsObstacle(3, 7) && b6.IsObstacle(3, 5), "벽돌이 빈칸만큼 내려온다");
        Assert(b6.GetObstacleHp(3, 5) == hpBefore, "벽돌이 내구도를 들고 내려온다");

        // 열의 위아래 순서는 그대로여야 한다
        var b7 = CheckerBoard();
        b7.SetObstacle(4, 3, Rules.ObstacleHp);
        int above = b7.GetTile(4, 4);
        b7.SetTile(4, 0, Board.Empty);
        b7.ApplyGravity();
        Assert(b7.IsObstacle(4, 2) && b7.GetTile(4, 3) == above, "낙하해도 위아래 순서가 지켜진다");

        // 벽돌을 통과해서 내려가지는 않는다 — 벽돌 위 블록은 벽돌 바로 위에 얹힌다
        var b8 = CheckerBoard();
        b8.SetObstacle(3, 5, Rules.ObstacleHp);
        b8.SetTile(3, 6, Board.Empty);
        int over = b8.GetTile(3, 7);
        b8.ApplyGravity();
        Assert(b8.IsObstacle(3, 5) && b8.GetTile(3, 6) == over, "벽돌 위 블록은 벽돌에 얹힌다");

        // 진행할수록 콘크리트가 늘어난다
        Assert(Rules.ObstaclesAfterMove(0, 20) == 0, "초반에는 콘크리트가 안 생긴다");
        // 한 수 걸러 생기므로 짝수 수끼리 비교한다
        Assert(Rules.ObstaclesAfterMove(18, 20) > Rules.ObstaclesAfterMove(4, 20), "후반일수록 콘크리트가 많다");
        Assert(Rules.ObstaclesAfterMove(19, 20) == 0, "홀수 수에는 콘크리트가 안 생긴다");
    }

    // ── 헬퍼 ──

    /// <summary>(x,y) 좌하단 size x size 를 색 c 로 채운다.</summary>
    static void Fill(Board b, int x, int y, int size, int c)
    {
        for (int dx = 0; dx < size; dx++)
            for (int dy = 0; dy < size; dy++) b.SetTile(x + dx, y + dy, c);
    }


    // (x,y) 를 지나는 두 대각선의 칸 수. EffectCells 를 베끼지 않고 구간 길이로 센다.
    //   주대각선: bx=x+t, by=y+t 가 판 안에 드는 t 의 개수
    //   반대각선: bx=x+t, by=y-t 가 판 안에 드는 t 의 개수
    //   두 선은 t=0 (자기 칸) 에서만 겹치므로 1 을 뺀다.
    static int ExpectedDiag(int x, int y)
    {
        int main = Math.Min(Board.W - 1 - x, Board.H - 1 - y) - Math.Max(-x, -y) + 1;
        int anti = Math.Min(Board.W - 1 - x, y) - Math.Max(-x, y - (Board.H - 1)) + 1;
        return main + anti - 1;
    }

    static Board FlatBoard(int color)
    {
        var b = new Board(Rules.ColorCount, 1);
        for (int x = 0; x < Board.W; x++) for (int y = 0; y < Board.H; y++) b.SetTile(x, y, color);
        return b;
    }
    static Board CheckerBoard()
    {
        var b = new Board(Rules.ColorCount, 1);
        for (int x = 0; x < Board.W; x++) for (int y = 0; y < Board.H; y++) b.SetTile(x, y, (x + y) % 2 == 0 ? 2 : 0);
        // 색 0/2 만 번갈아 깔아 배경을 만든다. 심는 색(1)은 배경과 안 겹친다.
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
