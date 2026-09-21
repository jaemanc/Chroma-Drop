// GameUI.cs — 런타임 생성 uGUI (씬/프리팹/폰트 에셋 의존 없음).
// 게임 HUD / 결과 패널을 코드로 조립 (홈은 Menu 씬). 노치 대응(SafeArea) 포함.
// 한글 표시는 OS 폰트(iOS: Apple SD Gothic Neo, Android: Noto Sans CJK 등)를 동적 로드,
// 없으면 내장 폰트 + 영문 라벨로 폴백.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using ColorMatcher.Core;

public class GameUI : MonoBehaviour
{
    static readonly Color Accent = new Color(0.28f, 0.72f, 0.52f);

    GameManager gm;
    Font font;
    Sprite roundBig, roundSmall;                                   // 버튼 9슬라이스
    readonly List<UiButton> buttons = new List<UiButton>();
    readonly Dictionary<UiButton, UiKind> buttonKinds = new Dictionary<UiButton, UiKind>();

    GameObject gamePanel, resultPanel;
    TMP_Text scoreText, subText, rightText;   // 플레이보드 HUD 는 TextMeshPro
    Text resultTitle, resultScore, resultBest;
    Image resultDim;
    RectTransform resultCardRt;
    Coroutine resultDropCo;
    RectTransform chainRoot;          // 연쇄 팝업 — 두 줄 글자와 반짝이를 한 덩어리로 움직인다
    TMP_Text chainTop, chainBig;
    Image chainWord;                  // CHAIN 글자 그림 (없으면 chainTop 글자로 돌아간다)
    int chainWordNext;                // 다음에 쓸 글자 그림 번호 — 연쇄마다 한 장씩 돌아간다
    Image[] chainSparks;
    float chainAngle;
    Coroutine chainCo;
    Image feverTint;              // 피버 동안 배경을 붉게 물들이는 판
    Text feverPopup;
    Coroutine feverCo;
    bool feverOn;
    Image timerFill;
    GameObject timerBar;      // 타임어택=남은 시간 / 횟수 모드=조각 제한시간

    // 랭킹
    public enum SubmitState { Off, Pending, Sending, Done, Failed }

    /// <summary>리더보드 한 줄. 순위·국가배지·이름·점수를 각각 따로 그린다.</summary>
    struct RankRow
    {
        public Image Bg, Badge, Flag;
        public Text Rank, Code, Name, Score;
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
    TMP_Text[] itemBtnLabel;
    Image[] itemBtnFill;
    Text adCountdown;
    Image rankTabMe, rankTabNation;
    bool rankNationTab;
    Coroutine rankCo;
    const int RankRowCount = 10;
    TMP_Text gameEyebrow;   // 스테이지 모드의 목표 문구 (타임어택은 아트에 제목이 박혀 있어 숨긴다)
    TMP_Text goalSub;       // GOAL 팻말 안의 진행도

    // ---- 플레이 화면 (아트) ----
    //
    // 화면 전체가 테마 그림 한 장이다. 다만 보드는 월드 스프라이트라 Overlay 캔버스 위에
    // 그릴 수 없으므로, 아트는 카메라 캔버스(정렬 -30)에 두어 보드 뒤에 깔고
    // 버튼·글자·슬롯만 Overlay 캔버스에 얹는다. 두 캔버스 모두 같은 비율 맞춤(contain)이라
    // 자리가 어긋나지 않는다. 좌표는 전부 테마 그림 원본(1024x1536) 픽셀, y 는 위가 0.
    const float PlayW = 1024f, PlayH = 1536f;   // ponytail: 테마 그림은 전부 이 크기라고 가정한다. 크기가 다른 테마가 오면 BlockTheme 에 크기를 둔다
    RectTransform playRoot, boardSlot, traySlot;
    GameObject playArtCanvas;
    RectTransform rankBtn, nextBtn, retryBtn, homeBtn;
    bool rankAllowed;   // 결과 화면에서 리더보드를 열어도 되는가 — 스테이지를 깼으면 열지 않는다
    readonly List<Image> nextCells = new List<Image>();
    readonly List<Image> holdCells = new List<Image>();

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
            "Arial Rounded MT Bold", "Trebuchet MS",                  // example.html 과 같은 순서
            "SF Pro Rounded", "SFProRounded", "SF Compact Rounded",   // iOS/macOS
            "Avenir Next", "AvenirNext-DemiBold", "Avenir",
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
        BuildResultPanel();
        BuildRankPanel();
        BuildCountryPanel();
        BuildAdPanel();
        BuildShopPanel();
        gamePanel.SetActive(false);
        resultPanel.SetActive(false);
        rankPanel.SetActive(false);
        countryPanel.SetActive(false);
        adPanel.SetActive(false);
        shopPanel.SetActive(false);
        FinishButtons();
    }

    // ---------- 패널 전환 ----------

    public void ShowGame()
    {
        if (resultDropCo != null) { StopCoroutine(resultDropCo); resultDropCo = null; }
        rankAllowed = false;
        RefreshItemButtons();
        gamePanel.SetActive(true);
        resultPanel.SetActive(false);
        rankPanel.SetActive(false);
        countryPanel.SetActive(false);
        adPanel.SetActive(false);
        shopPanel.SetActive(false);
        if (playArtCanvas != null) playArtCanvas.SetActive(true);
        // 방금 켠 패널의 자리를 바로 잰다 — 안 재면 첫 프레임에 보드가 엉뚱한 곳에 한 번 그려진다
        Canvas.ForceUpdateCanvases();
    }

    void OnDestroy()
    {
        if (playArtCanvas != null) Destroy(playArtCanvas);
    }

    // ---------- 게임 HUD (chroma-drop.html) ----------
    static readonly Color Lilac     = Palette.Hex(0x9B8FE0);
    static readonly Color StatLabel = Palette.Hex(0x5F6A90);
    static readonly Color MintInk   = Palette.Hex(0x2B5148);
    static readonly Color Mint      = Palette.Hex(0x8FD6C4);
    static readonly Color ScoreFill   = Palette.Hex(0xFFF5B8);
    static readonly Color ScoreBorder = Palette.Hex(0xE4BD52);
    static readonly Color MovesFill   = Palette.Hex(0x42DC91);
    static readonly Color MovesBorder = Palette.Hex(0x2DBB7C);

    static readonly Color TimeInk   = Palette.Hex(0x1A4430);   // 시간 카드 글자 (아트의 진초록)
    static readonly Color ScoreInk  = Palette.Hex(0x3C2D23);   // 점수 카드 글자 (아트의 진갈색)
    static readonly Color BarOrange = Palette.Hex(0xECA175);   // 진행바 채움 (아트에서 뽑음)
    static readonly Color GoalInk   = Palette.Hex(0x7A4E2A);   // GOAL 팻말 글자
    static readonly Color TitleInk  = Palette.Hex(0x2F6FD6);   // 스테이지 목표 문구 (제목 자리)

    Image playArtImage;

    /// <summary>테마 배경 그림이 깔려 있으면 보드 판·트레이 받침은 그림이 그린다.</summary>
    public bool HasPlayArt { get { return playArtImage != null && playArtImage.enabled; } }

    static readonly Rect DefaultGrid = new Rect(112, 380, 800, 800);   // 테마 그림이 없을 때 보드 자리

    /// <summary>판을 시작할 때 고른 테마의 배경 그림을 깔고, 보드 자리를 그 그림의 격자에 맞춘다.
    /// 그림이 없으면 배경 없이 기본 자리를 쓴다 — 그림 한 장 빠졌다고 판이 안 그려지면 안 된다.</summary>
    public void SetTheme(BlockTheme theme)
    {
        var tex = theme.Board;
        playArtImage.sprite = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
        playArtImage.enabled = tex != null;
        var g = tex != null ? theme.Grid : DefaultGrid;
        PlaceSlot(boardSlot, g.x, g.y, g.width, g.height);
    }

    /// <summary>화면 비율과 무관하게 아트 전체가 보이도록(contain) 맞춘 자리. 남는 쪽은 여백이 된다.</summary>
    static RectTransform ContainRoot(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        var fit = go.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = PlayW / PlayH;
        return rt;
    }

    void BuildGamePanel()
    {
        gamePanel = NewRT("game", transform).gameObject;
        Stretch((RectTransform)gamePanel.transform);
        var safe = NewRT("safe", gamePanel.transform);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        // 보드 뒤에 깔리는 테마 그림 — 카메라 캔버스는 스프라이트와 같은 정렬 규칙을 따른다. 그림은 SetTheme 이 넣는다
        playArtCanvas = new GameObject("PlayArtCanvas");
        var cv = playArtCanvas.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceCamera;
        cv.worldCamera = Camera.main;
        cv.planeDistance = 100f;
        cv.sortingOrder = -30;
        var artSafe = NewRT("safe", playArtCanvas.transform);
        Stretch(artSafe);
        artSafe.gameObject.AddComponent<SafeAreaFitter>();
        playArtImage = NewImage("art", ContainRoot("fit", artSafe), Color.white);
        playArtImage.raycastTarget = false;
        playArtImage.enabled = false;
        Stretch(playArtImage.rectTransform);
        feverTint = NewImage("fevertint", artSafe, new Color(1f, 0.3f, 0.15f, 0f));   // 테마 그림 위, 보드 뒤
        feverTint.raycastTarget = false;
        Stretch(feverTint.rectTransform);
        feverTint.enabled = false;

        playRoot = ContainRoot("playroot", safe);
        curRoot = playRoot; curW = PlayW; curH = PlayH;
        var clear = new Color(0, 0, 0, 0);

        // ---- 상단: 테마 그림의 하늘 자리 ----
        HudButton("pausebtn", gm.hudPause, 40, 44, 120, 120);   // 일시정지 표시는 그림에 들어 있다
        ArtButton("pause", 40, 44, 120, 120, () => gm.GoHome());

        // 제목 자리: 스테이지는 목표 문구, 타임어택은 모드 이름.
        // 줄 수가 1~3 으로 달라지는 곳은 여기뿐이라, 자동 축소도 여기만 쓴다
        var titleBg = HudButton("eyebrowcard", gm.hudTitle, 180, 26, 814, 156);
        gameEyebrow = HudLabel(titleBg, "eyebrow", TitleInk, 58, 0.12f, 0.88f, 60);
        gameEyebrow.enableAutoSizing = true;
        gameEyebrow.fontSizeMin = 24;
        gameEyebrow.fontSizeMax = 58;
        gameEyebrow.textWrappingMode = TextWrappingModes.Normal;
        gameEyebrow.overflowMode = TextOverflowModes.Truncate;

        // 점수 / 남은 수(시간) — 라벨은 위, 값은 아래. 둘 다 배경 버튼의 자식이다
        var scoreBg = HudButton("scorecard", gm.hudScore, 40, 186, 460, 134);
        HudLabel(scoreBg, "scorelabel", Muted, 26, 0.10f, 0.44f, 30).text = "SCORE";
        scoreText = Outlined(HudLabel(scoreBg, "scoreval", ScoreInk, 54, 0.40f, 0.94f, 30));
        var timeBg = HudButton("timecard", gm.hudMoves, 524, 186, 460, 134);
        subText = HudLabel(timeBg, "timelabel", Muted, 26, 0.10f, 0.44f, 30);
        rightText = Outlined(HudLabel(timeBg, "timeval", TimeInk, 54, 0.40f, 0.94f, 30));

        // 진행바 — 이 조각을 놓을 남은 시간
        HudCard("timertrack", 40, 330, 944, 24).color = new Color(1, 1, 1, 0.45f);
        var bar = ArtSlot("timerbar", 40, 330, 944, 24);
        timerBar = bar.gameObject;
        timerFill = NewImage("fill", bar, BarOrange);
        timerFill.sprite = UiTheme.RoundedSprite(10); timerFill.type = Image.Type.Sliced; timerFill.raycastTarget = false;
        Stretch(timerFill.rectTransform);
        timerFill.rectTransform.pivot = new Vector2(0, 0.5f);

        // ---- 보드 / 트레이 자리 (그리는 건 월드 쪽, 여기선 자리만 잰다). 보드 자리는 SetTheme 이 격자에 맞춘다 ----
        boardSlot = ArtSlot("boardslot", DefaultGrid.x, DefaultGrid.y, DefaultGrid.width, DefaultGrid.height);
        traySlot = ArtSlot("trayslot", 624, 1275, 360, 200);

        // ---- 아이템 4종: 판 아래 ----
        int ni = Shop.Items.Length;
        itemBtnLabel = new TMP_Text[ni];
        itemBtnFill = new Image[ni];
        for (int i = 0; i < ni; i++)
        {
            var e = Shop.Items[i];
            float px = 40f + i * 244.3f;
            var itemBg = HudButton("itemcard" + i, gm.hudItems != null && i < gm.hudItems.Length ? gm.hudItems[i] : null, px, 1118, 211, 134);
            var btn = ArtButton("item" + i, px, 1118, 211, 134, () => { if (gm.UseItem(e.Item)) RefreshItemButtons(); });
            // 못 쓰는 상태면 흰 반투명 판으로 흐리게
            var dim = NewImage("dim", btn, new Color(1, 1, 1, 0));
            dim.sprite = UiTheme.RoundedSprite(40); dim.type = Image.Type.Sliced; dim.raycastTarget = false;
            Stretch(dim.rectTransform);
            itemBtnFill[i] = dim;
            HudLabel(itemBg, "itemname" + i, Ink, 26, 0.08f, 0.46f, 14).text = e.Name;
            itemBtnLabel[i] = Outlined(HudLabel(itemBg, "itemcnt" + i, Ink, 44, 0.42f, 0.94f, 14));
        }

        // ---- GOAL 카드 ----
        var goalBg = HudButton("goalcard", gm.hudGoal, 40, 1268, 560, 124);
        goalSub = Outlined(HudLabel(goalBg, "goalsub", GoalInk, 30, 0.14f, 0.86f, 40));

        // 연쇄 팝업 — 위에서 쾅 떨어지는 두 줄 글자에 반짝이가 사방으로 퍼진다
        chainRoot = NewRT("chainpop", safe);
        Place(chainRoot, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040, 520));
        chainSparks = new Image[10];
        for (int i = 0; i < chainSparks.Length; i++)
        {
            var sp = NewImage("spark" + i, chainRoot, Color.white);
            sp.sprite = UiTheme.SparkSprite();
            sp.raycastTarget = false;
            sp.rectTransform.anchorMin = sp.rectTransform.anchorMax = sp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            sp.rectTransform.sizeDelta = new Vector2(150, 150);
            chainSparks[i] = sp;
        }
        chainTop = ChainText("chaintop", 150, new Color(0.62f, 0.76f, 1f), new Color(0.16f, 0.24f, 0.72f), 120);
        chainBig = ChainText("chainbig", 230, new Color(1f, 0.90f, 0.45f), new Color(0.88f, 0.28f, 0.10f), -96);
        // 숫자는 알록달록한 판 위에 그냥 얹힌다 — 글자 그림과 같은 짙은 테두리와 그림자가 있어야 읽힌다
        chainBig.outlineColor = new Color32(16, 23, 63, 255);
        // 테두리와 그림자를 더 키우면 SDF 여백을 넘겨 글자가 덩어리로 뭉갠다. 여기까지가 한계다.
        chainBig.outlineWidth = 0.20f;
        var bigMat = chainBig.fontMaterial;
        bigMat.EnableKeyword("UNDERLAY_ON");
        bigMat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.03f, 0.05f, 0.18f, 0.75f));
        bigMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.40f);
        bigMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.40f);
        bigMat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.05f);
        bigMat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.30f);

        // CHAIN 글자 그림 — 있으면 글자 대신 이걸 쓴다 (글자는 보너스 표시로 남는다)
        chainWord = NewImage("chainword", chainRoot, Color.white);
        chainWord.raycastTarget = false;
        chainWord.preserveAspect = true;
        chainWord.rectTransform.anchorMin = chainWord.rectTransform.anchorMax
            = chainWord.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        chainWord.rectTransform.sizeDelta = new Vector2(820, 300);
        chainWord.rectTransform.anchoredPosition = new Vector2(0, 118);
        chainWord.enabled = false;
        chainRoot.gameObject.SetActive(false);

        feverPopup = NewText("feverpop", safe, "FEVER TIME!", 150, TextAnchor.MiddleCenter, new Color(1f, 0.32f, 0.18f));
        Place(feverPopup.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 220));
        var feverOutline = feverPopup.gameObject.AddComponent<Outline>();
        feverOutline.effectColor = Color.white;
        feverOutline.effectDistance = new Vector2(4f, -4f);
        feverPopup.gameObject.SetActive(false);
    }

    /// <summary>플레이보드 HUD 버튼 그림. 알약·구름은 좌우가 9슬라이스라 폭을 늘려도 끝이 안 찌그러진다.
    /// 그림이 연결되지 않았으면 예전처럼 흰 둥근 카드로 돌아간다 — 한 장 빠졌다고 HUD 가 사라지면 안 된다.</summary>
    Image HudButton(string name, Sprite sp, float x, float y, float w, float h)
    {
        if (sp == null) return HudCard(name, x, y, w, h);
        var img = ArtSlot(name, x, y, w, h).gameObject.AddComponent<Image>();
        img.sprite = sp;
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>버튼 그림 안에 얹는 HUD 글자. 배경의 자식이라 배경만 옮기면 글자가 따라온다.
    /// top·bottom 은 배경 높이에서 차지할 구간(0 = 위, 1 = 아래), pad 는 좌우 여백(둥근 끝을 피한다).
    /// 값이 바뀔 때 크기가 흔들리지 않게 글자 크기는 고정한다.</summary>
    TMP_Text HudLabel(Image bg, string name, Color ink, float size, float top, float bottom, float pad)
    {
        var rt = NewRT(name, bg.transform);
        rt.anchorMin = new Vector2(0, 1f - bottom);
        rt.anchorMax = new Vector2(1, 1f - top);
        rt.offsetMin = new Vector2(pad * PS, 0);
        rt.offsetMax = new Vector2(-pad * PS, 0);

        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.enableAutoSizing = false;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = ink;
        t.raycastTarget = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    /// <summary>밝고 알록달록한 알약 위에서도 읽히게 글자에 흰 테두리를 두른다 (글자가 어둡다).</summary>
    static TMP_Text Outlined(TMP_Text t)
    {
        t.outlineColor = new Color32(255, 255, 255, 230);
        t.outlineWidth = 0.14f;
        return t;
    }

    /// <summary>연쇄 팝업 글자 — 위아래 그라디언트에 두꺼운 테두리. 배경 없이 글자만으로 읽혀야 한다.</summary>
    TMP_Text ChainText(string name, float size, Color top, Color bottom, float y)
    {
        var rt = NewRT(name, chainRoot);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1000, 240);
        rt.anchoredPosition = new Vector2(0, y);

        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold | FontStyles.Italic;
        t.alignment = TextAlignmentOptions.Center;
        t.enableVertexGradient = true;
        t.colorGradient = new VertexGradient(top, top, bottom, bottom);
        t.outlineColor = new Color32(255, 255, 255, 255);
        t.outlineWidth = 0.28f;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>HUD 글자 (TextMeshPro). 칸을 넘치면 스스로 작아진다.</summary>
    TMP_Text HudText(string name, float x, float y, float w, float h, Color ink, float maxSize)
    {
        var t = ArtSlot(name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = maxSize;
        t.enableAutoSizing = true;
        t.fontSizeMin = 8;
        t.fontSizeMax = maxSize;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = ink;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>HUD 글자 밑에 까는 흰 둥근 카드. 테마 그림에는 HUD 자리가 없어서 직접 깐다.</summary>
    Image HudCard(string name, float x, float y, float w, float h)
    {
        var img = ArtSlot(name, x, y, w, h).gameObject.AddComponent<Image>();
        img.sprite = UiTheme.RoundedSprite(40); img.type = Image.Type.Sliced;
        img.color = new Color(1, 1, 1, 0.92f);
        img.raycastTarget = false;
        return img;
    }

    void LateUpdate()
    {
        // 아트 캔버스는 별도 루트라 게임 패널의 켜짐을 따라가게 한다
        if (playArtCanvas != null && gamePanel != null && playArtCanvas.activeSelf != gamePanel.activeInHierarchy)
            playArtCanvas.SetActive(gamePanel.activeInHierarchy);

        if (feverOn)
        {
            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);   // 달아오르듯 맥박친다
            feverTint.color = new Color(1f, 0.3f, 0.15f, Mathf.Lerp(0.16f, 0.30f, k));
        }
    }

    /// <summary>Overlay 캔버스의 월드 모서리는 곧 화면 픽셀이다. 레이아웃 전(폭 0)이면 false.</summary>
    static bool SlotRect(RectTransform rt, out Rect r)
    {
        r = new Rect();
        if (rt == null) return false;
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        r = Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        return r.width > 1f && r.height > 1f;
    }

    /// <summary>보드 칸 격자(W x H 칸)가 들어갈 화면 사각형.</summary>
    public bool BoardSlotRect(out Rect r) { return SlotRect(boardSlot, out r); }

    /// <summary>트레이 조각이 들어갈 화면 사각형.</summary>
    public bool TraySlotRect(out Rect r) { return SlotRect(traySlot, out r); }

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

    /// <summary>Card 에 아래 두께(lip)를 붙인 입체 버튼 — 홈 화면 시작 버튼과 같은 구조다.
    /// 평소엔 두께가 드러나 보이고 누르면 face 가 그 속으로 가라앉는다.
    /// 반환한 fill 이미지는 HookButton 에 그대로 넘길 수 있다 (2단 구조라 부모가 하나 더 있을 뿐).</summary>
    Image LipCard(Transform parent, string name, float x, float y, float w, float h,
                  Color fill, Color lipColor, float radius, float lipPx, Color? borderColor = null)
    {
        var root = NewRT(name, parent);
        Place(root, Top, Top, new Vector2(0, 1), P(x, y), Sz(w, h));

        var lip = NewImage("lip", root, lipColor);
        lip.sprite = Rounded(radius); lip.type = Image.Type.Sliced; lip.raycastTarget = false;
        var lrt = lip.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(0, -lipPx * PS); lrt.offsetMax = Vector2.zero;

        var faceRt = NewRT("face", root);
        faceRt.anchorMin = Vector2.zero; faceRt.anchorMax = Vector2.one;
        faceRt.offsetMin = faceRt.offsetMax = Vector2.zero;
        var faceOuter = faceRt.gameObject.AddComponent<Image>();
        faceOuter.sprite = Rounded(radius); faceOuter.type = Image.Type.Sliced;
        faceOuter.color = borderColor ?? Ink;

        var faceIn = NewImage("fill", faceRt, fill);
        faceIn.sprite = Rounded(Mathf.Max(2f, radius - Bd)); faceIn.type = Image.Type.Sliced;
        faceIn.raycastTarget = false;
        var fir = faceIn.rectTransform;
        fir.anchorMin = Vector2.zero; fir.anchorMax = Vector2.one;
        fir.offsetMin = new Vector2(Bd * PS, Bd * PS); fir.offsetMax = new Vector2(-Bd * PS, -Bd * PS);

        return faceIn;
    }

    /// <summary>lip 두께용 그림자 색 — 원래 색을 그대로 어둡게 낮춘다.</summary>
    static Color Shade(Color c) { return new Color(c.r * 0.78f, c.g * 0.78f, c.b * 0.78f, c.a); }

    Dictionary<ShopItem, Sprite> itemIcons;

    /// <summary>아이템 그림 — 참고 아트(Resources/items)를 그대로 쓰고, 없으면 폭탄만 절차 생성으로 돌아간다.</summary>
    Sprite ItemIconSprite(ShopItem it)
    {
        if (itemIcons == null) itemIcons = new Dictionary<ShopItem, Sprite>();
        Sprite got;
        if (itemIcons.TryGetValue(it, out got)) return got;

        string name = it == ShopItem.BombPiece ? "bomb"
                    : it == ShopItem.Hammer ? "hammer"
                    : it == ShopItem.Rainbow ? "rainbow" : "shuffle";
        var t = Resources.Load<Texture2D>("items/" + name);
        got = t != null
            ? Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), Mathf.Max(t.width, t.height))
            : (it == ShopItem.BombPiece ? BoardView.MakeBombSprite() : null);
        itemIcons[it] = got;
        return got;
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
            itemBtnLabel[i].text = n.ToString();
            itemBtnFill[i].color = new Color(1, 1, 1, usable ? 0f : 0.55f);   // 못 쓰면 흰 판으로 흐리게
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

    // 숫자 색 — CHAIN 글자 그림(파랑·주황·보라·무지개) 순서에 맞춘 네 벌.
    // 글자와 숫자가 따로 놀면 한 덩어리로 안 읽힌다. 마지막 한 벌은 네 꼭짓점을 다 다르게 줘서
    // 숫자 자체가 무지개가 된다.
    static readonly VertexGradient[] ChainInks = {
        new VertexGradient(Palette.Hex(0xCFEBFF), Palette.Hex(0xCFEBFF), Palette.Hex(0x1C5BE8), Palette.Hex(0x1C5BE8)),
        new VertexGradient(Palette.Hex(0xFFE9A0), Palette.Hex(0xFFE9A0), Palette.Hex(0xE8520C), Palette.Hex(0xE8520C)),
        new VertexGradient(Palette.Hex(0xF0D2FF), Palette.Hex(0xF0D2FF), Palette.Hex(0x7A22D8), Palette.Hex(0x7A22D8)),
        new VertexGradient(Palette.Hex(0x4CE0FF), Palette.Hex(0xFFE84C), Palette.Hex(0xFF3FA8), Palette.Hex(0x5CFF8A)),
    };
    static readonly VertexGradient ChainGold =
        new VertexGradient(Palette.Hex(0xFFE573), Palette.Hex(0xFFE573), Palette.Hex(0xE0471A), Palette.Hex(0xE0471A));

    public void ShowChainPopup(int chain, int scoreGained)
    {
        if (chainRoot == null) return;
        bool isChain = chain >= 2;

        // 연쇄일 때는 CHAIN 글자 그림을 한 장씩 돌아가며 쓴다. 그림이 없으면 예전 글자 그대로.
        var words = gm.chainWords;
        Sprite word = isChain && words != null && words.Length > 0 ? words[chainWordNext % words.Length] : null;
        if (word != null)
        {
            chainWord.sprite = word;
            chainBig.colorGradient = ChainInks[(chainWordNext % words.Length) % ChainInks.Length];
        }
        else chainBig.colorGradient = ChainGold;
        if (words != null && words.Length > 0) chainWordNext = (chainWordNext + 1) % words.Length;

        chainWord.enabled = word != null;
        chainTop.enabled = word == null;
        chainTop.text = isChain ? "CHAIN" : "BONUS";
        chainBig.text = isChain ? ("x" + chain + "!!") : ("+" + scoreGained.ToString("N0"));
        chainAngle = Random.Range(0f, 360f);   // 반짝이가 매번 같은 자리로 튀지 않게
        if (chainCo != null) StopCoroutine(chainCo);
        chainCo = StartCoroutine(ChainPopupCo());
    }

    /// <summary>위에서 가속해 떨어져 쾅 하고 박히고, 납작해졌다 튕겨 제자리로 온다.
    /// 박히는 순간 반짝이가 사방으로 퍼지고 화면이 한 번 흔들린다.</summary>
    IEnumerator ChainPopupCo()
    {
        chainRoot.gameObject.SetActive(true);
        const float fall = 0.16f, punch = 0.34f, fade = 0.22f;

        for (float t = 0; t < fall; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / fall);
            chainRoot.anchoredPosition = new Vector2(0, Mathf.Lerp(460f, 0f, k * k));   // 등가속 낙하
            SetChainPose(Mathf.Lerp(1.35f, 1.05f, k * k), Mathf.Lerp(0.8f, 1.05f, k * k), 0f, Mathf.Clamp01(k * 3f));
            yield return null;
        }

        gm.Shake(0.34f, 0.16f, true);
        for (float t = 0; t < punch; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / punch);
            float e = Punch(k);                        // 납작 → 되튐 → 안정
            SetChainPose(Mathf.Lerp(1.28f, 1f, e), Mathf.Lerp(0.72f, 1f, e), Mathf.Lerp(0.05f, 1f, k), 1f);
            yield return null;
        }

        for (float t = 0; t < fade; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / fade);
            chainRoot.anchoredPosition = new Vector2(0, k * 60f);   // 스르르 떠오르며 사라진다
            SetChainPose(1f, 1f, 1f + k * 0.6f, 1f - k);
            yield return null;
        }

        chainRoot.gameObject.SetActive(false);
        chainRoot.anchoredPosition = Vector2.zero;
        chainCo = null;
    }

    /// <summary>되튐 곡선 (0→1). 끝에서 1 을 살짝 넘겼다가 돌아온다 — 쾅 박히는 느낌을 준다.</summary>
    static float Punch(float k)
    {
        float p = Mathf.Clamp01(k) - 1f;
        return 1f + 2.70158f * p * p * p + 1.70158f * p * p;
    }

    /// <summary>팝업 자세 한 번에 — 가로·세로 눌림, 반짝이가 퍼진 정도(0~1 이상), 투명도.</summary>
    void SetChainPose(float sx, float sy, float sparkSpread, float alpha)
    {
        chainRoot.localScale = new Vector3(sx, sy, 1f);
        chainTop.alpha = chainBig.alpha = alpha;
        if (chainWord != null) chainWord.color = new Color(1f, 1f, 1f, alpha);
        for (int i = 0; i < chainSparks.Length; i++)
        {
            float ang = chainAngle + i * (360f / chainSparks.Length);
            float r = Mathf.Lerp(50f, 430f, Mathf.Clamp01(sparkSpread));
            var rt = chainSparks[i].rectTransform;
            rt.anchoredPosition = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad) * r, Mathf.Sin(ang * Mathf.Deg2Rad) * r * 0.62f);
            rt.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.1f, Mathf.Clamp01(sparkSpread));
            rt.localRotation = Quaternion.Euler(0, 0, ang * 0.5f);
            var c = chainSparks[i].color;
            c.a = alpha * (1f - Mathf.Clamp01(sparkSpread - 1f)) * 0.9f;
            chainSparks[i].color = c;
        }
    }

    /// <summary>피버 타임 시작/끝. 배경이 붉게 달아오르고, 시작할 때 가운데에 문구가 크게 뜬다.</summary>
    public void SetFever(bool on)
    {
        feverOn = on;
        feverTint.enabled = on;
        if (feverCo != null) { StopCoroutine(feverCo); feverCo = null; }
        feverPopup.gameObject.SetActive(false);
        if (on) feverCo = StartCoroutine(FeverPopupCo());
    }

    IEnumerator FeverPopupCo()
    {
        var rt = feverPopup.rectTransform;
        feverPopup.gameObject.SetActive(true);
        const float dur = 1.2f;   // 실제 초 — 피버 동안은 게임 시간이 빨라진다
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float k = t / dur;
            rt.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, Mathf.Clamp01(k * 4f));   // 쾅 하고 커졌다가 자리 잡는다
            var c = feverPopup.color;
            c.a = 1f - Mathf.Clamp01((k - 0.7f) / 0.3f);
            feverPopup.color = c;
            yield return null;
        }
        feverPopup.gameObject.SetActive(false);
        feverCo = null;
    }

    /// <summary>매 프레임 HUD 갱신 (Playing 중 GameManager가 호출)</summary>
    public void UpdateHud(GameManager g)
    {
        scoreText.text = g.Score.ToString("N0");

        // 제목 자리: 피버 중에는 남은 초, 스테이지는 목표 문구, 타임어택은 모드 이름
        bool ta = g.TimeAttackMode;
        if (g.FeverOn)
        {
            gameEyebrow.text = "FEVER TIME!  " + Mathf.CeilToInt(g.FeverLeftSec);
            gameEyebrow.color = Coral;
        }
        else if (!ta)
        {
            gameEyebrow.text = GoalLine(g);
            gameEyebrow.color = g.GoalMet ? Coral : TitleInk;
        }
        else
        {
            gameEyebrow.text = "TIME ATTACK!";
            gameEyebrow.color = TitleInk;
        }
        goalSub.text = ta ? "SCORE " + g.Score.ToString("N0") : ProgressLine(g);

        // 시간 카드: 타임어택은 남은 시간, 횟수 모드는 남은 수
        float frac;
        if (ta)
        {
            subText.text = "TIME LEFT";
            int sec = Mathf.CeilToInt(g.TimeLeftSec);
            rightText.text = (sec / 60) + ":" + (sec % 60).ToString("00");
            frac = g.PieceTimerFrac;   // 막대는 이 조각을 놓을 시간. 전체 시간은 카드에 있다
        }
        else
        {
            subText.text = g.PieceLimited ? "PIECES LEFT" : "MOVES LEFT";
            rightText.text = g.MovesLeft.ToString();
            frac = g.PieceTimerFrac;   // 다 지나가면 조각이 버려진다
        }
        timerFill.rectTransform.localScale = new Vector3(Mathf.Clamp01(frac), 1, 1);
    }

    /// <summary>이 판의 성격을 한마디로. 종류마다 문구가 하나씩이라
    /// 판에 들어서는 순간 무슨 판인지 읽힌다.
    /// 겹친 판은 조건마다 한 줄씩 모두 보여 준다 — 하나만 보이면 다른 조건이 남아
    /// 판이 안 끝나는 이유가 안 읽힌다. 제목 칸은 글자가 넘치면 스스로 줄어든다.</summary>
    static string GoalLine(GameManager g)
    {
        var st = g.Stage;
        var lines = new List<string>();
        if (g.MarksTotal > 0) lines.Add("POP TARGET BLOCKS!");
        if (g.PieceLimited) lines.Add("JUST " + st.PieceLimit + " PIECES!!");
        if (st.ClearBlocks > 0) lines.Add("POP " + st.ClearBlocks + " BLOCKS!");
        return lines.Count > 0 ? string.Join("\n", lines.ToArray()) : "KEEP POPPING!";
    }

    /// <summary>지금 얼마나 왔는지. 조건 이름은 제목이 모두 보여 주므로 여기선 수치만 쓴다.</summary>
    static string ProgressLine(GameManager g)
    {
        string s = "STAGE " + g.stageLevel;
        if (g.ClearTarget > 0) s += "   " + Mathf.Min(g.Broken, g.ClearTarget) + " / " + g.ClearTarget;
        if (g.MarksTotal > 0) s += "   TARGET " + (g.MarksTotal - g.MarksLeft) + " / " + g.MarksTotal;
        return s;
    }

    /// <summary>다음 조각 미리보기 (미니 셀 그리드)</summary>
    /// <summary>지금 들고 있는 조각. 크게, 채도를 올려 쨍하게 그린다.

    const float HoldCell = 56f;   // 들고 있는 조각 — 크게
    const float HoldStep = 60f;
    const float NextCell = 24f;   // 다음 조각 — 작게
    const float NextStep = 27f;

    /// <summary>채도와 밝기를 올려 쨍하게. 판 위 블록은 채도를 낮춰 그리므로

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

    // ---------- 아트 좌표 헬퍼 ----------
    //
    // 화면 아트 위에 투명 버튼과 '실제 값' 을 얹는다. 좌표는 아트 원본 픽셀 기준이며 y 는 위가 0 이다.

    RectTransform curRoot; float curW, curH;   // ArtSlot 이 지금 어느 아트 위에 자리를 잡는지

    /// <summary>아트 픽셀 좌표(위가 0)에 맞춘 자리 하나.</summary>
    RectTransform ArtSlot(string name, float x, float y, float w, float h)
    {
        var rt = NewRT(name, curRoot);
        PlaceSlot(rt, x, y, w, h);
        return rt;
    }

    void PlaceSlot(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(x / curW, 1f - (y + h) / curH);
        rt.anchorMax = new Vector2((x + w) / curW, 1f - y / curH);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>아트에 그려진 버튼 위에 얹는 투명 버튼. 누르면 눌리는 반응만 준다.</summary>
    RectTransform ArtButton(string name, float x, float y, float w, float h, UnityAction onClick)
    {
        var rt = ArtSlot(name, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0);      // 안 보이지만 터치는 받는다
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(onClick);
        rt.gameObject.AddComponent<UiPressImage>().target = rt;
        return rt;
    }

    /// <summary>아트에 그려진 숫자를 같은 색 판으로 덮고 그 위에 실제 값을 쓴다.
    /// 판을 안 깔면 밑에 그려진 숫자가 비쳐서 두 개로 겹쳐 보인다.</summary>
    Text ArtValue(string name, float x, float y, float w, float h,
                  Color patch, Color ink, TextAnchor anchor, int maxSize)
    {
        var rt = ArtSlot(name, x, y, w, h);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = patch;
        bg.raycastTarget = false;

        var t = NewText("v", rt, "", maxSize, anchor, ink);
        t.fontStyle = FontStyle.Bold;
        t.raycastTarget = false;
        t.resizeTextForBestFit = true;      // 아트가 화면 크기에 맞춰 늘어나므로 글자도 따라간다
        t.resizeTextMinSize = 8;
        t.resizeTextMaxSize = maxSize;
        Stretch(t.rectTransform);
        return t;
    }

    static readonly Vector2 Top = new Vector2(0.5f, 1f);

    /// <summary>글자 사이에 공백을 끼워 자간을 넓힌다 (uGUI 에는 letter-spacing 이 없다).</summary>
    static string Spaced(string t)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in t) { sb.Append(ch); sb.Append(' '); }
        return sb.ToString().TrimEnd();
    }

    // ---------- 결과 ----------

    Text resultCoins;

    void BuildResultPanel()
    {
        resultDim = NewImage("resultdim", transform, new Color(0.106f, 0.129f, 0.255f, 0.55f));
        resultPanel = resultDim.gameObject;
        Stretch((RectTransform)resultPanel.transform);

        var card = Card(resultPanel.transform, "rcard", 0, 0, 330, 380, Cream, 24);
        var cr = (RectTransform)card.transform.parent;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;
        resultCardRt = cr;

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

        HookButton(Card(card.transform, "rrank", 22, 244, 286, 46, Color.white, 16),
                   () => ShowRanking(false), "LEADERBOARD", 14);
        // 클리어했을 때만 NEXT 가 함께 뜬다. 자리 계산은 ShowResult 가 한다.
        HookButton(Card(card.transform, "rnext", 22, 302, 136, 52, Teal, 18),
                   () => gm.StartNextStage(), "NEXT", 16);
        HookButton(Card(card.transform, "retry", 22, 302, 136, 52, Coral, 18),
                   () => gm.StartGame(), "RETRY", 16);
        HookButton(Card(card.transform, "rhome", 172, 302, 136, 52, Color.white, 18),
                   () => gm.GoHome(), "HOME", 16);

        rankBtn = (RectTransform)card.transform.Find("rrank");
        nextBtn = (RectTransform)card.transform.Find("rnext");
        retryBtn = (RectTransform)card.transform.Find("retry");
        homeBtn = (RectTransform)card.transform.Find("rhome");
    }

    public void ShowResult(bool ta, int score, int best, bool newBest, int coins)
    { ShowResult(ta, score, best, newBest, coins, 0, false); }

    public void ShowResult(bool ta, int score, int best, bool newBest, int coins, int level, bool cleared)
    {
        resultPanel.SetActive(true);
        // 목표 점수를 없앴으므로 성공/실패가 아니라 '끝났다 + 얼마 냈다' 만 보여준다.
        // 타임어택은 성공/실패가 없다. 스테이지는 목표를 채웠는지로 갈린다.
        if (ta) resultTitle.text = "TIME'S UP!";
        else resultTitle.text = cleared ? "STAGE " + level + " CLEAR!" : "STAGE " + level + " FAILED";
        resultTitle.color = (cleared || newBest) ? Coral : Ink;
        resultScore.text = score.ToString("N0");
        resultBest.text = newBest ? "NEW BEST!" : "BEST  " + best.ToString("N0");
        resultBest.color = newBest ? Coral : Body;
        resultCoins.text = "+" + coins.ToString("N0");

        // 버튼 줄: 클리어면 NEXT / RETRY / HOME 셋, 아니면 RETRY / HOME 둘
        bool showNext = !ta && cleared;
        if (nextBtn != null) nextBtn.gameObject.SetActive(showNext);
        if (showNext)
        {
            const float w = 88f, gap = 11f;
            PlaceButton(nextBtn, 22, w);
            PlaceButton(retryBtn, 22 + w + gap, w);
            PlaceButton(homeBtn, 22 + (w + gap) * 2, w);
        }
        else
        {
            PlaceButton(retryBtn, 22, 136);
            PlaceButton(homeBtn, 172, 136);
        }

        // 리더보드: 스테이지를 깼으면 버튼도 감추고 저절로 열리지도 않는다. 실패·타임업만 연다
        rankAllowed = ta || !cleared;
        if (rankBtn != null) rankBtn.gameObject.SetActive(rankAllowed);

        if (resultDropCo != null) StopCoroutine(resultDropCo);
        resultCardRt.localScale = Vector3.one;   // 이전 연출이 중간에 끊겼을 수 있으니 초기화

        // 실패/타임업이면 카드가 위에서 덜컹 떨어지고, 클리어면 가운데서 짠 하고 튀어나온다.
        bool gameOver = ta || !cleared;
        resultDropCo = StartCoroutine(gameOver ? ResultDropInCo() : ResultPopInCo());
    }

    const float ResultDimAlpha = 0.55f;
    const float ResultDimAlphaGameOver = 0.68f;   // 실패/타임업 때 배경을 조금 더 어둡게

    void SetDimAlpha(float a)
    {
        var c = resultDim.color; c.a = a; resultDim.color = c;
    }

    /// <summary>게임오버 카드가 화면 위에서 덜컹거리며 떨어져 가운데에 안착한다. 착지 1초 뒤 리더보드를 연다.</summary>
    IEnumerator ResultDropInCo()
    {
        var panelRt = (RectTransform)resultPanel.transform;
        float cardH = resultCardRt.rect.height;
        float startY = panelRt.rect.height * 0.5f + cardH * 0.5f + 40f;
        const float overshoot = -16f;
        const float dur = 0.42f;

        SetDimAlpha(0f);
        resultCardRt.anchoredPosition = new Vector2(0, startY);

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float y = k < 0.75f
                ? Mathf.Lerp(startY, overshoot, (k / 0.75f) * (k / 0.75f))   // 가속하며 떨어지다 살짝 지나친다
                : Mathf.Lerp(overshoot, 0f, (k - 0.75f) / 0.25f);            // 덜컹, 제자리로
            resultCardRt.anchoredPosition = new Vector2(0, y);
            SetDimAlpha(Mathf.Lerp(0f, ResultDimAlphaGameOver, k));
            yield return null;
        }
        resultCardRt.anchoredPosition = Vector2.zero;
        SetDimAlpha(ResultDimAlphaGameOver);

        yield return OpenRankAfterLanding();
        resultDropCo = null;
    }

    /// <summary>클리어 카드가 가운데서 짠! 하고 튀어나오듯 확대·안착한다. 착지 1초 뒤 리더보드를 연다.</summary>
    IEnumerator ResultPopInCo()
    {
        const float overshoot = 1.12f;
        const float dur = 0.32f;

        resultCardRt.anchoredPosition = Vector2.zero;
        resultCardRt.localScale = Vector3.one * 0.4f;
        SetDimAlpha(0f);

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float scale = k < 0.7f
                ? Mathf.Lerp(0.4f, overshoot, k / 0.7f)          // 확 커지며 튀어나온다
                : Mathf.Lerp(overshoot, 1f, (k - 0.7f) / 0.3f);  // 짠! 하고 제자리로
            resultCardRt.localScale = Vector3.one * scale;
            SetDimAlpha(Mathf.Lerp(0f, ResultDimAlpha, k));
            yield return null;
        }
        resultCardRt.localScale = Vector3.one;
        SetDimAlpha(ResultDimAlpha);

        yield return OpenRankAfterLanding();
        resultDropCo = null;
    }

    /// <summary>착지 1초 뒤 리더보드를 연다 (결과 화면을 벗어났으면 열지 않는다).</summary>
    IEnumerator OpenRankAfterLanding()
    {
        if (!rankAllowed) yield break;
        if (Leaderboard.I == null || !Leaderboard.I.Configured) yield break;
        yield return new WaitForSeconds(1f);
        if (gm.Phase == GamePhase.Result) ShowRanking(false);
    }

    // ---------- 랭킹 ----------

    // 순위 표식 색 — 1~3위만 강조
    static readonly Color[] MedalColors = {
        Palette.Hex(0xE4C05A), Palette.Hex(0xA9B4BC), Palette.Hex(0xC98A57),
    };
    const float RowH = 40f;   // 프로토타입 단위. 행 10개가 내 점수/광고 버튼 위에서 끝나야 한다

    static readonly Color TabOff = new Color(0.52f, 0.58f, 0.72f, 1f);   // 그림 탭을 안 고른 쪽으로 흐리게
    static readonly Color DeepInk = Palette.Hex(0xEAF3FF);               // 짙은 판 위에 올라가는 글자색

    /// <summary>카드 하나에 리더보드 그림을 씌운다. 그림이 없으면 색 카드 그대로 둔다.
    /// 색을 입힐 대상이 그림 유무에 따라 달라지므로(테두리 vs 안쪽) 그 Image 를 돌려준다.</summary>
    static Image SkinCard(Image fill, Sprite sp, bool sliced, bool hideLabel)
    {
        if (fill == null || sp == null) return fill;
        if (hideLabel)
        {
            var l = fill.transform.Find("l");
            if (l != null) l.gameObject.SetActive(false);   // 글자가 그림에 이미 박혀 있다
        }
        var outer = fill.transform.parent.GetComponent<Image>();
        outer.sprite = sp;
        outer.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        outer.color = Color.white;
        fill.enabled = false;   // 자식(글자·국기)은 그대로 두고 판때기만 끈다
        return outer;
    }

    void BuildRankPanel()
    {
        var sk = gm.leaderboard;
        rankPanel = NewImage("rankdim", transform, new Color(0.106f, 0.129f, 0.255f, 0.62f)).gameObject;
        Stretch((RectTransform)rankPanel.transform);

        var card = Card(rankPanel.transform, "rcard", 0, 0, 350, 700, ScreenBg, 24);
        var cr = (RectTransform)card.transform.parent;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;
        SkinCard(card, sk.Panel, true, false);
        bool skin = sk.Panel != null;

        rankTitle = NewText("t", card.transform, "LEADERBOARD", Mathf.RoundToInt(20 * PS), TextAnchor.UpperLeft, Ink);
        rankTitle.fontStyle = FontStyle.Bold;
        Anchor(rankTitle.transform, 0, 1, 20, -18, 240, 30);

        rankSubTitle = NewText("st", card.transform, "", Mathf.RoundToInt(10 * PS), TextAnchor.UpperLeft, Muted);
        Anchor(rankSubTitle.transform, 0, 1, 20, -44, 240, 16);

        // 제목 배너는 판 위 테두리에 걸터앉는다 — 테두리 가운데 보석이 가려져 늘어난 티가 안 난다
        if (sk.Title != null)
        {
            rankTitle.enabled = false;
            var banner = NewImage("tart", card.transform, Color.white);
            banner.sprite = sk.Title; banner.raycastTarget = false; banner.preserveAspect = true;
            Anchor(banner.transform, 0.5f, 1, 0, 12, 250, 104);
            rankSubTitle.alignment = TextAnchor.UpperCenter;
            rankSubTitle.color = DeepInk;
            Anchor(rankSubTitle.transform, 0.5f, 1, 0, -46, 240, 16);
        }

        var closeFill = Card(card.transform, "rkclose", 0, 0, 62, 34, Color.white, 12);
        HookButton(closeFill, () => rankPanel.SetActive(false), "X", 13);
        SkinCard(closeFill, sk.Close, false, true);
        var clr = (RectTransform)card.transform.Find("rkclose");
        clr.anchorMin = clr.anchorMax = clr.pivot = new Vector2(1, 1);
        clr.sizeDelta = sk.Close != null ? Sz(38, 39) : Sz(62, 34);
        clr.anchoredPosition = new Vector2(-14 * PS, -14 * PS);

        var meBtn = Card(card.transform, "tabme", 20, 72, 152, 40, Teal, 14);
        HookButton(meBtn, () => { rankNationTab = false; RefreshRank(); }, "PLAYERS", 13);
        rankTabMe = SkinCard(meBtn, sk.TabPlayers, false, true);
        var natBtn = Card(card.transform, "tabnat", 182, 72, 148, 40, Color.white, 14);
        HookButton(natBtn, () => { rankNationTab = true; RefreshRank(); }, "NATIONS", 13);
        rankTabNation = SkinCard(natBtn, sk.TabNations, false, true);

        rankRows = new RankRow[RankRowCount];
        for (int i = 0; i < RankRowCount; i++)
            rankRows[i] = MakeRankRow(card.transform, "row" + i, 124 + i * RowH, false, sk.RowFor(i + 1));

        rankEmpty = NewText("e", card.transform, "", Mathf.RoundToInt(12 * PS), TextAnchor.MiddleCenter,
                            skin ? DeepInk : Muted);
        Anchor(rankEmpty.transform, 0.5f, 1, 0, -300, 300, 40);

        // ---- 맨 아래: 내 점수 ----
        // 내 줄은 그림을 쓰지 않는다 — 내 순위 바 그림에는 숫자(26)가 그려져 있어 순위가 바뀌면 맞지 않는다
        myRowLabel = NewText("ml", card.transform, "YOU", Mathf.RoundToInt(9 * PS), TextAnchor.UpperLeft,
                             skin ? DeepInk : Muted);
        Anchor(myRowLabel.transform, 0, 0, 22, 138, 200, 14);
        myRow = MakeRankRow(card.transform, "myrow", 0, true, null);
        var mrt = (RectTransform)myRow.Bg.transform.parent;
        mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0);
        mrt.anchoredPosition = new Vector2(0, 92 * PS);

        var adFill = Card(card.transform, "adbtn", 20, 0, 310, 48, Coral, 16);
        var art = (RectTransform)adFill.transform.parent;
        art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0);
        HookButton(adFill, OnAdButton, "WATCH AD", 14);
        SkinCard(adFill, sk.AdButton, false, true);
        art.sizeDelta = sk.AdButton != null ? Sz(300, 60) : Sz(310, 48);
        art.anchoredPosition = new Vector2(0, (sk.AdButton != null ? 34 : 30) * PS);
        adBtn = art.GetComponent<Button>();
        adBtnLabel = adFill.transform.Find("l").GetComponent<Text>();
    }

    /// <summary>순위 줄 하나: 배경 + 순위 + 국가배지 + 이름 + 점수.
    /// 줄 그림에는 순위 숫자와 빈 칸(동그라미·점수 칩)이 이미 그려져 있어, 글자를 그 칸 위치에 맞춰 얹는다.</summary>
    RankRow MakeRankRow(Transform parent, string name, float y, bool highlight, Sprite art)
    {
        var r = new RankRow();
        r.Bg = Card(parent, name, 20, y, 310, RowH - 6, highlight ? Yellow : Color.white, 12);
        var t = (RectTransform)r.Bg.transform.parent;
        t.anchorMin = t.anchorMax = t.pivot = new Vector2(0.5f, 1);
        t.sizeDelta = Sz(310, RowH - 6);
        t.anchoredPosition = new Vector2(0, -y * PS);
        SkinCard(r.Bg, art, false, false);
        bool skin = art != null;
        float badgeX = skin ? 66.5f : 40f;   // 그림 속 동그라미 칸 한가운데

        r.Rank = NewText("rk", r.Bg.transform, "", Mathf.RoundToInt(12 * PS), TextAnchor.MiddleCenter, Ink);
        r.Rank.fontStyle = FontStyle.Bold;
        Anchor(r.Rank.transform, 0, 0.5f, 8, 0, 30, 22);
        r.Rank.enabled = !skin;   // 줄 그림에 순위 숫자가 이미 박혀 있다

        // 국기 — 둥근 판(Badge)이 마스크가 되어 모서리를 잘라낸다. 국기가 없는 나라는 판 색으로 대신한다
        r.Badge = NewImage("badge", r.Bg.transform, Color.white);
        r.Badge.sprite = Rounded(6); r.Badge.type = Image.Type.Sliced; r.Badge.raycastTarget = false;
        Anchor(r.Badge.transform, 0, 0.5f, badgeX, 4.5f, 27, 18);   // 줄 안쪽은 반높이 14 정도 — 국기와 코드가 둘 다 그 안에 들어가야 한다
        r.Badge.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        r.Flag = NewImage("flag", r.Badge.transform, Color.white);
        r.Flag.raycastTarget = false;
        r.Flag.preserveAspect = true;
        Stretch(r.Flag.rectTransform);

        // 국기 밑에 작은 코드 글자
        r.Code = NewText("c", r.Bg.transform, "", Mathf.RoundToInt(6 * PS), TextAnchor.MiddleCenter, Muted);
        r.Code.fontStyle = FontStyle.Bold;
        Anchor(r.Code.transform, 0, 0.5f, badgeX, -9, 27, 7);   // 국기와 같은 폭 — 가운데가 맞는다

        r.Name = NewText("n", r.Bg.transform, "", Mathf.RoundToInt(12 * PS), TextAnchor.MiddleLeft, Ink);
        Anchor(r.Name.transform, 0, 0.5f, skin ? 98 : 82, 0, skin ? 148 : 100, 22);   // 점수 칸(190~)을 침범하지 않는다

        r.Score = NewText("s", r.Bg.transform, "", Mathf.RoundToInt(13 * PS), TextAnchor.MiddleRight, Ink);
        r.Score.fontStyle = FontStyle.Bold;
        Anchor(r.Score.transform, 1, 0.5f, -10, 0, 110, 22);
        return r;
    }

    void FillRow(RankRow r, int rank, string code, string name, int score, bool isMe)
    {
        r.SetActive(true);
        r.Rank.text = rank > 0 ? rank.ToString() : "-";
        r.Rank.color = rank >= 1 && rank <= 3 ? MedalColors[rank - 1] : Muted;
        var flag = CountryFlags.Get(code);
        r.Flag.sprite = flag;
        r.Flag.enabled = flag != null;
        r.Badge.color = flag != null ? Color.white : PlayerAccount.BadgeColor(code);   // 국기가 없으면 색 배지로 대신한다
        r.Code.text = code;
        r.Name.text = Trim(name, 11);
        r.Name.color = Ink;
        r.Score.text = score.ToString("N0");
        r.Score.color = Ink;
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
        bool skin = gm.leaderboard.TabPlayers != null;
        rankTabMe.color = skin ? (rankNationTab ? TabOff : Color.white) : (rankNationTab ? Color.white : Teal);
        rankTabNation.color = skin ? (rankNationTab ? Color.white : TabOff) : (rankNationTab ? Teal : Color.white);
        rankSubTitle.text = gm.timeAttack ? "TIME ATTACK" : "MOVES";
        // 제목 배너에 MOVES 띠가 이미 그려져 있다 — 타임어택일 때만 따로 적는다
        rankSubTitle.enabled = gm.leaderboard.Title == null || gm.timeAttack;
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

    /// <summary>결과 화면 버튼 한 칸을 옮긴다 (프로토 x, 너비).</summary>
    static void PlaceButton(RectTransform rt, float x, float w)
    {
        if (rt == null) return;
        rt.sizeDelta = Sz(w, 52);
        rt.anchoredPosition = P(x, 302);
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

        var card = Card(shopPanel.transform, "shopcard", 20, 50, 350, 744, ScreenBg, 24);
        var cr = (RectTransform)card.transform.parent;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;

        var title = NewText("t", card.transform, "SHOP", Mathf.RoundToInt(24 * PS), TextAnchor.UpperLeft, Ink);
        title.fontStyle = FontStyle.Bold;
        Anchor(title.transform, 0, 1, 20, -18, 200, 36);

        // 코인 칩 — 카드 오른쪽 위
        shopCoins = CoinChip(card.transform, "coinchip", 1, 1, -16, -14, 124, 38);
        var chipRoot = shopCoins.transform.parent.parent.gameObject;   // 칩 바깥(테두리)
        var cb = chipRoot.AddComponent<Button>();
        cb.targetGraphic = chipRoot.GetComponent<Image>();
        cb.transition = Selectable.Transition.None;
        cb.onClick.AddListener(TapCoins);

        var itemsLabel = NewText("il", card.transform, Spaced("ITEMS"), Mathf.RoundToInt(10 * PS),
                                 TextAnchor.UpperLeft, Muted);
        Anchor(itemsLabel.transform, 0, 1, 20, -88, 200, 16);

        int n = Shop.Items.Length;
        shopBuyFill = new Image[n]; shopBuyLabel = new Text[n]; shopOwned = new Text[n];
        for (int i = 0; i < n; i++)
        {
            var e = Shop.Items[i];
            int idx = i;
            float rowY = 108 + i * 78;

            var row = Card(card.transform, "item" + i, 0, 0, 314, 70, Color.white, 18);
            var rr = (RectTransform)row.transform.parent;
            rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0.5f, 1);
            rr.sizeDelta = Sz(314, 70); rr.anchoredPosition = new Vector2(0, -rowY * PS);

            var swatch = NewImage("sw", row.transform, e.Tint);
            swatch.sprite = Rounded(10); swatch.type = Image.Type.Sliced; swatch.raycastTarget = false;
            Anchor(swatch.transform, 0, 1, 12, -10, 48, 48);

            var nm = NewText("n", row.transform, e.Name, Mathf.RoundToInt(14 * PS), TextAnchor.UpperLeft, Ink);
            nm.fontStyle = FontStyle.Bold;
            Anchor(nm.transform, 0, 1, 70, -11, 150, 20);

            var ds = NewText("d", row.transform, e.Desc, Mathf.RoundToInt(9 * PS), TextAnchor.UpperLeft, Muted);
            Anchor(ds.transform, 0, 1, 70, -32, 165, 30);

            shopOwned[i] = NewText("own", row.transform, "", Mathf.RoundToInt(9 * PS), TextAnchor.LowerRight, Muted);
            Anchor(shopOwned[i].transform, 1, 0, -12, 8, 120, 16);

            shopBuyFill[i] = Card(row.transform, "buy", 0, 0, 78, 36, Teal, 12);
            var br = (RectTransform)shopBuyFill[i].transform.parent;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(1, 1);
            br.sizeDelta = Sz(78, 36); br.anchoredPosition = new Vector2(-12 * PS, -10 * PS);
            var bb = br.gameObject.AddComponent<Button>();
            bb.targetGraphic = br.GetComponent<Image>();
            bb.transition = Selectable.Transition.None;
            bb.onClick.AddListener(() => Buy(idx));
            br.gameObject.AddComponent<UiPressImage>().target = br;
            shopBuyLabel[i] = NewText("l", shopBuyFill[i].transform, "", Mathf.RoundToInt(13 * PS), TextAnchor.MiddleCenter, Ink);
            shopBuyLabel[i].fontStyle = FontStyle.Bold;
            Stretch(shopBuyLabel[i].rectTransform);
        }

        // ---- 타일 스킨 ----
        float skinTop = 108 + n * 78 + 14;
        var skinLabel = NewText("sl", card.transform, Spaced("TILE SKINS"), Mathf.RoundToInt(10 * PS),
                                TextAnchor.UpperLeft, Muted);
        Anchor(skinLabel.transform, 0, 1, 20, -skinTop, 200, 16);

        int m = Shop.Skins.Length;
        skinBuyFill = new Image[m]; skinBuyLabel = new Text[m]; skinSwatch = new Image[m];
        for (int i = 0; i < m; i++)
        {
            var sk = Shop.Skins[i];
            int idx = i;
            var row = Card(card.transform, "skin" + i, 0, 0, 314, 56, Color.white, 16);
            var rr = (RectTransform)row.transform.parent;
            rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0.5f, 1);
            rr.sizeDelta = Sz(314, 56);
            rr.anchoredPosition = new Vector2(0, -(skinTop + 20 + i * 64) * PS);

            // 실제 타일 스프라이트를 팔레트 색 3가지로 보여준다 — 사기 전에 재질이 보여야 한다.
            // 게임과 똑같이 광택·표정 오버레이까지 얹어야 미리보기가 실제와 같다.
            var sp = BoardView.MakeTileSprite(sk.Skin);
            var ovSp = BoardView.MakeTileOverlaySprite();
            var demo = Palette.Generate(4, gm.themes != null && gm.themes.Length > 0 ? gm.themes[0].Colors : null);
            for (int k = 0; k < 3; k++)
            {
                var sw = NewImage("sw" + k, row.transform, demo[k]);
                sw.sprite = sp; sw.raycastTarget = false;
                Anchor(sw.transform, 0, 0.5f, 12 + k * 40, 0, 36, 36);
                var swo = NewImage("ov" + k, sw.transform, Color.white);
                swo.sprite = ovSp; swo.raycastTarget = false;
                Stretch(swo.rectTransform);
                if (k == 0) skinSwatch[i] = sw;
            }

            var nm = NewText("n", row.transform, sk.Name, Mathf.RoundToInt(13 * PS), TextAnchor.MiddleLeft, Ink);
            nm.fontStyle = FontStyle.Bold;
            Anchor(nm.transform, 0, 0.5f, 136, 0, 90, 22);

            skinBuyFill[i] = Card(row.transform, "buy", 0, 0, 82, 34, Teal, 12);
            var br = (RectTransform)skinBuyFill[i].transform.parent;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(1, 0.5f);
            br.sizeDelta = Sz(82, 34); br.anchoredPosition = new Vector2(-10 * PS, 0);
            var bb = br.gameObject.AddComponent<Button>();
            bb.targetGraphic = br.GetComponent<Image>();
            bb.transition = Selectable.Transition.None;
            bb.onClick.AddListener(() => SkinTap(idx));
            br.gameObject.AddComponent<UiPressImage>().target = br;
            skinBuyLabel[i] = NewText("l", skinBuyFill[i].transform, "", Mathf.RoundToInt(12 * PS),
                                      TextAnchor.MiddleCenter, Ink);
            skinBuyLabel[i].fontStyle = FontStyle.Bold;
            Stretch(skinBuyLabel[i].rectTransform);
        }

        float adY = skinTop + 20 + m * 64 + 12;
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

    /// <summary>안 샀으면 사고, 샀으면 착용한다.</summary>
    void SkinTap(int i)
    {
        var sk = Shop.Skins[i];
        if (!Wallet.OwnsSkin(sk.Skin))
        {
            if (!Wallet.SpendCoins(sk.Price)) { RefreshShop(); return; }
            Wallet.UnlockSkin(sk.Skin);
        }
        Wallet.Skin = sk.Skin;
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
        for (int i = 0; i < Shop.Skins.Length; i++)
        {
            var sk = Shop.Skins[i];
            bool owned = Wallet.OwnsSkin(sk.Skin);
            bool on = Wallet.Skin == sk.Skin;
            bool afford = Wallet.Coins >= sk.Price;
            skinBuyLabel[i].text = on ? "EQUIPPED" : owned ? "EQUIP" : sk.Price.ToString();
            skinBuyFill[i].color = on ? Yellow : (owned || afford) ? Teal : new Color(0.85f, 0.86f, 0.88f);
            skinBuyLabel[i].color = (on || owned || afford) ? Ink : Muted;
        }
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

        // 국기가 있는 나라를 전부 담는다 — 목록이 길어 스크롤로 넘긴다
        var viewport = NewImage("cview", card.transform, new Color(0, 0, 0, 0));
        Place(viewport.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
              new Vector2(0, 20), new Vector2(880, 1040));
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = NewRT("ccontent", viewport.transform);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport.rectTransform;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 40f;

        var codes = CountryFlags.Codes;
        const int cols = 5;
        const float cellW = 168f, cellH = 132f;
        for (int i = 0; i < codes.Length; i++)
        {
            string code = codes[i];
            int cx = i % cols, cy = i / cols;
            var b = NewPlainButton("c" + code, content, new Color(1, 1, 1, 0.06f),
                () => { PlayerAccount.Country = code; countryPanel.SetActive(false); });
            var brt = (RectTransform)b.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0, 1);
            brt.pivot = new Vector2(0.5f, 1);
            brt.sizeDelta = new Vector2(cellW - 14, cellH - 14);
            brt.anchoredPosition = new Vector2(22 + cellW * (cx + 0.5f), -cy * cellH - 8);

            var fl = NewImage("f", b.transform, Color.white);
            fl.sprite = CountryFlags.Get(code);
            fl.preserveAspect = true;
            fl.raycastTarget = false;
            Place(fl.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                  new Vector2(0, -10), new Vector2(120, 74));
            var lt = NewText("l", b.transform, code, 30, TextAnchor.MiddleCenter, Color.white);
            Place(lt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                  new Vector2(0, 12), new Vector2(140, 34));
        }
        content.sizeDelta = new Vector2(0, Mathf.CeilToInt(codes.Length / (float)cols) * cellH + 20);

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

