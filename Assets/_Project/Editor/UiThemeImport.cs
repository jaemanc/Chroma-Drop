using System.IO;
using UnityEditor;
using UnityEngine;

// 메타와 설정 파일은 Unity가 생성한다. 새 아트가 들어오면 같은 설정을 갱신한다.
[InitializeOnLoad]
public class UiThemeImport : AssetPostprocessor
{
    const string FontRoot = "Assets/ThirdParty/Fonts/";
    const string FramePath = "Assets/_Project/Sprites/UI/Buttons/LeaderBoard/pause_panel_no_top_gem.png";
    const string MenuPath = "Assets/_Project/Sprites/UI/menu_background_no_text.png";

    static UiThemeImport() { EditorApplication.delayCall += Configure; }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var path in imported)
            if (path.StartsWith(FontRoot) || path == FramePath || path == MenuPath)
            { EditorApplication.delayCall += Configure; break; }
    }

    [MenuItem("Chroma Drop/Apply UI Fonts")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += Configure; return; }
        const string path = "Assets/Resources/ui-theme.asset";
        var set = AssetDatabase.LoadAssetAtPath<UiThemeAssets>(path);
        if (set == null)
        {
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
            set = ScriptableObject.CreateInstance<UiThemeAssets>();
            AssetDatabase.CreateAsset(set, path);
        }
        set.titleFont = AssetDatabase.LoadAssetAtPath<Font>(FontRoot + "Fredoka/Fredoka-Bold.ttf");
        set.menuFont = AssetDatabase.LoadAssetAtPath<Font>(FontRoot + "Nunito/Nunito-ExtraBold.ttf");
        set.bodyFont = AssetDatabase.LoadAssetAtPath<Font>(FontRoot + "Pretendard/Pretendard-Regular.otf");
        set.numberFont = AssetDatabase.LoadAssetAtPath<Font>(FontRoot + "Fredoka/Fredoka-SemiBold.ttf");
        set.effectFont = AssetDatabase.LoadAssetAtPath<Font>(FontRoot + "Baloo2/Baloo2-ExtraBold.ttf");
        set.koreanAccentFont = AssetDatabase.LoadAssetAtPath<Font>(FontRoot + "Jua/Jua-Regular.ttf");
        set.pausePanel = SpriteAt(FramePath, true);
        set.menuBackground = SpriteAt(MenuPath, false);
        set.primaryButton = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprites/UI/Buttons/Playboard/02_pill_gold.png");
        set.secondaryButton = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprites/UI/Buttons/Playboard/01_pill_cyan.png");
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
    }

    static Sprite SpriteAt(string path, bool sliced)
    {
        if (!File.Exists(path)) return null;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        var border = sliced ? new Vector4(width * 0.14f, height * 0.14f, width * 0.14f, height * 0.11f) : Vector4.zero;
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
            || importer.spriteBorder != border || !importer.alphaIsTransparency)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
