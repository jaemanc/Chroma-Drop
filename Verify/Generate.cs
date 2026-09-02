// Generate.cs — curve.config.json → stages.json 콘솔 진입점.

using System;
using System.IO;
using System.Text;
using ChromaDrop.Engine;

static class Generate
{
    const string CurvePath = "stages/curve.config.json";
    const string StagesPath = "stages/stages.json";

    static void Main(string[] args)
    {
        if (!File.Exists(CurvePath)) { Console.WriteLine("curve.config.json 없음"); Environment.Exit(1); }
        string existing = File.Exists(StagesPath) ? File.ReadAllText(StagesPath) : null;

        var res = CurveGen.Generate(File.ReadAllText(CurvePath), existing);
        bool verbose = Array.IndexOf(args, "-v") >= 0;
        if (verbose) foreach (var l in res.Log) Console.WriteLine("  " + l);

        if (res.Errors.Count > 0)
        {
            foreach (var e in res.Errors) Console.WriteLine("ERROR " + e);
            Environment.Exit(1);
        }
        File.WriteAllText(StagesPath, res.Json, new UTF8Encoding(false));
        Console.WriteLine("GENERATED " + StagesPath);
    }
}
