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
                // 资产现状（2026-10-31 更新）：7 张贴图已入库（body/head 的 color+normal、
                // 两组 specular、opacity），并由 RocketboxTextureLibrary 在运行时绑定到实例材质
                // ——FBX 内的贴图路径指向建模机临时目录，导入器永远解析不到，必须代码补。
                // 因此校验应当**通过**；同时必须确认贴图真的绑上了，
                // 不能出现「校验通过、实际仍是白模」的假通过。
                var validation = view.ValidateRig();
                Assert.IsTrue(validation.Ok, "贴图已补齐，角色应通过校验，当前：" + validation.Message);

                Assert.IsNotNull(view.RootTransform, "视图应暴露根节点");
                var smrs = view.RootTransform.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int totalSlots = 0, texturedSlots = 0;
                foreach (var smr in smrs)
                {
                    foreach (var mat in smr.sharedMaterials)
                    {
                        if (mat == null) continue;
                        totalSlots++;
                        bool hasTex = (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
                                   || (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null);
                        if (hasTex) texturedSlots++;
                    }
                }
                Assert.Greater(totalSlots, 0, "角色应至少有一个材质槽");
                Assert.AreEqual(totalSlots, texturedSlots,
                    string.Format("所有材质槽都必须绑定主贴图（{0}/{1}）——否则仍会露出白模", texturedSlots, totalSlots));

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

        /// <summary>
        /// 真实 Biped 骨骼上的轴推导验证：给每根手指的 MCP 施加 30° 弯曲，
        /// 指尖必须朝**掌心**（−手背法线）移动，而不是横移或抬起。
        /// <para>
        /// 这是「新角色能不能弹」的核心断言：改造前 FingerPoseController 假设骨骼局部 +X 是弯曲轴，
        /// Biped 骨架套用后指尖会横移/上抬，手指像在抽筋而不是按琴键。
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator JointAxes_CurlMovesRealFingertipsTowardPalm()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, "角色实例化失败：" + error);

            try
            {
                var axes = view.JointAxes;
                Assert.IsNotNull(axes, "角色应提供关节轴策略");
                Assert.IsTrue(axes is ImportedFingerJointAxes, "导入角色应使用推导轴，而不是默认局部 X 轴");

                var imported = (ImportedFingerJointAxes)axes;
                Assert.GreaterOrEqual(imported.JointCount, 27,
                    $"双手至少应有 27 个关节登记铰链轴（实测 {imported.JointCount}）");
                Assert.AreEqual(10, imported.FingerCount, "左右手各 5 指的指根瞄准数据都应登记");

                RocketboxHandRig[] hands = { view.RocketboxLeftHand, view.RocketboxRightHand };
                string[] labels = { "左手", "右手" };

                for (int h = 0; h < hands.Length; h++)
                {
                    RocketboxHandRig hand = hands[h];
                    // BackNormal 是腕骨局部空间的向量，掌心朝向要转到世界再比较
                    Vector3 palmDirection = -(hand.Wrist.rotation * hand.BackNormal);

                    for (int f = 0; f < RocketboxHandRig.FingersPerHand; f++)
                    {
                        Assert.IsTrue(hand.TryGetJoints(f, out Transform proximal, out _, out _));
                        Transform tip = hand.GetFingertip(f);
                        Assert.IsNotNull(tip);

                        Vector3 before = tip.position;
                        Quaternion original = proximal.localRotation;

                        proximal.localRotation = axes.CurlLocalRotation(proximal, 0, 30f);
                        Vector3 after = tip.position;

                        Vector3 motion = after - before;
                        Assert.Greater(motion.magnitude, 0.002f,
                            $"{labels[h]}第 {f} 指弯曲 30° 后指尖几乎没动（{motion.magnitude * 1000f:0.#} mm）");
                        // palmDirection 是「掌心朝向」，指尖屈曲应朝这个方向走，dot 应为正。
                        Assert.Greater(Vector3.Dot(motion, palmDirection), 0f,
                            $"{labels[h]}第 {f} 指弯曲方向错误：指尖没有朝掌心移动" +
                            $"（dot={Vector3.Dot(motion, palmDirection) * 1000f:0.##} mm）");

                        proximal.localRotation = original;
                    }
                }
            }
            finally
            {
                view.DisposeCharacterResources();
            }

            yield return null;
        }

        /// <summary>
        /// 演奏层默认策略必须与改造前逐位一致——否则程序化角色的 460 条用例会集体漂移。
        /// </summary>
        [UnityTest]
        public IEnumerator DefaultAxes_EqualLegacyEulerRotation()
        {
            var axes = PianoTeacher3D.Avatar.DefaultFingerJointAxes.Instance;

            var go = new GameObject("Joint");
            try
            {
                go.transform.localRotation = Quaternion.Euler(12f, 8f, -4f);   // 非单位绑定姿态
                Assert.AreEqual(Quaternion.Euler(30f, 0f, 0f),
                    axes.CurlLocalRotation(go.transform, 0, 30f),
                    "默认策略必须忽略绑定姿态，直接返回 Euler(deg,0,0)（与改造前一致）");
            }
            finally
            {
                Object.Destroy(go);
            }

            yield return null;
        }

        /// <summary>
        /// 会话层切换：贴图补齐后，写实女性应能真正切过去。
        /// <para>
        /// 原用例断言的是「资产不完整 → 拒绝并保留原角色」，依据是 2026-10-20 的资产现状
        /// （只有 head 两张贴图）。贴图入库后这条前提不再成立，改为验证正向路径；
        /// 「门禁仍能拦住坏资产」由 <c>CharacterAssetValidatorTests</c>（EditMode）直接覆盖，
        /// 保护没有丢失，只是从「依赖真实资产残缺」换成了「代码构造残缺」。
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Session_ActivatesTexturedCharacter()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<PianoTeacher3D.Core.LessonSessionController>();
            Assert.IsNotNull(session);

            string before = session.CurrentCharacterView != null
                ? session.CurrentCharacterView.CharacterId
                : string.Empty;
            Assert.IsNotEmpty(before, "场景装配完成后应已有当前角色");

            var result = session.ActivateCharacter(_profile);

            Assert.IsTrue(result.Succeeded, "贴图补齐后角色应可切换，当前：" + result.Error);
            Assert.AreEqual(CharacterRegistry.RocketboxFemaleId, session.CurrentCharacterView.CharacterId,
                "切换成功后当前角色应为写实女性（原角色 " + before + "）");
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
