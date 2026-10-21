// Ads.cs — 보상형 광고 한 곳. 지금은 SDK 가 없어 자리만 잡아 둔다.
//
// 광고 ID 를 바꾸는 곳은 이 파일 하나다. 구글 AdMob SDK(GoogleMobileAds)를 넣은 뒤
// 아래 ChromaDrop_AdMob 심볼을 Player Settings > Scripting Define Symbols 에 추가하면
// 실제 광고가 나가고, 없으면 GameUI 의 가짜 광고 패널로 돌아간다.
//
// 넣는 순서:
//   1) Google Mobile Ads Unity 플러그인 임포트
//   2) 아래 상수를 내 계정의 실제 ID 로 교체 (지금 값은 구글이 공개한 테스트 ID 다)
//   3) Define Symbols 에 ChromaDrop_AdMob 추가

using System;
using UnityEngine;

public static class Ads
{
    // ⚠ 구글 공식 테스트 ID. 실제 수익이 나지 않고, 실제 ID 대신 쓰면 계정이 안전하다.
    public const string AndroidAppId = "ca-app-pub-3940256099942544~3347511713";
    public const string IosAppId     = "ca-app-pub-3940256099942544~1458002511";
    public const string AndroidRewardedId = "ca-app-pub-3940256099942544/5224354917";
    public const string IosRewardedId     = "ca-app-pub-3940256099942544/1712485313";

    /// <summary>이 기기에서 쓸 보상형 광고 단위 ID.</summary>
    public static string RewardedId
    {
        get
        {
#if UNITY_IOS
            return IosRewardedId;
#else
            return AndroidRewardedId;
#endif
        }
    }

    /// <summary>SDK 가 붙어 있는가. false 면 호출부가 가짜 광고로 대신한다.</summary>
    public static bool Ready
    {
#if ChromaDrop_AdMob
        get { return !TestServices.Enabled; }
#else
        get { return false; }
#endif
    }

    /// <summary>보상형 광고를 띄운다. done(true) 면 보상을 줘도 되는 상태로 끝난 것이다.
    /// SDK 가 없으면 아무것도 하지 않고 done(false) — 호출부가 가짜 광고로 메운다.</summary>
    public static void ShowRewarded(Action<bool> done)
    {
#if ChromaDrop_AdMob
        // TODO: GoogleMobileAds 로 RewardedAd.Load(RewardedId, ...) 후 Show.
        //       보상 콜백에서 done(true), 실패·취소면 done(false).
        Debug.LogWarning("[ads] ChromaDrop_AdMob 가 켜져 있지만 SDK 연결 코드가 비어 있다.");
        if (done != null) done(false);
#else
        if (done != null) done(false);
#endif
    }
}
