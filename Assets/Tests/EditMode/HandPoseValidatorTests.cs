using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.Avatar;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 手部异常姿态检查测试（对应方案阶段五验收「异常姿态检查，避免动作失控或穿模」）。
    /// </summary>
    public class HandPoseValidatorTests
    {
        /// <summary>测试用允许范围：键面 0.75m、键盘 X ∈ [-0.62, 0.62]、深度 Z ∈ [-0.06, 0.28]。</summary>
        private static HandPoseLimits Limits => new HandPoseLimits(
            minY: 0.75f, maxY: 1.05f,
            minX: -0.62f, maxX: 0.62f,
            minZ: -0.06f, maxZ: 0.28f);

        [Test]
        public void Validate_PositionInsideLimits_ReturnsNull()
        {
            var wrist = new Vector3(0f, 0.78f, 0.05f);
            Assert.IsNull(HandPoseValidator.Validate(wrist, Limits));
        }

        [Test]
        public void Validate_BelowSurface_ReportsPenetrationRisk()
        {
            var wrist = new Vector3(0f, 0.70f, 0.05f);
            string issue = HandPoseValidator.Validate(wrist, Limits);

            Assert.IsNotNull(issue, "低于键面必须被判定为异常");
            StringAssert.Contains("低于", issue);
        }

        [Test]
        public void Validate_TooHigh_IsReported()
        {
            string issue = HandPoseValidator.Validate(new Vector3(0f, 1.5f, 0.05f), Limits);
            Assert.IsNotNull(issue);
            StringAssert.Contains("高于", issue);
        }

        [Test]
        public void Validate_BeyondLeftEdge_IsReported()
        {
            string issue = HandPoseValidator.Validate(new Vector3(-1.2f, 0.78f, 0.05f), Limits);
            Assert.IsNotNull(issue);
            StringAssert.Contains("左侧", issue);
        }

        [Test]
        public void Validate_BeyondRightEdge_IsReported()
        {
            string issue = HandPoseValidator.Validate(new Vector3(1.2f, 0.78f, 0.05f), Limits);
            Assert.IsNotNull(issue);
            StringAssert.Contains("右侧", issue);
        }

        [Test]
        public void Validate_InsidePianoBody_IsReported()
        {
            string issue = HandPoseValidator.Validate(new Vector3(0f, 0.78f, -0.3f), Limits);
            Assert.IsNotNull(issue);
            StringAssert.Contains("穿入琴体", issue);
        }

        [Test]
        public void Validate_TooFarOutside_IsReported()
        {
            string issue = HandPoseValidator.Validate(new Vector3(0f, 0.78f, 0.6f), Limits);
            Assert.IsNotNull(issue);
            StringAssert.Contains("靠外", issue);
        }

        [Test]
        public void Validate_WithinLateralMargin_IsAccepted()
        {
            // maxX + LateralMargin = 0.68，取 0.66 应仍在容差内
            Assert.IsNull(HandPoseValidator.Validate(new Vector3(0.66f, 0.78f, 0.05f), Limits));
        }

        [Test]
        public void Validate_ExactlyAtMinY_IsAccepted()
        {
            Assert.IsNull(HandPoseValidator.Validate(new Vector3(0f, 0.75f, 0.05f), Limits));
        }

        [Test]
        public void Clamp_BringsWildPositionBackInsideLimits()
        {
            var wild = new Vector3(99f, -99f, -99f);

            var clamped = HandPoseValidator.Clamp(wild, Limits);

            Assert.IsNull(HandPoseValidator.Validate(clamped, Limits), "钳制后的位置必须通过校验");
        }

        [Test]
        public void Clamp_NormalPosition_IsUnchanged()
        {
            var wrist = new Vector3(0.10f, 0.80f, 0.06f);
            Assert.AreEqual(wrist, HandPoseValidator.Clamp(wrist, Limits));
        }

        [Test]
        public void AreHandsCrossed_DetectsCrossing()
        {
            // 【左右约定】角色面向 -Z：左手在世界 +X。左手跑到更小的 X 才是交叉。
            Assert.IsTrue(HandPoseValidator.AreHandsCrossed(
                new Vector3(-0.2f, 0f, 0f), new Vector3(0.2f, 0f, 0f)), "左手落到右手一侧（更小 X）即为交叉");

            Assert.IsFalse(HandPoseValidator.AreHandsCrossed(
                new Vector3(0.2f, 0f, 0f), new Vector3(-0.2f, 0f, 0f)));
        }

        // ==================== 指尖目标误差与手指姿态异常（审查 1.2C / 第 4 章 HandPoseValidator 行） ====================

        [Test]
        public void FingertipError_MeasuresDistanceToTargetKey()
        {
            Assert.AreEqual(0.05f,
                HandPoseValidator.FingertipError(new Vector3(0f, 0f, 0.05f), Vector3.zero), 1e-5f);
        }

        [Test]
        public void ValidateFingertip_WithinTolerance_ReturnsNull()
        {
            var tip = new Vector3(0f, 0.742f, 0.045f);
            var target = new Vector3(0f, 0.742f, 0.050f); // 5 mm 偏差
            Assert.IsNull(HandPoseValidator.ValidateFingertip(tip, target, 0.035f));
        }

        [Test]
        public void ValidateFingertip_OutsideTolerance_ReportsError()
        {
            string issue = HandPoseValidator.ValidateFingertip(
                new Vector3(0f, 0.80f, 0.045f),
                new Vector3(0f, 0.742f, 0.045f), 0.035f);

            Assert.IsNotNull(issue, "指尖明显高于目标键面必须被判定为异常");
            StringAssert.Contains("指尖偏离", issue);
        }

        [Test]
        public void IsHovering_DetectsFingerAboveKeySurface()
        {
            var target = new Vector3(0f, 0.742f, 0f);

            Assert.IsTrue(HandPoseValidator.IsHovering(new Vector3(0f, 0.80f, 0f), target, 0.045f),
                "高出键面约 58 mm 应判为离键悬空");
            Assert.IsFalse(HandPoseValidator.IsHovering(new Vector3(0f, 0.75f, 0f), target, 0.045f),
                "贴近键面不应判为悬空");
        }

        [Test]
        public void ValidateFingerPose_DetectsDeviationBeyondLimit()
        {
            var root = Vector3.zero;
            var tip = new Vector3(0f, 0.05f, 0f); // 指向 +Y（与期望的 +Z 垂直）

            Assert.IsNull(HandPoseValidator.ValidateFingerPose(root, tip, Vector3.forward, 90f),
                "恰好等于上限不应判为异常（边界包含）");
            Assert.IsNotNull(HandPoseValidator.ValidateFingerPose(root, tip, Vector3.forward, 45f));
        }

        [Test]
        public void ValidateFingerPose_DegenerateSegment_IsReported()
        {
            string issue = HandPoseValidator.ValidateFingerPose(Vector3.one, Vector3.one, Vector3.forward, 45f);

            Assert.IsNotNull(issue, "指根与指尖重合属于异常姿态");
            StringAssert.Contains("退化", issue);
        }

        [Test]
        public void ValidateFingerPose_NoExpectedDirection_IsIgnored()
        {
            Assert.IsNull(HandPoseValidator.ValidateFingerPose(Vector3.zero, new Vector3(0f, 0.05f, 0f),
                Vector3.zero, 45f), "期望方向不可用时不应产生误报");
        }

        [Test]
        public void ThumbPalmAngle_AndAbnormalCheck()
        {
            float angle = HandPoseValidator.ThumbPalmAngle(new Vector3(1f, 0f, 1f), Vector3.forward);
            Assert.AreEqual(45f, angle, 0.5f, "45° 斜向拇指应测得约 45°");

            Assert.IsTrue(HandPoseValidator.IsThumbOrientationAbnormal(
                new Vector3(1f, 0f, 0f), Vector3.forward, 75f), "垂直于掌心前向的拇指方向异常");
            Assert.IsFalse(HandPoseValidator.IsThumbOrientationAbnormal(
                new Vector3(1f, 0f, 1f), Vector3.forward, 75f), "对掌方向的拇指不应被判为异常");
        }

        [Test]
        public void ThumbPalmAngle_DegenerateInput_ReturnsZero()
        {
            Assert.AreEqual(0f, HandPoseValidator.ThumbPalmAngle(Vector3.zero, Vector3.forward), 1e-5f);
            Assert.AreEqual(0f, HandPoseValidator.ThumbPalmAngle(Vector3.right, Vector3.zero), 1e-5f);
        }

        [Test]
        public void CountFingerPoseIssues_CountsAbnormalFingers()
        {
            var roots = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
            var tips = new[]
            {
                new Vector3(0f, 0f, 0.05f),  // 正常（沿 +Z）
                new Vector3(0f, 0.05f, 0f),  // 异常（沿 +Y，偏 90°）
                new Vector3(0f, 0f, 0.05f)   // 正常
            };

            Assert.AreEqual(1, HandPoseValidator.CountFingerPoseIssues(roots, tips, Vector3.forward, 45f));
            Assert.AreEqual(0, HandPoseValidator.CountFingerPoseIssues(null, tips, Vector3.forward, 45f),
                "空输入不崩且返回 0");
        }
    }
}
