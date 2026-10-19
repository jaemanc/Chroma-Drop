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
    [SetUp]
    public void DisableLiveLeaderboard()
    {
        // 씬 검증 중 운영 서버에 게스트 계정을 만들지 않는다.
        if (Leaderboard.I != null) Object.DestroyImmediate(Leaderboard.I.gameObject);
        var offline = new GameObject("OfflineLeaderboard").AddComponent<Leaderboard>();
        Object.DontDestroyOnLoad(offline.gameObject);
        typeof(Leaderboard).GetProperty("I").SetValue(null, offline);
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
        float timeLeft = game.TimeLeftSec;
        float pieceLeft = game.PieceTimerFrac;
        yield return new WaitForSecondsRealtime(0.08f);
        Assert.That(game.Paused, Is.True);
        Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
        Assert.That(game.TimeLeftSec, Is.EqualTo(timeLeft).Within(0.001f));
        Assert.That(game.PieceTimerFrac, Is.EqualTo(pieceLeft).Within(0.001f));
        Assert.That(game.TryStamp(0, 0), Is.False);
        Assert.That(game.UseItem(ShopItem.Hammer), Is.False);
        ui.GetComponentsInChildren<Button>().Single(b => b.name == "resume").onClick.Invoke();
        Assert.That(game.Paused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));

        typeof(GameManager).GetMethod("StartFever", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        float feverScale = Time.timeScale;
        pause.onClick.Invoke();
        float feverLeft = game.FeverLeftSec;
        yield return new WaitForSecondsRealtime(0.08f);
        game.ResumeGame();
        Assert.That(game.FeverOn, Is.True);
        Assert.That(game.FeverLeftSec, Is.EqualTo(feverLeft).Within(0.04f));
        Assert.That(Time.timeScale, Is.EqualTo(feverScale));
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

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        yield return SceneManager.LoadSceneAsync("Menu");
        if (Leaderboard.I != null) Object.Destroy(Leaderboard.I.gameObject);
        GameManager.LaunchTimeAttack = null;
        yield return null;
    }
}
