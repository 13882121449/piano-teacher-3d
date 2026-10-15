using System.Collections.Generic;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 曲目分析器（对应方案 2.2「根据 MIDI、乐谱或输入设备数据提供练习反馈」中
    /// 的「根据 MIDI」数据源），产出统计指标与规则式练习建议。
    /// <para>纯静态、不依赖 UnityEngine，便于单元测试。</para>
    /// </summary>
    public static class SongAnalyzer
    {
        /// <summary>和弦判定容差：起点差异在此秒数内的音符视为同时按下。</summary>
        public const float ChordToleranceSeconds = 0.02f;

        /// <summary>单手占比达到该值时给出分手练习建议。</summary>
        public const float ImbalancedHandRatio = 0.6f;

        /// <summary>黑键占比达到该值时给出指法提醒。</summary>
        public const float HeavyBlackKeyRatio = 0.3f;

        /// <summary>和弦跨度达到该半音数时给出放慢提醒（12 = 八度）。</summary>
        public const int LargeSpanSemitones = 12;

        /// <summary>相邻音符间隔小于该秒数时视为快速片段。</summary>
        public const float FastNoteGapSeconds = 0.12f;

        /// <summary>
        /// 判定「不同起始时刻」的最小间隔（秒）。
        /// 用于排除同一和弦内起点完全相同（gap ≈ 0）的音符——否则和弦会把
        /// 「最快音符间隔」掩盖为 0，导致快速片段建议失效。
        /// </summary>
        public const float MinDistinctStartGap = 0.001f;

        /// <summary>音域达到该八度数时提示手位移动。</summary>
        public const float WideOctaveRange = 3f;

        /// <summary>
        /// 分析曲目。任何输入（null / 空列表）都返回非 null 结果，不会抛异常。
        /// </summary>
        /// <param name="notes">标准化音符事件（可为 null，会按空曲目处理）。</param>
        /// <param name="tempos">tempo 变化点（可为 null，按 120 BPM 处理）。</param>
        /// <param name="totalDuration">总时长（秒）；≤ 0 时由音符结束时间推算。</param>
        public static SongAnalysis Analyze(IReadOnlyList<MidiNoteEvent> notes,
            IReadOnlyList<TempoChange> tempos = null, float totalDuration = 0f)
        {
            var analysis = new SongAnalysis();
            ApplyTempo(analysis, tempos);

            if (notes == null || notes.Count == 0)
            {
                analysis.TotalDuration = totalDuration > 0f ? totalDuration : 0f;
                analysis.Suggestions.Add("未加载曲目或曲目为空，暂无可分析内容。");
                return analysis;
            }

            // 使用排序副本，不修改调用方列表
            var sorted = new List<MidiNoteEvent>(notes);
            sorted.Sort((x, y) => x.StartTime.CompareTo(y.StartTime));

            analysis.NoteCount = sorted.Count;

            int lowest = int.MaxValue;
            int highest = int.MinValue;
            float fastestGap = float.MaxValue;
            float maxEnd = 0f;

            for (int i = 0; i < sorted.Count; i++)
            {
                var n = sorted[i];

                if (PianoNoteFilter.IsLeftHand(n.Pitch)) analysis.LeftHandCount++;
                else analysis.RightHandCount++;

                if (PianoNoteFilter.IsBlackKey(n.Pitch)) analysis.BlackKeyCount++;

                if (n.Pitch < lowest) lowest = n.Pitch;
                if (n.Pitch > highest) highest = n.Pitch;

                if (i > 0)
                {
                    float gap = n.StartTime - sorted[i - 1].StartTime;
                    // 只统计不同起始时刻之间的间隔（同和弦的 gap≈0 不参与，避免掩盖真实快速片段）
                    if (gap > MinDistinctStartGap && gap < fastestGap) fastestGap = gap;
                }

                float end = n.EndTime;
                if (end > maxEnd) maxEnd = end;
            }

            analysis.LowestPitch = lowest;
            analysis.HighestPitch = highest;
            analysis.FastestNoteGap = (sorted.Count > 1 && fastestGap < float.MaxValue) ? fastestGap : 0f;
            analysis.TotalDuration = totalDuration > 0f ? totalDuration : maxEnd;

            AnalyzeChords(analysis, sorted);
            BuildSuggestions(analysis);
            return analysis;
        }

        // ==================== 内部 ====================

        private static void AnalyzeChords(SongAnalysis analysis, List<MidiNoteEvent> sorted)
        {
            int groupStart = 0;
            while (groupStart < sorted.Count)
            {
                float groupTime = sorted[groupStart].StartTime;
                int groupEnd = groupStart;

                while (groupEnd + 1 < sorted.Count &&
                       sorted[groupEnd + 1].StartTime - groupTime <= ChordToleranceSeconds)
                {
                    groupEnd++;
                }

                int size = groupEnd - groupStart + 1;
                if (size >= 2)
                {
                    analysis.ChordCount++;
                    if (size > analysis.MaxChordSize) analysis.MaxChordSize = size;

                    int min = int.MaxValue, max = int.MinValue;
                    for (int i = groupStart; i <= groupEnd; i++)
                    {
                        if (sorted[i].Pitch < min) min = sorted[i].Pitch;
                        if (sorted[i].Pitch > max) max = sorted[i].Pitch;
                    }

                    int span = max - min;
                    if (span > analysis.MaxSpanSemitones) analysis.MaxSpanSemitones = span;
                }

                groupStart = groupEnd + 1;
            }
        }

        private static void ApplyTempo(SongAnalysis analysis, IReadOnlyList<TempoChange> tempos)
        {
            if (tempos == null || tempos.Count == 0) return; // 默认即 120/120

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < tempos.Count; i++)
            {
                float bpm = tempos[i].Bpm;
                if (bpm < min) min = bpm;
                if (bpm > max) max = bpm;
            }

            if (min <= float.MaxValue && max >= float.MinValue && min > 0f)
            {
                analysis.MinBpm = min;
                analysis.MaxBpm = max;
            }
        }

        private static void BuildSuggestions(SongAnalysis a)
        {
            if (a.IsEmpty) return;

            int leftPercent = Percent(a.LeftHandRatio);
            int rightPercent = 100 - leftPercent;

            if (a.LeftHandRatio >= ImbalancedHandRatio)
                a.Suggestions.Add($"左手音符占 {leftPercent}%，建议先用「左手练习」分段熟悉低声部。");
            else if (a.RightHandRatio >= ImbalancedHandRatio)
                a.Suggestions.Add($"右手音符占 {rightPercent}%，建议先用「右手练习」分段熟悉旋律。");
            else
                a.Suggestions.Add($"左右手分布均衡（左 {leftPercent}% / 右 {rightPercent}%），可双手合练。");

            if (a.BlackKeyRatio >= HeavyBlackKeyRatio)
                a.Suggestions.Add($"黑键比例 {Percent(a.BlackKeyRatio)}%，注意手型与指法位置。");

            if (a.ChordCount > 0)
                a.Suggestions.Add($"含 {a.ChordCount} 处和弦（最大 {a.MaxChordSize} 音），建议先单手分解再合手。");

            if (a.MaxSpanSemitones >= LargeSpanSemitones)
                a.Suggestions.Add($"存在 {a.MaxSpanSemitones} 半音的大跨度和弦（≥ 八度），建议放慢到 0.75x 以下。");

            if (a.FastestNoteGap > 0f && a.FastestNoteGap < FastNoteGapSeconds)
                a.Suggestions.Add($"存在快速音符（最小间隔 {(int)(a.FastestNoteGap * 1000f + 0.5f)} ms），建议先用 0.5x 慢速。");

            if (a.OctaveRange >= WideOctaveRange)
                a.Suggestions.Add($"音域跨度约 {a.OctaveRange:0.0} 个八度，注意手位移动，可按循环片段分段练习。");

            if (a.MaxBpm - a.MinBpm > 1f)
                a.Suggestions.Add($"曲目含速度变化（{a.MinBpm:0}–{a.MaxBpm:0} BPM），建议先按统一慢速练习。");

            if (a.Suggestions.Count == 0)
                a.Suggestions.Add("曲目结构简单，可直接原速练习。");
        }

        /// <summary>比例 → 百分比整数（四舍五入，纯 C# 实现，避免引入 UnityEngine 依赖）。</summary>
        private static int Percent(float ratio) => (int)(ratio * 100f + 0.5f);
    }
}
