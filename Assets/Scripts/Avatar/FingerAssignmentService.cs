using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 指尖目标来源：手指姿态控制器据此把某根手指「指向它该按的那个键」。
    /// 由 <see cref="HandIKController"/> 实现（它是唯一持有琴键按压点与手部几何的地方）。
    /// </summary>
    public interface IFingertipTargetSource
    {
        /// <summary>取某手某指当前的指尖目标世界坐标（无目标时返回 false）。索引 0 = 拇指。</summary>
        bool TryGetFingertipTarget(bool leftHand, int fingerIndex, out Vector3 target);
    }

    /// <summary>
    /// 双手共用的「手指 ↔ 音高」分配缓存（方案 5.3 第二阶段 / 审查 1.2B 第二步）。
    /// <para>
    /// <b>为什么必须共用一份</b>：改造前 <see cref="FingerPoseController"/> 按指法提供器决定「哪根手指弯下去」，
    /// 而 <see cref="HandIKController"/> 却用「离键最近的指尖」自行配对 —— 两套独立结果会互相矛盾，
    /// 于是出现「弯下去的手指并不是落在键上的手指」以及指尖平均误差接近 10 cm（手指根本没碰到键）。
    /// 现在两个控制器都读<b>同一份</b>分配结果，因此
    /// 「被分配的手指 == 被弯曲的手指 == 指尖指向该键的手指」，三者严格一一对应。
    /// </para>
    /// <para>
    /// <b>同一帧只算一次</b>：两个控制器都在自己 <c>Update</c> 里调用 <see cref="Refresh"/>（传 <c>Time.frameCount</c>），
    /// 谁先跑谁算，另一处直接复用，因此与脚本执行顺序无关，也不会产生 1 帧延迟的错配。
    /// </para>
    /// <para>纯逻辑、不依赖场景，可在 EditMode 单测（与 <c>HandPoseValidator</c> / <c>HandMotionMetrics</c> 同风格）。</para>
    /// </summary>
    public sealed class FingerAssignmentService
    {
        /// <summary>手指槽位数（与 <c>IHandRig.Fingers</c> 上限一致）。</summary>
        public const int MaxFingers = 8;

        // 左右手各持一份分配结果（必须隔离，否则后处理的手会清空前一只手的数据）
        private readonly int[] _leftFingerPitch = new int[MaxFingers];
        private readonly int[] _rightFingerPitch = new int[MaxFingers];

        private readonly List<int> _leftMelody = new List<int>();
        private readonly List<int> _rightMelody = new List<int>();

        private IFingeringProvider _provider;
        private int _leftFrame = -1;
        private int _rightFrame = -1;
        private int _leftSignature;
        private int _rightSignature;

        private float _leftConfidence = 1f;
        private float _rightConfidence = 1f;
        private bool _leftEstimated;
        private bool _rightEstimated;
        private string _leftReason = string.Empty;
        private string _rightReason = string.Empty;

        /// <summary>每手保留的最近旋律音数量（供提供器判断相邻音 / 重复音 / 拇指穿越）。</summary>
        public int MelodyHistoryLength = 8;

        /// <summary>
        /// 指法提供器。置为 null 时回退默认启发式提供器
        /// （AI / 人工指法只能作为可选增强，方案 10 / 取舍 3）。
        /// </summary>
        public IFingeringProvider Provider
        {
            get => _provider;
            set
            {
                _provider = value ?? new HeuristicFingeringProvider();
                Invalidate();
            }
        }

        /// <summary>当前提供器名称（供 UI / 诊断标注）。</summary>
        public string ProviderName => _provider != null ? _provider.Name : "(未设置)";

        /// <summary>把音符记进该手的旋律历史（由 <c>FingerPoseController.OnNoteOn</c> 调用）。</summary>
        public void PushMelody(bool leftHand, int pitch)
        {
            var melody = leftHand ? _leftMelody : _rightMelody;
            melody.Add(pitch);
            int limit = Mathf.Max(1, MelodyHistoryLength);
            while (melody.Count > limit) melody.RemoveAt(0);
        }

        /// <summary>
        /// 用该手当前按下的音高（升序）刷新分配；同一帧（<paramref name="frame"/> 相同）重复调用只算一次。
        /// </summary>
        public void Refresh(bool leftHand, IReadOnlyList<int> pitchesAscending, float time, int frame)
        {
            // 幂等判据 = 同一帧 + 音高集合签名相同。只比帧号在 EditMode（帧号恒为 0）下会误跳过，故同时比签名。
            int hash = ComputeSignature(pitchesAscending);
            if (leftHand)
            {
                if (_leftFrame == frame && _leftSignature == hash) return;
                _leftFrame = frame;
                _leftSignature = hash;
            }
            else
            {
                if (_rightFrame == frame && _rightSignature == hash) return;
                _rightFrame = frame;
                _rightSignature = hash;
            }

            var assign = leftHand ? _leftFingerPitch : _rightFingerPitch;
            ClearArray(assign);

            if (pitchesAscending == null || pitchesAscending.Count == 0)
            {
                SetResult(leftHand, 1f, false, string.Empty);
                return;
            }

            var provider = _provider ?? (_provider = new HeuristicFingeringProvider());
            var context = new FingeringContext(leftHand, pitchesAscending,
                leftHand ? _leftMelody : _rightMelody, time);
            var plan = provider.Assign(in context);

            if (plan?.FingerByPitch != null)
            {
                int n = Mathf.Min(plan.FingerByPitch.Length, pitchesAscending.Count);
                for (int i = 0; i < n; i++)
                {
                    int f = plan.FingerByPitch[i];
                    if (f < 0) continue;
                    f = Mathf.Clamp(f, 0, MaxFingers - 1);   // 超出手指数时复用槽位，绝不越界
                    assign[f] = pitchesAscending[i];
                }
            }

            SetResult(leftHand,
                plan != null ? plan.Confidence : 0f,
                plan == null || plan.IsEstimated,
                plan != null ? plan.Reason : "指法提供器返回空结果");
        }

        /// <summary>指定手指当前分配到的音高（-1 = 未分配）。</summary>
        public int GetPitch(bool leftHand, int fingerIndex)
        {
            if (fingerIndex < 0 || fingerIndex >= MaxFingers) return -1;
            return leftHand ? _leftFingerPitch[fingerIndex] : _rightFingerPitch[fingerIndex];
        }

        /// <summary>某音高被分配给了哪根手指（-1 = 未分配）。</summary>
        public int GetFinger(bool leftHand, int pitch)
        {
            var assign = leftHand ? _leftFingerPitch : _rightFingerPitch;
            for (int f = 0; f < MaxFingers; f++)
            {
                if (assign[f] == pitch) return f;
            }
            return -1;
        }

        /// <summary>该手是否已分配出至少一根手指。</summary>
        public bool HasAssignment(bool leftHand)
        {
            var assign = leftHand ? _leftFingerPitch : _rightFingerPitch;
            for (int f = 0; f < MaxFingers; f++)
            {
                if (assign[f] >= 0) return true;
            }
            return false;
        }

        /// <summary>最近一次求解的置信度（0..1）。</summary>
        public float GetConfidence(bool leftHand) => leftHand ? _leftConfidence : _rightConfidence;

        /// <summary>最近一次求解是否属于「估算指法」（教学诚实性：UI 不应显示确定编号）。</summary>
        public bool IsEstimated(bool leftHand) => leftHand ? _leftEstimated : _rightEstimated;

        /// <summary>最近一次求解的中文规则说明。</summary>
        public string GetReason(bool leftHand) => leftHand ? _leftReason : _rightReason;

        /// <summary>作废本帧缓存（音符集合变化时由控制器调用；也会在下一帧自动重算）。</summary>
        public void Invalidate()
        {
            _leftFrame = -1;
            _rightFrame = -1;
            _leftSignature = 0;
            _rightSignature = 0;
        }

        /// <summary>清空全部分配与旋律历史（Stop / 切曲 / Seek 重建时调用）。</summary>
        public void Clear()
        {
            ClearArray(_leftFingerPitch);
            ClearArray(_rightFingerPitch);
            _leftMelody.Clear();
            _rightMelody.Clear();
            _leftConfidence = _rightConfidence = 1f;
            _leftEstimated = _rightEstimated = false;
            _leftReason = _rightReason = string.Empty;
            Invalidate();
        }

        /// <summary>音高集合的签名（顺序敏感；用于「同一帧 + 同一集合」判重）。</summary>
        private static int ComputeSignature(IReadOnlyList<int> pitches)
        {
            if (pitches == null || pitches.Count == 0) return 0;
            int h = 19;
            for (int i = 0; i < pitches.Count; i++) h = h * 31 + pitches[i];
            return h * 31 + pitches.Count;
        }

        private static void ClearArray(int[] array)
        {
            for (int i = 0; i < array.Length; i++) array[i] = -1;
        }

        private void SetResult(bool leftHand, float confidence, bool estimated, string reason)
        {
            if (leftHand)
            {
                _leftConfidence = confidence;
                _leftEstimated = estimated;
                _leftReason = reason ?? string.Empty;
            }
            else
            {
                _rightConfidence = confidence;
                _rightEstimated = estimated;
                _rightReason = reason ?? string.Empty;
            }
        }
    }
}
