using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 悬停提示（对应方案 3.4「快捷键只在合理焦点下生效」与审查 2.2C「鼠标悬停按钮时显示简短用途，
    /// 快捷键写在提示中，例如『节拍器 · M』」）。
    /// <para>
    /// 用法：<c>UITooltip.SetText(button.gameObject, "节拍器 · M")</c>。
    /// 提示宿主是全局唯一对象（放在 overrideSorting 的嵌套 Canvas 上，永远绘制在其它 UI 之上），
    /// 位置自动夹取在屏幕内，键盘焦点与悬停都会显示。
    /// </para>
    /// <para>
    /// 注意：关键信息（当前速度 / 视角 / 练习手 / 循环状态 / A-B 时间）必须同时显示在界面上，
    /// 悬停提示只做补充（方案 3.3「不把关键信息只放进悬停提示」）。
    /// </para>
    /// </summary>
    public class UITooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
        ISelectHandler, IDeselectHandler
    {
        private string _text;
        private Coroutine _pending;

        private static GameObject _host;
        private static RectTransform _hostRt;
        private static Text _hostLabel;
        private static UITooltip _shownBy;

        /// <summary>提示是否可见（用于外部判断，不参与业务）。</summary>
        public static bool IsVisible => _host != null && _host.activeSelf;

        /// <summary>给目标控件设置（或更新）悬停提示文本；<paramref name="text"/> 为空时仅清除文本。</summary>
        public static UITooltip SetText(GameObject target, string text)
        {
            if (target == null) return null;
            var tip = target.GetComponent<UITooltip>();
            if (tip == null)
            {
                if (string.IsNullOrEmpty(text)) return null;
                tip = target.AddComponent<UITooltip>();
            }
            tip._text = text;
            return tip;
        }

        /// <summary>兼容别名：附加提示并返回触发组件。</summary>
        public static UITooltip Attach(GameObject target, string text) => SetText(target, text);

        /// <summary>立即隐藏提示（点击按钮、打开浮层、切场景时调用）。</summary>
        public static void Hide()
        {
            if (_shownBy != null)
            {
                _shownBy.CancelPending();
                _shownBy = null;
            }
            if (_host != null && _host.activeSelf) _host.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(_text)) return;
            CancelPending();
            _pending = StartCoroutine(ShowAfterDelay());
        }

        public void OnPointerExit(PointerEventData eventData) => HideIfOwner();

        public void OnPointerDown(PointerEventData eventData) => HideIfOwner();

        public void OnSelect(BaseEventData eventData)
        {
            // 键盘焦点同样给出提示（方案 3.4「控件应有键盘焦点顺序」）
            if (string.IsNullOrEmpty(_text)) return;
            CancelPending();
            _pending = StartCoroutine(ShowAfterDelay());
        }

        public void OnDeselect(BaseEventData eventData) => HideIfOwner();

        private void OnDisable() => HideIfOwner();

        private void OnDestroy()
        {
            CancelPending();
            if (_shownBy == this) Hide();
        }

        private void HideIfOwner()
        {
            CancelPending();
            if (_shownBy == this) Hide();
        }

        private void CancelPending()
        {
            if (_pending == null) return;
            StopCoroutine(_pending);
            _pending = null;
        }

        private System.Collections.IEnumerator ShowAfterDelay()
        {
            // 使用 unscaled 时间：暂停播放时提示仍应正常出现
            float until = Time.unscaledTime + UITheme.TooltipDelaySeconds;
            while (Time.unscaledTime < until) yield return null;
            _pending = null;
            if (!isActiveAndEnabled || string.IsNullOrEmpty(_text)) yield break;
            ShowNow();
        }

        private void ShowNow()
        {
            var canvas = GetComponentInParent<Canvas>();
            Transform root = canvas != null ? canvas.transform : transform.root;
            EnsureHost(root);
            if (_host == null || _hostRt == null || _hostLabel == null) return;

            Hide(); // 先收起上一个
            _hostLabel.text = _text;

            float maxWidth = UITheme.Layout.TooltipMaxWidth;
            _hostLabel.rectTransform.sizeDelta = new Vector2(maxWidth, 0f);
            Canvas.ForceUpdateCanvases();

            float textWidth = Mathf.Min(maxWidth, Mathf.Max(40f, _hostLabel.preferredWidth));
            float height = Mathf.Max(UITheme.Layout.TooltipMinHeight,
                _hostLabel.preferredHeight + UITheme.Layout.TooltipPadY * 2f);
            _hostLabel.rectTransform.sizeDelta = new Vector2(textWidth, height);
            _hostRt.sizeDelta = new Vector2(textWidth + UITheme.Layout.TooltipPadX * 2f, height);

            float scale = canvas != null ? canvas.scaleFactor : 1f;
            if (scale <= 0f) scale = 1f;

            var self = transform as RectTransform;
            Vector3 anchorWorld = self != null ? self.position : transform.position;
            float selfHeightPx = self != null ? self.rect.height * scale : 0f;
            float hostHeightPx = _hostRt.sizeDelta.y * scale;

            float x = anchorWorld.x;
            float y = anchorWorld.y + selfHeightPx * 0.5f + UITheme.Layout.TooltipGap * scale + hostHeightPx * 0.5f;

            float halfW = _hostRt.sizeDelta.x * scale * 0.5f;
            float margin = UITheme.Layout.TooltipScreenMargin;
            x = Mathf.Clamp(x, halfW + margin, Mathf.Max(halfW + margin, Screen.width - halfW - margin));
            y = Mathf.Min(y, Screen.height - margin - hostHeightPx * 0.5f);

            _hostRt.position = new Vector3(x, y, 0f);
            _host.SetActive(true);
            _shownBy = this;
        }

        /// <summary>创建（或复用）全局提示宿主：嵌套 Canvas + overrideSorting，永远最上层。</summary>
        private static void EnsureHost(Transform root)
        {
            if (_host != null && _hostRt != null)
            {
                if (root != null && _host.transform.parent != root)
                    _host.transform.SetParent(root, false);
                return;
            }

            _host = new GameObject("UITooltipHost");
            if (root != null) _host.transform.SetParent(root, false);

            var nested = _host.AddComponent<Canvas>();
            nested.overrideSorting = true;
            nested.sortingOrder = UITheme.Layout.TooltipSortingOrder;
            _host.AddComponent<GraphicRaycaster>();
            _hostRt = _host.GetComponent<RectTransform>();
            _hostRt.anchorMin = new Vector2(0.5f, 0.5f);
            _hostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _hostRt.pivot = new Vector2(0.5f, 0.5f);

            var bg = _host.AddComponent<Image>();
            bg.sprite = UITheme.GetRoundedSprite(UITheme.RadiusSmall);
            if (bg.sprite != null) bg.type = Image.Type.Sliced;
            bg.color = UITheme.PanelBgRaised;
            bg.raycastTarget = false;
            UITheme.SetOutline(bg, true, UITheme.Divider, 1f);

            _hostLabel = UITheme.CreateText(_host.transform, "", Vector2.zero, new Vector2(200f, 20f),
                UITheme.FontBodySmall, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            _hostLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hostLabel.verticalOverflow = VerticalWrapMode.Overflow;
            _hostLabel.raycastTarget = false;

            _host.SetActive(false);
        }
    }
}
