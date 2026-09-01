// IceStyle.cs — 얼음 블록의 생김새를 한곳에 모은 상수.
// 색을 코드 여기저기에 흩지 않기 위해 분리했다. ScriptableObject 대신 상수인 이유는
// 이 프로젝트가 외부 에셋(.asset 포함) 의존 0 을 유지하기 때문이다.
//
// 원칙: 얼음에는 채도 높은 색을 쓰지 않는다. 팔레트 블록과 헷갈리면 안 된다.
// 거의 흰색에 가까운 냉색이라 초록 배경·파스텔 블록 사이에서 바로 구분된다.

using UnityEngine;

public static class IceStyle
{
    // ---- 색 ----
    public static readonly Color Body    = Palette.Hex(0xD8EEF6);   // 얼음 몸통 (온전)
    public static readonly Color BodyMid = Palette.Hex(0xE6F5FA);   // 금이 간 뒤 — 하얗게 뜬다
    public static readonly Color Light   = Palette.Hex(0xF7FDFF);   // 빛 받는 면
    public static readonly Color Shadow  = Palette.Hex(0xA6C9D6);   // 그늘
    public static readonly Color Outline = Palette.Hex(0x4C7285);   // 외곽선·균열
    public static readonly Color Dot     = Palette.Hex(0x2E4E5C);   // 내구도 표시

    // ---- 형태 (스프라이트 한 변에 대한 비율) ----
    public const float Scale      = 0.86f;    // 셀 대비 크기 — 주변에 배경 여백이 남는다
    public const float RoundFrac  = 0.085f;   // 색 타일(34%)보다 훨씬 각지게 — 형태로 구분된다
    public const float LineFrac   = 0.055f;   // 외곽선 굵기
    public const float CrackFrac  = 0.045f;   // 균열 굵기
    public const float SplitFrac  = 0.085f;   // 마지막 단계에서 조각이 갈라진 틈
    public const float DotRFrac   = 0.085f;   // 내구도 도트 반지름

    /// <summary>손상 단계별 몸통 색. 금이 갈수록 하얗게 부서진 느낌이 난다.</summary>
    public static Color BodyFor(int stage)
    {
        return stage <= 0 ? Body : BodyMid;
    }
}
