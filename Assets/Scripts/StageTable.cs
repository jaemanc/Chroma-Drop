// StageTable.cs — 스테이지별 설정. 난이도 수치의 유일한 출처다.
//
// 값은 stages/stages.json 에 있고, 코드에는 '파일을 못 읽었을 때의 기본값' 만 둔다.
// 밸런스를 고칠 때 코드를 건드리지 않는다.
//
// 읽는 순서:
//   1) persistentDataPath/stages.json  — 빌드 후에도 교체 가능한 자리
//   2) StreamingAssets/stages.json     — 빌드에 들어간 기본값
// 값을 바꾸고 게임을 다시 켜면 반영된다.
//
// 파일 최상단의 difficulty(1~5) 가 전체 난이도 손잡이다. 스테이지별 수치는 3 기준으로 적고,
// 손잡이가 축마다 배율을 곱한다 — 판 하나하나를 다시 적지 않고 곡선 전체를 올리고 내린다.

using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>스테이지 한 판의 설정.</summary>
public class StageSetting
{
    public int Level = 1;
    // 이 판이 어떤 종류인지 읽는 사람을 위한 라벨. 겹친 판은 "cells+pieces" 처럼 '+' 로 잇는다.
    // 동작을 정하는 건 아래 수치들이고, 라벨이 그것과 어긋나지 않는지는 테스트가 본다.
    public string Kind = "blocks";
    public int ClearBlocks = 10;         // 이만큼 부수면 클리어
    public int Moves = 30;

    public int PieceTimeMaxMs = 12000;   // 첫 조각
    public int PieceTimeMinMs = 9000;    // 마지막 조각
    public int ColorCount = 4;

    public int ObstacleFromMove = 0;     // 0 이면 벽돌이 안 나온다
    public int ObstacleEveryMoves = 0;
    public int ObstacleMax = 0;
    public int ObstacleHp = 2;

    public bool PenaltyObstacle;         // 아무것도 안 터진 수에 벽돌이 생기는가
    public int SteelCount;               // 판을 열 때 놓는 강철 수
    public int SteelHp = 5;              // 강철을 부수는 데 필요한 인접 소거 횟수

    // ---- 스테이지 종류 ----
    // 승리 조건은 '블록 수'(clearBlocks)와 '표시된 칸'(targetPattern) 둘이다.
    // 둘 다 주면 둘 다 채워야 하고, clearBlocks 를 0 으로 두면 칸 목표만 남는다.
    // pieceLimit·steelCount 는 목표가 아니라 조건이다 — 어떤 목표와도 섞인다.
    public int PieceLimit;               // >0 이면 '피스 N개 이내'. 수 제한을 이 값이 대신한다
    public int PollutionEvery;           // >0 이면 오염 스테이지. 이 수마다 근원 옆이 오염된다
    public int PollutionPerSpread = 1;   // 한 번에 오염되는 칸 수
    public string TargetPattern = "";    // 목표 칸 무늬. 빈 문자열이면 좌표 목표가 없다
    public int TargetCount;              // scatter/rows/cols 에서 쓸 수. 그림 무늬는 제 모양대로다

    /// <summary>이 판에서 쓸 수(=피스) 제한. 피스 제한 스테이지면 그 값이 대신한다.</summary>
    public int MoveBudget { get { return PieceLimit > 0 ? PieceLimit : Moves; } }

    /// <summary>표시된 칸을 깨야 하는 스테이지인가.</summary>
    public bool HasCellGoal { get { return !string.IsNullOrEmpty(TargetPattern); } }

    /// <summary>오염이 번지는 스테이지인가.</summary>
    public bool HasPollution { get { return PollutionEvery > 0; } }

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

    /// <summary>기준 난이도. 파일의 수치는 이 값에서 그대로 쓰인다.</summary>
    public const int BaseDifficulty = 3;

    // 난이도 1~5 의 축별 배율. 세로로 읽으면 한 난이도가 무엇을 바꾸는지 보인다.
    //                                       1      2      3      4      5
    static readonly float[] GoalMul   = { 0.70f, 0.85f, 1.00f, 1.15f, 1.30f };  // 목표 블록
    static readonly float[] BudgetMul = { 1.30f, 1.15f, 1.00f, 0.90f, 0.80f };  // 수·피스·오염 주기
    static readonly float[] TimeMul   = { 1.40f, 1.20f, 1.00f, 0.85f, 0.70f };  // 조각 제한 시간
    static readonly float[] HazardMul = { 0.50f, 0.75f, 1.00f, 1.25f, 1.50f };  // 강철·벽돌 수

    static List<StageSetting> stages;

    /// <summary>지금 난이도 (1~5). 파일 최상단 difficulty 가 정한다.</summary>
    public static int Difficulty { get; private set; }

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

    /// <summary>난이도 손잡이를 한 판에 먹인다. 파일의 수치는 BaseDifficulty 기준이다.
    /// 0 인 값은 '이 판에 없는 것' 이므로 건드리지 않는다 — 쉬운 난이도에서 없던 게 생기면 안 된다.</summary>
    public static void ApplyDifficulty(StageSetting s, int difficulty)
    {
        int i = Mathf.Clamp(difficulty, 1, 5) - 1;

        s.ClearBlocks = Scale(s.ClearBlocks, GoalMul[i]);
        s.Moves = Scale(s.Moves, BudgetMul[i]);
        s.PieceLimit = Scale(s.PieceLimit, BudgetMul[i]);
        s.PieceTimeMaxMs = Scale(s.PieceTimeMaxMs, TimeMul[i]);
        s.PieceTimeMinMs = Scale(s.PieceTimeMinMs, TimeMul[i]);
        s.SteelCount = Scale(s.SteelCount, HazardMul[i]);
        s.SteelHp = Scale(s.SteelHp, HazardMul[i]);
        s.ObstacleMax = Scale(s.ObstacleMax, HazardMul[i]);

        // 오염은 '몇 수마다' 라 예산과 같은 방향이다 — 어려울수록 주기가 짧아진다
        s.PollutionEvery = Scale(s.PollutionEvery, BudgetMul[i]);
    }

    /// <summary>있던 것은 없어지지 않는다. 0 은 0 그대로, 1 이상은 최소 1 로 남는다.</summary>
    static int Scale(int v, float mul)
    {
        if (v <= 0) return v;
        return Mathf.Max(1, Mathf.RoundToInt(v * mul));
    }

    static void Load()
    {
        if (stages != null) return;
        stages = new List<StageSetting>();
        Difficulty = BaseDifficulty;

        string path = File.Exists(OverridePath) ? OverridePath
                    : File.Exists(BuiltInPath) ? BuiltInPath : null;
        if (path == null)
        {
            Debug.LogWarning("[stages] " + FileName + " 을 못 찾았다. 기본값으로 시작한다: " + BuiltInPath);
            stages.Add(new StageSetting());
            return;
        }

        var root = Json.AsMap(Json.Parse(File.ReadAllText(path)));
        if (root != null)
            Difficulty = Mathf.Clamp((int)Json.Num(root, "difficulty", BaseDifficulty), 1, 5);
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
                Kind = Json.Str(m, "kind", d.Kind),
                ClearBlocks = (int)Json.Num(m, "clearBlocks", d.ClearBlocks),
                Moves = (int)Json.Num(m, "moves", d.Moves),
                PieceTimeMaxMs = (int)Json.Num(m, "pieceTimeMaxMs", d.PieceTimeMaxMs),
                PieceTimeMinMs = (int)Json.Num(m, "pieceTimeMinMs", d.PieceTimeMinMs),
                ColorCount = (int)Json.Num(m, "colorCount", d.ColorCount),
                ObstacleFromMove = (int)Json.Num(m, "obstacleFromMove", d.ObstacleFromMove),
                ObstacleEveryMoves = (int)Json.Num(m, "obstacleEveryMoves", d.ObstacleEveryMoves),
                ObstacleMax = (int)Json.Num(m, "obstacleMax", d.ObstacleMax),
                ObstacleHp = (int)Json.Num(m, "obstacleHp", d.ObstacleHp),
                PenaltyObstacle = Json.Bool(m, "penaltyObstacle", d.PenaltyObstacle),
                SteelCount = (int)Json.Num(m, "steelCount", d.SteelCount),
                SteelHp = (int)Json.Num(m, "steelHp", d.SteelHp),
                PieceLimit = (int)Json.Num(m, "pieceLimit", d.PieceLimit),
                TargetPattern = Json.Str(m, "targetPattern", d.TargetPattern),
                TargetCount = (int)Json.Num(m, "targetCount", d.TargetCount),
                PollutionEvery = (int)Json.Num(m, "pollutionEvery", d.PollutionEvery),
                PollutionPerSpread = (int)Json.Num(m, "pollutionPerSpread", d.PollutionPerSpread),
            });
        }
        foreach (var st in stages) ApplyDifficulty(st, Difficulty);
        stages.Sort((a, b) => a.Level.CompareTo(b.Level));
    }
}
