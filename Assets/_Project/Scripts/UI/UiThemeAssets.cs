using UnityEngine;

// 폰트와 공통 UI 아트는 설정의 인스펙터 참조로 빌드에 포함한다.
public class UiThemeAssets : ScriptableObject
{
    public Font titleFont, menuFont, bodyFont, numberFont, effectFont, koreanAccentFont;
    public Sprite pausePanel, menuBackground, primaryButton, secondaryButton;
    public Sprite homeMenu;
    public Sprite homeBackground;
    public Sprite bonusReward;
    public Sprite comboTitle;
    public Sprite[] comboGlyphs; // 0~9, x 순서
    public Sprite nightBackground, nightTarget, nightCard, nightTimer, nightClock, nightPause, nightStage;
    public Sprite[] nightBoosters;
}
