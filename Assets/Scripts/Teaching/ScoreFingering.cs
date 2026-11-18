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
            var result = new int[notes?.Count ?? 0];
            if (notes == null || notes.Count == 0) return result;
            Assign(notes, result, NoteHand.Left);
            Assign(notes, result, NoteHand.Right);
            return result;
        }

        private static void Assign(IReadOnlyList<MidiNoteEvent> notes, int[] result, NoteHand hand)
        {
            bool left = hand == NoteHand.Left;
            var order = new List<int>();
            for (int i = 0; i < notes.Count; i++)
            {
                bool isLeft = notes[i].Hand == NoteHand.Left;
                if (isLeft == left) order.Add(i);
            }
            order.Sort((a, b) =>
            {
                int c = notes[a].StartTime.CompareTo(notes[b].StartTime);
                return c != 0 ? c : notes[a].Pitch.CompareTo(notes[b].Pitch);
            });

            int anchor = int.MinValue;   // 拇指所在白键序号（右手）/ 小指所在白键序号（左手）
            int k = 0;
            while (k < order.Count)
            {
                // 同时起音（30 ms 内）的一组
                int start = k;
                float t0 = notes[order[k]].StartTime;
                while (k < order.Count && notes[order[k]].StartTime - t0 < 0.03f) k++;
                int count = k - start;

                if (count == 1)
                {
                    int i = order[start];
                    int w = WhiteIndex(notes[i].Pitch);
                    if (anchor == int.MinValue) anchor = FirstAnchor(notes, order, start, w);
                    int finger = left ? 5 - (w - anchor) : w - anchor + 1;
                    if (finger < 1 || finger > 5)
                    {
                        bool outward = left ? finger > 5 : finger > 5;
                        // 右手往上 / 左手往下扩展：落到 5 指（右）或 5 指（左，低音侧）；往回落到 1 指
                        if (!left) anchor = outward ? w - 4 : w;
                        else anchor = finger > 5 ? w : w - 4;
                        finger = left ? 5 - (w - anchor) : w - anchor + 1;
                    }
                    result[i] = Clamp(finger);
                }
                else
                {
                    // 和弦：低 → 高；右手 1 起，左手 5 起；按跨度把中间音分到 2–4
                    var group = order.GetRange(start, count);
                    group.Sort((a, b) => notes[a].Pitch.CompareTo(notes[b].Pitch));
                    int low = WhiteIndex(notes[group[0]].Pitch);
                    int high = WhiteIndex(notes[group[count - 1]].Pitch);
                    int span = System.Math.Max(1, high - low);
                    for (int g = 0; g < count; g++)
                    {
                        int w = WhiteIndex(notes[group[g]].Pitch);
                        float pos = (w - low) / (float)span;            // 0 … 1
                        int finger = count >= 5 ? g + 1 : 1 + (int)System.Math.Round(pos * 4f);
                        if (left) finger = 6 - finger;
                        result[group[g]] = Clamp(finger);
                    }
                    // 去重：相邻音分到同一手指时往外挪
                    for (int g = 1; g < count; g++)
                    {
                        int prev = result[group[g - 1]];
                        int cur = result[group[g]];
                        if (!left && cur <= prev) result[group[g]] = Clamp(prev + 1);
                        if (left && cur >= prev) result[group[g]] = Clamp(prev - 1);
                    }
                    anchor = left ? high - 4 : low;
                }
            }
        }

        /// <summary>
        /// 首个五指位置：看接下来最多 6 个单音的白键跨度，跨度在五度以内就让最低音落在拇指（右手）/
        /// 小指（左手）上；跨度更大时让首音落在中指，给两边都留出余地。
        /// </summary>
        private static int FirstAnchor(IReadOnlyList<MidiNoteEvent> notes, List<int> order, int start, int first)
        {
            int min = first, max = first;
            for (int k = start; k < order.Count && k < start + 6; k++)
            {
                int w = WhiteIndex(notes[order[k]].Pitch);
                if (w < min) min = w;
                if (w > max) max = w;
            }
            return max - min <= 4 ? min : first - 2;
        }

        private static readonly string[] Labels = { "", "1", "2", "3", "4", "5" };

        /// <summary>指法数字的缓存字符串（界面每帧刷新时不分配）。</summary>
        public static string Label(int finger) => finger >= 1 && finger <= 5 ? Labels[finger] : string.Empty;

        private static int Clamp(int finger) => finger < 1 ? 1 : (finger > 5 ? 5 : finger);

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
