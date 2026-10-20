using System;
using UnityEngine;

namespace PianoTeacher3D.Customization
{
    /// <summary>
    /// 生成结果二次校验（方案 9.4 末段「生成的图片必须再次校验」）。
    /// <para>
    /// 服务端 / 提供方返回的纹理同样是不可信输入：类型、宽高、解码结果、内存大小都必须在
    /// 应用之前再检查一遍，不合格一律按 <see cref="ImageGenerationErrorCategory.Decode"/> 处理
    /// （不重试：同样的输入重跑一次仍然会得到同样的坏结果）。
    /// </para>
    /// </summary>
    public static class GenerationResultValidator
    {
        /// <summary>结果最短边（与输入下限一致：低于此值放大后不可用）。</summary>
        public const int MinSize = 64;

        /// <summary>结果最长边。</summary>
        public const int MaxSize = 4096;

        /// <summary>结果解码后的内存上限 32MB（RGBA32 约 8M 像素）。</summary>
        public const long MaxDecodedBytes = 32L * 1024L * 1024L;

        /// <summary>
        /// 校验生成结果。<paramref name="reason"/> / <paramref name="guidance"/> 均为可直接展示的中文。
        /// <b>不销毁</b>不合格纹理，所有权仍在调用方。
        /// </summary>
        public static bool TryValidate(Texture2D texture, out string reason, out string guidance)
        {
            reason = string.Empty;
            guidance = string.Empty;

            if (texture == null)
            {
                reason = "生成结果为空，没有可以预览的图片。";
                guidance = "请点「重新生成」再试一次；若连续失败，请换一张照片。";
                return false;
            }

            if (texture.width <= 0 || texture.height <= 0)
            {
                reason = "生成结果的尺寸无效（宽或高为 0），图片可能没有正确解码。";
                guidance = "请点「重新生成」；若仍然失败，请换一张照片或稍后再试。";
                return false;
            }

            if (texture.width < MinSize || texture.height < MinSize)
            {
                reason = $"生成结果只有 {texture.width}×{texture.height}，小于 {MinSize} 像素，无法用作外观。";
                guidance = "请点「重新生成」；若仍然过小，请换一张清晰度更高的照片。";
                return false;
            }

            if (texture.width > MaxSize || texture.height > MaxSize)
            {
                reason = $"生成结果 {texture.width}×{texture.height} 超过 {MaxSize} 像素上限，已拒绝应用。";
                guidance = "请点「重新生成」；若反复出现，请联系维护者检查生成参数。";
                return false;
            }

            long decodedBytes = (long)texture.width * texture.height * 4L;
            if (decodedBytes > MaxDecodedBytes)
            {
                reason = $"生成结果解码后约 {decodedBytes / 1024L / 1024L} MB，超过 {MaxDecodedBytes / 1024L / 1024L} MB 上限。";
                guidance = "请点「重新生成」；若反复出现，请联系维护者检查生成尺寸。";
                return false;
            }

            switch (texture.format)
            {
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.RGB24:
                    return true;
                default:
                    reason = $"生成结果的纹理格式（{texture.format}）不受支持，只接受 RGBA32 / ARGB32 / RGB24。";
                    guidance = "请点「重新生成」；若反复出现，请联系维护者检查生成格式。";
                    return false;
            }
        }
    }

    /// <summary>
    /// 图像生成任务状态机（方案 9.4 / P1-4）。宿主持有并轮询 <see cref="Tick"/>，不使用 <c>async void</c>。
    /// <para>
    /// 状态：<c>Queued → Running → Succeeded | Failed | Canceled | TimedOut</c>，结束后不再变化。
    /// </para>
    /// <para>
    /// <b>重试边界</b>：只有 <see cref="ImageGenerationErrorCategory.Network"/> 与
    /// <see cref="ImageGenerationErrorCategory.Server"/> 会自动重试，最多 <see cref="MaxRetries"/> 次；
    /// <c>InvalidInput</c> / <c>ContentRejected</c> / <c>Decode</c> 重试无意义，<c>Timeout</c> 不自动重试
    /// （否则用户会等两倍超时时间）。重试与超时都用注入时钟推进，测试可零等待。
    /// </para>
    /// <para>
    /// <b>取消</b>：<see cref="Cancel"/> 幂等；取消后 <see cref="Tick"/> 立即返回，
    /// <b>不再产生结果，也不触发 <see cref="Completed"/></b>。
    /// </para>
    /// </summary>
    public sealed class ImageGenerationJob
    {
        /// <summary>自动重试上限（不含首次尝试）。</summary>
        public const int MaxRetries = 2;

        private readonly GenerationRequest _request;
        private readonly Func<float> _clock;
        private readonly GenerationStep _step;

        private float _startedAt;
        private float _attemptStartedAt;
        private float _progress;
        private int _retryCount;
        private Texture2D _result;
        private string _message = string.Empty;
        private string _guidance = string.Empty;

        private ImageGenerationJob(GenerationRequest request, string providerId, Func<float> clock, GenerationStep step)
        {
            _request = request;
            ProviderId = providerId ?? string.Empty;
            _clock = clock;
            _step = step;
            State = ImageGenerationState.Queued;
        }

        /// <summary>提供方 ID（日志 / UI 展示用）。</summary>
        public string ProviderId { get; }

        /// <summary>请求 ID（本地生成，不含用户信息）。</summary>
        public string RequestId => _request != null ? _request.RequestId : string.Empty;

        /// <summary>当前状态。</summary>
        public ImageGenerationState State { get; private set; }

        /// <summary>进度（0..1）。</summary>
        public float Progress01 => _progress;

        /// <summary>已发生的自动重试次数。</summary>
        public int RetryCount => _retryCount;

        /// <summary>失败分类（成功时为 None）。</summary>
        public ImageGenerationErrorCategory ErrorCategory { get; private set; }

        /// <summary>失败原因文案（成功时为空）。</summary>
        public string ErrorMessage => _message;

        /// <summary>下一步建议（成功时为空）。</summary>
        public string ErrorGuidance => _guidance;

        /// <summary>
        /// 生成结果。<b>所有权</b>：成功后由宿主接管（用于预览 / 应用），本任务不再销毁它；
        /// 失败或取消时为 null。
        /// </summary>
        public Texture2D Result => _result;

        /// <summary>是否已进入终态。</summary>
        public bool IsFinished => State == ImageGenerationState.Succeeded
            || State == ImageGenerationState.Failed
            || State == ImageGenerationState.Canceled
            || State == ImageGenerationState.TimedOut;

        /// <summary>是否成功。</summary>
        public bool Succeeded => State == ImageGenerationState.Succeeded;

        /// <summary>
        /// 结束通知（成功 / 失败 / 超时会各触发一次；<b>取消不触发</b>）。
        /// UI 也可以只轮询 <see cref="State"/>，两种方式都支持。
        /// </summary>
        public event Action<ImageGenerationJob> Completed;

        /// <summary>
        /// 创建任务。<paramref name="clock"/> 为空时使用 <c>Time.unscaledTime</c>（只能在主线程用）；
        /// 测试请注入手动时钟以零等待推进。
        /// </summary>
        public static ImageGenerationJob Create(GenerationRequest request, string providerId,
            Func<float> clock, GenerationStep step)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (step == null) throw new ArgumentNullException(nameof(step));

            return new ImageGenerationJob(request, providerId, clock ?? DefaultClock, step);
        }

        /// <summary>
        /// 推进状态机。<b>只能在主线程调用</b>（<see cref="GenerationStep"/> 可能创建纹理）。
        /// </summary>
        public void Tick(float now)
        {
            if (IsFinished) return;

            if (float.IsNaN(now) || float.IsInfinity(now) || now < 0f) now = _clock();

            if (State == ImageGenerationState.Queued)
            {
                State = ImageGenerationState.Running;
                _startedAt = now;
                _attemptStartedAt = now;
                _progress = 0f;
            }

            // 超时判定必须在 step 之前：提供方卡住时也要能准时退出
            if (now - _startedAt > _request.EffectiveTimeoutSeconds)
            {
                TimedOut();
                return;
            }

            ImageAttemptSnapshot snapshot;
            try
            {
                snapshot = _step(_request, _attemptStartedAt, now);
            }
            catch (Exception)
            {
                // 提供方内部异常：按服务端错误处理（可重试），不把异常抛给 UI
                HandleFailure(ImageGenerationErrorCategory.Server,
                    "生成过程中出现异常，本次结果不可用。",
                    "请点「重试」；若反复失败，请换一张照片或稍后再试。", now);
                return;
            }

            _progress = snapshot.Progress01;
            if (!snapshot.Done) return;

            if (snapshot.Error != ImageGenerationErrorCategory.None)
            {
                HandleFailure(snapshot.Error, snapshot.Message, snapshot.Guidance, now);
                return;
            }

            if (snapshot.Result == null)
            {
                // 声称成功却没有结果 = 解码/传输问题，重试也不会变好
                Fail(ImageGenerationErrorCategory.Decode,
                    "生成服务没有返回图片数据。",
                    "请点「重试」；若反复出现，请换一张照片。");
                return;
            }

            if (!GenerationResultValidator.TryValidate(snapshot.Result, out string reason, out string guidance))
            {
                Fail(ImageGenerationErrorCategory.Decode, reason, guidance);
                return;
            }

            _progress = 1f;
            _result = snapshot.Result;
            State = ImageGenerationState.Succeeded;
            NotifyCompleted();
        }

        /// <summary>取消（幂等）。取消后不再产生结果，也不会触发 <see cref="Completed"/>。</summary>
        public void Cancel()
        {
            if (IsFinished) return;
            State = ImageGenerationState.Canceled;
            ErrorCategory = ImageGenerationErrorCategory.Canceled;
            _message = "已取消本次生成。";
            _guidance = "当前外观保持不变；需要时可以点「重新生成」。";
            _progress = 0f;
            _result = null;
        }

        /// <summary>该分类是否允许自动重试（方案 9.4：仅网络与服务端错误）。</summary>
        public static bool IsRetryable(ImageGenerationErrorCategory category)
            => category == ImageGenerationErrorCategory.Network || category == ImageGenerationErrorCategory.Server;

        // ==================== 内部 ====================

        private void HandleFailure(ImageGenerationErrorCategory category, string message, string guidance, float now)
        {
            if (IsRetryable(category) && _retryCount < MaxRetries)
            {
                _retryCount++;
                _attemptStartedAt = now;   // 重试从当前时刻重新计时，测试可零等待推进
                _progress = 0f;
                return;
            }

            Fail(category, message, guidance);
        }

        private void Fail(ImageGenerationErrorCategory category, string message, string guidance)
        {
            State = ImageGenerationState.Failed;
            ErrorCategory = category;
            _message = string.IsNullOrEmpty(message) ? "生成失败。" : message;
            _guidance = string.IsNullOrEmpty(guidance) ? "请点「重试」；若仍然失败，请换一张照片。" : guidance;
            _progress = 0f;
            _result = null;
            NotifyCompleted();
        }

        private void TimedOut()
        {
            State = ImageGenerationState.TimedOut;
            ErrorCategory = ImageGenerationErrorCategory.Timeout;
            _message = $"生成超过 {_request.EffectiveTimeoutSeconds:0} 秒仍未完成，已停止等待。";
            _guidance = "当前外观保持不变；请点「重试」，或换一张更小的照片再试。";
            _progress = 0f;
            _result = null;
            NotifyCompleted();
        }

        private void NotifyCompleted()
        {
            var handler = Completed;
            if (handler == null) return;
            try
            {
                handler(this);
            }
            catch (Exception)
            {
                // 订阅者异常不得影响任务终态；UI 轮询仍然能拿到正确结果
            }
        }

        private static float DefaultClock() => Time.unscaledTime;
    }
}
