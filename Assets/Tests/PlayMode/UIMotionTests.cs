using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PianoTeacher3D.UI;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>界面弹簧动效（2026-11-18）：按钮按压回弹、分段选中胶囊滑动、面板弹出 / 收回。</summary>
    public class UIMotionTests
    {
        private GameObject _canvasGo;
        private Canvas _canvas;

        [SetUp]
        public void SetUp()
        {
            UIMotion.Override = false;   // 批处理默认关闭动效，这里强制打开
            _canvasGo = new GameObject("MotionTestCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = _canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        [TearDown]
        public void TearDown()
        {
            UIMotion.Override = null;
            Object.Destroy(_canvasGo);
        }

        [UnityTest]
        public IEnumerator Button_ScalesOnHoverAndPress_ThenSettles()
        {
            var button = UITheme.CreateButton(_canvas.transform, "动效", Vector2.zero, new Vector2(160f, 40f), null);
            Assert.IsNotNull(button.GetComponent<UIPressMotion>(), "所有按钮都应挂上按压动效");
            var data = new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) };

            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerEnterHandler);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.Greater(button.transform.localScale.x, 1.01f, "悬停应轻微放大");

            data.button = PointerEventData.InputButton.Left;
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.Less(button.transform.localScale.x, 0.985f, "按下应回缩");

            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerExitHandler);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.AreEqual(1f, button.transform.localScale.x, 0.002f, "松开离开后回到原大小");
            Assert.AreEqual(0, UIMotionTicker.ActiveCount, "静止后不再占用逐帧更新");
        }

        [UnityTest]
        public IEnumerator Segmented_PillSlidesToSelection()
        {
            var seg = UIKit.Segmented(_canvas.transform, "Seg", new Vector2(300f, 32f), new[] { "一", "二", "三" }, 0, null);
            yield return null;
            yield return null;
            var pill = seg.SelectionPill;
            Assert.IsNotNull(pill);
            Assert.AreEqual(2f, pill.anchoredPosition.x, 0.5f, "初始选中第一项");

            seg.SetSelectedIndex(2);
            yield return new WaitForSecondsRealtime(0.06f);
            float mid = pill.anchoredPosition.x;
            Assert.Greater(mid, 2.5f, "胶囊开始滑动");
            Assert.Less(mid, 202f - 0.5f, "胶囊不是瞬移过去的");
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.AreEqual(202f, pill.anchoredPosition.x, 0.6f, "胶囊停在第三项");
            Assert.AreEqual(96f, pill.sizeDelta.x, 0.6f);
        }

        [UnityTest]
        public IEnumerator Panel_AppearsAndHides()
        {
            var panel = UITheme.CreatePanel(_canvas.transform, "MotionPanel", Vector2.zero, new Vector2(200f, 120f),
                UITheme.PanelBgRaised, UITheme.RadiusMedium);
            panel.gameObject.SetActive(false);
            var appear = UIAppear.Attach(panel.gameObject, 0f, 0.9f);
            panel.gameObject.SetActive(true);
            var group = panel.GetComponent<CanvasGroup>();
            Assert.Less(group.alpha, 0.2f, "刚打开时是透明的");
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.AreEqual(1f, group.alpha, 0.01f);
            Assert.AreEqual(1f, panel.localScale.x, 0.01f);

            bool hidden = false;
            UIAppear.Hide(panel.gameObject, () => hidden = true);
            Assert.IsTrue(panel.gameObject.activeSelf, "收起动画期间仍然可见");
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.IsFalse(panel.gameObject.activeSelf);
            Assert.IsTrue(hidden);
            Assert.IsFalse(appear.Animating);
        }

        [Test]
        public void ReducedMotion_SnapsImmediately()
        {
            UIMotion.Override = true;
            var panel = UITheme.CreatePanel(_canvas.transform, "SnapPanel", Vector2.zero, new Vector2(200f, 120f),
                UITheme.PanelBgRaised, UITheme.RadiusMedium);
            panel.gameObject.SetActive(false);
            UIAppear.Attach(panel.gameObject);
            panel.gameObject.SetActive(true);
            Assert.AreEqual(1f, panel.GetComponent<CanvasGroup>().alpha, 0.001f);
            UIAppear.Hide(panel.gameObject);
            Assert.IsFalse(panel.gameObject.activeSelf);
        }
    }
}
