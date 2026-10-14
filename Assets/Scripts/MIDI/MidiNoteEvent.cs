using System;

namespace PianoTeacher3D.MIDI
{
    /// <summary>
    /// 标准化 MIDI 音符事件（对应方案 3.3 的最小字段集）。
    /// 时间单位为秒，由 MidiParser 将 tick 转换为秒。
    /// </summary>
    [Serializable]
    public struct MidiNoteEvent
    {
        /// <summary>MIDI 音高（0-127，中央 C = 60）。</summary>
        public int Pitch;

        /// <summary>起始时间（秒）。</summary>
        public float StartTime;

        /// <summary>持续时间（秒），由 Note On / Note Off 推导。</summary>
        public float Duration;

        /// <summary>力度（0-127）。</summary>
        public int Velocity;

        /// <summary>轨道号。</summary>
        public int Track;

        /// <summary>通道号（0-15）。</summary>
        public int Channel;

        public float EndTime => StartTime + Duration;

        public MidiNoteEvent(int pitch, float startTime, float duration, int velocity, int track, int channel)
        {
            Pitch = pitch;
            StartTime = startTime;
            Duration = duration;
            Velocity = velocity;
            Track = track;
            Channel = channel;
        }

        public override string ToString()
        {
            return $"pitch={Pitch} start={StartTime:F3}s dur={Duration:F3}s vel={Velocity} trk={Track} ch={Channel}";
        }
    }
}
