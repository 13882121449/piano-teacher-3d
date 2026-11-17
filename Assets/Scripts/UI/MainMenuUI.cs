using System;
using System.Collections.Generic;
using System.IO;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 钢琴练习室首页。上方提供继续练习和示范入口，下方用一个滚动列表展示曲库与已导入的最近曲目。
    /// <para>
    /// 选中曲目通过静态字段 <see cref="SelectedSongPath"/> 传给课程场景（原有跨场景通道，保持不变）。
    /// </para>
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        /// <summary>由主菜单选中的曲目路径（课程场景读取，保持原有契约）。</summary>
        public static string SelectedSongPath = "";

        /// <summary>课程只在用户点击「继续练习」时恢复历史位置。</summary>
        public static bool ResumeSongRequested { get; set; }

        private const float RemoveConfirmSeconds = 4f;
        private const float HeaderHeight = 60f;
        private const float LibraryTop = 447f;
        private const string PrefReduceMotion = "piano.reduceMotion";
        private const float SongRowHeight = 48f;
        private const float SongRowGap = UITheme.Space8;

        private static bool _pendingImport;
        private static bool _pendingFirstLesson;

        internal static bool ConsumeFirstLesson()
        {
            bool requested = _pendingFirstLesson;
            _pendingFirstLesson = false;
            return requested;
        }

        private static void OpenFirstLesson()
        {
            SelectedSongPath = "";
            ResumeSongRequested = false;
            _pendingFirstLesson = true;
            SceneManager.LoadScene("PianoLesson");
        }
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
        private GameObject _ribbon;
        private Button _motionToggle;
        private readonly Dictionary<string, string> _songTitles = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        private SongLibraryService _library;
        private UserPreferences _preferences;
        private Button _pendingRemoveBtn;
        private float _removeConfirmUntil;
        private CanvasGroup _headerGroup;
        private CanvasGroup _contentGroup;
        private float _layoutWidth;
        private GameObject _overlay;
        private GameObject _overlayTrigger;

        private void Start()
        {
            ApplySavedSettings();
            _library = new SongLibraryService();
            _preferences = new UserPreferences();
            _preferences.Load();
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

            // CanvasScaler 在首帧设置参考宽度。宽度变化时只重排曲库和练习入口。
            if (_content != null)
            {
                float width = Width(_content);
                if (Mathf.Abs(width - _layoutWidth) > 1f) RebuildContent();
            }
        }

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            _canvas = UITheme.CreateCanvas(transform, "MainMenuCanvas");

            BuildStage();

            var header = UITheme.CreateRect(_canvas.transform, "MenuHeader", Vector2.zero, Vector2.zero,
                MenuStagePalette.Ink);
            UITheme.StretchTop(header, 0f, 0f, 0f, HeaderHeight);
            _headerGroup = header.gameObject.AddComponent<CanvasGroup>();

            var title = UIKit.Label(header, "钢琴 3D 教学", UITheme.FontSubtitle, MenuStagePalette.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold, "PageTitle");
            UITheme.PlaceTopLeft(title.rectTransform, UITheme.Space32, 14f, 144f, UITheme.HeightSmall);

            var characters = UIKit.TextButton(header, "角色", 72f, UITheme.HeightSmall, OpenCharacterPreview);
            characters.gameObject.name = "EntryCard_角色";
            UITheme.PlaceTopRight(characters.GetComponent<RectTransform>(), 116f, UITheme.Space16, 72f,
                UITheme.HeightSmall);
            var settings = UIKit.TextButton(header, "设置", 72f, UITheme.HeightSmall, OpenSettings);
            settings.gameObject.name = "EntryCard_设置";
            UITheme.PlaceTopRight(settings.GetComponent<RectTransform>(), UITheme.Space32, UITheme.Space16, 72f,
                UITheme.HeightSmall);

            _motionToggle = UIKit.TextButton(header, "减少动效", 118f, UITheme.HeightSmall, ToggleMotion);
            _motionToggle.gameObject.name = "MenuMotionToggle";
            UITheme.PlaceTopRight(_motionToggle.GetComponent<RectTransform>(), 200f, UITheme.Space16, 118f,
                UITheme.HeightSmall);
            UpdateMotion();

            _content = UITheme.CreateRect(_canvas.transform, "Content", Vector2.zero, Vector2.zero,
                UITheme.Transparent);
            _contentGroup = _content.gameObject.AddComponent<CanvasGroup>();
            UITheme.StretchAll(_content, UITheme.Space32, UITheme.Space32, UITheme.Space24,
                HeaderHeight);
            RebuildContent();
        }

        private void BuildStage()
        {
            var backdrop = UITheme.CreateRect(_canvas.transform, "Backdrop", Vector2.zero, Vector2.zero,
                MenuStagePalette.Ink);
            UITheme.StretchAll(backdrop, 0f, 0f, 0f, 0f);

            var art = new GameObject("MenuStageArt", typeof(RectTransform), typeof(RawImage));
            art.transform.SetParent(backdrop, false);
            var artRect = (RectTransform)art.transform;
            UITheme.StretchTop(artRect, 0f, 0f, 0f, HeaderHeight + LibraryTop + 32f);
            var image = art.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.texture = Resources.Load<Texture2D>("Menu/ConcertGrand");
            image.enabled = image.texture != null;
            if (image.texture != null)
            {
                var crop = art.AddComponent<MenuStageCrop>();
                crop.SourceAspect = image.texture.width / (float)image.texture.height;
                crop.Image = image;
            }

            _ribbon = new GameObject("MenuKeyRibbon", typeof(RectTransform), typeof(MenuKeyRibbon));
            _ribbon.transform.SetParent(art.transform, false);
            UITheme.StretchAll((RectTransform)_ribbon.transform, 0f, 0f, 0f, 0f);
            _ribbon.GetComponent<MenuKeyRibbon>().raycastTarget = false;

            var shade = new GameObject("MenuStageScrim", typeof(RectTransform), typeof(MenuStageScrim));
            shade.transform.SetParent(art.transform, false);
            UITheme.StretchAll((RectTransform)shade.transform, 0f, 0f, 0f, 0f);
            shade.GetComponent<MenuStageScrim>().raycastTarget = false;
            _ribbon.transform.SetAsLastSibling();
        }

        private void ToggleMotion()
        {
            bool active = PlayerPrefs.GetInt(PrefReduceMotion, 0) == 1;
            PlayerPrefs.SetInt(PrefReduceMotion, active ? 0 : 1);
            PlayerPrefs.Save();
            UpdateMotion();
        }

        private void UpdateMotion()
        {
            bool reduced = PlayerPrefs.GetInt(PrefReduceMotion, 0) == 1;
            var ribbon = _ribbon.GetComponent<MenuKeyRibbon>();
            ribbon.SetReducedMotion(reduced);
            UITheme.SetButtonLabel(_motionToggle, reduced ? "减少动效：开" : "减少动效：关");
        }

        private void BuildPracticeHero(string practicePath, bool isResume)
        {
            var hero = UITheme.CreateRect(_content, "PracticeHero", Vector2.zero, Vector2.zero, UITheme.Transparent);
            UITheme.StretchTop(hero, 0f, 0f, 0f, LibraryTop);

            float inset = _layoutWidth >= 920f ? 48f : 12f;
            float width = Mathf.Min(520f, _layoutWidth - inset - UITheme.Space24);
            int titleFont = _layoutWidth >= 880f ? 80 : 60;
            var title = UIKit.Label(hero, "钢琴练习室", titleFont, MenuStagePalette.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold, "PracticeTitle");
            UITheme.PlaceTopLeft(title.rectTransform, inset, 98f, width, 124f);

            bool started = PlayerPrefs.GetInt(GuidedPracticeUI.ProgressKey, 0) > 0;
            var current = UIKit.Label(hero, "第一课 · 右手五指与四拍", UITheme.FontSubtitle,
                MenuStagePalette.Secondary, TextAnchor.MiddleLeft, FontStyle.Normal, "PracticeSong");
            UITheme.PlaceTopLeft(current.rectTransform, inset + 2f, 232f, width, 28f);

            var play = UIKit.PrimaryButton(hero, started ? "继续练习" : "开始第一课",
                new Vector2(152f, UITheme.HeightPrimary), OpenFirstLesson, "右手五指，两小节");
            play.gameObject.name = "PracticePrimary";
            play.targetGraphic.color = MenuStagePalette.Action;
            UITheme.GetLabel(play).color = MenuStagePalette.Ink;
            UITheme.PlaceTopLeft(play.GetComponent<RectTransform>(), inset, 282f, 152f, UITheme.HeightPrimary);

            string cinematicPath = Path.Combine(SongsDirectory, "cinematic", "cinematic_tide.mid");
            if (SafeFileExists(cinematicPath))
            {
                var cinematic = UIKit.SecondaryButton(hero, "观看《潮光》",
                    new Vector2(128f, UITheme.HeightPrimary),
                    () => CinematicShowcaseLaunch.Open(cinematicPath), "海底琴房 · 自动运镜");
                cinematic.gameObject.name = "CinematicShowcasePrimary";
                UITheme.PlaceTopLeft(cinematic.GetComponent<RectTransform>(), inset + 316f, 282f,
                    128f, UITheme.HeightPrimary);
            }

            if (practicePath == null) return;
            var showcase = UIKit.SecondaryButton(hero, isResume ? "继续上次演奏" : "演奏展示",
                new Vector2(140f, UITheme.HeightPrimary), () => LoadLesson(practicePath, isResume), SongTitle(practicePath));
            showcase.gameObject.name = "ShowcasePrimary";
            UITheme.PlaceTopLeft(showcase.GetComponent<RectTransform>(), inset + 164f, 282f, 140f, UITheme.HeightPrimary);
        }

        private const string PrefQuality = "piano.quality";
        private const string PrefFullscreen = "piano.fullscreen";

        /// <summary>全屏遮罩（点击关闭）+ 居中面板 + 标题 + 右上关闭；返回可承载内容的面板。</summary>
        private RectTransform CreateOverlay(string name, float width, float height, string title, out float bodyTop)
        {
            if (_overlay == null && EventSystem.current != null)
                _overlayTrigger = EventSystem.current.currentSelectedGameObject;
            if (_overlay != null)
            {
                _overlay.SetActive(false);
                Destroy(_overlay);
            }
            SetBackgroundInteraction(false);

            var layerGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            layerGo.transform.SetParent(_canvas.transform, false);
            var layer = (RectTransform)layerGo.transform;
            UITheme.StretchAll(layer, 0f, 0f, 0f, 0f);

            var dim = layerGo.GetComponent<Image>();
            dim.color = UITheme.ModalShade;
            dim.raycastTarget = true;
            var dimBtn = layerGo.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.targetGraphic = dim;
            dimBtn.navigation = new Navigation { mode = Navigation.Mode.None };
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
                blocker.navigation = new Navigation { mode = Navigation.Mode.None };
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
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(close.gameObject);
            bodyTop = 72f;
            return panel;
        }

        private void CloseOverlay()
        {
            if (_overlay == null) return;
            _overlay.SetActive(false);
            Destroy(_overlay);
            _overlay = null;
            SetBackgroundInteraction(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_overlayTrigger);
            _overlayTrigger = null;
        }

        private void SetBackgroundInteraction(bool enabled)
        {
            _headerGroup.interactable = enabled;
            _headerGroup.blocksRaycasts = enabled;
            _contentGroup.interactable = enabled;
            _contentGroup.blocksRaycasts = enabled;
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

                var thumb = UIKit.Panel(card, "Portrait", UITheme.PanelBgDeep, UITheme.RadiusSmall);
                UITheme.PlaceTopLeft(thumb, 6f, 6f, cardW - 12f, 124f);
                UIKit.PlaceCharacterPreview(thumb, id);

                var nameLabel = UIKit.Label(card, displayName, UITheme.FontBody, UITheme.TextPrimary,
                    TextAnchor.MiddleCenter, FontStyle.Bold, "Name");
                UITheme.PlaceTopLeft(nameLabel.rectTransform, 6f, 134f, cardW - 12f, 22f);

            }

            var note = UIKit.Label(panel, "进入课程后，可在右上角切换老师。",
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
            OpenSettings();
        }

        private void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            PlayerPrefs.SetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0);
            PlayerPrefs.Save();
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

        // ---------- 单一曲库：内置、最近导入与失效文件 ----------

        private void BuildLibrarySection(List<string> songs, List<string> missing)
        {
            var shelf = UITheme.CreateRect(_content, "LibraryShelf", Vector2.zero, Vector2.zero,
                MenuStagePalette.Library);
            UITheme.StretchAll(shelf, -UITheme.Space32, -UITheme.Space32, -UITheme.Space24, LibraryTop - 12f);
            shelf.GetComponent<Image>().raycastTarget = false;
            var rule = UITheme.CreateRect(shelf, "LibraryRule", Vector2.zero, Vector2.zero, MenuStagePalette.Rule);
            UITheme.StretchTop(rule, 0f, 0f, 0f, 1f);
            rule.GetComponent<Image>().raycastTarget = false;

            var header = UIKit.SectionHeader(_content, "曲库");
            header.fontSize = UITheme.FontTitle;
            UITheme.PlaceTopLeft(header.rectTransform, 0f, LibraryTop, 200f, UITheme.HeightButton);

            bool hasSongs = songs.Count > 0;
            var import = hasSongs
                ? UIKit.SecondaryButton(_content, "导入 MIDI…", new Vector2(132f, UITheme.HeightButton),
                    RequestImport, "从本机选择 .mid 或 .midi 文件")
                : UIKit.PrimaryButton(_content, "导入 MIDI…", new Vector2(132f, UITheme.HeightButton),
                    RequestImport, "从本机选择 .mid 或 .midi 文件");
            UITheme.PlaceTopRight(import.GetComponent<RectTransform>(), 0f, LibraryTop, 132f,
                UITheme.HeightButton);

            const float rowsTop = LibraryTop + UITheme.HeightButton + UITheme.Space12;
            if (!hasSongs)
            {
                bool directoryExists = SafeDirectoryExists(SongsDirectory);
                var hint = UIKit.Label(_content, directoryExists ? "曲库为空" : "找不到内置曲库",
                    UITheme.FontSubtitle, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal,
                    directoryExists ? "LibraryEmpty" : "LibraryError");
                UITheme.StretchTop(hint.rectTransform, 0f, 0f, rowsTop, 28f);
                if (missing.Count == 0) return;
            }

            var viewport = UITheme.CreateRect(_content, "LibraryViewport", Vector2.zero, Vector2.zero,
                UITheme.Transparent);
            UITheme.StretchAll(viewport, 0f, 0f, 0f, rowsTop + (hasSongs ? 0f : 36f));
            viewport.GetComponent<Image>().raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            int columns = _layoutWidth >= 840f ? 2 : 1;
            int songRows = Mathf.CeilToInt(songs.Count / (float)columns);
            float rowsHeight = (songRows + missing.Count) * (SongRowHeight + SongRowGap);
            var list = UITheme.CreateRect(viewport, "LibrarySongs", Vector2.zero, Vector2.zero, UITheme.Transparent);
            list.pivot = new Vector2(0f, 1f);
            UITheme.StretchTop(list, 0f, 0f, 0f, rowsHeight);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = list;
            scroll.horizontal = false;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = SongRowHeight + SongRowGap;
            scroll.verticalNormalizedPosition = 1f;

            float cardWidth = (_layoutWidth - (columns - 1) * UITheme.Space12) / columns;
            for (int i = 0; i < songs.Count; i++)
            {
                string songPath = songs[i];
                string songName = SongTitle(songPath);
                float x = i % columns * (cardWidth + UITheme.Space12);
                float y = i / columns * (SongRowHeight + SongRowGap);
                var row = UIKit.SecondaryButton(list,
                    UITheme.Truncate(songName, Mathf.FloorToInt((cardWidth - 64f) / UITheme.FontBody)),
                    new Vector2(cardWidth, SongRowHeight), () => LoadLesson(songPath), songName,
                    fontSize: UITheme.FontBody);
                row.gameObject.name = "Card_" + songName;
                row.targetGraphic.color = MenuStagePalette.Row;
                UITheme.PlaceTopLeft(row.GetComponent<RectTransform>(), x, y, cardWidth, SongRowHeight);
                var label = UITheme.GetLabel(row);
                label.alignment = TextAnchor.MiddleLeft;
                UITheme.StretchAll(label.rectTransform, UITheme.Space16, 48f, 0f, 0f);
                var arrow = UIKit.Label(row.transform, "›", UITheme.FontTitle, UITheme.TextSecondary,
                    TextAnchor.MiddleRight, FontStyle.Normal, "OpenArrow");
                UITheme.PlaceTopRight(arrow.rectTransform, UITheme.Space16, 0f, 28f, SongRowHeight);
                KeepSelectionVisible(row, scroll, y);
            }

            for (int i = 0; i < missing.Count; i++)
                BuildMissingRow(list, scroll, missing[i], (songRows + i) * (SongRowHeight + SongRowGap));
        }

        private static void KeepSelectionVisible(Selectable control, ScrollRect scroll, float top)
        {
            var focus = control.gameObject.AddComponent<LibraryRowFocus>();
            focus.Scroll = scroll;
            focus.RowTop = top;
            focus.RowHeight = SongRowHeight;
        }

        private void BuildMissingRow(RectTransform list, ScrollRect scroll, string path, float top)
        {
            var row = UITheme.CreatePanel(list, "RecentMissing", Vector2.zero, Vector2.zero,
                UITheme.PanelBgRaised, UITheme.RadiusLarge);
            UITheme.StretchTop(row, 0f, 0f, top, SongRowHeight);

            float titleWidth = _layoutWidth - 208f;
            string name = UITheme.Truncate(SongTitle(path),
                Mathf.FloorToInt(titleWidth / UITheme.FontBody));
            var title = UIKit.Label(row, name, UITheme.FontBody, UITheme.TextWarning,
                TextAnchor.MiddleLeft, FontStyle.Normal, "MissingTitle");
            UITheme.PlaceTopLeft(title.rectTransform, UITheme.Space16, 4f, titleWidth, 22f);
            var hint = UIKit.Label(row, "文件已移动或删除", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.MiddleLeft, FontStyle.Normal, "MissingHint");
            UITheme.PlaceTopLeft(hint.rectTransform, UITheme.Space16, 26f, titleWidth, 18f);

            var reimport = UIKit.TextButton(row, "重新导入", 88f, UITheme.HeightSmall, RequestImport);
            UITheme.PlaceTopRight(reimport.GetComponent<RectTransform>(), 104f, UITheme.Space8, 88f,
                UITheme.HeightSmall);
            var remove = UITheme.CreateButton(row, "移除", Vector2.zero, new Vector2(88f, UITheme.HeightSmall),
                null, UITheme.FontBodySmall, ButtonStyle.Danger, "再次确认后移除最近记录");
            UITheme.PlaceTopRight(remove.GetComponent<RectTransform>(), UITheme.Space8, UITheme.Space8, 88f,
                UITheme.HeightSmall);
            remove.onClick.AddListener(() => RequestRemoveRecent(remove, path));
            KeepSelectionVisible(reimport, scroll, top);
            KeepSelectionVisible(remove, scroll, top);
        }

        /// <summary>破坏性操作二次确认：第一次点击改为「确认移除？」，4 秒内再次点击才真正移除。</summary>
        private void RequestRemoveRecent(Button button, string path)
        {
            if (button == null) return;

            if (_pendingRemoveBtn == button && Time.unscaledTime <= _removeConfirmUntil &&
                !string.IsNullOrEmpty(path))
            {
                RecentPaths.Remove(path);
                _library.DismissRecent(path);
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
            _layoutWidth = Width(_content);
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            var songs = new List<string>(_library.ListBuiltInSongs());
            var seen = new HashSet<string>(songs, StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            _songTitles.Clear();
            foreach (SongMetadata song in _library.RecentSongs)
            {
                if (song == null || string.IsNullOrEmpty(song.SourcePath)) continue;
                _songTitles[song.SourcePath] = song.Title;
                if (!seen.Add(song.SourcePath)) continue;
                if (SafeFileExists(song.SourcePath)) songs.Add(song.SourcePath);
                else missing.Add(song.SourcePath);
            }
            foreach (string path in RecentPaths)
            {
                if (!seen.Add(path)) continue;
                if (SafeFileExists(path)) songs.Add(path);
                else missing.Add(path);
            }

            string resume = ResolveResume(songs);
            string demo = songs.Find(path =>
                string.Equals(Path.GetFileNameWithoutExtension(path), "demo", StringComparison.OrdinalIgnoreCase));
            BuildPracticeHero(resume ?? demo ?? (songs.Count > 0 ? songs[0] : null), resume != null);
            BuildLibrarySection(songs, missing);
        }

        private string SongTitle(string path)
        {
            return _songTitles.TryGetValue(path, out string title) && !string.IsNullOrWhiteSpace(title)
                ? title : Path.GetFileNameWithoutExtension(path);
        }

        private string ResolveResume(List<string> songs)
        {
            string id = _preferences.LastSongId;
            if (!string.IsNullOrEmpty(id))
            {
                if (_library.TryResolveRecent(id, out _, out string path, out _)) return path;
                foreach (string entry in songs)
                    if (string.Equals(id, SongLibraryService.StableId(entry), StringComparison.OrdinalIgnoreCase))
                        return entry;
                return null;
            }
            return RecentPaths.Find(SafeFileExists);
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

        /// <summary>请求导入：进入课程场景后由控制层调用会话层的 <c>ImportMidi()</c>（应用内文件选择器）。</summary>
        private void RequestImport()
        {
            SelectedSongPath = "";
            ResumeSongRequested = false;
            _pendingImport = true;
            SceneManager.LoadScene("PianoLesson");
        }

        private static void LoadLesson(string songPath, bool resume = false)
        {
            SelectedSongPath = songPath;
            ResumeSongRequested = resume;
            NotifySongOpened(songPath);
            SceneManager.LoadScene("PianoLesson");
        }
    }

    /// <summary>键盘选中列表项时将该项滚进可视区域，鼠标滚轮仍交给父级列表。</summary>
    internal sealed class LibraryRowFocus : MonoBehaviour, ISelectHandler
    {
        public ScrollRect Scroll;
        public float RowTop;
        public float RowHeight;

        public void OnSelect(BaseEventData eventData)
        {
            float visibleHeight = Scroll.viewport.rect.height;
            float hiddenHeight = Scroll.content.rect.height - visibleHeight;
            if (hiddenHeight <= 0f || visibleHeight < RowHeight) return;
            float offset = (1f - Scroll.verticalNormalizedPosition) * hiddenHeight;
            float nextOffset = Mathf.Clamp(offset, RowTop + RowHeight - visibleHeight, RowTop);
            Scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(nextOffset / hiddenHeight);
        }
    }
}
