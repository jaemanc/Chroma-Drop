using System;
using NUnit.Framework;

public class AdsTests
{
    static AdMobConfig Example()
    {
        return new AdMobConfig {
            iosAppId = "ca-app-pub-0000000000000000~0000000000",
            scoreSubmitAdUnitId = "ca-app-pub-0000000000000000/0000000001",
            coinRewardAdUnitId = "ca-app-pub-0000000000000000/0000000002"
        };
    }
    [Test]
    public void PlacementsStaySeparateAndDevelopmentUsesTestAds()
    {
        var config = Example();
        Assert.That(config.Valid, Is.True);
        Assert.That(config.UnitId(AdPlacement.ScoreSubmit, false), Is.EqualTo(AdMobConfig.IosTestRewardedId));
        config.useTestAds = false;
        Assert.That(config.UnitId(AdPlacement.ScoreSubmit, false), Is.EqualTo(config.scoreSubmitAdUnitId));
        Assert.That(config.UnitId(AdPlacement.CoinReward, false), Is.EqualTo(config.coinRewardAdUnitId));
        Assert.That(config.UnitId(AdPlacement.CoinReward, true), Is.EqualTo(AdMobConfig.IosTestRewardedId));
        config.coinRewardAdUnitId = config.scoreSubmitAdUnitId;
        Assert.That(config.Valid, Is.False);
        Assert.That(new AdMobConfig().Valid, Is.False);
    }
    [TestCase(true)]
    [TestCase(false)]
    public void RewardAndCloseCallbacksCompleteOnlyOnce(bool earned)
    {
        int callbacks = 0, grants = 0;
        var completion = new AdRewardCompletion(ok => { callbacks++; if (ok) grants++; });
        completion.Complete(earned);
        completion.Complete(true);
        completion.Complete(false);
        Assert.That(callbacks, Is.EqualTo(1));
        Assert.That(grants, Is.EqualTo(earned ? 1 : 0));
    }
#if UNITY_EDITOR
    [Test]
    public void EnvParserReadsOnlyAdSettingsAndRejectsMalformedValues()
    {
        var type = Type.GetType("AdMobEnvImport, ChromaDrop.Editor");
        var read = type.GetMethod("ReadEnv");
        var config = (AdMobConfig)read.Invoke(null, new object[] { new[] {
            "export iosAppId='ca-app-pub-0000000000000000~0000000000'",
            "scoreSubmitAdUnitId=ca-app-pub-0000000000000000/0000000001 # 점수",
            "coinRewardAdUnitId=\"ca-app-pub-0000000000000000/0000000002\"",
            "OTHER_SECRET=must_not_be_included"
        }});
        Assert.That(config.Valid, Is.True);
        Assert.That(config.useTestAds, Is.True);
        config = (AdMobConfig)read.Invoke(null, new object[] { new[] { "useTestAds=false" } });
        Assert.That(config.useTestAds, Is.False);
        Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            read.Invoke(null, new object[] { new[] { "useTestAds=invalid" } }));
        config = (AdMobConfig)read.Invoke(null, new object[] { new[] { "iosAppId=invalid" } });
        Assert.That(config.Valid, Is.False);
    }
#endif
}
