using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 维护 MIDI 音高与琴键对象的映射关系（方案 4.2）。
    /// 采用真实钢琴比例（键宽 24mm / 键长 150mm / 键面高 0.72m），
    /// 并程序化生成带层次与材质差异的立式琴体（键床托板 / 后挡红绒 / 下箱体 / 音板 / 侧板 / 顶盖 / 谱架 / 底座 / 踏板），
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
        public float BlackKeyChamfer = 0.0006f;  // 约 0.6mm，黑键顶面四周

        [Header("键盘高度（地面至键面下沿，立式钢琴约 0.72m）")]
        public float KeybedHeight = DefaultKeybedHeight;

        // 黑键三层次的高度占比（顶面 / 侧壁 / 底部支撑），三者之和必须为 1。
        private const float BlackTopRatio = 0.62f;
        private const float BlackSideRatio = 0.20f;
        private const float BlackBaseRatio = 0.18f;

        private readonly Dictionary<int, Transform> _keyRoots = new();

        // 复用的网格与材质的持有者，ClearKeys 时统一释放（避免重复调用 BuildKeys 造成泄漏）
        private readonly List<Mesh> _ownedMeshes = new();

        private Transform _caseRoot;

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
            Material whiteMat = CreateMaterial("KeyWhite", new Color(0.96f, 0.96f, 0.94f), 0.00f, 0.55f);
            Material blackTopMat = CreateMaterial("KeyBlackTop", new Color(0.13f, 0.13f, 0.145f), 0.10f, 0.45f);
            Material blackSideMat = CreateMaterial("KeyBlackSide", new Color(0.055f, 0.055f, 0.062f), 0.00f, 0.15f);

            // ---- 网格（白键 1 份、黑键 3 份，全部复用于 88 键）----
            _whiteKeyMesh = Track(PianoMeshFactory.CreateChamferedBox(
                WhiteWidth, WhiteHeight, WhiteLength, WhiteKeyChamfer, ChamferMode.TopOnly));
            _blackTopMesh = Track(PianoMeshFactory.CreateChamferedBox(
                BlackWidth, BlackTopHeight, BlackLength, BlackKeyChamfer, ChamferMode.TopOnly));
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
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            }
            _keyRoots.Clear();

            if (_caseRoot != null) Destroy(_caseRoot.gameObject);
            _caseRoot = null;

            for (int i = 0; i < _ownedMeshes.Count; i++)
            {
                if (_ownedMeshes[i] != null) Destroy(_ownedMeshes[i]);
            }
            _ownedMeshes.Clear();
            _whiteKeyMesh = _blackTopMesh = _blackSideMesh = _blackBaseMesh = null;
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

        // ==================== 立式琴体（方案 5.4 / 审查 1.3A 第 3、4、5 项） ====================

        /// <summary>
        /// 程序化搭建立式琴体轮廓。
        /// <para>
        /// <b>位置语义保持不变</b>：键床托板 / 后挡红绒 / 下箱体 / 音板 / 谱架 / 琴腿的原有位置与尺寸一字未改，
        /// 琴体不会前移到键面上方（<see cref="Avatar.HandPoseValidator"/> 的 minZ 依赖键根世界 z 与琴体的相对关系）。
        /// 新增部件只补"近景与全景可见"的轮廓（侧板 / 底座 / 键盘盖边界 / 踏板），且不与键面区域相交。
        /// </para>
        /// <para>
        /// <b>材质分层</b>：木材（清漆微光泽）/ 深木（哑光）/ 键面（高光泽）/ 金属踏板（高金属度、高光泽）/ 红绒（几乎无光泽）
        /// 的金属度与光泽度明显不同，不再共享同一组参数。
        /// </para>
        /// <para>
        /// <b>接触关系</b>：下箱体、底座、琴腿的最低点均落在 y=0（场景地面顶面），不悬空。
        /// </para>
        /// </summary>
        private void BuildCase()
        {
            var c = new GameObject("PianoCase").transform;
            c.SetParent(transform, false);
            _caseRoot = c;

            var wood = CreateMaterial("CaseWood", new Color(0.225f, 0.152f, 0.110f), 0.00f, 0.32f);      // 胡桃木清漆
            var woodDark = CreateMaterial("CaseWoodDark", new Color(0.150f, 0.098f, 0.074f), 0.00f, 0.20f); // 哑光深木
            var felt = CreateMaterial("CaseFelt", new Color(0.72f, 0.14f, 0.14f), 0.00f, 0.05f);          // 键盘后挡红绒
            var brass = CreateMaterial("PedalMetal", new Color(0.62f, 0.50f, 0.24f), 0.85f, 0.72f);       // 黄铜踏板

            float w = TotalWidth;
            float mid = w * 0.5f;

            // ---- 键盘托板（键下方，稍微前凸）----
            CreateCasePart(c, "Keybed", new Vector3(mid, KeybedHeight - 0.02f, 0.02f),
                new Vector3(w + 0.05f, 0.04f, 0.21f), wood);

            // ---- 键后挡绒（遮挡键尾，经典红绒）----
            CreateCasePart(c, "BackFelt", new Vector3(mid, KeybedHeight + 0.012f, -WhiteLength * 0.5f - 0.006f),
                new Vector3(w + 0.05f, 0.030f, 0.012f), felt);

            // ---- 键盘盖边界（新增）：键尾上方的薄边条，位于键面之上 2mm，不前伸遮挡键面 ----
            CreateCasePart(c, "Fallboard", new Vector3(mid, KeybedHeight + 0.035f, -0.031f),
                new Vector3(w + 0.06f, 0.022f, 0.022f), woodDark);

            // ---- 键前挡条（新增）：键前端下方的收口边条，位于键面之下，不遮挡键面 ----
            CreateCasePart(c, "KeySlip", new Vector3(mid, KeybedHeight + 0.003f, 0.087f),
                new Vector3(w + 0.06f, 0.022f, 0.022f), woodDark);

            // ---- 下箱体（键盘下方落地箱）----
            CreateCasePart(c, "LowerCase", new Vector3(mid, (KeybedHeight - 0.04f) * 0.5f, -0.15f),
                new Vector3(w + 0.05f, KeybedHeight - 0.04f, 0.30f), woodDark);

            // ---- 上箱体（音板，键盘后方立式箱体）----
            CreateCasePart(c, "SoundBoard", new Vector3(mid, 1.08f, -0.17f),
                new Vector3(w + 0.05f, 0.68f, 0.26f), wood);

            // ---- 侧板（新增）：左右立柱，补足立式琴的完整轮廓；x 向外让开键盘，不与任何键相交 ----
            CreateCasePart(c, "Side_L", new Vector3(-0.042f, 0.71f, -0.19f),
                new Vector3(0.05f, 1.42f, 0.30f), woodDark);
            CreateCasePart(c, "Side_R", new Vector3(w + 0.042f, 0.71f, -0.19f),
                new Vector3(0.05f, 1.42f, 0.30f), woodDark);

            // ---- 底座（新增）：落地台座，最低点 y=0，与地面接触 ----
            CreateCasePart(c, "Plinth", new Vector3(mid, 0.035f, -0.15f),
                new Vector3(w + 0.14f, 0.07f, 0.34f), woodDark);

            // ---- 顶盖（轻微圆角面板，与原 Lid 位置/尺寸一致）----
            var lidMesh = Track(PianoMeshFactory.CreateRoundedPanel(w + 0.09f, 0.030f, 0.30f, 0.006f));
            CreateCaseMeshPart(c, "Lid", lidMesh, new Vector3(mid, 1.435f, -0.17f), woodDark);

            // ---- 谱架（斜放，位置与倾角保持原样）----
            var stand = CreateCasePart(c, "MusicStand", new Vector3(mid, 1.20f, -0.005f),
                new Vector3(w * 0.62f, 0.30f, 0.015f), woodDark);
            stand.localRotation = Quaternion.Euler(-14f, 0f, 0f);

            // ---- 琴腿（键盘两侧，最低点 y=0）----
            CreateCasePart(c, "Leg_L", new Vector3(0.01f, (KeybedHeight - 0.04f) * 0.5f, -0.02f),
                new Vector3(0.045f, KeybedHeight - 0.04f, 0.05f), woodDark);
            CreateCasePart(c, "Leg_R", new Vector3(w - 0.01f, (KeybedHeight - 0.04f) * 0.5f, -0.02f),
                new Vector3(0.045f, KeybedHeight - 0.04f, 0.05f), woodDark);

            // ---- 踏板（新增）：踏板立柱 + 三根金属踏板，金属度/光泽度与木质、键面明显不同 ----
            CreateCasePart(c, "PedalLyre", new Vector3(mid, 0.10f, 0.03f),
                new Vector3(0.11f, 0.20f, 0.06f), woodDark);

            var pedalMesh = Track(PianoMeshFactory.CreateChamferedBox(0.04f, 0.016f, 0.12f, 0.004f, ChamferMode.AllEdges));
            CreateCaseMeshPart(c, "Pedal_L", pedalMesh, new Vector3(mid - 0.09f, 0.185f, 0.12f), brass);
            CreateCaseMeshPart(c, "Pedal_C", pedalMesh, new Vector3(mid, 0.185f, 0.12f), brass);
            CreateCaseMeshPart(c, "Pedal_R", pedalMesh, new Vector3(mid + 0.09f, 0.185f, 0.12f), brass);
        }

        private static Transform CreateCasePart(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.Destroy(go.GetComponent<Collider>());
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
        private static Material CreateMaterial(string name, Color color, float metallic, float smoothness)
        {
            var shader = Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[PianoKeyMap] 未找到可用 shader，钢琴材质创建失败");
                return null;
            }

            var m = new Material(shader) { name = name, color = color };
            if (m.HasProperty(MetallicId)) m.SetFloat(MetallicId, metallic);
            if (m.HasProperty(GlossinessId)) m.SetFloat(GlossinessId, smoothness);
            if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, smoothness);
            return m;
        }
    }
}
