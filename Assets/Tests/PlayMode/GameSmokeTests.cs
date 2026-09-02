// GameSmokeTests.cs — PlayMode 실행 검증.
// 실제 유니티 런타임에서 게임을 부팅·조작해 표현 계층(뷰/UI/사운드/코루틴)까지 확인한다.
// 규칙 자체의 검증은 Tools/verify.sh 가 한다 — 여기서는 '유니티에서 실제로 도는가'를 본다.

using System.Collections;
using ChromaDrop.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class GameSmokeTests
{
    GameManager gm;

    GameManager NewGm()
    {
        var go = new GameObject("GM_under_test");
        return go.AddComponent<GameManager>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var o in Object.FindObjectsByType<GameManager>(FindObjectsSortMode.None))
            Object.DestroyImmediate(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GameUI>(FindObjectsSortMode.None))
            Object.DestroyImmediate(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GraphBoardView>(FindObjectsSortMode.None))
            Object.DestroyImmediate(o.gameObject);
    }

    [UnityTest]
    public IEnumerator 스테이지_설정이_실제로_로드된다()
    {
        var set = StageCatalog.Reload();
        Assert.IsTrue(set.Ok, "stages.json 로드 실패: " + string.Join(" / ", set.Errors.ToArray()));
        Assert.Greater(set.Count, 0, "스테이지가 없다");
        yield return null;
    }

    [UnityTest]
    public IEnumerator 게임을_켜면_홈이_뜬다()
    {
        gm = NewGm();
        yield return null;
        Assert.AreEqual(GamePhase.Home, gm.Phase);
    }

    [UnityTest]
    public IEnumerator 스테이지를_시작하면_설정대로_판이_선다()
    {
        gm = NewGm();
        yield return null;

        int count = StageCatalog.Count;
        for (int lv = 1; lv <= count; lv += 7)
        {
            gm.StartStage(lv);
            yield return null;

            var def = StageCatalog.Get(lv);
            Assert.AreEqual(GamePhase.Playing, gm.Phase, lv + "판이 시작되지 않았다");
            Assert.AreEqual(lv, gm.StageLevel);
            Assert.IsNotNull(gm.Instance, lv + "판 인스턴스가 없다");
            Assert.AreEqual(def.MinGroupSize, gm.Instance.Engine.MinGroupSize);
            Assert.AreEqual(def.ResolveTopology(), gm.Instance.Topo.Name);
            Assert.IsFalse(gm.StageCleared);

            if (def.Moves > 0) Assert.AreEqual(def.Moves, gm.MovesLeft, lv + "판 수 제한이 다르다");
        }
    }

    [UnityTest]
    public IEnumerator 시작_보드에는_바로_터지는_그룹이_없다()
    {
        gm = NewGm();
        yield return null;

        for (int lv = 1; lv <= StageCatalog.Count; lv += 5)
        {
            gm.StartStage(lv);
            yield return null;
            Assert.IsFalse(gm.Instance.Engine.HasClearableGroup(), lv + "판: 시작하자마자 터진다");
        }
    }

    [UnityTest]
    public IEnumerator 조각을_놓으면_수가_줄고_판이_바뀐다()
    {
        gm = NewGm();
        yield return null;
        gm.StartStage(1);
        yield return null;

        int before = gm.MovesLeft;
        int cell = FindPlaceable(gm);
        Assert.GreaterOrEqual(cell, 0, "놓을 자리를 못 찾았다");
        Assert.IsTrue(gm.TryPlace(cell), "놓기 실패");

        float t0 = Time.realtimeSinceStartup;
        while (gm.Busy && Time.realtimeSinceStartup - t0 < 20) yield return null;

        Assert.Less(gm.MovesLeft, before, "수가 줄지 않았다");
    }

    [UnityTest]
    public IEnumerator 회전은_조각만_바꾸고_보드는_그대로다()
    {
        gm = NewGm();
        yield return null;
        gm.StartStage(1);
        yield return null;

        var eng = gm.Instance.Engine;
        var before = new int[eng.Count];
        for (int i = 0; i < eng.Count; i++) before[i] = eng.Get(i);

        for (int r = 0; r < 5; r++) { gm.RotateCurrent(); yield return null; }

        for (int i = 0; i < eng.Count; i++)
            Assert.AreEqual(before[i], eng.Get(i), "회전이 보드를 바꿨다 (칸 " + i + ")");
    }

    [UnityTest]
    public IEnumerator 아이템은_보유량이_있어야_장전된다()
    {
        gm = NewGm();
        yield return null;
        gm.StartStage(1);
        yield return null;

        var def = StageCatalog.Get(1);
        Assert.Greater(def.ItemsAvailable.Count, 0, "1판에 아이템이 없다");
        string id = def.ItemsAvailable[0];

        while (Wallet.Count(id) > 0) Wallet.Use(id);
        Assert.IsFalse(gm.ArmItem(id, 0), "보유량 0 인데 장전됐다");

        Wallet.Add(id, 1);
        Assert.IsTrue(gm.ArmItem(id, 0), "장전 실패");
        Assert.IsNotNull(gm.ArmedItem);
        Assert.AreEqual(0, Wallet.Count(id), "보유량이 줄지 않았다");
    }

    [UnityTest]
    public IEnumerator 축_버튼_수는_토폴로지가_정한다()
    {
        gm = NewGm();
        yield return null;

        for (int lv = 1; lv <= StageCatalog.Count; lv += 6)
        {
            gm.StartStage(lv);
            yield return null;
            var def = StageCatalog.Get(lv);
            var topo = TopologyGen.Build(def.ResolveTopology(), def.GridSize);
            Assert.AreEqual(topo.Axes.Length, gm.AxisCount, lv + "판 축 수가 다르다");
        }
    }

    [UnityTest]
    public IEnumerator 클리어해야_다음_스테이지가_열린다()
    {
        Progress.ResetAll();
        Assert.AreEqual(1, Progress.Unlocked);

        Progress.Clear(1);
        Assert.AreEqual(2, Progress.Unlocked, "클리어했는데 다음이 안 열렸다");
        Assert.AreEqual(2, Progress.Selected);

        Progress.Clear(1);
        Assert.AreEqual(2, Progress.Unlocked, "해금이 뒤로 갔다");

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

        for (int level = 1; level <= StageCatalog.Count; level++)
        {
            var rt = FindRect(ui.gameObject, "island" + level);
            Assert.IsNotNull(rt, level + "번 섬이 없다");
            var btn = rt.GetComponent<Button>();
            Assert.IsNotNull(btn);
            Assert.AreEqual(level <= 4, btn.interactable, level + "번 섬 잠금 상태가 다르다");
        }

        var a = FindRect(ui.gameObject, "island1");
        var b = FindRect(ui.gameObject, "island2");
        Assert.Greater(Bottom(a), Top(b), "이웃한 두 섬이 겹친다");

        Progress.ResetAll();
    }

    [UnityTest]
    public IEnumerator 리더보드_칸이_서로_겹치지_않는다()
    {
        gm = NewGm();
        yield return null;
        var ui = Object.FindObjectOfType<GameUI>();
        ui.ShowRanking(false);
        yield return null;
        yield return null;

        var lastRow = FindRect(ui.gameObject, "row" + (RankRows - 1));
        var myRow = FindRect(ui.gameObject, "myrow");
        var adBtn = FindRect(ui.gameObject, "adbtn");
        Assert.IsNotNull(lastRow); Assert.IsNotNull(myRow); Assert.IsNotNull(adBtn);

        float rowBottom = Bottom(lastRow);
        Assert.GreaterOrEqual(rowBottom, Top(myRow), "마지막 행이 내 점수 행을 덮는다");
        Assert.GreaterOrEqual(rowBottom, Top(adBtn), "마지막 행이 광고 버튼을 덮는다");
        Assert.GreaterOrEqual(Bottom(myRow), Top(adBtn), "내 점수 행과 광고 버튼이 겹친다");

        var row0 = FindRect(ui.gameObject, "row0");
        var name = FindIn(row0, "n");
        var lv = FindIn(row0, "lv");
        var score = FindIn(row0, "s");
        Assert.LessOrEqual(Right(name), Left(lv), "이름 칸이 레벨 칸을 파고든다");
        Assert.LessOrEqual(Right(lv), Left(score), "레벨 칸이 점수 칸을 파고든다");
    }

    const int RankRows = 10;

    static int FindPlaceable(GameManager gm)
    {
        var turn = gm.Instance.Turn;
        for (int i = 0; i < gm.Instance.Engine.Count; i++)
            if (turn.PlacementAt(i) != null) return i;
        return -1;
    }

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
}
