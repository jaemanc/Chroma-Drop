// LeaderboardSkin.cs — 리더보드 화면에 쓰는 그림 한 벌.
// 그림은 Game 씬 GameManager 의 인스펙터(Leaderboard)에서 연결한다.
// 비어 있는 칸은 예전처럼 코드로 그린 카드로 돌아간다 — 한 장 빠졌다고 화면이 사라지면 안 된다.

using UnityEngine;

[System.Serializable]
public class LeaderboardSkin
{
    public Sprite Title;         // 제목 배너
    public Sprite Panel;         // 판 프레임
    public Sprite TabPlayers;    // 탭 — 플레이어
    public Sprite TabNations;    // 탭 — 국가
    public Sprite[] Rows;        // 순위 줄 배경 1~10위. 모자라면 마지막 것을 돌려 쓴다
    public Sprite AdButton;      // 광고 보고 점수 등록
    public Sprite Close;         // 닫기

    /// <summary>그 순위의 줄 배경. 목록이 짧으면 마지막 그림을 쓴다.</summary>
    public Sprite RowFor(int rank)
    {
        if (Rows == null || Rows.Length == 0) return null;
        int i = Mathf.Clamp(rank - 1, 0, Rows.Length - 1);
        return Rows[i];
    }
}
