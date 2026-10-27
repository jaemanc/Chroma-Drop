using System.IO;
using UnityEditor;
using UnityEngine;

// 메타와 설정 파일은 Unity가 생성한다. 새 아트가 들어오면 같은 설정을 갱신한다.
[InitializeOnLoad]
public class UiThemeImport : AssetPostprocessor
{
    const string FontRoot = "Assets/ThirdParty/Fonts/";
    const string FramePath = "Assets/_Project/Sprites/UI/Buttons/LeaderBoard/pause_panel_no_top_gem.png";
    const string NightRoot = "Assets/_Project/Sprites/UI/NightPlay/";
    const string ComboRoot = "Assets/_Project/Sprites/UI/Combo/";
    const string HomePath = "Assets/_Project/Sprites/UI/Home/home-menu-v2.png";
    const string HomeBackgroundPath = "Assets/_Project/Sprites/UI/Home/home-background-wide.png";
    const string RewardPath = "Assets/_Project/Sprites/UI/Home/bonus-reward.png";
    const string MenuPath = "Assets/_Project/Sprites/UI/menu_background_no_text.png";

    static UiThemeImport() { EditorApplication.delayCall += Configure; }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var path in imported)
            if (path.StartsWith(FontRoot) || path == FramePath || path == MenuPath || path == HomePath || path == HomeBackgroundPath || path == RewardPath || path.StartsWith(NightRoot) || path.StartsWith(ComboRoot))
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
        set.homeMenu = SpriteAt(HomePath, false);
        set.homeBackground = SpriteAt(HomeBackgroundPath, false);
        set.bonusReward = SpriteAt(RewardPath, false);
        set.nightBackground = SpriteAt(NightRoot + "night-background.png", false);
        SpriteAt(NightRoot + "night-hud-atlas.png", false);
        set.nightTarget = Slice("target", 145, 290, 377, 143, 30);
        set.nightCard = Slice("card", 716, 290, 181, 143, 30);
        set.nightTimer = Slice("timer", 215, 442, 680, 73, 24);
        set.nightClock = Slice("clock", 140, 441, 87, 75, 0);
        set.nightPause = Slice("pause", 68, 28, 108, 110, 0);
        set.nightStage = Slice("stage", 180, 1330, 460, 100, 35);
        set.nightBoosters = new [] {
            Slice("bomb", 195, 1160, 153, 153, 0), Slice("hammer", 355, 1160, 151, 153, 0),
            Slice("rainbow", 515, 1160, 151, 153, 0), Slice("shuffle", 675, 1160, 153, 153, 0)
        };
        var combo = SpriteAt(ComboRoot + "combo-atlas.png", false);
        if (combo != null)
        {
            float w = combo.texture.width, h = combo.texture.height;
            set.comboTitle = Slice("title", 0, 0, w, h * .535f, 0, ComboRoot, "combo-atlas.png");
            set.comboGlyphs = new Sprite[11];
            for (int i = 0; i < set.comboGlyphs.Length; i++)
                set.comboGlyphs[i] = Slice("glyph-" + i, i % 6 * w / 6, h * (.54f + i / 6 * .215f), w / 6, h * .215f, 0, ComboRoot, "combo-atlas.png");
        }
        set.primaryButton = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprites/UI/Buttons/Playboard/02_pill_gold.png");
        set.secondaryButton = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprites/UI/Buttons/Playboard/01_pill_cyan.png");
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
    }

    // 원본 PNG는 보존하고 Unity 스프라이트의 UV와 테두리만 설정한다.
    static Sprite Slice(string name, float x, float top, float width, float height, float border, string root = NightRoot, string atlas = "night-hud-atlas.png")
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(root + atlas);
        if (texture == null) return null;
        string path = root + "hud-" + name + ".asset";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null) return sprite;
        var rect = new Rect(x, texture.height - top - height, width, height);
        sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        AssetDatabase.CreateAsset(sprite, path);
        return sprite;
    }

    static Sprite SpriteAt(string path, bool sliced)
    {
        if (!File.Exists(path)) return null;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        var border = sliced ? new Vector4(width * 0.14f, height * 0.14f, width * 0.14f, height * 0.11f) : Vector4.zero;
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
            || importer.spriteBorder != border || !importer.alphaIsTransparency || importer.npotScale != TextureImporterNPOTScale.None || importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
