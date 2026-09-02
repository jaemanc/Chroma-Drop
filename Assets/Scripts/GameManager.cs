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
    public int stageLevel = 1;             // 스테이지. 설정은 stages/stages.json 이 갖는다

    [Header("연출 시간(초)")]
    public float stampTime = 0.34f;        // 들어올림 → 내려찍기 → 버팀 → 복원 전체
    public float destroyFlash = 0.22f;
    public float chainStep = 0.19f;        // 연쇄 한 단계가 터지는 간격
    public float chainFall = 0.32f;        // 연쇄 단계 사이 낙하 시간 (마지막 낙하는 fallTime)
    public float landTime = 0.18f;         // 착지 스쿼시·복원
    public float fallTime = 0.42f;
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
    StageSetting stage = new StageSetting();   // 이 판의 설정
    Piece current;                                   // 지금 집은 조각 (트레이에서 고른 것)
    readonly Piece[] tray = new Piece[BoardView.TraySlots];
    int selectedSlot = -1;                           // -1 이면 아무것도 안 집은 상태
    bool dragging;                                   // 손가락으로 끌고 있는 중인가
    bool pressedTraySlot;                            // 트레이의 지금 블록에서 누르기 시작했나
    System.Random pieceRng;
    Color[] palette;

    BoardView view;
    GameUI ui;
    Sfx sfx;
    Camera cam;
    SpriteRenderer bg;                      // 카메라를 덮는 배경 이미지

    int score, movesLeft, totalMoves;
    bool busy, taRunning, touchActive;
    float pieceDeadline, pieceTimeTotal, taDeadline;
    int lastW, lastH, curSeed;
    int pendingScore, pendingSeed, earnedCoins;
    bool pendingTa, pendingBomb;

    /// <summary>폭탄 조각이 장전돼 있나.</summary>
    public bool BombArmed { get { return pendingBomb; } }

    /// <summary>이 판의 설정. 값은 stages/stages.json 이 갖는다.</summary>
    public StageSetting Stage { get { return stage; } }

    Vector3 camBase;

    /// <summary>상단 HUD 가 차지하는 높이 비율. 이만큼 보드를 아래로 민다.
    /// HUD 를 줄였으므로 예전보다 작다.</summary>
    const float HudRoom = 0.095f;
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

    /// <summary>보드 바깥에 붙는 HUD(타이머·아이템 줄)를 제자리에 맞춘다.
    /// 패널이 꺼져 있으면 사각형 크기가 0 이라 한 번만 계산해선 안 된다 — 매 프레임 맞춘다.</summary>
    void LayoutHud()
    {
        if (ui == null || cam == null) return;
        // 보드 판은 칸 범위(0 ~ H-1)보다 여백·테두리만큼 넓다
        const float PanelEdge = 0.85f;
        ui.FollowWorld(cam, (Board.W - 1) / 2f, (Board.H - 1) + PanelEdge, -PanelEdge);
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

    public void StartGame(string diff, bool ta, int seedOverride)
    {
        StopAllCoroutines();
        shakeCo = null;
        if (cam != null) cam.transform.position = camBase;
        timeAttack = ta;

        // 판을 만들기 전에 이 스테이지의 설정부터 정한다.
        // 뒤에 대입하면 보드·팔레트가 이전 스테이지 값으로 만들어진다.
        stage = StageTable.Get(stageLevel);

        int s = seedOverride == 0 ? System.Environment.TickCount : seedOverride;
        curSeed = s;
        board = new Board(stage.ColorCount, s);
        pieceRng = new System.Random(s + 1);
        palette = Palette.Generate(stage.ColorCount, new System.Random(s + 2));

        taRunning = timeAttack;
        totalMoves = movesLeft = timeAttack ? 9999 : stage.Moves;
        score = 0;
        busy = false;
        touchActive = false;

        pendingBomb = false;
        selectedSlot = -1;
        dragging = false;
        for (int i = 0; i < tray.Length; i++) tray[i] = Piece.CreateRandom(pieceRng, stage.ColorCount);
        selectedSlot = BoardView.CurrentSlot;
        current = tray[BoardView.CurrentSlot];

        view.Build();
        view.ApplySkin(Wallet.Skin);   // 상점에서 바꾼 스킨을 다음 판부터 반영
        view.SetVisible(true);
        view.Refresh(board, palette);
        ui.ShowGame();
        RefreshTray();

        Phase = GamePhase.Playing;
        if (timeAttack) taDeadline = Time.time + Rules.TimeAttackMs / 1000f;
        else StartPieceTimer();
    }

    void EndGame()
    {
        Phase = GamePhase.Result;
        busy = false;
        view.HideGhost();

        string key = BestKey(taRunning, difficulty);
        int best = PlayerPrefs.GetInt(key, 0);
        bool newBest = score > best;
        if (newBest) { PlayerPrefs.SetInt(key, score); PlayerPrefs.Save(); best = score; }

        if (newBest) sfx.PlayWin(); else sfx.PlayLose();
        ui.ShowResult(taRunning, score, best, newBest, earnedCoins);

        // 순위 등록은 자동으로 하지 않는다 — 광고를 보고 사용자가 직접 올린다.
        earnedCoins = Rules.CoinsFor(score);
        Wallet.AddCoins(earnedCoins);

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
            var lb = Leaderboard.I;
            return lb != null && lb.Configured && pendingScore > 0;
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
        StartCoroutine(lb.Submit(pendingTa, Difficulty, pendingScore, pendingSeed, ok =>
        {
            ui.SetSubmitState(ok ? GameUI.SubmitState.Done : GameUI.SubmitState.Failed);
            if (done != null) done(ok);
        }));
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
            if (!busy && Time.time >= taDeadline) { EndGame(); return; }
        }
        else if (!busy && Time.time >= pieceDeadline)
        {
            StartCoroutine(ExpirePiece());
            return;
        }

        ui.UpdateHud(this);
        LayoutHud();
        if (busy) { view.HideGhost(); return; }
        HandleInput();
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.R)) RotateCurrent();
        if (current == null) return;

        // 모바일이 기준이다. 터치가 없으면 마우스로 같은 흐름을 흉내낸다.
        Vector2 sp;
        bool down = false, up = false;

        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            sp = t.position;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId))
            { CancelDrag(); return; }
            down = t.phase == TouchPhase.Began;
            up = t.phase == TouchPhase.Ended;
            if (t.phase == TouchPhase.Canceled) { CancelDrag(); return; }
        }
        else
        {
            sp = Input.mousePosition;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            { CancelDrag(); return; }
            down = Input.GetMouseButtonDown(0);
            up = Input.GetMouseButtonUp(0);
        }

        Vector3 w = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10));

        // 누르는 순간 바로 지금 블록을 집는다. 트레이를 먼저 고를 필요가 없다.
        if (down)
        {
            dragging = true;
            // 트레이의 지금 블록을 눌렀는지 기억한다 — 제자리에서 떼면 회전이다.
            pressedTraySlot = BoardView.TrayHit(new Vector2(w.x, w.y)) == BoardView.CurrentSlot;
            RefreshTray();
        }
        if (!dragging) { view.HideGhost(); return; }

        // 손가락이 조각을 가리지 않게 위로 띄운다
        float lift = Input.touchCount > 0 ? ghostLiftCells : 0f;
        Vector2 aim = new Vector2(w.x, w.y + lift);

        int ax = Mathf.RoundToInt(aim.x) - MaxCell(current, 0) / 2;
        int ay = Mathf.RoundToInt(aim.y) - MaxCell(current, 1) / 2;
        bool can = board.CanPlace(current, ax, ay);

        if (pendingBomb)
        {
            view.ShowBombGhost(ax, ay, can, can ? board.EffectCells(ItemType.Bomb5, ax, ay) : null);
        }
        else
        {
            // 커서 좌표를 그대로 넘긴다 — 칸에 붙여 그리면 들고 다니는 느낌이 안 난다
            view.ShowGhost(current, aim.x, aim.y, ax, ay, can, palette[current.Color]);
            view.ShowMatchPreview(can ? board.PreviewStamp(current, ax, ay) : null);
        }

        // 손을 떼면 놓는다.
        if (!up) return;
        dragging = false;

        // 트레이에서 눌러 트레이에서 뗐으면 놓는 게 아니라 회전이다.
        // 회전 버튼을 없앴으므로 이게 유일한 회전 수단이다.
        bool onTray = BoardView.TrayHit(new Vector2(w.x, w.y)) == BoardView.CurrentSlot;
        if (pressedTraySlot && onTray)
        {
            pressedTraySlot = false;
            view.HideGhost();
            RotateCurrent();
            sfx.PlayItem();
            return;
        }
        pressedTraySlot = false;

        if (can) StartCoroutine(DoStamp(ax, ay));
        else { view.HideGhost(); RefreshTray(); }
    }

    /// <summary>드래그를 취소한다.</summary>
    void CancelDrag()
    {
        if (dragging) { dragging = false; RefreshTray(); }
        view.HideGhost();
    }

    /// <summary>상점 아이템을 쓴다. 보유량이 없거나 지금 쓸 수 없으면 false.</summary>
    public bool UseItem(ShopItem it)
    {
        if (Phase != GamePhase.Playing || busy) return false;
        if (Wallet.Count(it) <= 0) return false;
        if (!EnsureSelected()) return false;

        // 이미 장전돼 있으면 또 쓰지 않는다.
        // 안 막으면 버튼을 누를 때마다 보유량이 계속 깎인다.
        if (it == ShopItem.BombPiece && pendingBomb) return false;

        switch (it)
        {
            case ShopItem.BombPiece:
                // 1칸짜리 폭탄. 매칭이 없어도 던진 자리에서 바로 터진다(Board.Detonate).
                current = new Piece("bomb", new List<Point> { new Point(0, 0) }, current.Color);
                pendingBomb = true;
                break;
        }

        Wallet.Use(it);
        tray[selectedSlot] = current;      // 폭탄으로 바뀐 조각을 트레이에도 반영
        RefreshTray();
        sfx.PlayItem();
        // 돈을 주고 바꾼 조각이다. 남은 시간이 얼마든 조준할 시간을 새로 준다.
        if (!taRunning) StartPieceTimer();
        return true;
    }

    public void RotateCurrent()
    {
        if (Phase != GamePhase.Playing || busy) return;
        if (current == null || selectedSlot < 0) return;   // 집은 게 없으면 돌릴 것도 없다
        current = current.Rotated();
        tray[selectedSlot] = current;                      // 트레이에도 돌린 모양을 반영한다
        RefreshTray();
    }

    /// <summary>프로그램/테스트용 스탬프 진입점. 성공 시 코루틴 시작.</summary>
    /// <summary>트레이에서 조각을 집는다. 지금 블록(0번)만 고를 수 있다.</summary>
    public bool SelectSlot(int i)
    {
        if (Phase != GamePhase.Playing || busy) return false;
        if (i != BoardView.CurrentSlot || tray[i] == null) return false;
        selectedSlot = i;
        current = tray[i];
        dragging = false;
        RefreshTray();
        return true;
    }

    /// <summary>지금 집어 놓은 슬롯. 아무것도 안 집었으면 -1.</summary>
    public int SelectedSlot { get { return selectedSlot; } }

    /// <summary>트레이 슬롯의 조각. 비었으면 null.</summary>
    public Piece TraySlot(int i) { return i >= 0 && i < tray.Length ? tray[i] : null; }

    /// <summary>지금 블록이 잡혀 있는지 확인한다. 트레이 0번이 항상 지금 블록이다.</summary>
    bool EnsureSelected()
    {
        if (current != null) return true;
        return SelectSlot(BoardView.CurrentSlot);
    }

    public bool TryStamp(int ax, int ay)
    {
        if (Phase != GamePhase.Playing || busy || board == null) return false;
        if (!EnsureSelected()) return false;
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

        // 아무 칸도 안 터진 수에는 벌칙으로 벽돌이 하나 생긴다.
        // 그 자리에 아이템이 있었으면 아이템은 사라진다.
        if (result.Destroyed.Count == 0 && stage.PenaltyObstacle)
        {
            Point put;
            if (board.PenaltyObstacle(stage.ObstacleHp, out put) >= 0)
            {
                view.Refresh(board, palette);
                yield return view.LandCells(new List<Point> { put }, landTime);
                sfx.PlayExpire();
                Shake(0.18f, 0.14f);
            }
        }

        // 3) 콘크리트 추가 — 수가 진행될수록 많아진다. 그 뒤 최종 상태 확정.
        if (!taRunning)
        {
            int used = totalMoves - movesLeft;
            int add = stage.ObstaclesAfterMove(used, totalMoves);
            if (add > 0 && board.SpawnObstacles(add, stage.ObstacleHp) > 0) sfx.PlayItem();
        }
        view.Refresh(board, palette);

        AdvanceTray();
        dragging = false;

        busy = false;
        if (!taRunning)
        {
            if (movesLeft <= 0) { EndGame(); yield break; }
            StartPieceTimer();
        }
    }

    /// <summary>트레이를 다시 그린다. 고른 슬롯은 들어올려 표시한다.</summary>
    void RefreshTray()
    {
        for (int i = 0; i < tray.Length; i++)
        {
            var p = tray[i];
            var c = p != null ? palette[p.Color] : Color.white;
            // 끌고 있는 동안에는 그 슬롯을 흐리게 — 조각이 손에 따라 나와 있다는 표시
            view.SetTraySlot(i, p, c, i == selectedSlot, dragging && i == selectedSlot);
        }
    }

    /// <summary>다음 블록을 지금 자리로 당기고, 새 조각을 다음 자리에 넣는다.</summary>
    void AdvanceTray()
    {
        for (int i = 0; i < tray.Length - 1; i++) tray[i] = tray[i + 1];
        tray[tray.Length - 1] = Piece.CreateRandom(pieceRng, stage.ColorCount);
        selectedSlot = BoardView.CurrentSlot;
        current = tray[BoardView.CurrentSlot];
        RefreshTray();
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
        dragging = false;
        AdvanceTray();

        busy = false;
        if (movesLeft <= 0) EndGame();
        else StartPieceTimer();
    }

    void StartPieceTimer()
    {
        pieceTimeTotal = stage.PieceTimeMs(movesLeft, totalMoves) / 1000f;
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
        // 살아남아 미끄러진 블록과 새로 들어온 블록을 나눈다.
        // 나누지 않으면 한 칸 내려온 블록도 판 밖에서 떨어지는 것처럼 보여
        // 열 전체가 통째로 교체되는 것처럼 읽힌다.
        // 벽돌도 중력을 받으므로 같이 센다 — 빈칸이 아닌 것은 전부 움직인다.
        var cells = new List<Point>();
        var drops = new List<float>();
        var newCells = new List<Point>();

        for (int x = 0; x < Board.W; x++)
        {
            var before = new List<int>();
            var after = new List<int>();
            for (int y = 0; y < Board.H; y++)
            {
                if (visual[x, y] != Board.Empty) before.Add(y);
                if (w.TilesAfter[x * Board.H + y] != Board.Empty) after.Add(y);
            }

            // 중력은 위아래 순서를 지키므로 아래에서부터 1:1 로 대응한다
            for (int i = 0; i < after.Count; i++)
            {
                int ny = after[i];
                float drop;
                if (i < before.Count) drop = before[i] - ny;                 // 미끄러진 거리
                else drop = (Board.H + (i - before.Count)) - ny;             // 판 위에서 새로 들어옴

                if (drop <= 0.001f) continue;                                 // 안 움직인 블록은 건드리지 않는다
                cells.Add(new Point(x, ny));
                drops.Add(drop);
                if (i >= before.Count) newCells.Add(new Point(x, ny));
            }
        }

        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++) visual[x, y] = w.TilesAfter[x * Board.H + y];

        view.ApplyState(w.TilesAfter, w.ItemsAfter, w.ObstacleHpAfter, palette);
        if (cells.Count == 0) yield break;

        yield return view.FallIn(cells, drops, dur, stagger);

        // 착지 타격: 멀리 떨어진 만큼 세게. 짧게 치고 빠진다.
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

            // 눈에서 터졌으니 시각 상태에서도 비운다.
            // 안 비우면 ApplyWave 가 낙하 전/후를 같은 것으로 봐서 이동 거리가 전부 0 이 된다.
            visual[p.X, p.Y] = Board.Empty;
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
        // 보드와 트레이가 함께 들어와야 한다. 트레이는 보드 아래 TrayY 에 있다.
        float need = (Board.H - 1) - (BoardView.TrayY - BoardView.TrayRadius) + 1.5f;
        float half = Mathf.Max(need * 0.5f, (Board.W / 2f + 0.44f) / aspect);
        cam.orthographicSize = half;

        // 보드를 위로, 트레이를 아래로. 다만 상단 HUD(점수·타이머·아이템)가
        // 보드를 덮지 않도록 전체를 그만큼 아래로 민다.
        float mid = ((Board.H - 1) + (BoardView.TrayY - BoardView.TrayRadius)) * 0.5f;
        camBase = new Vector3((Board.W - 1) / 2f, mid + half * HudRoom, -10);
        if (shakeCo == null) cam.transform.position = camBase;

        LayoutHud();
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
