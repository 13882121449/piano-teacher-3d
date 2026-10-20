using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 应用内 MIDI 文件选择器（对应方案 6.4「提供『导入 MIDI』按钮，允许通过文件选择器选择
    /// .mid/.midi，不要求用户手动复制到项目目录」、Sprint 4）。
    /// <para>
    /// 约束：Unity 没有跨平台原生文件对话框，且本方案要求「零第三方包 / 离线可用」，
    /// 因此这里用项目现有的 UGUI 程序化控件自建选择器，<b>不使用</b> <c>System.Windows.Forms</c>
    /// 或 <c>OpenFileDialog</c>，也不新增任何包。
    /// </para>
    /// <para>
    /// 只依赖 <see cref="UITheme"/> 的既有公开 API（画布 / 面板 / 文本 / 按钮 / 锚点布局助手 / 令牌）。
    /// 显示用文本一律来自会话层，本组件不做业务判断。
    /// </para>
    /// </summary>
    public class FileBrowserUI : MonoBehaviour
    {
        /// <summary>单个目录最多列出的条目数（超出则截断并提示，避免遍历超长目录）。</summary>
        private const int MaxEntriesPerDirectory = 300;

        /// <summary>每页显示的行数（无滚动条，用分页替代，避免额外的 UGUI 组件依赖）。</summary>
        private const int RowsPerPage = 9;

        private const float RowHeight = 32f;
        private const float RowStep = 36f;

        /// <summary>双击判定窗口（秒）。</summary>
        private const float DoubleClickSeconds = 0.4f;

        private const int CanvasSortingOrder = 600;

        private static FileBrowserUI _instance;

        private struct Entry
        {
            public string Path;
            public string Name;
            public bool IsDirectory;
            public long Size;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        private Action<string> _onPick;
        private Action _onCancel;
        private bool _open;
        private bool _finished;

        private RectTransform _listHost;
        private Text _pathText;
        private Text _messageText;
        private Text _selectionText;
        private Button _prevPageBtn;
        private Button _nextPageBtn;
        private Button _showAllBtn;

        private string _directory;        // null = 驱动器列表
        private int _page;
        private string _selectedFile;
        private bool _showAllFiles;
        private bool _truncated;
        private int _lastClickIndex = -1;
        private float _lastClickTime;

        /// <summary>选择器当前是否打开。</summary>
        public bool IsOpen => _open;

        // ==================== 打开 / 关闭 ====================

        /// <summary>
        /// 打开选择器。<paramref name="onPick"/> 与 <paramref name="onCancel"/> 互斥，且只会回调一次。
        /// 同一时间只允许存在一个选择器（再次打开会先取消上一个）。
        /// </summary>
        public static FileBrowserUI Show(string startDirectory, Action<string> onPick, Action onCancel)
        {
            CloseAll();

            var go = new GameObject("FileBrowserUI");
            var ui = go.AddComponent<FileBrowserUI>();
            ui._onPick = onPick;
            ui._onCancel = onCancel;
            ui._open = true;
            ui._finished = false;
            ui._directory = string.IsNullOrEmpty(startDirectory) ? null : startDirectory;
            ui._showAllFiles = false;
            ui._page = 0;
            ui._selectedFile = null;
            ui._lastClickIndex = -1;

            ui.BuildUI();
            _instance = ui;
            ui.OpenDirectory(ui._directory);
            return ui;
        }

        /// <summary>关闭当前选择器（若存在），不触发选择回调。</summary>
        public static void CloseAll()
        {
            var inst = _instance;
            _instance = null;
            if (inst != null) inst.Finish(null);
        }

        private void Finish(string pickedPath)
        {
            if (_finished) return;
            _finished = true;
            _open = false;
            if (_instance == this) _instance = null;

            // 先取出并清空回调，保证「互斥、只回调一次」，也允许回调里重新打开选择器
            var pick = _onPick;
            var cancel = _onCancel;
            _onPick = null;
            _onCancel = null;

            if (!string.IsNullOrEmpty(pickedPath))
            {
                if (pick != null) pick(pickedPath);
            }
            else
            {
                if (cancel != null) cancel();
            }

            Destroy(gameObject);
        }

        private void Update()
        {
            if (!_open) return;
            if (UITheme.ConsumeEscape()) Finish(null);
        }

        // ==================== 构建界面 ====================

        private void BuildUI()
        {
            // 打开时确保 EventSystem 存在，且面板本身接收射线 → 挡住场景旋转 / 点击（方案 3.4）
            UITheme.EnsureEventSystem();
            var canvas = UITheme.CreateCanvas(transform, "FileBrowserCanvas", CanvasSortingOrder);

            // 遮罩层：铺满全屏、不透明，阻止点击穿透到场景
            var overlay = UITheme.CreatePanel(canvas.transform, "Overlay", Vector2.zero,
                new Vector2(64f, 64f), UITheme.SceneBg);
            UITheme.StretchAll(overlay, 0f, 0f, 0f, 0f);

            var panel = UITheme.CreatePanel(overlay, "Panel", Vector2.zero, new Vector2(780f, 620f),
                UITheme.PanelBg, UITheme.RadiusLarge);
            UITheme.PlaceCenter(panel, 780f, 620f);

            var title = UITheme.CreateText(panel, "选择 MIDI 文件", Vector2.zero, new Vector2(520f, 28f),
                UITheme.FontTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(title.rectTransform, 24f, 16f, 520f, 28f);

            var hint = UITheme.CreateText(panel,
                "单击选中，双击或点「选择」确认；点文件夹可进入下一级。",
                Vector2.zero, new Vector2(700f, 16f), UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(hint.rectTransform, 24f, 46f, 700f, 16f);

            _pathText = UITheme.CreateText(panel, "", Vector2.zero, new Vector2(732f, 20f),
                UITheme.FontBodySmall, UITheme.Accent, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_pathText.rectTransform, 24f, 68f, 732f, 20f);

            _listHost = UITheme.CreatePanel(panel, "List", Vector2.zero, new Vector2(732f, 352f),
                UITheme.Transparent);
            UITheme.PlaceTopLeft(_listHost, 24f, 100f, 732f, 352f);
            var listImage = _listHost.GetComponent<Image>();
            if (listImage != null) listImage.raycastTarget = false; // 列表本身不拦射线，交给行按钮

            _messageText = UITheme.CreateText(panel, "", Vector2.zero, new Vector2(560f, 20f),
                UITheme.FontBodySmall, UITheme.TextWarning, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_messageText.rectTransform, 24f, 458f, 560f, 20f);

            _selectionText = UITheme.CreateText(panel, "尚未选择文件", Vector2.zero, new Vector2(560f, 20f),
                UITheme.FontBody, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_selectionText.rectTransform, 24f, 480f, 560f, 20f);

            // 行 A：驱动器 / 上一级 / 分页
            var drivesBtn = UITheme.CreateButton(panel, "驱动器", Vector2.zero, new Vector2(104f, 32f),
                GoToDrives, UITheme.FontBodySmall, ButtonStyle.Ghost, "回到驱动器列表");
            UITheme.Place(drivesBtn.GetComponent<RectTransform>(), 24f, 70f, 104f, 32f);

            var upBtn = UITheme.CreateButton(panel, "上一级", Vector2.zero, new Vector2(104f, 32f),
                GoUp, UITheme.FontBodySmall, ButtonStyle.Ghost, "返回上一级文件夹");
            UITheme.Place(upBtn.GetComponent<RectTransform>(), 136f, 70f, 104f, 32f);

            _prevPageBtn = UITheme.CreateButton(panel, "上一页", Vector2.zero, new Vector2(100f, 32f),
                () => ChangePage(-1), UITheme.FontBodySmall, ButtonStyle.Ghost, "查看上一页");
            UITheme.Place(_prevPageBtn.GetComponent<RectTransform>(), 260f, 70f, 100f, 32f);

            _nextPageBtn = UITheme.CreateButton(panel, "下一页", Vector2.zero, new Vector2(100f, 32f),
                () => ChangePage(1), UITheme.FontBodySmall, ButtonStyle.Ghost, "查看下一页");
            UITheme.Place(_nextPageBtn.GetComponent<RectTransform>(), 368f, 70f, 100f, 32f);

            // 行 B：筛选开关 / 取消 / 选择
            _showAllBtn = UITheme.CreateButton(panel, "显示全部文件：关", Vector2.zero, new Vector2(190f, 40f),
                ToggleShowAllFiles, UITheme.FontBodySmall, ButtonStyle.Ghost,
                "默认只显示 .mid / .midi；打开后显示该文件夹的所有文件");
            UITheme.Place(_showAllBtn.GetComponent<RectTransform>(), 24f, 18f, 190f, 40f);

            var cancelBtn = UITheme.CreateButton(panel, "取消", Vector2.zero, new Vector2(140f, 40f),
                () => Finish(null), UITheme.FontSubtitle, ButtonStyle.Secondary, "关闭并返回，不导入文件");
            UITheme.PlaceRight(cancelBtn.GetComponent<RectTransform>(), 186f, 18f, 140f, 40f);

            var pickBtn = UIKit.PrimaryButton(panel, "选择", new Vector2(150f, 40f),
                Confirm, "使用已选中的 MIDI 文件");
            UITheme.PlaceRight(pickBtn.GetComponent<RectTransform>(), 24f, 18f, 150f, 40f);
        }

        // ==================== 导航与列举 ====================

        private void OpenDirectory(string directory)
        {
            _directory = directory;
            _page = 0;
            _selectedFile = null;
            _lastClickIndex = -1;
            if (_selectionText != null) _selectionText.text = "尚未选择文件";
            Reload();
        }

        private void GoToDrives() => OpenDirectory(null);

        private void GoUp()
        {
            if (string.IsNullOrEmpty(_directory)) return;

            string parent = null;
            try { parent = Path.GetDirectoryName(_directory); }
            catch (Exception) { parent = null; }

            // 驱动器根（如 C:\）没有上级 → 回到驱动器列表
            OpenDirectory(string.IsNullOrEmpty(parent) ? null : parent);
        }

        private void ChangePage(int delta)
        {
            int totalPages = Mathf.Max(1, (_entries.Count + RowsPerPage - 1) / RowsPerPage);
            int page = Mathf.Clamp(_page + delta, 0, totalPages - 1);
            if (page == _page) return;
            _page = page;
            _lastClickIndex = -1; // 换页后行号语义已变，重置双击判定
            RebuildRows();
        }

        private void ToggleShowAllFiles()
        {
            _showAllFiles = !_showAllFiles;
            UITheme.SetButtonLabel(_showAllBtn, _showAllFiles ? "显示全部文件：开" : "显示全部文件：关");
            UITheme.SetButtonActive(_showAllBtn, _showAllFiles);
            _page = 0;
            Reload();
        }

        private void Reload()
        {
            _entries.Clear();
            _truncated = false;
            string problem = null;

            if (string.IsNullOrEmpty(_directory)) LoadDrives();
            else problem = LoadDirectory(_directory);

            if (_pathText != null)
                _pathText.text = string.IsNullOrEmpty(_directory)
                    ? "此电脑（驱动器列表）"
                    : EllipsizePath(_directory, 78);

            RebuildRows();
            UpdateStatusText(problem);
        }

        private void LoadDrives()
        {
            string[] drives;
            try { drives = Directory.GetLogicalDrives(); }
            catch (Exception) { drives = new string[0]; }

            Array.Sort(drives, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < drives.Length && _entries.Count < MaxEntriesPerDirectory; i++)
                _entries.Add(new Entry { Path = drives[i], Name = drives[i], IsDirectory = true });
        }

        /// <summary>列举一个目录；返回需要展示给用户的问题文案（null 表示正常）。</summary>
        private string LoadDirectory(string directory)
        {
            string[] directories;
            string[] files;

            try
            {
                directories = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                return "没有权限访问这个文件夹。请返回上一级，或选择其他位置。";
            }
            catch (DirectoryNotFoundException)
            {
                return "这个文件夹已不存在，可能被移动或删除。请返回上一级重新选择。";
            }
            catch (IOException)
            {
                return "无法打开这个文件夹（设备可能未就绪或正被占用）。请返回上一级重新选择。";
            }
            catch (ArgumentException)
            {
                return "这个路径无效。请返回上一级或驱动器列表重新选择。";
            }
            catch (Exception)
            {
                return "无法打开这个文件夹。请返回上一级重新选择。";
            }

            try { files = Directory.GetFiles(directory); }
            catch (Exception) { files = new string[0]; }

            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < directories.Length && _entries.Count < MaxEntriesPerDirectory; i++)
            {
                string name = SafeName(directories[i]);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                _entries.Add(new Entry { Path = directories[i], Name = name, IsDirectory = true });
            }

            int visibleFiles = 0;
            for (int i = 0; i < files.Length; i++)
            {
                if (!_showAllFiles && !MidiFileValidator.HasMidiExtension(files[i])) continue;
                visibleFiles++;

                if (_entries.Count >= MaxEntriesPerDirectory) continue;
                string name = SafeName(files[i]);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;

                long size = 0;
                try { size = new FileInfo(files[i]).Length; }
                catch (Exception) { size = 0; }

                _entries.Add(new Entry { Path = files[i], Name = name, IsDirectory = false, Size = size });
            }

            _truncated = directories.Length + visibleFiles > MaxEntriesPerDirectory;
            return null;
        }

        // ==================== 列表渲染 ====================

        private void RebuildRows()
        {
            if (_listHost == null) return;

            // Unity 的 Destroy 到帧末才真正生效，先解除父子关系，避免同一帧内重复叠加旧行
            for (int i = _listHost.childCount - 1; i >= 0; i--)
            {
                var child = _listHost.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            int totalPages = Mathf.Max(1, (_entries.Count + RowsPerPage - 1) / RowsPerPage);
            _page = Mathf.Clamp(_page, 0, totalPages - 1);

            int start = _page * RowsPerPage;
            for (int row = 0; row < RowsPerPage; row++)
            {
                int index = start + row;
                if (index >= _entries.Count) break;

                Entry entry = _entries[index];
                string label = entry.IsDirectory
                    ? "▸ " + entry.Name
                    : entry.Name + "    " + FormatSize(entry.Size);

                var button = UITheme.CreateButton(_listHost, label, Vector2.zero, new Vector2(732f, RowHeight),
                    () => OnRowClicked(index), UITheme.FontBodySmall,
                    entry.IsDirectory ? ButtonStyle.Ghost : ButtonStyle.Secondary,
                    entry.IsDirectory ? "进入文件夹" : "选择这个 MIDI 文件");

                var rt = button.GetComponent<RectTransform>();
                if (rt != null) UITheme.PlaceTopLeft(rt, 0f, row * RowStep, 732f, RowHeight);

                var text = UITheme.GetLabel(button);
                if (text != null) text.alignment = TextAnchor.MiddleLeft;
            }

            if (_prevPageBtn != null) _prevPageBtn.interactable = _page > 0;
            if (_nextPageBtn != null) _nextPageBtn.interactable = _page < totalPages - 1;
        }

        private void UpdateStatusText(string problem)
        {
            if (_messageText == null) return;

            if (!string.IsNullOrEmpty(problem))
            {
                _messageText.color = UITheme.TextError;
                _messageText.text = problem;
                return;
            }

            int totalPages = Mathf.Max(1, (_entries.Count + RowsPerPage - 1) / RowsPerPage);
            string status = _entries.Count == 0
                ? "该文件夹中没有可显示的文件"
                : $"第 {_page + 1}/{totalPages} 页　·　共 {_entries.Count} 项";

            if (_truncated)
                status += $"　·　条目过多，仅列出前 {MaxEntriesPerDirectory} 项，请进入更具体的文件夹";

            _messageText.color = _truncated ? UITheme.TextWarning : UITheme.TextSecondary;
            _messageText.text = status;
        }

        // ==================== 选择与确认 ====================

        private void OnRowClicked(int index)
        {
            if (index < 0 || index >= _entries.Count) return;
            Entry entry = _entries[index];

            float now = Time.unscaledTime;
            bool doubleClick = _lastClickIndex == index && (now - _lastClickTime) <= DoubleClickSeconds;
            _lastClickIndex = index;
            _lastClickTime = now;

            if (entry.IsDirectory)
            {
                OpenDirectory(entry.Path);
                return;
            }

            _selectedFile = entry.Path;
            if (_selectionText != null)
                _selectionText.text = "已选择：" + UITheme.Truncate(entry.Name, 46);

            if (doubleClick) Confirm();
        }

        private void Confirm()
        {
            if (string.IsNullOrEmpty(_selectedFile))
            {
                if (_messageText != null)
                {
                    _messageText.color = UITheme.TextWarning;
                    _messageText.text = "请先在上方列表中选择一个 .mid 或 .midi 文件。";
                }
                return;
            }

            Finish(_selectedFile);
        }

        // ==================== 小助手 ====================

        private static string SafeName(string path)
        {
            try { return Path.GetFileName(path) ?? ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>路径过长时保留尾部（尾部信息量更大），用于路径栏显示。</summary>
        private static string EllipsizePath(string path, int maxChars)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.Length <= maxChars) return path;
            if (maxChars <= 4) return "…";
            return "…" + path.Substring(path.Length - (maxChars - 1));
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 KB";
            if (bytes >= 1024 * 1024) return $"{bytes / 1024f / 1024f:0.0} MB";
            return $"{Mathf.Max(1f, bytes / 1024f):0} KB";
        }
    }
}
