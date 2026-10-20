using System;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 几何图标类型（全部用程序化 sprite / 纯色矩形拼出来，不引入任何图片素材）。
    /// <para>图标只做「视觉锚点」，语义始终由同一控件上的文字承担（方案 3.3 / 审查 2.2C）。</para>
    /// </summary>
    public enum UIIcon
    {
        None = 0,
        ArrowLeft,
        ChevronDown,
        Replay,
        Help,
        Play,
        Pause
    }

    /// <summary>
    /// 统一构件库（UIKit）：面板 / 卡片 / chip / 图标按钮 / 主按钮 / 分段控件 / 进度条 / 区块标题。
    /// <para>
    /// 设计目标：调用方**只**传尺寸、文字、图标与回调，圆角、内边距、悬停、焦点环、禁用态、
    /// raycastTarget 全部由这里统一处理，避免各脚本各写一套导致「顶栏四个浮块高度不一致」这类问题。
    /// </para>
    /// <para>纯表现层：不读取任何业务状态，回调由调用方提供。</para>
    /// </summary>
    public static class UIKit
    {
        // ==================== 面板 / 分隔 ====================

        /// <summary>圆角面板（默认 12 圆角），纯容器，不拦截射线。</summary>
        public static RectTransform Panel(Transform parent, string name, Color color,
            int radius = UITheme.RadiusLarge)
        {
            var rt = UITheme.CreatePanel(parent, name, Vector2.zero, Vector2.zero, color, radius);
            var img = rt.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            return rt;
        }

        /// <summary>1px 分割线（仅用于面板内分区）。</summary>
        public static RectTransform Divider(Transform parent, string name)
            => UITheme.CreateDivider(parent, name, Vector2.zero, Vector2.one);

        // ==================== 文本 ====================

        /// <summary>单行文本（默认左对齐、不换行、不拦截射线）。</summary>
        public static Text Label(Transform parent, string content, int fontSize, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal, string name = "Label")
        {
            var text = UITheme.CreateText(parent, content, Vector2.zero, Vector2.zero, fontSize, color, anchor, style);
            text.gameObject.name = name;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return text;
        }

        /// <summary>区块标题（12 号、次级色、加粗）：用来给控件分组，替代「一行密集状态文字」。</summary>
        public static Text SectionHeader(Transform parent, string content)
        {
            var text = UITheme.CreateText(parent, content, Vector2.zero, Vector2.zero,
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Bold);
            text.gameObject.name = "SectionHeader";
            return text;
        }

        // ==================== chip ====================

        /// <summary>
        /// 带状态后缀的小控件（chip）：常态透明 + 1px 描边，选中 <see cref="UITheme.Selected"/> 底。
        /// <paramref name="onClick"/> 为空时只做状态展示（仍保留悬停 / 焦点反馈）。
        /// </summary>
        public static Button Chip(Transform parent, string name, float width,
            UnityEngine.Events.UnityAction onClick, string tooltip = null, string shortcut = null)
        {
            var btn = UITheme.CreateButton(parent, "", Vector2.zero,
                new Vector2(width, UITheme.Layout.ChipHeight), onClick,
                UITheme.FontBodySmall, ButtonStyle.Ghost);
            btn.gameObject.name = name;

            var label = UITheme.GetLabel(btn);
            if (label != null)
            {
                label.alignment = TextAnchor.MiddleCenter;
                UITheme.StretchAll(label.rectTransform, UITheme.Space8, UITheme.Space8, 0f, 0f);
            }

            UITheme.PaintChip(btn, false, true);
            var feedback = UIHoverFeedback.Attach(btn, UITheme.Transparent, UITheme.ControlHover);
            if (feedback != null) feedback.SetOutlineState(true, UITheme.Divider);

            if (!string.IsNullOrEmpty(tooltip))
                UITheme.SetButtonTooltip(btn, UITheme.ComposeTooltip(tooltip, shortcut));
            return btn;
        }

        /// <summary>
        /// 刷新 chip 文案与状态：文案统一写成「名称：值」，值本身就在界面上，不依赖悬停提示。
        /// </summary>
        public static void SetChip(Button chip, string label, string value, bool active, bool enabled)
        {
            if (chip == null) return;
            UITheme.SetButtonLabel(chip, string.IsNullOrEmpty(value) ? label : $"{label}：{value}");
            UITheme.PaintChip(chip, active, enabled);

            var feedback = chip.GetComponent<UIHoverFeedback>();
            if (feedback != null)
            {
                feedback.SetInteractable(enabled);
                feedback.SetOutlineState(true, active && enabled ? UITheme.BorderSelected : UITheme.Divider);
                // 开启态悬停不再提亮（保持金色底，避免「选中项一悬停就变灰」）
                var idle = UITheme.ChipBackground(active, enabled);
                var hover = !enabled ? idle : (active ? UITheme.Selected : UITheme.ControlHover);
                feedback.SetColors(idle, hover);
            }
        }

        // ==================== 按钮 ====================

        /// <summary>
        /// 透明底文字按钮（顶栏曲名 / 低频工具）：常态无底，悬停出 <see cref="UITheme.ControlHover"/>。
        /// </summary>
        public static Button TextButton(Transform parent, string label, float width, float height,
            UnityEngine.Events.UnityAction onClick, int fontSize = UITheme.FontBodySmall,
            string tooltip = null, string shortcut = null)
        {
            var btn = UITheme.CreateButton(parent, label, Vector2.zero, new Vector2(width, height), onClick,
                fontSize, ButtonStyle.Ghost);
            if (btn.targetGraphic is Image img)
            {
                img.color = UITheme.Transparent;
                var sprite = UITheme.GetRoundedSprite(UITheme.RadiusSmall);
                if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            }
            UITheme.ApplyFlatLook(btn);
            ApplyGhostText(btn);
            UIHoverFeedback.Attach(btn, UITheme.Transparent, UITheme.ControlHover);
            if (!string.IsNullOrEmpty(tooltip))
                UITheme.SetButtonTooltip(btn, UITheme.ComposeTooltip(tooltip, shortcut));
            return btn;
        }

        /// <summary>图标按钮（方形，32×32 起）：图标 + 必需的悬停提示，禁用由调用方统一处理。</summary>
        public static Button IconButton(Transform parent, string name, UIIcon icon, float size,
            UnityEngine.Events.UnityAction onClick, string tooltip, string shortcut = null)
        {
            var btn = UITheme.CreateButton(parent, name, Vector2.zero, new Vector2(size, size), onClick,
                UITheme.FontBodySmall, ButtonStyle.Ghost);
            btn.gameObject.name = name;

            // 图标按钮不承担文字语义：本体的文字被移除，只保留 tooltip 里的完整说明
            var label = UITheme.GetLabel(btn);
            if (label != null) UnityEngine.Object.Destroy(label.gameObject);

            if (btn.targetGraphic is Image img)
            {
                img.color = UITheme.Transparent;
                var sprite = UITheme.GetRoundedSprite(UITheme.RadiusSmall);
                if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            }
            UITheme.ApplyFlatLook(btn);

            var iconRoot = BuildIcon(btn.transform, icon, UITheme.TextPrimary, size * 0.5f);
            iconRoot.name = "Icon";

            var feedback = UIHoverFeedback.Attach(btn, UITheme.Transparent, UITheme.ControlHover);
            if (feedback != null) feedback.SetOutlineState(false, UITheme.Divider);

            UITheme.SetButtonTooltip(btn, UITheme.ComposeTooltip(tooltip, shortcut));
            return btn;
        }

        /// <summary>主按钮（全屏唯一的实心强调色填充）：96×40，悬停提亮到 <see cref="UITheme.AccentBright"/>。</summary>
        public static Button PrimaryButton(Transform parent, string label, Vector2 size,
            UnityEngine.Events.UnityAction onClick, string tooltip = null, string shortcut = null,
            int fontSize = UITheme.FontSubtitle)
        {
            var btn = UITheme.CreateButton(parent, label, Vector2.zero, size, onClick, fontSize,
                ButtonStyle.Primary);
            var img = btn.targetGraphic as Image;
            if (img != null)
            {
                var sprite = UITheme.GetRoundedSprite(UITheme.RadiusMedium);
                if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
                UITheme.SetOutline(img, false, UITheme.BorderSelected, 1f);
            }
            var text = UITheme.GetLabel(btn);
            if (text != null)
            {
                text.fontStyle = FontStyle.Bold;
                text.color = UITheme.TextOnAccent;
            }
            UITheme.SetButtonTooltip(btn, UITheme.ComposeTooltip(tooltip, shortcut));
            return btn;
        }

        /// <summary>次按钮（40 高，Control 底 + 1px 描边，悬停 ControlHover）。</summary>
        public static Button SecondaryButton(Transform parent, string label, Vector2 size,
            UnityEngine.Events.UnityAction onClick, string tooltip = null, string shortcut = null,
            int fontSize = UITheme.FontBodySmall)
        {
            var btn = UITheme.CreateButton(parent, label, Vector2.zero, size, onClick, fontSize,
                ButtonStyle.Secondary);
            var img = btn.targetGraphic as Image;
            if (img != null)
            {
                var sprite = UITheme.GetRoundedSprite(UITheme.RadiusMedium);
                if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
                UITheme.SetOutline(img, true, UITheme.Divider, 1f);
            }
            UITheme.SetButtonTooltip(btn, UITheme.ComposeTooltip(tooltip, shortcut));
            return btn;
        }

        // ==================== 卡片 ====================

        /// <summary>
        /// 曲目卡片（240×96，12 圆角，<see cref="UITheme.PanelBgRaised"/> 底）：
        /// 悬停底色转 <see cref="UITheme.ControlHover"/> 并升起 2px；卡内有 2px 阴影条。
        /// </summary>
        public static Button Card(Transform parent, string title, string meta, Vector2 size,
            UnityEngine.Events.UnityAction onClick, string tooltip = null)
        {
            var btn = UITheme.CreateButton(parent, "", Vector2.zero, size, onClick,
                UITheme.FontSubtitle, ButtonStyle.Secondary);
            btn.gameObject.name = "Card_" + (string.IsNullOrEmpty(title) ? "Song" : title);

            if (btn.targetGraphic is Image img)
            {
                var sprite = UITheme.GetRoundedSprite(UITheme.RadiusLarge);
                if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
                img.color = UITheme.PanelBgRaised;
                UITheme.SetOutline(img, true, UITheme.Divider, 1f);
            }

            // 底部 2px 阴影条（左右各内缩一个圆角，避免露出直角）
            var shadow = UITheme.CreatePanel(btn.transform, "Shadow", Vector2.zero,
                new Vector2(size.x - UITheme.RadiusLarge * 2f, 2f), new Color(0f, 0f, 0f, 0.35f),
                UITheme.RadiusBar);
            UITheme.Place(shadow, UITheme.RadiusLarge, -2f, size.x - UITheme.RadiusLarge * 2f, 2f);

            var label = UITheme.GetLabel(btn);
            if (label != null)
            {
                label.text = title ?? "";
                label.alignment = TextAnchor.UpperLeft;
                label.fontStyle = FontStyle.Bold;
                label.color = UITheme.TextPrimary;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                // 标题区 40 高（15 号字两行）+ 4 间隙 + 元信息 20（12 号字一行）+ 上下 16
                UITheme.StretchAll(label.rectTransform, UITheme.Space16, UITheme.Space16, 40f, UITheme.Space16);
            }

            var metaText = Label(btn.transform, meta ?? "", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.UpperLeft, FontStyle.Normal, "Meta");
            UITheme.StretchAll(metaText.rectTransform, UITheme.Space16, UITheme.Space16, UITheme.Space16, 60f);
            metaText.horizontalOverflow = HorizontalWrapMode.Wrap;
            metaText.verticalOverflow = VerticalWrapMode.Truncate;

            UIHoverFeedback.Attach(btn, UITheme.PanelBgRaised, UITheme.ControlHover, 2f);
            if (!string.IsNullOrEmpty(tooltip)) UITheme.SetButtonTooltip(btn, tooltip);
            return btn;
        }

        // ==================== 分段控件 / 进度条 ====================

        /// <summary>分段选择器（透明轨道 + 选中 <see cref="UITheme.Selected"/> 底 + 1px 边框）。</summary>
        public static UISegmentedControl Segmented(Transform parent, string name, Vector2 size,
            string[] options, int selected, Action<int> onChanged, int fontSize = UITheme.FontBodySmall)
            => UISegmentedControl.Create(parent, name, Vector2.zero, size, options, selected, onChanged, fontSize);

        /// <summary>进度条（6px 细轨道 + 12px 圆点手柄），返回的 Slider 行为与旧版一致。</summary>
        public static Slider ProgressBar(Transform parent, string name, float height)
            => UITheme.CreateSlider(parent, name, Vector2.zero, new Vector2(0f, height));

        // ==================== 图标绘制 ====================

        /// <summary>按类型拼出几何图标（返回图标根节点，供调用方重新着色 / 定位）。</summary>
        public static RectTransform BuildIcon(Transform parent, UIIcon icon, Color color, float size)
        {
            var root = new GameObject("Icon").AddComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(size, size);

            switch (icon)
            {
                case UIIcon.ArrowLeft:
                    BuildChevron(root, color, size, true);
                    break;
                case UIIcon.ChevronDown:
                    BuildChevron(root, color, size, false);
                    break;
                case UIIcon.Replay:
                    BuildReplay(root, color, size);
                    break;
                case UIIcon.Play:
                    BuildTriangle(root, color, size);
                    break;
                case UIIcon.Pause:
                    BuildPause(root, color, size);
                    break;
                case UIIcon.Help:
                    BuildGlyph(root, "?", color, Mathf.RoundToInt(size * 0.9f));
                    break;
            }
            return root;
        }

        /// <summary>重新给图标着色（主按钮 / 禁用态切换时用）。</summary>
        public static void SetIconColor(Transform iconRoot, Color color)
        {
            if (iconRoot == null) return;
            var graphics = iconRoot.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++) graphics[i].color = color;
        }

        /// <summary>给 ghost 文字按钮上「主文字 + 居中 + 8px 内边距」的样式（长文案由调用方改左对齐）。</summary>
        private static void ApplyGhostText(Button btn)
        {
            var label = UITheme.GetLabel(btn);
            if (label == null) return;
            label.color = UITheme.TextPrimary;
            label.fontStyle = FontStyle.Normal;
            label.alignment = TextAnchor.MiddleCenter;
            UITheme.StretchAll(label.rectTransform, UITheme.Space8, UITheme.Space8, 0f, 0f);
        }

        private static void BuildChevron(RectTransform root, Color color, float size, bool pointsLeft)
        {
            float thickness = Mathf.Max(1.5f, size * 0.12f);
            float armLength = size * 0.55f;
            // 箭头尖点：← 尖在左，v 尖在下；两条斜杆都以尖点为轴（pivot 在杆首）
            Vector2 apex = pointsLeft ? new Vector2(-size * 0.30f, 0f) : new Vector2(0f, -size * 0.20f);
            CreateArm(root, color, apex, armLength, thickness, 45f);
            CreateArm(root, color, apex, armLength, thickness, pointsLeft ? -45f : 135f);

            if (pointsLeft)
            {
                var shaft = CreateBar(root, color, size * 0.55f, thickness);
                shaft.anchoredPosition = new Vector2(apex.x + size * 0.275f, 0f);
            }
        }

        private static void CreateArm(RectTransform root, Color color, Vector2 origin, float length,
            float thickness, float angle)
        {
            var bar = CreateBar(root, color, length, thickness);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.localRotation = Quaternion.Euler(0f, 0f, angle);
            bar.anchoredPosition = origin;
        }

        private static void BuildReplay(RectTransform root, Color color, float size)
        {
            var ring = new GameObject("Ring").AddComponent<RectTransform>();
            ring.SetParent(root, false);
            ring.anchorMin = new Vector2(0.5f, 0.5f);
            ring.anchorMax = new Vector2(0.5f, 0.5f);
            ring.sizeDelta = new Vector2(size * 0.86f, size * 0.86f);
            var ringImg = ring.gameObject.AddComponent<Image>();
            ringImg.sprite = UITheme.CreateRingSprite(32, 3f);
            ringImg.color = color;
            ringImg.raycastTarget = false;

            var head = new GameObject("Head").AddComponent<RectTransform>();
            head.SetParent(root, false);
            head.anchorMin = new Vector2(0.5f, 0.5f);
            head.anchorMax = new Vector2(0.5f, 0.5f);
            head.sizeDelta = new Vector2(size * 0.3f, size * 0.3f);
            head.anchoredPosition = new Vector2(size * 0.26f, size * 0.3f);
            head.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var headImg = head.gameObject.AddComponent<Image>();
            headImg.sprite = UITheme.TriangleSprite;
            headImg.color = color;
            headImg.raycastTarget = false;
        }

        private static void BuildTriangle(RectTransform root, Color color, float size)
        {
            var tri = new GameObject("Triangle").AddComponent<RectTransform>();
            tri.SetParent(root, false);
            tri.anchorMin = new Vector2(0.5f, 0.5f);
            tri.anchorMax = new Vector2(0.5f, 0.5f);
            tri.sizeDelta = new Vector2(size * 0.72f, size * 0.72f);
            tri.localRotation = Quaternion.Euler(0f, 0f, -90f);
            var img = tri.gameObject.AddComponent<Image>();
            img.sprite = UITheme.TriangleSprite;
            img.color = color;
            img.raycastTarget = false;
        }

        private static void BuildPause(RectTransform root, Color color, float size)
        {
            for (int i = 0; i < 2; i++)
            {
                var bar = CreateBar(root, color, Mathf.Max(2f, size * 0.14f), size * 0.66f);
                bar.anchoredPosition = new Vector2(size * (i == 0 ? -0.16f : 0.16f), 0f);
            }
        }

        private static void BuildGlyph(RectTransform root, string glyph, Color color, int fontSize)
        {
            var text = UITheme.CreateText(root, glyph, Vector2.zero, Vector2.zero, fontSize, color,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            text.gameObject.name = "Glyph";
            UITheme.StretchAll(text.rectTransform, 0f, 0f, 0f, 0f);
        }

        private static RectTransform CreateBar(RectTransform parent, Color color, float width, float height)
        {
            var rt = UITheme.CreateRect(parent, "Bar", Vector2.zero, new Vector2(width, height), color);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }
    }
}
