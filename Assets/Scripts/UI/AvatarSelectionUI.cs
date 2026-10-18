using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 角色选择 / 外观面板（对应方案 3.1 顶栏「当前人物」、审查 2.1 第三层面板、
    /// 审查 4 章「明确当前角色选中态，切换时保持课程状态，提供名称」）。
    /// <para>
    /// 要点：
    /// ① 选中态 = 强调色底 + 描边 + 「✓」标记 + 粗体（不只靠颜色）；
    /// ② 面板标题与每张卡片都显示角色名，顶栏同步显示「人物：xxx」；
    /// ③ 切换只调用 <c>SwitchAvatar</c>，不改动播放时间轴 / 速度 / 循环等课程状态。
    /// </para>
    /// </summary>
    public class AvatarSelectionUI : MonoBehaviour
    {
        /// <summary>角色显示名（与 <c>AvatarManager</c> 的预设顺序一致）。</summary>
        public static readonly string[] AvatarNames = { "写实女性", "写实男性", "卡通角色" };

        /// <summary>当前场景中的角色面板实例（供顶栏 / 工具栏按钮唤起）。</summary>
        public static AvatarSelectionUI Instance { get; private set; }

        /// <summary>按索引取角色显示名（越界返回「未知角色」，不抛异常）。</summary>
        public static string GetAvatarName(int index)
            => index >= 0 && index < AvatarNames.Length ? AvatarNames[index] : "未知角色";

        /// <summary>预设角色数量。</summary>
        public static int AvatarCount => AvatarNames.Length;

        private Core.LessonSessionController _session;
        private readonly List<Button> _cards = new List<Button>();
        private readonly List<Text> _cardLabels = new List<Text>();
        private RectTransform _panel;
        private Text _currentText;

        /// <summary>面板是否处于展开状态。</summary>
        public bool IsPanelOpen => _panel != null && _panel.gameObject.activeSelf;

        public void Initialize(Core.LessonSessionController session)
        {
            _session = session;
            Instance = this;
            if (_session != null) _session.OnAvatarChanged += OnAvatarChanged;
            BuildUI();
            Highlight(_session != null ? _session.CurrentAvatarIndex : 0);
        }

        private void OnDestroy()
        {
            if (_session != null) _session.OnAvatarChanged -= OnAvatarChanged;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // Esc：有浮层时不抢按键（由状态栏 / 主控制层优先处理）
            if (!IsPanelOpen) return;
            if (UITheme.ConsumeEscape()) ShowPanel(false);
        }

        /// <summary>展开 / 收起面板（顶栏「当前人物」与工具栏「角色 / 外观」都调用它）。</summary>
        public void TogglePanel() => ShowPanel(!IsPanelOpen);

        /// <summary>显示 / 隐藏面板。</summary>
        public void ShowPanel(bool show)
        {
            if (_panel == null) return;
            if (show) UITooltip.Hide();
            _panel.gameObject.SetActive(show);
            if (show) Highlight(_session != null ? _session.CurrentAvatarIndex : 0);
        }

        private void OnAvatarChanged(int index) => Highlight(index);

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            // 角色面板位于工具栏之上、顶栏之下（sortingOrder 介于两者之间）
            var canvas = UITheme.CreateCanvas(transform, "AvatarSelectionCanvas", UITheme.Layout.AvatarCanvasOrder);

            _panel = UITheme.CreatePanel(canvas.transform, "AvatarPanel", Vector2.zero,
                new Vector2(UITheme.Layout.AvatarPanelWidth, UITheme.Layout.AvatarPanelHeight),
                UITheme.PanelBg, UITheme.RadiusLarge);
            UITheme.PlaceTopRight(_panel, UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space8,
                UITheme.Layout.AvatarPanelWidth, UITheme.Layout.AvatarPanelHeight);

            var title = UITheme.CreateText(_panel, "角色 / 外观", Vector2.zero, new Vector2(150f, 24f),
                UITheme.FontSmall, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(title.rectTransform, 16f, 12f, 150f, 24f);

            var closeBtn = UITheme.CreateButton(_panel, "收起", Vector2.zero, new Vector2(64f, 24f),
                () => ShowPanel(false), UITheme.FontStatusLabel, ButtonStyle.Ghost, "收起角色面板");
            UITheme.PlaceTopRight(closeBtn.GetComponent<RectTransform>(), 16f, 12f, 64f, 24f);

            // 当前角色名单独一行（不与标题挤在同一条基线上）
            _currentText = UITheme.CreateText(_panel, "当前：—", Vector2.zero, new Vector2(320f, 20f),
                UITheme.FontStatusLabel, UITheme.Accent, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_currentText.rectTransform, 16f, 40f, 320f, 20f);

            var hint = UITheme.CreateText(_panel, "切换角色不会改变播放进度、速度与循环设置",
                Vector2.zero, new Vector2(336f, 20f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(hint.rectTransform, 16f, 62f, 336f, 20f);

            for (int i = 0; i < AvatarNames.Length; i++)
            {
                int index = i;
                float cardW = UITheme.Layout.AvatarCardWidth;
                var card = UITheme.CreateButton(_panel, AvatarNames[i], Vector2.zero, new Vector2(cardW, 50f),
                    () => Select(index), UITheme.FontCaption, ButtonStyle.Secondary,
                    $"切换到「{AvatarNames[i]}」（不影响播放进度）");
                UITheme.Place(card.GetComponent<RectTransform>(), 16f + index * (cardW + 8f), 22f, cardW, 50f);
                _cards.Add(card);
                _cardLabels.Add(UITheme.GetLabel(card));
            }

            UITheme.ChainHorizontal(_cards.ToArray());
            _panel.gameObject.SetActive(false);
        }

        /// <summary>点击角色卡片：只切表现层，保持课程状态（时间轴 / 速度 / 循环都不变）。</summary>
        private void Select(int index)
        {
            if (_session == null) return;
            if (index == _session.CurrentAvatarIndex) return; // 已是当前角色：不重复触发切换
            _session.SwitchAvatar(index);
        }

        private void Highlight(int active)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                bool on = i == active;
                UITheme.ApplyButtonState(_cards[i], on ? ButtonState.Selected : ButtonState.Default,
                    ButtonStyle.Secondary);
                var label = _cardLabels[i];
                if (label != null)
                {
                    // 「✓」标记 + 粗体：颜色之外的第二重选中标识
                    label.text = on ? "✓ " + AvatarNames[i] : AvatarNames[i];
                    label.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                }
            }
            if (_currentText != null)
                _currentText.text = "当前：" + GetAvatarName(active);
        }
    }
}
