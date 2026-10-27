using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class TestServicesTests
{
    string wallet;
    string ranking;

    [SetUp]
    public void SaveTestData()
    {
        // 지갑 초기화를 먼저 끝내고 사용자 테스트 기록은 검사 후 복구한다.
        var balance = Wallet.Coins;
        wallet = PlayerPrefs.GetString(TestPayments.StorageKey, "");
        ranking = PlayerPrefs.GetString("dummy_rank_ta", "");
        PlayerPrefs.DeleteKey(TestPayments.StorageKey);
        TestPayments.Balance = 100;
        TestPayments.NextResult = TestPurchaseResult.Success;
    }

    [TearDown]
    public void RestoreTestData()
    {
        Restore(TestPayments.StorageKey, wallet);
        Restore("dummy_rank_ta", ranking);
        TestPayments.NextResult = TestPurchaseResult.Success;
        PlayerPrefs.Save();
    }

    static void Restore(string key, string data)
    {
        if (data.Length == 0) PlayerPrefs.DeleteKey(key);
        else PlayerPrefs.SetString(key, data);
    }

    [Test]
    public void PurchaseCancelFailureAndRetryKeepWalletConsistent()
    {
        bool success = false;
        Iap.Buy(Iap.Packs[0], ok => success = ok);
        Assert.That(success, Is.True);
        Assert.That(Wallet.Coins, Is.EqualTo(400));
        Assert.That(TestPayments.RecordCount, Is.EqualTo(1));
        Assert.That(TestPayments.Retry(), Is.True);
        Assert.That(Wallet.Coins, Is.EqualTo(400));
        Assert.That(TestPayments.RecordCount, Is.EqualTo(1));

        TestPayments.NextResult = TestPurchaseResult.Cancelled;
        Iap.Buy(Iap.Packs[1], ok => success = ok);
        Assert.That(success, Is.False);
        Assert.That(TestPayments.HasPending, Is.False);
        Assert.That(Wallet.Coins, Is.EqualTo(400));

        TestPayments.NextResult = TestPurchaseResult.Failed;
        Iap.Buy(Iap.Packs[1], ok => success = ok);
        Assert.That(success, Is.False);
        Assert.That(TestPayments.HasPending, Is.True);
        Assert.That(TestPayments.RecordCount, Is.EqualTo(1));
        Assert.That(Wallet.Coins, Is.EqualTo(400));
        Assert.That(TestPayments.Retry(), Is.True);
        Assert.That(Wallet.Coins, Is.EqualTo(1250));
        Assert.That(TestPayments.HasPending, Is.False);
        Assert.That(TestPayments.RecordCount, Is.EqualTo(2));
        Assert.That(Wallet.SpendCoins(60), Is.True);
        Assert.That(TestPayments.Retry(), Is.True);
        Assert.That(Wallet.Coins, Is.EqualTo(1190));
    }

    [Test]
    public void ItemAndSkinPurchasesSaveDebitAndGrantTogether()
    {
        TestPayments.Balance = 1000;
        int before = Wallet.Count(ShopItem.Hammer);
        Assert.That(Wallet.BuyItem(ShopItem.Hammer), Is.True);
        Assert.That(Wallet.Coins, Is.EqualTo(920));
        Assert.That(Wallet.Count(ShopItem.Hammer), Is.EqualTo(before + 1));
        string snapshot = PlayerPrefs.GetString(TestPayments.StorageKey);
        Assert.That(snapshot, Does.Contain("inventory"));
        Assert.That(snapshot, Does.Contain("skins"));
        Wallet.Add(ShopItem.Hammer, int.MaxValue - Wallet.Count(ShopItem.Hammer));
        Assert.That(Wallet.BuyItem(ShopItem.Hammer), Is.False);
        Assert.That(Wallet.Coins, Is.EqualTo(920));
        TestPayments.Balance = 0;
        Assert.That(Wallet.BuyItem(ShopItem.Hammer), Is.False);
        Assert.That(Wallet.Coins, Is.Zero);

        // 외부 보유량과 무관하게 새 스냅샷에서 스킨 지급을 확인한다.
        PlayerPrefs.SetString(TestPayments.StorageKey,
            "{\"coins\":1000,\"pending\":[],\"pay\":[],\"inventory\":[0,0,0,0],\"skins\":[true,false,false,false,false]}");
        Assert.That(Wallet.BuySkin(TileSkin.Gem), Is.True);
        Assert.That(Wallet.OwnsSkin(TileSkin.Gem), Is.True);
        Assert.That(Wallet.Coins, Is.EqualTo(700));
        Assert.That(Wallet.BuySkin(TileSkin.Gem), Is.True);
        Assert.That(Wallet.Coins, Is.EqualTo(700));
    }

    [Test]
    public void CorruptWalletDoesNotReportPurchaseSuccessOrOverwriteData()
    {
        string broken = "{invalid-json";
        PlayerPrefs.SetString(TestPayments.StorageKey, broken);
        bool called = false;
        Iap.Buy(Iap.Packs[0], ok => { called = true; Assert.That(ok, Is.False); });
        Assert.That(called, Is.True);
        Assert.That(TestPayments.Retry(), Is.False);
        Assert.That(PlayerPrefs.GetString(TestPayments.StorageKey), Is.EqualTo(broken));
    }

    [UnityTest]
    public IEnumerator OfflineRankingSavesOnlyBestScore()
    {
        var lb = Leaderboard.Create();
        bool success = false;
        yield return lb.Submit(true, "normal", 20000, 1, ok => success = ok);
        Assert.That(success, Is.True);
        yield return lb.Submit(true, "normal", 100, 2, null);
        yield return lb.FetchTop(true, "normal", rows => {
            Assert.That(rows, Has.Count.EqualTo(13));
            Assert.That(rows.Single(r => r.Uid == "dummy-local-player").Score, Is.EqualTo(20000));
            Assert.That(rows[0].Uid, Is.EqualTo("dummy-local-player"));
        });
        Assert.That(TestServices.Enabled, Is.True);
    }

    [UnityTest]
    public IEnumerator ShopShowsOnlyItemsAndPurchasesAtomically()
    {
        GameManager.LaunchTimeAttack = false;
        GameManager.LaunchHomePanel = 2;
        yield return SceneManager.LoadSceneAsync("Game");
        yield return null;
        yield return null;
        var ui = Object.FindAnyObjectByType<GameUI>();
        ui.ShowShop();
        ScreenCapture.CaptureScreenshot("/tmp/chromadrop-items-shop.png");
        yield return null;
        Assert.That(ui.GetComponentsInChildren<Button>(true).Any(b => b.name == "shopbuycoins"), Is.False);
        Assert.That(ui.GetComponentsInChildren<Text>(true).Any(t => t.text == "TILE SKINS"), Is.False);
        var buy = ui.GetComponentsInChildren<Button>(true)
            .Single(b => b.name == "buy" && b.transform.parent.parent.name == "item1");
        int before = Wallet.Count(ShopItem.Hammer);
        buy.onClick.Invoke();
        Assert.That(Wallet.Coins, Is.EqualTo(20));
        Assert.That(Wallet.Count(ShopItem.Hammer), Is.EqualTo(before + 1));
        buy.onClick.Invoke();
        Assert.That(Wallet.Coins, Is.EqualTo(20));
        Assert.That(Wallet.Count(ShopItem.Hammer), Is.EqualTo(before + 1));
        var card = ui.transform.Find("shopdim/shopsafe/shopcard").GetComponent<RectTransform>();
        var corners = new Vector3[4]; card.GetWorldCorners(corners);
        Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(Screen.safeArea.yMin));
        Assert.That(corners[2].y, Is.LessThanOrEqualTo(Screen.safeArea.yMax));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void NewWalletStartsAt100AndExistingBalancesArePreserved()
    {
        bool hadLegacy = PlayerPrefs.HasKey("wallet_coins");
        int legacy = PlayerPrefs.GetInt("wallet_coins", 0);
        try
        {
            PlayerPrefs.DeleteKey(TestPayments.StorageKey);
            PlayerPrefs.DeleteKey("wallet_coins");
            Assert.That(Wallet.Coins, Is.EqualTo(100));
            PlayerPrefs.SetInt("wallet_coins", 317);
            Assert.That(Wallet.Coins, Is.EqualTo(317));
            TestPayments.Balance = 79;
            PlayerPrefs.SetInt("wallet_coins", 9999);
            Assert.That(Wallet.Coins, Is.EqualTo(79));
        }
        finally
        {
            if (hadLegacy) PlayerPrefs.SetInt("wallet_coins", legacy);
            else PlayerPrefs.DeleteKey("wallet_coins");
        }
    }

    static void Click(GameUI ui, string name)
    {
        ui.GetComponentsInChildren<Button>(true).Single(b => b.name == name).onClick.Invoke();
    }

    [UnityTest]
    public IEnumerator MissionBannerUsesLiveFontsForAllSixGoals()
    {
        GameManager.LaunchTimeAttack = false;
        yield return SceneManager.LoadSceneAsync("Game");
        yield return null;
        yield return null;
        var game = Object.FindAnyObjectByType<GameManager>();
        game.enabled = false;
        var banner = Object.FindAnyObjectByType<MissionBanner>();
        Assert.That(banner, Is.Not.Null);
        Assert.That(banner.transform.Find("missionpanel").GetComponent<Graphic>(), Is.Null,
            "문구 뒤에 별도의 네모판을 그리면 안 된다.");
        Assert.That(banner.transform.Find("liveprogress").GetComponent<Graphic>(), Is.Null);
        Canvas.ForceUpdateCanvases();
        var draw = typeof(MissionBanner).GetMethod("SetRow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (var message in new[] { "Pop target blocks", "Only 123 pieces", "Clear 123 blocks", "Time attack", "Fever time 12.3", "Keep popping!" })
        {
            draw.Invoke(banner, new object[] { 0, 1, message });
            var row = banner.transform.Find("missionpanel/missionrow0").GetComponent<TMPro.TMP_Text>();
            Assert.That(row.text, Is.EqualTo(message));
            Assert.That(row.font.sourceFontFile, Is.SameAs(UiTypography.FontFor(UiFontRole.Body)));
            Assert.That(row.fontStyle, Is.EqualTo(TMPro.FontStyles.Normal));
        }
        var progress = banner.transform.Find("liveprogress/progress").GetComponent<TMPro.TMP_Text>();
        Assert.That(progress.font.sourceFontFile, Is.SameAs(UiTypography.FontFor(UiFontRole.Numbers)));
        LogAssert.NoUnexpectedReceived();
    }

    [UnityTearDown]
    public IEnumerator CleanupScene()
    {
        yield return SceneManager.LoadSceneAsync("Menu");
        GameManager.LaunchTimeAttack = null;
        yield return null;
    }
}
