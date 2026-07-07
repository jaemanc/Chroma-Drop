// ProjectBootstrap.cs — 씬/빌드 설정/모바일 플레이어 설정을 코드로 구성.
// 메뉴 [ChromaDrop > Setup Project] 또는 배치모드 -executeMethod ProjectBootstrap.Setup 로 실행.
// 멱등: 여러 번 실행해도 같은 결과.

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProjectBootstrap
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("ChromaDrop/Setup Project")]
    public static void Setup()
    {
        // ----- 플레이어 설정 (iOS/Android 배포 대비) -----
        PlayerSettings.companyName = "jaemanc";
        PlayerSettings.productName = "Chroma Drop";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.jaemanc.chromadrop");
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.jaemanc.chromadrop");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

        // ----- 메인 씬 (카메라 + GameManager 하나면 충분 — 나머지는 런타임 생성) -----
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.09f, 0.09f, 0.12f);
        camGo.AddComponent<AudioListener>();

        var gmGo = new GameObject("GameManager");
        gmGo.AddComponent<GameManager>();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[ChromaDrop] Setup 완료: " + ScenePath + " + 모바일 플레이어 설정");
    }
}
