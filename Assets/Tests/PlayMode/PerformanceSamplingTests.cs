using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 性能采样测试（对应方案 <b>7.2 性能预算与排查顺序</b> / <b>11.3 建议验收门槛</b>）。
    /// <para>
    /// 方案 7.2 要求「记录 CPU 主线程、GPU、GC Alloc、音频回调、内存、帧时间百分位与帧率；
    /// 只记录平均 FPS 不足以发现卡顿」，因此本测试按帧采集 <c>Time.unscaledDeltaTime</c>，
    /// 输出 <b>p50 / p95 / p99 / 最大帧时间 / 平均 FPS / 1% low FPS</b>，并统计采样期间的分配增量。
    /// </para>
    /// <para>
    /// <b>两个用例都是 <c>[Explicit]</c></b>：它们需要 8 秒以上真实时间，不应拖慢常规验证；
    /// 通过 <c>-testCategory Perf</c> 选择分类即可运行（Unity 官方说明：<c>[Explicit]</c> 用例
    /// 在「按分类选择」时会被执行）。
    /// </para>
    /// <para>
    /// <b>诚实性（方案 0 节 / 审查 0.1）</b>：批处理（<c>-batchmode</c>）与
    /// <c>-nographics</c>（无图形设备）下的数据<b>不含渲染负载</b>，不能代表真实设备体验。
    /// 有图形设备时本测试只告警、不失败，真实数值必须在目标电脑上另行采集后定稿（方案 11.4）。
    /// </para>
    /// 运行示例（由协调者统一执行，本文件不自行运行 Unity）：
    /// <code>Unity.exe -batchmode -nographics -runTests -testPlatform PlayMode -testCategory Perf</code>
    /// </summary>
    public class PerformanceSamplingTests
    {
        /// <summary>单次采样的目标时长（秒）；方案 7.2 要求按帧分布判断卡顿，需足够样本量。</summary>
        private const float SampleSeconds = 8f;

        /// <summary>预热帧数：跳过场景装配 / JIT / 首帧着色器编译带来的异常帧。</summary>
        private const int WarmupFrames = 30;

        /// <summary>
        /// 无图形设备下的 p95 帧时间上限（毫秒）= 33.3 ms ≈ 30 FPS。
        /// 口径：这是「逻辑层不得长时间阻塞」的宽松下限，不是体验目标；有 GPU 时不做失败判定。
        /// </summary>
        private const float NographicsP95LimitMs = 33.3f;

        /// <summary>
        /// 切换负载下的 p95 帧时间上限（毫秒）：切换本身允许更多抖动，放宽到 50 ms（≈20 FPS）。
        /// 口径：仍为硬上限（非「恒真」），用于发现「一切换就长时间卡住」的回归。
        /// </summary>
        private const float SwitchingP95LimitMs = 50f;

        private LessonSessionController _session;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            _session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(_session, "PianoLesson 场景应包含 LessonSessionController");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Pause();
            yield return null;
        }

        // ==================== 用例 1：纯播放帧时间 ====================

        /// <summary>
        /// 播放 ≥ 8 秒，逐帧记录帧时间并输出百分位分布（方案 7.2）。
        /// 断言：无图形设备下 p95 帧时间 &lt; 33.3 ms；有图形设备时只告警不失败（避免机器差异导致 CI flaky）。
        /// </summary>
        [UnityTest]
        [Explicit]
        [Category("Perf")]
        public IEnumerator SampleFrameTiming()
        {
            if (!_session.HasSong) Assert.Ignore("示例曲目未加载，跳过帧时间采样");

            _session.SetCameraMode(CameraMode.Full);
            _session.Seek(0f);
            _session.Play();

            for (int i = 0; i < WarmupFrames; i++) yield return null;

            var frameTimes = new List<float>(1024);
            long allocStart = Profiler.GetTotalAllocatedMemoryLong();
            long allocPeak = allocStart;
            long managedStart = System.GC.GetTotalMemory(false);

            float elapsed = 0f;
            while (elapsed < SampleSeconds)
            {
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                frameTimes.Add(dt);

                long alloc = Profiler.GetTotalAllocatedMemoryLong();
                if (alloc > allocPeak) allocPeak = alloc;
                yield return null;
            }

            long allocEnd = Profiler.GetTotalAllocatedMemoryLong();
            long managedEnd = System.GC.GetTotalMemory(false);
            _session.Pause();

            var stats = Compute(frameTimes);
            bool nographics = IsNographics();

            string body = BuildFrameTimingSection("帧时间采样（纯播放）", stats,
                allocStart, allocEnd, allocPeak, managedStart, managedEnd, nographics, "播放 + 全景视角，无额外切换");
            PerfReportWriter.TryUpsertSection(PerfReportWriter.SamplingDocRelative, "frame-timing", body,
                PerfReportWriter.SamplingPreamble);

            Debug.Log($"[PT3D-PERF] 纯播放帧时间：帧数 {stats.Count}，均值 {stats.Mean * 1000f:0.#} ms，" +
                      $"p50 {stats.P50 * 1000f:0.#} / p95 {stats.P95 * 1000f:0.#} / p99 {stats.P99 * 1000f:0.#} ms，" +
                      $"最大 {stats.Max * 1000f:0.#} ms，平均 {stats.MeanFps:0.#} FPS，1% low {stats.OnePercentLowFps:0.#} FPS");

            Assert.Greater(stats.Count, 60,
                $"采样帧数 {stats.Count} 过少（8 秒内应至少 60 帧），统计无意义");

            if (nographics)
            {
                Assert.Less(stats.P95 * 1000f, NographicsP95LimitMs,
                    $"无图形设备下 p95 帧时间 {stats.P95 * 1000f:0.#} ms 应 < {NographicsP95LimitMs:0.#} ms" +
                    "（口径：逻辑层不得长时间阻塞；这是宽松下限，不是体验目标）");
            }
            else
            {
                Debug.LogWarning($"[PT3D-PERF] 检测到图形设备（{SystemInfo.graphicsDeviceType}），" +
                    $"p95={stats.P95 * 1000f:0.#} ms 仅供参考、不作为失败依据；" +
                    "真实体验数值必须在目标电脑上采集（方案 11.4）。");
            }
        }

        // ==================== 用例 2：切换负载下的帧时间 ====================

        /// <summary>
        /// 播放 + 每 20 帧切换一次视角 / 角色 / 速度，输出 p50/p95/p99，
        /// 用于发现「切换时掉帧」（方案 7.2「连续相机切换」、11.3「视角/角色切换不明显卡顿」）。
        /// </summary>
        [UnityTest]
        [Explicit]
        [Category("Perf")]
        public IEnumerator SampleFrameTiming_UnderSwitchingLoad()
        {
            if (!_session.HasSong) Assert.Ignore("示例曲目未加载，跳过切换负载采样");

            _session.SetCameraMode(CameraMode.Full);
            _session.Seek(0f);
            _session.Play();

            for (int i = 0; i < WarmupFrames; i++) yield return null;

            var frameTimes = new List<float>(1024);
            long allocStart = Profiler.GetTotalAllocatedMemoryLong();
            long allocPeak = allocStart;
            long managedStart = System.GC.GetTotalMemory(false);

            const int switchEveryFrames = 20;
            int frameIndex = 0;
            int switchCount = 0;
            float elapsed = 0f;

            while (elapsed < SampleSeconds)
            {
                if (frameIndex % switchEveryFrames == 0)
                {
                    // 轮换视角 / 角色 / 速度，模拟「用户连续快速切换」
                    switch ((frameIndex / switchEveryFrames) % 3)
                    {
                        case 0: _session.CycleCameraMode(); break;
                        case 1: _session.CycleAvatar(); break;
                        default: _session.CycleSpeed(); break;
                    }
                    switchCount++;
                }
                frameIndex++;

                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                frameTimes.Add(dt);

                long alloc = Profiler.GetTotalAllocatedMemoryLong();
                if (alloc > allocPeak) allocPeak = alloc;
                yield return null;
            }

            long allocEnd = Profiler.GetTotalAllocatedMemoryLong();
            long managedEnd = System.GC.GetTotalMemory(false);
            var playbackState = _session.PlaybackState;
            _session.Pause();

            var stats = Compute(frameTimes);
            bool nographics = IsNographics();

            string body = BuildFrameTimingSection("切换负载（每 20 帧切换视角/角色/速度）", stats,
                allocStart, allocEnd, allocPeak, managedStart, managedEnd, nographics,
                $"播放中每 20 帧切换一次，共触发 {switchCount} 次切换");
            PerfReportWriter.TryUpsertSection(PerfReportWriter.SamplingDocRelative, "switching-load", body,
                PerfReportWriter.SamplingPreamble);

            Debug.Log($"[PT3D-PERF] 切换负载帧时间：帧数 {stats.Count}，均值 {stats.Mean * 1000f:0.#} ms，" +
                      $"p50 {stats.P50 * 1000f:0.#} / p95 {stats.P95 * 1000f:0.#} / p99 {stats.P99 * 1000f:0.#} ms，" +
                      $"最大 {stats.Max * 1000f:0.#} ms，平均 {stats.MeanFps:0.#} FPS，1% low {stats.OnePercentLowFps:0.#} FPS");

            // 方案 11.3 / 审查 6.2：切换不得打断播放
            Assert.IsTrue(playbackState == PlaybackState.Playing || playbackState == PlaybackState.Paused,
                $"切换负载期间播放状态异常：{playbackState}（应为 Playing/Paused，切换不应把播放打成 Stopped）");
            Assert.Greater(stats.Count, 60, $"采样帧数 {stats.Count} 过少（8 秒内应至少 60 帧），统计无意义");

            if (nographics)
            {
                Assert.Less(stats.P95 * 1000f, SwitchingP95LimitMs,
                    $"无图形设备下切换负载 p95 帧时间 {stats.P95 * 1000f:0.#} ms 应 < {SwitchingP95LimitMs:0.#} ms" +
                    "（口径：切换允许更多抖动，但仍不得出现长时间阻塞）");
            }
            else
            {
                Debug.LogWarning($"[PT3D-PERF] 检测到图形设备，切换负载 p95={stats.P95 * 1000f:0.#} ms 仅供参考、不作为失败依据。");
            }
        }

        // ==================== 统计与报告 ====================

        /// <summary>帧时间统计结果。</summary>
        private sealed class FrameStats
        {
            public int Count;
            public float Mean;
            public float P50;
            public float P95;
            public float P99;
            public float Max;
            public float MeanFps;
            public float OnePercentLowFps;
        }

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>线性插值百分位（<paramref name="sorted"/> 必须升序）。</summary>
        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return 0f;
            if (sorted.Count == 1) return sorted[0];
            float rank = Mathf.Clamp01(p) * (sorted.Count - 1);
            int lo = Mathf.FloorToInt(rank);
            int hi = Mathf.CeilToInt(rank);
            return Mathf.Lerp(sorted[lo], sorted[hi], rank - lo);
        }

        private static FrameStats Compute(List<float> frameTimes)
        {
            var stats = new FrameStats { Count = frameTimes.Count };
            if (frameTimes.Count == 0) return stats;

            float sum = 0f;
            for (int i = 0; i < frameTimes.Count; i++) sum += frameTimes[i];
            stats.Mean = sum / frameTimes.Count;
            stats.MeanFps = stats.Mean > 1e-6f ? 1f / stats.Mean : 0f;

            var sorted = new List<float>(frameTimes);
            sorted.Sort();
            stats.P50 = Percentile(sorted, 0.50f);
            stats.P95 = Percentile(sorted, 0.95f);
            stats.P99 = Percentile(sorted, 0.99f);
            stats.Max = sorted[sorted.Count - 1];

            // 1% low FPS：取最慢的 1% 帧（至少 1 帧）的平均帧时间的倒数。
            int tail = Mathf.Max(1, Mathf.CeilToInt(sorted.Count * 0.01f));
            float tailSum = 0f;
            for (int i = 0; i < tail; i++) tailSum += sorted[sorted.Count - 1 - i];
            float tailMean = tailSum / tail;
            stats.OnePercentLowFps = tailMean > 1e-6f ? 1f / tailMean : 0f;
            return stats;
        }

        /// <summary>是否为无图形设备环境（<c>-nographics</c>）。</summary>
        internal static bool IsNographics()
            => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        /// <summary>生成一节帧时间报告（Markdown），含时间戳、环境信息与口径说明。</summary>
        private static string BuildFrameTimingSection(string title, FrameStats stats,
            long allocStart, long allocEnd, long allocPeak,
            long managedStart, long managedEnd, bool nographics, string scenario)
        {
            var sb = new StringBuilder();
            sb.Append("### ").Append(title).Append('\n').Append('\n');
            sb.Append(PerfReportWriter.DescribeEnvironment()).Append('\n');
            sb.Append("- 场景口径：").Append(scenario).Append('\n');
            sb.Append('\n');
            sb.Append("| 指标 | 数值 |\n|---|---|\n");
            sb.Append("| 采样帧数 | ").Append(stats.Count).Append(" |\n");
            sb.Append("| 平均帧时间 | ").Append((stats.Mean * 1000f).ToString("0.#", Inv)).Append(" ms |\n");
            sb.Append("| p50 帧时间 | ").Append((stats.P50 * 1000f).ToString("0.#", Inv)).Append(" ms |\n");
            sb.Append("| p95 帧时间 | ").Append((stats.P95 * 1000f).ToString("0.#", Inv)).Append(" ms |\n");
            sb.Append("| p99 帧时间 | ").Append((stats.P99 * 1000f).ToString("0.#", Inv)).Append(" ms |\n");
            sb.Append("| 最大帧时间 | ").Append((stats.Max * 1000f).ToString("0.#", Inv)).Append(" ms |\n");
            sb.Append("| 平均 FPS | ").Append(stats.MeanFps.ToString("0.#", Inv)).Append(" |\n");
            sb.Append("| 1% low FPS | ").Append(stats.OnePercentLowFps.ToString("0.#", Inv)).Append(" |\n");
            sb.Append("| 托管内存增量（GC.GetTotalMemory(false)） | ")
              .Append(((managedEnd - managedStart) / 1048576f).ToString("0.00", Inv)).Append(" MB |\n");
            sb.Append("| 总分配内存（Profiler.GetTotalAllocatedMemoryLong） | ")
              .Append((allocStart / 1048576f).ToString("0.00", Inv)).Append(" MB → ")
              .Append((allocEnd / 1048576f).ToString("0.00", Inv)).Append(" MB（峰值 ")
              .Append((allocPeak / 1048576f).ToString("0.00", Inv)).Append(" MB） |\n");
            sb.Append('\n');
            sb.Append("**口径说明**\n\n");
            sb.Append("- 帧时间取 `Time.unscaledDeltaTime`（不受 `Time.timeScale` 影响），逐帧采样，前 ")
              .Append(WarmupFrames).Append(" 帧为预热、不计入统计。\n");
            sb.Append("- p50/p95/p99 为线性插值百分位；1% low FPS = 最慢 1% 帧的平均帧时间的倒数。\n");
            sb.Append("- `Profiler.GetTotalAllocatedMemoryLong()` 在非开发构建 / 未启用 Profiler 时可能返回 0，")
              .Append("此时该行数值不可信（本次读数为 ")
              .Append(allocEnd == 0 ? "0，判定为「该环境不可用」" : "非 0，可用").Append("）。\n");
            if (nographics)
            {
                sb.Append("- 本次运行**无图形设备**：帧时间不含渲染负载，**不代表真实设备体验**。\n");
            }
            else
            {
                sb.Append("- 本次运行**有图形设备**：数值受本机硬件与场景影响，仍**不足以代表目标设备体验**，")
                  .Append("需在真实目标电脑上复测（方案 11.4）。\n");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 采样报告写入器：把结果按「分节」写入 <c>Docs/perf/</c> 下的 Markdown。
    /// <para>
    /// 每个小节用 <c>&lt;!-- SECTION:xxx:BEGIN --&gt; / END</c> 包裹，写入时<b>只覆盖自己的小节</b>，
    /// 因此多个测试先后运行不会互相清空内容。写文件失败只 <c>Debug.LogWarning</c>，
    /// <b>绝不让测试失败</b>（报告是产物，不是被测行为）。
    /// </para>
    /// </summary>
    internal static class PerfReportWriter
    {
        public const string SamplingDocRelative = "Docs/perf/sampling.md";
        public const string EnvironmentsDocRelative = "Docs/perf/environments.md";

        /// <summary>sampling.md 新建时的固定前言（含诚实性声明，方案 0 节 / 审查 0.1）。</summary>
        public const string SamplingPreamble =
            "# 性能采样报告（Docs/perf/sampling.md）\n\n" +
            "> 本文件由 PlayMode 性能 / 稳定性测试**自动写入或覆盖对应小节**，请勿手工编辑小节内容以外的结构。\n" +
            ">\n" +
            "> **诚实性声明（方案 0 节 / 审查 0.1）**：批处理（`-batchmode`）与无图形设备（`-nographics`）下采集的\n" +
            "> 帧率 / 帧时间**不含渲染负载，不能代表真实设备体验**；本节所有数值都是「本机、当前构建、当前场景」的实测值，\n" +
            "> 任何未在目标电脑上采集的数据都不得写成「已达标」（方案 11.3 / 11.4 要求真机测量后定稿）。\n";

        public const string EnvironmentsPreamble =
            "# 发布前环境矩阵记录（Docs/perf/environments.md）\n\n" +
            "> 对应方案 **11.4**「发布前在无网络环境、文件路径含中文/空格、非管理员权限、不同 DPI 的 Windows 环境下测试」\n" +
            "> 与 **15 发布前检查清单**「高 DPI、窄窗口、中文/空格路径和无网络环境测试通过」。\n" +
            ">\n" +
            "> 本文件由测试运行时自动更新「本机自检」小节；无法在本机覆盖的项必须如实保留「待测」，\n" +
            "> **不得把未实测的环境写成已通过**。\n";

        /// <summary>项目根目录（<c>Assets</c> 的上一级）。</summary>
        public static string ProjectRoot
        {
            get
            {
                try { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
                catch (System.Exception) { return null; }
            }
        }

        /// <summary>测试环境一行行描述（时间戳 + Unity/图形设备/分辨率/批处理等）。</summary>
        public static string DescribeEnvironment()
        {
            var sb = new StringBuilder();
            sb.Append("- 时间：").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv)).Append('\n');
            sb.Append("- Unity：").Append(Application.unityVersion).Append('\n');
            sb.Append("- 图形设备：").Append(SystemInfo.graphicsDeviceType)
              .Append("（").Append(SystemInfo.graphicsDeviceName).Append("）\n");
            sb.Append("- 分辨率：").Append(Screen.width).Append('×').Append(Screen.height)
              .Append(" @ ").Append(Screen.dpi.ToString("0.#", Inv)).Append(" dpi\n");
            sb.Append("- 是否批处理（-batchmode）：").Append(Application.isBatchMode ? "是" : "否")
              .Append("；是否 -nographics：").Append(PerformanceSamplingTests.IsNographics() ? "是" : "否").Append('\n');
            sb.Append("- 平台 / 系统：").Append(Application.platform).Append(" / ")
              .Append(SystemInfo.operatingSystem).Append('\n');
            return sb.ToString();
        }

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>
        /// 写入 / 覆盖某个小节。<paramref name="sectionId"/> 决定小节位置，
        /// 文件不存在时用 <paramref name="preambleIfNew"/> 作为文件前言。
        /// 任何异常都只记 Warning，不影响测试结果。
        /// </summary>
        public static void TryUpsertSection(string relativePath, string sectionId, string markdown, string preambleIfNew)
        {
            try
            {
                string root = ProjectRoot;
                if (string.IsNullOrEmpty(root)) return;

                string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string begin = $"<!-- SECTION:{sectionId}:BEGIN -->";
                string end = $"<!-- SECTION:{sectionId}:END -->";
                string block = begin + "\n" + markdown.TrimEnd() + "\n" + end;

                string existing = File.Exists(path)
                    ? File.ReadAllText(path, Encoding.UTF8)
                    : (preambleIfNew ?? string.Empty);

                int b = existing.IndexOf(begin, System.StringComparison.Ordinal);
                int e = existing.IndexOf(end, System.StringComparison.Ordinal);

                string updated;
                if (b >= 0 && e > b)
                {
                    updated = existing.Substring(0, b) + block + existing.Substring(e + end.Length);
                }
                else
                {
                    updated = existing.TrimEnd() + "\n\n" + block + "\n";
                }

                File.WriteAllText(path, updated, new UTF8Encoding(false));
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PT3D-PERF] 写入报告失败（不影响测试结果）：{relativePath} / {ex.GetType().Name}");
            }
        }
    }
}
