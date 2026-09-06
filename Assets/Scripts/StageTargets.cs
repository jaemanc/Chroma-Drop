// StageTargets.cs — 좌표 지정 클리어 스테이지의 목표 칸을 고른다.
//
// 무늬는 문자열 아트로 둔다 — 새 무늬를 넣는 일이 줄 몇 개 적는 일이 되게.
// '#' 이 목표 칸이고, 아트는 판 한가운데에 놓인다. 아트의 첫 줄이 화면 위쪽이다.
//
// 판정은 GameManager 가 한다. 여기서는 '어느 칸이냐' 만 정한다.

using ColorMatcher.Core;

public static class StageTargets
{
    // 무늬 하나가 판(14x14)의 절반쯤을 차지한다. 너무 크면 다 깨기 전에 수가 떨어진다.
    static readonly string[] Heart = {
        ".##..##.",
        "#..##..#",
        "#......#",
        ".#....#.",
        "..#..#..",
        "...##...",
    };

    static readonly string[] Paw = {
        ".##..##.",
        ".##..##.",
        "........",
        "..####..",
        ".######.",
        "..####..",
    };

    static readonly string[] Diamond = {
        "...##...",
        "..#..#..",
        ".#....#.",
        "#......#",
        ".#....#.",
        "..#..#..",
        "...##...",
    };

    /// <summary>이 스테이지의 목표 칸. pattern 이 비면 좌표 목표가 없다는 뜻으로 null 을 돌려준다.
    /// count 는 scatter/rows/cols 에서만 쓴다 — 무늬는 제 모양대로 놓인다.</summary>
    public static bool[,] Build(string pattern, int count, System.Random rng)
    {
        if (string.IsNullOrEmpty(pattern)) return null;

        var t = new bool[Board.W, Board.H];
        switch (pattern)
        {
            case "scatter": Scatter(t, count, rng); break;
            case "rows": Lines(t, count, true); break;
            case "cols": Lines(t, count, false); break;
            case "cross": Cross(t); break;
            case "heart": Art(t, Heart); break;
            case "paw": Art(t, Paw); break;
            case "diamond": Art(t, Diamond); break;
            default: return null;
        }
        return t;
    }

    /// <summary>목표 칸 수. 무늬마다 다르므로 세어서 알려준다.</summary>
    public static int Count(bool[,] t)
    {
        if (t == null) return 0;
        int n = 0;
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++) if (t[x, y]) n++;
        return n;
    }

    /// <summary>판 전체에 흩뿌린다. 같은 칸을 두 번 고르지 않는다.</summary>
    static void Scatter(bool[,] t, int count, System.Random rng)
    {
        int want = count > 0 ? count : 8;
        if (want > Board.W * Board.H) want = Board.W * Board.H;

        int placed = 0, guard = want * 50;
        while (placed < want && guard-- > 0)
        {
            int x = rng.Next(Board.W), y = rng.Next(Board.H);
            if (t[x, y]) continue;
            t[x, y] = true;
            placed++;
        }
    }

    /// <summary>행 또는 열을 count 개, 판에 고르게 벌려 놓는다.</summary>
    static void Lines(bool[,] t, int count, bool rows)
    {
        int n = count > 0 ? count : 1;
        int span = rows ? Board.H : Board.W;
        if (n > span) n = span;

        for (int i = 0; i < n; i++)
        {
            int at = (int)((i + 0.5f) * span / n);
            if (at >= span) at = span - 1;
            for (int j = 0; j < (rows ? Board.W : Board.H); j++)
            {
                if (rows) t[j, at] = true;
                else t[at, j] = true;
            }
        }
    }

    /// <summary>가운데 행 하나 + 가운데 열 하나.</summary>
    static void Cross(bool[,] t)
    {
        int cx = Board.W / 2, cy = Board.H / 2;
        for (int x = 0; x < Board.W; x++) t[x, cy] = true;
        for (int y = 0; y < Board.H; y++) t[cx, y] = true;
    }

    /// <summary>문자열 아트를 판 한가운데에 찍는다. 아트의 첫 줄이 위쪽이다.</summary>
    static void Art(bool[,] t, string[] art)
    {
        int h = art.Length, w = 0;
        foreach (var row in art) if (row.Length > w) w = row.Length;

        int ox = (Board.W - w) / 2;
        int oy = (Board.H - h) / 2;

        for (int r = 0; r < h; r++)
            for (int c = 0; c < art[r].Length; c++)
            {
                if (art[r][c] != '#') continue;
                int x = ox + c;
                int y = oy + (h - 1 - r);   // 아트 첫 줄이 위 → 보드 y 는 아래가 0
                if (x >= 0 && x < Board.W && y >= 0 && y < Board.H) t[x, y] = true;
            }
    }
}
