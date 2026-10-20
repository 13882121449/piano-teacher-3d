using UnityEngine;

namespace PianoTeacher3D.Customization
{
    /// <summary>
    /// 纹理缩放 / 色阶工具（纯 CPU，<b>必须在主线程调用</b>：涉及 Unity 纹理对象）。
    /// <para>
    /// 用「区域平均」实现：缩小是盒式滤波（不会像最近邻那样丢细节后出现锯齿），
    /// 放大退化为最近邻。相同输入 + 相同参数得到逐像素一致的结果，便于测试与复现。
    /// </para>
    /// </summary>
    public static class TextureScaleUtility
    {
        /// <summary>
        /// 等比缩放到长边不超过 <paramref name="maxSize"/>；已经够小则返回原尺寸副本。
        /// 源纹理必须可读，否则返回 null（调用方负责给出可读原因）。
        /// </summary>
        public static Texture2D Downscale(Texture2D source, int maxSize)
        {
            if (source == null || !source.isReadable) return null;
            maxSize = Mathf.Clamp(maxSize, 1, 4096);

            int srcW = source.width;
            int srcH = source.height;
            if (srcW <= 0 || srcH <= 0) return null;

            int longSide = Mathf.Max(srcW, srcH);
            if (longSide <= maxSize) return Resize(source, srcW, srcH);

            float scale = maxSize / (float)longSide;
            int dstW = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
            int dstH = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));
            return Resize(source, dstW, dstH);
        }

        /// <summary>缩放到指定尺寸（区域平均）。源纹理必须可读。</summary>
        public static Texture2D Resize(Texture2D source, int width, int height)
        {
            if (source == null || !source.isReadable) return null;
            if (width <= 0 || height <= 0) return null;

            Color32[] srcPixels;
            try
            {
                srcPixels = source.GetPixels32();
            }
            catch (UnityException)
            {
                return null;
            }

            int srcW = source.width;
            int srcH = source.height;
            if (srcW <= 0 || srcH <= 0 || srcPixels.Length < srcW * srcH) return null;

            var dstPixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                int sy0 = (int)((long)y * srcH / height);
                int sy1 = (int)((long)(y + 1) * srcH / height);
                if (sy1 <= sy0) sy1 = sy0 + 1;
                if (sy0 >= srcH) sy0 = srcH - 1;
                if (sy1 > srcH) sy1 = srcH;

                for (int x = 0; x < width; x++)
                {
                    int sx0 = (int)((long)x * srcW / width);
                    int sx1 = (int)((long)(x + 1) * srcW / width);
                    if (sx1 <= sx0) sx1 = sx0 + 1;
                    if (sx0 >= srcW) sx0 = srcW - 1;
                    if (sx1 > srcW) sx1 = srcW;

                    int r = 0, g = 0, b = 0, a = 0, count = 0;
                    for (int sy = sy0; sy < sy1; sy++)
                    {
                        int rowBase = sy * srcW;
                        for (int sx = sx0; sx < sx1; sx++)
                        {
                            Color32 c = srcPixels[rowBase + sx];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                            count++;
                        }
                    }

                    if (count <= 0) count = 1;
                    dstPixels[y * width + x] = new Color32(
                        (byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));
                }
            }

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = $"Customization_Resize_{width}x{height}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            result.SetPixels32(dstPixels);
            result.Apply(false, false);
            return result;
        }

        /// <summary>
        /// 色阶量化 + 轻微暖色偏移（确定性变换，供本地模拟生成使用）。
        /// 量化把照片"风格化"，暖偏与 UI 的琥珀强调色呼应。
        /// </summary>
        public static void QuantizeAndTint(Color32[] pixels, int levels, int tintR, int tintG, int tintB)
        {
            if (pixels == null) return;
            levels = Mathf.Clamp(levels, 2, 256);
            float step = 255f / (levels - 1);

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                byte r = Quantize(c.r, step, tintR);
                byte g = Quantize(c.g, step, tintG);
                byte b = Quantize(c.b, step, tintB);
                pixels[i] = new Color32(r, g, b, c.a);
            }
        }

        private static byte Quantize(byte value, float step, int tint)
        {
            int q = Mathf.RoundToInt(value / step);
            int v = Mathf.RoundToInt(q * step) + tint;
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)v;
        }
    }
}
