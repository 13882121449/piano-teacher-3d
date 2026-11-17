using System;
using System.Collections.Generic;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.MIDI
{
    /// <summary>
    /// 音符过滤工具（对应方案 2.2「左右手单独练习」）。
    /// 纯静态、不依赖 UnityEngine，便于单元测试。
    /// </summary>
    public static class PianoNoteFilter
    {
        /// <summary>左右手分界音高（中央 C4 = 60）：&lt;60 属左手，&gt;=60 属右手。</summary>
        public const int SplitPitch = 60;

        /// <summary>音级为黑键的 5 个半音（C#/D#/F#/G#/A#）。</summary>
        private static readonly bool[] BlackPitchClasses =
        {
            false, true, false, true, false, false, true, false, true, false, true, false
        };

        /// <summary>是否为黑键（纯逻辑，供琴键映射与曲目分析共用）。</summary>
        public static bool IsBlackKey(int pitch)
        {
            int pc = ((pitch % 12) + 12) % 12; // 负数音高也安全
            return BlackPitchClasses[pc];
        }

        /// <summary>该音高是否属左手（低于 C4）。</summary>
        public static bool IsLeftHand(int pitch) => pitch < SplitPitch;

        public static bool IsLeftHand(in MidiNoteEvent note) => note.Hand == NoteHand.Unspecified
            ? IsLeftHand(note.Pitch) : note.Hand == NoteHand.Left;

        /// <summary>在载入时估算同时起音的分手，保留事件顺序和已明确分手的音符。</summary>
        public static List<MidiNoteEvent> AssignHands(IReadOnlyList<MidiNoteEvent> notes)
        {
            var result = notes == null ? new List<MidiNoteEvent>() : new List<MidiNoteEvent>(notes);
            if (result.Count == 0) return result;

            var ordered = new int[result.Count];
            for (int i = 0; i < ordered.Length; i++) ordered[i] = i;
            Array.Sort(ordered, (a, b) => result[a].StartTime.CompareTo(result[b].StartTime));
            var chord = new List<int>(12);
            for (int start = 0, end; start < ordered.Length; start = end)
            {
                end = start + 1;
                float time = result[ordered[start]].StartTime;
                while (end < ordered.Length && result[ordered[end]].StartTime - time <= 0.02f) end++;
                chord.Clear();
                for (int i = start; i < end; i++) chord.Add(ordered[i]);
                chord.Sort((a, b) => result[a].Pitch.CompareTo(result[b].Pitch));

                int minLeft = int.MaxValue, maxLeft = -1, minRight = int.MaxValue, maxRight = -1;
                int countLeft = 0, countRight = 0;
                foreach (int index in chord)
                {
                    var note = result[index];
                    if (IsLeftHand(note))
                    {
                        minLeft = Math.Min(minLeft, note.Pitch);
                        maxLeft = Math.Max(maxLeft, note.Pitch);
                        countLeft++;
                    }
                    else
                    {
                        minRight = Math.Min(minRight, note.Pitch);
                        maxRight = Math.Max(maxRight, note.Pitch);
                        countRight++;
                    }
                }

                int bestSplit = -1;
                int bestCost = HandCost(minLeft, maxLeft, countLeft) + HandCost(minRight, maxRight, countRight);
                // 仅在双手同时起音时移动中音区的分界。独奏音及单手音群保留原分手。
                if (countLeft > 0 && countRight > 0 && chord.Count <= 10)
                {
                    for (int split = 1; split < chord.Count; split++)
                    {
                        int before = result[chord[split - 1]].Pitch;
                        int after = result[chord[split]].Pitch;
                        if (before == after) continue;
                        int cost = HandCost(result[chord[0]].Pitch, before, split)
                            + HandCost(after, result[chord[chord.Count - 1]].Pitch, chord.Count - split);
                        for (int j = 0; j < chord.Count && cost < bestCost; j++)
                        {
                            var note = result[chord[j]];
                            if (IsLeftHand(note) == (j < split)) continue;
                            if (note.Hand != NoteHand.Unspecified || Math.Abs(note.Pitch - SplitPitch) > 7)
                            {
                                cost = bestCost;
                                break;
                            }
                            cost += 3 + Math.Abs(note.Pitch - SplitPitch);
                        }
                        if (cost >= bestCost) continue;
                        bestCost = cost;
                        bestSplit = split;
                    }
                }

                for (int j = 0; j < chord.Count; j++)
                {
                    int index = chord[j];
                    var note = result[index];
                    if (note.Hand != NoteHand.Unspecified) continue;
                    bool left = bestSplit < 0 ? IsLeftHand(note.Pitch) : j < bestSplit;
                    note.Hand = left ? NoteHand.Left : NoteHand.Right;
                    result[index] = note;
                }
            }
            return result;
        }

        private static int HandCost(int lowest, int highest, int count)
        {
            if (count < 2) return 0;
            int excessSpan = Math.Max(0, highest - lowest - 12);
            return excessSpan * 4 + Math.Max(0, count - 5) * 32;
        }

        /// <summary>按练习模式过滤音符。Both 返回副本，LeftOnly/RightOnly 只保留对应手的音符。</summary>
        public static List<MidiNoteEvent> FilterByHand(IReadOnlyList<MidiNoteEvent> notes, PracticeHand hand)
        {
            var result = new List<MidiNoteEvent>();
            if (notes == null) return result;

            if (hand == PracticeHand.Both)
            {
                result.AddRange(notes);
                return result;
            }

            // 已分手的曲目直接复用。手工构造的音符也走同一套估算规则。
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].Hand != NoteHand.Unspecified) continue;
                notes = AssignHands(notes);
                break;
            }
            foreach (var n in notes)
            {
                bool isLeft = IsLeftHand(n);
                if ((hand == PracticeHand.LeftOnly && isLeft) ||
                    (hand == PracticeHand.RightOnly && !isLeft))
                {
                    result.Add(n);
                }
            }
            return result;
        }
    }

    /// <summary>保留重叠同音的两个手别。显示及动作冲突时优先采用该键原音域对应的手。</summary>
    internal sealed class ActiveNoteHands
    {
        private readonly int[] _left = new int[128];
        private readonly int[] _right = new int[128];

        public bool IsLeft(int pitch) => _left[pitch] > 0
            && (_right[pitch] == 0 || PianoNoteFilter.IsLeftHand(pitch));

        public void Press(in MidiNoteEvent note)
        {
            (PianoNoteFilter.IsLeftHand(note) ? _left : _right)[note.Pitch]++;
        }

        public bool Release(in MidiNoteEvent note)
        {
            var counts = PianoNoteFilter.IsLeftHand(note) ? _left : _right;
            if (counts[note.Pitch] > 0) counts[note.Pitch]--;
            return _left[note.Pitch] == 0 && _right[note.Pitch] == 0;
        }

        public void Clear()
        {
            Array.Clear(_left, 0, _left.Length);
            Array.Clear(_right, 0, _right.Length);
        }
    }
}
