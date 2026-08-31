// ColorMatcherCore.cs
// 칼라 매쳐 (Chroma Drop) 코어 로직 — UnityEngine 의존성 없음.
// HTML 프로토타입 v5에서 확정된 규칙을 그대로 옮긴 것.
//
// 담당 범위(표현 계층과 분리):
//  - 16x16 보드, 3색 랜덤 팔레트(색 인덱스만 관리, 실제 색은 표현 계층)
//  - 페인트 방식: 조각이 단색을 갖고, 찍으면 덮인 칸을 그 색으로 덮어쓰기
//  - 정사각형(2x2 이상) 매칭 판정(DP) + 겹침 그리디
//  - 아이템 그리드(row/col/diag/bomb9/colorclear) 생성·발동(BFS 연쇄)
//  - 중력/리필, 점수/콤보, 목표 점수 판정
//
// 표현 계층(Unity MonoBehaviour)이 담당: 렌더링, 입력, 애니메이션, 사운드,
//  조각 타이머(가변), 타임어택 타이머, 랭킹/공유.
//
// 주의: 밸런스 수치(난이도 목표/수, 아이템 등장 확률 등)는 플레이테스트로 검증되지 않은
//  추정치다. 실측 후 조정 대상.

using System;
using System.Collections.Generic;

namespace ColorMatcher.Core
{
    public struct Point
    {
        public int X, Y;
        public Point(int x, int y) { X = x; Y = y; }
        public override string ToString() { return "(" + X + "," + Y + ")"; }
    }

    public enum ItemType { None, Row, Col, Diag, Bomb5, ColorClear }

    public enum GameMode { Score, TimeAttack }

    /// <summary>같은 색 정사각형 하나</summary>
    public struct SquareMatch
    {
        public int X, Y, Size, Color;  // (X,Y)=좌하단, Size=한 변
    }

    /// <summary>한 번의 스탬프/발동 결과 요약 (표현 계층이 애니메이션에 사용)</summary>
    public class ResolveResult
    {
        public int ScoreGained;
        public int TilesDestroyed;                       // 목표/통계용 파괴 칸 수
        public int MaxChain;
        public List<SquareMatch> Matches = new List<SquareMatch>();
        public List<Point> Destroyed = new List<Point>(); // 이번에 비워진 칸(연출용)
        public List<SpawnedItem> Spawns = new List<SpawnedItem>();
        public bool BigHit;                              // 3x3 이상 매칭 발생 여부
        public List<Wave> Waves = new List<Wave>();      // 연쇄 단계별 경계(연출용)
    }

    /// <summary>Destroyed 안의 한 연쇄 단계 구간. 표현 계층이 순차 연출에 사용.</summary>
    public struct Wave
    {
        public int Start;      // Destroyed[Start .. MatchEnd) = 매칭으로 파괴된 칸
        public int MatchEnd;   // Destroyed[MatchEnd .. End)   = 아이템 발동으로 연쇄 파괴된 칸
        public int End;
    }

    public struct SpawnedItem
    {
        public int X, Y;
        public ItemType Type;
    }

    public class Piece
    {
        public string Name;
        public List<Point> Cells;
        public int Color;

        public Piece(string name, List<Point> cells, int color)
        {
            Name = name; Cells = cells; Color = color;
        }

        /// <summary>시계방향 90도 회전 (x,y)->(y,-x) 후 최소값 0 정규화</summary>
        public Piece Rotated()
        {
            var r = new List<Point>();
            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var c in Cells)
            {
                var p = new Point(c.Y, -c.X);
                r.Add(p);
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
            }
            for (int i = 0; i < r.Count; i++)
                r[i] = new Point(r[i].X - minX, r[i].Y - minY);
            return new Piece(Name, r, Color);
        }

        // v5 조각 세트: 1~2칸 제거 + 2x2 포함 조각(O,P,2x3) 제거.
        // 모든 조각이 (전 회전에서) 2x2 미포함 → "공짜 파괴" 조각 없음.
        public static readonly Dictionary<string, int[][]> Shapes = new Dictionary<string, int[][]>
        {
            { "I",  new[]{ new[]{0,0}, new[]{1,0}, new[]{2,0}, new[]{3,0} } },
            { "T",  new[]{ new[]{0,0}, new[]{1,0}, new[]{2,0}, new[]{1,1} } },
            { "S",  new[]{ new[]{1,0}, new[]{2,0}, new[]{0,1}, new[]{1,1} } },
            { "Z",  new[]{ new[]{0,0}, new[]{1,0}, new[]{1,1}, new[]{2,1} } },
            { "L",  new[]{ new[]{0,0}, new[]{0,1}, new[]{0,2}, new[]{1,0} } },
            { "J",  new[]{ new[]{1,0}, new[]{1,1}, new[]{1,2}, new[]{0,0} } },
            { "I3", new[]{ new[]{0,0}, new[]{1,0}, new[]{2,0} } },
            { "V3", new[]{ new[]{0,0}, new[]{1,0}, new[]{0,1} } },
            { "PLUS", new[]{ new[]{1,0}, new[]{0,1}, new[]{1,1}, new[]{2,1}, new[]{1,2} } },
            { "W5", new[]{ new[]{0,0}, new[]{0,1}, new[]{1,1}, new[]{1,2}, new[]{2,2} } },
            { "U5", new[]{ new[]{0,0}, new[]{2,0}, new[]{0,1}, new[]{1,1}, new[]{2,1} } },
            { "T5", new[]{ new[]{0,0}, new[]{1,0}, new[]{2,0}, new[]{1,1}, new[]{1,2} } },
            { "Z5", new[]{ new[]{0,0}, new[]{1,0}, new[]{1,1}, new[]{1,2}, new[]{2,2} } },
        };
        static readonly string[] ShapeNames;

        static Piece()
        {
            ShapeNames = new string[Shapes.Count];
            Shapes.Keys.CopyTo(ShapeNames, 0);
        }

        public static Piece CreateRandom(Random rng, int colorCount)
        {
            string name = ShapeNames[rng.Next(ShapeNames.Length)];
            var cells = new List<Point>();
            foreach (var xy in Shapes[name]) cells.Add(new Point(xy[0], xy[1]));
            return new Piece(name, cells, rng.Next(colorCount));
        }
    }

    public class Board
    {
        public const int W = 16, H = 16, Empty = -1;
        public const int BaseTileScore = 10;
        public const double ChainBonus = 0.5;

        public readonly int ColorCount;
        readonly int[,] tiles;
        readonly ItemType[,] items;
        readonly Random rng;

        public Board(int colorCount, int seed)
        {
            ColorCount = colorCount;
            rng = new Random(seed);
            tiles = new int[W, H];
            items = new ItemType[W, H];
            FillNoInitialMatch();
        }

        public int GetTile(int x, int y) { return tiles[x, y]; }
        public void SetTile(int x, int y, int c) { tiles[x, y] = c; }
        public ItemType GetItem(int x, int y) { return items[x, y]; }
        public void SetItem(int x, int y, ItemType t) { items[x, y] = t; }
        public bool InBounds(int x, int y) { return x >= 0 && x < W && y >= 0 && y < H; }

        public bool CanPlace(Piece p, int px, int py)
        {
            foreach (var c in p.Cells)
                if (!InBounds(px + c.X, py + c.Y)) return false;
            return true;
        }

        /// <summary>페인트 스탬프: 덮인 칸을 조각 색으로 덮어쓰기 → 연쇄 해소</summary>
        public ResolveResult Stamp(Piece p, int px, int py)
        {
            if (!CanPlace(p, px, py))
                throw new InvalidOperationException("piece out of bounds");
            foreach (var c in p.Cells)
                tiles[px + c.X, py + c.Y] = p.Color; // 덮어쓰기 (아이템은 유지)
            return Resolve();
        }

        /// <summary>매칭 → 아이템 발동(BFS) → 파괴 → 스폰 → 중력/리필을 매칭 없을 때까지</summary>
        public ResolveResult Resolve()
        {
            var res = new ResolveResult();
            int chain = 0;
            while (true)
            {
                var matches = FindSquares();
                if (matches.Count == 0) break;
                chain++;
                double mult = 1.0 + ChainBonus * (chain - 1);

                int matchTiles = 0;
                foreach (var m in matches) matchTiles += m.Size * m.Size;
                bool huge = false;   // 이 단계에 4x4 이상 매칭이 있었나 (colorclear 조건)

                var toDestroy = new Dictionary<int, Point>();
                var order = new List<Point>();   // 매칭 칸 먼저, 아이템 발동 칸 나중
                var actQueue = new Queue<Point>();

                foreach (var m in matches)
                {
                    int sizeMult = 1 << (m.Size - 2); // 2x2=x1,3x3=x2,4x4=x4
                    res.ScoreGained += (int)(m.Size * m.Size * BaseTileScore * sizeMult * mult);
                    res.Matches.Add(m);
                    if (m.Size >= 3) res.BigHit = true;
                    if (m.Size >= 4) huge = true;
                    for (int dx = 0; dx < m.Size; dx++)
                        for (int dy = 0; dy < m.Size; dy++)
                        {
                            int x = m.X + dx, y = m.Y + dy;
                            int k = Key(x, y);
                            if (!toDestroy.ContainsKey(k)) { toDestroy[k] = new Point(x, y); order.Add(new Point(x, y)); }
                            if (items[x, y] != ItemType.None) actQueue.Enqueue(new Point(x, y));
                        }
                }

                int matchEnd = order.Count;

                // 아이템 발동 BFS (발동으로 파괴된 칸의 아이템도 연쇄 발동)
                int actCount = 0;
                while (actQueue.Count > 0)
                {
                    var a = actQueue.Dequeue();
                    ItemType t = items[a.X, a.Y];
                    if (t == ItemType.None) continue;
                    foreach (var e in EffectCells(t, a.X, a.Y))
                    {
                        int k = Key(e.X, e.Y);
                        if (toDestroy.ContainsKey(k)) continue;
                        toDestroy[k] = e;
                        order.Add(e);
                        actCount++;
                        if (items[e.X, e.Y] != ItemType.None) actQueue.Enqueue(e);
                    }
                }
                if (actCount > 0)
                    res.ScoreGained += (int)(actCount * BaseTileScore * mult);

                // 실제 파괴
                res.TilesDestroyed += order.Count;
                int waveStart = res.Destroyed.Count;
                foreach (var pt in order)
                {
                    tiles[pt.X, pt.Y] = Empty;
                    items[pt.X, pt.Y] = ItemType.None;
                    res.Destroyed.Add(pt);
                }
                res.Waves.Add(new Wave
                {
                    Start = waveStart,
                    MatchEnd = waveStart + matchEnd,
                    End = res.Destroyed.Count,
                });

                ApplyGravity();
                Refill();

                // 스폰: 이 단계에서 (연쇄/동시파괴) 조건 충족 시. 즉시 파괴 방지 위해 실제 배치는 여기서.
                MaybeSpawn(chain, matchTiles, huge, res);
            }
            res.MaxChain = chain;
            return res;
        }

        // v5 스폰 규칙:
        //  - 한 단계 매칭 6타일 이상: row/col/diag 중 랜덤 1개
        //  - 연쇄 2 도달: row
        //  - 연쇄 3 도달: bomb5
        //  - 연쇄 5 이상 또는 4x4 이상 매칭: colorclear (랜덤 색으로 재칠)
        void MaybeSpawn(int chain, int matchTiles, bool huge, ResolveResult res)
        {
            if (matchTiles >= 6)
            {
                var pool = new[] { ItemType.Row, ItemType.Col, ItemType.Diag };
                SpawnItem(pool[rng.Next(pool.Length)], false, res);
            }
            // '도달'이므로 == 다. >= 로 두면 연쇄가 길어질수록 매 단계마다 또 나온다.
            if (chain == 2) SpawnItem(ItemType.Row, false, res);
            if (chain == 3) SpawnItem(ItemType.Bomb5, false, res);
            if (chain == 5 || huge) SpawnItem(ItemType.ColorClear, true, res);
        }

        void SpawnItem(ItemType type, bool randomColor, ResolveResult res)
        {
            var candidates = new List<Point>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (items[x, y] == ItemType.None && tiles[x, y] != Empty)
                        candidates.Add(new Point(x, y));
            if (candidates.Count == 0) return;

            var p = candidates[rng.Next(candidates.Count)];
            if (randomColor)
            {
                int guard = 0;
                do { tiles[p.X, p.Y] = rng.Next(ColorCount); }
                while (Makes2x2At(p.X, p.Y) && guard++ < 16); // 스폰이 즉시 매칭을 만들지 않게
            }
            items[p.X, p.Y] = type;
            res.Spawns.Add(new SpawnedItem { X = p.X, Y = p.Y, Type = type });
        }

        /// <summary>아이템 발동 시 파괴 대상 칸 목록</summary>
        public List<Point> EffectCells(ItemType t, int x, int y)
        {
            var cells = new List<Point>();
            switch (t)
            {
                case ItemType.Row:
                    for (int i = 0; i < W; i++) cells.Add(new Point(i, y));
                    break;
                case ItemType.Col:
                    for (int i = 0; i < H; i++) cells.Add(new Point(x, i));
                    break;
                case ItemType.Diag:
                    for (int bx = 0; bx < W; bx++)
                        for (int by = 0; by < H; by++)
                            if (bx - x == by - y || bx - x == -(by - y)) cells.Add(new Point(bx, by));
                    break;
                case ItemType.Bomb5:
                    for (int dx = -2; dx <= 2; dx++)
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            int bx = x + dx, by = y + dy;
                            if (InBounds(bx, by)) cells.Add(new Point(bx, by));
                        }
                    break;
                case ItemType.ColorClear:
                    int c = tiles[x, y];
                    if (c != Empty)
                        for (int bx = 0; bx < W; bx++)
                            for (int by = 0; by < H; by++)
                                if (tiles[bx, by] == c) cells.Add(new Point(bx, by));
                    break;
            }
            return cells;
        }

        /// <summary>DP 최대 정사각형 + 큰 것 우선 그리디</summary>
        public List<SquareMatch> FindSquares()
        {
            int[,] dp = new int[W, H];
            var cand = new List<SquareMatch>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    int c = tiles[x, y];
                    if (c == Empty) { dp[x, y] = 0; continue; }
                    if (x == 0 || y == 0) dp[x, y] = 1;
                    else if (tiles[x - 1, y] == c && tiles[x, y - 1] == c && tiles[x - 1, y - 1] == c)
                        dp[x, y] = Math.Min(dp[x - 1, y], Math.Min(dp[x, y - 1], dp[x - 1, y - 1])) + 1;
                    else dp[x, y] = 1;
                    if (dp[x, y] >= 2)
                    {
                        int s = dp[x, y];
                        cand.Add(new SquareMatch { X = x - s + 1, Y = y - s + 1, Size = s, Color = c });
                    }
                }
            cand.Sort((a, b) => b.Size.CompareTo(a.Size));
            bool[,] marked = new bool[W, H];
            var acc = new List<SquareMatch>();
            foreach (var m in cand)
            {
                bool has = false;
                for (int dx = 0; dx < m.Size && !has; dx++)
                    for (int dy = 0; dy < m.Size && !has; dy++)
                        if (!marked[m.X + dx, m.Y + dy]) has = true;
                if (!has) continue;
                for (int dx = 0; dx < m.Size; dx++)
                    for (int dy = 0; dy < m.Size; dy++)
                        marked[m.X + dx, m.Y + dy] = true;
                acc.Add(m);
            }
            return acc;
        }

        public void ApplyGravity()
        {
            for (int x = 0; x < W; x++)
            {
                int w = 0;
                for (int y = 0; y < H; y++)
                    if (tiles[x, y] != Empty)
                    {
                        tiles[x, w] = tiles[x, y];
                        items[x, w] = items[x, y];   // 아이템도 함께 낙하
                        if (w != y) { tiles[x, y] = Empty; items[x, y] = ItemType.None; }
                        w++;
                    }
                for (int y = w; y < H; y++) { tiles[x, y] = Empty; items[x, y] = ItemType.None; }
            }
        }

        public void Refill()
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (tiles[x, y] == Empty) tiles[x, y] = rng.Next(ColorCount);
        }

        void FillNoInitialMatch()
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    tiles[x, y] = rng.Next(ColorCount);
                    int guard = 0;
                    while (Makes2x2At(x, y) && guard++ < 16) tiles[x, y] = rng.Next(ColorCount);
                }
            // 정리 패스: 채우기 순서상 놓친 잔여 정사각형을 실제 매칭 판정으로 제거.
            // (칸 단위 Makes2x2At은 나중에 채워진 이웃이 완성하는 2x2를 놓칠 수 있음)
            int safety = 0;
            while (FindSquares().Count > 0 && safety++ < 64)
            {
                foreach (var m in FindSquares())
                    tiles[m.X, m.Y] = (tiles[m.X, m.Y] + 1) % ColorCount; // 한 꼭짓점만 바꿔 정사각형 깨기
            }
        }

        bool Makes2x2At(int x, int y)
        {
            int c = tiles[x, y];
            for (int ox = -1; ox <= 0; ox++)
                for (int oy = -1; oy <= 0; oy++)
                {
                    int bx = x + ox, by = y + oy;
                    if (bx < 0 || by < 0 || bx + 1 > W - 1 || by + 1 > H - 1) continue;
                    if (tiles[bx, by] == c && tiles[bx + 1, by] == c &&
                        tiles[bx, by + 1] == c && tiles[bx + 1, by + 1] == c) return true;
                }
            return false;
        }

        static int Key(int x, int y) { return x * 100 + y; }
    }

    /// <summary>난이도/모드 규칙 (표현 계층 참조용). 수치는 미검증 추정치.</summary>
    public static class Rules
    {
        public const int ColorCount = 3;            // v5: 3색 랜덤 팔레트
        public const int TimeAttackMs = 60000;      // 타임어택 60초
        public const int PieceTimeMaxMs = 8000;     // 첫 조각 8초
        public const int PieceTimeMinMs = 2500;     // 마지막 조각 2.5초

        public struct Difficulty { public int Moves; public int Goal; public string Label; }
        public static readonly Dictionary<string, Difficulty> Table = new Dictionary<string, Difficulty>
        {
            { "easy",   new Difficulty { Moves = 30, Goal = 15000, Label = "하" } },
            { "normal", new Difficulty { Moves = 25, Goal = 25000, Label = "중" } },
            { "hard",   new Difficulty { Moves = 20, Goal = 40000, Label = "상" } },
        };

        /// <summary>남은 수에 선형 비례하는 조각 제한 시간(ms). 첫 조각 MAX, 마지막 MIN.</summary>
        public static int PieceTimeMs(int movesLeft, int totalMoves)
        {
            if (totalMoves <= 1) return PieceTimeMinMs;
            double t = PieceTimeMinMs + (PieceTimeMaxMs - PieceTimeMinMs)
                       * (double)(movesLeft - 1) / (totalMoves - 1);
            if (t < PieceTimeMinMs) t = PieceTimeMinMs;
            if (t > PieceTimeMaxMs) t = PieceTimeMaxMs;
            return (int)t;
        }
    }
}
