using System;

namespace PianoTeacher3D.MIDI
{
    /// <summary>
    /// tempo 变化点（秒制）。由 <see cref="MidiParser"/> 在 tick→秒 转换时产出，
    /// 供节拍器与曲目分析使用，保证所有节奏相关模块共享同一份速度信息。
    /// </summary>
    [Serializable]
    public struct TempoChange
    {
        /// <summary>120 BPM（SMF 默认 tempo）。</summary>
        public const int DefaultMpq = 500000;

        /// <summary>该 tempo 生效的时间（秒）。</summary>
        public float TimeSeconds;

        /// <summary>每四分音符的微秒数（500000 = 120 BPM）。</summary>
        public int MicrosecondsPerQuarter;

        public TempoChange(float timeSeconds, int microsecondsPerQuarter)
        {
            TimeSeconds = timeSeconds < 0f ? 0f : timeSeconds;
            MicrosecondsPerQuarter = microsecondsPerQuarter > 0 ? microsecondsPerQuarter : DefaultMpq;
        }

        /// <summary>BPM（每分钟四分音符数）。非法 tempo 时回落到 120。</summary>
        public float Bpm => MicrosecondsPerQuarter > 0
            ? 60_000_000f / MicrosecondsPerQuarter
            : 120f;

        /// <summary>一拍的时长（秒）。</summary>
        public float BeatSeconds => MicrosecondsPerQuarter > 0
            ? MicrosecondsPerQuarter / 1_000_000f
            : 0.5f;

        public override string ToString() => $"t={TimeSeconds:F3}s bpm={Bpm:F1}";
    }
}
