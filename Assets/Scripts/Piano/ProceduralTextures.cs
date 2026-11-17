using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 程序化纹理：木纹 / 象牙 / 红绒。
    ///
    /// <para>
    /// 为什么在运行时生成而不是存成图片：项目坚持「零第三方资产」，而生成式木纹需要的
    /// 只是几层噪声叠加，代码不到百行。代价是启动时一次性算 3 张 256² 贴图（约 0.3–0.6 s），
    /// 换来的是仓库体积零增长、且参数可随画质档热调。
    /// </para>
    ///
    /// <para>
    /// 配合 <see cref="PianoMeshFactory"/> 的顶点级三平面 UV（不再需要拆顶点做 UV 接缝），
    /// 琴体各面的木纹方向都是对的：竖直面沿高度走、水平面沿长度走。
    /// </para>
    /// </summary>
    public static class ProceduralTextures
    {
        private const int WalnutSize = 256;
        private const int IvorySize = 128;
        private const int FeltSize = 96;

        private static Texture2D _walnut;
        private static Texture2D _ivory;
        private static Texture2D _felt;

        /// <summary>胡桃木：年轮条纹 + 沿纹理方向的长纤维 + 细微斑驳。</summary>
        public static Texture2D Walnut()
        {
            if (_walnut != null) return _walnut;

            int n = WalnutSize;
            var px = new Color32[n * n];
            var dark = new Color(0.128f, 0.082f, 0.055f);
            var mid = new Color(0.225f, 0.152f, 0.110f);
            var light = new Color(0.320f, 0.228f, 0.165f);

            for (int y = 0; y < n; y++)
            {
                float v = y / (float)n;
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n;

                    // 年轮：低频噪声扰动下的周期条纹，模拟树干同心圆被锯开后的弧线
                    float warp = Fbm(u * 2.2f, v * 2.2f, 3) - 0.5f;
                    float rings = Mathf.Sin((v * 13f + warp * 5.5f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    rings *= rings;   // 二次方锐化：让暗线细、亮带宽（真实年轮的疏密比）

                    // 长纤维：沿 v 方向高度拉伸的噪声，形成木料的顺纹
                    float fiber = Fbm(u * 26f, v * 2.5f, 2);
                    float blotch = Fbm(u * 5f, v * 5f, 2);

                    float t = Mathf.Clamp01(rings * 0.52f + fiber * 0.30f + blotch * 0.18f);
                    var c = t < 0.5f
                        ? Color.Lerp(dark, mid, t * 2f)
                        : Color.Lerp(mid, light, (t - 0.5f) * 2f);

                    px[y * n + x] = c;
                }
            }

            _walnut = Build(px, n, "ProceduralWalnut");
            return _walnut;
        }

        /// <summary>象牙白键：极淡的暖白 + 细微纵向纹路 + 轻微磨损（常按区域略深）。</summary>
        public static Texture2D Ivory()
        {
            if (_ivory != null) return _ivory;

            int n = IvorySize;
            var px = new Color32[n * n];
            var baseColor = new Color(0.930f, 0.918f, 0.880f);
            var grainColor = new Color(0.878f, 0.858f, 0.812f);

            for (int y = 0; y < n; y++)
            {
                float v = y / (float)n;
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n;
                    // 纵向细纹（键长方向），幅度极小 —— 象牙/仿象牙的纹理本来就很淡
                    float grain = Fbm(u * 30f, v * 3f, 2);
                    float wear = Fbm(u * 4f, v * 4f, 2);
                    float t = grain * 0.45f + wear * 0.25f;
                    px[y * n + x] = Color.Lerp(baseColor, grainColor, Mathf.Clamp01(t));
                }
            }

            _ivory = Build(px, n, "ProceduralIvory");
            return _ivory;
        }

        /// <summary>红绒：短绒噪声，高粗糙度，无方向性。</summary>
        public static Texture2D Felt()
        {
            if (_felt != null) return _felt;

            int n = FeltSize;
            var px = new Color32[n * n];
            var dark = new Color(0.48f, 0.075f, 0.075f);
            var light = new Color(0.78f, 0.165f, 0.155f);

            for (int y = 0; y < n; y++)
            {
                float v = y / (float)n;
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n;
                    // 高频各向同性噪声 = 绒毛
                    float fuzz = Fbm(u * 55f, v * 55f, 2);
                    px[y * n + x] = Color.Lerp(dark, light, Mathf.Clamp01(fuzz));
                }
            }

            _felt = Build(px, n, "ProceduralFelt");
            return _felt;
        }

        private static Texture2D Build(Color32[] px, int size, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        // ---- 确定性 value noise + fbm（无第三方依赖，结果可复现）----

        private static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        private static float ValueNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            // smoothstep 插值：避免线性插值的菱形格纹
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);

            float a = Hash(xi, yi);
            float b = Hash(xi + 1, yi);
            float c = Hash(xi, yi + 1);
            float d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        /// <summary>分形布朗运动：多个八度的 value noise 叠加，返回 [0,1]。</summary>
        private static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(x * freq, y * freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return norm > 1e-6f ? sum / norm : 0.5f;
        }

        /// <summary>释放运行时创建的纹理（场景销毁时由持有方调用）。</summary>
        public static void ReleaseAll()
        {
            if (_walnut != null) { Object.Destroy(_walnut); _walnut = null; }
            if (_ivory != null) { Object.Destroy(_ivory); _ivory = null; }
            if (_felt != null) { Object.Destroy(_felt); _felt = null; }
        }
    }
}
