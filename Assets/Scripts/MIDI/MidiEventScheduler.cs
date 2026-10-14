using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.MIDI
{
    /// <summary>
    /// MIDI 事件调度器（方案 4.2 / 3.3）：根据统一播放时间调度 Note On / Note Off。
    /// 本身不持有时间，仅消费 <see cref="Core.LessonTimeline"/> 提供的当前时间。
    /// 通过 OnNoteOn / OnNoteOff 事件向外广播，供琴键、音频、IK 订阅。
    /// </summary>
    public class MidiEventScheduler : MonoBehaviour
    {
        public event System.Action<MidiNoteEvent> OnNoteOn;
        public event System.Action<MidiNoteEvent> OnNoteOff;

        private readonly List<(float time, bool isOn, MidiNoteEvent note)> _events = new();
        private int _cursor = 0;

        /// <summary>加载曲目的标准化音符事件，重建事件流。</summary>
        public void Load(IReadOnlyList<MidiNoteEvent> notes)
        {
            _events.Clear();
            _cursor = 0;
            if (notes == null) return;

            foreach (var n in notes)
            {
                _events.Add((n.StartTime, true, n));
                _events.Add((n.EndTime, false, n));
            }
            _events.Sort((a, b) => a.time.CompareTo(b.time));
        }

        public void Clear()
        {
            _events.Clear();
            _cursor = 0;
        }

        /// <summary>按当前时间推进，触发所有到期事件。</summary>
        public void Tick(float currentTime)
        {
            while (_cursor < _events.Count && _events[_cursor].time <= currentTime)
            {
                var e = _events[_cursor++];
                if (e.isOn) OnNoteOn?.Invoke(e.note);
                else OnNoteOff?.Invoke(e.note);
            }
        }

        /// <summary>
        /// Seek 后调用：重建 seek 点之前已按下且仍在持续的按键状态，
        /// 并将游标推进到 seek 点之后，保证键盘与手部状态正确。
        /// </summary>
        public void SeekTo(float currentTime)
        {
            _cursor = 0;
            for (int i = 0; i < _events.Count; i++)
            {
                var e = _events[i];
                if (e.isOn && e.note.StartTime <= currentTime && e.note.EndTime > currentTime)
                {
                    OnNoteOn?.Invoke(e.note);
                }
            }
            while (_cursor < _events.Count && _events[_cursor].time <= currentTime)
            {
                _cursor++;
            }
        }
    }
}
