using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public class AppIconImport : IPreprocessBuildWithReport
{
    public const string IconPath = "Assets/_Project/Sprites/App/app-icon.png";
    public int callbackOrder { get { return -90; } }

    static AppIconImport() { EditorApplication.delayCall += Apply; }

    [MenuItem("Chroma Drop/Apply iOS App Icon")]
    public static void Apply()
    {
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (icon == null) return;
        var target = NamedBuildTarget.iOS;
        var sizes = PlayerSettings.GetIconSizes(target, IconKind.Any);
        var icons = new Texture2D[sizes.Length];
        for (int i = 0; i < icons.Length; i++) icons[i] = icon;
        PlayerSettings.SetIcons(target, icons, IconKind.Any);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS) return;
        if (!File.Exists(IconPath)) throw new BuildFailedException("iOS 앱 아이콘이 없습니다.");
        Apply();
    }
}
