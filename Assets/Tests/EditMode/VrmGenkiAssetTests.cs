using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// genki.vrm（VRoid AvatarSample_A，VRM 0.x）经 UniVRM VRM-1.0 导入器迁移后的资产门禁：
    /// 必须是 Humanoid、五指骨骼齐全、材质走 URP 版 MToon10、带 Vrm10Instance（表情 / 弹簧骨骼）。
    /// </summary>
    public class VrmGenkiAssetTests
    {
        private const string ResourcesPath = "Characters/VRoid/Genki";

        private static GameObject LoadPrefab()
        {
            var prefab = Resources.Load<GameObject>(ResourcesPath);
            Assert.IsNotNull(prefab, $"Resources/{ResourcesPath} 加载失败（VRM 导入器没有生成预制体？）");
            return prefab;
        }

        [Test]
        public void Prefab_IsHumanoidWithAllFingerBones()
        {
            var prefab = LoadPrefab();
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "缺少 Animator");
            Assert.IsNotNull(animator.avatar, "缺少 Avatar");
            Assert.IsTrue(animator.avatar.isValid && animator.avatar.isHuman, "Avatar 不是有效的 Humanoid");

            var instance = Object.Instantiate(prefab);
            try
            {
                var a = instance.GetComponentInChildren<Animator>(true);
                for (var b = HumanBodyBones.LeftThumbProximal; b <= HumanBodyBones.RightLittleDistal; b++)
                    Assert.IsNotNull(a.GetBoneTransform(b), $"缺少骨骼 {b}");
                foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
                             HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
                             HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    Assert.IsNotNull(a.GetBoneTransform(b), $"缺少骨骼 {b}");

                Transform hips = a.GetBoneTransform(HumanBodyBones.Hips);
                Transform head = a.GetBoneTransform(HumanBodyBones.Head);
                Transform hand = a.GetBoneTransform(HumanBodyBones.RightHand);
                Transform index1 = a.GetBoneTransform(HumanBodyBones.RightIndexProximal);
                Transform index3 = a.GetBoneTransform(HumanBodyBones.RightIndexDistal);
                Debug.Log($"[VRM] root fwd={instance.transform.forward} hips={hips.position} head={head.position} " +
                          $"rHand={hand.position} rIndex1={index1.position} rIndex3={index3.position}");
                Assert.Greater(head.position.y, 1.2f, "身高异常（头部低于 1.2 m）");
                // VRM 1.0 规定模型面向 +Z
                Vector3 leftToRight = a.GetBoneTransform(HumanBodyBones.RightUpperArm).position -
                                      a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
                Assert.Greater(Vector3.Cross(leftToRight, Vector3.up).z, 0f, "模型没有面向 +Z");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Materials_UseUrpMToon10()
        {
            var prefab = LoadPrefab();
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0);
            var shaders = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null)
                .Select(m => m.shader.name).Distinct().ToArray();
            Debug.Log("[VRM] shaders: " + string.Join(", ", shaders));
            CollectionAssert.Contains(shaders, "VRM10/Universal Render Pipeline/MToon10");
            foreach (var s in shaders) StringAssert.DoesNotContain("Hidden/InternalErrorShader", s);
        }

        [Test]
        public void Prefab_HasVrm10InstanceAndSpringBones()
        {
            var prefab = LoadPrefab();
            var components = prefab.GetComponentsInChildren<Component>(true).Where(c => c != null)
                .Select(c => c.GetType().FullName).Distinct().ToArray();
            Debug.Log("[VRM] components: " + string.Join(", ", components));
            CollectionAssert.Contains(components, "UniVRM10.Vrm10Instance");
        }
    }
}
