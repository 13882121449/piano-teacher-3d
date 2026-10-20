using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Customization
{
    /// <summary>
    /// 某个角色的外观记录（写入 <c>appearance.json</c>）。
    /// <para>
    /// <b>不写绝对路径、不写原图文件名</b>（方案 9.7：日志与元数据都不含能反推用户目录的信息）。
    /// 槽位 / 风格存字符串名称而不是枚举数值，避免以后枚举调整导致旧文件误读。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AppearanceRecord
    {
        /// <summary>记录格式版本。</summary>
        public int Version = 1;

        /// <summary>角色 ID（见 <see cref="CharacterIdRules"/>）。</summary>
        public string CharacterId = string.Empty;

        /// <summary>应用的槽位名称。</summary>
        public string Slot = string.Empty;

        /// <summary>生成时的角色风格名称。</summary>
        public string Style = string.Empty;

        /// <summary>保存时间（UTC，ISO 8601）。</summary>
        public string SavedAtUtc = string.Empty;

        /// <summary>生成结果宽。</summary>
        public int Width;

        /// <summary>生成结果高。</summary>
        public int Height;

        /// <summary>生成提供方 ID（不含密钥）。</summary>
        public string ProviderId = string.Empty;
    }

    /// <summary>
    /// 外观落盘（方案 9.6）：<c>Application.persistentDataPath/customization/{characterId}/</c>。
    /// <para>
    /// 目录内文件名<b>固定</b>：原图 <c>original.photo</c>（保留原始字节，格式由魔数判定）、
    /// 生成结果 <c>result.png</c>、缩略图 <c>thumbnail.png</c>、元数据 <c>appearance.json</c>。
    /// 固定文件名既避免用户文件名泄漏，也避免同名覆盖问题。
    /// </para>
    /// <para>
    /// 所有写入都走 <see cref="AtomicFileWrite"/>：先写同目录临时文件再原子替换，
    /// 断电 / 崩溃不会留下半张图；启动时用 <see cref="CleanupIncomplete"/> 清掉残留的
    /// <c>.tmp</c> 与 <c>.stage</c>（方案 9.6「下次启动时清理不完整的临时文件」）。
    /// </para>
    /// <para><b>主线程约束</b>：涉及 <see cref="Texture2D"/> 编码，必须在主线程调用。</para>
    /// </summary>
    public sealed class AppearanceStorage
    {
        /// <summary>持久化子目录名。</summary>
        public const string FolderName = "customization";

        /// <summary>原图文件名（不带图片扩展名：格式由魔数判定，不靠扩展名自证）。</summary>
        public const string OriginalFileName = "original.photo";

        /// <summary>生成结果文件名。</summary>
        public const string ResultFileName = "result.png";

        /// <summary>缩略图文件名。</summary>
        public const string ThumbnailFileName = "thumbnail.png";

        /// <summary>元数据文件名。</summary>
        public const string ManifestFileName = "appearance.json";

        /// <summary>缩略图长边。</summary>
        public const int ThumbnailSize = 256;

        /// <summary>字节写入用的中转文件后缀（写完立即删除）。</summary>
        public const string StagingSuffix = ".stage";

        private readonly string _root;

        /// <summary>构造并指定根目录（测试用临时目录，避免触碰真实用户数据）。</summary>
        public AppearanceStorage(string rootDirectory)
        {
            _root = rootDirectory ?? string.Empty;
        }

        /// <summary>默认根目录：<c>{persistentDataPath}/customization</c>（取不到时回落临时目录）。</summary>
        public static string DefaultRoot
        {
            get
            {
                string root;
                try
                {
                    root = Application.persistentDataPath;
                    if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
                }
                catch (Exception)
                {
                    root = Path.GetTempPath();
                }
                return Path.Combine(root, FolderName);
            }
        }

        /// <summary>根目录。</summary>
        public string Root => _root;

        /// <summary>取某角色的目录；角色 ID 不合法时返回 null。</summary>
        public string GetCharacterDirectory(string characterId)
        {
            if (!CharacterIdRules.Validate(characterId).Ok) return null;
            return Path.Combine(_root, characterId);
        }

        // ==================== 写入 ====================

        /// <summary>保存原图（直接复制用户选择的文件，字节不做任何改写）。</summary>
        public bool TrySaveOriginal(string characterId, string sourceFilePath, out string error)
        {
            string dir = GetCharacterDirectory(characterId);
            if (dir == null)
            {
                error = "角色 ID 不合法，无法保存照片。";
                return false;
            }

            if (string.IsNullOrEmpty(sourceFilePath))
            {
                error = "没有可保存的照片文件。";
                return false;
            }

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                error = "无法创建照片保存目录，请检查磁盘空间与权限。";
                return false;
            }

            string target = Path.Combine(dir, OriginalFileName);
            if (AtomicFileWrite.TryCopyFile(sourceFilePath, target, out string copyError))
            {
                error = string.Empty;
                return true;
            }

            error = $"保存原图失败：{copyError}。请检查磁盘空间后重试。";
            return false;
        }

        /// <summary>保存生成结果（PNG，原子写）。</summary>
        public bool TrySaveResult(string characterId, Texture2D texture, out string error)
            => TrySaveTexture(characterId, texture, ResultFileName, out error);

        /// <summary>保存缩略图（长边 <see cref="ThumbnailSize"/>，原子写）。</summary>
        public bool TrySaveThumbnail(string characterId, Texture2D texture, out string error)
        {
            Texture2D thumb = TextureScaleUtility.Downscale(texture, ThumbnailSize);
            if (thumb == null)
            {
                error = "无法生成缩略图（图片不可读或尺寸异常）。";
                return false;
            }

            try
            {
                return TrySaveTexture(characterId, thumb, ThumbnailFileName, out error);
            }
            finally
            {
                DestroyTexture(thumb);
            }
        }

        /// <summary>保存元数据（UTF-8 JSON，原子写）。</summary>
        public bool TrySaveManifest(AppearanceRecord record, out string error)
        {
            if (record == null || string.IsNullOrEmpty(record.CharacterId))
            {
                error = "外观记录为空，无法保存。";
                return false;
            }

            string dir = GetCharacterDirectory(record.CharacterId);
            if (dir == null)
            {
                error = "角色 ID 不合法，无法保存外观记录。";
                return false;
            }

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                error = "无法创建外观记录目录，请检查磁盘空间与权限。";
                return false;
            }

            string json = JsonUtility.ToJson(record, true);
            string path = Path.Combine(dir, ManifestFileName);
            if (AtomicFileWrite.TryWriteAllText(path, json, out string writeError))
            {
                error = string.Empty;
                return true;
            }

            error = $"保存外观记录失败：{writeError}。";
            return false;
        }

        // ==================== 读取 / 删除 / 清理 ====================

        /// <summary>读取外观记录；缺失或损坏时返回 false（不抛异常）。</summary>
        public bool TryLoadManifest(string characterId, out AppearanceRecord record)
        {
            record = null;
            string dir = GetCharacterDirectory(characterId);
            if (dir == null) return false;

            string path = Path.Combine(dir, ManifestFileName);
            try
            {
                if (!File.Exists(path)) return false;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return false;
                record = JsonUtility.FromJson<AppearanceRecord>(json);
                return record != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 读回已保存的生成结果（用于重启 / 换回角色后恢复外观）。<b>主线程调用</b>（内部 LoadImage）。
        /// 失败时 <paramref name="error"/> 说明原因与下一步，不抛异常。
        /// </summary>
        public bool TryLoadResult(string characterId, out Texture2D texture, out string error)
        {
            texture = null;
            error = string.Empty;

            string dir = GetCharacterDirectory(characterId);
            if (dir == null)
            {
                error = "角色 ID 不合法，无法读取已保存的外观。";
                return false;
            }

            string path = Path.Combine(dir, ResultFileName);
            byte[] bytes;
            try
            {
                if (!File.Exists(path))
                {
                    error = "这个角色还没有保存过外观。请先生成并应用一次。";
                    return false;
                }
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception)
            {
                error = "读取已保存的外观失败（文件可能正被占用）。请稍后重试。";
                return false;
            }

            if (bytes.Length == 0)
            {
                error = "已保存的外观文件是空的。请重新生成一次。";
                return false;
            }

            Texture2D loaded = null;
            try
            {
                loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Customization_StoredResult",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };

                if (!loaded.LoadImage(bytes, false))
                {
                    DestroyTexture(loaded);
                    error = "已保存的外观无法解码（文件可能损坏）。请重新生成一次。";
                    return false;
                }
            }
            catch (Exception)
            {
                DestroyTexture(loaded);
                error = "解码已保存的外观时出错。请重新生成一次。";
                return false;
            }

            texture = loaded;
            return true;
        }

        /// <summary>某个角色的照片与结果是否已落盘。</summary>
        public bool HasStoredData(string characterId)
        {
            string dir = GetCharacterDirectory(characterId);
            if (dir == null) return false;
            return File.Exists(Path.Combine(dir, OriginalFileName))
                || File.Exists(Path.Combine(dir, ResultFileName));
        }

        /// <summary>
        /// 删除单个角色的原图、结果、缩略图、元数据与所有残留临时文件（方案 9.6「删除」）。
        /// 文件本来就不存在也算成功（幂等）。
        /// </summary>
        public bool DeleteOne(string characterId, out string error)
        {
            error = string.Empty;
            string dir = GetCharacterDirectory(characterId);
            if (dir == null)
            {
                error = "角色 ID 不合法，无法删除照片。";
                return false;
            }

            try
            {
                if (!Directory.Exists(dir)) return true;

                // 目录内的所有文件都属于本模块（固定文件名 + .bak + .tmp/.stage），一律清掉：
                // 只删「已知文件名」会把 .bak（旧版本图片残留）留在磁盘上，等于没删干净。
                string[] files = Directory.GetFiles(dir);
                for (int i = 0; i < files.Length; i++) TryDeleteFile(files[i]);
                DeleteIncompleteFilesIn(dir);

                // 目录空了就一并删掉，避免留下空壳
                try
                {
                    if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
                }
                catch (Exception)
                {
                    // 目录删不掉不影响「数据已删除」的结论
                }

                return true;
            }
            catch (Exception)
            {
                error = "删除照片失败，文件可能正被其它程序占用。请关闭占用程序后重试。";
                return false;
            }
        }

        /// <summary>删除所有角色的照片与结果（不删除隐私同意记录）。返回处理的角色目录数。</summary>
        public int DeleteAll()
        {
            int removed = 0;
            string[] directories;
            try
            {
                if (!Directory.Exists(_root)) return 0;
                directories = Directory.GetDirectories(_root);
            }
            catch (Exception)
            {
                return 0;
            }

            for (int i = 0; i < directories.Length; i++)
            {
                string name = Path.GetFileName(directories[i]);
                if (DeleteOne(name, out _)) removed++;
            }

            return removed;
        }

        /// <summary>
        /// 启动清理：删除所有残留的 <c>.tmp</c> / <c>.stage</c>（方案 9.6）。
        /// 返回清理的文件数量；失败静默（下一次写入会覆盖同名文件）。
        /// </summary>
        public int CleanupIncomplete()
        {
            if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root)) return 0;

            var pending = new List<string>();
            try
            {
                CollectIncompleteFiles(_root, pending);
            }
            catch (Exception)
            {
                return 0;
            }

            int cleaned = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                if (TryDeleteFile(pending[i])) cleaned++;
            }
            return cleaned;
        }

        // ==================== 内部 ====================

        private bool TrySaveTexture(string characterId, Texture2D texture, string fileName, out string error)
        {
            string dir = GetCharacterDirectory(characterId);
            if (dir == null)
            {
                error = "角色 ID 不合法，无法保存图片。";
                return false;
            }

            if (texture == null || !texture.isReadable)
            {
                error = "图片不可读，无法保存（纹理需要勾选 Read/Write）。";
                return false;
            }

            byte[] png;
            try
            {
                png = texture.EncodeToPNG();
            }
            catch (Exception)
            {
                png = null;
            }

            if (png == null || png.Length == 0)
            {
                error = "图片编码失败，无法保存。请重新生成后再试。";
                return false;
            }

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                error = "无法创建保存目录，请检查磁盘空间与权限。";
                return false;
            }

            string path = Path.Combine(dir, fileName);
            if (TryWriteBytesAtomic(path, png, out string writeError))
            {
                error = string.Empty;
                return true;
            }

            error = $"保存图片失败：{writeError}。请检查磁盘空间后重试。";
            return false;
        }

        /// <summary>
        /// 原子写字节：先整块写到 <c>*.stage</c>，再用 <see cref="AtomicFileWrite.TryCopyFile"/>
        /// 做「临时文件 → 原子替换」，写失败不会破坏已有文件。
        /// </summary>
        private static bool TryWriteBytesAtomic(string path, byte[] bytes, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || bytes == null)
            {
                error = "路径或内容为空";
                return false;
            }

            string staging = path + StagingSuffix;
            try
            {
                File.WriteAllBytes(staging, bytes);
            }
            catch (Exception ex)
            {
                TryDeleteFile(staging);
                error = $"{ex.GetType().Name}: 写入中转文件失败";
                return false;
            }

            bool ok = AtomicFileWrite.TryCopyFile(staging, path, out error);
            TryDeleteFile(staging);
            return ok;
        }

        private static void CollectIncompleteFiles(string directory, List<string> pending)
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                if (file.EndsWith(AtomicFileWrite.TempSuffix, StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(StagingSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    pending.Add(file);
                }
            }

            foreach (string child in Directory.GetDirectories(directory))
            {
                CollectIncompleteFiles(child, pending);
            }
        }

        private static void DeleteIncompleteFilesIn(string directory)
        {
            var pending = new List<string>();
            try
            {
                CollectIncompleteFiles(directory, pending);
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < pending.Count; i++) TryDeleteFile(pending[i]);
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
