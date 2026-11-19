using System.Collections.Generic;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Teaching
{
    /// <summary>
    /// 乐谱级指法估算（2026-11-18）：给下落音符与五线谱标指法数字（1 拇指 … 5 小指）。
    /// <para>
    /// 规则（初学者教材里最常见的「五指位置」法）：每只手维持一个五指位置（拇指所在的白键序号），
    /// 新音落在位置内就用对应手指；超出位置时整体平移，让新音落在该手最自然的手指上
    /// （往外扩展时右手用 5 / 左手用 1，往回时右手用 1 / 左手用 5）。和弦按音高从低到高分配，
    /// 音数不足 5 时按音程跨度挑手指。不追求与演奏者动画逐音一致，目标是读谱时「顺手、不打结」。
    /// </para>
    /// </summary>
    public static class ScoreFingering
    {
        /// <summary>按 <paramref name="notes"/> 的下标返回指法（1–5），算不出时为 0。</summary>
        public static int[] Compute(IReadOnlyList<MidiNoteEvent> notes)
        {
            return PianoTeacher3D.Avatar.PianoFingeringSolver.SolveScore(notes);
        }

        private static readonly string[] Labels = { "", "1", "2", "3", "4", "5" };

        /// <summary>指法数字的缓存字符串（界面每帧刷新时不分配）。</summary>
        public static string Label(int finger) => finger >= 1 && finger <= 5 ? Labels[finger] : string.Empty;

        /// <summary>白键序号（黑键按其左侧白键算，C4 = 23）。</summary>
        public static int WhiteIndex(int pitch)
        {
            int octave = pitch / 12;
            int pc = pitch % 12;
            int[] map = { 0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6 };
            return octave * 7 + map[pc];
        }
    }
}
