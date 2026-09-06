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

    // 수 제한은 스테이지 설정이 갖는다 (피스 제한 스테이지면 그 값이 대신한다)
    static int Moves { get { return StageTable.Get(1).MoveBudget; } }

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

    // 회전 조작을 없앴으므로 방향은 조각이 나올 때 정해진다.
    // 같은 모양이 늘 같은 방향으로만 나오면 회전을 없앤 의미가 사라진다.
    [UnityTest]
    public IEnumerator 조각은_무작위_방향으로_나온다()
    {
        gm = NewGm();
        yield return null;

        var seen = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
        for (int seed = 1; seed <= 40; seed++)
        {
            gm.StartGame(GameManager.Difficulty, false, seed);
            yield return null;

            for (int slot = 0; slot < BoardView.TraySlots; slot++)
            {
                var p = gm.TraySlot(slot);
                if (p == null) continue;
                if (!seen.ContainsKey(p.Name)) seen[p.Name] = new System.Collections.Generic.HashSet<string>();
                seen[p.Name].Add(Shape(p));
            }
        }

        int turned = 0;
        foreach (var kv in seen) if (kv.Value.Count > 1) turned++;
        Assert.Greater(turned, 0, "어떤 조각도 두 방향으로 나오지 않는다 — 방향이 안 섞였다");
    }

    /// <summary>조각의 칸 배치를 문자열 하나로. 같은 모양·같은 방향이면 같은 문자열이다.</summary>
    static string Shape(Piece p)
    {
        var cells = new System.Collections.Generic.List<string>();
        foreach (var c in p.Cells) cells.Add(c.X + "," + c.Y);
        cells.Sort(System.StringComparer.Ordinal);
        return string.Join(" ", cells.ToArray());
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

        Assert.AreEqual(StageTable.Get(1).ClearBlocks, gm.ClearTarget, "1스테이지 목표가 설정과 다르다");
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

        // 스테이지마다 종류가 달라지므로 모든 축이 매판 오르지는 않는다.
        // 좌표 스테이지는 블록을 안 세고(clearBlocks 0) 수를 넉넉히 주며,
        // 강철 스테이지는 그 판만 강철이 솟는다. 그래서 비교는 '같은 종류끼리' 한다.
        var prev = StageTable.Get(1);
        var prevCount = StageTable.Get(1);   // 마지막으로 본 '블록을 세는 판'
        for (int lv = 2; lv <= StageTable.Count; lv++)
        {
            var cur = StageTable.Get(lv);
            // 좌표 목표가 있는 판은 무늬를 지우는 데 수가 더 드니 따로 논다 — 여기선 뺀다.
            if (cur.ClearBlocks > 0 && !cur.HasCellGoal)
            {
                Assert.Greater(cur.ClearBlocks, prevCount.ClearBlocks, lv + "판 목표가 안 늘었다");
                // 난이도는 '한 수에 깨야 하는 양' 이 정한다. 수를 계속 줄이는 것으로는
                // 100판까지 못 간다 — 한 수에 깰 수 있는 양에 한계가 있기 때문이다.
                Assert.GreaterOrEqual(cur.ClearBlocks / (float)cur.Moves,
                                      prevCount.ClearBlocks / (float)prevCount.Moves - 0.001f,
                                      lv + "판이 한 수당으로 보면 더 쉬워졌다");
                prevCount = cur;
            }
            Assert.LessOrEqual(cur.PieceTimeMaxMs, prev.PieceTimeMaxMs, lv + "판 시간이 늘었다");
            prev = cur;
        }

        // 1~5 는 벽돌 없음. 강철은 구간 기본값이 아니라 '강철 스테이지' 에만 깔린다.
        for (int lv = 1; lv <= 5; lv++)
            Assert.AreEqual(0, StageTable.Get(lv).ObstacleFromMove, lv + "판에 벽돌이 있다");
        Assert.Greater(StageTable.Get(3).SteelCount, 0, "3판이 강철 스테이지가 아니다");
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

    // ---------- 스테이지 종류 ----------

    [Test]
    public void 무늬_목표_칸은_판_안에_들어온다()
    {
        foreach (var name in new[] { "diamond", "heart", "paw", "cross", "rows", "cols", "scatter" })
        {
            var t = StageTargets.Build(name, 2, new System.Random(7));   // 행/열은 2줄이면 충분하다
            Assert.IsNotNull(t, name + " 무늬를 못 만든다");

            int n = StageTargets.Count(t);
            Assert.Greater(n, 0, name + " 에 목표 칸이 하나도 없다");
            Assert.Less(n, Board.W * Board.H / 2, name + " 이 판의 절반을 넘는다 — 다 깨기 전에 수가 떨어진다");
        }

        Assert.AreEqual(8, StageTargets.Count(StageTargets.Build("scatter", 8, new System.Random(3))),
                        "scatter 는 요청한 수만큼 흩뿌려야 한다");
        Assert.IsNull(StageTargets.Build("", 0, new System.Random(1)), "무늬가 없으면 좌표 목표도 없다");
        Assert.AreEqual(0, StageTargets.Count(null), "좌표 목표가 없으면 0 칸이다");
    }

    [Test]
    public void 피스_제한_스테이지는_수_예산이_피스_수다()
    {
        var s = new StageSetting { Moves = 30 };
        Assert.AreEqual(30, s.MoveBudget, "제한이 없으면 수 그대로다");
        Assert.IsFalse(s.HasCellGoal);

        s.PieceLimit = 18;
        Assert.AreEqual(18, s.MoveBudget, "피스 제한이 수를 대신해야 한다");

        s.TargetPattern = "heart";
        Assert.IsTrue(s.HasCellGoal);
    }

    // stages.json 의 kind 라벨은 설명용이다. 동작은 수치가 정하므로 둘이 어긋나면 읽는 사람이 속는다.
    [Test]
    public void 종류_라벨이_실제_설정과_맞는다()
    {
        for (int lv = 1; lv <= StageTable.Count; lv++)
        {
            var s = StageTable.Get(lv);
            var kinds = new System.Collections.Generic.List<string>(s.Kind.Split('+'));
            string at = lv + "판(" + s.Kind + ")";

            Assert.IsNotEmpty(s.Kind, lv + "판에 종류 라벨이 없다");
            foreach (var k in kinds)
                switch (k)
                {
                    case "blocks": Assert.Greater(s.ClearBlocks, 0, at + " 는 블록을 안 센다"); break;
                    case "pieces": Assert.Greater(s.PieceLimit, 0, at + " 에 피스 제한이 없다"); break;
                    case "steel": Assert.Greater(s.SteelCount, 0, at + " 에 강철이 없다"); break;
                    case "cells": Assert.IsTrue(s.HasCellGoal, at + " 에 좌표 목표가 없다"); break;
                    case "pollution": Assert.IsTrue(s.HasPollution, at + " 에 오염이 없다"); break;
                    default: Assert.Fail(at + " 에 모르는 종류가 있다: " + k); break;
                }

            // 반대 방향 — 설정에 있는 특징은 라벨에도 나와야 한다 (블록 목표는 기본이라 뺀다)
            if (s.PieceLimit > 0) Assert.Contains("pieces", kinds, at + " 라벨에 pieces 가 빠졌다");
            if (s.SteelCount > 0) Assert.Contains("steel", kinds, at + " 라벨에 steel 이 빠졌다");
            if (s.HasCellGoal) Assert.Contains("cells", kinds, at + " 라벨에 cells 가 빠졌다");
            if (s.HasPollution) Assert.Contains("pollution", kinds, at + " 라벨에 pollution 이 빠졌다");

            // 오염 근원은 '못 깨는 칸이 하나뿐' 이라는 전제로 찾는다. 강철과 섞으면 엉뚱한 데서 번진다.
            Assert.IsFalse(s.HasPollution && s.SteelCount > 0, at + " 는 오염과 강철을 같이 쓴다");

            // 10단위는 겹친 판, 나머지는 한 종류
            bool combo = s.Kind.Contains("+");
            Assert.AreEqual(lv % 10 == 0, combo, at + " 의 겹침이 10단위 규칙과 다르다");
        }
    }

    // 난이도 손잡이(stages.json 최상단 difficulty)는 축마다 방향이 다르다.
    // 없던 것이 생기거나 있던 것이 사라지면 스테이지 종류 자체가 바뀌어 버린다.
    [Test]
    public void 난이도_손잡이가_모든_축을_움직인다()
    {
        var raw = Sample();
        var mid = Sample(); StageTable.ApplyDifficulty(mid, StageTable.BaseDifficulty);
        Assert.AreEqual(raw.ClearBlocks, mid.ClearBlocks, "기준 난이도가 파일 값을 바꿨다");
        Assert.AreEqual(raw.Moves, mid.Moves, "기준 난이도가 수를 바꿨다");
        Assert.AreEqual(raw.SteelCount, mid.SteelCount, "기준 난이도가 강철을 바꿨다");

        var easy = Sample(); StageTable.ApplyDifficulty(easy, 1);
        var hard = Sample(); StageTable.ApplyDifficulty(hard, 5);
        Assert.Less(easy.ClearBlocks, hard.ClearBlocks, "목표가 난이도를 안 탄다");
        Assert.Greater(easy.Moves, hard.Moves, "수가 난이도를 안 탄다");
        Assert.Greater(easy.PieceLimit, hard.PieceLimit, "피스 제한이 난이도를 안 탄다");
        Assert.Greater(easy.PieceTimeMaxMs, hard.PieceTimeMaxMs, "조각 시간이 난이도를 안 탄다");
        Assert.Less(easy.SteelCount, hard.SteelCount, "강철이 난이도를 안 탄다");
        Assert.Greater(easy.PollutionEvery, hard.PollutionEvery, "오염 주기가 난이도를 안 탄다");

        // 없던 것은 어떤 난이도에서도 생기지 않는다
        var plain = new StageSetting { ClearBlocks = 20, Moves = 20 };
        StageTable.ApplyDifficulty(plain, 5);
        Assert.AreEqual(0, plain.SteelCount, "안 쓰던 강철이 생겼다");
        Assert.AreEqual(0, plain.PollutionEvery, "안 쓰던 오염이 생겼다");
        Assert.AreEqual(0, plain.PieceLimit, "안 쓰던 피스 제한이 생겼다");

        // 있던 것은 가장 쉬운 난이도에서도 사라지지 않는다
        var thin = new StageSetting { SteelCount = 1, PollutionEvery = 1, PieceLimit = 1, ObstacleMax = 1 };
        StageTable.ApplyDifficulty(thin, 1);
        Assert.Greater(thin.SteelCount, 0, "강철 스테이지에서 강철이 사라졌다");
        Assert.Greater(thin.PollutionEvery, 0, "오염 스테이지에서 오염이 사라졌다");
        Assert.Greater(thin.PieceLimit, 0, "피스 제한이 사라졌다");
        Assert.Greater(thin.ObstacleMax, 0, "벽돌이 사라졌다");
    }

    static StageSetting Sample()
    {
        return new StageSetting
        {
            ClearBlocks = 100, Moves = 30, PieceLimit = 20,
            PieceTimeMaxMs = 12000, PieceTimeMinMs = 9000,
            SteelCount = 10, ObstacleMax = 3, PollutionEvery = 3,
        };
    }

    // 타임어택 설정은 손으로 잡은 값이다. 난이도 손잡이가 먹으면 파일에 적은 초가 그대로 안 나온다.
    [Test]
    public void 타임어택은_파일에_적은_값_그대로_쓴다()
    {
        var ta = StageTable.TimeAttack;
        Assert.Greater(ta.Seconds, 0, "타임어택 길이가 없다");
        Assert.AreEqual(ta.PieceTimeMaxMs, ta.PieceTimeMs(9999, 9999),
                        "조각 시간이 파일 값과 다르다 — 난이도 배율이 먹었을 수 있다");
        Assert.AreEqual(0, ta.PieceLimit, "타임어택에 피스 제한이 걸려 있다");
    }

    [Test]
    public void 모든_스테이지에_목표와_수가_있다()
    {
        for (int lv = 1; lv <= StageTable.Count; lv++)
        {
            var s = StageTable.Get(lv);
            Assert.IsTrue(s.ClearBlocks > 0 || s.HasCellGoal, lv + " 단계에 목표가 없다");
            Assert.Greater(s.MoveBudget, 0, lv + " 단계에 쓸 수가 없다");
            if (s.HasCellGoal)
                Assert.IsNotNull(StageTargets.Build(s.TargetPattern, s.TargetCount, new System.Random(1)),
                                 lv + " 단계의 무늬 이름(" + s.TargetPattern + ")을 모른다");
        }
    }

    // 목표 칸은 블록 가장자리에서 빛나고 바깥 3px 까지 번진다. 블록 뒤에 깔면 배경에 묻혀 안 보이고,
    // 블록보다 작으면 바깥으로 못 번진다. 깨도 사라지지 않고 흐려진다 — 어디를 깼는지 보여야 한다.
    [UnityTest]
    public IEnumerator 목표_칸에만_표시가_붙는다()
    {
        gm = NewGm();
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 4242);   // 홈 화면에서는 BoardView 가 꺼져 있다
        yield return null;

        var view = Object.FindObjectOfType<BoardView>();
        var m = StageTargets.Build("diamond", 0, new System.Random(1));
        view.SetMarks(m);
        yield return null;

        int mx = -1, my = -1;
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                var sr = view.transform.Find("m_" + x + "_" + y).GetComponent<SpriteRenderer>();
                Assert.AreEqual(m[x, y], sr.enabled, "(" + x + "," + y + ") 표시가 목표와 다르다");
                if (!m[x, y]) continue;

                var tile = view.transform.Find("t_" + x + "_" + y).GetComponent<SpriteRenderer>();
                Assert.Greater(sr.sortingOrder, tile.sortingOrder, "표시가 블록 뒤에 있어 안 보인다");
                Assert.Greater(sr.transform.localScale.x, tile.transform.localScale.x,
                               "표시가 블록보다 작아 바깥으로 안 번진다");
                if (mx < 0) { mx = x; my = y; }
            }

        // 남은 칸은 밝아졌다 어두워지며 계속 뛴다
        var glow = view.transform.Find("m_" + mx + "_" + my).GetComponent<SpriteRenderer>();
        var fill = view.transform.Find("mf_" + mx + "_" + my).GetComponent<SpriteRenderer>();
        var first = glow.color;
        yield return null;
        yield return null;
        Assert.AreNotEqual(first, glow.color, "목표 칸이 반짝이지 않는다");

        // 깨고 나면 빛이 꺼져야 한다. 켜 둔 채 색만 바꾸면 아직 깨야 할 칸처럼 계속 물들어 보인다.
        view.ClearMark(mx, my);
        Assert.IsFalse(glow.enabled, "깬 칸에서 빛이 계속 새어나온다");

        var done = fill.color;
        Assert.Less(done.a, 0.25f, "깬 칸의 흔적이 너무 진하다");
        yield return null;
        yield return null;
        Assert.AreEqual(done, fill.color, "깬 칸이 아직도 반짝인다");
    }

    // 오염은 근원(못 깨는 칸) 바로 옆으로만 번지고, 번진 오염은 깰 수 있는 벽돌이다.
    [UnityTest]
    public IEnumerator 오염은_근원_옆으로_번진다()
    {
        int lv = 0;
        for (int i = 1; i <= StageTable.Count && lv == 0; i++)
            if (StageTable.Get(i).HasPollution && !StageTable.Get(i).HasCellGoal) lv = i;
        Assert.Greater(lv, 0, "오염 스테이지가 하나도 없다");

        gm = NewGm();
        gm.stageLevel = lv;
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 777);
        yield return null;

        var b = gm.BoardRef;
        int sx = -1, sy = -1, steel = 0;
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                if (b.IsSteel(x, y)) { steel++; sx = x; sy = y; }
        Assert.AreEqual(1, steel, "오염 근원은 판에 하나뿐이어야 한다");
        Assert.AreEqual(0, b.CountObstacles(), "판을 열자마자 오염이 번져 있다");

        // 딱 한 번 번질 만큼만 둔다. 더 두면 중력이 근원과 오염을 따로 옮겨서
        // '바로 옆' 인지 나중에 확인할 수 없다.
        int every = StageTable.Get(lv).PollutionEvery;
        for (int i = 0; i < every && gm.Phase == GamePhase.Playing; i++)
        {
            Assert.IsTrue(StampAnywhere(gm), "놓을 자리를 못 찾았다");
            float t0 = Time.realtimeSinceStartup;
            while (gm.Busy && Time.realtimeSinceStartup - t0 < 10) yield return null;
        }

        Assert.Greater(b.CountObstacles(), 0, "수를 뒀는데 오염이 안 번졌다");

        // 번진 오염은 근원 바로 옆이어야 한다 (근원도 중력을 받으므로 자리를 다시 찾는다)
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                if (b.IsSteel(x, y)) { sx = x; sy = y; }
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                if (b.IsObstacle(x, y))
                    Assert.LessOrEqual(Mathf.Max(Mathf.Abs(x - sx), Mathf.Abs(y - sy)), 1,
                                       "(" + x + "," + y + ") 오염이 근원 옆이 아니다");
    }

    /// <summary>놓을 수 있는 아무 자리에나 놓는다. 자리가 없으면 false.</summary>
    static bool StampAnywhere(GameManager g)
    {
        var b = g.BoardRef;
        var p = g.CurrentPiece;
        if (b == null || p == null) return false;
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                if (b.CanPlace(p, x, y) && g.TryStamp(x, y)) return true;
        return false;
    }

    // 최상단 목표 문구는 제 칸 안에 있어야 한다. 넘치면 왼쪽 홈 버튼 위로 글자가 올라탄다.
    [UnityTest]
    public IEnumerator 목표_문구가_HUD_와_안_겹친다()
    {
        gm = NewGm();
        yield return null;
        var ui = Object.FindObjectOfType<GameUI>();

        for (int lv = 1; lv <= StageTable.Count; lv++)
        {
            gm.stageLevel = lv;
            gm.StartGame(GameManager.Difficulty, false, 1234 + lv);
            yield return null;

            var eyebrow = FindRect(ui.gameObject, "eyebrow").GetComponent<UnityEngine.UI.Text>();
            var sub = FindRect(ui.gameObject, "goalsub").GetComponent<UnityEngine.UI.Text>();
            Assert.IsNotEmpty(eyebrow.text, lv + " 단계에 목표 문구가 없다");
            Assert.IsTrue(eyebrow.fontStyle == FontStyle.Bold || eyebrow.fontStyle == FontStyle.BoldAndItalic,
                          "미션 문구가 볼드가 아니다");

            // 종류마다 문구가 하나씩. 겹친 판은 가장 특이한 것이 큰 줄을 가져간다.
            var s = StageTable.Get(lv);
            string want = s.HasCellGoal ? "TARGET"
                        : s.HasPollution ? "ROT"
                        : s.PieceLimit > 0 ? "PIECES"
                        : s.SteelCount > 0 ? "STEEL"
                        : "BLOCKS";
            StringAssert.Contains(want, eyebrow.text, lv + "판(" + s.Kind + ") 문구가 종류와 안 맞는다");
            Assert.LessOrEqual(eyebrow.preferredWidth, eyebrow.rectTransform.rect.width,
                               lv + " 단계 목표 문구가 칸을 넘친다: " + eyebrow.text);
            Assert.LessOrEqual(sub.preferredWidth, sub.rectTransform.rect.width,
                               lv + " 단계 진행도 줄이 칸을 넘친다: " + sub.text);

            // 두 줄 다 홈 버튼 오른쪽, 스탯 카드 위에 있어야 한다
            var home = FindRect(ui.gameObject, "home");
            var score = FindRect(ui.gameObject, "statscore");
            foreach (var line in new[] { eyebrow.rectTransform, sub.rectTransform })
            {
                Assert.GreaterOrEqual(Left(line), Right(home), lv + " 단계 문구가 홈 버튼과 겹친다");
                Assert.GreaterOrEqual(Bottom(line), Top(score), lv + " 단계 문구가 스탯 카드와 겹친다");
            }
        }
    }

    static float Left(RectTransform r) { r.GetWorldCorners(corners); return corners[0].x; }
    static float Right(RectTransform r) { r.GetWorldCorners(corners); return corners[2].x; }
}
