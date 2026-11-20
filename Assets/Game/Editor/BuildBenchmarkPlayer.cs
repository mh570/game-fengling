using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>构建 Windows benchmark player。</summary>
    public static class BuildBenchmarkPlayer
    {
        public static void Run()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/RpgLab.unity" },
                locationPathName = "D:/item/game/Build/FenglingBench.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[BuildBench] {summary.result} size={summary.totalSize} errors={summary.totalErrors}");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
