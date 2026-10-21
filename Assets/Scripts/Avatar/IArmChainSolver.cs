using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 手臂解算策略：把「手腕要到哪里」翻译成对该骨架的具体操作。
    ///
    /// <para>
    /// 为什么必须分两种：<see cref="HandIKController"/> 改造前把手臂当成**视觉胶囊**驱动
    /// ——直接写 <c>wrist.position</c>、用 <c>PlaceSegment</c> 改 <c>localScale.y</c> 把胶囊拉长。
    /// 这对程序化角色是合理做法（胶囊本来就是拿来看的），但对**蒙皮骨架**是毁灭性的：
    /// 缩放骨骼会拉长网格、直接搬腕骨会让前臂与手断开、并破坏蒙皮权重 → 表现为
    /// 「人物破面 + 手不跟键」。所以蒙皮角色必须只用**旋转**解算。
    /// </para>
    ///
    /// <para>
    /// 默认策略保持与改造前逐位一致（<see cref="CapsuleArmChainSolver"/>），
    /// 现有 466 条 EditMode / 114 条 PlayMode 用例都建立在该行为上。
    /// </para>
    /// </summary>
    public interface IArmChainSolver
    {
        /// <summary>
        /// 把手臂解到 <paramref name="wristTarget"/>，返回解算后**实际**的腕部世界位置
        /// （够不到时会与目标不同，调用方据此统计误差，不得假装到位）。
        /// </summary>
        Vector3 Solve(Transform shoulder, Transform upperArm, Transform foreArm, Transform elbowVisual,
            Transform wrist, Vector3 wristTarget, bool leftHand);
    }

    /// <summary>
    /// 程序化胶囊手臂：平移腕骨 + 拉伸两段胶囊 + 摆肘球。
    /// 与 <c>HandIKController</c> 改造前的 <c>StretchArm</c> 逐位一致。
    /// </summary>
    public sealed class CapsuleArmChainSolver : IArmChainSolver
    {
        /// <summary>共享实例（无状态）。</summary>
        public static readonly CapsuleArmChainSolver Instance = new CapsuleArmChainSolver();

        private CapsuleArmChainSolver() { }

        /// <inheritdoc />
        public Vector3 Solve(Transform shoulder, Transform upperArm, Transform foreArm, Transform elbowVisual,
            Transform wrist, Vector3 wristTarget, bool leftHand)
        {
            if (wrist == null) return wristTarget;

            // 胶囊式：腕骨直接被搬到目标位置（程序化骨架没有蒙皮，搬骨骼不会破面）
            wrist.position = wristTarget;

            if (shoulder == null || upperArm == null) return wrist.position;

            Vector3 S = shoulder.position;
            Vector3 W = wrist.position;
            Vector3 sw = W - S;
            float d = sw.magnitude;
            if (d < 1e-5f) return wrist.position;

            float l1 = AvatarBodyMetrics.UpperArmLength;
            float l2 = AvatarBodyMetrics.LowerArmLength;
            float maxReach = l1 + l2;
            if (d > maxReach * 0.999f)
            {
                float k = d / (maxReach * 0.999f);
                l1 *= k;
                l2 *= k;
            }

            Vector3 u = sw / d;
            float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float hSq = l1 * l1 - a * a;
            float h = hSq > 0f ? Mathf.Sqrt(hSq) : 0f;

            Vector3 E = S + u * a + ArmSolveHint.ElbowOut(u, leftHand) * h;

            PlaceCapsule(upperArm, S, E);
            if (foreArm != null) PlaceCapsule(foreArm, E, W);
            if (elbowVisual != null) elbowVisual.position = E;

            return wrist.position;
        }

        /// <summary>把一根胶囊摆到 a→b：中点定位、up 对齐方向、按长度设置 y 缩放（胶囊原始高度为 2）。</summary>
        private static void PlaceCapsule(Transform seg, Vector3 a, Vector3 b)
        {
            if (seg == null) return;
            Vector3 delta = b - a;
            float len = delta.magnitude;
            if (len < 1e-5f) return;
            seg.position = a + delta * 0.5f;
            seg.up = delta / len;
            var sc = seg.localScale;
            seg.localScale = new Vector3(sc.x, len * 0.5f, sc.z);
        }
    }

    /// <summary>
    /// 蒙皮骨架手臂：<b>纯旋转</b>的两段 IK。
    ///
    /// <para>
    /// 做法：解出肘位置后，把上臂与前臂各自「绕自身原点旋转」到指向下一关节，
    /// 全程<b>不写 position、不改 localScale</b>。骨骼的「长度方向」用其子骨位置推出，
    /// 因此不依赖具体骨骼的局部轴约定（Biped / Mixamo / 自建骨架都适用）。
    /// </para>
    ///
    /// <para>
    /// 够不到时不假装到位：把目标夹到可达球面上，返回真实的腕部位置，由调用方统计误差。
    /// </para>
    /// </summary>
    public sealed class RotationalArmChainSolver : IArmChainSolver
    {
        /// <summary>共享实例（无状态）。</summary>
        public static readonly RotationalArmChainSolver Instance = new RotationalArmChainSolver();

        private RotationalArmChainSolver() { }

        /// <inheritdoc />
        public Vector3 Solve(Transform shoulder, Transform upperArm, Transform foreArm, Transform elbowVisual,
            Transform wrist, Vector3 wristTarget, bool leftHand)
        {
            // elbowVisual 是程序化角色专用的「肘球」，蒙皮骨架不需要也不允许移动它
            if (upperArm == null || foreArm == null || wrist == null) return wristTarget;

            Vector3 S = upperArm.position;
            float l1 = Vector3.Distance(upperArm.position, foreArm.position);
            float l2 = Vector3.Distance(foreArm.position, wrist.position);
            if (l1 < 1e-4f || l2 < 1e-4f) return wrist.position;

            Vector3 toTarget = wristTarget - S;
            float d = toTarget.magnitude;
            if (d < 1e-5f) return wrist.position;

            float maxReach = (l1 + l2) * 0.999f;
            Vector3 target = d > maxReach ? S + toTarget * (maxReach / d) : wristTarget;

            float dd = Vector3.Distance(S, target);
            float a = (l1 * l1 - l2 * l2 + dd * dd) / (2f * dd);
            float hSq = l1 * l1 - a * a;
            float h = hSq > 0f ? Mathf.Sqrt(hSq) : 0f;

            Vector3 u = (target - S) / dd;
            Vector3 E = S + u * a + ArmSolveHint.ElbowOut(u, leftHand) * h;

            RotateBoneToward(upperArm, E);
            RotateBoneToward(foreArm, target);

            return wrist.position;
        }

        /// <summary>
        /// 绕骨骼原点把「当前长度方向」转到指向 <paramref name="toward"/>。
        /// 用 <c>FromToRotation</c> 而不是 <c>LookRotation</c>：后者隐含「骨骼朝 +Z」，
        /// 而 Biped / Mixamo 的骨骼朝向各不相同，只有前者与轴约定无关。
        /// </summary>
        private static void RotateBoneToward(Transform bone, Vector3 toward)
        {
            if (bone == null) return;

            Vector3 desired = toward - bone.position;
            if (desired.sqrMagnitude < 1e-10f) return;
            desired.Normalize();

            Transform child = bone.childCount > 0 ? bone.GetChild(0) : null;
            Vector3 current = child != null
                ? child.position - bone.position
                : bone.rotation * Vector3.forward;

            if (current.sqrMagnitude < 1e-10f) return;

            Quaternion delta = Quaternion.FromToRotation(current.normalized, desired);
            bone.rotation = delta * bone.rotation;
        }
    }

    /// <summary>肘部外翻方向（两种解算共用，保证观感一致）。</summary>
    internal static class ArmSolveHint
    {
        /// <summary>世界坐标下角色左手在 +X；肘朝身体外侧、略向下。</summary>
        public static Vector3 ElbowOut(Vector3 u, bool leftHand)
        {
            float sideSign = leftHand ? 1f : -1f;
            Vector3 hint = new Vector3(sideSign * 0.85f, -0.55f, 0.10f);
            Vector3 v = hint - u * Vector3.Dot(hint, u);
            if (v.sqrMagnitude < 1e-6f) v = Vector3.Cross(u, Vector3.forward);
            return v.normalized;
        }
    }
}
