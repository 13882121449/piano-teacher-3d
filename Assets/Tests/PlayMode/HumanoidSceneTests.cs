using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 场景内 Humanoid 骨架集成验证（对应方案 4.1「支持 Humanoid 骨骼的模型」）。
    /// 验证三个预设角色都持有一套 Unity 认可的合法 Humanoid 骨架与绑定 Avatar 的 Animator。
    /// </summary>
    public class HumanoidSceneTests
    {
        private AvatarManager _avatar;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            _avatar = Object.FindFirstObjectByType<AvatarManager>();
            Assert.IsNotNull(_avatar, "PianoLesson 场景应包含 AvatarManager");
        }

        [UnityTest]
        public IEnumerator CurrentAvatar_HasValidHumanoidRig()
        {
            Assert.IsTrue(_avatar.HasHumanoidRig, "当前角色应持有合法 Humanoid 骨架");
            Assert.IsNotNull(_avatar.CurrentSkeleton.Avatar);
            Assert.IsTrue(_avatar.CurrentSkeleton.Avatar.isHuman);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AllPresets_HaveValidHumanoidRigs()
        {
            for (int i = 0; i < AvatarManager.AvatarCount; i++)
            {
                var skeleton = _avatar.GetSkeleton(i);
                Assert.IsNotNull(skeleton, $"角色索引 {i} 应已构建骨架");
                Assert.IsTrue(skeleton.IsHumanoid, $"角色索引 {i} 的 Avatar 应为合法 Humanoid");
                Assert.IsTrue(skeleton.LeftFingers[0] != null && skeleton.RightFingers[4] != null,
                    $"角色索引 {i} 应具备左右手各 5 根手指骨骼");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SwitchingAvatar_KeepsHumanoidRigValid()
        {
            for (int i = 0; i < AvatarManager.AvatarCount; i++)
            {
                _avatar.SwitchAvatar(i);
                yield return null;

                Assert.IsTrue(_avatar.HasHumanoidRig, $"切换到角色 {i} 后骨架应仍为合法 Humanoid");
                Assert.AreEqual(i, _avatar.CurrentIndex);
            }

            _avatar.SwitchAvatar(0);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HumanoidRig_HasAnimatorBoundToAvatar()
        {
            var skeleton = _avatar.CurrentSkeleton;
            Assert.IsNotNull(skeleton);

            var animator = skeleton.Root.GetComponent<Animator>();
            Assert.IsNotNull(animator, "骨架根上应绑定 Animator");
            Assert.AreEqual(skeleton.Avatar, animator.avatar, "Animator 应绑定同一 Avatar");
            Assert.IsFalse(animator.applyRootMotion, "不应启用根运动（骨架仅作坐标基准）");
            yield return null;
        }

        [UnityTest]
        public IEnumerator HumanoidRig_DoesNotAffectVisibleGeometry()
        {
            // 骨架为纯坐标节点，不应引入任何 Renderer（避免与程序化网格重复渲染）
            var skeleton = _avatar.CurrentSkeleton;
            var renderers = skeleton.Root.GetComponentsInChildren<Renderer>(true);

            Assert.AreEqual(0, renderers.Length, "骨架节点不应包含任何 Renderer");
            yield return null;
        }
    }
}
