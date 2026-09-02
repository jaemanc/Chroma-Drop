// StageTools.cs — 스테이지 설정 생성·검증 진입점.
// 배치모드에서 실행:
//   Unity -batchmode -quit -projectPath . -executeMethod StageTools.Generate
//   Unity -batchmode -quit -projectPath . -executeMethod StageTools.Validate

using System.IO;
using System.Text;
using ColorMatcher.Core;
using UnityEditor;
using UnityEngine;

public static class StageTools
{
    [MenuItem("Chroma Drop/스테이지 생성 (curve.config.json → stages.json)")]
    public static void Generate()
    {
        var existing = StageLoader.Report.Ok ? StageLoader.All : null;
        var rep = CurveGenerator.Generate(existing);

        foreach (var line in rep.Lines) Debug.Log("[curve] " + line);
        if (rep.Errors.Count > 0)
        {
            foreach (var e in rep.Errors) Debug.LogError("[curve] " + e);
            Debug.LogError("STAGE GENERATE FAILED");
            return;
        }

        string dir = Path.Combine(Application.streamingAssetsPath, StageLoader.FolderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, StageLoader.StagesFile), rep.Json, new UTF8Encoding(false));
        AssetDatabase.Refresh();
        Debug.Log("STAGE GENERATE OK: " + Path.Combine(dir, StageLoader.StagesFile));
    }

    [MenuItem("Chroma Drop/스테이지 검증")]
    public static void Validate()
    {
        var rep = StageLoader.Reload();
        Debug.Log("[stages] 원본: " + rep.SourcePath);
        foreach (var w in rep.Warnings) Debug.LogWarning("[stages] " + w);
        if (!rep.Ok)
        {
            foreach (var e in rep.Errors) Debug.LogError("[stages] " + e);
            Debug.LogError("STAGE VALIDATE FAILED");
            return;
        }

        int bad = 0;
        foreach (var cfg in rep.Stages)
        {
            var v = StageValidator.Check(cfg);
            var board = new Board(cfg.ToBoardSetup(Rules.ColorCount), cfg.StageId);
            StageValidator.CheckBoard(cfg, board, v);

            foreach (var i in v.Info) Debug.Log("[stage " + cfg.StageId + "] " + i);
            foreach (var w in v.Warnings) Debug.LogWarning("[stage " + cfg.StageId + "] " + w);
            foreach (var e in v.Errors) { Debug.LogError("[stage " + cfg.StageId + "] " + e); bad++; }
        }

        if (bad > 0) { Debug.LogError("STAGE VALIDATE FAILED: 오류 " + bad + "건"); return; }
        Debug.Log("STAGE VALIDATE OK: " + rep.Stages.Count + "개 스테이지");
    }
}
