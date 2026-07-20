// BuildScript.cs — Android AAB(App Bundle) 빌드 자동화.
// Google Play 업로드용 .aab를 생성한다. Play Store 요구사항(IL2CPP + ARM64, App Bundle,
// 서명)을 코드로 강제해 재현 가능한 빌드를 만든다.
//
// 실행 방법:
//   1) 에디터 메뉴: ChromaDrop > Build Android AAB
//   2) 배치모드(권장, CI/자동화):
//      Unity -quit -batchmode -nographics -projectPath . -executeMethod BuildScript.BuildAAB
//      서명 자격증명은 환경변수로 전달:
//        CHROMADROP_KEYSTORE, CHROMADROP_KEYSTORE_PASS, CHROMADROP_KEY_ALIAS, CHROMADROP_KEY_PASS
//      출력 경로(선택): CHROMADROP_AAB_OUTPUT (기본 build/ChromaDrop.aab)
//
// 주의: 서명 자격증명은 절대 코드/로그에 하드코딩하지 않는다. 환경변수로만 주입한다.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class BuildScript
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string DefaultOutput = "build/ChromaDrop.aab";
    const string AppId = "com.jaemanc.chromadrop";

    [MenuItem("ChromaDrop/Build Android AAB")]
    public static void BuildAAB()
    {
        string output = EnvOr("CHROMADROP_AAB_OUTPUT", DefaultOutput);
        string absOutput = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(absOutput));

        ConfigurePlayerSettings();
        ConfigureSigning();

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        // App Bundle(.aab) 출력 (Play Store 필수 포맷)
        EditorUserBuildSettings.buildAppBundle = true;

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = absOutput,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        };

        Debug.Log("[ChromaDrop] AAB 빌드 시작 → " + absOutput);
        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;

        if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"[ChromaDrop] AAB 빌드 성공: {absOutput} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime})");
        }
        else
        {
            Debug.LogError($"[ChromaDrop] AAB 빌드 실패: {summary.result} (errors={summary.totalErrors})");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    static void ConfigurePlayerSettings()
    {
        PlayerSettings.companyName = "jaemanc";
        PlayerSettings.productName = "Chroma Drop";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AppId);

        // 버전: 환경변수로 오버라이드 가능. versionCode는 Play Store 업로드마다 증가해야 함.
        PlayerSettings.bundleVersion = EnvOr("CHROMADROP_VERSION", "1.0.0");
        PlayerSettings.Android.bundleVersionCode = int.Parse(EnvOr("CHROMADROP_VERSION_CODE", "1"));

        // Play Store 요구: 64비트(ARM64) + IL2CPP
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);

        // Play Store는 배포용에서 디버깅 비활성
        EditorUserBuildSettings.development = false;
    }

    static void ConfigureSigning()
    {
        string keystore = Environment.GetEnvironmentVariable("CHROMADROP_KEYSTORE");
        string keystorePass = Environment.GetEnvironmentVariable("CHROMADROP_KEYSTORE_PASS");
        string keyAlias = Environment.GetEnvironmentVariable("CHROMADROP_KEY_ALIAS");
        string keyPass = Environment.GetEnvironmentVariable("CHROMADROP_KEY_PASS");

        if (string.IsNullOrEmpty(keystore) || string.IsNullOrEmpty(keystorePass) ||
            string.IsNullOrEmpty(keyAlias) || string.IsNullOrEmpty(keyPass))
        {
            // 자격증명이 없으면 서명 없이(디버그 키) 빌드 — 로컬 확인용. Play 업로드는 불가.
            PlayerSettings.Android.useCustomKeystore = false;
            Debug.LogWarning("[ChromaDrop] 서명 환경변수 없음 → 디버그 키로 빌드. Play Store 업로드용은 CHROMADROP_KEYSTORE* 환경변수를 설정하세요.");
            return;
        }

        string absKeystore = Path.GetFullPath(keystore);
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = absKeystore;
        PlayerSettings.Android.keystorePass = keystorePass;
        PlayerSettings.Android.keyaliasName = keyAlias;
        PlayerSettings.Android.keyaliasPass = keyPass;
        Debug.Log("[ChromaDrop] 커스텀 키스토어로 서명 (alias 이름은 로그에 노출하지 않음).");
    }

    static string EnvOr(string key, string fallback)
    {
        string v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }
}
