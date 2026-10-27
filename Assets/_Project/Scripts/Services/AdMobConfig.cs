using System;
using System.Text.RegularExpressions;

[Serializable]
public class AdMobConfig
{
    public string iosAppId, scoreSubmitAdUnitId, coinRewardAdUnitId;
    public bool useTestAds = true;
    public const string IosTestRewardedId = "ca-app-pub-3940256099942544/1712485313";
    public bool Valid
    {
        get { return Match(iosAppId, '~') && Match(scoreSubmitAdUnitId, '/') && Match(coinRewardAdUnitId, '/') && scoreSubmitAdUnitId != coinRewardAdUnitId; }
    }
    static bool Match(string value, char separator)
    {
        return value != null && Regex.IsMatch(value, "^ca-app-pub-[0-9]{16}" + separator + "[0-9]{10}$");
    }
    public string UnitId(AdPlacement placement, bool development)
    {
        return useTestAds || development ? IosTestRewardedId : placement == AdPlacement.ScoreSubmit ? scoreSubmitAdUnitId : coinRewardAdUnitId;
    }
}

// SDK가 보상·닫기·오류 콜백을 중복 전달해도 지급은 한 번만 처리한다.
public sealed class AdRewardCompletion
{
    readonly Action<bool> done;
    bool completed;
    public AdRewardCompletion(Action<bool> done) { this.done = done; }
    public void Complete(bool earned)
    {
        if (completed) return;
        completed = true;
        if (done != null) done(earned);
    }
}
