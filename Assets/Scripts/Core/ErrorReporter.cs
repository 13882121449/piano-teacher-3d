using System;
using UnityEngine;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 统一错误上报（对应方案 8.1「底层模块只产出数据，由会话层决定后续，UI 只展示」、
    /// 8.4「日志包含时间、模块、错误码、课程状态和可复现上下文」、第 14 章第 8 项）。
    /// <para>
    /// 本组件<b>只写日志与广播</b>，不负责 UI 文案决策（UI 直接读 <see cref="AppError.UserMessage"/>）。
    /// 日志一律通过 <see cref="AppError.ToLogString"/> 输出（已脱敏），并按严重程度选择
    /// <c>Debug.Log</c> / <c>Debug.LogWarning</c> / <c>Debug.LogError</c>。
    /// </para>
    /// <para>
    /// 隐私（方案 8.4）：<b>默认不上传</b>任何日志 / 曲目 / 照片；本组件也不做任何网络访问。
    /// </para>
    /// </summary>
    public class ErrorReporter : MonoBehaviour
    {
        /// <summary>最近一次上报的错误（无效错误不会覆盖它）。</summary>
        public AppError Last { get; private set; }

        /// <summary>错误广播（UI / 会话层订阅）。</summary>
        public event Action<AppError> OnError;

        /// <summary>
        /// 课程状态提供者（由会话层设置）。仅用于日志中的 <c>state=</c> 字段，可空。
        /// </summary>
        public Func<LessonState> StateProvider;

        /// <summary>当前模块名（可选；留空时使用错误自带的 <see cref="AppError.Context"/>）。</summary>
        public string Module = "ErrorReporter";

        /// <summary>上报一个错误：脱敏 → 写日志 → 更新 <see cref="Last"/> → 广播。</summary>
        public void Report(AppError error)
        {
            if (!error.IsValid) return;

            // 日志脱敏（方案 8.4）：只处理诊断信息，用户文案保持原样
            if (!string.IsNullOrEmpty(error.Detail))
                error.Detail = PathSanitizer.SanitizeMessage(error.Detail);

            Last = error;

            string module = string.IsNullOrEmpty(error.Context) ? Module : error.Context;
            string state = StateProvider != null ? StateProvider().ToString() : "Unknown";
            string line = $"[PianoTeacher3D][{DateTime.Now:yyyy-MM-dd HH:mm:ss}][{module}][state={state}] {error.ToLogString()}";

            switch (error.Severity)
            {
                case ErrorSeverity.Info:
                    Debug.Log(line);
                    break;
                case ErrorSeverity.Warning:
                    Debug.LogWarning(line);
                    break;
                default: // Recoverable / Fatal
                    Debug.LogError(line);
                    break;
            }

            OnError?.Invoke(error);
        }

        /// <summary>便捷上报（自动组装 <see cref="AppError"/>）。</summary>
        public void Report(ErrorCode code, ErrorSeverity severity, string userMessage, string detail = null,
            RecoveryAction recovery = RecoveryAction.None, string recoveryLabel = null, string context = null)
        {
            Report(AppError.Create(code, severity, userMessage, detail, recovery, recoveryLabel, context));
        }

        /// <summary>清空最近错误（不影响已写入的日志）。</summary>
        public void Clear()
        {
            Last = default;
        }

        // ==================== 映射助手（集中文案，避免散落在业务代码里） ====================

        /// <summary>MIDI 解析失败 → 稳定错误码 + 「发生了什么 + 为什么 + 下一步」（方案 8.3）。</summary>
        public static AppError FromMidiFailure(string parseError, string path)
        {
            string safePath = PathSanitizer.Sanitize(path);
            string detail = string.IsNullOrEmpty(parseError) ? safePath : $"{safePath}：{parseError}";

            if (!string.IsNullOrEmpty(parseError) && parseError.Contains("不存在"))
            {
                return AppError.Recoverable(ErrorCode.FileNotFound,
                    "找不到这个文件。它可能已被移动、重命名或删除。请重新点击「导入 MIDI」，选择文件当前所在的位置。",
                    RecoveryAction.ChooseFile, null, detail);
            }

            if (!string.IsNullOrEmpty(parseError)
                && (parseError.Contains("无权限") || parseError.Contains("读取失败") || parseError.Contains("读取异常")))
            {
                return AppError.Recoverable(ErrorCode.FileUnreadable,
                    "无法读取这个文件（可能没有访问权限或正被其他程序占用）。" +
                    "请关闭占用它的程序后重试，或先把文件复制到「文档」等可访问的位置再导入。",
                    RecoveryAction.ChooseFile, null, detail);
            }

            if (!string.IsNullOrEmpty(parseError)
                && (parseError.Contains("SMPTE") || parseError.Contains("PPQ") || parseError.Contains("时间基")))
            {
                return AppError.Recoverable(ErrorCode.UnsupportedFormat,
                    "这个 MIDI 文件使用了不支持的时间格式。请换一个标准 MIDI 文件，" +
                    "或先用其他 MIDI 软件另存为 Format 0 / 1 后再导入。",
                    RecoveryAction.ChooseFile, null, detail);
            }

            return AppError.Recoverable(ErrorCode.MidiHeaderInvalid,
                "无法读取该 MIDI 文件。文件可能损坏或不是标准 MIDI 文件。" +
                "请重新选择文件，或先用其他 MIDI 播放器确认文件可用。",
                RecoveryAction.ChooseFile, null, detail);
        }

        /// <summary>没有可用曲目就执行了需要曲目的操作（方案 8.2 第 1 行）。</summary>
        public static AppError NoSong(string action)
        {
            string verb = string.IsNullOrEmpty(action) ? "继续" : action;
            return AppError.Recoverable(ErrorCode.NoSongLoaded,
                $"还没有载入曲目，无法{verb}。请先在曲库中选择一首示例曲，或点击「导入 MIDI」导入自己的文件。",
                RecoveryAction.ChooseFile, null, $"action={verb}");
        }

        /// <summary>该手在本曲目中没有音符（方案 3.4 / 8.2：功能降级但可继续）。</summary>
        public static AppError HandHasNoNotes(bool leftHand)
        {
            string hand = leftHand ? "左手" : "右手";
            return AppError.Warning(ErrorCode.HandHasNoNotes,
                $"本曲目中{hand}没有音符，该手将保持静音。请改用另一只手或「双手」练习，或换一首曲目。",
                $"hand={hand}");
        }

        /// <summary>A-B 循环区间无效（方案 8.3 示例文案）。</summary>
        public static AppError LoopRangeInvalid()
        {
            return AppError.Recoverable(ErrorCode.LoopRangeInvalid,
                "当前循环区间无效，已关闭循环。请重新设置开始点和结束点。",
                RecoveryAction.ResetLoop, null, "loop=A/B");
        }

        /// <summary>防重入：正在执行上一个操作（方案 8.2「用户快速连续点击 / 切换」）。</summary>
        public static AppError Busy(string action)
        {
            string verb = string.IsNullOrEmpty(action) ? "处理" : action;
            return AppError.Warning(ErrorCode.Busy,
                $"正在{verb}，请稍候。若长时间没有反应，可以稍后再试一次。",
                $"action={verb}");
        }

        /// <summary>音频初始化失败（方案 8.2：可选择静音预览或重试）。</summary>
        public static AppError AudioInitFailed(string detail)
        {
            return AppError.Create(ErrorCode.AudioInitFailed, ErrorSeverity.Warning,
                "音频初始化失败，当前无法发声。你可以选择静音继续练习，或检查音频设备后重试。",
                detail, RecoveryAction.ContinueMuted, null, "AudioPlaybackService");
        }

        /// <summary>人物不可用 / 缺少手部骨骼（方案 8.3 示例文案：已切回默认人物，播放不受影响）。</summary>
        public static AppError AvatarInvalid(string detail)
        {
            return AppError.Create(ErrorCode.AvatarInvalid, ErrorSeverity.Warning,
                "当前人物缺少可用的手部骨骼，已切回默认人物。曲目播放未受影响。",
                detail, RecoveryAction.UseDefaultAvatar, null, "AvatarManager");
        }

        /// <summary>文件已被移动 / 删除（方案 8.2：清晰区分路径失效与格式错误）。</summary>
        public static AppError FileMissing(string path)
        {
            return AppError.Recoverable(ErrorCode.FileNotFound,
                "文件已不在原位置，可能被移动、重命名或删除。请重新点击「导入 MIDI」选择它的新位置。",
                RecoveryAction.ChooseFile, null, PathSanitizer.Sanitize(path));
        }

        /// <summary>曲目过复杂（音符过多或过长，方案 8.2「极端 tempo / 大量事件」）。</summary>
        public static AppError TooComplex(int noteCount, float duration)
        {
            return AppError.Recoverable(ErrorCode.MidiTooComplex,
                $"这首曲目过于复杂（{noteCount} 个音符 / 约 {duration:0} 秒）。" +
                "导入后会占用大量内存并可能卡顿，请选择更短或更简单的曲目，或先在 MIDI 软件中精简后再导入。",
                RecoveryAction.ChooseFile, null, $"notes={noteCount} duration={duration:F1}s");
        }
    }
}
