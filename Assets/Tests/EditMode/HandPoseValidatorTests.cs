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
            Assert.IsTrue(HandPoseValidator.AreHandsCrossed(
                new Vector3(0.2f, 0f, 0f), new Vector3(-0.2f, 0f, 0f)), "左手在右侧即为交叉");

            Assert.IsFalse(HandPoseValidator.AreHandsCrossed(
                new Vector3(-0.2f, 0f, 0f), new Vector3(0.2f, 0f, 0f)));
        }
    }
}
