using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 手部动作质量集成测试（方案阶段五验收：动作连续性 / 目标琴键命中率 / 左右手协调 / 穿模检查）。
    /// </summary>
    public class HandMotionTests
    {
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
            Assert.IsNotNull(_handIK, "PianoLesson 场景应包含 HandIKController");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        private IEnumerator PlayFor(float seconds)
        {
            _session.Seek(0f);
            _session.Play();
            yield return new WaitForSeconds(seconds);
            _session.Pause();
        }

        // ==================== 姿态范围与穿模 ====================

        [UnityTest]
        public IEnumerator PoseLimits_AreDerivedFromPianoGeometry()
        {
            Assert.IsTrue(_handIK.TryGetPoseLimits(out var limits), "应能从钢琴几何构建姿态允许范围");
            Assert.Less(limits.MinY, limits.MaxY, "高度范围应有效");
            Assert.Less(limits.MinX, limits.MaxX, "键盘 X 范围应有效");
            Assert.Less(limits.MinZ, limits.MaxZ, "深度范围应有效");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayingSession_ProducesNoPoseViolations()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            yield return PlayFor(2.5f);

            Assert.AreEqual(0, _handIK.PoseIssueCount,
                "播放期间手腕不应越界（穿模风险）：" + string.Join(" | ", _handIK.PoseIssues));
        }

        [UnityTest]
        public IEnumerator PlayingSession_HandsNeverTeleport()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            yield return PlayFor(2.5f);

            var report = _session.HandMotion;
            Assert.IsNotNull(report);
            Assert.Greater(report.Samples, 10, "应产生足够采样");
            Assert.Less(report.MaxStepDistance, 0.15f, "单帧位移应小于 15cm（无瞬移）");
        }

        // ==================== 命中率 / 连续性 / 协调 ====================

        [UnityTest]
        public IEnumerator PlayingSession_KeyHitRateAboveThreshold()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            yield return PlayFor(3f);

            var report = _session.HandMotion;
            Debug.Log($"[MOTION] {report.ToLogString()}");

            Assert.Greater(report.TargetSamples, 0, "应存在带目标的采样帧");
            Assert.Greater(report.HitRate, 0.8f,
                $"目标琴键命中率应高于 80%，实际 {report.HitRate * 100f:0.#}%");
        }

        [UnityTest]
        public IEnumerator PlayingSession_MetricsAreWithinValidRange()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            yield return PlayFor(1.5f);

            var report = _session.HandMotion;
            Assert.GreaterOrEqual(report.Smoothness, 0f);
            Assert.LessOrEqual(report.Smoothness, 1f);
            Assert.GreaterOrEqual(report.CoordinationRatio, 0f);
            Assert.LessOrEqual(report.CoordinationRatio, 1f);
            Assert.Greater(report.LeftPathLength + report.RightPathLength, 0f, "播放期间手腕应有位移");
        }

        // ==================== 状态切换边界 ====================

        [UnityTest]
        public IEnumerator LoadingNewSong_ResetsMotionMetrics()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            yield return PlayFor(0.8f);
            int before = _session.HandMotion.Samples;
            Assert.Greater(before, 0, "播放后应有采样");

            string dir = Path.Combine(Application.streamingAssetsPath, "Songs");
            var files = Directory.GetFiles(dir, "*.mid");
            if (files.Length == 0) Assert.Ignore("无可用曲目文件");

            _session.LoadSong(files[0]);
            yield return null;

            Assert.Less(_session.HandMotion.Samples, before, "换曲后指标应清零并重新计数");
            Assert.AreEqual(0, _session.PoseIssueCount, "换曲后姿态告警应清零");
        }

        [UnityTest]
        public IEnumerator Stop_LeavesHandsInValidPose()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            yield return PlayFor(0.8f);
            _session.Stop();
            yield return null;
            yield return null;

            Assert.AreEqual(0, _handIK.PoseIssueCount, "停止后不应出现姿态越界");
        }

        // ==================== 回归测试（本轮修复的缺陷） ====================

        /// <summary>回归：左右手的手指分配必须互相隔离（缺陷 B1）。</summary>
        [UnityTest]
        public IEnumerator FingerPose_LeftAndRightAssignmentsAreIndependent()
        {
            var fingerPose = Object.FindFirstObjectByType<FingerPoseController>();
            Assert.IsNotNull(fingerPose, "场景应包含 FingerPoseController");

            // 左右手各喂一个按键，走两帧让 Update 完成分配
            fingerPose.OnNoteOn(new MidiNoteEvent(48, 0f, 1f, 90, 0, 0)); // 左手（< C4）
            fingerPose.OnNoteOn(new MidiNoteEvent(72, 0f, 1f, 90, 0, 0)); // 右手（≥ C4）
            yield return null;
            yield return null;

            bool leftAssigned = false;
            bool rightAssigned = false;
            for (int f = 0; f < 5; f++)
            {
                if (fingerPose.GetFingerPitch(true, f) == 48) leftAssigned = true;
                if (fingerPose.GetFingerPitch(false, f) == 72) rightAssigned = true;
            }

            fingerPose.ReleaseAll();

            Assert.IsTrue(leftAssigned, "左手分配结果应保留（左右手数据必须隔离，不可互相覆盖）");
            Assert.IsTrue(rightAssigned, "右手分配结果应保留");
        }

        /// <summary>回归：手腕目标经平滑后，单帧变化量应受限（缺陷 B3）。</summary>
        [UnityTest]
        public IEnumerator HandTarget_SmoothingLimitsPerFrameJump()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            _session.Seek(0f);
            _session.Play();
            yield return null;
            yield return null; // 跳过首次对齐帧

            float maxJump = 0f;
            var prevLeft = _handIK.LeftSmoothedTarget;
            var prevRight = _handIK.RightSmoothedTarget;

            for (int i = 0; i < 120; i++)
            {
                yield return null;

                var left = _handIK.LeftSmoothedTarget;
                var right = _handIK.RightSmoothedTarget;
                maxJump = Mathf.Max(maxJump,
                    Mathf.Max(Vector3.Distance(prevLeft, left), Vector3.Distance(prevRight, right)));

                prevLeft = left;
                prevRight = right;
            }
            _session.Pause();

            Debug.Log($"[MOTION] 目标单帧最大变化 {maxJump * 1000f:0.#} mm（阈值 {_handIK.TargetJumpThreshold * 1000f:0.#} mm）");
            Assert.Less(maxJump, 0.25f,
                $"目标单帧变化应受分级约束（小变化精确对齐 / 大跳变限速），实际 {maxJump * 1000f:0.#} mm");
        }
    }
}
