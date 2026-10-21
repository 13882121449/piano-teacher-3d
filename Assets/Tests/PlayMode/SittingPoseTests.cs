using System.Collections;
using NUnit.Framework;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Characters;
using UnityEngine;
using UnityEngine.TestTools;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 导入模型的坐姿几何（方案 8.2「统一模型朝向、比例、地面高度和钢琴座椅位置」）。
    ///
    /// <para>
    /// 关键点：<b>不能用欧拉角</b>。导入骨架（3ds Max Biped）的局部轴与 Unity Humanoid 约定不同，
    /// 给大腿加 80° 的 X 旋转实测<b>完全无效</b>（膝盖仍在髋正下方一整个腿长）。
    /// 现在按世界方向摆骨，因此这里的断言全部是「世界坐标下的身体姿势」，
    /// 与骨骼命名、局部轴、左右镜像都无关。
    /// </para>
    /// </summary>
    public class SittingPoseTests
    {
        private CharacterProfile _profile;

        [SetUp]
        public void SetUp()
        {
            var registry = new CharacterRegistry();
            for (int i = 0; i < registry.All.Count; i++)
            {
                var p = registry.All[i];
                if (p != null && p.Source == CharacterSource.Prefab) { _profile = p; break; }
            }
            Assert.IsNotNull(_profile, "角色库中应有一个外部预制体角色");
        }

        [UnityTest]
        public IEnumerator Apply_SitsOnStool_WithFeetOnFloor()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, error);

            try
            {
                var settings = view.PoseSettings;
                view.OnCharacterActivated();
                yield return null;

                var animator = view.Animator;
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                Transform lAnkle = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rAnkle = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);

                float expectedHipY = settings.SeatSurfaceHeight + settings.HipAboveSeat;
                Assert.AreEqual(expectedHipY, hips.position.y, 0.03f,
                    $"髋关节应坐在凳面上方 {settings.HipAboveSeat * 100f:0} cm，实际 {hips.position.y:F3}");
                Assert.AreEqual(settings.SeatDepth, hips.position.z, 0.06f,
                    $"髋关节应落在座位深度 {settings.SeatDepth:F2}，实际 {hips.position.z:F3}");

                // 大腿朝前：膝盖必须在髋前方（−Z）且低于髋
                Assert.Less(knee.position.z, hips.position.z - 0.20f,
                    $"膝盖应明显前伸（坐姿），实际 z={knee.position.z:F3}，髋 z={hips.position.z:F3}");
                Assert.Less(knee.position.y, hips.position.y - 0.01f,
                    "膝盖应低于髋关节");

                // 双脚落地：踝关节离地 0–12 cm（踝骨本身高于脚底），左右一致
                Assert.Greater(lAnkle.position.y, -0.01f, $"左踝穿地：{lAnkle.position.y:F3}");
                Assert.Less(lAnkle.position.y, 0.12f, $"左踝过高（脚悬空）：{lAnkle.position.y:F3}");
                Assert.AreEqual(lAnkle.position.y, rAnkle.position.y, 0.01f, "左右踝高度应一致");
                Assert.AreEqual(-lAnkle.position.x, rAnkle.position.x, 0.01f, "左右腿应关于中线对称");

                // 躯干长度合理：头应比髋高 50–70 cm
                float torso = head.position.y - hips.position.y;
                Assert.That(torso, Is.InRange(0.50f, 0.70f), $"头髋距离异常：{torso:F3} m");
            }
            finally
            {
                view.DisposeCharacterResources();
            }
        }

        [UnityTest]
        public IEnumerator Apply_KeepsLeftAndRightArmsSymmetric()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, error);

            try
            {
                view.OnCharacterActivated();
                yield return null;

                var animator = view.Animator;
                Transform l = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform r = animator.GetBoneTransform(HumanBodyBones.RightHand);

                // 静置姿态必须左右对称：早期用同一组欧拉角摆双臂，
                // Biped 左右镜像轴不同，实测右臂比左臂高 19 cm
                Assert.AreEqual(-l.position.x, r.position.x, 0.02f,
                    $"左右手 X 应镜像对称：L={l.position.x:F3} R={r.position.x:F3}");
                Assert.AreEqual(l.position.y, r.position.y, 0.02f,
                    $"左右手高度应一致：L={l.position.y:F3} R={r.position.y:F3}");
            }
            finally
            {
                view.DisposeCharacterResources();
            }
        }

        [UnityTest]
        public IEnumerator Restore_ReturnsToBindPose()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, error);

            try
            {
                var animator = view.Animator;
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Vector3 bindHips = hips.position;
                Vector3 bindRoot = view.RootTransform.position;

                view.OnCharacterActivated();
                yield return null;
                Assert.Greater(Mathf.Abs(hips.position.y - bindHips.y), 0.05f, "坐姿应明显改变髋高");

                view.OnCharacterDeactivated();
                yield return null;

                Assert.AreEqual(bindHips.y, hips.position.y, 0.02f, "停用后髋高应回到绑定姿态");
                Assert.AreEqual(bindRoot.z, view.RootTransform.position.z, 0.02f,
                    "停用后根节点应回到绑定位置");
            }
            finally
            {
                view.DisposeCharacterResources();
            }
        }
    }
}
