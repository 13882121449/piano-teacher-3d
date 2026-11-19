using System;
using System.Collections.Generic;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 基于生物力学与钢琴演奏规则的全局动态规划（Viterbi）指法求解器。
    /// <para>
    /// 替代原先逐音贪心的固定锚点规则，解决「高音区全程只用小指」、「跨八度后连续只用拇指」等不真实指法：
    /// ① 乐句级全局代价寻优：避免异音同指连击，保持手位（五指把位）稳定；
    /// ② 黑键避短指：拇指尽量不上黑键，优先使用 2/3/4 长指；
    /// ③ 自然穿指与跨指：上行允许 3/4→1 穿指，下行允许 1→3/2 跨指，禁止非拇指交叉；
    /// ④ 硬约束保持音不换指、超宽跨度（&gt;14 半音或 &gt;5 音）不硬塞；
    /// ⑤ 与教学读谱（<c>ScoreFingering</c>）及 3D 演奏（<c>FingerAssignmentService</c>）共用同一份求解结果。
    /// </para>
    /// </summary>
    public static class PianoFingeringSolver
    {
        private const int FingerCount = 5;
        private const int MaxChordSpan = 14;
        private const float SameOnsetSeconds = 0.02f;
        private const float PhraseGapSeconds = 0.40f;
        private const float RepeatContinuityGapSeconds = 0.60f;

        // 解剖学指距表（右手方向，单位：半音；索引为 ordered pair [lowFinger, highFinger]）
        // 含：最小可行距、最小舒适距、最大舒适距、最大可行距
        private struct SpanSpec
        {
            public readonly int MinPrac;
            public readonly int MinComf;
            public readonly int MaxComf;
            public readonly int MaxPrac;

            public SpanSpec(int minPrac, int minComf, int maxComf, int maxPrac)
            {
                MinPrac = minPrac;
                MinComf = minComf;
                MaxComf = maxComf;
                MaxPrac = maxPrac;
            }
        }

        private static readonly SpanSpec[,] Spans = BuildSpans();

        // 五指在自然 C 大调手位上的半音偏移（用于估算手掌当前把位锚点）
        private static readonly int[] NaturalSemitoneOffset = { 0, 2, 4, 5, 7 };

        private static SpanSpec[,] BuildSpans()
        {
            var table = new SpanSpec[FingerCount, FingerCount];
            table[0, 1] = new SpanSpec(-5, 1, 5, 9);
            table[0, 2] = new SpanSpec(-4, 3, 7, 11);
            table[0, 3] = new SpanSpec(-3, 5, 9, 13);
            table[0, 4] = new SpanSpec(-1, 7, 11, 14);
            table[1, 2] = new SpanSpec(1, 1, 3, 5);
            table[1, 3] = new SpanSpec(2, 3, 5, 7);
            table[1, 4] = new SpanSpec(4, 5, 8, 10);
            table[2, 3] = new SpanSpec(1, 1, 2, 4);
            table[2, 4] = new SpanSpec(2, 3, 5, 7);
            table[3, 4] = new SpanSpec(1, 1, 3, 5);
            return table;
        }

        private sealed class Slice
        {
            public float Onset;
            public float MaxEnd;
            public readonly List<int> Pitches = new List<int>(8);
            public readonly List<int> NewPitches = new List<int>(8);
            public readonly HashSet<int> HeldPitches = new HashSet<int>();
            public readonly List<int> NoteIndices = new List<int>(8);
            public bool Unplayable;
        }

        private struct State
        {
            public int[] Fingers; // 与 Slice.Pitches 一一对应
            public float Emission;
        }

        /// <summary>
        /// 为单手音符序列求解真实钢琴指法。返回键为 <c>(Pitch, StartTime)</c>、值为 0..4（-1 表示超出单手可按范围）。
        /// </summary>
        public static Dictionary<(int Pitch, float Start), int> SolveHand(
            IReadOnlyList<MidiNoteEvent> handNotes,
            bool leftHand,
            IReadOnlyDictionary<(int Pitch, float Start), int> lockedNotes = null)
        {
            var output = new Dictionary<(int Pitch, float Start), int>();
            if (handNotes == null || handNotes.Count == 0) return output;

            var sortedIndices = SortNoteIndices(handNotes);
            var slices = BuildSlices(handNotes, sortedIndices);
            if (slices.Count == 0) return output;

            var statesPerSlice = new List<List<State>>(slices.Count);
            for (int s = 0; s < slices.Count; s++)
            {
                var slice = slices[s];
                var states = EnumerateStates(handNotes, slice, leftHand, lockedNotes);
                statesPerSlice.Add(states);
            }

            // Viterbi 前向 DP
            var dp = new List<float[]>(slices.Count);
            var parent = new List<int[]>(slices.Count);
            for (int s = 0; s < slices.Count; s++)
            {
                int m = statesPerSlice[s].Count;
                dp.Add(new float[m]);
                parent.Add(new int[m]);
            }

            for (int j = 0; j < statesPerSlice[0].Count; j++)
            {
                dp[0][j] = statesPerSlice[0][j].Emission;
                parent[0][j] = -1;
            }

            for (int s = 1; s < slices.Count; s++)
            {
                var prevSlice = slices[s - 1];
                var currSlice = slices[s];
                var prevStates = statesPerSlice[s - 1];
                var currStates = statesPerSlice[s];

                for (int j = 0; j < currStates.Count; j++)
                {
                    float bestCost = float.PositiveInfinity;
                    int bestPrev = 0;
                    for (int i = 0; i < prevStates.Count; i++)
                    {
                        float prevTotal = dp[s - 1][i];
                        if (float.IsPositiveInfinity(prevTotal)) continue;

                        float trans = TransitionCost(prevSlice, prevStates[i], currSlice, currStates[j], leftHand);
                        float total = prevTotal + trans + currStates[j].Emission;
                        if (total < bestCost)
                        {
                            bestCost = total;
                            bestPrev = i;
                        }
                    }

                    // 极端情况（如保持音与硬锁定冲突）：退化为忽略硬转移约束的最小发射代价，保证永不断裂
                    if (float.IsPositiveInfinity(bestCost))
                    {
                        for (int i = 0; i < prevStates.Count; i++)
                        {
                            float fallback = dp[s - 1][i] + currStates[j].Emission + 50f;
                            if (fallback < bestCost)
                            {
                                bestCost = fallback;
                                bestPrev = i;
                            }
                        }
                    }

                    dp[s][j] = bestCost;
                    parent[s][j] = bestPrev;
                }
            }

            // 回溯最优路径
            int lastSlice = slices.Count - 1;
            int bestFinal = 0;
            float bestFinalCost = float.PositiveInfinity;
            for (int j = 0; j < dp[lastSlice].Length; j++)
            {
                if (dp[lastSlice][j] < bestFinalCost)
                {
                    bestFinalCost = dp[lastSlice][j];
                    bestFinal = j;
                }
            }

            var chosenStates = new State[slices.Count];
            int curIdx = bestFinal;
            for (int s = lastSlice; s >= 0; s--)
            {
                chosenStates[s] = statesPerSlice[s][curIdx];
                curIdx = parent[s][curIdx];
                if (curIdx < 0) curIdx = 0;
            }

            // 写入每个音符的结果（同一音符只按起音所在切片赋值）
            for (int s = 0; s < slices.Count; s++)
            {
                var slice = slices[s];
                var state = chosenStates[s];
                for (int k = 0; k < slice.NoteIndices.Count; k++)
                {
                    var note = handNotes[slice.NoteIndices[k]];
                    int pIdx = slice.Pitches.IndexOf(note.Pitch);
                    int finger = pIdx >= 0 && pIdx < state.Fingers.Length ? state.Fingers[pIdx] : -1;
                    output[(note.Pitch, note.StartTime)] = finger;
                }
            }

            return output;
        }

        /// <summary>
        /// 为整首混合音符序列生成教学乐谱指法（按 <paramref name="notes"/> 原索引返回 1..5，算不出为 0）。
        /// </summary>
        public static int[] SolveScore(IReadOnlyList<MidiNoteEvent> notes)
        {
            int count = notes?.Count ?? 0;
            var result = new int[count];
            if (count == 0) return result;

            SolveScoreHand(notes, true, result);
            SolveScoreHand(notes, false, result);
            return result;
        }

        private static void SolveScoreHand(IReadOnlyList<MidiNoteEvent> notes, bool leftHand, int[] result)
        {
            var handNotes = new List<MidiNoteEvent>();
            var originalIndices = new List<int>();
            for (int i = 0; i < notes.Count; i++)
            {
                if (PianoNoteFilter.IsLeftHand(notes[i]) != leftHand) continue;
                handNotes.Add(notes[i]);
                originalIndices.Add(i);
            }
            if (handNotes.Count == 0) return;

            var solved = SolveHand(handNotes, leftHand, null);
            for (int i = 0; i < handNotes.Count; i++)
            {
                var n = handNotes[i];
                if (solved.TryGetValue((n.Pitch, n.StartTime), out int finger) && finger >= 0 && finger < FingerCount)
                {
                    result[originalIndices[i]] = finger + 1;
                }
            }
        }

        // ==================== 切片与状态枚举 ====================

        private static int[] SortNoteIndices(IReadOnlyList<MidiNoteEvent> notes)
        {
            var order = new int[notes.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int cmp = notes[a].StartTime.CompareTo(notes[b].StartTime);
                return cmp != 0 ? cmp : notes[a].Pitch.CompareTo(notes[b].Pitch);
            });
            return order;
        }

        private static List<Slice> BuildSlices(IReadOnlyList<MidiNoteEvent> notes, int[] order)
        {
            var slices = new List<Slice>();
            var activeUntil = new float[128];

            for (int start = 0; start < order.Length;)
            {
                float onset = notes[order[start]].StartTime;
                int end = start + 1;
                while (end < order.Length && notes[order[end]].StartTime <= onset + SameOnsetSeconds)
                    end++;

                var slice = new Slice { Onset = onset, MaxEnd = onset };
                for (int i = start; i < end; i++)
                {
                    int idx = order[i];
                    var n = notes[idx];
                    slice.NoteIndices.Add(idx);
                    if (!slice.NewPitches.Contains(n.Pitch)) slice.NewPitches.Add(n.Pitch);
                    if (!slice.Pitches.Contains(n.Pitch)) slice.Pitches.Add(n.Pitch);
                    if (n.EndTime > slice.MaxEnd) slice.MaxEnd = n.EndTime;
                }
                slice.NewPitches.Sort();

                for (int p = 0; p < activeUntil.Length; p++)
                {
                    if (activeUntil[p] > onset + 0.0005f)
                    {
                        if (!slice.NewPitches.Contains(p))
                        {
                            slice.HeldPitches.Add(p);
                            slice.Pitches.Add(p);
                        }
                        if (activeUntil[p] > slice.MaxEnd) slice.MaxEnd = activeUntil[p];
                    }
                }
                slice.Pitches.Sort();

                int span = slice.Pitches.Count > 0 ? slice.Pitches[slice.Pitches.Count - 1] - slice.Pitches[0] : 0;
                slice.Unplayable = slice.Pitches.Count > FingerCount || span > MaxChordSpan;

                for (int i = start; i < end; i++)
                {
                    var n = notes[order[i]];
                    if (n.Pitch >= 0 && n.Pitch < 128 && n.EndTime > activeUntil[n.Pitch])
                        activeUntil[n.Pitch] = n.EndTime;
                }

                slices.Add(slice);
                start = end;
            }

            return slices;
        }

        private static List<State> EnumerateStates(
            IReadOnlyList<MidiNoteEvent> notes,
            Slice slice,
            bool leftHand,
            IReadOnlyDictionary<(int Pitch, float Start), int> lockedNotes)
        {
            var states = new List<State>();
            int k = slice.Pitches.Count;
            if (k == 0)
            {
                states.Add(new State { Fingers = Array.Empty<int>(), Emission = 0f });
                return states;
            }

            if (slice.Unplayable)
            {
                // 超出五指或单手最大跨度：新音留空（-1），不硬塞（已有保持音由前序决定；此处提供全 -1 保底并优先匹配保持音）
                var f = new int[k];
                for (int i = 0; i < k; i++) f[i] = -1;
                states.Add(new State { Fingers = f, Emission = 0f });
                return states;
            }

            // 检查是否已被锁定（例如标准 C 大调单八度音阶穿指）
            int[] locked = null;
            if (lockedNotes != null && lockedNotes.Count > 0)
            {
                for (int ni = 0; ni < slice.NoteIndices.Count; ni++)
                {
                    var note = notes[slice.NoteIndices[ni]];
                    if (lockedNotes.TryGetValue((note.Pitch, note.StartTime), out int lf) && lf >= 0 && lf < FingerCount)
                    {
                        if (locked == null)
                        {
                            locked = new int[k];
                            for (int i = 0; i < k; i++) locked[i] = -1;
                        }
                        int pIdx = slice.Pitches.IndexOf(note.Pitch);
                        if (pIdx >= 0) locked[pIdx] = lf;
                    }
                }
            }

            var current = new int[k];
            GenerateCombinations(slice, leftHand, locked, current, 0, 0, states);

            // 若锁定与五指单调组合冲突，放开锁定重新枚举（确保永不出现空状态集）
            if (states.Count == 0)
                GenerateCombinations(slice, leftHand, null, current, 0, 0, states);

            return states;
        }

        private static void GenerateCombinations(
            Slice slice,
            bool leftHand,
            int[] locked,
            int[] current,
            int depth,
            int startSlot,
            List<State> output)
        {
            int k = slice.Pitches.Count;
            if (depth == k)
            {
                var fingers = new int[k];
                Array.Copy(current, fingers, k);
                output.Add(new State
                {
                    Fingers = fingers,
                    Emission = EmissionCost(slice, fingers, leftHand)
                });
                return;
            }

            int maxSlot = FingerCount - (k - depth);
            for (int slot = startSlot; slot <= maxSlot; slot++)
            {
                // 右手低→高对应 0→4；左手低→高对应 4→0
                int finger = leftHand ? (FingerCount - 1 - slot) : slot;
                if (locked != null && locked[depth] >= 0 && locked[depth] != finger) continue;
                current[depth] = finger;
                GenerateCombinations(slice, leftHand, locked, current, depth + 1, slot + 1, output);
            }
        }

        // ==================== 代价函数 ====================

        private static float EmissionCost(Slice slice, int[] fingers, bool leftHand)
        {
            float cost = 0f;
            int k = slice.Pitches.Count;

            for (int i = 0; i < k; i++)
            {
                int pitch = slice.Pitches[i];
                int finger = fingers[i];
                bool black = PianoNoteFilter.IsBlackKey(pitch);

                if (black)
                {
                    // 黑键避短指（拇指与小指），偏好长指（2/3/4 指）
                    if (finger == 0) cost += 5.0f;
                    else if (finger == 4) cost += 1.8f;
                    else cost -= 0.25f;
                }

                // 轻微偏好中强指，防止无意义地频繁使用 4/5 指
                if (k == 1)
                {
                    if (finger == 3) cost += 0.14f;
                    else if (finger == 4) cost += 0.26f;
                }
            }

            if (k >= 2)
            {
                for (int i = 0; i < k; i++)
                {
                    for (int j = i + 1; j < k; j++)
                    {
                        int fa = fingers[i];
                        int fb = fingers[j];
                        int lo = Math.Min(fa, fb);
                        int hi = Math.Max(fa, fb);
                        int span = Math.Abs(slice.Pitches[j] - slice.Pitches[i]);
                        SpanSpec spec = Spans[lo, hi];

                        if (span < spec.MinComf)
                        {
                            float d = spec.MinComf - span;
                            cost += (span < spec.MinPrac ? 12.0f : 1.4f) * d;
                        }
                        else if (span > spec.MaxComf)
                        {
                            float d = span - spec.MaxComf;
                            cost += (span > spec.MaxPrac ? 15.0f : 1.2f) * d * d;
                        }
                        else
                        {
                            float ideal = 0.5f * (spec.MinComf + spec.MaxComf);
                            cost += 0.12f * Math.Abs(span - ideal);
                        }
                    }
                }

                // 经典三和弦 1-3-5 微调偏好
                if (k == 3)
                {
                    int f0 = leftHand ? fingers[2] : fingers[0];
                    int f1 = fingers[1];
                    int f2 = leftHand ? fingers[0] : fingers[2];
                    if (f0 == 0 && f1 == 2 && f2 == 4 && slice.Pitches[2] - slice.Pitches[0] <= 9)
                        cost -= 0.45f;
                }
                else if (k == 2)
                {
                    int span = slice.Pitches[1] - slice.Pitches[0];
                    int fLo = leftHand ? fingers[1] : fingers[0];
                    int fHi = leftHand ? fingers[0] : fingers[1];
                    if (span <= 4 && fLo == 0 && fHi == 2) cost -= 0.25f;
                    else if (span >= 7 && fLo == 0 && fHi == 4) cost -= 0.35f;
                }
            }

            return cost;
        }

        private static float TransitionCost(
            Slice prevSlice, State prevState,
            Slice currSlice, State currState,
            bool leftHand)
        {
            if (prevSlice.Unplayable || currSlice.Unplayable) return 0f;

            // ① 硬约束：仍在保持中的音符绝不能换指
            foreach (int held in currSlice.HeldPitches)
            {
                int prevIdx = prevSlice.Pitches.IndexOf(held);
                int currIdx = currSlice.Pitches.IndexOf(held);
                if (prevIdx >= 0 && currIdx >= 0 && prevState.Fingers[prevIdx] != currState.Fingers[currIdx])
                    return float.PositiveInfinity;
            }

            float gap = currSlice.Onset - prevSlice.MaxEnd;

            // 乐句断开后重新定位，但短休止同音重复保持连续指法
            if (gap > PhraseGapSeconds)
            {
                if (gap <= RepeatContinuityGapSeconds && currSlice.Pitches.Count == 1)
                {
                    int p = currSlice.Pitches[0];
                    int prevIdx = prevSlice.Pitches.IndexOf(p);
                    if (prevIdx >= 0)
                        return prevState.Fingers[prevIdx] == currState.Fingers[0] ? -1.2f : 1.8f;
                }
                return 0f;
            }

            float connectedWeight = gap <= 0.15f
                ? 1.0f
                : 1.0f - 0.60f * ((gap - 0.15f) / (PhraseGapSeconds - 0.15f));
            if (connectedWeight < 0.35f) connectedWeight = 0.35f;

            float cost = 0f;

            // 单音且在前序和弦/单音里出现过（同音重复）
            if (currSlice.Pitches.Count == 1)
            {
                int p = currSlice.Pitches[0];
                int prevIdx = prevSlice.Pitches.IndexOf(p);
                if (prevIdx >= 0)
                {
                    return prevState.Fingers[prevIdx] == currState.Fingers[0]
                        ? -2.0f
                        : 2.5f * connectedWeight;
                }
            }

            // 单音 → 单音，或新起单音 → 新起单音（例如伴奏分解和弦保持低音后依次上行）
            if (prevSlice.NewPitches.Count == 1 && currSlice.NewPitches.Count == 1)
            {
                int prevP = prevSlice.NewPitches[0];
                int currP = currSlice.NewPitches[0];
                int prevF = prevState.Fingers[prevSlice.Pitches.IndexOf(prevP)];
                int currF = currState.Fingers[currSlice.Pitches.IndexOf(currP)];
                cost += PairTransitionCost(prevP, prevF, currP, currF, leftHand, connectedWeight);
            }
            else
            {
                // 多音和弦/混合起音过渡：比较外声部边界与手位锚点平移
                int prevLowF = prevState.Fingers[0];
                int prevHighF = prevState.Fingers[prevSlice.Pitches.Count - 1];
                int currLowF = currState.Fingers[0];
                int currHighF = currState.Fingers[currSlice.Pitches.Count - 1];
                cost += 0.35f * PairTransitionCost(
                    prevSlice.Pitches[0], prevLowF,
                    currSlice.Pitches[0], currLowF,
                    leftHand, connectedWeight);
                cost += 0.35f * PairTransitionCost(
                    prevSlice.Pitches[prevSlice.Pitches.Count - 1], prevHighF,
                    currSlice.Pitches[currSlice.Pitches.Count - 1], currHighF,
                    leftHand, connectedWeight);
            }

            // 手位锚点平滑惩罚：使五指尽量留在同一把位，减少无谓甩手
            float prevAnchor = ImpliedAnchor(prevSlice, prevState.Fingers, leftHand);
            float currAnchor = ImpliedAnchor(currSlice, currState.Fingers, leftHand);
            cost += 0.32f * Math.Abs(currAnchor - prevAnchor) * connectedWeight;

            return cost;
        }

        private static float PairTransitionCost(
            int prevPitch, int prevFinger,
            int currPitch, int currFinger,
            bool leftHand, float connectedWeight)
        {
            if (prevPitch == currPitch)
                return prevFinger == currFinger ? -2.0f : 2.5f * connectedWeight;

            // 异音同指：严厉惩罚，彻底消除「全程一根指头戳」现象
            if (prevFinger == currFinger)
            {
                int leap = Math.Abs(currPitch - prevPitch);
                return (leap >= 9 ? 6.5f : 14.0f) * connectedWeight;
            }

            int dir = leftHand ? -1 : 1;
            int dp = dir * (currPitch - prevPitch); // >0 表示向小指方向延伸，<0 表示向拇指方向回收
            int df = currFinger - prevFinger;

            if (df > 0)
            {
                SpanSpec spec = Spans[prevFinger, currFinger];
                if (dp >= spec.MinComf && dp <= spec.MaxComf)
                {
                    float ideal = 0.5f * (spec.MinComf + spec.MaxComf);
                    return 0.12f * Math.Abs(dp - ideal) * connectedWeight;
                }
                if (dp > spec.MaxComf && dp <= spec.MaxPrac)
                {
                    float ex = dp - spec.MaxComf;
                    return 0.6f * ex * ex * connectedWeight;
                }
                if (dp > spec.MaxPrac)
                {
                    float ex = dp - spec.MaxPrac;
                    return (5.0f + 1.4f * ex) * connectedWeight;
                }
                if (dp >= spec.MinPrac && dp < spec.MinComf)
                {
                    if (dp > 0)
                    {
                        float ex = spec.MinComf - dp;
                        return 0.6f * ex * ex * connectedWeight;
                    }
                    // prevFinger == 0 且 dp <= 0：从拇指上方跨指（如右手下行 1→3/2）
                    return FingerOverThumbCost(prevPitch, currPitch, currFinger, -dp) * connectedWeight;
                }
                if (prevFinger == 0 && dp < 0)
                    return (FingerOverThumbCost(prevPitch, currPitch, currFinger, -dp)
                            + 1.2f * (-dp - Math.Abs(spec.MinPrac))) * connectedWeight;

                // 非拇指逆向交叉（如右手上行却用 2→3 往低音跨）
                return (18.0f + 1.5f * (spec.MinPrac - dp)) * connectedWeight;
            }
            else
            {
                // df < 0：currFinger < prevFinger
                SpanSpec spec = Spans[currFinger, prevFinger];
                int backwardDp = -dp;
                if (backwardDp >= spec.MinComf && backwardDp <= spec.MaxComf)
                {
                    float ideal = 0.5f * (spec.MinComf + spec.MaxComf);
                    return 0.12f * Math.Abs(backwardDp - ideal) * connectedWeight;
                }
                if (backwardDp > spec.MaxComf && backwardDp <= spec.MaxPrac)
                {
                    float ex = backwardDp - spec.MaxComf;
                    return 0.6f * ex * ex * connectedWeight;
                }
                if (backwardDp > spec.MaxPrac)
                {
                    float ex = backwardDp - spec.MaxPrac;
                    return (5.0f + 1.4f * ex) * connectedWeight;
                }
                if (backwardDp >= spec.MinPrac && backwardDp < spec.MinComf)
                {
                    if (backwardDp > 0)
                    {
                        float ex = spec.MinComf - backwardDp;
                        return 0.6f * ex * ex * connectedWeight;
                    }
                    // currFinger == 0 且 dp >= 0：拇指从长指下方穿越（如右手上行 3→1）
                    return ThumbUnderCost(currPitch, prevFinger, dp) * connectedWeight;
                }
                if (currFinger == 0 && dp >= 0)
                    return (ThumbUnderCost(currPitch, prevFinger, dp)
                            + 1.2f * (dp - Math.Abs(spec.MinPrac))) * connectedWeight;

                // 非拇指逆向交叉
                return (18.0f + 1.5f * (spec.MinPrac - backwardDp)) * connectedWeight;
            }
        }

        private static float FingerOverThumbCost(int thumbPitch, int overFingerPitch, int overFinger, int semitones)
        {
            float thumbBlack = PianoNoteFilter.IsBlackKey(thumbPitch) ? 3.5f : 0f;
            float fingerBlack = PianoNoteFilter.IsBlackKey(overFingerPitch) ? -0.35f : 0f;
            float baseCost = overFinger == 2 ? 2.0f : (overFinger == 1 ? 2.4f : (overFinger == 3 ? 2.6f : 7.0f));
            float dist = 0.7f * Math.Max(0, semitones - 4);
            return baseCost + dist + thumbBlack + fingerBlack;
        }

        private static float ThumbUnderCost(int thumbPitch, int fromFinger, int semitones)
        {
            float thumbBlack = PianoNoteFilter.IsBlackKey(thumbPitch) ? 4.2f : 0f;
            float baseCost = fromFinger == 2 ? 2.0f : (fromFinger == 3 ? 2.3f : (fromFinger == 1 ? 3.0f : 7.5f));
            float dist = 0.7f * Math.Max(0, semitones - 3);
            return baseCost + dist + thumbBlack;
        }

        private static float ImpliedAnchor(Slice slice, int[] fingers, bool leftHand)
        {
            int dir = leftHand ? -1 : 1;
            float sum = 0f;
            int count = slice.Pitches.Count;
            for (int i = 0; i < count; i++)
            {
                int f = fingers[i];
                if (f < 0 || f >= FingerCount) continue;
                sum += slice.Pitches[i] - dir * NaturalSemitoneOffset[f];
            }
            return count > 0 ? sum / count : 0f;
        }
    }
}
