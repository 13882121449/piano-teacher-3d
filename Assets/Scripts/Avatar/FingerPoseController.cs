using System.Collections.Generic;
using UnityEngine;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Avatar
{
    /// <summary>手指动作阶段（评估文档附录 A.4.3）：接近 → 按压 → 保持 → 释放。</summary>
    public enum FingerPhase
    {
        /// <summary>无目标：回到自然弯曲的悬停姿态。</summary>
        Rest = 0,

        /// <summary>有目标但尚未按下：朝琴键靠近并形成手型。</summary>
        Approach = 1,

        /// <summary>音符开始：指尖沿键面法向下压。</summary>
        Press = 2,

        /// <summary>长音期间：保持关节姿态，避免持续抖动。</summary>
        Hold = 3
    }

    /// <summary>
    /// 手指姿态控制器（方案 4.2 / 5.3 / 审查 1.2B / 评估文档附录 A.4）。
    /// <para>
    /// <b>与琴键一一对应</b>：手指分配与 <see cref="HandIKController"/> 共用同一份
    /// <see cref="FingerAssignmentService"/>，因此「弯下去的手指」必然就是「指尖伸向该键的手指」。
    /// </para>
    /// <para>
    /// <b>本轮升级（附录 A.4.2 / A.4.4）</b>：
    /// ① <b>关节级姿态</b>：每指 MCP / PIP / DIP 三个关节独立旋转，各自受角度上限约束
    ///    （不再只旋转指根一根骨头，因此指节能形成弧线而不是折线）；
    /// ② <b>受限指尖 IK</b>：<b>指根朝向由手掌决定</b>（只取目标方向的水平分量、俯角限制在
    ///    <see cref="MinRootPitchDegrees"/>–<see cref="MaxRootPitchDegrees"/>），三个关节在各自局部 X 轴上
    ///    做 CCD 把指尖送到键面；关节角不允许反向、不允许超过限位，
    ///    不可达时停在最接近的合法姿态并计数，<b>不放宽</b>手腕单帧位移约束；
    /// ③ <b>四阶段模型</b>：Approach / Press / Hold / Rest，只消费统一时间轴与音符事件，
    ///    不另建计时器；阶段切换使用四元数插值，快音符也不会跳过关节限位。
    /// </para>
    /// <para>
    /// <b>向后兼容</b>：手部未实现 <see cref="IFingerJointRig"/> 时自动退回「只旋转指根 + 固定按压角」的旧行为；
    /// <see cref="PressAngle"/> / <see cref="RestAngle"/> / <see cref="SlerpSpeed"/> / <see cref="SpreadAngle"/>
    /// 仍是兜底姿态参数。
    /// </para>
    /// <para>性能（方案 7.2）：<c>Update</c> 无 LINQ、无字符串拼接、无每帧 <c>new</c>；容器全部预分配复用。</para>
    /// </summary>
    public class FingerPoseController : MonoBehaviour, IHandJointIssueSource
    {
        [Header("手指姿态（无目标 / 无三关节时的兜底姿态）")]
        public float PressAngle = 32f;  // 手指按下弯曲角度（绕 X 正角 = 指尖下压向琴键）
        public float RestAngle = 18f;   // 未按下时的自然弯曲（避免僵直的手型）
        public float SlerpSpeed = 15f;
        public float SpreadAngle = 4f;  // 和弦手型左右张开角（绕 Y）

        [Header("关节级姿态（附录 A.4.2）")]
        /// <summary>指根朝向的最小/最大俯角（度，向下为正）：避免指根向后反折或抬成爪状。</summary>
        /// <remarks>
        /// 上限取 36°：真实演奏时近节指骨大约在水平到 35° 之间。上限过大（例如 58°）会让指根直接指向琴键，
        /// 指尖把手指"拉直"，看起来仍是僵直的杆子；上限较小则关节必须屈起来才能落到键面，手型自然。
        /// </remarks>
        public float MinRootPitchDegrees = 10f;
        public float MaxRootPitchDegrees = 36f;

        /// <summary>无目标时的自然弯曲总量（度）。</summary>
        public float RestCurlDegrees = 26f;

        /// <summary>
        /// 关节轴策略：默认「绕骨骼局部 X 轴弯曲」（程序化手部，行为与改造前逐位一致）。
        /// 导入的 Biped/Humanoid 骨架由角色视图提供由绑定姿态推导的铰链轴。
        /// </summary>
        public IFingerJointAxes JointAxes { get; set; } = DefaultFingerJointAxes.Instance;

        /// <summary>接近阶段只形成部分手型（总弯曲的相对比例）。</summary>
        [Range(0.1f, 1f)] public float ApproachCurlScale = 0.62f;

        /// <summary>保持阶段的额外轻微下压（度）：长音不抖，但保持"按住"的视觉。</summary>
        public float HoldSettleDegrees = 2.5f;

        /// <summary>MCP / PIP / DIP 的总弯曲分配权重（三者之和建议为 1）。</summary>
        [Range(0f, 1f)] public float McpWeight = 0.46f;
        [Range(0f, 1f)] public float PipWeight = 0.34f;
        [Range(0f, 1f)] public float DipWeight = 0.20f;

        /// <summary>各关节角度上限（度）：超过即钳制并计入 <see cref="JointLimitClampCount"/>。</summary>
        public float MaxMcpDegrees = 82f;
        public float MaxPipDegrees = 95f;
        public float MaxDipDegrees = 62f;

        /// <summary>关节插值速度（比指根对准略快，避免指尖落后于手腕）。</summary>
        public float JointSlerpSpeed = 20f;

        /// <summary>按下后多久算"保持"阶段（秒）。</summary>
        public float HoldSettleSeconds = 0.12f;

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

        // 每手每指的阶段计时与当前阶段（供诊断与测试；无每帧分配）
        private readonly float[] _leftHeldSeconds = new float[MaxFingers];
        private readonly float[] _rightHeldSeconds = new float[MaxFingers];
        private readonly FingerPhase[] _leftPhases = new FingerPhase[MaxFingers];
        private readonly FingerPhase[] _rightPhases = new FingerPhase[MaxFingers];

        // CCD 求解的复用缓冲（弧度 / 关节引用），避免每帧分配
        private readonly float[] _jointScratch = new float[3];
        private readonly Transform[] _joints = new Transform[3];

        /// <summary>当前指法分配（由会话层注入，与 <see cref="HandIKController"/> 是同一个实例）。</summary>
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

        /// <summary>累计触发关节角度上限的次数（附录 A.6.3「关节限位触发次数」）。</summary>
        public int JointLimitClampCount { get; private set; }

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

        /// <summary>取某手某指当前所处阶段（供测试与诊断）。</summary>
        public FingerPhase GetPhase(bool leftHand, int fingerIndex)
        {
            if (fingerIndex < 0 || fingerIndex >= MaxFingers) return FingerPhase.Rest;
            return leftHand ? _leftPhases[fingerIndex] : _rightPhases[fingerIndex];
        }

        private void Awake()
        {
            if (_assignment == null) Assignment = null;   // 触发惰性创建
        }

        public void Initialize(IHandRig leftHand, IHandRig rightHand)
        {
            _leftHand = leftHand;
            _rightHand = rightHand;
            Assignment.Invalidate();
            ResetPhaseTimers();
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
            ResetPhaseTimers();
            JointLimitClampCount = 0;
        }

        private void ResetPhaseTimers()
        {
            for (int f = 0; f < MaxFingers; f++)
            {
                _leftHeldSeconds[f] = 0f;
                _rightHeldSeconds[f] = 0f;
                _leftPhases[f] = FingerPhase.Rest;
                _rightPhases[f] = FingerPhase.Rest;
            }
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

            var jointRig = hand as IFingerJointRig;
            var held = leftHand ? _leftHeldSeconds : _rightHeldSeconds;
            var phases = leftHand ? _leftPhases : _rightPhases;

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
                bool pressed = pitch >= 0 && _pressed.Contains(pitch);

                Vector3 worldTarget = Vector3.zero;
                bool hasTarget = _targetSource != null
                    && _targetSource.TryGetFingertipTarget(leftHand, f, out worldTarget);

                // ---- 阶段判定（附录 A.4.3）：只消费音符状态，不另建计时器 ----
                FingerPhase phase;
                if (pressed)
                {
                    held[f] += Time.deltaTime;
                    phase = held[f] < HoldSettleSeconds ? FingerPhase.Press : FingerPhase.Hold;
                }
                else
                {
                    held[f] = 0f;
                    phase = hasTarget ? FingerPhase.Approach : FingerPhase.Rest;
                }
                phases[f] = phase;

                if (jointRig != null && jointRig.TryGetJoints(f, out var proximal, out var intermediate, out var distal))
                {
                    UpdateFingerJoints(finger, jointRig, f, proximal, intermediate, distal,
                        worldTarget, hasTarget, phase);
                }
                else
                {
                    UpdateFingerLegacy(finger, pitch, hasTarget, worldTarget, minPitch, span);
                }
            }
        }

        // ==================== 三关节铰链 IK（附录 A.4.2 / A.4.4） ====================

        /// <summary>
        /// 单指求解：<b>指根朝向由手掌决定</b>（不追着琴键走），关节负责把指尖送到目标键。
        /// <para>
        /// 这条分工是「手型自然」的关键：改造前让指根对准目标，当琴键位于指根后下方时，
        /// 指根会向后翻转 → 整只手呈爪状，且指尖仍落不到键上（实测 49–59 mm）。
        /// 现在指根只取目标方向的水平分量、俯角限制在 8°–58°，
        /// 由 MCP / PIP / DIP 三个铰链在各自局部 X 轴上做 CCD 收敛到键面。
        /// </para>
        /// </summary>
        private void UpdateFingerJoints(Transform finger, IFingerJointRig rig, int fingerIndex,
            Transform proximal, Transform intermediate, Transform distal,
            Vector3 worldTarget, bool hasTarget, FingerPhase phase)
        {
            float l0 = Vector3.Distance(proximal.position, intermediate.position);
            float l1 = Vector3.Distance(intermediate.position, distal.position);
            float l2 = Mathf.Max(0.004f, rig.GetFingerLength(fingerIndex) - l0 - l1);

            var angles = _jointScratch;      // 弧度，[MCP, PIP, DIP]
            bool aim = false;
            Vector3 aimDirection = Vector3.zero;
            Vector3 aimUp = Vector3.up;
            float distance = 0f;

            if (hasTarget)
            {
                Vector3 basePos = proximal.position;
                Vector3 toTarget = worldTarget - basePos;
                distance = toTarget.magnitude;
                Vector3 dir = distance > 1e-5f ? toTarget / distance : finger.forward;

                // 指根朝向：只保留目标方向的水平分量，并把俯角夹在合理区间
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude < 1e-6f) flat = new Vector3(0f, 0f, -1f);
                flat.Normalize();

                float downDegrees = Mathf.Asin(Mathf.Clamp(-dir.y, -1f, 1f)) * Mathf.Rad2Deg;
                float pitch = Mathf.Clamp(downDegrees, MinRootPitchDegrees, MaxRootPitchDegrees) * Mathf.Deg2Rad;
                aimDirection = flat * Mathf.Cos(pitch) + Vector3.down * Mathf.Sin(pitch);

                // 滚转由手掌决定（手背朝上），不让手指各自旋转
                Vector3 palmUp = finger.parent.rotation * Vector3.up;
                aimUp = palmUp.sqrMagnitude > 1e-6f ? palmUp : Vector3.up;
                aim = true;

                SolveJointAngles(proximal, intermediate, distal, l2, worldTarget, angles);
            }
            else
            {
                float rest = RestCurlDegrees * Mathf.Deg2Rad;
                float w0 = Mathf.Max(0f, McpWeight);
                float w1 = Mathf.Max(0f, PipWeight);
                float w2 = Mathf.Max(0f, DipWeight);
                float sum = Mathf.Max(1e-5f, w0 + w1 + w2);
                angles[0] = rest * w0 / sum;
                angles[1] = rest * w1 / sum;
                angles[2] = rest * w2 / sum;
            }

            // ---- 指根朝向（手掌给出的基础方向 + 水平朝向目标） ----
            Transform chain = finger.childCount > 0 ? finger.GetChild(0) : null;
            if (aim && chain != null && aimDirection.sqrMagnitude > 1e-8f)
            {
                Quaternion localTarget = JointAxes.RootAimLocalRotation(finger, aimDirection, aimUp);
                finger.localRotation = Quaternion.Slerp(finger.localRotation, localTarget, Time.deltaTime * SlerpSpeed);
            }
            else
            {
                finger.localRotation = Quaternion.Slerp(finger.localRotation, Quaternion.identity,
                    Time.deltaTime * SlerpSpeed);
            }

            // ---- 阶段调整：接近阶段只形成部分手型；保持阶段轻微下压 ----
            if (phase == FingerPhase.Approach)
            {
                for (int i = 0; i < 3; i++) angles[i] *= ApproachCurlScale;
            }
            else if (phase == FingerPhase.Hold && hasTarget)
            {
                angles[0] += HoldSettleDegrees * Mathf.Deg2Rad;
            }

            // ---- 关节角：逐关节限幅并插值 ----
            ApplyJoint(proximal, 0, angles[0] * Mathf.Rad2Deg, MaxMcpDegrees);
            ApplyJoint(intermediate, 1, angles[1] * Mathf.Rad2Deg, MaxPipDegrees);
            ApplyJoint(distal, 2, angles[2] * Mathf.Rad2Deg, MaxDipDegrees);

            if (hasTarget) RecordReachIssue(distance, l0 + l1 + l2, angles);
        }

        private void ApplyJoint(Transform joint, int jointIndex, float degrees, float maxDegrees)
        {
            if (joint == null) return;
            // 弯曲轴由策略决定：程序化手部绕局部 X（与改造前一致），导入骨架绕推导出的铰链轴
            var target = JointAxes.CurlLocalRotation(joint, jointIndex, Mathf.Clamp(degrees, 0f, maxDegrees));
            joint.localRotation = Quaternion.Slerp(joint.localRotation, target,
                Time.deltaTime * JointSlerpSpeed);
        }

        /// <summary>
        /// 铰链轴 CCD（Cyclic Coordinate Descent）：每根手指只绕自身局部 X 轴屈伸，
        /// 从末端关节到指根依次把「关节 → 指尖」转向目标。
        /// <para>
        /// 关节角一律 ≥ 0（不允许反关节），并受 MCP/PIP/DIP 各自上限约束；
        /// 6 次迭代对 3 关节足以收敛。目标不可达时停在最接近的合法姿态，并计入限位统计。
        /// </para>
        /// </summary>
        private void SolveJointAngles(Transform proximal, Transform intermediate, Transform distal,
            float l2, Vector3 target, float[] angles)
        {
            // 初值取自然弯曲：全直状态下目标若正好在手指射线上，旋转梯度为 0 会卡住
            float rest = RestCurlDegrees * Mathf.Deg2Rad;
            float w0 = Mathf.Max(0.001f, McpWeight);
            float w1 = Mathf.Max(0.001f, PipWeight);
            float w2 = Mathf.Max(0.001f, DipWeight);
            float sum = w0 + w1 + w2;
            angles[0] = rest * w0 / sum;
            angles[1] = rest * w1 / sum;
            angles[2] = rest * w2 / sum;

            _joints[0] = proximal;
            _joints[1] = intermediate;
            _joints[2] = distal;

            float maxMcp = MaxMcpDegrees * Mathf.Deg2Rad;
            float maxPip = MaxPipDegrees * Mathf.Deg2Rad;
            float maxDip = MaxDipDegrees * Mathf.Deg2Rad;

            for (int iteration = 0; iteration < 6; iteration++)
            {
                for (int j = 2; j >= 0; j--)
                {
                    Transform joint = _joints[j];
                    if (joint == null) continue;

                    // 先把当前关节角写回，保证指尖位置反映本轮求解结果
                    joint.localRotation = JointAxes.CurlLocalRotation(joint, j, angles[j] * Mathf.Rad2Deg);

                    Vector3 jointPos = joint.position;
                    Vector3 tip = distal.TransformPoint(0f, 0f, l2);
                    Vector3 axis = JointAxes.WorldCurlAxis(joint, j);

                    Vector3 a = Vector3.ProjectOnPlane(tip - jointPos, axis);
                    Vector3 b = Vector3.ProjectOnPlane(target - jointPos, axis);
                    if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) continue;

                    float delta = Vector3.SignedAngle(a, b, axis) * Mathf.Deg2Rad;
                    float max = j == 0 ? maxMcp : (j == 1 ? maxPip : maxDip);
                    angles[j] = Mathf.Clamp(angles[j] + delta, 0f, max);
                }
            }
        }

        /// <summary>记录「够不到」：目标距离超出可达范围且关节已达上限时计入限位统计。</summary>
        private void RecordReachIssue(float distance, float reach, float[] angles)
        {
            bool clamped = angles[0] >= MaxMcpDegrees * Mathf.Deg2Rad - 1e-3f
                        || angles[1] >= MaxPipDegrees * Mathf.Deg2Rad - 1e-3f
                        || angles[2] >= MaxDipDegrees * Mathf.Deg2Rad - 1e-3f;
            if (distance > reach && clamped) JointLimitClampCount++;
        }


        // ==================== 旧路径（无三关节时） ====================

        private void UpdateFingerLegacy(Transform finger, int pitch, bool hasTarget, Vector3 worldTarget,
            int minPitch, int span)
        {
            Quaternion target;
            if (hasTarget && TryAimAt(finger, worldTarget, out Quaternion aim))
            {
                target = aim;
            }
            else if (pitch >= 0)
            {
                float ratio = span > 0 ? Mathf.Clamp01((pitch - minPitch) / (float)span) : 0.5f;
                float spreadAngle = (ratio - 0.5f) * 2f * SpreadAngle;
                target = Quaternion.Euler(PressAngle, spreadAngle, 0f);
            }
            else
            {
                target = Quaternion.Euler(RestAngle, 0f, 0f);
            }

            finger.localRotation = Quaternion.Slerp(finger.localRotation, target, Time.deltaTime * SlerpSpeed);
        }

        /// <summary>旧路径的对准：让「链的前向」指向世界目标。</summary>
        private static bool TryAimAt(Transform finger, Vector3 worldTarget, out Quaternion rotation)
        {
            rotation = finger.localRotation;

            Vector3 aimWorld = worldTarget - finger.position;
            if (aimWorld.sqrMagnitude < 1e-8f) return false;

            Transform chain = finger.childCount > 0 ? finger.GetChild(0) : null;
            Vector3 chainLocal = chain != null ? chain.localRotation * Vector3.forward : Vector3.forward;

            Transform parent = finger.parent;
            Vector3 aimParent = parent != null
                ? parent.InverseTransformDirection(aimWorld).normalized
                : aimWorld.normalized;

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
