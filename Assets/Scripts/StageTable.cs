// StageTable.cs — 스테이지별 설정. 난이도 수치의 유일한 출처다.
//
// 값은 stages/stages.json 에 있고, 코드에는 '파일을 못 읽었을 때의 기본값' 만 둔다.
// 밸런스를 고칠 때 코드를 건드리지 않는다.
//
// 읽는 순서:
//   1) persistentDataPath/stages.json  — 빌드 후에도 교체 가능한 자리
//   2) StreamingAssets/stages.json     — 빌드에 들어간 기본값
// 값을 바꾸고 게임을 다시 켜면 반영된다.

using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>스테이지 한 판의 설정.</summary>
public class StageSetting
{
    public int Level = 1;
    public int Moves = 30;

    public int PieceTimeMaxMs = 12000;   // 첫 조각
    public int PieceTimeMinMs = 9000;    // 마지막 조각
    public int ColorCount = 4;

    public int ObstacleFromMove = 0;     // 0 이면 벽돌이 안 나온다
    public int ObstacleEveryMoves = 0;
    public int ObstacleMax = 0;
    public int ObstacleHp = 2;

    public bool PenaltyObstacle;         // 아무것도 안 터진 수에 벽돌이 생기는가

    /// <summary>남은 수에 선형 비례하는 조각 제한 시간(ms). 첫 조각 Max, 마지막 Min.</summary>
    public int PieceTimeMs(int movesLeft, int totalMoves)
    {
        if (totalMoves <= 1) return PieceTimeMinMs;
        double t = PieceTimeMinMs + (PieceTimeMaxMs - PieceTimeMinMs)
                   * (double)(movesLeft - 1) / (totalMoves - 1);
        return (int)Mathf.Clamp((float)t, Mathf.Min(PieceTimeMinMs, PieceTimeMaxMs),
                                          Mathf.Max(PieceTimeMinMs, PieceTimeMaxMs));
    }

    /// <summary>이번 수가 끝난 뒤 새로 놓을 벽돌 수.</summary>
    public int ObstaclesAfterMove(int movesUsed, int totalMoves)
    {
        if (ObstacleFromMove <= 0 || ObstacleEveryMoves <= 0 || ObstacleMax <= 0) return 0;
        if (movesUsed < ObstacleFromMove) return 0;
        if ((movesUsed - ObstacleFromMove) % ObstacleEveryMoves != 0) return 0;
        if (totalMoves <= 0) return 1;

        // 후반으로 갈수록 1개에서 Max 개까지 늘어난다
        double t = movesUsed / (double)totalMoves;
        int n = 1 + (int)(t * (ObstacleMax - 1) + 0.0001);
        return Mathf.Clamp(n, 1, ObstacleMax);
    }
}

public static class StageTable
{
    public const string FileName = "stages.json";

    static List<StageSetting> stages;

    public static string OverridePath
    {
        get { return Path.Combine(Application.persistentDataPath, FileName); }
    }
    public static string BuiltInPath
    {
        get { return Path.Combine(Application.streamingAssetsPath, FileName); }
    }

    public static int Count { get { Load(); return stages.Count; } }

    /// <summary>다시 읽는다.</summary>
    public static void Reload() { stages = null; Load(); }

    /// <summary>레벨 설정. 범위를 넘으면 마지막 스테이지를 반복한다.</summary>
    public static StageSetting Get(int level)
    {
        Load();
        if (stages.Count == 0) return new StageSetting();
        int i = Mathf.Clamp(level, 1, stages.Count) - 1;
        return stages[i];
    }

    static void Load()
    {
        if (stages != null) return;
        stages = new List<StageSetting>();

        string path = File.Exists(OverridePath) ? OverridePath
                    : File.Exists(BuiltInPath) ? BuiltInPath : null;
        if (path == null)
        {
            Debug.LogWarning("[stages] " + FileName + " 을 못 찾았다. 기본값으로 시작한다: " + BuiltInPath);
            stages.Add(new StageSetting());
            return;
        }

        var root = Json.AsMap(Json.Parse(File.ReadAllText(path)));
        var arr = root != null && root.ContainsKey("stages") ? Json.AsList(root["stages"]) : null;
        if (arr == null)
        {
            Debug.LogError("[stages] " + path + " 을 읽을 수 없다. 기본값으로 시작한다.");
            stages.Add(new StageSetting());
            return;
        }

        foreach (var item in arr)
        {
            var m = Json.AsMap(item);
            if (m == null) continue;
            var d = new StageSetting();
            stages.Add(new StageSetting
            {
                Level = (int)Json.Num(m, "level", d.Level),
                Moves = (int)Json.Num(m, "moves", d.Moves),
                PieceTimeMaxMs = (int)Json.Num(m, "pieceTimeMaxMs", d.PieceTimeMaxMs),
                PieceTimeMinMs = (int)Json.Num(m, "pieceTimeMinMs", d.PieceTimeMinMs),
                ColorCount = (int)Json.Num(m, "colorCount", d.ColorCount),
                ObstacleFromMove = (int)Json.Num(m, "obstacleFromMove", d.ObstacleFromMove),
                ObstacleEveryMoves = (int)Json.Num(m, "obstacleEveryMoves", d.ObstacleEveryMoves),
                ObstacleMax = (int)Json.Num(m, "obstacleMax", d.ObstacleMax),
                ObstacleHp = (int)Json.Num(m, "obstacleHp", d.ObstacleHp),
                PenaltyObstacle = Json.Bool(m, "penaltyObstacle", d.PenaltyObstacle),
            });
        }
        stages.Sort((a, b) => a.Level.CompareTo(b.Level));
    }
}
