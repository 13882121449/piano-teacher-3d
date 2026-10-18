using System;
using System.IO;

namespace PianoTeacher3D.Core
{
    /// <summary>错误严重程度（方案 8.1）：提示 / 警告 / 可恢复错误 / 致命错误。</summary>
    public enum ErrorSeverity
    {
        /// <summary>纯提示，不影响继续使用。</summary>
        Info = 0,

        /// <summary>警告：功能降级但仍可继续（例如音频不可用、角色不可用）。</summary>
        Warning = 1,

        /// <summary>可恢复错误：当前操作失败，用户可按建议动作恢复。</summary>
        Recoverable = 2,

        /// <summary>致命错误：无法继续（例如资源整体缺失）。</summary>
        Fatal = 3
    }

    /// <summary>
    /// 稳定错误码表（方案 8.1「Code：稳定的错误代码」）。
    /// 新增错误时必须追加枚举值，不要复用旧值语义。
    /// </summary>
    public enum ErrorCode
    {
        None = 0,

        // ---- 曲库 / 文件 ----
        SongDirectoryMissing = 100,
        SongListEmpty = 101,
        FileNotFound = 102,
        FileUnreadable = 103,
        FileTooLarge = 104,
        UnsupportedExtension = 105,
        UnsupportedFormat = 106,
        MidiHeaderInvalid = 107,
        MidiNoNotes = 108,
        MidiZeroDuration = 109,
        MidiTooComplex = 110,
        MidiDanglingNotes = 111,
        MidiNoTempo = 112,

        // ---- 运行时模块 ----
        AudioInitFailed = 200,
        AvatarInvalid = 201,
        AvatarBonesMissing = 202,
        CameraTargetLost = 203,
        LoopRangeInvalid = 204,
        NoSongLoaded = 205,
        HandHasNoNotes = 206,
        Busy = 207,

        Unknown = 999
    }

    /// <summary>
    /// 建议的恢复动作（方案 8.1「RecoveryAction：建议按钮或恢复动作」）。
    /// UI 据此显示恢复按钮，会话层据此处执行动作。
    /// </summary>
    public enum RecoveryAction
    {
        /// <summary>无可用恢复动作（仅展示信息）。</summary>
        None = 0,

        /// <summary>重新选择 MIDI 文件。</summary>
        ChooseFile,

        /// <summary>重试当前操作（重新解析 / 重新初始化）。</summary>
        Retry,

        /// <summary>重新设置 A-B 循环起止点。</summary>
        ResetLoop,

        /// <summary>切回默认人物。</summary>
        UseDefaultAvatar,

        /// <summary>改用静音预览继续练习。</summary>
        ContinueMuted,

        /// <summary>重新载入曲目。</summary>
        ReloadSong
    }

    /// <summary>
    /// 统一错误结构（方案 8.1）：底层模块只产出数据，由会话层决定是否继续 / 回滚 / 禁用操作，
    /// UI 只负责展示。文案遵循「发生了什么 + 为什么可能发生 + 下一步做什么」（方案 8.3）。
    /// <para>值类型，无 UnityEngine 依赖，便于单元测试。</para>
    /// </summary>
    [Serializable]
    public struct AppError
    {
        /// <summary>稳定错误码。</summary>
        public ErrorCode Code;

        /// <summary>严重程度。</summary>
        public ErrorSeverity Severity;

        /// <summary>面向用户的中文提示（必须含下一步动作）。</summary>
        public string UserMessage;

        /// <summary>开发诊断信息：仅写日志，不向普通用户展示堆栈。</summary>
        public string Detail;

        /// <summary>建议的恢复动作。</summary>
        public RecoveryAction Recovery;

        /// <summary>恢复按钮文案（Recovery != None 时应非空）。</summary>
        public string RecoveryLabel;

        /// <summary>上下文：模块名 / 会话状态 / 曲目 ID（已脱敏，不含完整路径）。</summary>
        public string Context;

        /// <summary>是否可用于展示（UserMessage 非空）。</summary>
        public bool IsValid => !string.IsNullOrEmpty(UserMessage);

        public static AppError Create(ErrorCode code, ErrorSeverity severity, string userMessage,
            string detail = null, RecoveryAction recovery = RecoveryAction.None,
            string recoveryLabel = null, string context = null)
        {
            return new AppError
            {
                Code = code,
                Severity = severity,
                UserMessage = userMessage,
                Detail = detail,
                Recovery = recovery,
                RecoveryLabel = string.IsNullOrEmpty(recoveryLabel) ? DefaultRecoveryLabel(recovery) : recoveryLabel,
                Context = context
            };
        }

        /// <summary>便捷构造：可恢复错误。</summary>
        public static AppError Recoverable(ErrorCode code, string userMessage,
            RecoveryAction recovery = RecoveryAction.None, string recoveryLabel = null, string detail = null)
            => Create(code, ErrorSeverity.Recoverable, userMessage, detail, recovery, recoveryLabel);

        /// <summary>便捷构造：警告（功能降级）。</summary>
        public static AppError Warning(ErrorCode code, string userMessage, string detail = null)
            => Create(code, ErrorSeverity.Warning, userMessage, detail);

        public string ToLogString()
        {
            var ctx = string.IsNullOrEmpty(Context) ? "-" : Context;
            var detail = string.IsNullOrEmpty(Detail) ? "-" : Detail;
            return $"[{Severity}][{Code}] ctx={ctx} user=\"{UserMessage}\" detail={detail}";
        }

        private static string DefaultRecoveryLabel(RecoveryAction action) => action switch
        {
            RecoveryAction.ChooseFile => "重新选择文件",
            RecoveryAction.Retry => "重试",
            RecoveryAction.ResetLoop => "重新设置循环",
            RecoveryAction.UseDefaultAvatar => "切回默认人物",
            RecoveryAction.ContinueMuted => "静音继续",
            RecoveryAction.ReloadSong => "重新载入曲目",
            _ => null
        };
    }

    /// <summary>
    /// 日志脱敏工具（方案 8.4）：日志不保存完整个人文件路径、私人歌曲元数据或照片内容。
    /// 只保留文件名（必要时截断），避免把用户目录结构写进日志。
    /// </summary>
    public static class PathSanitizer
    {
        /// <summary>把路径压缩为「所在目录的首段省略 + 文件名」，例如 <c>…\songs\demo.mid</c>。</summary>
        public static string Sanitize(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            try
            {
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) return "…";
                return "…" + Path.DirectorySeparatorChar + name;
            }
            catch (ArgumentException)
            {
                return "…";
            }
        }

        /// <summary>把异常消息中的绝对路径替换为脱敏形式（保留可诊断信息，去掉用户目录）。</summary>
        public static string SanitizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            if (message.Length > 512) message = message.Substring(0, 512) + "…";

            // 简单模式：形如 X:\...\file 或 /.../file 的绝对路径整体替换为文件名
            try
            {
                return System.Text.RegularExpressions.Regex.Replace(
                    message,
                    @"[A-Za-z]:\\[^\s""']+|/[^\s""':]+/[^\s""']*",
                    m => PathSanitizer.Sanitize(m.Value));
            }
            catch (ArgumentException)
            {
                return message;
            }
        }
    }
}
