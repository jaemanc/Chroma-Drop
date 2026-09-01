// GameUI.cs — 런타임 생성 uGUI (씬/프리팹/폰트 에셋 의존 없음).
// 홈 / 게임 HUD / 결과 3개 패널을 코드로 조립. 노치 대응(SafeArea) 포함.
// 한글 표시는 OS 폰트(iOS: Apple SD Gothic Neo, Android: Noto Sans CJK 등)를 동적 로드,
// 없으면 내장 폰트 + 영문 라벨로 폴백.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ColorMatcher.Core;

public class GameUI : MonoBehaviour
{
    static readonly Color Accent = new Color(0.28f, 0.72f, 0.52f);

    GameManager gm;
    Font font;
    Sprite roundBig, roundSmall;                                   // 버튼 9슬라이스
    readonly List<UiButton> buttons = new List<UiButton>();
    readonly Dictionary<UiButton, UiKind> buttonKinds = new Dictionary<UiButton, UiKind>();

    GameObject homePanel, gamePanel, resultPanel;
    Text scoreText, subText, rightText, bestHomeText, resultTitle, resultScore, resultBest;
    Text chainPopup;
    Coroutine chainCo;
    Image timerFill;
    GameObject timerBar;      // 타임어택=남은 시간 / 횟수 모드=조각 제한시간
    UiButton[] modeBtns;

    // 랭킹
    public enum SubmitState { Off, Pending, Sending, Done, Failed }

    /// <summary>리더보드 한 줄. 순위·국가배지·이름·점수를 각각 따로 그린다.</summary>
    struct RankRow
    {
        public Image Bg, Badge;
        public Text Rank, Code, Name, Score;
        public void SetActive(bool v) { Bg.gameObject.SetActive(v); }
    }
    GameObject rankPanel, countryPanel;
    Text submitText, rankTitle, rankSubTitle, rankEmpty;
    RankRow[] rankRows;
    RankRow myRow;
    Text myRowLabel;
    Button adBtn;
    Text adBtnLabel;
    GameObject adPanel;
    Text adCountdown;
    UiButton rankTabMe, rankTabNation;
    Image homeBadge; Text homeBadgeText;
    bool rankNationTab;
    Coroutine rankCo;
    const int RankRowCount = 12;
    RectTransform nextRoot;
    readonly List<Image> nextCells = new List<Image>();

    public static GameUI Create(GameManager gm)
    {
        var go = new GameObject("GameUI");
        var ui = go.AddComponent<GameUI>();
        ui.gm = gm;
        ui.Build();
        return ui;
    }

    void LoadFont()
    {
        string[] prefer = {
            "Apple SD Gothic Neo", "AppleSDGothicNeo",       // iOS/macOS
            "Noto Sans CJK KR", "Noto Sans KR", "NotoSansCJKkr-Regular", // Android
            "Malgun Gothic", "NanumGothic", "Droid Sans Fallback"
        };
        var installed = new HashSet<string>(Font.GetOSInstalledFontNames() ?? new string[0]);
        foreach (var n in prefer)
            if (installed.Contains(n))
            {
                font = Font.CreateDynamicFontFromOSFont(n, 64);
                if (font != null) break;
            }
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void Build()
    {
        LoadFont();

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        roundBig = UiTheme.RoundedSprite(UiTheme.Radius);
        roundSmall = UiTheme.RoundedSprite(4f);

        BuildGamePanel();
        BuildHomePanel();
        BuildResultPanel();
        BuildRankPanel();
        BuildCountryPanel();
        BuildAdPanel();
        homePanel.SetActive(false);
        gamePanel.SetActive(false);
        resultPanel.SetActive(false);
        rankPanel.SetActive(false);
        countryPanel.SetActive(false);
        adPanel.SetActive(false);
        FinishButtons();
    }

    // ---------- 패널 전환 ----------

    public void ShowHome()
    {
        homePanel.SetActive(true);
        gamePanel.SetActive(false);
        resultPanel.SetActive(false);
        rankPanel.SetActive(false);
        countryPanel.SetActive(false);
        adPanel.SetActive(false);
        RefreshHomeButtons();
    }

    public void ShowGame()
    {
        homePanel.SetActive(false);
        gamePanel.SetActive(true);
        resultPanel.SetActive(false);
        rankPanel.SetActive(false);
        countryPanel.SetActive(false);
        adPanel.SetActive(false);
    }

    public void ShowResult(bool win, bool ta, int score, int best, bool newBest)
    {
        resultPanel.SetActive(true);
        resultTitle.text = ta ? "TIME'S UP!"
                              : (win ? "CLEAR!" : "FAILED");
        resultTitle.color = ta || win ? Accent : new Color(0.9f, 0.45f, 0.4f);
        resultScore.text = score.ToString("N0") + (newBest ? "  ★" + "NEW BEST!" : "");
        resultBest.text = "BEST " + best.ToString("N0");

        // 결과 카드를 잠깐 보여준 뒤 리더보드를 띄운다.
        if (Leaderboard.I != null && Leaderboard.I.Configured)
            StartCoroutine(OpenRankAfter(1.1f));
    }

    // ---------- 게임 HUD ----------

    void BuildGamePanel()
    {
        gamePanel = NewRT("game", transform).gameObject;
        Stretch((RectTransform)gamePanel.transform);
        var safe = NewRT("safe", gamePanel.transform);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        var top = NewImage("top", safe, new Color(0, 0, 0, 0.35f));
        Place(top.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 240));

        scoreText = NewText("score", top.transform, "0", 92, TextAnchor.UpperLeft, Color.white);
        Place(scoreText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -24), new Vector2(620, 105));

        subText = NewText("sub", top.transform, "", 42, TextAnchor.UpperLeft, new Color(1, 1, 1, 0.75f));
        Place(subText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -138), new Vector2(620, 60));

        rightText = NewText("right", top.transform, "", 54, TextAnchor.UpperRight, Color.white);
        Place(rightText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -28), new Vector2(500, 66));

        nextRoot = NewRT("next", top.transform);
        Place(nextRoot, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -112), new Vector2(320, 110));


        var barBg = NewImage("barbg", top.transform, new Color(1, 1, 1, 0.12f));
        Place(barBg.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 12));
        timerBar = barBg.gameObject;
        timerFill = NewImage("fill", barBg.transform, Accent);
        Stretch(timerFill.rectTransform);
        timerFill.rectTransform.pivot = new Vector2(0, 0.5f);

        var rot = NewButton("rotate", safe, "ROTATE", UiKind.Secondary, () => gm.RotateCurrent());
        Place((RectTransform)rot.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 44), new Vector2(280, 150));

        var home = NewButton("home", safe, "HOME", UiKind.Secondary, () => gm.GoHome());
        Place((RectTransform)home.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(36, 44), new Vector2(170, 110));

        // 연쇄/득점 팝업 (보드 중앙 근처에서 튀어오르며 사라짐)
        chainPopup = NewText("chainpop", safe, "", 100, TextAnchor.MiddleCenter, Accent);
        Place(chainPopup.rectTransform, new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 180));
        chainPopup.gameObject.SetActive(false);
    }

    /// <summary>연쇄/큰 득점 시 튀어오르는 팝업 (타격감). GameManager가 파괴 직후 호출.</summary>
    public void ShowChainPopup(int chain, int scoreGained)
    {
        if (chainPopup == null) return;
        chainPopup.text = chain >= 2 ? ("CHAIN " + "x" + chain) : ("+" + scoreGained.ToString("N0"));
        chainPopup.color = chain >= 3 ? new Color(1f, 0.82f, 0.3f) : Accent;
        if (chainCo != null) StopCoroutine(chainCo);
        chainCo = StartCoroutine(ChainPopupCo());
    }

    IEnumerator ChainPopupCo()
    {
        var rt = chainPopup.rectTransform;
        chainPopup.gameObject.SetActive(true);
        float dur = 0.7f;
        float t = 0;
        Color baseCol = chainPopup.color;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float scale = Mathf.Lerp(1.45f, 1.0f, Mathf.Clamp01(k * 3f));
            rt.localScale = Vector3.one * scale;
            rt.anchoredPosition = new Vector2(0, k * 70f);
            var c = baseCol; c.a = 1f - Mathf.Clamp01((k - 0.55f) / 0.45f); chainPopup.color = c;
            yield return null;
        }
        chainPopup.gameObject.SetActive(false);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        chainCo = null;
    }

    /// <summary>매 프레임 HUD 갱신 (Playing 중 GameManager가 호출)</summary>
    public void UpdateHud(GameManager g)
    {
        scoreText.text = g.Score.ToString("N0");
        timerBar.SetActive(true);
        float frac;
        if (g.TimeAttackMode)
        {
            subText.text = "TIME ATTACK";
            int sec = Mathf.CeilToInt(g.TimeLeftSec);
            rightText.text = (sec / 60) + ":" + (sec % 60).ToString("00");
            frac = g.TimeLeftSec / (Rules.TimeAttackMs / 1000f);
        }
        else
        {
            subText.text = "GOAL " + g.Goal.ToString("N0");
            rightText.text = "MOVES " + g.MovesLeft;
            frac = g.PieceTimerFrac;   // 다 지나가면 조각이 버려진다
        }
        frac = Mathf.Clamp01(frac);
        timerFill.rectTransform.localScale = new Vector3(frac, 1, 1);
        timerFill.color = Color.Lerp(new Color(0.9f, 0.4f, 0.35f), Accent, frac);
    }

    /// <summary>다음 조각 미리보기 (미니 셀 그리드)</summary>
    public void SetNext(IReadOnlyList<Piece> pieces, Color[] palette)
    {
        int need = 0;
        for (int i = 0; i < pieces.Count && i < 2; i++) need += pieces[i].Cells.Count;
        while (nextCells.Count < need)
        {
            var img = NewImage("n" + nextCells.Count, nextRoot, Color.white);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(20, 20);
            nextCells.Add(img);
        }
        int idx = 0;
        for (int i = 0; i < pieces.Count && i < 2; i++)
        {
            var p = pieces[i];
            foreach (var c in p.Cells)
            {
                var img = nextCells[idx++];
                img.gameObject.SetActive(true);
                img.color = palette[p.Color];
                img.rectTransform.anchoredPosition = new Vector2(-(i * 130) - (3 - c.X) * 24, -(3 - c.Y) * 24);
            }
        }
        for (int i = idx; i < nextCells.Count; i++) nextCells[i].gameObject.SetActive(false);
    }

    // ---------- 홈 ----------

    void BuildHomePanel()
    {
        var homeBg = NewImage("homebg", transform, Color.white);
        homeBg.sprite = MakeGradientSprite(new Color(0.10f, 0.09f, 0.19f),   // 위: 짙은 남보라
                                           new Color(0.04f, 0.04f, 0.07f));  // 아래: 거의 검정
        homeBg.type = Image.Type.Simple;
        homePanel = homeBg.gameObject;
        Stretch((RectTransform)homePanel.transform);

        // 게임 블록이 천천히 떠다니는 배경. 팔레트와 같은 방식으로 색을 만들어 게임과 톤을 맞춘다.
        var blocksRoot = NewRT("bgblocks", homePanel.transform);
        Stretch(blocksRoot);
        blocksRoot.gameObject.AddComponent<BgBlocks>().Build(BoardView.MakeTileSprite());

        var safe = NewRT("safe", homePanel.transform);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        var title = NewText("title", safe, "CHROMA DROP", 104, TextAnchor.MiddleCenter, Color.white);
        Place(title.rectTransform, new Vector2(0.5f, 0.80f), new Vector2(0.5f, 0.80f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 130));
        title.gameObject.AddComponent<UiPulse>();

        var underline = NewImage("titleline", safe, Accent);
        Place(underline.rectTransform, new Vector2(0.5f, 0.775f), new Vector2(0.5f, 0.775f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360, 8));

        var sub = NewText("subtitle", safe, "COLOR MATCHER", 46, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f));
        Place(sub.rectTransform, new Vector2(0.5f, 0.74f), new Vector2(0.5f, 0.74f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 70));

        modeBtns = new UiButton[2];
        string[] modeLabels = { "MOVES", "TIME ATTACK" };
        for (int i = 0; i < 2; i++)
        {
            bool ta = i == 1;
            var b = NewButton("mode" + i, safe, modeLabels[i], UiKind.Secondary, () => { gm.timeAttack = ta; RefreshHomeButtons(); });
            Place((RectTransform)b.transform, new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -190 : 190, 0), new Vector2(350, 110));
            modeBtns[i] = b.GetComponent<UiButton>();
        }

        bestHomeText = NewText("best", safe, "", 42, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.65f));
        Place(bestHomeText.rectTransform, new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 60));

        var start = NewButton("start", safe, "START", UiKind.Primary, () => gm.StartGame());
        Place((RectTransform)start.transform, new Vector2(0.5f, 0.29f), new Vector2(0.5f, 0.29f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 150));
        start.gameObject.AddComponent<UiPulse>();

        // 국가 배지 — 누르면 국가를 바꿀 수 있다 (기본값은 기기 로케일에서 추정).
        var badgeBtn = NewPlainButton("country", safe, new Color(1, 1, 1, 0.05f), () => ShowCountryPicker());
        Place((RectTransform)badgeBtn.transform, new Vector2(0.5f, 0.19f), new Vector2(0.5f, 0.19f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430, 96));
        homeBadge = NewImage("badge", badgeBtn.transform, Color.white);
        Place(homeBadge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0, 0), new Vector2(96, 68));
        homeBadgeText = NewText("badgecode", homeBadge.transform, "", 40, TextAnchor.MiddleCenter, Color.white);
        Stretch(homeBadgeText.rectTransform);
        var nameText = NewText("acctname", badgeBtn.transform, "", 38, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.7f));
        Place(nameText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(112, 0), new Vector2(320, 60));
        nameText.text = PlayerAccount.Name;

        var rankBtn = NewButton("rankhome", safe, "LEADERBOARD", UiKind.Secondary, () => ShowRanking(false));
        Place((RectTransform)rankBtn.transform, new Vector2(0.5f, 0.115f), new Vector2(0.5f, 0.115f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430, 96));

        var footer = NewText("footer", safe, "v1.0.0  ·  jaemanc", 34, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.35f));
        Place(footer.rectTransform, new Vector2(0.5f, 0.06f), new Vector2(0.5f, 0.06f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 50));
    }

    void RefreshHomeButtons()
    {
        if (modeBtns == null) return;
        for (int i = 0; i < 2; i++)
            modeBtns[i].SetKind((i == 1) == gm.timeAttack ? UiKind.Primary : UiKind.Secondary);
        bestHomeText.text = "BEST " + gm.BestForSelection().ToString("N0");
        RefreshBadge();
    }

    void RefreshBadge()
    {
        if (homeBadge == null) return;
        string c = PlayerAccount.Country;
        homeBadge.color = PlayerAccount.BadgeColor(c);
        homeBadgeText.text = c;
    }

    // ---------- 결과 ----------

    void BuildResultPanel()
    {
        resultPanel = NewImage("resultdim", transform, new Color(0, 0, 0, 0.65f)).gameObject;
        Stretch((RectTransform)resultPanel.transform);

        var card = NewImage("card", resultPanel.transform, new Color(0.10f, 0.10f, 0.15f, 0.97f));
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(840, 760));

        resultTitle = NewText("rtitle", card.transform, "", 92, TextAnchor.MiddleCenter, Accent);
        Place(resultTitle.rectTransform, new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 120));

        resultScore = NewText("rscore", card.transform, "", 78, TextAnchor.MiddleCenter, Color.white);
        Place(resultScore.rectTransform, new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 100));

        resultBest = NewText("rbest", card.transform, "", 46, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.65f));
        Place(resultBest.rectTransform, new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 60));

        submitText = NewText("rsubmit", card.transform, "", 36, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f));
        Place(submitText.rectTransform, new Vector2(0.5f, 0.385f), new Vector2(0.5f, 0.385f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 50));

        var rankB = NewButton("rrank", card.transform, "LEADERBOARD", UiKind.Secondary, () => ShowRanking(false));
        Place((RectTransform)rankB.transform, new Vector2(0.5f, 0.325f), new Vector2(0.5f, 0.325f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430, 84));

        var retry = NewButton("retry", card.transform, "RETRY", UiKind.Primary, () => gm.StartGame());
        Place((RectTransform)retry.transform, new Vector2(0.5f, 0.19f), new Vector2(0.5f, 0.19f), new Vector2(0.5f, 0.5f), new Vector2(-185, 0), new Vector2(340, 130));

        var homeB = NewButton("rhome", card.transform, "HOME", UiKind.Secondary, () => gm.GoHome());
        Place((RectTransform)homeB.transform, new Vector2(0.5f, 0.19f), new Vector2(0.5f, 0.19f), new Vector2(0.5f, 0.5f), new Vector2(185, 0), new Vector2(340, 130));
    }

    // ---------- 랭킹 ----------

    // 순위 표식 색 — 1~3위만 강조하고 나머지는 무채색
    static readonly Color[] MedalColors = {
        Palette.Hex(0xE8C24A), Palette.Hex(0xB9C0C8), Palette.Hex(0xC98A57),
    };
    const float RowH = 74f;

    void BuildRankPanel()
    {
        rankPanel = NewImage("rankdim", transform, new Color(0, 0, 0, 0.82f)).gameObject;
        Stretch((RectTransform)rankPanel.transform);

        var card = NewImage("rcard", rankPanel.transform, new Color(0.11f, 0.11f, 0.16f, 0.99f));
        card.sprite = roundBig; card.type = Image.Type.Sliced;
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920, 1420));

        rankTitle = NewText("rktitle", card.transform, "LEADERBOARD", 54, TextAnchor.MiddleCenter, Color.white);
        Place(rankTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(860, 70));

        var sub = NewText("rksub", card.transform, "", 34, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.45f));
        Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -98), new Vector2(860, 44));
        rankSubTitle = sub;

        var meBtn = NewButton("tabme", card.transform, "PLAYERS", UiKind.Primary, () => { rankNationTab = false; RefreshRank(); });
        Place((RectTransform)meBtn.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-158, -168), new Vector2(300, 84));
        rankTabMe = meBtn.GetComponent<UiButton>();

        var natBtn = NewButton("tabnat", card.transform, "NATIONS", UiKind.Secondary, () => { rankNationTab = true; RefreshRank(); });
        Place((RectTransform)natBtn.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(158, -168), new Vector2(300, 84));
        rankTabNation = natBtn.GetComponent<UiButton>();

        rankRows = new RankRow[RankRowCount];
        for (int i = 0; i < RankRowCount; i++)
            rankRows[i] = MakeRankRow(card.transform, "row" + i, -238 - i * RowH, false);

        rankEmpty = NewText("rkempty", card.transform, "", 36, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f));
        Place(rankEmpty.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 120));

        // ---- 맨 아래: 내 점수 고정 줄 ----
        var divider = NewImage("rkdiv", card.transform, new Color(1, 1, 1, 0.10f));
        Place(divider.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 246), new Vector2(860, 2));

        myRowLabel = NewText("mylabel", card.transform, "YOU", 28, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.45f));
        Place(myRowLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-390, 218), new Vector2(200, 36));

        myRow = MakeRankRow(card.transform, "myrow", 0, true);
        var mrt = myRow.Bg.rectTransform;
        mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0f);
        mrt.anchoredPosition = new Vector2(0, 152);

        // ---- 광고 버튼 ----
        adBtn = NewButton("adbtn", card.transform, "WATCH AD", UiKind.Primary, OnAdButton);
        Place((RectTransform)adBtn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 34), new Vector2(560, 108));
        adBtnLabel = adBtn.transform.Find("face/label").GetComponent<Text>();

        var close = NewButton("rkclose", card.transform, "CLOSE", UiKind.Secondary, () => rankPanel.SetActive(false));
        Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28, -30), new Vector2(150, 76));
    }

    /// <summary>순위 줄 하나: 배경 + 순위 + 국가배지 + 이름 + 점수</summary>
    RankRow MakeRankRow(Transform parent, string name, float y, bool highlight)
    {
        var r = new RankRow();
        r.Bg = NewImage(name, parent, new Color(1, 1, 1, highlight ? 0.10f : 0.04f));
        r.Bg.sprite = roundSmall; r.Bg.type = Image.Type.Sliced;
        Place(r.Bg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(852, RowH - 8));

        r.Rank = NewText("rk", r.Bg.transform, "", 34, TextAnchor.MiddleCenter, Color.white);
        Place(r.Rank.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14, 0), new Vector2(64, 48));

        r.Badge = NewImage("badge", r.Bg.transform, Color.white);
        r.Badge.sprite = roundSmall; r.Badge.type = Image.Type.Sliced;
        Place(r.Badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(88, 0), new Vector2(74, 44));
        r.Code = NewText("code", r.Badge.transform, "", 28, TextAnchor.MiddleCenter, Color.white);
        Stretch(r.Code.rectTransform);

        r.Name = NewText("nm", r.Bg.transform, "", 34, TextAnchor.MiddleLeft, Color.white);
        Place(r.Name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(176, 0), new Vector2(420, 48));

        r.Score = NewText("sc", r.Bg.transform, "", 36, TextAnchor.MiddleRight, Color.white);
        Place(r.Score.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18, 0), new Vector2(280, 48));
        return r;
    }

    void FillRow(RankRow r, int rank, string code, string name, int score, bool isMe)
    {
        r.SetActive(true);
        r.Rank.text = rank > 0 ? rank.ToString() : "-";
        r.Rank.color = rank >= 1 && rank <= 3 ? MedalColors[rank - 1] : new Color(1, 1, 1, 0.55f);
        r.Badge.color = PlayerAccount.BadgeColor(code);
        r.Code.text = code;
        r.Name.text = Trim(name, 14);
        r.Name.color = isMe ? Accent : Color.white;
        r.Score.text = score.ToString("N0");
        r.Score.color = isMe ? Accent : Color.white;
    }

    IEnumerator OpenRankAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (gm.Phase == GamePhase.Result) ShowRanking(false);
    }

    public void ShowRanking(bool nation)
    {
        rankNationTab = nation;
        rankPanel.SetActive(true);
        rankPanel.transform.SetAsLastSibling();
        RefreshRank();
    }

    void RefreshRank()
    {
        rankTabMe.SetKind(rankNationTab ? UiKind.Secondary : UiKind.Primary);
        rankTabNation.SetKind(rankNationTab ? UiKind.Primary : UiKind.Secondary);
        rankSubTitle.text = gm.timeAttack ? "TIME ATTACK" : "MOVES  ·  GOAL " + gm.Goal.ToString("N0");
        foreach (var r in rankRows) r.SetActive(false);
        RefreshMyRow(0);
        RefreshAdButton();

        var lb = Leaderboard.I;
        if (lb == null || !lb.Configured)
        {
            rankEmpty.text = "Leaderboard not configured";
            return;
        }
        rankEmpty.text = "Loading...";
        if (rankCo != null) StopCoroutine(rankCo);
        rankCo = StartCoroutine(lb.FetchTop(gm.timeAttack, gm.difficulty, FillRank));
    }

    void FillRank(List<ScoreEntry> rows)
    {
        rankCo = null;
        if (rows == null) { rankEmpty.text = "Failed to load"; return; }
        if (rows.Count == 0) { rankEmpty.text = "No records yet"; RefreshMyRow(0); return; }
        rankEmpty.text = "";

        string myUid = Leaderboard.I != null ? Leaderboard.I.Uid : "";
        int myRank = 0;

        if (rankNationTab)
        {
            var nations = NationRanking.Aggregate(rows);
            for (int i = 0; i < rankRows.Length && i < nations.Count; i++)
            {
                var n = nations[i];
                bool mine = n.Country == PlayerAccount.Country;
                if (mine) myRank = i + 1;
                FillRow(rankRows[i], i + 1, n.Country, PlayerAccount.DisplayName(n.Country), n.Total, mine);
            }
        }
        else
        {
            for (int i = 0; i < rankRows.Length && i < rows.Count; i++)
            {
                var e = rows[i];
                bool mine = e.Uid == myUid;
                if (mine) myRank = i + 1;
                FillRow(rankRows[i], i + 1, e.Country, e.Name, e.Score, mine);
            }
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Uid == myUid) { myRank = i + 1; break; }
        }
        RefreshMyRow(myRank);
    }

    /// <summary>맨 아래 고정 줄 — 목록을 스크롤하지 않아도 내 위치가 보인다.</summary>
    void RefreshMyRow(int rank)
    {
        int score = gm.PendingScore > 0 ? gm.PendingScore : gm.BestForSelection();
        FillRow(myRow, rank, PlayerAccount.Country, PlayerAccount.Name, score, true);
        myRowLabel.text = rank > 0 ? "YOU  ·  RANK " + rank : "YOU  ·  UNRANKED";
    }

    void RefreshAdButton()
    {
        bool canSubmit = gm.CanSubmit;
        adBtnLabel.text = canSubmit ? "WATCH AD  ·  SUBMIT SCORE" : "WATCH AD  ·  REFRESH";
        adBtn.gameObject.SetActive(Leaderboard.I != null && Leaderboard.I.Configured);
    }

    /// <summary>광고를 보고 나면 점수를 올리거나 목록을 새로고침한다.</summary>
    void OnAdButton()
    {
        bool submit = gm.CanSubmit;
        ShowAd(() =>
        {
            if (submit) gm.SubmitPending(ok => RefreshRank());
            else RefreshRank();
        });
    }

    static string Trim(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "?";
        return s.Length <= n ? s : s.Substring(0, n);
    }

    public void SetSubmitState(SubmitState st)
    {
        if (submitText == null) return;
        switch (st)
        {
            case SubmitState.Pending: submitText.text = "Watch an ad to submit your score"; break;
            case SubmitState.Sending: submitText.text = "Submitting..."; break;
            case SubmitState.Done: submitText.text = "Submitted"; break;
            case SubmitState.Failed: submitText.text = "Submit failed (offline?)"; break;
            default: submitText.text = ""; break;
        }
    }

    // ---------- 광고 (자리표시) ----------
    //
    // ⚠ 실제 광고 SDK 는 붙어 있지 않다. 보상형 광고의 '흐름'만 만들어 둔 것이다.
    //   AdMob/Unity Ads 를 붙일 때 ShowAd 안쪽만 실제 호출로 갈아끼우면 된다.
    //   지금은 카운트다운 뒤 바로 보상을 준다.

    const float AdSeconds = 3f;

    void BuildAdPanel()
    {
        adPanel = NewImage("addim", transform, new Color(0, 0, 0, 0.92f)).gameObject;
        Stretch((RectTransform)adPanel.transform);

        var tag = NewText("adtag", adPanel.transform, "AD", 46, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.35f));
        Place(tag.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 70));

        var note = NewText("adnote", adPanel.transform, "AD PLACEHOLDER\nno ad SDK integrated yet", 40,
                           TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.75f));
        Place(note.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 160));

        adCountdown = NewText("adcount", adPanel.transform, "", 72, TextAnchor.MiddleCenter, Accent);
        Place(adCountdown.rectTransform, new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 110));
    }

    void ShowAd(System.Action onReward)
    {
        adPanel.SetActive(true);
        adPanel.transform.SetAsLastSibling();
        StartCoroutine(AdCo(onReward));
    }

    IEnumerator AdCo(System.Action onReward)
    {
        float t = AdSeconds;
        while (t > 0f)
        {
            adCountdown.text = Mathf.CeilToInt(t).ToString();
            t -= Time.unscaledDeltaTime;
            yield return null;
        }
        adPanel.SetActive(false);
        if (onReward != null) onReward();
    }

    // ---------- 국가 선택 ----------

    void BuildCountryPanel()
    {
        countryPanel = NewImage("cdim", transform, new Color(0, 0, 0, 0.8f)).gameObject;
        Stretch((RectTransform)countryPanel.transform);

        var card = NewImage("ccard", countryPanel.transform, new Color(0.10f, 0.10f, 0.15f, 0.98f));
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(940, 1320));

        var t = NewText("ctitle", card.transform, "COUNTRY", 52, TextAnchor.MiddleCenter, Color.white);
        Place(t.rectTransform, new Vector2(0.5f, 0.95f), new Vector2(0.5f, 0.95f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880, 70));

        // 6열 그리드 배지 버튼
        var list = PlayerAccount.PickList;
        const int cols = 6;
        for (int i = 0; i < list.Length; i++)
        {
            string code = list[i];
            int cx = i % cols, cy = i / cols;
            var b = NewPlainButton("c" + code, card.transform, PlayerAccount.BadgeColor(code),
                () => { PlayerAccount.Country = code; RefreshBadge(); countryPanel.SetActive(false); });
            Place((RectTransform)b.transform, new Vector2(0.5f, 0.885f), new Vector2(0.5f, 0.885f), new Vector2(0.5f, 0.5f),
                new Vector2((cx - (cols - 1) / 2f) * 145, -cy * 92), new Vector2(130, 78));
            var lt = NewText("l", b.transform, code, 38, TextAnchor.MiddleCenter, Color.white);
            Stretch(lt.rectTransform);
        }

        var close = NewButton("cclose", card.transform, "CLOSE", UiKind.Secondary, () => countryPanel.SetActive(false));
        Place((RectTransform)close.transform, new Vector2(0.5f, 0.04f), new Vector2(0.5f, 0.04f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 110));
    }

    void ShowCountryPicker()
    {
        countryPanel.SetActive(true);
        countryPanel.transform.SetAsLastSibling();
    }

    // ---------- UI 조립 헬퍼 ----------

    RectTransform NewRT(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    Image NewImage(string name, Transform parent, Color c)
    {
        var rt = NewRT(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        return img;
    }

    Text NewText(string name, Transform parent, string s, int size, TextAnchor anchor, Color c)
    {
        var rt = NewRT(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = s;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = anchor;
        t.color = c;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>국가 배지처럼 자체 그림을 갖는 곳에 쓰는 단순 버튼 (입체 처리 없음).</summary>
    Button NewPlainButton(string name, Transform parent, Color bg, UnityAction onClick)
    {
        var img = NewImage(name, parent, bg);
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        return b;
    }

    /// <summary>입체 버튼: 어두운 lip 위에 밝은 face 를 얹는다. 누르면 face 가 내려앉는다.
    /// 터치 판정은 보이는 크기와 별개로 최소 44 를 보장한다.</summary>
    Button NewButton(string name, Transform parent, string label, UiKind kind, UnityAction onClick)
    {
        var root = NewRT(name, parent);

        // 터치 영역 — 투명하지만 raycast 를 받는다. 보이는 버튼이 작아도 넉넉하게.
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0, 0, 0, 0);
        hit.sprite = null;

        var b = root.gameObject.AddComponent<Button>();
        b.targetGraphic = hit;
        b.transition = Selectable.Transition.None;   // 색 전환은 UiButton 이 맡는다
        b.onClick.AddListener(onClick);

        var lip = NewImage("lip", root, Color.white);
        lip.sprite = roundBig; lip.type = Image.Type.Sliced; lip.raycastTarget = false;
        Stretch(lip.rectTransform);

        var face = NewImage("face", root, Color.white);
        face.sprite = roundBig; face.type = Image.Type.Sliced; face.raycastTarget = false;
        var fr = face.rectTransform;
        fr.anchorMin = new Vector2(0, 1); fr.anchorMax = new Vector2(1, 1);
        fr.pivot = new Vector2(0.5f, 1);
        fr.offsetMin = new Vector2(0, 0); fr.offsetMax = new Vector2(0, 0);
        fr.sizeDelta = new Vector2(0, ((RectTransform)root).rect.height - UiTheme.Lip);
        fr.anchoredPosition = Vector2.zero;

        var hi = NewImage("hi", face.transform, Color.white);
        hi.sprite = roundSmall; hi.type = Image.Type.Sliced; hi.raycastTarget = false;
        var hr = hi.rectTransform;
        hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
        hr.pivot = new Vector2(0.5f, 1);
        hr.offsetMin = new Vector2(UiTheme.HiInset, 0); hr.offsetMax = new Vector2(-UiTheme.HiInset, 0);
        hr.sizeDelta = new Vector2(-UiTheme.HiInset * 2, UiTheme.HiBar);
        hr.anchoredPosition = new Vector2(0, -UiTheme.HiInset * 0.6f);

        var t = NewText("label", face.transform, label, 40, TextAnchor.MiddleCenter, Color.white);
        t.fontStyle = FontStyle.Normal;              // 700 은 캐주얼 게임에서 무겁다
        Stretch(t.rectTransform);

        var ui = root.gameObject.AddComponent<UiButton>();
        ui.face = fr; ui.faceImg = face; ui.lipImg = lip; ui.hiImg = hi; ui.label = t;
        buttons.Add(ui);
        buttonKinds[ui] = kind;
        return b;
    }

    /// <summary>Place() 로 크기가 정해진 뒤에 호출한다 — face 높이·글자 크기·터치 영역을 확정한다.</summary>
    void FinishButtons()
    {
        foreach (var ui in buttons)
        {
            var rt = (RectTransform)ui.transform;
            // 보이는 크기와 별개로 최소 터치 영역을 보장한다
            if (rt.rect.height < UiTheme.MinTouch || rt.rect.width < UiTheme.MinTouch)
                rt.sizeDelta = new Vector2(Mathf.Max(rt.rect.width, UiTheme.MinTouch),
                                           Mathf.Max(rt.rect.height, UiTheme.MinTouch));
            ui.face.sizeDelta = new Vector2(0, rt.rect.height - UiTheme.Lip);
            ui.Init(buttonKinds[ui]);
        }
    }

    // 세로 그라데이션 스프라이트 (에셋 없이 런타임 생성)
    static Sprite MakeGradientSprite(Color top, Color bottom)
    {
        const int H = 128;
        var tex = new Texture2D(2, H) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[2 * H];
        for (int y = 0; y < H; y++)
        {
            // 위쪽이 더 오래 밝게 남도록 살짝 휘어준 보간
            float k = y / (float)(H - 1);
            var c = Color.Lerp(bottom, top, k * k * (3f - 2f * k));
            px[y * 2] = px[y * 2 + 1] = c;
        }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 2, H), new Vector2(0.5f, 0.5f), 100f);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}

/// <summary>홈 배경: 게임 블록 모양이 천천히 떠오르며 회전. 화면 위로 나가면 아래에서 다시 들어온다.</summary>
public class BgBlocks : MonoBehaviour
{
    const int Count = 16;

    RectTransform[] rts;
    float[] speed, spin, size;

    public void Build(Sprite tile)
    {
        var rng = new System.Random(7);                      // 실행마다 같은 배치
        var palette = Palette.Generate(4, new System.Random(7));
        rts = new RectTransform[Count];
        speed = new float[Count]; spin = new float[Count]; size = new float[Count];

        for (int i = 0; i < Count; i++)
        {
            var go = new GameObject("blk" + i, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            size[i] = 70f + (float)rng.NextDouble() * 150f;
            rt.sizeDelta = new Vector2(size[i], size[i]);
            rt.anchoredPosition = new Vector2(((float)rng.NextDouble() - 0.5f) * 1100f,
                                              (float)rng.NextDouble() * 2100f);
            rt.localRotation = Quaternion.Euler(0, 0, (float)rng.NextDouble() * 360f);

            var img = go.AddComponent<Image>();
            img.sprite = tile;
            var c = palette[rng.Next(palette.Length)];
            // 큰 블록일수록 더 흐리게 — 배경이 앞을 잡아먹지 않게
            img.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.13f, 0.05f, (size[i] - 70f) / 150f));
            img.raycastTarget = false;

            rts[i] = rt;
            speed[i] = 8f + (float)rng.NextDouble() * 18f;
            spin[i] = ((float)rng.NextDouble() - 0.5f) * 7f;
        }
    }

    void Update()
    {
        if (rts == null) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < rts.Length; i++)
        {
            var p = rts[i].anchoredPosition;
            p.y += speed[i] * dt;
            if (p.y - size[i] > 2200f) p.y = -size[i];       // 위로 나가면 아래에서 재진입
            rts[i].anchoredPosition = p;
            rts[i].Rotate(0, 0, spin[i] * dt);
        }
    }
}

/// <summary>노치/홈 인디케이터 회피: RectTransform을 Screen.safeArea에 맞춤</summary>
public class SafeAreaFitter : MonoBehaviour
{
    Rect last = new Rect(-1, -1, -1, -1);

    void Update()
    {
        var sa = Screen.safeArea;
        if (sa == last || Screen.width == 0 || Screen.height == 0) return;
        last = sa;
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
        rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}

/// <summary>은은한 맥동 스케일 (홈 타이틀/시작 버튼 등 강조용)</summary>
public class UiPulse : MonoBehaviour
{
    public float amp = 0.04f, speed = 2.2f;
    float phase;

    void Awake() { phase = Random.value * 6.28318f; }

    void Update()
    {
        float s = 1f + Mathf.Sin(Time.unscaledTime * speed + phase) * amp;
        transform.localScale = new Vector3(s, s, 1f);
    }
}
