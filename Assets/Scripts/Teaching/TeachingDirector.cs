using System;
using System.Collections.Generic;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;
using UnityEngine;

namespace PianoTeacher3D.Teaching
{
    /// <summary>
    /// 教学总控（2026-11-18）：把学生输入、评分、等待模式、下落音符、琴键音名、难点提示、
    /// A-B 循环与渐进提速、练习记录串起来。挂在课程会话对象上，由 <see cref="LessonSessionController"/> 创建。
    /// <para>设置用 PlayerPrefs 保存（前缀 <c>teach.</c>），下次打开保持。</para>
    /// </summary>
    public sealed class TeachingDirector : MonoBehaviour
    {
        /// <summary>电脑键盘弹奏是否开启（开启时课程界面的字母快捷键让位）。</summary>
        public static bool KeyboardPlayActive { get; private set; }

        public struct RunResult
        {
            public float Accuracy;
            public int Stars;
            public bool Improved;
            public int BestStreak;
            public float Speed;
        }

        private LessonSessionController _session;
        private PianoKeyMap _keys;
        private PracticeInput _input;
        private FallingNotesView _falling;
        private KeyNameLabels _names;
        private readonly List<MidiNoteEvent> _practice = new List<MidiNoteEvent>();
        private int[] _fingers = Array.Empty<int>();
        private float _lastNow;
        private float _lastUserInput = -100f;
        private float? _loopA;
        private bool _wasPlaying;

        public PracticeScorer Scorer { get; } = new PracticeScorer();
        public TeachingCoach Coach { get; } = new TeachingCoach();

        /// <summary>当前练习音符（已按练习手过滤、按时间排序）与对应指法，五线谱共用。</summary>
        public IReadOnlyList<MidiNoteEvent> PracticeNotes => _practice;
        public int[] Fingering => _fingers;

        public bool FallingNotes { get; private set; } = true;
        public bool StaffVisible { get; private set; }
        public NoteNameStyle NoteNames { get; private set; } = NoteNameStyle.Off;
        public bool WaitMode { get; private set; }
        public bool KeyboardPlay { get; private set; }
        public bool SpeedRamp { get; private set; }
        public float? LoopA => _loopA;
        public bool HasABLoop => _session != null && _session.IsLooping && _session.LoopStart > 0.001f;
        public string MidiDevice => _input != null ? _input.DeviceName : string.Empty;

        public event Action<Judgement, int, float> Judged;
        public event Action<TeachingCoach.Tip> TipRaised;
        public event Action<RunResult> RunFinished;
        public event Action SettingsChanged;

        public void Initialize(LessonSessionController session, PianoKeyMap keys)
        {
            _session = session;
            _keys = keys;
            FallingNotes = PlayerPrefs.GetInt("teach.falling", 1) == 1;
            StaffVisible = PlayerPrefs.GetInt("teach.staff", 0) == 1;
            NoteNames = (NoteNameStyle)Mathf.Clamp(PlayerPrefs.GetInt("teach.names", 0), 0, 2);
            WaitMode = PlayerPrefs.GetInt("teach.wait", 0) == 1;
            KeyboardPlay = PlayerPrefs.GetInt("teach.keyboard", 0) == 1;
            SpeedRamp = PlayerPrefs.GetInt("teach.ramp", 0) == 1;
            Scorer.WaitMode = WaitMode;
            KeyboardPlayActive = KeyboardPlay;

            _input = new PracticeInput { KeyboardEnabled = KeyboardPlay };
            _input.NoteOn += PlayUserNote;
            _input.NoteOff += ReleaseUserNote;
            Scorer.Judged += (j, p, d) => Judged?.Invoke(j, p, d);

            if (_keys != null)
            {
                _falling = new GameObject("FallingNotes").AddComponent<FallingNotesView>();
                _falling.Initialize(_keys);
                _falling.gameObject.SetActive(FallingNotes);
                _names = new GameObject("KeyNameLabels").AddComponent<KeyNameLabels>();
                _names.transform.SetParent(_keys.transform, false);
                _names.Initialize(_keys);
                _names.SetStyle(NoteNames);
            }

            if (_session != null)
            {
                _session.OnSongLoaded += ReloadNotes;
                _session.OnPracticeHandChanged += _ => ReloadNotes();
            }
            if (_session != null) _session.OnLooped += OnLooped;
            ReloadNotes();
        }

        private void OnDestroy()
        {
            KeyboardPlayActive = false;
            _input?.Dispose();
            if (_session != null)
            {
                _session.OnLooped -= OnLooped;
                _session.SetTeachingHold(false);
            }
        }

        // ==================== 设置 ====================

        public void SetFallingNotes(bool on)
        {
            FallingNotes = on;
            PlayerPrefs.SetInt("teach.falling", on ? 1 : 0);
            if (_falling != null) _falling.gameObject.SetActive(on);
            SettingsChanged?.Invoke();
        }

        public void SetStaffVisible(bool on)
        {
            StaffVisible = on;
            PlayerPrefs.SetInt("teach.staff", on ? 1 : 0);
            SettingsChanged?.Invoke();
        }

        public void CycleNoteNames()
        {
            NoteNames = (NoteNameStyle)(((int)NoteNames + 1) % 3);
            PlayerPrefs.SetInt("teach.names", (int)NoteNames);
            _names?.SetStyle(NoteNames);
            SettingsChanged?.Invoke();
        }

        public void SetWaitMode(bool on)
        {
            WaitMode = on;
            Scorer.WaitMode = on;
            Scorer.Reset(_session != null ? _session.CurrentTime : 0f);
            PlayerPrefs.SetInt("teach.wait", on ? 1 : 0);
            if (!on) _session?.SetTeachingHold(false);
            SettingsChanged?.Invoke();
        }

        public void SetKeyboardPlay(bool on)
        {
            KeyboardPlay = on;
            KeyboardPlayActive = on;
            if (_input != null) _input.KeyboardEnabled = on;
            PlayerPrefs.SetInt("teach.keyboard", on ? 1 : 0);
            SettingsChanged?.Invoke();
        }

        public void SetSpeedRamp(bool on)
        {
            SpeedRamp = on;
            PlayerPrefs.SetInt("teach.ramp", on ? 1 : 0);
            if (on && _session != null && HasABLoop) _session.SetSpeed(0.6f);
            SettingsChanged?.Invoke();
        }

        /// <summary>A-B 循环：第一次点记 A，第二次点记 B 并开始循环，再点清除。</summary>
        public void StepABLoop()
        {
            if (_session == null || !_session.HasSong) return;
            float now = _session.CurrentTime;
            if (HasABLoop)
            {
                _session.ClearLoop();
                _loopA = null;
            }
            else if (!_loopA.HasValue)
            {
                _loopA = now;
            }
            else
            {
                float a = Mathf.Min(_loopA.Value, now);
                float b = Mathf.Max(_loopA.Value, now);
                if (b - a < 0.5f) b = Mathf.Min(_session.TotalDuration, a + 2f);
                _session.SetLoop(Mathf.Max(0.001f, a), b);
                _loopA = null;
                if (SpeedRamp) _session.SetSpeed(0.6f);
                Scorer.Reset(a);
                _session.Seek(a);
            }
            SettingsChanged?.Invoke();
        }

        /// <summary>学生在弹（等待模式，或最近 4 s 内有输入）时，示范演奏不再替练习手出声，避免两份声音叠在一起。</summary>
        public bool ShouldMuteDemo(MidiNoteEvent note)
        {
            bool userPlaying = WaitMode || Time.unscaledTime - _lastUserInput < 4f;
            if (!userPlaying || _session == null) return false;
            var hand = _session.PracticeHand;
            if (hand == PracticeHand.Both) return true;
            return (hand == PracticeHand.LeftOnly) == (note.Hand == NoteHand.Left);
        }

        // ==================== 每帧 ====================

        private void Update()
        {
            _input?.Poll();
            if (_session == null || !_session.HasSong)
            {
                _session?.SetTeachingHold(false);
                return;
            }

            float now = _session.CurrentTime;
            bool playing = _session.PlaybackState == PlaybackState.Playing && !_session.IsPreparing;
            float speed = _session.PlaybackSpeed;

            // 跳转（往回或一次跳过 1 s 以上）：评分与提示重新对齐
            if (now < _lastNow - 0.05f || now > _lastNow + 1.0f)
            {
                Scorer.Reset(now);
                Coach.Seek(now);
            }
            _lastNow = now;

            if (_falling != null && _falling.gameObject.activeSelf) _falling.Refresh(now);

            if (playing)
            {
                // 只在学生真的在弹（等待模式，或 8 s 内有按键）时判漏弹；只看示范时不出「漏弹」
                bool engaged = WaitMode || (Scorer.InputCount > 0 && Time.unscaledTime - _lastUserInput < 8f);
                bool hold = engaged && Scorer.Advance(now, speed);
                _session.SetTeachingHold(WaitMode && hold);
                PracticeProgressStore.Shared.AddPracticeSeconds(Time.unscaledDeltaTime);
                var tip = Coach.Next(now, Time.unscaledTime);
                if (tip != TeachingCoach.Tip.None) TipRaised?.Invoke(tip);
            }
            else
            {
                _session.SetTeachingHold(false);
            }

            // 播到结尾（时间轴停在终点）：结算这一遍
            if (_wasPlaying && _session.PlaybackState == PlaybackState.Paused && now >= _session.TotalDuration - 0.05f) FinishRun();
            _wasPlaying = playing;
        }

        /// <summary>学生按下一个键（MIDI / 电脑键盘 / 屏幕键盘共用入口）：出声、动键、计分。</summary>
        public void PlayUserNote(int pitch, int velocity)
        {
            float previousInput = _lastUserInput;
            _lastUserInput = Time.unscaledTime;
            if (_session == null) return;
            _session.PlayGuidedKey(pitch, velocity);
            if (_session.HasSong && _session.PlaybackState == PlaybackState.Playing)
            {
                // 刚开始弹（或停了一阵又接着弹）：从当前位置重新计分，之前只看示范的部分不算漏弹
                if (!WaitMode && (Scorer.InputCount == 0 || Time.unscaledTime - previousInput > 8f))
                    Scorer.Reset(_session.CurrentTime);
                Scorer.OnInput(pitch, _session.CurrentTime, _session.PlaybackSpeed);
            }
        }

        /// <summary>学生抬起一个键。</summary>
        public void ReleaseUserNote(int pitch)
        {
            _session?.ReleaseGuidedKey(pitch);
        }

        private void OnLooped(float start)
        {
            FinishRun();
            Scorer.Reset(start);
            Coach.Seek(start);
            if (SpeedRamp && _session != null && _session.PlaybackSpeed < 0.999f)
                _session.SetSpeed(Mathf.Min(1f, _session.PlaybackSpeed + 0.05f));
        }

        private void FinishRun()
        {
            if (Scorer.InputCount < 4 || _session == null) return;
            float accuracy = Scorer.Accuracy;
            float speed = _session.PlaybackSpeed;
            string key = !string.IsNullOrEmpty(_session.CurrentSongId) ? _session.CurrentSongId : _session.CurrentSongName;
            bool improved = PracticeProgressStore.Shared.RecordRun(key, accuracy, speed);
            RunFinished?.Invoke(new RunResult
            {
                Accuracy = accuracy,
                Stars = PracticeScorer.StarsFor(accuracy, speed),
                Improved = improved,
                BestStreak = Scorer.BestStreak,
                Speed = speed
            });
            Scorer.Reset(_session != null ? _session.CurrentTime : 0f);
        }

        private void ReloadNotes()
        {
            _practice.Clear();
            var all = _session != null ? _session.SongNotes : null;
            if (all != null)
            {
                var filtered = PianoNoteFilter.FilterByHand(all, _session.PracticeHand);
                _practice.AddRange(filtered);
                _practice.Sort((a, b) => a.StartTime != b.StartTime ? a.StartTime.CompareTo(b.StartTime) : a.Pitch.CompareTo(b.Pitch));
            }
            _fingers = ScoreFingering.Compute(_practice);
            Scorer.Load(_practice);
            Coach.Analyze(_practice);
            _falling?.SetNotes(_practice, _fingers);
            _lastNow = _session != null ? _session.CurrentTime : 0f;
            _loopA = null;
            SettingsChanged?.Invoke();
        }
    }
}
