// ObstacleStyle.cs — 장애물 블록(콘크리트)의 생김새를 한곳에 모은 상수.
// 색을 코드 여기저기에 흩지 않기 위해 분리했다. ScriptableObject 대신 상수인 이유는
// 이 프로젝트가 외부 에셋(.asset 포함) 의존 0 을 유지하기 때문이다.
//
// 원칙: 장애물에는 유채색을 쓰지 않는다. 팔레트 블록과 헷갈리면 안 된다.
// 무채색 + 각진 실루엣 + 굵은 외곽선 세 가지가 겹쳐야 밝은 판 위에서 바로 읽힌다.

using UnityEngine;

public static class ObstacleStyle
{
    // ---- 색 (전부 무채색) ----
    public static readonly Color Body    = Palette.Hex(0x6E7278);   // 본체 (온전)
    public static readonly Color BodyHit = Palette.Hex(0x83878E);   // 금 간 뒤 — 한 단계 밝게
    public static readonly Color Light   = Palette.Hex(0x9CA0A8);   // 빛 받는 면
    public static readonly Color Shadow  = Palette.Hex(0x4A4D53);   // 그늘
    public static readonly Color Outline = Palette.Hex(0x2A2C30);   // 외곽선·균열

    // ---- 형태 (스프라이트 한 변에 대한 비율) ----
    public const float Scale     = 0.86f;    // 셀 대비 크기 — 주변에 여백이 남는다
    public const float RoundFrac = 0.075f;   // 색 타일(34%)보다 훨씬 각지게
    public const float LineFrac  = 0.070f;   // 외곽선 굵기
    public const float SplitFrac = 0.085f;   // 마지막 단계에서 조각이 벌어진 틈

    /// <summary>손상 단계별 본체 색. 깨질수록 한 단계 밝아진다 —
    /// 텍스처가 안 보이는 크기에서도 밝기만으로 상태가 구분되게.</summary>
    public static Color BodyFor(int stage)
    {
        return stage <= 0 ? Body : BodyHit;
    }
}
