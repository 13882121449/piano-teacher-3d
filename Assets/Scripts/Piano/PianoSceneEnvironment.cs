using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 教学场景环境（对应方案 5.1 第 6 项「环境与后处理」、方案 5.4「钢琴材质与灯光」、
    /// 审查 1.3B「建立小而完整的教学场景」、审查 1.3C「为教学目标优化」、
    /// 方案 7.1 / 7.2「设备档位与性能预算」、方案 9.2「配置与内容分离」、方案 9.3「场景装配与测试」）。
    /// <para>
    /// <b>职责边界</b>（不越界，避免重复创建对象）：
    /// ① 只负责「地面 / 低干扰背景远景 / 受控装饰 / 三点光照 + 键盘点光 / 接触阴影近似 / 内容包围盒 / 画质档位联动」；
    /// ② <b>不</b>创建琴凳（<c>AvatarManager</c> 已负责）、<b>不</b>创建钢琴本体与琴键（<c>PianoKeyMap</c> 已负责）、
    /// <b>不</b>修改相机背景色与环境光（会话层负责：<c>LessonSessionController.BuildScene</c> 第 7 段保留在会话层）。
    /// </para>
    /// <para>
    /// <b>灯光设计</b>（方案 5.4 / 审查 1.3B）：
    /// 采用「<b>1 个稳定主光</b>（唯一投影灯）+ <b>柔和填充光</b>（无阴影，抬暗部，保证肤色与白键对比）+
    /// <b>有限轮廓光</b>（无阴影，弱，把人物 / 琴体从背景中分出层次）」，
    /// 另保留原「键盘上方点光」提升琴键与手部可读性。刻意<b>避免多个强光造成琴键反光刺眼</b>。
    /// </para>
    /// <para>
    /// <b>诚实性说明</b>（方案 0 节 / 审查 0.1；写实化批次 2 修订）：
    /// URP 激活时本组件通过全局 Volume 提供真实后处理（ACES / Bloom / 暗角 / 色彩调整），
    /// 真实 SSAO 由 Renderer Feature 承担（<c>RenderPipelineSetup</c> 生成的管线资产内置）；
    /// 内置管线（未配置 URP）仍<b>没有实现真实的环境遮蔽（SSAO）或任何后处理</b>，行为与历史版本一致。
    /// 「接触阴影」= 主光软阴影 + 贴地极淡径向渐变薄片（程序化生成、透明度极低）<b>近似</b>——
    /// 即使在 URP 下它也<b>不是</b> SSAO（真实 AO 由管线资产承担），请勿在文档 / UI 中混用表述。
    /// </para>
    /// <para>
    /// <b>性能</b>（方案 7.2）：几何 / 材质 / 贴图只在 <see cref="Build"/> 期生成，
    /// 组件<b>无 Update</b>，运行期零分配、零每帧查找；环境物体一律销毁 Collider（无物理需求）。
    /// </para>
    /// </summary>
    public class PianoSceneEnvironment : MonoBehaviour
    {
        // ==================== 场景数值（方案 9.2：数值集中在此，不散落到各脚本） ====================

        /// <summary>环境几何根物体名（受 <see cref="SetEnvironmentVisible"/> 控制）。</summary>
        private const string RootName = "SceneEnvironment";

        // —— 水面下的兜底地板与琴台接触高度 ——
        private const float FloorY = -1.28f;
        private const float FloorZ = 0.2f;
        private static readonly Vector3 FloorScale = new Vector3(80f, 0.06f, 80f);
        private static readonly Color FloorColor = new Color(0.035f, 0.085f, 0.16f);
        private const float FloorSmoothness = 0.26f;
        private const float WaterY = -0.145f;
        private static readonly Vector3 PlatformCenter = new Vector3(0f, 0f, -0.32f);
        private static readonly int StageEffects = Shader.PropertyToID("_Effects");
        private static readonly int StageSequenceParameters = Shader.PropertyToID("_StageSequence");
        private static readonly int StageWaterTint = Shader.PropertyToID("_WaterTint");

        // —— 主光从较高的侧前方打在脸、双手和琴键上，缩短地面阴影 ——
        private static readonly Color MainLightColor = new Color(1f, 0.90f, 0.78f);
        private const float MainLightIntensity = 1.02f;
        private static readonly Vector3 MainLightEuler = new Vector3(56f, -48f, 0f);
        private const float MainLightShadowBias = 0.02f;
        private const float MainLightShadowNormalBias = 0.03f;

        // —— 补光：从主光对侧抬起暗部，冷一点、约主光 1/3，无阴影（方案 5.4） ——
        private static readonly Color FillColor = new Color(0.88f, 0.91f, 0.98f);
        private const float FillIntensity = 0.39f;
        private static readonly Vector3 FillEuler = new Vector3(16f, 152f, 0f);

        // —— 轮廓光弱于补光，分出演奏者和琴盖 ——
        private static readonly Color RimColor = new Color(0.58f, 0.71f, 1f);
        private const float RimIntensity = 0.38f;
        private static readonly Vector3 RimEuler = new Vector3(33f, 41f, 0f);

        // —— 键盘上方点光留作轻微提亮，URP 下按现有衰减系数折算 ——
        private static readonly Color KeyLightColor = new Color(1f, 0.97f, 0.92f);
        private const float KeyLightIntensity = 0.62f;
        private const float UrpPointLightScale = 0.42f;
        private const float KeyLightRange = 2.4f;
        private static readonly Vector3 KeyLightLocalPosition = new Vector3(0f, 1.65f, 0.35f);

        // —— 高光泛光只影响最亮的星芒和反光 ——
        private const float BloomThreshold = 1.3f;
        private const float BloomIntensityHigh = 0.24f; // 高画质档增强舞台发光，标准档使用材质中的光晕
        private const float BloomScatter = 0.55f;
        private const float VignetteIntensity = 0.10f;
        private const float VignetteSmoothness = 0.4f;
        private const float PostExposure = 0.1f;       // 轻微提亮补偿 Linear 迁移后的整体变暗
        private const float ContrastBoost = 3f;
        private const float SaturationBoost = -3f;

        // —— 接触阴影近似薄片（地面顶面 y = 0，薄片贴地 3 mm，透明度极低） ——
        private const float DecalY = 0.0030f;
        private const float DecalThickness = 0.002f;
        private static readonly Color ContactShadowTint = new Color(0f, 0f, 0f, 0.16f); // 极淡
        private static readonly Color AmbientOcclusionTint = new Color(0f, 0f, 0f, 0.08f); // 更淡（仅高画质档）

        // ==================== 公开只读状态（冻结 API） ====================

        /// <summary>主方向光（唯一投影灯）。可能为 null（未 Build 或创建失败），调用方需容错。</summary>
        public Light MainLight { get; private set; }

        /// <summary>补光（冷、弱、无阴影）。可能为 null，调用方需容错。</summary>
        public Light FillLight { get; private set; }

        /// <summary>轮廓光（背后 / 侧后方、弱、无阴影）。可能为 null，调用方需容错。</summary>
        public Light RimLight { get; private set; }

        /// <summary>键盘上方点光（提升琴键与手部可读性）。可能为 null，调用方需容错。</summary>
        public Light KeyLight { get; private set; }

        /// <summary>
        /// 场景内容包围盒（世界空间）：由「键盘 / 琴体（<c>keyMap</c> 及其子 Renderer）+ 地面」联合计算，
        /// 供相机取景与诊断使用。<see cref="Build"/> 返回后立即可用；无任何可统计物体时为键盘尺度兜底值。
        /// </summary>
        public Bounds ContentBounds { get; private set; }

        /// <summary>最近一次 <see cref="ApplyQuality"/> 生效的档位（供诊断 / 断言）。</summary>
        public QualityTier AppliedTier { get; private set; }

        // ==================== 内部状态 ====================

        private Transform _root;
        private ReflectionProbe _reflectionProbe;
        /// <summary>实际写入点光的强度（按管线缩放后；ApplyQuality 每档都会重写，必须用这个值）。</summary>
        private float _keyLightIntensity;
        // —— 后处理 Volume（写实化批次 2）：ACES 色调映射 / Bloom / 微暗角 / 色彩微调 ——
        private Volume _postVolume;
        private VolumeProfile _postProfile;
        private Bloom _bloom;
        private Tonemapping _tonemapping;
        private Vignette _vignette;
        private ColorAdjustments _colorAdjustments;
        private Renderer _floorRenderer;
        private Renderer _aoDecalRenderer;
        private Material _stageSky;
        private Material _stageWater;
        private Material _nearRibbon;
        private Material _farRibbon;
        private UnderwaterStage _underwater;
        private bool _hasStageCue;
        private PianoKeyMap _keyMap;
        private bool _built;
        private readonly List<Object> _ownedVisuals = new();

        private T Own<T>(T resource) where T : Object
        {
            if (resource != null) _ownedVisuals.Add(resource);
            return resource;
        }

        /// <summary>环境几何是否可见（调试对比用；灯光不受其影响）。</summary>
        private bool _environmentVisible = true;

        /// <summary>当前档位是否要求「环境遮蔽观感」的近似薄片（内置管线下 = 贴地暗色渐变，非真实 AO）。</summary>
        private bool _aoApproxEnabled;

        /// <summary>受可见性控制的全部环境几何 Renderer（缓存以避免每次切换都做 GetComponentsInChildren 分配）。</summary>
        private readonly List<Renderer> _environmentRenderers = new();

        // ==================== 装配 ====================

        /// <summary>
        /// 装配教学场景环境并应用画质档位。
        /// <para>
        /// <b>幂等</b>：重复调用不会重复创建地面 / 背景 / 装饰 / 灯光（第二次起只重新应用档位并刷新包围盒）。
        /// </para>
        /// <para>
        /// <paramref name="keyMap"/> 允许为 null（仅用于计算 <see cref="ContentBounds"/> 与琴体尺度参考）：
        /// 为 null 时包围盒退化为「地面 + 键盘尺度兜底」，不抛异常。
        /// <paramref name="quality"/> 允许为 null：此时使用 <see cref="QualityProfile.DetectRecommended"/> 的推荐档位。
        /// </para>
        /// </summary>
        public void Build(PianoKeyMap keyMap, QualityProfile quality)
        {
            if (keyMap != null) _keyMap = keyMap;
            if (quality == null) quality = QualityProfile.For(QualityProfile.DetectRecommended());

            if (!_built)
            {
                BuildGeometry();
                BuildLights();
                BuildReflectionProbe();
                BuildPostProcessVolume();
                _built = true;
            }

            ApplyQuality(quality);
            RecomputeContentBounds();
        }

        /// <summary>
        /// 应用 / 热切换画质档位（可重复调用）。
        /// <para>
        /// <b>可降级但不移除</b>（方案 7.1 / 5.4）：标准档只降低阴影分辨率、阴影距离、软硬与抗锯齿；
        /// 补光、轮廓光、键盘点光与琴键高亮一律保留（禁止「移除重要琴键高亮」换取性能）。
        /// </para>
        /// </summary>
        public void ApplyQuality(QualityProfile quality)
        {
            if (quality == null) quality = QualityProfile.For(QualityProfile.DetectRecommended());
            AppliedTier = quality.Tier;

            // 设备 / 画质判断统一落地到 QualitySettings 与 targetFrameRate（方案 7.2：不散落到各脚本）
            quality.Apply();

            // 主光：唯一投影灯；软硬 / 强度 / 分辨率随档位（方案 5.4「开启阴影前要测性能」）
            if (MainLight != null)
            {
                MainLight.shadows = quality.SoftShadows ? LightShadows.Soft : LightShadows.Hard;
                MainLight.shadowStrength = quality.ShadowStrength;
                MainLight.shadowCustomResolution = quality.ShadowResolution;
                MainLight.shadowBias = MainLightShadowBias;
                MainLight.shadowNormalBias = MainLightShadowNormalBias;
            }

            // 补光 / 轮廓光 / 键盘点光：恒定启用且不投影。
            // 只有一处投影灯 → 不会出现多套阴影叠加，也不会因「多个强光」造成琴键反光刺眼（方案 5.4）。
            if (FillLight != null)
            {
                FillLight.enabled = true;
                FillLight.shadows = LightShadows.None;
                FillLight.intensity = FillIntensity;
            }
            if (RimLight != null)
            {
                RimLight.enabled = true;
                RimLight.shadows = LightShadows.None;
                RimLight.intensity = RimIntensity;
            }
            if (KeyLight != null)
            {
                KeyLight.enabled = true; // 教学下限：任何档位都不得关闭键盘点光
                KeyLight.shadows = LightShadows.None;
                KeyLight.intensity = _keyLightIntensity > 0f ? _keyLightIntensity : KeyLightIntensity;
            }

            // 环境遮蔽观感：内置管线无后处理栈 → 只切换贴地极淡暗色渐变薄片（近似，不是真实 AO）
            _aoApproxEnabled = quality.EnableAmbientOcclusion;
            RefreshEnvironmentVisibility();

            float effects = quality.Tier == QualityTier.High ? 1f : 0.48f;
            if (_stageSky != null) _stageSky.SetFloat(StageEffects, effects);
            if (_stageWater != null) _stageWater.SetFloat(StageEffects, effects);
            if (_nearRibbon != null) _nearRibbon.SetFloat(StageEffects, effects);
            if (_farRibbon != null) _farRibbon.SetFloat(StageEffects, effects);
            _underwater?.SetQuality(quality.Tier == QualityTier.High);

            // 后处理按档分层（写实化批次 2）：URP 激活时 Bloom 只在高画质档呼吸发光，
            // Standard 档强度归零（等效关闭，不改 QualityProfile 结构）；ACES / 暗角 / 色彩两档共用。
            if (_bloom != null)
            {
                _bloom.intensity.Override(quality.Tier == QualityTier.High ? BloomIntensityHigh : 0f);
            }

            // 反射探针按档开关（写实化批次 5）：High 档启用并渲染一次；Standard 档关闭省开销。
            // 只在「从关到开」时渲染，避免每次改画质都重跑 6 面立方体渲染。
            if (_reflectionProbe != null)
            {
                bool wantProbe = quality.Tier == QualityTier.High;
                if (wantProbe && !_reflectionProbe.enabled)
                {
                    _reflectionProbe.enabled = true;
                    TryRenderProbe();
                }
                else if (!wantProbe && _reflectionProbe.enabled)
                {
                    _reflectionProbe.enabled = false;
                }
            }
        }

        /// <summary>
        /// 渲染一次反射探针。批处理 / 无图形设备时安全跳过（<c>-nographics</c> 下渲染会失败并刷警告），
        /// 失败不影响任何逻辑——反射缺失只是观感差一点，不影响琴键与指标。
        /// </summary>
        private void TryRenderProbe()
        {
            if (_reflectionProbe == null) return;
            if (!Application.isPlaying || Application.isBatchMode) return;
            try
            {
                _reflectionProbe.RenderProbe();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PianoSceneEnvironment] 反射探针渲染失败（不影响功能）：{ex.Message}");
            }
        }

        /// <summary>
        /// 反射探针（写实化批次 5）：琴漆面板与黄铜踏板的环境反射来源。
        /// <para>
        /// 只渲染一次（<c>RenderProbe</c>），不逐帧更新——钢琴 / 地面 / 背景板都是静态的，
        /// 场景没有会移动的反射体（角色不参与反射：探针盒只覆盖琴体一侧，教学场景也不需要倒影）。
        /// 盒投影（boxProjection）让平面上的反射方向随位置变化，避免「整块金属贴纸」的假感。
        /// </para>
        /// <para>
        /// 画质分档：只在 High 档启用（Standard 档关掉，省下一次 128² 立方体渲染）；
        /// 开关逻辑在 <see cref="ApplyQuality"/>，此处只负责创建。
        /// </para>
        /// </summary>
        private void BuildReflectionProbe()
        {
            var go = new GameObject("SceneReflectionProbe");
            go.transform.SetParent(transform, false);
            // 探针中心略高于琴体中心、向琴体一侧（-Z）偏：把琴首与背景板都纳入反射
            go.transform.localPosition = new Vector3(-0.06f, 0.86f, -0.6f);

            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 128;
            probe.hdr = true;
            probe.importance = 1;
            probe.boxProjection = true;
            probe.size = new Vector3(4.0f, 2.6f, 3.7f);
            // 场景没有 skybox：用中性冷色清屏，避免探针采到纯黑导致琴漆发死
            probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color(0.30f, 0.32f, 0.38f);
            probe.enabled = false;   // 由 ApplyQuality 按档位开启

            _reflectionProbe = probe;
        }

        /// <summary>
        /// 创建全局后处理 Volume（写实化批次 2，URP 专属；内置管线跳过）。
        /// <para>
        /// ACES 色调映射 + Bloom（按档调制，见 <see cref="ApplyQuality"/>）+ 微暗角 + 轻微色彩调整。
        /// SSAO 由 Renderer Feature 承担（<c>RenderPipelineSetup</c> 生成资产时挂上），不在此处。
        /// Volume 挂在本组件节点下（<b>不是</b> _root 下——几何可见性开关不得连累后处理），
        /// isGlobal 使其不受位置限制。profile 为运行时创建的 SO，OnDestroy 负责销毁。
        /// </para>
        /// </summary>
        private void BuildPostProcessVolume()
        {
            if (GraphicsSettings.currentRenderPipeline == null) return;   // 内置管线：无后处理栈，诚实跳过

            var go = new GameObject("PostProcessVolume");
            go.transform.SetParent(transform, false);

            _postVolume = go.AddComponent<Volume>();
            _postVolume.isGlobal = true;
            _postProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _postVolume.sharedProfile = _postProfile;

            _tonemapping = _postProfile.Add<Tonemapping>(true);
            _tonemapping.mode.Override(TonemappingMode.ACES);

            _bloom = _postProfile.Add<Bloom>(true);
            _bloom.threshold.Override(BloomThreshold);
            _bloom.intensity.Override(BloomIntensityHigh);   // 默认 High；Standard 档由 ApplyQuality 归零
            _bloom.scatter.Override(BloomScatter);

            _vignette = _postProfile.Add<Vignette>(true);
            _vignette.intensity.Override(VignetteIntensity);
            _vignette.smoothness.Override(VignetteSmoothness);

            _colorAdjustments = _postProfile.Add<ColorAdjustments>(true);
            _colorAdjustments.postExposure.Override(PostExposure);
            _colorAdjustments.contrast.Override(ContrastBoost);
            _colorAdjustments.saturation.Override(SaturationBoost);
        }

        private void OnDestroy()
        {
            // profile 是运行时创建的 ScriptableObject，不随 GameObject 销毁，必须手动释放
            if (_postProfile != null) DestroyNative(_postProfile);
            foreach (var resource in _ownedVisuals) DestroyNative(resource);
            _ownedVisuals.Clear();
            _postProfile = null;
            _postVolume = null;
            _bloom = null;
            _tonemapping = null;
            _vignette = null;
            _colorAdjustments = null;
        }

        /// <summary>
        /// 调试用：快速对比「有 / 无环境」。
        /// <para>
        /// 只开关<b>几何</b>（地面 / 背景 / 装饰 / 接触阴影薄片）；<b>不</b>关闭任何灯光 ——
        /// 否则「琴键与手部可读性」会随对比一起变化，失去对比意义，也违反方案 7.1 的降级边界。
        /// </para>
        /// </summary>
        public void SetEnvironmentVisible(bool visible)
        {
            _environmentVisible = visible;
            RefreshEnvironmentVisibility();
        }

        /// <summary>更新演奏展示的环境布光。进入教学时恢复稳定的常规曝光与舞台循环。</summary>
        public void ApplyStageCue(StageCue? cue)
        {
            if (!_built || (!cue.HasValue && !_hasStageCue)) return;
            _hasStageCue = cue.HasValue;
            var sample = cue.GetValueOrDefault();
            _underwater?.ApplyStageCue(cue);
            if (_stageSky != null)
            {
                var parameters = _hasStageCue
                    ? new Vector4(sample.Seconds, sample.SunStrength, sample.FogDensity, 0f)
                    : new Vector4(-1f, 1f, 1f, 0f);
                _stageSky.SetVector(StageSequenceParameters, parameters);
                _stageSky.SetVector(StageWaterTint, _hasStageCue ? (Vector4)sample.WaterTint : Vector4.one);
            }
            if (MainLight != null)
            {
                MainLight.color = _hasStageCue
                    ? Color.Lerp(MainLightColor, new Color(0.78f, 0.91f, 1f), sample.ShoalApproach * 0.68f)
                    : MainLightColor;
                MainLight.intensity = MainLightIntensity * (_hasStageCue
                    ? Mathf.Lerp(0.90f, 1.14f, Mathf.InverseLerp(0.45f, 1.5f, sample.SunStrength)) : 1f);
            }
            if (RimLight != null)
                RimLight.intensity = _hasStageCue ? RimIntensity + sample.ShoalApproach * 0.17f : RimIntensity;
            if (_colorAdjustments != null)
                _colorAdjustments.postExposure.Override(PostExposure + (_hasStageCue
                    ? (sample.SunStrength - 0.85f) * 0.22f : 0f));
        }

        // ==================== 几何装配 ====================

        private void BuildGeometry()
        {
            if (_root == null)
            {
                _root = new GameObject(RootName).transform;
                _root.SetParent(transform, false);
            }

            BuildFloor();
            BuildStarStage();
            BuildContactShadowDecals();
        }

        /// <summary>
        /// 地板保留原接触高度，接收钢琴、琴凳和人物的阴影。
        /// <para>轻微粗糙度、<b>接收</b>阴影但<b>不投射</b>阴影（<c>ShadowCastingMode.Off</c> + <c>receiveShadows = true</c>）。</para>
        /// </summary>
        private void BuildFloor()
        {
            var mat = CreateLitMaterial("EnvFloor", FloorColor, FloorSmoothness, 0f);
            var floor = CreateBox("Floor", new Vector3(0f, FloorY, FloorZ), FloorScale, mat,
                castShadows: false, receiveShadows: true);
            _floorRenderer = floor != null ? floor.GetComponent<Renderer>() : null;
            Register(floor);
        }

        /// <summary>
        /// 接触阴影近似：贴地的极淡径向渐变薄片（程序化生成，透明度极低），
        /// 与主光软阴影一起，让钢琴 / 琴凳 / 人物与地面有可信接触关系（审查 1.3B「避免物体漂浮」）。
        /// <para>
        /// <b>这不是环境遮蔽（AO），更不是后处理</b>：内置渲染管线没有后处理栈，此处只是几何近似。
        /// 薄片位置按会话既有布局估算（琴体居中于 X、演奏者在 +Z 侧），因此不依赖角色具体骨骼。
        /// </para>
        /// </summary>
        private void BuildContactShadowDecals()
        {
            var tex = CreateRadialGradientTexture(64);
            var contactMat = CreateDecalMaterial(ContactShadowTint, tex);
            if (contactMat == null)
            {
                // 找不到透明 shader：宁可不加薄片，也不制造不透明硬边矩形（诚实降级，方案 0 / 7.1）
                Debug.LogWarning("[PianoSceneEnvironment] 未找到可用透明 shader，接触阴影薄片已跳过（主光软阴影仍然生效）");
                if (tex != null) DestroyNative(tex);
                return;
            }

            Own(tex);

            // 三角琴与演奏者分别有接触阴影，保留键盘前部以突出脚和键的高度。
            Register(CreateBox("ContactShadow_Piano", new Vector3(-0.12f, DecalY, -0.70f),
                new Vector3(1.80f, DecalThickness, 1.90f), contactMat, castShadows: false, receiveShadows: false));
            Register(CreateBox("ContactShadow_KeyboardFront", new Vector3(0f, DecalY, 0.22f),
                new Vector3(1.55f, DecalThickness, 0.55f), contactMat, castShadows: false, receiveShadows: false));
            Register(CreateBox("ContactShadow_Performer", new Vector3(0f, DecalY, 0.55f),
                new Vector3(1.35f, DecalThickness, 1.00f), contactMat, castShadows: false, receiveShadows: false));

            // 「环境遮蔽观感」近似薄片：更大更淡，仅高画质档启用（标准档切换到 Standard 时会自动关闭）
            var aoMat = CreateDecalMaterial(AmbientOcclusionTint, tex);
            if (aoMat != null)
            {
                var ao = CreateBox("AmbientOcclusionApprox", new Vector3(0f, DecalY, 0.18f),
                    new Vector3(2.60f, DecalThickness, 1.70f), aoMat, castShadows: false, receiveShadows: false);
                _aoDecalRenderer = ao != null ? ao.GetComponent<Renderer>() : null;
                Register(ao);
            }
        }

        // ==================== 灯光装配 ====================

        private void BuildLights()
        {
            // URP 点光按物理单位衰减，同等强度比内置亮得多（实测琴体上半板被打成黄斑），
            // 因此只对点光做管线感知缩放；三盏平行光语义一致，不动。
            bool srp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            _keyLightIntensity = srp ? KeyLightIntensity * UrpPointLightScale : KeyLightIntensity;

            MainLight = CreateDirectional("MainLight", MainLightColor, MainLightIntensity, MainLightEuler);
            FillLight = CreateDirectional("FillLight", FillColor, FillIntensity, FillEuler);
            RimLight = CreateDirectional("RimLight", RimColor, RimIntensity, RimEuler);
            KeyLight = CreatePoint("KeyLight", KeyLightColor, _keyLightIntensity, KeyLightRange, KeyLightLocalPosition);
        }

        private Light CreateDirectional(string name, Color color, float intensity, Vector3 euler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None; // 由 ApplyQuality 按档位决定（仅主光会开启）
            return light;
        }

        private Light CreatePoint(string name, Color color, float intensity, float range, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None; // 点光阴影最贵；接触感由主光阴影 + 贴地薄片承担
            return light;
        }

        // ==================== 包围盒 ====================

        /// <summary>
        /// 重算 <see cref="ContentBounds"/> = 「键盘 / 琴体（<c>keyMap</c> 及其子 Renderer）+ 地面」的联合包围盒。
        /// 只在装配期调用（运行期无分配，方案 7.2）。
        /// </summary>
        private void RecomputeContentBounds()
        {
            Bounds bounds = default;
            bool has = false;

            if (_keyMap != null)
            {
                var renderers = _keyMap.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null) continue;
                    if (!has) { bounds = r.bounds; has = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }

            if (!has && _floorRenderer != null)
            {
                bounds = _floorRenderer.bounds;
                has = true;
            }

            if (!has)
            {
                // 兜底：无任何可统计物体时给出「键盘尺度」的保守盒（52 白键 × 24 mm × 键面高），
                // 避免相机 / 诊断拿到 0 尺寸包围盒
                float keySurfaceY = _keyMap != null ? _keyMap.KeySurfaceY : 0.742f;
                bounds = new Bounds(Vector3.zero,
                    new Vector3(PianoKeyMap.WhiteKeyCount * 0.024f, keySurfaceY, 0.5f));
            }

            ContentBounds = bounds;
        }

        // ==================== 可见性 ====================

        /// <summary>统一应用「环境可见性 × 档位」到全部环境几何（几何可能被整体隐藏，AO 近似薄片还受档位控制）。</summary>
        private void RefreshEnvironmentVisibility()
        {
            if (_root != null) _root.gameObject.SetActive(_environmentVisible);

            for (int i = 0; i < _environmentRenderers.Count; i++)
            {
                var r = _environmentRenderers[i];
                if (r == null) continue;
                r.enabled = _environmentVisible && ShouldEnable(r);
            }
        }

        private void Register(GameObject go)
        {
            if (go == null) return;
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            _environmentRenderers.Add(r);
            // 新加入的几何立即对齐当前可见性 / 档位状态（避免二次 Build 时遗留旧状态）
            r.enabled = _environmentVisible && ShouldEnable(r);
        }

        /// <summary>该几何在当前环境下是否应启用：「环境遮蔽观感」近似薄片只在对应档位启用，其余恒定启用。</summary>
        private bool ShouldEnable(Renderer r) =>
            !(_aoDecalRenderer != null && ReferenceEquals(r, _aoDecalRenderer) && !_aoApproxEnabled);

        private void BuildStarStage()
        {
            CreatePlatformLayer("MoonlitPlatformBase", 3.56f, -0.113f, 0.156f,
                new Color(0.072f, 0.125f, 0.20f), 0.35f);
            CreatePlatformLayer("MoonlitPlatformInlay", 3.61f, -0.037f, 0.018f,
                new Color(0.31f, 0.48f, 0.55f), 0.56f);
            CreatePlatformLayer("MoonlitPlatform", 3.54f, -0.022f, 0.044f,
                new Color(0.086f, 0.16f, 0.22f), 0.43f);

            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            var skyShader = Resources.Load<Shader>("StarStage/StarStageSky");
            var waterShader = Resources.Load<Shader>("StarStage/StarStageWater");
            if (skyShader == null || waterShader == null) return;
            var sceneryShader = Resources.Load<Shader>("StarStage/UnderwaterScenery");
            var sunShader = Resources.Load<Shader>("StarStage/UnderwaterSun");

            // 着色器可用时由水面和琴台承担可见地面。保留兜底地板的包围盒。
            _floorRenderer.forceRenderingOff = true;

            var sky = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sky.name = "StarSky";
            sky.transform.SetParent(_root, false);
            sky.transform.localScale = Vector3.one * 42f;
            DestroyCollider(sky);
            var dome = sky.GetComponent<Renderer>();
            _stageSky = Own(new Material(skyShader));
            dome.sharedMaterial = _stageSky;
            dome.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dome.receiveShadows = false;
            Register(sky);

            _stageWater = Own(new Material(waterShader) { renderQueue = 3010 });
            _stageWater.SetColor("_BaseColor", new Color(0.010f, 0.056f, 0.10f, 0.88f));
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "StarWater";
            water.transform.SetParent(_root, false);
            water.transform.localPosition = Vector3.up * WaterY;
            water.transform.localScale = new Vector3(8f, 1f, 8f);
            DestroyCollider(water);
            var surface = water.GetComponent<Renderer>();
            surface.sharedMaterial = _stageWater;
            surface.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surface.receiveShadows = false;
            Register(water);

            BuildPlatformInlay();
            if (sceneryShader != null && sunShader != null)
            {
                var habitat = new GameObject("UnderwaterHabitat");
                habitat.transform.SetParent(_root, false);
                _underwater = habitat.AddComponent<UnderwaterStage>();
                var platform = _root.Find("MoonlitPlatform").GetComponent<Renderer>();
                _underwater.Build(sceneryShader, sunShader, platform, mesh => Own(mesh), material => Own(material));
                foreach (var habitatRenderer in habitat.GetComponentsInChildren<Renderer>(true))
                    Register(habitatRenderer.gameObject);
                var etching = _root.Find("MoonlitPlatformEtching");
                if (etching != null) etching.GetComponent<Renderer>().forceRenderingOff = true;
                surface.forceRenderingOff = true;
                return;
            }

            BuildStageRibbons();

            if (_keyMap == null) return;
            var piano = _keyMap.transform.Find("PianoCase");
            if (piano == null) return;
            var reflection = Own(new Material(waterShader) { renderQueue = 3000 });
            reflection.SetColor("_BaseColor", new Color(0.12f, 0.37f, 0.70f, 0.84f));
            reflection.SetFloat("_Reflection", 1f);
            reflection.SetFloat("_WaterY", water.transform.position.y);
            // 镜像使用静态琴体的共用网格。水面覆盖远景，琴体仍遮挡其在前景的倒影。
            foreach (var piece in piano.GetComponentsInChildren<MeshFilter>())
            {
                if (piece.sharedMesh == null || !(piece.name.StartsWith("Grand", System.StringComparison.Ordinal) ||
                    piece.name.StartsWith("Leg_", System.StringComparison.Ordinal) || piece.name == "Lid")) continue;
                var copy = new GameObject("WaterReflection_" + piece.name);
                var source = piece.transform;
                copy.transform.SetParent(_root, false);
                copy.transform.SetPositionAndRotation(source.position, source.rotation);
                copy.transform.localScale = source.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = piece.sharedMesh;
                var renderer = copy.AddComponent<MeshRenderer>();
                renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 20f);
                renderer.sharedMaterial = reflection;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Register(copy);
            }
        }

        private void BuildStageRibbons()
        {
            var shader = Resources.Load<Shader>("StarStage/StageRibbon");
            if (shader == null) return;

            var root = new GameObject("StageRibbons");
            root.transform.SetParent(_root, false);
            _nearRibbon = Own(new Material(shader));
            _nearRibbon.SetColor("_Tone", new Color(0.08f, 0.56f, 0.68f));
            _farRibbon = Own(new Material(shader));
            _farRibbon.SetColor("_Tone", new Color(0.12f, 0.34f, 0.57f));
            var near = CreateStageRibbon(root.transform, "RibbonNear", _nearRibbon, 5.5f, 0.80f, 0.45f, -158f, 88f);
            var far = CreateStageRibbon(root.transform, "RibbonFar", _farRibbon, 8.2f, 1.55f, 0.58f, -178f, 96f);
            root.AddComponent<StageRibbonMotion>().Bind(near, far);
        }

        private Transform CreateStageRibbon(Transform parent, string name, Material material,
            float radius, float height, float width, float start, float sweep)
        {
            var ribbon = new GameObject(name);
            ribbon.transform.SetParent(parent, false);
            ribbon.AddComponent<MeshFilter>().sharedMesh = Own(StageRibbonMotion.CreateArc(radius, height, width, start, sweep));
            var renderer = ribbon.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Register(ribbon);
            return ribbon.transform;
        }

        private void CreatePlatformLayer(string layerName, float diameter, float height, float thickness,
            Color color, float smoothness)
        {
            const int segments = 96;
            var outline = new Vector2[segments];
            float radius = diameter * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * (2f * Mathf.PI / segments);
                outline[i] = radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            var layer = new GameObject(layerName);
            layer.transform.SetParent(_root, false);
            layer.transform.localPosition = PlatformCenter + Vector3.up * (height - thickness * 0.5f);
            layer.AddComponent<MeshFilter>().sharedMesh = Own(PianoMeshFactory.CreateGrandPlate(outline, Vector2.zero, thickness, 1f));
            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateLitMaterial(layerName, color, smoothness, 0f);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            Register(layer);
        }

        private void BuildPlatformInlay()
        {
            var shader = Resources.Load<Shader>("StarStage/StarStageInlay");
            if (shader == null) return;
            var inlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
            inlay.name = "MoonlitPlatformEtching";
            inlay.transform.SetParent(_root, false);
            inlay.transform.localPosition = PlatformCenter + Vector3.up * 0.001f;
            inlay.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            inlay.transform.localScale = Vector3.one * 3.5f;
            DestroyCollider(inlay);
            var renderer = inlay.GetComponent<Renderer>();
            renderer.sharedMaterial = Own(new Material(shader));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Register(inlay);
        }

        // ==================== 构建辅助 ====================

        /// <summary>创建程序化体块（无 Collider，无物理需求）。</summary>
        private GameObject CreateBox(string name, Vector3 localPosition, Vector3 localScale, Material mat,
            bool castShadows, bool receiveShadows)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var t = go.transform;
            t.SetParent(_root, false);
            t.localPosition = localPosition;
            t.localScale = localScale;

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (mat != null) r.sharedMaterial = mat;
                r.shadowCastingMode = castShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = receiveShadows;
            }

            DestroyCollider(go);
            return go;
        }

        /// <summary>
        /// 创建受光材质（Standard / URP Lit 兜底）。
        /// <para>数值通过参数传入，便于各环境物体设置不同粗糙度 / 金属度（审查 1.3A「材质分层」）。</para>
        /// </summary>
        private Material CreateLitMaterial(string name, Color color, float smoothness, float metallic)
        {
            // 写实化批次 2：由当前管线状态决定顺序（SRP 下 Standard 渲染为洋红，见 PianoKeyMap 同注释）
            var shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Sprites/Default")
                : Shader.Find("Standard")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = Own(new Material(shader) { name = name, color = color });
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness); // Standard
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness); // URP Lit
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }

        /// <summary>
        /// 创建接触阴影薄片材质：透明 unlit（<c>Sprites/Default</c>）+ 径向渐变贴图，靠 α 做极淡的柔和衰减。
        /// 找不到透明 shader 时返回 null，由调用方诚实降级（不制造不透明硬边矩形）。
        /// </summary>
        private Material CreateDecalMaterial(Color tint, Texture2D gradient)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader == null) return null;

            var mat = Own(new Material(shader) { name = "ContactShadowDecal", color = tint });
            if (gradient != null) mat.mainTexture = gradient;
            return mat;
        }

        /// <summary>
        /// 程序化生成径向 α 渐变贴图（中心不透明 → 边缘完全透明，平方衰减使边缘更淡）。
        /// 只用于近似接触阴影的柔和边界；不是 AO / 不是后处理。
        /// </summary>
        private static Texture2D CreateRadialGradientTexture(int size)
        {
            size = Mathf.Max(size, 8);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ContactShadowGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float inv = 1f / Mathf.Max(center, 1f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) * inv;
                    float dy = (y - center) * inv;
                    float d = Mathf.Sqrt(dx * dx + dy * dy); // 0 = 中心，1 = 贴图边缘
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// 销毁纯视觉件上的 Collider（环境无物理需求，避免误触）。
        /// <para>
        /// 运行期用 <c>Destroy</c>（延迟到帧末）；<b>EditMode（不处于播放状态）必须用 <c>DestroyImmediate</c></b>，
        /// 否则 Unity 会报 "Destroy may not be called from edit mode"。
        /// 这样本组件既能被运行期装配调用，也能被 EditMode 单测直接装配。
        /// </para>
        /// </summary>
        private static void DestroyCollider(GameObject go)
        {
            if (go == null) return;
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }

        /// <summary>销毁运行期创建的贴图等原生对象（运行期 Destroy / EditMode DestroyImmediate）。</summary>
        private static void DestroyNative(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
