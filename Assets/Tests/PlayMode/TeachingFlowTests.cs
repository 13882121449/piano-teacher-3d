using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Core;
using PianoTeacher3D.Teaching;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>教学模块在真实课程场景里的流程（2026-11-18）。</summary>
    public class TeachingFlowTests
    {
        private LessonSessionController _session;
        private TeachingDirector _teaching;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PlayerPrefs.DeleteKey("teach.wait");
            PlayerPrefs.DeleteKey("teach.staff");
            PlayerPrefs.DeleteKey("teach.falling");
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;
            _session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(_session);
            _teaching = _session.Teaching;
            Assert.IsNotNull(_teaching, "课程场景应创建教学总控");
            _session.LoadSong(Path.Combine(Application.streamingAssetsPath, "Songs", "bench", "bench_chords.mid"));
            yield return null;
            Assert.IsTrue(_session.HasSong);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_teaching != null)
            {
                _teaching.SetWaitMode(false);
                _teaching.SetStaffVisible(false);
            }
            if (_session != null) _session.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator FallingNotes_ShowUpcomingBars()
        {
            Assert.IsTrue(_teaching.FallingNotes, "下落音符默认开启");
            Assert.Greater(_teaching.PracticeNotes.Count, 0);
            _session.Seek(0f);
            _session.Play();
            while (_session.IsPreparing) yield return null;
            yield return new WaitForSeconds(0.3f);
            var view = Object.FindFirstObjectByType<FallingNotesView>();
            Assert.IsNotNull(view);
            Assert.Greater(view.VisibleBars, 0, "未来 3 秒内的音符应画成下落条");
        }

        [UnityTest]
        public IEnumerator WaitMode_HoldsUntilStudentPlaysTheChord()
        {
            _teaching.SetWaitMode(true);
            _session.Seek(0f);
            _session.Play();
            while (_session.IsPreparing) yield return null;
            float first = _teaching.PracticeNotes[0].StartTime;
            float timeout = Time.time + first + 3f;
            while (!_teaching.Scorer.IsHolding && Time.time < timeout) yield return null;
            Assert.IsTrue(_teaching.Scorer.IsHolding, "到第一组音时应停下等学生");
            float held = _session.CurrentTime;
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(held, _session.CurrentTime, 0.02f, "等待时时间轴不走");

            // 弹出第一组（同时起音）的全部音
            var notes = _teaching.PracticeNotes;
            for (int i = 0; i < notes.Count && notes[i].StartTime - notes[0].StartTime < 0.03f; i++)
                _teaching.PlayUserNote(notes[i].Pitch, 90);
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(_session.CurrentTime, held + 0.05f, "弹完这一组后继续往下走");
            Assert.Greater(_teaching.Scorer.Perfect, 0);
        }

        [UnityTest]
        public IEnumerator Staff_AndTeachingPanel_Open()
        {
            var chip = GameObject.Find("Btn_教学");
            Assert.IsNotNull(chip, "底栏应有「教学」入口");
            ExecuteEvents.Execute(chip, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            yield return null;
            var panel = GameObject.Find("TeachingPanel");
            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.activeInHierarchy, "点「教学」打开教学面板");

            _teaching.SetStaffVisible(true);
            _session.Seek(0f);
            yield return null;
            yield return null;
            var staff = Object.FindFirstObjectByType<StaffView>();
            Assert.IsNotNull(staff);
            Assert.IsTrue(staff.Panel.gameObject.activeInHierarchy);
            Assert.Greater(staff.VisibleHeads, 0, "五线谱上应画出即将到来的符头");
        }
    }
}
