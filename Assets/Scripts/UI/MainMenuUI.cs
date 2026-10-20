using System;
using System.Collections.Generic;
using System.IO;
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

        private void Start()
        {
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
        }

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            _canvas = UITheme.CreateCanvas(transform, "MainMenuCanvas");

            // ---- 标题区：28 hero + 12 副标题（居中，与内容区同一条中轴）----
            var title = UIKit.Label(_canvas.transform, "钢琴 3D 教学", UITheme.FontPageTitle, UITheme.TextPrimary,
                TextAnchor.MiddleCenter, FontStyle.Bold, "PageTitle");
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.StretchTop(title.rectTransform, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Space24, 38f);

            var subtitle = UIKit.Label(_canvas.transform, "选择曲目开始练习，或导入自己的 MIDI 文件",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleCenter, FontStyle.Normal,
                "PageSubtitle");
            subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.StretchTop(subtitle.rectTransform, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Space24 + 42f, 16f);

            _content = UITheme.CreateRect(_canvas.transform, "Content", Vector2.zero, Vector2.zero,
                UITheme.Transparent);
            UITheme.StretchAll(_content, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                ContentBottom, ContentTop);

            BuildLibrarySection();
            BuildRecentSection();
            BuildFooter();
        }

        // ---------- 曲库：统一卡片网格 + 导入入口 + 空 / 错误状态 ----------

        private void BuildLibrarySection()
        {
            var header = UIKit.SectionHeader(_content, "曲库");
            UITheme.PlaceTopLeft(header.rectTransform, 0f, 10f, 300f, 20f);

            var importBtn = UIKit.PrimaryButton(_content, "导入 MIDI…", new Vector2(148f, 40f),
                RequestImport, "选择本机 .mid / .midi 文件导入后直接进入课程（不必手动复制到工程目录）");
            UITheme.PlaceTopRight(importBtn.GetComponent<RectTransform>(), 0f, 0f, 148f, 40f);

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
                    leftOffset + col * (cardW + gap), GridTopGap + row * (cardH + gap), cardW, cardH);
            }

            int rows = Mathf.CeilToInt(shown / (float)columns);
            float gridBottom = GridTopGap + rows * cardH + Mathf.Max(0, rows - 1) * gap;

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
            return GridTopGap + rows * UITheme.Layout.SongCardHeight + (rows - 1) * gap + UITheme.Space24;
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
