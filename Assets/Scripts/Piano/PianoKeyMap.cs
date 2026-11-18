using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 维护 MIDI 音高与琴键对象的映射关系（方案 4.2）。
    /// 采用真实钢琴比例（键宽 24mm / 键长 150mm / 键面高 0.72m），
    /// 并程序化生成开盖三角琴（独立琴键、弧形琴壳、音板、琴弦、三腿和三踏板），
    /// 零外部模型资产。
    /// <para>
    /// <b>几何精修（方案 5.4 / 审查 1.3A 第 1、3 项）</b>：
    /// 白键与黑键顶面改为带细小倒角的键身（<see cref="PianoMeshFactory"/>），
    /// 88 键的真实坐标、键长/键宽/键高常量与"键根位于键后端、绕局部 X 轴下压"的语义<b>完全不变</b>。
    /// </para>
    /// <para>
    /// <b>性能</b>：白键 1 份网格、黑键 3 份网格、材质 4~7 份，全部复用；
    /// 88 个键不产生任何独立网格或材质，倒角网格只在 <see cref="BuildKeys"/> 中一次性创建。
    /// </para>
    /// <para>
    /// <b>高亮契约</b>：每个键根下第一个子物体固定命名为 <c>"Mesh"</c>，承载键身本体色，
    /// 也是 <see cref="PianoKeyHighlighter"/> 通过 <c>GetComponentInChildren&lt;Renderer&gt;()</c> 命中的唯一渲染器；
    /// 装饰件（黑键侧壁 / 底部支撑）挂在不参与高亮的 <c>"Trim"</c> 子物体下。
    /// </para>
    /// </summary>
    public class PianoKeyMap : MonoBehaviour
    {
        public const int MinPitch = 21;  // A0
        public const int MaxPitch = 108; // C8
        public const int KeyCount = 88;
        public const int WhiteKeyCount = 52;

        // ==================== 默认几何常量 ====================
        // 这些常量即字段默认值，同时供纯逻辑单测（EditMode，无 GameObject）直接引用。

        /// <summary>默认白键宽（米，真实钢琴 24mm）。</summary>
        public const float DefaultWhiteWidth = 0.024f;

        /// <summary>默认白键长（米，真实钢琴 150mm）。</summary>
        public const float DefaultWhiteLength = 0.15f;

        /// <summary>默认白键高（米）。</summary>
        public const float DefaultWhiteHeight = 0.022f;

        /// <summary>默认黑键宽（米）。</summary>
        public const float DefaultBlackWidth = 0.014f;

        /// <summary>默认黑键长（米）。</summary>
        public const float DefaultBlackLength = 0.10f;

        /// <summary>默认黑键高（米，比白键高 10mm）。</summary>
        public const float DefaultBlackHeight = 0.032f;

        /// <summary>默认键床高度（地面至键面下沿，米）。</summary>
        public const float DefaultKeybedHeight = 0.72f;

        [Header("键体几何（真实钢琴比例，单位：米）")]
        public float WhiteWidth = DefaultWhiteWidth;
        public float WhiteLength = DefaultWhiteLength;
        public float WhiteHeight = DefaultWhiteHeight;
        public float BlackWidth = DefaultBlackWidth;
        public float BlackLength = DefaultBlackLength;
        public float BlackHeight = DefaultBlackHeight;

        [Header("键面倒角（方案 5.4 / 审查 1.3A：细小倒角 + 柔和高光，单位：米）")]
        public float WhiteKeyChamfer = 0.0008f;  // 约 0.8mm，白键顶面四周
        public float BlackKeyChamfer = 0.0012f;  // 约 1.2mm：真实黑键顶棱是圆润过渡，0.6mm 读起来像切角

        [Header("键盘高度（地面至键面下沿，本项目标定为 0.72m）")]
        public float KeybedHeight = DefaultKeybedHeight;

        // 黑键三层次的高度占比（顶面 / 侧壁 / 底部支撑），三者之和必须为 1。
        private const float BlackTopRatio = 0.62f;
        private const float BlackSideRatio = 0.20f;
        private const float BlackBaseRatio = 0.18f;

        private readonly Dictionary<int, Transform> _keyRoots = new();

        // 复用的网格与材质的持有者，ClearKeys 时统一释放（避免重复调用 BuildKeys 造成泄漏）
        private readonly List<Mesh> _ownedMeshes = new();
        private readonly List<Material> _ownedMaterials = new();

        private Transform _caseRoot;
        private Transform _underwaterCase;
        private Transform _cinematicCase;
        private readonly List<Transform> _classicCaseOnly = new();
        private readonly List<Transform> _legacyParts = new();
        private readonly List<(Renderer surface, Material classic, Material underwater)> _caseFinishes = new();

        /// <summary>右侧延音踏板实际铰链，绕本地 X 正转时鞋掌接触点下降。</summary>
        public Transform RightPedalHinge { get; private set; }

        /// <summary>延音踏板的鞋掌接触面标记，随踏板运动。</summary>
        public Transform RightPedalContact { get; private set; }

        /// <summary>右脚跟在琴前地面上的参考落点。</summary>
        public Transform RightHeelAnchor { get; private set; }

        /// <summary>成人琴凳中心的席面参考点，用于新角色校准。</summary>
        public Transform BenchAnchor { get; private set; }

        // 复用网格（每次 BuildKeys 只创建一份）
        private Mesh _whiteKeyMesh;
        private Mesh _blackTopMesh;
        private Mesh _blackSideMesh;
        private Mesh _blackBaseMesh;

        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");   // Standard
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");   // URP Lit

        /// <summary>pitch → 键根 Transform（pivot 位于键后端，按下绕其局部 X 轴旋转）。</summary>
        public IReadOnlyDictionary<int, Transform> KeyRoots => _keyRoots;

        /// <summary>钢琴总宽度（52 个白键）。</summary>
        public float TotalWidth => WhiteKeyCount * WhiteWidth;

        /// <summary>键面（键身顶面）的世界 Y 高度，供手部 IK 对齐。</summary>
        public float KeySurfaceY => KeybedHeight + WhiteHeight;

        // 黑键三层次的实际高度
        private float BlackTopHeight => BlackHeight * BlackTopRatio;
        private float BlackSideHeight => BlackHeight * BlackSideRatio;
        private float BlackBaseHeight => BlackHeight * BlackBaseRatio;

        public static bool IsInRange(int pitch) => pitch >= MinPitch && pitch <= MaxPitch;

        /// <summary>是否为黑键（与 <see cref="MIDI.PianoNoteFilter.IsBlackKey"/> 共用同一判定）。</summary>
        public static bool IsBlackKey(int pitch) => MIDI.PianoNoteFilter.IsBlackKey(pitch);

        public bool TryGetKey(int pitch, out Transform key) => _keyRoots.TryGetValue(pitch, out key);

        /// <summary>程序化生成琴体与 88 键并建立映射。可重复调用（先清空旧对象）。</summary>
        public void BuildKeys()
        {
            ClearKeys();
            BuildCase();

            // ---- 材质（白键 1 份、黑键顶面 1 份、黑键侧壁/支撑 1 份；禁止每键 new Material）----
            // 黑键 albedo 从 0.07 提到 0.13 并给非零光泽度，避免"完全无反射的纯黑"（方案 5.4）。
            // 2026-11-18 精修：白键用干净的暖白清漆键面（现代钢琴亚克力键面的柔和高光），
            // 黑键顶面与侧壁统一为乌木清漆，侧壁不再发灰。材质资产缺失时退回原程序化材质。
            Material sharedWhite = PianoSharedMaterials.Get("PianoKeyIvory");
            Material sharedBlack = PianoSharedMaterials.Get("PianoKeyEbony");
            Material whiteMat = sharedWhite ?? CreateMaterial("KeyWhite", new Color(0.96f, 0.96f, 0.94f), 0.00f, 0.32f);
            Material blackTopMat = sharedBlack ?? CreateMaterial("KeyBlackTop", new Color(0.11f, 0.10f, 0.10f), 0.00f, 0.30f);
            Material blackSideMat = sharedBlack ?? CreateMaterial("KeyBlackSide", new Color(0.055f, 0.055f, 0.062f), 0.00f, 0.15f);

            // 程序化象牙纹理（写实化二期）：顶点级三平面 UV 已就位，贴图可以直接用。
            // 基色归白是必须的——贴图自带颜色，若仍乘原来的 0.96 白，会再压暗一层。
            if (sharedWhite == null) ApplyProceduralTexture(whiteMat, ProceduralTextures.Ivory(), 1f);

            // ---- 网格（白键 1 份、黑键 3 份，全部复用于 88 键）----
            // 白键从 TopOnly 改为 AllEdges：真实白键的前端是圆润收边的，只有顶面倒角时
            // 前端呈直角切边，近景里像一片「平贴的塑料板」。AllEdges 顶点 24（TopOnly 12），
            // 但 88 键共享同一 mesh，总开销不变。
            _whiteKeyMesh = Track(PianoMeshFactory.CreateChamferedBox(
                WhiteWidth, WhiteHeight, WhiteLength, WhiteKeyChamfer, ChamferMode.AllEdges));
            _blackTopMesh = Track(PianoMeshFactory.CreateChamferedBox(
                BlackWidth, BlackTopHeight, BlackLength, BlackKeyChamfer, ChamferMode.AllEdges));
            _blackSideMesh = Track(PianoMeshFactory.CreateChamferedBox(
                BlackWidth, BlackSideHeight, BlackLength, BlackKeyChamfer * 0.5f, ChamferMode.AllEdges));
            _blackBaseMesh = Track(PianoMeshFactory.CreateChamferedBox(
                BlackWidth * 0.88f, BlackBaseHeight, BlackLength * 0.92f, BlackKeyChamfer * 0.5f, ChamferMode.AllEdges));

            for (int pitch = MinPitch; pitch <= MaxPitch; pitch++)
            {
                bool black = IsBlackKey(pitch);
                float keyWidth = black ? BlackWidth : WhiteWidth;

                // 【左右约定】低音必须落在演奏者的左手侧 = 世界 +X 侧 = 教学相机画面左侧。
                // 演奏者位于 +Z、面向 -Z 弹奏，因此其左手在世界 +X；钢琴键盘也必须按同一方向摆放，
                // 否则整个场景左右镜像（低音跑到画面右侧，与乐谱/钢琴的通用方向相反）。
                // 索引仍按音高递增（WhiteIndex/BlackKeyLeftWhiteIndex 语义不变，便于单测），
                // 只在换算到世界坐标时镜像：x = 键盘总宽 − 原x − 键宽。
                float indexX = black ? (BlackKeyLeftWhiteIndex(pitch) + 0.5f) * WhiteWidth
                                     : WhiteIndex(pitch) * WhiteWidth;
                float x = TotalWidth - indexX - keyWidth;

                float keyLength = black ? BlackLength : WhiteLength;

                // 键根：位于键后端（-Z），绕局部 X 轴旋转实现按下（语义与改造前完全一致）
                var root = new GameObject($"Key_{pitch}").transform;
                root.SetParent(transform, false);
                root.localPosition = new Vector3(x, KeybedHeight, -keyLength * 0.5f);

                if (black) BuildBlackKeyVisual(root, blackTopMat, blackSideMat);
                else BuildWhiteKeyVisual(root, whiteMat);

                _keyRoots[pitch] = root;
            }
        }

        public void ClearKeys()
        {
            foreach (var kv in _keyRoots)
            {
                if (kv.Value != null) { kv.Value.gameObject.SetActive(false); Release(kv.Value.gameObject); }
            }
            _keyRoots.Clear();

            if (_caseRoot != null) { _caseRoot.gameObject.SetActive(false); Release(_caseRoot.gameObject); }
            _caseRoot = null;
            _underwaterCase = null;
            _cinematicCase = null;
            _classicCaseOnly.Clear();
            _legacyParts.Clear();
            _caseFinishes.Clear();
            RightPedalHinge = RightPedalContact = RightHeelAnchor = BenchAnchor = null;
            ReleaseVisuals();

        }

        private void ReleaseVisuals()
        {
            foreach (var mesh in _ownedMeshes) Release(mesh);
            foreach (var material in _ownedMaterials) Release(material);
            _ownedMeshes.Clear();
            _ownedMaterials.Clear();
            _whiteKeyMesh = _blackTopMesh = _blackSideMesh = _blackBaseMesh = null;
        }

        private static void Release(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource);
            else DestroyImmediate(resource);
        }

        // ==================== 键身视觉 ====================

        /// <summary>白键：单部件带顶面倒角的键身，键根下第一个子物体 <c>"Mesh"</c> 即高亮目标。</summary>
        private void BuildWhiteKeyVisual(Transform root, Material mat)
        {
            CreateKeyMeshPart(root, "Mesh", _whiteKeyMesh,
                new Vector3(0f, WhiteHeight * 0.5f, WhiteLength * 0.5f), mat);
        }

        /// <summary>
        /// 黑键：顶面 / 侧壁 / 底部支撑三层次（审查 1.3A 第 1 项）。
        /// 顶面为键根下第一个子物体 <c>"Mesh"</c>（承载本体色与高亮），侧壁与支撑挂在 <c>"Trim"</c> 下不参与高亮。
        /// 三层次高度之和等于 <see cref="BlackHeight"/>，整体包围盒与原实现一致。
        /// </summary>
        private void BuildBlackKeyVisual(Transform root, Material topMat, Material sideMat)
        {
            float topH = BlackTopHeight;
            float sideH = BlackSideHeight;
            float baseH = BlackBaseHeight;

            // 主表面（顶面）：必须第一个创建，保证 GetComponentInChildren<Renderer>() 命中它
            CreateKeyMeshPart(root, "Mesh", _blackTopMesh,
                new Vector3(0f, baseH + sideH + topH * 0.5f, BlackLength * 0.5f), topMat);

            var trim = new GameObject("Trim").transform;
            trim.SetParent(root, false);

            // 侧壁（哑光、低光泽：与顶面反射形成层次）
            CreateKeyMeshPart(trim, "SideWall", _blackSideMesh,
                new Vector3(0f, baseH + sideH * 0.5f, BlackLength * 0.5f), sideMat);

            // 底部支撑（略窄，形成可见的收进层次）
            CreateKeyMeshPart(trim, "BaseSupport", _blackBaseMesh,
                new Vector3(0f, baseH * 0.5f, BlackLength * 0.5f), sideMat);
        }

        /// <summary>创建键身/装饰件网格部件（无 Collider：琴键不需要碰撞）。</summary>
        private static Transform CreateKeyMeshPart(Transform parent, string name, Mesh mesh, Vector3 localPos, Material mat)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;

            if (mesh != null) go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            return t;
        }

        // ==================== 开盖三角琴 ====================

        private void BuildCase()
        {
            var frame = CreateAnchor(transform, "PianoCase", Vector3.zero);
            _caseRoot = frame;

            var lacquer = CreateLacquer("GrandLacquer", new Color(0.77f, 0.85f, 0.89f));
            var lidLining = CreateLacquer("LidLining", new Color(0.45f, 0.66f, 0.75f));
            var iceEdge = CreateLacquer("IceEdge", new Color(0.40f, 0.72f, 0.84f));
            var dark = CreateMaterial("GrandTrim", new Color(0.19f, 0.27f, 0.34f), 0.15f, 0.30f);
            var soundboard = CreateMaterial("GrandSoundboard", new Color(0.32f, 0.40f, 0.43f), 0.00f, 0.33f);
            var felt = CreateMaterial("CaseFelt", new Color(0.72f, 0.14f, 0.14f), 0.00f, 0.05f);
            var brass = CreateMaterial("PedalMetal", new Color(0.70f, 0.48f, 0.25f), 0.38f, 0.65f);
            var strings = CreateMaterial("PianoStrings", new Color(0.72f, 0.67f, 0.55f), 0.51f, 0.6f);
            ApplyProceduralTexture(felt, ProceduralTextures.Felt(), 1f);

            float width = TotalWidth;
            float mid = width * 0.5f;
            float waist = KeybedHeight + 0.13f;
            Vector2 center = new Vector2(mid, -0.52f);
            Vector2[] outline = PianoMeshFactory.GrandOutline(width);
            var rim = Track(PianoMeshFactory.CreateGrandRim(outline, 0.17f, 0.022f, center));
            CreateCaseMeshPart(frame, "GrandRim", rim, new Vector3(0f, waist - 0.17f, 0f), lacquer);
            var rimBead = Track(PianoMeshFactory.CreateGrandRim(outline, 0.006f, 0.005f, center));
            CreateCaseMeshPart(frame, "RimBead", rimBead, new Vector3(0f, waist + 0.0005f, 0f), iceEdge);
            var baseMesh = Track(PianoMeshFactory.CreateGrandPlate(outline, center, 0.043f, 0.98f));
            CreateCaseMeshPart(frame, "GrandBase", baseMesh, new Vector3(0f, waist - 0.193f, 0f), dark);
            var boardMesh = Track(PianoMeshFactory.CreateGrandPlate(outline, center, 0.014f, 0.936f));
            CreateCaseMeshPart(frame, "SoundBoard", boardMesh, new Vector3(0f, waist - 0.098f, 0f), soundboard);
            CreateCaseMeshPart(frame, "Strings", Track(PianoMeshFactory.CreateGrandStrings(width)),
                new Vector3(0f, waist - 0.078f, 0f), strings);
            BuildCastFrame(frame, width, waist, brass);

            BuildKeyboardCase(frame, lacquer, dark, felt);
            BuildLid(frame, outline, center, waist, lacquer, lidLining, brass);
            BuildLeg(frame, "Leg_L", 0.008f, -0.096f, lacquer, brass);
            BuildLeg(frame, "Leg_R", width - 0.008f, -0.096f, lacquer, brass);
            BuildLeg(frame, "Leg_Tail", width * 0.24f, -1.49f, lacquer, brass);
            BuildPedals(frame, dark, brass);
        }

        /// <summary>切换演奏外壳，键盘、踏板和琴凳参考点始终共用。</summary>
        public void SetUnderwaterAppearance(bool enabled, bool legacy = false)
        {
            if (_caseRoot == null) return;
            bool useCinematic = enabled && !legacy;
            bool useLegacy = enabled && legacy;
            if (useCinematic && _cinematicCase == null)
            {
                // 键在琴壳根节点的外侧，三个活动踏板则挂在琴壳下独立铰接。
                foreach (Transform piece in _caseRoot)
                    if (piece.TryGetComponent<Renderer>(out _) || piece.name == "CastFrame")
                        _legacyParts.Add(piece);
                _cinematicCase = CreateAnchor(_caseRoot, "CinematicPiano", Vector3.zero);
                new CinematicPianoCase(_cinematicCase, TotalWidth, KeybedHeight, Track, CreateMaterial)
                    .Build();
            }
            if (useLegacy && _underwaterCase == null) BuildUnderwaterCase();

            if (_underwaterCase != null) _underwaterCase.gameObject.SetActive(useLegacy);
            if (_cinematicCase != null) _cinematicCase.gameObject.SetActive(useCinematic);
            foreach (var part in _legacyParts) part.gameObject.SetActive(!useCinematic);
            foreach (var part in _classicCaseOnly) part.gameObject.SetActive(!useCinematic && !useLegacy);
            foreach (var finish in _caseFinishes)
                finish.surface.sharedMaterial = useLegacy ? finish.underwater : finish.classic;
        }

        private void BuildUnderwaterCase()
        {
            _underwaterCase = CreateAnchor(_caseRoot, "UnderwaterPiano", Vector3.zero);
            foreach (var name in new[] { "Lid", "LidProp", "LidHingeNear", "LidHingeFar", "MusicStand" })
            {
                var piece = _caseRoot.Find(name);
                if (piece != null) _classicCaseOnly.Add(piece);
            }
            foreach (Transform piece in _caseRoot)
                if (piece.name.StartsWith("Leg_", System.StringComparison.Ordinal)) _classicCaseOnly.Add(piece);

            var blue = CreateLacquer("潮汐深蓝漆", new Color(0.042f, 0.127f, 0.186f));
            var bevel = CreateLacquer("弧梁亮边", new Color(0.13f, 0.36f, 0.47f));
            var wire = CreateMaterial("内弦银镍", new Color(0.28f, 0.42f, 0.44f), 0.31f, 0.37f);
            var pegs = CreateMaterial("弦钉黄铜", new Color(0.54f, 0.40f, 0.23f), 0.65f, 0.56f);
            foreach (var name in new[] { "GrandRim", "GrandBase", "RimBead", "Keybed", "Fallboard", "KeySlip", "Cheek_L", "Cheek_R" })
            {
                var piece = _caseRoot.Find(name);
                if (piece == null || !piece.TryGetComponent<Renderer>(out var surface)) continue;
                _caseFinishes.Add((surface, surface.sharedMaterial, name == "RimBead" ? bevel : blue));
            }

            float width = TotalWidth;
            float waist = KeybedHeight + 0.13f;
            Vector3 Path(float t) => new(
                Mathf.Lerp(-0.037f, width * 0.84f, t) + 0.14f * Mathf.Sin(t * Mathf.PI),
                waist + 0.044f + 0.61f * t + 0.18f * Mathf.Sin(t * Mathf.PI * 0.92f),
                Mathf.Lerp(-0.225f, -1.39f, t) + 0.15f * Mathf.Sin(t * Mathf.PI));
            Vector3 Base(float t)
            {
                var arch = Path(t);
                arch.y = waist + 0.025f + 0.07f * Mathf.Sin(t * Mathf.PI);
                arch.z += 0.06f;
                return arch;
            }

            var frame = new StageMesh();
            frame.Tube(66, 18, Path, t => Mathf.Lerp(0.042f, 0.027f, t), _ => Color.white);
            frame.Tube(60, 14, Base, _ => 0.025f, _ => Color.white);
            // 高端收回到琴壳，构成有支撑的弧架。
            frame.Tube(31, 13, t => Vector3.Lerp(Path(1f), Base(0.96f), t) +
                new Vector3(0.13f, 0f, -0.08f) * Mathf.Sin(t * Mathf.PI),
                t => Mathf.Lerp(0.026f, 0.039f, t), _ => Color.white);
            CreateCaseMeshPart(_underwaterCase, "TidalArchFrame", Track(frame.Build("TidalArchFrame")), Vector3.zero, blue);

            var trim = new StageMesh();
            trim.Tube(60, 9, t => Path(t) + new Vector3(0.022f, 0.021f, 0.018f), _ => 0.009f, _ => Color.white);
            trim.Tube(60, 9, t => Base(t) + new Vector3(0.019f, 0.018f, 0.012f), _ => 0.006f, _ => Color.white);
            trim.Tube(55, 12, t => Vector3.Lerp(Base(0.03f), Path(0.88f), t) +
                new Vector3(0.025f, -0.10f, 0.075f) * Mathf.Sin(t * Mathf.PI),
                _ => 0.012f, _ => Color.white);
            CreateCaseMeshPart(_underwaterCase, "TidalArchBeading", Track(trim.Build("TidalArchBeading")), Vector3.zero, bevel);

            var strings = new StageMesh();
            var anchors = new StageMesh();
            for (int i = 0; i < 46; i++)
            {
                float t = Mathf.Lerp(0.075f, 0.976f, i / 45f);
                Vector3 top = Path(t) - Vector3.up * 0.025f;
                Vector3 bottom = Base(t) + Vector3.up * 0.013f;
                strings.Tube(1, 5, v => Vector3.LerpUnclamped(bottom, top, v),
                    _ => Mathf.Lerp(0.0021f, 0.00125f, t), _ => Color.white);
                anchors.Tube(1, 7, v => Vector3.LerpUnclamped(top, top - Vector3.up * 0.018f, v),
                    _ => 0.0036f, _ => Color.white);
            }
            CreateCaseMeshPart(_underwaterCase, "TidalVerticalStrings", Track(strings.Build("TidalVerticalStrings")), Vector3.zero, wire);
            CreateCaseMeshPart(_underwaterCase, "TidalTuningPegs", Track(anchors.Build("TidalTuningPegs")), Vector3.zero, pegs);

            var legs = new StageMesh();
            var trimLegs = new StageMesh();
            AddLeg(0.03f, -0.115f, -1f);
            AddLeg(width - 0.03f, -0.115f, 1f);
            AddLeg(width * 0.28f, -1.46f, -1f);
            CreateCaseMeshPart(_underwaterCase, "TidalCurvedLegs", Track(legs.Build("TidalCurvedLegs")), Vector3.zero, blue);
            CreateCaseMeshPart(_underwaterCase, "TidalLegBeading", Track(trimLegs.Build("TidalLegBeading")), Vector3.zero, bevel);

            void AddLeg(float x, float z, float side)
            {
                Vector3 Spine(float t) => new(x + side * (0.087f * Mathf.Sin(t * 5.2f) + 0.011f * t),
                    Mathf.LerpUnclamped(waist - 0.15f, 0.039f, t),
                    z - 0.15f * Mathf.Sin(t * Mathf.PI) + 0.124f * t * t);
                legs.Tube(36, 15, Spine, t => Mathf.Lerp(0.066f, 0.029f, t) + 0.012f * Mathf.Sin(t * Mathf.PI * 2f), _ => Color.white);
                legs.Tube(31, 12, t => Spine(1f) + new Vector3(side * (0.048f * Mathf.Sin(t * 5.19f) - 0.084f * t),
                    -0.008f + 0.09f * t * t, 0.053f * Mathf.Sin(t * Mathf.PI)), t => Mathf.Lerp(0.027f, 0.010f, t), _ => Color.white);
                trimLegs.Tube(35, 9, t => Spine(t) + new Vector3(side * Mathf.Lerp(0.051f, 0.023f, t), 0f,
                    Mathf.Lerp(0.024f, 0.013f, t)), _ => 0.006f, _ => Color.white);
            }
        }

        private void BuildCastFrame(Transform frame, float width, float waist, Material brass)
        {
            float y = waist - 0.061f;
            var cast = CreateAnchor(frame, "CastFrame", Vector3.zero);
            CreateCaseBar(cast, "FrontRail", new Vector3(0.02f, y, -0.245f),
                new Vector3(width * 0.94f, y, -0.325f), 0.030f, brass);
            CreateCaseBar(cast, "BassBrace", new Vector3(width * 0.31f, y, -0.27f),
                new Vector3(width * 0.20f, y, -1.52f), 0.023f, brass);
            CreateCaseBar(cast, "TrebleBrace", new Vector3(width * 0.68f, y, -0.30f),
                new Vector3(width * 0.36f, y, -1.41f), 0.026f, brass);
            CreateCaseBar(cast, "BridgeBrace", new Vector3(width * 0.04f, y, -1.03f),
                new Vector3(width * 0.61f, y, -1.19f), 0.025f, brass);
        }

        private void BuildKeyboardCase(Transform frame, Material lacquer, Material dark, Material felt)
        {
            float mid = TotalWidth * 0.5f;
            CreateCasePart(frame, "Keybed", new Vector3(mid, KeybedHeight - 0.035f, 0.008f),
                new Vector3(TotalWidth + 0.145f, 0.069f, 0.22f), lacquer);
            CreateCasePart(frame, "KeyGapShade", new Vector3(mid, KeybedHeight - 0.002f, 0.019f),
                new Vector3(TotalWidth + 0.02f, 0.003f, 0.20f), dark);
            CreateCasePart(frame, "BackFelt", new Vector3(mid, KeybedHeight + 0.015f, -0.076f),
                new Vector3(TotalWidth + 0.042f, 0.032f, 0.012f), felt);
            CreateCasePart(frame, "Fallboard", new Vector3(mid, KeybedHeight + 0.056f, -0.099f),
                new Vector3(TotalWidth + 0.093f, 0.086f, 0.044f), lacquer);
            CreateCasePart(frame, "KeySlip", new Vector3(mid, KeybedHeight - 0.008f, 0.091f),
                new Vector3(TotalWidth + 0.10f, 0.028f, 0.025f), lacquer);

            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -0.046f : TotalWidth + 0.025f;
                CreateCasePart(frame, side == 0 ? "Cheek_L" : "Cheek_R", new Vector3(x, KeybedHeight + 0.020f, 0.004f),
                    new Vector3(0.065f, 0.087f, 0.20f), lacquer);
            }

            var stand = CreateCasePart(frame, "MusicStand", new Vector3(mid, KeybedHeight + 0.128f, -0.192f),
                new Vector3(TotalWidth * 0.47f, 0.09f, 0.015f), dark);
            stand.localRotation = Quaternion.Euler(-56f, 0f, 0f);
        }

        private void BuildLid(Transform frame, Vector2[] outline, Vector2 center, float waist,
            Material lacquer, Material underside, Material brass)
        {
            float hingeX = -0.055f * TotalWidth;
            // 把网格的坐标原点移到平直侧边，以便琴盖绕整排铰链打开。
            var lidOutline = new Vector2[outline.Length];
            for (int i = 0; i < outline.Length; i++)
                lidOutline[i] = outline[i] - new Vector2(hingeX, 0f);
            var lidCenter = center - new Vector2(hingeX, 0f);
            var panel = Track(PianoMeshFactory.CreateBeveledLid(lidOutline, lidCenter));
            var lid = CreateCaseMeshPart(frame, "Lid", panel, new Vector3(hingeX, waist + 0.012f, 0f), lacquer);
            lid.localRotation = Quaternion.Euler(0f, 0f, 23f);
            var lining = Track(PianoMeshFactory.CreateGrandPlate(lidOutline, lidCenter, 0.004f, 0.955f));
            CreateCaseMeshPart(lid, "LidUnderside", lining, new Vector3(0f, -0.0045f, 0f), underside);

            CreateCasePart(frame, "LidHingeNear", new Vector3(hingeX, waist + 0.009f, -0.36f),
                new Vector3(0.024f, 0.024f, 0.067f), brass);
            CreateCasePart(frame, "LidHingeFar", new Vector3(hingeX, waist + 0.009f, -1.13f),
                new Vector3(0.024f, 0.024f, 0.067f), brass);

            Vector3 bottom = new Vector3(TotalWidth * 0.75f, waist - 0.024f, -0.70f);
            Vector3 top = lid.localPosition + lid.localRotation * new Vector3(TotalWidth * 0.84f, 0f, -0.63f);
            CreateCaseBar(frame, "LidProp", bottom, top, 0.014f, brass);
        }

        private void BuildLeg(Transform frame, string name, float x, float z, Material lacquer, Material brass)
        {
            float length = KeybedHeight - 0.09f;
            CreateCaseMeshPart(frame, name, Track(PianoMeshFactory.CreateTaperedLeg(length)),
                new Vector3(x, 0.04f, z), lacquer);
            CreateCasePart(frame, name + "_Shoulder", new Vector3(x, KeybedHeight - 0.060f, z),
                new Vector3(0.087f, 0.056f, 0.088f), lacquer);
            CreateCasePart(frame, name + "_Caster", new Vector3(x, 0.025f, z),
                new Vector3(0.045f, 0.05f, 0.048f), brass);
            CreateCasePart(frame, name + "_Ferrule", new Vector3(x, 0.066f, z),
                new Vector3(0.052f, 0.018f, 0.044f), brass);
        }

        private void BuildPedals(Transform frame, Material lacquer, Material brass)
        {
            float mid = TotalWidth * 0.5f;
            const float hingeHeight = 0.046f;
            const float hingeDepth = -0.077f;
            CreateCasePart(frame, "PedalLyre", new Vector3(mid, 0.045f, -0.101f),
                new Vector3(0.245f, 0.070f, 0.080f), lacquer);
            CreateCaseBar(frame, "PedalSupportL", new Vector3(mid + 0.095f, 0.070f, -0.125f),
                new Vector3(mid + 0.082f, KeybedHeight - 0.10f, -0.233f), 0.020f, lacquer);
            CreateCaseBar(frame, "PedalSupportR", new Vector3(mid - 0.095f, 0.070f, -0.125f),
                new Vector3(mid - 0.082f, KeybedHeight - 0.10f, -0.233f), 0.020f, lacquer);

            var arm = Track(PianoMeshFactory.CreateRoundedPanel(0.018f, 0.012f, 0.102f, 0.004f));
            var tip = Track(PianoMeshFactory.CreateRoundedPanel(0.039f, 0.014f, 0.054f, 0.006f));
            BuildPedal(frame, "Pedal_L", new Vector3(mid + 0.09f, hingeHeight, hingeDepth), arm, tip, brass);
            BuildPedal(frame, "Pedal_C", new Vector3(mid, hingeHeight, hingeDepth), arm, tip, brass);
            RightPedalHinge = BuildPedal(frame, "Pedal_R",
                new Vector3(mid - 0.09f, hingeHeight, hingeDepth), arm, tip, brass);
            RightPedalContact = RightPedalHinge.Find("Contact");

            RightHeelAnchor = CreateAnchor(frame, "RightHeel", new Vector3(mid - 0.12f, 0f, 0.21f));
            BenchAnchor = CreateAnchor(frame, "Bench", new Vector3(mid, Avatar.AvatarBodyMetrics.StoolTopHeight, 0.52f));
        }

        private static Transform BuildPedal(Transform frame, string name, Vector3 position,
            Mesh arm, Mesh tip, Material brass)
        {
            var hinge = CreateAnchor(frame, name, position);
            CreateCaseMeshPart(hinge, "Arm", arm, new Vector3(0f, 0f, 0.051f), brass);
            CreateCaseMeshPart(hinge, "Tread", tip, new Vector3(0f, 0f, 0.107f), brass);
            CreateAnchor(hinge, "Contact", new Vector3(0f, 0.007f, 0.107f));
            return hinge;
        }

        private void CreateCaseBar(Transform parent, string name, Vector3 from, Vector3 to,
            float thickness, Material material)
        {
            Vector3 direction = to - from;
            var bar = CreateCasePart(parent, name, (from + to) * 0.5f,
                new Vector3(thickness, thickness, direction.magnitude), material);
            bar.localRotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private static Transform CreateAnchor(Transform parent, string name, Vector3 position)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = position;
            return anchor;
        }

        /// <summary>
        /// 创建琴体部件（倒角化）。
        /// <para>
        /// <b>为什么不用 <c>PrimitiveType.Cube</c></b>：真实木制品没有数学意义上的锐利边缘，
        /// 任何加工过的边都有零点几毫米到几毫米的圆角。这些圆角会沿边缘捕捉一条细长高光，
        /// 而那条高光正是大脑判断「这是实体而非贴图」的依据。用 Cube 时侧板/箱体/琴腿的边缘
        /// 完全没有高光，整台琴读起来就是「纸板拼的柜子」（实测观感，V01/V13 截图）。
        /// </para>
        /// <para>
        /// 倒角量取最短边的 15%，钳制在 2.5–10 mm：太小学数学盒子，太大则像肥皂。
        /// <see cref="PianoMeshFactory.CreateChamferedBox"/> 的 AllEdges 模式顶点 ≤24，
        /// 与 Cube 的 24 顶点持平，不增加任何几何预算。
        /// </para>
        /// </summary>
        private Transform CreateCasePart(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            float minEdge = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
            float chamfer = Mathf.Clamp(minEdge * 0.15f, 0.0025f, 0.010f);
            var mesh = Track(PianoMeshFactory.CreateChamferedBox(
                scale.x, scale.y, scale.z, chamfer, ChamferMode.AllEdges));

            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            if (mesh != null) go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            return t;
        }

        /// <summary>用程序化网格（倒角/圆角）创建琴体部件；无 Collider。</summary>
        private static Transform CreateCaseMeshPart(Transform parent, string name, Mesh mesh, Vector3 pos, Material mat)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            if (mesh != null) go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            return t;
        }

        /// <summary>登记本次构建创建的网格，便于 <see cref="ClearKeys"/> 统一释放。</summary>
        private Mesh Track(Mesh mesh)
        {
            if (mesh != null) _ownedMeshes.Add(mesh);
            return mesh;
        }

        // ==================== 白键索引计算（从 A0 起，每八度 7 个白键） ====================

        // 八度内半音偏移 → 累计白键数（A,A#,B,C,C#,D,D#,E,F,F#,G,G#）
        private static readonly int[] WhiteCountUpTo = { 1, 1, 2, 3, 3, 4, 4, 5, 6, 6, 7, 7 };

        /// <summary>
        /// 音高 → 键盘上的白键序号（0..51）。
        /// <para>仅对 <see cref="MinPitch"/>..<see cref="MaxPitch"/> 有效；A0(21) → 0，C8(108) → 51。</para>
        /// <para>公开为静态方法以便纯逻辑单测（EditMode 不需要 GameObject）。</para>
        /// </summary>
        public static int WhiteIndex(int pitch)
        {
            int n = pitch - MinPitch;
            int oct = n / 12;
            int off = n % 12;
            return oct * 7 + WhiteCountUpTo[off] - 1;
        }

        /// <summary>
        /// 黑键音高 → 其左侧白键的白键序号（0..50）。用于计算黑键中心的 X 坐标。
        /// <para>仅对黑键音高有意义；公开为静态方法以便纯逻辑单测。</para>
        /// </summary>
        public static int BlackKeyLeftWhiteIndex(int pitch)
        {
            int n = pitch - MinPitch;
            int oct = n / 12;
            int off = n % 12;
            int leftOff = off switch
            {
                1 => 0,   // A# 在 A 与 B 之间
                4 => 3,   // C# 在 C 与 D 之间
                6 => 5,   // D# 在 D 与 E 之间
                9 => 8,   // F# 在 F 与 G 之间
                11 => 10, // G# 在 G 与 A 之间
                _ => 0
            };
            return oct * 7 + WhiteCountUpTo[leftOff] - 1;
        }

        /// <summary>
        /// 创建材质并设置金属度/光泽度（方案 5.4 / 审查 1.3A 第 4 项材质分层）。
        /// <para>内置渲染管线用 <c>_Glossiness</c>，URP Lit 用 <c>_Smoothness</c>，两者都尝试写入。</para>
        /// <para><c>Shader.Find</c> 可能返回 null（无内置 Standard），按项目既有约定逐级回落。</para>
        /// </summary>
        private Material CreateLacquer(string name, Color color)
        {
            var shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? Resources.Load<Shader>("StarStage/GrandLacquer") : null;
            if (shader == null) return CreateMaterial(name, color, 0.19f, 0.65f);
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            _ownedMaterials.Add(material);
            return material;
        }

        private Material CreateMaterial(string name, Color color, float metallic, float smoothness)
        {
            // 写实化批次 2：shader 选择由**当前管线状态**决定，而非 Find 顺序——
            // SRP 激活时 Standard 仍能被 Find 到但渲染为洋红（不被 URP 支持），URP/Lit 必须优先；
            // 内置管线则相反。Sprites/Default 是最后的可渲染兜底（与两侧管线都兼容）。
            var shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Sprites/Default")
                : Shader.Find("Standard")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[PianoKeyMap] 未找到可用 shader，钢琴材质创建失败");
                return null;
            }

            var m = new Material(shader) { name = name, color = color };
            _ownedMaterials.Add(m);
            if (m.HasProperty(MetallicId)) m.SetFloat(MetallicId, metallic);
            if (m.HasProperty(GlossinessId)) m.SetFloat(GlossinessId, smoothness);
            if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, smoothness);
            return m;
        }

        /// <summary>
        /// 把程序化纹理绑到材质（兼容 Standard 的 <c>_MainTex</c> 与 URP/Lit 的 <c>_BaseMap</c>）。
        /// <para>
        /// <paramref name="tint"/> 把基色设成灰度，用于「同一张贴图产出不同明度」——
        /// 木材正面用 1.0、侧面用 0.72，形成层次而不需要第二张纹理。
        /// 基色必须显式覆盖：贴图自带颜色，若继续乘原来的深褐色会再压暗一层。
        /// </para>
        /// </summary>
        private static void ApplyProceduralTexture(Material mat, Texture2D tex, float tint)
        {
            if (mat == null || tex == null) return;

            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);

            var c = new Color(tint, tint, tint, 1f);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        }

        private void OnDestroy()
        {
            ReleaseVisuals();
            // 程序化纹理是静态缓存，场景销毁时统一释放（单钢琴实例，不存在共享竞争）
            ProceduralTextures.ReleaseAll();
        }
    }
}
