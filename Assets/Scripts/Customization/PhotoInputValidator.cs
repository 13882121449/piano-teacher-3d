using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace PianoTeacher3D.Customization
{
    /// <summary>照片的真实格式（由文件头魔数判定，不信任扩展名与 MIME）。</summary>
    public enum PhotoFormat
    {
        /// <summary>无法判定 / 不在白名单内。</summary>
        Unknown = 0,

        /// <summary>PNG（魔数 89 50 4E 47 0D 0A 1A 0A）。</summary>
        Png = 1,

        /// <summary>JPEG（魔数 FF D8 FF）。</summary>
        Jpeg = 2
    }

    /// <summary>
    /// 照片被拒绝的结构化原因（方案 9.3）。
    /// 与文案分离：UI / 测试断言代码，不匹配中文字符串。
    /// </summary>
    public enum PhotoRejectReason
    {
        /// <summary>通过。</summary>
        None = 0,

        /// <summary>没有选择文件 / 路径为空。</summary>
        NoFile = 1,

        /// <summary>空文件（0 字节）。</summary>
        EmptyFile = 2,

        /// <summary>超过大小上限。</summary>
        TooLarge = 3,

        /// <summary>扩展名不在白名单内。</summary>
        UnsupportedExtension = 4,

        /// <summary>扩展名合法但文件头不是 PNG / JPEG（伪装文件）。</summary>
        UnsupportedFormat = 5,

        /// <summary>读取失败（不存在 / 没权限 / 被占用）。</summary>
        Unreadable = 6,

        /// <summary>解码失败（文件损坏 / 截断）。</summary>
        DecodeFailed = 7,

        /// <summary>尺寸过小。</summary>
        TooSmall = 8,

        /// <summary>单边超过上限。</summary>
        TooLargeDimension = 9,

        /// <summary>总像素超过上限。</summary>
        TooManyPixels = 10
    }

    /// <summary>
    /// 照片校验结果。文案遵循方案 12.3：<see cref="Reason"/> 说清「发生了什么 + 为什么」，
    /// <see cref="Guidance"/> 说清「下一步怎么办」，不允许只写「失败」。
    /// <para>
    /// 资源所有权：<see cref="Texture"/> 仅在 <see cref="Ok"/> 时非空，所有权交给调用方，
    /// 调用方用完必须 <c>Object.Destroy</c>（视图或存储接手后由对应模块负责）。
    /// </para>
    /// </summary>
    public readonly struct PhotoValidationResult
    {
        /// <summary>是否通过。</summary>
        public readonly bool Ok;

        /// <summary>结构化拒绝原因。</summary>
        public readonly PhotoRejectReason ReasonCode;

        /// <summary>面向用户的原因（通过时为空字符串）。</summary>
        public readonly string Reason;

        /// <summary>面向用户的下一步建议（通过时为空字符串）。</summary>
        public readonly string Guidance;

        /// <summary>解码后的宽（通过时有效；仅头部预检通过但未解码时为 0）。</summary>
        public readonly int Width;

        /// <summary>解码后的高。</summary>
        public readonly int Height;

        /// <summary>文件字节数。</summary>
        public readonly long Bytes;

        /// <summary>由魔数判定的真实格式。</summary>
        public readonly PhotoFormat Format;

        /// <summary>解码结果（所有权归调用方，见类型注释）。</summary>
        public readonly Texture2D Texture;

        private PhotoValidationResult(bool ok, PhotoRejectReason reasonCode, string reason, string guidance,
            int width, int height, long bytes, PhotoFormat format, Texture2D texture)
        {
            Ok = ok;
            ReasonCode = reasonCode;
            Reason = ok ? string.Empty : (reason ?? "照片未通过校验。");
            Guidance = ok ? string.Empty : (guidance ?? "请换一张照片后重试。");
            Width = width;
            Height = height;
            Bytes = bytes;
            Format = format;
            Texture = texture;
        }

        /// <summary>通过。</summary>
        public static PhotoValidationResult Pass(Texture2D texture, long bytes, PhotoFormat format)
            => new PhotoValidationResult(true, PhotoRejectReason.None, null, null,
                texture != null ? texture.width : 0, texture != null ? texture.height : 0, bytes, format, texture);

        /// <summary>拒绝。</summary>
        public static PhotoValidationResult Reject(PhotoRejectReason code, string reason, string guidance,
            long bytes = 0, PhotoFormat format = PhotoFormat.Unknown, int width = 0, int height = 0)
            => new PhotoValidationResult(false, code, reason, guidance, width, height, bytes, format, null);

        /// <summary>便于日志输出（不含文件名与路径，方案 9.7）。</summary>
        public override string ToString()
            => Ok ? $"OK {Width}x{Height} {Format} {Bytes}B" : $"{ReasonCode}: {Reason}";
    }

    /// <summary>
    /// 照片输入校验（方案 9.3「上传照片的格式与尺寸校验」）。
    /// <para>
    /// 三道关卡：
    /// ① 扩展名白名单（.png / .jpg / .jpeg）——只做初筛，不作为安全依据；
    /// ② 文件头魔数判定真实格式——扩展名可随意改，魔数不会；
    /// ③ 从文件头预读宽高 + 真实解码验证——尺寸/像素上限在<b>分配纹理之前</b>拦截，防内存耗尽。
    /// </para>
    /// <para>
    /// <b>主线程约束</b>：<see cref="ValidateBytes"/> / <see cref="ValidateFile"/> 内部调用
    /// <c>Texture2D.LoadImage</c>，Unity API 只能在主线程调用，因此这两个方法必须由主线程调用；
    /// 需要后台读文件时用 <see cref="PhotoDecodeJob"/>（只把字节读取放到后台线程）。
    /// </para>
    /// </summary>
    public static class PhotoInputValidator
    {
        /// <summary>单文件大小上限 8MB：防止一次读入超大文件造成内存峰值。</summary>
        public const long MaxBytes = 8L * 1024L * 1024L;

        /// <summary>最短边下限 64px：更小的图放大后糊成一团，无法用于生成。</summary>
        public const int MinDimension = 64;

        /// <summary>单边上限 4096px：满足贴图需求，同时避免 GPU / CPU 纹理过大。</summary>
        public const int MaxDimension = 4096;

        /// <summary>
        /// 总像素上限 16M（16,000,000，十进制）。
        /// 与 <see cref="MaxDimension"/> 一起构成「防内存耗尽」双保险：RGBA32 解码后约 64MB。
        /// </summary>
        public const long MaxPixels = 16000000L;

        /// <summary>白名单扩展名（小写比较，不区分大小写）。</summary>
        public static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>PNG 文件头（8 字节）。</summary>
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>扩展名是否在白名单内（大小写不敏感；纯 BCL，可在后台线程调用）。</summary>
        public static bool HasAllowedExtension(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = SafeExtension(path);
            for (int i = 0; i < AllowedExtensions.Length; i++)
            {
                if (string.Equals(ext, AllowedExtensions[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>按文件头魔数判定真实格式（纯 BCL，可在后台线程调用）。</summary>
        public static PhotoFormat SniffSignature(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 3) return PhotoFormat.Unknown;
            if (bytes.Length >= 8 && MatchesPng(bytes)) return PhotoFormat.Png;
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return PhotoFormat.Jpeg;
            return PhotoFormat.Unknown;
        }

        /// <summary>
        /// 校验内存中的照片字节（含真实解码）。<b>必须在主线程调用</b>。
        /// <paramref name="fileNameOrPath"/> 只用于取扩展名，不会被记录到日志。
        /// </summary>
        public static PhotoValidationResult ValidateBytes(byte[] bytes, string fileNameOrPath)
        {
            long size = bytes != null ? bytes.Length : 0;

            if (bytes == null || bytes.Length == 0)
            {
                return PhotoValidationResult.Reject(PhotoRejectReason.EmptyFile,
                    "这个文件是空的（0 字节），没有可用的图像数据。",
                    "请重新选择一张能正常打开的 PNG 或 JPG 照片。");
            }

            if (!HasAllowedExtension(fileNameOrPath))
            {
                string shown = SafeExtension(fileNameOrPath);
                return PhotoValidationResult.Reject(PhotoRejectReason.UnsupportedExtension,
                    string.IsNullOrEmpty(shown)
                        ? "只支持 PNG / JPG 格式的照片。"
                        : $"「{shown}」格式不受支持，只支持 .png / .jpg / .jpeg。",
                    "请先在画图或系统照片应用里另存为 PNG 或 JPG，再重新上传。",
                    size, PhotoFormat.Unknown);
            }

            if (size > MaxBytes)
            {
                return PhotoValidationResult.Reject(PhotoRejectReason.TooLarge,
                    $"照片有 {FormatBytes(size)}，超过 {FormatBytes(MaxBytes)} 的上限。",
                    "请把照片缩小或压缩到 8MB 以内再上传（大多数手机相册可以「另存为较小尺寸」）。",
                    size, PhotoFormat.Unknown);
            }

            PhotoFormat format = SniffSignature(bytes);
            if (format == PhotoFormat.Unknown)
            {
                return PhotoValidationResult.Reject(PhotoRejectReason.UnsupportedFormat,
                    "文件的真实内容不是 PNG 或 JPEG（扩展名与实际格式不一致，或文件已损坏）。",
                    "请用图片查看器确认这张图能正常打开，并另存为 PNG / JPG 后重试。",
                    size, PhotoFormat.Unknown);
            }

            // ② 从文件头预读宽高：在分配纹理之前拦截超尺寸，防内存耗尽
            if (TryReadHeaderSize(bytes, format, out int headerWidth, out int headerHeight))
            {
                if (TryCheckDimensions(headerWidth, headerHeight, size, format, out var headerReject))
                    return headerReject;
            }

            // ③ 真实解码：唯一可信的「能不能用」判据
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "PhotoInput",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };

                // markNonReadable = false：生成端需要读取像素做确定性变换
                if (!texture.LoadImage(bytes, false))
                {
                    DestroyTexture(ref texture);
                    return PhotoValidationResult.Reject(PhotoRejectReason.DecodeFailed,
                        "这张图能读到数据，但无法解码成图像（可能已损坏或只下载了一半）。",
                        "请重新选择未损坏的原图；如果是网上下载的图，请重新下载后再试。",
                        size, format);
                }
            }
            catch (Exception)
            {
                DestroyTexture(ref texture);
                return PhotoValidationResult.Reject(PhotoRejectReason.DecodeFailed,
                    "解码这张图时出错，图像内容可能不完整。",
                    "请换一张照片，或用图片查看器另存为 PNG 后重试。",
                    size, format);
            }

            var sizeCheck = CheckDimensionsOrReject(texture.width, texture.height, size, format);
            if (sizeCheck.HasValue)
            {
                DestroyTexture(ref texture);
                return sizeCheck.Value;
            }

            texture.name = $"PhotoInput_{texture.width}x{texture.height}";
            return PhotoValidationResult.Pass(texture, size, format);
        }

        /// <summary>
        /// 同步校验一个文件（含读盘与解码）。<b>必须在主线程调用</b>；交互流程请改用
        /// <see cref="PhotoDecodeJob"/>，避免读盘卡住界面。
        /// </summary>
        public static PhotoValidationResult ValidateFile(string path)
        {
            PhotoProbe probe = PhotoProbe.Read(path);
            if (!probe.Ok)
            {
                return PhotoValidationResult.Reject(probe.ReasonCode, probe.Reason, probe.Guidance,
                    probe.FileBytes, probe.Format);
            }
            return ValidateBytes(probe.Bytes, path);
        }

        // ==================== 内部：尺寸检查 / 头部读取 ====================

        /// <summary>尺寸/像素合规性检查：返回 null 表示通过，否则返回可直接展示的拒绝结果。</summary>
        private static PhotoValidationResult? CheckDimensionsOrReject(int width, int height, long bytes,
            PhotoFormat format)
        {
            if (width <= 0 || height <= 0)
            {
                return PhotoValidationResult.Reject(PhotoRejectReason.DecodeFailed,
                    "读不出这张照片的宽高，文件可能不完整。",
                    "请重新选择原图，或另存为 PNG 后再试。",
                    bytes, format, width, height);
            }

            if (width < MinDimension || height < MinDimension)
            {
                return PhotoValidationResult.Reject(PhotoRejectReason.TooSmall,
                    $"照片只有 {width}×{height}，短边小于 {MinDimension} 像素，放大后会糊。",
                    $"请换一张至少 {MinDimension}×{MinDimension} 像素的清晰照片。",
                    bytes, format, width, height);
            }

            if (width > MaxDimension || height > MaxDimension)
            {
                return PhotoValidationResult.Reject(PhotoRejectReason.TooLargeDimension,
                    $"照片是 {width}×{height}，单边超过 {MaxDimension} 像素。",
                    $"请把照片长边缩到 {MaxDimension} 像素以内（例如 2048×2048）再上传。",
                    bytes, format, width, height);
            }

            long pixels = (long)width * height;
            if (pixels > MaxPixels)
            {
                return PhotoValidationResult.Reject(PhotoRejectReason.TooManyPixels,
                    $"照片是 {width}×{height}（约 {pixels / 1000000f:0.0} 百万像素），超过 {MaxPixels / 1000000} 百万像素上限。",
                    "请把照片缩小到 4000×4000 以内再上传，避免解码占用过多内存。",
                    bytes, format, width, height);
            }

            return null;
        }

        private static bool TryCheckDimensions(int width, int height, long bytes, PhotoFormat format,
            out PhotoValidationResult rejection)
        {
            var check = CheckDimensionsOrReject(width, height, bytes, format);
            rejection = check.HasValue ? check.Value : default;
            return check.HasValue;
        }

        /// <summary>只读文件头拿宽高（PNG 的 IHDR / JPEG 的 SOFn），失败返回 false 交给解码兜底。</summary>
        private static bool TryReadHeaderSize(byte[] bytes, PhotoFormat format, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                if (format == PhotoFormat.Png) return TryReadPngSize(bytes, out width, out height);
                if (format == PhotoFormat.Jpeg) return TryReadJpegSize(bytes, out width, out height);
            }
            catch (IndexOutOfRangeException)
            {
                // 头部被截断：交给解码器给出准确的「损坏」结论
            }
            return false;
        }

        private static bool TryReadPngSize(byte[] bytes, out int width, out int height)
        {
            width = 0;
            height = 0;
            // 8 字节签名 + 4 字节长度 + "IHDR" + 宽(4) + 高(4)
            if (bytes.Length < 24) return false;
            if (bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R') return false;

            width = ReadBigEndianInt32(bytes, 16);
            height = ReadBigEndianInt32(bytes, 20);
            return width > 0 && height > 0;
        }

        private static bool TryReadJpegSize(byte[] bytes, out int width, out int height)
        {
            width = 0;
            height = 0;
            int i = 2; // 跳过 SOI
            while (i + 3 < bytes.Length)
            {
                if (bytes[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                byte marker = bytes[i + 1];
                if (marker == 0xFF)
                {
                    i++;
                    continue;
                }

                // SOF0..SOF15（排除 DHT=C4 / JPG=C8 / DAC=CC）里带尺寸
                bool isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isSof && i + 8 < bytes.Length)
                {
                    height = (bytes[i + 5] << 8) | bytes[i + 6];
                    width = (bytes[i + 7] << 8) | bytes[i + 8];
                    return width > 0 && height > 0;
                }

                // 无长度的独立标记
                if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
                {
                    i += 2;
                    continue;
                }

                int segmentLength = (bytes[i + 2] << 8) | bytes[i + 3];
                if (segmentLength < 2) return false;
                i += 2 + segmentLength;
            }
            return false;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
            => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        private static bool MatchesPng(byte[] bytes)
        {
            for (int i = 0; i < PngSignature.Length; i++)
            {
                if (bytes[i] != PngSignature[i]) return false;
            }
            return true;
        }

        private static string SafeExtension(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            try
            {
                return Path.GetExtension(path) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L) return $"{bytes / 1024f / 1024f:0.0} MB";
            if (bytes >= 1024L) return $"{bytes / 1024f:0} KB";
            return $"{bytes} 字节";
        }

        /// <summary>销毁校验过程中产生的临时纹理（仅销毁自己 new 出来的，不碰视图持有的）。</summary>
        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }
    }

    /// <summary>
    /// 后台读盘结果（纯数据、纯 BCL，可在任意线程产生，方案 9.3「读取失败也要能解释原因」）。
    /// </summary>
    public readonly struct PhotoProbe
    {
        /// <summary>是否读到可用字节。</summary>
        public readonly bool Ok;

        /// <summary>拒绝原因代码。</summary>
        public readonly PhotoRejectReason ReasonCode;

        /// <summary>原因文案。</summary>
        public readonly string Reason;

        /// <summary>下一步建议。</summary>
        public readonly string Guidance;

        /// <summary>读取到的字节（失败时为 null）。</summary>
        public readonly byte[] Bytes;

        /// <summary>文件字节数。</summary>
        public readonly long FileBytes;

        /// <summary>魔数判定出的格式。</summary>
        public readonly PhotoFormat Format;

        private PhotoProbe(bool ok, PhotoRejectReason code, string reason, string guidance,
            byte[] bytes, long length, PhotoFormat format)
        {
            Ok = ok;
            ReasonCode = code;
            Reason = reason;
            Guidance = guidance;
            Bytes = bytes;
            FileBytes = length;
            Format = format;
        }

        private static PhotoProbe Fail(PhotoRejectReason code, string reason, string guidance, long length = 0)
            => new PhotoProbe(false, code, reason, guidance, null, length, PhotoFormat.Unknown);

        /// <summary>
        /// 只做「读字节 + 白名单 + 魔数」三步，<b>不调用任何 Unity API</b>，可安全地在后台线程执行。
        /// </summary>
        public static PhotoProbe Read(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Fail(PhotoRejectReason.NoFile,
                    "还没有选择照片。", "请先点「选择照片」挑一张 PNG 或 JPG。");
            }

            if (!PhotoInputValidator.HasAllowedExtension(path))
            {
                string ext = string.Empty;
                try { ext = Path.GetExtension(path) ?? string.Empty; }
                catch (ArgumentException) { ext = string.Empty; }

                return Fail(PhotoRejectReason.UnsupportedExtension,
                    string.IsNullOrEmpty(ext)
                        ? "只支持 PNG / JPG 格式的照片。"
                        : $"「{ext}」格式不受支持，只支持 .png / .jpg / .jpeg。",
                    "请先把照片另存为 PNG 或 JPG，再重新选择。");
            }

            long length;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return Fail(PhotoRejectReason.Unreadable,
                        "找不到这个文件，它可能已被移动、改名或删除。",
                        "请点「选择照片」重新选择一次。");
                }
                length = info.Length;
            }
            catch (Exception)
            {
                return Fail(PhotoRejectReason.Unreadable,
                    "无法访问这个文件（路径无效或没有读取权限）。",
                    "请把照片复制到「图片」或桌面等常用位置后再选择。");
            }

            if (length == 0)
            {
                return Fail(PhotoRejectReason.EmptyFile,
                    "这个文件是空的（0 字节）。",
                    "请重新选择一张能正常打开的照片。");
            }

            if (length > PhotoInputValidator.MaxBytes)
            {
                return Fail(PhotoRejectReason.TooLarge,
                    $"照片有 {length / 1024f / 1024f:0.0} MB，超过 8 MB 上限。",
                    "请把照片压缩或缩小到 8MB 以内再上传。", length);
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (UnauthorizedAccessException)
            {
                return Fail(PhotoRejectReason.Unreadable,
                    "没有读取这个文件的权限。", "请把照片复制到「图片」文件夹后再选择。", length);
            }
            catch (IOException)
            {
                return Fail(PhotoRejectReason.Unreadable,
                    "读取这个文件时出错（可能正被其它程序占用）。",
                    "请关闭正在编辑它的程序，或重新选择一张照片。", length);
            }
            catch (Exception)
            {
                return Fail(PhotoRejectReason.Unreadable,
                    "读取这个文件时出错。", "请重新选择一张照片。", length);
            }

            // 读盘期间文件可能变大：再确认一次，避免绕过上限
            if (bytes.Length == 0)
            {
                return Fail(PhotoRejectReason.EmptyFile,
                    "这个文件是空的（0 字节）。", "请重新选择一张能正常打开的照片。");
            }

            if (bytes.Length > PhotoInputValidator.MaxBytes)
            {
                return Fail(PhotoRejectReason.TooLarge,
                    "照片超过 8 MB 上限。", "请把照片压缩或缩小到 8MB 以内再上传。", bytes.Length);
            }

            PhotoFormat format = PhotoInputValidator.SniffSignature(bytes);
            if (format == PhotoFormat.Unknown)
            {
                return Fail(PhotoRejectReason.UnsupportedFormat,
                    "文件的真实内容不是 PNG 或 JPEG（可能只是改了扩展名，或文件已损坏）。",
                    "请用图片查看器确认能打开，并另存为 PNG 后重试。", bytes.Length);
            }

            return new PhotoProbe(true, PhotoRejectReason.None, string.Empty, string.Empty,
                bytes, bytes.Length, format);
        }
    }

    /// <summary>
    /// 照片解码任务：<b>后台线程只读字节</b>，<b>主线程解码</b>，宿主持有并每帧轮询。
    /// <para>
    /// 不用 <c>async void</c>：状态只在 <see cref="Poll"/>（主线程）里推进，
    /// 因此不存在「await 之后回到哪个线程」的隐患，也不会在对象被销毁后回调。
    /// </para>
    /// <para>取消后 <see cref="Poll"/> 直接返回 true 且不产生结果，已分配的字节随引用释放。</para>
    /// </summary>
    public sealed class PhotoDecodeJob
    {
        private readonly string _path;
        private readonly object _gate = new object();

        private PhotoProbe _probe;
        private int _probeDone;
        private bool _cancelled;
        private bool _finished;
        private PhotoValidationResult _result;

        private PhotoDecodeJob(string path)
        {
            _path = path;
        }

        /// <summary>已结束（成功、失败或取消）。</summary>
        public bool IsDone => _finished;

        /// <summary>已取消。</summary>
        public bool IsCancelled => _cancelled;

        /// <summary>结果（未结束时为默认值）。</summary>
        public PhotoValidationResult Result => _result;

        /// <summary>启动后台读盘。立即返回，不阻塞调用方。</summary>
        public static PhotoDecodeJob Start(string path)
        {
            var job = new PhotoDecodeJob(path);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                PhotoProbe probe = PhotoProbe.Read(path);
                lock (job._gate)
                {
                    job._probe = probe;
                    Volatile.Write(ref job._probeDone, 1);
                }
            });
            return job;
        }

        /// <summary>
        /// 主线程轮询：后台读完后就地解码（Unity API 必须在主线程）。
        /// 返回 true 表示任务已结束，此时读取 <see cref="Result"/>。
        /// </summary>
        public bool Poll()
        {
            if (_finished) return true;
            if (_cancelled)
            {
                _finished = true;
                return true;
            }

            if (Volatile.Read(ref _probeDone) == 0) return false;

            PhotoProbe probe;
            lock (_gate) probe = _probe;

            _result = probe.Ok
                ? PhotoInputValidator.ValidateBytes(probe.Bytes, _path)
                : PhotoValidationResult.Reject(probe.ReasonCode, probe.Reason, probe.Guidance,
                    probe.FileBytes, probe.Format);

            _finished = true;
            return true;
        }

        /// <summary>放弃本次解码：之后 <see cref="Poll"/> 不再产出结果。</summary>
        public void Cancel()
        {
            if (_finished) return;
            _cancelled = true;
        }
    }
}
