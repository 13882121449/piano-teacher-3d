using System;
using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 坐姿参数（方案 8.2「导入模型需要坐姿适配」）。
    /// <para>
    /// <b>重要：默认值未标定。</b>这些角度是按人体坐姿的解剖学范围给出的<b>起点</b>
    /// （髋屈曲 ≈80°、膝屈曲 ≈85°、上臂前伸 ≈55°、肘屈曲 ≈70°），
    /// 但 3ds Max Biped 的骨骼局部轴朝向与新 Unity 场景中的实际视觉，<b>必须在 Unity 里打开预制体逐一核对</b>。
    /// 核对通过后请把 <see cref="Calibrated"/> 置为 true——UI 与日志据此区分「已标定」和「待标定」。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class SittingPoseSettings
    {
        [Tooltip("臀部整体升降（米）。用于把模型臀部贴合琴凳坐面")]
        public float SeatHeightOffset;

        [Header("下肢（骨骼局部欧拉角，度；正负号待标定）")]
        [Tooltip("髋关节（骨盆）基础旋转")]
        public Vector3 HipsEuler = new Vector3(0f, 0f, 0f);

        [Tooltip("大腿：绕 X 正方向前抬 ≈ 髋屈曲")]
        public Vector3 ThighEuler = new Vector3(80f, 0f, 0f);

        [Tooltip("小腿：回折 ≈ 膝屈曲（方向通常与大腿相反）")]
        public Vector3 CalfEuler = new Vector3(-85f, 0f, 0f);

        [Tooltip("踝关节补偿（保持脚掌贴地）")]
        public Vector3 FootEuler = new Vector3(5f, 0f, 0f);

        [Header("上肢（骨骼局部欧拉角，度；正负号待标定）")]
        [Tooltip("上臂：前伸 + 略微内收，形成弹奏时的自然下垂")]
        public Vector3 UpperArmEuler = new Vector3(55f, 0f, 8f);

        [Tooltip("前臂：肘屈曲")]
        public Vector3 ForeArmEuler = new Vector3(-70f, 0f, 0f);

        [Header("标定状态")]
        [Tooltip("是否已在 Unity 内人工核对过角度。默认 false = 待标定。")]
        public bool Calibrated;
    }

    /// <summary>
    /// 坐姿应用器：把站姿 T-Pose 的导入模型摆成坐姿（方案 8.2）。
    /// <para>
    /// 只改<b>骨骼局部旋转</b>与模型根的局部位移，不改网格、不改 Animator 的 Avatar 映射；
    /// 首次 <see cref="Apply"/> 时记录绑定姿态作为基线，<see cref="Restore"/> 可完整还原，
    /// 因此可以反复启用/停用途中的预览而不会累积漂移。
    /// </para>
    /// </summary>
    public sealed class SittingPoseApplier
    {
        /// <summary>参与坐姿调整的骨骼（顺序固定，便于基线记录）。</summary>
        private static readonly HumanBodyBones[] PoseBones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm
        };

        private readonly Dictionary<HumanBodyBones, Quaternion> _baseline =
            new Dictionary<HumanBodyBones, Quaternion>(PoseBones.Length);

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

        /// <summary>
        /// 应用坐姿。可重复调用（不会累积旋转）；<paramref name="animator"/> 为空时不做任何事并返回 false。
        /// </summary>
        public bool Apply(Animator animator)
        {
            if (animator == null || Settings == null) return false;
            // GetBoneTransform 只在合法 Humanoid 上可用；非 Humanoid 时直接跳过，避免每帧刷错误日志
            if (animator.avatar == null || !animator.avatar.isHuman) return false;

            if (!_hasBaseline || !ReferenceEquals(_boundAnimator, animator)) CaptureBaseline(animator);

            ApplyBone(animator, HumanBodyBones.Hips, Settings.HipsEuler);
            ApplyBone(animator, HumanBodyBones.LeftUpperLeg, Settings.ThighEuler);
            ApplyBone(animator, HumanBodyBones.RightUpperLeg, Settings.ThighEuler);
            ApplyBone(animator, HumanBodyBones.LeftLowerLeg, Settings.CalfEuler);
            ApplyBone(animator, HumanBodyBones.RightLowerLeg, Settings.CalfEuler);
            ApplyBone(animator, HumanBodyBones.LeftFoot, Settings.FootEuler);
            ApplyBone(animator, HumanBodyBones.RightFoot, Settings.FootEuler);
            ApplyBone(animator, HumanBodyBones.LeftUpperArm, Settings.UpperArmEuler);
            ApplyBone(animator, HumanBodyBones.RightUpperArm, Settings.UpperArmEuler);
            ApplyBone(animator, HumanBodyBones.LeftLowerArm, Settings.ForeArmEuler);
            ApplyBone(animator, HumanBodyBones.RightLowerArm, Settings.ForeArmEuler);

            var root = animator.transform;
            root.localPosition = _baselineRootPosition + Vector3.up * Settings.SeatHeightOffset;
            return true;
        }

        /// <summary>还原绑定姿态。</summary>
        public void Restore(Animator animator)
        {
            if (animator == null || !_hasBaseline) return;

            for (int i = 0; i < PoseBones.Length; i++)
            {
                var bone = animator.GetBoneTransform(PoseBones[i]);
                if (bone == null) continue;
                if (_baseline.TryGetValue(PoseBones[i], out var q)) bone.localRotation = q;
            }
            if (ReferenceEquals(_boundAnimator, animator)) animator.transform.localPosition = _baselineRootPosition;
        }

        private void CaptureBaseline(Animator animator)
        {
            _baseline.Clear();
            for (int i = 0; i < PoseBones.Length; i++)
            {
                var bone = animator.GetBoneTransform(PoseBones[i]);
                if (bone == null) continue;
                _baseline[PoseBones[i]] = bone.localRotation;
            }
            _baselineRootPosition = animator.transform.localPosition;
            _boundAnimator = animator;
            _hasBaseline = true;
        }

        private void ApplyBone(Animator animator, HumanBodyBones id, Vector3 euler)
        {
            if (!_baseline.TryGetValue(id, out var baseline)) return;
            var bone = animator.GetBoneTransform(id);
            if (bone == null) return;
            // 以绑定姿态为基准左乘局部旋转：同一组参数重复 Apply 结果一致，不会累积
            bone.localRotation = baseline * Quaternion.Euler(euler);
        }
    }
}
