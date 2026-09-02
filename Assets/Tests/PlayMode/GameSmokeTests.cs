// GameSmokeTests.cs — PlayMode 실행 검증.
// 실제 Unity 런타임에서 게임을 부팅·조작해 표현 계층 전체(뷰/UI/사운드/코루틴)를 확인한다.
// 실행: 에디터 Test Runner(PlayMode) 또는
//   Unity -batchmode -runTests -testPlatform PlayMode -projectPath .

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using ColorMatcher.Core;

public class GameSmokeTests
{
    GameManager gm;

    // 수 제한은 스테이지 설정에서 온다
    static int Moves
    {
        get
        {
            var st = StageLoader.Get(1);
            return st != null && st.Moves > 0 ? st.Moves : 20;
        }
    }

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
        for (int x = 0; x < Defaults.Width; x++)
            for (int y = 0; y < Defaults.Height; y++)
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
        yield return null;
        gm.StartGame(GameManager.Difficulty, false, 767);
        yield return null;

        Wallet.Add(ShopItem.BombPiece, 1);
        Assert.IsTrue(gm.UseItem(ShopItem.BombPiece));
        Assert.IsTrue(gm.BombArmed);

        // 손대지 않고 만료를 기다린다
        float t0 = Time.realtimeSinceStartup;
        while (gm.BombArmed && Time.realtimeSinceStartup - t0 < 12) yield return null;
        Assert.IsFalse(gm.BombArmed, "만료됐는데 폭탄 예약이 남아 있다");
        Assert.Greater(gm.CurrentPiece.Cells.Count, 1, "폭탄 조각이 그대로다");
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

    // 회귀: 리더보드 행이 아래쪽 고정 요소(내 점수·광고 버튼)를 덮거나,
    // 행 안에서 이름이 점수 칸을 파고들면 안 된다. 실제 배치 좌표로 검사한다.
    [UnityTest]
    public IEnumerator 리더보드_칸이_서로_겹치지_않는다()
    {
        gm = NewGm();
        yield return null;
        var ui = Object.FindObjectOfType<GameUI>();
        Assert.IsNotNull(ui, "GameUI 를 찾지 못했다");
        ui.ShowRanking(false);
        yield return null;
        yield return null;

        var lastRow = FindRect(ui.gameObject, "row" + (RankRows - 1));
        var myRow = FindRect(ui.gameObject, "myrow");
        var adBtn = FindRect(ui.gameObject, "adbtn");
        Assert.IsNotNull(lastRow, "마지막 행이 없다 — RankRows 상수가 실제와 다르다");
        Assert.IsNotNull(myRow); Assert.IsNotNull(adBtn);

        float rowBottom = Bottom(lastRow);
        Assert.GreaterOrEqual(rowBottom, Top(myRow), "마지막 행이 내 점수 행을 덮는다");
        Assert.GreaterOrEqual(rowBottom, Top(adBtn), "마지막 행이 광고 버튼을 덮는다");
        Assert.GreaterOrEqual(Bottom(myRow), Top(adBtn), "내 점수 행과 광고 버튼이 겹친다");

        // 행 안쪽: 이름 오른쪽 끝이 점수 왼쪽 끝을 넘지 않는다
        var row0 = FindRect(ui.gameObject, "row0");
        var name = FindIn(row0, "n");
        var score = FindIn(row0, "s");
        Assert.IsNotNull(name); Assert.IsNotNull(score);
        var lv = FindIn(row0, "lv");
        Assert.IsNotNull(lv, "스테이지 레벨 칸이 없다");
        Assert.LessOrEqual(Right(name), Left(lv), "이름 칸이 레벨 칸을 파고든다");
        Assert.LessOrEqual(Right(lv), Left(score), "레벨 칸이 점수 칸을 파고든다");
    }


    [UnityTest]
    public IEnumerator 클리어해야_다음_스테이지가_열린다()
    {
        Progress.ResetAll();
        Assert.AreEqual(1, Progress.Unlocked, "처음엔 1스테이지만 열려 있다");
        Assert.AreEqual(1, Progress.Selected);

        Progress.Clear(1);
        Assert.AreEqual(2, Progress.Unlocked, "클리어했는데 다음이 안 열렸다");
        Assert.AreEqual(2, Progress.Selected, "클리어하면 다음 판이 선택된다");

        // 이미 지난 판을 다시 깨도 진행이 뒤로 가지 않는다
        Progress.Clear(1);
        Assert.AreEqual(2, Progress.Unlocked, "해금이 뒤로 갔다");

        // 열리지 않은 판은 고를 수 없다
        Progress.Selected = 99;
        Assert.AreEqual(Progress.Unlocked, Progress.Selected, "안 열린 판이 선택됐다");

        Progress.ResetAll();
        yield return null;
    }

    [UnityTest]
    public IEnumerator 지도는_열린_섬만_고를_수_있다()
    {
        Progress.ResetAll();
        PlayerPrefs.SetInt("stage_unlocked", 4);
        PlayerPrefs.Save();

        gm = NewGm();
        yield return null;
        var ui = Object.FindObjectOfType<GameUI>();
        ui.ShowMap();
        yield return null;

        for (int level = 1; level <= StageLoader.Count; level++)
        {
            var rt = FindRect(ui.gameObject, "island" + level);
            Assert.IsNotNull(rt, level + "번 섬이 없다");
            var btn = rt.GetComponent<Button>();
            Assert.IsNotNull(btn, level + "번 섬에 버튼이 없다");
            Assert.AreEqual(level <= 4, btn.interactable,
                            level + "번 섬의 잠금 상태가 해금(4)과 맞지 않는다");
        }

        // 섬은 항로를 따라 아래로 내려간다 — 겹치면 안 된다
        var a = FindRect(ui.gameObject, "island1");
        var b = FindRect(ui.gameObject, "island2");
        Assert.Greater(Bottom(a), Top(b), "이웃한 두 섬이 겹친다");

        Progress.ResetAll();
    }



    // 회귀: 내구도가 2든 5든 한 대 맞을 때마다 겉모습이 반드시 달라져야 한다.
    // 예전엔 손상 단계를 상수 Rules.ObstacleHp 로 계산해서, 내구도 3 이상인 판에서는
    // 멀쩡해 보이다가 한 방에 사라지는 것처럼 보였다.
    [UnityTest]
    public IEnumerator 방해블록은_맞을_때마다_겉모습이_달라진다()
    {
        foreach (int maxHp in new[] { 2, 3, 4, 5 })
        {
            int prev = -1;
            for (int hp = maxHp; hp >= 1; hp--)
            {
                int st = ObstacleStyle.StageFor(hp, maxHp);
                Assert.Greater(st, prev,
                    "내구도 " + maxHp + ", 남은 " + hp + ": 손상 단계가 " + prev + " 에서 안 올라갔다");
                Assert.Less(st, ObstacleStyle.Stages, "손상 단계가 스프라이트 개수를 넘는다");
                prev = st;
            }
        }

        // 설정에 있는 모든 내구도가 이 규칙을 만족해야 한다
        foreach (var st in StageLoader.All)
            foreach (var ob in st.Obstacles)
            {
                if (ob.Cell != Board.Obstacle) continue;
                Assert.AreEqual(0, ObstacleStyle.StageFor(ob.HitsToBreak, ob.HitsToBreak),
                                st.StageId + "판: 새로 놓인 벽돌이 이미 손상돼 보인다");
            }
        yield return null;
    }

    const int RankRows = 10;

    static RectTransform FindRect(GameObject root, string name)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == name) return rt;
        return null;
    }
    static RectTransform FindIn(RectTransform root, string name)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == name) return rt;
        return null;
    }
    static readonly Vector3[] corners = new Vector3[4];
    static float Bottom(RectTransform r) { r.GetWorldCorners(corners); return corners[0].y; }
    static float Top(RectTransform r) { r.GetWorldCorners(corners); return corners[1].y; }
    static float Left(RectTransform r) { r.GetWorldCorners(corners); return corners[0].x; }
    static float Right(RectTransform r) { r.GetWorldCorners(corners); return corners[2].x; }

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
