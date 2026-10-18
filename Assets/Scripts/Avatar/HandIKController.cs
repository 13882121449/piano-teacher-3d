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
        public float HandHeight = 0.768f; // 手在键面上方（键面约 0.742m，按下时指尖落到键面）
        public float HandDepth = 0.12f;   // 相对键根（世界 z≈-0.075）的深度偏移

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

        [Header("动作质量指标")]
        public bool EnableMetrics = true;
        public float HitRadius = 0.09f;      // 「到达目标键」判定半径（米）
        public float MovingThreshold = 0.0006f; // 「正在移动」判定单帧位移（米）

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
            if (Piano.PianoKeyMap.IsInRange(e.Pitch)) _pressed.Add(e.Pitch);

            if (!float.IsNaN(_lastNoteTime))
            {
                float gap = Mathf.Clamp(_currentTime - _lastNoteTime, 0.005f, 5f);
                _avgNoteGap = Mathf.Lerp(_avgNoteGap, gap, 0.25f);
            }
            _lastNoteTime = _currentTime;
        }

        public void OnNoteOff(MIDI.MidiNoteEvent e) => _pressed.Remove(e.Pitch);

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
                rawTarget = new Vector3(rawTarget.x, rawTarget.y + def.HandHeightOffset, rawTarget.z + def.HandDepthOffset);

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
            var arm = leftHand ? _avatar.LeftArm : _avatar.RightArm;

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

            if (hand == null || shoulder == null || arm == null) return;

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
            StretchArm(shoulder, arm, hand.Wrist);

            UpdateFingertipTargets(hand, leftHand);
        }

        // ==================== 指尖目标（方案 5.3 第二阶段 / 审查 1.2B 第二步） ====================

        /// <summary>
        /// 指尖目标求解：先把每个按下音高的键面按压点分配给「离它最近的指尖」（可复用目标点），
        /// 再按和弦成形进度把目标从手型上方过渡到键面（避免所有指尖在同一帧突然到位）。
        /// 手腕/掌心的移动规则完全不受影响（<see cref="UpdateHand"/> 已处理）。
        /// </summary>
        private void UpdateFingertipTargets(IHandRig hand, bool leftHand)
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
            for (int f = 0; f < fingerCount; f++)
            {
                int pitch = Assignment.GetPitch(leftHand, f);
                if (pitch < 0) continue;
                if (!_keyTouches.TryGetValue(pitch, out var touch)) continue;

                targets[f] = touch.Press;
                has[f] = true;
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
                has[free] = true;
            }

            // 和弦（≥2 个音）成形过渡：从手型上方在 ChordFormationSeconds 内下压到键面。
            // 注意：blend 是跨帧累积量，因此这里只清「是否有目标」标志，不清 blend（否则过渡永远走不完）。
            bool chord = _pitchScratch.Count >= 2;
            var blend = leftHand ? _leftFingerBlend : _rightFingerBlend;
            float rate = ChordFormationSeconds > 1e-4f ? Time.deltaTime / ChordFormationSeconds : 1f;
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

            bool crossed = HandPoseValidator.AreHandsCrossed(left.Wrist.position, right.Wrist.position);
            if (crossed) _crossedHandWarningCount++;

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
                LargeMotionFrame = _leftLargeMotion >= LargeMotionDistance || _rightLargeMotion >= LargeMotionDistance
            };
            _metrics.AddSample(sample);

            _leftLargeMotion = 0f;
            _rightLargeMotion = 0f;
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

        private static void StretchArm(Transform shoulder, Transform arm, Transform wrist)
        {
            var delta = wrist.position - shoulder.position;
            if (delta.sqrMagnitude < 1e-6f) return;
            arm.position = shoulder.position + delta * 0.5f;
            arm.up = delta.normalized;
            var s = arm.localScale;
            arm.localScale = new Vector3(s.x, delta.magnitude * 0.5f, s.z);
        }
    }
}
