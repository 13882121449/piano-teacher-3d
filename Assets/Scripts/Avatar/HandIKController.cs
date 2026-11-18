using System.Collections.Generic;
using PianoTeacher3D.MIDI;
using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 把音符目标转换为手腕位置与手部目标（方案 4.2 / 3.3 数据流第 6 步）。
    /// <para>
    /// 阶段五改进（方案 7 阶段五「改善手腕路径、手指姿态和双手过渡动作」）：
    /// ① <b>换位抬升</b>：水平位移超过阈值时目标先抬升、接近后回落，形成「抬起—平移—落下」过渡，
    ///    避免贴键直线横扫造成的穿模与跳姿；
    /// ② <b>间隔自适应速度</b>：按最近音符的平均间隔调整移动速度（稀疏慢、密集快）；
    /// ③ <b>异常姿态钳制</b>：每帧校验手腕是否越界并钳回，对应方案阶段五验收「异常姿态检查」；
    /// ④ <b>动作质量采样</b>：对应方案阶段五验收「评估动作连续性、目标琴键命中率、左右手协调」。
    /// </para>
    /// <para>
    /// <b>本轮升级（方案 5.3 第二阶段 / 审查 1.2B 第二步）</b>：
    /// ⑤ <b>键面目标点</b>：为每个琴键预计算可复用的按压目标点（键面前半段，而不是键位中心的任意高度）；
    /// ⑥ <b>黑键独立高度</b>：黑键目标高度按 <c>BlackHeight</c>、白键按 <c>WhiteHeight</c> 分别计算
    ///    （不改变琴键映射的真实坐标，仅用于目标与误差统计）；
    /// ⑦ <b>指尖与手腕分离求解</b>：手腕/掌心仍按原有速度与钳制规则移动，指尖目标单独求解并用于
    ///    「指尖到目标键面误差 / 离键悬空」统计；
    /// ⑧ <b>和弦先成形再下压</b>：和弦出现时指尖目标从（成型的）手型上方在 <see cref="ChordFormationSeconds"/>
    ///    内过渡到键面，避免所有指尖在同一帧突然到位。
    /// </para>
    /// 左右手读取曲目加载时的分配，指法由估算器给出（方案 2.3）。
    /// </summary>
    [DefaultExecutionOrder(3)]
    public class HandIKController : MonoBehaviour, IFingertipTargetSource
    {
        [Header("手部目标几何（真实钢琴尺度，米）")]
        /// <summary>
        /// 腕部高度（键面约 0.742 m）。取 0.799 使掌指关节比键面高约 60 mm ——
        /// 这是「手指弯曲后指尖仍落在键上」的几何前提；手太低会让手指必须向后反折。
        /// </summary>
        public float HandHeight = 0.799f;
        /// <summary>
        /// 空闲时（无音符、无预置）腕高额外下沉量（米）。
        /// 为什么需要：演奏帧的指尖目标会把手指拉低到键面（门禁 P50 ≈ 0），
        /// 但一停止演奏，手指回到静止弯曲（RestCurlDegrees），指尖就抬高 40–50 mm，
        /// 特写里整只手像悬在半空。真实的钢琴静止手型是「掌 C 型、指腹轻搭在键面上」，
        /// 所以空闲时把腕整体压低，让静止手型也贴住键面。
        /// </summary>
        public float IdleHandDrop = 0f;

        /// <summary>
        /// 腕部相对键根的深度偏移（米）。取 0.232 使掌指关节位于键面按压点<b>后方约 55 mm</b>（靠演奏者一侧）：
        /// 指尖因此是「向前下方」落到键上，而不是向后缩回掌心。
        /// 旧值 0.12 会把指根放到按压点<b>前方</b>，导致指尖必须反折、手型呈爪状。
        /// </summary>
        public float HandDepth = 0.232f;

        /// <summary>
        /// 手臂解算策略。默认 <see cref="CapsuleArmChainSolver"/>（程序化胶囊，行为与改造前逐位一致）；
        /// 蒙皮角色必须换成 <see cref="RotationalArmChainSolver"/>，否则会拉伸骨骼、破坏蒙皮。
        /// </summary>
        public IArmChainSolver ArmSolver { get; set; } = CapsuleArmChainSolver.Instance;

        [Header("移动（过渡动作与自适应速度）")]
        public float MoveSpeed = 12f;        // 基准移动平滑速度
        public float MinSpeedScale = 0.55f;  // 长间隔（稀疏片段）速度倍率下限
        public float MaxSpeedScale = 1.8f;   // 短间隔（密集片段）速度倍率上限
        public float SlowGapSeconds = 0.6f;  // 达到速度下限对应的音符平均间隔
        public float FastGapSeconds = 0.12f; // 达到速度上限对应的音符平均间隔
        public float GlideThreshold = 0.07f; // 水平位移超过该值视为换位，触发抬升过渡（米）
        public float GlideLift = 0.055f;     // 换位过程中的额外抬升高度（米）
        public float MaxStepPerFrame = 0.035f; // 单帧最大位移（米）：硬约束，防瞬移与动作失控

        [Header("自然运动轨迹（临界阻尼二阶跟随，写实化三期）")]
        /// <summary>
        /// 用临界阻尼弹簧（二阶动力学）替代一阶指数逼近，解决「起步突兀 = 僵硬」。
        /// <para>
        /// <b>为什么一阶指数逼近看起来僵硬</b>：<c>Lerp(cur, tgt, k)</c> 的位移是
        /// <c>1−(1−k)^n</c>，速度 <c>k(1−k)^n</c> 在<b>第一帧就达到最大</b>、之后单调衰减。
        /// 也就是手一启动就全速冲出、然后一路减速，缺了人手的「加速段」。
        /// </para>
        /// <para>
        /// <b>为什么不用 minimum jerk（五阶多项式）</b>：它要求「先固定目标、再规划整段」，
        /// 而本场景的目标每帧都在变（手掌修正、预置切换、连续音符），
        /// 实测会退化成「每帧重建轨迹段、每帧原地不动」——键面 P95 从 10 mm 恶化到 84 mm
        /// （bench 三曲，见 Docs/perf/hand-baseline-2026-10-31.md）。
        /// 二阶跟随器没有「段」的概念，目标怎么漂都只是重新受力，不存在重建死锁。
        /// </para>
        /// <para>
        /// <b>物理依据</b>：<c>a = ω²(x_target − x) − 2ωv</c> 是临界阻尼弹簧（PD 控制器），
        /// 阶跃响应下加速度在起点最大、速度呈钟形单峰、终点平滑落位——正是人手臂
        /// 「慢起—快中—缓停」的形状。游戏动画里用它做次级运动（follow/lag）是标准做法。
        /// </para>
        /// </summary>
        public bool UseDampedSpring = false;

        /// <summary>
        /// 弹簧角频率（rad/s）。整定依据：临界阻尼 2% 整定时间 ≈ 5.8/ω，
        /// 取 18 → 约 0.32 s，与原来指数逼近（MoveSpeed = 12，时间常数 0.083 s、
        /// 整定时间约 0.33 s）的<b>总时长基本一致</b>——只改曲线形状，不改跟随快慢。
        /// 峰值速度 ≈ 0.37·ω·d：250 mm 换位 ≈ 1.67 m/s ≈ 28 mm/帧，低于 35 mm 硬约束。
        /// </summary>
        public float SpringOmega = 18f;

        /// <summary>
        /// 积分子步上限（秒）。半隐式欧拉在 ω·dt 接近 1 时会振荡，
        /// 低帧率下必须切子步（ω=18 时 dt 上限 1/120 s → ω·dt = 0.15，余量充足）。
        /// </summary>
        public float SpringMaxSubStep = 1f / 120f;

        [Header("距离自适应速度（写实化二期：A/B 后默认关闭）")]
        /// <summary>
        /// 是否启用「远距离满速直线、近距离指数收敛」的自适应速度。
        /// <para>
        /// <b>默认关闭，且实测无收益</b>：bench 三曲 A/B 显示换位峰值几乎不动
        /// （63.9→66.5 / 141.2→142.7 / 250.9→249.7 mm），压键帧与悬空帧反而上升
        /// （chords 悬空 227→272、压键 225→264）。原因：<b>提速不是瓶颈</b>——
        /// 滞后发生在「目标何时被更新」，而不是「手腕每帧能走多远」。
        /// 手正在按音时预置分支不生效（见 UpdateHand 的 preselected 判定），
        /// 目标始终等于「当前按下的键」，那么手腕再快也只是追一个刚好被替换的目标。
        /// 真正的修法需要把目标提前到未来音符（预判式），属独立课题。
        /// 实现与参数保留，供后续接入预判后复用。
        /// </para>
        /// </summary>
        public bool UseDistanceAdaptiveSpeed = false;

        /// <summary>
        /// 小于该距离时完全走指数收敛（保留原有细腻手感）。取 40 mm ≈ 一个键宽多一点。
        /// </summary>
        public float SmoothFollowDistance = 0.04f;
        /// <summary>
        /// 大于该距离时按单帧硬约束满速直线推进。取 120 mm ≈ 跨三个八度的换位量级。
        /// 两值之间用 InverseLerp 连续混合，阈值处不会跳变。
        /// </summary>
        public float FullSpeedDistance = 0.12f;

        /// <summary>
        /// 手移到键盘两端时的额外腕背伸（度）。手伸向琴键外侧时，肩—肘—腕连线偏离身体中线，
        /// 手指要保持竖直落键只能靠腕关节多背伸来补偿；固定俯仰角会让两端的手「手腕僵住」，
        /// 这也是「手腕与前臂不协调」的观感来源之一。取 0 即关闭。
        /// </summary>
        public float OuterWristPitchBias = 6f;

        /// <summary>
        /// 手臂可达比例（接触方案 2.3）：可达半径 = (上臂 + 前臂) × BodyScale × 该值。
        /// 取 0.98 留 2% 余量吸收指尖长度与肩带微动，避免临界抖动误报为不可达。
        /// </summary>
        public const float ArmReachRatio = 0.98f;

        [Header("预置（即将到来的音符，方案 5.3 第二阶段 / 4.2.3 轻量预测）")]
        public float HandLeadSpeedScale = 0.7f; // 预置移动的速度倍率（比按键时略慢，避免抢拍感）

        [Header("指尖贴键（手掌几何反馈，方案 5.3 第二阶段 / 审查 1.2B 第二步）")]
        /// <summary>手掌修正的最大幅度（米）：手腕朝「消除指尖平均误差」的方向移动，幅度有界。</summary>
        public float MaxPalmCorrection = 0.11f;
        /// <summary>手掌修正的跟随速度（指数阻尼）；越大越快收敛，越大也越容易抖。</summary>
        public float PalmCorrectionSpeed = 14f;
        /// <summary>手掌修正增益（0..1）：把平均误差的多少比例用于平移手掌。</summary>
        [Range(0f, 1f)] public float PalmCorrectionGain = 0.8f;
        /// <summary>
        /// 手掌修正的竖直上限（米）。手掌修正原本只修 XZ，竖直误差全留给手指弯曲；
        /// 但实测竖直分量占绝对主导（20.9 mm 里 20.5 mm），手指靠弯曲解不出来。
        /// 这里给竖直分量一个更小的独立上限，避免把手腕抬到手指够不到键面。
        /// </summary>
        public float MaxPalmCorrectionY = 0.022f;
        /// <summary>
        /// H3：按可达上界解析求解掌位偏移。
        /// <para>
        /// 2026-11-06 实测（bench 三曲）：开启后键面 P95 9.9→6.1 / 6.8→2.8 mm（黑键、音阶改善），
        /// 但 P50 从 +1.8→−1.6、−2.6→−5.5、−4.9→−6.2 mm（整体压得更深），穿键帧 241→245，
        /// 和弦曲 P95 5.7→6.7 反而变差 —— 等于「用穿透换悬空」，不是干净收益，默认关闭。
        /// 保留实现供 H3b（几何感知指法分配，先保证目标本身就够得到）落地后重新评估。
        /// </para>
        /// </summary>
        public bool PalmReachSolve = false;

        /// <summary>可达判定余量：目标距离 &lt; 指长 × 该系数即视为可达（留一点弯曲余量）。</summary>
        [Range(0.6f, 0.99f)] public float PalmReachMargin = 0.92f;

        /// <summary>掌位解算的单帧最大偏移（米）。</summary>
        public float MaxPalmReachOffset = 0.06f;

        /// <summary>Tikhonov 阻尼（相对样本数），防止单指病态时把整只手拽飞。</summary>
        public float PalmReachDamping = 0.08f;

        /// <summary>掌位偏移的跟随速度（1/s）。</summary>
        public float PalmReachSpeed = 9f;
        public float TargetMaxSpeed = 4.0f;       // 大跳变时的目标变化速率上限（米/秒）；0 = 不限速
        public float TargetJumpThreshold = 0.20f; // 目标变化小于该值（同手位内旋律跳进）时直接精确对齐

        [Header("姿态校验（防穿模）")]
        public bool EnablePoseClamp = true;

        [Header("手腕姿态（附录 A.4.2）")]
        /// <summary>是否让手腕按角色配置做受限俯仰 / 滚转（只影响姿态，不改变单帧位移上限）。</summary>
        public bool EnableWristOrientation = true;

        /// <summary>与手指关节约束配合，在触键前规划实际指根可达的腕位。</summary>
        public bool UseAnatomicalContact = true;

        /// <summary>角色未提供 <see cref="HandMetrics.WristPitch"/> 时的默认俯仰（度）。</summary>
        public float DefaultWristPitch = 8f;

        /// <summary>手腕最大滚转（度）：键盘两端的掌根朝外侧轻微侧倾。</summary>
        public float MaxWristRollDegrees = 6f;

        /// <summary>手腕姿态跟随速度（指数阻尼）。</summary>
        public float WristOrientationSpeed = 7f;

        [Header("动作质量指标")]
        public bool EnableMetrics = true;
        public float HitRadius = 0.09f;      // 「到达目标键」判定半径（米）
        public float MovingThreshold = 0.0006f; // 「正在移动」判定单帧位移（米）

        /// <summary>指尖压入键面的容差（米）：超过即计为穿模。</summary>
        public float FingertipPenetrationTolerance = 0.004f;

        /// <summary>判定「换位过渡帧」的手腕单帧位移阈值（米）。</summary>
        public float TransitionStepThreshold = 0.010f;

        [Header("指尖目标与黑键（方案 5.3 第二阶段 / 审查 1.2B）")]
        public bool EnableFingertipMetrics = true;
        public float FingertipTargetForward = 0.80f;  // 按压区沿键长方向的比例（0 = 键根，1 = 键前端）
        public float FingertipReachEstimate = 0.085f; // 缺少指尖 Transform 时的指根→指尖长度估计（米）

        /// <summary>
        /// 指腹半径（米）：让<b>指腹</b>贴键，而不是骨骼末端穿过键面。
        /// <para>
        /// <see cref="FingertipWorld"/> 取的是指尖骨骼末端；真实指腹是有厚度的软垫，
        /// 骨骼末端距接触面约 5 mm，所以控制目标要抬这么多。
        /// </para>
        /// <para>
        /// 不抬的后果（实测）：CCD 优化的是 3D 距离，当键位在 XZ 上偏 30–40 mm 时它会
        /// 牺牲 Y 去换总距离，骨骼末端于是穿过键面 5–9 mm —— 表现为「键面 P50 为负、
        /// 压键帧暴涨」（关节二阶准确跟随之后才暴露，此前的一阶滞后一直掩盖着它）。
        /// 统计口径仍按真实键面计算，不会被这个抬升美化。
        /// </para>
        /// </summary>
        public float FingertipPadRadius = 0.005f;
        public float FingertipErrorTolerance = 0.035f;// 指尖到目标键面的「命中」容差（米）
        public float HoverHeight = 0.045f;            // 指尖高于键面超过该值视为「离键悬空」（米）
        public float ThumbOrientationMaxDegrees = 75f; // 拇指轴与掌心前向的最大夹角（超过记为拇指方向异常）
        public float ChordFormationSeconds = 0.10f;   // 和弦「先成手型再下压」的短过渡时长（秒）
        public float ChordApproachHeight = 0.022f;    // 和弦成形阶段指尖相对键面的额外高度（米）
        public float LargeMotionDistance = 0.10f;     // 「跨手位移」分组判定的目标位移阈值（米）
        public float FastScaleGapSeconds = 0.18f;     // 「快速音阶」分组判定的平均音符间隔上限（秒）
        public float SpeedAnomalyStepDelta = 0.020f;  // 连续帧位移变化超过该值记为速度突变（米）

        [Header("手腕高度自适应（接触方案批次 2.4：按指尖—键面法向反馈修正每手腕高）")]
        /// <summary>
        /// 是否启用腕高自适应：按每手「被分配指尖到键面的法向距离」反馈微调该手腕高，
        /// 修正「手型整体偏高/偏低」造成的持续悬空或压键。
        /// <para>
        /// <b>默认关闭</b>——A/B 实测（bench 三曲，2026-10-31，基线见
        /// <c>Docs/perf/hand-baseline-2026-10-31.md</c>）：±8 mm 限幅的收益落在噪声内
        /// （键面 P95 变化 ≤0.5 mm、压键帧 ±5），而真问题的量级是 17–46 mm / 数百帧。
        /// 根因是<b>手指弯曲不足</b>（指尖到不了目标）而非腕高，应由批次 4（C 形手）与
        /// 2.3（可达性）解决。保留实现与参数供后续改进反馈口径后复测开启。
        /// </para>
        /// </summary>
        public bool WristHeightAdaptation = false;

        /// <summary>腕高自适应的单手偏移限幅（米）。±8 mm：只做微调，大幅偏差属于姿态/目标问题。</summary>
        public float MaxWristHeightAdaptation = 0.008f;

        /// <summary>腕高自适应的指数平滑时间常数（秒）。</summary>
        public float WristAdaptationSmoothSeconds = 0.06f;

        /// <summary>指尖高于键面超过该值（米）视为「持续悬空」，触发降腕。</summary>
        public float WristAdaptationHoverThreshold = 0.015f;

        /// <summary>指尖压入键面超过该值（米）视为「压键过深」，触发升腕（与 4 mm 穿模容差留出余量）。</summary>
        public float WristAdaptationPressThreshold = 0.006f;

        [Header("黑键按压（接触方案批次 2：黑键键面高 10 mm，弯曲不足导致指尖压入键面）")]
        /// <summary>
        /// 黑键指尖控制目标相对键面的抬升量（米）。只抬「控制目标」，统计口径仍按真实键面
        /// （<see cref="KeyTouchPoint.SurfaceY"/>），因此指标不会被美化。
        /// <para>
        /// <b>默认 0（不抬）</b>——A/B 实测（bench_black_keys，2026-10-31）：抬 3 mm 后
        /// 指尖压键帧 110→107（仅 -3，噪声级），而键面距离 P50 9.1→11.0 mm、P95 10.2→12.1 mm
        /// 全线变差。说明该曲的压键量级远超 3 mm（平均压入 >7 mm），靠抬目标救不回来，
        /// 必须由批次 4 的手指弯曲控制解决。保留参数供弯曲改进后配合调参。
        /// </para>
        /// </summary>
        public float BlackKeyTargetLift = 0f;
        /// <summary>按住黑键时抬腕的高度（米）：黑键键面比白键高 22 mm，试 11 mm 实测无效（R2 残差 20.9→21.0 mm），默认关。</summary>
        public float BlackKeyHandLift = 0f;
        /// <summary>绕手指轴附加侧倾的上限（度）。</summary>
        public float MaxWristTwistDegrees = 120f;

        private const int MaxRecordedIssues = 8;

        private AvatarManager _avatar;
        private Piano.PianoKeyMap _keyMap;
        private readonly HashSet<int> _pressed = new();
        private readonly MIDI.ActiveNoteHands _pressedHands = new();

        // 腕骨「静止姿态」缓存（rest-relative 朝向用）：只在第一次施加朝向之前采样，
        // 之后不再被 IK 写入污染。左右手分开存，因为两手绑定姿态不同。
        private Quaternion _leftWristRestLocal = Quaternion.identity;
        private Quaternion _rightWristRestLocal = Quaternion.identity;
        private bool _leftWristRestCaptured;
        private bool _rightWristRestCaptured;

        /// <summary>按下音符的时值（秒）：供和弦成形时长自适应（接触方案批次 2.5）。</summary>
        private readonly Dictionary<int, float> _pressedDurations = new();

        private float _currentTime;              // 由控制器每帧传入的统一时间轴时间
        private float _lastNoteTime = float.NaN;
        private float _avgNoteGap = 0.4f;        // 指数平滑的音符平均间隔

        private HandPoseLimits _limits;
        private bool _hasLimits;
        private int _poseIssueCount;
        private int _crossedHandWarningCount;
        private readonly List<string> _poseIssues = new();

        private readonly HandMotionAccumulator _metrics = new();
        private Vector3 _leftTargetPos, _rightTargetPos;
        private bool _leftHasTarget, _rightHasTarget;
        private Vector3 _leftSmoothedTarget, _rightSmoothedTarget;
        private bool _hasLeftSmoothed;
        private bool _hasRightSmoothed;

        private int _upcomingLeftPitch = -1;
        private int _upcomingRightPitch = -1;
        private float _upcomingLeftTime, _upcomingRightTime;
        private int _upcomingLeftVelocity, _upcomingRightVelocity;
        private bool _primeOpeningThumb;
        private const float FingerPreparationSeconds = 0.17f;

        // 手腕速度状态（临界阻尼二阶跟随的内部状态；每手独立）
        private Quaternion _leftWristOrientation;
        private Quaternion _rightWristOrientation;
        private struct WristContactFit
        {
            public long NoteSet;
            public Vector3 Position;
            public float Yaw;
        }

        private WristContactFit _leftContactFit;
        private WristContactFit _rightContactFit;
        private readonly Vector3[] _contactFitTargets = new Vector3[5];
        private readonly int[] _contactFitFingers = new int[5];
        private readonly int[] _leftUpcomingChord = { -1, -1, -1, -1, -1 };
        private readonly int[] _rightUpcomingChord = { -1, -1, -1, -1, -1 };

        private bool _leftHasWristOrientation;
        private bool _rightHasWristOrientation;
        private float _leftContactHeight;
        private float _rightContactHeight;
        private float _leftPlayingWristHeight;
        private float _rightPlayingWristHeight;
        private Vector3 _leftWristVelocity;
        private Vector3 _rightWristVelocity;

        // 空闲锚点：无目标时手停在「最后一个真实键位」而不是跳回键盘中央。
        // 真实演奏音与音之间不会抬回中央再重新下移——那会让每个间隙都产生一次
        // 「抬起—落下」的往复，观感机械（原实现每次间隙都回 DefaultHandPos）。
        private Vector3 _leftIdleAnchor;
        private Vector3 _rightIdleAnchor;
        private bool _hasLeftIdleAnchor;
        private bool _hasRightIdleAnchor;

        // 手掌修正（指尖贴键的几何反馈量，仅作用于 XZ）
        private Vector3 _leftPalmCorrection;
        private bool _leftUsesChordPlacement;
        private bool _rightUsesChordPlacement;
        private Vector3 _rightPalmCorrection;
        private Vector3 _leftReachOffset;
        private Vector3 _rightReachOffset;

        // 腕高自适应（接触方案批次 2.4）：每手独立的「腕高偏移」反馈量与连续悬空帧计数。
        // 偏移量有界（±MaxWristHeightAdaptation）且按指数平滑跟随；无按下音时目标归零、
        // 偏移自然衰减回 0，因此无需显式复位。
        private float _leftWristHeightAdapt;
        private float _rightWristHeightAdapt;
        private int _leftHoverStreak;
        private int _rightHoverStreak;

        // 腕高自适应的每帧指尖—键面距离暂存（≤5 指，预分配零 GC；排序取极值/中位数用）
        private readonly float[] _surfaceScratch = new float[5];

        // 双手共用的「手指 ↔ 音高」分配（方案 5.3 第二阶段）：与 FingerPoseController 是同一个实例
        private FingerAssignmentService _assignment;

        // ---- 键面目标点（初始化时预计算，Update 路径零分配复用）----
        private readonly Dictionary<int, KeyTouchPoint> _keyTouches = new();
        private readonly Vector3[] _chordReachCenters = new Vector3[5];
        private readonly float[] _chordReachRadii = new float[5];

        // ---- 指尖目标（每手 5 个，预分配） + 和弦成形过渡进度 ----
        private readonly Vector3[] _leftFingerTargets = new Vector3[5];
        private readonly Vector3[] _rightFingerTargets = new Vector3[5];
        private readonly bool[] _leftFingerHasTarget = new bool[5];
        private readonly bool[] _rightFingerHasTarget = new bool[5];
        private readonly float[] _leftFingerBlend = new float[5];
        private readonly float[] _rightFingerBlend = new float[5];
        private readonly List<int> _pitchScratch = new();

        // 每手一份「当前按下的音高」缓存：RefreshAssignment 每帧算一次，
        // UpdateFingertipTargets 直接复用，避免同一帧对 _pressed 做四次哈希遍历。
        private readonly List<int> _leftPitches = new();
        private readonly List<int> _rightPitches = new();
        private bool _leftHadPrevTarget, _rightHadPrevTarget;

        // ---- 帧级分组/抽查标记 ----
        private bool _leftHovering, _rightHovering;
        private bool _thumbOrientationIssue;
        private bool _palmPenetration;
        private float _leftLargeMotion, _rightLargeMotion;

        // ---- 过渡帧判定（附录 A.6.3「换位峰值」需要与普通帧分开统计） ----
        private Vector3 _prevLeftWrist, _prevRightWrist;
        private bool _hasPrevWrist;
        private bool _transitionFrame;

        // ---- 关节限位增量（来自 PoseIssueSource） ----
        private int _lastJointLimitCount;

        // ---- 接触失败的归因计数（接触方案批次 2.1「先诊断」）----
        // 三类失败必须分开数，否则「键亮着但手指没到」永远只能靠猜：
        // (a) 指法未分配；(b) 该音高没有建按压点；(c) 有目标但超出可达范围。
        private int _fingertipTargetSamples;
        private int _unassignedFingertipSamples;
        private int _missingKeyTouchSamples;
        private int _unreachableTargetSamples;

        /// <summary>(a) 按下的音没有被指法分配到的样本数（分母见 <see cref="FingertipTargetSampleCount"/>）。</summary>
        public int UnassignedFingertipSamples => _unassignedFingertipSamples;

        /// <summary>(b) 音高在按压点表里缺失的样本数。</summary>
        public int MissingKeyTouchSamples => _missingKeyTouchSamples;

        /// <summary>(c) 目标点超出姿态允许范围的样本数（按 <see cref="TryGetPoseLimits"/> 的包围盒判定）。</summary>
        public int UnreachableTargetSamples => _unreachableTargetSamples;

        /// <summary>参与归因统计的指尖目标样本总数（三类失败 + 正常）。</summary>
        public int FingertipTargetSampleCount => _fingertipTargetSamples;

        /// <summary>一个琴键的可复用按压目标点（键面前半段 + 该键真实键面高度）。</summary>
        private readonly struct KeyTouchPoint
        {
            private readonly Transform _key;
            private readonly Vector3 _localPress;
            private readonly Vector3 _localThumbPress;
            public readonly Vector3 Rest;
            public readonly Vector3 Depressed;
            public readonly Vector3 ThumbRest;
            public readonly Vector3 ThumbDepressed;

            /// <summary>按压点世界坐标（落在键面合理按压区域，而不是键位中心的任意高度）。</summary>
            public Vector3 Press => _key.TransformPoint(_localPress);
            public Vector3 ThumbPress => _key.TransformPoint(_localThumbPress);

            /// <summary>是否为黑键（用于分组指标与独立目标高度）。</summary>
            public readonly bool Black;

            /// <summary>本帧按压点的键面世界高度，随琴键下沉变化。</summary>
            public float SurfaceY => Press.y;

            public KeyTouchPoint(Transform key, Vector3 localPress, Vector3 localThumbPress, bool black,
                Quaternion depressedRotation)
            {
                _key = key;
                _localPress = localPress;
                _localThumbPress = localThumbPress;
                Rest = key.TransformPoint(localPress);
                ThumbRest = key.TransformPoint(localThumbPress);
                var down = Matrix4x4.TRS(key.localPosition, depressedRotation, key.localScale);
                Depressed = key.parent.TransformPoint(down.MultiplyPoint3x4(localPress));
                ThumbDepressed = key.parent.TransformPoint(down.MultiplyPoint3x4(localThumbPress));
                Black = black;
            }
        }

        /// <summary>动作质量累加器（Session / 测试读取）。</summary>
        public HandMotionAccumulator Metrics => _metrics;

        /// <summary>
        /// 关节限位来源（由会话层注入 <c>FingerPoseController</c>）：
        /// 让指标统计「关节被钳制次数」，而不让 IK 反向依赖姿态控制器。
        /// </summary>
        public IHandJointIssueSource PoseIssueSource { get; set; }

        /// <summary>累计检测到并已钳制的异常姿态次数（方案阶段五「避免动作失控或穿模」）。</summary>
        public int PoseIssueCount => _poseIssueCount;

        /// <summary>最近若干条异常姿态描述（用于诊断，最多保留 8 条）。</summary>
        public IReadOnlyList<string> PoseIssues => _poseIssues;

        /// <summary>
        /// 累计检测到「左右手交叉」的帧数（方案 6.3 组合规则 / 审查 1.2B：分手练习之外的越界姿态告警）。
        /// 与 <see cref="PoseIssueCount"/> 分开统计：交叉是动作质量告警，不属于「手腕越界穿模」。
        /// </summary>
        public int CrossedHandWarningCount => _crossedHandWarningCount;

        /// <summary>左手当前平滑后的目标位置（供测试与调试；未经平滑时为原始目标）。</summary>
        public Vector3 LeftSmoothedTarget => _leftSmoothedTarget;

        /// <summary>右手当前平滑后的目标位置（供测试与调试；未经平滑时为原始目标）。</summary>
        public Vector3 RightSmoothedTarget => _rightSmoothedTarget;

        /// <summary>
        /// 双手共用的指法分配（由会话层注入，与 <see cref="FingerPoseController.Assignment"/> 同一实例）。
        /// 指尖目标严格按这份分配确定，因此不会出现「弯的手指 ≠ 指向键的手指」。
        /// 未注入时自动创建私有实例（组件可单独使用 / 单测）。
        /// </summary>
        public FingerAssignmentService Assignment
        {
            get => _assignment ?? (_assignment = new FingerAssignmentService());
            set => _assignment = value ?? new FingerAssignmentService();
        }

        /// <summary>当前使用的姿态允许范围（钢琴几何就绪后有效）。</summary>
        public bool TryGetPoseLimits(out HandPoseLimits limits)
        {
            limits = _limits;
            return _hasLimits;
        }

        /// <summary>取某音高在当前键面上的按压点。音高越界或未建键时返回 false。</summary>
        public bool TryGetKeyPressPoint(int pitch, out Vector3 point)
        {
            if (_keyTouches.TryGetValue(pitch, out var touch))
            {
                point = touch.Press;
                return true;
            }
            point = Vector3.zero;
            return false;
        }

        /// <summary>取某手某指的指尖目标（无目标时返回 false）。索引与 <c>IHandRig.Fingers</c> 一致（0 = 拇指）。</summary>
        public bool TryGetFingertipTarget(bool leftHand, int fingerIndex, out Vector3 target)
        {
            if (fingerIndex < 0 || fingerIndex >= 5)
            {
                target = Vector3.zero;
                return false;
            }
            bool has = leftHand ? _leftFingerHasTarget[fingerIndex] : _rightFingerHasTarget[fingerIndex];
            target = leftHand ? _leftFingerTargets[fingerIndex] : _rightFingerTargets[fingerIndex];
            return has;
        }

        /// <summary>黑白键键面高度差（米），供诊断与「黑键单独目标高度」验证。</summary>
        public float BlackWhiteSurfaceDelta => _keyMap != null ? (_keyMap.BlackHeight - _keyMap.WhiteHeight) : 0f;

        public void Initialize(AvatarManager avatar, Piano.PianoKeyMap keyMap, Piano.PianoKeyAnimator keyAnimator = null)
        {
            _avatar = avatar;
            _keyMap = keyMap;
            BuildLimits();
            BuildKeyTouches(keyAnimator != null ? keyAnimator.DepressedRotation : Quaternion.identity);
            _clearanceProbe.Build(keyMap);
        }

        // ==================== 指节防穿模（2026-11-18 全面优化 P4） ====================
        //
        // 指尖贴键之外，中节 / 近节 / 拇指侧面也不能压进琴键：诊断显示两类真实穿模——
        // ① 未分配的休息手指垂得太低，末节陷进白键 10–14 mm；② 演奏手指从低腕斜着伸向白键，
        // 末节压进相邻黑键的前角。两者都随抬腕消失：腕高了，演奏手指更立、更弯（也更符合钢琴手型），
        // 休息手指整体离开键面。
        // 每帧在 LateUpdate（手指解算完成后）测一次两手指节余量，下一帧在所有腕位路径（含和弦摆位、
        // 接触拟合）之后统一加一个「抬腕量」：穿模时以 ≤ 250 mm/s 抬起，余量回到 4 mm 以上后以 20 mm/s 放回，
        // 中间区间保持不动，避免「抬起 → 指尖找键压回 → 再穿模」的来回抖动。

        /// <summary>是否启用指节防穿模（基准测试做 A/B 用；默认开启）。</summary>
        public bool EnableSegmentClearance = false;

        private const float ClearanceReleaseMargin = 0.004f;
        private const float ClearanceLiftDecay = 0.02f;
        private const float ClearanceLiftRate = 0.25f;
        // 抬腕上限 6 mm：只处理演奏手指斜压到相邻黑键前角的情况；再高就会牺牲指尖贴键（悬空）
        private const float MaxClearanceLift = 0.006f;

        private readonly KeyClearanceProbe _clearanceProbe = new KeyClearanceProbe();
        private float _leftSegmentClearance = float.PositiveInfinity;
        private float _rightSegmentClearance = float.PositiveInfinity;
        private float _leftPlayingClearance = float.PositiveInfinity;
        private float _rightPlayingClearance = float.PositiveInfinity;

        /// <summary>指节余量探针（手指控制器用它让休息手指离开键面）。</summary>
        public KeyClearanceProbe ClearanceProbe => _clearanceProbe;
        private float _leftClearanceLift;
        private float _rightClearanceLift;

        /// <summary>该手当前因防穿模而附加的抬腕量（米）。</summary>
        public float GetClearanceLift(bool leftHand) => leftHand ? _leftClearanceLift : _rightClearanceLift;

        /// <summary>最近一帧该手指节与相邻琴键的最小余量（米，负 = 穿模深度；无法测量为 +∞）。</summary>
        public float GetSegmentClearance(bool leftHand) => leftHand ? _leftSegmentClearance : _rightSegmentClearance;

        /// <summary>诊断用：描述该手当前最深的指节穿模点（会分配字符串，只在测试中调用）。</summary>
        public string DescribeSegmentClearance(bool leftHand)
            => _clearanceProbe.DescribeWorst((leftHand ? _avatar?.LeftHand : _avatar?.RightHand) as IFingerJointRig,
                Assignment, leftHand);

        private void MeasureSegmentClearance()
        {
            if (!_clearanceProbe.IsBuilt || _avatar == null)
            {
                _leftSegmentClearance = float.PositiveInfinity;
                _rightSegmentClearance = float.PositiveInfinity;
                _leftPlayingClearance = float.PositiveInfinity;
                _rightPlayingClearance = float.PositiveInfinity;
                return;
            }
            var left = _avatar.LeftHand as IFingerJointRig;
            var right = _avatar.RightHand as IFingerJointRig;
            _leftSegmentClearance = _clearanceProbe.HandClearance(left, Assignment, true);
            _rightSegmentClearance = _clearanceProbe.HandClearance(right, Assignment, false);
            _leftPlayingClearance = _clearanceProbe.HandClearance(left, Assignment, true, playingOnly: true);
            _rightPlayingClearance = _clearanceProbe.HandClearance(right, Assignment, false, playingOnly: true);
        }

        /// <summary>按上一帧测得的指节余量更新该手的抬腕量（米）。</summary>
        private float UpdateClearanceLift(bool leftHand)
        {
            ref float lift = ref (leftHand ? ref _leftClearanceLift : ref _rightClearanceLift);
            if (!EnableSegmentClearance)
            {
                lift = 0f;
                return 0f;
            }

            float clearance = leftHand ? _leftPlayingClearance : _rightPlayingClearance;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (clearance < -KeyClearanceProbe.PenetrationTolerance * 0.5f)
                lift = Mathf.Min(MaxClearanceLift, lift + Mathf.Min(-clearance + 0.001f, ClearanceLiftRate * dt));
            else if (clearance > ClearanceReleaseMargin)
                lift = Mathf.Max(0f, lift - ClearanceLiftDecay * dt);
            return lift;
        }

        /// <summary>由控制器每帧传入统一时间轴时间（用于自适应速度与指标采样）。</summary>
        public void Tick(float currentTime) => _currentTime = currentTime;

        public void OnNoteOn(MIDI.MidiNoteEvent e)
        {
            if (Piano.PianoKeyMap.IsInRange(e.Pitch))
            {
                _pressedHands.Press(e);
                _pressed.Add(e.Pitch);
                if (e.Duration > 0f) _pressedDurations[e.Pitch] = e.Duration;
            }

            if (!float.IsNaN(_lastNoteTime))
            {
                float gap = Mathf.Clamp(_currentTime - _lastNoteTime, 0.005f, 5f);
                _avgNoteGap = Mathf.Lerp(_avgNoteGap, gap, 0.25f);
            }
            _lastNoteTime = _currentTime;
        }

        public void OnNoteOff(MIDI.MidiNoteEvent e)
        {
            if (!Piano.PianoKeyMap.IsInRange(e.Pitch) || !_pressedHands.Release(e)) return;
            _pressed.Remove(e.Pitch);
            _pressedDurations.Remove(e.Pitch);
        }

        /// <summary>当前这个键由哪只手演示。重叠同音共用一个琴键。</summary>
        public bool IsActivePitchLeft(int pitch) => _pressedHands.IsLeft(pitch);

        /// <summary>
        /// 由会话层每帧推入「该手即将到来、此刻尚未按下的音高」（无则 -1）。
        /// 手会在音符到来前先移向对应琴键（真实演奏的预置动作），
        /// 使音符开始时手腕已经在位；预置帧不参与命中率统计（只有真正按住音符的帧才算），
        /// 因此该优化不会美化指标，只减少「换位途中错过音符」。
        /// </summary>
        public void SetUpcomingPitches(int leftPitch, int rightPitch)
            => SetUpcomingPitches(leftPitch, rightPitch, _currentTime + 0.1f, _currentTime + 0.1f, 80, 80);

        /// <summary>接收时间轴选出的各手下一个落键。时间和力度仅用于动作预备。</summary>
        public void SetUpcomingPitches(int leftPitch, int rightPitch, float leftStartTime, float rightStartTime,
            int leftVelocity, int rightVelocity, bool primeRightThumb = false)
        {
            _primeOpeningThumb = primeRightThumb && rightPitch >= 0;
            _upcomingLeftPitch = leftPitch;
            _upcomingRightPitch = rightPitch;
            _upcomingLeftTime = leftStartTime;
            _upcomingRightTime = rightStartTime;
            _upcomingLeftVelocity = leftVelocity;
            _upcomingRightVelocity = rightVelocity;
            System.Array.Fill(_leftUpcomingChord, -1);
            System.Array.Fill(_rightUpcomingChord, -1);
        }

        internal void SetUpcomingChords(IReadOnlyList<MidiNoteEvent> notes)
        {
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                bool left = PianoNoteFilter.IsLeftHand(note);
                int next = left ? _upcomingLeftPitch : _upcomingRightPitch;
                float start = left ? _upcomingLeftTime : _upcomingRightTime;
                if (next < 0 || Mathf.Abs(note.StartTime - start) > 0.0005f) continue;
                int finger = Assignment.PreviewFinger(left, note.Pitch, note.StartTime);
                if (finger >= 0 && finger < 5)
                    (left ? _leftUpcomingChord : _rightUpcomingChord)[finger] = note.Pitch;
            }
        }

        public void ReleaseAll()
        {
            _pressed.Clear();
            _pressedHands.Clear();
            _pressedDurations.Clear();
            _lastNoteTime = float.NaN;   // 复位间隔统计，避免 Seek 后沿用旧节奏
            _avgNoteGap = 0.4f;
            _upcomingLeftPitch = -1;     // 复位预置目标，避免停止/切曲后手继续移向旧音符
            _upcomingRightPitch = -1;
            _primeOpeningThumb = false;
            System.Array.Fill(_leftUpcomingChord, -1);
            System.Array.Fill(_rightUpcomingChord, -1);
            // 停止和定位时清除空闲键位。换人可沿用正在播放的键位。
            _hasLeftIdleAnchor = false;
            _hasRightIdleAnchor = false;
            ResetRigMotion();
        }

        /// <summary>复位依赖骨架的平滑状态，保留音符、指法和预备目标。</summary>
        public void ResetRigMotion()
        {
            _leftContactHeight = 0f;
            _rightContactHeight = 0f;
            _leftPlayingWristHeight = 0f;
            _rightPlayingWristHeight = 0f;
            _leftWristVelocity = Vector3.zero;
            _rightWristVelocity = Vector3.zero;
            _leftContactFit = default;
            _rightContactFit = default;
            _leftHasWristOrientation = false;
            _rightHasWristOrientation = false;
            _leftPalmCorrection = Vector3.zero;
            _rightPalmCorrection = Vector3.zero;
            _leftReachOffset = Vector3.zero;
            _rightReachOffset = Vector3.zero;
            _leftWristHeightAdapt = 0f;
            _rightWristHeightAdapt = 0f;
            _hasLeftSmoothed = false;
            _hasRightSmoothed = false;
            _leftHadPrevTarget = false;
            _rightHadPrevTarget = false;
            _leftLargeMotion = 0f;
            _rightLargeMotion = 0f;
            _hasPrevWrist = false;
            _transitionFrame = false;
            ResetFingertipTargets(leftHand: true);
            ResetFingertipTargets(leftHand: false);
        }

        /// <summary>重置动作质量指标与姿态告警记录（换曲或重新开始测量时调用）。</summary>
        public void ResetMetrics()
        {
            _metrics.Reset();
            _poseIssueCount = 0;
            _crossedHandWarningCount = 0;
            _poseIssues.Clear();
            _hasPrevWrist = false;
            _transitionFrame = false;
            _lastJointLimitCount = PoseIssueSource != null ? PoseIssueSource.JointLimitClampCount : 0;
            _fingertipTargetSamples = 0;
            _unassignedFingertipSamples = 0;
            _missingKeyTouchSamples = 0;
            _unreachableTargetSamples = 0;
            _leftWristHeightAdapt = 0f;
            _rightWristHeightAdapt = 0f;
            _leftHoverStreak = 0;
            _rightHoverStreak = 0;
        }

        /// <summary>
        /// 目标点是否落在姿态允许范围内（含少量余量）。
        /// <para>
        /// 用于把「IK 解不到」从「指法没分配」里分离出来（批次 2.1 的 (c) 类）；
        /// 未建立范围时一律视为可达，避免误报。
        /// </para>
        /// </summary>
        private bool IsWithinPoseLimits(Vector3 point)
        {
            if (!_hasLimits) return true;
            const float margin = 0.02f;
            return point.x >= _limits.MinX - margin && point.x <= _limits.MaxX + margin
                && point.y >= _limits.MinY - margin && point.y <= _limits.MaxY + margin
                && point.z >= _limits.MinZ - margin && point.z <= _limits.MaxZ + margin;
        }

        /// <summary>
        /// 手臂可达性判定（接触方案 2.3）：<b>指尖</b>目标到同侧肩的直线距离超过
        /// <c>(上臂 + 前臂 + 掌长 + 最长手指) × BodyScale × ArmReachRatio</c> 即为不可达。
        /// <para>
        /// 为什么必须加手长：判定的对象是「指尖按压点」，而手臂只负责把<b>手腕</b>送到位置，
        /// 掌长 + 手指（约 0.156 m）本身也是可及距离的一部分。只算上臂+前臂会把三首曲子的
        /// 全部按压点判成不可达（实测 100% 误报）。
        /// </para>
        /// <para>
        /// 为什么不能只用 <see cref="IsWithinPoseLimits"/>：那个盒子是「键盘包围盒 + 边距」，
        /// 按压点永远落在盒内（实测 (c) 类触发数为 0），对「手臂够不够长」没有判别力。
        /// 两个判定分工：盒子管「目标是否在合法区域」（穿模/越界），本方法管「人体比例上够不够得到」。
        /// </para>
        /// </summary>
        private bool IsArmReachable(Vector3 point, bool leftHand)
        {
            if (_avatar == null) return true;
            var shoulder = leftHand ? _avatar.LeftShoulder : _avatar.RightShoulder;
            if (shoulder == null) return true;

            float scale = 1f;
            float handReach = AvatarBodyMetrics.PalmLength + AvatarBodyMetrics.FingerLengths[2];
            var def = _avatar.CurrentDefinition;
            if (def != null)
            {
                scale = Mathf.Clamp(def.BodyScale, 0.5f, 1.6f);
                if (def.Hand != null)
                    handReach = def.Hand.PalmLength + def.Hand.FingerLength(2);
            }

            float limit = (AvatarBodyMetrics.UpperArmLength + AvatarBodyMetrics.LowerArmLength + handReach)
                          * scale * ArmReachRatio;
            return Vector3.Distance(shoulder.position, point) <= limit;
        }

        /// <summary>生成动作质量报告（方案阶段五验收量化）。</summary>
        public HandMotionReport BuildMotionReport() => _metrics.Build();

        private void Update()
        {
            if (_avatar == null || _keyMap == null) return;

            // 每帧先保证「手指 ↔ 音高」分配是最新的（与 FingerPoseController 共用同一份，
            // 同一帧内谁先跑谁算，因此两处永远看到同一个答案）。
            RefreshAssignment(leftHand: true);
            RefreshAssignment(leftHand: false);

            UpdateHand(leftHand: true);
            UpdateHand(leftHand: false);

            // 手掌修正用「本帧的实际指尖位置」反推，供下一帧的手腕目标使用（一帧延迟的负反馈，稳定且不发散）
            UpdatePalmCorrection(leftHand: true);
            UpdatePalmCorrection(leftHand: false);

            // 腕高自适应同哲学：用本帧指尖—键面法向距离更新每手腕高偏移，下一帧生效
            if (WristHeightAdaptation)
            {
                UpdateWristHeightAdaptation(leftHand: true);
                UpdateWristHeightAdaptation(leftHand: false);
            }

        }

        private void LateUpdate()
        {
            // 采集手腕和手指均完成更新后的实际姿态。
            if (_avatar != null && _keyMap != null) MeasureSegmentClearance();
            if (EnableMetrics && _avatar != null && _keyMap != null) SampleMetrics();
        }

        private void RefreshAssignment(bool leftHand)
        {
            // 一手一份缓存：收集 + 排序只做一次，UpdateFingertipTargets 直接复用结果。
            var pitches = leftHand ? _leftPitches : _rightPitches;
            pitches.Clear();
            foreach (var pitch in _pressed)
            {
                if (IsActivePitchLeft(pitch) == leftHand) pitches.Add(pitch);
            }
            pitches.Sort();
            Assignment.Refresh(leftHand, pitches, _currentTime, Time.frameCount);
        }

        /// <summary>
        /// 手掌几何反馈（让指尖真正落到键上）：把「分配到的各指尖到各自琴键按压点的平均误差」
        /// 作为手腕目标的修正量（只修正 XZ，高度交给手指弯曲，避免手腕被压到键面以下触发姿态钳制）。
        /// 修正量有界且按指数阻尼跟随，因此不会抖动，也不会突破单帧位移硬约束。
        /// </summary>
        /// <summary>
        /// H3 掌位解析解：把「够不到」的目标距离残差写成线性最小二乘，直接解出手掌该往哪挪。
        /// <para>
        /// 对每根按住的指头：<c>d = |target − mcp|</c>，<c>reach = 指长</c>，残差 <c>e = d − reach·margin ≥ 0</c>
        /// 表示「差这么远才够得到」。把手掌平移 <c>δ</c> 时，<c>e</c> 的一阶变化约为 <c>−u·δ</c>（<c>u</c> = 指向目标单位向量），
        /// 于是问题化为 <c>min ‖Aδ − e‖²</c>（A 的行就是各指头的 u），用正规方程 + Tikhonov 阻尼闭式求解。
        /// </para>
        /// <para>
        /// 与既有 <see cref="PalmCorrectionGain"/> 增益闭环的区别：后者是「朝残差方向按比例挪」，
        /// 会和指骨 CCD 形成正反馈（实测 R2 20.9→24.6 mm，越修越偏）；这里是**先算掌位、再解指骨**的顺序修正，
        /// 只处理「几何上根本够不到」的部分，不动「够得到但姿态不好」的部分。
        /// </para>
        /// </summary>
        private Vector3 SolvePalmReachOffset(bool leftHand)
        {
            if (!PalmReachSolve) return Vector3.zero;

            var hand = leftHand ? _avatar.LeftHand : _avatar.RightHand;
            if (hand == null) return Vector3.zero;
            if (!(hand is IFingerJointRig rig)) return Vector3.zero;

            var targets = leftHand ? _leftFingerTargets : _rightFingerTargets;
            var has = leftHand ? _leftFingerHasTarget : _rightFingerHasTarget;

            // 正规方程 AᵀA δ = Aᵀe 的对称 3×3 累加
            double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0;
            double b0 = 0, b1 = 0, b2 = 0;
            int used = 0;

            for (int f = 0; f < 5; f++)
            {
                if (!has[f]) continue;
                if (!rig.TryGetJoints(f, out var proximal, out _, out _)) continue;
                float reach = rig.GetFingerLength(f);
                if (reach <= 1e-4f) continue;

                Vector3 toTarget = targets[f] - proximal.position;
                float distance = toTarget.magnitude;
                if (distance < 1e-5f) continue;

                float deficit = distance - reach * PalmReachMargin;
                if (deficit <= 0f) continue;          // 够得到：不参与（把姿态问题留给手指解算）

                // 只在「挪一点手就够得到」的量级上求解。
                // 实测：拇指目标常常差 70 mm（L0 rootDist 138.8 vs 指长 68.6），远超手掌可移动范围；
                // 让它参与最小二乘会把整只手拽向拇指，反而使其余手指变差（R2 21.0→34.8 mm）。
                // 这种量级的偏差是指法分配/键位数据问题，不该由掌位补偿。
                if (deficit > MaxPalmReachOffset) continue;

                Vector3 u = toTarget / distance;
                a00 += u.x * u.x; a01 += u.x * u.y; a02 += u.x * u.z;
                a11 += u.y * u.y; a12 += u.y * u.z; a22 += u.z * u.z;
                b0 += u.x * deficit; b1 += u.y * deficit; b2 += u.z * deficit;
                used++;
            }

            if (used == 0) return Vector3.zero;

            double lambda = PalmReachDamping * used;
            a00 += lambda; a11 += lambda; a22 += lambda;

            // 3×3 对称正定：按余子式求逆（det 有下界阻尼保证）
            double c00 = a11 * a22 - a12 * a12;
            double c01 = a02 * a12 - a01 * a22;
            double c02 = a01 * a12 - a02 * a11;
            double det = a00 * c00 + a01 * c01 + a02 * c02;
            if (Mathf.Abs((float)det) < 1e-12f) return Vector3.zero;

            double inv = 1.0 / det;
            double ix = (c00 * b0 + c01 * b1 + c02 * b2) * inv;
            double iy = (c01 * b0 + (a00 * a22 - a02 * a02) * b1 + (a01 * a02 - a00 * a12) * b2) * inv;
            double iz = (c02 * b0 + (a01 * a02 - a00 * a12) * b1 + (a00 * a11 - a01 * a01) * b2) * inv;

            var offset = new Vector3((float)ix, (float)iy, (float)iz);
            return Vector3.ClampMagnitude(offset, MaxPalmReachOffset);
        }
        private void UpdatePalmCorrection(bool leftHand)
        {
            IHandRig hand = leftHand ? _avatar.LeftHand : _avatar.RightHand;
            var targets = leftHand ? _leftFingerTargets : _rightFingerTargets;
            var has = leftHand ? _leftFingerHasTarget : _rightFingerHasTarget;

            Vector3 sum = Vector3.zero;
            int count = 0;

            bool usesChordPlacement = leftHand ? _leftUsesChordPlacement : _rightUsesChordPlacement;
            if (hand != null && hand.Fingers != null && (!usesChordPlacement || !(hand is IHandOrientationRig)))
            {
                int fingerCount = Mathf.Min(hand.Fingers.Count, 5);
                for (int f = 0; f < fingerCount; f++)
                {
                    if (!has[f]) continue;
                    sum += targets[f] - FingertipWorld(hand, f);
                    count++;
                }
            }

            var current = leftHand ? _leftPalmCorrection : _rightPalmCorrection;
            // 本帧残差来自已偏移的位置，到位后保留已生效的偏移。
            Vector3 want = count > 0 ? current + (sum / count) * PalmCorrectionGain : Vector3.zero;
            want.y = 0f;                                        // 高度不由手掌平移承担
            want = Vector3.ClampMagnitude(want, usesChordPlacement ? 0.018f : MaxPalmCorrection);

            float k = 1f - Mathf.Exp(-PalmCorrectionSpeed * Time.deltaTime);
            Vector3 next = Vector3.Lerp(current, want, k);

            if (leftHand) _leftPalmCorrection = next;
            else _rightPalmCorrection = next;
        }

        /// <summary>
        /// 腕高自适应（接触方案批次 2.4）：按该手「被分配指尖到键面的法向距离」的**分布**
        /// 反向微调该手腕高——用<b>最小值</b>判压键（任一指尖压入即升腕，压键是硬伤）、
        /// 用<b>中位数</b>判整体悬空（连续帧确认后降腕）。
        /// <para>
        /// 为什么不用均值：一手 5 指里 3 指悬空 20 mm + 2 指压入 8 mm 时均值只有 +8.8 mm，
        /// 落在死区内 → 永远不修。A/B 实测（bench 三曲，2026-10-31）证实均值口径收益为 0
        /// （键面 P95 变化 ≤0.5 mm、压键帧仅降 3 帧），故改用极值 + 中位数。
        /// </para>
        /// 偏移限幅 ±<see cref="MaxWristHeightAdaptation"/>、指数平滑
        /// <see cref="WristAdaptationSmoothSeconds"/>；无按下音时目标归零，偏移自然衰减。
        /// 统计口径（<see cref="AssignedFingertipError"/>）用真实键面，不受本偏移美化。
        /// </summary>
        private void UpdateWristHeightAdaptation(bool leftHand)
        {
            var hand = leftHand ? _avatar.LeftHand : _avatar.RightHand;
            var pitches = leftHand ? _leftPitches : _rightPitches;
            int streak = leftHand ? _leftHoverStreak : _rightHoverStreak;

            int count = 0;
            if (hand != null && hand.Fingers != null && pitches.Count > 0)
            {
                for (int i = 0; i < pitches.Count && count < _surfaceScratch.Length; i++)
                {
                    if (!_keyTouches.TryGetValue(pitches[i], out var touch)) continue;
                    int finger = Assignment.GetFinger(leftHand, pitches[i]);
                    if (finger < 0 || finger >= hand.Fingers.Count) continue;

                    // + = 指尖高于键面（悬空），− = 压入键面（压键）。
                    // 指尖位置与统计口径同源（FingertipWorld：优先 IFingertipRig 的真实指尖）
                    _surfaceScratch[count++] = FingertipWorld(hand, finger).y - touch.SurfaceY;
                }
            }

            float want;
            if (count == 0)
            {
                want = 0f;                                   // 该手无按下音：偏移自然衰减回 0
                streak = 0;
            }
            else
            {
                // 插入排序（≤5 个元素，零分配）：sorted[0] = 最压入，sorted[count/2] = 中位数
                for (int i = 1; i < count; i++)
                {
                    float v = _surfaceScratch[i];
                    int j = i - 1;
                    while (j >= 0 && _surfaceScratch[j] > v) { _surfaceScratch[j + 1] = _surfaceScratch[j]; j--; }
                    _surfaceScratch[j + 1] = v;
                }

                float lowest = _surfaceScratch[0];             // 最压入的那根指尖
                float median = _surfaceScratch[count / 2];     // 该手整体水平

                if (lowest < -WristAdaptationPressThreshold)
                {
                    streak = 0;
                    // 压键立即升腕（不等连续帧确认：压键是硬伤，多等一帧就多穿模一帧）
                    want = -(lowest + WristAdaptationPressThreshold);
                }
                else if (median > WristAdaptationHoverThreshold)
                {
                    streak++;                                // 「持续悬空」需连续帧确认，防单帧毛刺
                    // 连续 2 帧以上整体悬空才降腕：收敛到阈值带内（不追求 0，避免过冲再反向修）
                    want = streak >= 2 ? -(median - WristAdaptationHoverThreshold) : 0f;
                }
                else
                {
                    streak = 0;
                    want = 0f;                               // 死区：阈值带内不修
                }
            }

            if (leftHand) _leftHoverStreak = streak;
            else _rightHoverStreak = streak;

            want = Mathf.Clamp(want, -MaxWristHeightAdaptation, MaxWristHeightAdaptation);
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, WristAdaptationSmoothSeconds));
            if (leftHand) _leftWristHeightAdapt = Mathf.Lerp(_leftWristHeightAdapt, want, k);
            else _rightWristHeightAdapt = Mathf.Lerp(_rightWristHeightAdapt, want, k);
        }

        internal bool AlignOpeningRightThumb()
        {
            if (!_primeOpeningThumb || _upcomingRightTime > 0.0005f) return false;
            RefreshAssignment(leftHand: false);
            return UpdateHand(leftHand: false, alignOpeningThumb: true);
        }

        private bool UpdateHand(bool leftHand, bool alignOpeningThumb = false)
        {
            int? targetPitch = FindHandTarget(leftHand);
            bool hasTarget = targetPitch.HasValue && _keyMap.TryGetKey(targetPitch.Value, out _);

            // 预置（方案 5.3 第二阶段「先形成手型再下压」/ 方案 4.2.3「对音符密集区使用轻量预测」）：
            // 该手此刻没有按住音符、但马上就有音符时，先把手腕移向那个键，
            // 使音符开始时手已经在位——这是真实演奏的预置动作，也让「目标琴键命中率」不再被换位途中拖累。
            // 预置帧不算「有目标」（不参与命中率统计），因此指标不会被美化。
            bool preselected = false;
            Vector3 rawTarget;
            if (hasTarget)
            {
                _keyMap.TryGetKey(targetPitch.Value, out var key);
                rawTarget = KeyToHandPos(key);
            }
            else
            {
                int upcoming = leftHand ? _upcomingLeftPitch : _upcomingRightPitch;
                if (upcoming >= 0 && _keyMap.TryGetKey(upcoming, out var upcomingKey) && upcomingKey != null)
                {
                    rawTarget = KeyToHandPos(upcomingKey);
                    preselected = true;
                }
                else
                {
                    // 空闲时停在「上一个真实键位」，不跳回键盘中央（见 _leftIdleAnchor 说明）。
                    // 只有 ReleaseAll / 停止后才回 DefaultHandPos —— 那才是真正的「收手」。
                    rawTarget = leftHand
                        ? (_hasLeftIdleAnchor ? _leftIdleAnchor : DefaultHandPos(1f))
                        : (_hasRightIdleAnchor ? _rightIdleAnchor : DefaultHandPos(-1f));

                }
            }

            // 记录空闲锚点：取「真实键位目标」（尚未叠加角色偏移与手掌修正，
            // 那些量每帧微变，作为锚点会让空闲位置持续漂移）
            if (hasTarget || preselected)
            {
                if (leftHand) { _leftIdleAnchor = rawTarget; _hasLeftIdleAnchor = true; }
                else { _rightIdleAnchor = rawTarget; _hasRightIdleAnchor = true; }
            }

            // 每角色 IK 偏移（方案 5.2 中期「每角色 IK 偏移」；默认 0，不影响 HandHeight/HandDepth 的既有语义）
            var def = _avatar.CurrentDefinition;
            if (def != null)
            {
                rawTarget = new Vector3(
                    rawTarget.x,
                    rawTarget.y + def.HandHeightOffset + def.Hand.PalmKeyPlaneOffset,
                    rawTarget.z + def.HandDepthOffset);
            }

            if (def != null && def.Hand.PlayingWristHeightOffset != 0f)
            {
                // 静置时食指轻搭键沿，演奏时腕部向实际落键高度过渡。
                ref float playingHeight = ref (leftHand ? ref _leftPlayingWristHeight : ref _rightPlayingWristHeight);
                float targetHeight = hasTarget || preselected ? def.Hand.PlayingWristHeightOffset : 0f;
                playingHeight = Mathf.Lerp(playingHeight, targetHeight, 1f - Mathf.Exp(-24f * Time.deltaTime));
                rawTarget.y += playingHeight;
            }

            // 腕高自适应偏移（接触方案批次 2.4）：按每手反馈量微调腕高，不改公共 HandHeight 常量语义。
            // 与 def.HandHeightOffset 的叠加方式一致（参照 AvatarDefinition 叠加点先例）。
            if (WristHeightAdaptation)
                rawTarget.y += leftHand ? _leftWristHeightAdapt : _rightWristHeightAdapt;

            // 黑键抬手（H4-lite，2026-11-05）：黑键键面比白键高 22 mm，实测按住黑键时指尖比键面低 20 mm 左右。
            // 抬「指尖目标」无效（试过 12 mm，残差 20.9→21.1），因为目标抬高只会让手指更伸展；
            // 真实演奏是把手抬起来越过白键去够黑键，所以这里直接抬腕目标。
            if (hasTarget && Piano.PianoKeyMap.IsBlackKey(targetPitch.Value))
                rawTarget.y += BlackKeyHandLift;
            // 指尖贴键的手掌修正（方案 5.3 第二阶段）：把手腕朝「消灭指尖平均误差」的方向平移。
            // 只修正 XZ（高度由手指弯曲负责），并夹在键盘允许范围内，避免把手腕推出琴键区触发姿态钳制。
            var hand = leftHand ? _avatar.LeftHand : _avatar.RightHand;
            if (!hasTarget && !preselected && hand is IHandOrientationRig && hand is IFingertipRig restRig)
            {
                // 空闲食指轻搭键面。取相对腕高，避免把上一帧腕部位移再次叠加进去。
                Transform index = restRig.GetFingertip(1);
                if (index != null)
                {
                    float neutralHeight = _keyMap.KeySurfaceY + 0.003f + hand.Wrist.position.y - index.position.y;
                    rawTarget.y = Mathf.Clamp(neutralHeight, rawTarget.y - 0.037f, rawTarget.y + 0.037f);
                }
            }
            bool constrained = UseAnatomicalContact && hand is IHandOrientationRig;
            int upcomingPitch = leftHand ? _upcomingLeftPitch : _upcomingRightPitch;
            float upcomingTime = leftHand ? _upcomingLeftTime : _upcomingRightTime;
            bool thumbPad = hand is IThumbContactRig { HasThumbPad: true };
            // 和弦中拇指与其余手指共享腕位。用同一次试算找有足够抬腕空间的姿势。
            bool directThumbFit = thumbPad && (hasTarget
                ? Assignment.GetPitch(leftHand, 0) >= 0
                : preselected && !constrained && Assignment.PreviewFinger(leftHand, upcomingPitch, upcomingTime) == 0);
            ref WristContactFit fit = ref (leftHand ? ref _leftContactFit : ref _rightContactFit);
            bool usesChordPlacement = hasTarget && TryPlaceChordWrist(hand, leftHand, ref rawTarget, constrained);
            bool fitted = (constrained || directThumbFit) && (hasTarget || preselected)
                && def != null && def.Hand.WristOrientationFromRest && PoseIssueSource is FingerPoseController pose
                && TryFitContactWrist(hand, leftHand, hasTarget, pose, ref fit, ref rawTarget,
                    directThumbFit && !hasTarget && !constrained ? upcomingPitch : -1);
            if (!fitted) fit = default;
            usesChordPlacement |= fitted;
            if (constrained)
            {
                if (!fitted) rawTarget.y += AdjustContactHeight(hand, leftHand, hasTarget);
                SeparateIdleWrist(leftHand, hasTarget || preselected, ref rawTarget);
            }
            // P4 指节防穿模：和弦摆位、接触拟合、腕高闭环之后统一抬腕，任何路径都不让指节压进琴键
            rawTarget.y += UpdateClearanceLift(leftHand);
            if (leftHand) _leftUsesChordPlacement = usesChordPlacement;
            else _rightUsesChordPlacement = usesChordPlacement;
            if (!usesChordPlacement || !(hand is IHandOrientationRig))
                rawTarget += leftHand ? _leftPalmCorrection : _rightPalmCorrection;

            // H3 掌位解析解：只补「几何上够不到」的那部分位移，阻尼跟随避免抖动。
            {
                Vector3 wantReach = SolvePalmReachOffset(leftHand);
                float kReach = 1f - Mathf.Exp(-PalmReachSpeed * Time.deltaTime);
                if (leftHand) _leftReachOffset = Vector3.Lerp(_leftReachOffset, wantReach, kReach);
                else _rightReachOffset = Vector3.Lerp(_rightReachOffset, wantReach, kReach);
                rawTarget += leftHand ? _leftReachOffset : _rightReachOffset;
            }
            if (_hasLimits)
            {
                rawTarget = new Vector3(
                    Mathf.Clamp(rawTarget.x, _limits.MinX, _limits.MaxX),
                    rawTarget.y,
                    Mathf.Clamp(rawTarget.z, _limits.MinZ, _limits.MaxZ));
            }

            var shoulder = leftHand ? _avatar.LeftShoulder : _avatar.RightShoulder;
            // 两段手臂：上臂 + 前臂 + 肘球（改造前是单段胶囊，见 AvatarManager.BuildArm）
            var upperArm = leftHand ? _avatar.LeftArm : _avatar.RightArm;
            var foreArm = leftHand ? _avatar.LeftForeArm : _avatar.RightForeArm;
            var elbow = leftHand ? _avatar.LeftElbow : _avatar.RightElbow;

            // 跨手位移分组判定（对比上一帧原始目标）
            if (leftHand ? _leftHadPrevTarget : _rightHadPrevTarget)
            {
                float jump = leftHand
                    ? Vector3.Distance(_leftTargetPos, rawTarget)
                    : Vector3.Distance(_rightTargetPos, rawTarget);
                if (leftHand) _leftLargeMotion = jump;
                else _rightLargeMotion = jump;
            }

            if (leftHand)
            {
                _leftTargetPos = rawTarget;
                _leftHasTarget = hasTarget;
                _leftHadPrevTarget = true;
            }
            else
            {
                _rightTargetPos = rawTarget;
                _rightHasTarget = hasTarget;
                _rightHadPrevTarget = true;
            }

            if (hand == null || shoulder == null || upperArm == null) return false;
            if (alignOpeningThumb && (!fitted || !directThumbFit || _rightPitches.Count > 1)) return false;

            var current = hand.Wrist.position;
            float speedScale = CurrentSpeedScale();
            if (preselected)
            {
                // 短间隔大跳优先让前臂跟上。邻键仍以较缓的速度预备。
                float gap = Mathf.Abs(rawTarget.x - current.x);
                float travel = Mathf.InverseLerp(0.10f, 0.24f, gap);
                speedScale *= Mathf.Lerp(HandLeadSpeedScale, 1.15f, travel);
            }

            if (!leftHand && fitted && thumbPad &&
                (hasTarget ? Assignment.GetPitch(false, 0) >= 0 : upcomingTime - _currentTime <= 0.09f))
            {
                // 手掌还离目标较远时才加快换位，贴键后回到普通阻尼，保留单帧安全上限。
                float travel = Mathf.InverseLerp(0.036f, 0.15f, Mathf.Abs(rawTarget.x - current.x));
                speedScale = Mathf.Lerp(speedScale, Mathf.Max(speedScale, 2.65f), travel);
            }

            Vector3 next;
            if (alignOpeningThumb)
            {
                next = rawTarget;
                _rightSmoothedTarget = next;
                _hasRightSmoothed = true;
                _rightWristVelocity = Vector3.zero;
                _rightHasWristOrientation = false;
            }
            else if (UseDampedSpring)
            {
                // 目标限速保留（它抑制换手位时的单帧抖跳），但弹簧不受目标漂移影响——
                // 这正是与 minimum jerk 的关键差别：没有「段」，就不会出现「每帧重建段」的死锁。
                var target = ApplyGlideLift(current, LimitTargetRate(leftHand, rawTarget));
                next = leftHand
                    ? AdvanceSpring(ref _leftWristVelocity, current, target, speedScale)
                    : AdvanceSpring(ref _rightWristVelocity, current, target, speedScale);

                // 硬约束：单帧位移上限（防瞬移与动作失控）。弹簧峰值已在 ω 里整定，
                // 这里只兜底目标突跳（切曲 / Seek / 换角色）。
                var delta = next - current;
                float step = delta.magnitude;
                if (step > MaxStepPerFrame && step > 1e-6f)
                    next = current + delta * (MaxStepPerFrame / step);
            }
            else
            {
                var target = ApplyGlideLift(current, LimitTargetRate(leftHand, rawTarget));
                if (UseDistanceAdaptiveSpeed)
                {
                    float dist = Vector3.Distance(current, target);
                    float frameScale = Mathf.Clamp01(Time.deltaTime * 60f);
                    float smoothStep = dist * Mathf.Clamp01(Time.deltaTime * MoveSpeed * speedScale);
                    float fullStep = MaxStepPerFrame * frameScale;
                    float blend = Mathf.InverseLerp(SmoothFollowDistance, FullSpeedDistance, dist);
                    float stepLen = Mathf.Min(Mathf.Lerp(smoothStep, fullStep, blend), dist);
                    stepLen = Mathf.Min(stepLen, MaxStepPerFrame * frameScale);
                    next = dist > 1e-6f ? current + (target - current) * (stepLen / dist) : target;
                }
                else
                {
                    // 旧路径：指数逼近 + 单帧硬约束（改动前逐位一致，作为回退）
                    next = Vector3.Lerp(current, target, Time.deltaTime * MoveSpeed * speedScale);
                    var delta = next - current;
                    float step = delta.magnitude;
                    if (step > MaxStepPerFrame && step > 1e-6f)
                        next = current + delta * (MaxStepPerFrame / step);
                }
            }

            if (EnablePoseClamp && _hasLimits)
            {
                string issue = HandPoseValidator.Validate(next, _limits);
                if (issue != null) RecordPoseIssue(issue);
                next = HandPoseValidator.Clamp(next, _limits);
            }

            // 手臂解算交给策略：程序化角色平移+拉伸胶囊；蒙皮角色只旋转骨骼（不写 position/scale）
            ArmSolver.Solve(shoulder, upperArm, foreArm, elbow, hand.Wrist, next, leftHand);

            if (EnableWristOrientation) ApplyWristOrientation(hand, leftHand, def, fitted ? fit.Yaw : (float?)null);

            UpdateFingertipTargets(hand, leftHand, def);
            return true;
        }

        /// <summary>
        /// 手腕姿态（附录 A.4.2「除位置外，增加受限的俯仰/滚转」）：
        /// ① 俯仰让掌心略朝键面，同时把腕部抬高使掌指关节基本留在原位（否则掌体会压进琴键）；
        /// ② 滚转在键盘两端产生轻微侧倾，幅度受限；
        /// ③ 只改姿态，<b>不影响</b> <see cref="MaxStepPerFrame"/> 的位置硬约束。
        /// </summary>
        private void ApplyWristOrientation(IHandRig hand, bool leftHand, AvatarDefinition def, float? contactYaw)
        {
            if (hand == null || hand.Wrist == null) return;

            float pitch = def != null ? def.Hand.WristPitch : DefaultWristPitch;
            float roll = 0f;

            if (_hasLimits)
            {
                float center = (_limits.MinX + _limits.MaxX) * 0.5f;
                float half = Mathf.Max(0.001f, (_limits.MaxX - _limits.MinX) * 0.5f);
                float offset = Mathf.Clamp((hand.Wrist.position.x - center) / half, -1f, 1f);

                // 外侧背伸：手离开中线越远，腕背伸越大（|offset| 而不是 offset——两端对称）。
                // 与 roll 的分工：roll 负责尺偏/桡偏（方向相反），pitch 负责补偿够不到的落键角度。
                pitch += Mathf.Abs(offset) * Mathf.Max(0f, OuterWristPitchBias);

                roll = offset * (def != null ? def.Hand.WristRollPerHandUnit : MaxWristRollDegrees) * (leftHand ? -1f : 1f);
            }

            // 说明：不做位置补偿 —— 腕部位置是每帧递推的状态量，叠加抬高会形成累积漂移。
            // 俯仰带来的指根位移由 FingerPoseController 的指尖 IK 每帧重新求解（以实际指根为基准），
            // 掌体与键面之间仍有约 18 mm 余量，不会穿模。
            var delta = Quaternion.Euler(pitch, 0f, roll);
            float speed = contactYaw.HasValue ? Mathf.Max(WristOrientationSpeed, 24f) : WristOrientationSpeed;
            float k = 1f - Mathf.Exp(-speed * Time.deltaTime);

            // 【导入骨架】手臂用「旋转骨骼」解算，只约束肩→腕的连线，绕前臂轴的滚转是自由的，
            // 掌心朝向完全由绑定姿态决定（实测坐姿下五指指尖高度横跨 72 mm、拇指侧整体抬高）。
            // 因此这里不做「欧拉角写 localRotation」，而是按手骨自身的基向上做**世界空间瞄准**：
            // 令手背法线朝上（按 WristPitch 前倾）、手指朝前下方（按 roll 侧倾）。
            if (def != null && def.Hand.WristOrientationFromRest && hand is IHandOrientationRig basis)
            {
                float yaw = contactYaw ?? NaturalWristYaw(hand.Wrist);
                Quaternion aim = ContactWristRotation(basis, yaw, pitch, roll);
                // 前臂旋转会带动腕骨。保留上次解算后的世界朝向作为插值起点。
                if (leftHand)
                {
                    _leftWristOrientation = _leftHasWristOrientation
                        ? Quaternion.Slerp(_leftWristOrientation, aim, k) : aim;
                    _leftHasWristOrientation = true;
                    hand.Wrist.rotation = _leftWristOrientation;
                }
                else
                {
                    _rightWristOrientation = _rightHasWristOrientation
                        ? Quaternion.Slerp(_rightWristOrientation, aim, k) : aim;
                    _rightHasWristOrientation = true;
                    hand.Wrist.rotation = _rightWristOrientation;
                }
                return;
            }

            // 【程序化角色】腕骨本地帧是手写标定的（初始 localRotation 接近单位四元数），
            // 直接把 localRotation 设为 Euler(pitch, 0, roll) 即为预期姿态。
            var target = delta;
            if (def != null && def.Hand.WristOrientationFromRest)
            {
                if (leftHand)
                {
                    if (!_leftWristRestCaptured)
                    {
                        _leftWristRestLocal = hand.Wrist.localRotation;
                        _leftWristRestCaptured = true;
                    }
                    target = _leftWristRestLocal * delta;
                }
                else
                {
                    if (!_rightWristRestCaptured)
                    {
                        _rightWristRestLocal = hand.Wrist.localRotation;
                        _rightWristRestCaptured = true;
                    }
                    target = _rightWristRestLocal * delta;
                }
            }

            hand.Wrist.localRotation = Quaternion.Slerp(hand.Wrist.localRotation, target, k);
        }

        // ==================== 指尖目标（方案 5.3 第二阶段 / 审查 1.2B 第二步） ====================

        /// <summary>
        /// 指尖目标求解：先把每个按下音高的键面按压点分配给「离它最近的指尖」（可复用目标点），
        /// 再按和弦成形进度把目标从手型上方过渡到键面（避免所有指尖在同一帧突然到位）。
        /// 手腕/掌心的移动规则完全不受影响（<see cref="UpdateHand"/> 已处理）。
        /// </summary>
        private void UpdateFingertipTargets(IHandRig hand, bool leftHand, AvatarDefinition def)
        {
            ClearFingertipTargets(leftHand);
            if (!EnableFingertipMetrics) return;
            if (hand == null || hand.Fingers == null) return;

            // 复用 RefreshAssignment 本帧已经算好的「该手按下的音高」（已排序），不再重复遍历 _pressed。
            var pitches = leftHand ? _leftPitches : _rightPitches;

            int fingerCount = Mathf.Min(hand.Fingers.Count, 5);
            if (fingerCount == 0) return;

            var targets = leftHand ? _leftFingerTargets : _rightFingerTargets;
            var has = leftHand ? _leftFingerHasTarget : _rightFingerHasTarget;

            // 指尖目标严格来自「手指 ↔ 音高」的共用分配结果：
            // 第 f 根手指的目标就是「分配给第 f 根手指的那个音高」的键面按压点。
            // 这样弯曲的手指、指向的目标、真正按下的琴键三者严格一一对应
            // （旧实现用「离键最近的指尖」自行配对，会与指法分配产生矛盾）。
            bool anatomical = UseAnatomicalContact && hand is IHandOrientationRig;
            float reachAdjust = def != null ? def.Hand.FingertipReachAdjustment : 0f;

            for (int f = 0; f < fingerCount; f++)
            {
                int pitch = Assignment.GetPitch(leftHand, f);
                if (pitch < 0) continue;
                if (!_keyTouches.TryGetValue(pitch, out var touch)) continue;

                bool thumbPad = f == 0 && hand is IThumbContactRig { HasThumbPad: true };
                targets[f] = thumbPad ? touch.ThumbPress : touch.Press;
                // 拇指参考点落在蒙皮外侧，其他手指继续使用距键顶有余量的参考点。
                targets[f].y += anatomical || thumbPad ? ContactPadHeight(hand, f) : FingertipPadRadius;
                // 黑键按压（接触方案批次 2）：黑键键面高 10 mm，弯曲不足会让指尖压入键面；
                // 只抬「控制目标」（FingerPoseController 朝目标弯曲），统计仍按真实键面口径。
                if (touch.Black && BlackKeyTargetLift != 0f) targets[f].y += BlackKeyTargetLift;
                if (reachAdjust != 0f && !thumbPad) targets[f].y += reachAdjust;
                has[f] = true;
            }

            // 归因统计（批次 2.1）：每个「按下的音」计入一次样本，按三类失败分别计数
            for (int i = 0; i < pitches.Count; i++)
            {
                int pitch = pitches[i];
                _fingertipTargetSamples++;
                if (Assignment.GetFinger(leftHand, pitch) < 0) _unassignedFingertipSamples++;
                if (!_keyTouches.TryGetValue(pitch, out var touch)) { _missingKeyTouchSamples++; continue; }
                // (c) 类：合法的键盘区域内但手臂够不到（2.3 引入肩距判定；盒子判定同时保留，
                // 因为「目标在盒外」同样是解不到的情形，例如角色比例异常时）
                if (!IsWithinPoseLimits(touch.Press) || !IsArmReachable(touch.Press, leftHand))
                    _unreachableTargetSamples++;
            }

            // 兜底：极端情况下（提供器没能给出分配）仍保证「按下的音都有指尖指向」，
            // 否则会出现"键亮了但没有手指按"的空档。
            for (int i = 0; i < pitches.Count; i++)
            {
                int pitch = pitches[i];
                if (Assignment.GetFinger(leftHand, pitch) >= 0 || Assignment.HasUnplayableNotes(leftHand)) continue;
                if (!_keyTouches.TryGetValue(pitch, out var touch)) continue;

                int free = -1;
                for (int f = 0; f < fingerCount; f++)
                {
                    if (!has[f]) { free = f; break; }
                }
                if (free < 0) break;
                targets[free] = touch.Press;
                targets[free].y += FingertipPadRadius;
                if (touch.Black && BlackKeyTargetLift != 0f) targets[free].y += BlackKeyTargetLift;
                if (reachAdjust != 0f) targets[free].y += reachAdjust;
                has[free] = true;
            }

            // 和弦（≥2 个音）成形过渡：从手型上方在「自适应后的成形时长」内下压到键面。
            // 批次 2.5：短音符（16 分、快速和弦）上固定 0.10 s 常走不完，起音瞬间仍悬空；
            // 因此把成形时长压到当前和弦最短音符时值的 40%（上限仍是 ChordFormationSeconds）。
            // 注意：blend 是跨帧累积量，因此这里只清「是否有目标」标志，不清 blend（否则过渡永远走不完）。
            bool chord = pitches.Count >= 2;
            var blend = leftHand ? _leftFingerBlend : _rightFingerBlend;
            float formation = ChordFormationSeconds;
            if (chord)
            {
                float shortest = float.MaxValue;
                for (int i = 0; i < pitches.Count; i++)
                {
                    if (_pressedDurations.TryGetValue(pitches[i], out float dur) && dur > 0f && dur < shortest)
                        shortest = dur;
                }
                if (shortest < float.MaxValue) formation = Mathf.Min(formation, shortest * 0.4f);
            }
            float rate = formation > 1e-4f ? Time.deltaTime / formation : 1f;
            for (int f = 0; f < 5; f++)
            {
                if (!has[f])
                {
                    blend[f] = 0f;
                    continue;
                }
                if (chord) blend[f] = Mathf.Clamp01(blend[f] + rate);
                else blend[f] = 1f;

                // 蒙皮角色在起音前已准备手型。起音后只保留小幅缓冲，避免合奏时再抬一次指尖。
                float approachHeight = hand is IHandOrientationRig
                    ? (UseAnatomicalContact ? 0f : Mathf.Min(ChordApproachHeight, 0.005f)) : ChordApproachHeight;
                targets[f].y += approachHeight * (1f - Mathf.SmoothStep(0f, 1f, blend[f]));
            }

            if (anatomical)
                PrepareUpcomingChord(hand, leftHand, fingerCount, targets, has);
            else
                PrepareUpcomingFinger(hand, leftHand, fingerCount, targets, has);
        }

        private void PrepareUpcomingChord(IHandRig hand, bool leftHand, int fingerCount,
            Vector3[] targets, bool[] has)
        {
            float lead = FingerPreparationLead(leftHand);
            if (lead <= 0f || lead > FingerPreparationSeconds || !(hand is IFingerJointRig rig)) return;
            var upcoming = leftHand ? _leftUpcomingChord : _rightUpcomingChord;
            float approach = 1f - Mathf.Clamp01(lead / FingerPreparationSeconds);
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(approach * 3f));
            for (int finger = 0; finger < fingerCount; finger++)
            {
                if (has[finger] || upcoming[finger] < 0 || !_keyTouches.TryGetValue(upcoming[finger], out var key))
                    continue;
                var tip = rig.GetFingertip(finger);
                if (tip == null) continue;
                bool thumbPad = finger == 0 && hand is IThumbContactRig { HasThumbPad: true };
                float lift = thumbPad && !leftHand
                    ? RightThumbApproachHeight(hand, approach, 0.65f)
                    : ContactPadHeight(hand, finger) + 0.008f * Mathf.Sin(approach * Mathf.PI);
                targets[finger] = Vector3.Lerp(tip.position, (thumbPad ? key.ThumbPress : key.Press) + Vector3.up * lift, blend);
                has[finger] = true;
            }
        }

        private void PrepareUpcomingFinger(IHandRig hand, bool leftHand, int fingerCount,
            Vector3[] targets, bool[] has)
        {
            int pitch = leftHand ? _upcomingLeftPitch : _upcomingRightPitch;
            if (pitch < 0 || _pressed.Contains(pitch) || !_keyTouches.TryGetValue(pitch, out var touch)) return;

            float time = leftHand ? _upcomingLeftTime : _upcomingRightTime;
            float lead = FingerPreparationLead(leftHand);
            if (lead <= 0f || lead > FingerPreparationSeconds) return;

            int finger = Assignment.PreviewFinger(leftHand, pitch, time);
            if (finger < 0 || finger >= fingerCount || has[finger]) return;
            if (!(hand is IFingerJointRig rig) || !rig.TryGetJoints(finger, out var root, out _, out _)) return;

            float velocity = (leftHand ? _upcomingLeftVelocity : _upcomingRightVelocity) / 127f;
            float approach = 1f - Mathf.Clamp01(lead / FingerPreparationSeconds);
            // 先给指腹一个小弧线。力度影响抬指高度，接近发声时回落到键面上方。
            bool thumbPad = finger == 0 && hand is IThumbContactRig { HasThumbPad: true };
            float lift = thumbPad && !leftHand
                ? RightThumbApproachHeight(hand, approach, velocity)
                : 0.005f + Mathf.Sin(approach * Mathf.PI) * Mathf.Lerp(0.005f, 0.016f, velocity);
            var target = (thumbPad ? touch.ThumbPress : touch.Press) + Vector3.up * lift;
            float reach = rig.GetFingerLength(finger);
            var pitches = leftHand ? _leftPitches : _rightPitches;
            float reachWeight = pitches.Count == 0 ? 1f
                : 1f - Mathf.InverseLerp(reach * 0.85f, reach * 1.05f, Vector3.Distance(root.position, target));
            float entry = Mathf.SmoothStep(0f, 1f, approach * 3f) * reachWeight;
            if (entry <= 0f) return;
            var tip = rig.GetFingertip(finger);
            targets[finger] = tip == null ? target : Vector3.Lerp(tip.position, target, entry);
            has[finger] = true;
        }

        private float FingerPreparationLead(bool leftHand)
        {
            if (!leftHand && _primeOpeningThumb) return 1f / 60f;
            return (leftHand ? _upcomingLeftTime : _upcomingRightTime) - _currentTime;
        }

        private static float RightThumbApproachHeight(IHandRig hand, float phase, float velocity)
        {
            // 拇指从略高于键面的弧线贴近指腹接触点，落键帧沿用实时键面高度。
            float arch = Mathf.Sin(phase * Mathf.PI);
            return ContactPadHeight(hand, 0) + Mathf.Lerp(0.009f, 0.014f, velocity) * arch * arch
                + 0.004f * (1f - Mathf.SmoothStep(0f, 1f, phase));
        }

        /// <summary>每帧清空「该指是否已有目标」标志（blend 保留，用于和弦成形过渡）。</summary>
        private void ClearFingertipTargets(bool leftHand)
        {
            var has = leftHand ? _leftFingerHasTarget : _rightFingerHasTarget;
            for (int f = 0; f < 5; f++) has[f] = false;
        }

        /// <summary>彻底复位指尖目标与成形进度（ReleaseAll / Seek 后调用，避免沿用旧手型）。</summary>
        private void ResetFingertipTargets(bool leftHand)
        {
            var has = leftHand ? _leftFingerHasTarget : _rightFingerHasTarget;
            var blend = leftHand ? _leftFingerBlend : _rightFingerBlend;
            for (int f = 0; f < 5; f++)
            {
                has[f] = false;
                blend[f] = 0f;
            }
        }

        /// <summary>指尖世界坐标：优先使用 <see cref="IFingertipRig"/>，否则按指根 + 前向 + 长度估计。</summary>
        private Vector3 FingertipWorld(IHandRig hand, int fingerIndex)
        {
            if (hand is IFingertipRig tipRig)
            {
                var tip = tipRig.GetFingertip(fingerIndex);
                if (tip != null) return tip.position;
            }
            var fingers = hand.Fingers;
            if (fingers == null || fingerIndex >= fingers.Count) return hand.Wrist.position;
            var f = fingers[fingerIndex];
            if (f == null) return hand.Wrist.position;
            return f.TransformPoint(0f, 0f, FingertipReachEstimate);
        }

        /// <summary>该手「指尖到目标键面」的平均最近点误差（米）；无样本返回 -1。</summary>
        private float MeanFingertipError(IHandRig hand, bool leftHand, out bool hovering)
        {
            hovering = false;
            if (hand == null || hand.Fingers == null || hand.Fingers.Count == 0) return -1f;

            CollectHandPitches(leftHand);
            if (_pitchScratch.Count == 0) return -1f;

            float sum = 0f;
            int samples = 0;
            for (int i = 0; i < _pitchScratch.Count; i++)
            {
                if (!_keyTouches.TryGetValue(_pitchScratch[i], out var touch)) continue;

                float best = float.MaxValue;
                int count = Mathf.Min(hand.Fingers.Count, 5);
                for (int f = 0; f < count; f++)
                {
                    float d = Vector3.Distance(FingertipWorld(hand, f), touch.Press);
                    if (d < best) best = d;
                }
                if (best == float.MaxValue) continue;

                sum += best;
                samples++;
                if (best > HoverHeight) hovering = true;
            }
            return samples > 0 ? sum / samples : -1f;
        }

        /// <summary>拇指方向抽查：拇指轴与掌心前向的夹角超过阈值即为异常（默认 75°，正常手型约 45°–60°）。</summary>
        private bool HasThumbOrientationIssue()
        {
            if (_avatar == null) return false;
            return ThumbIssueFor(_avatar.LeftHand, true) || ThumbIssueFor(_avatar.RightHand, false);
        }

        private bool ThumbIssueFor(IHandRig hand, bool leftHand)
        {
            if (hand == null || hand.Palm == null || hand.Fingers == null || hand.Fingers.Count == 0) return false;

            CollectHandPitches(leftHand);
            if (_pitchScratch.Count == 0) return false;   // 该手未按下时不抽查，避免静止姿态误报

            var thumb = hand.Fingers[0];
            if (thumb == null) return false;

            float angle = Vector3.Angle(thumb.forward, hand.Palm.forward);
            return angle > ThumbOrientationMaxDegrees;
        }

        private bool HasPalmPenetration()
        {
            if (!_hasLimits) return false;
            var left = _avatar != null ? _avatar.LeftHand : null;
            var right = _avatar != null ? _avatar.RightHand : null;
            return PalmBelow(left, true) || PalmBelow(right, false);
        }

        private bool PalmBelow(IHandRig hand, bool leftHand)
        {
            if (hand == null || hand.Palm == null) return false;
            CollectHandPitches(leftHand);
            if (_pitchScratch.Count == 0) return false;
            return hand.Palm.position.y < _limits.MinY - HandPoseValidator.Tolerance;
        }

        // ==================== 目标平滑与限速（保持既有语义） ====================

        /// <summary>
        /// 分级限制目标位置变化：
        /// ① 小变化（同一手位内的音程移动，≤ <see cref="TargetJumpThreshold"/>）**直接精确对齐**
        ///    —— 保证「目标琴键命中率」不被平滑拖累；
        /// ② 大跳变（换手位）才以 <see cref="TargetMaxSpeed"/> 限速过渡，抑制单帧抖跳。
        /// </summary>
        private Vector3 LimitTargetRate(bool leftHand, Vector3 rawTarget)
        {
            // 首次采样（或 ReleaseAll 后）**按手各自对齐**，避免从原点插值飞入。
            // 注意：必须左右手各持一个标志位——早期实现用单一标志把两手都写成同一个 rawTarget，
            // 会让其中一只手的目标瞬移到另一只手的位置（换位/暂停恢复时可见的跳变）。
            if (leftHand)
            {
                if (!_hasLeftSmoothed)
                {
                    _leftSmoothedTarget = rawTarget;
                    _hasLeftSmoothed = true;
                    return rawTarget;
                }
            }
            else
            {
                if (!_hasRightSmoothed)
                {
                    _rightSmoothedTarget = rawTarget;
                    _hasRightSmoothed = true;
                    return rawTarget;
                }
            }

            var current = leftHand ? _leftSmoothedTarget : _rightSmoothedTarget;

            if (TargetJumpThreshold <= 0f || Vector3.Distance(current, rawTarget) <= TargetJumpThreshold)
            {
                if (leftHand) _leftSmoothedTarget = rawTarget;
                else _rightSmoothedTarget = rawTarget;
                return rawTarget;
            }

            if (TargetMaxSpeed <= 0f) return rawTarget;

            float maxStep = TargetMaxSpeed * Time.deltaTime;
            if (leftHand)
            {
                _leftSmoothedTarget = MoveTowards(_leftSmoothedTarget, rawTarget, maxStep);
                return _leftSmoothedTarget;
            }
            _rightSmoothedTarget = MoveTowards(_rightSmoothedTarget, rawTarget, maxStep);
            return _rightSmoothedTarget;
        }

        /// <summary>朝目标移动最多 <paramref name="maxDelta"/> 距离；距离不超过该值时直接精确到达。</summary>
        private static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
        {
            var delta = target - current;
            float distance = delta.magnitude;
            if (distance < 1e-6f || distance <= maxDelta) return target;
            return current + delta * (maxDelta / distance);
        }

        /// <summary>
        /// 临界阻尼二阶跟随：<c>a = ω²·(target − pos) − 2ω·v</c>。
        /// <para>
        /// 目标每帧可变，不需要「轨迹段」——这是它与 minimum jerk 的本质差别，
        /// 也是本场景唯一可行的建模方式（目标由手掌修正与预置切换连续驱动）。
        /// 阶跃响应：加速度在起点最大、速度呈钟形单峰、终点零速落位，
        /// 即人手「慢起—快中—缓停」的形状，而不是一阶指数逼近的「起步即全速」。
        /// </para>
        /// <para>
        /// 半隐式欧拉按子步积分：ω·dt 接近 1 会振荡，低帧率下必须切子步
        /// （ω=18 时子步 1/120 s → ω·dt = 0.15）。
        /// </para>
        /// </summary>
        private Vector3 AdvanceSpring(ref Vector3 velocity, Vector3 current, Vector3 target, float speedScale)
        {
            float omega = Mathf.Max(1f, SpringOmega * Mathf.Clamp(speedScale, 0.2f, 3f));

            float remaining = Mathf.Max(0f, Time.deltaTime);
            if (remaining <= 1e-6f) return current;

            float h = Mathf.Clamp(SpringMaxSubStep, 1f / 480f, 0.05f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(remaining / h), 1, 32);
            float dt = remaining / steps;

            var pos = current;
            var vel = velocity;

            for (int i = 0; i < steps; i++)
            {
                // a = ω²(x_t − x) − 2ωv（临界阻尼：阻尼系数 = 2√(k·m) 且 m = 1）
                var accel = (target - pos) * (omega * omega) - vel * (2f * omega);
                vel += accel * dt;
                pos += vel * dt;
            }

            // 速度状态与实际位移约束保持一致：否则速度会在状态里累积，
            // 外层硬约束钳住位移后，下一帧又会拿着过大的速度继续冲（观感是「粘住后猛弹」）。
            float maxSpeed = MaxStepPerFrame / remaining;
            vel = Vector3.ClampMagnitude(vel, maxSpeed);
            velocity = vel;

            return pos;
        }

        /// <summary>
        /// 换位过渡：水平位移超过阈值时把目标抬高，接近目标后自然回落，
        /// 使手腕走「抬起—平移—落下」弧线而非贴键横扫。
        /// </summary>
        private Vector3 ApplyGlideLift(Vector3 current, Vector3 target)
        {
            float horizontal = Vector2.Distance(
                new Vector2(current.x, current.z),
                new Vector2(target.x, target.z));

            if (horizontal <= GlideThreshold) return target;

            float t = Mathf.Clamp01((horizontal - GlideThreshold) / GlideThreshold);
            return new Vector3(target.x, target.y + GlideLift * t, target.z);
        }

        /// <summary>按音符平均间隔在 [MinSpeedScale, MaxSpeedScale] 之间插值。</summary>
        private float CurrentSpeedScale()
        {
            if (_avgNoteGap <= FastGapSeconds) return MaxSpeedScale;
            if (_avgNoteGap >= SlowGapSeconds) return MinSpeedScale;
            float t = Mathf.InverseLerp(FastGapSeconds, SlowGapSeconds, _avgNoteGap);
            return Mathf.Lerp(MaxSpeedScale, MinSpeedScale, t);
        }

        // ==================== 指标采样 ====================

        private void SampleMetrics()
        {
            var left = _avatar.LeftHand;
            var right = _avatar.RightHand;
            if (left == null || right == null) return;

            // 指尖误差 / 离键悬空（审查 1.2C：不要只统计手腕距离）
            float leftError = -1f;
            float rightError = -1f;
            if (EnableFingertipMetrics)
            {
                leftError = MeanFingertipError(left, true, out _leftHovering);
                rightError = MeanFingertipError(right, false, out _rightHovering);
            }
            else
            {
                _leftHovering = false;
                _rightHovering = false;
            }

            // 视觉抽查标记
            _thumbOrientationIssue = HasThumbOrientationIssue();
            _palmPenetration = HasPalmPenetration();

            // 被分配指尖误差 + 键面距离（附录 A.6.3：与「最近指尖」分开统计）
            float leftAssigned = AssignedFingertipError(left, true, out float leftSurface, out bool leftPenetration);
            float rightAssigned = AssignedFingertipError(right, false, out float rightSurface, out bool rightPenetration);

            bool crossed = HandPoseValidator.AreHandsCrossed(left.Wrist.position, right.Wrist.position);
            if (crossed) _crossedHandWarningCount++;

            // 过渡帧：目标大跳变或手腕单帧位移超过阈值（用于「换位峰值」口径）
            float step = 0f;
            if (_hasPrevWrist)
            {
                step = Mathf.Max(
                    Vector3.Distance(_prevLeftWrist, left.Wrist.position),
                    Vector3.Distance(_prevRightWrist, right.Wrist.position));
            }
            _prevLeftWrist = left.Wrist.position;
            _prevRightWrist = right.Wrist.position;
            _hasPrevWrist = true;

            _transitionFrame = _leftLargeMotion >= LargeMotionDistance
                || _rightLargeMotion >= LargeMotionDistance
                || step >= TransitionStepThreshold;

            int jointLimitTotal = PoseIssueSource != null ? PoseIssueSource.JointLimitClampCount : 0;
            int jointLimitDelta = Mathf.Max(0, jointLimitTotal - _lastJointLimitCount);
            _lastJointLimitCount = jointLimitTotal;

            var sample = new HandMotionSample
            {
                LeftWrist = left.Wrist.position,
                RightWrist = right.Wrist.position,
                LeftTarget = _leftTargetPos,
                RightTarget = _rightTargetPos,
                LeftHasTarget = _leftHasTarget,
                RightHasTarget = _rightHasTarget,
                HitRadius = HitRadius,
                MovingThreshold = MovingThreshold,

                HasFingertipErrors = EnableFingertipMetrics,
                LeftFingertipError = leftError,
                RightFingertipError = rightError,
                FingertipTolerance = FingertipErrorTolerance,
                SpeedAnomalyThreshold = SpeedAnomalyStepDelta,

                HandsCrossed = crossed,
                ThumbOrientationIssue = _thumbOrientationIssue,
                PalmPenetration = _palmPenetration,
                Hovering = _leftHovering || _rightHovering,

                BlackKeyFrame = HasBlackKeyTarget(),
                ChordFrame = CountHandPitches(true) >= 2 || CountHandPitches(false) >= 2,
                FastScaleFrame = _avgNoteGap <= FastScaleGapSeconds,
                LargeMotionFrame = _leftLargeMotion >= LargeMotionDistance || _rightLargeMotion >= LargeMotionDistance,

                HasAssignedFingertipErrors = EnableFingertipMetrics && (leftAssigned >= 0f || rightAssigned >= 0f),
                LeftAssignedFingertipError = leftAssigned,
                RightAssignedFingertipError = rightAssigned,

                HasSurfaceDistances = EnableFingertipMetrics && (leftSurface > -100f || rightSurface > -100f),
                LeftSurfaceDistance = leftSurface,
                RightSurfaceDistance = rightSurface,

                TransitionFrame = _transitionFrame,
                JointLimitHits = jointLimitDelta,
                FingerPenetration = leftPenetration || rightPenetration,
                HasSegmentClearance = _clearanceProbe.IsBuilt,
                SegmentClearance = Mathf.Min(_leftSegmentClearance, _rightSegmentClearance)
            };
            _metrics.AddSample(sample);

            _leftLargeMotion = 0f;
            _rightLargeMotion = 0f;
        }

        /// <summary>
        /// 被分配指尖到目标键按压点的平均误差（米），以及该手指尖到键面的平均法向距离。
        /// 与 <see cref="MeanFingertipError"/>（最近指尖 / 乐观下界）分开，口径与视觉一致性测试一致。
        /// </summary>
        private float AssignedFingertipError(IHandRig hand, bool leftHand, out float meanSurface, out bool penetration)
        {
            meanSurface = -999f;
            penetration = false;
            if (hand == null) return -1f;

            CollectHandPitches(leftHand);
            if (_pitchScratch.Count == 0) return -1f;

            float sum = 0f;
            float surfaceSum = 0f;
            int count = 0;

            for (int i = 0; i < _pitchScratch.Count; i++)
            {
                int pitch = _pitchScratch[i];
                if (!_keyTouches.TryGetValue(pitch, out var touch)) continue;

                int finger = Assignment.GetFinger(leftHand, pitch);
                if (finger < 0) continue;

                Vector3 tip = FingertipWorld(hand, finger);
                Vector3 point = finger == 0 && hand is IThumbContactRig { HasThumbPad: true } ? touch.ThumbPress : touch.Press;
                sum += Vector3.Distance(tip, point);

                float surface = HandPoseValidator.SignedDistanceToSurface(tip.y, point.y);
                surfaceSum += surface;
                if (surface < -FingertipPenetrationTolerance) penetration = true;
                count++;
            }

            if (count == 0) return -1f;
            meanSurface = surfaceSum / count;
            return sum / count;
        }

        private bool HasBlackKeyTarget()
        {
            foreach (var pitch in _pressed)
            {
                if (Piano.PianoKeyMap.IsBlackKey(pitch)) return true;
            }
            return false;
        }

        private int CountHandPitches(bool leftHand)
        {
            CollectHandPitches(leftHand);
            return _pitchScratch.Count;
        }

        private void RecordPoseIssue(string issue)
        {
            _poseIssueCount++;
            if (_poseIssues.Count < MaxRecordedIssues) _poseIssues.Add(issue);
        }

        // ==================== 几何与查找 ====================

        /// <summary>按钢琴实际几何构建手腕允许范围（键盘两端、键面高度与深度）。</summary>
        private void BuildLimits()
        {
            _hasLimits = false;
            if (_keyMap == null) return;
            if (!_keyMap.TryGetKey(Piano.PianoKeyMap.MinPitch, out var lowKey) || lowKey == null) return;
            if (!_keyMap.TryGetKey(Piano.PianoKeyMap.MaxPitch, out var highKey) || highKey == null) return;

            float minX = Mathf.Min(lowKey.position.x, highKey.position.x);
            float maxX = Mathf.Max(lowKey.position.x, highKey.position.x);
            float keyRootZ = lowKey.position.z;

            // 键根位于键底部（世界 y ≈ KeybedHeight），键面在其上方一个键身高度处。
            // 注意：不能再叠加 KeySurfaceY —— 键根世界 y 已包含 KeybedHeight。
            float surfaceY = lowKey.position.y + _keyMap.WhiteHeight;

            // 深度上界取得较宽：手腕从人物侧就位时会经过琴键前方的空间，
            // 那属于正常过渡路径而非穿模；真正的穿模风险在 z 下界（深入琴体）。
            _limits = new HandPoseLimits(
                minY: surfaceY + 0.004f,          // 不低于键面（防穿入琴键）
                maxY: surfaceY + 0.30f,           // 不过分悬空
                minX: minX - 0.02f,
                maxX: maxX + 0.02f,
                minZ: keyRootZ + 0.01f,           // 不深入琴体（穿模）
                maxZ: keyRootZ + 0.90f);

            _hasLimits = true;
        }

        /// <summary>
        /// 为 88 键预计算可复用的按压目标点：落在键面前半段（不是键位中心的任意高度），
        /// 并按黑键 / 白键分别取真实键面高度（方案 5.3 第二阶段 / 审查 1.2B）。
        /// 该过程只在 <see cref="Initialize"/> 执行一次，Update 路径零分配。
        /// </summary>
        private void BuildKeyTouches(Quaternion depressedRotation)
        {
            _keyTouches.Clear();
            if (_keyMap == null) return;

            float forward = Mathf.Clamp(FingertipTargetForward, 0.1f, 1f);
            for (int pitch = Piano.PianoKeyMap.MinPitch; pitch <= Piano.PianoKeyMap.MaxPitch; pitch++)
            {
                if (!_keyMap.TryGetKey(pitch, out var key) || key == null) continue;

                bool black = Piano.PianoKeyMap.IsBlackKey(pitch);
                float length = black ? _keyMap.BlackLength : _keyMap.WhiteLength;
                float height = black ? _keyMap.BlackHeight : _keyMap.WhiteHeight;
                // 键根在键后端：沿 +Z（向演奏者）偏移到按压区
                var press = new Vector3(0f, height, length * forward);
                // 指腹落在白键前部，给键前沿及相邻黑键都保留余量。
                var thumbPress = black ? press : new Vector3(0f, height, length * Mathf.Clamp(forward, 0.90f, 0.93f));
                _keyTouches[pitch] = new KeyTouchPoint(key, press, thumbPress, black, depressedRotation);
            }
        }

        private void CollectHandPitches(bool leftHand)
        {
            _pitchScratch.Clear();
            foreach (var pitch in _pressed)
            {
                if (IsActivePitchLeft(pitch) == leftHand) _pitchScratch.Add(pitch);
            }
        }

        private int? FindHandTarget(bool leftHand)
        {
            int? best = null;
            foreach (var pitch in _pressed)
            {
                bool isLeft = IsActivePitchLeft(pitch);
                if (isLeft != leftHand) continue;
                if (Assignment.HasUnplayableNotes(leftHand) && Assignment.GetFinger(leftHand, pitch) < 0) continue;
                if (!best.HasValue) { best = pitch; continue; }
                if (leftHand && pitch < best.Value) best = pitch;    // 左手取最低音
                if (!leftHand && pitch > best.Value) best = pitch;   // 右手取最高音
            }
            return best;
        }

        private Vector3 KeyToHandPos(Transform key)
        {
            var p = key.position;
            return new Vector3(p.x, HandHeight, p.z + HandDepth);
        }

        private bool TryPlaceChordWrist(IHandRig hand, bool leftHand, ref Vector3 target, bool constrained)
        {
            var pitches = leftHand ? _leftPitches : _rightPitches;
            int minimumNotes = constrained ? 1 : 2;
            if (pitches.Count < minimumNotes || !(hand is IFingerJointRig rig)) return false;

            int count = 0;
            Vector3 anchors = Vector3.zero;
            float minimumDepth = float.NegativeInfinity;
            foreach (int pitch in pitches)
            {
                int finger = Assignment.GetFinger(leftHand, pitch);
                if (finger < 0 || !_keyTouches.TryGetValue(pitch, out var key)
                    || !rig.TryGetJoints(finger, out var root, out _, out _)) continue;

                Vector3 offset = root.position - hand.Wrist.position;
                bool thumbPad = finger == 0 && hand is IThumbContactRig { HasThumbPad: true };
                Vector3 point = thumbPad ? key.ThumbPress : key.Press;
                float pad = thumbPad ? ContactPadHeight(hand, finger) : FingertipPadRadius;
                _chordReachCenters[count] = point + Vector3.up * pad - offset;
                float fingerLength = rig.GetFingerLength(finger);
                _chordReachRadii[count] = fingerLength * 0.92f;
                anchors += _chordReachCenters[count] + new Vector3(0f, fingerLength * 0.23f, fingerLength * 0.67f);
                // 蒙皮骨架的指根需要留在琴键的演奏者一侧，避免手指反向折回。
                if (hand is IHandOrientationRig)
                    minimumDepth = Mathf.Max(minimumDepth, _chordReachCenters[count].z + 0.003f);
                count++;
            }
            if (count < minimumNotes) return false;

            // 每根手指给出腕部的可达球。只允许小幅降腕，兼顾短指的落键与掌面高度。
            Vector3 origin = target;
            Vector3 position = target;
            if (constrained)
            {
                Vector3 anchor = anchors / count;
                // 平均指根的自然落键位置。现有腕高仍作为上界，限制一次校正的幅度。
                position.x = anchor.x;
                position.y = Mathf.Max(origin.y, anchor.y + 0.014f);
                position.z = anchor.z + 0.024f;
            }
            position.z = Mathf.Max(position.z, minimumDepth);
            float minimumHeight = origin.y - 0.025f;
            if (_hasLimits) minimumHeight = Mathf.Min(origin.y, Mathf.Max(minimumHeight, _limits.MinY));
            float maxHeight = constrained ? origin.y + 0.020f : origin.y;
            position.y = Mathf.Clamp(position.y, constrained ? origin.y : minimumHeight, maxHeight);
            for (int iteration = 0; iteration < 12; iteration++)
            {
                var correction = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    Vector3 delta = _chordReachCenters[i] - position;
                    float distance = delta.magnitude;
                    if (distance > _chordReachRadii[i] && distance > 1e-5f)
                        correction += delta * (1f - _chordReachRadii[i] / distance);
                }
                if (correction.sqrMagnitude < 1e-8f) break;
                position += correction / count;
                position.y = Mathf.Clamp(position.y, constrained ? origin.y : minimumHeight, maxHeight);
                position.z = Mathf.Max(position.z, minimumDepth);
            }
            target = origin + Vector3.ClampMagnitude(position - origin, MaxPalmCorrection);
            return true;
        }

        private static float NaturalWristYaw(Transform wrist)
        {
            if (wrist.parent == null) return 0f;
            var arm = Vector3.ProjectOnPlane(wrist.position - wrist.parent.position, Vector3.up);
            return arm.sqrMagnitude > 1e-6f
                ? Mathf.Clamp(Vector3.SignedAngle(Vector3.back, arm, Vector3.up), -28f, 28f) : 0f;
        }

        private static Quaternion ContactWristRotation(IHandOrientationRig basis, float yaw, float pitch, float roll)
        {
            Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.back;
            float slope = Mathf.Tan(Mathf.Clamp(pitch, 0f, 20f) * Mathf.Deg2Rad);
            Vector3 forward = (direction + Vector3.down * slope).normalized;
            var bind = Quaternion.LookRotation(basis.FingerForwardLocal, basis.BackNormalLocal);
            return Quaternion.AngleAxis(roll, forward) * Quaternion.LookRotation(forward, Vector3.up)
                * Quaternion.Inverse(bind);
        }

        // 指尖参考点略高于键面，给按下过程中仍在移动的键帽留余量。
        private const float AnatomicalContactHeight = 0.0025f;

        private static float ContactPadHeight(IHandRig hand, int finger) =>
            finger == 0 && hand is IThumbContactRig { HasThumbPad: true } ? 0.0008f : AnatomicalContactHeight;

        private bool TryFitContactWrist(IHandRig hand, bool leftHand, bool pressed, FingerPoseController pose,
            ref WristContactFit fit, ref Vector3 target, int upcomingThumbPitch)
        {
            if (!(hand is IHandOrientationRig basis) || !(hand is IFingerJointRig rig)) return false;
            var definition = _avatar.CurrentDefinition.Hand;
            int count = 0;
            long noteSet = 0;
            var upcoming = leftHand ? _leftUpcomingChord : _rightUpcomingChord;
            for (int finger = 0; finger < 5; finger++)
            {
                noteSet <<= 7;
                int pitch = pressed ? Assignment.GetPitch(leftHand, finger)
                    : upcomingThumbPitch < 0 ? upcoming[finger] : finger == 0 ? upcomingThumbPitch : -1;
                if (pitch < 0 || !_keyTouches.ContainsKey(pitch)) continue;
                noteSet |= (uint)(pitch + 1);
                _contactFitFingers[count++] = finger;
            }
            if (count == 0) return false;
            bool thumbSolo = count == 1 && _contactFitFingers[0] == 0 && hand is IThumbContactRig { HasThumbPad: true };
            // 右拇指提前对齐压键时的腕位。起音后只用指节跟随键程，腕位不再换目标。
            bool bridgeThumbStroke = thumbSolo && !leftHand;
            if (thumbSolo && pressed && !bridgeThumbStroke) noteSet |= 1L << 35;
            if (fit.NoteSet != noteSet)
            {
                for (int i = 0; i < count; i++)
                {
                    int finger = _contactFitFingers[i];
                    int pitch = pressed ? Assignment.GetPitch(leftHand, finger)
                        : upcomingThumbPitch >= 0 ? upcomingThumbPitch : upcoming[finger];
                    var touch = _keyTouches[pitch];
                    bool thumbPad = finger == 0 && hand is IThumbContactRig { HasThumbPad: true };
                    float pad = ContactPadHeight(hand, finger);
                    if (!thumbPad) pad += definition.FingertipReachAdjustment;
                    if (touch.Black) pad += BlackKeyTargetLift;
                    // 琴键运动期间，右拇指沿同一腕位完成预备和下压。
                    bool down = count > 1 || bridgeThumbStroke || (thumbSolo && pressed);
                    Vector3 point = thumbPad ? (down ? touch.ThumbDepressed : touch.ThumbRest)
                        : (down ? touch.Depressed : touch.Rest);
                    _contactFitTargets[i] = point + Vector3.up * pad;
                }
                Vector3 seed = target;
                // 短音起音前先保证换位距离，和弦则容许较大腕位调整来同时覆盖多键。
                float followCost = count == 1 ? 0.020f : 0.004f;
                float naturalYaw = NaturalWristYaw(hand.Wrist);
                Vector3 current = hand.Wrist.position;
                Vector3 savedPosition = hand.Wrist.localPosition;
                Quaternion savedRotation = hand.Wrist.localRotation;
                Vector3 best = seed;
                float bestYaw = naturalYaw;
                float bestScore = float.PositiveInfinity;

                void Consider(Vector3 position, float yaw)
                {
                    if (_hasLimits) position = HandPoseValidator.Clamp(position, _limits);
                    yaw = Mathf.Clamp(yaw, -40f, 40f);
                    float offset = _hasLimits
                        ? Mathf.Clamp((position.x - (_limits.MinX + _limits.MaxX) * 0.5f) /
                            Mathf.Max(0.001f, (_limits.MaxX - _limits.MinX) * 0.5f), -1f, 1f) : 0f;
                    float roll = offset * definition.WristRollPerHandUnit * (leftHand ? -1f : 1f);
                    float pitch = definition.WristPitch + Mathf.Abs(offset) * Mathf.Max(0f, OuterWristPitchBias);
                    hand.Wrist.SetPositionAndRotation(position, ContactWristRotation(basis, yaw, pitch,
                        Mathf.Clamp(roll, -MaxWristTwistDegrees, MaxWristTwistDegrees)));
                    float error = 0f;
                    float worst = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 contact = _contactFitTargets[i];
                        Vector3 miss = pose.PredictAnatomicalTip(hand, basis, rig, _contactFitFingers[i], contact) - contact;
                        // 同一腕位下同时够到各键，避免用另一指的低误差抵消悬空。
                        float heightWeight = count > 1 && miss.y > 0f ? 6f : 3f;
                        bool thumb = _contactFitFingers[i] == 0 && hand is IThumbContactRig { HasThumbPad: true };
                        float depthWeight = thumb ? 1f : 0.30f;
                        float missCost = miss.x * miss.x + heightWeight * miss.y * miss.y + depthWeight * miss.z * miss.z;
                        if (thumb && count > 1)
                        {
                            // 和弦中的短拇指容易偏到邻键。先将指腹留在键帽内，再兼顾其他指尖。
                            float offCenter = Mathf.Max(0f, Mathf.Abs(miss.x) - 0.004f);
                            float pastFront = Mathf.Max(0f, miss.z - 0.004f);
                            missCost += 16f * offCenter * offCenter + 12f * pastFront * pastFront;
                        }
                        error += missCost;
                        worst = Mathf.Max(worst, missCost);
                    }
                    float cost = error / count + worst * 0.6f + followCost * (position - current).sqrMagnitude
                        + 0.00000006f * (yaw - naturalYaw) * (yaw - naturalYaw);
                    if (cost >= bestScore) return;
                    bestScore = cost;
                    best = position;
                    bestYaw = yaw;
                }

                // 先试几种不同掌位，再逐级缩小步长，避免当前腕位把所有候选困在指根后方。
                Consider(seed, naturalYaw);
                Consider(current, fit.NoteSet != 0 ? fit.Yaw : naturalYaw);
                if (fit.NoteSet != 0) Consider(fit.Position, fit.Yaw);
                foreach (float yaw in ContactYawSeeds)
                {
                    Consider(seed + Vector3.forward * 0.024f, yaw);
                    Consider(seed + Vector3.back * 0.018f, yaw);
                }
                void Refine()
                {
                    for (float step = 0.024f, yawStep = 16f; step >= 0.0029f; step *= 0.5f, yawStep *= 0.5f)
                    {
                        Consider(best + Vector3.right * step, bestYaw);
                        Consider(best - Vector3.right * step, bestYaw);
                        Consider(best + Vector3.up * step, bestYaw);
                        Consider(best - Vector3.up * step, bestYaw);
                        Consider(best + Vector3.forward * step, bestYaw);
                        Consider(best - Vector3.forward * step, bestYaw);
                        Consider(best, bestYaw + yawStep);
                        Consider(best, bestYaw - yawStep);
                    }
                }

                Refine();
                // 和弦再检查一条较低腕位的搜索路径。用同一代价淘汰较差结果。
                if (count > 1 && bestScore > 0.000025f)
                {
                    Vector3 first = best;
                    float firstYaw = bestYaw;
                    float firstScore = bestScore;
                    bestScore = float.PositiveInfinity;
                    Consider(first - Vector3.up * 0.018f, firstYaw);
                    Refine();
                    if (bestScore >= firstScore)
                    {
                        best = first;
                        bestYaw = firstYaw;
                    }
                }
                hand.Wrist.localPosition = savedPosition;
                hand.Wrist.localRotation = savedRotation;
                fit = new WristContactFit { NoteSet = noteSet, Position = best, Yaw = bestYaw };
            }
            target = fit.Position;
            return true;
        }

        private static readonly float[] ContactYawSeeds = { -24f, 0f, 24f };

        private float AdjustContactHeight(IHandRig hand, bool leftHand, bool hasTarget)
        {
            ref float adjustment = ref (leftHand ? ref _leftContactHeight : ref _rightContactHeight);
            if (!hasTarget || !(hand is IFingerJointRig rig))
            {
                adjustment = Mathf.MoveTowards(adjustment, 0f, 0.14f * Time.deltaTime);
                return adjustment;
            }

            float lowest = float.PositiveInfinity;
            for (int finger = 0; finger < hand.Fingers.Count; finger++)
            {
                int pitch = Assignment.GetPitch(leftHand, finger);
                if (pitch < 0 || !_keyTouches.TryGetValue(pitch, out var touch)) continue;
                var tip = rig.GetFingertip(finger);
                if (tip == null || Mathf.Abs(tip.position.x - touch.Press.x) > 0.04f
                    || Mathf.Abs(tip.position.z - touch.Press.z) > 0.06f) continue;
                lowest = Mathf.Min(lowest, tip.position.y - touch.SurfaceY);
            }

            if (!float.IsPositiveInfinity(lowest))
            {
                float goal = Mathf.Clamp(adjustment + 0.003f - lowest, -0.016f, 0.028f);
                adjustment = Mathf.Lerp(adjustment, goal, 1f - Mathf.Exp(-19f * Time.deltaTime));
            }
            return adjustment;
        }

        private void SeparateIdleWrist(bool leftHand, bool movingToKey, ref Vector3 target)
        {
            if (movingToKey) return;
            var other = leftHand ? _avatar.RightHand : _avatar.LeftHand;
            if (other?.Palm == null || !FindHandTarget(!leftHand).HasValue) return;

            // 避让只发生在闲置手，活动手指仍以自己的键帽为准。
            const float Clearance = 0.065f;
            Vector3 delta = target - other.Wrist.position;
            if (Mathf.Abs(delta.x) >= Clearance || Mathf.Abs(delta.z) >= Clearance) return;
            float side = leftHand ? 1f : -1f;
            float clearanceWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(delta.z) / Clearance);
            float shift = Mathf.Max(0f, Clearance - side * delta.x) * clearanceWeight;
            target.x += side * shift;
        }

        /// <summary>无目标时的手位（<paramref name="side"/>：左手 +1 = 世界 +X，右手 −1）。</summary>
        private Vector3 DefaultHandPos(float side) => new Vector3(side * 0.30f, HandHeight, 0.10f);
    }
}
