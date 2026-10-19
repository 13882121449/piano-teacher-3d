using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 曲库服务（对应方案 6.4「曲目导入与曲库」、9.1「<c>SongLibraryService</c>：导入、曲目元数据、
    /// 最近列表和文件失效处理」、Sprint 4）。
    /// <para>
    /// 职责边界：只负责文件校验、复制、元数据与最近列表持久化、路径失效判断；
    /// <b>不</b>负责解析算法（交给 <see cref="MidiParser"/> / <see cref="MidiFileValidator"/>）、
    /// <b>不</b>负责 UI 与播放（交给会话层与 UI）。
    /// </para>
    /// <para>
    /// 数据分离（方案 9.2）：只读示例曲位于 <c>StreamingAssets/Songs</c>；用户导入的副本与最近列表
    /// 位于 <c>Application.persistentDataPath/PianoTeacher3D/</c>，<b>用户数据绝不写回 StreamingAssets</b>。
    /// </para>
    /// <para>
    /// 纯 C# 类（不继承 MonoBehaviour、不依赖场景），构造函数只做目录初始化，首次写入时才创建目录，
    /// 因此可在 EditMode 下直接单测。
    /// </para>
    /// </summary>
    public sealed class SongLibraryService
    {
        /// <summary>最近曲目上限（方案 6.4「最近打开列表只保存必要元数据」）。</summary>
        public const int MaxRecentSongs = 20;

        private const string ProductFolderName = "PianoTeacher3D";
        private const string LibraryFileName = "library.json";

        private readonly string _productRoot;
        private readonly string _userSongDirectory;
        private readonly string _builtInSongDirectory;
        private readonly string _libraryPath;
        private readonly List<SongMetadata> _recent = new List<SongMetadata>();

        public SongLibraryService()
        {
            _productRoot = Path.Combine(SafePersistentRoot(), ProductFolderName);
            _userSongDirectory = Path.Combine(_productRoot, "Songs");
            _builtInSongDirectory = Path.Combine(SafeStreamingRoot(), "Songs");
            _libraryPath = Path.Combine(_productRoot, LibraryFileName);
            Load();
        }

        /// <summary>内置示例曲目录（<c>StreamingAssets/Songs</c>，只读）。</summary>
        public string BuiltInSongDirectory => _builtInSongDirectory;

        /// <summary>用户导入曲目目录（<c>persistentDataPath/PianoTeacher3D/Songs</c>，可写、可删除）。</summary>
        public string UserSongDirectory => _userSongDirectory;

        /// <summary>最近曲目（最新在前，最多 <see cref="MaxRecentSongs"/> 条）。</summary>
        public IReadOnlyList<SongMetadata> RecentSongs => _recent;

        // ==================== 列举与校验 ====================

        /// <summary>列举内置示例曲（只读，按路径排序；目录缺失或不可读时返回空列表而非抛异常）。</summary>
        public IReadOnlyList<string> ListBuiltInSongs()
        {
            var result = new List<string>();
            try
            {
                if (!Directory.Exists(_builtInSongDirectory)) return result;
                var files = Directory.GetFiles(_builtInSongDirectory);
                if (files == null) return result;
                for (int i = 0; i < files.Length; i++)
                {
                    if (MidiFileValidator.HasMidiExtension(files[i])) result.Add(files[i]);
                }
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                result.Clear();
            }
            return result;
        }

        /// <summary>只校验不落盘（方案 6.4）。</summary>
        public ImportCheck Validate(string path) => MidiFileValidator.Validate(path);

        // ==================== 导入 ====================

        /// <summary>
        /// 导入事务：校验 → 复制到用户目录 → 生成元数据 → 写入最近曲目 → 落盘。
        /// 任一步失败都返回可恢复错误（不抛异常、不产生半初始化状态，方案 6.2 / 6.4）。
        /// <para>
        /// <paramref name="prevalidated"/> 供「后台已校验/解析过」的调用方复用结果（优化方案 §7-8），
        /// 避免同一文件在主线程上再解析一遍；为空时按原逻辑自行校验。
        /// </para>
        /// </summary>
        public ImportResult Import(string path, ImportCheck? prevalidated = null)
        {
            var check = prevalidated ?? MidiFileValidator.Validate(path);
            if (!check.Ok)
                return new ImportResult { Ok = false, Meta = null, Check = check };

            var parse = check.Parse;
            string id = StableId(path);
            string destination = Path.Combine(_userSongDirectory, BuildCopyFileName(path, id));

            // 源就是目标（重复导入同一份用户副本）时无需复制
            if (!PathsEqual(path, destination))
            {
                try
                {
                    EnsureUserSongDirectory();
                }
                catch (Exception ex)
                {
                    return CopyFailure(id, destination, ImportRejection.Unreadable,
                        "无法创建用户曲目目录。请检查磁盘空间与目录权限后重试。",
                        $"创建目录失败：{ex.GetType().Name}");
                }

                // 先写同目录临时文件，校验大小后原子替换（优化方案 P1-10）
                if (!AtomicFileWrite.TryCopyFile(path, destination, out string copyError))
                {
                    return CopyFailure(id, destination, ImportRejection.Unreadable,
                        "复制曲目失败（可能是磁盘空间不足或目标文件被占用）。请清理磁盘空间或关闭占用该目录的程序后重试。",
                        $"复制失败：{PathSanitizer.SanitizeMessage(copyError)}");
                }
            }

            SongMetadata meta;
            try
            {
                meta = BuildMetadata(path, destination, id, parse);
            }
            catch (Exception ex)
            {
                return CopyFailure(id, destination, ImportRejection.Unreadable,
                    "曲目信息整理失败，未能加入曲库。请重试一次；若仍然失败，请换一个 MIDI 文件。",
                    $"生成元数据失败：{ex.GetType().Name}");
            }

            UpsertRecent(meta);
            Save();

            return new ImportResult { Ok = true, Meta = meta, Check = check };
        }

        // ==================== 解析路径（文件被移动 / 删除时不崩溃） ====================

        /// <summary>
        /// 解析曲目的可用路径。文件被移动或删除时返回 <paramref name="problem"/>（含下一步动作），
        /// <b>不抛异常</b>、<b>不自动删除</b>最近记录（UI 可据 <paramref name="problem"/> 标出失效项）。
        /// </summary>
        public bool TryResolve(SongMetadata meta, out string resolvedPath, out string problem)
        {
            resolvedPath = null;
            problem = null;

            if (meta == null)
            {
                problem = "曲目信息不完整，请重新导入文件。";
                return false;
            }

            string path = meta.SourcePath;
            if (string.IsNullOrEmpty(path))
            {
                problem = "该曲目没有可用的文件路径，请重新导入文件。";
                return false;
            }

            bool exists = false;
            try { exists = File.Exists(path); }
            catch (Exception) { exists = false; }

            if (!exists)
            {
                // 这里只判断「位置是否失效」，与「格式错误」区分：格式问题由 Validate / Import 报告。
                problem = IsUserImported(meta)
                    ? "文件已不在原位置，可能被移动、重命名或删除。请重新点击「导入 MIDI」选择它的新位置，或移除这条记录。"
                    : "示例曲文件缺失，可能被移动或删除。请重新导入一个 MIDI 文件继续练习。";
                return false;
            }

            // 文件在，但可能暂时不可读（被占用 / 无权限）
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (!fs.CanRead)
                    {
                        problem = "文件存在但无法读取。请检查文件权限后重试。";
                        return false;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                problem = "文件存在但没有读取权限。请以有权限的账户运行，或先把文件复制到「文档」后再导入。";
                return false;
            }
            catch (IOException)
            {
                problem = "文件正被其他程序占用，暂时无法读取。请关闭占用它的程序后重试。";
                return false;
            }
            catch (Exception)
            {
                problem = "文件存在但无法读取，可能已损坏。请检查文件后重试。";
                return false;
            }

            try { resolvedPath = Path.GetFullPath(path); }
            catch (Exception) { resolvedPath = path; }
            return true;
        }

        /// <summary>按 ID 或路径解析最近曲目，再走 <c>TryResolve</c>。</summary>
        public bool TryResolveRecent(string idOrPath, out SongMetadata meta, out string resolvedPath, out string problem)
        {
            meta = null;
            resolvedPath = null;
            problem = null;

            if (string.IsNullOrEmpty(idOrPath))
            {
                problem = "没有指定要打开的曲目，请从曲库或最近列表中选择一首。";
                return false;
            }

            var found = FindByIdOrPath(idOrPath);
            if (found == null)
            {
                problem = "最近列表中没有找到这首曲目，请重新选择文件。";
                return false;
            }

            meta = found;
            return TryResolve(found, out resolvedPath, out problem);
        }

        // ==================== 移除与清理（方案 8.4「支持删除与清除缓存」） ====================

        /// <summary>从最近列表移除该曲目，并删除用户目录内的副本（内置示例曲不会被删除）。</summary>
        public void Forget(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            var found = FindByIdOrPath(id);
            if (found != null && IsUserImported(found))
                TryDeleteUserFile(found.SourcePath);

            _recent.RemoveAll(m => m == null
                || string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(m.SourcePath) && PathsEqual(m.SourcePath, id)));

            Save();
        }

        /// <summary>清空最近曲目（不删除用户目录中的副本）。</summary>
        public void ClearRecent()
        {
            _recent.Clear();
            Save();
        }

        /// <summary>清除用户导入副本与最近记录（内置示例曲不受影响）。</summary>
        public void ClearCache()
        {
            DeleteUserDirectoryContents();
            _recent.Clear();
            Save();
        }

        // ==================== 记录与持久化 ====================

        /// <summary>记录一次播放：把该曲目移到最近列表最前（去重、上限 20）并落盘。</summary>
        public void RecordPlayed(in SongMetadata meta)
        {
            if (meta == null) return;
            SongMetadata copy = meta;
            if (string.IsNullOrEmpty(copy.Id)) copy.Id = StableId(copy.SourcePath);
            UpsertRecent(copy);
            Save();
        }

        /// <summary>
        /// 持久化最近曲目（幂等；失败只忽略本次写入，不抛异常）。
        /// 采用「临时文件 + 原子替换」，并在替换前保留上一版为 <c>library.json.bak</c>（优化方案 P1-10）。
        /// </summary>
        public void Save()
        {
            try
            {
                EnsureProductDirectory();
                var file = new LibraryFile { Version = 1, Recent = new List<SongMetadata>(_recent) };
                AtomicFileWrite.TryWriteAllText(_libraryPath, JsonUtility.ToJson(file, true), out _);
            }
            catch (Exception)
            {
                // 持久化失败不影响本次运行；下次成功保存时再写（方案 8.4 最小保留原则）
            }
        }

        /// <summary>
        /// 读取最近曲目（缺失 / 损坏时保持空列表，不抛异常）。
        /// 主文件损坏时回退最近一次有效备份 <c>library.json.bak</c>（优化方案 P1-10「必要时保留最近有效备份」）。
        /// </summary>
        public void Load()
        {
            _recent.Clear();
            if (TryLoadFrom(_libraryPath)) return;
            if (TryLoadFrom(_libraryPath + AtomicFileWrite.BackupSuffix)) return;
            _recent.Clear();
        }

        /// <summary>从指定文件读取最近曲目；文件缺失、为空或解析失败返回 false。</summary>
        private bool TryLoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return false;

                var file = JsonUtility.FromJson<LibraryFile>(json);
                if (file == null || file.Recent == null) return false;

                _recent.Clear();
                for (int i = 0; i < file.Recent.Count; i++)
                {
                    var meta = file.Recent[i];
                    if (meta == null || string.IsNullOrEmpty(meta.Id)) continue;
                    _recent.Add(meta);
                }
                TrimRecent();
                return true; // 解析成功即视为有效（空列表也是合法状态，不能因此回退到旧备份）
            }
            catch (Exception)
            {
                _recent.Clear();
                return false;
            }
        }

        /// <summary>该曲目是否位于用户目录（即由用户导入的副本）。</summary>
        public bool IsUserImported(in SongMetadata meta)
        {
            if (meta == null) return false;
            return IsInsideUserDirectory(meta.SourcePath);
        }

        // ==================== 内部：路径与安全 ====================

        /// <summary>稳定 ID：对源路径做哈希（FNV-1a 64），与运行进程 / 哈希随机化无关。</summary>
        public static string StableId(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return "0000000000000000";

            string normalized;
            try { normalized = Path.GetFullPath(sourcePath); }
            catch (Exception) { normalized = sourcePath; }
            normalized = normalized.Replace('\\', '/').ToLowerInvariant();

            byte[] bytes = Encoding.UTF8.GetBytes(normalized);
            ulong hash = 14695981039346656037UL; // FNV-1a 64 位偏移基准
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }

        private static string BuildCopyFileName(string sourcePath, string id)
        {
            string extension = SafeExtension(sourcePath);
            string baseName = SafeBaseName(sourcePath);
            return $"{baseName}_{id}{extension}";
        }

        private static string SafeExtension(string path)
        {
            string ext = "";
            try { ext = Path.GetExtension(path) ?? ""; }
            catch (ArgumentException) { ext = ""; }

            if (string.IsNullOrEmpty(ext)) return ".mid";
            return string.Equals(ext, ".midi", StringComparison.OrdinalIgnoreCase) ? ".midi" : ".mid";
        }

        /// <summary>把原始文件名压成安全文件名（保留中文，替换 Windows 非法字符，截断防超长路径）。</summary>
        private static string SafeBaseName(string path)
        {
            string name = "";
            try { name = Path.GetFileNameWithoutExtension(path) ?? ""; }
            catch (Exception) { name = ""; }
            if (string.IsNullOrEmpty(name)) return "song";

            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                // 保留 ASCII 字母数字与中文等非 ASCII 字符；其余（空格、<>:"/\|?* 等）替换为下划线
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 0x7F) sb.Append(c);
                else sb.Append('_');
            }

            string safe = sb.ToString().Trim('_');
            if (string.IsNullOrEmpty(safe)) safe = "song";
            if (safe.Length > 40) safe = safe.Substring(0, 40);
            return safe;
        }

        /// <summary>路径规范化后判断是否位于用户曲目目录之下（防目录穿越，含 <c>..</c>）。</summary>
        private bool IsInsideUserDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetFullPath(_userSongDirectory);
                if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    root += Path.DirectorySeparatorChar;
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>只允许删除用户曲目目录内的文件（保护性检查，失败静默）。</summary>
        private bool TryDeleteUserFile(string path)
        {
            if (!IsInsideUserDirectory(path)) return false;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void DeleteUserDirectoryContents()
        {
            try
            {
                if (!Directory.Exists(_userSongDirectory)) return;
                var files = Directory.GetFiles(_userSongDirectory);
                if (files == null) return;
                for (int i = 0; i < files.Length; i++) TryDeleteUserFile(files[i]);
            }
            catch (Exception)
            {
                // 清理失败不影响内存状态与后续使用
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        // ==================== 内部：目录与数据 ====================

        private static string SafePersistentRoot()
        {
            try
            {
                string root = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(root)) return root;
            }
            catch (Exception) { /* 极端环境下回落到临时目录 */ }
            return Path.GetTempPath();
        }

        private static string SafeStreamingRoot()
        {
            try
            {
                string root = Application.streamingAssetsPath;
                if (!string.IsNullOrEmpty(root)) return root;
            }
            catch (Exception) { /* 忽略 */ }
            return "";
        }

        private void EnsureProductDirectory()
        {
            if (!Directory.Exists(_productRoot)) Directory.CreateDirectory(_productRoot);
        }

        private void EnsureUserSongDirectory()
        {
            if (!Directory.Exists(_userSongDirectory)) Directory.CreateDirectory(_userSongDirectory);
        }

        private SongMetadata BuildMetadata(string sourcePath, string destination, string id, MidiParseResult parse)
        {
            long size = 0;
            try
            {
                var info = new FileInfo(destination);
                if (info.Exists) size = info.Length;
            }
            catch (Exception) { size = 0; }

            string title = "";
            try { title = Path.GetFileNameWithoutExtension(sourcePath) ?? ""; }
            catch (Exception) { title = ""; }
            if (string.IsNullOrEmpty(title)) title = "未命名曲目";

            return new SongMetadata
            {
                Id = id,
                Title = title,
                SourcePath = destination,
                DurationSeconds = parse.TotalDurationSeconds,
                NoteCount = parse.Notes != null ? parse.Notes.Count : 0,
                TrackCount = parse.TrackCount,
                TempoChangeCount = parse.TempoChanges != null ? parse.TempoChanges.Count : 0,
                MidiFormat = parse.MidiFormat,
                FileSizeBytes = size,
                DanglingNoteCount = parse.DanglingNoteCount,
                ImportedAtIso = DateTime.UtcNow.ToString("o")
            };
        }

        private ImportResult CopyFailure(string id, string destination, ImportRejection reason,
            string userMessage, string detail)
        {
            return new ImportResult
            {
                Ok = false,
                Meta = null,
                Check = ImportCheck.Failure(reason, userMessage,
                    $"{detail}：{PathSanitizer.Sanitize(destination)}")
            };
        }

        private void UpsertRecent(SongMetadata meta)
        {
            if (meta == null) return;
            _recent.RemoveAll(m => m != null && string.Equals(m.Id, meta.Id, StringComparison.Ordinal));
            _recent.Insert(0, meta);
            TrimRecent();
        }

        private void TrimRecent()
        {
            while (_recent.Count > MaxRecentSongs)
                _recent.RemoveAt(_recent.Count - 1);
        }

        private SongMetadata FindByIdOrPath(string idOrPath)
        {
            for (int i = 0; i < _recent.Count; i++)
            {
                var meta = _recent[i];
                if (meta == null) continue;
                if (string.Equals(meta.Id, idOrPath, StringComparison.OrdinalIgnoreCase)) return meta;
                if (!string.IsNullOrEmpty(meta.SourcePath) && PathsEqual(meta.SourcePath, idOrPath)) return meta;
            }
            return null;
        }

        /// <summary>最近曲目的持久化包装（<c>JsonUtility</c> 不支持顶层列表，故包一层）。</summary>
        [Serializable]
        private sealed class LibraryFile
        {
            public int Version = 1;
            public List<SongMetadata> Recent = new List<SongMetadata>();
        }
    }
}
