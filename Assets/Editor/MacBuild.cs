// MacBuild.cs — 맥 데스크톱 빌드 진입점. 개발 중 육안 확인용이다.
// 에디터: 메뉴 [ChromaDrop > Build > Mac 앱 (로컬 확인용)]
// CLI:    Unity -batchmode -nographics -quit -projectPath . \
//           -buildTarget OSXUniversal -executeMethod MacBuild.App
//
// CLI 인자 (-key value):
//   -output <경로>   결과 .app 경로. 기본 Builds/Mac/ChromaDrop.app
//
// 스토어 배포용이 아니므로 서명·공증은 하지 않는다. Mono 백엔드로 빌드해 시간을 줄인다.

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MacBuild
{
    const string OutputDir = "Builds/Mac";

    [MenuItem("ChromaDrop/Build/Mac 앱 (로컬 확인용)")]
    public static void AppMenu() => Build(batch: false);

    // 배치모드 진입점 — 실패 시 종료코드 1.
    public static void App() => Build(batch: true);

    static void Build(bool batch)
    {
        try
        {
            var path = Configure();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new Exception("빌드 씬이 비어 있다. [ChromaDrop > Setup Project] 를 먼저 실행할 것.");

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
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

    // 플레이어 설정을 맥 확인용으로 맞추고 출력 경로를 돌려준다.
    static string Configure()
    {
        var standalone = NamedBuildTarget.Standalone;

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);

        PlayerSettings.companyName = "jaemanc";
        PlayerSettings.productName = "Chroma Drop";
        PlayerSettings.bundleVersion = ChromaVersion.Value;
        PlayerSettings.SetApplicationIdentifier(standalone, "com.jaemanc.chromadrop");
        PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);

        // 세로 화면 게임이라 창 모드로 띄운다. 모바일 빌드는 이 값을 쓰지 않는다.
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 540;
        PlayerSettings.defaultScreenHeight = 960;
        PlayerSettings.resizableWindow = true;

        var path = Arg("-output");
        if (string.IsNullOrEmpty(path)) path = Path.Combine(OutputDir, "ChromaDrop.app");
        return path;
    }

    static string Arg(string name)
    {
        var argv = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(argv, name);
        return i >= 0 && i + 1 < argv.Length ? argv[i + 1] : null;
    }
}
