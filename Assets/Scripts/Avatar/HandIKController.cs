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
    /// 左右手以 C4（pitch=60）分界；首版为基础目标移动，不承诺专业指法准确性。
    /// </summary>
    public class HandIKController : MonoBehaviour
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
        public float TargetMaxSpeed = 4.0f;       // 大跳变时的目标变化速率上限（米/秒）；0 = 不限速
        public float TargetJumpThreshold = 0.20f; // 目标变化小于该值（同手位内旋律跳进）时直接精确对齐

        [Header("姿态校验（防穿模）")]
        public bool EnablePoseClamp = true;

        [Header("动作质量指标")]
        public bool EnableMetrics = true;
        public float HitRadius = 0.09f;      // 「到达目标键」判定半径（米）
        public float MovingThreshold = 0.0006f; // 「正在移动」判定单帧位移（米）

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
        private readonly List<string> _poseIssues = new();

        private readonly HandMotionAccumulator _metrics = new();
        private Vector3 _leftTargetPos, _rightTargetPos;
        private bool _leftHasTarget, _rightHasTarget;
        private Vector3 _leftSmoothedTarget, _rightSmoothedTarget;
        private bool _hasSmoothedTarget;

        /// <summary>动作质量累加器（Session / 测试读取）。</summary>
        public HandMotionAccumulator Metrics => _metrics;

        /// <summary>累计检测到并已钳制的异常姿态次数（方案阶段五「避免动作失控或穿模」）。</summary>
        public int PoseIssueCount => _poseIssueCount;

        /// <summary>最近若干条异常姿态描述（用于诊断，最多保留 8 条）。</summary>
        public IReadOnlyList<string> PoseIssues => _poseIssues;

        /// <summary>左手当前平滑后的目标位置（供测试与调试；未经平滑时为原始目标）。</summary>
        public Vector3 LeftSmoothedTarget => _leftSmoothedTarget;

        /// <summary>右手当前平滑后的目标位置（供测试与调试；未经平滑时为原始目标）。</summary>
        public Vector3 RightSmoothedTarget => _rightSmoothedTarget;

        /// <summary>当前使用的姿态允许范围（钢琴几何就绪后有效）。</summary>
        public bool TryGetPoseLimits(out HandPoseLimits limits)
        {
            limits = _limits;
            return _hasLimits;
        }

        public void Initialize(AvatarManager avatar, Piano.PianoKeyMap keyMap)
        {
            _avatar = avatar;
            _keyMap = keyMap;
            BuildLimits();
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

        public void ReleaseAll()
        {
            _pressed.Clear();
            _lastNoteTime = float.NaN;   // 复位间隔统计，避免 Seek 后沿用旧节奏
            _avgNoteGap = 0.4f;
            _hasSmoothedTarget = false;  // 复位目标平滑，Seek 后重新对齐而非从旧目标滑过去
        }

        /// <summary>重置动作质量指标与姿态告警记录（换曲或重新开始测量时调用）。</summary>
        public void ResetMetrics()
        {
            _metrics.Reset();
            _poseIssueCount = 0;
            _poseIssues.Clear();
        }

        /// <summary>生成动作质量报告（方案阶段五验收量化）。</summary>
        public HandMotionReport BuildMotionReport() => _metrics.Build();

        private void Update()
        {
            if (_avatar == null || _keyMap == null) return;

            UpdateHand(leftHand: true);
            UpdateHand(leftHand: false);

            if (EnableMetrics) SampleMetrics();
        }

        private void UpdateHand(bool leftHand)
        {
            int? targetPitch = FindHandTarget(leftHand);
            bool hasTarget = targetPitch.HasValue && _keyMap.TryGetKey(targetPitch.Value, out _);
            Vector3 rawTarget = hasTarget && _keyMap.TryGetKey(targetPitch.Value, out var key)
                ? KeyToHandPos(key)
                : DefaultHandPos(leftHand ? -1f : 1f);

            var hand = leftHand ? _avatar.LeftHand : _avatar.RightHand;
            var shoulder = leftHand ? _avatar.LeftShoulder : _avatar.RightShoulder;
            var arm = leftHand ? _avatar.LeftArm : _avatar.RightArm;

            if (leftHand)
            {
                _leftTargetPos = rawTarget;
                _leftHasTarget = hasTarget;
            }
            else
            {
                _rightTargetPos = rawTarget;
                _rightHasTarget = hasTarget;
            }

            if (hand == null || shoulder == null || arm == null) return;

            var current = hand.Wrist.position;
            var target = ApplyGlideLift(current, LimitTargetRate(leftHand, rawTarget));
            var next = Vector3.Lerp(current, target, Time.deltaTime * MoveSpeed * CurrentSpeedScale());

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
        }

        /// <summary>
        /// 分级限制目标位置变化：
        /// ① 小变化（同一手位内的音程移动，≤ <see cref="TargetJumpThreshold"/>）**直接精确对齐**
        ///    —— 保证「目标琴键命中率」不被平滑拖累；
        /// ② 大跳变（换手位）才以 <see cref="TargetMaxSpeed"/> 限速过渡，抑制单帧抖跳。
        /// </summary>
        private Vector3 LimitTargetRate(bool leftHand, Vector3 rawTarget)
        {
            if (!_hasSmoothedTarget)
            {
                // 首次采样直接对齐，避免从原点插值飞入
                _leftSmoothedTarget = _rightSmoothedTarget = rawTarget;
                _hasSmoothedTarget = true;
                return rawTarget;
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

        private void SampleMetrics()
        {
            var left = _avatar.LeftHand;
            var right = _avatar.RightHand;
            if (left == null || right == null) return;

            var sample = new HandMotionSample
            {
                LeftWrist = left.Wrist.position,
                RightWrist = right.Wrist.position,
                LeftTarget = _leftTargetPos,
                RightTarget = _rightTargetPos,
                LeftHasTarget = _leftHasTarget,
                RightHasTarget = _rightHasTarget,
                HitRadius = HitRadius,
                MovingThreshold = MovingThreshold
            };
            _metrics.AddSample(sample);
        }

        private void RecordPoseIssue(string issue)
        {
            _poseIssueCount++;
            if (_poseIssues.Count < MaxRecordedIssues) _poseIssues.Add(issue);
        }

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
