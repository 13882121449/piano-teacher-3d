using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.Avatar;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 手部动作质量指标测试（对应方案阶段五验收「评估动作连续性、目标琴键命中率、左右手协调」）。
    /// </summary>
    public class HandMotionMetricsTests
    {
        private static HandMotionSample Sample(
            Vector3 leftWrist, Vector3 rightWrist,
            Vector3 leftTarget, Vector3 rightTarget,
            bool leftHas = true, bool rightHas = true)
        {
            return new HandMotionSample
            {
                LeftWrist = leftWrist,
                RightWrist = rightWrist,
                LeftTarget = leftTarget,
                RightTarget = rightTarget,
                LeftHasTarget = leftHas,
                RightHasTarget = rightHas,
                HitRadius = 0.09f,
                MovingThreshold = 0.001f
            };
        }

        [Test]
        public void EmptyAccumulator_ReportsZeroSamplesAndFullHitRate()
        {
            var report = new HandMotionAccumulator().Build();

            Assert.AreEqual(0, report.Samples);
            Assert.AreEqual(0, report.TargetSamples);
            Assert.AreEqual(1f, report.HitRate, 1e-4f, "无目标帧时命中率按 1 计，避免误报");
        }

        [Test]
        public void AllWristsWithinRadius_HitRateIsOne()
        {
            var acc = new HandMotionAccumulator();
            for (int i = 0; i < 10; i++)
            {
                acc.AddSample(Sample(
                    new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
                    new Vector3(0f, 1.05f, 0f), new Vector3(1f, 1.05f, 0f))); // 距离 0.05 < 0.09
            }

            Assert.AreEqual(1f, acc.Build().HitRate, 1e-4f);
        }

        [Test]
        public void AllWristsOutsideRadius_HitRateIsZero()
        {
            var acc = new HandMotionAccumulator();
            for (int i = 0; i < 5; i++)
            {
                acc.AddSample(Sample(Vector3.zero, Vector3.zero,
                    new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 0f))); // 距离 1.0 >> 0.09
            }

            Assert.AreEqual(0f, acc.Build().HitRate, 1e-4f);
        }

        [Test]
        public void HitRadiusBoundary_IsInclusive()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(new Vector3(0.09f, 0f, 0f), Vector3.zero,
                Vector3.zero, Vector3.zero, leftHas: true, rightHas: false));

            Assert.AreEqual(1f, acc.Build().HitRate, 1e-4f, "距离恰好等于半径应判为命中");
        }

        [Test]
        public void VerticalOffset_DoesNotAffectHitRate()
        {
            // 命中判定只看水平对准（换位抬升会临时抬高手腕，不应因此判为未命中）
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(
                new Vector3(0f, 1.30f, 0f), Vector3.zero,
                new Vector3(0f, 1.00f, 0f), Vector3.zero,
                leftHas: true, rightHas: false));

            Assert.AreEqual(1f, acc.Build().HitRate, 1e-4f);
        }

        [Test]
        public void FramesWithoutTarget_AreExcludedFromHitRate()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                leftHas: false, rightHas: false));

            var report = acc.Build();
            Assert.AreEqual(0, report.TargetSamples);
            Assert.AreEqual(1f, report.HitRate, 1e-4f);
        }

        [Test]
        public void MaxStepDistance_TracksLargestSingleFrameMove()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero));
            acc.AddSample(Sample(new Vector3(0.5f, 0f, 0f), Vector3.zero, Vector3.zero, Vector3.zero)); // 0.5
            acc.AddSample(Sample(new Vector3(0.6f, 0f, 0f), Vector3.zero, Vector3.zero, Vector3.zero)); // 0.1

            Assert.AreEqual(0.5f, acc.Build().MaxStepDistance, 1e-4f);
        }

        [Test]
        public void StraightLineMotion_SmoothnessIsNearOne()
        {
            var acc = new HandMotionAccumulator();
            for (int i = 0; i <= 10; i++)
            {
                var p = new Vector3(i * 0.01f, 0f, 0f);
                acc.AddSample(Sample(p, p, p, p));
            }

            Assert.Greater(acc.Build().Smoothness, 0.99f, "直线移动的平滑度应接近 1");
        }

        [Test]
        public void BackAndForthMotion_SmoothnessIsLow()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero));
            acc.AddSample(Sample(new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), Vector3.zero, Vector3.zero));
            acc.AddSample(Sample(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero));

            Assert.Less(acc.Build().Smoothness, 0.2f, "往复移动路径长而净位移为零，平滑度应很低");
        }

        [Test]
        public void CoordinationRatio_CountsOnlyFramesWhereBothHandsMove()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero));
            // 第 1 次比较：仅左手移动
            acc.AddSample(Sample(new Vector3(0.1f, 0f, 0f), Vector3.zero, Vector3.zero, Vector3.zero));
            // 第 2 次比较：双手同时移动
            acc.AddSample(Sample(new Vector3(0.2f, 0f, 0f), new Vector3(0.1f, 0f, 0f), Vector3.zero, Vector3.zero));

            Assert.AreEqual(0.5f, acc.Build().CoordinationRatio, 1e-3f);
        }

        [Test]
        public void Reset_ClearsAllStatistics()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(new Vector3(0.5f, 0f, 0f), Vector3.zero, Vector3.zero, Vector3.zero));
            Assert.AreEqual(1, acc.Build().Samples);

            acc.Reset();
            var report = acc.Build();

            Assert.AreEqual(0, report.Samples);
            Assert.AreEqual(0f, report.MaxStepDistance);
            Assert.AreEqual(1f, report.HitRate, 1e-4f);
        }

        [Test]
        public void ReportString_ContainsKeyMetrics()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero));

            string text = acc.Build().ToLogString();

            StringAssert.Contains("命中率", text);
            StringAssert.Contains("最大帧位移", text);
            StringAssert.Contains("平滑度", text);
        }
    }
}
