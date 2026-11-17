using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PianoTeacher3D.Tests.PlayMode
{
    public sealed class GuidedClassroomTests
    {
        [UnityTest]
        public IEnumerator FirstLesson_CorrectKeyAdvancesAndWrongKeyDoesNot()
        {
            SceneManager.LoadScene("MainMenu");
            yield return null;
            yield return null;
            Button entry = GameObject.Find("PracticePrimary").GetComponent<Button>();
            entry.onClick.Invoke();
            yield return null;
            yield return null;
            var status = GameObject.Find("GuidedFeedback");
            Assert.IsNotNull(status, "首页主按钮应进入第一课");
            GameObject.Find("GuidedStartWait").GetComponent<Button>().onClick.Invoke();
            GameObject.Find("GuidedKey_62").GetComponent<Button>().onClick.Invoke();
            StringAssert.Contains("弹成了 D4", status.GetComponent<Text>().text);
            GameObject.Find("GuidedKey_60").GetComponent<Button>().onClick.Invoke();
            StringAssert.Contains("C4 正确", status.GetComponent<Text>().text);
            StringAssert.Contains("D4", GameObject.Find("GuidedPrompt").GetComponent<Text>().text);
        }

        [UnityTest]
        public IEnumerator FirstLesson_IndependentModeHidesTargetNotes()
        {
            SceneManager.LoadScene("MainMenu");
            yield return null;
            yield return null;
            GameObject.Find("PracticePrimary").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
            var independent = GameObject.Find("GuidedStartIndependent");
            Assert.IsNotNull(independent, "课堂应提供独立考核入口");
            independent.GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(GameObject.Find("GuidedScore").GetComponent<CanvasGroup>().blocksRaycasts);
            Assert.AreEqual(0f, GameObject.Find("GuidedScore").GetComponent<CanvasGroup>().alpha);
            StringAssert.DoesNotContain("C4", GameObject.Find("GuidedPrompt").GetComponent<Text>().text);
        }
    }
}
