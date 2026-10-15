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

            foreach (var n in notes)
            {
                bool isLeft = n.Pitch < SplitPitch;
                if ((hand == PracticeHand.LeftOnly && isLeft) ||
                    (hand == PracticeHand.RightOnly && !isLeft))
                {
                    result.Add(n);
                }
            }
            return result;
        }
    }
}
