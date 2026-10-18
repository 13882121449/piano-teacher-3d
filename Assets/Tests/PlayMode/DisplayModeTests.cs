using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 显示模式（展示 / 教学）回归测试：
    /// ① 教学模式隐藏手部，且只高亮「被按下的琴键」（左右手分色）；
    /// ② 展示模式保持完整手部动画；
    /// ③ 两种模式可自由切换，切换后立即生效且<b>不残留状态</b>（手部可见性、高亮模式、按键高亮集合、时间轴）。
    /// </summary>
    public class DisplayModeTests
    {
        private LessonSessionController _session;
        private PianoKeyHighlighter _highlighter;
        private AvatarManager _avatar;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            _session = Object.FindFirstObjectByType<LessonSessionController>();
            _highlighter = Object.FindFirstObjectByType<PianoKeyHighlighter>();
            _avatar = Object.FindFirstObjectByType<AvatarManager>();

            Assert.IsNotNull(_session, "场景应存在 LessonSessionController");
            Assert.IsNotNull(_highlighter, "场景应存在 PianoKeyHighlighter");
            Assert.IsNotNull(_avatar, "场景应存在 AvatarManager");

            _session.Stop();
            _session.SetDisplayMode(DisplayMode.Showcase);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null)
            {
                _session.SetDisplayMode(DisplayMode.Showcase);
                _session.Stop();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DefaultMode_IsShowcase_WithHandsVisible()
        {
            Assert.AreEqual(DisplayMode.Showcase, _session.DisplayMode);
            Assert.IsFalse(_session.IsTeachingMode);
            Assert.IsTrue(_avatar.HandsAndArmsVisible, "展示模式必须显示手部与手臂");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Teaching_HidesHandsAndForcesPressedKeyHighlight()
        {
            _session.SetHighlightMode(HighlightMode.Off);
            yield return null;

            _session.SetDisplayMode(DisplayMode.Teaching);
            yield return null;

            Assert.IsTrue(_session.IsTeachingMode);
            Assert.IsFalse(_avatar.HandsAndArmsVisible, "教学模式必须隐藏手部 / 手臂网格");
            Assert.AreEqual(HighlightMode.Active, _session.EffectiveHighlightMode,
                "教学模式应固定为「仅高亮被按下的琴键」（Active）");

            yield return new WaitForSeconds(0.2f);
        }

        [UnityTest]
        public IEnumerator Teaching_HighlightedKeysMatchActuallyPressedKeys()
        {
            if (!_session.HasSong) Assert.Ignore("示例曲目未加载，跳过");

            _session.SetDisplayMode(DisplayMode.Teaching);
            _session.Seek(0f);
            _session.Play();

            bool sawPressed = false;
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                if (_session.PressedPitchCount > 0 && _highlighter.HighlightedKeyCount > 0)
                {
                    sawPressed = true;
                    Assert.AreEqual(_session.PressedPitchCount, _highlighter.HighlightedKeyCount,
                        "教学模式：高亮的琴键集合必须与「正在按下的琴键」完全一致（不许多亮、不许少亮）");
                    Assert.AreEqual(0, _highlighter.UpcomingNoteCount,
                        "教学模式不应出现预提示高亮（只高亮被按下的键）");
                    break;
                }
            }

            Assert.IsTrue(sawPressed, "播放 2 秒内应出现「有键被按下」的采样，否则用例无意义");
            _session.Pause();
        }

        [UnityTest]
        public IEnumerator SwitchRoundTrip_LeavesNoResidue()
        {
            // 用户在展示模式下把高亮设为「全部」，教学模式冻结后再切回，必须恢复为「全部」
            _session.SetHighlightMode(HighlightMode.Both);
            yield return null;

            _session.Seek(0f);
            _session.Play();
            yield return new WaitForSeconds(0.6f);

            float timeBefore = _session.CurrentTime;
            PlaybackState stateBefore = _session.PlaybackState;

            _session.SetDisplayMode(DisplayMode.Teaching);
            yield return null;
            Assert.IsFalse(_avatar.HandsAndArmsVisible);
            Assert.AreEqual(HighlightMode.Active, _session.EffectiveHighlightMode);

            _session.SetDisplayMode(DisplayMode.Showcase);
            yield return null;

            // ① 手部恢复可见
            Assert.IsTrue(_avatar.HandsAndArmsVisible, "切回展示模式后手部必须恢复可见");
            // ② 高亮模式恢复为用户偏好（而不是停在教学模式的强制值）
            Assert.AreEqual(HighlightMode.Both, _session.EffectiveHighlightMode,
                "切回展示模式后应恢复用户原来选择的高亮模式");
            // ③ 时间轴不受影响（切换只改表现层）
            Assert.AreEqual(stateBefore, _session.PlaybackState, "切换显示模式不应改变播放状态");
            Assert.GreaterOrEqual(_session.CurrentTime, timeBefore, "切换显示模式不应导致时间回退");
            // ④ 不残留旧模式的按键高亮：高亮集合必须与"当前按下集合 + 预提示集合"一致
            yield return null;
            Assert.LessOrEqual(_highlighter.HighlightedKeyCount,
                _session.PressedPitchCount + _highlighter.UpcomingNoteCount,
                "切回展示模式后出现了不该有的残留高亮琴键");

            _session.Pause();
        }

        [UnityTest]
        public IEnumerator ToggleDisplayMode_FlipsBothWays()
        {
            Assert.AreEqual(DisplayMode.Showcase, _session.DisplayMode);

            _session.ToggleDisplayMode();
            yield return null;
            Assert.AreEqual(DisplayMode.Teaching, _session.DisplayMode);
            Assert.IsFalse(_avatar.HandsAndArmsVisible);

            _session.ToggleDisplayMode();
            yield return null;
            Assert.AreEqual(DisplayMode.Showcase, _session.DisplayMode);
            Assert.IsTrue(_avatar.HandsAndArmsVisible);
        }

        [UnityTest]
        public IEnumerator Teaching_SurvivesAvatarSwitch_HandsStayHidden()
        {
            _session.SetDisplayMode(DisplayMode.Teaching);
            yield return null;

            _session.CycleAvatar();
            yield return null;

            Assert.IsTrue(_session.IsTeachingMode, "切换角色不应改变显示模式");
            Assert.IsFalse(_avatar.HandsAndArmsVisible, "教学模式下换角色也必须保持手部隐藏");
        }
    }
}
