using System.Collections;
using NUnit.Framework;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Characters;
using UnityEngine;
using UnityEngine.TestTools;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 手臂解算策略的几何契约（方案 6.3「指尖、手掌、腕部、肘部按层级协同求解」）。
    ///
    /// <para>
    /// 必须钉住的核心事实：蒙皮骨架只能用**旋转**驱动。
    /// 改造前 <c>HandIKController</c> 直接写 <c>wrist.position</c> 并用 <c>localScale.y</c>
    /// 拉伸胶囊——在蒙皮模型上会拉长网格、让前臂与手断开、破坏权重，
    /// 表现就是「人物破面 + 手不跟键」。这组用例把该约束变成可执行断言。
    /// </para>
    /// </summary>
    public class ArmChainSolverTests
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
            Assert.IsNotNull(_profile, "角色库中应有一个外部预制体角色用于本组用例");
        }

        [UnityTest]
        public IEnumerator Rotational_ReachesReachableTarget_WithoutTouchingPositionOrScale()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, "角色实例化失败：" + error);

            try
            {
                Transform upper = view.LeftArm;
                Transform fore = view.LeftForeArm;
                Transform wrist = view.LeftHand.Wrist;
                Assert.IsNotNull(upper, "缺少上臂");
                Assert.IsNotNull(fore, "缺少前臂");
                Assert.IsNotNull(wrist, "缺少腕骨");

                float l1 = Vector3.Distance(upper.position, fore.position);
                float l2 = Vector3.Distance(fore.position, wrist.position);
                Vector3 scaleUpperBefore = upper.localScale;
                Vector3 scaleForeBefore = fore.localScale;
                Vector3 wristBefore = wrist.position;

                Vector3 S = upper.position;
                // 可达目标：朝前下方 85% 臂长处（模拟手落到键面上方）
                Vector3 target = S + new Vector3(0.05f, -0.22f, -0.55f).normalized * ((l1 + l2) * 0.85f);

                Vector3 solved = RotationalArmChainSolver.Instance.Solve(
                    view.LeftShoulder, upper, fore, null, wrist, target, true);

                Assert.Less(Vector3.Distance(solved, target), 0.025f,
                    $"可达目标应解到 25 mm 内，实际 {(Vector3.Distance(solved, target) * 1000f):0.#} mm");
                Assert.Greater(Vector3.Distance(wrist.position, wristBefore), 0.01f,
                    "腕部应真的移动了（否则说明解算没生效）");

                Assert.Less(Vector3.Distance(upper.localScale, scaleUpperBefore), 1e-4f,
                    "蒙皮骨骼的缩放绝不能被改动（会拉长网格）");
                Assert.Less(Vector3.Distance(fore.localScale, scaleForeBefore), 1e-4f,
                    "前臂缩放绝不能被改动");

                Assert.AreEqual(l1, Vector3.Distance(upper.position, fore.position), 1e-3f,
                    "上臂骨长必须保持不变（纯旋转不改变骨长）");
                Assert.AreEqual(l2, Vector3.Distance(fore.position, wrist.position), 1e-3f,
                    "前臂骨长必须保持不变");
            }
            finally
            {
                view.DisposeCharacterResources();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Rotational_UnreachableTarget_DoesNotPretendToReach()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, error);

            try
            {
                Transform upper = view.LeftArm;
                Transform fore = view.LeftForeArm;
                Transform wrist = view.LeftHand.Wrist;

                float l1 = Vector3.Distance(upper.position, fore.position);
                float l2 = Vector3.Distance(fore.position, wrist.position);
                Vector3 S = upper.position;
                Vector3 target = S + new Vector3(0f, 0f, -5f);   // 远超臂长

                Vector3 solved = RotationalArmChainSolver.Instance.Solve(
                    view.LeftShoulder, upper, fore, null, wrist, target, true);

                Assert.Greater(Vector3.Distance(solved, target), 1f,
                    "够不到时不得假装到位（返回值必须反映真实腕位）");
                Assert.AreEqual(l1 + l2, Vector3.Distance(S, solved), l1 * 0.06f + 0.02f,
                    "出手方向应在可达球面上");
            }
            finally
            {
                view.DisposeCharacterResources();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Capsule_Solver_KeepsLegacyBehaviourForProceduralArm()
        {
            var root = new GameObject("CapsuleArm");
            try
            {
                Transform shoulder = new GameObject("Shoulder").transform;
                shoulder.SetParent(root.transform, false);
                shoulder.position = Vector3.zero;

                Transform upper = new GameObject("Upper").transform;
                upper.SetParent(root.transform, false);
                upper.position = new Vector3(0f, -0.14f, 0f);

                Transform fore = new GameObject("Fore").transform;
                fore.SetParent(root.transform, false);
                fore.position = new Vector3(0f, -0.28f, 0f);

                Transform elbow = new GameObject("Elbow").transform;
                elbow.SetParent(root.transform, false);

                Transform wrist = new GameObject("Wrist").transform;
                wrist.SetParent(root.transform, false);
                wrist.position = new Vector3(0f, -0.42f, 0f);

                Vector3 target = new Vector3(0.10f, -0.30f, -0.20f);
                Vector3 solved = CapsuleArmChainSolver.Instance.Solve(
                    shoulder, upper, fore, elbow, wrist, target, true);

                Assert.AreEqual(target, solved, "胶囊式必须把腕骨精确搬到目标（改造前的语义）");
                Assert.AreEqual(target, wrist.position);

                // 胶囊式的语义就是「拉伸视觉件」：这里显式确认它确实改了缩放，
                // 从而说明为什么蒙皮骨架不能用它
                Assert.Greater(upper.localScale.y, 0.05f, "胶囊应被拉伸到两段长度");
            }
            finally
            {
                Object.Destroy(root);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator AvatarManager_PicksRotationalSolver_OnlyForSkinnedCharacters()
        {
            var view = RocketboxCharacterView.TryCreate(_profile, null, out string error);
            Assert.IsNotNull(view, error);

            try
            {
                var root = new GameObject("AvatarHost");
                var manager = root.AddComponent<PianoTeacher3D.Avatar.AvatarManager>();
                try
                {
                    // 未构建内置角色时，CurrentView 为 null → 默认胶囊式
                    Assert.IsTrue(manager.ArmSolver is CapsuleArmChainSolver,
                        "没有外部角色时必须是胶囊式（内置角色行为不能变）");
                }
                finally
                {
                    Object.Destroy(root);
                }
            }
            finally
            {
                view.DisposeCharacterResources();
            }

            yield return null;
        }
    }
}
