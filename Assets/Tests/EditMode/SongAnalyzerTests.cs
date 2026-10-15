using System.Collections.Generic;
using NUnit.Framework;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 曲目分析与练习反馈测试（方案 2.2「根据 MIDI…提供练习反馈」）。
    /// </summary>
    public class SongAnalyzerTests
    {
        private static MidiNoteEvent Note(int pitch, float start, float duration = 0.5f, int velocity = 90)
            => new MidiNoteEvent(pitch, start, duration, velocity, 0, 0);

        // ==================== 空输入 ====================

        [Test]
        public void Analyze_NullNotes_ReturnsEmptyAnalysisWithSuggestion()
        {
            var a = SongAnalyzer.Analyze(null);

            Assert.IsNotNull(a);
            Assert.IsTrue(a.IsEmpty);
            Assert.AreEqual(0, a.NoteCount);
            Assert.IsNotNull(a.Suggestions, "Suggestions 永不为 null");
            Assert.Greater(a.Suggestions.Count, 0, "空曲目也应给出提示文案");
            Assert.AreEqual(-1, a.LowestPitch);
        }

        [Test]
        public void Analyze_EmptyList_ReturnsEmptyAnalysis()
        {
            var a = SongAnalyzer.Analyze(new List<MidiNoteEvent>());

            Assert.IsTrue(a.IsEmpty);
            Assert.AreEqual(0f, a.LeftHandRatio);
            Assert.AreEqual(0f, a.BlackKeyRatio);
        }

        // ==================== 基础统计 ====================

        [Test]
        public void Analyze_HandSplit_AtC4()
        {
            var notes = new List<MidiNoteEvent> { Note(59, 0f), Note(60, 0.5f), Note(48, 1f) };

            var a = SongAnalyzer.Analyze(notes);

            Assert.AreEqual(3, a.NoteCount);
            Assert.AreEqual(2, a.LeftHandCount, "59 与 48 属左手");
            Assert.AreEqual(1, a.RightHandCount, "60（C4）属右手");
            Assert.AreEqual(2f / 3f, a.LeftHandRatio, 1e-3f, "2 左手音 / 3 总音符");
        }

        [Test]
        public void Analyze_BlackKeys_Counted()
        {
            // C4=60 白, C#4=61 黑, D4=62 白, F#4=66 黑
            var notes = new List<MidiNoteEvent> { Note(60, 0f), Note(61, 0.1f), Note(62, 0.2f), Note(66, 0.3f) };

            var a = SongAnalyzer.Analyze(notes);

            Assert.AreEqual(2, a.BlackKeyCount);
            Assert.AreEqual(0.5f, a.BlackKeyRatio, 1e-3f);
        }

        [Test]
        public void Analyze_PitchRange_AndFastestGap()
        {
            var notes = new List<MidiNoteEvent> { Note(48, 0f), Note(84, 1f), Note(85, 1.05f) };

            var a = SongAnalyzer.Analyze(notes);

            Assert.AreEqual(48, a.LowestPitch);
            Assert.AreEqual(85, a.HighestPitch);
            Assert.AreEqual(37, a.PitchRange);
            Assert.AreEqual(0.05f, a.FastestNoteGap, 1e-4f);
            Assert.AreEqual(37f / 12f, a.OctaveRange, 1e-3f);
        }

        [Test]
        public void Analyze_SingleNote_NoChordAndZeroGap()
        {
            var a = SongAnalyzer.Analyze(new List<MidiNoteEvent> { Note(60, 0f) });

            Assert.AreEqual(1, a.NoteCount);
            Assert.AreEqual(0, a.ChordCount);
            Assert.AreEqual(0, a.MaxChordSize);
            Assert.AreEqual(0f, a.FastestNoteGap);
            Assert.AreEqual(0, a.PitchRange);
        }

        // ==================== 和弦识别 ====================

        [Test]
        public void Analyze_SimultaneousNotes_DetectedAsChordWithSpan()
        {
            // C4/E4/G4 同时按下 → 1 个和弦，3 音，跨度 7 半音
            var notes = new List<MidiNoteEvent> { Note(60, 0f), Note(64, 0f), Note(67, 0f) };

            var a = SongAnalyzer.Analyze(notes);

            Assert.AreEqual(1, a.ChordCount);
            Assert.AreEqual(3, a.MaxChordSize);
            Assert.AreEqual(7, a.MaxSpanSemitones);
        }

        [Test]
        public void Analyze_SequentialNotes_NotChord()
        {
            var notes = new List<MidiNoteEvent> { Note(60, 0f), Note(62, 0.5f), Note(64, 1f) };

            var a = SongAnalyzer.Analyze(notes);

            Assert.AreEqual(0, a.ChordCount);
            Assert.AreEqual(0, a.MaxChordSize);
        }

        [Test]
        public void Analyze_NotesBeyondChordTolerance_SplitIntoSeparateGroups()
        {
            // 起点相差 50ms > 20ms 容差 → 不视为同一和弦
            var notes = new List<MidiNoteEvent> { Note(60, 0f), Note(64, 0.05f) };

            var a = SongAnalyzer.Analyze(notes);

            Assert.AreEqual(0, a.ChordCount);
        }

        [Test]
        public void Analyze_MultipleChords_PicksLargestSizeAndSpan()
        {
            var notes = new List<MidiNoteEvent>
            {
                Note(60, 0f), Note(64, 0f),            // 和弦 1：2 音，跨度 4
                Note(48, 1f), Note(52, 1f), Note(60, 1f), Note(72, 1f) // 和弦 2：4 音，跨度 24
            };

            var a = SongAnalyzer.Analyze(notes);

            Assert.AreEqual(2, a.ChordCount);
            Assert.AreEqual(4, a.MaxChordSize);
            Assert.AreEqual(24, a.MaxSpanSemitones);
        }

        // ==================== tempo 与时长 ====================

        [Test]
        public void Analyze_TempoChanges_ProduceBpmRange()
        {
            var tempos = new List<TempoChange>
            {
                new TempoChange(0f, 500_000),  // 120
                new TempoChange(1f, 250_000)   // 240
            };

            var a = SongAnalyzer.Analyze(new List<MidiNoteEvent> { Note(60, 0f) }, tempos);

            Assert.AreEqual(120f, a.MinBpm, 0.01f);
            Assert.AreEqual(240f, a.MaxBpm, 0.01f);
        }

        [Test]
        public void Analyze_NoTempoInfo_DefaultsTo120Bpm()
        {
            var a = SongAnalyzer.Analyze(new List<MidiNoteEvent> { Note(60, 0f) });

            Assert.AreEqual(120f, a.MinBpm, 0.01f);
            Assert.AreEqual(120f, a.MaxBpm, 0.01f);
        }

        [Test]
        public void Analyze_DurationFallsBackToLastNoteEnd()
        {
            var notes = new List<MidiNoteEvent> { Note(60, 1f, 0.5f), Note(62, 2f, 1f) };

            var a = SongAnalyzer.Analyze(notes, null, 0f);

            Assert.AreEqual(3f, a.TotalDuration, 1e-3f, "无总时长时按最后一个音符结束时间推算");
        }

        // ==================== 不可变性 ====================

        [Test]
        public void Analyze_DoesNotModifyInputList()
        {
            var notes = new List<MidiNoteEvent> { Note(64, 1f), Note(60, 0f) };

            SongAnalyzer.Analyze(notes);

            Assert.AreEqual(64, notes[0].Pitch, "输入列表顺序不应被修改");
            Assert.AreEqual(60, notes[1].Pitch);
        }

        // ==================== 建议规则 ====================

        [Test]
        public void Analyze_LeftHandDominated_ProducesLeftPracticeSuggestion()
        {
            var notes = new List<MidiNoteEvent>();
            for (int i = 0; i < 8; i++) notes.Add(Note(40 + i, i * 0.3f));
            notes.Add(Note(72, 3f));

            var a = SongAnalyzer.Analyze(notes);

            Assert.Greater(a.LeftHandRatio, SongAnalyzer.ImbalancedHandRatio);
            Assert.IsTrue(a.Suggestions.Exists(s => s.Contains("左手练习")), "应建议左手练习");
        }

        [Test]
        public void Analyze_RightHandDominated_ProducesRightPracticeSuggestion()
        {
            var notes = new List<MidiNoteEvent>();
            for (int i = 0; i < 8; i++) notes.Add(Note(72 + i, i * 0.3f));
            notes.Add(Note(40, 3f));

            var a = SongAnalyzer.Analyze(notes);

            Assert.IsTrue(a.Suggestions.Exists(s => s.Contains("右手练习")), "应建议右手练习");
        }

        [Test]
        public void Analyze_LargeSpanChord_SuggestsSlowerTempo()
        {
            var notes = new List<MidiNoteEvent> { Note(48, 0f), Note(64, 0f) }; // 跨度 16 半音

            var a = SongAnalyzer.Analyze(notes);

            Assert.GreaterOrEqual(a.MaxSpanSemitones, SongAnalyzer.LargeSpanSemitones);
            Assert.IsTrue(a.Suggestions.Exists(s => s.Contains("大跨度")), "应提示大跨度和弦");
        }

        [Test]
        public void Analyze_FastNotes_SuggestsSlowPractice()
        {
            var notes = new List<MidiNoteEvent> { Note(60, 0f), Note(62, 0.05f), Note(64, 0.1f) };

            var a = SongAnalyzer.Analyze(notes);

            Assert.Less(a.FastestNoteGap, SongAnalyzer.FastNoteGapSeconds);
            Assert.IsTrue(a.Suggestions.Exists(s => s.Contains("快速音符")), "应提示快速片段");
        }

        [Test]
        public void Analyze_SimpleSong_StillProducesAtLeastOneSuggestion()
        {
            var a = SongAnalyzer.Analyze(new List<MidiNoteEvent> { Note(60, 0f), Note(62, 1f) });

            Assert.Greater(a.Suggestions.Count, 0, "任何非空曲目都应至少有一条建议");
        }

        // ==================== 回归：和弦不应掩盖最快间隔（缺陷 B2） ====================

        [Test]
        public void Analyze_ChordPlusFastNotes_FastestGapReflectsRealFastPart()
        {
            var notes = new List<MidiNoteEvent>
            {
                Note(60, 0f), Note(64, 0f), Note(67, 0f), // 三音和弦：相邻 gap = 0（同一起点）
                Note(72, 0.06f), Note(74, 0.12f)          // 真实快速连接：间隔 60ms
            };

            var a = SongAnalyzer.Analyze(notes);

            Assert.AreEqual(1, a.ChordCount);
            Assert.AreEqual(0.06f, a.FastestNoteGap, 1e-3f,
                "和弦的 0 间隔不应参与统计，否则会掩盖真实快速片段");
            Assert.IsTrue(a.Suggestions.Exists(s => s.Contains("快速音符")), "应给出快速片段建议");
        }

        [Test]
        public void Analyze_OnlySimultaneousNotes_NoFastSuggestion()
        {
            var notes = new List<MidiNoteEvent> { Note(60, 0f), Note(64, 0f), Note(67, 0f) };

            var a = SongAnalyzer.Analyze(notes);

            Assert.AreEqual(0f, a.FastestNoteGap, "全为同起始时刻时无「不同起点」间隔可统计");
            Assert.IsFalse(a.Suggestions.Exists(s => s.Contains("快速音符")),
                "无真实快速片段时不应误报");
        }
    }
}
