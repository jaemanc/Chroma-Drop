// GameUI.cs — 런타임 생성 uGUI (씬/프리팹/폰트 에셋 의존 없음).
// 홈 / 게임 HUD / 결과 3개 패널을 코드로 조립. 노치 대응(SafeArea) 포함.
// 한글 표시는 OS 폰트(iOS: Apple SD Gothic Neo, Android: Noto Sans CJK 등)를 동적 로드,
// 없으면 내장 폰트 + 영문 라벨로 폴백.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ColorMatcher.Core;

public class GameUI : MonoBehaviour
{
    static readonly Color Accent = new Color(0.28f, 0.72f, 0.52f);
    static readonly Color BtnDim = new Color(1, 1, 1, 0.12f);
    static readonly Color BtnSel = new Color(0.28f, 0.72f, 0.52f, 0.85f);

    GameManager gm;
    Font font;
    bool korean;

    GameObject homePanel, gamePanel, resultPanel;
    Text scoreText, subText, rightText, bestHomeText, resultTitle, resultScore, resultBest;
    Image timerFill;
    Image[] modeBtnBgs; Image[] diffBtnBgs;
    RectTransform nextRoot;
    readonly List<Image> nextCells = new List<Image>();

    static readonly string[] DiffKeys = { "easy", "normal", "hard" };

    public static GameUI Create(GameManager gm)
    {
        var go = new GameObject("GameUI");
        var ui = go.AddComponent<GameUI>();
        ui.gm = gm;
        ui.Build();
        return ui;
    }

    string L(string ko, string en) { return korean ? ko : en; }

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
                if (font != null) { korean = true; break; }
            }
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            korean = false;
        }
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

        BuildGamePanel();
        BuildHomePanel();
        BuildResultPanel();
        homePanel.SetActive(false);
        gamePanel.SetActive(false);
        resultPanel.SetActive(false);
    }

    // ---------- 패널 전환 ----------

    public void ShowHome()
    {
        homePanel.SetActive(true);
        gamePanel.SetActive(false);
        resultPanel.SetActive(false);
        RefreshHomeButtons();
    }

    public void ShowGame()
    {
        homePanel.SetActive(false);
        gamePanel.SetActive(true);
        resultPanel.SetActive(false);
    }

    public void ShowResult(bool win, bool ta, int score, int best, bool newBest)
    {
        resultPanel.SetActive(true);
        resultTitle.text = ta ? L("타임어택 종료!", "TIME'S UP!")
                              : (win ? L("성공!", "CLEAR!") : L("실패...", "FAILED"));
        resultTitle.color = ta || win ? Accent : new Color(0.9f, 0.45f, 0.4f);
        resultScore.text = score.ToString("N0") + (newBest ? "  ★" + L("신기록!", "NEW BEST!") : "");
        resultBest.text = L("최고 기록 ", "BEST ") + best.ToString("N0");
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
        timerFill = NewImage("fill", barBg.transform, Accent);
        Stretch(timerFill.rectTransform);
        timerFill.rectTransform.pivot = new Vector2(0, 0.5f);

        var rot = NewButton("rotate", safe, L("회전", "ROTATE"), 54, BtnDim, () => gm.RotateCurrent());
        Place((RectTransform)rot.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 44), new Vector2(280, 150));

        var home = NewButton("home", safe, L("홈", "HOME"), 40, BtnDim, () => gm.GoHome());
        Place((RectTransform)home.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(36, 44), new Vector2(170, 110));
    }

    /// <summary>매 프레임 HUD 갱신 (Playing 중 GameManager가 호출)</summary>
    public void UpdateHud(GameManager g)
    {
        scoreText.text = g.Score.ToString("N0");
        float frac;
        if (g.TimeAttackMode)
        {
            subText.text = L("타임어택", "TIME ATTACK");
            rightText.text = g.TimeLeftSec.ToString("0.0") + L("초", "s");
            frac = g.TimeLeftSec / (Rules.TimeAttackMs / 1000f);
        }
        else
        {
            subText.text = L("목표 ", "GOAL ") + g.Goal.ToString("N0");
            rightText.text = L("남은 수 ", "MOVES ") + g.MovesLeft;
            frac = g.PieceTimerFrac;
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
        homePanel = NewImage("homebg", transform, new Color(0.07f, 0.07f, 0.11f, 1)).gameObject;
        Stretch((RectTransform)homePanel.transform);
        var safe = NewRT("safe", homePanel.transform);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        var title = NewText("title", safe, "CHROMA DROP", 104, TextAnchor.MiddleCenter, Color.white);
        Place(title.rectTransform, new Vector2(0.5f, 0.80f), new Vector2(0.5f, 0.80f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 130));

        var sub = NewText("subtitle", safe, L("칼라 매쳐", "COLOR MATCHER"), 46, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f));
        Place(sub.rectTransform, new Vector2(0.5f, 0.74f), new Vector2(0.5f, 0.74f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 70));

        modeBtnBgs = new Image[2];
        string[] modeLabels = { L("점수 모드", "SCORE"), L("타임어택", "TIME ATTACK") };
        for (int i = 0; i < 2; i++)
        {
            bool ta = i == 1;
            var b = NewButton("mode" + i, safe, modeLabels[i], 44, BtnDim, () => { gm.timeAttack = ta; RefreshHomeButtons(); });
            Place((RectTransform)b.transform, new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -190 : 190, 0), new Vector2(350, 110));
            modeBtnBgs[i] = b.GetComponent<Image>();
        }

        diffBtnBgs = new Image[3];
        string[] diffLabels = { L("하", "EASY"), L("중", "NORMAL"), L("상", "HARD") };
        for (int i = 0; i < 3; i++)
        {
            string key = DiffKeys[i];
            var b = NewButton("diff" + i, safe, diffLabels[i], 44, BtnDim, () => { gm.difficulty = key; RefreshHomeButtons(); });
            Place((RectTransform)b.transform, new Vector2(0.5f, 0.49f), new Vector2(0.5f, 0.49f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 250, 0), new Vector2(230, 100));
            diffBtnBgs[i] = b.GetComponent<Image>();
        }

        bestHomeText = NewText("best", safe, "", 42, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.65f));
        Place(bestHomeText.rectTransform, new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 60));

        var start = NewButton("start", safe, L("시작", "START"), 62, Accent, () => gm.StartGame());
        Place((RectTransform)start.transform, new Vector2(0.5f, 0.29f), new Vector2(0.5f, 0.29f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 150));
    }

    void RefreshHomeButtons()
    {
        if (modeBtnBgs == null) return;
        for (int i = 0; i < 2; i++)
            modeBtnBgs[i].color = (i == 1) == gm.timeAttack ? BtnSel : BtnDim;
        for (int i = 0; i < 3; i++)
            diffBtnBgs[i].color = DiffKeys[i] == gm.difficulty ? BtnSel : BtnDim;
        bestHomeText.text = L("최고 기록 ", "BEST ") + gm.BestForSelection().ToString("N0");
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

        var retry = NewButton("retry", card.transform, L("다시하기", "RETRY"), 50, Accent, () => gm.StartGame());
        Place((RectTransform)retry.transform, new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.5f), new Vector2(-185, 0), new Vector2(340, 130));

        var homeB = NewButton("rhome", card.transform, L("홈", "HOME"), 50, BtnDim, () => gm.GoHome());
        Place((RectTransform)homeB.transform, new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.5f), new Vector2(185, 0), new Vector2(340, 130));
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

    Button NewButton(string name, Transform parent, string label, int fontSize, Color bg, UnityAction onClick)
    {
        var img = NewImage(name, parent, bg);
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        var t = NewText("label", img.transform, label, fontSize, TextAnchor.MiddleCenter, Color.white);
        Stretch(t.rectTransform);
        return b;
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
