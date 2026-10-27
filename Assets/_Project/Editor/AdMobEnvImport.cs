using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public class AdMobEnvImport : IPreprocessBuildWithReport
{
    const string ConfigPath = "Assets/Resources/admob-config.json";
    const string SettingsPath = "Assets/ThirdParty/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";
    public int callbackOrder { get { return -100; } }
    static AdMobEnvImport() { EditorApplication.delayCall += ApplyWhenReady; }
    static void ApplyWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += ApplyWhenReady; return; }
        if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(".env")) Apply();
    }
    [MenuItem("Chroma Drop/Apply AdMob .env")]
    public static void Apply()
    {
        var config = ReadEnv(File.ReadAllLines(".env"));
        if (!config.Valid) throw new BuildFailedException("AdMob .env의 iOS 앱 ID·광고 단위 ID 두 개를 확인해 주세요.");
        string json = JsonUtility.ToJson(config, true);
        if (!File.Exists(ConfigPath) || File.ReadAllText(ConfigPath) != json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            File.WriteAllText(ConfigPath, json);
            AssetDatabase.ImportAsset(ConfigPath);
        }
        var type = Type.GetType("GoogleMobileAds.Editor.GoogleMobileAdsSettings, GoogleMobileAds.Editor");
        if (type == null) throw new BuildFailedException("Google Mobile Ads Unity 플러그인이 필요합니다.");
        var settings = Resources.Load("GoogleMobileAdsSettings") as ScriptableObject;
        if (settings == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            AssetDatabase.Refresh();
            settings = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        else if (AssetDatabase.GetAssetPath(settings) != SettingsPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            AssetDatabase.Refresh();
            string error = AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(settings), SettingsPath);
            if (error.Length > 0) throw new BuildFailedException("AdMob SDK 설정 파일 이동에 실패했습니다.");
        }
        var serialized = new SerializedObject(settings);
        serialized.FindProperty("adMobIOSAppId").stringValue = config.iosAppId;
        serialized.FindProperty("adMobAndroidAppId").stringValue = "ca-app-pub-3940256099942544~3347511713";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }
    public static AdMobConfig ReadEnv(string[] lines)
    {
        var config = new AdMobConfig();
        foreach (string line in lines)
        {
            var match = Regex.Match(line, @"^\s*(?:export\s+)?[""']?(iosAppId|scoreSubmitAdUnitId|coinRewardAdUnitId|useTestAds)[""']?\s*[:=]\s*[""']?([^\s""',#}]+)[""']?\s*,?\s*(?:#.*)?$");
            if (!match.Success) continue;
            string value = match.Groups[2].Value;
            switch (match.Groups[1].Value)
            {
                case "iosAppId": config.iosAppId = value; break;
                case "scoreSubmitAdUnitId": config.scoreSubmitAdUnitId = value; break;
                case "coinRewardAdUnitId": config.coinRewardAdUnitId = value; break;
                case "useTestAds":
                    bool useTestAds;
                    if (!bool.TryParse(value, out useTestAds)) throw new BuildFailedException("useTestAds는 true 또는 false여야 합니다.");
                    config.useTestAds = useTestAds;
                    break;
            }
        }
        return config;
    }
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS) return;
        if (!File.Exists(".env")) throw new BuildFailedException("iOS 광고 설정을 위한 .env 파일이 없습니다.");
        Apply();
    }
}
