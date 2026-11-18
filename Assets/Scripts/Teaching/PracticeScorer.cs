using System;
using System.Collections.Generic;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Teaching
{
    /// <summary>一次判定的结果。</summary>
    public enum Judgement
    {
        Perfect,   // 准确（±80 ms）
        Early,     // 偏早
        Late,      // 偏晚
        Miss,      // 漏弹
        Wrong      // 弹错 / 多弹
    }

    /// <summary>
    /// 练习评分与等待模式（2026-11-18）。纯逻辑类，不依赖场景，EditMode 可测。
    /// <para>
    /// <b>评分</b>：学生每次按键在 ±250 ms（按播放速度换算成乐曲时间）内找同音高、尚未判定的最近音符，
    /// |偏差| ≤ 80 ms 记「准确」，否则「偏早 / 偏晚」；窗口里没有匹配的按键记「弹错」；
    /// 音符过了窗口还没人弹记「漏弹」。漏弹与弹错打断连击。
    /// </para>
    /// <para>
    /// <b>等待模式</b>：时间轴走到下一组（30 ms 内同时起音）音符时停住，学生把这一组音都弹到后放开；
    /// 等待模式下按键只判对错，不判早晚。
    /// </para>
    /// </summary>
    public sealed class PracticeScorer
    {
        public const float PerfectWindow = 0.08f;
        public const float MatchWindow = 0.25f;

        private readonly List<MidiNoteEvent> _notes = new List<MidiNoteEvent>();
        private bool[] _judged = Array.Empty<bool>();
        private int _missCursor;
        private int _waitIndex;
        private readonly HashSet<int> _waitPending = new HashSet<int>();

        public int Perfect { get; private set; }
        public int Good { get; private set; }
        public int Missed { get; private set; }
        public int Wrong { get; private set; }
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }

        /// <summary>学生按过的键数（用来判断「这一遍有没有真的弹」）。</summary>
        public int InputCount { get; private set; }

        /// <summary>等待模式开关。</summary>
        public bool WaitMode { get; set; }

        /// <summary>等待模式下当前是否在等学生。</summary>
        public bool IsHolding { get; private set; }

        /// <summary>每次判定后触发：结果、音高、时间偏差（秒，正 = 偏晚）。</summary>
        public event Action<Judgement, int, float> Judged;

        /// <summary>命中率 0–1：(准确 + 早晚) / (准确 + 早晚 + 漏弹 + 弹错)。</summary>
        public float Accuracy
        {
            get
            {
                int total = Perfect + Good + Missed + Wrong;
                return total == 0 ? 0f : (Perfect + Good) / (float)total;
            }
        }

        /// <summary>按命中率与速度给星：≥80% 一星，≥90% 二星，≥97% 且原速三星。</summary>
        public static int StarsFor(float accuracy, float speed)
        {
            if (accuracy >= 0.97f && speed >= 0.999f) return 3;
            if (accuracy >= 0.90f) return 2;
            if (accuracy >= 0.80f) return 1;
            return 0;
        }

        /// <summary>载入要练的音符（已按练习手过滤）。重置全部统计。</summary>
        public void Load(IEnumerable<MidiNoteEvent> notes)
        {
            _notes.Clear();
            if (notes != null) _notes.AddRange(notes);
            _notes.Sort((a, b) => a.StartTime != b.StartTime ? a.StartTime.CompareTo(b.StartTime) : a.Pitch.CompareTo(b.Pitch));
            _judged = new bool[_notes.Count];
            Reset(0f);
        }

        /// <summary>从 <paramref name="time"/> 开始重新计分（跳转 / 重播 / 循环回到 A 点时调用）。</summary>
        public void Reset(float time)
        {
            Array.Clear(_judged, 0, _judged.Length);
            Perfect = Good = Missed = Wrong = Streak = InputCount = 0;
            _missCursor = 0;
            while (_missCursor < _notes.Count && _notes[_missCursor].StartTime < time - 0.001f)
            {
                _judged[_missCursor] = true;
                _missCursor++;
            }
            _waitIndex = _missCursor;
            _waitPending.Clear();
            IsHolding = false;
        }

        /// <summary>
        /// 每帧推进：判漏弹、计算等待模式是否该停住。返回 true 表示时间轴应停在原地。
        /// <paramref name="speed"/> 用于把判定窗口从「真实秒」换算成乐曲时间。
        /// </summary>
        public bool Advance(float time, float speed)
        {
            float window = MatchWindow * Math.Max(0.25f, speed);
            if (!WaitMode)
            {
                while (_missCursor < _notes.Count && _notes[_missCursor].StartTime < time - window)
                {
                    if (!_judged[_missCursor])
                    {
                        _judged[_missCursor] = true;
                        Missed++;
                        Streak = 0;
                        Judged?.Invoke(Judgement.Miss, _notes[_missCursor].Pitch, 0f);
                    }
                    _missCursor++;
                }
                IsHolding = false;
                return false;
            }

            // 等待模式：找到下一组还没弹完的音
            while (_waitIndex < _notes.Count && _judged[_waitIndex]) _waitIndex++;
            if (_waitIndex >= _notes.Count)
            {
                IsHolding = false;
                return false;
            }
            float groupTime = _notes[_waitIndex].StartTime;
            if (_waitPending.Count == 0)
            {
                for (int i = _waitIndex; i < _notes.Count && _notes[i].StartTime - groupTime < 0.03f; i++)
                    if (!_judged[i]) _waitPending.Add(i);
            }
            IsHolding = time >= groupTime - 0.01f && _waitPending.Count > 0;
            return IsHolding;
        }

        /// <summary>学生按下一个键（乐曲时间 <paramref name="time"/>）。</summary>
        public void OnInput(int pitch, float time, float speed)
        {
            InputCount++;
            if (WaitMode)
            {
                foreach (int i in _waitPending)
                {
                    if (_notes[i].Pitch != pitch) continue;
                    _judged[i] = true;
                    _waitPending.Remove(i);
                    Perfect++;
                    Bump();
                    Judged?.Invoke(Judgement.Perfect, pitch, 0f);
                    return;
                }
                Wrong++;
                Streak = 0;
                Judged?.Invoke(Judgement.Wrong, pitch, 0f);
                return;
            }

            float window = MatchWindow * Math.Max(0.25f, speed);
            int best = -1;
            float bestDelta = float.MaxValue;
            for (int i = Math.Max(0, _missCursor - 8); i < _notes.Count; i++)
            {
                float delta = time - _notes[i].StartTime;
                if (delta < -window) break;
                if (_judged[i] || _notes[i].Pitch != pitch || delta > window) continue;
                if (Math.Abs(delta) < Math.Abs(bestDelta))
                {
                    best = i;
                    bestDelta = delta;
                }
            }
            if (best < 0)
            {
                Wrong++;
                Streak = 0;
                Judged?.Invoke(Judgement.Wrong, pitch, 0f);
                return;
            }
            _judged[best] = true;
            float real = bestDelta / Math.Max(0.25f, speed);   // 换回真实秒
            Judgement j = Math.Abs(real) <= PerfectWindow ? Judgement.Perfect : (real < 0f ? Judgement.Early : Judgement.Late);
            if (j == Judgement.Perfect) Perfect++;
            else Good++;
            Bump();
            Judged?.Invoke(j, pitch, real);
        }

        private void Bump()
        {
            Streak++;
            if (Streak > BestStreak) BestStreak = Streak;
        }
    }
}
