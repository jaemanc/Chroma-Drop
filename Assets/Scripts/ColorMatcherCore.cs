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

    public enum ItemType { None, Row, Col, Diag, Bomb5 }

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
        public int ObstaclesCracked;                        // 이번 스탬프에 금이 간 콘크리트 수
    }

    /// <summary>Destroyed 안의 한 연쇄 단계 구간. 표현 계층이 순차 연출에 사용.</summary>
    public struct Wave
    {
        public int Start;      // Destroyed[Start .. MatchEnd) = 매칭으로 파괴된 칸
        public int MatchEnd;   // Destroyed[MatchEnd .. End)   = 아이템 발동으로 연쇄 파괴된 칸
        public int End;

        // 이 단계의 중력·리필·아이템 스폰까지 끝난 뒤의 보드 (열 우선: x * H + y).
        // 다음 단계의 매칭은 여기 내려온 블록들이 만든 것이므로,
        // 뷰가 다음 폭발을 재생하기 전에 이 상태로 낙하를 보여줘야 인과가 보인다.
        public int[] TilesAfter;
        public ItemType[] ItemsAfter;
        public int[] ObstacleHpAfter;
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
        public const int W = 14, H = 14, Empty = -1;

        // 특수 칸. 음수라 색 인덱스(0..ColorCount-1)와 겹치지 않는다.
        public const int Obstacle = -2;      // 벽돌: 매칭에 안 끼고, 옆 칸이 터질 때만 금이 간다
        public const int Steel = -3;         // 강철: 무슨 수를 써도 안 부서진다. 중력은 받는다
        public const int MinMatch = 2;    // 최소 매칭 정사각형 한 변
        public const int BaseTileScore = 10;
        public const double ChainBonus = 0.5;

        public readonly int ColorCount;
        readonly int[,] tiles;
        readonly int[,] obstacleHp;
        readonly ItemType[,] items;
        readonly Random rng;

        public Board(int colorCount, int seed)
        {
            ColorCount = colorCount;
            rng = new Random(seed);
            tiles = new int[W, H];
            obstacleHp = new int[W, H];
            items = new ItemType[W, H];
            FillNoInitialMatch();
        }

        public int GetTile(int x, int y) { return tiles[x, y]; }
        public void SetTile(int x, int y, int c) { tiles[x, y] = c; }
        public ItemType GetItem(int x, int y) { return items[x, y]; }
        public void SetItem(int x, int y, ItemType t) { items[x, y] = t; }
        public bool InBounds(int x, int y) { return x >= 0 && x < W && y >= 0 && y < H; }

        public bool IsObstacle(int x, int y) { return tiles[x, y] == Obstacle; }
        public bool IsSteel(int x, int y) { return tiles[x, y] == Steel; }

        /// <summary>블록이 통과하지 못하는 칸. 매칭에도 안 낀다.</summary>
        static bool IsBlocker(int t) { return t == Obstacle || t == Steel; }

        /// <summary>판에 있는 강철 수.</summary>
        public int CountSteel()
        {
            int n = 0;
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) if (tiles[x, y] == Steel) n++;
            return n;
        }

        /// <summary>빈칸이 아닌 일반 칸을 강철로 바꾼다. 실제로 놓은 개수를 돌려준다.</summary>
        public int SpawnSteel(int count)
        {
            int placed = 0;
            for (int t = 0; t < count * 40 && placed < count; t++)
            {
                int x = rng.Next(W), y = rng.Next(H);
                if (!IsColor(tiles[x, y])) continue;
                tiles[x, y] = Steel;
                items[x, y] = ItemType.None;
                obstacleHp[x, y] = 0;
                placed++;
            }
            return placed;
        }

        /// <summary>판에 있는 벽돌 수.</summary>
        public int CountObstacles()
        {
            int n = 0;
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) if (tiles[x, y] == Obstacle) n++;
            return n;
        }
        /// <summary>남은 내구도. 콘크리트가 아니면 0.</summary>
        public int GetObstacleHp(int x, int y) { return tiles[x, y] == Obstacle ? obstacleHp[x, y] : 0; }
        public void SetObstacle(int x, int y, int hp)
        {
            tiles[x, y] = Obstacle;
            obstacleHp[x, y] = hp;
            items[x, y] = ItemType.None;   // 콘크리트 자리에는 아이템이 남지 않는다
        }

        /// <summary>일반 색 칸인가 (콘크리트/빈칸 제외).</summary>
        static bool IsColor(int t) { return t >= 0; }

        public bool CanPlace(Piece p, int px, int py)
        {
            foreach (var c in p.Cells)
            {
                int x = px + c.X, y = py + c.Y;
                if (!InBounds(x, y)) return false;
                if (IsBlocker(tiles[x, y])) return false;   // 벽돌·강철은 덮어쓸 수 없다
            }
            return true;
        }

        /// <summary>페인트 스탬프: 덮인 칸을 조각 색으로 덮어쓰기 → 연쇄 해소</summary>
        /// <summary>여기에 찍으면 이번 한 웨이브에 어느 칸이 사라지는지 미리 계산한다.
        /// 보드는 바꾸지 않는다. 후속 연쇄는 리필이 무작위라 예고하지 않는다.</summary>
        public List<Point> PreviewStamp(Piece p, int px, int py)
        {
            var res = new List<Point>();
            if (!CanPlace(p, px, py)) return res;

            // 실제로 찍어보고 계산한 뒤 되돌린다 — 판정이 Resolve 와 어긋날 수 없다
            var saved = new int[p.Cells.Count];
            for (int i = 0; i < p.Cells.Count; i++)
            {
                var c = p.Cells[i];
                saved[i] = tiles[px + c.X, py + c.Y];
                tiles[px + c.X, py + c.Y] = p.Color;
            }

            var seen = new HashSet<int>();
            var actQueue = new Queue<Point>();
            foreach (var m in FindSquares())
                for (int dx = 0; dx < m.Size; dx++)
                    for (int dy = 0; dy < m.Size; dy++)
                    {
                        int x = m.X + dx, y = m.Y + dy;
                        if (seen.Add(Key(x, y))) res.Add(new Point(x, y));
                        if (items[x, y] != ItemType.None) actQueue.Enqueue(new Point(x, y));
                    }

            // 아이템 발동까지 보여준다 — 실제 파괴와 같은 BFS
            while (actQueue.Count > 0)
            {
                var a = actQueue.Dequeue();
                ItemType t = items[a.X, a.Y];
                if (t == ItemType.None) continue;
                foreach (var e in EffectCells(t, a.X, a.Y))
                {
                    if (tiles[e.X, e.Y] == Steel) continue;
                    if (!seen.Add(Key(e.X, e.Y))) continue;
                    res.Add(e);
                    if (items[e.X, e.Y] != ItemType.None) actQueue.Enqueue(e);
                }
            }

            for (int i = 0; i < p.Cells.Count; i++)
            {
                var c = p.Cells[i];
                tiles[px + c.X, py + c.Y] = saved[i];
            }
            return res;
        }

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
            ResolveLoop(res, 0);
            return res;
        }

        /// <summary>(x,y) 의 아이템을 매칭 없이 즉시 발동시키고 이어지는 연쇄까지 해소한다.
        /// 손으로 던지는 폭탄처럼 스스로 터져야 하는 경우에 쓴다.</summary>
        public ResolveResult Detonate(int x, int y)
        {
            var res = new ResolveResult();
            if (!InBounds(x, y) || items[x, y] == ItemType.None) return res;

            var toDestroy = new Dictionary<int, Point>();
            var order = new List<Point>();
            var actQueue = new Queue<Point>();

            // 발동 지점 자체도 함께 사라진다
            var seed = new Point(x, y);
            toDestroy[Key(x, y)] = seed;
            order.Add(seed);
            actQueue.Enqueue(seed);

            // matchEnd = 1 — 던진 칸은 '직접', 터진 범위는 '연계' 로 연출된다
            ProcessWave(res, toDestroy, order, actQueue, 1, 1, 1.0, 0, false);
            res.MaxChain = 1;
            ResolveLoop(res, 1);   // 무너진 뒤 새로 생긴 매칭부터 이어서
            return res;
        }

        /// <summary>매칭이 없을 때까지 연쇄를 돈다. startChain 은 이미 지나온 단계 수.</summary>
        void ResolveLoop(ResolveResult res, int startChain)
        {
            int chain = startChain;
            while (true)
            {
                var matches = FindSquares();
                if (matches.Count == 0) break;
                chain++;
                double mult = 1.0 + ChainBonus * (chain - 1);

                int matchTiles = 0;
                foreach (var m in matches) matchTiles += m.Size * m.Size;

                var toDestroy = new Dictionary<int, Point>();
                var order = new List<Point>();   // 매칭 칸 먼저, 아이템 발동 칸 나중
                var actQueue = new Queue<Point>();

                foreach (var m in matches)
                {
                    int sizeMult = 1 << (m.Size - 2); // 2x2=x1,3x3=x2,4x4=x4
                    res.ScoreGained += (int)(m.Size * m.Size * BaseTileScore * sizeMult * mult);
                    res.Matches.Add(m);
                    if (m.Size >= 3) res.BigHit = true;
                    for (int dx = 0; dx < m.Size; dx++)
                        for (int dy = 0; dy < m.Size; dy++)
                        {
                            int x = m.X + dx, y = m.Y + dy;
                            int k = Key(x, y);
                            if (!toDestroy.ContainsKey(k)) { toDestroy[k] = new Point(x, y); order.Add(new Point(x, y)); }
                            if (items[x, y] != ItemType.None) actQueue.Enqueue(new Point(x, y));
                        }
                }

                ProcessWave(res, toDestroy, order, actQueue, order.Count, chain, mult, matchTiles, true);
            }
            if (chain > res.MaxChain) res.MaxChain = chain;
        }

        /// <summary>한 연쇄 단계를 끝까지 처리한다 — 아이템 발동 BFS, 콘크리트 손상, 실제 파괴,
        /// 중력·리필, 스폰, 웨이브 기록. 매칭 경로와 강제 발동 경로가 같은 규칙을 쓰도록 공유한다.
        /// toDestroy/order 에는 시작 칸이 이미 들어 있어야 한다.</summary>
        void ProcessWave(ResolveResult res, Dictionary<int, Point> toDestroy, List<Point> order,
                         Queue<Point> actQueue, int matchEnd, int chain, double mult,
                         int matchTiles, bool allowSpawn)
        {
            // 아이템 발동 BFS (발동으로 파괴된 칸의 아이템도 연쇄 발동)
            int actCount = 0;
            while (actQueue.Count > 0)
            {
                var a = actQueue.Dequeue();
                ItemType t = items[a.X, a.Y];
                if (t == ItemType.None) continue;
                foreach (var e in EffectCells(t, a.X, a.Y))
                {
                    if (tiles[e.X, e.Y] == Steel) continue;   // 강철은 폭발도 못 뚫는다
                    int k = Key(e.X, e.Y);
                    if (toDestroy.ContainsKey(k)) continue;
                    toDestroy[k] = e;
                    order.Add(e);
                    actCount++;
                    if (items[e.X, e.Y] != ItemType.None) actQueue.Enqueue(e);
                }
            }

            // 콘크리트: 인접 칸이 터지면 금이 간다. 웨이브당 1 만 깎는다.
            var cracked = new HashSet<int>();
            var crackSeeds = new List<Point>(order);   // 순회 중 order 에 추가되므로 복사본으로 돈다
            foreach (var pt in crackSeeds)
                foreach (var e in Neighbors(pt))
                {
                    if (tiles[e.X, e.Y] != Obstacle) continue;
                    int k = Key(e.X, e.Y);
                    if (!cracked.Add(k)) continue;
                    if (--obstacleHp[e.X, e.Y] > 0) continue;
                    toDestroy[k] = e;
                    order.Add(e);
                    actCount++;
                }
            res.ObstaclesCracked += cracked.Count;

            if (actCount > 0)
                res.ScoreGained += (int)(actCount * BaseTileScore * mult);

            // 실제 파괴
            res.TilesDestroyed += order.Count;
            int waveStart = res.Destroyed.Count;
            foreach (var pt in order)
            {
                tiles[pt.X, pt.Y] = Empty;
                items[pt.X, pt.Y] = ItemType.None;
                obstacleHp[pt.X, pt.Y] = 0;
                res.Destroyed.Add(pt);
            }
            ApplyGravity();
            Refill();

            // 스폰: 즉시 파괴 방지 위해 실제 배치는 여기서.
            if (allowSpawn) MaybeSpawn(chain, matchTiles, res);

            res.Waves.Add(new Wave
            {
                Start = waveStart,
                MatchEnd = waveStart + matchEnd,
                End = res.Destroyed.Count,
                TilesAfter = SnapshotTiles(),
                ItemsAfter = SnapshotItems(),
                ObstacleHpAfter = SnapshotBrickHp(),
            });
        }

        int[] SnapshotTiles()
        {
            var a = new int[W * H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) a[x * H + y] = tiles[x, y];
            return a;
        }

        int[] SnapshotBrickHp()
        {
            var a = new int[W * H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) a[x * H + y] = obstacleHp[x, y];
            return a;
        }

        ItemType[] SnapshotItems()
        {
            var a = new ItemType[W * H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) a[x * H + y] = items[x, y];
            return a;
        }

        // v5 스폰 규칙:
        //  - 한 단계 매칭 6타일 이상: row/col/diag 중 랜덤 1개
        //  - 연쇄 2 도달: row
        //  - 연쇄 3 도달: bomb5
        //  - 연쇄 5 이상 또는 4x4 이상 매칭: colorclear (랜덤 색으로 재칠)
        void MaybeSpawn(int chain, int matchTiles, ResolveResult res)
        {
            if (matchTiles >= 6)
            {
                var pool = new[] { ItemType.Row, ItemType.Col, ItemType.Diag };
                SpawnItem(pool[rng.Next(pool.Length)], false, res);
            }
            // '도달'이므로 == 다. >= 로 두면 연쇄가 길어질수록 매 단계마다 또 나온다.
            if (chain == 2) SpawnItem(ItemType.Row, false, res);
            if (chain == 3) SpawnItem(ItemType.Bomb5, false, res);
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
            }
            return cells;
        }

        /// <summary>DP 최대 정사각형 + 큰 것 우선 그리디</summary>
        /// <summary>겹치지 않는 최대 정사각형 매칭. 한 변 MinMatch 이상만 인정한다.</summary>
        public List<SquareMatch> FindSquares()
        {
            int[,] dp = new int[W, H];
            var cand = new List<SquareMatch>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    int c = tiles[x, y];
                    if (!IsColor(c)) { dp[x, y] = 0; continue; }   // 빈칸/콘크리트는 매칭에 안 낀다
                    if (x == 0 || y == 0) dp[x, y] = 1;
                    else if (tiles[x - 1, y] == c && tiles[x, y - 1] == c && tiles[x - 1, y - 1] == c)
                        dp[x, y] = Math.Min(dp[x - 1, y], Math.Min(dp[x, y - 1], dp[x - 1, y - 1])) + 1;
                    else dp[x, y] = 1;

                    if (dp[x, y] >= MinMatch)
                    {
                        int sz = dp[x, y];
                        cand.Add(new SquareMatch { X = x - sz + 1, Y = y - sz + 1, Size = sz, Color = c });
                    }
                }

            // 큰 것부터 자리를 차지하고, 이미 다 덮인 후보는 버린다.
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

        static readonly int[] NX = { 1, -1, 0, 0 };
        static readonly int[] NY = { 0, 0, 1, -1 };

        /// <summary>상하좌우 이웃 칸 (판 안쪽만).</summary>
        IEnumerable<Point> Neighbors(Point p)
        {
            for (int i = 0; i < 4; i++)
            {
                int x = p.X + NX[i], y = p.Y + NY[i];
                if (InBounds(x, y)) yield return new Point(x, y);
            }
        }


        /// <summary>중력. 위에서 아래 한 방향 고정이다.
        /// 벽돌도 함께 떨어진다 — 판에 떠 있는 것은 없다.
        /// 한 열의 빈칸을 아래에서부터 메우되 블록의 위아래 순서는 그대로 지킨다.</summary>
        public void ApplyGravity()
        {
            for (int x = 0; x < W; x++)
            {
                int w = 0;
                for (int y = 0; y < H; y++)
                {
                    if (tiles[x, y] == Empty) continue;

                    if (w != y)
                    {
                        tiles[x, w] = tiles[x, y];
                        items[x, w] = items[x, y];             // 아이템도 함께 낙하
                        obstacleHp[x, w] = obstacleHp[x, y];   // 벽돌은 남은 내구도를 들고 내려간다
                        tiles[x, y] = Empty;
                        items[x, y] = ItemType.None;
                        obstacleHp[x, y] = 0;
                    }
                    w++;
                }
                for (int k = w; k < H; k++)
                {
                    tiles[x, k] = Empty;
                    items[x, k] = ItemType.None;
                    obstacleHp[x, k] = 0;
                }
            }
        }

        public void Refill()
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (tiles[x, y] == Empty) tiles[x, y] = rng.Next(ColorCount);
        }

        /// <summary>빈칸이 아닌 일반 칸을 콘크리트로 바꾼다. 실제로 놓은 개수를 돌려준다.</summary>
        /// <summary>아무 칸도 안 터진 수에 대한 벌칙으로 벽돌 하나를 놓는다.
        /// 그 자리에 아이템이 있었으면 아이템은 사라지고 벽돌이 대신 들어선다.
        /// 놓을 자리를 못 찾으면 -1.</summary>
        public int PenaltyObstacle(out Point where) { return PenaltyObstacle(Rules.ObstacleHp, out where); }

        public int PenaltyObstacle(int hp, out Point where)
        {
            where = new Point(0, 0);
            var open = new List<Point>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (IsColor(tiles[x, y])) open.Add(new Point(x, y));
            if (open.Count == 0) return -1;

            var p = open[rng.Next(open.Count)];
            bool hadItem = items[p.X, p.Y] != ItemType.None;
            SetObstacle(p.X, p.Y, hp);   // SetObstacle 이 아이템을 지운다
            where = p;
            return hadItem ? 1 : 0;
        }

        public int SpawnObstacles(int count) { return SpawnObstacles(count, Rules.ObstacleHp); }

        public int SpawnObstacles(int count, int hp)
        {
            int placed = 0;
            for (int t = 0; t < count * 40 && placed < count; t++)
            {
                int x = rng.Next(W), y = rng.Next(H);
                if (!IsColor(tiles[x, y])) continue;   // 빈칸/벽돌 자리는 건너뛴다
                SetObstacle(x, y, hp);
                placed++;
            }
            return placed;
        }

        void FillNoInitialMatch()
        {
            // 먼저 전부 Empty 로 둔다. int[,] 기본값이 0 이라 그냥 채우면
            // 아직 안 채운 칸이 '색0' 으로 읽혀 Makes2x2At 이 가짜 2x2 를 잡는다.
            // 그러면 색0 만 계속 재추첨당해 분포가 크게 치우친다 (196칸 중 7칸 수준).
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) tiles[x, y] = Empty;

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
        public const int ColorCount = 4;            // 랜덤 팔레트 색 수
        public const int TimeAttackMs = 180000;     // 타임어택 3분
        // 두 값이 같으면 조각 제한시간이 3초로 고정된다.
        // 뒤로 갈수록 조여들게 하려면 Max 를 올리면 된다 (남은 수에 선형 비례).
        public const int PieceTimeMaxMs = 4000;     // 첫 조각
        public const int PieceTimeMinMs = 4000;     // 마지막 조각

        /// <summary>남은 수에 선형 비례하는 조각 제한 시간(ms). 첫 조각 MAX, 마지막 MIN.
        /// 시간이 다 되면 그 조각은 버려지고 기회도 한 번 소모된다.</summary>
        public static int PieceTimeMs(int movesLeft, int totalMoves)
        {
            if (totalMoves <= 1) return PieceTimeMinMs;
            double t = PieceTimeMinMs + (PieceTimeMaxMs - PieceTimeMinMs)
                       * (double)(movesLeft - 1) / (totalMoves - 1);
            if (t < PieceTimeMinMs) t = PieceTimeMinMs;
            if (t > PieceTimeMaxMs) t = PieceTimeMaxMs;
            return (int)t;
        }

        // 1000점당 10코인
        public const int ScorePerCoin = 100;

        /// <summary>점수를 코인으로 환산한다. 버림.</summary>
        public static int CoinsFor(int score) { return score <= 0 ? 0 : score / ScorePerCoin; }

        public const int ObstacleHp = 2;               // 콘크리트는 2번 금이 가야 부서진다

        /// <summary>이번 수가 끝난 뒤 새로 놓을 콘크리트 수. 진행할수록 늘어난다.</summary>
        public static int ObstaclesAfterMove(int movesUsed, int totalMoves)
        {
            if (movesUsed < 3) return 0;                       // 첫 세 수는 판을 익히게 둔다
            if (movesUsed % 2 != 0) return 0;                  // 한 수 걸러 한 번
            if (totalMoves <= 0) return 1;
            double t = movesUsed / (double)totalMoves;         // 0 → 1
            return 1 + (int)(t * 2);                           // 1개에서 3개까지
        }

        // 목표 점수는 없앴다 — 주어진 기회 안에서 최대한 많이 얻는 방식이다.
        public struct Difficulty { public int Moves; public string Label; }
        public static readonly Dictionary<string, Difficulty> Table = new Dictionary<string, Difficulty>
        {
            { "hard",   new Difficulty { Moves = 20, Label = "상" } },
        };

    }
}
