using System.Collections.Generic;
using NUnit.Framework;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 节拍器拍点生成与 tempo 数据结构的边界测试（方案 2.2 节拍器）。
    /// </summary>
    public class TempoAndMetronomeTests
    {
        // ==================== TempoChange 边界 ====================

        [Test]
        public void TempoChange_ZeroOrNegativeMpq_FallsBackToDefault()
        {
            foreach (int mpq in new[] { 0, -1, int.MinValue })
            {
                var t = new TempoChange(0f, mpq);
                Assert.AreEqual(TempoChange.DefaultMpq, t.MicrosecondsPerQuarter, $"mpq={mpq}");
                Assert.AreEqual(120f, t.Bpm, 0.01f);
                Assert.AreEqual(0.5f, t.BeatSeconds, 1e-4f);
            }
        }

        [Test]
        public void TempoChange_NegativeTime_ClampedToZero()
        {
            var t = new TempoChange(-3f, 500000);
            Assert.AreEqual(0f, t.TimeSeconds);
        }

        [Test]
        public void TempoChange_BpmConversion_IsCorrect()
        {
            Assert.AreEqual(60f, new TempoChange(0f, 1_000_000).Bpm, 0.01f);
            Assert.AreEqual(120f, new TempoChange(0f, 500_000).Bpm, 0.01f);
            Assert.AreEqual(240f, new TempoChange(0f, 250_000).Bpm, 0.01f);
            Assert.AreEqual(2f, new TempoChange(0f, 2_000_000).BeatSeconds, 1e-4f);
        }

        // ==================== BuildBeatTimes 边界 ====================

        [Test]
        public void BuildBeatTimes_NonPositiveDuration_ReturnsEmpty()
        {
            Assert.AreEqual(0, Metronome.BuildBeatTimes(null, 0f).Count);
            Assert.AreEqual(0, Metronome.BuildBeatTimes(null, -5f).Count);
            Assert.AreEqual(0, Metronome.BuildBeatTimes(new List<TempoChange>(), 0f).Count);
        }

        [Test]
        public void BuildBeatTimes_NullTempo_UsesDefault120Bpm()
        {
            // 120 BPM → 每拍 0.5s；时长 2.0s → 0, 0.5, 1.0, 1.5, 2.0
            var beats = Metronome.BuildBeatTimes(null, 2f);

            Assert.AreEqual(5, beats.Count);
            for (int i = 0; i < beats.Count; i++)
                Assert.AreEqual(i * 0.5f, beats[i], 1e-4f);
        }

        [Test]
        public void BuildBeatTimes_TempoChange_AccumulatesPerSegment()
        {
            // 0–2s：120 BPM（0.5s/拍）；2s 之后：60 BPM（1.0s/拍）；总时长 4s
            var tempos = new List<TempoChange>
            {
                new TempoChange(0f, 500_000),
                new TempoChange(2f, 1_000_000)
            };

            var beats = Metronome.BuildBeatTimes(tempos, 4f);

            // 期望：0, 0.5, 1.0, 1.5, 2.0, 3.0, 4.0
            Assert.AreEqual(7, beats.Count);
            Assert.AreEqual(0f, beats[0], 1e-4f);
            Assert.AreEqual(2f, beats[4], 1e-4f, "tempo 切换点本身是拍点");
            Assert.AreEqual(3f, beats[5], 1e-4f, "切换后按 60BPM 递进");
            Assert.AreEqual(4f, beats[6], 1e-4f);
        }

        [Test]
        public void BuildBeatTimes_FirstTempoNotAtZero_StillStartsWithBeatZero()
        {
            var tempos = new List<TempoChange> { new TempoChange(1f, 500_000) };

            var beats = Metronome.BuildBeatTimes(tempos, 2f);

            Assert.IsNotEmpty(beats);
            Assert.AreEqual(0f, beats[0], 1e-4f, "拍点表必须从 0 开始");
        }

        [Test]
        public void BuildBeatTimes_AbsurdlyFastTempo_IsCappedAtMaxBeats()
        {
            // 200 微秒/四分音符 = 0.0002s/拍 → 100 秒需要 50 万拍，必须被上限截断
            var tempos = new List<TempoChange> { new TempoChange(0f, 200) };

            var beats = Metronome.BuildBeatTimes(tempos, 100f);

            Assert.AreEqual(Metronome.MaxBeats, beats.Count, "应恰好触及拍点上限");
        }

        [Test]
        public void BuildBeatTimes_UnorderedTempoPoints_SkipsInvalidSegments()
        {
            // 第二个时间点早于第一个（异常数据）→ 不应死循环或抛异常
            var tempos = new List<TempoChange>
            {
                new TempoChange(2f, 500_000),
                new TempoChange(1f, 250_000)
            };

            var beats = Metronome.BuildBeatTimes(tempos, 3f);

            Assert.IsNotEmpty(beats);
            Assert.AreEqual(0f, beats[0], 1e-4f);
            Assert.Less(beats.Count, Metronome.MaxBeats);
        }

        [Test]
        public void BuildBeatTimes_BeatsAreStrictlyIncreasing()
        {
            var tempos = new List<TempoChange>
            {
                new TempoChange(0f, 500_000),
                new TempoChange(1.5f, 300_000)
            };

            var beats = Metronome.BuildBeatTimes(tempos, 5f);

            for (int i = 1; i < beats.Count; i++)
                Assert.Greater(beats[i], beats[i - 1], $"拍点 {i} 应严格递增");
        }
    }
}
