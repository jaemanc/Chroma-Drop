// GameSmokeTests.cs — PlayMode 실행 검증.
// 실제 Unity 런타임에서 게임을 부팅·조작해 표현 계층 전체(뷰/UI/사운드/코루틴)를 확인한다.
// 실행: 에디터 Test Runner(PlayMode) 또는
//   Unity -batchmode -runTests -testPlatform PlayMode -projectPath .

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ColorMatcher.Core;

public class GameSmokeTests
{
    GameManager gm;

    // 수 제한은 스테이지 설정이 갖는다
    static int Moves { get { return StageTable.Get(1).Moves; } }

    GameManager NewGm()
    {
        var go = new GameObject("GM_under_test");
        var g = go.AddComponent<GameManager>();
        // 테스트 가속: 연출 대기 최소화
        g.stampTime = g.destroyFlash = g.fallTime = 0.02f;
        g.chainStep = g.chainFall = g.landTime = 0.01f;
        return g;
    }

    [TearDown]
    public void Cleanup()
    {
        // GameUI / BoardView 는 GameManager 가 따로 만드는 오브젝트라 같이 지워야 한다.
        // 안 그러면 다음 테스트가 이전 화면의 버튼을 잡고, 그 화면은 이미 파괴된
        // GameManager 를 붙들고 있어 MissingReferenceException 이 난다.
        if (gm != null) Object.DestroyImmediate(gm.gameObject);
        gm = null;
        foreach (var n in new[] { "GameUI", "BoardView" })
            for (var g = GameObject.Find(n); g != null; g = GameObject.Find(n))
                Object.DestroyImmediate(g);
    }

    [UnityTest]
    public IEnumerator BootsToHome()
    {
        gm = NewGm();
        yield return null;
        Assert.AreEqual(GamePhase.Home, gm.Phase);
    }

    [UnityTest]
    public IEnumerator StampConsumesMoveAndKeepsBoardValid()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 12345);
        yield return null;
        Assert.AreEqual(GamePhase.Playing, gm.Phase);
        Assert.AreEqual(Moves, gm.MovesLeft);

        gm.RotateCurrent(); // 회전이 예외 없이 동작
        Assert.IsTrue(gm.TryStamp(3, 3), "고정 시드에서 (3,3) 배치는 항상 가능해야 함");

        float t0 = Time.realtimeSinceStartup;
        while (gm.Busy && Time.realtimeSinceStartup - t0 < 10) yield return null;
        Assert.IsFalse(gm.Busy, "스탬프 연출이 10초 안에 끝나야 함");
        Assert.AreEqual(Moves - 1, gm.MovesLeft);

        // 코어 불변식: 해소 후 보드에 빈 칸/잔여 매칭 없음
        var b = gm.BoardRef;
        Assert.AreEqual(0, b.FindSquares().Count, "해소 후 잔여 매칭 없음");
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                Assert.AreNotEqual(Board.Empty, b.GetTile(x, y), "해소 후 빈 칸 없음");
    }

    [UnityTest]
    public IEnumerator 제한시간이_지나면_조각이_버려지고_기회를_쓴다()
    {
        gm = NewGm();
        gm.stageLevel = StageTable.Count;   // 제한시간이 가장 짧은 스테이지
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 4321);
        yield return null;

        int moves = gm.MovesLeft;
        var before = gm.TraySlot(0);
        Assert.IsNotNull(before, "트레이가 비어 있다");
        Assert.Greater(gm.PieceTimerFrac, 0.9f, "시작 직후엔 제한시간이 거의 남아 있어야 함");

        // 아무것도 놓지 않고 제한시간이 지나기를 기다린다
        float t0 = Time.realtimeSinceStartup;
        while (gm.MovesLeft == moves && Time.realtimeSinceStartup - t0 < 20f)
            yield return null;

        Assert.AreEqual(moves - 1, gm.MovesLeft, "만료되면 기회를 하나 쓴다");
        Assert.AreNotSame(before, gm.TraySlot(0), "만료되면 트레이를 새로 뽑는다");
        Assert.AreEqual(0, gm.SelectedSlot, "만료 뒤에도 지금 블록이 잡혀 있다");
        Assert.Greater(gm.PieceTimerFrac, 0.5f, "새 트레이는 제한시간이 다시 채워진다");
    }

    [UnityTest]
    public IEnumerator 홈_시작버튼을_눌렀다_떼면_게임이_시작된다()
    {
        gm = NewGm();
        yield return null;   // 홈 화면 구성

        var start = GameObject.Find("start");
        Assert.IsNotNull(start, "시작 버튼을 찾지 못했다");
        var rt = (RectTransform)start.transform;
        var before = rt.anchoredPosition;

        var press = start.GetComponent<UiPressImage>();
        var ev = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
        press.OnPointerDown(ev);
        yield return null;
        // 눌린 동안에도 원래 자리 근처에 머물러야 한다 (예전엔 (0,0) 으로 튀었다)
        Assert.Less(Vector2.Distance(rt.anchoredPosition, before), 20f,
                    "누르는 순간 버튼이 제자리를 벗어났다");

        press.OnPointerUp(ev);
        start.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        yield return null;
        Assert.AreEqual(GamePhase.Playing, gm.Phase, "시작 버튼이 게임을 시작하지 못했다");
    }

    [UnityTest]
    public IEnumerator 아이템은_보유량이_있어야_쓰이고_기회를_안_쓴다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 555);
        yield return null;

        // 없으면 못 쓴다
        while (Wallet.Count(ShopItem.BombPiece) > 0) Wallet.Use(ShopItem.BombPiece);
        Assert.IsFalse(gm.UseItem(ShopItem.BombPiece), "보유량 0 인데 사용됐다");

        // 폭탄 조각: 2x2 로 바뀌고 기회는 그대로
        Wallet.Add(ShopItem.BombPiece, 1);
        int moves = gm.MovesLeft;
        Assert.IsTrue(gm.UseItem(ShopItem.BombPiece), "폭탄 조각 사용 실패");
        Assert.AreEqual(1, gm.CurrentPiece.Cells.Count, "1칸 폭탄 조각이 아니다");
        Assert.AreEqual(moves, gm.MovesLeft, "아이템이 기회를 소모했다");
        Assert.AreEqual(0, Wallet.Count(ShopItem.BombPiece), "보유량이 줄지 않았다");
    }

    // 회귀: 폭탄을 장전하면 조준할 시간이 새로 주어져야 한다.
    // 안 그러면 남은 시간이 짧을 때 폭탄이 그냥 만료되고, 한 번 더 눌러야 하는 것처럼 보였다.
    [UnityTest]
    public IEnumerator 폭탄을_장전하면_조각_타이머가_다시_시작된다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 313);
        yield return null;

        // 시간이 거의 다 흐른 상태를 만든다
        float full = gm.PieceTimerFrac;
        while (gm.PieceTimerFrac > full * 0.4f) yield return null;
        float low = gm.PieceTimerFrac;

        Wallet.Add(ShopItem.BombPiece, 1);
        Assert.IsTrue(gm.UseItem(ShopItem.BombPiece));
        Assert.Greater(gm.PieceTimerFrac, low, "장전했는데 타이머가 그대로다");
        Assert.IsTrue(gm.BombArmed, "폭탄이 장전되지 않았다");
    }

    // 회귀: 조각이 만료되면 폭탄 예약도 사라져야 한다. 남으면 다음 일반 조각이 폭탄이 된다.
    [UnityTest]
    public IEnumerator 조각이_만료되면_폭탄_장전도_풀린다()
    {
        gm = NewGm();
        gm.stageLevel = StageTable.Count;   // 제한시간이 가장 짧은 스테이지
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 767);
        yield return null;

        Wallet.Add(ShopItem.BombPiece, 1);
        Assert.IsTrue(gm.UseItem(ShopItem.BombPiece));
        Assert.IsTrue(gm.BombArmed);

        // 손대지 않고 만료를 기다린다
        float t0 = Time.realtimeSinceStartup;
        while (gm.BombArmed && Time.realtimeSinceStartup - t0 < 20) yield return null;
        Assert.IsFalse(gm.BombArmed, "만료됐는데 폭탄 예약이 남아 있다");
        Assert.AreEqual(0, gm.SelectedSlot, "만료 뒤에도 지금 블록이 잡혀 있다");
    }

    [UnityTest]
    public IEnumerator 폭탄_조각을_찍으면_주변이_넓게_터진다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 8080);
        yield return null;

        Wallet.Add(ShopItem.BombPiece, 1);
        Assert.IsTrue(gm.UseItem(ShopItem.BombPiece));

        int before = gm.Score;
        Assert.IsTrue(gm.TryStamp(5, 5), "폭탄 조각을 놓지 못했다");
        float t0 = Time.realtimeSinceStartup;
        while (gm.Busy && Time.realtimeSinceStartup - t0 < 20) yield return null;

        // 1칸으론 매칭이 안 생긴다. 점수가 났다면 폭탄이 스스로 터진 것이다.
        Assert.Greater(gm.Score - before, 200, "폭탄이 발동하지 않은 것으로 보인다");
    }

    [UnityTest]
    public IEnumerator 게임이_끝나면_점수만큼_코인을_준다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 777);
        yield return null;

        int before = Wallet.Coins;
        var rng = new System.Random(3);
        float t0 = Time.realtimeSinceStartup;
        while (gm.Phase == GamePhase.Playing && Time.realtimeSinceStartup - t0 < 90)
        {
            if (!gm.Busy) gm.TryStamp(rng.Next(11), rng.Next(11));
            yield return null;
        }
        Assert.AreEqual(GamePhase.Result, gm.Phase);
        Assert.AreEqual(Rules.CoinsFor(gm.Score), gm.EarnedCoins, "지급 코인이 환산값과 다르다");
        Assert.AreEqual(before + gm.EarnedCoins, Wallet.Coins, "지갑에 반영되지 않았다");
    }

    [UnityTest]
    public IEnumerator OutOfBoundsStampRejected()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 999);
        yield return null;
        Assert.IsFalse(gm.TryStamp(15, 15), "경계 밖 배치는 거부");
        Assert.AreEqual(Moves, gm.MovesLeft);
    }

    [UnityTest]
    public IEnumerator FullRandomGameReachesResult()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 777);
        yield return null;

        // 낙하 연출은 실시간 시차 대기(~0.3초/수)를 쓰므로 가드는 프레임 수가 아니라 실시간 기준.
        var rng = new System.Random(1);
        float t0 = Time.realtimeSinceStartup;
        while (gm.Phase == GamePhase.Playing && Time.realtimeSinceStartup - t0 < 90)
        {
            // 앵커 0..12는 모든 조각(최대 폭 4)이 들어가므로 busy만 아니면 항상 성공
            if (!gm.Busy) gm.TryStamp(rng.Next(13), rng.Next(13));
            yield return null;
        }
        Assert.AreEqual(GamePhase.Result, gm.Phase, "제한 수 안에 게임이 종료(성공/실패)돼야 함");
    }

    [UnityTest]
    public IEnumerator TimeAttackRunsAndHudUpdates()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, true, 4242);
        yield return null;
        Assert.IsTrue(gm.TimeAttackMode);
        Assert.Greater(gm.TimeLeftSec, 55f);
        Assert.IsTrue(gm.TryStamp(5, 5));
        float t0 = Time.realtimeSinceStartup;
        while (gm.Busy && Time.realtimeSinceStartup - t0 < 10) yield return null;
        Assert.AreEqual(GamePhase.Playing, gm.Phase, "타임어택은 수 소진으로 끝나지 않음");
    }

    // 회귀: 블록이 터지면 위에 있던 것이 실제로 '떨어져야' 한다.
    // 터진 칸을 시각 상태에서 안 비우면 낙하 전/후가 같은 것으로 보여
    // 이동 거리가 전부 0 이 되고, 애니메이션 없이 칸만 갈아끼우는 것처럼 보인다.
    [UnityTest]
    public IEnumerator 소거되면_위_블록이_실제로_떨어진다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 4242);
        yield return null;

        var view = Object.FindObjectOfType<BoardView>();
        Assert.IsNotNull(view, "BoardView 를 찾지 못했다");

        // 확실히 터지도록 2x2 한 귀퉁이만 남겨 둔다
        var board = gm.BoardRef;
        int c = board.GetTile(0, 0);
        board.SetTile(5, 5, c); board.SetTile(6, 5, c); board.SetTile(5, 6, c);

        Assert.IsTrue(gm.TryStamp(6, 6), "놓지 못했다");
        float t0 = Time.realtimeSinceStartup;
        while (gm.Busy && Time.realtimeSinceStartup - t0 < 20) yield return null;

        Assert.Greater(view.LastMaxDrop, 0f,
                       "아무 블록도 떨어지지 않았다 — 낙하 연출이 사라진 상태다");
    }

    [UnityTest]
    public IEnumerator 트레이는_지금_블록과_다음_블록을_들고_있다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 909);
        yield return null;

        var now = gm.TraySlot(0);
        var next = gm.TraySlot(1);
        Assert.IsNotNull(now, "지금 블록이 없다");
        Assert.IsNotNull(next, "다음 블록이 없다");
        Assert.AreSame(now, gm.CurrentPiece, "지금 블록이 바로 잡혀 있어야 한다");
        Assert.AreEqual(0, gm.SelectedSlot);

        // 놓으면 다음 블록이 지금 자리로 당겨진다
        Assert.IsTrue(gm.TryStamp(5, 5), "놓지 못했다");
        float t0 = Time.realtimeSinceStartup;
        while (gm.Busy && Time.realtimeSinceStartup - t0 < 20) yield return null;

        Assert.AreSame(next, gm.TraySlot(0), "다음 블록이 지금 자리로 안 왔다");
        Assert.IsNotNull(gm.TraySlot(1), "새 다음 블록이 안 채워졌다");
        Assert.AreSame(gm.TraySlot(0), gm.CurrentPiece, "놓은 뒤에도 지금 블록이 잡혀 있어야 한다");
    }

    // 회귀: 폭탄이 이미 장전돼 있으면 또 쓰이면 안 된다.
    // 안 막으면 버튼을 누를 때마다 보유량이 계속 깎인다.
    [UnityTest]
    public IEnumerator 폭탄은_한_번만_장전되고_중복_차감되지_않는다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 515);
        yield return null;

        while (Wallet.Count(ShopItem.BombPiece) > 0) Wallet.Use(ShopItem.BombPiece);
        Wallet.Add(ShopItem.BombPiece, 3);

        Assert.IsTrue(gm.UseItem(ShopItem.BombPiece), "첫 장전이 안 됐다");
        Assert.AreEqual(2, Wallet.Count(ShopItem.BombPiece), "하나만 소모해야 한다");

        for (int i = 0; i < 5; i++)
            Assert.IsFalse(gm.UseItem(ShopItem.BombPiece), "이미 장전됐는데 또 쓰였다");

        Assert.AreEqual(2, Wallet.Count(ShopItem.BombPiece), "중복으로 차감됐다");
        Assert.IsTrue(gm.BombArmed);
    }

    // 아무것도 안 터지는 수를 두면 벌칙으로 벽돌이 하나 생긴다
    [UnityTest]
    public IEnumerator 아무것도_안_터지면_벽돌이_생긴다()
    {
        gm = NewGm();
        gm.stageLevel = FindLevelWithPenalty();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 2468);
        yield return null;
        Assert.IsTrue(gm.Stage.PenaltyObstacle, "벌칙 규칙이 켜진 스테이지를 못 찾았다");

        var board = gm.BoardRef;
        int before = board.CountObstacles();

        // 판을 바둑판으로 만들어 어디에 놓아도 2x2 가 안 생기게 한다
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                board.SetTile(x, y, (x + y) % 2);

        Assert.IsTrue(gm.TryStamp(5, 5), "놓지 못했다");
        float t0 = Time.realtimeSinceStartup;
        while (gm.Busy && Time.realtimeSinceStartup - t0 < 20) yield return null;

        Assert.Greater(board.CountObstacles(), before, "안 터졌는데 벽돌이 안 생겼다");
    }

    // 회전 버튼을 없앴으므로 RotateCurrent 가 여전히 조각만 바꾸는지 지킨다
    [UnityTest]
    public IEnumerator 회전은_조각만_바꾸고_보드는_그대로다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 313);
        yield return null;

        var board = gm.BoardRef;
        var before = new int[Board.W, Board.H];
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++) before[x, y] = board.GetTile(x, y);

        for (int r = 0; r < 4; r++) { gm.RotateCurrent(); yield return null; }

        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                Assert.AreEqual(before[x, y], board.GetTile(x, y), "회전이 보드를 바꿨다");

        // 돌린 모양이 트레이에도 반영돼야 한다
        Assert.AreSame(gm.CurrentPiece, gm.TraySlot(0), "트레이가 돌린 조각을 안 들고 있다");
    }

    // 타이머와 아이템 줄은 보드 판 바깥에 있어야 한다.
    // 프로토 좌표로 고정하면 화면 비율이 달라질 때 판 위로 올라탄다 —
    // 그래서 월드 좌표를 따라가게 해 뒀고, 그게 실제로 지켜지는지 화면 좌표로 확인한다.
    [UnityTest]
    public IEnumerator 타이머와_아이템_줄이_보드를_덮지_않는다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 777);
        yield return null;
        yield return null;

        var cam = Camera.main;
        Assert.IsNotNull(cam);
        var ui = Object.FindObjectOfType<GameUI>();

        // 보드 판의 위·아래 끝 (칸 범위보다 여백·테두리만큼 넓다)
        const float PanelEdge = 0.85f;
        float boardTopY = cam.WorldToScreenPoint(new Vector3(0, (Board.H - 1) + PanelEdge, 0)).y;
        float boardBottomY = cam.WorldToScreenPoint(new Vector3(0, -PanelEdge, 0)).y;
        float trayTopY = cam.WorldToScreenPoint(
            new Vector3(0, BoardView.TrayY + BoardView.TrayRadius, 0)).y;

        var timer = FindRect(ui.gameObject, "timerrow");
        var items = FindRect(ui.gameObject, "itemrow");
        Assert.IsNotNull(timer, "타이머 줄이 없다");
        Assert.IsNotNull(items, "아이템 줄이 없다");

        Assert.GreaterOrEqual(Bottom(timer), boardTopY, "타이머가 보드를 덮는다");

        // 점수·남은 수 카드가 타이머 바와 겹치면 안 된다
        var scoreCard = FindRect(ui.gameObject, "statscore");
        var movesCard = FindRect(ui.gameObject, "statmoves");
        Assert.IsNotNull(scoreCard); Assert.IsNotNull(movesCard);
        Assert.GreaterOrEqual(Bottom(scoreCard), Top(timer), "점수 카드가 타이머와 겹친다");
        Assert.GreaterOrEqual(Bottom(movesCard), Top(timer), "남은 수 카드가 타이머와 겹친다");
        Assert.LessOrEqual(Top(items), boardBottomY, "아이템 줄이 보드를 덮는다");
        Assert.GreaterOrEqual(Bottom(items), trayTopY, "아이템 줄이 트레이를 덮는다");
    }

    /// <summary>벌칙 벽돌 규칙이 켜진 첫 스테이지.</summary>
    static int FindLevelWithPenalty()
    {
        for (int lv = 1; lv <= StageTable.Count; lv++)
            if (StageTable.Get(lv).PenaltyObstacle) return lv;
        return StageTable.Count;
    }

    static RectTransform FindRect(GameObject root, string name)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == name) return rt;
        return null;
    }
    static readonly Vector3[] corners = new Vector3[4];
    static float Bottom(RectTransform r) { r.GetWorldCorners(corners); return corners[0].y; }
    static float Top(RectTransform r) { r.GetWorldCorners(corners); return corners[1].y; }

    // 목표만큼 부수면 수가 남아도 클리어되고 다음 스테이지가 열린다
    [UnityTest]
    public IEnumerator 목표를_채우면_클리어되고_다음_스테이지가_열린다()
    {
        Progress.ResetAll();
        gm = NewGm();
        gm.stageLevel = 1;
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 1357);
        yield return null;

        Assert.AreEqual(10, gm.ClearTarget, "1스테이지 목표가 설정과 다르다");
        Assert.AreEqual(0, gm.Broken);
        Assert.IsFalse(gm.Cleared);

        // 큰 정사각형이 한 번에 터지도록 판을 깔아 목표를 넘긴다
        var board = gm.BoardRef;
        for (int x = 3; x <= 7; x++)
            for (int y = 3; y <= 7; y++) board.SetTile(x, y, 1);

        Assert.IsTrue(gm.TryStamp(5, 5), "놓지 못했다");
        float t0 = Time.realtimeSinceStartup;
        while (gm.Busy && Time.realtimeSinceStartup - t0 < 20) yield return null;
        yield return null;

        Assert.GreaterOrEqual(gm.Broken, gm.ClearTarget, "목표만큼 안 부쉈다");
        Assert.IsTrue(gm.Cleared, "클리어 처리가 안 됐다");
        Assert.AreEqual(2, Progress.Unlocked, "다음 스테이지가 안 열렸다");

        Progress.ResetAll();
    }

    // 5스테이지마다 난이도 축이 실제로 올라간다
    [UnityTest]
    public IEnumerator 스테이지가_오를수록_어려워진다()
    {
        Assert.Greater(StageTable.Count, 1, "스테이지 설정이 없다");

        var prev = StageTable.Get(1);
        for (int lv = 2; lv <= StageTable.Count; lv++)
        {
            var cur = StageTable.Get(lv);
            Assert.Greater(cur.ClearBlocks, prev.ClearBlocks, lv + "판 목표가 안 늘었다");
            Assert.LessOrEqual(cur.Moves, prev.Moves, lv + "판 수가 늘었다");
            Assert.LessOrEqual(cur.PieceTimeMaxMs, prev.PieceTimeMaxMs, lv + "판 시간이 늘었다");
            Assert.GreaterOrEqual(cur.SteelCount, prev.SteelCount, lv + "판 강철이 줄었다");
            prev = cur;
        }

        // 1~5 는 벽돌 없음, 11 부터 못 깨는 강철이 등장한다
        for (int lv = 1; lv <= 5; lv++)
            Assert.AreEqual(0, StageTable.Get(lv).ObstacleFromMove, lv + "판에 벽돌이 있다");
        Assert.AreEqual(0, StageTable.Get(10).SteelCount, "10판에 강철이 있다");
        Assert.Greater(StageTable.Get(11).SteelCount, 0, "11판에 강철이 없다");
        yield return null;
    }

    // 안 깬 스테이지는 고를 수 없고, 진행은 앱을 껐다 켜도 남는다
    [UnityTest]
    public IEnumerator 깬_스테이지까지만_고를_수_있다()
    {
        Progress.ResetAll();
        Assert.AreEqual(1, Progress.Unlocked, "처음엔 1스테이지만 열려 있다");
        Assert.AreEqual(1, Progress.Selected);

        // 안 열린 판은 고를 수 없다
        Progress.Selected = 5;
        Assert.AreEqual(1, Progress.Selected, "안 깬 스테이지가 선택됐다");

        Progress.Clear(1);
        Assert.AreEqual(2, Progress.Unlocked, "클리어했는데 다음이 안 열렸다");
        Progress.Selected = 2;
        Assert.AreEqual(2, Progress.Selected);

        // 지난 판으로 되돌아가는 것은 된다
        Progress.Selected = 1;
        Assert.AreEqual(1, Progress.Selected);

        // 이미 깬 판을 또 깨도 진행이 뒤로 가지 않는다
        Progress.Clear(1);
        Assert.AreEqual(2, Progress.Unlocked, "해금이 뒤로 갔다");

        // PlayerPrefs 에 남으므로 새 인스턴스에서도 그대로 읽힌다
        PlayerPrefs.Save();
        Assert.AreEqual(2, Progress.Unlocked, "저장된 진행이 안 읽힌다");

        Progress.ResetAll();
        yield return null;
    }

    // 클리어하면 NEXT 버튼이 뜨고, 실패하면 안 뜬다
    [UnityTest]
    public IEnumerator 클리어하면_NEXT_버튼이_뜬다()
    {
        gm = NewGm();
        yield return null;
        var ui = Object.FindObjectOfType<GameUI>();

        ui.ShowResult(false, 100, 0, false, 0, 3, true);
        yield return null;
        var next = FindRect(ui.gameObject, "rnext");
        Assert.IsNotNull(next, "NEXT 버튼이 없다");
        Assert.IsTrue(next.gameObject.activeSelf, "클리어했는데 NEXT 가 안 뜬다");

        ui.ShowResult(false, 100, 0, false, 0, 3, false);
        yield return null;
        Assert.IsFalse(next.gameObject.activeSelf, "실패했는데 NEXT 가 뜬다");

        // 세 버튼이 서로 겹치지 않아야 한다
        ui.ShowResult(false, 100, 0, false, 0, 3, true);
        yield return null;
        var retry = FindRect(ui.gameObject, "retry");
        var home = FindRect(ui.gameObject, "rhome");
        Assert.LessOrEqual(Right(next), Left(retry), "NEXT 와 RETRY 가 겹친다");
        Assert.LessOrEqual(Right(retry), Left(home), "RETRY 와 HOME 이 겹친다");
    }

    static float Left(RectTransform r) { r.GetWorldCorners(corners); return corners[0].x; }
    static float Right(RectTransform r) { r.GetWorldCorners(corners); return corners[2].x; }
}
