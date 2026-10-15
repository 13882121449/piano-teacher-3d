using System;
using System.Collections.Generic;
using UnityEngine;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 节拍器（对应方案 2.2「节拍器」）。
    /// <para>
    /// 严格遵循方案 4.3：**不自建计时器**，由 <see cref="LessonTimeline"/> 的当前时间驱动，
    /// 拍点表由曲目 tempo map 生成，因此节拍与音乐、琴键、手部动作始终同源。
    /// </para>
    /// 击拍音为程序化合成（零第三方音频资产，符合方案「遵守资源许可证」原则）。
    /// </summary>
    public class Metronome : MonoBehaviour
    {
        /// <summary>拍点数量上限（防御异常 tempo 造成的超长生成循环）。</summary>
        public const int MaxBeats = 20000;

        private const int SampleRate = 44100;

        [SerializeField] private bool _enabled;
        [SerializeField] private int _beatsPerBar = 4;

        private readonly List<float> _beatTimes = new();
        private int _cursor;
        private float _lastTime;

        private AudioSource _source;
        private AudioClip _strongClip;
        private AudioClip _weakClip;

        /// <summary>是否发声（不影响游标推进，因此重新打开时不会补发历史拍点）。</summary>
        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>每小节拍数（4 = 4/4 拍）。下限 1，避免取模为 0。</summary>
        public int BeatsPerBar
        {
            get => _beatsPerBar;
            set => _beatsPerBar = value < 1 ? 1 : value;
        }

        public int BeatCount => _beatTimes.Count;
        public IReadOnlyList<float> BeatTimes => _beatTimes;

        /// <summary>每次击拍触发，参数为小节内拍序号（0 = 强拍）。</summary>
        public event Action<int> OnBeat;

        /// <summary>创建音频源与击拍音（幂等，可重复调用）。</summary>
        public void Initialize()
        {
            if (_source != null) return;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _strongClip = CreateClick(1320f, 0.045f);
            _weakClip = CreateClick(880f, 0.035f);
        }

        /// <summary>按 tempo map 生成拍点表；空曲目（总时长 ≤ 0）生成空表。</summary>
        public void Load(IReadOnlyList<TempoChange> tempos, float totalDuration)
        {
            _beatTimes.Clear();
            _beatTimes.AddRange(BuildBeatTimes(tempos, totalDuration));
            _cursor = 0;
            _lastTime = 0f;
        }

        /// <summary>停止 / 换曲 / 手动复位时调用：按给定时间重新定位游标（不补发历史拍点）。</summary>
        public void Reset(float time = 0f)
        {
            _lastTime = time < 0f ? 0f : time;
            _cursor = LowerBoundOfBeat(_lastTime);
        }

        /// <summary>由控制器每帧调用，传入统一时间轴的当前时间。</summary>
        public void Tick(float currentTime)
        {
            if (currentTime < _lastTime)
            {
                // 时间回退（循环跳转 / Seek 向前回退）：重新定位，不补发已跳过的拍点
                Reset(currentTime);
                return;
            }

            while (_cursor < _beatTimes.Count && _beatTimes[_cursor] <= currentTime)
            {
                if (_enabled) TriggerBeat(_cursor);
                _cursor++;
            }
            _lastTime = currentTime;
        }

        /// <summary>
        /// 由 tempo map 生成拍点时间表（秒）。纯静态、不依赖 UnityEngine，便于单元测试。
        /// tempo 缺失或非法时按 120 BPM 处理；拍点数受 <see cref="MaxBeats"/> 上限保护。
        /// </summary>
        public static List<float> BuildBeatTimes(IReadOnlyList<TempoChange> tempos, float totalDuration)
        {
            var beats = new List<float>();
            if (totalDuration <= 0f) return beats;   // 空曲目 / 无时长

            var segments = BuildSegments(tempos);
            double t = 0.0;
            int seg = 0;

            while (t <= totalDuration + 1e-3 && beats.Count < MaxBeats)
            {
                beats.Add((float)t);

                while (seg < segments.Count - 1 && t >= segments[seg].End) seg++;
                double beat = segments[seg].BeatSeconds;
                if (beat <= 1e-4 || double.IsNaN(beat) || double.IsInfinity(beat)) beat = 0.5;
                t += beat;
            }
            return beats;
        }

        // ==================== 内部 ====================

        private void TriggerBeat(int index)
        {
            int beatInBar = index % BeatsPerBar;
            var clip = beatInBar == 0 ? _strongClip : _weakClip;

            if (_source != null && clip != null)
                _source.PlayOneShot(clip, beatInBar == 0 ? 0.55f : 0.32f);

            OnBeat?.Invoke(beatInBar);
        }

        private int LowerBoundOfBeat(float time)
        {
            int lo = 0, hi = _beatTimes.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_beatTimes[mid] < time) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private readonly struct TempoSegment
        {
            public readonly double Start;
            public readonly double End;
            public readonly double BeatSeconds;

            public TempoSegment(double start, double end, double beatSeconds)
            {
                Start = start;
                End = end;
                BeatSeconds = beatSeconds;
            }
        }

        private static List<TempoSegment> BuildSegments(IReadOnlyList<TempoChange> tempos)
        {
            var segments = new List<TempoSegment>();

            if (tempos == null || tempos.Count == 0)
            {
                segments.Add(new TempoSegment(0, double.MaxValue, 0.5));
                return segments;
            }

            for (int i = 0; i < tempos.Count; i++)
            {
                double start = tempos[i].TimeSeconds;
                double end = (i + 1 < tempos.Count) ? tempos[i + 1].TimeSeconds : double.MaxValue;
                if (end <= start) continue; // 异常：时间点乱序/重复，跳过空段
                segments.Add(new TempoSegment(start, end, tempos[i].BeatSeconds));
            }

            if (segments.Count == 0) segments.Add(new TempoSegment(0, double.MaxValue, 0.5));
            return segments;
        }

        private static AudioClip CreateClick(float frequency, float seconds)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(SampleRate * seconds));
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-32f * t); // 快速衰减，模拟木质击拍
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.7f;
            }

            var clip = AudioClip.Create($"MetronomeClick_{frequency:0}", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
