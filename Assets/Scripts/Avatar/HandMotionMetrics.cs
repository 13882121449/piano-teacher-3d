using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>一帧的手部采样输入。</summary>
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
    }

    /// <summary>
    /// 手部动作质量报告（对应方案阶段五验收「评估动作连续性、目标琴键命中率、左右手协调」）。
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

        public string ToLogString()
        {
            return $"命中率 {HitRate * 100f:0.#}%（{HitSamples}/{TargetSamples}） · " +
                   $"最大帧位移 {MaxStepDistance * 1000f:0.#} mm · " +
                   $"平滑度 {Smoothness:0.00} · 双手协调 {CoordinationRatio * 100f:0.#}% · 采样 {Samples}";
        }
    }

    /// <summary>
    /// 手部动作质量累加器：逐帧喂入左右手腕与目标位置，最后 <see cref="Build"/> 出报告。
    /// <para>
    /// 无场景依赖（仅使用 <see cref="Vector3"/> 数学类型），可在 EditMode 单元测试中直接构造序列验证。
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
            }

            if (s.LeftHasTarget)
            {
                _targetSamples++;
                if (HorizontalDistance(s.LeftWrist, s.LeftTarget) <= s.HitRadius) _hitSamples++;
            }
            if (s.RightHasTarget)
            {
                _targetSamples++;
                if (HorizontalDistance(s.RightWrist, s.RightTarget) <= s.HitRadius) _hitSamples++;
            }

            _prevLeft = s.LeftWrist;
            _prevRight = s.RightWrist;
            _hasPrev = true;
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
                RightPathLength = _rightPath
            };

            report.HitRate = _targetSamples > 0 ? _hitSamples / (float)_targetSamples : 1f;

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
        }

        /// <summary>
        /// 水平（XZ 平面）距离：「目标琴键命中」关注手腕是否对准该键的横向位置，
        /// 与手腕高度无关（换位抬升会临时抬高手腕，不应因此判为未命中）。
        /// </summary>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
            => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
