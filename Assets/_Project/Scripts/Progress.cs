// Progress.cs — 스테이지 진행. 어디까지 깼는지와 스테이지별 최고 점수를 들고 있다.
// PlayerPrefs 에 로컬 저장한다.

using UnityEngine;

public static class Progress
{
    const string UnlockedKey = "stage_unlocked";
    const string LastKey = "stage_last";

    /// <summary>도전할 수 있는 가장 높은 스테이지. 1부터 시작한다.</summary>
    public static int Unlocked
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(UnlockedKey, 1), 1, Mathf.Max(1, StageTable.Count)); }
    }

    /// <summary>지금 고른 스테이지. 열린 범위 안으로 눌린다.</summary>
    public static int Selected
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(LastKey, Unlocked), 1, Unlocked); }
        set { PlayerPrefs.SetInt(LastKey, Mathf.Clamp(value, 1, Unlocked)); PlayerPrefs.Save(); }
    }

    /// <summary>클리어하면 다음 스테이지를 연다. 지난 판을 다시 깨도 뒤로 가지 않는다.</summary>
    public static void Clear(int level)
    {
        int next = Mathf.Min(level + 1, Mathf.Max(1, StageTable.Count));
        if (next > PlayerPrefs.GetInt(UnlockedKey, 1)) PlayerPrefs.SetInt(UnlockedKey, next);
        PlayerPrefs.SetInt(LastKey, Mathf.Min(next, Unlocked));
        PlayerPrefs.Save();
    }

    /// <summary>개발용 초기화.</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(UnlockedKey);
        PlayerPrefs.DeleteKey(LastKey);
        PlayerPrefs.Save();
    }
}
