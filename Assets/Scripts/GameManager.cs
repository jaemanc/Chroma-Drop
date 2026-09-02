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
    [Header("모드 (홈 화면 초기값)")]
    // 난이도 선택은 없앴다. 규칙표는 그대로 두고 '상' 하나만 쓴다.
    public const string Difficulty = "hard";
    public string difficulty { get { return Difficulty; } }
    public bool timeAttack = false;
    public int seed = 0;                   // 0 = 랜덤

    [Header("연출 시간(초)")]
    public float stampTime = 0.34f;        // 들어올림 → 내려찍기 → 버팀 → 복원 전체
    public float destroyFlash = 0.22f;
    public float chainStep = 0.19f;        // 연쇄 한 단계가 터지는 간격
    public float chainFall = 0.19f;        // 연쇄 단계 사이 낙하 시간 (마지막 낙하는 fallTime)
    public float landTime = 0.15f;         // 착지 스쿼시·복원
    public float fallTime = 0.24f;
    public float ghostLiftCells = 2.5f;    // 터치 시 손가락 위로 띄우는 칸 수

    public GamePhase Phase { get; private set; }
    public bool Busy { get { return busy; } }
    public int Score { get { return score; } }
    public int MovesLeft { get { return movesLeft; } }
    public bool TimeAttackMode { get { return taRunning; } }
    public float TimeLeftSec { get { return taRunning ? Mathf.Max(0, taDeadline - Time.time) : 0; } }
    /// <summary>조각 제한시간 남은 비율 (1 → 0). 타임어택에서는 쓰지 않는다.</summary>
    public float PieceTimerFrac { get { return pieceTimeTotal <= 0 ? 1 : Mathf.Clamp01((pieceDeadline - Time.time) / pieceTimeTotal); } }
    public Board BoardRef { get { return board; } }
    public Piece CurrentPiece { get { return current; } }

    // 스탬프로 바로 터진 칸 / 연계(아이템 발동·후속 연쇄)로 터진 칸을 색으로 구분한다.
    static readonly Color DirectFlash = new Color(1f, 1f, 1f);
    static readonly Color ChainFlash = new Color(1f, 0.72f, 0.18f);

    Board board;
    Piece current;
    readonly Queue<Piece> queue = new Queue<Piece>();
    System.Random pieceRng;
    Color[] palette;

    BoardView view;
    GameUI ui;
    Sfx sfx;
    Camera cam;
    SpriteRenderer bg;                      // 카메라를 덮는 배경 이미지

    int score, movesLeft, totalMoves;
    int stageLevel;                  // 몇 번째 판인가
    StageConfig stage;               // 이 판의 설정. 난이도의 유일한 출처다
    ObjectiveTracker objectives;     // 목표 진행도
    bool stageCleared;
    bool busy, taRunning, touchActive;
    float pieceDeadline, pieceTimeTotal, taDeadline;
    int lastW, lastH, curSeed;
    int pendingScore, pendingSeed, earnedCoins;
    bool pendingTa, pendingBomb;

    /// <summary>폭탄 조각이 장전돼 있나.</summary>
    public bool BombArmed { get { return pendingBomb; } }

    // ---------- 스테이지 ----------
    public int StageLevel { get { return stageLevel; } }
    public StageConfig Stage { get { return stage; } }
    public bool StageCleared { get { return stageCleared; } }

    /// <summary>목표별 진행도. UI 가 그대로 그린다.</summary>
    public List<ObjectiveProgress> Objectives
    {
        get
        {
            return objectives != null ? objectives.Snapshot() : new List<ObjectiveProgress>();
        }
    }

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
        cam.backgroundColor = Palette.Hex(0xCDE3EE);   // 배경 스프라이트 밖 여백 (그림 하늘색)

        Leaderboard.Create();
        BuildBackground();
        view = new GameObject("BoardView").AddComponent<BoardView>();
        sfx = gameObject.AddComponent<Sfx>();
        ui = GameUI.Create(this);

        FitCamera();
        GoHome();
    }


    /// <summary>플레이 화면 배경. 보드 프레임(-2)보다 뒤에 오도록 -20 에 둔다.</summary>
    void BuildBackground()
    {
        var tex = Resources.Load<Texture2D>("jungle_bg");
        if (tex == null) return;

        var go = new GameObject("Background");
        go.transform.SetParent(transform, false);
        bg = go.AddComponent<SpriteRenderer>();
        bg.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        bg.color = Color.white;      // 채도·명도는 파일에 구워져 있다 (Tools/tune-image.py)
        bg.sortingOrder = -20;
    }

    void FitBackground()
    {
        if (bg == null || cam == null) return;
        float h = cam.orthographicSize * 2f;
        float w = h * cam.aspect;
        var sz = bg.sprite.bounds.size;
        float k = Mathf.Max(w / sz.x, h / sz.y);      // contain 이 아니라 cover
        bg.transform.localScale = Vector3.one * k;
        bg.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 10);
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
    public void StartGame() { StartGame(Difficulty, timeAttack, seed); }

    /// <summary>스테이지 한 판을 시작한다.</summary>
    public void StartStage(int level) { StartStage(level, seed); }

    public void StartStage(int level, int seedOverride)
    {
        timeAttack = false;
        stageLevel = Mathf.Clamp(level, 1, Mathf.Max(1, StageLoader.Count));
        StartGame(Difficulty, false, seedOverride);
    }

    public void StartGame(string diff, bool ta, int seedOverride)
    {
        StopAllCoroutines();
        shakeCo = null;
        if (cam != null) cam.transform.position = camBase;
        timeAttack = ta;

        int s = seedOverride == 0 ? System.Environment.TickCount : seedOverride;
        curSeed = s;

        // 판을 만들기 전에 어떤 스테이지인지부터 정해야 지형을 넘길 수 있다
        taRunning = timeAttack;
        if (!taRunning && stageLevel < 1) stageLevel = Progress.Selected;
        stage = taRunning ? null : StageLoader.Get(stageLevel);
        if (!taRunning && stage == null)
        {
            Debug.LogError("[stage] " + stageLevel + "번 설정을 못 읽었다 — 기본 판으로 시작한다. "
                           + string.Join(" / ", StageLoader.Report.Errors.ToArray()));
        }

        // 판의 모든 조건은 설정에서 온다. 타임어택은 스테이지가 없으므로 기본값.
        var setup = stage != null ? stage.ToBoardSetup(Rules.ColorCount)
                                  : BoardSetup.Default(Rules.ColorCount);
        board = new Board(setup, s);
        LogStageTopology();
        pieceRng = new System.Random(s + 1);
        palette = Palette.Generate(Rules.ColorCount, new System.Random(s + 2));

        objectives = stage != null
                   ? new ObjectiveTracker(stage.Objectives, stage.ObjectiveMode)
                   : null;
        stageCleared = false;
        totalMoves = movesLeft = stage != null && stage.Moves > 0 ? stage.Moves : 9999;
        score = 0;
        busy = false;
        touchActive = false;

        pendingBomb = false;
        queue.Clear();
        current = Piece.CreateRandom(pieceRng, Rules.ColorCount);
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));

        view.SetObstacleMaxHp(MaxBrickHp());
        view.Build();
        view.ApplySkin(Wallet.Skin);   // 상점에서 바꾼 스킨을 다음 판부터 반영
        view.SetVisible(true);
        view.Refresh(board, palette);
        ui.ShowGame();
        ui.SetNext(new List<Piece>(queue), palette);

        Phase = GamePhase.Playing;
        if (timeAttack) taDeadline = Time.time + Rules.TimeAttackMs / 1000f;
        else if (stage != null && stage.TimeSeconds > 0) taDeadline = Time.time + stage.TimeSeconds;
        else StartPieceTimer();
    }

    void EndGame()
    {
        Phase = GamePhase.Result;
        busy = false;
        view.HideGhost();

        // 코인부터 정산한다. 결과 화면에 이번 판에서 번 코인을 보여줘야 하기 때문이다.
        earnedCoins = Rules.CoinsFor(score);
        Wallet.AddCoins(earnedCoins);

        int best;
        bool newBest;
        if (taRunning)
        {
            string key = BestKey(true, difficulty);
            best = PlayerPrefs.GetInt(key, 0);
            newBest = score > best;
            if (newBest) { PlayerPrefs.SetInt(key, score); PlayerPrefs.Save(); best = score; }
        }
        else
        {
            stageCleared = objectives != null && objectives.Cleared;
            best = Progress.Best(stageLevel);
            newBest = score > best;
            Progress.SetBest(stageLevel, score);
            if (newBest) best = score;
            if (stageCleared) Progress.Clear(stageLevel);   // 다음 스테이지를 연다
        }

        if (stageCleared || newBest) sfx.PlayWin(); else sfx.PlayLose();
        ui.ShowResult(taRunning, score, best, newBest, earnedCoins,
                      taRunning ? 0 : stageLevel, stageCleared);

        // 순위 등록은 자동으로 하지 않는다 — 광고를 보고 사용자가 직접 올린다.

        pendingScore = score;
        pendingTa = taRunning;
        pendingSeed = curSeed;
        ui.SetSubmitState(CanSubmit ? GameUI.SubmitState.Pending : GameUI.SubmitState.Off);
    }

    /// <summary>아직 올리지 않은 점수가 있는가 (랭킹이 설정돼 있고 0점이 아닐 때).</summary>
    public bool CanSubmit
    {
        get
        {
            // 스테이지 점수는 올리지 않는다 — 판마다 목표가 달라 한 표에 섞을 수 없다.
            var lb = Leaderboard.I;
            return pendingTa && lb != null && lb.Configured && pendingScore > 0;
        }
    }

    public int PendingScore { get { return pendingScore; } }
    /// <summary>직전 게임에서 번 코인 (결과 화면 표시용).</summary>
    public int EarnedCoins { get { return earnedCoins; } }

    /// <summary>직전 게임 점수를 랭킹에 올린다. 광고를 본 뒤 호출된다.</summary>
    public void SubmitPending(System.Action<bool> done)
    {
        var lb = Leaderboard.I;
        if (!CanSubmit) { if (done != null) done(false); return; }
        ui.SetSubmitState(GameUI.SubmitState.Sending);
        StartCoroutine(lb.Submit(pendingTa, Difficulty, pendingScore, pendingSeed, Progress.Unlocked, ok =>
        {
            ui.SetSubmitState(ok ? GameUI.SubmitState.Done : GameUI.SubmitState.Failed);
            if (done != null) done(ok);
        }));
    }

    public static string BestKey(bool ta, string diff) { return ta ? "best_ta" : "best_score_" + diff; }
    public int BestForSelection()
    {
        return timeAttack ? PlayerPrefs.GetInt(BestKey(true, difficulty), 0)
                          : Progress.Best(Progress.Selected);
    }

    // ---------- 루프 ----------

    void Update()
    {
        if (Screen.width != lastW || Screen.height != lastH) FitCamera();
        if (Phase != GamePhase.Playing) return;

        if (taRunning)
        {
            if (!busy && Time.time >= taDeadline) { EndGame(); return; }
        }
        else if (!busy && Time.time >= pieceDeadline)
        {
            StartCoroutine(ExpirePiece());
            return;
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
        if (pendingBomb)
        {
            view.ShowBombGhost(ax, ay, can, can ? board.EffectCells(ItemType.Bomb5, ax, ay) : null);
        }
        else
        {
            view.ShowGhost(current, ax, ay, can, palette[current.Color]);
            // 여기 놓으면 어느 칸이 사라지는지 흰색으로 예고한다
            view.ShowMatchPreview(can ? board.PreviewStamp(current, ax, ay) : null);
        }
        if (stamp && can) StartCoroutine(DoStamp(ax, ay));
    }

    /// <summary>상점 아이템을 쓴다. 보유량이 없거나 지금 쓸 수 없으면 false.</summary>
    public bool UseItem(ShopItem it)
    {
        if (Phase != GamePhase.Playing || busy) return false;
        if (Wallet.Count(it) <= 0) return false;

        switch (it)
        {
            case ShopItem.BombPiece:
                // 1칸짜리 폭탄. 매칭이 없어도 던진 자리에서 바로 터진다(Board.Detonate).
                current = new Piece("bomb", new List<Point> { new Point(0, 0) }, current.Color);
                pendingBomb = true;
                break;
        }

        Wallet.Use(it);
        sfx.PlayItem();
        // 돈을 주고 바꾼 조각이다. 남은 시간이 얼마든 조준할 시간을 새로 준다.
        if (!taRunning) StartPieceTimer();
        return true;
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
        var visual = new int[Defaults.Width, Defaults.Height];
        for (int x = 0; x < Defaults.Width; x++)
            for (int y = 0; y < Defaults.Height; y++)
                visual[x, y] = board.GetTile(x, y);
        var stamped = new List<Point>();
        foreach (var c in current.Cells)
        {
            var p = new Point(ax + c.X, ay + c.Y);
            stamped.Add(p);
            visual[p.X, p.Y] = current.Color;
        }

        // 폭탄 조각은 매칭을 기다리지 않는다 — 심고 그 자리에서 바로 터뜨린다.
        ResolveResult result;
        if (pendingBomb)
        {
            pendingBomb = false;
            board.SetItem(ax, ay, ItemType.Bomb5);
            result = board.Detonate(ax, ay);
        }
        else result = board.Stamp(current, ax, ay);
        if (!taRunning) movesLeft--;

        // 1) 스탬프 — 들어올렸다가 내려찍는다. 소리와 셰이크는 '꽂히는 순간'에 맞춘다.
        yield return view.StampCells(stamped, palette[current.Color], stampTime, () =>
        {
            sfx.PlayStamp();
            Shake(0.22f, 0.14f, true);
        });

        // 2) 파괴 — 연쇄 단계별로 순차 폭발
        if (result.Destroyed.Count > 0)
        {
            yield return DestroyWaves(result, visual);
            if (result.MaxChain >= 2 || result.ScoreGained >= 500)
                ui.ShowChainPopup(result.MaxChain, result.ScoreGained);
            yield return new WaitForSeconds(destroyFlash);
        }
        if (result.Spawns.Count > 0) sfx.PlayItem();

        score += result.ScoreGained;
        if (objectives != null) objectives.Apply(result, score);

        // 장애물은 게임 중에 늘리지 않는다 — 자리는 스테이지 설정이 유일한 출처다.
        view.Refresh(board, palette);

        // 다음 조각
        current = queue.Dequeue();
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));
        ui.SetNext(new List<Piece>(queue), palette);

        busy = false;
        if (!taRunning)
        {
            // 목표를 채웠으면 수가 남아도 그 자리에서 끝난다
            if (objectives != null && objectives.Cleared) { stageCleared = true; EndGame(); yield break; }
            if (movesLeft <= 0) { EndGame(); yield break; }
            StartPieceTimer();
        }
    }


    /// <summary>이 판 brick 의 최대 내구도. 설정이 준 값 중 가장 큰 것.</summary>
    int MaxBrickHp()
    {
        int hp = 1;
        if (stage != null)
            foreach (var o in stage.Obstacles)
                if (o.Cell == Board.Obstacle && o.HitsToBreak > hp) hp = o.HitsToBreak;
        return hp;
    }

    /// <summary>스테이지를 열 때 보드 구조를 로그로 남긴다. 에러가 아니라 정보다.</summary>
    void LogStageTopology()
    {
        if (stage == null) return;
        Debug.Log("[stage " + stage.StageId + "] 조작 가능 칸 = "
                  + stage.Width * stage.Height + " - " + stage.ObstacleCells
                  + " = " + stage.PlayableCells);

        var regions = BoardTopology.Regions(board);
        if (regions.Count > 1)
            Debug.Log("[stage " + stage.StageId + "] 보드가 " + regions.Count + "개 영역으로 분단됨");
        for (int i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            string tag = r.DeadZone ? "dead zone" : (r.Refillable ? "정상" : "고립(소모전)");
            Debug.Log("[stage " + stage.StageId + "] 영역 " + (i + 1) + ": " + r.Size + "칸 · " + tag);
        }
    }

    /// <summary>제한 시간이 다 된 조각은 버려진다. 기회도 한 번 소모한다 —
    /// 아니면 가만히 두는 것만으로 조각을 공짜로 넘길 수 있다.</summary>
    IEnumerator ExpirePiece()
    {
        busy = true;
        view.HideGhost();
        sfx.PlayExpire();
        yield return new WaitForSeconds(0.15f);

        movesLeft--;
        pendingBomb = false;   // 폭탄은 그 조각에만 붙는다. 다음 조각으로 넘어가지 않는다.
        current = queue.Dequeue();
        queue.Enqueue(Piece.CreateRandom(pieceRng, Rules.ColorCount));
        ui.SetNext(new List<Piece>(queue), palette);

        busy = false;
        if (movesLeft <= 0) EndGame();
        else StartPieceTimer();
    }

    void StartPieceTimer()
    {
        pieceTimeTotal = Rules.PieceTimeMs(movesLeft, totalMoves) / 1000f;
        pieceDeadline = Time.time + pieceTimeTotal;
    }

    /// <summary>연쇄 단계(웨이브)를 하나씩 터뜨린다. 각 웨이브 안에서도 매칭 → 아이템 발동 순.</summary>
    IEnumerator DestroyWaves(ResolveResult result, int[,] visual)
    {
        // 단계가 많으면 전체가 늘어지므로 조금씩 줄이되, 눈이 못 따라갈 만큼 빨라지지는 않게 한다.
        int segments = 0;
        foreach (var w in result.Waves)
            segments += (w.MatchEnd > w.Start ? 1 : 0) + (w.End > w.MatchEnd ? 1 : 0);
        float step = segments <= 1 ? 0f : Mathf.Max(0.10f, chainStep - 0.004f * segments);
        float fall = Mathf.Max(0.11f, chainFall - 0.005f * result.Waves.Count);

        for (int i = 0; i < result.Waves.Count; i++)
        {
            var w = result.Waves[i];
            sfx.PlayDestroy(i + 1);

            // 첫 웨이브의 매칭만 '직접 터진 칸'. 아이템 발동과 후속 웨이브는 전부 '연계'.
            yield return BurstSegment(result, visual, w.Start, w.MatchEnd,
                i == 0 ? DirectFlash : ChainFlash, i + 1, result.BigHit, step);
            yield return BurstSegment(result, visual, w.MatchEnd, w.End,
                ChainFlash, i + 1, result.BigHit, step);

            // 다음 단계의 매칭은 여기서 내려온 블록이 만든 것이다.
            // 낙하를 먼저 보여주고 새로 채워진 칸을 반짝여야 인과가 읽힌다.
            bool last = i == result.Waves.Count - 1;
            yield return ApplyWave(w, visual, last ? fallTime : fall, last ? 0.02f : 0.011f);
        }
    }

    /// <summary>한 단계의 중력·리필 결과를 낙하 연출로 반영하고, 새로 채워진 칸을 반짝인다.</summary>
    IEnumerator ApplyWave(Wave w, int[,] visual, float dur, float stagger)
    {
        var changed = new bool[Defaults.Width, Defaults.Height];
        var newCells = new List<Point>();
        bool any = false;
        for (int x = 0; x < Defaults.Width; x++)
            for (int y = 0; y < Defaults.Height; y++)
            {
                int after = w.TilesAfter[x * Defaults.Height + y];
                if (after == visual[x, y]) continue;
                changed[x, y] = true;
                any = true;
                if (after != Board.Empty) newCells.Add(new Point(x, y));
                visual[x, y] = after;
            }

        view.ApplyState(w.TilesAfter, w.ItemsAfter, w.ObstacleHpAfter, palette);
        if (!any) yield break;

        yield return view.FallIn(changed, dur, stagger);

        // 착지 타격: 8~12px 로 시작해 0.15초 안에 급격히 잦아든다.
        // 화면 높이 19 월드유닛 / 960px 이므로 1px ≈ 0.0198 유닛.
        float impact = Mathf.Clamp01(view.LastMaxDrop / 6f);
        if (impact > 0.1f) Shake(Mathf.Lerp(0.158f, 0.238f, impact), 0.15f, true);
        yield return view.LandCells(newCells, landTime);
    }

    IEnumerator BurstSegment(ResolveResult result, int[,] visual, int from, int to,
                             Color flash, int chain, bool bigHit, float step)
    {
        if (to <= from) yield break;

        var pts = new List<Point>(to - from);
        var colors = new List<Color>(to - from);
        for (int i = from; i < to; i++)
        {
            var p = result.Destroyed[i];
            pts.Add(p);
            int ci = visual[p.X, p.Y];
            colors.Add(ci >= 0 && ci < palette.Length ? palette[ci] : Color.white);
        }

        view.FlashCells(pts, flash);
        float energy = 1f + 0.35f * Mathf.Clamp(chain - 1, 0, 6) + (bigHit ? 0.6f : 0f);
        view.Burst(pts, colors, energy, flash);

        float mag = Mathf.Min(0.7f, 0.12f + 0.05f * (chain - 1) + 0.01f * Mathf.Min(pts.Count, 30));
        Shake(mag, 0.18f);

        if (step > 0f) yield return new WaitForSeconds(step);
    }



    // 세로(모바일)/가로(에디터) 모두 보드 전체 + 상단 HUD 공간이 나오게 카메라 맞춤
    void FitCamera()
    {
        lastW = Screen.width; lastH = Screen.height;
        float aspect = (float)Mathf.Max(1, Screen.width) / Mathf.Max(1, Screen.height);
        // 가로 여백이 카메라 크기를 결정한다. 보드 판이 잘리지 않는 선까지 좁혔다.
        float half = Mathf.Max(Defaults.Height / 2f + 2.5f, (Defaults.Width / 2f + 0.44f) / aspect);
        cam.orthographicSize = half;
        camBase = new Vector3((Defaults.Width - 1) / 2f, (Defaults.Height - 1) / 2f + half * 0.06f, -10);
        if (shakeCo == null) cam.transform.position = camBase;
        FitBackground();
    }

    /// <summary>카메라 흔들림 (타격감). 파괴 규모/연쇄에 비례해 호출.</summary>
    public void Shake(float magnitude, float duration) { Shake(magnitude, duration, false); }

    /// <summary>카메라 흔들림. sharp=true 면 처음에 세게 때리고 급격히 잦아든다 (착지 타격용).</summary>
    public void Shake(float magnitude, float duration, bool sharp)
    {
        if (!isActiveAndEnabled || cam == null) return;
        if (shakeCo != null) StopCoroutine(shakeCo);
        shakeCo = StartCoroutine(ShakeCo(magnitude, duration, sharp));
    }

    IEnumerator ShakeCo(float mag, float dur, bool sharp)
    {
        float t = 0;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / dur);
            // 선형 감쇠는 '흔들린다'로 읽히고, 제곱 감쇠는 '맞았다'로 읽힌다
            float damper = sharp ? k * k * k : k;
            Vector2 r = UnityEngine.Random.insideUnitCircle;
            if (sharp) r.y = Mathf.Abs(r.y) * (UnityEngine.Random.value < 0.5f ? -1.4f : 1.4f); // 세로 성분 강조
            cam.transform.position = camBase + (Vector3)(r * (mag * damper));
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
