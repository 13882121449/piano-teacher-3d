using System.Collections;
using NUnit.Framework;
using PianoTeacher3D.Characters;
using UnityEngine;
using UnityEngine.TestTools;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// Rocketbox 导入角色的<b>结构</b>验证（P2）：预制体可加载、骨骼齐全、Humanoid Animator 有效。
    /// <para>
    /// 只断言结构，不断言指尖精度与坐姿角度——那些需要人工在编辑器里看，见
    /// <c>Docs/character-asset-requirements.md</c> 的「待标定项」。
    /// </para>
    /// </summary>
    public class CharacterRocketboxTests
    {
        private CharacterProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = CharacterRegistry.CreateRocketbox();
        }

        [TearDown]
        public void TearDown()
        {
            if (_profile != null) Object.DestroyImmediate(_profile);
            _profile = null;
        }

        [UnityTest]
        public IEnumerator Prefab_IsLoadableFromResources()
        {
            var prefab = Resources.Load<GameObject>(CharacterRegistry.RocketboxResourcesPath);
            Assert.IsNotNull(prefab,
                $"Resources/{CharacterRegistry.RocketboxResourcesPath} 加载失败（检查 FBX 导入设置）");
            yield return null;
        }

        [UnityTest]
        public IEnumerator View_LoadsAndValidatesRig()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, "角色实例化失败：" + error);

            try
            {
                var validation = view.ValidateRig();
                Assert.IsTrue(validation.Ok, "骨架校验失败：" + validation.Message);

                Assert.IsTrue(view.IsActivated == false, "未激活前 IsActivated 应为 false");
                view.OnCharacterActivated();
                Assert.IsTrue(view.IsActivated);
                Assert.IsNotNull(view.RootTransform);
                Assert.IsTrue(view.RootTransform.gameObject.activeSelf);
            }
            finally
            {
                view.OnCharacterDeactivated();
                view.DisposeCharacterResources();
            }

            yield return null;
            Assert.IsTrue(view.RootTransform == null, "释放后根节点应被销毁");
        }

        [UnityTest]
        public IEnumerator BothHands_HaveFiveFingersWithThreeSegments()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, "角色实例化失败：" + error);

            try
            {
                AssertHand(view.RocketboxLeftHand, "左手");
                AssertHand(view.RocketboxRightHand, "右手");
            }
            finally
            {
                view.DisposeCharacterResources();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator HandRig_FingertipsAndLengthsAreUsable()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, "角色实例化失败：" + error);

            try
            {
                var hand = view.RocketboxLeftHand;
                Assert.IsNotNull(hand);

                var left = hand;
                var right = view.RocketboxRightHand;
                Assert.IsNotNull(right, "右手 rig 缺失");

                for (int f = 0; f < RocketboxHandRig.FingersPerHand; f++)
                {
                    var tip = hand.GetFingertip(f);
                    Assert.IsNotNull(tip, $"左手第 {f} 指缺少指尖节点");

                    float length = hand.GetFingerLength(f);
                    Assert.Greater(length, 0.01f, $"左手第 {f} 指的静止长度不合理：{length}");

                    Assert.IsTrue(hand.TryGetJoints(f, out var p, out var m, out var d));
                    Assert.IsNotNull(p);
                    Assert.IsNotNull(m);
                    Assert.IsNotNull(d);
                }

                // 规范基：三轴互相垂直且为单位向量（推导算法自洽性检查）
                Assert.AreEqual(1f, hand.FingerDirection.magnitude, 0.01f);
                Assert.AreEqual(1f, hand.BackNormal.magnitude, 0.01f);
                Assert.AreEqual(1f, hand.ThumbSide.magnitude, 0.01f);
                Assert.AreEqual(0f, Vector3.Dot(hand.FingerDirection, hand.BackNormal), 0.02f);
                Assert.AreEqual(0f, Vector3.Dot(hand.FingerDirection, hand.ThumbSide), 0.02f);

                // 左右手各自的规范基必须自洽（LookRotation 与三个基向量一致）
                Assert.Less(Vector3.Angle(left.CanonicalToWrist * Vector3.forward, left.FingerDirection), 1f);
                Assert.Less(Vector3.Angle(right.CanonicalToWrist * Vector3.forward, right.FingerDirection), 1f);
                Assert.Less(Vector3.Angle(left.CanonicalToWrist * Vector3.up, left.BackNormal), 1f);
            }
            finally
            {
                view.DisposeCharacterResources();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Animator_IsHumanoidAndAvatarIsValid()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, "角色实例化失败：" + error);

            try
            {
                var animator = view.Animator;
                Assert.IsNotNull(animator, "角色缺少 Animator 组件");
                Assert.IsNotNull(animator.avatar, "Animator 未绑定 Avatar（检查 FBX 的 Rig 导入设置）");
                Assert.IsTrue(animator.avatar.isValid, "Avatar 无效");
                Assert.IsTrue(animator.avatar.isHuman, "Avatar 不是 Humanoid");

                var skeleton = view.Skeleton;
                Assert.IsNotNull(skeleton);
                Assert.IsTrue(skeleton.IsHumanoid);
                Assert.IsNotNull(skeleton.Hips, "缺少髋骨映射");
                Assert.IsNotNull(skeleton.LeftHand, "缺少左手映射");
                Assert.IsNotNull(skeleton.RightHand, "缺少右手映射");
            }
            finally
            {
                view.DisposeCharacterResources();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator BoneNameTable_MatchesImportedHierarchy()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, "角色实例化失败：" + error);

            try
            {
                var index = CharacterAssetValidator.BuildTransformIndex(view.RootTransform.gameObject);
                var required = CharacterAssetValidator.BuildBipedHandBoneNames();
                var missing = new System.Collections.Generic.List<string>();
                for (int i = 0; i < required.Length; i++)
                {
                    if (!index.ContainsKey(required[i])) missing.Add(required[i]);
                }
                Assert.AreEqual(0, missing.Count,
                    "缺失骨骼：" + string.Join("、", missing));
            }
            finally
            {
                view.DisposeCharacterResources();
            }

            yield return null;
        }

        private static void AssertHand(RocketboxHandRig hand, string label)
        {
            Assert.IsNotNull(hand, label + " rig 为空");
            Assert.IsTrue(hand.IsValid, label + " rig 不完整");
            Assert.IsNotNull(hand.Wrist, label + " 缺少腕骨");
            Assert.IsNotNull(hand.Palm, label + " 缺少掌心中继节点");
            Assert.AreEqual(RocketboxHandRig.FingersPerHand, hand.Fingers.Count, label + " 手指数不对");
        }
    }
}
