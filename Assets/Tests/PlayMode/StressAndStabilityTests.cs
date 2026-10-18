using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 压力与长时稳定性测试（对应方案 <b>11.4 自动化与真实设备验证</b> 与审查 <b>6.2 相机验收</b>）。
    /// <para>
    /// 覆盖三条方案要求：
    /// ① 连续快速执行相机 / 角色 / 分手 / 速度切换的压力测试（<see cref="Stress_RapidSwitching"/>）；
    /// ② 定时 / 循环播放 10 分钟以上的稳定性测试（<see cref="Soak_PlaysContinuously"/>，10 分钟不可拖慢常规验证，故标 <c>[Explicit]</c>）；
    /// ③ 资源加载耗时与失败率采样（<see cref="ResourceLoad_Sampling"/>）。
    /// </para>
    /// <para>
    /// 所有写文件的动作都被 <c>try/catch</c> 包住：报告写入失败只 <c>Debug.LogWarning</c>，<b>不让测试失败</b>。
    /// </para>
    /// 运行示例（由协调者统一执行，本文件不自行运行 Unity）：
    /// <code>
    /// Unity.exe -batchmode -nographics -runTests -testPlatform PlayMode -testCategory Stress
    /// set PT3D_SOAK_SECONDS=600 &amp;&amp; Unity.exe -batchmode -nographics -runTests -testPlatform PlayMode -testCategory Soak
    /// </code>
    /// </summary>
    public class StressAndStabilityTests
    {
        /// <summary>快速切换压力测试的操作次数（方案 11.4「连续快速执行」）。</summary>
        private const int StressOperations = 200;

        /// <summary>手部「单帧最大位移」阈值（米）：与既有 HandMotionTests 保持一致，0.15 m = 15 cm。</summary>
        private const float MaxStepThreshold = 0.15f;

        /// <summary>长跑默认时长（秒）= 10 分钟，方案 11.4「定时/循环播放 10 分钟以上」。</summary>
        private const int DefaultSoakSeconds = 600;

        private LessonSessionController _session;
        private CameraModeController _camera;
        private AvatarManager _avatar;
        private HandIKController _handIK;
        private LessonTimeline _timeline;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            _session = Object.FindFirstObjectByType<LessonSessionController>();
            _camera = Object.FindFirstObjectByType<CameraModeController>();
            _avatar = Object.FindFirstObjectByType<AvatarManager>();
            _handIK = Object.FindFirstObjectByType<HandIKController>();
            _timeline = Object.FindFirstObjectByType<LessonTimeline>();

            Assert.IsNotNull(_session, "PianoLesson 场景应包含 LessonSessionController");
            Assert.IsNotNull(_camera, "PianoLesson 场景应包含 CameraModeController");
            Assert.IsNotNull(_avatar, "PianoLesson 场景应包含 AvatarManager");
            Assert.IsNotNull(_handIK, "PianoLesson 场景应包含 HandIKController");
            Assert.IsNotNull(_timeline, "PianoLesson 场景应包含 LessonTimeline（唯一时间源）");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        // ==================== 用例 1：连续快速切换压力（普通用例，默认运行，≤15 秒） ====================

        /// <summary>
        /// 播放中连续快速执行 ≥200 次随机切换（相机模式 / 角色 / 分手 / 速度 / 高亮 / 节拍器 / Seek / Play-Pause）。
        /// <para>
        /// 断言（方案 11.4、审查 6.2）：
        /// ① 全程不抛异常（协程内任何异常都会直接让用例失败）；
        /// ② 播放状态始终是 Playing/Paused（切换不得把播放打成 Stopped）；
        /// ③ <c>CurrentTime</c> 单调不减，仅在 Seek / 循环回退 / 曲终重播时允许回退；
        /// ④ 场景内关键组件数量仍为 1（防重复 AddComponent / 重复协程）；
        /// ⑤ 停止操作后 <c>CameraModeController.IsTransitioning</c> 在有限帧内收敛为 false（审查 6.2「无持续相机抖动」）；
        /// ⑥ <c>PoseIssueCount == 0</c> 且同一骨架下的 <c>MaxStepDistance &lt; 0.15 m</c>（无瞬移、无穿模）。
        /// </para>
        /// </summary>
        [UnityTest]
        [Category("Stress")]
        public IEnumerator Stress_RapidSwitching()
        {
            if (!_session.HasSong) Assert.Ignore("示例曲目未加载，压力测试需要可播放曲目");

            var rng = new System.Random(20261016); // 固定种子：失败可复现
            float duration = _timeline != null ? _timeline.TotalDuration : 0f;

            _session.Seek(0f);
            _session.Play();
            yield return null;

            float prevTime = _session.CurrentTime;
            int restarts = 0;

            for (int i = 0; i < StressOperations; i++)
            {
                bool mayRegress = PerformRandomOperation(_session, _avatar, _timeline, rng, out bool avatarSwitched);
                yield return null;

                // ② 播放状态：切换只改表现层，绝不允许把播放打成 Stopped
                var state = _session.PlaybackState;
                Assert.IsTrue(state == PlaybackState.Playing || state == PlaybackState.Paused,
                    $"第 {i + 1} 次操作后播放状态为 {state}（阈值口径：应为 Playing/Paused；切换不应停止播放）");

                // ③ 时间范围与单调性
                float now = _session.CurrentTime;
                Assert.GreaterOrEqual(now, -0.001f, $"第 {i + 1} 次操作后 CurrentTime={now:0.####} 不应为负");
                Assert.LessOrEqual(now, duration + 0.5f,
                    $"第 {i + 1} 次操作后 CurrentTime={now:0.###} 超出曲目时长 {duration:0.###} + 0.5 s");

                if (now < prevTime - 0.0005f)
                {
                    bool songEndRestart = prevTime >= duration - 0.2f && now <= 0.25f;
                    if (songEndRestart) restarts++;
                    bool allowed = mayRegress || _session.IsLooping || songEndRestart;
                    Assert.IsTrue(allowed,
                        $"第 {i + 1} 次操作后 CurrentTime 非法回退：{prevTime:0.###} → {now:0.###} s " +
                        "（口径：仅 Seek 操作、循环回退、曲终重播允许回退）");
                }
                prevTime = now;

                // ⑥ 每次操作后检查姿态越界（在重置指标之前，保证切换那一帧也被覆盖）
                Assert.AreEqual(0, _handIK.PoseIssueCount,
                    $"第 {i + 1} 次操作后出现 {_handIK.PoseIssueCount} 次手腕越界（穿模风险）：" +
                    string.Join(" | ", _handIK.PoseIssues));

                // 角色切换会让「另一套骨架」首次激活，其腕部就位位置与旧骨架的收敛位置不同，
                // 会产生一次非连续性跳变；这不是「同一骨架内的动作瞬移」，故在此边界重置动作指标。
                // （若同一骨架内也出现 >0.15 m 的跳变，静默窗口断言仍会失败并暴露问题。）
                if (avatarSwitched) _handIK.ResetMetrics();
            }

            // ⑤ 停止操作后相机过渡应在有限帧内收敛
            const int maxConvergeFrames = 180; // ≈3 秒 @60fps，远大于 0.35 s 的过渡时长
            int convergeFrames = 0;
            while (_camera.IsTransitioning && convergeFrames < maxConvergeFrames)
            {
                convergeFrames++;
                yield return null;
            }
            Assert.IsFalse(_camera.IsTransitioning,
                $"停止切换后 CameraModeController.IsTransitioning 应在 {maxConvergeFrames} 帧内收敛为 false（实际仍为 true，超过 {convergeFrames} 帧）");

            // ④ 组件数量：防重复 AddComponent / 重复协程
            Assert.AreEqual(1, Object.FindObjectsByType<LessonSessionController>(FindObjectsSortMode.None).Length,
                "压力测试后 LessonSessionController 数量应为 1");
            Assert.AreEqual(1, Object.FindObjectsByType<CameraModeController>(FindObjectsSortMode.None).Length,
                "压力测试后 CameraModeController 数量应为 1");
            Assert.AreEqual(1, Object.FindObjectsByType<AvatarManager>(FindObjectsSortMode.None).Length,
                "压力测试后 AvatarManager 数量应为 1");
            Assert.AreEqual(1, Object.FindObjectsByType<HandIKController>(FindObjectsSortMode.None).Length,
                "压力测试后 HandIKController 数量应为 1");

            // ⑥ 静默窗口：固定到单一骨架、双手、原速后连续播放，验证连续动作无瞬移
            _handIK.ResetMetrics();
            _session.ClearLoop();
            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetSpeed(1f);
            _session.SetCameraMode(CameraMode.Full);
            _session.Seek(0f);
            _session.Play();
            yield return null; // 首帧对齐不计入单帧位移（累加器首个样本不计算 delta）

            for (int i = 0; i < 120; i++) yield return null; // ≈2 秒真实演奏（批处理下帧率更高，帧数保证样本量）
            _session.Pause();

            var report = _session.HandMotion;
            Assert.IsNotNull(report, "应能获取手部动作报告");
            Assert.Greater(report.Samples, 30,
                $"静默窗口内手部采样数 {report.Samples} 过少（应 > 30），指标无意义");

            Debug.Log($"[PT3D-STRESS] 快速切换 {StressOperations} 次完成（含 {restarts} 次曲终重播）；" +
                      $"静默窗口 MaxStepDistance={report.MaxStepDistance * 1000f:0.#} mm，PoseIssueCount={_handIK.PoseIssueCount}");

            Assert.Less(report.MaxStepDistance, MaxStepThreshold,
                $"同一骨架静默播放期间单帧最大位移 {report.MaxStepDistance * 1000f:0.#} mm 应 < {MaxStepThreshold * 1000f:0} mm" +
                "（口径：静默窗口内无角色切换，逐帧腕部位移；阈值 15 cm 与 HandMotionTests 一致）");
            Assert.AreEqual(0, _handIK.PoseIssueCount,
                "静默播放期间不应出现姿态越界：" + string.Join(" | ", _handIK.PoseIssues));
        }

        /// <summary>
        /// 随机执行一个切换操作。返回 <c>true</c> 表示该操作**允许时间回退**（当前仅 Seek）。
        /// </summary>
        private static bool PerformRandomOperation(LessonSessionController session, AvatarManager avatar,
            LessonTimeline timeline, System.Random rng, out bool avatarSwitched)
        {
            avatarSwitched = false;
            switch (rng.Next(13))
            {
                case 0: session.CycleCameraMode(); return false;
                case 1: session.SetCameraMode((CameraMode)rng.Next(3)); return false;
                case 2: session.ResetCamera(); return false;
                case 3: session.ReturnToCameraPreset(); return false;
                case 4:
                    session.CycleAvatar();
                    avatarSwitched = true;
                    return false;
                case 5:
                {
                    int current = avatar != null ? avatar.CurrentIndex : 0;
                    session.SwitchAvatar((current + 1 + rng.Next(AvatarManager.AvatarCount - 1)) % AvatarManager.AvatarCount);
                    avatarSwitched = true;
                    return false;
                }
                case 6: session.CyclePracticeHand(); return false;
                case 7: session.CycleSpeed(); return false;
                case 8: session.CycleHighlightMode(); return false;
                case 9: session.ToggleMetronome(); return false;
                case 10:
                    session.SeekNormalized((float)rng.NextDouble());
                    return true; // Seek 允许回退
                case 11: session.PlayPause(); return false;
                default:
                    if (timeline == null) return false;
                    if (session.IsLooping) session.ClearLoop();
                    else session.SetLoop(0f, timeline.TotalDuration);
                    return false;
            }
        }

        // ==================== 用例 2：资源加载耗时与失败率采样（普通用例，默认运行，≤10 秒） ====================

        /// <summary>
        /// 对 <c>StreamingAssets/Songs</c> 下每个曲目计时执行 <see cref="LessonSessionController.LoadSong"/>。
        /// 断言：单曲加载 &lt; 5 秒（宽松上限）、失败率 = 0、加载后 <c>HasSong == true</c>；
        /// 并把每首曲目的耗时写入 <c>Docs/perf/sampling.md</c> 的「资源加载」小节。
        /// </summary>
        [UnityTest]
        [Category("Stress")]
        public IEnumerator ResourceLoad_Sampling()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Songs");
            string[] files = CollectMidiFiles(dir);
            if (files.Length == 0)
                Assert.Ignore("StreamingAssets/Songs 下没有 .mid/.midi 曲目，跳过资源加载采样");

            const float perSongLimitMs = 5000f; // 宽松上限：示例曲为毫秒级解析，5 秒仅用于捕获「卡死」
            var rows = new StringBuilder();
            int failures = 0;
            float worstMs = 0f;

            for (int i = 0; i < files.Length; i++)
            {
                string songPath = files[i];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _session.LoadSong(songPath);
                sw.Stop();
                yield return null; // 让加载事务与状态落定走完一帧

                float ms = (float)sw.Elapsed.TotalMilliseconds;
                bool ok = _session.HasSong;
                if (!ok) failures++;
                if (ms > worstMs) worstMs = ms;

                string name = Path.GetFileName(songPath);
                rows.Append("| ").Append(name).Append(" | ")
                    .Append(ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" | ").Append(ok ? "成功" : "失败").Append(" |\n");

                Assert.Less(ms, perSongLimitMs,
                    $"曲目「{name}」加载耗时 {ms:0.0} ms 应 < {perSongLimitMs:0} ms（阈值口径：捕获卡死/超长解析，而非性能目标）");
                Assert.IsTrue(ok, $"曲目「{name}」加载后 HasSong 应为 true（加载事务必须成功提交）");
            }

            Assert.AreEqual(0, failures,
                $"资源加载失败率应为 0（本次 {failures}/{files.Length} 失败）");

            var body = new StringBuilder();
            body.Append("### 资源加载采样（方案 11.4「资源加载耗时和失败率采样」）\n\n");
            body.Append(PerfReportWriter.DescribeEnvironment()).Append('\n');
            body.Append("- 曲目目录：`StreamingAssets/Songs`；曲目数：").Append(files.Length).Append('\n');
            body.Append("- 最慢单曲：").Append(worstMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" ms；失败数：").Append(failures).Append('\n');
            body.Append('\n');
            body.Append("| 曲目 | 加载耗时 (ms) | 结果 |\n|---|---|---|\n");
            body.Append(rows);
            body.Append('\n');
            body.Append("**口径说明**：耗时用 `System.Diagnostics.Stopwatch` 包裹 `LessonSessionController.LoadSong`，")
                .Append("包含校验 + 解析 + 一次性提交；示例曲为毫秒级，5 秒上限只用于捕获卡死，不代表目标设备上的加载性能。\n");
            PerfReportWriter.TryUpsertSection(PerfReportWriter.SamplingDocRelative, "resource-load", body.ToString(),
                PerfReportWriter.SamplingPreamble);

            Debug.Log($"[PT3D-STRESS] 资源加载采样：{files.Length} 首，最慢 {worstMs:0.0} ms，失败 {failures}");
        }

        /// <summary>列出目录下的 .mid / .midi 文件（目录缺失或不可读时返回空数组，不抛异常）。</summary>
        private static string[] CollectMidiFiles(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return new string[0];
                var all = Directory.GetFiles(dir);
                var result = new List<string>();
                for (int i = 0; i < all.Length; i++)
                {
                    string ext = Path.GetExtension(all[i]);
                    if (string.Equals(ext, ".mid", System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".midi", System.StringComparison.OrdinalIgnoreCase))
                        result.Add(all[i]);
                }
                result.Sort(System.StringComparer.OrdinalIgnoreCase);
                return result.ToArray();
            }
            catch (System.Exception)
            {
                return new string[0];
            }
        }

        // ==================== 用例 3：长时稳定性（Soak，[Explicit] 不拖慢常规验证） ====================

        /// <summary>
        /// 循环播放 <c>PT3D_SOAK_SECONDS</c> 秒（默认 600 = 10 分钟，方案 11.4），周期性做视角 / 角色 / 分手切换与
        /// <c>SeekNormalized</c>，末尾增加一段无切换测量窗口。
        /// <para>
        /// 断言口径：
        /// ① 期间 Error/Exception/Assert 级日志数 == 0；
        /// ② <c>PoseIssueCount == 0</c>；
        /// ③ 托管内存（<c>GC.GetTotalMemory(false)</c>）第 4 个四分位窗口均值相对第 2 个四分位窗口均值增长 &lt; 25%
        ///    （分母下限 1 MB，避免小数值抖动放大比值）；
        /// ④ 定时器漂移：无切换窗口内 <c>Σmax(0, ΔCurrentTime)</c> 相对 <c>ΣunscaledDeltaTime</c> 的最大偏差比例 &lt; 15%
        ///    （循环回退帧不计入实际进度）。
        /// </para>
        /// </summary>
        [UnityTest]
        [Explicit]
        [Category("Soak")]
        public IEnumerator Soak_PlaysContinuously()
        {
            int soakSeconds = ReadSoakSeconds();
            if (!_session.HasSong) Assert.Ignore("示例曲目未加载，长时稳定性测试需要可播放曲目");

            float duration = _timeline != null ? _timeline.TotalDuration : 0f;
            var rng = new System.Random(20261016);

            var errorLogs = new List<string>();
            Application.LogCallback onLog = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errorLogs.Add(condition);
            };

            var managedSamples = new List<long>();
            int switches = 0;
            long allocStart = 0L;

            Application.logMessageReceived += onLog;
            try
            {
                _session.SetSpeed(1f);
                _session.SetLoop(0f, duration);
                _session.Seek(0f);
                _session.Play();

                allocStart = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                managedSamples.Add(System.GC.GetTotalMemory(false));

                float t0 = Time.realtimeSinceStartup;
                const float switchInterval = 2f;
                float nextTick = t0 + switchInterval;

                while (Time.realtimeSinceStartup - t0 < soakSeconds)
                {
                    if (Time.realtimeSinceStartup >= nextTick)
                    {
                        nextTick += switchInterval;

                        PerformRandomOperation(_session, _avatar, _timeline, rng, out bool avatarSwitched);
                        switches++;

                        Assert.AreEqual(0, _handIK.PoseIssueCount,
                            $"长跑第 {switches} 次切换后出现 {_handIK.PoseIssueCount} 次手腕越界：" +
                            string.Join(" | ", _handIK.PoseIssues));
                        if (avatarSwitched) _handIK.ResetMetrics();

                        managedSamples.Add(System.GC.GetTotalMemory(false));
                    }
                    yield return null;
                }

                long allocEnd = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();

                // ---- 无切换测量窗口：定时器漂移（口径见方法注释） ----
                _session.ClearLoop();
                _session.SetLoop(0f, duration);
                _session.SetSpeed(1f);
                _session.Play();
                yield return null;

                const float driftWindowSeconds = 15f;
                float winStart = Time.realtimeSinceStartup;
                float winExpected = 0f;
                float winActual = 0f;
                float maxDriftRatio = 0f;
                float prevTime = _session.CurrentTime;
                float nextCheck = winStart + 2f;

                while (Time.realtimeSinceStartup - winStart < driftWindowSeconds)
                {
                    float dt = Time.unscaledDeltaTime;
                    float now = _session.CurrentTime;
                    float delta = now - prevTime;
                    if (delta > 0f) winActual += delta; // 循环回退帧（delta < 0）不计入实际进度
                    prevTime = now;
                    winExpected += dt;

                    if (Time.realtimeSinceStartup >= nextCheck)
                    {
                        nextCheck += 2f;
                        float ratio = winExpected > 0.5f ? Mathf.Abs(winActual - winExpected) / winExpected : 0f;
                        if (ratio > maxDriftRatio) maxDriftRatio = ratio;
                    }
                    yield return null;
                }

                _session.Pause();

                float q2 = WindowMean(managedSamples, 0.25f, 0.50f);
                float q4 = WindowMean(managedSamples, 0.75f, 1.00f);
                float growth = q2 > 0f ? (q4 - q2) / Mathf.Max(q2, 1048576f) : 0f; // 分母下限 1 MB

                // 只有采样点足够多（≈默认 600 s 的运行）才做内存趋势断言；
                // 极短的冒烟式运行（PT3D_SOAK_SECONDS=10 等）样本不足，改为提示而不产生误报。
                bool canAssertMemory = managedSamples.Count >= 8 && q2 > 0f;
                if (!canAssertMemory)
                    Debug.LogWarning($"[PT3D-SOAK] 内存采样点仅 {managedSamples.Count} 个，本次跳过内存趋势断言" +
                                     "（请用默认 PT3D_SOAK_SECONDS=600 运行以获得有效结论）。");

                var body = new StringBuilder();
                body.Append("### 长跑稳定性（Soak，方案 11.4「定时/循环播放 10 分钟以上的稳定性测试」）\n\n");
                body.Append(PerfReportWriter.DescribeEnvironment()).Append('\n');
                body.Append("- 目标时长：").Append(soakSeconds).Append(" s（环境变量 `PT3D_SOAK_SECONDS`，默认 ")
                    .Append(DefaultSoakSeconds).Append("）；另有 ")
                    .Append(driftWindowSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" s 无切换测量窗口\n");
                body.Append("- 循环切换次数：").Append(switches).Append('\n');
                body.Append("- 错误级日志（Error/Exception/Assert）：").Append(errorLogs.Count).Append('\n');
                body.Append("- 姿态越界（PoseIssueCount）：").Append(_handIK.PoseIssueCount).Append('\n');
                body.Append("- 托管内存（`GC.GetTotalMemory(false)`）窗口均值：第 2 四分位 ")
                    .Append((q2 / 1048576f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" MB → 第 4 四分位 ")
                    .Append((q4 / 1048576f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" MB，增长 ")
                    .Append((growth * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("%（阈值 < 25%）\n");
                body.Append("- 总分配内存（`Profiler.GetTotalAllocatedMemoryLong`）：")
                    .Append((allocStart / 1048576f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" MB → ")
                    .Append((allocEnd / 1048576f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" MB")
                    .Append(allocEnd == 0 ? "（读数 0：该环境不可用，不作为结论依据）" : string.Empty)
                    .Append('\n');
                body.Append("- 定时器漂移（无切换窗口）：最大偏差比例 ")
                    .Append((maxDriftRatio * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("%（阈值 < 15%）\n");
                body.Append('\n');
                body.Append("**口径说明**\n\n");
                body.Append("- 漂移 = |Σmax(0, ΔCurrentTime) − ΣunscaledDeltaTime| / ΣunscaledDeltaTime，")
                    .Append("在末尾无切换、原速、循环播放的窗口内采样（每 2 s 取一次检查点），循环回退帧不计入实际进度。\n");
                body.Append("- 内存增长对比第 2 与第 4 四分位窗口均值，避免被启动期缓存增长误判为泄漏；分母下限 1 MB。\n");
                body.Append("- 角色切换边界会重置手部动作指标（另一套骨架首次就位会产生一次非连续跳变），")
                    .Append("姿态越界仍在每次切换后即时检查。\n");
                body.Append("- 本节数值为本机批处理环境实测，**不代表真实设备体验**（方案 0 节 / 审查 0.1）。\n");
                PerfReportWriter.TryUpsertSection(PerfReportWriter.SamplingDocRelative, "soak", body.ToString(),
                    PerfReportWriter.SamplingPreamble);

                Debug.Log($"[PT3D-SOAK] {soakSeconds} s 完成：切换 {switches} 次，错误日志 {errorLogs.Count}，" +
                          $"姿态越界 {_handIK.PoseIssueCount}，托管内存增长 {growth * 100f:0.0}%，漂移 {maxDriftRatio * 100f:0.0}%");

                // ---- 断言 ----
                Assert.AreEqual(0, errorLogs.Count,
                    $"长跑期间不应出现错误级日志（共 {errorLogs.Count} 条），前几条：{FirstFew(errorLogs)}");
                Assert.AreEqual(0, _handIK.PoseIssueCount,
                    $"长跑期间手部不应越界（共 {_handIK.PoseIssueCount} 次）：{string.Join(" | ", _handIK.PoseIssues)}");
                if (canAssertMemory)
                {
                    Assert.Less(growth, 0.25f,
                        $"后半段托管内存相对前半段增长 {growth * 100f:0.0}% 应 < 25%" +
                        "（口径：第 4 四分位窗口均值 vs 第 2 四分位窗口均值，分母下限 1 MB）");
                }
                Assert.Less(maxDriftRatio, 0.15f,
                    $"无切换窗口内定时器漂移 {maxDriftRatio * 100f:0.0}% 应 < 15%（口径：Σmax(0,ΔCurrentTime) 相对 ΣunscaledDeltaTime 的最大偏差比例）");
            }
            finally
            {
                Application.logMessageReceived -= onLog;
                if (_session != null) _session.ClearLoop();
            }
        }

        /// <summary>读取长跑时长：环境变量 <c>PT3D_SOAK_SECONDS</c>（默认 600，clamp 到 [10, 86400]）。</summary>
        private static int ReadSoakSeconds()
        {
            try
            {
                string raw = System.Environment.GetEnvironmentVariable("PT3D_SOAK_SECONDS");
                if (int.TryParse(raw, out int seconds) && seconds > 0)
                    return Mathf.Clamp(seconds, 10, 24 * 3600);
            }
            catch (System.Exception)
            {
                // 读取环境变量失败 → 使用默认值
            }
            return DefaultSoakSeconds;
        }

        /// <summary>取某个比例区间的均值（按采样序号切窗，<paramref name="from"/> / <paramref name="to"/> ∈ [0,1]）。</summary>
        private static float WindowMean(List<long> values, float from, float to)
        {
            if (values.Count == 0) return 0f;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(values.Count * from), 0, values.Count - 1);
            int i1 = Mathf.Clamp(Mathf.CeilToInt(values.Count * to) - 1, i0, values.Count - 1);
            long sum = 0L;
            int n = 0;
            for (int i = i0; i <= i1; i++)
            {
                sum += values[i];
                n++;
            }
            return n > 0 ? sum / (float)n : 0f;
        }

        private static string FirstFew(List<string> items)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < 3; i++)
            {
                if (i > 0) sb.Append(" ／ ");
                sb.Append(items[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 发布前环境矩阵自检（方案 11.4 / 15 发布前检查清单）。
    /// <para>
    /// <b>这不是正确性测试</b>：它只<b>记录</b>运行环境的关键属性，把能覆盖到的项写进
    /// <c>Docs/perf/environments.md</c>，覆盖不到的项如实保留「待测」。
    /// 因此它<b>不会因为环境不满足而失败</b>，只对必须成立的不变量做断言（分辨率 / 版本非空）。
    /// </para>
    /// </summary>
    public class EnvironmentMatrixChecklistTests
    {
        [Test]
        [Category("Stress")]
        public void Environment_Matrix_Checklist()
        {
            string dataPath = SafeDataPath();
            bool pathHasChinese = ContainsNonAscii(dataPath);
            bool pathHasSpace = !string.IsNullOrEmpty(dataPath) && dataPath.Contains(" ");
            string admin = DescribeAdministrator();
            string reach = DescribeReachability();
            bool nographics = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

            var sb = new StringBuilder();
            sb.Append("### 本机自检（由 Environment_Matrix_Checklist 自动写入）\n\n");
            sb.Append(PerfReportWriter.DescribeEnvironment()).Append('\n');
            sb.Append("| 矩阵项 | 期望 | 本机实测 | 结论 |\n|---|---|---|---|\n");
            sb.Append("| 路径含中文 | 可正常加载/导入 | `").Append(dataPath).Append("` → ")
                .Append(pathHasChinese ? "含中文" : "不含中文").Append(" | ")
                .Append(pathHasChinese ? "已覆盖（本次运行路径含中文）" : "**待测**（本次运行路径不含中文）").Append(" |\n");
            sb.Append("| 路径含空格 | 可正常加载/导入 | ")
                .Append(pathHasSpace ? "含空格" : "不含空格").Append(" | ")
                .Append(pathHasSpace ? "已覆盖（本次运行路径含空格）" : "**待测**（本次运行路径不含空格）").Append(" |\n");
            sb.Append("| 非管理员权限 | 全部功能可用 | ").Append(admin).Append(" | ")
                .Append(admin == "否（标准用户）" ? "已覆盖（本次为非管理员运行）" : "**待测**（需以标准用户身份复测）").Append(" |\n");
            sb.Append("| 无网络环境 | 全部功能可用（本地优先） | 网络可达性：").Append(reach).Append(" | ")
                .Append(reach == "不可达" ? "已覆盖（本次为无网络/弱网）" : "**待测**（需断网复测）").Append(" |\n");
            sb.Append("| 不同 DPI / 缩放 | 布局可操作、不遮挡演奏区 | 屏幕 ").Append(Screen.width).Append('×')
                .Append(Screen.height).Append(" @ ")
                .Append(Screen.dpi.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" dpi；nographics=").Append(nographics ? "是" : "否").Append(" | ")
                .Append(nographics ? "**待测**（无图形设备，无法核对视觉布局）" : "部分覆盖（需按 100%/125%/150% 缩放各测一次）").Append(" |\n");
            sb.Append("| 窄窗口（1280×720） | 主画面未被控制栏过度覆盖 | Screen=").Append(Screen.width).Append('×')
                .Append(Screen.height).Append(" | ")
                .Append(Screen.width <= 1280 ? "已覆盖（当前窗口已 ≤1280 宽）" : "**待测**（需在 1280×720 窗口下复测）").Append(" |\n");
            sb.Append('\n');
            sb.Append("> 本表只记录事实：**任何未覆盖的项都保持「待测」，不得写成已通过**（方案 0 节 / 审查 0.1）；")
                .Append("覆盖状态来自本次运行环境，不代表其他机器。\n");

            PerfReportWriter.TryUpsertSection(PerfReportWriter.EnvironmentsDocRelative, "local-check", sb.ToString(),
                PerfReportWriter.EnvironmentsPreamble);

            Debug.Log($"[PT3D-ENV] dataPath={dataPath}；中文={(pathHasChinese ? "是" : "否")}；空格={(pathHasSpace ? "是" : "否")}；" +
                      $"管理员={admin}；网络={reach}；{Screen.width}×{Screen.height}@{Screen.dpi:0.#}dpi；nographics={nographics}");

            // 只断言必须成立的不变量（环境不满足不会 fail，只记录）
            Assert.IsFalse(string.IsNullOrEmpty(Application.unityVersion),
                "Unity 版本不应为空（环境自检必须能读到版本）");
            Assert.Greater(Screen.width, 0, "屏幕宽度应大于 0");
            Assert.Greater(Screen.height, 0, "屏幕高度应大于 0");
        }

        private static string SafeDataPath()
        {
            try { return Application.dataPath ?? string.Empty; }
            catch (System.Exception) { return string.Empty; }
        }

        /// <summary>路径是否含非 ASCII 字符（用于判断「路径含中文」）。</summary>
        private static bool ContainsNonAscii(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] > 0x7F) return true;
            }
            return false;
        }

        /// <summary>网络可达性（弱网/无网一律按「不可达」处理，方案 11.4）。</summary>
        private static string DescribeReachability()
        {
            switch (Application.internetReachability)
            {
                case NetworkReachability.ReachableViaLocalAreaNetwork:
                    return "可达（局域网）";
                case NetworkReachability.ReachableViaCarrierDataNetwork:
                    return "可达（移动数据）";
                default:
                    return "不可达";
            }
        }

        /// <summary>
        /// 是否以管理员身份运行。用反射调用 <c>System.Security.Principal</c>，
        /// 避免在 .NET Standard 配置下引入编译期程序集依赖；任何失败都返回「未知」而不抛异常。
        /// </summary>
        private static string DescribeAdministrator()
        {
            try
            {
                const string assembly = "System.Security.Principal.Windows";
                System.Type identityType =
                    System.Type.GetType("System.Security.Principal.WindowsIdentity, " + assembly, false)
                    ?? System.Type.GetType("System.Security.Principal.WindowsIdentity", false);
                if (identityType == null) return "未知（运行时未提供 WindowsIdentity）";

                System.Reflection.MethodInfo getCurrent = identityType.GetMethod("GetCurrent", System.Type.EmptyTypes);
                object identity = getCurrent != null ? getCurrent.Invoke(null, null) : null;
                if (identity == null) return "未知（无法获取当前身份）";

                System.Type principalType =
                    System.Type.GetType("System.Security.Principal.WindowsPrincipal, " + assembly, false)
                    ?? System.Type.GetType("System.Security.Principal.WindowsPrincipal", false);
                if (principalType == null) return "未知（运行时未提供 WindowsPrincipal）";

                System.Reflection.ConstructorInfo ctor = principalType.GetConstructor(new[] { identityType });
                object principal = ctor != null ? ctor.Invoke(new[] { identity }) : null;
                if (principal == null) return "未知（无法构造 WindowsPrincipal）";

                System.Type roleType =
                    System.Type.GetType("System.Security.Principal.WindowsBuiltInRole, " + assembly, false)
                    ?? System.Type.GetType("System.Security.Principal.WindowsBuiltInRole", false);
                if (roleType == null) return "未知（运行时未提供 WindowsBuiltInRole）";

                System.Reflection.MethodInfo isInRole = principalType.GetMethod("IsInRole", new[] { roleType });
                if (isInRole == null) return "未知（无法调用 IsInRole）";

                const int builtInRoleAdministrator = 544; // WindowsBuiltInRole.Administrator
                object adminRole = System.Enum.ToObject(roleType, builtInRoleAdministrator);
                object result = isInRole.Invoke(principal, new[] { adminRole });
                if (result is bool isAdmin) return isAdmin ? "是（管理员）" : "否（标准用户）";
                return "未知（IsInRole 返回非布尔值）";
            }
            catch (System.Exception)
            {
                return "未知（检测异常）";
            }
        }
    }
}
