using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// Humanoid 骨架与 Avatar 验证（对应方案 4.1「支持 Humanoid 骨骼的模型」/ 3.2「统一人体骨骼规范」）。
    /// </summary>
    public class HumanoidRigTests
    {
        private AvatarRigBuilder.Skeleton _skeleton;
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("RigTestRoot");
            _skeleton = AvatarRigBuilder.Build(_root.transform, 1f);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            yield return null;
        }

        [Test]
        public void Skeleton_ContainsAllRequiredHumanoidBones()
        {
            Assert.IsNotNull(_skeleton.Hips, "Hips 为 Humanoid 必需骨骼");
            Assert.IsNotNull(_skeleton.Spine);
            Assert.IsNotNull(_skeleton.Head);
            Assert.IsNotNull(_skeleton.LeftUpperArm);
            Assert.IsNotNull(_skeleton.LeftLowerArm);
            Assert.IsNotNull(_skeleton.LeftHand);
            Assert.IsNotNull(_skeleton.RightUpperArm);
            Assert.IsNotNull(_skeleton.RightLowerArm);
            Assert.IsNotNull(_skeleton.RightHand);
            Assert.IsNotNull(_skeleton.LeftUpperLeg);
            Assert.IsNotNull(_skeleton.LeftLowerLeg);
            Assert.IsNotNull(_skeleton.LeftFoot);
            Assert.IsNotNull(_skeleton.RightUpperLeg);
            Assert.IsNotNull(_skeleton.RightLowerLeg);
            Assert.IsNotNull(_skeleton.RightFoot);
        }

        [Test]
        public void Skeleton_HierarchyMatchesHumanoidConvention()
        {
            Assert.AreEqual(_skeleton.Hips, _skeleton.Spine.parent);
            Assert.AreEqual(_skeleton.Chest, _skeleton.UpperChest.parent);
            Assert.AreEqual(_skeleton.UpperChest, _skeleton.LeftShoulder.parent);
            Assert.AreEqual(_skeleton.LeftShoulder, _skeleton.LeftUpperArm.parent);
            Assert.AreEqual(_skeleton.LeftUpperArm, _skeleton.LeftLowerArm.parent);
            Assert.AreEqual(_skeleton.LeftLowerArm, _skeleton.LeftHand.parent);
            Assert.AreEqual(_skeleton.Hips, _skeleton.LeftUpperLeg.parent);
            Assert.AreEqual(_skeleton.UpperChest, _skeleton.Neck.parent);
            Assert.AreEqual(_skeleton.Neck, _skeleton.Head.parent);
        }

        [Test]
        public void Skeleton_EachHandHasFiveFingersUnderHand()
        {
            for (int f = 0; f < 5; f++)
            {
                Assert.IsNotNull(_skeleton.LeftFingers[f], $"左手第 {f} 指应存在");
                Assert.IsNotNull(_skeleton.RightFingers[f], $"右手第 {f} 指应存在");
                Assert.AreEqual(_skeleton.LeftHand, _skeleton.LeftFingers[f].parent);
                Assert.AreEqual(_skeleton.RightHand, _skeleton.RightFingers[f].parent);
            }
        }

        [Test]
        public void Avatar_IsValidAndRecognizedAsHumanoid()
        {
            Assert.IsNotNull(_skeleton.Avatar, "应成功生成 Avatar");
            Assert.IsTrue(_skeleton.Avatar.isValid, "Avatar 应为有效");
            Assert.IsTrue(_skeleton.Avatar.isHuman, "Avatar 应被 Unity 识别为 Humanoid");
            Assert.IsTrue(_skeleton.IsHumanoid);
        }

        [Test]
        public void Avatar_ExposesStandardHumanBoneMapping()
        {
            var human = _skeleton.Avatar.humanDescription.human;
            Assert.GreaterOrEqual(human.Length, 20, "应包含完整骨骼映射（躯干 + 四肢 + 手指）");

            bool hasHips = false, hasHead = false, hasLeftHand = false, hasFinger = false;
            foreach (var bone in human)
            {
                switch (bone.humanName)
                {
                    case "Hips": hasHips = true; break;
                    case "Head": hasHead = true; break;
                    case "LeftHand": hasLeftHand = true; break;
                    case "LeftThumbProximal": hasFinger = true; break;
                }
            }

            Assert.IsTrue(hasHips && hasHead && hasLeftHand, "应包含 Hips / Head / LeftHand 标准映射");
            Assert.IsTrue(hasFinger, "应包含手指骨骼映射");
        }

        [Test]
        public void Skeleton_ArmsAreTwoSegmented()
        {
            float shoulderToUpper = Vector3.Distance(_skeleton.LeftShoulder.position, _skeleton.LeftUpperArm.position);
            float upperToLower = Vector3.Distance(_skeleton.LeftUpperArm.position, _skeleton.LeftLowerArm.position);
            float lowerToHand = Vector3.Distance(_skeleton.LeftLowerArm.position, _skeleton.LeftHand.position);

            Assert.Greater(upperToLower, 0.1f, "上臂长度应显著（两段手臂）");
            Assert.Greater(lowerToHand, 0.1f, "前臂长度应显著（两段手臂）");
            Assert.Greater(shoulderToUpper, 0f);
        }

        [Test]
        public void Skeleton_ScaleAffectsProportions()
        {
            var small = AvatarRigBuilder.Build(new GameObject("SmallRoot").transform, 0.5f);
            var large = AvatarRigBuilder.Build(new GameObject("LargeRoot").transform, 1.5f);

            float smallSpan = Vector3.Distance(small.Hips.position, small.Head.position);
            float largeSpan = Vector3.Distance(large.Hips.position, large.Head.position);

            Object.Destroy(small.Root.root.gameObject);
            Object.Destroy(large.Root.root.gameObject);

            Assert.Greater(largeSpan, smallSpan, "缩放系数应影响骨架比例");
        }
    }
}
