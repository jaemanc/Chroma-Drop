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
    GameObject adPanel, shopPanel;
    Text shopCoins;
    Image[] shopBuyFill;
    Text[] shopBuyLabel, shopOwned;
    Text[] itemBtnLabel;
    Image[] itemBtnFill;
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
        // 캐주얼 퍼즐에 어울리는 둥근/기하 계열을 먼저 찾는다.
        // 기본 내장 폰트(LegacyRuntime)는 사무용 산세리프라 게임에 안 어울린다.
        string[] prefer = {
            "SF Pro Rounded", "SFProRounded", "SF Compact Rounded",   // iOS/macOS
            "Avenir Next", "AvenirNext-DemiBold", "Avenir",
            "Arial Rounded MT Bold",
            "Nunito", "Poppins", "Quicksand",                         // 있으면 더 좋다
            "SF Pro Display", "Helvetica Neue",
            "Noto Sans", "Roboto", "Droid Sans"                       // Android 폴백
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
        BuildShopPanel();
        homePanel.SetActive(false);
        gamePanel.SetActive(false);
        resultPanel.SetActive(false);
        rankPanel.SetActive(false);
        countryPanel.SetActive(false);
        adPanel.SetActive(false);
        shopPanel.SetActive(false);
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
        shopPanel.SetActive(false);
        RefreshHomeButtons();
    }

    public void ShowGame()
    {
        RefreshItemButtons();
        homePanel.SetActive(false);
        gamePanel.SetActive(true);
        resultPanel.SetActive(false);
        rankPanel.SetActive(false);
        countryPanel.SetActive(false);
        adPanel.SetActive(false);
        shopPanel.SetActive(false);
    }

    public void ShowResult(bool ta, int score, int best, bool newBest, int coins)
    {
        resultPanel.SetActive(true);
        // 목표 점수를 없앴으므로 성공/실패가 아니라 '끝났다 + 얼마 냈다' 만 보여준다.
        resultTitle.text = ta ? "TIME'S UP!" : "GAME OVER";
        resultTitle.color = newBest ? Accent : new Color(1, 1, 1, 0.9f);
        resultScore.text = score.ToString("N0") + (newBest ? "  ★ NEW BEST!" : "");
        resultBest.text = "BEST " + best.ToString("N0") + (coins > 0 ? "     +" + coins + " COINS" : "");

        // 결과 카드를 잠깐 보여준 뒤 리더보드를 띄운다.
        if (Leaderboard.I != null && Leaderboard.I.Configured)
            StartCoroutine(OpenRankAfter(1.1f));
    }

    // ---------- 게임 HUD ----------

    // ---------- 게임 HUD (chroma-drop.html) ----------
    static readonly Color Lilac     = Palette.Hex(0x9B8FE0);
    static readonly Color StatLabel = Palette.Hex(0x5F6A90);
    static readonly Color MintInk   = Palette.Hex(0x2B5148);
    static readonly Color Mint      = Palette.Hex(0x8FD6C4);

    void BuildGamePanel()
    {
        gamePanel = NewRT("game", transform).gameObject;
        Stretch((RectTransform)gamePanel.transform);
        var safe = NewRT("safe", gamePanel.transform);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        Label(safe, "eyebrow", Spaced("CHROMA DROP"), 11, TextAnchor.MiddleCenter, Muted, 24, 26, 342, 20);

        // ---- 점수 / 남은 수 카드 ----
        float cw = (390f - 48f - 14f) / 2f;
        var scoreCard = Card(safe, "statscore", 24, 54, cw, 78, Cream, 22);
        var sl = Label(scoreCard.transform, "l", Spaced("SCORE"), 12, TextAnchor.UpperLeft, StatLabel, 0, 0, 0, 0);
        Anchor(sl.transform, 0, 1, 16, -12, cw - 32, 18);
        scoreText = NewText("v", scoreCard.transform, "0", Mathf.RoundToInt(34 * PS), TextAnchor.UpperLeft, Ink);
        scoreText.fontStyle = FontStyle.Bold;
        Anchor(scoreText.transform, 0, 1, 16, -30, cw - 32, 46);

        var movesCard = Card(safe, "statmoves", 24 + cw + 14, 54, cw, 78, Mint, 22);
        subText = Label(movesCard.transform, "l", "", 12, TextAnchor.UpperRight, MintInk, 0, 0, 0, 0);
        Anchor(subText.transform, 1, 1, -16, -12, cw - 32, 18);
        rightText = NewText("v", movesCard.transform, "", Mathf.RoundToInt(34 * PS), TextAnchor.UpperRight, Ink);
        rightText.fontStyle = FontStyle.Bold;
        Anchor(rightText.transform, 1, 1, -16, -30, cw - 32, 46);

        // ---- 다음 조각 ----
        Label(safe, "nextlabel", Spaced("NEXT"), 11, TextAnchor.MiddleCenter, Muted, 24, 146, 342, 18);
        nextRoot = NewRT("next", safe);
        Place(nextRoot, Top, Top, new Vector2(0.5f, 1), P(195, 166), Sz(320, 60));

        // ---- 제한시간 바 ----
        var bar = Card(safe, "bar", 24, 232, 342, 16, Cream, 8);
        timerBar = bar.transform.parent.gameObject;
        timerFill = NewImage("fill", bar.transform, Coral);
        timerFill.sprite = Rounded(5); timerFill.type = Image.Type.Sliced; timerFill.raycastTarget = false;
        Stretch(timerFill.rectTransform);
        timerFill.rectTransform.pivot = new Vector2(0, 0.5f);

        // ---- 아이템 (보유량이 0 이면 흐려진다) ----
        int ni = Shop.Items.Length;
        itemBtnLabel = new Text[ni];
        itemBtnFill = new Image[ni];
        float iw = (390f - 48f - (ni - 1) * 10f) / ni;
        for (int i = 0; i < ni; i++)
        {
            var e = Shop.Items[i];
            var f = Card(safe, "item" + i, 24 + i * (iw + 10), 690, iw, 54, e.Tint, 16);
            HookButton(f, () => { if (gm.UseItem(e.Item)) RefreshItemButtons(); }, e.Name, 12);
            itemBtnFill[i] = f;
            itemBtnLabel[i] = f.transform.Find("l").GetComponent<Text>();
        }

        // ---- 하단 조작 ----
        HookButton(Card(safe, "home", 24, 762, cw, 58, Cream, 22), () => gm.GoHome(), "HOME", 19);
        HookButton(Card(safe, "rotate", 24 + cw + 14, 762, cw, 58, Lilac, 22), () => gm.RotateCurrent(), "ROTATE", 19);

        chainPopup = NewText("chainpop", safe, "", 100, TextAnchor.MiddleCenter, Coral);
        Place(chainPopup.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 180));
        chainPopup.gameObject.SetActive(false);
    }

    /// <summary>카드 바깥(테두리 오브젝트)에 버튼과 눌림 반응, 가운데 글자를 붙인다.</summary>
    void HookButton(Image cardFill, UnityAction onClick, string text, float size)
    {
        var outer = cardFill.transform.parent.gameObject;
        var b = outer.AddComponent<Button>();
        b.targetGraphic = outer.GetComponent<Image>();
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(onClick);
        outer.AddComponent<UiPressImage>().target = (RectTransform)outer.transform;

        var t = NewText("l", cardFill.transform, text, Mathf.RoundToInt(size * PS), TextAnchor.MiddleCenter, Ink);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform);
    }

    /// <summary>아이템 버튼의 보유량 표시. 0 이면 흐리게.</summary>
    void RefreshItemButtons()
    {
        if (itemBtnFill == null) return;
        for (int i = 0; i < Shop.Items.Length; i++)
        {
            var e = Shop.Items[i];
            int n = Wallet.Count(e.Item);
            bool usable = n > 0 && !(e.MovesOnly && gm.timeAttack);
            itemBtnLabel[i].text = e.Name + (n > 0 ? "  x" + n : "");
            itemBtnFill[i].color = usable ? e.Tint : Color.Lerp(e.Tint, ScreenBg, 0.72f);
            itemBtnLabel[i].color = usable ? Ink : Muted;
        }
    }

    /// <summary>부모 모서리 기준으로 자식을 배치한다 (프로토타입 좌표).</summary>
    static void Anchor(Transform t, float ax, float ay, float x, float y, float w, float h)
    {
        var rt = (RectTransform)t;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(ax, ay);
        rt.sizeDelta = Sz(w, h);
        rt.anchoredPosition = new Vector2(x * PS, y * PS);
    }

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
            subText.text = Spaced("TIME LEFT");
            int sec = Mathf.CeilToInt(g.TimeLeftSec);
            rightText.text = (sec / 60) + ":" + (sec % 60).ToString("00");
            frac = g.TimeLeftSec / (Rules.TimeAttackMs / 1000f);
        }
        else
        {
            subText.text = Spaced("MOVES LEFT");
            rightText.text = g.MovesLeft.ToString();
            frac = g.PieceTimerFrac;   // 다 지나가면 조각이 버려진다
        }
        frac = Mathf.Clamp01(frac);
        timerFill.rectTransform.localScale = new Vector3(frac, 1, 1);
        timerFill.color = Color.Lerp(Coral, Palette.Hex(0x7FCFC0), frac);
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
                img.rectTransform.anchoredPosition = new Vector2(-(i * 150) - (3 - c.X) * 30, -(3 - c.Y) * 30);
            }
        }
        for (int i = idx; i < nextCells.Count; i++) nextCells[i].gameObject.SetActive(false);
    }

    // ---------- 홈 ----------

    // chroma_drop_title.html 의 배색·구성을 옮긴 것.

    // ---------- 홈 (chroma_drop_prototype.html) ----------
    //
    // 프로토타입은 390x844 기준이라 그 좌표를 그대로 옮기고 스케일만 맞춘다.
    // 세로를 기준으로 맞춰야 비율이 유지된다 (1920/844). 남는 가로 여백은 양옆으로 간다.
    // 상단 노치는 실기기에 이미 있으므로 그리지 않는다.

    const float PS = 1920f / 844f;      // 프로토타입 → 캔버스 배율
    const float ProtoW = 390f;
    const float Bd = 3f;                // 테두리 굵기 (프로토타입 단위)

    static readonly Color Ink        = Palette.Hex(0x14162B);
    static readonly Color ScreenBg   = Palette.Hex(0xE4EEF2);
    static readonly Color BlobYellow = Palette.Hex(0xF0D97A);
    static readonly Color BlobPurple = Palette.Hex(0xC9BFEC);
    static readonly Color Coral      = Palette.Hex(0xE4795A);
    static readonly Color CoralLip   = Palette.Hex(0xB5573B);
    static readonly Color Purple     = Palette.Hex(0x8B84D6);
    static readonly Color Teal       = Palette.Hex(0x7FCFC0);
    static readonly Color Yellow     = Palette.Hex(0xF0C64D);
    static readonly Color Cream      = Palette.Hex(0xFBF8EE);
    static readonly Color SwatchBg   = Palette.Hex(0xE7EEF0);
    static readonly Color Muted      = Palette.Hex(0x6B7094);
    static readonly Color Body       = Palette.Hex(0x5B6088);
    static readonly Color TealInk    = Palette.Hex(0x0E4A3E);

    readonly Dictionary<int, Sprite> roundCache = new Dictionary<int, Sprite>();
    Sprite circleSprite;
    Image[] modeFill;
    Text[] modeEyebrow;
    Text selectedModeText, coinHomeText;

    Sprite Rounded(float protoRadius)
    {
        int r = Mathf.Max(2, Mathf.RoundToInt(protoRadius * PS));
        Sprite sp;
        if (!roundCache.TryGetValue(r, out sp)) roundCache[r] = sp = UiTheme.RoundedSprite(r);
        return sp;
    }

    /// <summary>프로토타입 좌표(좌상단 기준)를 캔버스 앵커 좌표로.</summary>
    static Vector2 P(float x, float y) { return new Vector2((x - ProtoW * 0.5f) * PS, -y * PS); }
    static Vector2 Sz(float w, float h) { return new Vector2(w * PS, h * PS); }

    /// <summary>굵은 잉크 테두리를 가진 둥근 카드. 자식은 반환된 안쪽 Image 에 붙인다.</summary>
    Image Card(Transform parent, string name, float x, float y, float w, float h, Color fill, float radius)
    {
        var outer = NewImage(name, parent, Ink);
        outer.sprite = Rounded(radius); outer.type = Image.Type.Sliced;
        Place(outer.rectTransform, Top, Top, new Vector2(0, 1), P(x, y), Sz(w, h));

        var inner = NewImage("fill", outer.transform, fill);
        inner.sprite = Rounded(Mathf.Max(2f, radius - Bd)); inner.type = Image.Type.Sliced;
        inner.raycastTarget = false;
        var rt = inner.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(Bd * PS, Bd * PS);
        rt.offsetMax = new Vector2(-Bd * PS, -Bd * PS);
        return inner;
    }

    Text Label(Transform parent, string name, string text, float size, TextAnchor anchor, Color c,
               float x, float y, float w, float h)
    {
        var t = NewText(name, parent, text, Mathf.RoundToInt(size * PS), anchor, c);
        t.fontStyle = FontStyle.Bold;
        Place(t.rectTransform, Top, Top, new Vector2(0, 1), P(x, y), Sz(w, h));
        return t;
    }

    void BuildHomePanel()
    {
        homePanel = NewImage("homebg", transform, ScreenBg).gameObject;
        Stretch((RectTransform)homePanel.transform);

        var safe = NewRT("safe", homePanel.transform);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        if (circleSprite == null) circleSprite = MakeCircleSprite();

        // ---- 장식: 원형 얼룩 두 개 + 기울어진 칩 두 개 ----
        Blob(safe, "blobY", BlobYellow, 0.55f, -40, 90, 300);
        Blob(safe, "blobP", BlobPurple, 0.60f, ProtoW - 200, 844 - 200, 260);
        Chip(safe, "chipC", Coral, 18, 150, -14f);
        Chip(safe, "chipP", Purple, ProtoW - 18 - 64, 165, 12f);

        // ---- 상단 바 (노치는 그리지 않는다) ----
        var menu = Card(safe, "menu", 20, 22, 44, 44, Color.white, 14);
        var menuBtn = menu.transform.parent.gameObject.AddComponent<Button>();
        menuBtn.targetGraphic = menu.transform.parent.GetComponent<Image>();
        menuBtn.transition = Selectable.Transition.None;
        menuBtn.onClick.AddListener(ShowCountryPicker);
        var dotCols = new[] { Coral, Yellow, Purple, Teal };
        for (int i = 0; i < 4; i++)
        {
            var d = NewImage("dot" + i, menu.transform, dotCols[i]);
            d.sprite = Rounded(4); d.type = Image.Type.Sliced; d.raycastTarget = false;
            var rt = d.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = Sz(12, 12);
            rt.anchoredPosition = new Vector2((5 + (i % 2) * 16) * PS, -(5 + (i / 2) * 16) * PS);
            if (i == 3) homeBadge = d;                       // 국가 색이 여기 들어간다
        }

        homeBadgeText = Label(safe, "acct", "", 11, TextAnchor.MiddleLeft, Muted, 72, 34, 160, 20);

        var ranks = Card(safe, "ranks", ProtoW - 20 - 108, 22, 108, 44, Color.white, 20);
        var ranksBtn = ranks.transform.parent.gameObject.AddComponent<Button>();
        ranksBtn.targetGraphic = ranks.transform.parent.GetComponent<Image>();
        ranksBtn.transition = Selectable.Transition.None;
        ranksBtn.onClick.AddListener(() => ShowRanking(false));
        var rl = NewText("l", ranks.transform, "RANKINGS", Mathf.RoundToInt(12 * PS), TextAnchor.MiddleCenter, Ink);
        rl.fontStyle = FontStyle.Bold;
        Stretch(rl.rectTransform);

        // ---- 타이틀 ----
        Label(safe, "eyebrow", Spaced("COLOR MATCHER"), 11, TextAnchor.MiddleCenter, Muted, 20, 94, 350, 20);
        var title = Label(safe, "title", "", 42, TextAnchor.MiddleCenter, Ink, 20, 114, 350, 96);
        title.supportRichText = true;
        title.lineSpacing = 1.02f;
        title.text = "CHROMA\n<color=#E4795A>DROP</color>";
        Label(safe, "tagline", "Match. Pop. Beat your best.", 14, TextAnchor.MiddleCenter, Body, 20, 216, 350, 24);

        // ---- 모드 카드 ----
        modeFill = new Image[2];
        modeEyebrow = new Text[2];
        string[] eyebrows = { "CLASSIC", "RUSH" };
        string[] labels = { "Moves", "Time Attack" };
        for (int i = 0; i < 2; i++)
        {
            bool ta = i == 1;
            float cw = (ProtoW - 40 - 12) / 2f;
            var card = Card(safe, "mode" + i, 20 + i * (cw + 12), 268, cw, 78, Color.white, 18);
            var b = card.transform.parent.gameObject.AddComponent<Button>();
            b.targetGraphic = card.transform.parent.GetComponent<Image>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { gm.timeAttack = ta; RefreshHomeButtons(); });
            card.transform.parent.gameObject.AddComponent<UiPressImage>().target = (RectTransform)card.transform.parent;

            modeEyebrow[i] = Label(card.transform, "eb", eyebrows[i], 10, TextAnchor.UpperLeft, Muted, 0, 0, 0, 0);
            var er = modeEyebrow[i].rectTransform;
            er.anchorMin = er.anchorMax = er.pivot = new Vector2(0, 1);
            er.sizeDelta = Sz(cw - 28, 16); er.anchoredPosition = new Vector2(14 * PS, -12 * PS);

            var ml = NewText("ml", card.transform, labels[i], Mathf.RoundToInt(17 * PS), TextAnchor.UpperLeft, Ink);
            ml.fontStyle = FontStyle.Bold;
            var mr = ml.rectTransform;
            mr.anchorMin = mr.anchorMax = mr.pivot = new Vector2(0, 1);
            mr.sizeDelta = Sz(cw - 28, 44); mr.anchoredPosition = new Vector2(14 * PS, -32 * PS);
            modeFill[i] = card;
        }

        // ---- 시작 버튼 (아래 두께가 있는 카드) ----
        var startRoot = NewRT("start", safe);
        Place(startRoot, Top, Top, new Vector2(0, 1), P(20, 362), Sz(350, 60));
        var lip = NewImage("lip", startRoot, CoralLip);
        lip.sprite = Rounded(18); lip.type = Image.Type.Sliced; lip.raycastTarget = false;
        var lrt = lip.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(0, -5 * PS); lrt.offsetMax = Vector2.zero;

        var faceRt = NewRT("face", startRoot);
        faceRt.anchorMin = Vector2.zero; faceRt.anchorMax = Vector2.one;
        faceRt.offsetMin = faceRt.offsetMax = Vector2.zero;
        var faceOuter = faceRt.gameObject.AddComponent<Image>();
        faceOuter.sprite = Rounded(18); faceOuter.type = Image.Type.Sliced; faceOuter.color = Ink;
        var faceIn = NewImage("fill", faceRt, Coral);
        faceIn.sprite = Rounded(15); faceIn.type = Image.Type.Sliced; faceIn.raycastTarget = false;
        var fir = faceIn.rectTransform;
        fir.anchorMin = Vector2.zero; fir.anchorMax = Vector2.one;
        fir.offsetMin = new Vector2(Bd * PS, Bd * PS); fir.offsetMax = new Vector2(-Bd * PS, -Bd * PS);

        var sl = NewText("l", faceIn.transform, "START GAME", Mathf.RoundToInt(17 * PS), TextAnchor.MiddleLeft, Color.white);
        sl.fontStyle = FontStyle.Bold;
        Place(sl.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24 * PS, 0), Sz(240, 30));
        var ar = NewText("arrow", faceIn.transform, "\u2192", Mathf.RoundToInt(20 * PS), TextAnchor.MiddleRight, Color.white);
        ar.fontStyle = FontStyle.Bold;
        Place(ar.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24 * PS, 0), Sz(40, 32));

        var sb = startRoot.gameObject.AddComponent<Button>();
        sb.targetGraphic = faceOuter;
        sb.transition = Selectable.Transition.None;
        sb.onClick.AddListener(() => gm.StartGame());
        startRoot.gameObject.AddComponent<UiPressImage>().target = faceRt;

        selectedModeText = Label(safe, "selmode", "", 13, TextAnchor.MiddleLeft, Body, 20, 434, 350, 20);

        // ---- 최고 기록 카드 ----
        var best = Card(safe, "bestcard", 20, 472, 350, 328, Cream, 22);
        Label(best.transform, "bl", Spaced("PERSONAL BEST"), 11, TextAnchor.UpperLeft, Muted, 0, 0, 0, 0);
        var blr = best.transform.Find("bl").GetComponent<RectTransform>();
        blr.anchorMin = blr.anchorMax = blr.pivot = new Vector2(0, 1);
        blr.sizeDelta = Sz(220, 18); blr.anchoredPosition = new Vector2(18 * PS, -18 * PS);

        bestHomeText = NewText("bv", best.transform, "0", Mathf.RoundToInt(34 * PS), TextAnchor.UpperLeft, Ink);
        bestHomeText.fontStyle = FontStyle.Bold;
        var bvr = bestHomeText.rectTransform;
        bvr.anchorMin = bvr.anchorMax = bvr.pivot = new Vector2(0, 1);
        bvr.sizeDelta = Sz(240, 46); bvr.anchoredPosition = new Vector2(18 * PS, -34 * PS);

        coinHomeText = NewText("coins", best.transform, "", Mathf.RoundToInt(11 * PS), TextAnchor.UpperRight, Muted);
        Anchor(coinHomeText.transform, 1, 1, -68, -22, 160, 18);

        var star = Card(best.transform, "star", 0, 0, 40, 40, Yellow, 12);
        var srt2 = (RectTransform)star.transform.parent;
        srt2.anchorMin = srt2.anchorMax = srt2.pivot = new Vector2(1, 1);
        srt2.sizeDelta = Sz(40, 40); srt2.anchoredPosition = new Vector2(-18 * PS, -18 * PS);
        var st = NewText("s", star.transform, "\u2605", Mathf.RoundToInt(18 * PS), TextAnchor.MiddleCenter, Ink);
        Stretch(st.rectTransform);

        // 색 견본 그리드 — 팔레트를 미리 보여준다
        var grid = NewImage("swatches", best.transform, SwatchBg);
        grid.sprite = Rounded(14); grid.type = Image.Type.Sliced; grid.raycastTarget = false;
        var gr = grid.rectTransform;
        gr.anchorMin = gr.anchorMax = gr.pivot = new Vector2(0, 1);
        gr.sizeDelta = Sz(314, 162); gr.anchoredPosition = new Vector2(18 * PS, -86 * PS);
        var sw = new[] { Coral, Yellow, Teal, Purple, Coral, Teal,
                         Yellow, Purple, Coral, Yellow, Teal, Purple,
                         Teal, Coral, Yellow, Purple, Teal, Coral };
        for (int i = 0; i < sw.Length; i++)
        {
            var q = NewImage("sw" + i, grid.transform, sw[i]);
            q.sprite = Rounded(8); q.type = Image.Type.Sliced; q.raycastTarget = false;
            var qr = q.rectTransform;
            qr.anchorMin = qr.anchorMax = qr.pivot = new Vector2(0, 1);
            qr.sizeDelta = Sz(42, 42);
            qr.anchoredPosition = new Vector2((10 + (i % 6) * 50) * PS, -(10 + (i / 6) * 50) * PS);
        }

        var lb = Card(best.transform, "lbbtn", 0, 0, 152, 48, Color.white, 16);
        var lbr = (RectTransform)lb.transform.parent;
        lbr.anchorMin = lbr.anchorMax = lbr.pivot = new Vector2(0, 1);
        lbr.sizeDelta = Sz(152, 48); lbr.anchoredPosition = new Vector2(18 * PS, -262 * PS);
        var lbBtn = lb.transform.parent.gameObject.AddComponent<Button>();
        lbBtn.targetGraphic = lb.transform.parent.GetComponent<Image>();
        lbBtn.transition = Selectable.Transition.None;
        lbBtn.onClick.AddListener(() => ShowRanking(false));
        lb.transform.parent.gameObject.AddComponent<UiPressImage>().target = lbr;
        var lbl = NewText("l", lb.transform, "RANKS", Mathf.RoundToInt(15 * PS), TextAnchor.MiddleCenter, Ink);
        lbl.fontStyle = FontStyle.Bold;
        Stretch(lbl.rectTransform);

        var sh = Card(best.transform, "shopbtn", 0, 0, 152, 48, Yellow, 16);
        var shr = (RectTransform)sh.transform.parent;
        shr.anchorMin = shr.anchorMax = shr.pivot = new Vector2(0, 1);
        shr.sizeDelta = Sz(152, 48); shr.anchoredPosition = new Vector2(180 * PS, -262 * PS);
        HookButton(sh, ShowShop, "SHOP", 15);
    }

    void Blob(Transform parent, string name, Color c, float alpha, float x, float y, float d)
    {
        var img = NewImage(name, parent, new Color(c.r, c.g, c.b, alpha));
        img.sprite = circleSprite; img.raycastTarget = false;
        Place(img.rectTransform, Top, Top, new Vector2(0, 1), P(x, y), Sz(d, d));
    }

    void Chip(Transform parent, string name, Color c, float x, float y, float deg)
    {
        var chip = Card(parent, name, x, y, 64, 64, c, 16);
        var rt = (RectTransform)chip.transform.parent;
        rt.localRotation = Quaternion.Euler(0, 0, deg);
        chip.raycastTarget = false;
        chip.transform.parent.GetComponent<Image>().raycastTarget = false;
    }

    static Sprite MakeCircleSprite()
    {
        const int S = 128;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        float c = (S - 1) / 2f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(c - d));
            }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    static readonly Vector2 Top = new Vector2(0.5f, 1f);

    /// <summary>글자 사이에 공백을 끼워 자간을 넓힌다 (uGUI 에는 letter-spacing 이 없다).</summary>
    static string Spaced(string t)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in t) { sb.Append(ch); sb.Append(' '); }
        return sb.ToString().TrimEnd();
    }

    void RefreshHomeButtons()
    {
        if (modeFill == null) return;
        for (int i = 0; i < 2; i++)
        {
            bool on = (i == 1) == gm.timeAttack;
            modeFill[i].color = on ? Teal : Color.white;
            modeEyebrow[i].color = on ? TealInk : Muted;
        }
        bestHomeText.text = gm.BestForSelection().ToString("N0");
        if (coinHomeText != null) coinHomeText.text = Wallet.Coins.ToString("N0") + " COINS";
        selectedModeText.text = gm.timeAttack
            ? "Selected mode: time attack · 3 min"
            : "Selected mode: " + Rules.Table[GameManager.Difficulty].Moves + " moves";
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

        var meBtn = NewButton("tabme", card.transform, "PLAYERS", UiKind.Selected, () => { rankNationTab = false; RefreshRank(); });
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
        rankTabMe.SetKind(rankNationTab ? UiKind.Secondary : UiKind.Selected);
        rankTabNation.SetKind(rankNationTab ? UiKind.Selected : UiKind.Secondary);
        rankSubTitle.text = gm.timeAttack ? "TIME ATTACK" : "MOVES";
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

    // ---------- 상점 ----------

    void BuildShopPanel()
    {
        shopPanel = NewImage("shopdim", transform, new Color(0.06f, 0.08f, 0.10f, 0.82f)).gameObject;
        Stretch((RectTransform)shopPanel.transform);

        var card = Card(shopPanel.transform, "shopcard", 20, 90, 350, 620, ScreenBg, 24);
        var cr = (RectTransform)card.transform.parent;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;

        var title = NewText("t", card.transform, "SHOP", Mathf.RoundToInt(26 * PS), TextAnchor.UpperCenter, Ink);
        title.fontStyle = FontStyle.Bold;
        Anchor(title.transform, 0.5f, 1, 0, -20, 300, 40);

        shopCoins = NewText("coins", card.transform, "", Mathf.RoundToInt(15 * PS), TextAnchor.UpperCenter, Body);
        Anchor(shopCoins.transform, 0.5f, 1, 0, -58, 300, 26);

        int n = Shop.Items.Length;
        shopBuyFill = new Image[n]; shopBuyLabel = new Text[n]; shopOwned = new Text[n];
        for (int i = 0; i < n; i++)
        {
            var e = Shop.Items[i];
            int idx = i;
            float rowY = 96 + i * 92;

            var row = Card(card.transform, "item" + i, 0, 0, 314, 78, Color.white, 18);
            var rr = (RectTransform)row.transform.parent;
            rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0.5f, 1);
            rr.sizeDelta = Sz(314, 78); rr.anchoredPosition = new Vector2(0, -rowY * PS);

            var swatch = NewImage("sw", row.transform, e.Tint);
            swatch.sprite = Rounded(10); swatch.type = Image.Type.Sliced; swatch.raycastTarget = false;
            Anchor(swatch.transform, 0, 1, 12, -12, 54, 54);

            var nm = NewText("n", row.transform, e.Name, Mathf.RoundToInt(15 * PS), TextAnchor.UpperLeft, Ink);
            nm.fontStyle = FontStyle.Bold;
            Anchor(nm.transform, 0, 1, 76, -12, 150, 22);

            var ds = NewText("d", row.transform, e.Desc, Mathf.RoundToInt(10 * PS), TextAnchor.UpperLeft, Muted);
            Anchor(ds.transform, 0, 1, 76, -34, 160, 32);

            shopOwned[i] = NewText("own", row.transform, "", Mathf.RoundToInt(10 * PS), TextAnchor.LowerRight, Muted);
            Anchor(shopOwned[i].transform, 1, 0, -12, 10, 120, 18);

            shopBuyFill[i] = Card(row.transform, "buy", 0, 0, 84, 40, Teal, 12);
            var br = (RectTransform)shopBuyFill[i].transform.parent;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(1, 1);
            br.sizeDelta = Sz(84, 40); br.anchoredPosition = new Vector2(-12 * PS, -12 * PS);
            var bb = br.gameObject.AddComponent<Button>();
            bb.targetGraphic = br.GetComponent<Image>();
            bb.transition = Selectable.Transition.None;
            bb.onClick.AddListener(() => Buy(idx));
            br.gameObject.AddComponent<UiPressImage>().target = br;
            shopBuyLabel[i] = NewText("l", shopBuyFill[i].transform, "", Mathf.RoundToInt(13 * PS), TextAnchor.MiddleCenter, Ink);
            shopBuyLabel[i].fontStyle = FontStyle.Bold;
            Stretch(shopBuyLabel[i].rectTransform);
        }

        float adY = 96 + n * 92 + 12;
        var adRow = Card(card.transform, "shopad", 0, 0, 314, 62, Coral, 18);
        var ar = (RectTransform)adRow.transform.parent;
        ar.anchorMin = ar.anchorMax = ar.pivot = new Vector2(0.5f, 1);
        ar.sizeDelta = Sz(314, 62); ar.anchoredPosition = new Vector2(0, -adY * PS);
        HookButton(adRow, () => ShowAd(() => { Wallet.AddCoins(Shop.AdReward); RefreshShop(); }),
                   "WATCH AD  ·  +" + Shop.AdReward, 15);

        var close = Card(card.transform, "shopclose", 0, 0, 314, 54, Cream, 18);
        var clr = (RectTransform)close.transform.parent;
        clr.anchorMin = clr.anchorMax = clr.pivot = new Vector2(0.5f, 0);
        clr.sizeDelta = Sz(314, 54); clr.anchoredPosition = new Vector2(0, 18 * PS);
        HookButton(close, () => shopPanel.SetActive(false), "CLOSE", 15);
    }

    public void ShowShop()
    {
        shopPanel.SetActive(true);
        shopPanel.transform.SetAsLastSibling();
        RefreshShop();
    }

    void Buy(int i)
    {
        var e = Shop.Items[i];
        if (Wallet.SpendCoins(e.Price)) Wallet.Add(e.Item, 1);
        RefreshShop();
    }

    void RefreshShop()
    {
        shopCoins.text = Wallet.Coins.ToString("N0") + " COINS";
        for (int i = 0; i < Shop.Items.Length; i++)
        {
            var e = Shop.Items[i];
            bool afford = Wallet.Coins >= e.Price;
            shopBuyLabel[i].text = e.Price.ToString();
            shopBuyFill[i].color = afford ? Teal : new Color(0.85f, 0.86f, 0.88f);
            shopBuyLabel[i].color = afford ? Ink : Muted;
            shopOwned[i].text = "owned " + Wallet.Count(e.Item);
        }
        RefreshItemButtons();
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
