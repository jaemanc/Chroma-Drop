// BlockTheme.cs — 블록 세트마다 한 벌씩: 블록 그림, 팔레트 색, 판 크기, 판 배경 그림.
// 값은 Game 씬 GameManager 의 인스펙터(Themes)에 있다. 판을 시작할 때 이 중 하나를 무작위로 골라 판 크기부터 정한다.

using UnityEngine;

[System.Serializable]
public class BlockTheme
{
    public string Name;           // 인스펙터에서 구분하는 이름
    public Texture2D[] Blocks;    // 블록 그림. 넣은 순서와 상관없이 파일 이름순이 색 순서다
    public Color[] Colors;        // 블록 그림마다 평균색 (파티클·고스트용). Blocks 파일 이름순과 같다
    public int Size;              // 원본 아트의 칸 수. 플레이 보드는 PlaySize로 한 줄씩 확장한다.
    public float BlockScale;      // 칸 대비 블록 그림 크기. 그림 둘레 여백이 클수록 키운다 — 실제 그림이 1칸을 넘으면 옆 칸과 겹친다
    public Texture2D Board;       // 판 배경 그림 (1024x1536)
    public Rect Grid;             // 배경 그림에서 격자 안쪽 자리 (그림 픽셀, 위가 0)

    public const float BoardZoom = 1.10f;
    public int PlaySize { get { return Size + 1; } }
    // 보드는 10% 커지고 칸은 한 줄 늘어난다. 블록도 기존 화면 크기보다 10% 커지게 보정한다.
    public float DrawScale { get { return (float)PlaySize / Size; } }
}
