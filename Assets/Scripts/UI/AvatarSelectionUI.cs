using System.Collections.Generic;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Characters;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 角色库面板（对应方案 3.1 顶栏「当前人物」、7.1 角色库、9.5 外观槽位、审查 2.1 第三层面板）。
    /// <para>
    /// 要点：
    /// ① 卡片内容全部来自 <see cref="CharacterRegistry"/>，不再硬编码角色名：
    ///    每张卡片有<b>缩略图 / 名称 / 风格 · 质量标签 / 是否支持照片外观 / 许可</b>；
    /// ② 选中态 = 强调色底 + 边框 + 「✓」+ 粗体（不只靠颜色区分，审查 2.2）；
    /// ③ 切换只换表现层：内置角色走 <c>LessonSessionController.SwitchAvatar</c>，
    ///    外部角色走 <c>AvatarManager.TryActivate</c>，两者都不触碰时间轴 / 速度 / 循环 / 音频；
    /// ④ 底部一行显示当前卡片来源与许可，保证「资产可追溯」（方案 18）。
    /// </para>
    /// </summary>
    public class AvatarSelectionUI : MonoBehaviour
    {
        /// <summary>角色显示名（与 <c>AvatarManager</c> 的内置顺序一致；外部角色不参与该表）。</summary>
        public static readonly string[] AvatarNames = { "写实女性", "写实男性", "卡通角色" };

        /// <summary>当前场景中的角色面板实例（供顶栏 / 工具栏按钮唤起）。</summary>
        public static AvatarSelectionUI Instance { get; private set; }

        /// <summary>按索引取角色显示名（越界返回「未知角色」，不抛异常）。</summary>
        public static string GetAvatarName(int index)
            => index >= 0 && index < AvatarNames.Length ? AvatarNames[index] : "未知角色";

        /// <summary>预设角色数量（内置程序化角色）。</summary>
        public static int AvatarCount => AvatarNames.Length;

        // 面板尺寸：4 张卡片一行（4 × 108 + 3 × 8 = 456），加左右各 16 的内边距
        private const float PanelWidth = 488f;
        private const float PanelHeight = 300f;
        private const float CardWidth = 108f;
        private const float CardHeight = 132f;
        private const float CardGap = 8f;
        private const float CardTop = 96f;

        private Core.LessonSessionController _session;
        private AvatarManager _avatarManager;
        private CharacterRegistry _registry;

        private readonly List<Button> _cards = new List<Button>();
        private readonly List<Text> _cardLabels = new List<Text>();
        private readonly List<Text> _cardTags = new List<Text>();
        private readonly List<Image> _cardThumbs = new List<Image>();
        private readonly List<CharacterProfile> _profiles = new List<CharacterProfile>();
        private readonly List<Sprite> _sprites = new List<Sprite>();

        private RectTransform _panel;
        private Text _currentText;
        private Text _licenseText;
        private Sprite _whiteSprite;
        private Texture2D _whiteTexture;

        /// <summary>面板是否处于展开状态。</summary>
        public bool IsPanelOpen => _panel != null && _panel.gameObject.activeSelf;

        /// <summary>当前面板使用的角色库（供外部读取/测试）。</summary>
        public CharacterRegistry Registry => _registry;

        public void Initialize(Core.LessonSessionController session)
        {
            _session = session;
            Instance = this;
            if (_session != null) _session.OnAvatarChanged += OnAvatarChanged;

            // 一次性查找：AvatarManager 由 LessonSessionController 在子节点上创建，
            // 会话控制器没有公开访问器，这里只在初始化时找一次（不在 Update 里查找）。
            _avatarManager = _session != null ? _session.GetComponentInChildren<AvatarManager>(true) : null;

            if (_registry == null) _registry = new CharacterRegistry();

            BuildUI();
            Highlight(CurrentCharacterId());
        }

        private void OnDestroy()
        {
            if (_session != null) _session.OnAvatarChanged -= OnAvatarChanged;
            if (_avatarManager != null) _avatarManager.OnCharacterViewChanged -= OnViewChanged;

            for (int i = 0; i < _sprites.Count; i++)
            {
                if (_sprites[i] != null) Destroy(_sprites[i]);
            }
            _sprites.Clear();
            if (_whiteTexture != null)
            {
                Destroy(_whiteTexture);
                _whiteTexture = null;
            }
            _whiteSprite = null;

            _registry?.Dispose();
            _registry = null;

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
            if (show) Highlight(CurrentCharacterId());
        }

        private void OnAvatarChanged(int index) => Highlight(CurrentCharacterId());

        private void OnViewChanged(ICharacterView view) => Highlight(CurrentCharacterId());

        /// <summary>当前角色 ID：优先取 AvatarManager 的视图 ID（含外部角色），否则用内置索引映射。</summary>
        private string CurrentCharacterId()
        {
            if (_avatarManager != null && _avatarManager.CurrentView != null)
                return _avatarManager.CurrentView.CharacterId;

            int index = _session != null ? _session.CurrentAvatarIndex : 0;
            return index >= 0 && index < CharacterRegistry.BuiltInIds.Length
                ? CharacterRegistry.BuiltInIds[index]
                : string.Empty;
        }

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            // 角色面板位于工具栏之上、顶栏之下（sortingOrder 介于两者之间）
            var canvas = UITheme.CreateCanvas(transform, "AvatarSelectionCanvas", UITheme.Layout.AvatarCanvasOrder);

            _panel = UITheme.CreatePanel(canvas.transform, "AvatarPanel", Vector2.zero,
                new Vector2(PanelWidth, PanelHeight), UITheme.PanelBg, UITheme.RadiusLarge);
            UITheme.PlaceTopRight(_panel, UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space8,
                PanelWidth, PanelHeight);

            var title = UITheme.CreateText(_panel, "角色库", Vector2.zero, new Vector2(150f, 24f),
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
                Vector2.zero, new Vector2(440f, 20f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(hint.rectTransform, 16f, 62f, 440f, 20f);

            BuildCards();

            _licenseText = UITheme.CreateText(_panel, string.Empty, Vector2.zero,
                new Vector2(PanelWidth - 32f, 20f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_licenseText.rectTransform, 16f, PanelHeight - 30f, PanelWidth - 32f, 20f);

            _panel.gameObject.SetActive(false);

            if (_avatarManager != null) _avatarManager.OnCharacterViewChanged += OnViewChanged;
        }

        private void BuildCards()
        {
            _cards.Clear();
            _cardLabels.Clear();
            _cardTags.Clear();
            _cardThumbs.Clear();
            _profiles.Clear();

            var all = _registry != null ? _registry.All : null;
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                var profile = all[i];
                if (profile == null) continue;

                int slot = _profiles.Count;
                float x = 16f + slot * (CardWidth + CardGap);

                var card = UITheme.CreateButton(_panel, profile.DisplayName, Vector2.zero,
                    new Vector2(CardWidth, CardHeight), () => Select(slot),
                    UITheme.FontCaption, ButtonStyle.Secondary,
                    $"切换到「{profile.DisplayName}」（不影响播放进度）");
                UITheme.Place(card.GetComponent<RectTransform>(), x, CardTop, CardWidth, CardHeight);
                _cards.Add(card);
                _profiles.Add(profile);

                // 缩略图（没有纹理时用风格色块占位，避免空白卡片）
                var thumb = CreateThumbnail(card.transform, profile);
                _cardThumbs.Add(thumb);

                // 名称放在缩略图下方（按钮自带的 label 直接复用，避免多一层文本）
                var label = UITheme.GetLabel(card);
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleCenter;
                    label.fontSize = UITheme.FontCaption;
                    UITheme.Place(label.rectTransform, 4f, CardHeight - 40f, CardWidth - 8f, 18f);
                }
                _cardLabels.Add(label);   // 保持与 _cards 索引一一对应（可能为 null）

                // 风格 · 质量 · 照片外观支持
                var tag = UITheme.CreateText(card.transform, BuildTag(profile), Vector2.zero,
                    new Vector2(CardWidth - 8f, 16f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                    TextAnchor.MiddleCenter);
                UITheme.Place(tag.rectTransform, 4f, CardHeight - 58f, CardWidth - 8f, 16f);
                _cardTags.Add(tag);
            }

            if (_cards.Count > 0) UITheme.ChainHorizontal(_cards.ToArray());
        }

        private Image CreateThumbnail(Transform parent, CharacterProfile profile)
        {
            var go = new GameObject("Thumb");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            UITheme.Place(rt, 6f, CardHeight - 34f, CardWidth - 12f, 28f);

            var image = go.AddComponent<Image>();
            image.preserveAspect = false;
            image.raycastTarget = false;

            if (profile.Thumbnail != null)
            {
                var sprite = Sprite.Create(profile.Thumbnail,
                    new Rect(0f, 0f, profile.Thumbnail.width, profile.Thumbnail.height),
                    new Vector2(0.5f, 0.5f), 100f);
                _sprites.Add(sprite);
                image.sprite = sprite;
                image.color = Color.white;
            }
            else
            {
                image.sprite = EnsureWhiteSprite();
                image.color = PlaceholderColor(profile.Style);
            }
            return image;
        }

        /// <summary>缩略图占位色：按风格区分，让「有没有真实预览图」一眼可辨。</summary>
        private static Color PlaceholderColor(CharacterStyle style)
        {
            switch (style)
            {
                case CharacterStyle.Cartoon: return new Color(0.24f, 0.62f, 0.45f, 0.55f);
                case CharacterStyle.Anime: return new Color(0.55f, 0.38f, 0.78f, 0.55f);
                case CharacterStyle.Custom: return new Color(0.85f, 0.55f, 0.25f, 0.55f);
                default: return new Color(0.38f, 0.48f, 0.62f, 0.55f);
            }
        }

        private static string BuildTag(CharacterProfile profile)
        {
            string style = profile.Style switch
            {
                CharacterStyle.Realistic => "写实",
                CharacterStyle.Cartoon => "卡通",
                CharacterStyle.Anime => "动漫",
                _ => "自定义"
            };
            string tier = profile.QualityTier switch
            {
                0 => "低",
                2 => "高",
                _ => "标准"
            };
            bool photo = profile.SupportsSlot(AppearanceSlot.Thumbnail);
            return $"{style} · {tier} · 照片{(photo ? "✓" : "✗")}";
        }

        /// <summary>点击角色卡片：只切表现层，保持课程状态（时间轴 / 速度 / 循环都不变）。</summary>
        private void Select(int slot)
        {
            if (slot < 0 || slot >= _profiles.Count) return;
            var profile = _profiles[slot];
            if (profile == null) return;

            if (profile.CharacterId == CurrentCharacterId()) return;   // 已是当前角色：不重复触发切换

            if (profile.Source == CharacterSource.ProceduralBuiltIn)
            {
                if (_session == null) return;
                _session.SwitchAvatar(profile.ProceduralIndex);
                return;
            }

            // 外部角色：走切换事务（含校验与回滚），失败时旧角色保持可驱动
            if (_avatarManager == null) return;
            _avatarManager.TryActivate(profile);
            Highlight(CurrentCharacterId());
        }

        private void Highlight(string activeCharacterId)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var profile = _profiles[i];
                bool on = profile != null && profile.CharacterId == activeCharacterId;

                UITheme.ApplyButtonState(_cards[i], on ? ButtonState.Selected : ButtonState.Default,
                    ButtonStyle.Secondary);

                var label = _cardLabels[i];
                if (label != null)
                {
                    // 「✓」标记 + 粗体：颜色之外的第二重选中标识
                    label.text = on ? "✓ " + profile.DisplayName : profile.DisplayName;
                    label.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                }

                if (_cardThumbs[i] != null && profile != null && profile.Thumbnail == null)
                    _cardThumbs[i].color = PlaceholderColor(profile.Style) * (on ? 1f : 0.7f);

                if (_licenseText != null)
                {
                    if (!on) continue;
                    string license = string.IsNullOrEmpty(profile.LicenseName) ? "未声明" : profile.LicenseName;
                    string source = profile.Source == CharacterSource.ProceduralBuiltIn ? "程序化生成" : "导入资产";
                    _licenseText.text = $"许可：{license} · 来源：{source}" +
                                        (string.IsNullOrEmpty(profile.AssetVersion) ? string.Empty : $" · {profile.AssetVersion}");
                }
            }

            if (_currentText != null)
            {
                string name = GetAvatarName(_session != null ? _session.CurrentAvatarIndex : 0);
                for (int i = 0; i < _profiles.Count; i++)
                {
                    if (_profiles[i] != null && _profiles[i].CharacterId == activeCharacterId)
                    {
                        name = _profiles[i].DisplayName;
                        break;
                    }
                }
                _currentText.text = "当前：" + name;
            }
        }

        /// <summary>1×1 纯白 sprite（占位色块用）。由本组件持有并在销毁时释放。</summary>
        private Sprite EnsureWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                name = "AvatarSelectionUI_White",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            _whiteTexture = tex;

            _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            _sprites.Add(_whiteSprite);
            return _whiteSprite;
        }
    }
}
