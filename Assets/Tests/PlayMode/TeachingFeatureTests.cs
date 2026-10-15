using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 教学功能集成测试（方案 2.2：琴键高亮提示 / 节拍器 / 曲目分析与练习反馈）。
    /// 验证「播放期间开启、切换、停止后无残留」等运行时状态一致性。
    /// </summary>
    public class TeachingFeatureTests
    {
        private LessonSessionController _session;
        private PianoKeyHighlighter _highlighter;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            _session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(_session, "PianoLesson 场景应包含 LessonSessionController");
            _highlighter = Object.FindFirstObjectByType<PianoKeyHighlighter>();
            Assert.IsNotNull(_highlighter, "控制器应装配琴键高亮组件");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        // ==================== 琴键高亮 ====================

        [UnityTest]
        public IEnumerator Highlight_DefaultModeIsOff()
        {
            Assert.AreEqual(HighlightMode.Off, _session.KeyHighlight);
            Assert.AreEqual(0, _highlighter.HighlightedKeyCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Highlight_CycleVisitsAllModesThenReturnsToOff()
        {
            var visited = new List<HighlightMode>();
            for (int i = 0; i < 4; i++)
            {
                _session.CycleHighlightMode();
                visited.Add(_session.KeyHighlight);
            }

            CollectionAssert.Contains(visited, HighlightMode.Both);
            CollectionAssert.Contains(visited, HighlightMode.Active);
            CollectionAssert.Contains(visited, HighlightMode.Upcoming);
            Assert.AreEqual(HighlightMode.Off, _session.KeyHighlight, "4 次循环后应回到关闭");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Highlight_LookAheadIsClamped()
        {
            _session.HighlightLookAhead = -10f;
            Assert.GreaterOrEqual(_session.HighlightLookAhead, 0.05f, "过小提前量应被 clamp");

            _session.HighlightLookAhead = 999f;
            Assert.LessOrEqual(_session.HighlightLookAhead, 3f, "过大提前量应被 clamp");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Highlight_WhilePlaying_ColorsKeys()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            _session.SetHighlightMode(HighlightMode.Both);
            _session.Seek(1f);
            _session.Play();
            yield return new WaitForSeconds(0.6f);

            int colored = _highlighter.HighlightedKeyCount;
            _session.Pause();
            Assert.Greater(colored, 0, "播放中应出现高亮或预提示琴键");
        }

        [UnityTest]
        public IEnumerator Highlight_StopAlwaysLeavesNoResidue()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            _session.SetHighlightMode(HighlightMode.Both);
            _session.Seek(1f);
            _session.Play();
            yield return new WaitForSeconds(0.5f);

            _session.Stop();
            yield return null;
            yield return null; // 留一帧给控制器执行停止态清理

            Assert.AreEqual(0, _highlighter.HighlightedKeyCount, "停止后不应残留着色琴键");
            Assert.AreEqual(0, _highlighter.UpcomingNoteCount, "停止后不应残留预提示");
        }

        [UnityTest]
        public IEnumerator Highlight_SwitchToOff_ClearsImmediately()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            _session.SetHighlightMode(HighlightMode.Both);
            _session.Seek(1f);
            _session.Play();
            yield return new WaitForSeconds(0.4f);

            _session.SetHighlightMode(HighlightMode.Off);
            yield return null;

            Assert.AreEqual(HighlightMode.Off, _session.KeyHighlight);
            Assert.AreEqual(0, _highlighter.HighlightedKeyCount, "关闭高亮应立即清除颜色");
            _session.Pause();
        }

        [UnityTest]
        public IEnumerator Highlight_RepeatedSeek_DoesNotAccumulateColoring()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            _session.SetHighlightMode(HighlightMode.Active);
            _session.Play();
            yield return null;
            _session.Pause();
            yield return null; // 进入 Paused 态（停止态不显示任何高亮，不适合验证 Seek 重建）

            _session.Seek(1f);
            yield return null;
            int atOneSecond = _highlighter.HighlightedKeyCount;

            _session.Seek(5f);
            yield return null;

            _session.Seek(1f);
            yield return null;
            int backToOneSecond = _highlighter.HighlightedKeyCount;

            Assert.AreEqual(atOneSecond, backToOneSecond,
                "回到同一位置应得到完全相同的着色集合（证明状态是重建而非累积）");
        }

        /// <summary>回归：预提示集合未变化时不应重写材质属性块（缺陷 B4）。</summary>
        [UnityTest]
        public IEnumerator Highlight_StableUpcomingSet_DoesNotRewriteMaterials()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            _session.SetHighlightMode(HighlightMode.Both);
            _session.Seek(2f);
            yield return null;
            yield return null; // Paused 态：预提示集合已静止

            int before = _highlighter.MpbWriteCount;
            for (int i = 0; i < 30; i++) yield return null;
            int after = _highlighter.MpbWriteCount;

            Assert.AreEqual(before, after,
                "预提示集合未变化时不应重写材质属性块（避免每帧无谓的 GPU 状态操作）");
        }

        // ==================== 节拍器 ====================

        [UnityTest]
        public IEnumerator Metronome_DefaultDisabled()
        {
            Assert.IsFalse(_session.MetronomeEnabled);
            Assert.AreEqual(4, _session.BeatsPerBar);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Metronome_ToggleFlipsState()
        {
            _session.ToggleMetronome();
            Assert.IsTrue(_session.MetronomeEnabled);

            _session.ToggleMetronome();
            Assert.IsFalse(_session.MetronomeEnabled);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Metronome_FiresBeatsWhilePlaying()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            int beats = 0;
            int firstBeatInBar = -1;
            System.Action<int> handler = b =>
            {
                if (beats == 0) firstBeatInBar = b;
                beats++;
            };
            _session.OnMetronomeBeat += handler;
            try
            {
                _session.ToggleMetronome();
                _session.Seek(0f);
                _session.Play();
                yield return new WaitForSeconds(1.3f); // 120BPM → 约 2–3 拍
            }
            finally
            {
                _session.OnMetronomeBeat -= handler;
                _session.Pause();
            }

            Assert.Greater(beats, 0, "节拍器开启后应触发拍点事件");
            Assert.AreEqual(0, firstBeatInBar, "首拍应为强拍（小节内序号 0）");
        }

        [UnityTest]
        public IEnumerator Metronome_DisabledDoesNotFireBeats()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            int beats = 0;
            System.Action<int> handler = _ => beats++;
            _session.OnMetronomeBeat += handler;
            try
            {
                _session.Seek(0f);
                _session.Play();
                yield return new WaitForSeconds(1.1f);
            }
            finally
            {
                _session.OnMetronomeBeat -= handler;
                _session.Pause();
            }

            Assert.AreEqual(0, beats, "节拍器关闭时不应发出拍点");
        }

        [UnityTest]
        public IEnumerator Metronome_BeatsPerBarClampedToAtLeastOne()
        {
            _session.SetBeatsPerBar(0);
            Assert.GreaterOrEqual(_session.BeatsPerBar, 1);

            _session.SetBeatsPerBar(-5);
            Assert.GreaterOrEqual(_session.BeatsPerBar, 1);

            _session.SetBeatsPerBar(3);
            Assert.AreEqual(3, _session.BeatsPerBar);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Metronome_StopThenRestart_DoesNotReplayPastBeats()
        {
            if (!_session.HasSong) Assert.Ignore("演示曲目未加载，跳过");

            _session.ToggleMetronome();
            _session.Seek(0f);
            _session.Play();
            yield return new WaitForSeconds(1.1f);
            _session.Stop();
            yield return null;

            int beatsAfterStop = 0;
            System.Action<int> handler = _ => beatsAfterStop++;
            _session.OnMetronomeBeat += handler;
            try
            {
                yield return new WaitForSeconds(0.4f); // 停止期间不应有任何拍点
            }
            finally
            {
                _session.OnMetronomeBeat -= handler;
            }

            Assert.AreEqual(0, beatsAfterStop, "停止后不应继续触发拍点");
        }

        // ==================== 曲目分析 ====================

        [UnityTest]
        public IEnumerator Analysis_AvailableAndConsistentWithLoadedSong()
        {
            var analysis = _session.Analysis;

            Assert.IsNotNull(analysis, "Analysis 不应为 null");
            Assert.IsNotNull(analysis.Suggestions, "Suggestions 不应为 null");

            if (_session.HasSong)
            {
                Assert.Greater(analysis.NoteCount, 0, "已加载曲目应有音符统计");
                Assert.AreEqual(analysis.NoteCount, analysis.LeftHandCount + analysis.RightHandCount);
                Assert.Greater(analysis.Suggestions.Count, 0, "应给出至少一条练习建议");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Analysis_HandRatiosSumToOne()
        {
            if (_session.HasSong)
            {
                float sum = _session.Analysis.LeftHandRatio + _session.Analysis.RightHandRatio;
                Assert.AreEqual(1f, sum, 1e-3f, "左右手占比之和应为 1");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Analysis_LoadFailure_PreservesPreviousAnalysis()
        {
            int before = _session.Analysis.NoteCount;

            LogAssert.Expect(LogType.Error, new Regex(@"\[PianoTeacher3D\]"));
            _session.LoadSong(Path.Combine(Path.GetTempPath(), "missing_for_analysis.mid"));
            yield return null;

            Assert.AreEqual(before, _session.Analysis.NoteCount, "加载失败不应破坏已有分析结果");
        }
    }
}
