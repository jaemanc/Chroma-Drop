// UiTheme.cs — 버튼 색·치수 상수와 입체 버튼 컴포넌트.
// 값을 코드 여기저기에 흩지 않으려고 분리했다. ScriptableObject 대신 상수인 이유는
// 이 프로젝트가 외부 에셋(.asset 포함) 의존 0 을 유지하기 때문이다.
//
// 입체감은 그라디언트·그림자·블러 없이 단색 두 겹(lip + face)으로만 만든다.
// 보드 팔레트(블루/퍼플/옐로우/로즈)와 겹치지 않게 초록·청록·붉은 계열에서 골랐다.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum UiKind { Primary, Secondary, Destructive }

public static class UiTheme
{
    // ---- 치수 (캔버스 기준 1080x1920) ----
    public const float Radius     = 14f;   // 그리드 타일보다 크게 — UI 와 보드를 형태로 구분
    public const float Lip        = 8f;    // 평소 드러나는 아래 두께
    public const float LipPressed = 2f;    // 눌렀을 때
    public const float HiBar      = 9f;    // 상단 하이라이트 바 높이
    public const float HiInset    = 10f;   // 하이라이트 바 좌우 인셋
    public const float PressTime  = 0.07f; // 60~80ms
    public const float MinTouch   = 44f;   // 최소 터치 영역
    public const float TextScale  = 0.30f; // 버튼 높이 대비 글자 크기

    // ---- 색 ----
    public static Color Face(UiKind k)
    {
        switch (k)
        {
            case UiKind.Primary:     return Palette.Hex(0x25B573);   // 초록
            case UiKind.Destructive: return Palette.Hex(0xE0625C);   // 붉은
            default:                 return Palette.Hex(0x596170);   // 무채색 계열
        }
    }

    /// <summary>눌렀을 때의 face — 한 단계 어둡게.</summary>
    public static Color FacePressed(UiKind k) { return Darken(Face(k), 0.14f); }

    /// <summary>lip = face 명도의 65%.</summary>
    public static Color LipColor(UiKind k) { return Darken(Face(k), 0.35f); }

    /// <summary>face 상단 하이라이트 — face 보다 밝게.</summary>
    public static Color Highlight(UiKind k) { return Lighten(Face(k), 0.18f); }

    /// <summary>글자색 — 같은 계열의 가장 어두운 톤. 검정/흰색을 쓰지 않는다.</summary>
    public static Color Text(UiKind k)
    {
        switch (k)
        {
            case UiKind.Primary:     return Palette.Hex(0x0B3D28);
            case UiKind.Destructive: return Palette.Hex(0x4A1614);
            default:                 return Palette.Hex(0x151A22);
        }
    }

    static Color Darken(Color c, float t) { return new Color(c.r * (1 - t), c.g * (1 - t), c.b * (1 - t), c.a); }
    static Color Lighten(Color c, float t) { return Color.Lerp(c, Color.white, t); }

    /// <summary>둥근 사각 9슬라이스 스프라이트. 버튼 크기가 달라도 모서리 반경이 유지된다.</summary>
    public static Sprite RoundedSprite(float radius)
    {
        int r = Mathf.Max(2, Mathf.RoundToInt(radius));
        int S = r * 2 + 4;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * S + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f,
                             0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }
}

/// <summary>아래 두께(lip)를 가진 입체 버튼. 누르면 face 가 내려앉아 두께가 줄어든다.</summary>
public class UiButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public RectTransform face;
    public Image faceImg, lipImg, hiImg;
    public Text label;

    UiKind kind;
    bool pressed;
    float t;          // 0 = 평소, 1 = 눌림

    public void Init(UiKind k)
    {
        kind = k;
        Apply(0f);
        var rt = (RectTransform)transform;
        if (label != null) label.fontSize = Mathf.RoundToInt(rt.rect.height * UiTheme.TextScale);
    }

    /// <summary>선택 상태 표시처럼 종류를 바꿀 때.</summary>
    public void SetKind(UiKind k) { kind = k; Apply(t); }

    public void OnPointerDown(PointerEventData e) { pressed = true; }
    public void OnPointerUp(PointerEventData e) { pressed = false; }

    void Update()
    {
        float target = pressed ? 1f : 0f;
        if (Mathf.Approximately(t, target)) return;
        t = Mathf.MoveTowards(t, target, Time.unscaledDeltaTime / UiTheme.PressTime);
        Apply(t);
    }

    void Apply(float k)
    {
        float e = 1f - (1f - k) * (1f - k);                       // ease-out
        float drop = (UiTheme.Lip - UiTheme.LipPressed) * e;      // 8 → 2 만큼 내려앉는다
        if (face != null) face.anchoredPosition = new Vector2(0, -drop);
        if (faceImg != null) faceImg.color = Color.Lerp(UiTheme.Face(kind), UiTheme.FacePressed(kind), e);
        if (lipImg != null) lipImg.color = UiTheme.LipColor(kind);
        if (hiImg != null) hiImg.color = UiTheme.Highlight(kind);
        if (label != null) label.color = UiTheme.Text(kind);
    }
}
