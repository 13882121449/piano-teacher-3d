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
        Hold = 3,

        /// <summary>抬指离键，平滑回到预备或休息姿势。</summary>
        Release = 4
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
    [DefaultExecutionOrder(10)]
    public class FingerPoseController : MonoBehaviour, IHandJointIssueSource
    {
        [Header("手指姿态（无目标 / 无三关节时的兜底姿态）")]
        public float PressAngle = 32f;  // 手指按下弯曲角度（绕 X 正角 = 指尖下压向琴键）
        /// <summary>
        /// 单手兜底路径（无三关节骨架）的静止弯曲（度）。
        /// 与三关节路径的 MCP 规格值对齐（25°）——弹琴的静止手型是 C 形，
        /// 旧值 18° 偏平，视觉上接近「伸手」而不是「搭在键上」。
        /// </summary>
        public float RestAngle = 25f;
        public float SlerpSpeed = 15f;

        /// <summary>启用导入骨架的受限触键解算。关闭时用于同曲回放旧版动作。</summary>
        public bool UseAnatomicalContact = true;
        /// <summary>和弦手型左右张开角（绕 Y）。规格 2°：真实演奏时手指基本平行，张开靠指根分叉而非旋外。</summary>
        public float SpreadAngle = 2f;

        [Header("关节级姿态（附录 A.4.2）")]
        /// <summary>指根朝向的最小/最大俯角（度，向下为正）：避免指根向后反折或抬成爪状。</summary>
        /// <remarks>
        /// 上限取 36°：真实演奏时近节指骨大约在水平到 35° 之间。上限过大（例如 58°）会让指根直接指向琴键，
        /// 指尖把手指"拉直"，看起来仍是僵直的杆子；上限较小则关节必须屈起来才能落到键面，手型自然。
        /// </remarks>
        public float MinRootPitchDegrees = 10f;
        public float MaxRootPitchDegrees = 36f;

        /// <summary>
        /// 铰链平面朝「世界上方向」混合的比例：1 = 竖直平面，0 = 跟随手掌滚转（默认，保持既有手型）。
        /// <para>
        /// 2026-11-05 A/B（bench_chords，静置 0.8 s 后逐指残差）：0 → 1 后残差从 20.6 mm 变 20.9 mm，
        /// 无收益。说明「按键手指离键面」不是铰链平面滚转造成的，参数保留但默认关闭。
        /// </para>
        /// </summary>
        public float AimPlaneUpBlend = 0f;

        /// <summary>
        /// 静止手型的三关节总弯曲（度）。规格值 70 = MCP 25 + PIP 30 + DIP 15（标准 C 形手）。
        /// <para>
        /// 旧值 26° 偏平（视觉上像「举手」），悬空/压键指标也说明指尖离键面偏远。
        /// 分配权重按 25:30:15 重标，使三关节各自落到规格角度。
        /// </para>
        /// </summary>
        public float RestCurlDegrees = 70f;

        /// <summary>
        /// 空闲手指（没有分配音符）的弯曲总量（度）。
        /// <para>
        /// 为什么比 <see cref="RestCurlDegrees"/> 小：实测演奏中「没分到音符的手指」会垂到
        /// 比正在按弦的手指还低（指尖 0.753 vs 0.780），看起来像五指乱戳而不是弹琴。
        /// 空闲手指应当<b>悬在键面上方收着</b>，把弯曲量收回来指尖就抬起来。
        /// </para>
        /// </summary>
        public float RestHoverCurlDegrees = 70f;

        /// <summary>
        /// 三关节 CCD 的初值总弯曲（度）。与 <see cref="RestCurlDegrees"/> 解耦：
        /// 前者是「求解起点」（只影响收敛路径与迭代次数），后者是「无目标时的静止手型」（影响观感）。
        /// <para>
        /// 为什么不能直接共用：把初值抬到 70° 会让 CCD 从极度弯曲的状态往里收，收敛路径与前一次
        /// 逐位不同（实测会造成键面距离/压键指标漂移）。指尖目标求解应当保持既有基线，
        /// 观感改进走独立参数。
        /// </para>
        /// </summary>
        public float SolveSeedCurlDegrees = 26f;

        /// <summary>
        /// 关节轴策略：默认「绕骨骼局部 X 轴弯曲」（程序化手部，行为与改造前逐位一致）。
        /// 导入的 Biped/Humanoid 骨架由角色视图提供由绑定姿态推导的铰链轴。
        /// </summary>
        public IFingerJointAxes JointAxes { get; set; } = DefaultFingerJointAxes.Instance;

        /// <summary>接近阶段只形成部分手型（总弯曲的相对比例）。</summary>
        [Range(0.1f, 1f)] public float ApproachCurlScale = 0.62f;

        /// <summary>保持阶段的额外轻微下压（度）：长音不抖，但保持"按住"的视觉。</summary>
        public float HoldSettleDegrees = 2.5f;

        /// <summary>
        /// MCP / PIP / DIP 的总弯曲分配权重（三者之和建议为 1）。
        /// <para>
        /// 规格值来自静止手型目标角 25° / 30° / 15°（总 70°）的归一化：
        /// 25/70 = 0.357、30/70 = 0.429、15/70 = 0.214。
        /// 旧值 0.46 / 0.34 / 0.20 会让 MCP 明显多弯、PIP/DIP 偏直，呈「钩手」而非 C 形。
        /// </para>
        /// </summary>
        [Range(0f, 1f)] public float McpWeight = 0.36f;
        [Range(0f, 1f)] public float PipWeight = 0.43f;
        [Range(0f, 1f)] public float DipWeight = 0.21f;

        /// <summary>各关节角度上限（度）：超过即钳制并计入 <see cref="JointLimitClampCount"/>。</summary>
        public float MaxMcpDegrees = 82f;
        public float MaxPipDegrees = 95f;
        public float MaxDipDegrees = 62f;

        /// <summary>关节插值速度（比指根对准略快，避免指尖落后于手腕）。</summary>
        public float JointSlerpSpeed = 20f;

        [Header("关节自然运动（写实化三期：抓握与伸展的过渡）")]
        /// <summary>
        /// 关节角改用临界阻尼二阶跟随，替代一阶指数插值。
        /// <para>
        /// <b>为什么一阶插值让手指看起来像机械爪</b>：<c>Slerp(cur, tgt, dt·k)</c> 的角速度是
        /// <c>k(1−k)^n</c>——<b>第一帧就最大</b>、之后单调衰减。真实手指屈曲由肌腱拉动，
        /// 有明确的加速段（肌力>惯性时加速、接近目标时被对抗肌减速），角速度呈钟形。
        /// 一阶插值把「起步」直接做成峰值，观感就是「啪」地弹到位，缺抓握的酝酿感。
        /// </para>
        /// <para>
        /// 用角度标量（不是四元数）做二阶：每个关节只绕自身铰链轴屈伸，单自由度即可描述，
        /// 状态量小、无万向锁、且与 CCD 的 <c>angles[]</c> 语义直接对应。
        /// </para>
        /// </summary>
        public bool UseDampedJointMotion = true;

        /// <summary>
        /// 关节弹簧角频率（rad/s）。整定依据：临界阻尼整定时间 ≈ 5.8/ω，
        /// 取 26 → 约 0.22 s，与原来 JointSlerpSpeed=20（时间常数 0.05 s、整定约 0.20 s）
        /// <b>总时长一致</b>——只改曲线形状，不改手指到位快慢。
        /// </summary>
        public float JointSpringOmega = 26f;

        /// <summary>关节弹簧积分子步上限（秒）：低帧率下 ω·dt 过大会振荡。</summary>
        public float JointMaxSubStep = 1f / 120f;

        /// <summary>
        /// DIP 与 PIP 的屈曲耦合比。真实手部 DIP 由掌腱膜被动牵拉，
        /// 屈曲量约为 PIP 的 0.6–0.67（没有独立伸肌腱，无法单独伸直）。
        /// </summary>
        public float DipPipRatio = 0.62f;

        /// <summary>
        /// 耦合容差（度）：DIP 允许在「耦合值 ± 该容差」内自由调整以够到目标。
        /// 完全锁死会损失可达性（指尖到不了键面），完全放开则退化成无约束的钩手。
        /// </summary>
        public float DipCouplingSlackDegrees = 14f;

        /// <summary>
        /// 接触带宽度（度）：关节误差小于该值时切换到过阻尼。指尖按到键面后由琴键刚体挡住，
        /// 关节不可能继续自由屈曲——自由二阶在「目标持续朝同一方向变化后突然停止」时会保留
        /// 残余速度并冲过目标（实测键面 P50 由 +8.6 mm 变成 −0.3 mm，压键帧 120→183）。
        /// 提高阻尼比让减速提前开始：速度曲线仍是钟形，但尾部不再越界。
        /// </summary>
        public float JointContactBandDegrees = 6f;

        /// <summary>
        /// 接触带内的阻尼比（1 = 临界阻尼，&gt;1 = 过阻尼）。取 2.2：无过冲且减速平滑，
        /// 不会像「到点即停」那样产生硬切。
        /// </summary>
        public float JointContactDampingRatio = 2.2f;


        /// <summary>按下后多久算"保持"阶段（秒）。</summary>
        public float HoldSettleSeconds = 0.12f;

        [Header("指法提供器（方案 5.3）")]
        /// <summary>每手保留的最近旋律音数量（供提供器判断相邻音 / 重复音 / 拇指穿越）。</summary>
        public int MelodyHistoryLength = 8;

        private const int MaxFingers = FingerAssignmentService.MaxFingers;

        private readonly HashSet<int> _pressed = new();
        private readonly ActiveNoteHands _pressedHands = new();
        private readonly List<int> _scratch = new();

        private FingerAssignmentService _assignment;
        private IFingertipTargetSource _targetSource;
        private IHandRig _leftHand;
        private IHandRig _rightHand;

        // 每手每指的阶段计时与当前阶段（供诊断与测试；无每帧分配）
        private readonly float[] _leftSideAngles = new float[MaxFingers];
        private readonly float[] _rightSideAngles = new float[MaxFingers];
        private readonly float[] _leftHeldSeconds = new float[MaxFingers];
        private readonly float[] _rightHeldSeconds = new float[MaxFingers];
        private readonly FingerPhase[] _leftPhases = new FingerPhase[MaxFingers];
        private readonly FingerPhase[] _rightPhases = new FingerPhase[MaxFingers];

        // CCD 求解的复用缓冲（弧度 / 关节引用），避免每帧分配
        private readonly float[] _jointScratch = new float[3];
        private readonly Transform[] _joints = new Transform[3];

        // 关节角二阶跟随状态（每手 5 指 × 3 关节 = 15 槽；角度 + 角速度 + 是否已初始化）
        // 用状态量而不是反解 localRotation：导入骨架的铰链轴是任意方向，反解不可靠。
        private readonly float[] _leftJointAngle = new float[MaxFingers * 3];
        private readonly float[] _leftJointVelocity = new float[MaxFingers * 3];
        private readonly float[] _rightJointAngle = new float[MaxFingers * 3];
        private readonly float[] _rightJointVelocity = new float[MaxFingers * 3];
        private readonly bool[] _leftJointPrimed = new bool[MaxFingers * 3];
        private readonly bool[] _rightJointPrimed = new bool[MaxFingers * 3];

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
            _pressedHands.Press(e);
            Assignment.PushMelody(PianoNoteFilter.IsLeftHand(e), in e);
            Assignment.Invalidate();
        }

        public void OnNoteOff(MidiNoteEvent e)
        {
            if (!Piano.PianoKeyMap.IsInRange(e.Pitch) || !_pressed.Contains(e.Pitch)) return;
            if (_pressedHands.Release(e)) _pressed.Remove(e.Pitch);
            Assignment.Invalidate();
        }

        internal void AlignOpeningRightThumb()
        {
            System.Array.Clear(_rightJointPrimed, 0, 3);
        }

        public void ReleaseAll()
        {
            _pressed.Clear();
            _pressedHands.Clear();
            Assignment.Clear();
            ResetPhaseTimers();
            JointLimitClampCount = 0;
        }

        private void ResetPhaseTimers()
        {
            for (int f = 0; f < MaxFingers; f++)
            {
                _leftSideAngles[f] = 0f;
                _rightSideAngles[f] = 0f;
                _leftHeldSeconds[f] = 0f;
                _rightHeldSeconds[f] = 0f;
                _leftPhases[f] = FingerPhase.Rest;
                _rightPhases[f] = FingerPhase.Rest;
            }

            // 关节二阶状态同步复位：不复位会让 Seek / 切曲后手指带着旧角速度冲过头
            for (int i = 0; i < _leftJointAngle.Length; i++)
            {
                _leftJointAngle[i] = 0f;
                _leftJointVelocity[i] = 0f;
                _leftJointPrimed[i] = false;
                _rightJointAngle[i] = 0f;
                _rightJointVelocity[i] = 0f;
                _rightJointPrimed[i] = false;
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

        // ==================== 休息手指防穿模（2026-11-18 全面优化 P4） ====================
        //
        // 诊断显示最深的指节穿模来自没有分配音符的休息手指：静止弯曲时末节垂得太低，陷进白键 10–14 mm
        // （键面挡住了，画面上不显眼，但侧视与贴近特写能看到手指「切进」琴键）。这里在每只手解算完成后，
        // 只在手正在演奏时，对其中的休息手指测指节余量，按所需角度在指根（MCP）沿弯曲轴的反方向抬指；演奏手指不受影响，
        // 不牺牲指尖贴键。抬指量快升（300°/s）慢落（60°/s），手指开始演奏时 180°/s 收回。

        /// <summary>是否启用休息手指防穿模（默认开启）。</summary>
        public bool EnableRestingClearance = true;

        private const float RestingLiftLever = 0.06f;      // 指根到典型穿模点的力臂（米）
        private const float RestingLiftMaxDegrees = 28f;
        private const float RestingThumbMaxDegrees = 14f;
        private const float RestingLiftRiseSpeed = 300f;
        private const float RestingLiftFallSpeed = 60f;
        private const float RestingLiftReleaseSpeed = 180f;
        private readonly float[] _leftRestingLift = new float[MaxFingers];
        private readonly float[] _rightRestingLift = new float[MaxFingers];

        /// <summary>某手某指当前的休息抬指角（度，测试 / 诊断用）。</summary>
        public float GetRestingLift(bool leftHand, int finger)
        {
            var lifts = leftHand ? _leftRestingLift : _rightRestingLift;
            return finger >= 0 && finger < lifts.Length ? lifts[finger] : 0f;
        }

        private void ApplyRestingClearance(IHandRig hand, bool leftHand)
        {
            var lifts = leftHand ? _leftRestingLift : _rightRestingLift;
            var probe = (_targetSource as HandIKController)?.ClearanceProbe;
            if (!EnableRestingClearance || probe == null || !probe.IsBuilt || !(hand is IFingerJointRig rig))
            {
                System.Array.Clear(lifts, 0, lifts.Length);
                return;
            }

            float dt = Mathf.Max(0f, Time.deltaTime);
            float scale = probe.HandScale(rig);
            int count = Mathf.Min(hand.Fingers.Count, MaxFingers);

            // 只在这只手正在演奏（至少一根手指分配了音符）时处理：整手空闲时手悬在键面上方，
            // 保持规格静止 C 形（MCP 25° / PIP 30° / DIP 15°，见 RestingHand_ReturnsToNaturalCurl）。
            bool handPlaying = false;
            for (int f = 0; f < count && !handPlaying; f++) handPlaying = Assignment.GetPitch(leftHand, f) >= 0;

            for (int f = 0; f < count; f++)
            {
                if (!rig.TryGetJoints(f, out var proximal, out _, out _) || proximal == null) continue;
                // 演奏中没有分配音符的手指视为休息手指（即使控制器给了预备目标，也不应压在琴键里）
                bool resting = handPlaying && Assignment.GetPitch(leftHand, f) < 0;
                float maxDegrees = f == 0 ? RestingThumbMaxDegrees : RestingLiftMaxDegrees;
                if (resting)
                {
                    // 在本帧解算出的原始姿态上测量，得到「需要多少」而不是增量，避免累积漂移
                    float clearance = probe.FingerClearance(rig, f, -1, scale);
                    float needed = clearance < -KeyClearanceProbe.PenetrationTolerance * 0.5f
                        ? Mathf.Min(maxDegrees, Mathf.Rad2Deg * (-clearance + 0.0015f) / RestingLiftLever)
                        : 0f;
                    float speed = needed > lifts[f] ? RestingLiftRiseSpeed : RestingLiftFallSpeed;
                    lifts[f] = Mathf.MoveTowards(lifts[f], needed, speed * dt);
                }
                else
                {
                    lifts[f] = Mathf.MoveTowards(lifts[f], 0f, RestingLiftReleaseSpeed * dt);
                }

                if (lifts[f] > 0.01f)
                {
                    // 指根沿弯曲轴的反方向转动 = 伸展（抬指）
                    Vector3 axis = JointAxes.WorldCurlAxis(proximal, 0);
                    proximal.rotation = Quaternion.AngleAxis(-lifts[f], axis) * proximal.rotation;
                }
            }
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
                    if (phases[f] == FingerPhase.Release) held[f] = 0f;
                    held[f] += Time.deltaTime;
                    phase = held[f] < HoldSettleSeconds ? FingerPhase.Press : FingerPhase.Hold;
                }
                else
                {
                    if (hasTarget)
                    {
                        held[f] = 0f;
                        phase = FingerPhase.Approach;
                    }
                    else
                    {
                        // 保留短促回弹，直接回到静止弯曲会看起来像逐键切换姿态。
                        float release = phases[f] == FingerPhase.Press || phases[f] == FingerPhase.Hold
                            ? 0.075f : held[f];
                        held[f] = Mathf.Max(0f, release - Time.deltaTime);
                        phase = held[f] > 0f ? FingerPhase.Release : FingerPhase.Rest;
                    }
                }
                phases[f] = phase;

                if (jointRig != null && jointRig.TryGetJoints(f, out var proximal, out var intermediate, out var distal))
                {
                    bool thumbPad = f == 0 && hand is IThumbContactRig { HasThumbPad: true };
                    if ((UseAnatomicalContact || thumbPad) && finger == proximal && hand is IHandOrientationRig basis)
                    {
                        UpdateAnatomicalFinger(hand, basis, jointRig, f, proximal, intermediate, distal,
                            worldTarget, hasTarget, pressed, leftHand, span > 0, held[f], phase == FingerPhase.Release);
                    }
                    else
                    {
                        UpdateFingerJoints(finger, jointRig, f, proximal, intermediate, distal,
                            worldTarget, hasTarget, phase, leftHand);
                        if (pressed && _targetSource != null && _targetSource.TryGetKeyPressPoint(pitch, out var press))
                            ConstrainFingertipContact(finger, jointRig.GetFingertip(f), press, finger == proximal, held[f]);
                    }
                }
                else
                {
                    UpdateFingerLegacy(finger, pitch, hasTarget, worldTarget, minPitch, span);
                }
            }

            ApplyRestingClearance(hand, leftHand);
        }

        // ==================== 三关节铰链 IK（附录 A.4.2 / A.4.4） ====================

        private void UpdateAnatomicalFinger(IHandRig hand, IHandOrientationRig basis, IFingerJointRig rig,
            int fingerIndex, Transform proximal, Transform intermediate, Transform distal,
            Vector3 target, bool hasTarget, bool pressed, bool leftHand, bool chord, float heldSeconds,
            bool releasing = false)
        {
            // 侧摆仅发生在掌指关节，铰链方向随侧摆共同旋转。中节与末节只允许沿自己的轴屈伸。
            var parent = proximal.parent;
            var tip = rig.GetFingertip(fingerIndex);
            // 落键后短时间内指腹仍在目标附近时，保留现有手型。
            // 避免解算器在多个等价弯曲姿势间折返。腕位变化让指腹离开接触区后自然恢复追踪。
            bool thumb = fingerIndex == 0 && hand is IThumbContactRig { HasThumbPad: true };
            float contactTolerance = thumb ? 0f : 0.003f;
            if (!thumb && chord && UseDampedJointMotion && pressed && hasTarget && tip != null)
            {
                var error = tip.position - target;
                // 键顶允许小幅横向移动，沿键面法向保留更严格的接触范围。
                if (Mathf.Abs(error.y) <= 0.0025f && error.x * error.x + error.z * error.z <= 0.000036f)
                {
                    var velocities = leftHand ? _leftJointVelocity : _rightJointVelocity;
                    System.Array.Clear(velocities, fingerIndex * 3, 3);
                    return;
                }
            }

            var sides = leftHand ? _leftSideAngles : _rightSideAngles;
            float side = sides[fingerIndex];
            var angles = _jointScratch;
            var curls = leftHand ? _leftJointAngle : _rightJointAngle;
            var primed = leftHand ? _leftJointPrimed : _rightJointPrimed;
            // 进音时目标若已换到另一键，留出侧摆和触键所需的速度。
            bool reaching = thumb && pressed && hasTarget && tip != null &&
                Vector3.ProjectOnPlane(tip.position - target, Vector3.up).sqrMagnitude > 0.0004f;
            var previousAngles = thumb && !reaching && primed[0] ? curls : null;
            if (thumb && hasTarget && tip != null && !reaching)
            {
                // 指腹进键前末节轻弯，下压后松开一部分。位置不足以同时达到时优先保证触键。
                float load = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(heldSeconds / Mathf.Max(0.01f, HoldSettleSeconds)));
                float bend = pressed ? Mathf.Lerp(11f, 8f, load) : 13f;
                Vector3 contact = FitAnatomicalFinger(hand, basis, fingerIndex, proximal, intermediate, distal,
                    tip, target, angles, ref side, previousAngles, bend, releasing);
                if ((contact - target).sqrMagnitude > 0.00000625f)
                {
                    side = sides[fingerIndex];
                    FitAnatomicalFinger(hand, basis, fingerIndex, proximal, intermediate, distal,
                        tip, target, angles, ref side, previousAngles, 6f, releasing);
                }
            }
            else
            {
                FitAnatomicalFinger(hand, basis, fingerIndex, proximal, intermediate, distal,
                    hasTarget ? tip : null, target, angles, ref side, previousAngles, 6f, releasing);
            }
            Vector3 sideAxis = parent.InverseTransformDirection(hand.Wrist.TransformDirection(basis.BackNormalLocal));

            float dt = Time.deltaTime;
            bool preparingThumb = thumb && !pressed && hasTarget;
            float sideTarget = thumb && !pressed
                ? Mathf.Lerp(sides[fingerIndex], side, 1f - Mathf.Exp(-dt / (preparingThumb ? 0.023f : 0.063f)))
                : side;
            float sideStep = (pressed ? (thumb && !reaching ? 360f : 540f) : preparingThumb ? 380f : 250f) * dt;
            sides[fingerIndex] = thumb && !primed[0]
                ? side : Mathf.MoveTowards(sides[fingerIndex], sideTarget, sideStep);
            bool trackContact = pressed && hasTarget;
            float extension = thumb ? -13f : 0f;
            float contactSpeed = thumb && !reaching ? 410f : 540f;
            // 预备要跟上将到的音符，离键之后再让指节缓慢回到放松姿态。
            float relaxSeconds = preparingThumb ? 0.034f : thumb ? 0.062f : 0.082f;
            float relaxSpeed = preparingThumb ? 380f : 250f;
            ApplyJoint(proximal, 0, angles[0], MaxMcpDegrees, leftHand, fingerIndex, !pressed, trackContact,
                extension, contactSpeed, relaxSeconds, relaxSpeed);
            ApplyJoint(intermediate, 1, angles[1], MaxPipDegrees, leftHand, fingerIndex, !pressed, trackContact,
                thumb ? 4f : 0f, contactSpeed, relaxSeconds, relaxSpeed);
            ApplyJoint(distal, 2, angles[2], MaxDipDegrees, leftHand, fingerIndex, !pressed, trackContact,
                thumb ? 6f : 0f, contactSpeed, relaxSeconds, relaxSpeed);
            Quaternion flexion = UseDampedJointMotion ? JointAxes.CurlLocalRotation(proximal, 0, curls[fingerIndex * 3])
                : proximal.localRotation;
            var appliedSide = Quaternion.AngleAxis(sides[fingerIndex], sideAxis);
            proximal.localRotation = appliedSide * flexion;
            if (!UseDampedJointMotion || !pressed || !hasTarget || tip == null ||
                tip.position.y >= target.y - contactTolerance) return;

            // 只沿已标定的屈伸轴解除过度下压，从近节开始回退。侧摆与腕部朝向不参与兜底。
            float floor = target.y - contactTolerance;
            for (int j = 0; j < 3 && tip.position.y < floor; j++)
            {
                var joint = j == 0 ? proximal : j == 1 ? intermediate : distal;
                int slot = fingerIndex * 3 + j;
                float before = curls[slot];
                float bestY = tip.position.y;
                var sideRotation = j == 0 ? appliedSide : Quaternion.identity;
                float open = j == 0 ? extension : thumb ? (j == 1 ? 4f : 6f) : 0f;
                joint.localRotation = sideRotation * JointAxes.CurlLocalRotation(joint, j, open);
                if (tip.position.y <= bestY)
                {
                    joint.localRotation = sideRotation * JointAxes.CurlLocalRotation(joint, j, before);
                    continue;
                }
                float safe = open;
                float low = open;
                float high = before;
                if (tip.position.y >= floor)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float middle = (low + high) * 0.5f;
                        joint.localRotation = sideRotation * JointAxes.CurlLocalRotation(joint, j, middle);
                        if (tip.position.y >= floor) low = middle;
                        else high = middle;
                    }
                    safe = low;
                }
                joint.localRotation = sideRotation * JointAxes.CurlLocalRotation(joint, j, safe);
                curls[slot] = safe;
                (leftHand ? _leftJointVelocity : _rightJointVelocity)[slot] = 0f;
            }
        }

        // 候选腕姿态和实际演奏共用同一套指关节解算。试算后还原骨骼，不写入平滑状态。
        internal Vector3 PredictAnatomicalTip(IHandRig hand, IHandOrientationRig basis, IFingerJointRig rig,
            int finger, Vector3 target)
        {
            if (!rig.TryGetJoints(finger, out var proximal, out var intermediate, out var distal) ||
                rig.GetFingertip(finger) == null) return hand.Wrist.position;
            float side = 0f;
            return FitAnatomicalFinger(hand, basis, finger, proximal, intermediate, distal,
                rig.GetFingertip(finger), target, _jointScratch, ref side);
        }

        private Vector3 FitAnatomicalFinger(IHandRig hand, IHandOrientationRig basis, int fingerIndex,
            Transform proximal, Transform intermediate, Transform distal, Transform tip, Vector3 target,
            float[] angles, ref float side, float[] previous = null, float thumbDistalBend = 6f,
            bool releasing = false)
        {
            bool thumb = fingerIndex == 0 && hand is IThumbContactRig { HasThumbPad: true };
            float sideLimit = thumb ? 35f : fingerIndex == 0 ? 25f : 15f;
            Vector3 palmNormal = hand.Wrist.TransformDirection(basis.BackNormalLocal);
            var parent = proximal.parent;
            Vector3 sideAxis = parent.InverseTransformDirection(palmNormal);

            float rest = RestCurlDegrees;
            float sum = Mathf.Max(0.001f, McpWeight + PipWeight + DipWeight);
            angles[0] = rest * McpWeight / sum;
            angles[1] = rest * PipWeight / sum;
            angles[2] = rest * DipWeight / sum;
            if (thumb && tip == null)
            {
                // 双手拇指对称回到半松开的对掌手型，腕下的三段不沿用四指的 C 形弯曲量。
                angles[0] = 8f;
                angles[1] = 10f;
                angles[2] = 12f;
                side = 7f * basis.SideSign;
                return target;
            }
            if (!thumb && tip == null && releasing)
            {
                angles[0] *= 0.72f;
                angles[1] *= 0.72f;
                angles[2] *= 0.72f;
            }

            if (tip != null)
            {
                _joints[0] = proximal;
                _joints[1] = intermediate;
                _joints[2] = distal;
                // 从轻弯手型求解，阻尼层负责保留跨帧连续性。
                angles[0] *= 0.40f;
                angles[1] *= 0.40f;
                angles[2] *= 0.40f;
                if (thumb)
                {
                    // 对掌时保留末节的轻弯，并从上一帧开始微调，避免近节独自拉直整根拇指。
                    angles[0] = previous == null ? 8f : previous[0];
                    angles[1] = previous == null ? 10f : Mathf.Max(4f, previous[1]);
                    angles[2] = Mathf.Max(thumbDistalBend, previous == null ? 12f : previous[2]);
                }

                Quaternion before0 = proximal.localRotation;
                Quaternion before1 = intermediate.localRotation;
                Quaternion before2 = distal.localRotation;
                var rawDirection = Vector3.ProjectOnPlane(target - proximal.position, palmNormal);
                var ahead = Vector3.ProjectOnPlane(hand.Wrist.TransformDirection(basis.FingerForwardLocal), palmNormal);
                // 普通手指不能转身触摸指根后方。预备指和拇指保留各自的自然侧摆范围。
                bool behind = Vector3.Dot(rawDirection, ahead) < -0.004f * ahead.magnitude;
                if (behind && fingerIndex != 0)
                {
                    side = 0f;
                    angles[0] = rest * McpWeight / sum;
                    angles[1] = rest * PipWeight / sum;
                    angles[2] = rest * DipWeight / sum;
                }
                else
                {
                    var sideRotation = Quaternion.AngleAxis(side, sideAxis);
                    proximal.localRotation = sideRotation * JointAxes.CurlLocalRotation(proximal, 0, angles[0]);
                    intermediate.localRotation = JointAxes.CurlLocalRotation(intermediate, 1, angles[1]);
                    distal.localRotation = JointAxes.CurlLocalRotation(distal, 2, angles[2]);

                    for (int i = 0, iterations = thumb ? 12 : 7; i < iterations; i++)
                    {
                        float sideDelta = RotationToward(proximal.position, palmNormal, tip.position, target);
                        side = Mathf.Clamp(side + sideDelta, -sideLimit, sideLimit);
                        sideRotation = Quaternion.AngleAxis(side, sideAxis);
                        proximal.localRotation = sideRotation * JointAxes.CurlLocalRotation(proximal, 0, angles[0]);

                        for (int j = 0; j < 3; j++)
                        {
                            var joint = _joints[j];
                            var axis = JointAxes.WorldCurlAxis(joint, j);
                            if (j == 0) axis = parent.rotation * sideRotation * Quaternion.Inverse(parent.rotation) * axis;
                            float max = j == 0 ? Mathf.Min(MaxMcpDegrees, 65f) : j == 1 ? Mathf.Min(MaxPipDegrees, 72f) : Mathf.Min(MaxDipDegrees, 48f);
                            float delta = RotationToward(joint.position, axis, tip.position, target);
                            if (thumb && j == 0) max = Mathf.Min(max, 44f);
                            float minimum = thumb && j == 0 ? -13f : thumb ? (j == 1 ? 4f : thumbDistalBend) : 0f;
                            float preferredBend = thumb && j > 0
                                ? ((j == 1 ? 8f : 11f) - angles[j]) * 0.12f : 0f;
                            angles[j] = Mathf.Clamp(angles[j] + delta * 0.8f + preferredBend, minimum, max);
                            // 拇指的指间关节与掌指关节不使用四指的中末节耦合。
                            if (!thumb && j == 2 && DipCouplingSlackDegrees >= 0f)
                            {
                                float coupled = angles[1] * DipPipRatio;
                                angles[j] = Mathf.Clamp(angles[j], Mathf.Max(0f, coupled - DipCouplingSlackDegrees),
                                    Mathf.Min(max, coupled + DipCouplingSlackDegrees));
                            }
                            Quaternion curl = JointAxes.CurlLocalRotation(joint, j, angles[j]);
                            joint.localRotation = j == 0 ? sideRotation * curl : curl;
                        }
                        if ((tip.position - target).sqrMagnitude < (thumb ? 0.0000015f : 0.000004f)) break;
                    }
                    Vector3 solved = tip.position;
                    proximal.localRotation = before0;
                    intermediate.localRotation = before1;
                    distal.localRotation = before2;
                    return solved;
                }
            }
            side = 0f;
            return tip != null ? tip.position : target;
        }

        private static float RotationToward(Vector3 pivot, Vector3 axis, Vector3 current, Vector3 target)
        {
            var from = Vector3.ProjectOnPlane(current - pivot, axis);
            var to = Vector3.ProjectOnPlane(target - pivot, axis);
            return from.sqrMagnitude > 1e-8f && to.sqrMagnitude > 1e-8f
                ? Vector3.SignedAngle(from, to, axis) : 0f;
        }

        private static void ConstrainFingertipContact(Transform root, Transform tip, Vector3 press, bool sharedRoot, float heldSeconds)
        {
            if (tip == null || tip == root || !tip.IsChildOf(root)) return;

            Vector3 offset = tip.position - root.position;
            float radius = offset.magnitude;
            if (radius < 1e-5f) return;

            if (sharedRoot || press.z < root.position.z)
            {
                // 在近节保持连续的同时，让屈曲后的整条指链朝落点偏转。
                const float ContactHeight = 0.003f;
                float maxYaw = sharedRoot ? 42f : 17f;
                float maxPitch = sharedRoot ? 56f : 17f;
                Vector3 desired = press + Vector3.up * ContactHeight - root.position;
                var horizontal = Vector3.ProjectOnPlane(desired, Vector3.up);
                var tipHorizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
                if (horizontal.sqrMagnitude > 1e-8f && tipHorizontal.sqrMagnitude > 1e-8f)
                {
                    float yaw = Vector3.SignedAngle(tipHorizontal, horizontal, Vector3.up);
                    var turn = Quaternion.AngleAxis(Mathf.Clamp(yaw, -maxYaw, maxYaw), Vector3.up);
                    Vector3 turned = turn * offset;
                    Vector3 pitchAxis = Vector3.Cross(turned, Vector3.up);
                    float currentPitch = Mathf.Asin(Mathf.Clamp(turned.y / radius, -1f, 1f));
                    float targetPitch = Mathf.Asin(Mathf.Clamp(desired.y / radius, -1f, 1f));
                    float pitchDelta = Mathf.Clamp((targetPitch - currentPitch) * Mathf.Rad2Deg, -maxPitch, maxPitch);
                    var bend = Quaternion.AngleAxis(pitchDelta, pitchAxis);
                    var steered = bend * turned;
                    float oldHeightError = offset.y - desired.y;
                    float newHeightError = steered.y - desired.y;
                    // 键面法向误差决定指腹是否浮在键上。比较时给高度更多权重，
                    // 同时保留水平代价，避免把指尖转到隔壁琴键。
                    float oldCost = (offset - desired).sqrMagnitude + 4f * oldHeightError * oldHeightError;
                    float newCost = (steered - desired).sqrMagnitude + 4f * newHeightError * newHeightError;
                    if (newCost < oldCost)
                    {
                        // 起音后短促地进入贴键姿势，避免共享近节的蒙皮骨骼突然拧向目标。
                        float contact = Mathf.SmoothStep(0f, 1f, heldSeconds / 0.032f);
                        root.rotation = Quaternion.Slerp(Quaternion.identity, bend * turn, contact) * root.rotation;
                    }
                    offset = tip.position - root.position;
                }
            }

            if (tip.position.y >= press.y) return;
            Vector3 axis = Vector3.Cross(offset, Vector3.up);
            if (axis.sqrMagnitude < 1e-10f) return;

            // 在关节阻尼之后校正末端高度，保持指骨长度与各关节的相对角度。
            float from = Mathf.Asin(Mathf.Clamp(offset.y / radius, -1f, 1f));
            float to = Mathf.Asin(Mathf.Clamp((press.y - root.position.y) / radius, -1f, 1f));
            root.rotation = Quaternion.AngleAxis((to - from) * Mathf.Rad2Deg, axis) * root.rotation;
        }

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
            Vector3 worldTarget, bool hasTarget, FingerPhase phase, bool leftHand)
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
                // 铰链平面：默认取手掌上方向（手背朝上）。手掌带滚转时该平面会倾斜，
                // 而琴键目标始终在竖直平面内 —— 20° 滚转就能造成 20 mm 级「够不到」
                // （实测 R2 残差几乎全落在 palmUp 方向上）。AimPlaneUpBlend 把平面朝世界上方
                // 混合回去：1 = 竖直平面（目标必在面内），0 = 完全跟随手掌。
                Vector3 palmUp = finger.parent.rotation * Vector3.up;
                palmUp = palmUp.sqrMagnitude > 1e-6f ? palmUp : Vector3.up;
                aimUp = Vector3.Slerp(palmUp, Vector3.up, Mathf.Clamp01(AimPlaneUpBlend));
                if (aimUp.sqrMagnitude < 1e-6f) aimUp = Vector3.up;
                aim = true;

                SolveJointAngles(proximal, intermediate, distal, l2, worldTarget, angles,
                    rig.GetFingertip(fingerIndex));
            }
            else
            {
                float rest = RestCurlDegrees * Mathf.Deg2Rad;
                if (phase == FingerPhase.Release) rest *= 0.72f;
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

            // ---- 关节角：逐关节限幅并按二阶动力学推进 ----
            ApplyJoint(proximal, 0, angles[0] * Mathf.Rad2Deg, MaxMcpDegrees, leftHand, fingerIndex);
            ApplyJoint(intermediate, 1, angles[1] * Mathf.Rad2Deg, MaxPipDegrees, leftHand, fingerIndex);
            ApplyJoint(distal, 2, angles[2] * Mathf.Rad2Deg, MaxDipDegrees, leftHand, fingerIndex);

            if (hasTarget) RecordReachIssue(distance, l0 + l1 + l2, angles);
        }

        /// <summary>
        /// 单关节：把目标角按临界阻尼二阶动力学推进后写入旋转。
        /// <para>
        /// 一阶 <c>Slerp(cur, tgt, dt·k)</c> 的角速度在第一帧就达峰值（「啪」地弹到位），
        /// 二阶 <c>a = ω²(θ_t − θ) − 2ω·ω_ang</c> 才有「加速—匀速—减速」的肌腱拉动感。
        /// 状态按 (手指, 关节) 索引复用数组，Update 路径零分配。
        /// </para>
        /// </summary>
        private void ApplyJoint(Transform joint, int jointIndex, float degrees, float maxDegrees,
            bool leftHand, int fingerIndex, bool relaxAnatomicalFinger = false, bool trackContact = false,
            float minDegrees = 0f, float contactSpeed = 540f, float relaxSeconds = 0.082f, float relaxSpeed = 250f)
        {
            if (joint == null) return;

            float targetDeg = Mathf.Clamp(degrees, minDegrees, maxDegrees);
            int slot = fingerIndex * 3 + jointIndex;
            if (slot < 0 || slot >= _leftJointAngle.Length) return;

            if (!UseDampedJointMotion)
            {
                // 回退路径：与改动前逐位一致
                var legacy = JointAxes.CurlLocalRotation(joint, jointIndex, targetDeg);
                joint.localRotation = Quaternion.Slerp(joint.localRotation, legacy,
                    Time.deltaTime * JointSlerpSpeed);
                return;
            }

            var angles = leftHand ? _leftJointAngle : _rightJointAngle;
            var velos = leftHand ? _leftJointVelocity : _rightJointVelocity;
            var primed = leftHand ? _leftJointPrimed : _rightJointPrimed;

            float dt = Mathf.Max(0f, Time.deltaTime);
            if (!primed[slot])
            {
                // 首帧直接到位：否则手指会从 0° 慢慢爬到 C 形，入场动画里可见
                angles[slot] = targetDeg;
                velos[slot] = 0f;
                primed[slot] = true;
                joint.localRotation = JointAxes.CurlLocalRotation(joint, jointIndex, targetDeg);
                return;
            }

            if (dt <= 1e-6f) return;

            if (relaxAnatomicalFinger)
            {
                // 预备和离键用连续回落，避免下一目标变化时游离指突然折返。
                float velocity = velos[slot];
                float angle = Mathf.SmoothDamp(angles[slot], targetDeg, ref velocity,
                    relaxSeconds, relaxSpeed, dt);
                angles[slot] = Mathf.Clamp(angle, minDegrees, maxDegrees);
                velos[slot] = angle == angles[slot] ? velocity : 0f;
                joint.localRotation = JointAxes.CurlLocalRotation(joint, jointIndex, angles[slot]);
                return;
            }

            float omega = Mathf.Max(1f, JointSpringOmega);
            float h = Mathf.Clamp(JointMaxSubStep, 1f / 480f, 0.05f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / h), 1, 32);
            float step = dt / steps;

            float a = angles[slot];
            float v = velos[slot];
            float band = Mathf.Max(0f, JointContactBandDegrees);
            float contactZeta = Mathf.Max(1f, JointContactDampingRatio);

            for (int i = 0; i < steps; i++)
            {
                float error = targetDeg - a;

                // 接触带内切换过阻尼（键面刚体阻挡的等效建模）。自由二阶在目标停止后会保留
                // 残余速度、冲过目标 —— 实测键面 P50 由全关时的 +8.6 mm 变成 −0.3 mm，
                // 压键帧 120→183。过阻尼让减速提前，速度曲线仍呈钟形但尾部不越界。
                float zeta = Mathf.Abs(error) < band ? contactZeta : 1f;
                float accel = omega * omega * error - 2f * zeta * omega * v;
                v += accel * step;
                a += v * step;
            }

            // 不允许反关节：过冲穿过 0° 时钳住并把速度归零（否则会在 0 附近来回抖）
            if (a < minDegrees)
            {
                a = minDegrees;
                if (v < 0f) v = 0f;
            }
            // 目标角度限幅不能阻止残余速度造成过冲，实际角度也必须受上限约束。
            if (a > maxDegrees)
            {
                a = maxDegrees;
                if (v > 0f) v = 0f;
            }

            if (trackContact)
            {
                float tracked = Mathf.MoveTowards(a, targetDeg, contactSpeed * dt);
                if (contactSpeed < 540f)
                    tracked = Mathf.MoveTowards(angles[slot], tracked, contactSpeed * dt);
                if (tracked != a) v = 0f;
                a = tracked;
            }
            angles[slot] = a;
            velos[slot] = v;
            joint.localRotation = JointAxes.CurlLocalRotation(joint, jointIndex, a);
        }

        /// <summary>
        /// 铰链轴 CCD（Cyclic Coordinate Descent）：每根手指只绕自身局部 X 轴屈伸，
        /// 从末端关节到指根依次把「关节 → 指尖」转向目标。
        /// <para>
        /// 关节角一律 ≥ 0（不允许反关节），并受 MCP/PIP/DIP 各自上限约束；
        /// 6 次迭代对 3 关节足以收敛。目标不可达时停在最接近的合法姿态，并计入限位统计。
        /// </para>
        /// <para>
        /// <b>指尖模型必须与度量口径一致</b>：优先用 <see cref="IFingertipRig"/> 给出的真实指尖
        /// 节点，只有拿不到时才退回 <c>distal.TransformPoint(0,0,l2)</c> 的长度估算。
        /// 之前一律用估算，导致「求解认为到了、真实指尖还差几毫米」的系统偏差——
        /// 一阶关节插值的滞后恰好抵消了它，换成准确跟随的二阶后立刻暴露为
        /// 「键面 P50 由 +8.6 mm 变成 −5.6 mm」的压键。
        /// </para>
        /// </summary>
        private void SolveJointAngles(Transform proximal, Transform intermediate, Transform distal,
            float l2, Vector3 target, float[] angles, Transform fingertip)
        {
            // 初值取自然弯曲：全直状态下目标若正好在手指射线上，旋转梯度为 0 会卡住。
            // 注意用 SolveSeedCurlDegrees（求解起点）而不是 RestCurlDegrees（静止手型）——
            // 后者已按 C 形手规格调到 70°，直接当初值会改变收敛路径。
            float rest = SolveSeedCurlDegrees * Mathf.Deg2Rad;
            float w0 = Mathf.Max(0.001f, McpWeight);
            float w1 = Mathf.Max(0.001f, PipWeight);
            float w2 = Mathf.Max(0.001f, DipWeight);
            float sum = w0 + w1 + w2;
            angles[0] = rest * w0 / sum;
            angles[1] = rest * w1 / sum;
            angles[2] = rest * w2 / sum;

            // CCD 临时写入关节以测指尖位置，输出只由阻尼层提交。
            Quaternion proximalBefore = proximal.localRotation;
            Quaternion intermediateBefore = intermediate.localRotation;
            Quaternion distalBefore = distal.localRotation;

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
                    Vector3 tip = fingertip != null ? fingertip.position : distal.TransformPoint(0f, 0f, l2);
                    Vector3 axis = JointAxes.WorldCurlAxis(joint, j);

                    Vector3 a = Vector3.ProjectOnPlane(tip - jointPos, axis);
                    Vector3 b = Vector3.ProjectOnPlane(target - jointPos, axis);
                    if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) continue;

                    float delta = Vector3.SignedAngle(a, b, axis) * Mathf.Deg2Rad;
                    float max = j == 0 ? maxMcp : (j == 1 ? maxPip : maxDip);
                    angles[j] = Mathf.Clamp(angles[j] + delta, 0f, max);

                    // DIP ↔ PIP 解剖耦合（掌腱膜被动牵拉：DIP 无法单独伸直，屈曲约为 PIP 的 0.62）。
                    // 完全锁死会损害可达性（指尖够不到键面），完全放开则退化成无约束的钩手；
                    // 所以只在「耦合值 ± 容差」内允许自由调整。<see cref="DipCouplingSlackDegrees"/>
                    // 设为负值即禁用耦合（回退到无约束 CCD）。
                    if (j == 2 && DipCouplingSlackDegrees >= 0f)
                    {
                        float coupled = angles[1] * DipPipRatio;
                        float slack = DipCouplingSlackDegrees * Mathf.Deg2Rad;
                        angles[2] = Mathf.Clamp(angles[2], coupled - slack, coupled + slack);
                        angles[2] = Mathf.Clamp(angles[2], 0f, maxDip);
                    }
                }
            }

            proximal.localRotation = proximalBefore;
            intermediate.localRotation = intermediateBefore;
            distal.localRotation = distalBefore;
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
                if (_pressedHands.IsLeft(pitch) == leftHand) _scratch.Add(pitch);
            }
            _scratch.Sort();
        }
    }
}
