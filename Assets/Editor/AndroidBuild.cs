// AndroidBuild.cs — 안드로이드 APK/AAB 빌드 진입점.
// 에디터: 메뉴 [ChromaDrop > Build > Android APK (기기에 떨구기)] / [Android AAB (스토어 업로드)]
// CLI:    Tools/build-android.sh apk | aab  (내부적으로 -executeMethod AndroidBuild.Apk / .Aab 호출)
//
// CLI 인자 (-key value):
//   -output <경로>        결과 파일 경로. 기본 Builds/Android/ChromaDrop-<ver>.(apk|aab)
//   -appVersion <1.0.0>   bundleVersion
//   -versionCode <N>      bundleVersionCode (미지정 시 현재 값 유지)
//   -scriptingBackend <il2cpp|mono>  기본 il2cpp (mono 는 빠른 테스트 빌드용, 64bit 미지원)
//   -development          개발 빌드(프로파일러 연결 가능)
//
// 릴리즈 서명 키스토어는 환경변수로 전달한다. 하나라도 없으면 Unity 디버그 키로 서명한다.
//   CHROMADROP_KEYSTORE, CHROMADROP_KEYSTORE_PASS, CHROMADROP_KEYALIAS, CHROMADROP_KEYALIAS_PASS

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class AndroidBuild
{
    const string OutputDir = "Builds/Android";

    [MenuItem("ChromaDrop/Build/Android APK (기기에 떨구기)")]
    public static void ApkMenu() => Build(false, batch: false);

    [MenuItem("ChromaDrop/Build/Android AAB (스토어 업로드)")]
    public static void AabMenu() => Build(true, batch: false);

    // 배치모드 진입점 — 실패 시 종료코드 1.
    public static void Apk() => Build(false, batch: true);
    public static void Aab() => Build(true, batch: true);

    static void Build(bool appBundle, bool batch)
    {
        try
        {
            var path = Configure(appBundle);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new Exception("빌드 씬이 비어 있다. [ChromaDrop > Setup Project] 를 먼저 실행할 것.");

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var options = BuildOptions.None;
            if (HasFlag("-development"))
                options |= BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = options,
            });

            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
                throw new Exception($"빌드 실패: {s.result} (오류 {s.totalErrors}건)");

            Debug.Log($"[ChromaDrop] 빌드 성공 → {path} ({s.totalSize / 1024f / 1024f:F1} MB, {s.totalTime.TotalSeconds:F0}s)");
            Console.WriteLine($"CHROMADROP_BUILD_OUTPUT={Path.GetFullPath(path)}");
            if (batch) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[ChromaDrop] " + e.Message);
            if (batch) EditorApplication.Exit(1);
            else throw;
        }
    }

    // 플레이어 설정을 안드로이드 배포 상태로 맞추고 출력 경로를 돌려준다.
    static string Configure(bool appBundle)
    {
        var android = NamedBuildTarget.Android;

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        PlayerSettings.companyName = "jaemanc";
        PlayerSettings.productName = "Chroma Drop";
        PlayerSettings.SetApplicationIdentifier(android, "com.jaemanc.chromadrop");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        var backend = (Arg("-scriptingBackend") ?? "il2cpp").ToLowerInvariant();
        if (backend == "mono")
        {
            // 빠른 확인용. 32bit 전용이라 스토어 업로드에는 쓸 수 없다.
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.Mono2x);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
        }
        else
        {
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        }

        // -appVersion 이 없으면 코드에 적힌 버전을 쓴다. 버전은 한곳에서만 정한다.
        var appVersion = Arg("-appVersion");
        PlayerSettings.bundleVersion = string.IsNullOrEmpty(appVersion) ? ChromaVersion.Value : appVersion;

        var versionCode = Arg("-versionCode");
        if (!string.IsNullOrEmpty(versionCode) && int.TryParse(versionCode, out var code))
            PlayerSettings.Android.bundleVersionCode = code;

        EditorUserBuildSettings.buildAppBundle = appBundle;
        ApplyKeystore();

        var ext = appBundle ? "aab" : "apk";
        var path = Arg("-output");
        if (string.IsNullOrEmpty(path))
            path = Path.Combine(OutputDir, $"ChromaDrop-{PlayerSettings.bundleVersion}.{ext}");
        return path;
    }

    // 환경변수 4개가 모두 있으면 릴리즈 키로, 아니면 디버그 키로 서명한다.
    static void ApplyKeystore()
    {
        var store = Environment.GetEnvironmentVariable("CHROMADROP_KEYSTORE");
        var storePass = Environment.GetEnvironmentVariable("CHROMADROP_KEYSTORE_PASS");
        var alias = Environment.GetEnvironmentVariable("CHROMADROP_KEYALIAS");
        var aliasPass = Environment.GetEnvironmentVariable("CHROMADROP_KEYALIAS_PASS");

        if (new[] { store, storePass, alias, aliasPass }.Any(string.IsNullOrEmpty))
        {
            PlayerSettings.Android.useCustomKeystore = false;
            Debug.Log("[ChromaDrop] 키스토어 환경변수 없음 → 디버그 키로 서명 (사이드로드 전용).");
            return;
        }

        if (!File.Exists(store))
            throw new Exception($"키스토어 파일을 찾을 수 없다: {store}");

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = Path.GetFullPath(store);
        PlayerSettings.Android.keystorePass = storePass;
        PlayerSettings.Android.keyaliasName = alias;
        PlayerSettings.Android.keyaliasPass = aliasPass;
        Debug.Log($"[ChromaDrop] 릴리즈 키로 서명: {Path.GetFileName(store)} (alias={alias})");
    }

    static string Arg(string name)
    {
        var argv = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(argv, name);
        return i >= 0 && i + 1 < argv.Length ? argv[i + 1] : null;
    }

    static bool HasFlag(string name) => Environment.GetCommandLineArgs().Contains(name);
}
