using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>一帧的手部采样输入。所有后加字段都有默认值，既有构造方式（对象初始化器）继续可用。</summary>
    public struct HandMotionSample
    {
        public Vector3 LeftWrist;
        public Vector3 RightWrist;

        public Vector3 LeftTarget;
        public Vector3 RightTarget;

        public bool LeftHasTarget;
        public bool RightHasTarget;

        /// <summary>判定「已到达目标键」的距离阈值（米）。</summary>
        public float HitRadius;

        /// <summary>判定「该手正在移动」的单帧位移阈值（米）。</summary>
        public float MovingThreshold;

        // ==================== 审查 1.2C 扩充字段（默认值 = 该维度不参与统计） ====================
        // 注意：C# 9 的 struct 不能有实例字段初始化器，因此「是否有样本」必须用显式标志表示，
        // 不能依赖「0 = 无样本」这类隐式约定（否则既有测试直接 new 出来的采样会被误计入指尖统计）。

        /// <summary>该帧是否包含指尖误差样本；false 时忽略下面两个误差字段。</summary>
        public bool HasFingertipErrors;

        /// <summary>左手「指尖到目标键面」的最近点误差（米）。</summary>
        public float LeftFingertipError;

        /// <summary>右手「指尖到目标键面」的最近点误差（米）。</summary>
        public float RightFingertipError;

        /// <summary>指尖命中容差（米）：误差不超过该值记为「指尖命中」；0 = 不统计指尖命中率。</summary>
        public float FingertipTolerance;

        /// <summary>连续帧位移变化（速度突变）判定阈值（米）；0 = 不统计。</summary>
        public float SpeedAnomalyThreshold;

        /// <summary>该帧左右手是否交叉（左手在右手右侧）。</summary>
        public bool HandsCrossed;

        /// <summary>该帧是否检测到拇指方向异常（拇指轴与掌心前向夹角过大）。</summary>
        public bool ThumbOrientationIssue;

        /// <summary>该帧是否检测到手掌穿模（掌心低于允许范围）。</summary>
        public bool PalmPenetration;

        /// <summary>该帧是否检测到离键悬空（指尖高于目标键面超过阈值）。</summary>
        public bool Hovering;

        /// <summary>该帧目标音是否包含黑键（黑键分组）。</summary>
        public bool BlackKeyFrame;

        /// <summary>该帧是否存在和弦（同一手 ≥ 2 个同时按下的音）。</summary>
        public bool ChordFrame;

        /// <summary>该帧是否属于快速音阶（平均音符间隔低于阈值）。</summary>
        public bool FastScaleFrame;

        /// <summary>该帧目标位移是否超过跨手位移阈值。</summary>
        public bool LargeMotionFrame;
    }

    /// <summary>
    /// 手部动作质量报告（对应方案阶段五验收「评估动作连续性、目标琴键命中率、左右手协调」，
    /// 并按审查 1.2C「不要只看聚合数字」扩充分项、分布、分组与视觉抽查标记）。
    /// <para>
    /// <b>向后兼容</b>：既有属性名与语义保持不变（<c>UI.LessonControlsUI.BuildMotionText</c> /
    /// <c>RefreshMotion</c> 直接读取 <c>HitRate / Smoothness / MaxStepDistance / CoordinationRatio / Samples</c>），
    /// 本轮只<b>追加</b>字段；无样本的分组指标返回 1（与 <see cref="HitRate"/> 的既有约定一致），
    /// 同时暴露对应样本数，UI 可据此显示「样本 0」而不是伪造 100%。
    /// </para>
    /// </summary>
    public sealed class HandMotionReport
    {
        public int Samples { get; internal set; }
        public int TargetSamples { get; internal set; }
        public int HitSamples { get; internal set; }

        /// <summary>目标琴键命中率（手腕落在目标键可达范围内），0..1；无目标帧按 1 计。</summary>
        public float HitRate { get; internal set; } = 1f;

        /// <summary>最大单帧位移（米）——用于发现瞬移。</summary>
        public float MaxStepDistance { get; internal set; }

        public float LeftPathLength { get; internal set; }
        public float RightPathLength { get; internal set; }

        /// <summary>平滑度 = 净位移 / 路径长度（1 = 完全直线，越低越绕）。</summary>
        public float Smoothness { get; internal set; } = 1f;

        /// <summary>双手同时移动的采样占比（左右手协调度）。</summary>
        public float CoordinationRatio { get; internal set; }

        // ==================== 左右手分项命中率（审查 1.2C 第 1 项） ====================

        public int LeftTargetSamples { get; internal set; }
        public int RightTargetSamples { get; internal set; }

        /// <summary>左手目标命中率 0..1；左手无目标帧时为 1（与总命中率口径一致）。</summary>
        public float LeftHitRate { get; internal set; } = 1f;

        /// <summary>右手目标命中率 0..1；右手无目标帧时为 1。</summary>
        public float RightHitRate { get; internal set; } = 1f;

        // ==================== 指尖到目标键面的距离分布（审查 1.2C 第 2 项） ====================

        /// <summary>指尖到目标键面的平均误差（米）；无样本为 0。</summary>
        public float FingertipMeanError { get; internal set; }

        /// <summary>指尖到目标键面的最大误差（米）；无样本为 0。</summary>
        public float FingertipMaxError { get; internal set; }

        /// <summary>参与统计的指尖样本数（每手每帧最多 1 个）。</summary>
        public int FingertipSamples { get; internal set; }

        /// <summary>指尖命中样本数（误差 ≤ 容差）。</summary>
        public int FingertipHitSamples { get; internal set; }

        /// <summary>指尖命中率 0..1；无样本为 1。</summary>
        public float FingertipHitRate { get; internal set; } = 1f;

        // ==================== 速度变化异常（审查 1.2C 第 3 项） ====================

        /// <summary>连续帧位移变化超过阈值的次数（加速度异常，用于发现抖动/顿挫）。</summary>
        public int SpeedAnomalyCount { get; internal set; }

        // ==================== 分组表现（审查 1.2C 第 4 项） ====================

        public int BlackKeyTargetSamples { get; internal set; }
        public int ChordTargetSamples { get; internal set; }
        public int FastScaleTargetSamples { get; internal set; }
        public int LargeMotionTargetSamples { get; internal set; }

        /// <summary>黑键目标命中率；无样本为 1（配合样本数显示「样本 0」）。</summary>
        public float BlackKeyHitRate { get; internal set; } = 1f;

        /// <summary>和弦目标命中率；无样本为 1。</summary>
        public float ChordHitRate { get; internal set; } = 1f;

        /// <summary>快速音阶目标命中率；无样本为 1。</summary>
        public float FastScaleHitRate { get; internal set; } = 1f;

        /// <summary>跨手位移目标命中率；无样本为 1。</summary>
        public float LargeMotionHitRate { get; internal set; } = 1f;

        // ==================== 视觉抽查标记（审查 1.2C 第 5 项） ====================

        /// <summary>手指交叉的帧数。</summary>
        public int CrossedHandFrames { get; internal set; }

        /// <summary>拇指方向异常的次数。</summary>
        public int ThumbOrientationIssues { get; internal set; }

        /// <summary>手掌穿模的帧数。</summary>
        public int PalmPenetrationFrames { get; internal set; }

        /// <summary>离键悬空的帧数（按键时指尖明显高于目标键面）。</summary>
        public int HoveringFrames { get; internal set; }

        /// <summary>诊断日志：保留既有字段与顺序，本轮仅在末尾追加新维度。</summary>
        public string ToLogString()
        {
            return $"命中率 {HitRate * 100f:0.#}%（{HitSamples}/{TargetSamples}） · " +
                   $"最大帧位移 {MaxStepDistance * 1000f:0.#} mm · " +
                   $"平滑度 {Smoothness:0.00} · 双手协调 {CoordinationRatio * 100f:0.#}% · 采样 {Samples}" +
                   $"｜左 {LeftHitRate * 100f:0.#}%（{LeftTargetSamples}） 右 {RightHitRate * 100f:0.#}%（{RightTargetSamples}）" +
                   $" · 指尖误差 均值 {FingertipMeanError * 1000f:0.#} mm / 最大 {FingertipMaxError * 1000f:0.#} mm（{FingertipSamples}）" +
                   $"｜分组 黑键 {GroupText(BlackKeyHitRate, BlackKeyTargetSamples)} 和弦 {GroupText(ChordHitRate, ChordTargetSamples)}" +
                   $" 快速音阶 {GroupText(FastScaleHitRate, FastScaleTargetSamples)} 跨手 {GroupText(LargeMotionHitRate, LargeMotionTargetSamples)}" +
                   $"｜抽查 交叉 {CrossedHandFrames} 拇指 {ThumbOrientationIssues} 穿模 {PalmPenetrationFrames} 悬空 {HoveringFrames}" +
                   $" · 速度异常 {SpeedAnomalyCount}";
        }

        private static string GroupText(float rate, int samples)
            => samples > 0 ? $"{rate * 100f:0.#}%({samples})" : "样本 0";
    }

    /// <summary>
    /// 手部动作质量累加器：逐帧喂入左右手腕与目标位置，最后 <see cref="Build"/> 出报告。
    /// <para>
    /// 无场景依赖（仅使用 <see cref="Vector3"/> 数学类型），可在 EditMode 单元测试中直接构造序列验证。
    /// 新增统计全部由采样字段驱动（阈值 0 / 误差 &lt;0 即不参与），因此既有测试的采样构造方式无需改动。
    /// </para>
    /// </summary>
    public sealed class HandMotionAccumulator
    {
        private int _samples;
        private int _targetSamples;
        private int _hitSamples;
        private int _bothMovingSamples;
        private float _maxStep;
        private float _leftPath;
        private float _rightPath;

        private Vector3 _firstLeft, _firstRight;
        private Vector3 _prevLeft, _prevRight;
        private bool _hasPrev;

        // ---- 分项命中率 ----
        private int _leftTargetSamples, _leftHitSamples;
        private int _rightTargetSamples, _rightHitSamples;

        // ---- 指尖误差分布 ----
        private float _fingertipSum, _fingertipMax;
        private int _fingertipSamples, _fingertipHitSamples;

        // ---- 速度变化异常 ----
        private float _prevLeftStep, _prevRightStep;
        private bool _hasPrevStep;
        private int _speedAnomalies;

        // ---- 分组 ----
        private int _blackTarget, _blackHit;
        private int _chordTarget, _chordHit;
        private int _fastTarget, _fastHit;
        private int _largeTarget, _largeHit;

        // ---- 视觉抽查 ----
        private int _crossedFrames, _thumbIssues, _palmPenetrationFrames, _hoveringFrames;

        public int Samples => _samples;

        public void AddSample(in HandMotionSample s)
        {
            _samples++;
            if (!_hasPrev)
            {
                _firstLeft = s.LeftWrist;
                _firstRight = s.RightWrist;
            }
            else
            {
                float dl = Vector3.Distance(_prevLeft, s.LeftWrist);
                float dr = Vector3.Distance(_prevRight, s.RightWrist);

                if (dl > _maxStep) _maxStep = dl;
                if (dr > _maxStep) _maxStep = dr;

                _leftPath += dl;
                _rightPath += dr;

                if (dl > s.MovingThreshold && dr > s.MovingThreshold) _bothMovingSamples++;

                // 速度变化异常：单帧位移相对上一帧的变化超过阈值（加速度突变 / 顿挫）
                if (s.SpeedAnomalyThreshold > 0f)
                {
                    if (_hasPrevStep)
                    {
                        if (Mathf.Abs(dl - _prevLeftStep) > s.SpeedAnomalyThreshold) _speedAnomalies++;
                        if (Mathf.Abs(dr - _prevRightStep) > s.SpeedAnomalyThreshold) _speedAnomalies++;
                    }
                    _prevLeftStep = dl;
                    _prevRightStep = dr;
                    _hasPrevStep = true;
                }
            }

            if (s.LeftHasTarget)
            {
                _targetSamples++;
                _leftTargetSamples++;
                bool hit = HorizontalDistance(s.LeftWrist, s.LeftTarget) <= s.HitRadius;
                if (hit)
                {
                    _hitSamples++;
                    _leftHitSamples++;
                }
                AccumulateGroups(s, hit);
            }
            if (s.RightHasTarget)
            {
                _targetSamples++;
                _rightTargetSamples++;
                bool hit = HorizontalDistance(s.RightWrist, s.RightTarget) <= s.HitRadius;
                if (hit)
                {
                    _hitSamples++;
                    _rightHitSamples++;
                }
                AccumulateGroups(s, hit);
            }

            AccumulateFingertip(s);
            AccumulateVisualChecks(s);

            _prevLeft = s.LeftWrist;
            _prevRight = s.RightWrist;
            _hasPrev = true;
        }

        private void AccumulateGroups(in HandMotionSample s, bool hit)
        {
            if (s.BlackKeyFrame) { _blackTarget++; if (hit) _blackHit++; }
            if (s.ChordFrame) { _chordTarget++; if (hit) _chordHit++; }
            if (s.FastScaleFrame) { _fastTarget++; if (hit) _fastHit++; }
            if (s.LargeMotionFrame) { _largeTarget++; if (hit) _largeHit++; }
        }

        private void AccumulateFingertip(in HandMotionSample s)
        {
            if (!s.HasFingertipErrors) return;
            AccumulateFingertipValue(s.LeftFingertipError, s.FingertipTolerance);
            AccumulateFingertipValue(s.RightFingertipError, s.FingertipTolerance);
        }

        private void AccumulateFingertipValue(float error, float tolerance)
        {
            if (error < 0f) return;
            _fingertipSamples++;
            _fingertipSum += error;
            if (error > _fingertipMax) _fingertipMax = error;
            if (tolerance > 0f && error <= tolerance) _fingertipHitSamples++;
        }

        private void AccumulateVisualChecks(in HandMotionSample s)
        {
            if (s.HandsCrossed) _crossedFrames++;
            if (s.ThumbOrientationIssue) _thumbIssues++;
            if (s.PalmPenetration) _palmPenetrationFrames++;
            if (s.Hovering) _hoveringFrames++;
        }

        public HandMotionReport Build()
        {
            var report = new HandMotionReport
            {
                Samples = _samples,
                TargetSamples = _targetSamples,
                HitSamples = _hitSamples,
                MaxStepDistance = _maxStep,
                LeftPathLength = _leftPath,
                RightPathLength = _rightPath,

                LeftTargetSamples = _leftTargetSamples,
                RightTargetSamples = _rightTargetSamples,
                FingertipMeanError = _fingertipSamples > 0 ? _fingertipSum / _fingertipSamples : 0f,
                FingertipMaxError = _fingertipMax,
                FingertipSamples = _fingertipSamples,
                FingertipHitSamples = _fingertipHitSamples,
                SpeedAnomalyCount = _speedAnomalies,

                BlackKeyTargetSamples = _blackTarget,
                ChordTargetSamples = _chordTarget,
                FastScaleTargetSamples = _fastTarget,
                LargeMotionTargetSamples = _largeTarget,

                CrossedHandFrames = _crossedFrames,
                ThumbOrientationIssues = _thumbIssues,
                PalmPenetrationFrames = _palmPenetrationFrames,
                HoveringFrames = _hoveringFrames
            };

            report.HitRate = _targetSamples > 0 ? _hitSamples / (float)_targetSamples : 1f;

            // 左右手分项：该手无目标帧时按 1 计（与总命中率口径一致，避免误报）
            report.LeftHitRate = _leftTargetSamples > 0 ? _leftHitSamples / (float)_leftTargetSamples : 1f;
            report.RightHitRate = _rightTargetSamples > 0 ? _rightHitSamples / (float)_rightTargetSamples : 1f;

            report.FingertipHitRate = _fingertipSamples > 0 ? _fingertipHitSamples / (float)_fingertipSamples : 1f;

            report.BlackKeyHitRate = _blackTarget > 0 ? _blackHit / (float)_blackTarget : 1f;
            report.ChordHitRate = _chordTarget > 0 ? _chordHit / (float)_chordTarget : 1f;
            report.FastScaleHitRate = _fastTarget > 0 ? _fastHit / (float)_fastTarget : 1f;
            report.LargeMotionHitRate = _largeTarget > 0 ? _largeHit / (float)_largeTarget : 1f;

            // 平滑度：净位移 / 路径长度（每手分别计算后取平均）
            float leftNet = _hasPrev ? Vector3.Distance(_firstLeft, _prevLeft) : 0f;
            float rightNet = _hasPrev ? Vector3.Distance(_firstRight, _prevRight) : 0f;
            float leftSmooth = _leftPath > 1e-4f ? Mathf.Clamp01(leftNet / _leftPath) : 1f;
            float rightSmooth = _rightPath > 1e-4f ? Mathf.Clamp01(rightNet / _rightPath) : 1f;
            report.Smoothness = (leftSmooth + rightSmooth) * 0.5f;

            report.CoordinationRatio = _samples > 1 ? _bothMovingSamples / (float)(_samples - 1) : 0f;
            return report;
        }

        public void Reset()
        {
            _samples = 0;
            _targetSamples = 0;
            _hitSamples = 0;
            _bothMovingSamples = 0;
            _maxStep = 0f;
            _leftPath = 0f;
            _rightPath = 0f;
            _hasPrev = false;

            _leftTargetSamples = _leftHitSamples = 0;
            _rightTargetSamples = _rightHitSamples = 0;

            _fingertipSum = 0f;
            _fingertipMax = 0f;
            _fingertipSamples = 0;
            _fingertipHitSamples = 0;

            _prevLeftStep = _prevRightStep = 0f;
            _hasPrevStep = false;
            _speedAnomalies = 0;

            _blackTarget = _blackHit = 0;
            _chordTarget = _chordHit = 0;
            _fastTarget = _fastHit = 0;
            _largeTarget = _largeHit = 0;

            _crossedFrames = _thumbIssues = _palmPenetrationFrames = _hoveringFrames = 0;
        }

        /// <summary>
        /// 水平（XZ 平面）距离：「目标琴键命中」关注手腕是否对准该键的横向位置，
        /// 与手腕高度无关（换位抬升会临时抬高手腕，不应因此判为未命中）。
        /// </summary>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
            => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
