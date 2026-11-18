using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Teaching;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>教学模块的纯逻辑（2026-11-18）：评分 / 等待模式、乐谱指法、难点提示、练习记录。</summary>
    public class TeachingLogicTests
    {
        private static MidiNoteEvent Note(int pitch, float start, NoteHand hand = NoteHand.Right, float duration = 0.4f)
            => new MidiNoteEvent(pitch, start, duration, 90, 0, 0) { Hand = hand };

        [Test]
        public void Scorer_JudgesPerfectEarlyLateWrongAndMiss()
        {
            var scorer = new PracticeScorer();
            scorer.Load(new[] { Note(60, 1f), Note(62, 2f), Note(64, 3f), Note(65, 4f) });
            var seen = new List<Judgement>();
            scorer.Judged += (j, _, _) => seen.Add(j);

            scorer.OnInput(60, 1.03f, 1f);    // +30 ms → 准确
            scorer.OnInput(62, 1.85f, 1f);    // −150 ms → 偏早
            scorer.OnInput(64, 3.20f, 1f);    // +200 ms → 偏晚
            scorer.OnInput(70, 3.5f, 1f);     // 窗口里没有这个音 → 弹错
            scorer.Advance(4.5f, 1f);         // 65 过窗无人弹 → 漏弹

            CollectionAssert.AreEqual(new[] { Judgement.Perfect, Judgement.Early, Judgement.Late, Judgement.Wrong, Judgement.Miss }, seen);
            Assert.AreEqual(1, scorer.Perfect);
            Assert.AreEqual(2, scorer.Good);
            Assert.AreEqual(1, scorer.Wrong);
            Assert.AreEqual(1, scorer.Missed);
            Assert.AreEqual(3f / 5f, scorer.Accuracy, 1e-4f);
            Assert.AreEqual(3, scorer.BestStreak);
            Assert.AreEqual(0, scorer.Streak);
        }

        [Test]
        public void Scorer_WindowScalesWithSpeed()
        {
            var scorer = new PracticeScorer();
            scorer.Load(new[] { Note(60, 1f) });
            Judgement? got = null;
            scorer.Judged += (j, _, _) => got = j;
            // 0.5× 速度：乐曲时间偏差 0.03 s = 真实 60 ms，仍算准确
            scorer.OnInput(60, 1.03f, 0.5f);
            Assert.AreEqual(Judgement.Perfect, got);
        }

        [Test]
        public void WaitMode_HoldsUntilWholeChordIsPlayed()
        {
            var scorer = new PracticeScorer { WaitMode = true };
            scorer.Load(new[] { Note(60, 1f), Note(64, 1f), Note(67, 1f), Note(72, 2f) });
            Assert.IsFalse(scorer.Advance(0.5f, 1f), "没到第一组音时不等");
            Assert.IsTrue(scorer.Advance(1.0f, 1f), "到了和弦就停住");
            scorer.OnInput(60, 1.0f, 1f);
            scorer.OnInput(67, 1.0f, 1f);
            Assert.IsTrue(scorer.Advance(1.0f, 1f), "和弦还差一个音");
            scorer.OnInput(61, 1.0f, 1f);
            Assert.AreEqual(1, scorer.Wrong, "弹错的音只记错，不放行");
            scorer.OnInput(64, 1.0f, 1f);
            Assert.IsFalse(scorer.Advance(1.0f, 1f), "整组弹完放行");
            Assert.IsTrue(scorer.Advance(2.0f, 1f), "下一组再停");
        }

        [Test]
        public void Stars_FollowAccuracyAndSpeed()
        {
            Assert.AreEqual(0, PracticeScorer.StarsFor(0.79f, 1f));
            Assert.AreEqual(1, PracticeScorer.StarsFor(0.80f, 0.6f));
            Assert.AreEqual(2, PracticeScorer.StarsFor(0.95f, 1f));
            Assert.AreEqual(2, PracticeScorer.StarsFor(0.99f, 0.8f), "三星要求原速");
            Assert.AreEqual(3, PracticeScorer.StarsFor(0.97f, 1f));
        }

        [Test]
        public void Fingering_ScaleAndChordAreReadable()
        {
            var notes = new List<MidiNoteEvent>();
            int[] scale = { 60, 62, 64, 65, 67 };
            for (int i = 0; i < scale.Length; i++) notes.Add(Note(scale[i], i * 0.5f));
            notes.Add(Note(60, 3f));
            notes.Add(Note(64, 3f));
            notes.Add(Note(67, 3f));
            int[] f = ScoreFingering.Compute(notes);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, new[] { f[0], f[1], f[2], f[3], f[4] }, "右手五指位置 C–G 用 1–5 指");
            Assert.Less(f[5], f[6]);
            Assert.Less(f[6], f[7], "右手和弦从低到高手指递增");
            foreach (int x in f) Assert.That(x, Is.InRange(1, 5));
        }

        [Test]
        public void Fingering_LeftHandMirrors()
        {
            var notes = new List<MidiNoteEvent>();
            int[] scale = { 48, 50, 52, 53, 55 };
            for (int i = 0; i < scale.Length; i++) notes.Add(Note(scale[i], i * 0.5f, NoteHand.Left));
            int[] f = ScoreFingering.Compute(notes);
            CollectionAssert.AreEqual(new[] { 5, 4, 3, 2, 1 }, f, "左手 C–G 上行用 5–1 指");
        }

        [Test]
        public void Coach_FindsOctaveChordFastRunAndThumbUnder()
        {
            var notes = new List<MidiNoteEvent>
            {
                Note(48, 1f, NoteHand.Left), Note(60, 1f, NoteHand.Left),                        // 八度
                Note(60, 3f), Note(64, 3f), Note(67, 3f), Note(72, 3f)                            // 四音和弦
            };
            for (int i = 0; i < 10; i++) notes.Add(Note(72 + (i % 2) * 2, 5f + i * 0.09f));    // 快速音群
            int[] run = { 60, 62, 64, 65, 67 };
            for (int i = 0; i < run.Length; i++) notes.Add(Note(run[i], 9f + i * 0.4f));       // 上行音阶
            var coach = new TeachingCoach();
            coach.Analyze(notes);
            var tips = new List<TeachingCoach.Tip>();
            for (float t = 0f; t < 12f; t += 0.1f)
            {
                var tip = coach.Next(t, t * 100f);   // 真实时间放大，跳过 20 s 防重复
                if (tip != TeachingCoach.Tip.None) tips.Add(tip);
            }
            CollectionAssert.Contains(tips, TeachingCoach.Tip.Octave);
            CollectionAssert.Contains(tips, TeachingCoach.Tip.Chord);
            CollectionAssert.Contains(tips, TeachingCoach.Tip.FastRun);
            CollectionAssert.Contains(tips, TeachingCoach.Tip.ThumbUnder);
        }

        [Test]
        public void ProgressStore_KeepsBestAndCountsStreak()
        {
            string path = Path.Combine(Path.GetTempPath(), "pt3d_progress_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new PracticeProgressStore(path);
                Assert.IsTrue(store.RecordRun("Songs/demo.mid", 0.85f, 1f));
                Assert.IsFalse(store.RecordRun("songs/DEMO.mid", 0.70f, 1f), "更差的成绩不刷新纪录");
                Assert.IsTrue(store.RecordRun("demo.mid", 0.98f, 1f));
                var record = store.GetSong("demo.mid");
                Assert.AreEqual(3, record.Stars);
                Assert.AreEqual(3, record.Plays);

                var today = new DateTime(2026, 11, 18, 20, 0, 0);
                store.AddPracticeSeconds(120f, today.AddDays(-2));
                store.AddPracticeSeconds(90f, today.AddDays(-1));
                store.AddPracticeSeconds(30f, today);
                Assert.AreEqual(2, store.StreakDays(today), "今天不足 1 分钟时从昨天往前数");
                store.AddPracticeSeconds(40f, today);
                Assert.AreEqual(3, store.StreakDays(today));
                store.Save();

                var reloaded = new PracticeProgressStore(path);
                Assert.AreEqual(3, reloaded.GetSong("demo.mid").Stars);
                Assert.AreEqual(70f, reloaded.SecondsOn(today), 0.01f);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
