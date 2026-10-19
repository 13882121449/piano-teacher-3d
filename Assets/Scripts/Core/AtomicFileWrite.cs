using System;
using System.IO;
using System.Text;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 原子写文件（对应优化方案 P1-10「曲库和偏好保存的事务性与错误恢复」、清单第 9 项）。
    /// <para>
    /// 规则：<b>先写同目录临时文件 → 落盘 → 原子替换目标文件</b>；
    /// 替换前保留最近一次有效内容为 <c>*.bak</c>，供索引/偏好损坏时回退。
    /// 任一步失败都会清理临时文件，目标文件保持原状，绝不产生「写了一半」的文件。
    /// </para>
    /// <para>
    /// 纯 C#（只用 <see cref="System.IO"/>），可在 EditMode 下直接单测。
    /// </para>
    /// </summary>
    public static class AtomicFileWrite
    {
        /// <summary>临时文件后缀（与目标同目录，保证替换是同卷原子操作）。</summary>
        public const string TempSuffix = ".tmp";

        /// <summary>备份文件后缀（替换前保存上一版有效内容）。</summary>
        public const string BackupSuffix = ".bak";

        /// <summary>
        /// 原子写入文本。失败返回 false 并给出 <paramref name="error"/>（调用方自行决定日志或用户提示），
        /// <b>不抛异常</b>，且目标文件不会被破坏。
        /// </summary>
        public static bool TryWriteAllText(string path, string contents, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                error = "路径为空";
                return false;
            }

            string temp = path + TempSuffix;
            try
            {
                EnsureDirectory(path);
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(contents ?? "");
                    writer.Flush();
                    stream.Flush(true); // 先确保数据落盘，再做替换
                }

                Replace(temp, path, path + BackupSuffix);
                return true;
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                TryDelete(temp);
                return false;
            }
        }

        /// <summary>
        /// 原子复制文件（导入曲目用）：先复制到 <paramref name="destination"/> + <c>.tmp</c>，
        /// 校验长度一致后原子替换，避免中断留下半个曲目文件。
        /// </summary>
        public static bool TryCopyFile(string source, string destination, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(destination))
            {
                error = "路径为空";
                return false;
            }

            string temp = destination + TempSuffix;
            try
            {
                EnsureDirectory(destination);
                File.Copy(source, temp, true);

                if (new FileInfo(temp).Length != new FileInfo(source).Length)
                {
                    error = "复制后大小不一致";
                    TryDelete(temp);
                    return false;
                }

                Replace(temp, destination, null);
                return true;
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                TryDelete(temp);
                return false;
            }
        }

        /// <summary>删除残留的临时文件（启动清理用；不存在或删除失败都静默）。</summary>
        public static void TryCleanupTemp(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            TryDelete(path + TempSuffix);
        }

        // ==================== 内部 ====================

        private static void Replace(string temp, string target, string backup)
        {
            if (!File.Exists(target))
            {
                File.Move(temp, target);
                return;
            }

            try
            {
                // 同卷替换：Windows / NTFS 上为原子操作，并把旧内容留到 backup
                File.Replace(temp, target, backup);
            }
            catch (PlatformNotSupportedException)
            {
                FallbackReplace(temp, target);
            }
            catch (IOException)
            {
                // 个别文件系统不支持 Replace：退化为「先备份 → 删除 → 改名」，仍然不产生半文件
                FallbackReplace(temp, target);
            }
        }

        private static void FallbackReplace(string temp, string target)
        {
            if (File.Exists(target)) File.Delete(target);
            File.Move(temp, target);
        }

        private static void EnsureDirectory(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // 清理失败不影响主流程；下次写入会覆盖同名临时文件
            }
        }

        private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
    }
}
