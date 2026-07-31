// GameManager.cs — 게임 흐름/입력 오케스트레이션 (v5 규칙).
// 씬에는 이 컴포넌트 하나(+카메라)만 있으면 됨 — BoardView/GameUI/Sfx는 런타임 생성.
//
// 입력: 터치(드래그로 고스트, 떼면 스탬프, 손가락 위로 띄운 프리뷰) + 마우스(호버 프리뷰, 클릭 스탬프).
// 회전: 화면 버튼 또는 R 키.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using ColorMatcher.Core;

public enum GamePhase { Home, Playing, Result }

public class GameManager : MonoBehaviour
{
    [Header("모드/난이도 (홈 화면 초기값)")]
    public string difficulty = "normal";   // easy | normal | hard
    public bool timeAttack = false;
    public int seed = 0;                   // 0 = 랜덤

    [Header("연출 시간(초)")]
    public float stampPop = 0.10f;
    public float destroyFlash = 0.20f;
    public float fallTime = 0.20f;
    public float ghostLiftCells = 2.5f;    // 터치 시 손가락 위로 띄우는 칸 수

    public GamePhase Phase { get; private set; }
    public bool Busy { get { return busy; } }
    public int Score { get { return score; } }
    public int Goal { get { return goal; } }
    public int MovesLeft { get { return movesLeft; } }
    public bool TimeAttackMode { get { return taRunning; } }
    public float TimeLeftSec { get { return taRunning ? Mathf.Max(0, taDeadline - Time.time) : 0; } }
    public float PieceTimerFrac { get { return pieceTimeTotal <= 0 ? 1 : Mathf.Clamp01((pieceDeadline - Time.time) / pieceTimeTotal); } }
    public Board BoardRef { get { return board; } }
    public Piece CurrentPiece { get { return current; } }

    Board board;
    Piece current;
    readonly Queue<Piece> queue = new Queue<Piece>();
    System.Random pieceRng;
    Color[] palette;

    BoardView view;
    GameUI ui;
    Sfx sfx;
    Camera cam;

    int score, movesLeft, totalMoves, goal;
    bool busy, taRunning, touchActive;
    float pieceDeadline, pieceTimeTotal, taDeadline;
    int lastW, lastH;
    Vector3 camBase;
    Coroutine shakeCo;

    void Awake()
    {
        Application.targetFrameRate = Application.isEditor ? -1 : 60;

        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.09f, 0.09f, 0.12f);

        view = new GameObject("BoardView").AddComponent<BoardView>();
        sfx = gameObject.AddComponent<Sfx>();
        ui = GameUI.Create(this);

        FitCamera();
        GoHome();
    }

    // ---------- 화면 전환 ----------

    public void GoHome()
    {
        StopAllCoroutines();
        shakeCo = null;
        if (cam != null) cam.transform.position = camBase;
        busy = false;
        Phase = GamePhase.Home;
        if (view != null) { view.HideGhost(); view.SetVisible(false); }
        ui.ShowHome();
    }

    /// <summary>홈 화면에서 선택된 difficulty/timeAttack/seed로 시작</summary>
    public void StartGame() { StartGame(difficulty, timeAttack, seed); }

    public void StartGame(string diff, bool ta, int seedOverride)
    {
        StopAllCoroutines();
        shakeCo = null;
        if (cam != null) cam.transform.position = camBase;
        difficulty = Rules.Table.ContainsKey(diff) ? diff : "normal";
        timeAttack = ta;

        int s = seedOverride == 0 ? System.Environment.TickCount : seedOverride;
        board = new Board(Rules.ColorCount, s);
        pieceRng = new System.Random(s + 1);
        palette = Palette.Generate(Rules.ColorCount, new System.Random(s + 2));

        var d = Rules.Table[difficulty];
        taRunning = timeAttack;
        if (timeAttack) { totalMoves = movesLeft = 9999; goal = 0; }
        else { totalMoves = movesLeft = d.Moves; goal = d.Goal; }
        score = 0;
        busy = false;
        touchActive = false;

        queue.Clear();
        current = Piece.CreateRandom(pieceRng, Rules.ColorCount);
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));

        view.Build();
        view.SetVisible(true);
        view.Refresh(board, palette);
        ui.ShowGame();
        ui.SetNext(new List<Piece>(queue), palette);

        Phase = GamePhase.Playing;
        if (timeAttack) taDeadline = Time.time + Rules.TimeAttackMs / 1000f;
        else StartPieceTimer();
    }

    void EndGame(bool win)
    {
        Phase = GamePhase.Result;
        busy = false;
        view.HideGhost();

        string key = BestKey(taRunning, difficulty);
        int best = PlayerPrefs.GetInt(key, 0);
        bool newBest = score > best;
        if (newBest) { PlayerPrefs.SetInt(key, score); PlayerPrefs.Save(); best = score; }

        if (win || taRunning) sfx.PlayWin(); else sfx.PlayLose();
        ui.ShowResult(win, taRunning, score, best, newBest);
    }

    public static string BestKey(bool ta, string diff) { return ta ? "best_ta" : "best_score_" + diff; }
    public int BestForSelection() { return PlayerPrefs.GetInt(BestKey(timeAttack, difficulty), 0); }

    // ---------- 루프 ----------

    void Update()
    {
        if (Screen.width != lastW || Screen.height != lastH) FitCamera();
        if (Phase != GamePhase.Playing) return;

        if (taRunning)
        {
            if (!busy && Time.time >= taDeadline) { EndGame(false); return; }
        }
        else
        {
            if (!busy && Time.time >= pieceDeadline) { StartCoroutine(ExpirePiece()); return; }
        }

        ui.UpdateHud(this);
        if (busy) { view.HideGhost(); return; }
        HandleInput();
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.R)) RotateCurrent();

        Vector2 sp;
        bool stamp = false;
        float lift = 0;

        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            sp = t.position;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId))
            { view.HideGhost(); touchActive = false; return; }
            if (t.phase == TouchPhase.Began) touchActive = true;
            if (!touchActive) { view.HideGhost(); return; }
            if (t.phase == TouchPhase.Ended) { stamp = true; touchActive = false; }
            else if (t.phase == TouchPhase.Canceled) { touchActive = false; view.HideGhost(); return; }
            lift = ghostLiftCells;
        }
        else
        {
            sp = Input.mousePosition;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            { view.HideGhost(); return; }
            stamp = Input.GetMouseButtonDown(0);
        }

        Vector3 w = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10));
        int cx = Mathf.RoundToInt(w.x);
        int cy = Mathf.RoundToInt(w.y + lift);
        int ax = cx - MaxCell(current, 0) / 2;
        int ay = cy - MaxCell(current, 1) / 2;

        bool can = board.CanPlace(current, ax, ay);
        view.ShowGhost(current, ax, ay, can, palette[current.Color]);
        if (stamp && can) StartCoroutine(DoStamp(ax, ay));
    }

    public void RotateCurrent()
    {
        if (Phase != GamePhase.Playing || busy) return;
        current = current.Rotated();
    }

    /// <summary>프로그램/테스트용 스탬프 진입점. 성공 시 코루틴 시작.</summary>
    public bool TryStamp(int ax, int ay)
    {
        if (Phase != GamePhase.Playing || busy || board == null) return false;
        if (!board.CanPlace(current, ax, ay)) return false;
        StartCoroutine(DoStamp(ax, ay));
        return true;
    }

    IEnumerator DoStamp(int ax, int ay)
    {
        busy = true;
        view.HideGhost();

        // 연출 diff용: 스탬프 직후(파괴 전) 시각 상태 스냅샷
        var visual = new int[Board.W, Board.H];
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                visual[x, y] = board.GetTile(x, y);
        var stamped = new List<Point>();
        foreach (var c in current.Cells)
        {
            var p = new Point(ax + c.X, ay + c.Y);
            stamped.Add(p);
            visual[p.X, p.Y] = current.Color;
        }

        var result = board.Stamp(current, ax, ay);
        if (!taRunning) movesLeft--;

        // 1) 스탬프
        sfx.PlayStamp();
        view.PaintCells(stamped, palette[current.Color]);
        yield return new WaitForSeconds(stampPop);

        // 2) 파괴 플래시
        if (result.Destroyed.Count > 0)
        {
            sfx.PlayDestroy(result.MaxChain);
            view.FlashCells(result.Destroyed);

            var burstColors = new List<Color>(result.Destroyed.Count);
            foreach (var bp in result.Destroyed)
            {
                int ci = visual[bp.X, bp.Y];
                burstColors.Add(ci >= 0 && ci < palette.Length ? palette[ci] : Color.white);
            }
            float energy = 1f + 0.35f * Mathf.Clamp(result.MaxChain - 1, 0, 6) + (result.BigHit ? 0.6f : 0f);
            view.Burst(result.Destroyed, burstColors, energy);

            float mag = Mathf.Min(0.7f, 0.12f + 0.05f * (result.MaxChain - 1) + 0.01f * Mathf.Min(result.Destroyed.Count, 30));
            Shake(mag, 0.18f);
            if (result.MaxChain >= 2 || result.ScoreGained >= 500)
                ui.ShowChainPopup(result.MaxChain, result.ScoreGained);
            yield return new WaitForSeconds(destroyFlash);
        }
        if (result.Spawns.Count > 0) sfx.PlayItem();

        score += result.ScoreGained;

        // 3) 최종 상태 반영 + 바뀐 칸 낙하 연출
        var changed = new bool[Board.W, Board.H];
        bool any = false;
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                if (board.GetTile(x, y) != visual[x, y]) { changed[x, y] = true; any = true; }
        foreach (var p in result.Destroyed) { changed[p.X, p.Y] = true; any = true; }
        view.Refresh(board, palette);
        if (any) yield return view.FallIn(changed, fallTime);

        // 다음 조각
        current = queue.Dequeue();
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));
        ui.SetNext(new List<Piece>(queue), palette);

        busy = false;
        if (!taRunning)
        {
            if (score >= goal) { EndGame(true); yield break; }
            if (movesLeft <= 0) { EndGame(false); yield break; }
            StartPieceTimer();
        }
    }

    IEnumerator ExpirePiece()
    {
        busy = true;
        view.HideGhost();
        sfx.PlayExpire();
        yield return new WaitForSeconds(0.15f);
        movesLeft--;
        current = queue.Dequeue();
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));
        ui.SetNext(new List<Piece>(queue), palette);
        busy = false;
        if (movesLeft <= 0) EndGame(false);
        else StartPieceTimer();
    }

    void StartPieceTimer()
    {
        pieceTimeTotal = Rules.PieceTimeMs(movesLeft, totalMoves) / 1000f;
        pieceDeadline = Time.time + pieceTimeTotal;
    }

    // 세로(모바일)/가로(에디터) 모두 보드 전체 + 상단 HUD 공간이 나오게 카메라 맞춤
    void FitCamera()
    {
        lastW = Screen.width; lastH = Screen.height;
        float aspect = (float)Mathf.Max(1, Screen.width) / Mathf.Max(1, Screen.height);
        float half = Mathf.Max(Board.H / 2f + 2.5f, (Board.W / 2f + 1.0f) / aspect);
        cam.orthographicSize = half;
        camBase = new Vector3((Board.W - 1) / 2f, (Board.H - 1) / 2f + half * 0.10f, -10);
        if (shakeCo == null) cam.transform.position = camBase;
    }

    /// <summary>카메라 흔들림 (타격감). 파괴 규모/연쇄에 비례해 호출.</summary>
    public void Shake(float magnitude, float duration)
    {
        if (!isActiveAndEnabled || cam == null) return;
        if (shakeCo != null) StopCoroutine(shakeCo);
        shakeCo = StartCoroutine(ShakeCo(magnitude, duration));
    }

    IEnumerator ShakeCo(float mag, float dur)
    {
        float t = 0;
        while (t < dur)
        {
            t += Time.deltaTime;
            float damper = 1f - Mathf.Clamp01(t / dur);
            Vector2 r = UnityEngine.Random.insideUnitCircle * (mag * damper);
            cam.transform.position = camBase + new Vector3(r.x, r.y, 0);
            yield return null;
        }
        cam.transform.position = camBase;
        shakeCo = null;
    }

    static int MaxCell(Piece p, int axis)
    {
        int m = 0;
        foreach (var c in p.Cells) { int v = axis == 0 ? c.X : c.Y; if (v > m) m = v; }
        return m;
    }
}
