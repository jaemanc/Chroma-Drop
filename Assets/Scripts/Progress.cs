using ColorMatcher.Core;
using UnityEngine;

/// <summary>스테이지 진행 상황. 어디까지 열렸는지와 스테이지별 최고 점수를 들고 있다.</summary>
public static class Progress
{
    /// <summary>스테이지 개수. 설정 파일이 유일한 출처다.</summary>
    static int StageCount { get { return Mathf.Max(1, StageLoader.Count); } }

    const string UnlockedKey = "stage_unlocked";
    const string LastKey = "stage_last";

    /// <summary>도전할 수 있는 가장 높은 스테이지. 1부터 시작한다.</summary>
    public static int Unlocked
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(UnlockedKey, 1), 1, StageCount); }
    }

    /// <summary>홈 화면에 띄울 스테이지. 사용자가 고른 값이 없으면 마지막으로 열린 것.</summary>
    public static int Selected
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(LastKey, Unlocked), 1, Unlocked); }
        set { PlayerPrefs.SetInt(LastKey, Mathf.Clamp(value, 1, Unlocked)); PlayerPrefs.Save(); }
    }

    public static bool AllCleared { get { return Unlocked >= StageCount; } }

    /// <summary>클리어했을 때 다음 스테이지를 연다. 이미 지난 스테이지를 다시 깨도 뒤로 가지 않는다.</summary>
    public static void Clear(int level)
    {
        int next = Mathf.Min(level + 1, StageCount);
        if (next > PlayerPrefs.GetInt(UnlockedKey, 1)) PlayerPrefs.SetInt(UnlockedKey, next);
        Selected = Mathf.Min(next, Unlocked);
        PlayerPrefs.Save();
    }

    public static string BestKey(int level) { return "best_stage_" + level; }

    public static int Best(int level) { return PlayerPrefs.GetInt(BestKey(level), 0); }

    public static void SetBest(int level, int score)
    {
        if (score <= Best(level)) return;
        PlayerPrefs.SetInt(BestKey(level), score);
        PlayerPrefs.Save();
    }

    /// <summary>개발용 초기화. 상점의 코인 치트와 함께 출시 전에 정리한다.</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(UnlockedKey);
        PlayerPrefs.DeleteKey(LastKey);
        for (int i = 1; i <= StageCount; i++) PlayerPrefs.DeleteKey(BestKey(i));
        PlayerPrefs.Save();
    }
}
