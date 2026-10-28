// Unity 밖에서 실제 StageTable의 파일 누락/손상 경로를 검증한다.
// 아래 Unity 대역은 경로와 수학 함수만 제공하며 씬 검증을 대신하지 않는다.
using System;
using System.IO;
using System.Reflection;

class StageTableTests
{
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "chromadrop-stages-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        UnityEngine.Application.persistentDataPath = root;
        UnityEngine.Application.streamingAssetsPath = Path.Combine(root, "missing");
        try
        {
            CheckFallback("파일 없음");
            File.WriteAllText(Path.Combine(root, "stages.json"), "{");
            CheckFallback("손상된 JSON");
            File.WriteAllText(Path.Combine(root, "stages.json"), "{}");
            CheckFallback("stages 필드 없음");
            File.Delete(Path.Combine(root, "stages.json"));
            UnityEngine.Application.streamingAssetsPath = Path.GetFullPath("Assets/StreamingAssets");
            typeof(StageTable).GetField("stages", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(StageTable).GetField("timeAttack", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            if (File.ReadAllText("stages/stages.json") != File.ReadAllText(StageTable.BuiltInPath))
                throw new Exception("스테이지 원본과 빌드 사본 불일치");
            if (StageTable.Count != 100 || StageTable.Get(1).MoveBudget != 20 ||
                StageTable.Get(2).MoveBudget != 16 || StageTable.Get(100).MoveBudget != 18)
                throw new Exception("강화된 이동·피스 제한이 런타임 설정에 반영되지 않음");
            if (StageTable.Get(1).ClearBlocks != 7 || StageTable.Get(1).PieceTimeMaxMs != 16800 ||
                StageTable.TimeAttack.Seconds != 120 || StageTable.TimeAttack.PieceTimeMaxMs != 4000)
                throw new Exception("목표·제한시간 또는 타임어택이 변경됨");
            Console.WriteLine("[PASS] 강화된 스테이지 예산과 기존 목표·제한시간 보존");
            Console.WriteLine("StageTable: 4 통과 / 0 실패");
        }
        finally { Directory.Delete(root, true); }
    }

    static void CheckFallback(string name)
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(StageTable).GetField("stages", flags).SetValue(null, null);
        typeof(StageTable).GetField("timeAttack", flags).SetValue(null, null);
        if (StageTable.TimeAttack == null || StageTable.TimeAttack.Seconds <= 0 || StageTable.Count != 1)
            throw new Exception(name + ": 기본 타임어택/스테이지 설정 없음");
        Console.WriteLine("[PASS] " + name);
    }
}

namespace UnityEngine
{
    public static class Application
    {
        public static string persistentDataPath, streamingAssetsPath;
    }
    public static class Debug
    {
        public static void LogWarning(object value) { }
        public static void LogError(object value) { }
    }
    public static class Mathf
    {
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static int Clamp(int v, int lo, int hi) { return Math.Min(hi, Math.Max(lo, v)); }
        public static float Clamp(float v, float lo, float hi) { return Math.Min(hi, Math.Max(lo, v)); }
        public static int RoundToInt(float v) { return (int)Math.Round(v); }
    }
}
