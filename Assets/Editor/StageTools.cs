// StageTools.cs — 스테이지 설정 생성·검증 진입점.
//   Unity -batchmode -quit -projectPath . -executeMethod StageTools.Generate
//   Unity -batchmode -quit -projectPath . -executeMethod StageTools.Validate
//
// 콘솔에서 더 빠르게 돌리려면 Tools/verify.sh / Tools/debug-stage.sh 를 쓴다.

using System.IO;
using System.Text;
using ChromaDrop.Engine;
using UnityEditor;
using UnityEngine;

public static class StageTools
{
    const string CurveFile = "curve.config.json";

    /// <summary>원본. 밸런싱 담당자가 고치는 자리다.</summary>
    static string SourceDir { get { return Path.Combine(Directory.GetCurrentDirectory(), StageCatalog.FolderName); } }
    /// <summary>빌드에 들어가는 사본.</summary>
    static string StagesDir { get { return Path.Combine(Application.streamingAssetsPath, StageCatalog.FolderName); } }
    static string CurvePath { get { return Path.Combine(SourceDir, CurveFile); } }
    static string StagesPath { get { return Path.Combine(SourceDir, StageCatalog.FileName); } }

    [MenuItem("Chroma Drop/스테이지 생성 (curve.config.json → stages.json)")]
    public static void Generate()
    {
        if (!File.Exists(CurvePath)) { Debug.LogError("curve.config.json 없음: " + CurvePath); return; }
        string existing = File.Exists(StagesPath) ? File.ReadAllText(StagesPath) : null;

        var res = CurveGen.Generate(File.ReadAllText(CurvePath), existing);
        foreach (var l in res.Log) Debug.Log("[curve] " + l);
        if (res.Errors.Count > 0)
        {
            foreach (var e in res.Errors) Debug.LogError("[curve] " + e);
            Debug.LogError("STAGE GENERATE FAILED");
            return;
        }

        Directory.CreateDirectory(SourceDir);
        File.WriteAllText(StagesPath, res.Json, new UTF8Encoding(false));
        Sync();
        AssetDatabase.Refresh();
        Debug.Log("STAGE GENERATE OK: " + StagesPath);
    }

    /// <summary>원본을 빌드용 사본으로 옮긴다. 원본이 유일한 출처다.</summary>
    [MenuItem("Chroma Drop/스테이지 동기화 (stages → StreamingAssets)")]
    public static void Sync()
    {
        Directory.CreateDirectory(StagesDir);
        foreach (var name in new[] { StageCatalog.FileName, "stage-schema.json", CurveFile })
        {
            string src = Path.Combine(SourceDir, name);
            if (File.Exists(src)) File.Copy(src, Path.Combine(StagesDir, name), true);
        }
        AssetDatabase.Refresh();
    }

    [MenuItem("Chroma Drop/스테이지 검증")]
    public static void Validate()
    {
        var set = StageCatalog.Reload();
        Debug.Log("[stages] 원본: " + set.SourcePath);
        foreach (var w in set.Warnings) Debug.LogWarning("[stages] " + w);
        if (!set.Ok)
        {
            foreach (var e in set.Errors) Debug.LogError("[stages] " + e);
            Debug.LogError("STAGE VALIDATE FAILED");
            return;
        }

        var bg = new Rgb(0.80, 0.89, 0.93);
        int bad = 0;
        foreach (var def in set.Stages)
        {
            var c = StageValidator.Check(def, bg);
            foreach (var i in c.Info) Debug.Log("[stage " + def.StageId + "] " + i);
            foreach (var w in c.Warnings) Debug.LogWarning("[stage " + def.StageId + "] " + w);
            foreach (var e in c.Errors) { Debug.LogError("[stage " + def.StageId + "] " + e); bad++; }
        }
        if (bad > 0) { Debug.LogError("STAGE VALIDATE FAILED: 오류 " + bad + "건"); return; }
        Debug.Log("STAGE VALIDATE OK: " + set.Count + "개 스테이지");
    }
}
