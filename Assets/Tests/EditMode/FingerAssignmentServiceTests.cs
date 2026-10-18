using NUnit.Framework;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 「手指 ↔ 音高」共用分配服务（方案 5.3 第二阶段 / 审查 1.2B）：
    /// 手指姿态控制器与手部 IK 必须读同一份结果，否则会出现
    /// 「弯下去的手指并不是落在键上的手指」这类错配。
    /// </summary>
    public class FingerAssignmentServiceTests
    {
        private FingerAssignmentService NewService()
        {
            var service = new FingerAssignmentService();
            service.Provider = new HeuristicFingeringProvider();
            return service;
        }

        [Test]
        public void Refresh_AssignsEveryPitchToExactlyOneFinger()
        {
            var service = NewService();
            var pitches = new[] { 48, 52, 55 };

            service.Refresh(true, pitches, 0f, 1);

            int assigned = 0;
            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
            {
                int pitch = service.GetPitch(true, f);
                if (pitch < 0) continue;
                assigned++;

                // 反向查询必须得到同一根手指（一一对应，不允许"一个音挂多指"）
                Assert.AreEqual(f, service.GetFinger(true, pitch),
                    "同一音高被分配给多根手指（" + pitch + "）");
            }
            Assert.AreEqual(pitches.Length, assigned, "每个按下的音高都应被分到一根手指");
        }

        [Test]
        public void Refresh_SameFrameAndSamePitches_IsIdempotentAndConsistentAcrossCallers()
        {
            var service = NewService();
            var pitches = new[] { 60, 64 };

            service.Refresh(true, pitches, 0f, 100);
            int first = service.GetPitch(true, 0);

            // 第二个调用者（同一帧、同一集合）不应重算出不同答案
            service.Refresh(true, pitches, 0f, 100);
            Assert.AreEqual(first, service.GetPitch(true, 0), "同帧重复刷新产生了不同结果");

            // 集合变化（同一帧内）必须重算
            service.Refresh(true, new[] { 60 }, 0f, 100);
            Assert.AreEqual(1, CountAssigned(service, true), "音高集合变化后应重新求解");
        }

        [Test]
        public void Refresh_LeftAndRightAreIsolated()
        {
            var service = NewService();
            service.Refresh(true, new[] { 48, 52 }, 0f, 1);
            service.Refresh(false, new[] { 72, 76 }, 0f, 1);

            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
            {
                int l = service.GetPitch(true, f);
                if (l >= 0) Assert.Less(l, PianoNoteFilter.SplitPitch, "左手分配到了右手音区");
                int r = service.GetPitch(false, f);
                if (r >= 0) Assert.GreaterOrEqual(r, PianoNoteFilter.SplitPitch, "右手分配到了左手音区");
            }
        }

        [Test]
        public void Refresh_EmptyInput_ClearsAssignmentAndNeverThrows()
        {
            var service = NewService();
            service.Refresh(true, new[] { 48, 52 }, 0f, 1);
            Assert.AreEqual(2, CountAssigned(service, true));

            service.Refresh(true, new int[0], 0f, 2);
            Assert.AreEqual(0, CountAssigned(service, true), "空输入应清空分配");

            Assert.DoesNotThrow(() => service.Refresh(true, null, 0f, 3), "null 输入不得抛异常");
            Assert.IsFalse(service.HasAssignment(true));
        }

        [Test]
        public void Clear_RemovesAssignmentAndMelodyHistory()
        {
            var service = NewService();
            service.PushMelody(true, 48);
            service.Refresh(true, new[] { 48, 52 }, 0f, 1);
            Assert.IsTrue(service.HasAssignment(true));

            service.Clear();

            Assert.IsFalse(service.HasAssignment(true));
            Assert.AreEqual(-1, service.GetPitch(true, 0));
            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
                Assert.AreEqual(-1, service.GetFinger(true, 48 + f));
        }

        [Test]
        public void Provider_NullFallsBackToHeuristicInsteadOfThrowing()
        {
            var service = new FingerAssignmentService();
            service.Provider = null;

            Assert.DoesNotThrow(() => service.Refresh(true, new[] { 48, 52, 55 }, 0f, 1));
            Assert.IsTrue(service.HasAssignment(true), "未设置提供器时应回退启发式而不是产生空分配");
            Assert.IsNotEmpty(service.ProviderName);
        }

        [Test]
        public void Refresh_MorePitchesThanFingers_NeverWritesOutOfRangeIndex()
        {
            var service = NewService();
            var many = new[] { 40, 43, 45, 47, 48, 50, 52, 55, 57, 59 };

            Assert.DoesNotThrow(() => service.Refresh(true, many, 0f, 1));

            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
            {
                int pitch = service.GetPitch(true, f);
                if (pitch >= 0) Assert.Contains(pitch, many, "分配出的音高必须来自输入集合");
            }
        }

        private static int CountAssigned(FingerAssignmentService service, bool leftHand)
        {
            int n = 0;
            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
            {
                if (service.GetPitch(leftHand, f) >= 0) n++;
            }
            return n;
        }
    }
}
