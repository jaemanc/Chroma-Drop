using UnityEngine;

// 폰트와 공통 UI 아트는 설정의 인스펙터 참조로 빌드에 포함한다.
public class UiThemeAssets : ScriptableObject
{
    public Font titleFont, menuFont, bodyFont, numberFont, effectFont, koreanAccentFont;
    public Sprite pausePanel, menuBackground, primaryButton, secondaryButton;
}
