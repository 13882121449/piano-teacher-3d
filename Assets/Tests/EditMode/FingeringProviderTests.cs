using NUnit.Framework;
using PianoTeacher3D.Avatar;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 指法提供器测试（对应方案 5.3「第一阶段规则化指法分配」/ 审查 1.2B）。
    /// <para>
    /// 覆盖：单音落中间手指、相邻音就近、跨度大展开、黑键可达、拇指穿越、重复音同指、
    /// 和弦一致手型、低置信度标记 IsEstimated、空输入不崩、超过手指数复用不越界，
    /// 以及左右手分界与「手指编号恒在 0..4」这两条契约性约束。
    /// </para>
    /// <para>
    /// 这些断言验证的是「规则是否被正确执行」，不声称指法专业正确（方案 2.3 教学诚实性）。
    /// </para>
    /// </summary>
    public class FingeringProviderTests
    {
        private readonly HeuristicFingeringProvider _provider = new HeuristicFingeringProvider();

        private static int[] Pitches(params int[] pitches) => pitches;

        private FingeringPlan Assign(bool leftHand, int[] pitches, int[] melody = null)
            => _provider.Assign(new FingeringContext(leftHand, pitches, melody, 0f));

        [Test]
        public void Provider_ExposesName()
        {
            Assert.IsFalse(string.IsNullOrEmpty(_provider.Name), "提供器应提供可显示的名称");
        }

        // ==================== 1. 单音落中间手指 ====================

        [Test]
        public void SingleNote_CentralRegister_LandsOnMiddleFinger()
        {
            var plan = Assign(false, Pitches(60)); // C4（右手锚点）

            Assert.AreEqual(1, plan.FingerByPitch.Length);
            Assert.AreEqual(2, plan.FingerByPitch[0], "中央音域的单音应落在中间手指（第 3 指）");
            Assert.IsFalse(plan.IsEstimated, "锚点附近的单音属于高置信度");
            StringAssert.Contains("音高就近", plan.Reason);
        }

        // ==================== 2. 相邻音就近 ====================

        [Test]
        public void AdjacentNote_StaysOnNeighborFinger()
        {
            int baseFinger = Assign(false, Pitches(60)).FingerByPitch[0];
            int upper = Assign(false, Pitches(62), Pitches(60)).FingerByPitch[0];
            int lower = Assign(false, Pitches(59), Pitches(60)).FingerByPitch[0];

            Assert.LessOrEqual(System.Math.Abs(upper - baseFinger), 1, "相邻音（上）应保持手指连续");
            Assert.LessOrEqual(System.Math.Abs(lower - baseFinger), 1, "相邻音（下）应保持手指连续");
        }

        // ==================== 3. 跨度大展开 ====================

        [Test]
        public void WideStretch_ExpandsFinger()
        {
            var plan = Assign(false, Pitches(67), Pitches(60, 62, 64, 65, 66));

            Assert.AreEqual(4, plan.FingerByPitch[0], "远离锚点的高音应向外展开到小指");
            StringAssert.Contains("展开", plan.Reason);
            Assert.IsFalse(plan.IsEstimated);
        }

        // ==================== 4. 黑键可达 ====================

        [Test]
        public void BlackKey_AvoidsShortFinger()
        {
            // 左手 D#3（pitch 51，黑键，锚点上方 3 半音）：左手镜像后落在食指（短指），规则应改到更长的中指
            var plan = Assign(true, Pitches(51));

            Assert.AreEqual(2, plan.FingerByPitch[0], "黑键应避免短指（拇指/食指），改用较长手指");
            StringAssert.Contains("黑键", plan.Reason);
        }

        [Test]
        public void WhiteKey_IsNotShiftedByBlackKeyRule()
        {
            var plan = Assign(true, Pitches(45)); // A2（白键，锚点下方 3 半音 → 左手无名指）

            Assert.AreEqual(3, plan.FingerByPitch[0], "白键不应触发黑键改指规则，按左手镜像音域映射");
            Assert.IsFalse(plan.Reason != null && plan.Reason.Contains("黑键"));
        }

        // ==================== 5. 拇指穿越 ====================

        [Test]
        public void WideMelody_UsesThumbCrossing()
        {
            var melody = Pitches(60, 62, 64, 65, 67, 69, 71, 72);
            var plan = Assign(false, Pitches(74), melody);

            Assert.AreEqual(0, plan.FingerByPitch[0], "音域跨度达到一个八度以上时应使用拇指换位");
            StringAssert.Contains("拇指穿越", plan.Reason);
            Assert.Less(plan.Confidence, 0.8f, "拇指穿越属于估算，置信度应下调");
        }

        // ==================== 6. 重复音同指 ====================

        [Test]
        public void RepeatedNote_KeepsSameFinger()
        {
            int first = Assign(false, Pitches(64)).FingerByPitch[0];
            var repeat = Assign(false, Pitches(64), Pitches(64));

            Assert.AreEqual(first, repeat.FingerByPitch[0], "重复音必须沿用同一手指");
            StringAssert.Contains("重复音", repeat.Reason);
            Assert.Greater(repeat.Confidence, 0.9f, "重复音是最确定的规则之一");
        }

        // ==================== 7. 和弦一致手型 ====================

        [Test]
        public void Chord_UsesConsistentHandShape()
        {
            var a = Assign(false, Pitches(60, 64, 67)); // C-E-G
            var b = Assign(false, Pitches(60, 64, 67));

            CollectionAssert.AreEqual(a.FingerByPitch, b.FingerByPitch, "同一组音高必须得到同一手型");
            CollectionAssert.AreEqual(Pitches(0, 2, 4), a.FingerByPitch, "三和弦使用经典 1-3-5 手型");
            StringAssert.Contains("和弦手型", a.Reason);
            StringAssert.Contains("1-3-5", a.Reason);
            Assert.IsFalse(a.IsEstimated);
        }

        [Test]
        public void Dyad_TemplateDependsOnSpan()
        {
            var narrow = Assign(false, Pitches(60, 62)); // 二度
            var wide = Assign(false, Pitches(60, 69));   // 六度

            Assert.AreEqual(2, narrow.FingerByPitch[1], "窄音程使用拇指 + 中指");
            Assert.AreEqual(4, wide.FingerByPitch[1], "宽音程使用拇指 + 小指");
        }

        // ==================== 8. 低置信度标记 ====================

        [Test]
        public void OutOfComfortRange_MarksEstimatedAndLowersConfidence()
        {
            var plan = Assign(true, Pitches(21)); // A0：远低于左手舒适音域

            Assert.IsTrue(plan.IsEstimated, "超出单手舒适范围时必须标记为估算指法");
            Assert.Less(plan.Confidence, 0.55f);
            StringAssert.Contains("超出单手舒适范围", plan.Reason);
        }

        [Test]
        public void LargeChordWithShortFingerOnBlackKey_MarksEstimated()
        {
            var plan = Assign(false, Pitches(61, 63, 65, 66, 68, 69)); // 6 个音，含黑键

            Assert.AreEqual(6, plan.FingerByPitch.Length);
            Assert.IsTrue(plan.IsEstimated, "音数超过手指数且存在黑键可按性风险时应标记估算");
            StringAssert.Contains("复用手指", plan.Reason);
        }

        // ==================== 9. 空输入不崩 ====================

        [Test]
        public void EmptyInput_ReturnsEmptyPlanWithoutThrowing()
        {
            FingeringPlan plan = null;
            Assert.DoesNotThrow(() => plan = Assign(false, Pitches()));

            Assert.IsNotNull(plan);
            Assert.AreEqual(0, plan.FingerByPitch.Length);
            Assert.IsTrue(plan.IsEstimated, "没有输入时不可能是确定指法");
        }

        [Test]
        public void NullPitches_ReturnsEmptyPlan()
        {
            var plan = _provider.Assign(new FingeringContext(false, null, null, 0f));

            Assert.IsNotNull(plan);
            Assert.AreEqual(0, plan.FingerByPitch.Length);
        }

        // ==================== 10. 超过手指数复用不越界 ====================

        [Test]
        public void MorePitchesThanFingers_ReuseFingersWithoutOutOfRange()
        {
            var plan = Assign(false, Pitches(60, 62, 64, 65, 67, 69, 71)); // 7 个音 > 5 根手指

            Assert.AreEqual(7, plan.FingerByPitch.Length, "结果长度必须与输入等长");
            for (int i = 0; i < plan.FingerByPitch.Length; i++)
            {
                Assert.GreaterOrEqual(plan.FingerByPitch[i], 0, "手指编号不得为负");
                Assert.LessOrEqual(plan.FingerByPitch[i], 4, "手指编号不得超过小指（4）");
            }
        }

        [Test]
        public void Provider_NeverReturnsOutOfRangeFinger_ForStressInputs()
        {
            var cases = new[]
            {
                Pitches(21), Pitches(108), Pitches(21, 108), Pitches(36, 43, 48, 55, 60),
                Pitches(60, 61, 62, 63, 64, 65, 66, 67), Pitches(100, 101, 102)
            };

            foreach (var pitches in cases)
            {
                var plan = Assign(false, pitches);
                Assert.AreEqual(pitches.Length, plan.FingerByPitch.Length);
                foreach (int f in plan.FingerByPitch)
                {
                    Assert.GreaterOrEqual(f, 0);
                    Assert.LessOrEqual(f, 4);
                }
                Assert.IsFalse(string.IsNullOrEmpty(plan.Reason), "任何结果都应给出可诊断的中文说明");
                Assert.GreaterOrEqual(plan.Confidence, 0f);
                Assert.LessOrEqual(plan.Confidence, 1f);
            }
        }

        // ==================== 左右手分界（方案 6.3 组合规则 / 审查 1.2B） ====================

        [Test]
        public void WrongSidePitch_LowersConfidenceAndIsFlagged()
        {
            var plan = Assign(false, Pitches(48)); // C3 出现在右手请求中

            Assert.IsTrue(plan.IsEstimated, "对手侧音高属于异常输入，必须标注为估算");
            Assert.LessOrEqual(plan.Confidence, 0.5f);
            StringAssert.Contains("左右手分界", plan.Reason);
        }

        [Test]
        public void IsWrongSide_UsesSplitPitch60()
        {
            Assert.IsTrue(_provider.IsWrongSide(false, 59), "低于 C4 的音属于左手侧");
            Assert.IsFalse(_provider.IsWrongSide(false, 60), "C4 起属于右手侧");
            Assert.IsTrue(_provider.IsWrongSide(true, 60), "右手侧音高出现在左手请求中即为对手侧");
            Assert.AreEqual(60, _provider.SplitPitch, "分界音高必须与 PianoNoteFilter.SplitPitch 一致");
        }

        [Test]
        public void LeftHand_LowRegister_UsesLittleFinger()
        {
            var plan = Assign(true, Pitches(43)); // G2（白键，低音区，低于左手锚点 C3）

            // 左手低位由小指负责（拇指在高音侧）：这与右手「低音用拇指」正好相反。
            // 改造前该断言期望 0（拇指），等于把右手指法套在左手上 —— 正是「左右手反置」的指法侧根因。
            Assert.AreEqual(4, plan.FingerByPitch[0], "左手低音区应使用小指");
            Assert.IsFalse(plan.IsEstimated);
        }

        [Test]
        public void LeftHand_Chord_UsesMirroredTemplate()
        {
            // 左手 C3-E3-G3：从小指到拇指（5-3-1），而不是右手的 1-3-5
            var plan = Assign(true, Pitches(48, 52, 55));

            CollectionAssert.AreEqual(new[] { 4, 2, 0 }, plan.FingerByPitch,
                "左手和弦应从低音的小指排到高音的拇指");
        }

        [Test]
        public void LeftAndRightHands_AreEvaluatedIndependently()
        {
            var left = Assign(true, Pitches(48, 55, 59));
            var right = Assign(false, Pitches(60, 64, 67));

            Assert.AreEqual(3, left.FingerByPitch.Length);
            Assert.AreEqual(3, right.FingerByPitch.Length);
            Assert.AreNotSame(left.FingerByPitch, right.FingerByPitch, "左右手结果不得共享同一数组实例");
        }
    }
}
