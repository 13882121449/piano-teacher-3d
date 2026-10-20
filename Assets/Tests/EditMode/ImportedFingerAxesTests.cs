using System.Collections.Generic;
using NUnit.Framework;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Characters;
using UnityEngine;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 关节轴策略的几何正确性（方案 6.3「关节旋转限位与小/最大弯曲角」、6.4「手工骨架回退」）。
    ///
    /// <para>
    /// 这里不跑 IK、不依赖场景：用手搭一根三节手指，验证「弯曲 30° 之后指尖确实朝掌心移动」。
    /// 这条断言正是改造前失败的原因——Biped 骨骼套用 <c>Euler(deg,0,0)</c> 时指尖会横移/扭转，
    /// 而不是朝掌心收。
    /// </para>
    /// </summary>
    public class ImportedFingerAxesTests
    {
        private GameObject _hand;
        private FakeFingerRig _rig;

        /// <summary>假手：一根手指，三节骨骼，手背朝 +Y，手指沿 −Z 伸出。</summary>
        private sealed class FakeFingerRig : IHandRig, IFingerJointRig
        {
            private readonly List<Transform> _fingers = new List<Transform>();
            public Transform Proximal, Intermediate, Distal;

            public void Add(Transform proximal, Transform intermediate, Transform distal)
            {
                Proximal = proximal;
                Intermediate = intermediate;
                Distal = distal;
                _fingers.Add(proximal);
            }

            public Transform Wrist => Proximal;
            public Transform Palm => Proximal;
            public IReadOnlyList<Transform> Fingers => _fingers;
            public Transform GetFingertip(int fingerIndex) => Distal;
            public float GetFingerLength(int fingerIndex) =>
                Vector3.Distance(Proximal.position, Intermediate.position) +
                Vector3.Distance(Intermediate.position, Distal.position);

            public bool TryGetJoints(int fingerIndex, out Transform proximal,
                out Transform intermediate, out Transform distal)
            {
                proximal = Proximal;
                intermediate = Intermediate;
                distal = Distal;
                return fingerIndex == 0 && Proximal != null;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _hand = new GameObject("Hand");
            _hand.transform.position = Vector3.zero;
            _hand.transform.rotation = Quaternion.identity;

            Transform proximal = new GameObject("Proximal").transform;
            proximal.SetParent(_hand.transform, false);
            proximal.localPosition = Vector3.zero;

            Transform intermediate = new GameObject("Intermediate").transform;
            intermediate.SetParent(proximal, false);
            intermediate.localPosition = new Vector3(0f, 0f, -0.030f);

            Transform distal = new GameObject("Distal").transform;
            distal.SetParent(intermediate, false);
            distal.localPosition = new Vector3(0f, 0f, -0.025f);

            _rig = new FakeFingerRig();
            _rig.Add(proximal, intermediate, distal);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hand != null) Object.DestroyImmediate(_hand);
        }

        [Test]
        public void AddHand_RegistersAllThreeJointsAndFingerAim()
        {
            var axes = new ImportedFingerJointAxes();
            bool ok = axes.AddHand(_rig, Vector3.up, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(3, axes.JointCount, "三节骨骼都应登记铰链轴（含没有指尖节点的 DIP）");
            Assert.AreEqual(1, axes.FingerCount, "指根瞄准数据应登记");
        }

        [Test]
        public void Curl_MovesFingertipTowardPalm()
        {
            var axes = new ImportedFingerJointAxes();
            Assert.IsTrue(axes.AddHand(_rig, Vector3.up, out string error), error);

            Vector3 before = _rig.GetFingertip(0).position;
            _rig.Proximal.localRotation = axes.CurlLocalRotation(_rig.Proximal, 0, 30f);
            Vector3 after = _rig.GetFingertip(0).position;

            Vector3 motion = after - before;
            Assert.Greater(motion.magnitude, 0.005f, "弯曲 30° 指尖应有可见位移");
            Assert.Less(Vector3.Dot(motion, Vector3.down), 0f,
                "弯曲应让指尖朝掌心（−手背法线 = −Y）移动，而不是横移或扭转");
        }

        [Test]
        public void Curl_KeepsBindOrientationOfBone()
        {
            var axes = new ImportedFingerJointAxes();
            Assert.IsTrue(axes.AddHand(_rig, Vector3.up, out string error), error);

            // 零度弯曲必须回到绑定姿态，不能把骨架自带的局部朝向抹掉
            _rig.Proximal.localRotation = axes.CurlLocalRotation(_rig.Proximal, 0, 0f);
            Assert.Less(Quaternion.Angle(Quaternion.identity, _rig.Proximal.localRotation), 0.01f);

            // 弯曲后骨长不变（铰链只旋转不缩放）
            float before = Vector3.Distance(_rig.Proximal.position, _rig.Distal.position);
            _rig.Proximal.localRotation = axes.CurlLocalRotation(_rig.Proximal, 0, 55f);
            float after = Vector3.Distance(_rig.Proximal.position, _rig.Distal.position);
            Assert.AreEqual(before, after, 1e-4f, "弯曲不得改变关节到末节的距离");
        }

        [Test]
        public void WorldCurlAxis_IsPerpendicularToBone()
        {
            var axes = new ImportedFingerJointAxes();
            Assert.IsTrue(axes.AddHand(_rig, Vector3.up, out string error), error);

            Vector3 axis = axes.WorldCurlAxis(_rig.Proximal, 0);
            Vector3 bone = (_rig.Intermediate.position - _rig.Proximal.position).normalized;

            Assert.AreEqual(1f, axis.magnitude, 0.01f, "轴必须是单位向量");
            Assert.AreEqual(0f, Vector3.Dot(axis, bone), 0.02f, "铰链轴应与骨方向垂直");
        }

        [Test]
        public void AddHand_DegenerateBackNormal_IsRejected()
        {
            var axes = new ImportedFingerJointAxes();
            bool ok = axes.AddHand(_rig, Vector3.zero, out string error);

            Assert.IsFalse(ok);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void DefaultAxes_BehaveAsBefore()
        {
            // 默认策略必须与改造前的 Quaternion.Euler(deg,0,0) 逐位一致
            var axes = DefaultFingerJointAxes.Instance;
            Assert.AreEqual(Quaternion.Euler(30f, 0f, 0f),
                axes.CurlLocalRotation(_rig.Proximal, 0, 30f));
            Assert.AreEqual(_rig.Proximal.rotation * Vector3.right,
                axes.WorldCurlAxis(_rig.Proximal, 0));
        }
    }
}
