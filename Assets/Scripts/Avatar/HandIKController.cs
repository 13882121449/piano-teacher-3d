using System.Collections.Generic;
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
    /// 左右手以 C4（pitch=60）分界；不承诺专业指法准确性（方案 2.3）。
    /// </summary>
    public class HandIKController : MonoBehaviour, IFingertipTargetSource
    {
        [Header("手部目标几何（真实钢琴尺度，米）")]
        /// <summary>
        /// 腕部高度（键面约 0.742 m）。取 0.799 使掌指关节比键面高约 60 mm ——
        /// 这是「手指弯曲后指尖仍落在键上」的几何前提；手太低会让手指必须向后反折。
        /// </summary>
        public float HandHeight = 0.799f;

        /// <summary>
        /// 腕部相对键根的深度偏移（米）。取 0.232 使掌指关节位于键面按压点<b>后方约 55 mm</b>（靠演奏者一侧）：
        /// 指尖因此是「向前下方」落到键上，而不是向后缩回掌心。
        /// 旧值 0.12 会把指根放到按压点<b>前方</b>，导致指尖必须反折、手型呈爪状。
        /// </summary>
        public float HandDepth = 0.232f;

        [Header("移动（过渡动作与自适应速度）")]
        public float MoveSpeed = 12f;        // 基准移动平滑速度
        public float MinSpeedScale = 0.55f;  // 长间隔（稀疏片段）速度倍率下限
        public float MaxSpeedScale = 1.8f;   // 短间隔（密集片段）速度倍率上限
        public float SlowGapSeconds = 0.6f;  // 达到速度下限对应的音符平均间隔
        public float FastGapSeconds = 0.12f; // 达到速度上限对应的音符平均间隔
        public float GlideThreshold = 0.07f; // 水平位移超过该值视为换位，触发抬升过渡（米）
        public float GlideLift = 0.055f;     // 换位过程中的额外抬升高度（米）
        public float MaxStepPerFrame = 0.035f; // 单帧最大位移（米）：硬约束，防瞬移与动作失控

        [Header("预置（即将到来的音符，方案 5.3 第二阶段 / 4.2.3 轻量预测）")]
        public float HandLeadSpeedScale = 0.7f; // 预置移动的速度倍率（比按键时略慢，避免抢拍感）

        [Header("指尖贴键（手掌几何反馈，方案 5.3 第二阶段 / 审查 1.2B 第二步）")]
        /// <summary>手掌修正的最大幅度（米）：手腕朝「消除指尖平均误差」的方向移动，幅度有界。</summary>
        public float MaxPalmCorrection = 0.11f;
        /// <summary>手掌修正的跟随速度（指数阻尼）；越大越快收敛，越大也越容易抖。</summary>
        public float PalmCorrectionSpeed = 14f;
        /// <summary>手掌修正增益（0..1）：把平均误差的多少比例用于平移手掌。</summary>
        [Range(0f, 1f)] public float PalmCorrectionGain = 0.8f;
        public float TargetMaxSpeed = 4.0f;       // 大跳变时的目标变化速率上限（米/秒）；0 = 不限速
        public float TargetJumpThreshold = 0.20f; // 目标变化小于该值（同手位内旋律跳进）时直接精确对齐

        [Header("姿态校验（防穿模）")]
        public bool EnablePoseClamp = true;

        [Header("手腕姿态（附录 A.4.2）")]
        /// <summary>是否让手腕按角色配置做受限俯仰 / 滚转（只影响姿态，不改变单帧位移上限）。</summary>
        public bool EnableWristOrientation = true;

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
        public float FingertipErrorTolerance = 0.035f;// 指尖到目标键面的「命中」容差（米）
        public float HoverHeight = 0.045f;            // 指尖高于键面超过该值视为「离键悬空」（米）
        public float ThumbOrientationMaxDegrees = 75f; // 拇指轴与掌心前向的最大夹角（超过记为拇指方向异常）
        public float ChordFormationSeconds = 0.10f;   // 和弦「先成手型再下压」的短过渡时长（秒）
        public float ChordApproachHeight = 0.022f;    // 和弦成形阶段指尖相对键面的额外高度（米）
        public float LargeMotionDistance = 0.10f;     // 「跨手位移」分组判定的目标位移阈值（米）
        public float FastScaleGapSeconds = 0.18f;     // 「快速音阶」分组判定的平均音符间隔上限（秒）
        public float SpeedAnomalyStepDelta = 0.020f;  // 连续帧位移变化超过该值记为速度突变（米）

        private const int MaxRecordedIssues = 8;

        private AvatarManager _avatar;
        private Piano.PianoKeyMap _keyMap;
        private readonly HashSet<int> _pressed = new();

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

        // 手掌修正（指尖贴键的几何反馈量，仅作用于 XZ）
        private Vector3 _leftPalmCorrection;
        private Vector3 _rightPalmCorrection;

        // 双手共用的「手指 ↔ 音高」分配（方案 5.3 第二阶段）：与 FingerPoseController 是同一个实例
        private FingerAssignmentService _assignment;

        // ---- 键面目标点（初始化时预计算，Update 路径零分配复用）----
        private readonly Dictionary<int, KeyTouchPoint> _keyTouches = new();

        // ---- 指尖目标（每手 5 个，预分配） + 和弦成形过渡进度 ----
        private readonly Vector3[] _leftFingerTargets = new Vector3[5];
        private readonly Vector3[] _rightFingerTargets = new Vector3[5];
        private readonly bool[] _leftFingerHasTarget = new bool[5];
        private readonly bool[] _rightFingerHasTarget = new bool[5];
        private readonly float[] _leftFingerBlend = new float[5];
        private readonly float[] _rightFingerBlend = new float[5];
        private readonly List<int> _pitchScratch = new();
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
            /// <summary>按压点世界坐标（落在键面合理按压区域，而不是键位中心的任意高度）。</summary>
            public readonly Vector3 Press;

            /// <summary>是否为黑键（用于分组指标与独立目标高度）。</summary>
            public readonly bool Black;

            /// <summary>该键键面世界高度（黑键 = 键根 y + BlackHeight，白键 = 键根 y + WhiteHeight）。</summary>
            public readonly float SurfaceY;

            public KeyTouchPoint(Vector3 press, bool black, float surfaceY)
            {
                Press = press;
                Black = black;
                SurfaceY = surfaceY;
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

        /// <summary>取某音高的可复用键面按压目标点（初始化时预计算；音高越界或未建键时返回 false）。</summary>
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

        public void Initialize(AvatarManager avatar, Piano.PianoKeyMap keyMap)
        {
            _avatar = avatar;
            _keyMap = keyMap;
            BuildLimits();
            BuildKeyTouches();
        }

        /// <summary>由控制器每帧传入统一时间轴时间（用于自适应速度与指标采样）。</summary>
        public void Tick(float currentTime) => _currentTime = currentTime;

        public void OnNoteOn(MIDI.MidiNoteEvent e)
        {
            if (Piano.PianoKeyMap.IsInRange(e.Pitch))
            {
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
            _pressed.Remove(e.Pitch);
            _pressedDurations.Remove(e.Pitch);
        }

        /// <summary>
        /// 由会话层每帧推入「该手即将到来、此刻尚未按下的音高」（无则 -1）。
        /// 手会在音符到来前先移向对应琴键（真实演奏的预置动作），
        /// 使音符开始时手腕已经在位；预置帧不参与命中率统计（只有真正按住音符的帧才算），
        /// 因此该优化不会美化指标，只减少「换位途中错过音符」。
        /// </summary>
        public void SetUpcomingPitches(int leftPitch, int rightPitch)
        {
            _upcomingLeftPitch = leftPitch;
            _upcomingRightPitch = rightPitch;
        }

        public void ReleaseAll()
        {
            _pressed.Clear();
            _pressedDurations.Clear();
            _lastNoteTime = float.NaN;   // 复位间隔统计，避免 Seek 后沿用旧节奏
            _avgNoteGap = 0.4f;
            _upcomingLeftPitch = -1;     // 复位预置目标，避免停止/切曲后手继续移向旧音符
            _upcomingRightPitch = -1;
            _leftPalmCorrection = Vector3.zero;   // 复位手掌修正，避免 Seek 后从旧修正量滑过去
            _rightPalmCorrection = Vector3.zero;
            _hasLeftSmoothed = false;    // 复位目标平滑，Seek 后重新对齐而非从旧目标滑过去（左右手各自复位）
            _hasRightSmoothed = false;
            _leftHadPrevTarget = false;  // 复位分组判定，避免 Seek 后的首个采样被误判为「跨手大位移」
            _rightHadPrevTarget = false;
            _hasPrevWrist = false;       // 复位过渡帧判定（否则首帧位移会被当成换位）
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

            if (EnableMetrics) SampleMetrics();
        }

        private void RefreshAssignment(bool leftHand)
        {
            CollectHandPitches(leftHand);
            _pitchScratch.Sort();
            Assignment.Refresh(leftHand, _pitchScratch, _currentTime, Time.frameCount);
        }

        /// <summary>
        /// 手掌几何反馈（让指尖真正落到键上）：把「分配到的各指尖到各自琴键按压点的平均误差」
        /// 作为手腕目标的修正量（只修正 XZ，高度交给手指弯曲，避免手腕被压到键面以下触发姿态钳制）。
        /// 修正量有界且按指数阻尼跟随，因此不会抖动，也不会突破单帧位移硬约束。
        /// </summary>
        private void UpdatePalmCorrection(bool leftHand)
        {
            IHandRig hand = leftHand ? _avatar.LeftHand : _avatar.RightHand;
            var targets = leftHand ? _leftFingerTargets : _rightFingerTargets;
            var has = leftHand ? _leftFingerHasTarget : _rightFingerHasTarget;

            Vector3 sum = Vector3.zero;
            int count = 0;

            if (hand != null && hand.Fingers != null)
            {
                int fingerCount = Mathf.Min(hand.Fingers.Count, 5);
                for (int f = 0; f < fingerCount; f++)
                {
                    if (!has[f]) continue;
                    sum += targets[f] - FingertipWorld(hand, f);
                    count++;
                }
            }

            Vector3 want = count > 0 ? (sum / count) * PalmCorrectionGain : Vector3.zero;
            want.y = 0f;                                        // 高度不由手掌平移承担
            want = Vector3.ClampMagnitude(want, MaxPalmCorrection);

            var current = leftHand ? _leftPalmCorrection : _rightPalmCorrection;
            float k = 1f - Mathf.Exp(-PalmCorrectionSpeed * Time.deltaTime);
            Vector3 next = Vector3.Lerp(current, want, k);

            if (leftHand) _leftPalmCorrection = next;
            else _rightPalmCorrection = next;
        }

        private void UpdateHand(bool leftHand)
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
                    // 【左右约定】左手在 +X（角色面向 -Z，其左手在世界 +X = 低音侧 = 画面左侧）
                    rawTarget = DefaultHandPos(leftHand ? 1f : -1f);
                }
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

            // 指尖贴键的手掌修正（方案 5.3 第二阶段）：把手腕朝「消灭指尖平均误差」的方向平移。
            // 只修正 XZ（高度由手指弯曲负责），并夹在键盘允许范围内，避免把手腕推出琴键区触发姿态钳制。
            rawTarget += leftHand ? _leftPalmCorrection : _rightPalmCorrection;
            if (_hasLimits)
            {
                rawTarget = new Vector3(
                    Mathf.Clamp(rawTarget.x, _limits.MinX, _limits.MaxX),
                    rawTarget.y,
                    Mathf.Clamp(rawTarget.z, _limits.MinZ, _limits.MaxZ));
            }

            var hand = leftHand ? _avatar.LeftHand : _avatar.RightHand;
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

            if (hand == null || shoulder == null || upperArm == null) return;

            var current = hand.Wrist.position;
            var target = ApplyGlideLift(current, LimitTargetRate(leftHand, rawTarget));
            float speedScale = CurrentSpeedScale() * (preselected ? HandLeadSpeedScale : 1f);
            var next = Vector3.Lerp(current, target, Time.deltaTime * MoveSpeed * speedScale);

            // 硬约束：单帧位移上限（对应方案阶段五「避免动作失控」，杜绝瞬移观感）
            var delta = next - current;
            float step = delta.magnitude;
            if (step > MaxStepPerFrame && step > 1e-6f)
                next = current + delta * (MaxStepPerFrame / step);

            if (EnablePoseClamp && _hasLimits)
            {
                string issue = HandPoseValidator.Validate(next, _limits);
                if (issue != null) RecordPoseIssue(issue);
                next = HandPoseValidator.Clamp(next, _limits);
            }

            hand.Wrist.position = next;
            StretchArm(shoulder, upperArm, foreArm, elbow, hand.Wrist, leftHand);

            if (EnableWristOrientation) ApplyWristOrientation(hand, leftHand, def);

            UpdateFingertipTargets(hand, leftHand, def);
        }

        /// <summary>
        /// 手腕姿态（附录 A.4.2「除位置外，增加受限的俯仰/滚转」）：
        /// ① 俯仰让掌心略朝键面，同时把腕部抬高使掌指关节基本留在原位（否则掌体会压进琴键）；
        /// ② 滚转在键盘两端产生轻微侧倾，幅度受限；
        /// ③ 只改姿态，<b>不影响</b> <see cref="MaxStepPerFrame"/> 的位置硬约束。
        /// </summary>
        private void ApplyWristOrientation(IHandRig hand, bool leftHand, AvatarDefinition def)
        {
            if (hand == null || hand.Wrist == null) return;

            float pitch = def != null ? def.Hand.WristPitch : DefaultWristPitch;
            float roll = 0f;

            if (_hasLimits)
            {
                float center = (_limits.MinX + _limits.MaxX) * 0.5f;
                float half = Mathf.Max(0.001f, (_limits.MaxX - _limits.MinX) * 0.5f);
                float offset = Mathf.Clamp((hand.Wrist.position.x - center) / half, -1f, 1f);
                roll = offset * MaxWristRollDegrees * (leftHand ? -1f : 1f);
            }

            // 说明：不做位置补偿 —— 腕部位置是每帧递推的状态量，叠加抬高会形成累积漂移。
            // 俯仰带来的指根位移由 FingerPoseController 的指尖 IK 每帧重新求解（以实际指根为基准），
            // 掌体与键面之间仍有约 18 mm 余量，不会穿模。
            var target = Quaternion.Euler(pitch, 0f, roll);
            float k = 1f - Mathf.Exp(-WristOrientationSpeed * Time.deltaTime);
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

            CollectHandPitches(leftHand);
            if (_pitchScratch.Count == 0) return;

            int fingerCount = Mathf.Min(hand.Fingers.Count, 5);
            if (fingerCount == 0) return;

            var targets = leftHand ? _leftFingerTargets : _rightFingerTargets;
            var has = leftHand ? _leftFingerHasTarget : _rightFingerHasTarget;

            // 指尖目标严格来自「手指 ↔ 音高」的共用分配结果：
            // 第 f 根手指的目标就是「分配给第 f 根手指的那个音高」的键面按压点。
            // 这样弯曲的手指、指向的目标、真正按下的琴键三者严格一一对应
            // （旧实现用「离键最近的指尖」自行配对，会与指法分配产生矛盾）。
            float reachAdjust = def != null ? def.Hand.FingertipReachAdjustment : 0f;

            for (int f = 0; f < fingerCount; f++)
            {
                int pitch = Assignment.GetPitch(leftHand, f);
                if (pitch < 0) continue;
                if (!_keyTouches.TryGetValue(pitch, out var touch)) continue;

                targets[f] = touch.Press;
                if (reachAdjust != 0f) targets[f].y += reachAdjust;
                has[f] = true;
            }

            // 归因统计（批次 2.1）：每个「按下的音」计入一次样本，按三类失败分别计数
            for (int i = 0; i < _pitchScratch.Count; i++)
            {
                int pitch = _pitchScratch[i];
                _fingertipTargetSamples++;
                if (Assignment.GetFinger(leftHand, pitch) < 0) _unassignedFingertipSamples++;
                if (!_keyTouches.TryGetValue(pitch, out var touch)) { _missingKeyTouchSamples++; continue; }
                if (!IsWithinPoseLimits(touch.Press)) _unreachableTargetSamples++;
            }

            // 兜底：极端情况下（提供器没能给出分配）仍保证「按下的音都有指尖指向」，
            // 否则会出现"键亮了但没有手指按"的空档。
            for (int i = 0; i < _pitchScratch.Count; i++)
            {
                int pitch = _pitchScratch[i];
                if (Assignment.GetFinger(leftHand, pitch) >= 0) continue;
                if (!_keyTouches.TryGetValue(pitch, out var touch)) continue;

                int free = -1;
                for (int f = 0; f < fingerCount; f++)
                {
                    if (!has[f]) { free = f; break; }
                }
                if (free < 0) break;
                targets[free] = touch.Press;
                if (reachAdjust != 0f) targets[free].y += reachAdjust;
                has[free] = true;
            }

            // 和弦（≥2 个音）成形过渡：从手型上方在「自适应后的成形时长」内下压到键面。
            // 批次 2.5：短音符（16 分、快速和弦）上固定 0.10 s 常走不完，起音瞬间仍悬空；
            // 因此把成形时长压到当前和弦最短音符时值的 40%（上限仍是 ChordFormationSeconds）。
            // 注意：blend 是跨帧累积量，因此这里只清「是否有目标」标志，不清 blend（否则过渡永远走不完）。
            bool chord = _pitchScratch.Count >= 2;
            var blend = leftHand ? _leftFingerBlend : _rightFingerBlend;
            float formation = ChordFormationSeconds;
            if (chord)
            {
                float shortest = float.MaxValue;
                for (int i = 0; i < _pitchScratch.Count; i++)
                {
                    if (_pressedDurations.TryGetValue(_pitchScratch[i], out float dur) && dur > 0f && dur < shortest)
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

                targets[f].y += ChordApproachHeight * (1f - blend[f]);
            }
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
                FingerPenetration = leftPenetration || rightPenetration
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
                sum += Vector3.Distance(tip, touch.Press);

                float surface = HandPoseValidator.SignedDistanceToSurface(tip.y, touch.SurfaceY);
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
        private void BuildKeyTouches()
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
                float surfaceY = key.position.y + height;

                // 键根在键后端：沿 +Z（向演奏者）偏移到按压区
                var press = new Vector3(key.position.x, surfaceY, key.position.z + length * forward);
                _keyTouches[pitch] = new KeyTouchPoint(press, black, surfaceY);
            }
        }

        private void CollectHandPitches(bool leftHand)
        {
            _pitchScratch.Clear();
            foreach (var pitch in _pressed)
            {
                if (MIDI.PianoNoteFilter.IsLeftHand(pitch) == leftHand) _pitchScratch.Add(pitch);
            }
        }

        private int? FindHandTarget(bool leftHand)
        {
            int? best = null;
            foreach (var pitch in _pressed)
            {
                bool isLeft = pitch < MIDI.PianoNoteFilter.SplitPitch;
                if (isLeft != leftHand) continue;
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

        /// <summary>无目标时的手位（<paramref name="side"/>：左手 +1 = 世界 +X，右手 −1）。</summary>
        private Vector3 DefaultHandPos(float side) => new Vector3(side * 0.30f, HandHeight, 0.10f);

        /// <summary>
        /// 两段解析 IK：把「肩 → 腕」分解为「上臂 + 前臂」，肘位置由余弦定理求出。
        /// <para>
        /// 设 d = |SW|、L1/L2 为上臂/前臂长、u = SW 的单位向量，则肘在 SW 上的投影
        /// a = (L1² − L2² + d²) / 2d、垂距 h = √(L1² − a²)，肘 E = S + u·a + v·h。
        /// v 由 hint（身体外侧 + 下方）正交化得到，决定肘朝哪边弯 —— 坐姿弹奏时
        /// 大臂自然下垂、肘略向后外，前臂前伸落到键盘上。
        /// </para>
        /// <para>
        /// 软拉伸：当 d 超过 L1+L2（大跨度换位）时按比例放大两段长度，保证仍能触键，
        /// 不引入「够不到」的硬失败，避免破坏既有动作断言。
        /// </para>
        /// </summary>
        private static void StretchArm(Transform shoulder, Transform upper, Transform fore, Transform elbow,
            Transform wrist, bool leftHand)
        {
            if (shoulder == null || upper == null || wrist == null) return;

            Vector3 S = shoulder.position;
            Vector3 W = wrist.position;
            Vector3 sw = W - S;
            float d = sw.magnitude;
            if (d < 1e-5f) return;

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

            // 世界坐标下角色左手在 +X（整体绕 Y 旋转 180°）；肘朝身体外侧、略向下。
            float sideSign = leftHand ? 1f : -1f;
            Vector3 hint = new Vector3(sideSign * 0.85f, -0.55f, 0.10f);
            Vector3 v = hint - u * Vector3.Dot(hint, u);
            if (v.sqrMagnitude < 1e-6f) v = Vector3.Cross(u, Vector3.forward);
            v.Normalize();

            Vector3 E = S + u * a + v * h;

            PlaceSegment(upper, S, E);
            if (fore != null) PlaceSegment(fore, E, W);
            if (elbow != null) elbow.position = E;
        }

        /// <summary>把一根胶囊摆到 a→b：中点定位、up 对齐方向、按长度设置 y 缩放（胶囊原始高度为 2）。</summary>
        private static void PlaceSegment(Transform seg, Vector3 a, Vector3 b)
        {
            Vector3 delta = b - a;
            float len = delta.magnitude;
            if (len < 1e-5f) return;
            seg.position = a + delta * 0.5f;
            seg.up = delta / len;
            var sc = seg.localScale;
            seg.localScale = new Vector3(sc.x, len * 0.5f, sc.z);
        }
    }
}
