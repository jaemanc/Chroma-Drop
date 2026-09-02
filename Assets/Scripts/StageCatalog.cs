// StageCatalog.cs — 런타임 stages.json 로드. 파싱은 엔진의 StageData 가 한다.
//
// 읽는 순서:
//   1) persistentDataPath/stages/stages.json  — 빌드 후에도 교체 가능한 자리
//   2) StreamingAssets/stages/stages.json     — 빌드에 들어간 기본값
// 값 하나 바꾸고 게임을 다시 켜면 반영된다. 빌드는 필요 없다.

using System.IO;
using ChromaDrop.Engine;
using UnityEngine;

public static class StageCatalog
{
    public const string FolderName = "stages";
    public const string FileName = "stages.json";

    static StageSet cached;

    public static string OverridePath
    {
        get { return Path.Combine(Path.Combine(Application.persistentDataPath, FolderName), FileName); }
    }

    public static string BuiltInPath
    {
        get { return Path.Combine(Path.Combine(Application.streamingAssetsPath, FolderName), FileName); }
    }

    public static StageSet Set { get { if (cached == null) Load(); return cached; } }
    public static int Count { get { return Set.Count; } }
    public static StageDef Get(int stageId) { return Set.Get(stageId); }

    /// <summary>다시 읽는다.</summary>
    public static StageSet Reload() { cached = null; return Set; }

    static void Load()
    {
        string path = File.Exists(OverridePath) ? OverridePath
                    : File.Exists(BuiltInPath) ? BuiltInPath : null;

        if (path == null)
        {
            cached = new StageSet();
            cached.Errors.Add("stages.json 을 찾지 못했다: " + BuiltInPath);
            return;
        }

        cached = StageData.Parse(File.ReadAllText(path));
        cached.SourcePath = path;
    }
}
