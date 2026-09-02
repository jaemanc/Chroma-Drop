// ObstacleStyle.cs — 장애물 블록(벽돌)의 생김새를 한곳에 모은 상수.
// 색을 코드 여기저기에 흩지 않기 위해 분리했다. ScriptableObject 대신 상수인 이유는
// 이 프로젝트가 외부 에셋(.asset 포함) 의존 0 을 유지하기 때문이다.
//
// 원칙: 팔레트 블록과 헷갈리면 안 된다. 예전에는 무채색으로 그 거리를 벌었지만,
// 지금은 흰 줄눈이 그리는 '쌓아올린 격자' 가 그 역할을 한다. 색 타일에는 격자가 없다.
// 채도를 낮춘 점토색 + 굵은 외곽선 + 흰 줄눈 세 가지가 겹쳐야 밝은 판 위에서 바로 읽힌다.

using UnityEngine;

public static class ObstacleStyle
{
    // ---- 색 ----
    public static readonly Color Brick     = Palette.Hex(0xA8776A);   // 점토 (온전)
    public static readonly Color BrickPale = Palette.Hex(0xD9C3B8);   // 흰기가 섞인 벽돌
    public static readonly Color Mortar    = Palette.Hex(0xF3EDE4);   // 줄눈 — 흰색
    public static readonly Color Light     = Palette.Hex(0xC79688);   // 빛 받는 면
    public static readonly Color Shadow    = Palette.Hex(0x7A5147);   // 그늘
    public static readonly Color Outline   = Palette.Hex(0x2A2C30);   // 외곽선·균열

    // ---- 형태 (스프라이트 한 변에 대한 비율) ----
    public const float Scale     = 0.86f;    // 셀 대비 크기 — 주변에 여백이 남는다
    public const float RoundFrac = 0.075f;   // 색 타일(34%)보다 훨씬 각지게
    public const float LineFrac  = 0.070f;   // 외곽선 굵기
    public const float SplitFrac = 0.085f;   // 마지막 단계에서 조각이 벌어진 틈
    public const float MortarFrac = 0.026f;  // 줄눈 굵기 — 굵으면 벽돌보다 줄눈이 먼저 보인다

    /// <summary>손상 단계 수. 내구도가 몇이든 이 개수 안으로 눌러서 보여준다.</summary>
    public const int Stages = 5;

    /// <summary>남은 내구도를 손상 단계(0=온전 … Stages-1=곧 부서짐)로 바꾼다.
    /// 내구도가 2든 5든 한 대 맞을 때마다 겉모습이 반드시 달라져야 한다.</summary>
    public static int StageFor(int hp, int maxHp)
    {
        if (maxHp <= 0) return 0;
        int hits = Mathf.Clamp(maxHp - hp, 0, maxHp);
        return Mathf.Clamp(Mathf.RoundToInt(hits * (float)Stages / maxHp), 0, Stages - 1);
    }

    /// <summary>손상 단계별 본체 색. 깨질수록 밝아진다 —
    /// 텍스처가 안 보이는 크기에서도 밝기만으로 상태가 구분되게.</summary>
    public static Color BodyFor(int stage)
    {
        float t = Stages <= 1 ? 0f : stage / (float)(Stages - 1);
        return Color.Lerp(Brick, BrickPale, t * 0.55f);
    }
}
