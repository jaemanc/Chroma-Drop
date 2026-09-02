// GameManager.cs — 게임 흐름. 규칙은 전부 ChromaDrop.Engine 이 갖고,
// 난이도는 전부 stages.json 이 갖는다. 여기에는 스테이지별 분기가 없다.
//
// 조작: 셀을 누르면 지금 조각을 그 자리에 놓는다. ROTATE 는 조각만 바꾼다 —
// 보드도 중력도 회전하지 않는다 (중력은 위→아래 상수).

using System.Collections;
using System.Collections.Generic;
using ChromaDrop.Engine;
using UnityEngine;
using UnityEngine.EventSystems;

public enum GamePhase { Home, Playing, Result }

public class GameManager : MonoBehaviour
{
    public int seed = 0;
    public bool timeAttack = false;

    // ---------- 연출 속도 ----------
    public float stampTime = 0.34f;    // 들어올림 → 내려찍기 → 버팀 → 복원
    public float flashTime = 0.20f;    // 사라지기 직전 번쩍임
    public float chainStep = 0.14f;    // 연쇄 한 단계 사이 간격
    public float fallTime = 0.18f;

    public GamePhase Phase { get; private set; }
    public bool Busy { get { return busy; } }
    public int Score { get { return score; } }
    public int MovesLeft { get { return movesLeft; } }
    public bool TimeAttackMode { get { return taRunning; } }
    public float TimeLeftSec { get { return deadline > 0 ? Mathf.Max(0, deadline - Time.time) : 0; } }

    public int StageLevel { get { return stageLevel; } }
    public StageDef Stage { get { return stage; } }
    public StageInstance Instance { get { return inst; } }
    public bool StageCleared { get { return cleared; } }
    public int RerollsLeft { get { return inst != null ? inst.Turn.RerollsLeft : 0; } }

    public List<ObjectiveProgress> Objectives
    {
        get { return inst != null ? inst.Objectives.Snapshot() : new List<ObjectiveProgress>(); }
    }

    public int PendingScore { get { return pendingScore; } }
    public int EarnedCoins { get { return earnedCoins; } }

    StageDef stage;
    StageInstance inst;
    int stageLevel = 1;
    int score, movesLeft, totalMoves;
    bool busy, taRunning, cleared;
    float deadline;
    int pendingScore, pendingSeed, earnedCoins;
    bool pendingTa;

    GraphBoardView view;
    GameUI ui;
    Sfx sfx;
    Camera cam;
    SpriteRenderer bg;
    Vector3 camBase;
    int lastW, lastH;
    Coroutine shakeCo;

    // 아이템: 지금 고른 아이템과 축 (line 은 축을 고를 수 있다)
    SpatialItem armed;
    int armedAxis;

    public SpatialItem ArmedItem { get { return armed; } }
    public int ArmedAxis { get { return armedAxis; } }

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
        cam.backgroundColor = BackgroundColor;

        Leaderboard.Create();
        BuildBackground();
        view = new GameObject("BoardView").AddComponent<GraphBoardView>();
        sfx = gameObject.AddComponent<Sfx>();
        ui = GameUI.Create(this);

        FitCamera();
        GoHome();
    }

    static readonly Color BackgroundColor = new Color(0.80f, 0.89f, 0.93f);

    // 보드가 화면에서 차지하는 자리 (세로 844 기준 프로토타입 비율).
    // 위쪽 HUD 와 아래쪽 버튼 줄을 피해 예전 판이 있던 곳에 맞춘다.
    const float BoardWidthFrac = 0.94f;
    const float BoardHeightFrac = 0.58f;
    const float BoardCenterFrac = 0.53f;   // 화면 위에서부터의 비율

    void BuildBackground()
    {
        var tex = Resources.Load<Texture2D>("jungle_bg");
        if (tex == null) return;
        var go = new GameObject("Background");
        bg = go.AddComponent<SpriteRenderer>();
        bg.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        bg.sortingOrder = -20;
    }

    public void GoHome()
    {
        Phase = GamePhase.Home;
        busy = false;
        armed = null;
        if (view != null) { view.SetVisible(false); view.HideNextPiece(); }
        ui.ShowHome();
    }

    // ---------- 시작 ----------

    public void StartGame() { StartStage(Progress.Selected, seed); }

    public void StartStage(int level) { StartStage(level, seed); }

    public void StartStage(int level, int seedOverride)
    {
        StopAllCoroutines();
        shakeCo = null;
        if (cam != null) cam.transform.position = camBase;

        stageLevel = Mathf.Clamp(level, 1, Mathf.Max(1, StageCatalog.Count));
        stage = StageCatalog.Get(stageLevel);
        if (stage == null)
        {
            Debug.LogError("[stage] " + stageLevel + "번 설정을 못 읽었다: "
                           + string.Join(" / ", StageCatalog.Set.Errors.ToArray()));
            GoHome();
            return;
        }

        // 시드를 넘기면 그 시드로 판을 만든다 (테스트·리플레이용)
        if (seedOverride != 0) stage.Seed = seedOverride;

        inst = StageBuilder.Build(stage, PaletteBridge.FromUnity(BackgroundColor));
        foreach (var line in inst.Log) Debug.Log("[stage " + stageLevel + "] " + line);

        taRunning = stage.TimeSeconds > 0;
        totalMoves = movesLeft = stage.Moves > 0 ? stage.Moves : int.MaxValue;
        score = 0;
        cleared = false;
        busy = false;
        armed = null;

        view.SetPalette(PaletteBridge.ToUnity(inst.Palette));
        FitCamera();
        view.SetVisible(true);
        view.Refresh(inst.Engine);

        ui.ShowGame();
        ui.RebuildAxisButtons();      // 축 버튼 수는 토폴로지가 정한다
        ShowNextPiece();
        Phase = GamePhase.Playing;
        deadline = taRunning ? Time.time + stage.TimeSeconds : 0;

        WarnTouchSize();
    }

    void WarnTouchSize()
    {
        // 화면에서 셀이 너무 작으면 터치가 어렵다. 규칙이 아니라 품질 경고다.
        float pxPerUnit = Screen.height / (cam.orthographicSize * 2f);
        float cellPt = view.CellSize * pxPerUnit / Mathf.Max(1f, Screen.dpi / 72f);
        string w = Render.TouchWarning(cellPt);
        if (w != null) Debug.LogWarning("[render] " + w);
    }

    // ---------- 루프 ----------

    void Update()
    {
        if (Screen.width != lastW || Screen.height != lastH) FitCamera();
        if (Phase != GamePhase.Playing) return;

        if (taRunning && !busy && Time.time >= deadline) { EndGame(); return; }

        ui.UpdateHud(this);
        if (busy) { view.ClearHighlight(); return; }
        HandleInput();
    }

    void HandleInput()
    {
        Vector2 sp;
        bool commit = false;

        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            sp = t.position;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId)) return;
            if (t.phase == TouchPhase.Ended) commit = true;
        }
        else
        {
            sp = Input.mousePosition;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            commit = Input.GetMouseButtonDown(0);
        }

        Vector3 world = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10));
        int cell = view.HitTest(world);
        if (cell < 0) { view.ClearHighlight(); return; }

        // 아이템이 장전돼 있으면 아이템 범위를, 아니면 조각이 놓일 자리를 보여준다.
        // 프리뷰와 실행이 같은 함수를 쓰므로 어긋날 수 없다.
        if (armed != null)
        {
            view.ShowGhost(null, Color.white);
            view.ShowDoomed(ItemSystem.Affected(inst.Engine, armed, cell, armedAxis), ItemPreview);
            if (commit) StartCoroutine(FireItem(cell));
            return;
        }

        var place = inst.Turn.PlacementAt(cell);
        if (place == null)
        {
            view.ShowGhost(null, Color.white);
            view.ShowDoomed(null, Color.white);
            return;
        }

        // 여기 놓으면 어느 칸이 사라지는지 흰색으로 예고한다.
        // 조각이 놓일 칸도 그 안에 들면 같이 깜빡인다.
        var pal = PaletteBridge.ToUnity(inst.Palette);
        int color = inst.Turn.CurrentColor;
        var doomed = inst.Engine.PreviewPlace(place, color);

        var doomedSet = new HashSet<int>(doomed);
        var ghostOnly = new List<int>();
        foreach (int id in place) if (!doomedSet.Contains(id)) ghostOnly.Add(id);

        view.ShowGhost(ghostOnly, new Color(pal[color].r, pal[color].g, pal[color].b, 0.9f));
        view.ShowDoomed(doomed, MatchPreview);

        if (commit) StartCoroutine(DoPlace(place));
    }

    static readonly Color MatchPreview = new Color(1f, 1f, 1f);          // 사라질 칸 — 흰색
    static readonly Color ItemPreview = new Color(1f, 0.10f, 0.06f);     // 아이템 범위 — 빨강
    static readonly Color DirectFlash = new Color(1f, 1f, 1f);           // 직접 터진 칸
    static readonly Color ChainFlash = new Color(1f, 0.72f, 0.18f);      // 연계로 터진 칸

    // ---------- 한 수 ----------

    IEnumerator DoPlace(List<int> cells)
    {
        busy = true;
        view.ClearHighlight();

        int color = inst.Turn.CurrentColor;
        foreach (int id in cells) inst.Engine.Set(id, color);
        view.Refresh(inst.Engine);

        // 들어올렸다가 내려찍는다. 소리와 셰이크는 꽂히는 순간에 맞춘다.
        yield return view.StampCells(cells, stampTime, () =>
        {
            sfx.PlayStamp();
            Shake(0.26f, 0.14f);
        });

        movesLeft--;

        // 놓은 자리가 공중이면 여기서 내려앉는다. 위가 막혀 있으면 그대로 멈춘다.
        var settled = inst.Engine.Settle();
        if (settled.Count > 0)
        {
            view.Refresh(inst.Engine);
            yield return view.DropIn(settled, fallTime);
        }

        yield return Resolve(false);

        if (Phase != GamePhase.Playing) yield break;

        // 다음 조각. 놓을 데가 없으면 리롤하고, 리롤도 없으면 끝난다.
        if (inst.Turn.Advance() == NoMoveAction.EndGame)
        {
            Debug.Log("[stage " + stageLevel + "] 놓을 자리가 없고 리롤도 없다");
            EndGame();
            yield break;
        }

        busy = false;
        ShowNextPiece();
        CheckEnd();
    }

    /// <summary>들고 있는 조각을 보드 위쪽에 실제 모양으로 그린다.
    /// 글자만으로는 무슨 모양인지 눈에 안 들어온다.</summary>
    void ShowNextPiece()
    {
        if (inst == null) return;
        var shape = inst.Turn.Current;

        // 어디서든 잘리지 않는 기준 칸을 골라 모양을 편다
        List<int> cells = null;
        for (int i = 0; i < inst.Topo.Count && cells == null; i++)
            cells = PieceShapes.Resolve(inst.Topo, shape, i);

        var pal = PaletteBridge.ToUnity(inst.Palette);
        int c = inst.Turn.CurrentColor;
        var color = c >= 0 && c < pal.Length ? pal[c] : Color.white;

        float visH = cam.orthographicSize * 2f;
        float y = visH * (0.5f - NextCenterFrac);
        view.ShowNextPiece(inst.Topo, cells, new Vector3(0, y, 0), visH * NextSizeFrac, color);

        ui.SetNext(shape.Name, cells != null ? cells.Count : 0, color);
    }

    // 다음 조각 미리보기 자리 (화면 세로 대비)
    const float NextCenterFrac = 0.155f;
    const float NextSizeFrac = 0.075f;

    IEnumerator FireItem(int cell)
    {
        busy = true;
        var item = armed;
        armed = null;

        var res = ItemSystem.Fire(inst.Engine, item, cell, armedAxis);
        score += res.Score;
        inst.Objectives.Apply(res, score, true);
        sfx.PlayItem();
        Shake(0.34f, 0.20f);
        yield return view.FlashCells(res.Cleared, DirectFlash, flashTime);
        view.Refresh(inst.Engine);

        if (stage.ItemCostsMove) movesLeft--;
        yield return Resolve(true);

        busy = false;
        CheckEnd();
    }

    /// <summary>연쇄가 멎을 때까지 소거·낙하·리필을 돌리며 단계마다 보여준다.</summary>
    /// <summary>연쇄가 멎을 때까지 한 단계씩 보여준다.
    /// 단계마다 '무엇이 사라지는지' 를 먼저 번쩍이고 나서 없앤다 — 인과가 읽혀야 한다.</summary>
    IEnumerator Resolve(bool fromItem)
    {
        int chain = 0;
        while (true)
        {
            var doomed = inst.Engine.Clearable();
            if (doomed.Count == 0) break;
            chain++;

            // 첫 단계는 방금 놓은 수가 직접 만든 것, 이후는 무너져 생긴 연계다
            yield return view.FlashCells(doomed, chain == 1 ? DirectFlash : ChainFlash, flashTime);

            var res = inst.Engine.ResolveOnce();
            if (res.Cleared.Count == 0) break;

            score += res.Score;
            inst.Objectives.Apply(res, score, fromItem);
            sfx.PlayDestroy(chain);
            Shake(0.14f + 0.06f * chain, 0.13f);

            view.Refresh(inst.Engine);
            yield return view.DropIn(res.Refilled, fallTime);
            yield return new WaitForSeconds(chainStep);

            fromItem = false;   // 첫 단계만 아이템 발동이고 이후 연쇄는 일반 소거다
            if (!stage.ChainReaction) break;
        }
        view.Refresh(inst.Engine);
    }

    void CheckEnd()
    {
        if (Phase != GamePhase.Playing) return;
        if (inst.Objectives.Cleared) { cleared = true; EndGame(); return; }
        if (!taRunning && movesLeft <= 0) EndGame();
    }

    // ---------- 아이템 ----------

    /// <summary>이 판에서 쓸 수 있는 아이템.</summary>
    public IList<SpatialItem> Items { get { return inst != null ? inst.Items : new List<SpatialItem>(); } }

    /// <summary>축 선택 버튼 수는 토폴로지가 정한다. 하드코딩하지 않는다.</summary>
    public int AxisCount { get { return inst != null ? inst.Topo.Axes.Length : 0; } }
    public string AxisLabel(int i)
    {
        return inst != null && i >= 0 && i < inst.Topo.Axes.Length ? inst.Topo.Axes[i].Label : "";
    }

    public bool ArmItem(string id, int axis)
    {
        if (Phase != GamePhase.Playing || busy) return false;
        if (Wallet.Count(id) <= 0) return false;
        foreach (var it in inst.Items)
            if (it.Id == id)
            {
                armed = it;
                armedAxis = Mathf.Clamp(axis, 0, Mathf.Max(0, AxisCount - 1));
                Wallet.Use(id);
                return true;
            }
        return false;
    }

    public void CancelItem() { armed = null; }

    public void RotateCurrent()
    {
        // 조각만 바꾼다. 보드와 중력은 건드리지 않는다.
        if (Phase == GamePhase.Playing && !busy && inst != null) inst.Turn.Rotate();
    }

    /// <summary>테스트·자동화용 — 이 칸에 지금 조각을 놓는다.</summary>
    public bool TryPlace(int cell)
    {
        if (Phase != GamePhase.Playing || busy || inst == null) return false;
        var place = inst.Turn.PlacementAt(cell);
        if (place == null) return false;
        StartCoroutine(DoPlace(place));
        return true;
    }

    // ---------- 종료 ----------

    void EndGame()
    {
        Phase = GamePhase.Result;
        busy = false;
        view.ClearHighlight();

        earnedCoins = Wallet.CoinsFor(score);
        Wallet.AddCoins(earnedCoins);

        cleared = inst != null && inst.Objectives.Cleared;
        int best = Progress.Best(stageLevel);
        bool newBest = score > best;
        Progress.SetBest(stageLevel, score);
        if (newBest) best = score;
        if (cleared) Progress.Clear(stageLevel);

        if (cleared || newBest) sfx.PlayWin(); else sfx.PlayLose();
        ui.ShowResult(taRunning, score, best, newBest, earnedCoins, stageLevel, cleared);

        pendingScore = score;
        pendingTa = taRunning;
        pendingSeed = stage != null ? stage.Seed : 0;
        ui.SetSubmitState(CanSubmit ? GameUI.SubmitState.Pending : GameUI.SubmitState.Off);
    }

    public bool CanSubmit
    {
        get
        {
            var lb = Leaderboard.I;
            return pendingTa && lb != null && lb.Configured && pendingScore > 0;
        }
    }

    public void SubmitPending(System.Action<bool> done)
    {
        var lb = Leaderboard.I;
        if (!CanSubmit) { if (done != null) done(false); return; }
        ui.SetSubmitState(GameUI.SubmitState.Sending);
        StartCoroutine(lb.Submit(pendingTa, BoardId, pendingScore, pendingSeed, Progress.Unlocked, ok =>
        {
            ui.SetSubmitState(ok ? GameUI.SubmitState.Done : GameUI.SubmitState.Pending);
            if (ok) pendingScore = 0;
            if (done != null) done(ok);
        }));
    }

    /// <summary>랭킹 보드 구분자. 타임어택만 올라간다.</summary>
    public const string BoardId = "timeattack";

    public static string BestKey(bool ta, string board) { return ta ? "best_ta" : "best_" + board; }
    public int BestForSelection()
    {
        return timeAttack ? PlayerPrefs.GetInt(BestKey(true, BoardId), 0) : Progress.Best(Progress.Selected);
    }

    // ---------- 카메라 ----------

    void FitCamera()
    {
        lastW = Screen.width; lastH = Screen.height;
        if (cam == null) return;

        cam.orthographicSize = 10f;
        camBase = new Vector3(0, 0, -10);
        cam.transform.position = camBase;

        if (inst != null && view != null)
        {
            // 보드가 차지하는 자리. 예전 화면에서 판이 있던 비율을 그대로 쓴다.
            float visH = cam.orthographicSize * 2f;
            float visW = visH * (Screen.height <= 0 ? 1f : Screen.width / (float)Screen.height);

            float w = visW * BoardWidthFrac;
            float h = visH * BoardHeightFrac;
            view.Build(inst.Topo, w, h);
            view.transform.position = new Vector3(0, visH * (0.5f - BoardCenterFrac), 0);
            view.SetPalette(PaletteBridge.ToUnity(inst.Palette));
            view.Refresh(inst.Engine);
        }
        FitBackground();
    }

    void FitBackground()
    {
        if (bg == null || bg.sprite == null || cam == null) return;
        float h = cam.orthographicSize * 2f;
        float w = h * (Screen.height <= 0 ? 1f : Screen.width / (float)Screen.height);
        var size = bg.sprite.bounds.size;
        float s = Mathf.Max(w / size.x, h / size.y);
        bg.transform.localScale = Vector3.one * s;
        bg.transform.position = new Vector3(0, 0, 5);
    }

    public void Shake(float mag, float dur)
    {
        if (shakeCo != null) StopCoroutine(shakeCo);
        shakeCo = StartCoroutine(ShakeRoutine(mag, dur));
    }

    IEnumerator ShakeRoutine(float mag, float dur)
    {
        for (float t = 0; t < dur; t += Time.deltaTime)
        {
            float k = 1f - t / dur;
            float damp = k * k * k;
            cam.transform.position = camBase + new Vector3(
                (Random.value - 0.5f) * mag * damp,
                (Random.value - 0.5f) * mag * damp * 1.4f, 0);
            yield return null;
        }
        cam.transform.position = camBase;
        shakeCo = null;
    }
}
