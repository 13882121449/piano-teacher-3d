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

        // ==================== §7-3 指标口径（逐帧位移分布与术语别名） ====================

        [Test]
        public void PerFrameDisplacement_ReportsMeanAndP95_ForBothHands()
        {
            var acc = new HandMotionAccumulator();

            // 10 帧：左手每帧移动 0.010 m，右手每帧移动 0.030 m → 20 个样本
            var left = Vector3.zero;
            var right = Vector3.zero;
            acc.AddSample(Sample(left, right, Vector3.zero, Vector3.zero, false, false));
            for (int i = 0; i < 10; i++)
            {
                left += new Vector3(0.010f, 0f, 0f);
                right += new Vector3(0f, 0f, 0.030f);
                acc.AddSample(Sample(left, right, Vector3.zero, Vector3.zero, false, false));
            }

            var report = acc.Build();

            Assert.AreEqual(20, report.PerFrameDisplacement.Samples, "每手每帧各一个样本");
            Assert.AreEqual(0.020f, report.PerFrameDisplacement.Mean, 1e-4f, "均值应为两手位移的平均");
            Assert.AreEqual(0.030f, report.PerFrameDisplacement.Max, 1e-4f);
            Assert.AreEqual(0.030f, report.PerFrameDisplacement.P95, 1e-4f,
                "P95 应落在较大的那组（右手 0.030 m）");
        }

        [Test]
        public void MetricAliases_ShareTheSameUnderlyingStats()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(new HandMotionSample
            {
                HasAssignedFingertipErrors = true,
                LeftAssignedFingertipError = 0.02f,
                RightAssignedFingertipError = 0.04f,
                HasSurfaceDistances = true,
                LeftSurfaceDistance = 0.010f,
                RightSurfaceDistance = -0.002f,
                TransitionFrame = true
            });

            var report = acc.Build();

            Assert.AreSame(report.AssignedFingertipError, report.FingertipTargetError,
                "FingertipTargetError 必须与 AssignedFingertipError 是同一份统计（同一口径）");
            Assert.AreSame(report.FingertipSurfaceDistance, report.KeyTopDistance,
                "KeyTopDistance 必须与 FingertipSurfaceDistance 是同一份统计");
            Assert.AreEqual(report.TransitionFingertipError.Max, report.TransitionPeakError, 1e-6f,
                "TransitionPeakError 必须是过渡帧误差的峰值");
            Assert.AreEqual(0.04f, report.TransitionPeakError, 1e-4f);
        }

        // ==================== 审查 1.2C 扩充指标 ====================

        private static HandMotionSample GroupSample(bool hit, bool black, bool chord, bool fast, bool large)
        {
            return new HandMotionSample
            {
                LeftWrist = hit ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 1f, 0f),
                RightWrist = Vector3.zero,
                LeftTarget = Vector3.zero,
                RightTarget = Vector3.zero,
                LeftHasTarget = true,
                RightHasTarget = false,
                HitRadius = 0.09f,
                MovingThreshold = 0.001f,
                BlackKeyFrame = black,
                ChordFrame = chord,
                FastScaleFrame = fast,
                LargeMotionFrame = large
            };
        }

        private static void AddLeftStep(HandMotionAccumulator acc, Vector3 left, float speedAnomalyThreshold)
        {
            acc.AddSample(new HandMotionSample
            {
                LeftWrist = left,
                RightWrist = Vector3.zero,
                LeftHasTarget = false,
                RightHasTarget = false,
                MovingThreshold = 0.001f,
                SpeedAnomalyThreshold = speedAnomalyThreshold
            });
        }

        [Test]
        public void LeftAndRightHitRates_AreReportedSeparately()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(
                new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),   // 左手对准、右手偏离
                Vector3.zero, Vector3.zero));

            var report = acc.Build();
            Assert.AreEqual(1f, report.LeftHitRate, 1e-4f, "左手命中");
            Assert.AreEqual(0f, report.RightHitRate, 1e-4f, "右手未命中");
            Assert.AreEqual(1, report.LeftTargetSamples);
            Assert.AreEqual(1, report.RightTargetSamples);
        }

        [Test]
        public void FingertipErrors_AreAveragedAndMaxIsTracked()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(new HandMotionSample
            {
                LeftWrist = Vector3.zero,
                RightWrist = Vector3.zero,
                LeftHasTarget = false,
                RightHasTarget = false,
                HasFingertipErrors = true,
                LeftFingertipError = 0.01f,
                RightFingertipError = 0.03f,
                FingertipTolerance = 0.02f
            });

            var report = acc.Build();
            Assert.AreEqual(2, report.FingertipSamples);
            Assert.AreEqual(0.02f, report.FingertipMeanError, 1e-5f);
            Assert.AreEqual(0.03f, report.FingertipMaxError, 1e-5f);
            Assert.AreEqual(1, report.FingertipHitSamples, "只有左手落在容差内");
            Assert.AreEqual(0.5f, report.FingertipHitRate, 1e-4f);
        }

        [Test]
        public void FingertipErrors_AreIgnoredWithoutSampleFlag()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero));

            var report = acc.Build();
            Assert.AreEqual(0, report.FingertipSamples,
                "未标记含指尖样本时不得计入（默认 0 不能被误当成 0 误差）");
            Assert.AreEqual(0f, report.FingertipMeanError);
            Assert.AreEqual(1f, report.FingertipHitRate, 1e-4f, "无样本时按 1 计，避免误报");
        }

        [Test]
        public void SpeedAnomaly_CountsOnlyWhenThresholdProvided()
        {
            var without = new HandMotionAccumulator();
            AddLeftStep(without, Vector3.zero, 0f);
            AddLeftStep(without, new Vector3(0.05f, 0f, 0f), 0f);
            Assert.AreEqual(0, without.Build().SpeedAnomalyCount, "阈值为 0 时不统计");

            var with = new HandMotionAccumulator();
            AddLeftStep(with, Vector3.zero, 0.02f);                       // 首个对比帧尚无上一帧步长
            AddLeftStep(with, new Vector3(0.05f, 0f, 0f), 0.02f);         // step = 0.05
            AddLeftStep(with, new Vector3(0.05f, 0f, 0f), 0.02f);         // step = 0 → |0-0.05| > 0.02
            Assert.AreEqual(1, with.Build().SpeedAnomalyCount);
        }

        [Test]
        public void GroupRates_ExposeSampleCountsAndHits()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(GroupSample(hit: true, black: true, chord: true, fast: true, large: false));
            acc.AddSample(GroupSample(hit: false, black: true, chord: false, fast: false, large: true));

            var report = acc.Build();
            Assert.AreEqual(2, report.BlackKeyTargetSamples);
            Assert.AreEqual(0.5f, report.BlackKeyHitRate, 1e-4f);
            Assert.AreEqual(1, report.ChordTargetSamples);
            Assert.AreEqual(1f, report.ChordHitRate, 1e-4f);
            Assert.AreEqual(1, report.FastScaleTargetSamples);
            Assert.AreEqual(1f, report.FastScaleHitRate, 1e-4f);
            Assert.AreEqual(1, report.LargeMotionTargetSamples);
            Assert.AreEqual(0f, report.LargeMotionHitRate, 1e-4f);
        }

        [Test]
        public void GroupRates_WithoutSamples_AreOneAndSamplesAreZero()
        {
            var report = new HandMotionAccumulator().Build();

            Assert.AreEqual(1f, report.BlackKeyHitRate, 1e-4f);
            Assert.AreEqual(0, report.BlackKeyTargetSamples, "无样本时应能显示「样本 0」");
            Assert.AreEqual(1f, report.ChordHitRate, 1e-4f);
            Assert.AreEqual(0, report.ChordTargetSamples);
            Assert.AreEqual(1f, report.FastScaleHitRate, 1e-4f);
            Assert.AreEqual(0, report.FastScaleTargetSamples);
            Assert.AreEqual(1f, report.LargeMotionHitRate, 1e-4f);
            Assert.AreEqual(0, report.LargeMotionTargetSamples);
        }

        [Test]
        public void VisualCheckFlags_AreCountedPerFrame()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(new HandMotionSample
            {
                LeftWrist = Vector3.zero,
                RightWrist = Vector3.zero,
                LeftHasTarget = false,
                RightHasTarget = false,
                HandsCrossed = true,
                ThumbOrientationIssue = true,
                PalmPenetration = true,
                Hovering = true
            });

            var report = acc.Build();
            Assert.AreEqual(1, report.CrossedHandFrames);
            Assert.AreEqual(1, report.ThumbOrientationIssues);
            Assert.AreEqual(1, report.PalmPenetrationFrames);
            Assert.AreEqual(1, report.HoveringFrames);
        }

        [Test]
        public void ExtendedReportString_KeepsLegacyTextAndAddsNewDimensions()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(Sample(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero));

            string text = acc.Build().ToLogString();

            StringAssert.Contains("命中率", text);       // 向后兼容：既有字段仍在
            StringAssert.Contains("最大帧位移", text);
            StringAssert.Contains("平滑度", text);
            StringAssert.Contains("指尖误差", text);     // 新增维度
            StringAssert.Contains("分组", text);
            StringAssert.Contains("抽查", text);
            StringAssert.Contains("样本 0", text);       // 无分组样本时明确标注，而不是伪造 100%
        }

        [Test]
        public void Reset_ClearsExtendedStatistics()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(GroupSample(hit: true, black: true, chord: true, fast: true, large: true));

            acc.Reset();
            var report = acc.Build();

            Assert.AreEqual(0, report.BlackKeyTargetSamples);
            Assert.AreEqual(0, report.CrossedHandFrames);
            Assert.AreEqual(0, report.FingertipSamples);
            Assert.AreEqual(0, report.SpeedAnomalyCount);
            Assert.AreEqual(1f, report.BlackKeyHitRate, 1e-4f);
        }

        // ==================== 左右手分项（接触方案批次 1.3） ====================

        private static HandMotionSample HandSample(float leftError, float rightError,
            float leftSurface = 0f, float rightSurface = 0f, bool hasSurface = false)
        {
            var s = Sample(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero);
            s.HasAssignedFingertipErrors = true;
            s.LeftAssignedFingertipError = leftError;
            s.RightAssignedFingertipError = rightError;
            s.HasSurfaceDistances = hasSurface;
            s.LeftSurfaceDistance = leftSurface;
            s.RightSurfaceDistance = rightSurface;
            return s;
        }

        [Test]
        public void PerHandFingertipErrors_AreSeparated()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(HandSample(0.020f, 0.060f));

            var report = acc.Build();

            Assert.AreEqual(1, report.LeftFingertipTargetError.Samples);
            Assert.AreEqual(1, report.RightFingertipTargetError.Samples);
            Assert.AreEqual(0.020f, report.LeftFingertipTargetError.Mean, 1e-5f, "左手误差不得被右手样本污染");
            Assert.AreEqual(0.060f, report.RightFingertipTargetError.Mean, 1e-5f);
        }

        [Test]
        public void PerHandFingertipErrors_IgnoreNegativeSamples()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(HandSample(-1f, 0.030f));   // 负值 = 该手无样本

            var report = acc.Build();

            Assert.AreEqual(0, report.LeftFingertipTargetError.Samples, "负值样本不得计入左手");
            Assert.AreEqual(1, report.RightFingertipTargetError.Samples);
        }

        [Test]
        public void PerHandKeyTopDistance_AreSeparated()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(HandSample(0.01f, 0.01f, leftSurface: 0.004f, rightSurface: 0.020f, hasSurface: true));

            var report = acc.Build();

            Assert.AreEqual(1, report.LeftKeyTopDistance.Samples);
            Assert.AreEqual(1, report.RightKeyTopDistance.Samples);
            Assert.AreEqual(0.004f, report.LeftKeyTopDistance.Mean, 1e-5f);
            Assert.AreEqual(0.020f, report.RightKeyTopDistance.Mean, 1e-5f);
            Assert.AreEqual(0.012f, report.KeyTopDistance.Mean, 1e-5f, "整体口径仍是两手合并");
        }

        [Test]
        public void LogString_ContainsPerHandSections()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(HandSample(0.02f, 0.03f, 0.001f, 0.002f, hasSurface: true));

            string text = acc.Build().ToLogString();

            StringAssert.Contains("左 指尖", text);
            StringAssert.Contains("右 指尖", text);
            StringAssert.Contains("键面", text);
        }

        [Test]
        public void Reset_ClearsPerHandStatistics()
        {
            var acc = new HandMotionAccumulator();
            acc.AddSample(HandSample(0.02f, 0.03f, 0.001f, 0.002f, hasSurface: true));

            acc.Reset();
            var report = acc.Build();

            Assert.AreEqual(0, report.LeftFingertipTargetError.Samples);
            Assert.AreEqual(0, report.RightKeyTopDistance.Samples);
        }
    }
}
