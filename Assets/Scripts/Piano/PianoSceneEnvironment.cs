using System.Collections.Generic;
using UnityEngine;
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
    /// <b>诚实性说明</b>（方案 0 节 / 审查 0.1）：内置渲染管线<b>没有</b>后处理栈，
    /// 本组件<b>没有实现真实的环境遮蔽（SSAO）或任何后处理</b>。
    /// 「接触阴影」= 主光软阴影 + 贴地极淡径向渐变薄片（程序化生成、透明度极低）<b>近似</b>；
    /// 它<b>不是</b> AO，也不是后处理。请勿在文档 / UI 中表述为「已实现环境遮蔽」。
    /// </para>
    /// <para>
    /// <b>性能</b>（方案 7.2）：几何 / 材质 / 贴图只在 <see cref="Build"/> 期生成（约 9 个 Renderer + 4 个灯），
    /// 组件<b>无 Update</b>，运行期零分配、零每帧查找；环境物体一律销毁 Collider（无物理需求）。
    /// </para>
    /// </summary>
    public class PianoSceneEnvironment : MonoBehaviour
    {
        // ==================== 场景数值（方案 9.2：数值集中在此，不散落到各脚本） ====================

        /// <summary>环境几何根物体名（受 <see cref="SetEnvironmentVisible"/> 控制）。</summary>
        private const string RootName = "SceneEnvironment";

        // —— 地面：忠实接替既有基线（位置 / 尺寸 / 深色基调不变，避免既有视觉基线整体跳变）——
        private const float FloorY = -0.03f;
        private const float FloorZ = 0.2f;
        private static readonly Vector3 FloorScale = new Vector3(6f, 0.06f, 6f);
        private static readonly Color FloorColor = new Color(0.105f, 0.115f, 0.135f);
        private const float FloorSmoothness = 0.25f; // 轻微粗糙度，避免地板成为镜面

        // —— 背景远景：低饱和、低对比的素色板，放在琴体后方足够远（相机为 +Z 侧，故背景在 -Z）——
        private static readonly Color BackdropColor = new Color(0.125f, 0.132f, 0.152f);
        private const float BackdropSmoothness = 0.10f;

        // —— 受控装饰（共 3 个，全部为极简体块、低对比，不抢教学焦点）——
        private static readonly Color TrimColor = new Color(0.085f, 0.090f, 0.104f);   // 踢脚线
        private static readonly Color FloorInsetColor = new Color(0.135f, 0.140f, 0.150f); // 地面中性色区域
        private static readonly Color DistantBlockColor = new Color(0.075f, 0.080f, 0.092f); // 远处暗色体块

        // —— 主光（保留既有方向 / 暖白 / 量级，仅按档位调阴影质量）——
        private static readonly Color MainLightColor = new Color(1f, 0.96f, 0.90f);
        private const float MainLightIntensity = 1.05f;   // 原基线 1.2；略降以抵消新增补光/轮廓光，使整体亮度基本持平
        private static readonly Vector3 MainLightEuler = new Vector3(42f, -32f, 0f);
        private const float MainLightShadowBias = 0.02f;
        private const float MainLightShadowNormalBias = 0.03f;

        // —— 补光：从主光对侧抬起暗部，冷一点、约主光 1/3，无阴影（方案 5.4） ——
        private static readonly Color FillColor = new Color(0.78f, 0.84f, 0.95f); // 低饱和冷色，不与冷蓝语义色争抢
        private const float FillIntensity = 0.30f;
        private static readonly Vector3 FillEuler = new Vector3(16f, 152f, 0f);

        // —— 轮廓光：从左侧后方打过（正向计算所得来光方向 ≈ (-0.55, +0.55, -0.63)），
        //    与主光 (+X,+Y,-Z)、补光 (-X,+Y,+Z) 构成三个互不重叠的来光方向；弱、无阴影 ——
        private static readonly Color RimColor = new Color(0.86f, 0.89f, 0.99f);
        private const float RimIntensity = 0.15f;
        private static readonly Vector3 RimEuler = new Vector3(33f, 41f, 0f);

        // —— 键盘上方点光：保留既有作用（提升琴键与手部可读性），位置 / 范围 / 强度与基线一致 ——
        private static readonly Color KeyLightColor = new Color(1f, 0.97f, 0.92f);
        private const float KeyLightIntensity = 1.1f;
        private const float KeyLightRange = 2.4f;
        private static readonly Vector3 KeyLightLocalPosition = new Vector3(0f, 1.65f, 0.35f);

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
        private Renderer _floorRenderer;
        private Renderer _aoDecalRenderer;
        private PianoKeyMap _keyMap;
        private bool _built;

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
                KeyLight.intensity = KeyLightIntensity;
            }

            // 环境遮蔽观感：内置管线无后处理栈 → 只切换贴地极淡暗色渐变薄片（近似，不是真实 AO）
            _aoApproxEnabled = quality.EnableAmbientOcclusion;
            RefreshEnvironmentVisibility();
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

        // ==================== 几何装配 ====================

        private void BuildGeometry()
        {
            if (_root == null)
            {
                _root = new GameObject(RootName).transform;
                _root.SetParent(transform, false);
            }

            BuildFloor();
            BuildBackdrop();
            BuildDecorations();
            BuildContactShadowDecals();
        }

        /// <summary>
        /// 地面：接替既有 <c>Floor</c>（位置 (0,-0.03,0.2)、尺寸 (6,0.06,6)、深色基调 (0.105,0.115,0.135) 全部保持不变）。
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
        /// 低干扰背景远景板：置于琴体后方、与地面远端对齐（<b>-Z</b>；相对全景机位即背景），
        /// 素色、低饱和、不做花纹、对比极低，保证不与暖金 / 冷蓝 / 暖橙教学语义色争注意力（审查 1.3B / 1.3C）。
        /// </summary>
        private void BuildBackdrop()
        {
            var mat = CreateLitMaterial("EnvBackdrop", BackdropColor, BackdropSmoothness, 0f);
            // z = -2.85：近面 -2.79，仅比地面远端 (-2.8) 内收 0.01 m，避免地平线处出现"漏底"的暗色缝隙
            Register(CreateBox("Backdrop", new Vector3(0f, 1.5f, -2.85f), new Vector3(7f, 3f, 0.12f), mat,
                castShadows: false, receiveShadows: true));
        }

        /// <summary>受控装饰：共 3 个极简体块（踢脚线 / 地面中性色区域 / 远处暗色体块），全部低对比。</summary>
        private void BuildDecorations()
        {
            var trimMat = CreateLitMaterial("EnvTrim", TrimColor, 0.12f, 0f);
            Register(CreateBox("Trim", new Vector3(0f, 0.06f, -2.78f), new Vector3(7f, 0.12f, 0.05f), trimMat,
                castShadows: false, receiveShadows: true));

            // 地面中性色区域（演奏区）：仅比地板略亮，作为安静的「舞台」暗示，不做花纹
            var insetMat = CreateLitMaterial("EnvFloorInset", FloorInsetColor, 0.08f, 0f);
            Register(CreateBox("FloorInset", new Vector3(0f, 0.0012f, 0.25f), new Vector3(2.6f, 0.002f, 1.6f), insetMat,
                castShadows: false, receiveShadows: true));

            // 远处暗色体块：给空间一点纵深参照，比背景板更暗，绝不形成高对比装饰
            var blockMat = CreateLitMaterial("EnvDistantBlock", DistantBlockColor, 0.10f, 0f);
            Register(CreateBox("DistantBlock", new Vector3(-3.1f, 0.35f, -2.6f), new Vector3(0.5f, 0.7f, 0.5f), blockMat,
                castShadows: false, receiveShadows: true));
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

            // 琴体 / 键盘前方 / 演奏者（琴凳与脚）三处足迹
            Register(CreateBox("ContactShadow_Piano", new Vector3(0f, DecalY, -0.10f),
                new Vector3(1.80f, DecalThickness, 0.85f), contactMat, castShadows: false, receiveShadows: false));
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
            MainLight = CreateDirectional("MainLight", MainLightColor, MainLightIntensity, MainLightEuler);
            FillLight = CreateDirectional("FillLight", FillColor, FillIntensity, FillEuler);
            RimLight = CreateDirectional("RimLight", RimColor, RimIntensity, RimEuler);
            KeyLight = CreatePoint("KeyLight", KeyLightColor, KeyLightIntensity, KeyLightRange, KeyLightLocalPosition);
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

            if (_floorRenderer != null)
            {
                if (!has) { bounds = _floorRenderer.bounds; has = true; }
                else bounds.Encapsulate(_floorRenderer.bounds);
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
        private static Material CreateLitMaterial(string name, Color color, float smoothness, float metallic)
        {
            var shader = Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader) { name = name, color = color };
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness); // Standard
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness); // URP Lit
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }

        /// <summary>
        /// 创建接触阴影薄片材质：透明 unlit（<c>Sprites/Default</c>）+ 径向渐变贴图，靠 α 做极淡的柔和衰减。
        /// 找不到透明 shader 时返回 null，由调用方诚实降级（不制造不透明硬边矩形）。
        /// </summary>
        private static Material CreateDecalMaterial(Color tint, Texture2D gradient)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "ContactShadowDecal", color = tint };
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
