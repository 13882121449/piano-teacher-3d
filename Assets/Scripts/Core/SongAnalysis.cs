using System.Collections.Generic;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 曲目分析结果（对应方案 2.2「根据 MIDI…提供练习反馈」）。
    /// 纯数据容器，不依赖 UnityEngine。
    /// </summary>
    public sealed class SongAnalysis
    {
        /// <summary>音符总数。</summary>
        public int NoteCount { get; internal set; }

        /// <summary>左手音符数（音高 &lt; C4）。</summary>
        public int LeftHandCount { get; internal set; }

        /// <summary>右手音符数（音高 ≥ C4）。</summary>
        public int RightHandCount { get; internal set; }

        /// <summary>黑键音符数。</summary>
        public int BlackKeyCount { get; internal set; }

        /// <summary>和弦（同时按下 ≥ 2 音）处数。</summary>
        public int ChordCount { get; internal set; }

        /// <summary>最大和弦音数。</summary>
        public int MaxChordSize { get; internal set; }

        /// <summary>同一和弦内最大音程跨度（半音）。</summary>
        public int MaxSpanSemitones { get; internal set; }

        /// <summary>最低音高（无音符时为 -1）。</summary>
        public int LowestPitch { get; internal set; } = -1;

        /// <summary>最高音高（无音符时为 -1）。</summary>
        public int HighestPitch { get; internal set; } = -1;

        /// <summary>相邻音符起点的最小间隔（秒）。单音符或无音符时为 0。</summary>
        public float FastestNoteGap { get; internal set; }

        /// <summary>最低 BPM（无 tempo 信息时为 120）。</summary>
        public float MinBpm { get; internal set; } = 120f;

        /// <summary>最高 BPM（无 tempo 信息时为 120）。</summary>
        public float MaxBpm { get; internal set; } = 120f;

        /// <summary>曲目总时长（秒）。</summary>
        public float TotalDuration { get; internal set; }

        /// <summary>规则式练习建议（保证非 null，可能为空列表）。</summary>
        public List<string> Suggestions { get; } = new List<string>();

        public bool IsEmpty => NoteCount == 0;

        /// <summary>音域宽度（半音）。</summary>
        public int PitchRange => (LowestPitch < 0 || HighestPitch < 0) ? 0 : HighestPitch - LowestPitch;

        /// <summary>音域宽度（八度）。</summary>
        public float OctaveRange => PitchRange / 12f;

        public float LeftHandRatio => NoteCount > 0 ? LeftHandCount / (float)NoteCount : 0f;
        public float RightHandRatio => NoteCount > 0 ? RightHandCount / (float)NoteCount : 0f;
        public float BlackKeyRatio => NoteCount > 0 ? BlackKeyCount / (float)NoteCount : 0f;

        /// <summary>双手负担差异（0 = 完全均衡，1 = 完全偏向单手）。</summary>
        public float HandImbalance => NoteCount > 0
            ? System.Math.Abs(LeftHandCount - RightHandCount) / (float)NoteCount
            : 0f;
    }
}
