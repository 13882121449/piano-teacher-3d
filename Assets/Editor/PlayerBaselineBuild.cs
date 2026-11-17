#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
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

        public static void Build() => BuildWindows(new[] { LessonScene }, BuildOptions.Development, Path.Combine("_verify_out", "player"));

        /// <summary>构建包含主菜单与课程的 Windows 使用版。</summary>
        public static void BuildRelease()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            BuildWindows(scenes, BuildOptions.None, Path.Combine("Builds", "Windows", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
        }

        private static void BuildWindows(string[] scenes, BuildOptions options, string defaultFolder)
        {
            string output = Environment.GetEnvironmentVariable("PT3D_PLAYER_OUT");
            if (string.IsNullOrEmpty(output))
                output = Path.GetFullPath(Path.Combine(defaultFolder, "PianoTeacher3D.exe"));

            string dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (scenes.Length == 0 || scenes.Any(scene => !File.Exists(scene)))
            {
                Debug.LogError("[PT3D][Build] 构建场景为空或缺失：" + string.Join(", ", scenes));
                EditorApplication.Exit(2);
                return;
            }

            var build = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = options
            };

            BuildReport report = BuildPipeline.BuildPlayer(build);
            BuildSummary summary = report.summary;

            Debug.Log($"[PT3D][Build] result={summary.result} errors={summary.totalErrors} " +
                      $"warnings={summary.totalWarnings} size={summary.totalSize} output={output}");

            if (summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
#endif
