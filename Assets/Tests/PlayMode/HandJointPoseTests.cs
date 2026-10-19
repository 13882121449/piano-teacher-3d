using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 手部三关节姿态与受限指尖 IK 的运行时回归（评估文档附录 A.6.2 / A.6.3）。
    /// <para>
    /// 覆盖：每指是否真有 MCP/PIP/DIP 三个可独立旋转的关节、关节角是否落在限位内、
    /// 按压时指尖是否真的落到目标键附近、静止手型是否回到自然弯曲、
    /// 三个角色 × 三种视角下骨架与指尖是否始终有效。
    /// </para>
    /// </summary>
    public class HandJointPoseTests
    {
        private LessonSessionController _session;
        private HandIKController _handIK;
        private FingerPoseController _fingerPose;
        private AvatarManager _avatar;
        private PianoKeyMap _keyMap;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            _session = Object.FindFirstObjectByType<LessonSessionController>();
            _handIK = Object.FindFirstObjectByType<HandIKController>();
            _fingerPose = Object.FindFirstObjectByType<FingerPoseController>();
            _avatar = Object.FindFirstObjectByType<AvatarManager>();
            _keyMap = Object.FindFirstObjectByType<PianoKeyMap>();

            Assert.IsNotNull(_session);
            Assert.IsNotNull(_handIK);
            Assert.IsNotNull(_fingerPose);
            Assert.IsNotNull(_avatar);
            Assert.IsNotNull(_keyMap);

            _session.Stop();
            _session.SetDisplayMode(DisplayMode.Showcase);
            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetCameraMode(CameraMode.Full);
            yield return new WaitForSeconds(0.2f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        // ==================================================================
        // ① 三关节骨架
        // ==================================================================

        [UnityTest]
        public IEnumerator EveryFinger_HasThreeIndependentlyDrivenJoints()
        {
            yield return null;

            for (int hand = 0; hand < 2; hand++)
            {
                bool left = hand == 0;
                var rig = (left ? _avatar.LeftHand : _avatar.RightHand) as IFingerJointRig;
                Assert.IsNotNull(rig, (left ? "左手" : "右手") + "应实现 IFingerJointRig（三关节）");

                for (int f = 0; f < 5; f++)
                {
                    Assert.IsTrue(rig.TryGetJoints(f, out var proximal, out var intermediate, out var distal),
                        $"{(left ? "左手" : "右手")}第 {f} 指缺少三关节链");

                    Assert.IsNotNull(proximal);
                    Assert.IsNotNull(intermediate);
                    Assert.IsNotNull(distal);

                    Assert.AreEqual(proximal, intermediate.parent, "PIP 应是 MCP 的子节点");
                    Assert.AreEqual(intermediate, distal.parent, "DIP 应是 PIP 的子节点");

                    float proximalLength = Vector3.Distance(proximal.position, intermediate.position);
                    float intermediateLength = Vector3.Distance(intermediate.position, distal.position);
                    Assert.Greater(proximalLength, 0.008f, $"第 {f} 指近节长度退化");
                    Assert.Greater(intermediateLength, 0.006f, $"第 {f} 指中节长度退化");

                    Assert.IsNotNull(rig.GetFingertip(f), $"第 {f} 指缺少指尖节点");
                    Assert.Greater(rig.GetFingerLength(f), proximalLength + intermediateLength,
                        $"第 {f} 指总长应大于两个近端关节之和");
                }
            }
        }

        [UnityTest]
        public IEnumerator HumanoidSkeleton_ExposesThreeJointsPerFinger()
        {
            var skeleton = _avatar.CurrentSkeleton;
            Assert.IsNotNull(skeleton, "当前角色应有 Humanoid 骨架");

            for (int f = 0; f < 5; f++)
            {
                Assert.IsTrue(skeleton.TryGetFingerJoints(true, f, out _, out _, out _),
                    $"Humanoid 骨架左手第 {f} 指缺少三关节");
                Assert.IsTrue(skeleton.TryGetFingerJoints(false, f, out _, out _, out _),
                    $"Humanoid 骨架右手第 {f} 指缺少三关节");
            }

            Assert.IsTrue(skeleton.Avatar.isValid, "三关节扩展后 Avatar 仍应有效");
            Assert.IsTrue(skeleton.Avatar.isHuman, "三关节扩展后仍应被识别为 Humanoid");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Palm_IsSingleLoftedMeshNotPrimitiveCluster()
        {
            yield return null;

            var wrist = _avatar.LeftHand.Wrist;
            var palmMesh = wrist.Find("PalmMesh");
            Assert.IsNotNull(palmMesh, "掌部应是一块独立网格（PalmMesh）");

            var filter = palmMesh.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter);
            Assert.IsNotNull(filter.sharedMesh, "掌部网格不应为空");
            Assert.Greater(filter.sharedMesh.vertexCount, 40, "掌部放样网格应远多于一个原始球体");

            // 掌部不再是多个椭球拼接：腕下只剩一块带 MeshFilter 的掌部网格 + 5 个指根
            int palmMeshes = 0;
            for (int i = 0; i < wrist.childCount; i++)
            {
                var child = wrist.GetChild(i);
                if (child.name.StartsWith("Palm") && child.GetComponent<MeshFilter>() != null) palmMeshes++;
            }
            Assert.AreEqual(1, palmMeshes, "掌部应只有一块网格（不再有 PalmRoot/PalmCore/KnuckleRidge 拼接）");
        }

        // ==================================================================
        // ② 关节角与指尖落点
        // ==================================================================

        [UnityTest]
        public IEnumerator PressedChord_CurlsAssignedFingersWithinLimits()
        {
            int[] chord = { 48, 52, 55, 60 };

            for (int i = 0; i < chord.Length; i++)
            {
                var n = new MidiNoteEvent(chord[i], 0f, 3f, 90, 0, 0);
                _fingerPose.OnNoteOn(n);
                _handIK.OnNoteOn(n);
            }

            for (int frame = 0; frame < 45; frame++) yield return null;

            var limits = FingerJointLimits.Default;
            int checkedFingers = 0;

            for (int i = 0; i < chord.Length; i++)
            {
                int pitch = chord[i];
                bool left = PianoNoteFilter.IsLeftHand(pitch);
                int finger = _fingerPose.GetFingerIndexForPitch(left, pitch);
                Assert.GreaterOrEqual(finger, 0, $"音高 {pitch} 未被分配手指");

                var rig = (left ? _avatar.LeftHand : _avatar.RightHand) as IFingerJointRig;
                Assert.IsTrue(rig.TryGetJoints(finger, out var proximal, out var intermediate, out var distal));

                float mcp = NormalizeAngle(proximal.localRotation.eulerAngles.x);
                float pip = NormalizeAngle(intermediate.localRotation.eulerAngles.x);
                float dip = NormalizeAngle(distal.localRotation.eulerAngles.x);

                Assert.IsNull(HandPoseValidator.ValidateJointAngles(mcp, pip, dip, limits),
                    $"音高 {pitch}（指 {finger}）关节角越界：MCP {mcp:F1}° / PIP {pip:F1}° / DIP {dip:F1}°");

                _handIK.TryGetKeyPressPoint(pitch, out Vector3 press);
                float tipError = Vector3.Distance(rig.GetFingertip(finger).position, press);

                // 「可见弯曲」只在目标可达时要求：短指（拇指/小指）够不到远处键位时，
                // 最接近的合法姿态就是接近伸直 —— 这是手长的物理限制，已记录进 JointLimitHits。
                float reach = rig.GetFingerLength(finger);
                float distance = Vector3.Distance(proximal.position, press);
                if (distance <= reach * 0.95f)
                {
                    Assert.Greater(mcp, 1f, $"音高 {pitch} 的指根几乎没有弯曲（MCP {mcp:F1}°）——可达时必须有可见弯曲");
                }
                else
                {
                    Assert.Less(tipError, reach, $"音高 {pitch} 不可达时，指尖也应停在最接近的位置");
                }

                Assert.Less(tipError, 0.045f, string.Format(
                    "音高 {0}（指 {1}）收敛后指尖仍离键面 {2:F1} mm", pitch, finger, tipError * 1000f));

                checkedFingers++;
            }

            Assert.GreaterOrEqual(checkedFingers, 3, "至少应检查三根手指");

            _handIK.ReleaseAll();
            _fingerPose.ReleaseAll();
        }

        [UnityTest]
        public IEnumerator RestingHand_ReturnsToNaturalCurl()
        {
            _handIK.ReleaseAll();
            _fingerPose.ReleaseAll();

            for (int frame = 0; frame < 45; frame++) yield return null;

            var rig = _avatar.LeftHand as IFingerJointRig;
            Assert.IsTrue(rig.TryGetJoints(2, out var proximal, out var intermediate, out var distal));

            float mcp = NormalizeAngle(proximal.localRotation.eulerAngles.x);
            float pip = NormalizeAngle(intermediate.localRotation.eulerAngles.x);
            float dip = NormalizeAngle(distal.localRotation.eulerAngles.x);

            Assert.Greater(mcp, 3f, "静止时指根应保留自然弯曲（不应僵直）");
            Assert.Less(mcp, 40f, "静止时指根不应过度弯曲");
            Assert.Greater(pip, 1f, "静止时中节应有轻微弯曲");
            Assert.Greater(dip, 0f, "静止时末节应有轻微弯曲");
            Assert.IsNull(HandPoseValidator.ValidateJointAngles(mcp, pip, dip, FingerJointLimits.Default));
        }

        [UnityTest]
        public IEnumerator Thumb_KeepsAbductionRatherThanParallelToOtherFingers()
        {
            yield return null;

            var hand = _avatar.LeftHand;
            var rig = hand as IFingerJointRig;
            Assert.IsTrue(rig.TryGetJoints(0, out var thumb, out _, out _));
            Assert.IsTrue(rig.TryGetJoints(1, out var index, out _, out _));

            // 解剖朝向（外展/对掌）烘在指链节点上（指根的父节点），不是每帧被覆盖的关节角
            float thumbYaw = NormalizeAngle(thumb.parent.localRotation.eulerAngles.y);
            float indexYaw = NormalizeAngle(index.parent.localRotation.eulerAngles.y);

            Assert.Greater(Mathf.Abs(thumbYaw), 30f, $"拇指应在指根处外展（当前 {thumbYaw:F1}°）");
            Assert.Greater(Mathf.Abs(thumbYaw - indexYaw), 25f, "拇指与食指不应平行");
        }

        // ==================================================================
        // ③ 三角色 × 三视角
        // ==================================================================

        [UnityTest]
        public IEnumerator ThreeAvatarsAndThreeViews_KeepHandRigValid()
        {
            var modes = new[] { CameraMode.Full, CameraMode.Closeup, CameraMode.Free };
            var note = new MidiNoteEvent(60, 0f, 4f, 90, 0, 0);

            for (int avatar = 0; avatar < AvatarManager.AvatarCount; avatar++)
            {
                _session.SwitchAvatar(avatar);
                yield return null;

                _fingerPose.OnNoteOn(note);
                _handIK.OnNoteOn(note);

                for (int m = 0; m < modes.Length; m++)
                {
                    _session.SetCameraMode(modes[m]);
                    for (int frame = 0; frame < 12; frame++) yield return null;

                    var rig = _avatar.CurrentSkeleton;
                    Assert.IsNotNull(rig, $"角色 {avatar} 在 {modes[m]} 下骨架丢失");
                    Assert.IsTrue(rig.IsHumanoid, $"角色 {avatar} 在 {modes[m]} 下不是合法 Humanoid");

                    var handRig = _avatar.RightHand as IFingerJointRig;
                    Assert.IsNotNull(handRig, $"角色 {avatar} 的右手缺少三关节");
                    Assert.IsTrue(handRig.TryGetJoints(1, out _, out _, out _),
                        $"角色 {avatar} 在 {modes[m]} 下关节链失效");

                    Assert.IsTrue(_handIK.TryGetKeyPressPoint(60, out Vector3 press),
                        $"角色 {avatar} 找不到音高 60 的按压点");
                    Assert.Greater(press.y, 0.5f, "按压点高度应落在键盘附近");
                }

                _handIK.ReleaseAll();
                _fingerPose.ReleaseAll();
            }
        }

        [UnityTest]
        public IEnumerator TeachingMode_HidesHandsWithoutBreakingRig()
        {
            _session.SetDisplayMode(DisplayMode.Teaching);
            yield return null;

            var wrist = _avatar.LeftHand.Wrist;
            Assert.IsFalse(wrist.gameObject.activeInHierarchy, "教学模式下应隐藏手部网格");

            var rig = _avatar.LeftHand as IFingerJointRig;
            Assert.IsTrue(rig.TryGetJoints(2, out _, out _, out _), "隐藏手部不应破坏关节链");

            _session.SetDisplayMode(DisplayMode.Showcase);
            yield return null;
            Assert.IsTrue(wrist.gameObject.activeInHierarchy, "切回展示模式应立即恢复手部");
        }

        // ==================================================================

        /// <summary>把 0..360 的角度折算到 −180..180（欧拉角读回值可能是 350° 而非 −10°）。</summary>
        private static float NormalizeAngle(float degrees)
        {
            float d = degrees % 360f;
            if (d > 180f) d -= 360f;
            return d;
        }
    }
}
