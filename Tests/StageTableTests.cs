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
            Console.WriteLine("StageTable: 3 통과 / 0 실패");
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
