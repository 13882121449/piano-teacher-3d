using System.Collections.Generic;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;
using PianoTeacher3D.Customization;
using UnityEngine;
using UnityEngine.EventSystems;
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
    ///    外部角色走 <c>LessonSessionController.ActivateCharacter</c>，两者都不触碰时间轴 / 速度 / 循环 / 音频；
    /// ④ 底部一行显示当前卡片来源与许可，保证「资产可追溯」（方案 18）。
    /// </para>
    /// </summary>
    public class AvatarSelectionUI : MonoBehaviour
    {
        /// <summary>角色显示名（与 <c>AvatarManager</c> 的内置顺序一致；外部角色不参与该表）。</summary>
        public static readonly string[] AvatarNames = { "写实女性", "写实男性", "卡通角色" };

        /// <summary>当前场景中的角色面板实例（供顶栏 / 工具栏按钮唤起）。</summary>
        public static AvatarSelectionUI Instance { get; private set; }

        public static string GetCurrentAvatarName(LessonSessionController session)
        {
            var active = session?.CurrentCharacterView;
            var registry = session?.CharacterRegistry;
            if (active != null && registry != null && registry.TryGet(active.CharacterId, out var profile))
                return profile.DisplayName;

            return GetAvatarName(session?.CurrentAvatarIndex ?? 0);
        }

        /// <summary>按索引取角色显示名（越界返回「未知角色」，不抛异常）。</summary>
        public static string GetAvatarName(int index)
            => index >= 0 && index < AvatarNames.Length ? AvatarNames[index] : "未知角色";

        /// <summary>预设角色数量（内置程序化角色）。</summary>
        public static int AvatarCount => AvatarNames.Length;

        // 面板尺寸：4 张卡片一行（4 × 108 + 3 × 8 = 456），加左右各 16 的内边距
        // 3 张竖版人像卡：132×184（图 132×140 + 名称 18 + 标签 16 + 内边距）
        private const float PanelWidth = 460f;
        private const float PanelHeight = 406f;
        private const float CardWidth = 132f;
        private const float CardHeight = 184f;
        private const float CardGap = 12f;
        private const float CardTop = 92f;
        private const float ThumbHeight = 132f;
        private const int CardsPerPage = 3;

        private Core.LessonSessionController _session;
        private AvatarManager _avatarManager;
        private CharacterRegistry _registry;
        /// <summary>registry 是否由本面板自建（写实化批次 1：共用会话实例时 UI 不负责释放）。</summary>
        private bool _ownsRegistry;

        private readonly List<Button> _cards = new List<Button>();
        private readonly List<Text> _cardLabels = new List<Text>();
        private readonly List<Text> _cardTags = new List<Text>();
        private readonly List<CharacterProfile> _profiles = new List<CharacterProfile>();

        private RectTransform _panel;
        private Text _currentText;
        private Text _licenseText;
        private Button _photoButton;
        private PhotoCustomizationPanel _photoPanel;
        private PhotoAvatarPanel _createPhotoPanel;
        private Button _pageButton;
        private Button _archivePhotoButton;
        private string _confirmArchiveId;
        private int _page;
        private Button _closeButton;
        private GameObject _panelTrigger;

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

            // 角色注册表共用会话实例（写实化批次 1）：消除双份创建与双份 Resources 扫描，
            // 卡片数据与会话偏好恢复看到的是同一份档案。会话不存在（独立预览 / 旧测试）时保底自建，
            // 自建的由本面板负责释放（见 OnDestroy 的 _ownsRegistry 判断）。
            _registry = _session != null && _session.CharacterRegistry != null
                ? _session.CharacterRegistry
                : CreateOwnedRegistry();

            BuildUI();
            Highlight(CurrentCharacterId());
        }

        /// <summary>仅在无会话时自建注册表（保底路径）；自建实例由本面板在 OnDestroy 释放。</summary>
        private CharacterRegistry CreateOwnedRegistry()
        {
            _ownsRegistry = true;
            return new CharacterRegistry();
        }

        private void OnDestroy()
        {
            if (_session != null) _session.OnAvatarChanged -= OnAvatarChanged;
            if (_avatarManager != null) _avatarManager.OnCharacterViewChanged -= OnViewChanged;

            if (_ownsRegistry) _registry?.Dispose();
            _registry = null;
            _ownsRegistry = false;

            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // Esc：有浮层时不抢按键（由状态栏 / 主控制层优先处理）
            if (!IsPanelOpen) return;
            if (_createPhotoPanel != null && _createPhotoPanel.IsOpen) return;
            if (UITheme.ConsumeEscape()) ShowPanel(false);
        }

        /// <summary>展开 / 收起面板（顶栏「当前人物」与工具栏「角色 / 外观」都调用它）。</summary>
        public void TogglePanel() => ShowPanel(!IsPanelOpen);

        /// <summary>显示 / 隐藏面板。</summary>
        public void ShowPanel(bool show)
        {
            if (_panel == null || show == IsPanelOpen) return;
            var events = EventSystem.current;
            if (show)
            {
                UITooltip.Hide();
                _panelTrigger = events != null ? events.currentSelectedGameObject : null;
                GoToCurrentPage();
            }

            _panel.gameObject.SetActive(show);
            if (show) Highlight(CurrentCharacterId());
            if (events != null)
                events.SetSelectedGameObject(show ? _closeButton.gameObject : _panelTrigger);
            if (!show) _panelTrigger = null;
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

            _closeButton = UIKit.TextButton(_panel, "收起", 64f, UITheme.HeightSmall,
                () => ShowPanel(false), UITheme.FontBodySmall, "收起角色面板");
            UITheme.PlaceTopRight(_closeButton.GetComponent<RectTransform>(), UITheme.Space16, UITheme.Space8,
                64f, UITheme.HeightSmall);

            // 当前角色名单独一行（不与标题挤在同一条基线上）
            _currentText = UITheme.CreateText(_panel, "当前：—", Vector2.zero, new Vector2(320f, 20f),
                UITheme.FontStatusLabel, UITheme.Accent, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_currentText.rectTransform, 16f, 40f, 320f, 20f);

            var hint = UITheme.CreateText(_panel, "切换角色不会改变播放进度、速度与循环设置",
                Vector2.zero, new Vector2(440f, 20f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(hint.rectTransform, 16f, 62f, 440f, 20f);

            BuildCards();

            // 照片外观入口（方案 12.2）：挂在角色库内，避免为低频操作占用常驻控件。
            // 未装配外观管线时按钮仍显示但禁用——用户能看到能力存在，点下去不会报错。
            bool hasCustomization = _session != null && _session.Customization != null
                                    && _session.PrivacyConsent != null;
            _photoButton = UITheme.CreateButton(_panel, "用照片定制外观…", Vector2.zero,
                new Vector2(PanelWidth - 32f, 32f), OpenPhotoCustomizationPanel,
                UITheme.FontSmall, ButtonStyle.Secondary,
                hasCustomization
                    ? "上传一张照片，生成当前角色的个性化外观（仅本机处理，不上传）"
                    : "照片外观管线未启用（本地存储不可用）");
            UITheme.PlaceTopLeft(_photoButton.GetComponent<RectTransform>(),
                16f, CardTop + CardHeight + 8f, PanelWidth - 32f, 32f);
            _photoButton.interactable = hasCustomization;

            var addPhoto = UITheme.CreateButton(_panel, "从照片创建…", Vector2.zero,
                new Vector2(220f, 32f), OpenPhotoAvatarPanel, UITheme.FontSmall, ButtonStyle.Primary,
                "在本地用正脸照片创建一位新的演奏角色");
            UITheme.PlaceTopLeft(addPhoto.GetComponent<RectTransform>(), 16f, CardTop + CardHeight + 46f, 220f, 32f);
            addPhoto.interactable = _session != null;

            _archivePhotoButton = UITheme.CreateButton(_panel, "移出角色库", Vector2.zero,
                new Vector2(112f, 32f), ArchiveCurrentPhoto, UITheme.FontCaption, ButtonStyle.Ghost,
                "将当前照片角色移到本地回收目录");
            UITheme.PlaceTopRight(_archivePhotoButton.GetComponent<RectTransform>(), 16f,
                CardTop + CardHeight + 46f, 112f, 32f);
            _archivePhotoButton.gameObject.SetActive(false);

            _pageButton = UITheme.CreateButton(_panel, "下一页", Vector2.zero,
                new Vector2(88f, 26f), NextPage, UITheme.FontCaption, ButtonStyle.Ghost);
            UITheme.PlaceTopRight(_pageButton.GetComponent<RectTransform>(), 16f, 57f, 88f, 26f);
            RefreshPageButton();

            // 许可 / 失败原因：允许换行两行，避免长原因（例如资产缺贴图）溢出面板造成视觉错位
            _licenseText = UITheme.CreateText(_panel, string.Empty, Vector2.zero,
                new Vector2(PanelWidth - 32f, 36f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                TextAnchor.UpperLeft);
            _licenseText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _licenseText.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(_licenseText.rectTransform, 16f, PanelHeight - 42f, PanelWidth - 32f, 36f);

            _panel.gameObject.SetActive(false);

            if (_avatarManager != null) _avatarManager.OnCharacterViewChanged += OnViewChanged;
        }

        private void BuildCards()
        {
            foreach (var old in _cards)
            {
                if (old == null) continue;
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            _cards.Clear();
            _cardLabels.Clear();
            _cardTags.Clear();
            _profiles.Clear();

            var all = _registry != null ? _registry.All : null;
            if (all == null) return;

            int start = _page * CardsPerPage;
            for (int i = start; i < Mathf.Min(start + CardsPerPage, all.Count); i++)
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

                var thumb = UIKit.Panel(card.transform, "Thumb", UITheme.PanelBgDeep, UITheme.RadiusSmall);
                UITheme.Place(thumb, UITheme.Space4, CardHeight - ThumbHeight - UITheme.Space8,
                    CardWidth - UITheme.Space8, ThumbHeight);
                UIKit.PlaceCharacterPreview(thumb, profile.CharacterId, profile.Thumbnail);

                // 名称放在缩略图下方（按钮自带的 label 直接复用，避免多一层文本）
                var label = UITheme.GetLabel(card);
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleCenter;
                    label.fontSize = UITheme.FontBodySmall;
                    UITheme.Place(label.rectTransform, 4f, 26f, CardWidth - 8f, 18f);
                }
                _cardLabels.Add(label);   // 保持与 _cards 索引一一对应（可能为 null）

                // 风格 · 质量 · 照片外观支持
                var tag = UITheme.CreateText(card.transform, BuildTag(profile), Vector2.zero,
                    new Vector2(CardWidth - 8f, 16f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                    TextAnchor.MiddleCenter);
                UITheme.Place(tag.rectTransform, 4f, 8f, CardWidth - 8f, 16f);
                _cardTags.Add(tag);
            }
            RefreshPageButton();

        }

        private int PageCount => Mathf.Max(1, ((_registry?.Count ?? 0) + CardsPerPage - 1) / CardsPerPage);

        private void RefreshPageButton()
        {
            if (_pageButton == null) return;
            _pageButton.gameObject.SetActive(PageCount > 1);
            var label = UITheme.GetLabel(_pageButton);
            if (label != null) label.text = $"下一页 {_page + 1}/{PageCount}";
        }

        private void NextPage()
        {
            _page = (_page + 1) % PageCount;
            BuildCards();
            Highlight(CurrentCharacterId());
        }

        private void GoToCurrentPage()
        {
            if (_registry == null) return;
            string id = CurrentCharacterId();
            for (int i = 0; i < _registry.Count; i++)
            {
                if (_registry.All[i].CharacterId != id || _page == i / CardsPerPage) continue;
                _page = i / CardsPerPage;
                BuildCards();
                break;
            }
        }

        private void OpenPhotoAvatarPanel()
        {
            if (_session == null) return;
            if (_createPhotoPanel == null)
            {
                var go = new GameObject("PhotoAvatarCreator");
                go.transform.SetParent(transform, false);
                _createPhotoPanel = go.AddComponent<PhotoAvatarPanel>();
                _createPhotoPanel.Initialize(SavePhotoAvatar);
            }
            _createPhotoPanel.Show();
        }

        private string SavePhotoAvatar(PhotoAvatarData data)
        {
            if (_registry == null || _session == null) return "角色库还未准备好。";
            if (!_registry.SavePhotoAvatar(data, out var profile, out string error)) return error;
            var result = _session.ActivateCharacter(profile);
            if (!result.Succeeded)
            {
                if (!_registry.ArchivePhotoAvatar(profile, out string archiveError))
                    return result.Error + " " + archiveError;
                return result.Error;
            }
            GoToCurrentPage();
            BuildCards();
            Highlight(CurrentCharacterId());
            return null;
        }

        private void ArchiveCurrentPhoto()
        {
            if (_session == null || !_registry.TryGet(CurrentCharacterId(), out var profile) ||
                profile.PhotoAvatar == null) return;
            if (_confirmArchiveId != profile.CharacterId)
            {
                _confirmArchiveId = profile.CharacterId;
                UITheme.GetLabel(_archivePhotoButton).text = "确认移出";
                return;
            }

            if (!_registry.TryGet(CharacterRegistry.RocketboxFemaleId, out var fallback)) return;
            var result = _session.ActivateCharacter(fallback);
            if (!result.Succeeded || !_registry.ArchivePhotoAvatar(profile, out _)) return;
            _page = 0;
            BuildCards();
            Highlight(CurrentCharacterId());
        }

        /// <summary>面板里只放前 <paramref name="max"/> 个字符，完整原因进日志，避免文本溢出。</summary>
        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
            Debug.LogWarning("[AvatarSelectionUI] " + text);
            return text.Substring(0, max) + "…";
        }

        /// <summary>
        /// 打开照片定制面板（方案 9.2 的第一步：角色库 →「用照片定制外观」）。
        /// 面板只在首次打开时创建，之后复用同一实例（预览与任务状态都不丢）。
        /// </summary>
        private void OpenPhotoCustomizationPanel()
        {
            var service = _session != null ? _session.Customization : null;
            var consent = _session != null ? _session.PrivacyConsent : null;
            if (service == null || consent == null)
            {
                _photoButton.interactable = false;
                return;
            }

            AppearanceSlot slot = ResolvePhotoSlot();

            if (_photoPanel == null)
            {
                var go = new GameObject("PhotoCustomizationPanel");
                go.transform.SetParent(transform, false);
                _photoPanel = go.AddComponent<PhotoCustomizationPanel>();
                _photoPanel.Initialize(service, consent, slot);
            }
            else
            {
                // 角色可能在两次打开之间被切换：槽位能力变了就跟着换
                _photoPanel.SetSlot(slot, out _);
            }

            _photoPanel.ShowPanel(true);
        }

        /// <summary>
        /// 按当前角色的**运行时能力**选外观槽位（方案 9.5：不兼容的槽位不给应用）。
        /// 缩略图最安全（任何角色都支持），其次整体色调，最后才考虑贴图类槽位。
        /// </summary>
        private AppearanceSlot ResolvePhotoSlot()
        {
            var view = _session != null ? _session.CurrentCharacterView : null;
            if (view == null) return AppearanceSlot.Thumbnail;

            if (view.Supports(AppearanceSlot.Thumbnail)) return AppearanceSlot.Thumbnail;
            if (view.Supports(AppearanceSlot.BodyTint)) return AppearanceSlot.BodyTint;
            if (view.Supports(AppearanceSlot.FaceTexture)) return AppearanceSlot.FaceTexture;
            if (view.Supports(AppearanceSlot.HeadMaterial)) return AppearanceSlot.HeadMaterial;
            return AppearanceSlot.Thumbnail;
        }

        private static string BuildTag(CharacterProfile profile)
        {
            if (profile.PhotoAvatar != null) return "照片拟合 · 本地";
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

            // 外部角色：走切换事务（含校验与回滚），失败时旧角色保持可驱动。
            // 失败原因必须显示出来：资产不全（例如缺贴图）时如果只是静默不切换，
            // 用户只会觉得「点了没反应」，反而更困惑。
            if (_session == null) return;
            var result = _session.ActivateCharacter(profile);

            if (result.Succeeded)
            {
                Highlight(CurrentCharacterId());
                return;
            }

            Highlight(CurrentCharacterId());   // 高亮保持在实际生效的角色上
            if (_licenseText != null)
            {
                _licenseText.text = "切换失败：" + Truncate(result.Error, 72);
                _licenseText.color = UITheme.TextError;
            }
        }

        private void Highlight(string activeCharacterId)
        {
            if (_archivePhotoButton != null)
            {
                bool photo = _registry != null && _registry.TryGet(activeCharacterId, out var current) &&
                    current.PhotoAvatar != null;
                _archivePhotoButton.gameObject.SetActive(photo);
                if (activeCharacterId != _confirmArchiveId)
                {
                    _confirmArchiveId = null;
                    UITheme.GetLabel(_archivePhotoButton).text = "移出角色库";
                }
            }
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

                // 选中卡的底是强调色，标签必须换成深色，否则浅灰文字在金色上几乎看不见。
                if (_cardTags.Count > i && _cardTags[i] != null)
                    _cardTags[i].color = on ? UITheme.TextOnAccent : UITheme.TextSecondary;

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
                _currentText.text = "当前：" + GetCurrentAvatarName(_session);
            }
        }

    }
}
