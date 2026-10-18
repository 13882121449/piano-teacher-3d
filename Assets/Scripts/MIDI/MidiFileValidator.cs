using System;
using System.IO;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.MIDI
{
    /// <summary>
    /// 导入前的纯静态 MIDI 校验（对应方案 6.4「解析前检查文件大小、扩展名、MIDI 头部、事件数量和总时长上限；
    /// 扩展名只是初筛，真实格式由解析结果决定」）。
    /// <para>
    /// 不落盘、不依赖场景与 UnityEngine，可在 EditMode 下单测；所有 IO 异常都被归类成
    /// <see cref="ImportCheck"/>，<b>不向调用方抛异常</b>。
    /// </para>
    /// <para>
    /// 安全边界（方案 6.4）：只解析 MIDI 数据本身，<b>绝不</b>执行被导入文件中的任何命令或脚本。
    /// </para>
    /// </summary>
    public static class MidiFileValidator
    {
        /// <summary>文件大小上限（8 MB）：超过则视为过复杂，避免导入时长时间卡顿。</summary>
        public const long MaxFileBytes = 8 * 1024 * 1024;

        /// <summary>音符数上限（20 万）。</summary>
        public const int MaxNoteCount = 200000;

        /// <summary>总时长上限（1 小时）。</summary>
        public const float MaxDurationSeconds = 3600f;

        private static readonly string[] MidiExtensions = { ".mid", ".midi" };

        /// <summary>时长为零的判定阈值（秒）：小于该值视为「无可播放内容」。</summary>
        private const float MinDurationSeconds = 0.01f;

        /// <summary>
        /// 扩展名初筛（方案 6.4）：只用于尽早给出可读提示，真正的格式判断由解析结果决定。
        /// </summary>
        public static bool HasMidiExtension(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext)) return false;
                for (int i = 0; i < MidiExtensions.Length; i++)
                {
                    if (string.Equals(ext, MidiExtensions[i], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// 校验一个待导入的 MIDI 文件。校验顺序与方案 6.4 一致：
        /// 存在 → 可读 → 大小上限 → 扩展名初筛 → 真实解析（头部 / PPQ / 格式）→ 无音符 → 时长为零 → 过复杂。
        /// </summary>
        /// <param name="path">源文件路径（可含中文与空格）。</param>
        /// <param name="maxBytes">大小上限（字节），≤ 0 表示不限制。</param>
        /// <param name="maxNotes">音符数上限，≤ 0 表示不限制。</param>
        /// <param name="maxDuration">总时长上限（秒），≤ 0 表示不限制。</param>
        public static ImportCheck Validate(string path, long maxBytes = MaxFileBytes,
            int maxNotes = MaxNoteCount, float maxDuration = MaxDurationSeconds)
        {
            // ---- 1) 路径与存在性 ----
            if (string.IsNullOrEmpty(path))
                return ImportCheck.Failure(ImportRejection.Missing,
                    "还没有选择文件。请点击「导入 MIDI」，然后选择一个 .mid 或 .midi 文件。",
                    "path 为空");

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                return ImportCheck.Failure(ImportRejection.Missing,
                    "这个文件路径无法识别。请重新点击「导入 MIDI」，从文件夹中重新选择该文件。",
                    $"路径非法：{ex.GetType().Name}");
            }

            bool isDirectory = false;
            try { isDirectory = Directory.Exists(full); }
            catch (Exception) { /* 忽略：下面按文件处理 */ }
            if (isDirectory)
                return ImportCheck.Failure(ImportRejection.Unreadable,
                    "选择的是一个文件夹，不是 MIDI 文件。请进入该文件夹后再选择里面的 .mid 或 .midi 文件。",
                    $"路径是目录：{PathSanitizer.Sanitize(full)}");

            bool exists = false;
            try { exists = File.Exists(full); }
            catch (Exception) { exists = false; }
            if (!exists)
                return ImportCheck.Failure(ImportRejection.Missing,
                    "找不到这个文件。它可能已被移动、重命名或删除。请重新点击「导入 MIDI」，选择文件当前所在的位置。",
                    $"文件不存在：{PathSanitizer.Sanitize(full)}");

            // ---- 2) 可读性与大小 ----
            long size;
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists)
                    return ImportCheck.Failure(ImportRejection.Missing,
                        "找不到这个文件。它可能已被移动、重命名或删除。请重新点击「导入 MIDI」，选择文件当前所在的位置。",
                        $"FileInfo 报告不存在：{PathSanitizer.Sanitize(full)}");
                size = info.Length;
            }
            catch (UnauthorizedAccessException)
            {
                return ImportCheck.Failure(ImportRejection.Unreadable,
                    "没有权限读取这个文件。请把它复制到「文档」等可访问的位置后再导入，或以有权限的账户运行。",
                    $"读取文件信息被拒绝：{PathSanitizer.Sanitize(full)}");
            }
            catch (Exception ex)
            {
                return ImportCheck.Failure(ImportRejection.Unreadable,
                    "无法读取这个文件（可能被其他程序占用或磁盘不可用）。请关闭占用它的程序后重试，或先把文件复制到其他位置。",
                    $"读取文件信息失败：{ex.GetType().Name} {PathSanitizer.SanitizeMessage(ex.Message)}");
            }

            if (maxBytes > 0 && size > maxBytes)
                return ImportCheck.Failure(ImportRejection.TooLarge,
                    $"这个文件有 {FormatBytes(size)}，超过了 {FormatBytes(maxBytes)} 的导入上限。" +
                    "过大的 MIDI 会包含极多事件，导入后容易卡顿。请选择更小的文件，或先用 MIDI 软件裁剪后再导入。",
                    $"size={size} > max={maxBytes}");

            // ---- 3) 扩展名初筛 ----
            if (!HasMidiExtension(full))
                return ImportCheck.Failure(ImportRejection.BadExtension,
                    "这不是 MIDI 文件（扩展名不是 .mid 或 .midi）。可能选错了文件类型。请选择扩展名为 .mid 或 .midi 的文件。",
                    $"扩展名={SafeExtension(full)}");

            // ---- 4) 读取字节（区分权限 / 占用）----
            byte[] data;
            try
            {
                data = File.ReadAllBytes(full);
            }
            catch (UnauthorizedAccessException)
            {
                return ImportCheck.Failure(ImportRejection.Unreadable,
                    "没有权限读取这个文件。请把它复制到「文档」等可访问的位置后再导入，或以有权限的账户运行。",
                    $"读取被拒绝：{PathSanitizer.Sanitize(full)}");
            }
            catch (IOException ex)
            {
                return ImportCheck.Failure(ImportRejection.Unreadable,
                    "无法读取这个文件（可能正被其他程序占用）。请关闭占用它的程序后重试，或先把文件复制到其他位置。",
                    $"读取失败：{PathSanitizer.SanitizeMessage(ex.Message)}");
            }
            catch (Exception ex)
            {
                return ImportCheck.Failure(ImportRejection.Unreadable,
                    "无法读取这个文件。请关闭占用它的程序后重试，或先把文件复制到其他位置。",
                    $"读取异常：{ex.GetType().Name}");
            }

            // ---- 5) 真实解析（头部 MThd / PPQ / running status 等）----
            MidiParseResult parse;
            try
            {
                parse = MidiParser.Parse(data);
            }
            catch (Exception ex)
            {
                return ImportCheck.Failure(ImportRejection.HeaderInvalid,
                    "无法读取该 MIDI 文件。文件可能损坏或不是标准 MIDI 文件。请重新选择文件，或先用其他 MIDI 播放器确认文件可用。",
                    $"解析抛出异常：{ex.GetType().Name} {PathSanitizer.SanitizeMessage(ex.Message)}");
            }

            if (parse == null || !parse.Success)
            {
                string reason = parse != null ? parse.Error : "解析返回空结果";
                ImportRejection rejection = ClassifyParseError(reason);
                return ImportCheck.Failure(rejection, MessageForRejection(rejection),
                    $"{PathSanitizer.Sanitize(full)}：{reason}");
            }

            // ---- 6) 格式是否受支持（真实格式由解析结果决定，方案 6.4）----
            if (parse.MidiFormat < 0 || parse.MidiFormat > 2)
                return ImportCheck.Failure(ImportRejection.UnsupportedFormat,
                    MessageForRejection(ImportRejection.UnsupportedFormat),
                    $"不支持的 MIDI 格式：format={parse.MidiFormat}");

            // ---- 7) 无音符 ----
            if (parse.Notes == null || parse.Notes.Count == 0)
                return ImportCheck.Failure(ImportRejection.NoNotes,
                    "这个 MIDI 文件里没有可弹奏的音符（可能只有伴奏或控制信息，也可能是空文件）。请选择包含钢琴音符的 MIDI 文件。",
                    "解析成功但音符数为 0");

            // ---- 8) 时长为零 ----
            if (!(parse.TotalDurationSeconds > MinDurationSeconds))
                return ImportCheck.Failure(ImportRejection.ZeroDuration,
                    "这个 MIDI 文件的时长为零，没有可播放的内容。文件可能只有元数据或已损坏。请选择包含有效演奏的 MIDI 文件。",
                    $"duration={parse.TotalDurationSeconds:F3}s");

            // ---- 9) 事件数 / 时长过复杂（方案 8.2「极端 tempo / 大量事件」）----
            if (maxNotes > 0 && parse.Notes.Count > maxNotes)
                return ImportCheck.Failure(ImportRejection.TooComplex,
                    $"这首曲目音符过多（{parse.Notes.Count} 个，超过 {maxNotes} 的上限）。导入后会占用大量内存并可能卡顿。" +
                    "请选择更短或更简单的曲目，或先在 MIDI 软件中精简后再导入。",
                    $"notes={parse.Notes.Count} > max={maxNotes}");

            if (maxDuration > 0f && parse.TotalDurationSeconds > maxDuration)
                return ImportCheck.Failure(ImportRejection.TooComplex,
                    $"这首曲目太长（约 {FormatMinutes(parse.TotalDurationSeconds)}，超过 {FormatMinutes(maxDuration)} 的上限）。" +
                    "导入后会占用大量内存并可能卡顿。请选择更短的曲目，或先在 MIDI 软件中裁剪后再导入。",
                    $"duration={parse.TotalDurationSeconds:F1}s > max={maxDuration:F1}s");

            return ImportCheck.Success(parse);
        }

        // ==================== 内部助手 ====================

        /// <summary>把解析错误归类为导入拒绝原因（SMPTE / PPQ 属格式问题，其余按头部损坏处理）。</summary>
        private static ImportRejection ClassifyParseError(string error)
        {
            if (string.IsNullOrEmpty(error)) return ImportRejection.HeaderInvalid;
            if (error.Contains("SMPTE") || error.Contains("PPQ") || error.Contains("时间基"))
                return ImportRejection.UnsupportedFormat;
            return ImportRejection.HeaderInvalid;
        }

        /// <summary>按拒绝原因给出标准文案（方案 8.3：发生了什么 + 为什么 + 下一步）。</summary>
        private static string MessageForRejection(ImportRejection rejection) => rejection switch
        {
            ImportRejection.UnsupportedFormat =>
                "这个 MIDI 文件使用了不支持的时间格式（例如 SMPTE 时间基或非标准结构）。" +
                "请换一个标准 MIDI 文件，或先用其他 MIDI 软件另存为 Format 0 / 1 后再导入。",
            ImportRejection.NoNotes =>
                "这个 MIDI 文件里没有可弹奏的音符（可能只有伴奏或控制信息，也可能是空文件）。请选择包含钢琴音符的 MIDI 文件。",
            ImportRejection.ZeroDuration =>
                "这个 MIDI 文件的时长为零，没有可播放的内容。文件可能只有元数据或已损坏。请选择包含有效演奏的 MIDI 文件。",
            _ =>
                "无法读取该 MIDI 文件。文件可能损坏或不是标准 MIDI 文件。" +
                "请重新选择文件，或先用其他 MIDI 播放器确认文件可用。"
        };

        private static string SafeExtension(string path)
        {
            try { return Path.GetExtension(path) ?? ""; }
            catch (ArgumentException) { return ""; }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 KB";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:0.#} KB";
            return $"{bytes / 1024f / 1024f:0.#} MB";
        }

        private static string FormatMinutes(float seconds)
        {
            if (seconds <= 0f) return "0 分钟";
            return $"{seconds / 60f:0.#} 分钟";
        }
    }
}
