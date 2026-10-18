using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 相机取景纯逻辑测试（对应方案 4.1–4.5、审查 3.1B / 3.1C / 3.1D 与 3.2「视角可用性」）。
    /// <para>
    /// 只测 <see cref="FramingPolicy"/> / <see cref="CameraPoseTransition"/> 等纯数学部分，
    /// 不依赖场景、摄像机与 MonoBehaviour，因此全部可在 EditMode 稳定运行。
    /// </para>
    /// </summary>
    public class CameraFramingTests
    {
        private const float Aspect = 16f / 9f;

        /// <summary>构造一个手部取景范围（世界 AABB）。</summary>
        private static HandRegion Region(float xMin, float xMax, float yMin, float yMax, float zMin, float zMax)
        {
            var center = new Vector3((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f, (zMin + zMax) * 0.5f);
            var size = new Vector3(
                Mathf.Abs(xMax - xMin),
                Mathf.Abs(yMax - yMin),
                Mathf.Abs(zMax - zMin));
            return HandRegion.FromBounds(new Bounds(center, size));
        }

        /// <summary>断言一个点位于由取景目标决定的视锥内（与 SolveFraming 使用同一基底与半角关系）。</summary>
        private static void AssertPointInsideFrustum(Vector3 point, in CameraTarget target, float aspect, string message)
        {
            Vector3 back = target.Direction.normalized;
            Vector3 forward = -back;
            Vector3 right = Vector3.Cross(forward, Vector3.up);
            right = right.sqrMagnitude > 1e-8f ? right.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(right, forward).normalized;

            Vector3 q = point - target.Focus;
            float depth = target.Distance - Vector3.Dot(q, back);
            Assert.Greater(depth, 0.01f, message + "（相机不得穿过被摄物体）");

            float tanV = Mathf.Tan(target.FieldOfView * Mathf.Deg2Rad * 0.5f);
            float tanH = tanV * aspect;
            Assert.LessOrEqual(Mathf.Abs(Vector3.Dot(q, up)), depth * tanV + 1e-3f, message + "（垂直方向出框）");
            Assert.LessOrEqual(Mathf.Abs(Vector3.Dot(q, right)), depth * tanH + 1e-3f, message + "（水平方向出框）");
        }

        /// <summary>断言整个包围盒都在视锥内。</summary>
        private static void AssertBoxInsideFrustum(in Bounds box, in CameraTarget target, float aspect, string message)
        {
            Vector3 min = box.min;
            Vector3 max = box.max;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                AssertPointInsideFrustum(corner, target, aspect, message);
            }
        }

        // ==================== 手部特写：距离 / FOV 限幅（方案 4.2.2 / 审查 3.1B） ====================

        [Test]
        public void ComputeCloseupTarget_HandsApart_IncreasesDistanceAndFov_WithinLimits()
        {
            var tuning = CloseupFramingTuning.Default();

            // 双手集中在中央（和弦段）
            var nearLeft = Region(-0.09f, 0f, 0.75f, 0.755f, -0.05f, 0.05f);
            var nearRight = Region(0f, 0.09f, 0.75f, 0.755f, -0.05f, 0.05f);
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(nearLeft, nearRight, default, false,
                PracticeHand.Both, tuning, Aspect, out CameraTarget nearTarget));

            // 双手相距很远的低音—高音跳跃段
            var farLeft = Region(-0.42f, -0.12f, 0.75f, 0.755f, -0.05f, 0.05f);
            var farRight = Region(0.12f, 0.42f, 0.75f, 0.755f, -0.05f, 0.05f);
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(farLeft, farRight, default, false,
                PracticeHand.Both, tuning, Aspect, out CameraTarget farTarget));

            Assert.Greater(farTarget.Distance, nearTarget.Distance, "两手分开时应适度拉远相机");
            Assert.Greater(farTarget.FieldOfView, nearTarget.FieldOfView, "距离到达上限后应由 FOV 补足取景范围");
            Assert.LessOrEqual(farTarget.Distance, tuning.MaxDistance + 1e-3f, "距离不得超过上限（避免剧烈拉远）");
            Assert.LessOrEqual(farTarget.FieldOfView, tuning.MaxFov + 1e-3f, "FOV 不得超过上限（抑制呼吸感）");
        }

        [Test]
        public void ComputeCloseupTarget_HandsClose_NeverBelowLimits()
        {
            var tuning = CloseupFramingTuning.Default();

            var left = Region(-0.02f, 0f, 0.75f, 0.76f, -0.02f, 0.03f);
            var right = Region(0f, 0.02f, 0.75f, 0.76f, -0.02f, 0.03f);
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(left, right, default, false,
                PracticeHand.Both, tuning, Aspect, out CameraTarget target));

            Assert.GreaterOrEqual(target.Distance, tuning.MinDistance - 1e-3f, "双手靠近时距离不得低于下限");
            Assert.GreaterOrEqual(target.FieldOfView, tuning.BaseFov - 1e-3f, "FOV 不得低于下限");
            Assert.Less(target.Distance, tuning.MaxDistance, "手部很集中时不应拉到最大距离（避免画面呼吸感过强）");
        }

        [Test]
        public void SolveFraming_TinyRegion_ClampsToMinDistanceAndBaseFov()
        {
            var tuning = CloseupFramingTuning.Default();
            tuning.HandPadding = 0.001f; // 只保留极点范围，检验下限是否真的生效

            var tiny = Region(0f, 0f, 0.75f, 0.75f, 0f, 0f);
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(tiny, HandRegion.Empty, default, false,
                PracticeHand.LeftOnly, tuning, Aspect, out CameraTarget target));

            Assert.AreEqual(tuning.MinDistance, target.Distance, 1e-3f, "距离应被钳制到下限");
            Assert.AreEqual(tuning.BaseFov, target.FieldOfView, 1e-3f, "FOV 应被钳制到下限");
        }

        [Test]
        public void SolveFraming_ExtremeSpan_RespectsMaxLimits()
        {
            var tuning = CloseupFramingTuning.Default();

            // 极端跨度（双手交叉/最大跨度）：限幅优先，不追求全覆盖（方案 4.2.2）
            var left = Region(-0.75f, -0.25f, 0.75f, 0.755f, -0.05f, 0.05f);
            var right = Region(0.25f, 0.75f, 0.75f, 0.755f, -0.05f, 0.05f);
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(left, right, default, false,
                PracticeHand.Both, tuning, Aspect, out CameraTarget target));

            Assert.AreEqual(tuning.MaxDistance, target.Distance, 1e-3f, "距离应钳制在上限");
            Assert.AreEqual(tuning.MaxFov, target.FieldOfView, 1e-3f, "FOV 应钳制在上限");
        }

        // ==================== 手部特写：目标点计算（方案 4.2.1 / 4.2.4） ====================

        [Test]
        public void ComputeCloseupTarget_BothHands_UsesUnionRangeNotWristCenterAverage()
        {
            var tuning = CloseupFramingTuning.Default();

            // 左手范围很宽（手掌张开），右手范围窄：两腕中心均值必然与联合范围中心不同
            var left = Region(-0.50f, -0.10f, 0.75f, 0.76f, -0.05f, 0.05f);
            var right = Region(0.30f, 0.35f, 0.75f, 0.76f, -0.05f, 0.05f);
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(left, right, default, false,
                PracticeHand.Both, tuning, Aspect, out CameraTarget both));

            float wristCenterAverageX = (left.Center.x + right.Center.x) * 0.5f;

            Assert.AreEqual(-0.075f, both.Focus.x, 0.01f, "焦点应为双手联合范围中心");
            Assert.Less(both.Focus.x, wristCenterAverageX - 0.02f, "焦点不得退化为两腕中心平均值");
        }

        [Test]
        public void ComputeCloseupTarget_LeftOnly_FocusShiftsTowardLeftHand()
        {
            var tuning = CloseupFramingTuning.Default();
            // 【左右约定】角色面向 -Z，左手在世界 +X（低音侧 = 教学画面左侧），右手在 -X
            var left = Region(0.10f, 0.50f, 0.75f, 0.76f, -0.05f, 0.05f);
            var right = Region(-0.35f, -0.30f, 0.75f, 0.76f, -0.05f, 0.05f);

            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(left, right, default, false,
                PracticeHand.Both, tuning, Aspect, out CameraTarget both));
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(left, right, default, false,
                PracticeHand.LeftOnly, tuning, Aspect, out CameraTarget leftOnly));

            Assert.Greater(leftOnly.Focus.x, both.Focus.x + 0.02f, "左手练习时焦点应落在左手范围（世界 +X 侧）");
        }

        [Test]
        public void ComputeCloseupTarget_RightOnly_FocusShiftsTowardRightHand()
        {
            var tuning = CloseupFramingTuning.Default();
            var left = Region(0.10f, 0.50f, 0.75f, 0.76f, -0.05f, 0.05f);
            var right = Region(-0.50f, -0.10f, 0.75f, 0.76f, -0.05f, 0.05f);

            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(left, right, default, false,
                PracticeHand.Both, tuning, Aspect, out CameraTarget both));
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(left, right, default, false,
                PracticeHand.RightOnly, tuning, Aspect, out CameraTarget rightOnly));

            Assert.Less(rightOnly.Focus.x, both.Focus.x - 0.02f, "右手练习时焦点应落在右手范围（世界 -X 侧）");
        }

        [Test]
        public void MergeWithShare_KeepsPrimaryDominant_AndPartiallyIncludesSecondary()
        {
            var primary = Region(-0.50f, -0.10f, 0.74f, 0.76f, -0.05f, 0.05f);

            // 次手在主手附近：应被部分并入，保留可见性
            var nearby = Region(0.00f, 0.40f, 0.74f, 0.76f, -0.05f, 0.05f);
            HandRegion merged = FramingPolicy.MergeWithShare(primary, nearby, 0.7f);

            Assert.AreEqual(primary.Min.x, merged.Min.x, 1e-4f, "主手侧边界不应收缩");
            Assert.Greater(merged.Max.x, primary.Max.x, "附近的次手应被部分并入");
            Assert.LessOrEqual(merged.Size.x, primary.Size.x / 0.7f + 1e-3f, "主手必须占据不少于 70% 的画面宽度");

            // 次手远离主手：不并入（方案 4.2.4：单手观察时允许另一只手离开主要取景区）
            var faraway = Region(0.60f, 0.70f, 0.74f, 0.76f, -0.05f, 0.05f);
            HandRegion notMerged = FramingPolicy.MergeWithShare(primary, faraway, 0.7f);
            Assert.AreEqual(primary.Min.x, notMerged.Min.x, 1e-4f);
            Assert.AreEqual(primary.Max.x, notMerged.Max.x, 1e-4f, "相距很远的次手不应扩大取景框");
        }

        [Test]
        public void ComputeCloseupTarget_UnionsUpcomingKeyBounds()
        {
            var tuning = CloseupFramingTuning.Default();
            var left = Region(-0.10f, 0f, 0.75f, 0.76f, -0.05f, 0.05f);
            var right = Region(0f, 0.10f, 0.75f, 0.76f, -0.05f, 0.05f);

            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(left, right, default, false,
                PracticeHand.Both, tuning, Aspect, out CameraTarget withoutKeys));

            // 当前/即将发生的琴键范围（例如右手即将移动到的中高音区）
            var keyBounds = new Bounds(new Vector3(0.55f, 0.74f, 0f), new Vector3(0.20f, 0.06f, 0.16f));
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(left, right, keyBounds, true,
                PracticeHand.Both, tuning, Aspect, out CameraTarget withKeys));

            Assert.Greater(withKeys.Focus.x, withoutKeys.Focus.x + 0.02f, "即将发生的琴键应并入取景目标");
            Assert.Greater(withKeys.Distance, withoutKeys.Distance, "并入更远的琴键后应适度拉远");
            Assert.LessOrEqual(withKeys.Distance, tuning.MaxDistance + 1e-3f);
        }

        [Test]
        public void ComputeCloseupTarget_VerticalFollow_IsLimited()
        {
            var tuning = CloseupFramingTuning.Default();

            var atKeybed = Region(-0.10f, 0.10f, 0.748f, 0.752f, -0.05f, 0.05f);
            var raised = Region(-0.10f, 0.10f, 1.043f, 1.047f, -0.05f, 0.05f); // 手腕抬升约 0.3m

            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(atKeybed, HandRegion.Empty, default, false,
                PracticeHand.LeftOnly, tuning, Aspect, out CameraTarget low));
            Assert.IsTrue(FramingPolicy.ComputeCloseupTarget(raised, HandRegion.Empty, default, false,
                PracticeHand.LeftOnly, tuning, Aspect, out CameraTarget high));

            float shift = Mathf.Abs(high.Focus.y - low.Focus.y);
            Assert.LessOrEqual(shift, tuning.VerticalFollowLimit + 1e-3f, "焦点垂直跟随幅度必须受限（不改俯仰观感）");
            Assert.Less(shift, 0.1f, "手腕明显上抬时焦点也不应跟着大幅移动");
        }

        // ==================== 全景（方案 4.3 / 审查 3.1C） ====================

        [Test]
        public void ComputeFullTarget_KeepsA0AndC8InsideFrustum_AndSubjectNotCropped()
        {
            var tuning = FullFramingTuning.Default();

            // 人物（坐姿，含琴凳）与 88 键（A0 pitch=21 至 C8 pitch=108）
            var subject = new Bounds(new Vector3(0f, 0.75f, 0.42f), new Vector3(0.70f, 1.50f, 0.70f));
            var a0 = new Vector3(-0.624f, 0.720f, -0.075f);
            var c8 = new Vector3(0.624f, 0.755f, 0.075f);
            var keyboard = new Bounds((a0 + c8) * 0.5f, new Vector3(1.272f, 0.035f, 0.150f));
            keyboard.Encapsulate(a0);
            keyboard.Encapsulate(c8);

            CameraTarget target = FramingPolicy.ComputeFullTarget(subject, keyboard, false, tuning, Aspect);

            AssertPointInsideFrustum(a0, target, Aspect, "A0（pitch=21）必须完整入画");
            AssertPointInsideFrustum(c8, target, Aspect, "C8（pitch=108）必须完整入画");
            AssertBoxInsideFrustum(keyboard, target, Aspect, "88 键包围盒必须完整入画");
            AssertBoxInsideFrustum(subject, target, Aspect, "人物头脚不得出框");

            Assert.GreaterOrEqual(target.Radius, Vector3.Distance(target.Focus, a0) - 1e-3f,
                "联合覆盖半径必须覆盖 A0");
            Assert.GreaterOrEqual(target.Radius, Vector3.Distance(target.Focus, c8) - 1e-3f,
                "联合覆盖半径必须覆盖 C8");
        }

        [Test]
        public void ComputeFullTarget_TallerSubject_GetsLighterFramingMargin()
        {
            var tuning = FullFramingTuning.Default();
            var keyboard = new Bounds(new Vector3(0f, 0.74f, 0f), new Vector3(1.272f, 0.05f, 0.16f));

            var standard = new Bounds(new Vector3(0f, 0.725f, 0.42f), new Vector3(0.70f, 1.45f, 0.70f));
            var taller = new Bounds(new Vector3(0f, 0.85f, 0.42f), new Vector3(0.70f, 1.90f, 0.70f));

            CameraTarget standardTarget = FramingPolicy.ComputeFullTarget(standard, keyboard, false, tuning, Aspect);
            CameraTarget tallerTarget = FramingPolicy.ComputeFullTarget(taller, keyboard, false, tuning, Aspect);

            Assert.Greater(tallerTarget.Distance, standardTarget.Distance,
                "身材更高的角色应获得更大取景余量（按身高/头身比轻量校正）");
            AssertBoxInsideFrustum(taller, tallerTarget, Aspect, "高个角色同样不能出框");
        }

        [Test]
        public void ComputeFullTarget_KeyboardSubView_KeepsKeyboardInsideFrustum()
        {
            var tuning = FullFramingTuning.Default();
            var subject = new Bounds(new Vector3(0f, 0.75f, 0.42f), new Vector3(0.70f, 1.50f, 0.70f));
            var keyboard = new Bounds(new Vector3(0f, 0.7375f, 0f), new Vector3(1.272f, 0.035f, 0.150f));

            CameraTarget normal = FramingPolicy.ComputeFullTarget(subject, keyboard, false, tuning, Aspect);
            CameraTarget subView = FramingPolicy.ComputeFullTarget(subject, keyboard, true, tuning, Aspect);

            AssertBoxInsideFrustum(keyboard, subView, Aspect, "「键盘无遮挡」教学子视角必须完整容纳 88 键");
            Assert.Greater(subView.Direction.y, normal.Direction.y, "教学子视角俯角应更陡（默认关闭，需明确标识）");
        }

        // ==================== 过渡中断（方案 4.4 末条 / 审查 3.1D） ====================

        [Test]
        public void Transition_Interrupted_StartsFromCurrentPose()
        {
            var from = CameraPose.Create(new Vector3(0f, 1f, 0f), Quaternion.identity, 20f);
            var first = CameraPose.Create(new Vector3(5f, 2f, 3f), Quaternion.Euler(10f, 20f, 0f), 55f);
            var second = CameraPose.Create(new Vector3(-4f, 1.5f, -2f), Quaternion.Euler(-5f, 40f, 0f), 35f);

            var transition = new CameraPoseTransition();
            transition.Begin(from, first, 0.4f);
            transition.Advance(0.2f);
            CameraPose mid = transition.Evaluate();

            // 连续快速切换：以当前实际姿态作为新过渡起点，不叠加插值状态
            transition.Begin(mid, second, 0.4f);
            CameraPose restarted = transition.Evaluate();

            Assert.IsTrue(transition.Active);
            Assert.AreEqual(0f, transition.Elapsed, 1e-4f);
            Assert.AreEqual(mid.Position.x, restarted.Position.x, 1e-4f);
            Assert.AreEqual(mid.Position.y, restarted.Position.y, 1e-4f);
            Assert.AreEqual(mid.Position.z, restarted.Position.z, 1e-4f);
            Assert.AreEqual(mid.FieldOfView, restarted.FieldOfView, 1e-4f);
            Assert.Less(Quaternion.Angle(mid.Rotation, restarted.Rotation), 0.01f);
        }

        [Test]
        public void Transition_ReachesTargetAtDuration_AndStops()
        {
            var from = CameraPose.Create(new Vector3(0f, 1f, 0f), Quaternion.identity, 30f);
            var to = CameraPose.Create(new Vector3(2f, 3f, 4f), Quaternion.Euler(0f, 90f, 0f), 50f);

            var transition = new CameraPoseTransition();
            transition.Begin(from, to, 0.35f);

            Assert.IsTrue(transition.Advance(0.1f), "过渡期内应保持进行中");
            Assert.IsFalse(transition.Advance(0.30f), "累计时长超过 0.35s 后过渡应结束");

            CameraPose done = transition.Evaluate();
            Assert.AreEqual(to.Position.x, done.Position.x, 1e-3f);
            Assert.AreEqual(to.Position.z, done.Position.z, 1e-3f);
            Assert.AreEqual(to.FieldOfView, done.FieldOfView, 1e-3f);
            Assert.Less(Quaternion.Angle(to.Rotation, done.Rotation), 0.01f);
        }

        // ==================== 阻尼、限幅与输入规则 ====================

        [Test]
        public void Damp_ConvergesMonotonically_NoDivergence()
        {
            const float target = 10f;
            const float dt = 1f / 60f;
            float value = 0f;
            float previousError = Mathf.Abs(target - value);

            for (int i = 0; i < 600; i++)
            {
                value = FramingPolicy.Damp(value, target, 8f, dt);
                float error = Mathf.Abs(target - value);
                Assert.LessOrEqual(error, previousError + 1e-6f, "阻尼迭代必须单调收敛，不得发散或过冲");
                Assert.LessOrEqual(value, target + 1e-4f, "阻尼跟随不得过冲");
                previousError = error;
            }

            Assert.Less(previousError, 1e-3f, "长时间迭代后必须收敛到目标（不发散）");

            float factor = FramingPolicy.DampFactor(8f, dt);
            Assert.Greater(factor, 0f);
            Assert.Less(factor, 1f);
            Assert.AreEqual(10f, FramingPolicy.Damp(0f, 10f, 0f, dt), 1e-5f, "speed = 0 表示直接对齐");
        }

        [Test]
        public void ClampPitch_LimitsElevationRange()
        {
            Assert.AreEqual(85f, FramingPolicy.ClampPitch(120f, 5f, 85f), 1e-4f);
            Assert.AreEqual(5f, FramingPolicy.ClampPitch(-30f, 5f, 85f), 1e-4f);
            Assert.AreEqual(40f, FramingPolicy.ClampPitch(40f, 5f, 85f), 1e-4f);

            float clampedPitch = FramingPolicy.ClampPitch(-80f, 5f, 85f);
            Assert.Greater(FramingPolicy.DirectionFromAngles(0f, clampedPitch).y, 0f,
                "俯仰限幅后相机方向不得指向地面以下");
        }

        [Test]
        public void ShouldAcceptOrbitInput_BlocksSceneRotationOverUi()
        {
            Assert.IsFalse(FramingPolicy.ShouldAcceptOrbitInput(pointerOverUi: true, orbitButtonHeld: true),
                "鼠标悬停在 UI 面板/按钮/进度条上时不得旋转场景");
            Assert.IsFalse(FramingPolicy.ShouldAcceptOrbitInput(true, false));
            Assert.IsFalse(FramingPolicy.ShouldAcceptOrbitInput(false, false));
            Assert.IsTrue(FramingPolicy.ShouldAcceptOrbitInput(false, true));
        }

        [Test]
        public void PushOutside_PushesPointOutOfPianoBounds()
        {
            var piano = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 2f));
            var inside = new Vector3(0f, 1.95f, 0f);
            Assert.IsTrue(piano.Contains(inside));

            Vector3 pushed = FramingPolicy.PushOutside(piano, 0.1f, inside);
            Assert.AreEqual(2.1f, pushed.y, 1e-4f, "应沿最小穿透轴把相机推出包围盒（防穿琴体）");

            var outside = new Vector3(0f, 5f, 0f);
            Assert.AreEqual(outside, FramingPolicy.PushOutside(piano, 0.1f, outside), "盒外的点必须原样返回");
        }

        [Test]
        public void RateLimit_CapsPerStepChange()
        {
            Assert.AreEqual(30.3f, FramingPolicy.RateLimit(30f, 44f, 0.3f), 1e-4f, "单步变化受限于限幅值");
            Assert.AreEqual(30.2f, FramingPolicy.RateLimit(30f, 30.2f, 0.3f), 1e-4f, "目标在限幅范围内时直接到达");
            Assert.AreEqual(35f, FramingPolicy.RateLimit(40f, 30f, 5f), 1e-4f, "反向变化同样受限");
            Assert.AreEqual(44f, FramingPolicy.RateLimit(30f, 44f, 0f), 1e-4f, "限幅为 0 表示不限");
        }

        [Test]
        public void YawHelpers_NormalizeAndClamp()
        {
            Assert.AreEqual(-170f, FramingPolicy.NormalizeYaw(190f), 1e-3f);
            Assert.AreEqual(10f, FramingPolicy.ClampYaw(370f, -180f, 180f), 1e-3f, "默认不限范围但需规整");
            Assert.AreEqual(40f, FramingPolicy.ClampYaw(60f, -40f, 40f), 1e-3f, "限定旋转范围时生效");
        }

        [Test]
        public void SmoothStep01_IsMonotonicAndClamped()
        {
            Assert.AreEqual(0f, FramingPolicy.SmoothStep01(0f), 1e-6f);
            Assert.AreEqual(1f, FramingPolicy.SmoothStep01(1f), 1e-6f);
            Assert.AreEqual(0f, FramingPolicy.SmoothStep01(-3f), 1e-6f);
            Assert.AreEqual(1f, FramingPolicy.SmoothStep01(9f), 1e-6f);

            float previous = -1f;
            for (int i = 0; i <= 40; i++)
            {
                float v = FramingPolicy.SmoothStep01(i / 40f);
                Assert.GreaterOrEqual(v, previous, "缓动曲线必须单调递增");
                previous = v;
            }
        }
    }
}
