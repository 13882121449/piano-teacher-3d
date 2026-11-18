using System.Collections;
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 曲库卡片点击的回归用例。
    ///
    /// <para>
    /// 修复的缺陷：<c>MainMenuUI.BuildLibrarySection</c> 里 <c>() =&gt; LoadLesson(songs[i])</c>
    /// 捕获的是 <c>for</c> 的循环变量（所有卡片共享同一个 <c>i</c>），循环结束后 <c>i == shown</c>，
    /// 任何卡片被点击都会抛 <c>ArgumentOutOfRangeException</c>（线上崩溃栈就在这一行）。
    /// C# 只对 <c>foreach</c> 的迭代变量做逐次捕获，<c>for</c> 必须自己复制到局部变量。
    /// </para>
    ///
    /// <para>
    /// 断言方式：Unity Test Framework 会把任何被记录的异常判为失败，
    /// 所以「点一下不炸 + 真的切到课程场景」就足以钉住这个 bug。
    /// </para>
    /// </summary>
    public class MainMenuLibraryTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("MainMenu");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator LibraryCard_Click_LoadsSongWithoutIndexError()
        {
            var cards = FindSongCards();
            Assert.Greater(cards.Count, 0, "曲库应至少有一张卡片（StreamingAssets/Songs 下应有 .mid）");

            // 取最后一张：闭包 bug 修复前，任意卡片都会越界，但最后一张最容易暴露 off-by-one
            Button last = cards[cards.Count - 1];
            Assert.IsNotNull(last.onClick, "卡片应绑定点击回调");

            last.onClick.Invoke();
            yield return null;
            yield return null;

            Assert.AreEqual("PianoLesson", SceneManager.GetActiveScene().name,
                "点击曲目卡片应进入课程场景");
            Assert.IsTrue(File.Exists(MainMenuUI.SelectedSongPath),
                $"选中的曲目路径应真实存在：{MainMenuUI.SelectedSongPath}");
        }

        [UnityTest]
        public IEnumerator MainMenu_UsesOneLibraryAndOffersAPlayablePrimaryAction()
        {
            Assert.IsNull(GameObject.Find("EntryCard_选曲"), "首页已经提供曲库，不能再打开重复的选曲浮层");
            var library = GameObject.Find("LibraryViewport");
            Assert.IsNotNull(library, "所有曲目都应显示在唯一的可滚动曲库里");
            Assert.Greater(library.GetComponentsInChildren<Button>().Length, 0);
            var start = GameObject.Find("PracticePrimary");
            Assert.IsNotNull(start);
            Assert.IsTrue(start.GetComponent<Button>().interactable);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Settings_CloseRestoresFocusToItsTrigger()
        {
            var trigger = GameObject.Find("EntryCard_设置");
            Assert.IsNotNull(trigger);
            EventSystem.current.SetSelectedGameObject(trigger);
            trigger.GetComponent<Button>().onClick.Invoke();
            yield return null;

            var overlay = GameObject.Find("OverlaySettings");
            Assert.IsNotNull(overlay);
            var close = overlay.GetComponentInChildren<Button>(true);
            foreach (var button in overlay.GetComponentsInChildren<Button>())
                if (button.name == "Btn_关闭") close = button;
            Assert.AreEqual("Btn_关闭", close.name);
            Assert.AreEqual(close.gameObject, EventSystem.current.currentSelectedGameObject);
            close.onClick.Invoke();
            yield return null;

            Assert.AreEqual(trigger, EventSystem.current.currentSelectedGameObject);
        }

        [UnityTest]
        public IEnumerator StageVisual_MotionCanBeDisabled()
        {
            const string pref = "piano.reduceMotion";
            int previous = PlayerPrefs.GetInt(pref, -1);
            try
            {
                // 2026-11-18 精修：首页背景改为实时 3D 海底舞台（毛玻璃界面透出它），替代原静态三角琴图
                var room = Object.FindFirstObjectByType<MenuStageBackdrop>();
                var toggle = GameObject.Find("MenuMotionToggle");
                Assert.IsNotNull(room, "首页展示实时舞台背景");
                Assert.IsNotNull(toggle, "首页提供减少动效开关");
                Assert.IsNotNull(room.Environment, "背景复用课程场景的舞台环境");
                Assert.IsNotNull(room.transform.Find("SceneEnvironment/UnderwaterHabitat"), "背景是海底舞台");
                Assert.IsNotNull(room.GetComponentInChildren<PianoTeacher3D.Piano.PianoKeyMap>(), "舞台上有三角琴");

                var button = toggle.GetComponent<Button>();
                button.onClick.Invoke();
                Assert.AreEqual(previous == 1 ? 0 : 1, PlayerPrefs.GetInt(pref),
                    "切换减少动效时记住选择");
                Assert.AreEqual("减少动效：" + (previous == 1 ? "关" : "开"),
                    UITheme.GetLabel(button).text);
                Assert.AreEqual(previous != 1, room.IsMotionReduced, "减少动效时镜头停止漂移");

                var cam = Camera.main.transform;
                Vector3 before = cam.position;
                yield return null;
                yield return null;
                if (room.IsMotionReduced)
                    Assert.AreEqual(0f, Vector3.Distance(before, cam.position), 1e-5f, "减少动效时镜头保持静止");
            }
            finally
            {
                if (previous >= 0) PlayerPrefs.SetInt(pref, previous);
                else PlayerPrefs.DeleteKey(pref);
                PlayerPrefs.Save();
            }
            yield return null;
        }

        private static System.Collections.Generic.List<Button> FindSongCards()
        {
            var result = new System.Collections.Generic.List<Button>();
            var buttons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].gameObject.name.StartsWith("Card_"))
                    result.Add(buttons[i]);
            }
            return result;
        }
    }
}
