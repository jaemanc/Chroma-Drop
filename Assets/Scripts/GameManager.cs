// GameManager.cs — 게임 흐름/입력 오케스트레이션 (v5 규칙).
// 씬에는 이 컴포넌트 하나(+카메라)만 있으면 됨 — BoardView/GameUI/Sfx는 런타임 생성.
//
// 입력: 터치(드래그로 고스트, 떼면 스탬프, 손가락 위로 띄운 프리뷰) + 마우스(호버 프리뷰, 클릭 스탬프).
// 회전 조작은 없다 — 조각이 나올 때 방향이 무작위로 정해진다.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
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

    /// <summary>메인 화면 씬. 홈으로 가면 이 씬을 연다.</summary>
    public const string MenuScene = "MainGame";
    /// <summary>메인 화면에서 고른 모드 (true = 타임어택). 게임 씬이 뜨면 이 모드로 바로 시작한다.
    /// null 이면 메인 화면을 거치지 않고 열린 것이므로 메인 화면으로 돌아간다.</summary>
    public static bool? LaunchTimeAttack;

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

    /// <summary>지금까지 부순 블록 수와 목표.</summary>
    public int Broken { get { return broken; } }
    public int ClearTarget { get { return stage.ClearBlocks; } }
    public bool Cleared { get { return cleared; } }

    /// <summary>표시된 칸 목표. 이 스테이지에 없으면 Total 이 0 이다.</summary>
    public int MarksTotal { get { return marksTotal; } }
    public int MarksLeft { get { return marksLeft; } }
    /// <summary>피스 개수가 곧 제한인 스테이지인가. HUD 라벨이 달라진다.</summary>
    public bool PieceLimited { get { return !taRunning && stage.PieceLimit > 0; } }
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
    System.Random pieceRng;
    Color[] palette;

    BoardView view;
    GameUI ui;
    Sfx sfx;
    Camera cam;

    int score, movesLeft, totalMoves;
    int movesUsed;              // 이 판에서 지난 수. 벽돌·오염이 이 값을 보고 움직인다
    int targetRounds;           // 타임어택에서 표시된 칸을 몇 벌째 깼나
    int rotBroken;              // 이 판에서 부순 독벽돌 수. 근원을 무너뜨리는 조건이다
    int broken;                 // 이 판에서 지금까지 부순 블록 수
    bool[,] marks;              // 표시된 칸 (좌표 목표). null 이면 이 스테이지엔 없다
    int marksTotal, marksLeft;
    bool cleared;               // 목표를 채웠는가
    bool busy, taRunning, touchActive;
    bool aiming;                // 해머·무지개가 쓸 칸을 고르는 중인가
    ShopItem aimItem;
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
        cam.transparencySortMode = TransparencySortMode.Orthographic;   // 같은 정렬 순서면 z(줄 깊이)로 겹침을 정한다
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Palette.Hex(0x8FD0F8);   // 아트 밖 여백 (아트의 하늘색)

        Leaderboard.Create();
        view = new GameObject("BoardView").AddComponent<BoardView>();
        sfx = gameObject.AddComponent<Sfx>();
        ui = GameUI.Create(this);
        view.SetFramePanels(!ui.HasPlayArt);   // 보드 판은 플레이 아트에 그려져 있다

        stageLevel = Progress.Selected;   // 지난번에 고른 스테이지에서 이어간다
        FitCamera();
        if (LaunchTimeAttack.HasValue)
        {
            timeAttack = LaunchTimeAttack.Value;
            LaunchTimeAttack = null;
            StartGame();
        }
        else GoHome();
    }


    /// <summary>보드·트레이를 플레이 아트의 자리에 정확히 맞춘다.
    /// 아트는 화면 비율에 따라 크기가 달라지므로 UI 가 잰 화면 사각형으로 카메라를 정한다 —
    /// 칸 하나가 슬롯 폭의 1/W 픽셀이 되도록 카메라 크기를, 격자 중심이 슬롯 중심에 오도록
    /// 카메라 위치를 잡는다. 패널이 꺼져 있으면 사각형이 0 이라 매 프레임 다시 잰다.</summary>
    void LayoutHud()
    {
        if (ui == null || cam == null) return;
        Rect slot;
        if (!ui.BoardSlotRect(out slot)) return;

        float cellPx = slot.width / Board.W;
        if (cellPx <= 0.01f) return;
        float half = cam.pixelHeight / cellPx * 0.5f;
        Vector2 gridC = new Vector2((Board.W - 1) / 2f, (Board.H - 1) / 2f);
        Vector2 screenC = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
        Vector2 off = (screenC - slot.center) / cellPx;      // 화면 중심이 격자 중심에서 몇 칸 떨어졌나
        var want = new Vector3(gridC.x + off.x, gridC.y + off.y, -10);

        if (Mathf.Abs(cam.orthographicSize - half) > 1e-4f) cam.orthographicSize = half;
        if ((camBase - want).sqrMagnitude > 1e-8f)
        {
            camBase = want;
            if (shakeCo == null) cam.transform.position = camBase;
        }

        // 트레이: 지금 조각은 왼쪽 큰 칸, 다음 조각은 오른쪽 작은 칸
        Rect tray;
        if (ui.TraySlotRect(out tray))
        {
            Vector2 cur = cam.ScreenToWorldPoint(new Vector3(tray.xMin + tray.width * 0.42f, tray.center.y, 10));
            Vector2 nxt = cam.ScreenToWorldPoint(new Vector3(tray.xMin + tray.width * 0.85f, tray.center.y, 10));
            var curBox = new Vector2(tray.width * 0.62f, tray.height * 0.84f) / cellPx;
            var nxtBox = new Vector2(tray.width * 0.26f, tray.height * 0.44f) / cellPx;
            if (view.SetTrayLayout(cur, curBox, nxt, nxtBox)) RefreshTray();
        }
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
        SceneManager.LoadScene(MenuScene);   // 홈 화면은 따로 만든 메인 화면 씬이다
    }

    /// <summary>홈 화면에서 선택된 difficulty/timeAttack/seed로 시작</summary>
    public void StartGame() { StartGame(Difficulty, timeAttack, seed); }

    /// <summary>클리어한 뒤 다음 스테이지로. 마지막 판이면 그 판을 다시 한다.</summary>
    public void StartNextStage()
    {
        stageLevel = Mathf.Clamp(stageLevel + 1, 1, Mathf.Max(1, StageTable.Count));
        Progress.Selected = stageLevel;
        StartGame(Difficulty, false, seed);
    }

    public void StartGame(string diff, bool ta, int seedOverride)
    {
        StopAllCoroutines();
        shakeCo = null;
        if (cam != null) cam.transform.position = camBase;
        timeAttack = ta;

        // 판을 만들기 전에 이 스테이지의 설정부터 정한다.
        // 뒤에 대입하면 보드·팔레트가 이전 스테이지 값으로 만들어진다.
        stage = ta ? StageTable.TimeAttack : StageTable.Get(stageLevel);

        int s = seedOverride == 0 ? System.Environment.TickCount : seedOverride;
        curSeed = s;
        // 블록 테마를 판마다 무작위로 고른다. 판 크기가 테마를 따르므로 판을 만들기 전에 정한다
        var theme = BlockTheme.All[new System.Random(s + 2).Next(BlockTheme.All.Length)];
        ui.SetTheme(theme);
        if (Board.W != theme.Size || Board.H != theme.Size)
        {
            Board.SetSize(theme.Size, theme.Size);
            Destroy(view.gameObject);   // 칸 배열이 이전 판 크기로 지어져 있어 새로 짓는다
            view = new GameObject("BoardView").AddComponent<BoardView>();
        }
        view.SetFramePanels(!ui.HasPlayArt);   // 테마 그림이 없으면 코드로 그린 판을 쓴다
        board = new Board(stage.ColorCount, s);
        pieceRng = new System.Random(s + 1);
        palette = Palette.Generate(stage.ColorCount, theme.Colors);

        taRunning = timeAttack;
        // 피스 제한 스테이지는 '남은 수' 가 곧 '남은 피스' 다 — 세는 것이 같으므로 카운터도 같은 것을 쓴다.
        totalMoves = movesLeft = timeAttack ? 9999 : stage.MoveBudget;
        score = 0;
        movesUsed = 0;
        targetRounds = 0;
        rotBroken = 0;
        broken = 0;
        cleared = false;
        busy = false;
        touchActive = false;

        pendingBomb = false;
        selectedSlot = -1;
        dragging = false;
        for (int i = 0; i < tray.Length; i++) tray[i] = NextPiece();
        selectedSlot = BoardView.CurrentSlot;
        current = tray[BoardView.CurrentSlot];

        // 강철을 판에 심는다. 내구도가 있어 옆을 그만큼 터뜨리면 부서진다 —
        // 안 깨지는 강철은 오염 근원뿐이다.
        if (stage.SteelCount > 0) board.SpawnSteel(stage.SteelCount, stage.SteelHp);

        BuildMarks(s);
        if (stage.HasPollution) SpawnPollutionSource(new System.Random(s + 5));

        view.Build();
        view.UseTheme(theme);
        // 오염 판의 벽돌은 전부 독벽돌이다 — 손상 단계도 그 내구도로 환산해야 맞다
        view.SetObstacleMaxHp(stage.HasPollution ? stage.PollutionHp : stage.ObstacleHp);
        view.SetSteelMaxHp(stage.SteelHp);
        view.SetMarks(marks);
        view.SetPollutionStage(stage.HasPollution);
        view.ApplySkin(Wallet.Skin);   // 상점에서 바꾼 스킨을 다음 판부터 반영
        view.SetVisible(true);
        view.Refresh(board, palette);
        ui.ShowGame();
        LayoutHud();        // 패널이 켜진 직후 자리를 맞춘다 — 첫 프레임부터 제자리에 그린다
        RefreshTray();

        Phase = GamePhase.Playing;
        if (timeAttack) taDeadline = Time.time + Mathf.Max(10, stage.Seconds);
        StartPieceTimer();
    }

    /// <summary>표시된 칸을 정한다. 강철이 올라앉은 칸은 무슨 수를 써도 못 깨므로
    /// 일반 칸으로 되돌린다 — 아니면 클리어할 수 없는 판이 나온다.</summary>
    void BuildMarks(int seed)
    {
        marks = StageTargets.Build(stage.TargetPattern, stage.TargetCount, new System.Random(seed + 3));

        if (marks != null)
        {
            var rng = new System.Random(seed + 4);
            for (int x = 0; x < Board.W; x++)
                for (int y = 0; y < Board.H; y++)
                {
                    if (!marks[x, y] || !board.IsSteel(x, y)) continue;

                    // 깨지는 강철은 치워 주고, 안 깨지는 오염 근원 위에는 표시를 걷는다.
                    // 못 깨는 칸에 목표를 걸면 영영 못 지운다.
                    if (board.GetSteelHp(x, y) > 0) board.SetTile(x, y, rng.Next(stage.ColorCount));
                    else marks[x, y] = false;
                }
        }
        marksTotal = marksLeft = StageTargets.Count(marks);

        if (!taRunning && stage.ClearBlocks <= 0 && marksTotal == 0)
            Debug.LogWarning("[stage] " + stageLevel + " 단계에 목표가 없다 — "
                           + "clearBlocks 나 targetPattern 중 하나는 있어야 클리어할 수 있다.");
    }

    /// <summary>오염 근원 하나를 심는다. 못 깨는 칸이라 표시된 칸은 피한다 —
    /// 목표 위에 앉으면 클리어할 수 없는 판이 된다.</summary>
    void SpawnPollutionSource(System.Random rng)
    {
        for (int guard = 0; guard < 500; guard++)
        {
            int x = rng.Next(Board.W), y = rng.Next(Board.H);
            if (marks != null && marks[x, y]) continue;
            if (board.GetTile(x, y) < 0) continue;      // 이미 특수 칸이면 다른 자리를 본다
            board.SetTile(x, y, Board.Steel);
            return;
        }
    }

    /// <summary>독벽돌을 충분히 부수면 근원까지 무너진다. 무너뜨리고 나면 더는 번지지 않는다.
    /// PollutionSourceHits 가 0 인 판에서는 근원이 안 부서진다 (예전 그대로).</summary>
    bool BreakPollutionSource()
    {
        if (stage.PollutionSourceHits <= 0 || rotBroken < stage.PollutionSourceHits) return false;

        int sx, sy;
        if (!FindPollutionSource(out sx, out sy)) return false;

        board.SetTile(sx, sy, Board.Empty);
        board.ApplyGravity();
        board.Refill();
        view.Refresh(board, palette);
        // ⚠ 여기서 오염 표시를 끄면 안 된다. 근원이 무너지면 더 번지지 않지만
        //    남아 있는 것들은 여전히 독블록이다 — 그림만 평범한 벽돌로 바꾸면
        //    같은 칸이 갑자기 다른 블록처럼 보인다.

        score += stage.PollutionSourceHits * TargetBonus;
        ui.ShowChainPopup(0, stage.PollutionSourceHits * TargetBonus);
        sfx.PlayWin();
        Shake(0.3f, 0.2f, true);
        rotBroken = 0;
        return true;
    }

    /// <summary>오염 근원의 지금 자리. 중력을 받아 내려오므로 좌표를 기억하지 않고 매번 찾는다.
    /// 오염 스테이지에는 못 깨는 칸이 근원 하나뿐이다.</summary>
    bool FindPollutionSource(out int sx, out int sy)
    {
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                if (board.IsSteel(x, y) && board.GetSteelHp(x, y) == 0) { sx = x; sy = y; return true; }
        sx = sy = -1;
        return false;
    }

    /// <summary>오염 덩어리 가장자리 어디로든 한 칸씩 번진다. 번진 오염은 깰 수 있는 벽돌이라
    /// 옆 칸을 터뜨려야 없어진다.
    /// 근원 옆 여덟 칸만 보면 그 여덟이 차는 순간 더는 안 번진다 — 덩어리 전체의 가장자리를 본다.</summary>
    bool SpreadPollution()
    {
        int want = Mathf.Max(1, stage.PollutionPerSpread);
        bool any = false;
        for (int i = 0; i < want; i++)
        {
            // 한 칸 채울 때마다 다시 고른다 — 안 그러면 방금 찬 칸 때문에
            // 바깥으로 나가야 할 차례인데도 안쪽 후보를 계속 쓴다
            var open = PollutionFrontier();
            if (open.Count == 0) break;
            var at = open[pieceRng.Next(open.Count)];
            board.SetObstacle(at.X, at.Y, stage.PollutionHp);
            any = true;
        }
        return any;
    }

    /// <summary>다음에 오염될 후보 칸.
    /// 숙주 버섯 바로 옆 8칸이 먼저고, 그 8칸이 다 차야 이미 번진 독블록 바깥으로 나간다.
    /// 둘을 한 통에 담고 무작위로 뽑으면 옆이 비었는데 멀리 가서 생긴다.</summary>
    /// <summary>지금 오염될 수 있는 칸. 숙주(독버섯) 바로 옆 8칸만이다 —
    /// 새로 생긴 독블록은 다시 번지지 않으므로 오염은 숙주 둘레를 넘지 않는다.
    /// 플레이어가 그 8칸을 깨면 다시 오염 대상이 되므로 숙주는 계속 압박을 준다.</summary>
    List<Point> PollutionFrontier()
    {
        var open = new List<Point>();
        var seen = new HashSet<int>();

        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                if (!board.IsSteel(x, y) || board.GetSteelHp(x, y) != 0) continue;   // 숙주만

                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (!board.InBounds(nx, ny)) continue;
                        if (board.GetTile(nx, ny) < 0) continue;   // 이미 오염됐거나 특수 칸이다
                        if (seen.Add(nx * Board.H + ny)) open.Add(new Point(nx, ny));
                    }
            }
        return open;
    }

    /// <summary>목표를 다 채웠는가. 블록 수와 표시된 칸을 둘 다 건 스테이지는 둘 다 채워야 한다.</summary>
    public bool GoalMet
    {
        get
        {
            if (stage.ClearBlocks <= 0 && marksTotal == 0) return false;   // 목표가 없는 판은 클리어도 없다
            if (stage.ClearBlocks > 0 && broken < stage.ClearBlocks) return false;
            return marksLeft <= 0;
        }
    }

    /// <summary>표시된 칸 한 벌을 다 깼을 때 주는 보너스 (칸당).</summary>
    const int TargetBonus = 120;

    /// <summary>이번에 비워진 칸 중 표시된 칸을 지운다. 한 번 깨진 칸은 다시 세지 않는다.
    /// 타임어택은 끝이 시간이라 한 벌을 다 깨면 보너스를 주고 새 자리에 다시 깔아 준다.</summary>
    void ClearMarks(List<Point> destroyed)
    {
        if (marks == null) return;
        for (int i = 0; i < destroyed.Count; i++)
        {
            var p = destroyed[i];
            if (!marks[p.X, p.Y]) continue;
            marks[p.X, p.Y] = false;
            marksLeft--;
            view.ClearMark(p.X, p.Y);
        }

        if (!taRunning || marksTotal <= 0 || marksLeft > 0) return;

        int bonus = marksTotal * TargetBonus;
        score += bonus;
        ui.ShowChainPopup(0, bonus);
        sfx.PlayItem();
        BuildMarks(curSeed + 1000 * ++targetRounds);   // 다음 벌은 다른 자리에
        view.SetMarks(marks);
    }

    void EndGame()
    {
        Phase = GamePhase.Result;
        busy = false;
        view.HideGhost();

        if (!taRunning) cleared = GoalMet;
        if (cleared) Progress.Clear(stageLevel);

        string key = BestKey(taRunning, difficulty);
        int best = PlayerPrefs.GetInt(key, 0);
        bool newBest = score > best;
        if (newBest) { PlayerPrefs.SetInt(key, score); PlayerPrefs.Save(); best = score; }

        if (newBest) sfx.PlayWin(); else sfx.PlayLose();
        ui.ShowResult(taRunning, score, best, newBest, earnedCoins, stageLevel, cleared);

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

        // 타임어택은 전체 시간이 먼저다. 그 안에서 조각마다 놓을 시간이 따로 있다.
        if (taRunning && !busy && Time.time >= taDeadline) { EndGame(); return; }
        if (!busy && Time.time >= pieceDeadline)
        {
            StartCoroutine(ExpirePiece());
            return;
        }

        ui.UpdateHud(this);
        LayoutHud();
        view.SetIdle(!busy);               // 연출 중에는 블록이 스스로 움직이지 않는다
        if (busy) { view.HideGhost(); return; }
        HandleInput();
    }

    void HandleInput()
    {
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

        if (aiming)
        {
            AimInput(w, up);
            return;
        }

        // 누르는 순간 바로 지금 블록을 집는다. 트레이를 먼저 고를 필요가 없다.
        if (down)
        {
            dragging = true;
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

        // 해머·무지개는 '어느 칸에 쓸지' 를 먼저 고른다. 같은 버튼을 다시 누르면 취소한다.
        if (it == ShopItem.Hammer || it == ShopItem.Rainbow)
        {
            if (aiming && aimItem == it) { CancelAim(); return true; }
            aiming = true;
            aimItem = it;
            dragging = false;
            view.HideGhost();
            sfx.PlayItem();
            return true;
        }

        if (it == ShopItem.Shuffle)
        {
            CancelAim();
            ShuffleBoard();
            Wallet.Use(it);
            sfx.PlayItem();
            return true;
        }

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
        StartPieceTimer();
        return true;
    }

    /// <summary>조준 취소.</summary>
    void CancelAim()
    {
        if (!aiming) return;
        aiming = false;
        view.HideAim();
        view.HideGhost();
    }

    /// <summary>해머·무지개를 쓸 칸을 고르는 중의 입력. 탭을 떼는 순간 발동한다.</summary>
    void AimInput(Vector3 w, bool up)
    {
        // 손가락이 조각을 가리지 않게 띄우는 보정은 여기선 쓰지 않는다 — 정확히 찍은 칸을 쓴다
        int tx = Mathf.RoundToInt(w.x), ty = Mathf.RoundToInt(w.y);
        bool ok = board.InBounds(tx, ty) && board.GetTile(tx, ty) >= 0;   // 색 블록만 고를 수 있다

        var cells = new List<Point>();
        if (ok)
        {
            if (aimItem == ShopItem.Rainbow)
            {
                int c = board.GetTile(tx, ty);
                for (int x = 0; x < Board.W; x++)
                    for (int y = 0; y < Board.H; y++)
                        if (board.GetTile(x, y) == c) cells.Add(new Point(x, y));
            }
            else cells.Add(new Point(tx, ty));
        }

        view.ShowAim(aimItem == ShopItem.Hammer, tx, ty, ok, cells);
        if (!up || !ok) return;

        aiming = false;
        view.HideAim();
        Wallet.Use(aimItem);
        StartCoroutine(aimItem == ShopItem.Rainbow ? UseRainbow(cells) : UseHammer(cells));
    }

    /// <summary>해머 — 고른 칸 하나를 바로 부순다. 수는 쓰지 않는다.</summary>
    IEnumerator UseHammer(List<Point> cells)
    {
        sfx.PlayItem();
        yield return BreakCells(cells, false);
    }

    /// <summary>무지개 — 고른 색을 판에서 전부 지운다. 무지개 가루가 튄다.</summary>
    IEnumerator UseRainbow(List<Point> cells)
    {
        sfx.PlayItem();
        view.RainbowBurst(cells);
        yield return BreakCells(cells, true);
    }

    /// <summary>지금 판을 그대로 복사한다 (낙하 연출의 '전' 상태).</summary>
    int[,] Snapshot()
    {
        var v = new int[Board.W, Board.H];
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++) v[x, y] = board.GetTile(x, y);
        return v;
    }

    /// <summary>부수기 전 판(before)과 부순 칸(removed)을 알면 어느 블록이 얼마나
    /// 내려앉았는지 계산할 수 있다. 살아남은 블록은 순서를 지키며 내려오므로
    /// 아래에서부터 1:1 로 짝지으면 된다.
    ///
    /// ⚠ 지금 판만 두 번 비교하면 안 된다 — 채워 넣기(Refill)까지 끝난 뒤라
    /// 칸 수가 같아져 "아무도 안 떨어졌다"고 나온다.</summary>
    IEnumerator FallFromSnapshot(int[,] before, List<Point> removed)
    {
        var cells = new List<Point>();
        var drops = new List<float>();
        var newCells = new List<Point>();

        var gone = new HashSet<int>();
        if (removed != null) foreach (var p in removed) gone.Add(p.X * 1000 + p.Y);

        for (int x = 0; x < Board.W; x++)
        {
            var was = new List<int>();
            var now = new List<int>();
            for (int y = 0; y < Board.H; y++)
            {
                if (before[x, y] != Board.Empty && !gone.Contains(x * 1000 + y)) was.Add(y);
                if (board.GetTile(x, y) != Board.Empty) now.Add(y);
            }
            for (int i = 0; i < now.Count; i++)
            {
                int ny = now[i];
                float drop = i < was.Count ? was[i] - ny : (Board.H + (i - was.Count)) - ny;
                if (drop <= 0.001f) continue;
                cells.Add(new Point(x, ny));
                drops.Add(drop);
                if (i >= was.Count) newCells.Add(new Point(x, ny));
            }
        }
        if (cells.Count == 0) yield break;

        yield return view.FallIn(cells, drops, fallTime, 0.02f);
        float impact = Mathf.Clamp01(view.LastMaxDrop / 6f);
        if (impact > 0.1f) Shake(Mathf.Lerp(0.158f, 0.238f, impact), 0.15f, true);
        yield return view.LandCells(newCells, landTime);
    }

    /// <summary>고른 칸들을 부순다 (해머·무지개 공용). 부순 뒤 생긴 매칭은 평소 연쇄로 이어진다.</summary>
    IEnumerator BreakCells(List<Point> cells, bool bigHit)
    {
        if (cells == null || cells.Count == 0) yield break;
        busy = true;
        view.HideGhost();

        var colors = new List<Color>(cells.Count);
        foreach (var p in cells)
        {
            int ci = board.GetTile(p.X, p.Y);
            colors.Add(ci >= 0 && ci < palette.Length ? palette[ci] : Color.white);
        }

        var before = Snapshot();
        view.FlashCells(cells, DirectFlash);
        view.Burst(cells, colors, bigHit ? 1.9f : 1.2f, DirectFlash);
        sfx.PlayDestroy(1);
        Shake(bigHit ? 0.42f : 0.24f, 0.18f, true);
        yield return new WaitForSeconds(destroyFlash);

        int broke = 0;
        foreach (var p in cells)
        {
            if (board.GetTile(p.X, p.Y) == Board.Empty) continue;
            board.SetTile(p.X, p.Y, Board.Empty);
            broke++;
        }
        score += broke * Board.BaseTileScore;
        broken += broke;
        ClearMarks(cells);

        board.ApplyGravity();
        board.Refill();
        view.Refresh(board, palette);
        yield return FallFromSnapshot(before, cells);

        // 무너진 뒤 새로 생긴 매칭은 스탬프와 똑같이 연쇄로 처리한다
        var v2 = Snapshot();
        var after = board.Resolve();
        if (after.Destroyed.Count > 0)
        {
            yield return DestroyWaves(after, v2);
            score += after.ScoreGained;
            broken += after.TilesDestroyed;
            ClearMarks(after.Destroyed);
            if (after.MaxChain >= 2) ui.ShowChainPopup(after.MaxChain, after.ScoreGained);
            view.Refresh(board, palette);
        }

        busy = false;
        if (!taRunning && GoalMet) { cleared = true; EndGame(); }
    }

    /// <summary>셔플 — 색 블록의 자리만 섞는다. 벽돌·강철·별 표시는 그대로 둔다.
    /// 섞은 결과가 바로 매칭이 되면 공짜 점수가 되므로 다시 섞는다.</summary>
    void ShuffleBoard()
    {
        var spots = new List<Point>();
        var cols = new List<int>();
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                int t = board.GetTile(x, y);
                if (t < 0) continue;                 // 벽돌·강철은 자리를 지킨다
                spots.Add(new Point(x, y));
                cols.Add(t);
            }
        if (spots.Count < 2) return;

        for (int guard = 0; guard < 40; guard++)
        {
            for (int i = cols.Count - 1; i > 0; i--)
            {
                int j = pieceRng.Next(i + 1);
                int tmp = cols[i]; cols[i] = cols[j]; cols[j] = tmp;
            }
            for (int i = 0; i < spots.Count; i++) board.SetTile(spots[i].X, spots[i].Y, cols[i]);
            if (board.FindSquares().Count == 0) break;
        }
        view.Refresh(board, palette);
        Shake(0.2f, 0.2f);
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

        int rotBefore = stage.HasPollution ? board.CountObstacles() : 0;

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
        yield return view.StampCells(stamped, palette[current.Color], current.Color, stampTime, () =>
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
        broken += result.TilesDestroyed;
        ClearMarks(result.Destroyed);

        if (stage.HasPollution)
        {
            rotBroken += Mathf.Max(0, rotBefore - board.CountObstacles());
            if (BreakPollutionSource()) yield return new WaitForSeconds(destroyFlash);
        }

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
        movesUsed++;
        {
            int add = stage.ObstaclesAfterMove(movesUsed, totalMoves);
            if (add > 0 && board.SpawnObstacles(add, stage.ObstacleHp) > 0) sfx.PlayItem();

            // 오염은 근원 옆으로 번진다. 블록을 깼든 못 깼든 상관없이 그 턴에 번진다.
            if (stage.HasPollution && movesUsed % stage.PollutionEvery == 0 && SpreadPollution()) sfx.PlayExpire();
        }
        view.Refresh(board, palette);

        AdvanceTray();
        dragging = false;

        busy = false;
        if (!taRunning)
        {
            // 목표를 채웠으면 수가 남아도 그 자리에서 끝난다
            if (GoalMet) { cleared = true; EndGame(); yield break; }
            if (movesLeft <= 0) { EndGame(); yield break; }
        }
        StartPieceTimer();
    }

    /// <summary>새 조각 하나. 돌릴 방법이 없으므로 나올 때 방향을 무작위로 정한다 —
    /// 어느 방향으로 나오느냐가 그 조각을 어디에 쓸지를 정한다.</summary>
    Piece NextPiece()
    {
        var p = Piece.CreateRandom(pieceRng, stage.ColorCount);
        for (int r = pieceRng.Next(4); r > 0; r--) p = p.Rotated();
        return p;
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
        tray[tray.Length - 1] = NextPiece();
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

        // 오염은 한 턴도 쉬지 않는다. 조각을 흘려보낸 턴에도 번진다.
        movesUsed++;
        if (stage.HasPollution && SpreadPollution())
        {
            view.Refresh(board, palette);
            sfx.PlayExpire();
        }
        AdvanceTray();

        busy = false;
        if (!taRunning && movesLeft <= 0) EndGame();
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
