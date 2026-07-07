// GameManager.cs — Unity 표현 계층 (v5 규칙)
// 셋업은 README 참고. ColorMatcherCore.cs가 같은 프로젝트에 있어야 함.
//
// 담당: 보드 렌더링(런타임 스프라이트), 드래그 입력, 고스트 프리뷰,
//       점수/타임어택 모드 분기, 가변 조각 타이머, 파괴/낙하 연출 훅.
// 미담당(README의 TODO): 아이템 타일 아이콘, 사운드, 랭킹/공유 UI, 화면 전환 — 별도 구현 필요.
//
// 이 파일은 실제 Unity 에디터에서 실행 검증되지 않았다. 컴파일(문법/타입)만 스텁으로 확인했다.
// 카메라 배치, HUD 위치, 애니메이션 타이밍 등은 에디터 실행 후 조정 대상.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ColorMatcher.Core;

public class GameManager : MonoBehaviour
{
    [Header("모드/난이도")]
    public string difficulty = "normal";   // easy | normal | hard
    public bool timeAttack = false;         // true면 타임어택 60초
    public int seed = 0;                    // 0 = 랜덤

    [Header("연출 시간(초)")]
    public float stampFlash = 0.16f;
    public float destroyFlash = 0.22f;

    // 3색 랜덤 팔레트: 색상환 균등분할 + 지터 (HSL→RGB)
    Color[] palette;

    Board board;
    Piece current;
    readonly Queue<Piece> queue = new Queue<Piece>();
    System.Random pieceRng;

    SpriteRenderer[,] tileViews;
    SpriteRenderer[] ghostPool;
    Sprite tileSprite;

    int score, movesLeft, totalMoves, goal;
    bool over, busy;
    float pieceDeadline;      // Time.time 기준
    float taDeadline;

    void Start()
    {
        tileSprite = MakeSquareSprite();
        NewGame();
        FitCamera();
    }

    void NewGame()
    {
        int s = seed == 0 ? System.Environment.TickCount : seed;
        board = new Board(Rules.ColorCount, s);
        pieceRng = new System.Random(s + 1);
        palette = GenPalette(Rules.ColorCount, new System.Random(s + 2));

        var diff = Rules.Table[difficulty];
        if (timeAttack) { movesLeft = 9999; goal = 0; totalMoves = 9999; }
        else { movesLeft = diff.Moves; goal = diff.Goal; totalMoves = diff.Moves; }

        score = 0; over = false; busy = false;

        queue.Clear();
        current = Piece.CreateRandom(pieceRng, Rules.ColorCount);
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));

        BuildTileViews();
        RefreshTiles();

        if (timeAttack) taDeadline = Time.time + Rules.TimeAttackMs / 1000f;
        else StartPieceTimer();
    }

    void BuildTileViews()
    {
        foreach (Transform c in transform) Destroy(c.gameObject);
        tileViews = new SpriteRenderer[Board.W, Board.H];
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                var go = new GameObject("t_" + x + "_" + y);
                go.transform.parent = transform;
                go.transform.position = new Vector3(x, y, 0);
                go.transform.localScale = Vector3.one * 0.92f;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tileSprite;
                tileViews[x, y] = sr;
            }
        ghostPool = new SpriteRenderer[8]; // 최대 조각 6칸 + 여유
        for (int i = 0; i < ghostPool.Length; i++)
        {
            var go = new GameObject("ghost_" + i);
            go.transform.parent = transform;
            go.transform.localScale = Vector3.one * 0.6f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = tileSprite; sr.sortingOrder = 5; sr.enabled = false;
            ghostPool[i] = sr;
        }
    }

    void FitCamera()
    {
        var cam = Camera.main;
        cam.orthographic = true;
        cam.transform.position = new Vector3((Board.W - 1) / 2f, (Board.H - 1) / 2f, -10);
        cam.orthographicSize = Board.H / 2f + 1.5f;
        cam.backgroundColor = new Color(0.09f, 0.09f, 0.12f);
    }

    void Update()
    {
        if (over) { HideGhosts(); return; }

        if (timeAttack)
        {
            if (!busy && Time.time >= taDeadline) { over = true; return; }
        }
        else
        {
            if (!busy && Time.time >= pieceDeadline) { StartCoroutine(ExpirePiece()); return; }
        }

        if (busy) { HideGhosts(); return; }
        if (Input.GetKeyDown(KeyCode.R)) current = current.Rotated();

        Vector3 w = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        int cx = Mathf.RoundToInt(w.x), cy = Mathf.RoundToInt(w.y);
        int bx = MaxCell(current, 0), by = MaxCell(current, 1);
        int ax = cx - bx / 2, ay = cy - by / 2;

        bool can = board.CanPlace(current, ax, ay);
        ShowGhosts(ax, ay, can);
        if (Input.GetMouseButtonDown(0) && can) StartCoroutine(DoStamp(ax, ay));
    }

    IEnumerator DoStamp(int ax, int ay)
    {
        busy = true; HideGhosts();
        var result = board.Stamp(current, ax, ay);
        if (!timeAttack) movesLeft--;

        // 파괴 연출 (코어는 이미 최종 상태. 여기선 시각 훅만)
        foreach (var p in result.Destroyed)
            tileViews[p.X, p.Y].color = Color.white;
        yield return new WaitForSeconds(destroyFlash);

        score += result.ScoreGained;
        RefreshTiles();

        current = queue.Dequeue();
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));

        // 승패 판정 (점수 모드만; 타임어택은 Update의 시간 종료가 관장)
        if (!timeAttack)
        {
            if (score >= goal) over = true;
            else if (movesLeft <= 0) over = true;
        }
        busy = false;
        if (!over && !timeAttack) StartPieceTimer();
    }

    IEnumerator ExpirePiece()
    {
        busy = true; HideGhosts();
        yield return new WaitForSeconds(0.2f);
        movesLeft--;
        current = queue.Dequeue();
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));
        if (movesLeft <= 0) over = true;
        busy = false;
        if (!over) StartPieceTimer();
    }

    void StartPieceTimer()
    {
        int ms = Rules.PieceTimeMs(movesLeft, totalMoves);
        pieceDeadline = Time.time + ms / 1000f;
    }

    void RefreshTiles()
    {
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                int c = board.GetTile(x, y);
                tileViews[x, y].color = c == Board.Empty ? new Color(0.06f, 0.06f, 0.08f) : palette[c];
                // TODO: board.GetItem(x,y) != None 이면 아이템 아이콘 오버레이 표시
            }
    }

    void ShowGhosts(int ax, int ay, bool can)
    {
        for (int i = 0; i < ghostPool.Length; i++)
        {
            if (i < current.Cells.Count)
            {
                int gx = ax + current.Cells[i].X, gy = ay + current.Cells[i].Y;
                ghostPool[i].enabled = true;
                ghostPool[i].transform.position = new Vector3(gx, gy, -1);
                ghostPool[i].color = can
                    ? new Color(palette[current.Color].r, palette[current.Color].g, palette[current.Color].b, 0.95f)
                    : new Color(1, 1, 1, 0.25f);
            }
            else ghostPool[i].enabled = false;
        }
    }

    void HideGhosts()
    {
        if (ghostPool == null) return;
        foreach (var g in ghostPool) if (g != null) g.enabled = false;
    }

    int MaxCell(Piece p, int axis)
    {
        int m = 0;
        foreach (var c in p.Cells) { int v = axis == 0 ? c.X : c.Y; if (v > m) m = v; }
        return m;
    }

    // 색상환 균등분할 + 지터로 구분되는 3색 생성
    Color[] GenPalette(int n, System.Random r)
    {
        var outp = new Color[n];
        double baseH = r.NextDouble() * 360;
        for (int i = 0; i < n; i++)
        {
            double h = (baseH + i * (360.0 / n) + (r.NextDouble() * 36 - 18)) % 360;
            double sat = 0.68 + r.NextDouble() * 0.17;
            double lit = 0.52 + r.NextDouble() * 0.10;
            outp[i] = HslToRgb(h / 360.0, sat, lit);
        }
        return outp;
    }

    static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        return new Color((float)r, (float)g, (float)b);
    }
    static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    Sprite MakeSquareSprite()
    {
        var tex = new Texture2D(8, 8);
        var px = new Color[64];
        for (int i = 0; i < 64; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8);
    }

    // 표현 계층에서 HUD 표시에 쓸 값 (Unity UI/TextMeshPro에 연결)
    public int Score { get { return score; } }
    public int Goal { get { return goal; } }
    public int MovesLeft { get { return movesLeft; } }
    public bool IsOver { get { return over; } }
    public float TimeLeftSec { get { return timeAttack ? Mathf.Max(0, taDeadline - Time.time) : 0; } }
}
