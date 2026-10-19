#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PianoTeacher3D.EditorTools
{
    /// <summary>
    /// 真实 Windows Player 基线构建（优化方案 P0-01 / §7-1 / §7-4）。
    /// <para>
    /// 只构建 <c>PianoLesson</c> 场景，且使用 <see cref="BuildOptions.Development"/>
    /// （保留 Profiler 计数器，<c>PlayerPerfSampler</c> 才能读到内存数据）。
    /// 由 <c>Tools/verify.py --player</c> 通过 <c>-executeMethod</c> 调用，也可手工执行：
    /// </para>
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;项目&gt; \
    ///   -executeMethod PianoTeacher3D.EditorTools.PlayerBaselineBuild.Build
    /// </code>
    /// 输出路径由环境变量 <c>PT3D_PLAYER_OUT</c> 指定（默认 <c>_verify_out/player/PianoTeacher3D.exe</c>）。
    /// </summary>
    public static class PlayerBaselineBuild
    {
        private const string LessonScene = "Assets/Scenes/PianoLesson.unity";

        public static void Build()
        {
            string output = Environment.GetEnvironmentVariable("PT3D_PLAYER_OUT");
            if (string.IsNullOrEmpty(output))
                output = Path.GetFullPath(Path.Combine("_verify_out", "player", "PianoTeacher3D.exe"));

            string dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(LessonScene))
            {
                Debug.LogError("[PT3D][Build] 找不到场景：" + LessonScene);
                EditorApplication.Exit(2);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { LessonScene },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[PT3D][Build] result={summary.result} errors={summary.totalErrors} " +
                      $"warnings={summary.totalWarnings} size={summary.totalSize} output={output}");

            if (summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
#endif
