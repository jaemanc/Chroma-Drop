using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ColorMatcher.Core;

public class MainMenu : MonoBehaviour
{
    public const string GameScene = "Game";
    public const int AdCoins = 100;
    Text coins, hearts, refill, heartInfo;
    RectTransform art;
    Text[] homeLabels;
    int[] homeFontSizes;
    float lastTextHeight = -1;
    GameObject modal;
    bool launching, rewardBusy;
    int rewardGeneration, lastSecond = -1;

    void Awake()
    {
        AudioListener.volume = PlayerPrefs.GetInt("sound_muted", 0) == 1 ? 0 : 1;
        if (UiTypography.Assets == null || UiTypography.Assets.homeMenu == null)
        {
            foreach (var b in GetComponentsInChildren<Button>(true))
            {
                if (b.name == "Btn_Play" || b.name == "Btn_Classic") b.onClick.AddListener(() => Launch(false));
                if (b.name == "Btn_Rush") b.onClick.AddListener(() => Launch(true));
            }
            return;
        }
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = .5f;
        if (Camera.main != null) Camera.main.backgroundColor = new Color(.04f, .22f, .70f);
        // 정지 화면용 HomePreview도 숨긴 뒤 실제 잔액과 버튼을 연결한 화면을 만든다.
        for (int i = 0; i < canvas.transform.childCount; i++) canvas.transform.GetChild(i).gameObject.SetActive(false);
        art = CreateHomeArt(canvas.transform);
        coins = Label(art, "CoinBalance", "", .145f, .033f, .165f, .035f, 34, UiFontRole.Numbers);
        hearts = Label(art, "HeartBalance", "", .587f, .034f, .065f, .032f, 34, UiFontRole.Numbers);
        refill = Label(art, "HeartRefill", "", .667f, .036f, .111f, .029f, 28, UiFontRole.Numbers);
        Hit("Btn_Coins", .30f, .025f, .09f, .05f, ShowCoins);
        Hit("Btn_Hearts", .782f, .025f, .08f, .05f, ShowHearts);
        Hit("Btn_Settings", .876f, .022f, .094f, .054f, ShowSettings);
        Hit("Btn_Play", .205f, .635f, .605f, .107f, () => Launch(false));
        var play = Label(art, "PlayLabel", "PLAY", .413f, .660f, .338f, .051f, 66, UiFontRole.Menu);
        Outline(play, new Color(0.77f, 0.32f, 0.03f), .8f);
        Hit("Btn_Ranking", .11f, .758f, .39f, .076f, () => OpenPanel(1));
        var rank = Label(art, "RankLabel", TestServices.DevelopmentToolsEnabled ? "RANKING" : "RECORDS", .230f, .779f, .193f, .034f, 30, UiFontRole.Menu);
        Outline(rank, new Color(.23f, .08f, .52f), .6f);
        Hit("Btn_Shop", .51f, .758f, .39f, .076f, () => OpenPanel(2));
        var shop = Label(art, "ShopLabel", "SHOP", .655f, .779f, .164f, .034f, 32, UiFontRole.Menu);
        Outline(shop, new Color(0, .38f, .29f), .6f);
        Hit("Btn_AdReward", .17f, .857f, .646f, .09f, () => Reward(false));
        var ad = Label(art, "AdLabel", "WATCH AD", .473f, .873f, .24f, .025f, 28, UiFontRole.Menu);
        Outline(ad, new Color(.14f, .05f, .48f), .6f);
        var amount = Label(art, "AdAmount", "+100", .487f, .901f, .15f, .03f, 34, UiFontRole.Numbers);
        amount.color = new Color(1f, .86f, .22f);
        homeLabels = new[] { coins, hearts, refill, play, rank, shop, ad, amount };
        homeFontSizes = new int[homeLabels.Length];
        for (int i = 0; i < homeLabels.Length; i++)
        {
            homeFontSizes[i] = homeLabels[i].fontSize;
            homeLabels[i].horizontalOverflow = HorizontalWrapMode.Wrap;
            homeLabels[i].verticalOverflow = VerticalWrapMode.Truncate;
            homeLabels[i].resizeTextForBestFit = true;
        }
        UpdateResources();
        if (!string.IsNullOrEmpty(Hearts.Notice))
        {
            string notice = Hearts.Notice; Hearts.Notice = null;
            Popup("HEARTS", notice);
        }
    }

    // 배경은 화면 끝까지 채우고, 메뉴와 클릭 영역은 안전 영역 안에 온전히 표시한다.
    public static RectTransform CreateHomeArt(Transform parent)
    {
        var root = Box("HomeRoot", parent, 0, 0, 1, 1);
        var scenery = UiTypography.Assets.homeBackground;
        if (scenery != null)
        {
            var backdrop = Box("HomeBackground", root, 0, 0, 1, 1);
            var fill = backdrop.gameObject.AddComponent<AspectRatioFitter>();
            fill.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fill.aspectRatio = scenery.rect.width / scenery.rect.height;
            var background = backdrop.gameObject.AddComponent<Image>();
            background.sprite = scenery; background.raycastTarget = false;
        }
        var safe = new GameObject("HomeSafe", typeof(RectTransform), typeof(SafeAreaFitter)).GetComponent<RectTransform>();
        safe.SetParent(root, false); Stretch(safe);
        var fit = new GameObject("HomeArt", typeof(RectTransform), typeof(AspectRatioFitter), typeof(Image)).GetComponent<RectTransform>();
        fit.SetParent(safe, false);
        fit.anchorMin = fit.anchorMax = fit.pivot = new Vector2(.5f, .5f);
        var aspect = fit.GetComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        var sprite = UiTypography.Assets.homeMenu;
        aspect.aspectRatio = sprite.rect.width / sprite.rect.height;
        var image = fit.GetComponent<Image>(); image.sprite = sprite; image.raycastTarget = false;
        if (scenery != null) fit.gameObject.AddComponent<HomeArtEdgeBlend>();
        return fit;
    }

    void Update()
    {
        int second = Mathf.FloorToInt(Time.unscaledTime);
        if (second == lastSecond || coins == null) return;
        lastSecond = second; UpdateResources();
    }

    void LateUpdate()
    {
        if (art == null || homeLabels == null || art.rect.height <= 0 || art.rect.height == lastTextHeight) return;
        lastTextHeight = art.rect.height;
        // 버튼 그림과 같은 비율로 글자를 줄여 넓은 화면에서도 캔버스 배율만 커지지 않게 한다.
        float scale = art.rect.height / 1920f;
        for (int i = 0; i < homeLabels.Length; i++)
        {
            int size = Mathf.Max(8, Mathf.RoundToInt(homeFontSizes[i] * scale));
            homeLabels[i].resizeTextMinSize = Mathf.Max(6, Mathf.RoundToInt(size * .65f));
            homeLabels[i].resizeTextMaxSize = size;
            homeLabels[i].fontSize = size;
            var outline = homeLabels[i].GetComponent<Outline>();
            if (outline != null) outline.effectDistance = new Vector2(.6f * scale, -.6f * scale);
        }
    }

    void UpdateResources()
    {
        coins.text = Wallet.Coins.ToString("N0");
        int count = Hearts.Count, seconds = Hearts.SecondsToNext;
        hearts.text = count.ToString();
        refill.text = count == HeartClock.Capacity ? "FULL" : seconds / 60 + ":" + (seconds % 60).ToString("00");
        if (heartInfo != null) heartInfo.text = count + " / " + HeartClock.Capacity + " HEARTS\n" +
            (count == HeartClock.Capacity ? "Your hearts are full!" : "Next heart in " + refill.text) + "\n+1 every 10 minutes · 1 per game";
    }

    void Launch(bool timeAttack)
    {
        if (launching || rewardBusy) return;
        if (Hearts.Count == 0) { ShowHearts(); return; }
        launching = true;
        GameManager.LaunchHomePanel = 0;
        GameManager.LaunchFeverTest = false;
        GameManager.LaunchTimeAttack = timeAttack;
        SceneManager.LoadScene(GameScene);
    }

    void OpenPanel(int panel)
    {
        if (launching || rewardBusy) return;
        launching = true;
        GameManager.LaunchHomePanel = panel;
        GameManager.LaunchTimeAttack = null;
        SceneManager.LoadScene(GameScene);
    }

    void Hit(string name, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
    {
        var rt = Box(name, art, x, y, w, h);
        var image = rt.gameObject.AddComponent<Image>(); image.sprite = UiTheme.RoundedSprite(32); image.type = Image.Type.Sliced;
        var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = Color.clear; colors.highlightedColor = new Color(1, 1, 1, .035f);
        colors.pressedColor = new Color(1, 1, 1, .16f); colors.disabledColor = Color.clear;
        colors.fadeDuration = .08f; button.colors = colors;
        button.onClick.AddListener(action);
    }

    void CloseModal()
    {
        rewardGeneration++; rewardBusy = false;
        if (modal != null) Destroy(modal);
        modal = null; heartInfo = null;
    }

    RectTransform Popup(string title, string message, Sprite rewardArt = null)
    {
        CloseModal();
        var canvas = GetComponentInParent<Canvas>();
        modal = new GameObject("HomePopup", typeof(RectTransform), typeof(Image));
        var root = (RectTransform)modal.transform; root.SetParent(canvas.transform, false); Stretch(root);
        modal.GetComponent<Image>().color = new Color(.025f, .06f, .19f, .78f);
        var safe = new GameObject("Safe", typeof(RectTransform), typeof(SafeAreaFitter)).GetComponent<RectTransform>();
        safe.SetParent(root, false); Stretch(safe);
        bool reward = rewardArt != null;
        var bounds = reward ? Box("RewardBounds", safe, .04f, .10f, .92f, .80f) : safe;
        var card = reward ? Box("card", bounds, 0, 0, 1, 1) : Box("card", safe, .10f, .30f, .80f, .40f);
        if (reward)
        {
            var fit = card.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = rewardArt.rect.width / rewardArt.rect.height;
        }
        var bg = card.gameObject.AddComponent<Image>();
        bg.sprite = reward ? rewardArt : UiTypography.Assets.pausePanel;
        bg.type = reward ? Image.Type.Simple : Image.Type.Sliced;
        var heading = Label(card, "title", title, reward ? .17f : .08f, reward ? .18f : .12f, reward ? .66f : .84f, .12f, reward ? 66 : 48, UiFontRole.Title);
        if (reward) Outline(heading, new Color(.04f, .16f, .42f), 2);
        var body = Label(card, "body", message, .09f, reward ? .71f : .30f, .82f, reward ? .06f : .33f, reward ? 28 : 30, UiFontRole.Body);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        var close = PopupButton(card, "Close", "CLOSE", reward ? .24f : .20f, reward ? .795f : .78f, reward ? .52f : .60f, .11f, CloseModal);
        if (reward)
        {
            body.color = new Color(.40f, .23f, .13f);
            var amount = Label(card, "RewardAmount", "+" + AdCoins + " COINS", .15f, .625f, .70f, .09f, 72, UiFontRole.Title);
            amount.color = new Color(1, .81f, .20f); Outline(amount, new Color(.36f, .12f, .025f), 3);
            close.GetComponent<Image>().color = Color.clear;
            var text = close.GetComponentInChildren<Text>(); text.fontSize = 56;
            text.color = new Color(.36f, .16f, .035f); Outline(text, Color.white, 2);
        }
        return card;
    }

    Button PopupButton(RectTransform card, string name, string caption, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
    {
        var rt = Box(name, card, x, y, w, h);
        var image = rt.gameObject.AddComponent<Image>(); image.sprite = UiTypography.Assets.primaryButton; image.type = Image.Type.Sliced;
        var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image; if (action != null) button.onClick.AddListener(action);
        var text = Label(rt, "label", caption, .03f, .05f, .94f, .90f, 28, UiFontRole.Menu);
        text.color = new Color(.08f, .10f, .25f);
        return button;
    }

    void ShowHearts()
    {
        var card = Popup("HEARTS", "");
        heartInfo = card.Find("body").GetComponent<Text>();
        if (Hearts.Count < HeartClock.Capacity)
            PopupButton(card, "WatchHeartAd", "WATCH AD  ·  +1 HEART", .12f, .64f, .76f, .10f, () => Reward(true));
        UpdateResources();
    }

    void ShowCoins()
    {
        var card = Popup("COINS", "Earn coins by playing or watching an ad.");
        PopupButton(card, "WatchCoinAd", "WATCH AD  ·  +100 COINS", .12f, .64f, .76f, .10f, () => Reward(false));
    }

    void ShowSettings()
    {
        var card = Popup("SETTINGS", "");
        bool privacy = Ads.PrivacyOptionsRequired;
        if (privacy || AppPrivacy.HasPolicyUrl)
        {
            card.anchorMin = new Vector2(.10f, .24f); card.anchorMax = new Vector2(.90f, .76f);
            var close = (RectTransform)card.Find("Close");
            close.anchorMin = new Vector2(.20f, .05f); close.anchorMax = new Vector2(.80f, .16f);
        }
        if (privacy) PopupButton(card, "Privacy", "AD PRIVACY", .12f, .57f, .76f, .10f, () => Ads.ShowPrivacyOptions(CloseModal));
        if (AppPrivacy.HasPolicyUrl) PopupButton(card, "PrivacyPolicy", "PRIVACY POLICY", .12f, .18f, .76f, .10f, AppPrivacy.OpenPolicy);
        var sound = PopupButton(card, "Sound", AudioListener.volume > 0 ? "SOUND ON" : "SOUND OFF", .12f, .31f, .76f, .10f, null);
        sound.onClick.AddListener(() => {
            bool muted = AudioListener.volume > 0;
            AudioListener.volume = muted ? 0 : 1;
            PlayerPrefs.SetInt("sound_muted", muted ? 1 : 0); PlayerPrefs.Save();
            sound.GetComponentInChildren<Text>().text = muted ? "SOUND OFF" : "SOUND ON";
        });
        PopupButton(card, "Btn_Rush", "TIME RUSH", .12f, privacy ? .44f : .48f, .76f, .10f, () => Launch(true));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        PopupButton(card, "Btn_FeverTest", "FEVER PREVIEW", .12f, privacy ? .70f : .63f, .76f, .10f, () => {
            if (launching) return; launching = true;
            GameManager.LaunchHomePanel = 0; GameManager.LaunchFeverTest = true; GameManager.LaunchTimeAttack = false;
            SceneManager.LoadScene(GameScene);
        });
#endif
    }

    void Reward(bool heart)
    {
        if (rewardBusy) return;
        if (heart && Hearts.Count == HeartClock.Capacity) { ShowHearts(); return; }
        var card = Popup("BONUS REWARD", "", heart ? null : UiTypography.Assets.bonusReward);
        var info = card.Find("body").GetComponent<Text>();
        rewardBusy = true;
        int generation = rewardGeneration;
        if (Ads.UseOfflineTest) StartCoroutine(TestAd(heart, info, generation));
        else
        {
            bool completed = false;
            Ads.ShowRewarded(AdPlacement.CoinReward, ok => {
                if (completed) return; completed = true;
                if (ok) Grant(heart);
                rewardBusy = false;
                if (info != null) info.text = ok ? "Reward received!" : Ads.Message;
            });
        }
    }

    IEnumerator TestAd(bool heart, Text info, int generation)
    {
        for (int i = 3; i > 0; i--)
        {
            if (generation != rewardGeneration || info == null) yield break;
            info.text = !heart && UiTypography.Assets.bonusReward != null ? "TEST AD · " + i
                : "TEST AD · " + i + "\nWatch to receive " + (heart ? "1 heart" : AdCoins + " coins");
            yield return new WaitForSecondsRealtime(1);
        }
        if (generation != rewardGeneration || info == null) yield break;
        Grant(heart); rewardBusy = false;
        info.text = heart ? "+1 HEART\nReady for another round!" : "Reward received!";
    }

    void Grant(bool heart)
    {
        if (heart) Hearts.Add(1); else Wallet.AddCoins(AdCoins);
        UpdateResources();
    }

    static RectTransform Box(string name, Transform parent, float x, float y, float w, float h)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x, 1 - y - h); rt.anchorMax = new Vector2(x + w, 1 - y);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Text Label(Transform parent, string name, string value, float x, float y, float w, float h, int size, UiFontRole role)
    {
        var rt = Box(name, parent, x, y, w, h);
        var t = rt.gameObject.AddComponent<Text>(); UiTypography.Apply(t, role);
        t.text = value; t.fontSize = size; t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void Outline(Text label, Color color, float size)
    {
        var outline = label.gameObject.AddComponent<Outline>(); outline.effectColor = color; outline.effectDistance = new Vector2(size, -size);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
