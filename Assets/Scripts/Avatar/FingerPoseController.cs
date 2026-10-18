using System.Collections.Generic;
using UnityEngine;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 手指姿态控制器（方案 4.2 / 5.3 / 审查 1.2B）。
    /// <para>
    /// <b>本轮关键修复（手指与琴键一一对应）</b>：
    /// ① 手指分配不再由本组件独占 —— 与 <see cref="HandIKController"/> 共用同一份
    ///    <see cref="FingerAssignmentService"/> 结果，因此「弯下去的手指」必然就是「指尖伸向该键的手指」；
    /// ② 手指朝向由「固定的按压角 + 张开角」改为<b>朝目标琴键方向对准</b>
    ///    （取 <see cref="IFingertipTargetSource"/> 给出的该指指尖目标），
    ///    使左右张开与该指下压自然产生，键位与指头严格对应；
    /// ③ 无目标时才回退到 <see cref="RestAngle"/>（自然弯曲），不产生"空指乱弯"。
    /// </para>
    /// <para>
    /// 保留语义：<see cref="PressAngle"/> / <see cref="RestAngle"/> / <see cref="SlerpSpeed"/> /
    /// <see cref="SpreadAngle"/> 仍作为无目标时的兜底姿态；<see cref="GetFingerPitch"/> 直接反映分配结果；
    /// 弯曲只旋转 <c>IHandRig.Fingers[f]</c> 根节点（近端弯曲），不移动手腕，
    /// 因此不破坏 <c>HandIKController</c> 的单帧位移硬约束与防穿模钳制。
    /// </para>
    /// <para>性能（方案 7.2）：<c>Update</c> 无 LINQ、无字符串拼接、无每帧 <c>new</c>；容器全部预分配复用。</para>
    /// </summary>
    public class FingerPoseController : MonoBehaviour
    {
        [Header("手指姿态（无目标时的兜底姿态）")]
        public float PressAngle = 32f;  // 手指按下弯曲角度（绕 X 正角 = 指尖下压向琴键）
        public float RestAngle = 18f;   // 未按下时的自然弯曲（避免僵直的手型）
        public float SlerpSpeed = 15f;
        public float SpreadAngle = 4f;  // 和弦手型左右张开角（绕 Y）

        [Header("指法提供器（方案 5.3）")]
        /// <summary>每手保留的最近旋律音数量（供提供器判断相邻音 / 重复音 / 拇指穿越）。</summary>
        public int MelodyHistoryLength = 8;

        private const int MaxFingers = FingerAssignmentService.MaxFingers;

        private readonly HashSet<int> _pressed = new();
        private readonly List<int> _scratch = new();

        private FingerAssignmentService _assignment;
        private IFingertipTargetSource _targetSource;
        private IHandRig _leftHand;
        private IHandRig _rightHand;

        /// <summary>
        /// 双手共用的指法分配（由会话层注入，与 <see cref="HandIKController"/> 是同一个实例）。
        /// 未注入时自动创建一个私有实例，保证组件可单独使用 / 单测。
        /// </summary>
        public FingerAssignmentService Assignment
        {
            get => _assignment ?? (_assignment = new FingerAssignmentService());
            set
            {
                _assignment = value ?? new FingerAssignmentService();
                _assignment.MelodyHistoryLength = MelodyHistoryLength;
            }
        }

        /// <summary>指尖目标来源（通常就是 <see cref="HandIKController"/>）；为空时退化为兜底姿态。</summary>
        public IFingertipTargetSource TargetSource
        {
            get => _targetSource;
            set => _targetSource = value;
        }

        /// <summary>
        /// 当前指法提供器（转发到共用的 <see cref="FingerAssignmentService"/>）。
        /// 置为 null 时回退默认启发式提供器（AI / 人工指法只能作为可选增强，方案 10 / 取舍 3）。
        /// </summary>
        public IFingeringProvider Provider
        {
            get => Assignment.Provider;
            set => Assignment.Provider = value;
        }

        /// <summary>最近一次求解的置信度（取有分配的两手中较低者），0..1。</summary>
        public float LastConfidence
        {
            get
            {
                bool l = Assignment.HasAssignment(true);
                bool r = Assignment.HasAssignment(false);
                if (l && r) return Mathf.Min(Assignment.GetConfidence(true), Assignment.GetConfidence(false));
                if (l) return Assignment.GetConfidence(true);
                if (r) return Assignment.GetConfidence(false);
                return 1f;
            }
        }

        /// <summary>最近一次求解是否属于「估算指法」（任一手低置信度即为 true）。</summary>
        public bool LastEstimated
        {
            get
            {
                bool l = Assignment.HasAssignment(true);
                bool r = Assignment.HasAssignment(false);
                if (l && r) return Assignment.IsEstimated(true) || Assignment.IsEstimated(false);
                if (l) return Assignment.IsEstimated(true);
                if (r) return Assignment.IsEstimated(false);
                return false;
            }
        }

        /// <summary>最近一次求解的中文规则说明（取置信度较低的一手，供诊断面板显示）。</summary>
        public string LastFingeringReason
        {
            get
            {
                bool l = Assignment.HasAssignment(true);
                bool r = Assignment.HasAssignment(false);
                if (l && r)
                {
                    return Assignment.GetConfidence(true) <= Assignment.GetConfidence(false)
                        ? Assignment.GetReason(true)
                        : Assignment.GetReason(false);
                }
                if (l) return Assignment.GetReason(true);
                if (r) return Assignment.GetReason(false);
                return string.Empty;
            }
        }

        /// <summary>当前提供器名称（供 UI / 诊断标注）。</summary>
        public string ProviderName => Assignment.ProviderName;

        private void Awake()
        {
            if (_assignment == null) Assignment = null;   // 触发惰性创建
        }

        public void Initialize(IHandRig leftHand, IHandRig rightHand)
        {
            _leftHand = leftHand;
            _rightHand = rightHand;
            Assignment.Invalidate();
        }

        public void OnNoteOn(MidiNoteEvent e)
        {
            if (!Piano.PianoKeyMap.IsInRange(e.Pitch)) return;

            _pressed.Add(e.Pitch);
            Assignment.PushMelody(PianoNoteFilter.IsLeftHand(e.Pitch), e.Pitch);
            Assignment.Invalidate();
        }

        public void OnNoteOff(MidiNoteEvent e)
        {
            if (!_pressed.Remove(e.Pitch)) return;
            Assignment.Invalidate();
        }

        public void ReleaseAll()
        {
            _pressed.Clear();
            Assignment.Clear();
        }

        /// <summary>指定手指当前分配到的音高（-1 = 未按下），供测试与调试使用；结果直接反映共用分配。</summary>
        public int GetFingerPitch(bool leftHand, int fingerIndex) => Assignment.GetPitch(leftHand, fingerIndex);

        /// <summary>某音高由哪根手指负责（-1 = 无分配），供「手指 ↔ 琴键一一对应」验证。</summary>
        public int GetFingerIndexForPitch(bool leftHand, int pitch) => Assignment.GetFinger(leftHand, pitch);

        private void Update()
        {
            // 与 HandIKController 共用同一份分配：谁先跑谁算，另一处直接复用（同帧不会算出两个答案）
            RefreshAssignment(leftHand: true);
            RefreshAssignment(leftHand: false);

            UpdateFingers(_leftHand, leftHand: true);
            UpdateFingers(_rightHand, leftHand: false);
        }

        private void RefreshAssignment(bool leftHand)
        {
            CollectHandPitches(leftHand);
            Assignment.Refresh(leftHand, _scratch, Time.time, Time.frameCount);
        }

        private void UpdateFingers(IHandRig hand, bool leftHand)
        {
            if (hand == null) return;

            int fingerCount = Mathf.Min(hand.Fingers.Count, MaxFingers);
            if (fingerCount == 0) return;

            // 兜底姿态用的音高区间（仅在没有指尖目标时生效，保留 SpreadAngle 语义）
            int minPitch = int.MaxValue;
            int maxPitch = int.MinValue;
            for (int f = 0; f < fingerCount; f++)
            {
                int p = Assignment.GetPitch(leftHand, f);
                if (p < 0) continue;
                if (p < minPitch) minPitch = p;
                if (p > maxPitch) maxPitch = p;
            }
            int span = (maxPitch >= minPitch) ? maxPitch - minPitch : 0;

            for (int f = 0; f < hand.Fingers.Count && f < MaxFingers; f++)
            {
                Transform finger = hand.Fingers[f];
                if (finger == null) continue;

                int pitch = Assignment.GetPitch(leftHand, f);
                Quaternion target;

                if (_targetSource != null && _targetSource.TryGetFingertipTarget(leftHand, f, out Vector3 worldTarget)
                    && TryAimAt(finger, worldTarget, out Quaternion aim))
                {
                    // ① 对准目标琴键：直接解出「让手指链指向该键」所需的局部旋转（在手腕空间求解），
                    //    拇指的对掌偏航（烘在 Chain 节点上）因此被正确保留。
                    target = aim;
                }
                else if (pitch >= 0)
                {
                    // ② 无几何目标时的兜底：固定按压角 + 和弦张开角
                    float ratio = span > 0 ? (pitch - minPitch) / (float)span : 0.5f;
                    float spreadAngle = (ratio - 0.5f) * 2f * SpreadAngle;
                    target = Quaternion.Euler(PressAngle, spreadAngle, 0f);
                }
                else
                {
                    // ③ 未分配且无目标：自然弯曲（不出现"空指乱弯"）
                    target = Quaternion.Euler(RestAngle, 0f, 0f);
                }

                finger.localRotation = Quaternion.Slerp(finger.localRotation, target, Time.deltaTime * SlerpSpeed);
            }
        }

        /// <summary>
        /// 求「让手指链指向世界坐标 <paramref name="worldTarget"/>」所需的<b>局部旋转</b>
        /// （即 <c>finger.localRotation</c> 的目标值）。
        /// <para>
        /// 推导：设手指根在父级（手腕）空间下要让「链的前向」对准目标方向，
        /// 而链在手指根局部空间的前向 = <c>chain.localRotation * Vector3.forward</c>（烤进去的解剖朝向：
        /// 拇指对掌 57°、四指扇形）。于是目标局部旋转 = <c>FromToRotation(链的局部前向, 父空间下的目标方向)</c>。
        /// </para>
        /// </summary>
        private static bool TryAimAt(Transform finger, Vector3 worldTarget, out Quaternion rotation)
        {
            rotation = finger.localRotation;

            Vector3 aimWorld = worldTarget - finger.position;
            if (aimWorld.sqrMagnitude < 1e-8f) return false;

            // 链节点承载解剖朝向（拇指对掌 / 四指扇形），是手指实际伸出的方向基准
            Transform chain = finger.childCount > 0 ? finger.GetChild(0) : null;
            Vector3 chainLocal = chain != null ? chain.localRotation * Vector3.forward : Vector3.forward;

            // 目标方向换算到手指根的父空间（手腕局部空间）——与 chainLocal 同处"父空间"这一参照
            Transform parent = finger.parent;
            Vector3 aimParent = parent != null
                ? parent.InverseTransformDirection(aimWorld).normalized
                : aimWorld.normalized;

            // 目标几乎在正后方时不做翻转（避免手指绕到手背/翻转 180°）
            if (Vector3.Dot(chainLocal, aimParent) < -0.92f) return false;

            rotation = Quaternion.FromToRotation(chainLocal, aimParent);
            return true;
        }

        private void CollectHandPitches(bool leftHand)
        {
            _scratch.Clear();
            foreach (var pitch in _pressed)
            {
                if (PianoNoteFilter.IsLeftHand(pitch) == leftHand) _scratch.Add(pitch);
            }
            _scratch.Sort();
        }
    }
}
