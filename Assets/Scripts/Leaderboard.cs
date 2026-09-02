// Leaderboard.cs — Firebase 랭킹 (Firestore REST 전용, SDK 미사용).
// UnityWebRequest 로만 호출하므로 외부 플러그인/에셋 의존이 없다.
//
// 설정 (앞쪽이 우선):
//   1) 환경변수 CHROMADROP_FIREBASE_PROJECT / _APIKEY / _BASE / _AUTHBASE
//   2) Resources/leaderboard.json (Tools/make-leaderboard-config.sh 로 생성, 커밋 안 함)
//      { "projectId": "<프로젝트 ID>", "apiKey": "<웹 API 키>" }
// 둘 다 없으면 Configured=false 가 되어 랭킹 기능만 꺼지고 게임은 그대로 동작한다.
// _BASE / _AUTHBASE 는 에뮬레이터나 테스트용 목 서버를 가리킬 때만 준다 (평소 비움).
//
// 데이터 모델 (Firestore):
//   컬렉션 boards_{boardId} / 문서 {uid} = { name, country, score, diff, seed, updated }
//   boardId = "ta" | "score_easy" | "score_normal" | "score_hard"
//   score 단일 필드 정렬이라 복합 색인은 필요 없다.
//
// ⚠ 점수는 클라이언트가 올린다. 서버 검증이 없으면 조작이 가능하다 (COMMANDS.md 참고).

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

    string projectId, apiKey, idToken;
    string fsBase = "https://firestore.googleapis.com/v1";
    string signUpBase = "https://identitytoolkit.googleapis.com/v1";
    string tokenBase = "https://securetoken.googleapis.com/v1";
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
        projectId = Env("CHROMADROP_FIREBASE_PROJECT");
        apiKey = Env("CHROMADROP_FIREBASE_APIKEY");
        string baseOverride = Env("CHROMADROP_FIREBASE_BASE");
        string authBase = Env("CHROMADROP_FIREBASE_AUTHBASE");

        if (projectId.Length == 0 || apiKey.Length == 0)
        {
            var ta = Resources.Load<TextAsset>("leaderboard");
            if (ta == null)
            {
                LastError = "설정 없음 (환경변수도 Resources/leaderboard.json 도 없음)";
                return;
            }
            var m = Json.AsMap(Json.Parse(ta.text));
            if (projectId.Length == 0) projectId = Json.Str(m, "projectId", "");
            if (apiKey.Length == 0) apiKey = Json.Str(m, "apiKey", "");
            if (baseOverride.Length == 0) baseOverride = Json.Str(m, "firestoreBase", "");
            if (authBase.Length == 0) authBase = Json.Str(m, "authBase", "");
        }

        if (baseOverride.Length > 0) fsBase = baseOverride.TrimEnd('/');
        if (authBase.Length > 0) signUpBase = tokenBase = authBase.TrimEnd('/');

        Configured = projectId.Length > 0 && apiKey.Length > 0;
        if (!Configured) LastError = "projectId/apiKey 가 비어 있음";
        Uid = PlayerPrefs.GetString(PrefUid, "");
    }

    static string Env(string name)
    {
        return Environment.GetEnvironmentVariable(name) ?? "";
    }

    // ---------- 익명 인증 ----------

    /// <summary>저장된 refresh token 이 있으면 갱신하고, 없으면 새 게스트 계정을 만든다.</summary>
    public IEnumerator SignInAnonymously(Action<bool> done)
    {
        if (!Configured) { if (done != null) done(false); yield break; }
        idToken = null;   // 갱신에 실패했는데 낡은 토큰이 남아 성공으로 보이면 안 된다

        var refresh = PlayerPrefs.GetString(PrefRefresh, "");
        if (!string.IsNullOrEmpty(refresh))
        {
            yield return Post(tokenBase + "/token?key=" + apiKey,
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
        yield return Post(signUpBase + "/accounts:signUp?key=" + apiKey,
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

    string DocPath(string board) { return "boards_" + board; }
    string DocsBase { get { return fsBase + "/projects/" + projectId + "/databases/(default)/documents"; } }

    /// <summary>내 기록을 올린다. 서버에 이미 더 높은 점수가 있으면 덮어쓰지 않는다.</summary>
    public IEnumerator Submit(bool timeAttack, string difficulty, int score, int seed, Action<bool> done)
    {
        if (!Configured) { if (done != null) done(false); yield break; }
        yield return EnsureToken();
        if (!SignedIn) { if (done != null) done(false); yield break; }

        string col = DocPath(BoardId(timeAttack, difficulty));
        string docUrl = DocsBase + "/" + col + "/" + Uid;

        // 기존 기록 확인 — 더 낮으면 올리지 않는다. 문서가 없으면 404 라 prev 는 -1 로 남는다.
        int prev = -1;
        yield return Get(docUrl, res =>
        {
            var f = Fields(Json.AsMap(Json.Parse(res)));
            if (f != null) prev = IntField(f, "score");
        });
        if (prev >= score) { if (done != null) done(true); yield break; }

        long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        string body = "{\"fields\":{"
            + "\"name\":{\"stringValue\":" + Json.Quote(PlayerAccount.Name) + "},"
            + "\"country\":{\"stringValue\":" + Json.Quote(PlayerAccount.Country) + "},"
            + "\"score\":{\"integerValue\":\"" + score + "\"},"
            + "\"diff\":{\"stringValue\":" + Json.Quote(difficulty) + "},"
            + "\"seed\":{\"integerValue\":\"" + seed + "\"},"
            + "\"updated\":{\"integerValue\":\"" + now + "\"}"
            + "}}";

        // updateMask 를 명시해야 덮어쓰기 범위가 결정적이다. 문서가 없으면 새로 만든다.
        string mask = "?updateMask.fieldPaths=name&updateMask.fieldPaths=country"
                    + "&updateMask.fieldPaths=score&updateMask.fieldPaths=diff"
                    + "&updateMask.fieldPaths=seed&updateMask.fieldPaths=updated";

        bool ok = false;
        yield return Send(docUrl + mask, "PATCH", body, "application/json", res => ok = res != null);
        if (done != null) done(ok);
    }

    /// <summary>상위 TopN 을 점수 내림차순으로 받아온다.</summary>
    public IEnumerator FetchTop(bool timeAttack, string difficulty, Action<List<ScoreEntry>> done)
    {
        if (!Configured) { if (done != null) done(null); yield break; }
        yield return EnsureToken();

        string col = DocPath(BoardId(timeAttack, difficulty));
        string body = "{\"structuredQuery\":{"
            + "\"from\":[{\"collectionId\":" + Json.Quote(col) + "}],"
            + "\"orderBy\":[{\"field\":{\"fieldPath\":\"score\"},\"direction\":\"DESCENDING\"}],"
            + "\"limit\":" + TopN
            + "}}";

        List<ScoreEntry> rows = null;
        yield return Send(DocsBase + ":runQuery", "POST", body, "application/json", res =>
        {
            // 응답은 배열이고, 결과가 없는 경우 readTime 만 든 원소가 온다.
            var arr = Json.Parse(res) as List<object>;
            if (arr == null) return;
            rows = new List<ScoreEntry>(arr.Count);
            foreach (var item in arr)
            {
                var m = Json.AsMap(item);
                if (m == null || !m.ContainsKey("document")) continue;
                var doc = Json.AsMap(m["document"]);
                var f = Fields(doc);
                if (f == null) continue;
                string name = Json.Str(doc, "name", "");
                int slash = name.LastIndexOf('/');
                rows.Add(new ScoreEntry
                {
                    Uid = slash >= 0 ? name.Substring(slash + 1) : name,
                    Name = StrField(f, "name", "?"),
                    Country = StrField(f, "country", "ZZ"),
                    Score = IntField(f, "score"),
                    UpdatedMs = LongField(f, "updated"),
                });
            }
        });

        if (done != null) done(rows == null ? null : NationRanking.DedupeBest(rows));
    }

    // ---------- Firestore 값 표기 (모든 값이 {타입:값} 으로 한 겹 싸여 있다) ----------

    static Dictionary<string, object> Fields(Dictionary<string, object> doc)
    {
        if (doc == null || !doc.ContainsKey("fields")) return null;
        return Json.AsMap(doc["fields"]);
    }

    static Dictionary<string, object> Val(Dictionary<string, object> f, string key)
    {
        object v;
        return (f != null && f.TryGetValue(key, out v)) ? Json.AsMap(v) : null;
    }

    static string StrField(Dictionary<string, object> f, string key, string fallback)
    {
        return Json.Str(Val(f, key), "stringValue", fallback);
    }

    static long LongField(Dictionary<string, object> f, string key)
    {
        var v = Val(f, key);
        var s = Json.Str(v, "integerValue", null);   // integerValue 는 문자열로 온다
        long n;
        if (s != null && long.TryParse(s, out n)) return n;
        return Json.Num(v, "doubleValue", 0);
    }

    static int IntField(Dictionary<string, object> f, string key) { return (int)LongField(f, key); }

    // ---------- HTTP ----------
    // 인증 엔드포인트는 Bearer 를 붙이지 않는다 (Post). Firestore 호출만 붙인다 (Get/Send).

    IEnumerator Get(string url, Action<string> ok)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            Auth(req);
            req.timeout = Timeout;
            yield return req.SendWebRequest();
            if (Failed(req)) yield break;
            ok(req.downloadHandler.text);
        }
    }

    IEnumerator Send(string url, string method, string body, string contentType, Action<string> ok)
    {
        using (var req = new UnityWebRequest(url, method))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", contentType);
            Auth(req);
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

    void Auth(UnityWebRequest req)
    {
        if (!string.IsNullOrEmpty(idToken)) req.SetRequestHeader("Authorization", "Bearer " + idToken);
    }

    bool Failed(UnityWebRequest req)
    {
        if (req.result == UnityWebRequest.Result.Success) { LastError = null; return false; }
        LastError = req.error;
        // 404 는 '아직 기록이 없다'는 정상 흐름이다.
        if (req.responseCode != 404)
            Debug.LogWarning("[Leaderboard] " + req.url.Split('?')[0] + " → " + req.error);
        return true;
    }
}
