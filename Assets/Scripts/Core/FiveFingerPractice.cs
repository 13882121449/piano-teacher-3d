using System;
using System.Collections.Generic;

namespace PianoTeacher3D.Core
{
    public enum PracticeMode
    {
        WaitForCorrect,
        KeepTime,
        Independent
    }

    public enum PracticeFeedbackKind
    {
        None,
        Correct,
        WrongPitch,
        Missed,
        Early,
        Late,
        Extra
    }

    /// <summary>课上校订过的键位和右手指号。小节与拍均从 1 开始。</summary>
    public readonly struct PracticeNote
    {
        public readonly int Pitch;
        public readonly int Finger;
        public readonly int Measure;
        public readonly int Beat;

        internal PracticeNote(int pitch, int finger, int measure, int beat)
        {
            Pitch = pitch;
            Finger = finger;
            Measure = measure;
            Beat = beat;
        }
    }

    /// <summary>一个落键事件或漏音，NoteIndex 始终使用全课索引。</summary>
    public readonly struct PracticeFeedback
    {
        public readonly PracticeFeedbackKind Kind;
        public readonly int NoteIndex;
        public readonly int ExpectedPitch;
        public readonly int ActualPitch;
        public readonly double OffsetSeconds;

        internal PracticeFeedback(PracticeFeedbackKind kind, int noteIndex, int expectedPitch,
            int actualPitch, double offsetSeconds)
        {
            Kind = kind;
            NoteIndex = noteIndex;
            ExpectedPitch = expectedPitch;
            ActualPitch = actualPitch;
            OffsetSeconds = offsetSeconds;
        }
    }

    /// <summary>五指第一课的练习状态。所有时间均由输入方提供，便于按同一时钟判定。</summary>
    public sealed class FiveFingerPractice
    {
        private const int BeatsPerMeasure = 4;
        private const int MeasureCount = 2;
        private const double AcceptanceWindowBeats = 0.45;
        private const double MaxOnTimeSeconds = 0.12;
        private const double TimeEpsilon = 0.000001;

        public static IReadOnlyList<PracticeNote> Notes { get; } = Array.AsReadOnly(new[]
        {
            new PracticeNote(60, 1, 1, 1),
            new PracticeNote(62, 2, 1, 2),
            new PracticeNote(64, 3, 1, 3),
            new PracticeNote(65, 4, 1, 4),
            new PracticeNote(67, 5, 2, 1),
            new PracticeNote(64, 3, 2, 2),
            new PracticeNote(62, 2, 2, 3),
            new PracticeNote(60, 1, 2, 4)
        });

        private readonly List<PracticeFeedback> _results = new List<PracticeFeedback>();
        private int _nextNote;

        public FiveFingerPractice()
        {
            Results = _results.AsReadOnly();
        }

        public IReadOnlyList<PracticeFeedback> Results { get; }
        public PracticeMode Mode { get; private set; }
        public bool IsStarted { get; private set; }
        public bool IsComplete => IsStarted && _nextNote >= EndNoteIndexExclusive;
        public bool ShowsPrompt => IsStarted && !IsComplete && Mode != PracticeMode.Independent;
        public int CurrentNoteIndex => IsStarted && !IsComplete ? _nextNote : -1;
        public int StartNoteIndex { get; private set; }
        public int EndNoteIndexExclusive { get; private set; }
        public int SelectedMeasure { get; private set; }
        public double FirstNoteTime { get; private set; }
        public double BeatSeconds { get; private set; }

        /// <summary>小节 0 表示全课。节拍模式给四拍预备，等待模式直接开始。</summary>
        public void Start(PracticeMode mode, double nowSeconds, int bpm = 60, int measure = 0)
        {
            if (mode < PracticeMode.WaitForCorrect || mode > PracticeMode.Independent)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (bpm <= 0) throw new ArgumentOutOfRangeException(nameof(bpm));
            if (measure < 0 || measure > MeasureCount)
                throw new ArgumentOutOfRangeException(nameof(measure));
            if (!IsFinite(nowSeconds)) throw new ArgumentOutOfRangeException(nameof(nowSeconds));

            Mode = mode;
            SelectedMeasure = measure;
            StartNoteIndex = measure == 0 ? 0 : (measure - 1) * BeatsPerMeasure;
            EndNoteIndexExclusive = measure == 0 ? Notes.Count : StartNoteIndex + BeatsPerMeasure;
            _nextNote = StartNoteIndex;
            BeatSeconds = 60.0 / bpm;
            FirstNoteTime = mode == PracticeMode.WaitForCorrect
                ? nowSeconds : nowSeconds + BeatsPerMeasure * BeatSeconds;
            _results.Clear();
            IsStarted = true;
        }

        /// <summary>检查过期拍点。允许一次更新跨过多拍，每个漏音只记一次。</summary>
        public void Advance(double nowSeconds)
        {
            if (!IsStarted || Mode == PracticeMode.WaitForCorrect || !IsFinite(nowSeconds)) return;

            while (_nextNote < EndNoteIndexExclusive &&
                nowSeconds - ExpectedTime(_nextNote) > WindowSeconds + TimeEpsilon)
            {
                Record(PracticeFeedbackKind.Missed, _nextNote, Notes[_nextNote].Pitch, -1, 0.0);
                _nextNote++;
            }
        }

        /// <summary>提交按下事件。错音不会消耗当前拍，允许在触键窗口内修正。</summary>
        public PracticeFeedback PlayNote(int pitch, double nowSeconds)
        {
            if (!IsStarted || IsComplete || pitch < 0 || pitch > 127 || !IsFinite(nowSeconds))
                return default;

            Advance(nowSeconds);
            if (IsComplete) return default;

            var expected = Notes[_nextNote];
            bool timed = Mode != PracticeMode.WaitForCorrect;
            double offset = timed ? nowSeconds - ExpectedTime(_nextNote) : 0.0;
            if (timed && offset < -WindowSeconds - TimeEpsilon)
            {
                double relativeBeat = (nowSeconds - FirstNoteTime) / BeatSeconds;
                if (relativeBeat < -AcceptanceWindowBeats - TimeEpsilon) return default;

                // 重复敲击已经判定的音或打在拍间的额外按键都留在最接近的小节。
                int nearestBeat = (int)Math.Round(relativeBeat, MidpointRounding.AwayFromZero);
                int index = Math.Min(EndNoteIndexExclusive - 1, Math.Max(StartNoteIndex, StartNoteIndex + nearestBeat));
                return Record(PracticeFeedbackKind.Extra, index, -1, pitch, nowSeconds - ExpectedTime(index));
            }

            if (pitch != expected.Pitch)
                return Record(PracticeFeedbackKind.WrongPitch, _nextNote, expected.Pitch, pitch, offset);

            double onTime = Math.Min(MaxOnTimeSeconds, BeatSeconds * 0.25);
            var kind = offset < -onTime - TimeEpsilon ? PracticeFeedbackKind.Early
                : offset > onTime + TimeEpsilon ? PracticeFeedbackKind.Late
                : PracticeFeedbackKind.Correct;
            return Record(kind, _nextNote++, expected.Pitch, pitch, offset);
        }

        private double WindowSeconds => AcceptanceWindowBeats * BeatSeconds;

        private double ExpectedTime(int index) => FirstNoteTime + (index - StartNoteIndex) * BeatSeconds;

        private PracticeFeedback Record(PracticeFeedbackKind kind, int noteIndex, int expected, int actual, double offset)
        {
            var feedback = new PracticeFeedback(kind, noteIndex, expected, actual, offset);
            _results.Add(feedback);
            return feedback;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
