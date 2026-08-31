// Leaderboard.cs — Firebase 랭킹 (REST 전용, SDK 미사용).
// UnityWebRequest 로만 호출하므로 외부 플러그인/에셋 의존이 없다.
//
// 설정: Resources/leaderboard.json (Tools/make-leaderboard-config.sh 로 생성, 커밋 안 함)
//   { "databaseUrl": "https://<프로젝트>-default-rtdb.firebaseio.com", "apiKey": "<웹 API 키>" }
// 파일이 없으면 Configured=false 가 되어 랭킹 기능만 꺼지고 게임은 그대로 동작한다.
//
// 데이터 모델 (Realtime Database):
//   /boards/{boardId}/{uid} = { name, country, score, diff, seed, updated }
//   boardId = "ta" | "score_easy" | "score_normal" | "score_hard"
//
// ⚠ 점수는 클라이언트가 올린다. 서버 검증이 없으면 조작이 가능하다 (README/COMMANDS 참고).

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class Leaderboard : MonoBehaviour
{
    const string PrefRefresh = "lb_refresh";
    const string PrefUid = "lb_uid";
    const int TopN = 100;          // 국가 집계를 하려면 상위 한 줌으로는 부족하다
    const int Timeout = 10;

    public static Leaderboard I { get; private set; }

    /// <summary>설정 파일이 있고 URL/키가 채워져 있는가.</summary>
    public bool Configured { get; private set; }
    /// <summary>익명 로그인에 성공해 제출이 가능한 상태인가.</summary>
    public bool SignedIn { get { return !string.IsNullOrEmpty(idToken); } }
    public string Uid { get; private set; }
    public string LastError { get; private set; }

    string dbUrl, apiKey, idToken;
    float tokenExpiry;

    public static Leaderboard Create()
    {
        if (I != null) return I;
        var go = new GameObject("Leaderboard");
        DontDestroyOnLoad(go);
        I = go.AddComponent<Leaderboard>();
        I.LoadConfig();
        if (I.Configured) I.StartCoroutine(I.SignInAnonymously(null));
        return I;
    }

    void LoadConfig()
    {
        var ta = Resources.Load<TextAsset>("leaderboard");
        if (ta == null)
        {
            LastError = "설정 없음 (Resources/leaderboard.json)";
            return;
        }
        var m = Json.AsMap(Json.Parse(ta.text));
        dbUrl = Json.Str(m, "databaseUrl", "").TrimEnd('/');
        apiKey = Json.Str(m, "apiKey", "");
        Configured = dbUrl.Length > 0 && apiKey.Length > 0;
        if (!Configured) LastError = "설정 파일에 databaseUrl/apiKey 가 비어 있음";
        Uid = PlayerPrefs.GetString(PrefUid, "");
    }

    // ---------- 익명 인증 ----------

    /// <summary>저장된 refresh token 이 있으면 갱신하고, 없으면 새 게스트 계정을 만든다.</summary>
    public IEnumerator SignInAnonymously(Action<bool> done)
    {
        if (!Configured) { if (done != null) done(false); yield break; }

        var refresh = PlayerPrefs.GetString(PrefRefresh, "");
        if (!string.IsNullOrEmpty(refresh))
        {
            yield return Post("https://securetoken.googleapis.com/v1/token?key=" + apiKey,
                "grant_type=refresh_token&refresh_token=" + UnityWebRequest.EscapeURL(refresh),
                "application/x-www-form-urlencoded",
                res =>
                {
                    var m = Json.AsMap(Json.Parse(res));
                    idToken = Json.Str(m, "id_token", "");
                    Uid = Json.Str(m, "user_id", Uid);
                    StoreSession(Json.Str(m, "refresh_token", refresh), Json.Str(m, "expires_in", "3600"));
                });
            if (SignedIn) { if (done != null) done(true); yield break; }
        }

        // 새 게스트 계정
        yield return Post("https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + apiKey,
            "{\"returnSecureToken\":true}", "application/json",
            res =>
            {
                var m = Json.AsMap(Json.Parse(res));
                idToken = Json.Str(m, "idToken", "");
                Uid = Json.Str(m, "localId", "");
                PlayerPrefs.SetString(PrefUid, Uid);
                StoreSession(Json.Str(m, "refreshToken", ""), Json.Str(m, "expiresIn", "3600"));
            });

        if (done != null) done(SignedIn);
    }

    void StoreSession(string refreshToken, string expiresIn)
    {
        if (!string.IsNullOrEmpty(refreshToken)) PlayerPrefs.SetString(PrefRefresh, refreshToken);
        PlayerPrefs.Save();
        int secs;
        if (!int.TryParse(expiresIn, out secs)) secs = 3600;
        tokenExpiry = Time.realtimeSinceStartup + secs - 120;   // 만료 2분 전에 갱신
    }

    IEnumerator EnsureToken()
    {
        if (SignedIn && Time.realtimeSinceStartup < tokenExpiry) yield break;
        idToken = null;
        yield return SignInAnonymously(null);
    }

    // ---------- 제출 / 조회 ----------

    public static string BoardId(bool timeAttack, string difficulty)
    {
        return timeAttack ? "ta" : "score_" + difficulty;
    }

    /// <summary>내 기록을 올린다. 서버에 이미 더 높은 점수가 있으면 덮어쓰지 않는다.</summary>
    public IEnumerator Submit(bool timeAttack, string difficulty, int score, int seed, Action<bool> done)
    {
        if (!Configured) { if (done != null) done(false); yield break; }
        yield return EnsureToken();
        if (!SignedIn) { if (done != null) done(false); yield break; }

        string board = BoardId(timeAttack, difficulty);
        string url = dbUrl + "/boards/" + board + "/" + Uid + ".json?auth=" + idToken;

        // 기존 기록 확인 — 더 낮으면 올리지 않는다.
        int prev = -1;
        yield return Get(url, res =>
        {
            var m = Json.AsMap(Json.Parse(res));
            if (m != null) prev = (int)Json.Num(m, "score", -1);
        });
        if (prev >= score) { if (done != null) done(true); yield break; }

        long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        string body = "{"
            + "\"name\":" + Json.Quote(PlayerAccount.Name) + ","
            + "\"country\":" + Json.Quote(PlayerAccount.Country) + ","
            + "\"score\":" + score + ","
            + "\"diff\":" + Json.Quote(difficulty) + ","
            + "\"seed\":" + seed + ","
            + "\"updated\":" + now
            + "}";

        bool ok = false;
        yield return Put(url, body, res => ok = res != null);
        if (done != null) done(ok);
    }

    /// <summary>상위 TopN 을 받아 점수 내림차순으로 돌려준다.</summary>
    public IEnumerator FetchTop(bool timeAttack, string difficulty, Action<List<ScoreEntry>> done)
    {
        if (!Configured) { if (done != null) done(null); yield break; }
        yield return EnsureToken();

        string board = BoardId(timeAttack, difficulty);
        string url = dbUrl + "/boards/" + board + ".json"
                   + "?orderBy=" + UnityWebRequest.EscapeURL("\"score\"")
                   + "&limitToLast=" + TopN
                   + (SignedIn ? "&auth=" + idToken : "");

        List<ScoreEntry> rows = null;
        yield return Get(url, res =>
        {
            var m = Json.AsMap(Json.Parse(res));
            if (m == null) return;
            rows = new List<ScoreEntry>(m.Count);
            foreach (var kv in m)
            {
                var e = Json.AsMap(kv.Value);
                if (e == null) continue;
                rows.Add(new ScoreEntry
                {
                    Uid = kv.Key,
                    Name = Json.Str(e, "name", "?"),
                    Country = Json.Str(e, "country", "ZZ"),
                    Score = (int)Json.Num(e, "score", 0),
                    UpdatedMs = Json.Num(e, "updated", 0),
                });
            }
        });

        if (done != null) done(rows == null ? null : NationRanking.DedupeBest(rows));
    }

    // ---------- HTTP ----------

    IEnumerator Get(string url, Action<string> ok)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Timeout;
            yield return req.SendWebRequest();
            if (Failed(req)) yield break;
            ok(req.downloadHandler.text);
        }
    }

    IEnumerator Post(string url, string body, string contentType, Action<string> ok)
    {
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", contentType);
            req.timeout = Timeout;
            yield return req.SendWebRequest();
            if (Failed(req)) yield break;
            ok(req.downloadHandler.text);
        }
    }

    IEnumerator Put(string url, string body, Action<string> ok)
    {
        using (var req = UnityWebRequest.Put(url, body))
        {
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Timeout;
            yield return req.SendWebRequest();
            if (Failed(req)) yield break;
            ok(req.downloadHandler.text);
        }
    }

    bool Failed(UnityWebRequest req)
    {
        if (req.result == UnityWebRequest.Result.Success) { LastError = null; return false; }
        LastError = req.error;
        Debug.LogWarning("[Leaderboard] " + req.url.Split('?')[0] + " → " + req.error);
        return true;
    }
}
