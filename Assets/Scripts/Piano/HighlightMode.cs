namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 琴键高亮提示模式（对应方案 2.2「音符或琴键高亮提示」）。
    /// 「预提示」= 音符开始前提前亮起；「发声」= 音符正在演奏期间亮起。
    /// </summary>
    public enum HighlightMode
    {
        /// <summary>关闭高亮。</summary>
        Off = 0,

        /// <summary>仅预提示即将按下的音符（跟弹学习用）。</summary>
        Upcoming = 1,

        /// <summary>仅高亮正在发声的音符。</summary>
        Active = 2,

        /// <summary>预提示 + 发声高亮（默认教学模式）。</summary>
        Both = 3
    }
}
