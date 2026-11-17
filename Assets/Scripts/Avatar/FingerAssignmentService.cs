using System.Collections.Generic;
using UnityEngine;
using PianoTeacher3D.MIDI;

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

        /// <summary>取已分配音高对应的实际键面按压点。</summary>
        bool TryGetKeyPressPoint(int pitch, out Vector3 point);
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
        private readonly Dictionary<(int Pitch, float Start), int> _leftPlan = new();
        private readonly Dictionary<(int Pitch, float Start), int> _rightPlan = new();
        private readonly int[] _leftActivePlan = new int[128];
        private readonly int[] _rightActivePlan = new int[128];
        private readonly int[] _previewPitches = new int[1];
        private int _leftPreviewPitch = -1;
        private int _rightPreviewPitch = -1;
        private int _leftPreviewFinger = -1;
        private int _rightPreviewFinger = -1;

        private static readonly int[] RightAscending = { 0, 1, 2, 0, 1, 2, 3, 4 };
        private static readonly int[] LeftAscending = { 4, 3, 2, 1, 0, 2, 1, 0 };
        private static readonly int[] MajorScaleIntervals = { 2, 2, 1, 2, 2, 2, 1 };
        private const int UnknownFinger = -2;
        private const int PianoFingerCount = 5;
        private const int MaxChordSpan = 14;
        private const float SameOnsetSeconds = 0.02f;
        private const float PhraseGapSeconds = 0.40f;

        public FingerAssignmentService() => Clear();

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

        /// <summary>用完整音符事件规划和弦和保持音。可关闭以复现旧版指法。</summary>
        public bool UsePreparedPhrases { get; set; } = true;

        /// <summary>当前活动音中是否有无法展示的音。</summary>
        public bool HasUnplayableNotes(bool leftHand) => leftHand ? _leftUnplayable : _rightUnplayable;

        private bool _leftUnplayable;
        private bool _rightUnplayable;

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
                _leftPreviewPitch = _rightPreviewPitch = -1;
                Invalidate();
            }
        }

        /// <summary>当前提供器名称（供 UI / 诊断标注）。</summary>
        public string ProviderName => _provider != null ? _provider.Name : "(未设置)";

        /// <summary>载入时依乐句规划指序，按起音和音高定位同指重复与持续音。</summary>
        public void PrepareSong(IReadOnlyList<MidiNoteEvent> notes)
        {
            _leftPlan.Clear();
            _rightPlan.Clear();
            Clear();
            if (notes == null) return;

            var left = new List<MidiNoteEvent>();
            var right = new List<MidiNoteEvent>();
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                if (note.Duration <= 0f || !Piano.PianoKeyMap.IsInRange(note.Pitch)) continue;
                (PianoNoteFilter.IsLeftHand(note) ? left : right).Add(note);
            }
            PlanScales(left, true, _leftPlan);
            PlanScales(right, false, _rightPlan);
            if (!UsePreparedPhrases) return;
            PlanPhrases(left, true, _leftPlan);
            PlanPhrases(right, false, _rightPlan);
        }

        private void PlanPhrases(List<MidiNoteEvent> notes, bool leftHand,
            Dictionary<(int Pitch, float Start), int> output)
        {
            var provider = _provider ?? (_provider = new HeuristicFingeringProvider());
            var activeFingers = new int[128];
            var activeUntil = new float[128];
            var sounding = new List<int>(PianoFingerCount);
            var recent = new List<int>(8);
            ClearArray(activeFingers);
            var lastFinger = new int[128];
            var lastNoteEnd = new float[128];
            ClearArray(lastFinger);
            float previousEnd = -1f;
            for (int start = 0; start < notes.Count;)
            {
                float onset = notes[start].StartTime;
                int end = start + 1;
                while (end < notes.Count && notes[end].StartTime <= onset + 0.0001f) end++;

                sounding.Clear();
                for (int pitch = 0; pitch < activeUntil.Length; pitch++)
                    if (activeUntil[pitch] > onset + 0.0001f && activeFingers[pitch] >= 0)
                        sounding.Add(pitch);
                for (int i = start; i < end; i++)
                {
                    int pitch = notes[i].Pitch;
                    if (!sounding.Contains(pitch)) sounding.Add(pitch);
                }
                sounding.Sort();
                if (onset - previousEnd > PhraseGapSeconds + 0.0001f)
                {
                    recent.Clear();
                }
                var context = new FingeringContext(leftHand, sounding, recent, onset);
                var preferred = provider.Assign(in context)?.FingerByPitch;
                int minPitch = sounding[0];
                int maxPitch = sounding[sounding.Count - 1];
                if (sounding.Count <= PianoFingerCount && maxPitch - minPitch <= MaxChordSpan)
                {
                    if (sounding.Count == 1)
                    {
                        int pitch = sounding[0];
                        int chosen = onset - lastNoteEnd[pitch] <= PhraseGapSeconds + 0.0001f
                            ? lastFinger[pitch] : -1;
                        if (activeUntil[pitch] > onset) chosen = activeFingers[pitch];
                        if (output.TryGetValue((pitch, onset), out int scaleFinger)) chosen = scaleFinger;
                        if (chosen < 0 && preferred != null && preferred.Length > 0) chosen = preferred[0];
                        if (end < notes.Count && notes[end].StartTime < notes[start].EndTime
                            && !output.ContainsKey((pitch, onset)))
                        {
                            int nextPitch = notes[end].Pitch;
                            if (leftHand ? nextPitch < pitch : nextPitch > pitch) chosen = Mathf.Min(chosen, 3);
                            else if (nextPitch != pitch) chosen = Mathf.Max(chosen, 1);
                        }
                        for (int i = start; i < end; i++) output[(pitch, notes[i].StartTime)] = chosen;
                    }
                    else
                    {
                        int mask = PickChordMask(sounding, preferred, activeFingers, activeUntil, onset, leftHand);
                        for (int i = start; i < end; i++)
                        {
                            int pitch = notes[i].Pitch;
                            int rank = sounding.BinarySearch(pitch);
                            output[(pitch, notes[i].StartTime)] = mask == 0 ? -1 : FingerAtRank(mask, rank, leftHand);
                        }
                    }
                }
                else
                {
                    // 保留已经按住的音。当前新增音组过宽或多于五指时，整组不硬塞给空闲指。
                    for (int i = start; i < end; i++)
                    {
                        var note = notes[i];
                        output[(note.Pitch, note.StartTime)] = activeUntil[note.Pitch] > onset
                            ? activeFingers[note.Pitch] : -1;
                    }
                }

                for (int i = start; i < end; i++)
                {
                    var note = notes[i];
                    int finger = output[(note.Pitch, note.StartTime)];
                    activeUntil[note.Pitch] = Mathf.Max(activeUntil[note.Pitch], note.EndTime);
                    activeFingers[note.Pitch] = finger;
                    lastFinger[note.Pitch] = finger;
                    lastNoteEnd[note.Pitch] = note.EndTime;
                    previousEnd = Mathf.Max(previousEnd, note.EndTime);
                    recent.Add(note.Pitch);
                    if (recent.Count > 8) recent.RemoveAt(0);
                }
                // 后一个起音进入保持期前，已占用的手指保持原分配。
                start = end;
            }
        }

        private static int PickChordMask(List<int> pitches, int[] preferred,
            int[] active, float[] activeUntil, float onset, bool leftHand)
        {
            int bestMask = 0;
            float bestCost = float.PositiveInfinity;
            for (int mask = 1; mask < 1 << PianoFingerCount; mask++)
            {
                int count = 0;
                for (int f = 0; f < PianoFingerCount; f++) count += (mask >> f) & 1;
                if (count != pitches.Count) continue;

                float cost = 0f;
                for (int i = 0; i < pitches.Count; i++)
                {
                    int finger = FingerAtRank(mask, i, leftHand);
                    int pitch = pitches[i];
                    if (activeUntil[pitch] > onset + 0.0001f && active[pitch] != finger)
                    {
                        cost = float.PositiveInfinity;
                        break;
                    }
                    if (preferred != null && i < preferred.Length) cost += 0.6f * Mathf.Abs(finger - preferred[i]);
                    if (PianoNoteFilter.IsBlackKey(pitch) && finger == 0) cost += 1.3f;
                    if (i > 0)
                    {
                        int step = Mathf.Abs(finger - FingerAtRank(mask, i - 1, leftHand));
                        cost += Mathf.Abs((pitches[i] - pitches[i - 1]) - step * 2.5f) * 0.3f;
                    }
                }
                if (cost >= bestCost) continue;
                bestMask = mask;
                bestCost = cost;
            }
            return bestMask;
        }

        private static int FingerAtRank(int mask, int rank, bool leftHand)
        {
            for (int f = 0; f < PianoFingerCount; f++)
            {
                int finger = leftHand ? PianoFingerCount - 1 - f : f;
                if ((mask & (1 << finger)) == 0) continue;
                if (rank-- == 0) return finger;
            }
            return -1;
        }

        private static void PlanScales(List<MidiNoteEvent> notes, bool leftHand,
            Dictionary<(int Pitch, float Start), int> output)
        {
            notes.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            var pattern = leftHand ? LeftAscending : RightAscending;
            for (int start = 0; start + pattern.Length <= notes.Count; start++)
            {
                int end = start + pattern.Length - 1;
                int direction = notes[end].Pitch > notes[start].Pitch ? 1 : -1;
                // 白键完整八度使用标准穿指。含黑键的调式保留启发式指法。
                if (notes[start].Pitch % 12 != 0 || notes[end].Pitch % 12 != 0) continue;
                if (start > 0 && notes[start].StartTime - notes[start - 1].StartTime <= SameOnsetSeconds
                    || end + 1 < notes.Count && notes[end + 1].StartTime - notes[end].StartTime <= SameOnsetSeconds)
                    continue;

                bool matches = true;
                for (int i = 0; i < MajorScaleIntervals.Length; i++)
                {
                    float gap = notes[start + i + 1].StartTime - notes[start + i].StartTime;
                    int interval = notes[start + i + 1].Pitch - notes[start + i].Pitch;
                    int expected = MajorScaleIntervals[direction > 0 ? i : MajorScaleIntervals.Length - 1 - i];
                    if (gap > SameOnsetSeconds && gap <= PhraseGapSeconds && interval == direction * expected) continue;
                    matches = false;
                    break;
                }
                if (!matches) continue;
                for (int i = 0; i < pattern.Length; i++)
                {
                    var note = notes[start + i];
                    output[(note.Pitch, note.StartTime)] = pattern[direction > 0 ? i : pattern.Length - 1 - i];
                }
                start = end - 1; // 上下行共享折返音，允许从该音继续辨认下一组。
            }

            // 同指重复在已知音阶指法上延续，不因空档或回跳重新估计。
            for (int i = 1; i < notes.Count; i++)
            {
                var previous = notes[i - 1];
                var note = notes[i];
                if (note.Pitch == previous.Pitch && note.StartTime > previous.StartTime + SameOnsetSeconds
                    && note.StartTime - previous.EndTime < PhraseGapSeconds
                    && output.TryGetValue((previous.Pitch, previous.StartTime), out int finger))
                    output[(note.Pitch, note.StartTime)] = finger;
            }
        }

        /// <summary>保留音符身份，以便暂停、拖动和循环回跳仍采用曲目指法。</summary>
        public void PushMelody(bool leftHand, in MidiNoteEvent note)
        {
            if (note.Pitch < 0 || note.Pitch >= 128) return;
            var plans = leftHand ? _leftPlan : _rightPlan;
            var active = leftHand ? _leftActivePlan : _rightActivePlan;
            active[note.Pitch] = plans.TryGetValue((note.Pitch, note.StartTime), out int finger) ? finger : UnknownFinger;
            PushMelody(leftHand, note.Pitch);
        }

        /// <summary>把音符记进该手的旋律历史。</summary>
        public void PushMelody(bool leftHand, int pitch)
        {
            if (leftHand) _leftPreviewPitch = -1;
            else _rightPreviewPitch = -1;
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
            if (leftHand) _leftUnplayable = false;
            else _rightUnplayable = false;

            if (pitchesAscending == null || pitchesAscending.Count == 0)
            {
                SetResult(leftHand, 1f, false, string.Empty);
                return;
            }

            if (TryAssignPrepared(leftHand, pitchesAscending, assign))
            {
                SetResult(leftHand, HasUnplayableNotes(leftHand) ? 0f : 0.90f, true,
                    HasUnplayableNotes(leftHand) ? "该段超出当前指法的可演示范围" : "按曲目时间预先分配指法");
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

        private bool TryAssignPrepared(bool leftHand, IReadOnlyList<int> pitches, int[] assign)
        {
            var active = leftHand ? _leftActivePlan : _rightActivePlan;
            int used = 0;
            bool missing = false;
            for (int i = 0; i < pitches.Count; i++)
            {
                int pitch = pitches[i];
                int finger = pitch >= 0 && pitch < active.Length ? active[pitch] : UnknownFinger;
                if (finger == UnknownFinger) return false;
                if (finger < 0 || (used & (1 << finger)) != 0)
                {
                    if (!UsePreparedPhrases) return false;
                    missing = true;
                    continue;
                }
                used |= 1 << finger;
            }
            for (int i = 0; i < pitches.Count; i++)
            {
                int finger = active[pitches[i]];
                if (finger >= 0 && assign[finger] < 0) assign[finger] = pitches[i];
            }
            if (leftHand) _leftUnplayable = missing;
            else _rightUnplayable = missing;
            return true;
        }

        /// <summary>预测下一音的手指，保持正在演奏的分配不变。</summary>
        public int PreviewFinger(bool leftHand, int pitch, float startTime)
        {
            var plans = leftHand ? _leftPlan : _rightPlan;
            if (plans.TryGetValue((pitch, startTime), out int prepared)) return prepared;
            if ((leftHand ? _leftPreviewPitch : _rightPreviewPitch) == pitch)
                return leftHand ? _leftPreviewFinger : _rightPreviewFinger;

            _previewPitches[0] = pitch;
            var context = new FingeringContext(leftHand, _previewPitches, leftHand ? _leftMelody : _rightMelody, 0f);
            var provider = _provider ?? (_provider = new HeuristicFingeringProvider());
            var plan = provider.Assign(in context);
            int finger = plan?.FingerByPitch is { Length: > 0 } fingers ? fingers[0] : -1;
            if (leftHand)
            {
                _leftPreviewPitch = pitch;
                _leftPreviewFinger = finger;
            }
            else
            {
                _rightPreviewPitch = pitch;
                _rightPreviewFinger = finger;
            }
            return finger;
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
            System.Array.Fill(_leftActivePlan, UnknownFinger);
            System.Array.Fill(_rightActivePlan, UnknownFinger);
            _leftUnplayable = _rightUnplayable = false;
            _leftPreviewPitch = _rightPreviewPitch = -1;
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
