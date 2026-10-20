using System;
using UnityEngine;

namespace PianoTeacher3D.Customization
{
    /// <summary>
    /// 本地模拟生成提供方（方案 9.4 的离线实现）。
    /// <para>
    /// <b>纯本地、确定性、零网络</b>：不发起任何请求、不含任何 URL / 密钥字段。
    /// 变换固定为「长边缩到 <see cref="OutputSize"/> → 色阶量化到 16 级 → 轻微暖色偏移」，
    /// 相同源图 + 相同参数得到逐像素一致的结果。
    /// </para>
    /// <para>
    /// 耗时由注入时钟推进（构造函数接收 <c>Func&lt;float&gt;</c>），
    /// 因此 EditMode 测试可以在「零帧、零等待」的情况下把任务推到完成。
    /// </para>
    /// </summary>
    public sealed class MockImageGenerationProvider : IImageGenerationProvider
    {
        /// <summary>提供方 ID（用于 UI 与日志，不含密钥）。</summary>
        public const string Id = "mock-local";

        /// <summary>输出长边（256px：足够做缩略图与脸部贴图预览，且生成成本可忽略）。</summary>
        public const int OutputSize = 256;

        /// <summary>色阶量化级数（越小越"海报化"）。</summary>
        public const int QuantizeLevels = 16;

        /// <summary>默认模拟耗时（秒）；测试里传 0.1f 之类的更小值即可。</summary>
        public const float DefaultDurationSeconds = 0.4f;

        private readonly float _durationSeconds;

        /// <summary>
        /// 构造。<paramref name="clock"/> 会原样传给 <see cref="ImageGenerationJob"/>；
        /// 传 null 时任务使用 <c>Time.unscaledTime</c>（主线程）。
        /// </summary>
        public MockImageGenerationProvider(Func<float> clock, float durationSeconds = DefaultDurationSeconds)
        {
            Clock = clock;
            _durationSeconds = durationSeconds > 0f ? durationSeconds : 0.01f;
        }

        /// <summary>注入的时钟（任务推进用）。</summary>
        public Func<float> Clock { get; }

        /// <summary>本地模拟始终可用（不依赖网络）。</summary>
        public bool IsAvailable => true;

        /// <summary>提供方 ID。</summary>
        public string ProviderId => Id;

        /// <summary>启动一个本地生成任务。</summary>
        public ImageGenerationJob StartJob(GenerationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return ImageGenerationJob.Create(request, Id, Clock, Step);
        }

        // ==================== 单次尝试（主线程） ====================

        private ImageAttemptSnapshot Step(GenerationRequest request, float attemptStartedAt, float now)
        {
            if (request.Source == null)
            {
                return ImageAttemptSnapshot.Failed(ImageGenerationErrorCategory.InvalidInput,
                    "没有可用的源照片，无法生成。",
                    "请重新选择一张 PNG 或 JPG 照片。");
            }

            if (!request.Source.isReadable)
            {
                return ImageAttemptSnapshot.Failed(ImageGenerationErrorCategory.InvalidInput,
                    "源照片不可读（纹理被标记为不可读），无法处理像素。",
                    "请重新选择照片；若反复出现，请联系维护者把纹理导入设置改为可读写。");
            }

            float elapsed = now - attemptStartedAt;
            if (elapsed < 0f) elapsed = 0f;

            float t = elapsed / _durationSeconds;
            if (t < 1f)
            {
                // 前 90% 显示为进度，最后 10% 留给变换本身
                return ImageAttemptSnapshot.Running(Mathf.Clamp01(t) * 0.9f);
            }

            Texture2D result = Transform(request.Source);
            if (result == null)
            {
                return ImageAttemptSnapshot.Failed(ImageGenerationErrorCategory.Decode,
                    "本地生成未能产生图片（源照片尺寸异常或像素读取失败）。",
                    "请换一张照片，或把照片另存为 PNG 后重试。");
            }

            return ImageAttemptSnapshot.Succeeded(result);
        }

        /// <summary>
        /// 确定性变换：缩小 → 色阶量化 → 暖色偏移。返回新纹理（所有权交给任务 / 宿主）。
        /// </summary>
        private static Texture2D Transform(Texture2D source)
        {
            Texture2D scaled = TextureScaleUtility.Downscale(source, OutputSize);
            if (scaled == null) return null;

            Color32[] pixels;
            try
            {
                pixels = scaled.GetPixels32();
            }
            catch (UnityException)
            {
                Destroy(scaled);
                return null;
            }

            // 暖色偏移：红 +6 / 绿 +2 / 蓝 -10，呼应面板的琥珀强调色
            TextureScaleUtility.QuantizeAndTint(pixels, QuantizeLevels, 6, 2, -10);
            scaled.SetPixels32(pixels);
            scaled.Apply(false, false);
            scaled.name = "Customization_MockResult";
            return scaled;
        }

        private static void Destroy(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
