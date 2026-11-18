using System.Collections.Generic;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Teaching
{
    /// <summary>
    /// 弹奏要点提示（2026-11-18）：在难点到来前 1.5 s 给一句针对性的提醒，同一类提示 20 s 内不重复。
    /// 难点从乐谱里识别：八度及以上的跨度、四个音以上的和弦、快速音群（每秒 8 个音以上）、
    /// 右手上行 / 左手下行的拇指穿越（音阶连续 4 个音以上同向级进）。
    /// <para>纯逻辑类，EditMode 可测。看不到学生真实的手，所以提示放在「难点之前」，不是「做错之后」。</para>
    /// </summary>
    public sealed class TeachingCoach
    {
        public enum Tip
        {
            None,
            Octave,
            Chord,
            FastRun,
            ThumbUnder
        }

        public static readonly string[] PostureTips =
        {
            "手腕与键盘齐平，不要塌腕，也不要高耸",
            "手指自然弯曲成拱形，像轻轻握着一个小球，用指尖触键",
            "肩膀和手肘放松，力量从手臂自然落到指尖",
            "坐在琴凳前半部分，前臂与键盘大致平行"
        };

        private readonly List<(float time, Tip tip)> _events = new List<(float, Tip)>();
        private readonly Dictionary<Tip, float> _lastShown = new Dictionary<Tip, float>();
        private int _cursor;

        public static string TextFor(Tip tip) => tip switch
        {
            Tip.Octave => "八度跨度：手腕放松、手掌撑开，用 1 指和 5 指",
            Tip.Chord => "和弦：手掌撑成拱形，几个手指同时落下",
            Tip.FastRun => "快速音群：手指贴近琴键，靠手腕轻轻带动",
            Tip.ThumbUnder => "音阶：拇指从 3 指下方穿过，手腕保持平稳",
            _ => string.Empty
        };

        /// <summary>分析乐谱，生成难点时间表。</summary>
        public void Analyze(IReadOnlyList<MidiNoteEvent> notes)
        {
            _events.Clear();
            _lastShown.Clear();
            _cursor = 0;
            if (notes == null || notes.Count == 0) return;
            foreach (var hand in new[] { NoteHand.Left, NoteHand.Right })
            {
                var line = new List<MidiNoteEvent>();
                foreach (var n in notes) if ((n.Hand == NoteHand.Left) == (hand == NoteHand.Left)) line.Add(n);
                line.Sort((a, b) => a.StartTime != b.StartTime ? a.StartTime.CompareTo(b.StartTime) : a.Pitch.CompareTo(b.Pitch));
                int run = 0, dir = 0;
                for (int i = 0; i < line.Count; i++)
                {
                    // 和弦与八度：同一时刻的音
                    int j = i;
                    while (j + 1 < line.Count && line[j + 1].StartTime - line[i].StartTime < 0.03f) j++;
                    int size = j - i + 1;
                    if (size >= 4) _events.Add((line[i].StartTime, Tip.Chord));
                    else if (size >= 2 && line[j].Pitch - line[i].Pitch >= 12) _events.Add((line[i].StartTime, Tip.Octave));

                    // 快速音群：接下来 1 s 内的起音数
                    int count = 0;
                    for (int k = i; k < line.Count && line[k].StartTime - line[i].StartTime < 1f; k++) count++;
                    if (count >= 8) _events.Add((line[i].StartTime, Tip.FastRun));

                    // 级进音阶（单音）
                    if (size == 1 && i > 0)
                    {
                        int step = line[i].Pitch - line[i - 1].Pitch;
                        int d = step > 0 && step <= 2 ? 1 : (step < 0 && step >= -2 ? -1 : 0);
                        if (d != 0 && d == dir) run++;
                        else { run = d != 0 ? 1 : 0; dir = d; }
                        bool outward = hand == NoteHand.Right ? dir > 0 : dir < 0;
                        if (run == 3 && outward) _events.Add((line[i - 3].StartTime, Tip.ThumbUnder));
                    }
                    i = j;
                }
            }
            _events.Sort((a, b) => a.time.CompareTo(b.time));
        }

        /// <summary>按当前乐曲时间取出该显示的提示（没有时返回 None）。</summary>
        public Tip Next(float now, float realTime)
        {
            while (_cursor < _events.Count && _events[_cursor].time < now) _cursor++;
            for (int i = _cursor; i < _events.Count; i++)
            {
                var e = _events[i];
                if (e.time > now + 1.5f) break;
                if (_lastShown.TryGetValue(e.tip, out float last) && realTime - last < 20f) continue;
                _lastShown[e.tip] = realTime;
                _cursor = i + 1;
                return e.tip;
            }
            return Tip.None;
        }

        /// <summary>跳转后重新定位。</summary>
        public void Seek(float now)
        {
            _cursor = 0;
            while (_cursor < _events.Count && _events[_cursor].time < now) _cursor++;
        }

        /// <summary>难点数量（诊断 / 测试用）。</summary>
        public int EventCount => _events.Count;
    }
}
