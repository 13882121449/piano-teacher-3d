using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 分段选择器（对应方案 3.4 / 审查 2.1「用分段选择器直接显示当前选中项，不要靠循环点击猜当前值」）。
    /// <para>
    /// 用于：练习手「双手 | 左手 | 右手」、视角「特写 | 全景 | 自由」等互斥选项。
    /// 视觉：透明轨道（仅 1px 分组描边）+ 选中项 <see cref="UITheme.Selected"/> 底 +
    /// 1px <see cref="UITheme.BorderSelected"/> 边 + 粗体 —— 底色 / 描边 / 字重三重表达，不依赖颜色单一维度。
    /// </para>
    /// <para>纯表现层：只通过 <see cref="OnChanged"/> 通知调用方，自身不读写会话状态。</para>
    /// </summary>
    public class UISegmentedControl : MonoBehaviour, IUIMotionTickable
    {
        // 2026-11-18：iOS 式滑动选中胶囊。选中项本身透明，蓝色胶囊在各项下方弹簧滑动到新位置
        private Image _pill;
        private float _pillX, _pillXVel, _pillW, _pillWVel;
        private bool _pillPlaced;
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
            // 轨道本身透明：靠 1px 分组描边标识「这是一组互斥选项」，不额外叠一层底色（避免第 4 级色）
            var img = gameObject.AddComponent<Image>();
            img.color = UITheme.Transparent;
            var sprite = UITheme.GetRoundedSprite(UITheme.RadiusSmall);
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            img.raycastTarget = false;
            UITheme.SetOutline(img, true, UITheme.Divider, 1f);

            var pillGo = new GameObject("SelectionPill", typeof(RectTransform));
            pillGo.transform.SetParent(_rt, false);
            _pill = pillGo.AddComponent<Image>();
            _pill.raycastTarget = false;
            _pill.color = UITheme.Selected;
            UITheme.SetRoundedSprite(_pill, Mathf.Max(4, UITheme.RadiusSmall - 2));
            UITheme.SetOutline(_pill, true, UITheme.BorderSelected, 1f);
            var prt = _pill.rectTransform;
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 0.5f);
            prt.sizeDelta = new Vector2(0f, -4f);
            prt.anchoredPosition = Vector2.zero;
            _pill.enabled = false;
        }

        /// <summary>选中胶囊（测试 / 诊断用）。</summary>
        public RectTransform SelectionPill => _pill != null ? _pill.rectTransform : null;

        private void OnRectTransformDimensionsChange()
        {
            // 尺寸变化（窗口缩放）时胶囊直接对齐，不做动画
            _pillPlaced = false;
            if (isActiveAndEnabled) UIMotionTicker.Add(this);
        }

        private void OnEnable()
        {
            _pillPlaced = false;
            UIMotionTicker.Add(this);
        }

        private void OnDisable()
        {
            UIMotionTicker.Remove(this);
        }

        public bool Tick(float dt)
        {
            if (this == null || _pill == null || _rt == null) return false;
            int n = _segments.Count;
            float width = _rt.rect.width;
            if (n == 0 || _selected < 0 || width <= 1f)
            {
                _pill.enabled = false;
                return width <= 1f && n > 0;   // 布局还没算出宽度：下一帧再试
            }
            float segment = width / n;
            float targetX = segment * _selected + 2f;
            float targetW = segment - 4f;
            bool enabled = _interactable && _optionEnabled[_selected];
            _pill.color = enabled ? UITheme.Selected : UITheme.Disabled;
            _pill.enabled = true;
            if (!_pillPlaced || UIMotion.Reduced)
            {
                _pillX = targetX; _pillW = targetW; _pillXVel = _pillWVel = 0f;
                _pillPlaced = true;
            }
            else
            {
                UIMotion.Spring(ref _pillX, ref _pillXVel, targetX, 7f, 0.78f, dt);
                UIMotion.Spring(ref _pillW, ref _pillWVel, targetW, 7f, 0.9f, dt);
            }
            var prt = _pill.rectTransform;
            prt.anchoredPosition = new Vector2(_pillX, 0f);
            prt.sizeDelta = new Vector2(Mathf.Max(0f, _pillW), -4f);
            return !(UIMotion.Settled(_pillX, _pillXVel, targetX, 0.2f) && UIMotion.Settled(_pillW, _pillWVel, targetW, 0.2f));
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
                    () => Select(index, true), _fontSize, ButtonStyle.Ghost);
                // 分段项等分容器宽度：窄窗口下不会挤压其它控件
                UITheme.StretchFraction(btn.GetComponent<RectTransform>(), i / (float)options.Length,
                    (i + 1) / (float)options.Length, 1f);

                var label = UITheme.GetLabel(btn);
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleCenter;
                    UITheme.StretchAll(label.rectTransform, 2f, 2f, 0f, 0f);
                }
                UIHoverFeedback.Attach(btn, UITheme.Transparent, UITheme.ControlHover);
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
                bool enabled = _interactable && _optionEnabled[i];

                var btn = _segments[i];
                UITheme.PaintSegment(btn, selected, enabled);

                var label = _labels[i];
                if (label != null) label.text = _optionLabels[i];

                var feedback = btn != null ? btn.GetComponent<UIHoverFeedback>() : null;
                if (feedback != null)
                {
                    feedback.SetInteractable(enabled);
                    // 选中项由滑动胶囊表示：不再给透明底加描边（Outline 会把整个图形偏移复制四份，叠成一块白底）
                    feedback.SetOutlineState(false, UITheme.BorderSelected);
                    // 选中项悬停保持金色底（避免一悬停就失去「当前值」的视觉）
                    // 未选中项给实心 Control 底（与 chip 一致）：透明底在深色场景里只剩一圈描边，
                    // 观感上是「空白灰按钮」，分不清是禁用还是没选中。
                    feedback.SetColors(selected ? UITheme.Transparent : UITheme.Control,
                        !enabled ? UITheme.Disabled : (selected ? UITheme.Selected : UITheme.ControlHover));
                }
                // 选中项透明，由下方的滑动胶囊显示选中底色
                if (selected && btn != null && btn.targetGraphic is Image selectedImage)
                {
                    selectedImage.material = null;
                    selectedImage.color = UITheme.Transparent;
                    UITheme.SetOutline(selectedImage, false, UITheme.BorderSelected, 1f);
                }
            }
            if (isActiveAndEnabled) UIMotionTicker.Add(this);
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
