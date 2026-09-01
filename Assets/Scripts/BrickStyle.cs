// BrickStyle.cs — 암석(내구도) 블록의 생김새를 한곳에 모은 상수.
// 색을 코드 여기저기에 흩지 않기 위해 분리했다. ScriptableObject 대신 상수인 이유는
// 이 프로젝트가 외부 에셋(.asset 포함) 의존 0 을 유지하기 때문이다.
//
// 원칙: 암석에는 어떤 상태에서도 유채색을 쓰지 않는다.
// 팔레트 4색과 절대 겹치지 않아야 "매치 불가능한 장애물"로 즉시 읽힌다.

using UnityEngine;

public static class BrickStyle
{
    // ---- 색 (전부 무채색) ----
    public static readonly Color Body    = Palette.Hex(0x6E7278);   // 본체 (온전)
    public static readonly Color BodyMid = Palette.Hex(0x7E828A);   // 1회 손상
    public static readonly Color BodyHi  = Palette.Hex(0x8E929A);   // 2회 손상
    public static readonly Color Light   = Palette.Hex(0x8C9098);   // 밝은 면 (위/좌 베벨)
    public static readonly Color Shadow  = Palette.Hex(0x3A3D42);   // 외곽·음영 (아래/우 베벨)
    public static readonly Color Outline = Palette.Hex(0x2A2C30);   // 외곽선·균열
    public static readonly Color Dot     = Color.white;             // 내구도 도트

    // ---- 형태 (스프라이트 한 변에 대한 비율) ----
    public const float Scale      = 0.84f;    // 셀 대비 크기 — 주변에 배경 여백이 남는다
    public const float RoundFrac  = 0.073f;   // 일반 타일 라운드(7/32)의 1/3
    public const float LineFrac   = 0.075f;   // 외곽선 굵기 (≈ 2px @ 22px 셀)
    public const float CrackFrac  = 0.060f;   // 균열 굵기 = 셀 폭의 6%
    public const float SplitFrac  = 0.105f;   // 마지막 단계에서 조각이 갈라진 틈
    public const float DotRFrac   = 0.105f;   // 내구도 도트 반지름 — 22px 셀에서도 개수가 세어져야 한다

    /// <summary>손상 단계별 본체 색. 진행될수록 한 단계씩 밝아진다 —
    /// 텍스처가 안 보이는 크기에서도 밝기만으로 상태가 구분되게.</summary>
    public static Color BodyFor(int stage)
    {
        if (stage <= 0) return Body;
        return stage == 1 ? BodyMid : BodyHi;
    }
}
