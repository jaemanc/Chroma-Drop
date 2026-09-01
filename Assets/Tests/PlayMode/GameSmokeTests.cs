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

    // 난이도 선택이 없어져 규칙표에서 가져온다
    static int Moves { get { return Rules.Table[GameManager.Difficulty].Moves; } }

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
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 4321);
        yield return null;

        int moves = gm.MovesLeft;
        var piece = gm.CurrentPiece;
        Assert.Greater(gm.PieceTimerFrac, 0.9f, "시작 직후엔 제한시간이 거의 남아 있어야 함");

        // 아무것도 놓지 않고 첫 조각의 제한시간(최대 8초)이 지나기를 기다린다
        float t0 = Time.realtimeSinceStartup;
        while (gm.MovesLeft == moves && Time.realtimeSinceStartup - t0 < 15f)
            yield return null;

        Assert.AreEqual(moves - 1, gm.MovesLeft, "만료되면 기회를 하나 쓴다");
        Assert.AreNotSame(piece, gm.CurrentPiece, "다음 조각으로 넘어간다");
        Assert.Greater(gm.PieceTimerFrac, 0.5f, "새 조각은 제한시간이 다시 채워진다");
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
        while (Wallet.Count(ShopItem.Reroll) > 0) Wallet.Use(ShopItem.Reroll);
        Assert.IsFalse(gm.UseItem(ShopItem.Reroll), "보유량 0 인데 사용됐다");

        // 리롤: 조각이 바뀌고 기회는 그대로
        Wallet.Add(ShopItem.Reroll, 1);
        var before = gm.CurrentPiece;
        int moves = gm.MovesLeft;
        Assert.IsTrue(gm.UseItem(ShopItem.Reroll), "리롤이 실패했다");
        Assert.AreNotSame(before, gm.CurrentPiece, "조각이 바뀌지 않았다");
        Assert.AreEqual(moves, gm.MovesLeft, "아이템이 기회를 소모했다");
        Assert.AreEqual(0, Wallet.Count(ShopItem.Reroll), "보유량이 줄지 않았다");

        // 시간 추가: 남은 시간 비율이 올라간다
        Wallet.Add(ShopItem.AddTime, 1);
        float f0 = gm.PieceTimerFrac;
        Assert.IsTrue(gm.UseItem(ShopItem.AddTime));
        Assert.Greater(gm.PieceTimerFrac, f0, "제한시간이 늘지 않았다");

        // 기회 추가
        Wallet.Add(ShopItem.ExtraMoves, 1);
        moves = gm.MovesLeft;
        Assert.IsTrue(gm.UseItem(ShopItem.ExtraMoves));
        Assert.AreEqual(moves + 3, gm.MovesLeft, "기회가 3 늘지 않았다");
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
}
