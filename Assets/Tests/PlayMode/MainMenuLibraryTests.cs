using System.Collections;
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.UI;
using UnityEngine;
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
