using System.Collections.Generic;
using PianoTeacher3D.Avatar;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>坐姿参数（方案 8.2「统一模型朝向、比例、地面高度和钢琴座椅位置」）。</summary>
    public sealed class SittingPoseSettings
    {
        /// <summary>凳面高度（米）。与 <see cref="AvatarDefinition.StoolTopHeight"/> 保持一致。</summary>
        public float SeatSurfaceHeight = AvatarBodyMetrics.StoolTopHeight;

        /// <summary>髋关节中心高出凳面的距离（米）。0.075 是成年人坐姿的常见髋—凳面差。</summary>
        public float HipAboveSeat = 0.075f;

        /// <summary>髋关节应落在的座位深度（世界 Z）。与程序化角色的角色根 z 对齐。</summary>
        public float SeatDepth = 0.42f;

        /// <summary>额外的高度微调（米），叠加在自动落座之上。</summary>
        public float SeatHeightOffset;

        /// <summary>大腿与水平面的夹角（度）。0 = 大腿完全水平，正值 = 膝盖略低于髋。</summary>
        public float ThighPitchDegrees = 8f;

        /// <summary>小腿相对竖直方向的夹角（度）。正值 = 脚踝略靠后（脚掌前伸）。</summary>
        public float ShinPitchDegrees = 6f;

        /// <summary>脚掌相对水平面的夹角（度）。正值 = 脚尖下压。</summary>
        public float FootPitchDegrees = 6f;

        /// <summary>额外髋部旋转（度）。仅用于整体微调，默认不转。</summary>
        public Vector3 HipsEuler = new Vector3(0f, 0f, 0f);

        /// <summary>
        /// 兼容保留：早期实现用欧拉角摆腿/臂。Biped 骨骼局部轴与 Humanoid 约定不一致，
        /// 这套角度在导入模型上是「无效参数」（实测大腿转 80° 时膝盖仍在髋正下方一整个腿长），
        /// 现在改按世界方向摆骨，这些字段不再参与计算。
        /// </summary>
        public Vector3 ThighEuler = new Vector3(80f, 0f, 0f);
        /// <summary>兼容保留，见 <see cref="ThighEuler"/>。</summary>
        public Vector3 CalfEuler = new Vector3(-85f, 0f, 0f);
        /// <summary>兼容保留，见 <see cref="ThighEuler"/>。</summary>
        public Vector3 FootEuler = new Vector3(5f, 0f, 0f);
        /// <summary>兼容保留：手臂姿态现在由 <c>HandIKController</c> 的旋转式 IK 负责。</summary>
        public Vector3 UpperArmEuler = new Vector3(55f, 0f, 8f);
        /// <summary>兼容保留，见 <see cref="UpperArmEuler"/>。</summary>
        public Vector3 ForeArmEuler = new Vector3(-70f, 0f, 0f);

        /// <summary>是否已按真实模型标定过（角度来自实测而非解剖学估算）。</summary>
        public bool Calibrated;
    }

    /// <summary>
    /// 坐姿应用器：把站姿 T-Pose 的导入模型摆成坐姿（方案 8.2）。
    ///
    /// <para>
    /// <b>为什么不用欧拉角</b>：导入骨架（3ds Max Biped）的局部轴与 Unity Humanoid 约定不同，
    /// 给「大腿」加 80° 的 X 旋转在实测里<b>完全没有生效</b>（膝盖仍在髋正下方一整个腿长），
    /// 左右手臂还会镜像错开。所以这里改成<b>按世界方向摆骨</b>：
    /// 用「髋→膝→踝→趾」的目标点逐段把骨方向转过去（<c>FromToRotation</c>），与骨骼局部轴无关。
    /// </para>
    ///
    /// <para>
    /// <b>落座</b>：每次 <see cref="Apply"/> 都按「当前实测髋位置 → 目标髋位置」计算位移并加到根节点，
    /// 因此是绝对目标驱动、可重复调用而不会累积漂移。
    /// </para>
    ///
    /// <para>
    /// <b>手臂不在这里摆</b>：手臂交给 <c>HandIKController</c> 的旋转式 IK（<c>IArmChainSolver</c>），
    /// 静态摆姿与逐帧 IK 抢同一批骨骼只会互相打架。
    /// </para>
    /// </summary>
    public sealed class SittingPoseApplier
    {
        private static readonly HumanBodyBones[] BaselineBones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };

        private readonly Dictionary<HumanBodyBones, Quaternion> _baseline =
            new Dictionary<HumanBodyBones, Quaternion>(BaselineBones.Length);

        private Animator _boundAnimator;
        private Vector3 _baselineRootPosition;
        private bool _hasBaseline;

        public SittingPoseApplier(SittingPoseSettings settings = null)
        {
            Settings = settings ?? new SittingPoseSettings();
        }

        /// <summary>坐姿参数（可在运行时替换后重新 Apply）。</summary>
        public SittingPoseSettings Settings { get; set; }

        /// <summary>是否已记录绑定姿态基线。</summary>
        public bool HasBaseline => _hasBaseline;

        /// <summary>上次 Apply 之后的髋关节世界位置（诊断/测试用）。</summary>
        public Vector3 LastHipPosition { get; private set; }

        /// <summary>上次 Apply 之后的左右踝关节世界位置（诊断/测试用）。</summary>
        public Vector3 LastLeftAnkle { get; private set; }

        /// <summary>上次 Apply 之后的右踝关节世界位置。</summary>
        public Vector3 LastRightAnkle { get; private set; }

        /// <summary>应用坐姿。可重复调用（绝对目标驱动，不会累积）；<paramref name="animator"/> 无效时返回 false。</summary>
        public bool Apply(Animator animator)
        {
            if (animator == null || Settings == null) return false;
            if (animator.avatar == null || !animator.avatar.isHuman) return false;

            if (!_hasBaseline || !ReferenceEquals(_boundAnimator, animator)) CaptureBaseline(animator);

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return false;

            ApplyHipsRotation(hips);
            PlaceRoot(animator, hips);
            AimLeg(animator, true);
            AimLeg(animator, false);

            LastHipPosition = hips.position;
            Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (lf != null) LastLeftAnkle = lf.position;
            if (rf != null) LastRightAnkle = rf.position;
            return true;
        }

        /// <summary>还原绑定姿态（骨骼旋转 + 根节点位置）。</summary>
        public void Restore(Animator animator)
        {
            if (animator == null || !_hasBaseline) return;

            for (int i = 0; i < BaselineBones.Length; i++)
            {
                var bone = animator.GetBoneTransform(BaselineBones[i]);
                if (bone == null) continue;
                if (_baseline.TryGetValue(BaselineBones[i], out var q)) bone.localRotation = q;
            }

            if (ReferenceEquals(_boundAnimator, animator))
                animator.transform.position = _baselineRootPosition;
        }

        private void CaptureBaseline(Animator animator)
        {
            _baseline.Clear();
            for (int i = 0; i < BaselineBones.Length; i++)
            {
                var bone = animator.GetBoneTransform(BaselineBones[i]);
                if (bone == null) continue;
                _baseline[BaselineBones[i]] = bone.localRotation;
            }
            _baselineRootPosition = animator.transform.position;
            _boundAnimator = animator;
            _hasBaseline = true;
        }

        /// <summary>髋部微调（默认 0，保留给「角色略微前倾/后仰」这类整体调整）。</summary>
        private void ApplyHipsRotation(Transform hips)
        {
            if (!_baseline.TryGetValue(HumanBodyBones.Hips, out var baseline)) return;
            hips.localRotation = baseline * Quaternion.Euler(Settings.HipsEuler);
        }

        /// <summary>把根节点整体平移到「髋落在凳面上方」的位置。绝对目标，重复调用不漂移。</summary>
        private void PlaceRoot(Animator animator, Transform hips)
        {
            float targetY = Settings.SeatSurfaceHeight + Settings.HipAboveSeat + Settings.SeatHeightOffset;
            Vector3 hip = hips.position;

            Vector3 delta = new Vector3(
                0f,
                targetY - hip.y,
                Settings.SeatDepth - hip.z);

            if (delta.sqrMagnitude < 1e-10f) return;
            animator.transform.position += delta;
        }

        /// <summary>
        /// 摆一条腿：以髋为起点，按「大腿俯角 / 小腿俯角 / 脚掌俯角」算出膝、踝、趾的世界目标，
        /// 再逐段把骨骼方向转过去。朝钢琴的方向是 −Z（角色根绕 Y 旋转 180°）。
        /// </summary>
        private void AimLeg(Animator animator, bool left)
        {
            HumanBodyBones upperId = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            HumanBodyBones lowerId = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            HumanBodyBones footId = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;

            Transform upper = animator.GetBoneTransform(upperId);
            Transform lower = animator.GetBoneTransform(lowerId);
            Transform foot = animator.GetBoneTransform(footId);
            if (upper == null || lower == null) return;

            float thighPitch = Settings.ThighPitchDegrees * Mathf.Deg2Rad;
            float shinPitch = Settings.ShinPitchDegrees * Mathf.Deg2Rad;
            float footPitch = Settings.FootPitchDegrees * Mathf.Deg2Rad;
            float sideSign = left ? 1f : -1f;

            // 大腿：朝前（−Z）并略向下，轻微外展
            Vector3 thighDir = new Vector3(
                sideSign * 0.12f,
                -Mathf.Sin(thighPitch),
                -Mathf.Cos(thighPitch)).normalized;

            float thighLength = Vector3.Distance(upper.position, lower.position);
            Vector3 kneeTarget = upper.position + thighDir * thighLength;
            AimBoneToward(upper, kneeTarget);

            // 小腿：接近竖直，脚尖略前伸
            Vector3 shinDir = new Vector3(0f, -Mathf.Cos(shinPitch), Mathf.Sin(shinPitch)).normalized;
            float shinLength = Vector3.Distance(lower.position, foot.position);
            Vector3 ankleTarget = lower.position + shinDir * shinLength;
            AimBoneToward(lower, ankleTarget);

            if (foot == null) return;

            // 脚掌：朝前略下压，让脚背接近水平
            Vector3 toeDir = new Vector3(0f, -Mathf.Sin(footPitch), -Mathf.Cos(footPitch)).normalized;
            AimBoneToward(foot, foot.position + toeDir * 0.12f);
        }

        /// <summary>
        /// 绕骨骼原点把「当前长度方向」转到指向 <paramref name="toward"/>。
        /// 用 <c>FromToRotation</c> 而非 <c>LookRotation</c>：后者隐含「骨骼朝 +Z」，
        /// 而 Biped / Mixamo 的骨骼朝向各不相同，只有前者与轴约定无关。
        /// </summary>
        private static void AimBoneToward(Transform bone, Vector3 toward)
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

            bone.rotation = Quaternion.FromToRotation(current.normalized, desired) * bone.rotation;
        }
    }
}
