using System;
using System.Collections.Generic;
using System.IO;
using PianoTeacher3D.Characters;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 主菜单 / 曲库页（深色专业工作台风格）。
    /// <para>
    /// 结构：28 hero 标题 + 12 副标题 → 曲目卡片网格（240×96，悬停升起 2px）→ 最近播放
    /// → 底部快捷键说明。空态与错误态都用**居中卡片**（图标 + 说明 + 行动按钮）表达，
    /// 不退化成一行红色报错文字。
    /// </para>
    /// <para>
    /// 选中曲目通过静态字段 <see cref="SelectedSongPath"/> 传给课程场景（原有跨场景通道，保持不变）。
    /// </para>
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        /// <summary>由主菜单选中的曲目路径（课程场景读取，保持原有契约）。</summary>
        public static string SelectedSongPath = "";

        private const int MaxLibraryCards = 8;
        private const int MaxRecentRows = 2;
        private const float RecentRowHeight = 48f;
        private const float RecentRowGap = 8f;
        private const float RemoveConfirmSeconds = 4f;

        // ---- 卡片式四入口（方案 7.1：继续练习 / 选曲 / 角色 / 设置）----
        private const float HeroHeight = 88f;
        private const float EntryCardWidth = 216f;
        private const float EntryCardHeight = 96f;
        private const float EntryGap = 16f;
        /// <summary>入口行占用的纵向高度（含与下一个区块的间距）；0 表示窄屏没有这一行。</summary>
        private const float EntriesBlock = EntryCardHeight + UITheme.Space24;

        // ---- 纵向游标（参考分辨率下的顶部偏移，全部为 4 的倍数）----
        private const float ContentTop = 108f;
        private const float ContentBottom = 56f;
        private const float SectionHeaderHeight = 40f;
        private const float GridTopGap = 48f;

        private static bool _pendingImport;
        private static readonly List<string> RecentPaths = new List<string>();

        /// <summary>本次运行中从主菜单进入过的曲目（只保留安全元数据：路径）。</summary>
        public static IReadOnlyList<string> RecentSongs => RecentPaths;

        /// <summary>记录一次「从主菜单打开曲目」（跨场景静态通道，只存路径）。</summary>
        public static void NotifySongOpened(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            RecentPaths.Remove(path);
            RecentPaths.Insert(0, path);
            while (RecentPaths.Count > 8) RecentPaths.RemoveAt(RecentPaths.Count - 1);
        }

        /// <summary>课程场景启动时取走「请求导入 MIDI」标记（取走即清空，避免重复弹窗）。</summary>
        public static bool ConsumePendingImport()
        {
            if (!_pendingImport) return false;
            _pendingImport = false;
            return true;
        }

        private Canvas _canvas;
        private RectTransform _content;
        private Button _pendingRemoveBtn;
        private float _removeConfirmUntil;
        private bool _showRecent;
        /// <summary>入口行实际占用的高度（0 = 本分辨率下不显示入口行）。</summary>
        private float _entriesOffset;
        /// <summary>上一次排版的可用宽度（0 = 还没拿到 CanvasScaler 定下来的真实宽度）。</summary>
        private float _layoutWidth;
        /// <summary>当前打开的浮层（选曲 / 角色 / 设置），一次只允许一个。</summary>
        private GameObject _overlay;

        private void Start()
        {
            ApplySavedSettings();
            BuildUI();
        }

        private void Update()
        {
            // 「移除最近曲目」的二次确认超时复位（破坏性操作需要确认）
            if (_pendingRemoveBtn != null && Time.unscaledTime > _removeConfirmUntil)
            {
                UITheme.SetButtonLabel(_pendingRemoveBtn, "移除");
                UITheme.ApplyButtonState(_pendingRemoveBtn, ButtonState.Default, ButtonStyle.Danger);
                _pendingRemoveBtn = null;
            }

            if (_overlay != null && Input.GetKeyDown(KeyCode.Escape)) CloseOverlay();

            // 画布尺寸在 Start 之后才由 CanvasScaler 定下来（Start 时读到的还是窗口像素），
            // 所以这里等真实宽度稳定后再按实际可用宽度决定入口行显示 / 隐藏，窗口缩放时同步重排。
            if (_content != null)
            {
                float width = Width(_content);
                if (Mathf.Abs(width - _layoutWidth) > 1f)
                {
                    _layoutWidth = width;
                    RebuildContent();
                }
            }
        }

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            _canvas = UITheme.CreateCanvas(transform, "MainMenuCanvas");

            // 主菜单没有 3D 场景，铺一层暖木底色，避免露出场景相机的冷色天空盒
            var backdrop = UITheme.CreateRect(_canvas.transform, "Backdrop", Vector2.zero, Vector2.zero,
                UITheme.SceneBg);
            UITheme.StretchAll(backdrop, 0f, 0f, 0f, 0f);
            backdrop.SetAsFirstSibling();

            BuildHeroBand();

            _content = UITheme.CreateRect(_canvas.transform, "Content", Vector2.zero, Vector2.zero,
                UITheme.Transparent);
            UITheme.StretchAll(_content, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                ContentBottom, ContentTop);

            BuildEntryCards();
            BuildLibrarySection();
            BuildRecentSection();
            BuildFooter();
        }

        // ---------- 顶部 hero 带：标题 + 副标题 + 右侧曲库计数 ----------

        private void BuildHeroBand()
        {
            var band = UITheme.CreatePanel(_canvas.transform, "HeroBand", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, HeroHeight), UITheme.PanelBgDeep, 0);
            var bandBg = band.GetComponent<Image>();
            if (bandBg != null) bandBg.raycastTarget = false;
            UITheme.StretchTop(band, 0f, 0f, 0f, HeroHeight);

            var edge = UITheme.CreateDivider(band, "HeroRule", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, 1f));
            UITheme.StretchX(edge, 0f, 0f, HeroHeight - 1f, 1f);

            var title = UIKit.Label(band, "钢琴 3D 教学", UITheme.FontPageTitle, UITheme.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, "PageTitle");
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.PlaceTopLeft(title.rectTransform, UITheme.Layout.ScreenMargin, 16f, 560f, 34f);

            var subtitle = UIKit.Label(band, "三个真人角色 · 双手分离练习 · 逐帧对齐的键盘高亮",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal,
                "PageSubtitle");
            subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.PlaceTopLeft(subtitle.rectTransform, UITheme.Layout.ScreenMargin, 52f, 720f, 18f);

            int songs = SongsCount();
            string metaText = RecentPaths.Count > 0
                ? $"曲库 {songs} 首　·　最近 {RecentPaths.Count} 首"
                : $"曲库 {songs} 首";
            var meta = UIKit.Label(band, metaText, UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.MiddleRight, FontStyle.Normal, "HeroMeta");
            meta.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.PlaceTopRight(meta.rectTransform, UITheme.Layout.ScreenMargin, 20f, 420f, 18f);
        }

        // ---------- 卡片式四入口 ----------

        private void BuildEntryCards()
        {
            // 窄屏（<1080）放不下四张卡，退化成「曲库直接铺开」的老结构
            float avail = Width(_content);
            float needed = 4f * EntryCardWidth + 3f * EntryGap;
            _entriesOffset = avail >= needed ? EntriesBlock : 0f;
            if (_entriesOffset <= 0f) return;

            string recentPath = RecentPaths.Count > 0 ? RecentPaths[0] : "";
            bool hasRecent = !string.IsNullOrEmpty(recentPath) && SafeFileExists(recentPath);
            string recentName = hasRecent ? Path.GetFileNameWithoutExtension(recentPath) : "";

            var resume = UIKit.Card(_content, "继续练习",
                hasRecent ? $"《{recentName}》" : "还没有练习记录，先从曲库选一首",
                new Vector2(EntryCardWidth, EntryCardHeight),
                hasRecent
                    ? (UnityEngine.Events.UnityAction)(() => LoadLesson(recentPath))
                    : (UnityEngine.Events.UnityAction)null,
                hasRecent ? $"回到《{recentName}》继续练习" : "本次运行还没有打开过曲目",
                "EntryCard_");
            resume.interactable = hasRecent;
            PlaceEntry(resume.GetComponent<RectTransform>(), 0);

            var pick = UIKit.Card(_content, "选曲",
                $"曲库 {SongsCount()} 首 · 导入 MIDI",
                new Vector2(EntryCardWidth, EntryCardHeight), OpenSongPicker,
                "打开曲目清单：选一首开始，或导入本机 .mid 文件",
                "EntryCard_");
            PlaceEntry(pick.GetComponent<RectTransform>(), 1);

            var cast = UIKit.Card(_content, "角色",
                "写实女性 · 写实男性 · 儿童",
                new Vector2(EntryCardWidth, EntryCardHeight), OpenCharacterPreview,
                "三个内置真人角色的人像预览（进入课程后可切换）",
                "EntryCard_");
            PlaceEntry(cast.GetComponent<RectTransform>(), 2);

            var settings = UIKit.Card(_content, "设置",
                "画质 · 全屏 · 快捷键",
                new Vector2(EntryCardWidth, EntryCardHeight), OpenSettings,
                "画质档位、全屏开关与快捷键说明",
                "EntryCard_");
            PlaceEntry(settings.GetComponent<RectTransform>(), 3);
        }

        private void PlaceEntry(RectTransform rt, int index)
        {
            if (rt == null) return;
            UITheme.PlaceTopLeft(rt, index * (EntryCardWidth + EntryGap), 0f, EntryCardWidth, EntryCardHeight);
            var img = rt.GetComponent<Image>();
            if (img != null) UITheme.SetOutline(img, true, UITheme.Divider, 1f);
        }

        // ---------- 浮层：选曲 / 角色 / 设置（一次只开一个，Esc 或点遮罩关闭）----------

        private const string PrefQuality = "piano.quality";
        private const string PrefFullscreen = "piano.fullscreen";

        /// <summary>全屏遮罩（点击关闭）+ 居中面板 + 标题 + 右上关闭；返回可承载内容的面板。</summary>
        private RectTransform CreateOverlay(string name, float width, float height, string title, out float bodyTop)
        {
            CloseOverlay();

            var layerGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            layerGo.transform.SetParent(_canvas.transform, false);
            var layer = (RectTransform)layerGo.transform;
            UITheme.StretchAll(layer, 0f, 0f, 0f, 0f);

            var dim = layerGo.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;
            var dimBtn = layerGo.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.targetGraphic = dim;
            dimBtn.onClick.AddListener(CloseOverlay);

            var panel = UITheme.CreatePanel(layer, "OverlayPanel", Vector2.zero, new Vector2(width, height),
                UITheme.PanelBgRaised, UITheme.RadiusLarge);
            UITheme.PlaceCenter(panel, width, height);

            // 面板自己吃掉点击，避免点空白处把浮层关掉
            var panelImg = panel.GetComponent<Image>();
            if (panelImg != null)
            {
                var blocker = panel.gameObject.AddComponent<Button>();
                blocker.transition = Selectable.Transition.None;
                blocker.targetGraphic = panelImg;
            }

            var titleText = UIKit.Label(panel, title, UITheme.FontTitle, UITheme.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, "OverlayTitle");
            UITheme.PlaceTopLeft(titleText.rectTransform, UITheme.Space24, UITheme.Space16,
                width - UITheme.Space24 * 2f - 80f, 28f);

            var close = UIKit.TextButton(panel, "关闭", 64f, 28f, CloseOverlay, UITheme.FontBodySmall,
                "关闭浮层（Esc 或点击面板外也能关闭）");
            UITheme.PlaceTopRight(close.GetComponent<RectTransform>(), UITheme.Space16, UITheme.Space12, 64f, 28f);

            var rule = UITheme.CreateDivider(panel, "OverlayRule", Vector2.zero,
                new Vector2(width - UITheme.Space24 * 2f, 1f));
            UITheme.PlaceTopLeft(rule, UITheme.Space24, 56f, width - UITheme.Space24 * 2f, 1f);

            _overlay = layerGo;
            bodyTop = 72f;
            return panel;
        }

        private void CloseOverlay()
        {
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
        }

        private void OpenSongPicker()
        {
            const float width = 640f;
            const float rowW = 592f;
            const float rowH = 48f;
            const float rowGap = 4f;
            const int rowsShown = 8;

            var songs = ListSongs(SongsDirectory);
            int shown = Mathf.Min(songs.Count, rowsShown);
            // 面板高度跟着行数走：空曲库时只有一行提示 + 导入按钮，不留大片空白
            float height = Mathf.Max(230f, 72f + shown * (rowH + rowGap) + 76f);
            var panel = CreateOverlay("OverlaySongPicker", width, height, "选曲", out float bodyTop);
            if (songs.Count == 0)
            {
                var hint = UIKit.Label(panel,
                    "曲库还是空的。点下面的按钮从本机导入 .mid / .midi 文件，导入后直接进入课程。",
                    UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.UpperLeft, FontStyle.Normal,
                    "PickerEmpty");
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                hint.verticalOverflow = VerticalWrapMode.Truncate;
                UITheme.PlaceTopLeft(hint.rectTransform, UITheme.Space24, bodyTop, rowW, 44f);
            }
            else
            {
                for (int i = 0; i < shown; i++)
                {
                    // for 的循环变量在闭包里共享，必须先复制到局部变量（见 BuildLibrarySection 的历史崩溃）
                    string songPath = songs[i];
                    string songName = Path.GetFileNameWithoutExtension(songPath);
                    var row = UITheme.CreateButton(panel, songName, Vector2.zero, new Vector2(rowW, rowH),
                        () => { CloseOverlay(); LoadLesson(songPath); },
                        UITheme.FontBody, ButtonStyle.Secondary, $"开始学习《{songName}》");
                    UITheme.PlaceTopLeft(row.GetComponent<RectTransform>(), UITheme.Space24,
                        bodyTop + i * (rowH + rowGap), rowW, rowH);

                    var rowLabel = UITheme.GetLabel(row);
                    if (rowLabel != null) rowLabel.alignment = TextAnchor.MiddleLeft;

                    var meta = UIKit.Label(row.transform, DescribeSongMeta(songPath), UITheme.FontBodySmall,
                        UITheme.TextSecondary, TextAnchor.MiddleRight, FontStyle.Normal, "SongMeta");
                    UITheme.StretchAll(meta.rectTransform, UITheme.Space16, UITheme.Space16, 0f, 0f);
                }

                if (songs.Count > shown)
                {
                    var more = UIKit.Label(panel, $"还有 {songs.Count - shown} 首未显示（StreamingAssets/Songs）",
                        UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft,
                        FontStyle.Normal, "MoreHint");
                    UITheme.PlaceTopLeft(more.rectTransform, UITheme.Space24,
                        bodyTop + rowsShown * (rowH + rowGap), 420f, 18f);
                }
            }

            var import = UIKit.PrimaryButton(panel, "导入 MIDI…", new Vector2(148f, 40f),
                () => { CloseOverlay(); RequestImport(); },
                "选择本机 .mid / .midi 文件导入后直接进入课程");
            UITheme.PlaceTopRight(import.GetComponent<RectTransform>(), UITheme.Space24,
                height - 56f, 148f, 40f);
        }

        private void OpenCharacterPreview()
        {
            var panel = CreateOverlay("OverlayCharacters", 520f, 350f, "角色", out float bodyTop);
            bodyTop += 8f;

            const float cardW = 132f;
            const float cardH = 184f;
            const float gap = 16f;
            float startX = (520f - (3f * cardW + 2f * gap)) * 0.5f;

            int count = Mathf.Min(3, CharacterRegistry.BuiltInIds.Length);
            for (int i = 0; i < count; i++)
            {
                string id = CharacterRegistry.BuiltInIds[i];
                string displayName = i < CharacterRegistry.BuiltInDisplayNames.Length
                    ? CharacterRegistry.BuiltInDisplayNames[i]
                    : id;

                var card = UITheme.CreatePanel(panel, "CharCard_" + i, Vector2.zero,
                    new Vector2(cardW, cardH), UITheme.PanelBg, UITheme.RadiusLarge);
                UITheme.PlaceTopLeft(card, startX + i * (cardW + gap), bodyTop, cardW, cardH);
                var cardImg = card.GetComponent<Image>();
                if (cardImg != null) UITheme.SetOutline(cardImg, true, UITheme.Divider, 1f);

                var thumb = UITheme.CreatePanel(card, "Portrait", Vector2.zero,
                    new Vector2(cardW - 12f, 124f), UITheme.Control, UITheme.RadiusSmall);
                UITheme.PlaceTopLeft(thumb, 6f, 6f, cardW - 12f, 124f);
                var thumbImg = thumb.GetComponent<Image>();
                if (thumbImg != null)
                {
                    thumbImg.raycastTarget = false;
                    thumbImg.preserveAspect = true;
                    var tex = Resources.Load<Texture2D>(CharacterRegistry.PortraitResourcesPath(id));
                    if (tex != null)
                    {
                        thumbImg.sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f));
                        // Image.color 会与贴图相乘：有照片时必须回白，否则人像被面板底色染成暗棕
                        thumbImg.color = Color.white;
                    }
                }

                var nameLabel = UIKit.Label(card, displayName, UITheme.FontBody, UITheme.TextPrimary,
                    TextAnchor.MiddleCenter, FontStyle.Bold, "Name");
                UITheme.PlaceTopLeft(nameLabel.rectTransform, 6f, 134f, cardW - 12f, 22f);

                var tag = UIKit.Label(card, i == 2 ? "儿童 · 照片✓" : "成人 · 照片✓", UITheme.FontBodySmall,
                    UITheme.TextSecondary, TextAnchor.MiddleCenter, FontStyle.Normal, "Tag");
                UITheme.PlaceTopLeft(tag.rectTransform, 6f, 156f, cardW - 12f, 20f);
            }

            var note = UIKit.Label(panel, "进入课程后可随时从顶栏切换角色，不会影响播放进度、速度与循环设置。",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal, "Note");
            note.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.PlaceTopLeft(note.rectTransform, UITheme.Space24, bodyTop + cardH + 20f, 472f, 20f);
        }

        private void OpenSettings()
        {
            var panel = CreateOverlay("OverlaySettings", 560f, 404f, "设置", out float bodyTop);

            var qualityLabel = UIKit.Label(panel, "画质", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.MiddleLeft, FontStyle.Normal, "QualityLabel");
            UITheme.PlaceTopLeft(qualityLabel.rectTransform, UITheme.Space24, bodyTop, 120f, 20f);

            // 项目里有 6 个画质档（Very Low…Ultra），只显示英文原名对中文界面不友好：
            // 中文短标签上按钮，原始档位名进 tooltip；当前档位用 Selected 底 + BorderSelected 边标出。
            string[] tierLabels = { "极低", "低", "中", "高", "很高", "极高" };
            var qualityNames = QualitySettings.names;
            int current = QualitySettings.GetQualityLevel();
            int total = Mathf.Min(tierLabels.Length, qualityNames.Length);
            int columns = Mathf.Min(3, Mathf.Max(1, total));
            for (int i = 0; i < total; i++)
            {
                int level = i;
                string label = tierLabels[i];
                var chip = UIKit.Chip(panel, "Quality_" + i, 120f,
                    () => ApplyQuality(level), $"画质档位 {qualityNames[i]}（{label}）");
                UIKit.SetChip(chip, label, "", level == current, true);
                UITheme.PlaceTopLeft(chip.GetComponent<RectTransform>(),
                    UITheme.Space24 + (i % columns) * 128f,
                    bodyTop + 26f + (i / columns) * 36f, 120f, UITheme.Layout.ChipHeight);
            }

            // 档位列数多于一行时，后面的分组整体下移
            float dy = (Mathf.CeilToInt(total / (float)columns) - 1f) * 36f;

            var screenLabel = UIKit.Label(panel, "窗口", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.MiddleLeft, FontStyle.Normal, "ScreenLabel");
            UITheme.PlaceTopLeft(screenLabel.rectTransform, UITheme.Space24, bodyTop + 76f + dy, 120f, 20f);

            var fullscreen = UIKit.SecondaryButton(panel,
                Screen.fullScreen ? "全屏：开" : "全屏：关", new Vector2(140f, 32f),
                ToggleFullscreen, "切换全屏 / 窗口模式（立即生效并记住）");
            UITheme.PlaceTopLeft(fullscreen.GetComponent<RectTransform>(),
                UITheme.Space24, bodyTop + 100f + dy, 140f, 32f);

            var keysLabel = UIKit.Label(panel, "快捷键", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.MiddleLeft, FontStyle.Normal, "KeysLabel");
            UITheme.PlaceTopLeft(keysLabel.rectTransform, UITheme.Space24, bodyTop + 152f + dy, 120f, 20f);

            string[] shortcuts =
            {
                "空格　播放 / 暂停",
                "[　重播　　R　重置视角",
                "H　高亮双手　M　节拍器",
                "Tab　曲目分析　F1　帮助",
            };
            for (int i = 0; i < shortcuts.Length; i++)
            {
                var line = UIKit.Label(panel, shortcuts[i], UITheme.FontBody, UITheme.TextPrimary,
                    TextAnchor.MiddleLeft, FontStyle.Normal, "Shortcut_" + i);
                line.horizontalOverflow = HorizontalWrapMode.Overflow;
                UITheme.PlaceTopLeft(line.rectTransform, UITheme.Space24,
                    bodyTop + 176f + dy + i * 26f, 460f, 22f);
            }
        }

        private void ApplyQuality(int level)
        {
            if (level < 0 || level >= QualitySettings.names.Length) return;
            QualitySettings.SetQualityLevel(level, true);
            PlayerPrefs.SetInt(PrefQuality, level);
            PlayerPrefs.Save();
            // 重建浮层以刷新选中态（chip 的底色与边框在构建时定）
            CloseOverlay();
            OpenSettings();
        }

        private void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            PlayerPrefs.SetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0);
            PlayerPrefs.Save();
            CloseOverlay();
            OpenSettings();
        }

        private static void ApplySavedSettings()
        {
            if (PlayerPrefs.HasKey(PrefQuality))
            {
                int level = PlayerPrefs.GetInt(PrefQuality);
                if (level >= 0 && level < QualitySettings.names.Length) QualitySettings.SetQualityLevel(level, true);
            }
            if (PlayerPrefs.HasKey(PrefFullscreen) && PlayerPrefs.GetInt(PrefFullscreen) == 1 && !Screen.fullScreen)
            {
                Screen.fullScreen = true;
            }
        }

        // ---------- 曲库：统一卡片网格 + 导入入口 + 空 / 错误状态 ----------

        private void BuildLibrarySection()
        {
            var header = UIKit.SectionHeader(_content, "曲库");
            UITheme.PlaceTopLeft(header.rectTransform, 0f, 10f + _entriesOffset, 300f, 20f);

            var importBtn = UIKit.PrimaryButton(_content, "导入 MIDI…", new Vector2(148f, 40f),
                RequestImport, "选择本机 .mid / .midi 文件导入后直接进入课程（不必手动复制到工程目录）");
            UITheme.PlaceTopRight(importBtn.GetComponent<RectTransform>(), 0f, _entriesOffset, 148f, 40f);

            string dir = SongsDirectory;
            bool dirMissing = !SafeDirectoryExists(dir);
            var songs = ListSongs(dir);

            if (dirMissing)
            {
                _showRecent = false;
                BuildStateCard("!", "曲库不可用",
                    "没有找到示例曲目录 StreamingAssets/Songs。\n" +
                    "这不影响导入自己的文件：点下方按钮选择本机的 .mid / .midi 即可开始练习。",
                    UITheme.TextError, true);
                return;
            }
            if (songs.Count == 0)
            {
                _showRecent = false;
                BuildStateCard("♪", "曲库为空",
                    "还没有可练习的曲目。\n" +
                    "点下方按钮从本机选择 .mid / .midi 文件，或把示例文件放进 StreamingAssets/Songs。",
                    UITheme.Accent, false);
                return;
            }

            _showRecent = true;
            header.text = $"曲库（{songs.Count} 首）";

            float avail = Width(_content);
            float cardW = UITheme.Layout.SongCardWidth;
            float cardH = UITheme.Layout.SongCardHeight;
            float gap = UITheme.Layout.SongCardGap;
            int columns = Mathf.Max(UITheme.Layout.SongCardMinColumns,
                Mathf.FloorToInt((avail + gap) / (cardW + gap)));
            columns = Mathf.Min(columns, Mathf.Max(1, songs.Count));

            int shown = Mathf.Min(songs.Count, MaxLibraryCards);
            // 网格与标题、区块标题共享左基线（不居中），右侧余量留给「导入 MIDI…」
            const float leftOffset = 0f;

            for (int i = 0; i < shown; i++)
            {
                int col = i % columns;
                int row = i / columns;

                // 逐次迭代复制到局部变量再进闭包：for 的循环变量是**共享**的，
                // 直接写 () => LoadLesson(songs[i]) 会让所有卡片共用同一个 i，
                // 点击时 i 已等于 shown → ArgumentOutOfRangeException（实测崩溃栈见 BuildLibrarySection）。
                // foreach 的迭代变量才有逐次捕获语义，for 必须自己复制。
                string songPath = songs[i];
                string songName = Path.GetFileNameWithoutExtension(songPath);

                var card = UIKit.Card(_content, songName,
                    DescribeSongMeta(songPath), new Vector2(cardW, cardH),
                    () => LoadLesson(songPath), $"开始学习《{songName}》");
                UITheme.PlaceTopLeft(card.GetComponent<RectTransform>(),
                    leftOffset + col * (cardW + gap), GridTopGap + _entriesOffset + row * (cardH + gap), cardW, cardH);
            }

            int rows = Mathf.CeilToInt(shown / (float)columns);
            float gridBottom = GridTopGap + _entriesOffset + rows * cardH + Mathf.Max(0, rows - 1) * gap;

            if (songs.Count > shown)
            {
                var more = UIKit.Label(_content, $"还有 {songs.Count - shown} 首未显示（可滚动查看：StreamingAssets/Songs）",
                    UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal, "MoreHint");
                UITheme.PlaceTopLeft(more.rectTransform, 0f, gridBottom + UITheme.Space8, 600f, 16f);
            }
        }

        /// <summary>曲目元信息（只读文件大小与文件名，不解析文件内容，避免菜单卡顿）。</summary>
        private static string DescribeSongMeta(string path)
        {
            try
            {
                var info = new FileInfo(path);
                long bytes = info.Exists ? info.Length : 0;
                string size = bytes >= 1024 * 1024
                    ? $"{bytes / 1024f / 1024f:0.0} MB"
                    : $"{Mathf.Max(0f, bytes / 1024f):0.0} KB";
                return $"{size}　·　点击开始学习";
            }
            catch (Exception)
            {
                return "点击开始学习";
            }
        }

        /// <summary>空态 / 错误态统一用居中卡片：图标 + 说明（15）+ 行动按钮（40 高）。</summary>
        private void BuildStateCard(string glyph, string title, string body, Color glyphColor, bool isError)
        {
            const float w = 520f;
            const float h = 232f;

            var panel = UIKit.Panel(_content, isError ? "LibraryError" : "LibraryEmpty",
                isError ? UITheme.ErrorBannerColor(Core.ErrorSeverity.Recoverable) : UITheme.PanelBgRaised,
                UITheme.RadiusLarge);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(w, h);
            panel.anchoredPosition = new Vector2(0f, -8f);

            var icon = UITheme.CreatePanel(panel, "Icon", Vector2.zero, new Vector2(48f, 48f),
                isError ? UITheme.DangerBg : UITheme.PanelBgDeep, 24);
            UITheme.PlaceTopLeft(icon, (w - 48f) * 0.5f, UITheme.Space24, 48f, 48f);
            var iconImg = icon.GetComponent<Image>();
            if (iconImg != null) iconImg.raycastTarget = false;
            var glyphText = UIKit.Label(icon, glyph, UITheme.FontTitle, glyphColor, TextAnchor.MiddleCenter,
                FontStyle.Bold, "Glyph");
            UITheme.StretchAll(glyphText.rectTransform, 0f, 0f, 0f, 0f);

            var titleText = UIKit.Label(panel, title, UITheme.FontSubtitle,
                isError ? UITheme.TextError : UITheme.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold,
                "StateTitle");
            UITheme.PlaceTopLeft(titleText.rectTransform, UITheme.Space24, UITheme.Space24 + 48f + UITheme.Space8,
                w - UITheme.Space24 * 2f, 20f);

            var bodyText = UIKit.Label(panel, body, UITheme.FontBodySmall,
                isError ? UITheme.TextPrimary : UITheme.TextSecondary, TextAnchor.UpperCenter,
                FontStyle.Normal, "StateBody");
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(bodyText.rectTransform, UITheme.Space32, 112f, w - UITheme.Space32 * 2f, 44f);

            var importBtn = UIKit.PrimaryButton(panel, "导入 MIDI…", new Vector2(148f, 40f), RequestImport,
                "选择本机 MIDI 文件导入");
            UITheme.Place(importBtn.GetComponent<RectTransform>(), (w - 148f) * 0.5f, UITheme.Space16, 148f, 40f);

            if (isError)
                Debug.LogWarning("[PianoTeacher3D][UI] 曲库目录缺失，已显示错误态卡片。");
        }

        // ---------- 最近播放（含失效提示） ----------

        private void BuildRecentSection()
        {
            if (!_showRecent) return;   // 空态 / 错误态卡片居中显示，不再叠一个空的「最近播放」
            float avail = Width(_content);
            float top = RecentSectionTop();

            var divider = UITheme.CreateDivider(_content, "Divider", Vector2.zero, new Vector2(avail, 1f));
            UITheme.PlaceTopLeft(divider, 0f, top, avail, 1f);

            var header = UIKit.SectionHeader(_content, "最近播放（本次运行）");
            UITheme.PlaceTopLeft(header.rectTransform, 0f, top + UITheme.Space16, 400f, 20f);

            int shown = 0;
            for (int i = 0; i < RecentPaths.Count && shown < MaxRecentRows; i++)
            {
                string path = RecentPaths[i];
                bool missing = !SafeFileExists(path);
                BuildRecentRow(path, missing, shown, top + UITheme.Space16 + 28f, avail);
                shown++;
            }

            if (shown == 0)
            {
                var empty = UIKit.Label(_content, "本次运行还没有从本菜单进入过曲目。从上方曲库选择一首，或导入自己的 MIDI 文件。",
                    UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal,
                    "RecentEmpty");
                UITheme.PlaceTopLeft(empty.rectTransform, 0f, top + UITheme.Space16 + 28f, avail, 20f);
            }
        }

        /// <summary>最近播放区起点：曲库网格最多两行卡片之后。</summary>
        private float RecentSectionTop()
        {
            float avail = Width(_content);
            float gap = UITheme.Layout.SongCardGap;
            int columns = Mathf.Max(UITheme.Layout.SongCardMinColumns,
                Mathf.FloorToInt((avail + gap) / (UITheme.Layout.SongCardWidth + gap)));
            int shown = Mathf.Min(SongsCount(), MaxLibraryCards);
            if (shown <= 0) columns = UITheme.Layout.SongCardMinColumns;
            int rows = Mathf.Max(1, Mathf.CeilToInt(shown / (float)Mathf.Max(1, columns)));
            return GridTopGap + _entriesOffset + rows * UITheme.Layout.SongCardHeight
                + (rows - 1) * gap + UITheme.Space24;
        }

        private static int SongsCount()
        {
            string dir = SongsDirectory;
            return SafeDirectoryExists(dir) ? Mathf.Min(ListSongs(dir).Count, MaxLibraryCards) : 0;
        }

        private void BuildRecentRow(string path, bool missing, int index, float top, float width)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            float y = top + index * (RecentRowHeight + RecentRowGap);

            if (!missing)
            {
                var row = UITheme.CreateButton(_content, name, Vector2.zero, new Vector2(width, RecentRowHeight),
                    () => LoadLesson(path), UITheme.FontBody, ButtonStyle.Secondary,
                    $"再次打开《{name}》");
                UITheme.PlaceTopLeft(row.GetComponent<RectTransform>(), 0f, y, width, RecentRowHeight);
                var rowImg = row.targetGraphic as Image;
                if (rowImg != null)
                {
                    var sprite = UITheme.GetRoundedSprite(UITheme.RadiusMedium);
                    if (sprite != null) { rowImg.sprite = sprite; rowImg.type = Image.Type.Sliced; }
                    UITheme.SetOutline(rowImg, true, UITheme.Divider, 1f);
                }
                var label = UITheme.GetLabel(row);
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleLeft;
                    UITheme.StretchAll(label.rectTransform, UITheme.Space16, 160f, 0f, 0f);
                }
                var meta = UIKit.Label(row.transform, "可直接打开", UITheme.FontBodySmall,
                    UITheme.TextSecondary, TextAnchor.MiddleRight, FontStyle.Normal, "Meta");
                UITheme.StretchAll(meta.rectTransform, width - 140f, UITheme.Space16, 0f, 0f);
                return;
            }

            // 文件被移动 / 删除：给出失效提示 + 重新导入入口，并允许移除该条记录（危险色 + 二次确认）
            var panel = UITheme.CreatePanel(_content, "RecentMissing", Vector2.zero,
                new Vector2(width, RecentRowHeight), UITheme.PanelBgRaised, UITheme.RadiusLarge);
            var panelBg = panel.GetComponent<Image>();
            if (panelBg != null) UITheme.SetOutline(panelBg, true, UITheme.Divider, 1f);
            UITheme.PlaceTopLeft(panel, 0f, y, width, RecentRowHeight);

            var title = UIKit.Label(panel, "! " + name, UITheme.FontBody, UITheme.TextWarning,
                TextAnchor.MiddleLeft, FontStyle.Normal, "MissingTitle");
            UITheme.PlaceTopLeft(title.rectTransform, UITheme.Space16, 8f, 320f, 20f);

            var hint = UIKit.Label(panel, "文件已移动或删除：请点「重新导入」重新选择文件",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal, "MissingHint");
            UITheme.PlaceTopLeft(hint.rectTransform, UITheme.Space16, 28f, 420f, 16f);

            var reimportBtn = UITheme.CreateButton(panel, "重新导入", Vector2.zero, new Vector2(88f, 28f),
                RequestImport, UITheme.FontBodySmall, ButtonStyle.Ghost, "重新选择本机 MIDI 文件");
            UITheme.PlaceRight(reimportBtn.GetComponent<RectTransform>(), 104f, 10f, 88f, 28f);

            var removeBtn = UITheme.CreateButton(panel, "移除", Vector2.zero, new Vector2(88f, 28f),
                null, UITheme.FontBodySmall, ButtonStyle.Danger,
                "从最近列表移除这条记录（需再次点击确认）");
            UITheme.PlaceRight(removeBtn.GetComponent<RectTransform>(), 8f, 10f, 88f, 28f);
            removeBtn.onClick.AddListener(() => RequestRemoveRecent(removeBtn, path));
        }

        /// <summary>破坏性操作二次确认：第一次点击改为「确认移除？」，4 秒内再次点击才真正移除。</summary>
        private void RequestRemoveRecent(Button button, string path)
        {
            if (button == null) return;

            if (_pendingRemoveBtn == button && Time.unscaledTime <= _removeConfirmUntil &&
                !string.IsNullOrEmpty(path))
            {
                RecentPaths.Remove(path);
                _pendingRemoveBtn = null;
                UITooltip.Hide();
                RebuildContent();
                return;
            }

            _pendingRemoveBtn = button;
            _removeConfirmUntil = Time.unscaledTime + RemoveConfirmSeconds;
            UITheme.SetButtonLabel(button, "确认移除？");
            UITheme.ApplyButtonState(button, ButtonState.Selected, ButtonStyle.Danger);
        }

        /// <summary>只重建内容区（不重建画布，避免重复画布与事件系统残留）。</summary>
        private void RebuildContent()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
            BuildEntryCards();
            BuildLibrarySection();
            BuildRecentSection();
        }

        // ---------- 底部说明 ----------

        private void BuildFooter()
        {
            var footer = UIKit.Label(_canvas.transform,
                "进入课程后：空格 播放/暂停　·　[ 重播　·　R 重置视角　·　H 高亮　·　M 节拍器　·　Tab 分析　·　F1 帮助",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleCenter, FontStyle.Normal, "Footer");
            footer.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.StretchX(footer.rectTransform, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin, 20f);
        }

        private static float Width(RectTransform rt)
        {
            if (rt == null) return UITheme.Layout.ReferenceResolution.x - UITheme.Layout.ScreenMargin * 2f;
            float w = rt.rect.width;
            return w > 1f ? w : UITheme.Layout.ReferenceResolution.x - UITheme.Layout.ScreenMargin * 2f;
        }

        // ---------- 场景跳转与文件访问 ----------

        private static string SongsDirectory => Path.Combine(Application.streamingAssetsPath, "Songs");

        private static bool SafeDirectoryExists(string dir)
        {
            try
            {
                return Directory.Exists(dir);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SafeFileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                return File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<string> ListSongs(string dir)
        {
            var result = new List<string>();
            try
            {
                if (!Directory.Exists(dir)) return result;
                var files = Directory.GetFiles(dir, "*.mid");
                if (files != null) result.AddRange(files);
                result.Sort();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PianoTeacher3D][UI] 列举曲库失败：{ex.GetType().Name}");
            }
            return result;
        }

        /// <summary>请求导入：进入课程场景后由控制层调用会话层的 <c>ImportMidi()</c>（应用内文件选择器）。</summary>
        private void RequestImport()
        {
            SelectedSongPath = "";
            _pendingImport = true;
            SceneManager.LoadScene("PianoLesson");
        }

        private static void LoadLesson(string songPath)
        {
            SelectedSongPath = songPath;
            NotifySongOpened(songPath);
            SceneManager.LoadScene("PianoLesson");
        }
    }
}
