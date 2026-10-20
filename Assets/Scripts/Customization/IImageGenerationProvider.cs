using UnityEngine;
using PianoTeacher3D.Characters;

namespace PianoTeacher3D.Customization
{
    /// <summary>生成任务状态（方案 9.4 / P1-4）。单向推进，结束后不再变化。</summary>
    public enum ImageGenerationState
    {
        /// <summary>已排队，尚未开始。</summary>
        Queued = 0,

        /// <summary>生成中（含内部重试）。</summary>
        Running = 1,

        /// <summary>成功，结果已通过二次校验。</summary>
        Succeeded = 2,

        /// <summary>失败（不可自动恢复）。</summary>
        Failed = 3,

        /// <summary>用户取消（幂等，取消后不再产生结果）。</summary>
        Canceled = 4,

        /// <summary>超时。</summary>
        TimedOut = 5
    }

    /// <summary>
    /// 失败分类（方案 9.4）：决定「能不能重试」，也决定给用户的下一步建议。
    /// </summary>
    public enum ImageGenerationErrorCategory
    {
        /// <summary>没有错误。</summary>
        None = 0,

        /// <summary>输入不合法（源图不可读、参数错误）——不可重试。</summary>
        InvalidInput = 1,

        /// <summary>网络问题——可重试。</summary>
        Network = 2,

        /// <summary>超时——不自动重试（避免无休止等待）。</summary>
        Timeout = 3,

        /// <summary>被取消。</summary>
        Canceled = 4,

        /// <summary>内容被拒绝（照片不合规）——不可重试。</summary>
        ContentRejected = 5,

        /// <summary>服务端错误——可重试。</summary>
        Server = 6,

        /// <summary>结果无法解码 / 不合规——不可重试。</summary>
        Decode = 7
    }

    /// <summary>
    /// 生成请求。只描述本地输入，<b>不含任何密钥、URL 或服务端凭据</b>（方案 9.4：密钥只能在服务端）。
    /// </summary>
    public sealed class GenerationRequest
    {
        /// <summary>默认超时（秒）。超时后进入 <see cref="ImageGenerationState.TimedOut"/>，不自动重试。</summary>
        public const float DefaultTimeoutSeconds = 30f;

        /// <summary>源照片（只读使用，不转移所有权）。</summary>
        public Texture2D Source;

        /// <summary>目标角色风格。</summary>
        public CharacterStyle Style = CharacterStyle.Custom;

        /// <summary>本地请求 ID（仅用于日志关联与去重，不含用户信息）。</summary>
        public string RequestId = string.Empty;

        /// <summary>超时秒数（&lt;= 0 时回落为 <see cref="DefaultTimeoutSeconds"/>）。</summary>
        public float TimeoutSeconds = DefaultTimeoutSeconds;

        /// <summary>实际生效的超时。</summary>
        public float EffectiveTimeoutSeconds
            => TimeoutSeconds > 0f && !float.IsNaN(TimeoutSeconds) && !float.IsInfinity(TimeoutSeconds)
                ? TimeoutSeconds
                : DefaultTimeoutSeconds;

        /// <summary>构造请求。</summary>
        public static GenerationRequest Create(Texture2D source, CharacterStyle style, string requestId,
            float timeoutSeconds = DefaultTimeoutSeconds)
        {
            return new GenerationRequest
            {
                Source = source,
                Style = style,
                RequestId = requestId ?? string.Empty,
                TimeoutSeconds = timeoutSeconds
            };
        }
    }

    /// <summary>
    /// 单次尝试的快照。<see cref="Tick"/> 每次都返回一份，任务状态机据此决定「继续 / 成功 / 失败 / 重试」。
    /// 用值类型避免每帧分配。
    /// </summary>
    public readonly struct ImageAttemptSnapshot
    {
        /// <summary>本次尝试是否已结束。</summary>
        public readonly bool Done;

        /// <summary>进度（0..1）。</summary>
        public readonly float Progress01;

        /// <summary>成功结果（仅在 <see cref="Done"/> 且 <see cref="Error"/> 为 None 时非空）。</summary>
        public readonly Texture2D Result;

        /// <summary>失败分类。</summary>
        public readonly ImageGenerationErrorCategory Error;

        /// <summary>原因文案。</summary>
        public readonly string Message;

        /// <summary>下一步建议。</summary>
        public readonly string Guidance;

        private ImageAttemptSnapshot(bool done, float progress, Texture2D result,
            ImageGenerationErrorCategory error, string message, string guidance)
        {
            Done = done;
            Progress01 = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            Result = result;
            Error = error;
            Message = message ?? string.Empty;
            Guidance = guidance ?? string.Empty;
        }

        /// <summary>进行中。</summary>
        public static ImageAttemptSnapshot Running(float progress) =>
            new ImageAttemptSnapshot(false, progress, null, ImageGenerationErrorCategory.None, null, null);

        /// <summary>成功。</summary>
        public static ImageAttemptSnapshot Succeeded(Texture2D result) =>
            new ImageAttemptSnapshot(true, 1f, result, ImageGenerationErrorCategory.None, null, null);

        /// <summary>失败。</summary>
        public static ImageAttemptSnapshot Failed(ImageGenerationErrorCategory error, string message, string guidance) =>
            new ImageAttemptSnapshot(true, 0f, null, error, message, guidance);
    }

    /// <summary>
    /// 单次尝试的推进函数（由提供方实现，注入给 <see cref="ImageGenerationJob"/>）。
    /// <para>
    /// 参数：请求、本次尝试的开始时间、当前时间（均来自注入时钟）。
    /// 必须是纯本地计算或对已启动任务的轮询——<b>不得</b>访问 Unity 渲染 API 之外的跨线程资源。
    /// </para>
    /// </summary>
    public delegate ImageAttemptSnapshot GenerationStep(GenerationRequest request, float attemptStartedAt, float now);

    /// <summary>
    /// 图像生成提供方（方案 9.4）。接口刻意只保留三项：
    /// 身份、可用性、启动任务——重试/超时/取消全在 <see cref="ImageGenerationJob"/> 里，
    /// 提供方不需要也不允许知道密钥（密钥只能放在服务端）。
    /// </summary>
    public interface IImageGenerationProvider
    {
        /// <summary>稳定提供方 ID（用于日志与 UI 显示，不含密钥）。</summary>
        string ProviderId { get; }

        /// <summary>当前是否可用（离线 / 未配置时为 false，UI 据此降级而不是报错卡住）。</summary>
        bool IsAvailable { get; }

        /// <summary>启动一次生成任务。同一时刻只应有一个在飞任务（由编排层保证）。</summary>
        ImageGenerationJob StartJob(GenerationRequest request);
    }
}
