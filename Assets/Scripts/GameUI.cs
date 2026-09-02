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
using ChromaDrop.Engine;

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
        public Text Rank, Code, Name, Score, Stage;
        public void SetActive(bool v) { Bg.transform.parent.gameObject.SetActive(v); }
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
    Image[] skinBuyFill, skinSwatch;
    Text[] skinBuyLabel;
    Text[] itemBtnLabel;
    Image[] itemBtnFill;
    Text adCountdown;
    Image rankTabMe, rankTabNation;
    Image homeBadge; Text homeBadgeText;
    bool rankNationTab;
    Coroutine rankCo;
    const int RankRowCount = 10;
    RectTransform axisRoot;
    Text nextText;
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
        BuildMapPanel();
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
        if (mapPanel != null) mapPanel.SetActive(false);
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

        // 보드는 화면의 프로토 y 224~671 을 차지한다. HUD 는 그 위아래로만 둔다.
        gameEyebrow = Label(safe, "eyebrow", Spaced("CHROMA DROP"), 11, TextAnchor.MiddleCenter, Muted, 24, 20, 342, 18);

        // ---- 점수 / 남은 수 카드 ----
        float cw = (390f - 48f - 14f) / 2f;
        var scoreCard = Card(safe, "statscore", 24, 44, cw, 74, Cream, 22);
        var sl = Label(scoreCard.transform, "l", Spaced("SCORE"), 12, TextAnchor.UpperLeft, StatLabel, 0, 0, 0, 0);
        Anchor(sl.transform, 0, 1, 16, -12, cw - 32, 18);
        scoreText = NewText("v", scoreCard.transform, "0", Mathf.RoundToInt(34 * PS), TextAnchor.UpperLeft, Ink);
        scoreText.fontStyle = FontStyle.Bold;
        Anchor(scoreText.transform, 0, 1, 16, -30, cw - 32, 46);

        var movesCard = Card(safe, "statmoves", 24 + cw + 14, 44, cw, 74, Mint, 22);
        subText = Label(movesCard.transform, "l", "", 12, TextAnchor.UpperRight, MintInk, 0, 0, 0, 0);
        Anchor(subText.transform, 1, 1, -16, -12, cw - 32, 18);
        rightText = NewText("v", movesCard.transform, "", Mathf.RoundToInt(34 * PS), TextAnchor.UpperRight, Ink);
        rightText.fontStyle = FontStyle.Bold;
        Anchor(rightText.transform, 1, 1, -16, -30, cw - 32, 46);

        // ---- 다음 조각 + 축 선택 ----
        Label(safe, "nextlabel", Spaced("NEXT"), 10, TextAnchor.MiddleCenter, Muted, 24, 124, 342, 14);
        nextText = Label(safe, "nexttext", "", 14, TextAnchor.MiddleCenter, Ink, 24, 140, 342, 22);

        axisRoot = NewRT("axes", safe);
        Place(axisRoot, Top, Top, new Vector2(0.5f, 1), P(195, 166), Sz(390, 34));

        // ---- 제한시간 바 ----
        // 보드 위쪽 경계(224)보다 위에 둔다 — 예전엔 232 라 블록 위에 겹쳐 있었다
        var bar = Card(safe, "bar", 24, 198, 342, 14, Cream, 7);
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
            HookButton(f, () => { if (gm.ArmItem(e.Id, selectedAxis)) RefreshItemButtons(); }, e.Name, 12);
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

    /// <summary>이 판의 설정이 허용한 아이템인가.</summary>
    bool Available(string id)
    {
        if (gm.Stage == null) return false;
        foreach (var name in gm.Stage.ItemsAvailable) if (name == id) return true;
        return false;
    }

    /// <summary>축 선택. 버튼 수는 토폴로지가 정한다 — 하드코딩하지 않는다.</summary>
    int selectedAxis;
    Image[] axisFill;
    Text[] axisLabel;

    public void RebuildAxisButtons()
    {
        if (axisRoot == null) return;
        foreach (Transform c in axisRoot) Destroy(c.gameObject);

        int n = gm.AxisCount;
        axisFill = new Image[n];
        axisLabel = new Text[n];
        if (n <= 1) return;

        float w = (390f - 48f - (n - 1) * 8f) / n;
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            var f = Card(axisRoot, "axis" + i, 24 + i * (w + 8), 0, w, 34, Color.white, 10);
            HookButton(f, () => { selectedAxis = idx; RefreshAxisButtons(); }, gm.AxisLabel(i), 9);
            axisFill[i] = f;
            axisLabel[i] = f.transform.Find("l").GetComponent<Text>();
        }
        RefreshAxisButtons();
    }

    void RefreshAxisButtons()
    {
        if (axisFill == null) return;
        for (int i = 0; i < axisFill.Length; i++)
        {
            bool on = i == selectedAxis;
            axisFill[i].color = on ? Teal : Color.white;
            axisLabel[i].color = on ? TealInk : Muted;
        }
    }

    /// <summary>아이템 버튼의 보유량 표시. 0 이면 흐리게.</summary>
    void RefreshItemButtons()
    {
        if (itemBtnFill == null) return;
        for (int i = 0; i < Shop.Items.Length; i++)
        {
            var e = Shop.Items[i];
            int n = Wallet.Count(e.Id);
            bool usable = n > 0 && Available(e.Id);
            itemBtnLabel[i].text = e.Name + (n > 0 ? "  x" + n : "");
            itemBtnFill[i].color = usable ? e.Tint : Color.Lerp(e.Tint, ScreenBg, 0.72f);
            itemBtnLabel[i].color = usable ? Ink : Muted;
        }
    }

    /// <summary>$ 배지가 붙은 코인 칩. 반환값은 금액 Text.</summary>
    Text CoinChip(Transform parent, string name, float ax, float ay, float x, float y, float w, float h)
    {
        var chip = Card(parent, name, 0, 0, w, h, Cream, h * 0.5f);
        var rt = (RectTransform)chip.transform.parent;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(ax, ay);
        rt.sizeDelta = Sz(w, h);
        rt.anchoredPosition = new Vector2(x * PS, y * PS);

        float d = h - 12f;
        var coin = NewImage("coin", chip.transform, Yellow);
        coin.sprite = Rounded(d * 0.5f); coin.type = Image.Type.Sliced; coin.raycastTarget = false;
        Anchor(coin.transform, 0, 0.5f, 6, 0, d, d);
        var dollar = NewText("$", coin.transform, "$", Mathf.RoundToInt(d * 0.62f * PS), TextAnchor.MiddleCenter, Ink);
        dollar.fontStyle = FontStyle.Bold;
        Stretch(dollar.rectTransform);

        var amt = NewText("amt", chip.transform, "0", Mathf.RoundToInt(h * 0.46f * PS), TextAnchor.MiddleRight, Ink);
        amt.fontStyle = FontStyle.Bold;
        Anchor(amt.transform, 1, 0.5f, -10, 0, w - d - 20, h - 10);
        return amt;
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
        // 목표 진행은 아이브로우 줄에 얹는다 — 보드 위쪽 여백이 좁다
        // 목표는 여러 개일 수 있다. 진행도를 그대로 이어 붙여 보여준다.
        if (g.TimeAttackMode) { gameEyebrow.text = Spaced("CHROMA DROP"); gameEyebrow.color = Muted; }
        else
        {
            var progress = g.Objectives;
            var sb = new System.Text.StringBuilder("STAGE " + g.StageLevel);
            bool allDone = progress.Count > 0;
            foreach (var op in progress)
            {
                sb.Append("   ").Append(op.Label);
                if (!op.Done) allDone = false;
            }
            gameEyebrow.text = sb.ToString();
            gameEyebrow.color = allDone ? Coral : Muted;
        }
        timerBar.SetActive(true);
        float frac;
        if (g.TimeAttackMode)
        {
            subText.text = Spaced("TIME LEFT");
            int sec = Mathf.CeilToInt(g.TimeLeftSec);
            rightText.text = (sec / 60) + ":" + (sec % 60).ToString("00");
            float total = g.Stage != null && g.Stage.TimeSeconds > 0 ? g.Stage.TimeSeconds : 1f;
            frac = g.TimeLeftSec / total;
        }
        else
        {
            subText.text = Spaced("MOVES LEFT");
            rightText.text = g.MovesLeft.ToString();
            int total = g.Stage != null && g.Stage.Moves > 0 ? g.Stage.Moves : 1;
            frac = g.MovesLeft / (float)total;
        }
        frac = Mathf.Clamp01(frac);
        timerFill.rectTransform.localScale = new Vector3(frac, 1, 1);
        timerFill.color = Color.Lerp(Coral, Palette.Hex(0x7FCFC0), frac);
    }

    /// <summary>다음 조각 미리보기. 조각은 그래프 위 모양이라 칸 수와 이름으로 보여준다.</summary>
    public void SetNext(string shapeName, int cellCount, Color color)
    {
        if (nextText == null) return;
        nextText.text = shapeName + "  ·  " + cellCount;
        nextText.color = color;
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
    Text gameEyebrow;
    Image stagePrev, stageNext;
    GameObject mapPanel;
    RectTransform mapContent, boat;
    Image[] island;          // 섬 본체 (잠김/열림/클리어에 따라 색이 다르다)
    Text[] islandNum;
    Image[] islandLock;
    ScrollRect mapScroll;
    Image stageMapBtn;
    Text mapTitle;
    Coroutine sailCo;
    Text retryLabel;
    GameObject rankFromResult;

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
        string[] eyebrows = { "STAGE", "RUSH" };
        string[] labels = { "Stage", "Time Attack" };
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
        sb.onClick.AddListener(() => { if (gm.timeAttack) gm.StartGame(); else gm.StartStage(Progress.Selected); });
        startRoot.gameObject.AddComponent<UiPressImage>().target = faceRt;

        selectedModeText = Label(safe, "selmode", "", 13, TextAnchor.MiddleLeft, Body, 20, 434, 250, 20);
        // 스테이지는 지도에서 고른다. 타임어택이면 숨긴다.
        stageMapBtn = Card(safe, "stgmap", 268, 426, 102, 32, Yellow, 12);
        HookButton(stageMapBtn, () => ShowMap(), "MAP", 12);

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

        coinHomeText = CoinChip(best.transform, "coinchip", 1, 1, -66, -14, 112, 34);

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
        var ver = NewText("ver", safe, "v" + Application.version + "  ·  jaemanc",
                          Mathf.RoundToInt(10 * PS), TextAnchor.MiddleCenter, Muted);
        Place(ver.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
              new Vector2(0, 22 * PS), Sz(300, 18));

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
        if (coinHomeText != null) coinHomeText.text = Wallet.Coins.ToString("N0");
        if (gm.timeAttack)
        {
            selectedModeText.text = "Selected mode: time attack · 3 min";
        }
        else
        {
            var st = StageCatalog.Get(Progress.Selected);
            selectedModeText.text = st == null
                ? "Stage " + Progress.Selected
                : "Stage " + st.StageId + " · " + st.ResolveTopology()
                  + " · " + Summary(st) + " / " + st.Moves + " moves";
        }
        if (stageMapBtn != null)
            stageMapBtn.transform.parent.gameObject.SetActive(!gm.timeAttack);
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

    Text resultCoins;


    /// <summary>스테이지 개수. 설정 파일이 유일한 출처다.</summary>
    static int StageCount { get { return Mathf.Max(1, StageCatalog.Count); } }

    /// <summary>목표를 한 줄로 요약한다.</summary>
    static string Summary(StageDef st)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < st.Objectives.Count; i++)
        {
            if (i > 0) sb.Append(" + ");
            sb.Append(st.Objectives[i].Target);
            var ob = st.Objectives[i];
            if (ob.Type == "clear_color") sb.Append(" C").Append(ob.ColorIndex);
            else if (ob.Type == "break_obstacle") sb.Append(' ').Append(ob.ObstacleType);
            else if (ob.Type == "reach_score") sb.Append(" pts");
            else if (ob.Type == "clear_group_size") sb.Append(" x").Append(ob.GroupSize);
            else sb.Append(" blocks");
        }
        return sb.ToString();
    }

    // ---------- 스테이지 지도 ----------
    // 섬에서 섬으로 항로가 이어지고, 배가 지금 선 자리를 표시한다.
    const float IslandGap = 132f;    // 섬 사이 세로 간격 (프로토타입 단위)
    const float MapTop = 150f;       // 첫 섬까지 여백
    const float IslandSize = 84f;

    static readonly Color Sea       = Palette.Hex(0x9FD5E4);
    static readonly Color SeaDeep   = Palette.Hex(0x6FB8CE);
    static readonly Color IslandOn  = Palette.Hex(0x8FD06A);
    static readonly Color IslandDone= Palette.Hex(0x5FB88C);
    static readonly Color IslandOff = Palette.Hex(0xA9B3BC);
    static readonly Color Sand      = Palette.Hex(0xF2DFA8);

    /// <summary>섬의 가로 위치. 좌우로 굽이치는 항로를 만든다.</summary>
    static float IslandX(int i)
    {
        float[] lane = { 74f, 168f, 262f, 168f };
        return lane[i % 4];
    }
    static float IslandY(int i) { return MapTop + i * IslandGap; }

    void BuildMapPanel()
    {
        mapPanel = NewImage("mapbg", transform, Sea).gameObject;
        Stretch((RectTransform)mapPanel.transform);

        // 스크롤 영역 — 헤더 아래부터 화면 끝까지
        var viewport = NewImage("mapview", mapPanel.transform, new Color(0, 0, 0, 0));
        Place(viewport.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
              new Vector2(0, -46 * PS), new Vector2(0, -92 * PS));
        viewport.rectTransform.anchorMin = new Vector2(0, 0);
        viewport.rectTransform.anchorMax = new Vector2(1, 1);
        viewport.rectTransform.offsetMin = new Vector2(0, 0);
        viewport.rectTransform.offsetMax = new Vector2(0, -92 * PS);
        viewport.gameObject.AddComponent<RectMask2D>();

        mapContent = NewRT("mapcontent", viewport.transform);
        mapContent.anchorMin = new Vector2(0, 1);
        mapContent.anchorMax = new Vector2(1, 1);
        mapContent.pivot = new Vector2(0.5f, 1);
        mapContent.offsetMin = new Vector2(0, 0);
        mapContent.offsetMax = new Vector2(0, 0);
        float contentH = MapTop + StageCount * IslandGap;
        mapContent.sizeDelta = new Vector2(0, contentH * PS);
        mapContent.anchoredPosition = Vector2.zero;

        mapScroll = mapPanel.AddComponent<ScrollRect>();
        mapScroll.viewport = viewport.rectTransform;
        mapScroll.content = mapContent;
        mapScroll.horizontal = false;
        mapScroll.movementType = ScrollRect.MovementType.Clamped;
        mapScroll.scrollSensitivity = 24f;

        // 바다 무늬 — 잔물결
        for (int i = 0; i < 26; i++)
        {
            float wy = 60 + i * 96 + (i % 2) * 34;
            var w = NewImage("wave" + i, mapContent, new Color(1f, 1f, 1f, 0.20f));
            w.sprite = Rounded(6); w.type = Image.Type.Sliced; w.raycastTarget = false;
            Place(w.rectTransform, Top, Top, new Vector2(0, 1),
                  P(30 + (i * 61) % 250, wy), Sz(52, 8));
        }

        // 항로 — 섬과 섬 사이를 점선으로 잇는다
        for (int i = 0; i < StageCount - 1; i++)
        {
            float x0 = IslandX(i), y0 = IslandY(i), x1 = IslandX(i + 1), y1 = IslandY(i + 1);
            const int Dots = 7;
            for (int d = 1; d < Dots; d++)
            {
                float t = d / (float)Dots;
                var dot = NewImage("dot" + i + "_" + d, mapContent, new Color(1f, 1f, 1f, 0.7f));
                dot.sprite = Rounded(5); dot.type = Image.Type.Sliced; dot.raycastTarget = false;
                Place(dot.rectTransform, Top, Top, new Vector2(0.5f, 0.5f),
                      P(x0 + (x1 - x0) * t + IslandSize * 0.5f, y0 + (y1 - y0) * t + IslandSize * 0.5f),
                      Sz(9, 9));
            }
        }

        island = new Image[StageCount];
        islandNum = new Text[StageCount];
        islandLock = new Image[StageCount];
        for (int i = 0; i < StageCount; i++)
        {
            int level = i + 1;
            // 모래톱을 살짝 크게 깔고 그 위에 섬
            var sand = NewImage("sand" + level, mapContent, Sand);
            sand.sprite = Rounded(IslandSize * 0.5f); sand.type = Image.Type.Sliced;
            sand.raycastTarget = false;
            Place(sand.rectTransform, Top, Top, new Vector2(0, 1),
                  P(IslandX(i) - 7, IslandY(i) + 10), Sz(IslandSize + 14, IslandSize + 6));

            var fill = Card(mapContent, "island" + level, IslandX(i), IslandY(i),
                            IslandSize, IslandSize, IslandOn, IslandSize * 0.5f);
            island[i] = fill;
            HookButton(fill, () => SailTo(level), level.ToString(), 22);
            islandNum[i] = fill.transform.Find("l").GetComponent<Text>();

            islandLock[i] = NewImage("lock" + level, fill.transform, new Color(0.16f, 0.18f, 0.26f, 0.55f));
            islandLock[i].sprite = Rounded(IslandSize * 0.5f); islandLock[i].type = Image.Type.Sliced;
            islandLock[i].raycastTarget = false;
            Stretch(islandLock[i].rectTransform);
        }

        // 배 — 선택된 섬 위에 떠 있다
        boat = NewRT("boat", mapContent);
        boat.anchorMin = boat.anchorMax = Top; boat.pivot = new Vector2(0.5f, 0.5f);
        boat.sizeDelta = Sz(52, 49);
        var hull = NewImage("hull", boat, Color.white);
        hull.sprite = BoatSprite(); hull.type = Image.Type.Simple; hull.raycastTarget = false;
        Stretch(hull.rectTransform);

        // 헤더 — 스크롤 위에 고정
        var head = NewImage("maphead", mapPanel.transform, SeaDeep);
        Place(head.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
              Vector2.zero, new Vector2(0, 92 * PS));
        head.rectTransform.anchorMin = new Vector2(0, 1);
        head.rectTransform.anchorMax = new Vector2(1, 1);
        head.rectTransform.sizeDelta = new Vector2(0, 92 * PS);

        mapTitle = NewText("mt", head.transform, "VOYAGE", Mathf.RoundToInt(20 * PS), TextAnchor.MiddleLeft, Cream);
        mapTitle.fontStyle = FontStyle.Bold;
        Anchor(mapTitle.transform, 0, 0.5f, 24, 0, 240, 30);

        HookButton(Card(head.transform, "mapclose", 0, 0, 62, 34, Cream, 12),
                   () => { mapPanel.SetActive(false); ShowHome(); }, "X", 13);
        var mc = (RectTransform)head.transform.Find("mapclose");
        mc.anchorMin = mc.anchorMax = mc.pivot = new Vector2(1, 0.5f);
        mc.sizeDelta = Sz(62, 34); mc.anchoredPosition = new Vector2(-20 * PS, 0);

        mapPanel.SetActive(false);
    }


    static Sprite boatSprite;

    /// <summary>돛단배. 선체 사다리꼴 + 삼각돛 + 깃발.</summary>
    static Sprite BoatSprite()
    {
        if (boatSprite != null) return boatSprite;

        const int W = 64, H = 60;
        var tex = new Texture2D(W, H) { filterMode = FilterMode.Bilinear };
        var px = new Color[W * H];

        var hull = Palette.Hex(0x8A5A3B);
        var hullDark = Palette.Hex(0x6B4429);
        var mast = Palette.Hex(0x5C4630);
        var sail = Palette.Hex(0xFBF8EE);
        var sailShade = Palette.Hex(0xE3DCC6);
        var flag = Palette.Hex(0xE4795A);
        var line = Palette.Hex(0x14162B);

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color c = new Color(0, 0, 0, 0);
                float fx = x - 32f;

                // 선체 — 아래로 갈수록 좁아지는 사다리꼴
                if (y >= 4 && y <= 19)
                {
                    float halfw = 12f + (y - 4) * 1.35f;
                    if (Mathf.Abs(fx) <= halfw)
                        c = y <= 8 ? hullDark : hull;
                    if (Mathf.Abs(fx) > halfw - 1.6f && Mathf.Abs(fx) <= halfw) c = line;
                    if (y == 19 || y == 4) c = line;
                }
                // 돛대
                if (y >= 19 && y <= 52 && fx >= -2f && fx <= 0f) c = mast;
                // 삼각돛 — 돛대 오른쪽으로 펼쳐진다
                if (y >= 21 && y <= 50)
                {
                    float w = (50 - y) * 0.62f;
                    if (fx >= 1f && fx <= 1f + w) c = fx < 1f + w * 0.3f ? sailShade : sail;
                }
                // 깃발
                if (y >= 50 && y <= 55 && fx >= -1f && fx <= 8f) c = flag;

                px[y * W + x] = c;
            }

        tex.SetPixels(px); tex.Apply();
        boatSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);
        return boatSprite;
    }

    public void ShowMap()
    {
        mapPanel.SetActive(true);
        RefreshMap();
        // 지금 선 섬이 화면에 오도록 스크롤을 맞춘다
        float contentH = MapTop + StageCount * IslandGap;
        float target = Mathf.Clamp01((IslandY(Progress.Selected - 1) - 260f) / Mathf.Max(1f, contentH - 500f));
        mapScroll.verticalNormalizedPosition = 1f - target;
        PlaceBoat(Progress.Selected);
    }

    void RefreshMap()
    {
        for (int i = 0; i < island.Length; i++)
        {
            int level = i + 1;
            bool open = level <= Progress.Unlocked;
            bool done = level < Progress.Unlocked;
            island[i].color = !open ? IslandOff : (done ? IslandDone : IslandOn);
            islandNum[i].text = open ? level.ToString() : "\u2715";
            islandNum[i].color = open ? Ink : new Color(1f, 1f, 1f, 0.8f);
            islandLock[i].enabled = !open;
            var btn = island[i].transform.parent.GetComponent<Button>();
            if (btn != null) btn.interactable = open;
        }
    }

    void PlaceBoat(int level)
    {
        int i = Mathf.Clamp(level, 1, StageCount) - 1;
        boat.anchoredPosition = BoatSpot(i);
    }

    /// <summary>섬 왼쪽 물 위. 맨 왼쪽 항로에서는 오른쪽에 댄다.</summary>
    static Vector2 BoatSpot(int i)
    {
        float x = IslandX(i);
        float side = x < 100f ? IslandSize + 30f : -30f;
        return P(x + side, IslandY(i) + IslandSize * 0.55f);
    }

    /// <summary>고른 섬으로 배를 몰고 가서 그 스테이지를 시작한다.</summary>
    void SailTo(int level)
    {
        if (level > Progress.Unlocked) return;
        if (sailCo != null) StopCoroutine(sailCo);
        sailCo = StartCoroutine(Sail(level));
    }

    IEnumerator Sail(int level)
    {
        Vector2 from = boat.anchoredPosition;
        int i = level - 1;
        Vector2 to = BoatSpot(i);

        float d = Vector2.Distance(from, to);
        float dur = Mathf.Clamp(d / (420f * PS), 0.18f, 1.1f);
        for (float t = 0; t < dur; t += Time.deltaTime)
        {
            float k = t / dur;
            k = 1f - (1f - k) * (1f - k);                 // 감속
            var pos = Vector2.Lerp(from, to, k);
            pos.y += Mathf.Sin(k * Mathf.PI * 4f) * 5f * PS;   // 파도에 흔들린다
            boat.anchoredPosition = pos;
            yield return null;
        }
        boat.anchoredPosition = to;
        yield return new WaitForSeconds(0.12f);

        Progress.Selected = level;
        mapPanel.SetActive(false);
        gm.StartStage(level);
        sailCo = null;
    }

    void BuildResultPanel()
    {
        resultPanel = NewImage("resultdim", transform, new Color(0.106f, 0.129f, 0.255f, 0.55f)).gameObject;
        Stretch((RectTransform)resultPanel.transform);

        var card = Card(resultPanel.transform, "rcard", 0, 0, 330, 380, Cream, 24);
        var cr = (RectTransform)card.transform.parent;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;

        resultTitle = NewText("t", card.transform, "", Mathf.RoundToInt(24 * PS), TextAnchor.UpperCenter, Ink);
        resultTitle.fontStyle = FontStyle.Bold;
        Anchor(resultTitle.transform, 0.5f, 1, 0, -22, 300, 34);

        var scoreLabel = NewText("sl", card.transform, Spaced("SCORE"), Mathf.RoundToInt(10 * PS),
                                 TextAnchor.UpperCenter, Muted);
        Anchor(scoreLabel.transform, 0.5f, 1, 0, -66, 300, 16);

        resultScore = NewText("s", card.transform, "", Mathf.RoundToInt(46 * PS), TextAnchor.UpperCenter, Ink);
        resultScore.fontStyle = FontStyle.Bold;
        Anchor(resultScore.transform, 0.5f, 1, 0, -84, 300, 58);

        resultBest = NewText("b", card.transform, "", Mathf.RoundToInt(12 * PS), TextAnchor.UpperCenter, Body);
        Anchor(resultBest.transform, 0.5f, 1, 0, -146, 300, 20);

        // 이번 판에 번 코인
        resultCoins = CoinChip(card.transform, "earned", 0.5f, 1, 0, -174, 130, 38);

        submitText = NewText("sub", card.transform, "", Mathf.RoundToInt(10 * PS), TextAnchor.UpperCenter, Muted);
        Anchor(submitText.transform, 0.5f, 1, 0, -220, 300, 16);

        var rankBtn = Card(card.transform, "rrank", 22, 244, 286, 46, Color.white, 16);
        HookButton(rankBtn, () => ShowRanking(false), "LEADERBOARD", 14);
        rankFromResult = rankBtn.transform.parent.gameObject;
        var retryFill = Card(card.transform, "retry", 22, 302, 136, 52, Coral, 18);
        HookButton(retryFill, () =>
        {
            if (gm.timeAttack) gm.StartGame();
            else gm.StartStage(Progress.Selected);   // 클리어했으면 Selected 가 이미 다음 판이다
        }, "RETRY", 16);
        retryLabel = retryFill.transform.Find("l").GetComponent<Text>();
        HookButton(Card(card.transform, "rhome", 172, 302, 136, 52, Color.white, 18),
                   () => gm.GoHome(), "HOME", 16);
    }

    public void ShowResult(bool ta, int score, int best, bool newBest, int coins, int level, bool cleared)
    {
        resultPanel.SetActive(true);
        // 타임어택은 성공/실패가 없다. 스테이지는 목표를 채웠는지로 갈린다.
        if (ta) resultTitle.text = "TIME'S UP!";
        else resultTitle.text = cleared ? "STAGE " + level + " CLEAR!" : "STAGE " + level + " FAILED";
        // 깼으면 다음 판으로, 못 깼으면 같은 판 재도전
        retryLabel.text = ta ? "RETRY" : (cleared && Progress.Selected > level ? "NEXT" : "RETRY");
        resultTitle.color = (cleared || newBest) ? Coral : Ink;
        resultScore.text = score.ToString("N0");
        resultBest.text = newBest ? "NEW BEST!" : "BEST  " + best.ToString("N0");
        resultBest.color = newBest ? Coral : Body;
        resultCoins.text = "+" + coins.ToString("N0");

        // 리더보드는 타임어택 전용이다. 스테이지는 기록만 남기고 조용히 끝난다.
        rankFromResult.SetActive(ta);
        if (ta && Leaderboard.I != null && Leaderboard.I.Configured)
            StartCoroutine(OpenRankAfter(1.6f));
    }

    // ---------- 랭킹 ----------

    // 순위 표식 색 — 1~3위만 강조
    static readonly Color[] MedalColors = {
        Palette.Hex(0xE4C05A), Palette.Hex(0xA9B4BC), Palette.Hex(0xC98A57),
    };
    const float RowH = 40f;   // 프로토타입 단위. 행 10개가 내 점수/광고 버튼 위에서 끝나야 한다

    void BuildRankPanel()
    {
        rankPanel = NewImage("rankdim", transform, new Color(0.106f, 0.129f, 0.255f, 0.62f)).gameObject;
        Stretch((RectTransform)rankPanel.transform);

        var card = Card(rankPanel.transform, "rcard", 0, 0, 350, 700, ScreenBg, 24);
        var cr = (RectTransform)card.transform.parent;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;

        rankTitle = NewText("t", card.transform, "LEADERBOARD", Mathf.RoundToInt(20 * PS), TextAnchor.UpperLeft, Ink);
        rankTitle.fontStyle = FontStyle.Bold;
        Anchor(rankTitle.transform, 0, 1, 20, -18, 240, 30);

        rankSubTitle = NewText("st", card.transform, "", Mathf.RoundToInt(10 * PS), TextAnchor.UpperLeft, Muted);
        Anchor(rankSubTitle.transform, 0, 1, 20, -44, 240, 16);

        HookButton(Card(card.transform, "rkclose", 0, 0, 62, 34, Color.white, 12),
                   () => rankPanel.SetActive(false), "X", 13);
        var clr = (RectTransform)card.transform.Find("rkclose");
        clr.anchorMin = clr.anchorMax = clr.pivot = new Vector2(1, 1);
        clr.sizeDelta = Sz(62, 34); clr.anchoredPosition = new Vector2(-16 * PS, -16 * PS);

        var meBtn = Card(card.transform, "tabme", 20, 72, 152, 40, Teal, 14);
        HookButton(meBtn, () => { rankNationTab = false; RefreshRank(); }, "PLAYERS", 13);
        rankTabMe = meBtn;
        var natBtn = Card(card.transform, "tabnat", 182, 72, 148, 40, Color.white, 14);
        HookButton(natBtn, () => { rankNationTab = true; RefreshRank(); }, "NATIONS", 13);
        rankTabNation = natBtn;

        rankRows = new RankRow[RankRowCount];
        for (int i = 0; i < RankRowCount; i++)
            rankRows[i] = MakeRankRow(card.transform, "row" + i, 124 + i * RowH, false);

        rankEmpty = NewText("e", card.transform, "", Mathf.RoundToInt(12 * PS), TextAnchor.MiddleCenter, Muted);
        Anchor(rankEmpty.transform, 0.5f, 1, 0, -300, 300, 40);

        // ---- 맨 아래: 내 점수 ----
        myRowLabel = NewText("ml", card.transform, "YOU", Mathf.RoundToInt(9 * PS), TextAnchor.UpperLeft, Muted);
        Anchor(myRowLabel.transform, 0, 0, 22, 138, 200, 14);
        myRow = MakeRankRow(card.transform, "myrow", 0, true);
        var mrt = (RectTransform)myRow.Bg.transform.parent;
        mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0);
        mrt.anchoredPosition = new Vector2(0, 92 * PS);

        var adFill = Card(card.transform, "adbtn", 20, 0, 310, 48, Coral, 16);
        var art = (RectTransform)adFill.transform.parent;
        art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0);
        art.sizeDelta = Sz(310, 48); art.anchoredPosition = new Vector2(0, 30 * PS);
        HookButton(adFill, OnAdButton, "WATCH AD", 14);
        adBtn = art.GetComponent<Button>();
        adBtnLabel = adFill.transform.Find("l").GetComponent<Text>();
    }

    /// <summary>순위 줄 하나: 배경 + 순위 + 국가배지 + 이름 + 점수</summary>
    RankRow MakeRankRow(Transform parent, string name, float y, bool highlight)
    {
        var r = new RankRow();
        r.Bg = Card(parent, name, 20, y, 310, RowH - 6, highlight ? Yellow : Color.white, 12);
        var t = (RectTransform)r.Bg.transform.parent;
        t.anchorMin = t.anchorMax = t.pivot = new Vector2(0.5f, 1);
        t.sizeDelta = Sz(310, RowH - 6);
        t.anchoredPosition = new Vector2(0, -y * PS);

        r.Rank = NewText("rk", r.Bg.transform, "", Mathf.RoundToInt(12 * PS), TextAnchor.MiddleCenter, Ink);
        r.Rank.fontStyle = FontStyle.Bold;
        Anchor(r.Rank.transform, 0, 0.5f, 6, 0, 28, 22);

        r.Badge = NewImage("badge", r.Bg.transform, Color.white);
        r.Badge.sprite = Rounded(6); r.Badge.type = Image.Type.Sliced; r.Badge.raycastTarget = false;
        Anchor(r.Badge.transform, 0, 0.5f, 38, 0, 32, 22);
        r.Code = NewText("c", r.Badge.transform, "", Mathf.RoundToInt(10 * PS), TextAnchor.MiddleCenter, Color.white);
        r.Code.fontStyle = FontStyle.Bold;
        Stretch(r.Code.rectTransform);

        r.Name = NewText("n", r.Bg.transform, "", Mathf.RoundToInt(12 * PS), TextAnchor.MiddleLeft, Ink);
        Anchor(r.Name.transform, 0, 0.5f, 74, 0, 80, 22);   // 스테이지 칸(158~)을 침범하지 않는다

        r.Stage = NewText("lv", r.Bg.transform, "", Mathf.RoundToInt(10 * PS), TextAnchor.MiddleCenter, Muted);
        r.Stage.fontStyle = FontStyle.Bold;
        Anchor(r.Stage.transform, 0, 0.5f, 158, 0, 28, 22);   // 점수 왼쪽 끝(190)까지 여유를 둔다

        r.Score = NewText("s", r.Bg.transform, "", Mathf.RoundToInt(13 * PS), TextAnchor.MiddleRight, Ink);
        r.Score.fontStyle = FontStyle.Bold;
        Anchor(r.Score.transform, 1, 0.5f, -10, 0, 104, 22);
        return r;
    }

    void FillRow(RankRow r, int rank, string code, string name, int score, bool isMe)
    { FillRow(r, rank, code, name, score, 0, isMe); }

    void FillRow(RankRow r, int rank, string code, string name, int score, int stage, bool isMe)
    {
        r.SetActive(true);
        // 도달한 스테이지. 국가 집계 줄에는 해당 없음
        r.Stage.text = stage > 0 ? "L" + stage : "";
        r.Rank.text = rank > 0 ? rank.ToString() : "-";
        r.Rank.color = rank >= 1 && rank <= 3 ? MedalColors[rank - 1] : Muted;
        r.Badge.color = PlayerAccount.BadgeColor(code);
        r.Code.text = code;
        r.Name.text = Trim(name, 11);
        r.Name.color = Ink;
        r.Score.text = score.ToString("N0");
        r.Score.color = Ink;
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
        rankTabMe.color = rankNationTab ? Color.white : Teal;
        rankTabNation.color = rankNationTab ? Teal : Color.white;
        rankSubTitle.text = gm.timeAttack ? "TIME ATTACK" : "STAGE";
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
        rankCo = StartCoroutine(lb.FetchTop(gm.timeAttack, GameManager.BoardId, FillRank));
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
                FillRow(rankRows[i], i + 1, e.Country, e.Name, e.Score, e.Stage, mine);
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
        FillRow(myRow, rank, PlayerAccount.Country, PlayerAccount.Name, score, Progress.Unlocked, true);
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

        var card = Card(shopPanel.transform, "shopcard", 0, 0, 350, 560, ScreenBg, 24);
        var cr = (RectTransform)card.transform.parent;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;

        var title = NewText("st", card.transform, "SHOP", Mathf.RoundToInt(20 * PS), TextAnchor.UpperLeft, Ink);
        title.fontStyle = FontStyle.Bold;
        Anchor(title.transform, 0, 1, 20, -18, 200, 30);

        shopCoins = CoinChip(card.transform, "shopcoin", 1, 1, -16, -16, 130, 38);

        HookButton(Card(card.transform, "shclose", 0, 0, 62, 34, Color.white, 12),
                   () => { shopPanel.SetActive(false); }, "X", 13);
        var clr = (RectTransform)card.transform.Find("shclose");
        clr.anchorMin = clr.anchorMax = clr.pivot = new Vector2(1, 0);
        clr.sizeDelta = Sz(62, 34); clr.anchoredPosition = new Vector2(-20 * PS, 20 * PS);

        // 아이템 목록. 무엇을 파는지는 Shop.Items 가, 무엇을 쓸 수 있는지는 스테이지 설정이 정한다.
        shopBuyFill = new Image[Shop.Items.Length];
        shopBuyLabel = new Text[Shop.Items.Length];
        shopOwned = new Text[Shop.Items.Length];

        for (int i = 0; i < Shop.Items.Length; i++)
        {
            var e = Shop.Items[i];
            float y = 76 + i * 74;
            var row = Card(card.transform, "item" + i, 20, y, 310, 64, Color.white, 16);

            var swatch = NewImage("sw", row.transform, e.Tint);
            swatch.sprite = Rounded(10); swatch.type = Image.Type.Sliced; swatch.raycastTarget = false;
            Anchor(swatch.transform, 0, 0.5f, 12, 0, 44, 44);

            var name = NewText("n", row.transform, e.Name, Mathf.RoundToInt(13 * PS), TextAnchor.UpperLeft, Ink);
            name.fontStyle = FontStyle.Bold;
            Anchor(name.transform, 0, 1, 66, -10, 150, 20);

            var desc = NewText("d", row.transform, e.Desc, Mathf.RoundToInt(9 * PS), TextAnchor.UpperLeft, Muted);
            Anchor(desc.transform, 0, 1, 66, -30, 160, 26);

            shopOwned[i] = NewText("o", row.transform, "", Mathf.RoundToInt(9 * PS), TextAnchor.LowerRight, Muted);
            Anchor(shopOwned[i].transform, 1, 0, -12, 8, 90, 16);

            int idx = i;
            var buy = Card(row.transform, "buy", 0, 0, 76, 34, Teal, 12);
            HookButton(buy, () => Buy(idx), "", 12);
            var brt = (RectTransform)row.transform.Find("buy");
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1, 1);
            brt.sizeDelta = Sz(76, 34); brt.anchoredPosition = new Vector2(-12 * PS, -10 * PS);
            shopBuyFill[idx] = buy;
            shopBuyLabel[idx] = buy.transform.Find("l").GetComponent<Text>();
        }

        HookButton(Card(card.transform, "shad", 20, 468, 310, 48, Coral, 16),
                   () => ShowAd(() => { Wallet.AddCoins(Shop.AdReward); RefreshShop(); }),
                   "WATCH AD  +" + Shop.AdReward, 14);

        shopPanel.SetActive(false);
    }

    void Buy(int i)
    {
        var e = Shop.Items[i];
        if (Wallet.Coins < e.Price) return;
        Wallet.SpendCoins(e.Price);
        Wallet.Add(e.Id, 1);
        RefreshShop();
    }


    public void ShowShop()
    {
        shopPanel.SetActive(true);
        shopPanel.transform.SetAsLastSibling();
        RefreshShop();
    }



    int coinTaps;

    /// <summary>⚠ 개발용. 코인 칩을 5번 두드리면 코인을 넉넉히 넣는다.
    /// 실제로 사면서 테스트하기 위한 것이라 무한 모드가 아니라 지급이다.
    /// 스토어 배포 전에 이 메서드와 Wallet.DevGrant 를 제거할 것.</summary>
    void TapCoins()
    {
        if (++coinTaps < 5) return;
        coinTaps = 0;
        Wallet.AddCoins(Wallet.DevGrant);
        RefreshShop();
        RefreshItemButtons();
    }

    void RefreshShop()
    {
        shopCoins.text = Wallet.Coins.ToString("N0");
        for (int i = 0; i < Shop.Items.Length; i++)
        {
            var e = Shop.Items[i];
            bool afford = Wallet.Coins >= e.Price;
            shopBuyLabel[i].text = e.Price.ToString();
            shopBuyFill[i].color = afford ? Teal : new Color(0.85f, 0.86f, 0.88f);
            shopBuyLabel[i].color = afford ? Ink : Muted;
            shopOwned[i].text = "owned " + Wallet.Count(e.Id);
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
