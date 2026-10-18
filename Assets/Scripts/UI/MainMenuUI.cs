using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 主菜单（对应方案 3.1 曲库入口、6.4「导入 MIDI / 最近打开列表」，
    /// 审查 2.1 视觉层级、审查 4 章「统一曲目卡片、主操作与错误状态风格」）。
    /// <para>
    /// 结构：标题 → 曲库（统一卡片 + 导入 MIDI 入口）→ 本次运行的最近播放（含失效提示）
    /// → 底部快捷键说明。无曲目时显示空状态，曲库目录缺失时显示错误态并提供导入入口，
    /// 两种情况都不会退化成一行红色报错文字。
    /// </para>
    /// <para>
    /// 选中曲目通过静态字段 <see cref="SelectedSongPath"/> 传给课程场景（原有跨场景通道，保持不变）。
    /// </para>
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        /// <summary>由主菜单选中的曲目路径（课程场景读取，保持原有契约）。</summary>
        public static string SelectedSongPath = "";

        private const float CardHeight = 60f;
        private const float CardStep = 68f;
        private const float RecentRowHeight = 48f;
        private const float RecentRowStep = 54f;
        private const int MaxLibraryCards = 3;
        private const int MaxRecentRows = 2;
        private const float RemoveConfirmSeconds = 4f;

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

        private void Start()
        {
            BuildUI();
        }

        private void Update()
        {
            // 「移除最近曲目」的二次确认超时复位（破坏性操作需要确认，方案 3.4 / 审查 2.2B）
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

            // 标题区
            var title = UITheme.CreateText(_canvas.transform, "钢琴 3D 教学", Vector2.zero, new Vector2(600f, 52f),
                UITheme.FontPageTitle, UITheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.StretchTop(title.rectTransform, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Space24, 52f);

            var subtitle = UITheme.CreateText(_canvas.transform, "选择曲目开始练习，或导入自己的 MIDI 文件",
                Vector2.zero, new Vector2(600f, 26f), UITheme.FontSmall, UITheme.TextSecondary);
            UITheme.StretchTop(subtitle.rectTransform, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Space24 + 56f, 26f);

            _content = UITheme.CreateRect(_canvas.transform, "Content", Vector2.zero, new Vector2(640f, 452f),
                UITheme.Transparent);
            UITheme.PlaceCenter(_content, 640f, 452f);

            BuildLibrarySection();
            BuildRecentSection();
            BuildFooter();
        }

        // ---------- 曲库（统一卡片 + 导入入口 + 空 / 错误状态） ----------

        private void BuildLibrarySection()
        {
            var header = UITheme.CreateText(_content, "曲库", Vector2.zero, new Vector2(300f, 24f),
                UITheme.FontSmall, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(header.rectTransform, 0f, 0f, 300f, 24f);

            var importBtn = UITheme.CreateButton(_content, "导入 MIDI…", Vector2.zero, new Vector2(148f, 28f),
                RequestImport, UITheme.FontCaption, ButtonStyle.Primary,
                "选择本机 .mid / .midi 文件导入后直接进入课程（不必手动复制到工程目录）");
            UITheme.PlaceTopRight(importBtn.GetComponent<RectTransform>(), 0f, 0f, 148f, 28f);

            string dir = SongsDirectory;
            bool dirMissing = !SafeDirectoryExists(dir);
            var songs = ListSongs(dir);

            if (dirMissing)
            {
                BuildErrorState(dir);
                return;
            }
            if (songs.Count == 0)
            {
                BuildEmptyState();
                return;
            }

            header.text = $"曲库（{songs.Count} 首）";
            int shown = Mathf.Min(songs.Count, MaxLibraryCards);
            for (int i = 0; i < shown; i++)
            {
                BuildSongCard(songs[i], i);
            }

            if (songs.Count > shown)
            {
                var more = UITheme.CreateText(_content,
                    $"还有 {songs.Count - shown} 首未显示（可滚动查看：StreamingAssets/Songs）",
                    Vector2.zero, new Vector2(600f, 22f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                    TextAnchor.MiddleLeft);
                UITheme.PlaceTopLeft(more.rectTransform, 0f, 34f + shown * CardStep, 600f, 22f);
            }
        }

        /// <summary>统一风格的曲目卡片：标题 + 文件大小 + 「开始学习」提示。</summary>
        private void BuildSongCard(string path, int index)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var card = UITheme.CreateButton(_content, name, Vector2.zero, new Vector2(640f, CardHeight),
                () => LoadLesson(path), UITheme.FontButton, ButtonStyle.Secondary,
                $"开始学习《{name}》");
            UITheme.PlaceTopLeft(card.GetComponent<RectTransform>(), 0f, 34f + index * CardStep, 640f, CardHeight);

            var label = UITheme.GetLabel(card);
            if (label != null)
            {
                label.alignment = TextAnchor.MiddleLeft;
                label.fontStyle = FontStyle.Bold;
                UITheme.StretchAll(label.rectTransform, 18f, 160f, 22f, 6f);
            }

            var startHint = UITheme.CreateText(card.transform, "点击开始学习 ▸", Vector2.zero,
                new Vector2(140f, 20f), UITheme.FontStatusLabel, UITheme.TextSecondary, TextAnchor.MiddleRight);
            UITheme.StretchAll(startHint.rectTransform, 460f, 18f, 22f, 6f);

            var meta = UITheme.CreateText(card.transform, DescribeSongMeta(path), Vector2.zero,
                new Vector2(560f, 20f), UITheme.FontStatusLabel, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            UITheme.StretchAll(meta.rectTransform, 18f, 18f, 4f, 30f);
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
                return $"{size}　·　示例曲库：StreamingAssets/Songs";
            }
            catch (Exception)
            {
                return "示例曲库：StreamingAssets/Songs";
            }
        }

        /// <summary>空状态：给出「导入 MIDI」与目录说明，而不是一行报错文字（方案 8.2）。</summary>
        private void BuildEmptyState()
        {
            var panel = UITheme.CreatePanel(_content, "EmptyState", Vector2.zero, new Vector2(640f, 168f),
                UITheme.PanelBgRaised, UITheme.RadiusLarge);
            UITheme.PlaceTopLeft(panel, 0f, 34f, 640f, 168f);

            var title = UITheme.CreateText(panel, "曲库为空", Vector2.zero, new Vector2(600f, 26f),
                UITheme.FontSongTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(title.rectTransform, 20f, 16f, 600f, 26f);

            var body = UITheme.CreateText(panel,
                "还没有可练习的曲目。可点上方「导入 MIDI…」从本机选择 .mid / .midi 文件，\n" +
                "或把示例文件放入 StreamingAssets/Songs 目录后重新打开本界面。",
                Vector2.zero, new Vector2(600f, 52f), UITheme.FontCaption, UITheme.TextSecondary,
                TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(body.rectTransform, 20f, 48f, 600f, 52f);

            var importBtn = UITheme.CreateButton(panel, "导入 MIDI…", Vector2.zero, new Vector2(150f, 32f),
                RequestImport, UITheme.FontCaption, ButtonStyle.Primary, "选择本机 MIDI 文件导入");
            UITheme.Place(importBtn.GetComponent<RectTransform>(), 20f, 14f, 150f, 32f);
        }

        /// <summary>错误态：目录缺失（可恢复）→ 说明原因 + 下一步动作 + 导入入口，统一错误色与「!」标记。</summary>
        private void BuildErrorState(string directory)
        {
            var panel = UITheme.CreatePanel(_content, "LibraryError", Vector2.zero, new Vector2(640f, 168f),
                UITheme.ErrorBannerColor(Core.ErrorSeverity.Recoverable), UITheme.RadiusLarge);
            UITheme.PlaceTopLeft(panel, 0f, 34f, 640f, 168f);

            var tag = UITheme.CreateText(panel, "! 曲库不可用", Vector2.zero, new Vector2(200f, 24f),
                UITheme.FontCaption, UITheme.TextError, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(tag.rectTransform, 20f, 16f, 200f, 24f);

            var body = UITheme.CreateText(panel,
                "没有找到示例曲目录 StreamingAssets/Songs。\n" +
                "这不影响导入自己的文件：点下方「导入 MIDI…」选择本机的 .mid / .midi 文件即可开始练习。",
                Vector2.zero, new Vector2(600f, 56f), UITheme.FontCaption, UITheme.TextPrimary,
                TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(body.rectTransform, 20f, 44f, 600f, 56f);

            var importBtn = UITheme.CreateButton(panel, "导入 MIDI…", Vector2.zero, new Vector2(150f, 32f),
                RequestImport, UITheme.FontCaption, ButtonStyle.Primary, "选择本机 MIDI 文件导入");
            UITheme.Place(importBtn.GetComponent<RectTransform>(), 20f, 14f, 150f, 32f);

            Debug.LogWarning($"[PianoTeacher3D][UI] 曲库目录缺失：{Core.PathSanitizer.Sanitize(directory)}");
        }

        // ---------- 最近播放（含失效提示） ----------

        private void BuildRecentSection()
        {
            var divider = UITheme.CreateDivider(_content, "Divider", Vector2.zero, new Vector2(640f, 1f));
            UITheme.StretchTop(divider, 0f, 0f, 268f, 1f);

            var header = UITheme.CreateText(_content, "最近播放（本次运行）", Vector2.zero, new Vector2(400f, 24f),
                UITheme.FontCaption, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(header.rectTransform, 0f, 280f, 400f, 24f);

            int shown = 0;
            for (int i = 0; i < RecentPaths.Count && shown < MaxRecentRows; i++)
            {
                string path = RecentPaths[i];
                bool missing = !SafeFileExists(path);
                BuildRecentRow(path, missing, shown);
                shown++;
            }

            if (shown == 0)
            {
                var empty = UITheme.CreateText(_content,
                    "本次运行还没有从本菜单进入过曲目。从上方曲库选择一首，或导入自己的 MIDI 文件。",
                    Vector2.zero, new Vector2(620f, 22f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                    TextAnchor.MiddleLeft);
                UITheme.PlaceTopLeft(empty.rectTransform, 0f, 310f, 620f, 22f);
            }
        }

        private void BuildRecentRow(string path, bool missing, int index)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            float top = 310f + index * RecentRowStep;

            if (!missing)
            {
                var row = UITheme.CreateButton(_content, name, Vector2.zero, new Vector2(640f, RecentRowHeight),
                    () => LoadLesson(path), UITheme.FontCaption, ButtonStyle.Secondary,
                    $"再次打开《{name}》");
                UITheme.PlaceTopLeft(row.GetComponent<RectTransform>(), 0f, top, 640f, RecentRowHeight);
                var label = UITheme.GetLabel(row);
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleLeft;
                    UITheme.StretchAll(label.rectTransform, 16f, 150f, 0f, 0f);
                }
                var meta = UITheme.CreateText(row.transform, "可直接打开", Vector2.zero, new Vector2(130f, 20f),
                    UITheme.FontStatusLabel, UITheme.TextSecondary, TextAnchor.MiddleRight);
                UITheme.StretchAll(meta.rectTransform, 460f, 16f, 0f, 0f);
                return;
            }

            // 文件被移动 / 删除：给出失效提示 + 重新导入入口，并允许移除该条记录（危险色 + 二次确认）
            var panel = UITheme.CreatePanel(_content, "RecentMissing", Vector2.zero,
                new Vector2(640f, RecentRowHeight), UITheme.PanelBgRaised, UITheme.RadiusMedium);
            UITheme.PlaceTopLeft(panel, 0f, top, 640f, RecentRowHeight);

            var title = UITheme.CreateText(panel, "! " + name, Vector2.zero, new Vector2(340f, 20f),
                UITheme.FontCaption, UITheme.TextWarning, TextAnchor.MiddleLeft);
            UITheme.StretchAll(title.rectTransform, 16f, 296f, 22f, 6f);

            var hint = UITheme.CreateText(panel, "文件已移动或删除：请点「导入 MIDI…」重新选择文件",
                Vector2.zero, new Vector2(340f, 18f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                TextAnchor.MiddleLeft);
            UITheme.StretchAll(hint.rectTransform, 16f, 296f, 4f, 26f);

            var reimportBtn = UITheme.CreateButton(panel, "重新导入", Vector2.zero, new Vector2(88f, 28f),
                RequestImport, UITheme.FontStatusLabel, ButtonStyle.Secondary, "重新选择本机 MIDI 文件");
            UITheme.PlaceRight(reimportBtn.GetComponent<RectTransform>(), 104f, 10f, 88f, 28f);

            var removeBtn = UITheme.CreateButton(panel, "移除", Vector2.zero, new Vector2(92f, 28f),
                null, UITheme.FontStatusLabel, ButtonStyle.Danger,
                "从最近列表移除这条记录（需再次点击确认）");
            UITheme.PlaceRight(removeBtn.GetComponent<RectTransform>(), 8f, 10f, 92f, 28f);
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
            var footer = UITheme.CreateText(_canvas.transform,
                "进入课程后可用：空格 播放/暂停　·　[ 重播　·　R 重置视角　·　H 高亮　·　M 节拍器　·　Tab 曲目分析　·　F1 帮助",
                Vector2.zero, new Vector2(900f, 24f), UITheme.FontStatusLabel, UITheme.TextSecondary);
            UITheme.StretchX(footer.rectTransform, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin, 24f);
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
