// BlockTheme.cs — 블록 세트마다 한 벌씩: 블록 그림 폴더, 팔레트 색, 판 크기, 판 배경 그림.
// 판을 시작할 때 이 중 하나를 무작위로 골라 판 크기부터 정한다.

using UnityEngine;

public class BlockTheme
{
    public string Blocks;     // Resources/blocks/ 아래 폴더. 파일 이름순이 색 순서다
    public Color[] Colors;    // 블록 그림마다 평균색 (파티클·고스트용). Blocks 파일 순서와 같다
    public int Size;          // 판 한 변의 칸 수 — 배경 그림에 그려진 격자와 같아야 한다
    public float BlockScale;  // 칸 대비 블록 그림 크기. 그림 둘레 여백이 클수록 키운다 — 실제 그림이 1칸을 넘으면 옆 칸과 겹친다
    public string Art;        // Resources/ 아래 판 배경 그림 (1024x1536)
    public Rect Grid;         // 배경 그림에서 격자 안쪽 자리 (그림 픽셀, 위가 0)

    public static readonly BlockTheme[] All =
    {
        new BlockTheme
        {
            Blocks = "lego_blocks", Size = 10, BlockScale = 1.10f,   // 그림 둘레 여백 11%
            Art = "borad_theme/game_board", Grid = new Rect(232, 392, 558, 558),
            Colors = new[] { Palette.Hex(0xEC3A4D), Palette.Hex(0xF1CD2E), Palette.Hex(0x389EF0), Palette.Hex(0x5FBD49),
                             Palette.Hex(0x995BD8), Palette.Hex(0xF191B3), Palette.Hex(0xF08A2A), Palette.Hex(0x48CDDB) },
        },
        new BlockTheme
        {
            Blocks = "lego_blocks_cats", Size = 8, BlockScale = 0.94f,   // 그림이 캔버스를 꽉 채운다 — 칸 사이에 틈을 둔다
            Art = "borad_theme/game_board_cat", Grid = new Rect(210, 465, 619, 619),
            Colors = new[] { Palette.Hex(0x3B3734), Palette.Hex(0x9E9794), Palette.Hex(0xE89D5A), Palette.Hex(0xE5DFDC) },   // black, gray, orange, white
        },
    };
}
