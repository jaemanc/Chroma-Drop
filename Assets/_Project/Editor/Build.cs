// Build.cs — 배치모드 빌드 진입점. Tools/build-*.sh 가 -executeMethod 로 부른다.
// 에디터 GUI 에서 바꾼 값은 여기서 덮어쓰므로 빌드 설정의 출처는 이 파일이다.

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

static class BuildCommon
{
    /// <summary>빌드에 넣을 씬. Build Settings 에 켜 둔 것을 그대로 쓴다.</summary>
    public static string[] Scenes()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes) if (s.enabled) list.Add(s.path);
        return list.ToArray();
    }

    /// <summary>빌드하고 결과를 로그에 남긴다. 셸 스크립트가 이 줄을 읽어 산출물 경로를 찾는다.</summary>
    public static void Run(BuildPlayerOptions opts, string output)
    {
        var report = BuildPipeline.BuildPlayer(opts);
        if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0 || !System.IO.Directory.Exists(output))
        {
            Debug.LogError("빌드 실패: " + report.summary.result + " (에러 " + report.summary.totalErrors + "개)");
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log("CHROMADROP_BUILD_OUTPUT=" + output);
    }
}

public static class MacBuild
{
    /// <summary>맥 로컬 확인용 .app. 서명·공증은 하지 않는다.</summary>
    public static void App()
    {
        const string output = "Builds/Mac/ChromaDrop.app";
        BuildCommon.Run(new BuildPlayerOptions
        {
            scenes = BuildCommon.Scenes(),
            locationPathName = output,
            target = BuildTarget.StandaloneOSX,
            targetGroup = BuildTargetGroup.Standalone,
        }, output);
    }
}

public static class IosBuild
{
    /// <summary>iOS Xcode 프로젝트를 Builds/iOS 에 만든다.
    /// .app/.ipa 는 이 프로젝트를 Xcode(또는 xcodebuild)로 빌드해야 나온다 — 서명은 그쪽 몫이다.</summary>
    public static void Xcode()
    {
        const string output = "Builds/iOS";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.jaemanc.chromadrop");
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        BuildCommon.Run(new BuildPlayerOptions
        {
            scenes = BuildCommon.Scenes(),
            locationPathName = output,
            target = BuildTarget.iOS,
            targetGroup = BuildTargetGroup.iOS,
        }, output);
    }
}
