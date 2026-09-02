// LeaderboardCrudTests.cs — 랭킹 REST 경로 CRUD 검증.
// 실제 Firebase 대신 Firestore REST 를 흉내내는 목 서버를 띄우고 Leaderboard 를 그대로 통과시킨다.
// 환경변수 CHROMADROP_FIREBASE_PROJECT 가 없으면 건너뛴다 (평소 테스트는 그대로 통과).
//
// 실행:
//   python3 Tools/fake-firestore.py 8765 &
//   CHROMADROP_FIREBASE_PROJECT=test-proj \
//   CHROMADROP_FIREBASE_APIKEY=test \
//   CHROMADROP_FIREBASE_BASE=http://127.0.0.1:8765/v1 \
//   CHROMADROP_FIREBASE_AUTHBASE=http://127.0.0.1:8765/v1 \
//   Unity -batchmode -runTests -testPlatform PlayMode -projectPath .

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class LeaderboardCrudTests
{
    const string Diff = "normal";

    Leaderboard Ready()
    {
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("CHROMADROP_FIREBASE_PROJECT")))
            Assert.Ignore("랭킹 서버 환경변수가 없어 건너뜀 (목 서버를 띄우고 다시 실행할 것)");
        var lb = Leaderboard.Create();
        Assert.IsTrue(lb.Configured, "설정이 로드되지 않았다: " + lb.LastError);
        return lb;
    }

    static ScoreEntry Mine(List<ScoreEntry> rows, string uid)
    {
        if (rows == null) return null;
        foreach (var r in rows) if (r.Uid == uid) return r;
        return null;
    }

    [UnityTest]
    public IEnumerator 익명계정을_만들고_uid를_받는다()
    {
        var lb = Ready();
        yield return lb.SignInAnonymously(null);
        Assert.IsTrue(lb.SignedIn, "익명 로그인 실패: " + lb.LastError);
        Assert.IsNotEmpty(lb.Uid);
    }

    [UnityTest]
    public IEnumerator 제출_조회_갱신이_동작한다()
    {
        var lb = Ready();
        yield return lb.SignInAnonymously(null);
        Assert.IsTrue(lb.SignedIn, "익명 로그인 실패: " + lb.LastError);

        // 실서버는 계정과 기록이 남아 있으므로 기존 점수를 기준으로 삼는다.
        // 고정값을 기대하면 두 번째 실행부터 깨진다.
        List<ScoreEntry> rows = null;
        yield return lb.FetchTop(false, Diff, r => rows = r);
        Assert.IsNotNull(rows, "조회 실패: " + lb.LastError);
        var prev = Mine(rows, lb.Uid);
        int baseScore = prev != null ? prev.Score : 0;

        // C — 기존보다 높은 기록 제출
        int first = baseScore + 1000;
        bool ok = false;
        yield return lb.Submit(false, Diff, first, 42, 1, r => ok = r);
        Assert.IsTrue(ok, "제출 실패: " + lb.LastError);

        // R — 목록에서 내 기록이 보인다
        yield return lb.FetchTop(false, Diff, r => rows = r);
        var me = Mine(rows, lb.Uid);
        Assert.IsNotNull(me, "내 기록이 목록에 없다");
        Assert.AreEqual(first, me.Score);
        Assert.AreEqual(PlayerAccount.Country, me.Country);

        // U — 더 높은 점수로 갱신된다
        int higher = first + 1000;
        yield return lb.Submit(false, Diff, higher, 42, 1, r => ok = r);
        Assert.IsTrue(ok);
        yield return lb.FetchTop(false, Diff, r => rows = r);
        Assert.AreEqual(higher, Mine(rows, lb.Uid).Score, "더 높은 점수가 반영되지 않았다");

        // U — 더 낮은 점수는 기존 기록을 덮어쓰지 않는다
        yield return lb.Submit(false, Diff, higher - 500, 42, 1, r => ok = r);
        Assert.IsTrue(ok);
        yield return lb.FetchTop(false, Diff, r => rows = r);
        Assert.AreEqual(higher, Mine(rows, lb.Uid).Score, "낮은 점수가 기존 기록을 덮어썼다");
    }

    [UnityTest]
    public IEnumerator 보드는_모드_난이도별로_분리된다()
    {
        var lb = Ready();
        yield return lb.SignInAnonymously(null);

        // easy 보드에는 아무것도 올리지 않고 hard 에만 올린다.
        yield return lb.Submit(false, "hard", 777, 1, 1, null);

        List<ScoreEntry> easy = null;
        yield return lb.FetchTop(false, "easy", r => easy = r);
        Assert.IsNull(Mine(easy, lb.Uid), "hard 기록이 easy 보드에 섞였다");

        List<ScoreEntry> hard = null;
        yield return lb.FetchTop(false, "hard", r => hard = r);
        var me = Mine(hard, lb.Uid);
        Assert.IsNotNull(me, "hard 보드에 내 기록이 없다");
        Assert.GreaterOrEqual(me.Score, 777, "hard 기록이 반영되지 않았다");
    }
}
