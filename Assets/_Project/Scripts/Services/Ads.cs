using System;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

public enum AdPlacement { ScoreSubmit, CoinReward }

public static class Ads
{
    sealed class Slot { public RewardedAd Ad; public bool Loading; }
    static AdMobConfig config;
    static Slot[] slots = { new Slot(), new Slot() };
    static bool starting, initialized, showing;
    public static string Message { get; private set; }
    public static bool Ready { get { return initialized; } }
    public static bool UseOfflineTest { get { return !UsesSdk && TestServices.Enabled; } }
    public static bool UsesSdk
    {
        get { return !Application.isBatchMode && Config != null && Config.Valid && (Application.isEditor || Application.platform == RuntimePlatform.IPhonePlayer); }
    }
    static AdMobConfig Config
    {
        get
        {
            if (config == null)
            {
                var file = Resources.Load<TextAsset>("admob-config");
                if (file != null) config = JsonUtility.FromJson<AdMobConfig>(file.text);
            }
            return config;
        }
    }
    public static bool PrivacyOptionsRequired
    {
        get { return UsesSdk && ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required; }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        foreach (var slot in slots) if (slot.Ad != null) slot.Ad.Destroy();
        slots = new[] { new Slot(), new Slot() };
        config = null; starting = initialized = showing = false; Message = null;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Initialize()
    {
        if (!UsesSdk || starting || initialized) return;
        starting = true; Message = "광고 준비 중입니다.";
        if (Application.isEditor) { Connect(); return; }
        ConsentInformation.Update(new ConsentRequestParameters(), error => Main(() =>
        {
            ConsentForm.LoadAndShowConsentFormIfRequired(formError => Main(() =>
            {
                if (ConsentInformation.CanRequestAds()) Connect();
                else { starting = false; Message = "개인정보 동의 상태를 확인해 주세요."; }
            }));
        }));
    }
    static void Connect()
    {
        MobileAds.Initialize(status => Main(() =>
        {
            initialized = status != null; starting = false;
            Message = initialized ? "광고를 불러오는 중입니다." : "광고 초기화에 실패했습니다.";
            if (initialized) { Load(AdPlacement.ScoreSubmit); Load(AdPlacement.CoinReward); }
        }));
    }
    static void Main(Action action) { MobileAdsEventExecutor.ExecuteInUpdate(action); }
    public static string UnitId(AdPlacement placement)
    {
        return Config == null ? null : Config.UnitId(placement, Application.isEditor || Debug.isDebugBuild);
    }
    static void Load(AdPlacement placement)
    {
        var slot = slots[(int)placement];
        if (!initialized || slot.Loading || slot.Ad != null) return;
        slot.Loading = true;
        RewardedAd.Load(UnitId(placement), new AdRequest(), (ad, error) => Main(() =>
        {
            slot.Loading = false;
            if (error != null || ad == null)
            {
                if (ad != null) ad.Destroy();
                Message = "광고를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.";
                return;
            }
            slot.Ad = ad; Message = "광고 준비 완료";
        }));
    }
    public static void ShowRewarded(AdPlacement placement, Action<bool> done)
    {
        Initialize();
        if (!UsesSdk || !initialized || showing)
        {
            Message = showing ? "광고가 이미 진행 중입니다." : "광고 준비 중입니다. 잠시 후 다시 시도해 주세요.";
            if (done != null) done(false);
            return;
        }
        var slot = slots[(int)placement];
        if (slot.Ad == null || !slot.Ad.CanShowAd())
        {
            if (slot.Ad != null) { slot.Ad.Destroy(); slot.Ad = null; }
            Load(placement); Message = "광고 준비 중입니다. 잠시 후 다시 시도해 주세요.";
            if (done != null) done(false);
            return;
        }
        var ad = slot.Ad; slot.Ad = null; showing = true;
        var completion = new AdRewardCompletion(done);
        var game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        bool resume = game != null && game.Phase == GamePhase.Playing && !game.Paused;
        if (resume) game.PauseGame();
        bool closed = false;
        Action finish = () =>
        {
            if (closed) return;
            closed = true; showing = false;
            try
            {
                if (resume && game != null) game.ResumeGame();
                completion.Complete(false);
            }
            finally { ad.Destroy(); Load(placement); }
        };
        ad.OnAdFullScreenContentClosed += () => Main(finish);
        ad.OnAdFullScreenContentFailed += error => Main(finish);
        try { ad.Show(reward => Main(() => completion.Complete(true))); }
        catch (Exception) { finish(); }
    }
    public static void ShowPrivacyOptions(Action done)
    {
        ConsentForm.ShowPrivacyOptionsForm(error => Main(() => { if (done != null) done(); }));
    }
}
