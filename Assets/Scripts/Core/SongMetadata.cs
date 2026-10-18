using System;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 曲目元数据（对应方案 6.4「导入成功后生成安全的内部 ID；显示曲名、时长、音符数、轨道数和 tempo 变化」、
    /// 9.2「最近曲目只保存必要的用户数据」）。
    /// <para>
    /// 只保存必要元数据，不保存 MIDI 内容；<see cref="SourcePath"/> 仅内存使用，
    /// <b>写日志前必须</b>经 <see cref="PathSanitizer.Sanitize"/> 脱敏（方案 8.4）。
    /// </para>
    /// <para>标 <see cref="SerializableAttribute"/> 以便用 <c>JsonUtility</c> 持久化最近曲目。</para>
    /// </summary>
    [Serializable]
    public sealed class SongMetadata
    {
        /// <summary>安全内部 ID：对源路径做稳定哈希（不含用户目录结构，可直接进入文件名 / 日志）。</summary>
        public string Id;

        /// <summary>显示名（不含扩展名）。</summary>
        public string Title;

        /// <summary>该曲目当前所在路径（内置示例曲位于 StreamingAssets/Songs；导入曲位于用户数据目录）。</summary>
        public string SourcePath;

        /// <summary>总时长（秒）。</summary>
        public float DurationSeconds;

        /// <summary>音符数。</summary>
        public int NoteCount;

        /// <summary>轨道数。</summary>
        public int TrackCount;

        /// <summary>tempo 变化点数。</summary>
        public int TempoChangeCount;

        /// <summary>MIDI 格式（0 / 1 / 2）。</summary>
        public int MidiFormat;

        /// <summary>文件大小（字节）。</summary>
        public long FileSizeBytes;

        /// <summary>缺少 Note Off 而被按曲目末尾补全的音符数。</summary>
        public int DanglingNoteCount;

        /// <summary>导入时间（ISO8601 / UTC）。</summary>
        public string ImportedAtIso;

        public override string ToString()
            => $"Song[{Id}] {Title} {DurationSeconds:F1}s {NoteCount}notes fmt{MidiFormat}";
    }

    /// <summary>
    /// 导入被拒绝的原因（对应方案 6.4 的校验顺序与方案 8.2 的异常表）。
    /// </summary>
    public enum ImportRejection
    {
        /// <summary>未拒绝（校验通过）。</summary>
        None = 0,

        /// <summary>文件不存在 / 路径为空（路径失效）。</summary>
        Missing,

        /// <summary>文件存在但无法读取（无权限、被占用、IO 错误）。</summary>
        Unreadable,

        /// <summary>超出大小上限。</summary>
        TooLarge,

        /// <summary>扩展名不是 .mid / .midi。</summary>
        BadExtension,

        /// <summary>结构上不是受支持的 MIDI（SMPTE 时间基 / 未知格式等）。</summary>
        UnsupportedFormat,

        /// <summary>头部或数据损坏（缺少 MThd、截断、非法 running status 等）。</summary>
        HeaderInvalid,

        /// <summary>没有可播放的音符。</summary>
        NoNotes,

        /// <summary>总时长为零。</summary>
        ZeroDuration,

        /// <summary>音符过多或过长（方案 8.2「极端 tempo / 大量事件」）。</summary>
        TooComplex
    }

    /// <summary>
    /// 一次导入校验的结果（对应方案 6.4「解析前检查文件大小、扩展名、MIDI 头部、事件数量和总时长上限」）。
    /// <para>值类型、不含 UnityEngine 依赖，可在 EditMode 下单测。</para>
    /// </summary>
    public struct ImportCheck
    {
        /// <summary>是否通过校验。</summary>
        public bool Ok;

        /// <summary>被拒绝的原因（<see cref="Ok"/> 为 true 时是 <see cref="ImportRejection.None"/>）。</summary>
        public ImportRejection Reason;

        /// <summary>面向用户的提示（含「发生了什么 + 为什么 + 下一步」，方案 8.3）。</summary>
        public string UserMessage;

        /// <summary>开发诊断信息（已脱敏，不向普通用户展示）。</summary>
        public string Detail;

        /// <summary>解析结果（<see cref="Ok"/> 为 true 时非 null）。</summary>
        public MidiParseResult Parse;

        /// <summary>转成统一错误结构（含恢复动作，方案 8.1 / 8.3）。</summary>
        public AppError ToError()
        {
            if (Ok)
                return AppError.Create(ErrorCode.None, ErrorSeverity.Info,
                    string.IsNullOrEmpty(UserMessage) ? "文件可用，可以开始练习。" : UserMessage);

            string message = string.IsNullOrEmpty(UserMessage)
                ? "无法读取该文件。请重新选择文件。"
                : UserMessage;

            switch (Reason)
            {
                case ImportRejection.Missing:
                    return AppError.Recoverable(ErrorCode.FileNotFound, message, RecoveryAction.ChooseFile, null, Detail);
                case ImportRejection.Unreadable:
                    return AppError.Recoverable(ErrorCode.FileUnreadable, message, RecoveryAction.ChooseFile, null, Detail);
                case ImportRejection.TooLarge:
                    return AppError.Recoverable(ErrorCode.FileTooLarge, message, RecoveryAction.ChooseFile, null, Detail);
                case ImportRejection.BadExtension:
                    return AppError.Recoverable(ErrorCode.UnsupportedExtension, message, RecoveryAction.ChooseFile, null, Detail);
                case ImportRejection.UnsupportedFormat:
                    return AppError.Recoverable(ErrorCode.UnsupportedFormat, message, RecoveryAction.ChooseFile, null, Detail);
                case ImportRejection.HeaderInvalid:
                    return AppError.Recoverable(ErrorCode.MidiHeaderInvalid, message, RecoveryAction.ChooseFile, null, Detail);
                case ImportRejection.NoNotes:
                    return AppError.Recoverable(ErrorCode.MidiNoNotes, message, RecoveryAction.ChooseFile, null, Detail);
                case ImportRejection.ZeroDuration:
                    return AppError.Recoverable(ErrorCode.MidiZeroDuration, message, RecoveryAction.ChooseFile, null, Detail);
                case ImportRejection.TooComplex:
                    return AppError.Recoverable(ErrorCode.MidiTooComplex, message, RecoveryAction.ChooseFile, null, Detail);
                default:
                    return AppError.Recoverable(ErrorCode.Unknown, message, RecoveryAction.ChooseFile, null, Detail);
            }
        }

        /// <summary>构造通过校验的结果。</summary>
        public static ImportCheck Success(MidiParseResult parse)
            => new ImportCheck
            {
                Ok = true,
                Reason = ImportRejection.None,
                UserMessage = "文件可用，可以开始练习。",
                Detail = null,
                Parse = parse
            };

        /// <summary>构造被拒绝的结果。</summary>
        public static ImportCheck Failure(ImportRejection reason, string userMessage,
            string detail = null, MidiParseResult parse = null)
            => new ImportCheck
            {
                Ok = false,
                Reason = reason,
                UserMessage = userMessage,
                Detail = detail,
                Parse = parse
            };
    }

    /// <summary>
    /// 导入结果（对应方案 6.4 / 6.2 的「导入事务」）：成功时携带元数据与校验结果；失败时只携带校验结果。
    /// </summary>
    public struct ImportResult
    {
        /// <summary>是否导入成功（文件已复制到用户目录并写入最近曲目）。</summary>
        public bool Ok;

        /// <summary>成功时的曲目元数据。</summary>
        public SongMetadata Meta;

        /// <summary>校验结果（失败时含原因、文案与恢复动作）。</summary>
        public ImportCheck Check;
    }
}
