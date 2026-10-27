using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class SceneSmokeTests
{
    string savedHearts;
    bool hadHearts;
    [SetUp]
    public void DisableLiveLeaderboard()
    {
        hadHearts = PlayerPrefs.HasKey(Hearts.StorageKey);
        savedHearts = PlayerPrefs.GetString(Hearts.StorageKey, "");
        PlayerPrefs.SetString(Hearts.StorageKey, "{\"count\":" + ColorMatcher.Core.HeartClock.Capacity + ",\"nextUtc\":0}");
        // 씬 검증 중 운영 서버에 게스트 계정을 만들지 않는다.
        if (Leaderboard.I != null) Object.DestroyImmediate(Leaderboard.I.gameObject);
        var offline = new GameObject("OfflineLeaderboard").AddComponent<Leaderboard>();
        Object.DontDestroyOnLoad(offline.gameObject);
        typeof(Leaderboard).GetProperty("I").SetValue(null, offline);
    }

    [UnityTest]
    public IEnumerator ClassicButtonStartsPlayableBoard()
    {
        yield return Launch("Btn_Play", false);
    }

    [Test]
    public void TypographyUsesBundledRoleFontsAndKoreanFallback()
    {
        Assert.That(UiTypography.FontFor(UiFontRole.Title).name, Is.EqualTo("Fredoka-Bold"));
        Assert.That(UiTypography.FontFor(UiFontRole.Menu).name, Is.EqualTo("Nunito-ExtraBold"));
        Assert.That(UiTypography.FontFor(UiFontRole.Body).name, Is.EqualTo("Pretendard-Regular"));
        Assert.That(UiTypography.FontFor(UiFontRole.Numbers).name, Is.EqualTo("Fredoka-SemiBold"));
        Assert.That(UiTypography.FontFor(UiFontRole.Effects).name, Is.EqualTo("Baloo2-ExtraBold"));
        Assert.That(UiTypography.FontFor(UiFontRole.KoreanAccent).name, Is.EqualTo("Jua-Regular"));
        Assert.That(UiTypography.Assets.pausePanel, Is.Not.Null);
        foreach (UiFontRole role in System.Enum.GetValues(typeof(UiFontRole)))
        {
            var font = UiTypography.TmpFor(role);
            Assert.That(font.sourceFontFile, Is.SameAs(UiTypography.FontFor(role)));
            if (role != UiFontRole.Body)
                Assert.That(font.fallbackFontAssetTable, Does.Contain(UiTypography.TmpFor(UiFontRole.Body)));
        }
        Object.DestroyImmediate(UiTypography.TmpFor(UiFontRole.Body));
        Assert.That(UiTypography.TmpFor(UiFontRole.Menu).fallbackFontAssetTable,
            Does.Contain(UiTypography.TmpFor(UiFontRole.Body)));
        var label = new GameObject("FontProbe").AddComponent<Text>();
        try
        {
            label.text = "RESUME";
            UiTypography.Apply(label, UiFontRole.Menu);
            Assert.That(label.font, Is.SameAs(UiTypography.FontFor(UiFontRole.Menu)));
            Assert.That(label.fontStyle, Is.EqualTo(FontStyle.Normal));
            label.text = "설정";
            UiTypography.Apply(label, UiFontRole.Menu);
            Assert.That(label.font, Is.SameAs(UiTypography.FontFor(UiFontRole.KoreanAccent)));
        }
        finally { Object.DestroyImmediate(label.gameObject); }
    }

    [UnityTest]
    public IEnumerator HomeShopOpensWithoutSpendingAHeart()
    {
        GameManager.LaunchHomePanel = 0;
        yield return SceneManager.LoadSceneAsync("Menu");
        yield return null;
        int before = Hearts.Count;
        Object.FindObjectsByType<Button>().Single(b => b.name == "Btn_Shop").onClick.Invoke();
        yield return null;
        yield return null;
        var game = Object.FindAnyObjectByType<GameManager>();
        Assert.That(game.Phase, Is.EqualTo(GamePhase.Home));
        Assert.That(Hearts.Count, Is.EqualTo(before));
        Assert.That(GameObject.Find("shopdim"), Is.Not.Null);
        game.GoHome();
        yield return null;
        Assert.That(Object.FindAnyObjectByType<MainMenu>(), Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator RushButtonStartsTimedBoard()
    {
        yield return Launch("Btn_Rush", true);
    }

    [UnityTest]
    public IEnumerator FeverAcceptsRepeatedStampsWithoutWaitingForAnimations()
    {
        GameManager.LaunchTimeAttack = true;
        yield return SceneManager.LoadSceneAsync("Game");
        yield return null;
        var game = Object.FindAnyObjectByType<GameManager>();
        // 연출 시간을 길게 둬도 같은 프레임에서 다음 발사를 받아야 한다.
        game.stampTime = game.destroyFlash = game.fallTime = game.landTime = game.chainFall = 60f;
        typeof(GameManager).GetMethod("StartFever", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        for (int shot = 0; shot < 5; shot++)
        {
            int ax = -1, ay = -1;
            for (int x = 0; x < ColorMatcher.Core.Board.W && ax < 0; x++)
                for (int y = 0; y < ColorMatcher.Core.Board.H; y++)
                    if (game.BoardRef.CanPlace(game.CurrentPiece, x, y)) { ax = x; ay = y; break; }
            Assert.That(ax, Is.GreaterThanOrEqualTo(0));
            int before = game.Broken;
            var next = game.TraySlot(1);
            Assert.That(game.TryStamp(ax, ay), Is.True);
            Assert.That(game.Busy, Is.False);
            Assert.That(game.Broken, Is.GreaterThan(before));
            Assert.That(game.CurrentPiece, Is.SameAs(next));
        }
    }

    [UnityTest]
    public IEnumerator PauseButtonFreezesTimersAndResumePreservesFever()
    {
        GameManager.LaunchTimeAttack = true;
        yield return SceneManager.LoadSceneAsync("Game");
        yield return null;
        var game = Object.FindAnyObjectByType<GameManager>();
        var ui = Object.FindAnyObjectByType<GameUI>();
        var pause = ui.GetComponentsInChildren<Button>().Single(b => b.name == "pause");
        pause.onClick.Invoke();
        Canvas.ForceUpdateCanvases();
        var card = (RectTransform)ui.transform.Find("pausedim/pausesafe/pausecard/fill");
        foreach (var name in new[] { "resume", "pausehome" })
        {
            var button = (RectTransform)card.Find(name);
            var corners = new Vector3[4];
            button.GetWorldCorners(corners);
            foreach (var corner in corners)
                Assert.That(card.rect.Contains(card.InverseTransformPoint(corner)), Is.True, name + "이 카드 밖으로 벗어남");
            Assert.That(card.InverseTransformPoint(button.TransformPoint(button.rect.center)).x,
                Is.EqualTo(card.rect.center.x).Within(0.01f));
        }
        float timeLeft = game.TimeLeftSec;
        float pieceLeft = game.PieceTimerFrac;
        yield return new WaitForSecondsRealtime(0.08f);
        Assert.That(game.Paused, Is.True);
        Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
        Assert.That(Hearts.Count, Is.EqualTo(19));
        Assert.That(game.TimeLeftSec, Is.EqualTo(timeLeft).Within(0.001f));
        Assert.That(game.PieceTimerFrac, Is.EqualTo(pieceLeft).Within(0.001f));
        Assert.That(game.TryStamp(0, 0), Is.False);
        Assert.That(game.UseItem(ShopItem.Hammer), Is.False);
        ui.GetComponentsInChildren<Button>().Single(b => b.name == "resume").onClick.Invoke();
        Assert.That(game.Paused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));

        typeof(GameManager).GetMethod("StartFever", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        pause.onClick.Invoke();
        float feverLeft = game.FeverLeftSec;
        yield return new WaitForSecondsRealtime(0.08f);
        game.ResumeGame();
        Assert.That(game.FeverOn, Is.True);
        Assert.That(game.FeverLeftSec, Is.EqualTo(feverLeft).Within(0.04f));
        Assert.That(Time.timeScale, Is.EqualTo(3f));
        game.PauseGame();
        ui.GetComponentsInChildren<Button>().Single(b => b.name == "pausehome").onClick.Invoke();
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Menu"));
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    [UnityTest]
    public IEnumerator RetryClearsAimAndExpiredPieceSpawnsScheduledObstacle()
    {
        GameManager.LaunchTimeAttack = false;
        yield return SceneManager.LoadSceneAsync("Game");
        yield return null;
        var game = Object.FindAnyObjectByType<GameManager>();
        var aim = typeof(GameManager).GetField("aiming", BindingFlags.Instance | BindingFlags.NonPublic);
        aim.SetValue(game, true);
        game.StartGame();
        Assert.That(aim.GetValue(game), Is.False);

        var stage = game.Stage;
        int from = stage.ObstacleFromMove, every = stage.ObstacleEveryMoves, max = stage.ObstacleMax;
        try
        {
            stage.ObstacleFromMove = stage.ObstacleEveryMoves = stage.ObstacleMax = 1;
            int moves = game.MovesLeft;
            var expire = (IEnumerator)typeof(GameManager).GetMethod("ExpirePiece",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
            yield return game.StartCoroutine(expire);
            Assert.That(game.MovesLeft, Is.EqualTo(moves - 1));
            int obstacles = 0;
            for (int x = 0; x < ColorMatcher.Core.Board.W; x++)
                for (int y = 0; y < ColorMatcher.Core.Board.H; y++)
                    if (game.BoardRef.GetTile(x, y) == ColorMatcher.Core.Board.Obstacle) obstacles++;
            Assert.That(obstacles, Is.EqualTo(1));
        }
        finally
        {
            stage.ObstacleFromMove = from;
            stage.ObstacleEveryMoves = every;
            stage.ObstacleMax = max;
        }
    }

    static IEnumerator Launch(string buttonName, bool timeAttack)
    {
        yield return SceneManager.LoadSceneAsync("Menu");
        yield return null;
        if (buttonName == "Btn_Rush" && Object.FindObjectsByType<Button>().Any(b => b.name == "Btn_Settings"))
        {
            Object.FindObjectsByType<Button>().Single(b => b.name == "Btn_Settings").onClick.Invoke();
            yield return null;
        }
        if (buttonName == "Btn_Play")
        {
            Canvas.ForceUpdateCanvases();
            var playText = Object.FindObjectsByType<Text>().Single(t => t.name == "PlayLabel");
            Assert.That(playText.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Wrap));
            Assert.That(playText.fontSize, Is.LessThan(playText.rectTransform.rect.height));
            ScreenCapture.CaptureScreenshot("/tmp/chromadrop-home-text.png");
            yield return null;
        }
        var button = Object.FindObjectsByType<Button>()
            .Single(b => b.name == buttonName);
        button.onClick.Invoke();
        yield return null;
        yield return null;
        var game = Object.FindAnyObjectByType<GameManager>();
        Assert.That(game, Is.Not.Null);
        Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
        Assert.That(game.TimeAttackMode, Is.EqualTo(timeAttack));
        Assert.That(game.BoardRef, Is.Not.Null);
        var theme = game.themes[0];
        Assert.That(ColorMatcher.Core.Board.W, Is.EqualTo(theme.Size + 1));
        Assert.That(ColorMatcher.Core.Board.H, Is.EqualTo(theme.Size + 1));
        var ui = Object.FindAnyObjectByType<GameUI>();
        Canvas.ForceUpdateCanvases();
        Rect boardRect;
        Assert.That(ui.BoardSlotRect(out boardRect), Is.True);
        if (buttonName == "Btn_Play" && UiTypography.Assets.nightBackground != null)
        {
            var first = ui.transform.Find("game/safe/playroot/item0").GetComponent<RectTransform>();
            var last = ui.transform.Find("game/safe/playroot/item3").GetComponent<RectTransform>();
            var a = new Vector3[4]; var b = new Vector3[4]; first.GetWorldCorners(a); last.GetWorldCorners(b);
            Assert.That((a[0].x + b[2].x) * .5f, Is.EqualTo(boardRect.center.x).Within(1f));
            Rect tray; Assert.That(ui.TraySlotRect(out tray), Is.True);
            Assert.That(b[2].x, Is.LessThan(tray.xMin));
            var view = Object.FindAnyObjectByType<BoardView>();
            var cells = (SpriteRenderer[][])typeof(BoardView).GetField("trayCells", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            Assert.That(cells[1].All(cell => !cell.enabled), Is.True);
            ScreenCapture.CaptureScreenshot("/tmp/chromadrop-centered-items.png");
            yield return null;
        }
        var slot = GameObject.Find("boardslot").GetComponent<RectTransform>();
        if (UiTypography.Assets != null && UiTypography.Assets.nightBackground != null)
        {
            Assert.That(boardRect.width, Is.EqualTo(boardRect.height).Within(1f));
            Assert.That(boardRect.xMin, Is.GreaterThanOrEqualTo(Screen.safeArea.xMin));
            Assert.That(boardRect.xMax, Is.LessThanOrEqualTo(Screen.safeArea.xMax));
            if (Screen.safeArea.height >= Screen.safeArea.width)
                Assert.That(boardRect.width, Is.GreaterThan(Screen.safeArea.width * .70f));
        }
        else
        {
            Assert.That(slot.anchorMax.x - slot.anchorMin.x,
                Is.EqualTo(theme.Grid.width * BlockTheme.BoardZoom / 1024f).Within(0.00001f));
        }
        var tile = GameObject.Find("t_0_0");
        // 숨쉬기 애니메이션을 기본 자세로 돌린 같은 프레임에서 표시 크기를 검사한다.
        Object.FindAnyObjectByType<BoardView>().SetIdle(false);
        Assert.That(tile.transform.localScale.x, Is.EqualTo(theme.BlockScale * (theme.Size + 1f) / theme.Size).Within(0.001f));
        var timer = GameObject.Find("timerbar").GetComponent<RectTransform>();
        var timerCorners = new Vector3[4]; timer.GetWorldCorners(timerCorners);
        Assert.That(timerCorners[0].y - boardRect.yMax, Is.GreaterThan(1f));
        Assert.That(game.objectiveFont, Is.Not.Null);
        Assert.That(game.missionWords, Has.Length.EqualTo(6));
        Assert.That(Object.FindAnyObjectByType<MissionBanner>(), Is.Not.Null);
        Assert.That(game.CurrentPiece, Is.Not.Null);
        Assert.That(game.MovesLeft, Is.GreaterThan(0));
        if (timeAttack) Assert.That(game.TimeLeftSec, Is.GreaterThan(0));
        LogAssert.NoUnexpectedReceived();
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (hadHearts) PlayerPrefs.SetString(Hearts.StorageKey, savedHearts);
        else PlayerPrefs.DeleteKey(Hearts.StorageKey);
        PlayerPrefs.Save();
        GameManager.LaunchHomePanel = 0;
        yield return SceneManager.LoadSceneAsync("Menu");
        if (Leaderboard.I != null) Object.Destroy(Leaderboard.I.gameObject);
        GameManager.LaunchTimeAttack = null;
        GameManager.LaunchFeverTest = false;
        yield return null;
    }
}
