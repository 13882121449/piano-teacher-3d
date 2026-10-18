using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 分段选择器（对应方案 3.4 / 审查 2.1「用分段选择器直接显示当前选中项，不要靠循环点击猜当前值」）。
    /// <para>
    /// 用于：练习手「双手 | 左手 | 右手」、速度「0.5× | 1.0× | 1.5×」、高亮「关闭 | 预提示 | 发声 | 全部」。
    /// 选中态同时使用强调底 + 描边 + 「▍」标记 + 粗体，不依赖颜色单一维度（方案 3.3 / 审查 2.3）。
    /// </para>
    /// <para>纯表现层：只通过 <see cref="OnChanged"/> 通知调用方，自身不读写会话状态。</para>
    /// </summary>
    public class UISegmentedControl : MonoBehaviour
    {
        /// <summary>选中项标记符（颜色之外的区分维度）。</summary>
        private const string SelectedMarker = "▍";

        private readonly List<Button> _segments = new List<Button>();
        private readonly List<Text> _labels = new List<Text>();
        private readonly List<string> _optionLabels = new List<string>();
        private readonly List<bool> _optionEnabled = new List<bool>();

        private RectTransform _rt;
        private int _selected = -1;
        private int _fontSize = UITheme.FontCaption;
        private bool _interactable = true;

        /// <summary>选中项变化（参数为新的索引）。</summary>
        public event Action<int> OnChanged;

        /// <summary>当前选中索引（-1 = 无可选项）。</summary>
        public int SelectedIndex => _selected;

        /// <summary>分段数量。</summary>
        public int Count => _segments.Count;

        /// <summary>控件自身矩形（供调用方做锚点布局）。</summary>
        public RectTransform Rect => _rt;

        /// <summary>整体可交互性（加载 / 无曲目时统一禁用）。</summary>
        public bool Interactable
        {
            get => _interactable;
            set
            {
                if (_interactable == value) return;
                _interactable = value;
                RefreshStyles();
            }
        }

        /// <summary>
        /// 创建分段选择器。
        /// <paramref name="options"/> = 各分段文字，<paramref name="initialIndex"/> = 初始选中项，
        /// <paramref name="onChanged"/> 只在用户点击时触发（初始化不会回调）。
        /// </summary>
        public static UISegmentedControl Create(Transform parent, string name, Vector2 pos, Vector2 size,
            string[] options, int initialIndex, Action<int> onChanged, int fontSize = UITheme.FontCaption)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var control = go.AddComponent<UISegmentedControl>();
            control._rt = rt;
            control._fontSize = fontSize;
            control.BuildTrack();
            control.SetOptions(options);
            control.SetSelectedIndex(initialIndex, false);
            if (onChanged != null) control.OnChanged += onChanged;
            return control;
        }

        private void BuildTrack()
        {
            var img = gameObject.AddComponent<Image>();
            img.color = UITheme.Control;
            var sprite = UITheme.GetRoundedSprite(UITheme.RadiusSmall);
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            UITheme.SetOutline(img, true, UITheme.Divider); // 常驻细边框：分组身份可辨
        }

        /// <summary>重建选项（文字可随语言 / 语义变化）。</summary>
        public void SetOptions(string[] options)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                if (_segments[i] != null) Destroy(_segments[i].gameObject);
            }
            _segments.Clear();
            _labels.Clear();
            _optionLabels.Clear();
            _optionEnabled.Clear();

            if (options == null || options.Length == 0)
            {
                _selected = -1;
                return;
            }

            for (int i = 0; i < options.Length; i++)
            {
                string text = options[i] ?? "";
                _optionLabels.Add(text);
                _optionEnabled.Add(true);

                int index = i;
                var btn = UITheme.CreateButton(_rt, text, Vector2.zero, Vector2.zero,
                    () => Select(index, true), _fontSize, ButtonStyle.Secondary);
                // 分段项等分容器宽度：窄窗口下不会挤压主按钮
                UITheme.StretchFraction(btn.GetComponent<RectTransform>(), i / (float)options.Length,
                    (i + 1) / (float)options.Length, 2f);

                var label = UITheme.GetLabel(btn);
                if (label != null) label.alignment = TextAnchor.MiddleCenter;
                _segments.Add(btn);
                _labels.Add(label);
            }

            if (_selected >= options.Length) _selected = options.Length - 1;
            RefreshStyles();
            RefreshFocusChain();
        }

        /// <summary>设置选中项；<paramref name="notify"/> = true 时触发 <see cref="OnChanged"/>。</summary>
        public void SetSelectedIndex(int index, bool notify = false)
        {
            if (_segments.Count == 0) return;
            index = Mathf.Clamp(index, 0, _segments.Count - 1);
            bool changed = index != _selected;
            _selected = index;
            RefreshStyles();
            if (changed && notify) OnChanged?.Invoke(_selected);
        }

        /// <summary>通过文字匹配设置选中项（状态恢复时用，找不到则不改变）。</summary>
        public bool SetSelectedByLabel(string optionLabel, bool notify = false)
        {
            int index = _optionLabels.IndexOf(optionLabel);
            if (index < 0) return false;
            SetSelectedIndex(index, notify);
            return true;
        }

        /// <summary>逐项启用 / 禁用（例如某手无音符时禁用该项）。</summary>
        public void SetOptionEnabled(int index, bool enabled)
        {
            if (index < 0 || index >= _optionEnabled.Count) return;
            _optionEnabled[index] = enabled;
            RefreshStyles();
        }

        /// <summary>设置某一项的悬停提示（快捷键写在提示里）。</summary>
        public void SetOptionTooltip(int index, string tooltip, string shortcut = null)
        {
            if (index < 0 || index >= _segments.Count) return;
            UITheme.SetButtonTooltip(_segments[index], UITheme.ComposeTooltip(tooltip, shortcut));
        }

        /// <summary>取得某一项按钮（供整体焦点链使用）。</summary>
        public Button GetSegment(int index)
            => index >= 0 && index < _segments.Count ? _segments[index] : null;

        /// <summary>当前选中项文字（用于状态标签）。</summary>
        public string SelectedLabel
            => _selected >= 0 && _selected < _optionLabels.Count ? _optionLabels[_selected] : "";

        /// <summary>取出一段可读的状态文案，例如「练习手：左手」。</summary>
        public string Describe(string prefix)
            => string.IsNullOrEmpty(prefix) ? SelectedLabel : $"{prefix}：{SelectedLabel}";

        private void Select(int index, bool notify)
        {
            if (!_interactable) return;
            if (index < 0 || index >= _segments.Count) return;
            if (!_optionEnabled[index]) return;
            SetSelectedIndex(index, notify);
        }

        private void RefreshStyles()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                bool selected = i == _selected;
                ButtonState state;
                if (!_interactable || !_optionEnabled[i]) state = ButtonState.Disabled;
                else state = selected ? ButtonState.Selected : ButtonState.Default;

                var btn = _segments[i];
                UITheme.ApplyButtonState(btn, state, ButtonStyle.Secondary);

                var label = _labels[i];
                if (label != null)
                {
                    label.text = selected ? SelectedMarker + _optionLabels[i] : _optionLabels[i];
                    label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        private void RefreshFocusChain()
        {
            var chain = new List<Selectable>(_segments.Count);
            for (int i = 0; i < _segments.Count; i++) chain.Add(_segments[i]);
            UITheme.ChainHorizontal(chain.ToArray());
        }

        private void OnDestroy()
        {
            OnChanged = null;
        }
    }
}
