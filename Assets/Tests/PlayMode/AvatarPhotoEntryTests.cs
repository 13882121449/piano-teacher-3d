using System.Collections;
using NUnit.Framework;
using PianoTeacher3D.Core;
using PianoTeacher3D.Customization;
using PianoTeacher3D.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 照片个性化入口的端到端接线（方案 12.2：角色库 →「用照片定制外观」）。
    ///
    /// <para>
    /// 这层最容易在 UI 重排时被静默弄丢：面板类本身是好的，但没有任何代码创建它，
    /// 功能等于不存在。所以这里从场景加载开始，一路断言到面板真的被实例化并展开。
    /// </para>
    /// </summary>
    public class AvatarPhotoEntryTests
    {
        private LessonSessionController _session;
        private AvatarSelectionUI _avatarUI;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            _session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(_session, "PianoLesson 场景应包含 LessonSessionController");

            _avatarUI = Object.FindFirstObjectByType<AvatarSelectionUI>();
            Assert.IsNotNull(_avatarUI, "场景应包含 AvatarSelectionUI");

            _avatarUI.ShowPanel(true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Session_ExposesCustomizationPipeline()
        {
            Assert.IsNotNull(_session.Customization, "会话应装配照片外观管线");
            Assert.IsNotNull(_session.PrivacyConsent, "会话应装配隐私同意控制器");
            Assert.IsNotNull(_session.CurrentCharacterView, "应能取到当前角色契约实例");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AvatarPanel_HasPhotoEntryButton_ThatOpensPanel()
        {
            Button entry = FindPhotoEntryButton();
            Assert.IsNotNull(entry, "角色库面板应提供「用照片定制外观…」入口");
            Assert.IsTrue(entry.interactable, "外观管线可用时入口应可点");

            entry.onClick.Invoke();
            yield return null;

            var panel = Object.FindFirstObjectByType<PhotoCustomizationPanel>();
            Assert.IsNotNull(panel, "点击入口应实例化照片定制面板");
            Assert.IsTrue(panel.IsPanelOpen, "点击入口后面板应处于展开状态");

            // 关闭并清理：避免影响同批次其它用例的 UI 状态
            panel.ShowPanel(false);
            Object.Destroy(panel.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhotoEntry_UsesSlotSupportedByCurrentCharacter()
        {
            Button entry = FindPhotoEntryButton();
            Assert.IsNotNull(entry);
            entry.onClick.Invoke();
            yield return null;

            var panel = Object.FindFirstObjectByType<PhotoCustomizationPanel>();
            Assert.IsNotNull(panel);

            var view = _session.CurrentCharacterView;
            Assert.IsTrue(view.Supports(panel.AppliedSlot),
                $"入口选中的槽位 {panel.AppliedSlot} 必须是当前角色实际支持的槽位（方案 9.5）");

            panel.ShowPanel(false);
            Object.Destroy(panel.gameObject);
            yield return null;
        }

        private Button FindPhotoEntryButton()
        {
            var buttons = _avatarUI.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var label = buttons[i].GetComponentInChildren<Text>(true);
                if (label != null && label.text != null && label.text.Contains("照片")) return buttons[i];
            }
            return null;
        }
    }
}
