using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 手部接触评测基线（手部接触与视觉呈现优化方案 · 批次 1.2 / 1.4）。
    /// <para>
    /// 口径：<c>Assets/StreamingAssets/Songs/bench/</c> 下的三首自制曲目（黑键 / 和弦 / 快速音阶+大跨度），
    /// 每首<b>整曲</b>播放（不是固定 3 s 窗口），逐首输出 <see cref="HandMotionReport"/>。
    /// 曲目由 <c>Tools/make_bench_midi.py</c> 生成，可复现、无版权风险。
    /// </para>
    /// <para>
    /// 本类只做「测量 + 分组门禁」；任何 IK 参数调整都在批次 2 单独提交，回滚时不带走评测。
    /// </para>
    /// </summary>
    public class HandBenchmarkTests
    {
        private const string BenchFolder = "bench";

        /// <summary>分组门禁：各组命中率不得低于实测基线 − 3 个百分点（方案 1.4）。</summary>
        private const float GroupGateMargin = 0.03f;

        // 实测基线（2026-10-19，本机编辑器 -batchmode；曲目见 bench/）。数值取自
        // Bench_ThreeSongs_LogFullReport 的 [BENCH] 日志（Docs/perf/hand-baseline-2026-10-19.md），
        // 调整曲目或改动指法/IK 后必须同步更新。
        // 组内基线 → 门禁 = 基线 − 3pp：黑键 99.9→96.9、和弦 99.3→96.3、快速音阶 97.8→94.8；
        // 跨手样本仅 1，取 95% 避免单样本抖动导致假失败。
        private const float BaselineBlackKey = 0.969f;
        private const float BaselineChord = 0.963f;
        private const float BaselineFastScale = 0.948f;
        private const float BaselineLargeMotion = 0.95f;

        private LessonSessionController _session;
        private HandIKController _handIK;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;
            _session = Object.FindFirstObjectByType<LessonSessionController>();
            _handIK = Object.FindFirstObjectByType<HandIKController>();
            Assert.IsNotNull(_session, "PianoLesson 场景应包含 LessonSessionController");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        private static List<string> BenchFiles()
        {
            var result = new List<string>();
            string dir = Path.Combine(Application.streamingAssetsPath, "Songs", BenchFolder);
            if (!Directory.Exists(dir)) return result;
            var files = Directory.GetFiles(dir, "*.mid");
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
            result.AddRange(files);
            return result;
        }

        /// <summary>整曲播放一首基准曲，返回该曲的动作指标报告。</summary>
        private IEnumerator PlayWholeSong(string path, System.Action<HandMotionReport, float> onDone)
        {
            _session.LoadSong(path);
            yield return null;
            Assert.IsTrue(_session.HasSong, $"基准曲应能加载：{Path.GetFileName(path)}");

            float duration = Mathf.Clamp(_session.TotalDuration, 1f, 30f);
            _session.Seek(0f);
            _session.Play();
            yield return new WaitForSeconds(duration + 0.3f);
            _session.Pause();
            yield return null;

            onDone?.Invoke(_session.HandMotion, duration);
        }

        [UnityTest]
        public IEnumerator Bench_ThreeSongs_LogFullReport()
        {
            var files = BenchFiles();
            if (files.Count == 0) Assert.Ignore("未找到基准曲目集（先运行 Tools/make_bench_midi.py）");

            foreach (var file in files)
            {
                HandMotionReport report = null;
                float duration = 0f;
                yield return PlayWholeSong(file, (r, d) => { report = r; duration = d; });

                Assert.IsNotNull(report, "应产出动作报告");
                Debug.Log($"[BENCH] song={Path.GetFileNameWithoutExtension(file)} duration={duration:0.0}s " +
                          $"keys 键面P50={report.KeyTopDistance.P50 * 1000f:0.#}mm " +
                          $"键面P95={report.KeyTopDistance.P95 * 1000f:0.#}mm " +
                          $"键面峰值={report.KeyTopDistance.Max * 1000f:0.#}mm " +
                          $"指尖均值={report.FingertipTargetError.Mean * 1000f:0.#}mm " +
                          $"指尖P95={report.FingertipTargetError.P95 * 1000f:0.#}mm " +
                          $"换位峰值={report.TransitionPeakError * 1000f:0.#}mm " +
                          $"悬空={report.HoveringFrames} 指尖压键={report.FingerPenetrationFrames} " +
                          $"掌穿模={report.PalmPenetrationFrames} " +
                          $"命中率={report.HitRate * 100f:0.#}% 样本={report.TargetSamples}");
                Debug.Log($"[BENCH] song={Path.GetFileNameWithoutExtension(file)} full {report.ToLogString()}");

                // 批次 2.1 归因：把「哪一类失败」也打印出来（未分配 / 缺按压点 / 超可达范围）
                if (_handIK != null)
                {
                    int total = _handIK.FingertipTargetSampleCount;
                    int unassigned = _handIK.UnassignedFingertipSamples;
                    int missingTouch = _handIK.MissingKeyTouchSamples;
                    int unreachable = _handIK.UnreachableTargetSamples;
                    Debug.Log($"[BENCH-DIAG] song={Path.GetFileNameWithoutExtension(file)} " +
                              $"目标样本={total} 未分配={Pct(unassigned, total)} 缺按压点={Pct(missingTouch, total)} " +
                              $"超可达范围={Pct(unreachable, total)}");
                }
            }
        }

        [UnityTest]
        public IEnumerator Bench_GroupHitRates_NotBelowRecordedBaseline()
        {
            var files = BenchFiles();
            if (files.Count == 0) Assert.Ignore("未找到基准曲目集（先运行 Tools/make_bench_midi.py）");

            foreach (var file in files)
            {
                HandMotionReport report = null;
                yield return PlayWholeSong(file, (r, _) => report = r);

                string song = Path.GetFileNameWithoutExtension(file);
                AssertGroup(song, "黑键", report.BlackKeyHitRate, report.BlackKeyTargetSamples, BaselineBlackKey);
                AssertGroup(song, "和弦", report.ChordHitRate, report.ChordTargetSamples, BaselineChord);
                AssertGroup(song, "快速音阶", report.FastScaleHitRate, report.FastScaleTargetSamples, BaselineFastScale);
                AssertGroup(song, "跨手", report.LargeMotionHitRate, report.LargeMotionTargetSamples, BaselineLargeMotion);
            }
        }

        private static string Pct(int part, int total)
            => total > 0 ? $"{part}（{part * 100f / total:0.#}%）" : "0（样本 0）";

        private static void AssertGroup(string song, string group, float rate, int samples, float baseline)        {
            if (samples == 0) return; // 该曲不含此类帧：不计入（与「样本 0」口径一致）
            Assert.GreaterOrEqual(rate, baseline - GroupGateMargin,
                $"{song} 的「{group}」命中率 {rate * 100f:0.#}% 低于基线 {baseline * 100f:0.#}% −3pp（样本 {samples}）");
        }
    }
}
