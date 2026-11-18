using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PianoTeacher3D.Rendering
{
    /// <summary>
    /// 毛玻璃界面的背景模糊（iOS 风格材质的数据源）。
    /// <para>
    /// 在后处理之后把相机画面降采样，做双重 Kawase 模糊（先逐级缩小、再逐级放大），
    /// 结果写入全局纹理 <see cref="BlurTextureName"/>。UI 着色器 <c>PianoTeacher3D/UI/FrostedGlass</c>
    /// 按屏幕坐标采样它，面板就能透出背后被虚化的 3D 场景。
    /// </para>
    /// <para>
    /// 模糊链从 1/<see cref="Settings.Downsample"/> 分辨率开始，1280×720 下第一级只有 320×180，
    /// 每帧开销远小于一次全分辨率后处理。只处理 Game 相机；场景视图、预览、反射探针相机不参与。
    /// </para>
    /// </summary>
    public sealed class GlassBlurFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            [Tooltip("第一级降采样倍数（相对相机分辨率）")]
            [Range(2, 8)] public int Downsample = 4;

            [Tooltip("Kawase 级数：每多一级，模糊半径约翻倍")]
            [Range(1, 5)] public int Iterations = 3;

            [Tooltip("采样偏移（以源纹素为单位）")]
            [Range(0.5f, 3f)] public float Offset = 1.25f;
        }

        /// <summary>UI 着色器读取的全局模糊纹理名。</summary>
        public const string BlurTextureName = "_PT3D_GlassBlurTex";

        /// <summary>模糊纹理就绪标记（全局浮点）：未就绪时 UI 着色器退回半透明纯色。</summary>
        public const string BlurReadyName = "_PT3D_GlassBlurReady";

        private const string ShaderName = "Hidden/PianoTeacher3D/GlassBlur";

        public Settings settings = new Settings();

        private Material _material;
        private GlassBlurPass _pass;

        public override void Create()
        {
            var shader = Shader.Find(ShaderName);
            _material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
            _pass = new GlassBlurPass(settings)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!IsEligible(renderingData.cameraData)) return;
            renderer.EnqueuePass(_pass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!IsEligible(renderingData.cameraData)) return;
            _pass.Setup(renderer.cameraColorTargetHandle, _material);
        }

        private bool IsEligible(CameraData cameraData)
            => _material != null && _pass != null && cameraData.cameraType == CameraType.Game;

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            CoreUtils.Destroy(_material);
            _material = null;
        }
    }

    /// <summary>
    /// 双重 Kawase 模糊：第 0 级从相机画面降采样，逐级缩小到最小一级，再逐级放大回第 0 级。
    /// 最终的第 0 级纹理作为全局纹理留到本帧 UI 绘制（Overlay 画布在所有相机之后绘制）。
    /// </summary>
    internal sealed class GlassBlurPass : ScriptableRenderPass
    {
        private const int MaxLevels = 6;
        private const int MinLevelSize = 8;
        private const int DownPass = 0;
        private const int UpPass = 1;

        private static readonly int BlurTexId = Shader.PropertyToID(GlassBlurFeature.BlurTextureName);
        private static readonly int BlurReadyId = Shader.PropertyToID(GlassBlurFeature.BlurReadyName);
        private static readonly int SourceTexelId = Shader.PropertyToID("_GlassSourceTexel");
        private static readonly int OffsetId = Shader.PropertyToID("_GlassOffset");

        private readonly GlassBlurFeature.Settings _settings;
        private readonly RTHandle[] _levels = new RTHandle[MaxLevels];
        private readonly Vector2Int[] _sizes = new Vector2Int[MaxLevels];
        private readonly ProfilingSampler _sampler = new ProfilingSampler("PT3D Glass Blur");

        private RTHandle _source;
        private Material _material;
        private int _levelCount;
        private Vector2Int _sourceSize;

        public GlassBlurPass(GlassBlurFeature.Settings settings)
        {
            _settings = settings;
        }

        public void Setup(RTHandle source, Material material)
        {
            _source = source;
            _material = material;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            _sourceSize = new Vector2Int(Mathf.Max(1, desc.width), Mathf.Max(1, desc.height));

            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;

            int downsample = Mathf.Clamp(_settings.Downsample, 2, 8);
            int width = Mathf.Max(MinLevelSize, _sourceSize.x / downsample);
            int height = Mathf.Max(MinLevelSize, _sourceSize.y / downsample);
            int wanted = Mathf.Clamp(_settings.Iterations, 1, MaxLevels - 1) + 1;

            _levelCount = 0;
            for (int i = 0; i < wanted; i++)
            {
                int w = Mathf.Max(MinLevelSize, width >> i);
                int h = Mathf.Max(MinLevelSize, height >> i);
                if (i > 0 && w == _sizes[i - 1].x && h == _sizes[i - 1].y) break;
                desc.width = w;
                desc.height = h;
                _sizes[i] = new Vector2Int(w, h);
                RenderingUtils.ReAllocateIfNeeded(ref _levels[i], desc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: LevelName(i));
                _levelCount++;
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null || _source == null || _levelCount == 0) return;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _sampler))
            {
                cmd.SetGlobalFloat(OffsetId, _settings.Offset);

                SetSourceTexel(cmd, _sourceSize);
                Blitter.BlitCameraTexture(cmd, _source, _levels[0], _material, DownPass);

                for (int i = 1; i < _levelCount; i++)
                {
                    SetSourceTexel(cmd, _sizes[i - 1]);
                    Blitter.BlitCameraTexture(cmd, _levels[i - 1], _levels[i], _material, DownPass);
                }

                for (int i = _levelCount - 1; i > 0; i--)
                {
                    SetSourceTexel(cmd, _sizes[i]);
                    Blitter.BlitCameraTexture(cmd, _levels[i], _levels[i - 1], _material, UpPass);
                }

                cmd.SetGlobalTexture(BlurTexId, _levels[0]);
                cmd.SetGlobalFloat(BlurReadyId, 1f);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static void SetSourceTexel(CommandBuffer cmd, Vector2Int size)
        {
            cmd.SetGlobalVector(SourceTexelId, new Vector4(1f / size.x, 1f / size.y, size.x, size.y));
        }

        private static string LevelName(int index) => index switch
        {
            0 => "_PT3D_GlassBlur0",
            1 => "_PT3D_GlassBlur1",
            2 => "_PT3D_GlassBlur2",
            3 => "_PT3D_GlassBlur3",
            4 => "_PT3D_GlassBlur4",
            _ => "_PT3D_GlassBlur5"
        };

        public void Dispose()
        {
            for (int i = 0; i < _levels.Length; i++)
            {
                _levels[i]?.Release();
                _levels[i] = null;
            }
        }
    }
}
