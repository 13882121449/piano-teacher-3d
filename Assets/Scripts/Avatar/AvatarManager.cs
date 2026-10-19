using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 统一手部控制接口（方案 3.2）：手腕、手掌、手指。
    /// 各角色共用此接口，使 IK 与姿态控制与具体模型解耦。
    /// <para>
    /// <b>语义不变</b>：<see cref="Fingers"/> 仍然是「每指一个<b>根</b> Transform」，
    /// 由 <c>FingerPoseController</c> 旋转该根节点；三段指节（近端/中段/末端）与指尖球作为根的<b>子节点链</b>存在。
    /// </para>
    /// </summary>
    public interface IHandRig
    {
        Transform Wrist { get; }
        Transform Palm { get; }
        IReadOnlyList<Transform> Fingers { get; }
    }

    /// <summary>
    /// 可选扩展接口：暴露指尖 Transform。
    /// <para>
    /// 供 <c>HandIKController</c> 计算「指尖到目标键面的距离」（审查 1.2C：不要只统计手腕距离），
    /// 以及检查拇指朝向 / 离键悬空。实现方缺席时调用方使用等价估计，不视为错误。
    /// </para>
    /// </summary>
    public interface IFingertipRig
    {
        /// <summary>取指定手指的指尖 Transform（0 = 拇指 … 4 = 小指）；索引越界返回 null。</summary>
        Transform GetFingertip(int fingerIndex);
    }

    /// <summary>
    /// 可选扩展接口：暴露每指的 <b>MCP / PIP / DIP 三关节</b>与静止指长（评估文档附录 A.4.1 / A.4.4）。
    /// <para>
    /// <c>FingerPoseController</c> 据此做关节级姿态解算与受限指尖 IK；
    /// 实现方缺席时自动退回「只旋转指根」的旧行为（向后兼容，不影响既有测试）。
    /// </para>
    /// </summary>
    public interface IFingerJointRig : IFingertipRig
    {
        /// <summary>
        /// 取某指的三个关节（0 = 指根 MCP，1 = 中节 PIP，2 = 末节 DIP）。不支持时返回 false。
        /// </summary>
        bool TryGetJoints(int fingerIndex, out Transform proximal, out Transform intermediate, out Transform distal);

        /// <summary>该指关节原点到指尖接触点的静止长度（米）。</summary>
        float GetFingerLength(int fingerIndex);
    }

    /// <summary>预设角色外观参数（兼容保留：新代码请使用 <see cref="AvatarDefinition"/>）。</summary>
    [System.Serializable]
    public class AvatarPreset
    {
        public string Name;
        public float Height = 1.45f;
        public float HeadScale = 1f;
        public Color SkinColor = new Color(0.96f, 0.85f, 0.74f);
        public Color ClothColor = new Color(0.3f, 0.5f, 0.9f);
        public int HairStyle; // 0 长发 / 1 短发 / 2 卡通呆毛
        public bool Cartoon;
    }

    /// <summary>
    /// 管理人物加载、选择与切换（方案 4.2 / 3.2 / 9.1）。
    /// <para>
    /// 预生成 3 个程序化人形（写实女性 / 写实男性 / 卡通），采用统一角色尺寸表
    /// （<see cref="AvatarBodyMetrics"/>，头身比约 1:7，含肩、躯干收腰、坐姿腿部、五官与琴凳），零外部模型资产。
    /// </para>
    /// <para>
    /// <b>本轮升级</b>（方案 5.1 / 5.2 短期 + 审查 1.1 方案 A + 1.2A）：
    /// ① 手部由「一块扁平长方体 + 单段胶囊手指」升级为带掌根/掌背/虎口的连续轮廓 + <b>每指 3 段</b>、
    ///    指尖略圆、拇指具有独立对掌方向（约 57°，与四指形成 45°–60° 夹角）、五指长度粗细按真实手型区分；
    /// ② 人物头部由纯球体升级为椭球 + 下颌 + 耳朵 + 鼻 + 眼 + 眉，躯干用不同宽度的平滑过渡体块，
    ///    肩部用重叠体块覆盖接缝，服装（衣领 / 袖口 / 下摆）作为独立可见层，发型为「大轮廓 + 少量发束」；
    /// ③ 构建改由 <see cref="AvatarDefinition"/> 驱动，并做导入校验（失败回退默认角色，不影响曲目播放）。
    /// </para>
    /// <para>
    /// 切换仅替换外观与手部引用，不触碰时间轴与演奏状态（方案「人物与演奏解耦」/ 6.3「角色切换 + 视角跟随」）。
    /// </para>
    /// <para>
    /// <b>性能</b>（方案 7.2）：所有几何只在构建期生成；<c>Update</c> 路径不存在（本组件无 Update），
    /// 切换仅做 SetActive 与事件派发，无每帧分配。
    /// </para>
    /// </summary>
    public class AvatarManager : MonoBehaviour
    {
        public const int AvatarCount = 3;

        /// <summary>基准人体身高（由 <see cref="AvatarBodyMetrics"/> 统一管理）。</summary>
        private const float BaseHeight = AvatarBodyMetrics.ReferenceHeight;

        /// <summary>角色定义（纯数据，方案 9.1 / 9.2：模型与材质不写死在控制器里）。</summary>
        private readonly List<AvatarDefinition> _definitions = new();

        private readonly List<AvatarRig> _rigs = new();
        private int _currentIndex;

        /// <summary>手部与手臂网格当前是否可见（教学模式会隐藏；切角色后需重新套用）。</summary>
        private bool _limbsVisible = true;
        private string _lastRigValidationMessage;

        /// <summary>运行时生成的网格（手部放样网格），场景销毁时统一释放，避免原生对象泄漏。</summary>
        private readonly List<Mesh> _runtimeMeshes = new List<Mesh>();

        /// <summary>全部角色定义（只读；索引与 <see cref="SwitchAvatar"/> 一致）。</summary>
        public IReadOnlyList<AvatarDefinition> Definitions => _definitions;

        /// <summary>当前角色定义；未构建时为 null。</summary>
        public AvatarDefinition CurrentDefinition =>
            (_definitions.Count > 0 && _currentIndex >= 0 && _currentIndex < _definitions.Count)
                ? _definitions[_currentIndex]
                : null;

        /// <summary>
        /// 最近一次构建的骨架校验问题描述；无问题时为 null（方案 5.2 中期「校验失败返回一条问题描述供上层上报 Warning」）。
        /// </summary>
        public string LastRigValidationMessage => _lastRigValidationMessage;

        public int CurrentIndex => _currentIndex;

        /// <summary>当前角色显示名（与 <c>AvatarSelectionUI.AvatarNames</c> 顺序一致）。</summary>
        public string CurrentName
        {
            get
            {
                var def = CurrentDefinition;
                return def != null ? def.DisplayName : string.Empty;
            }
        }

        public event System.Action<int> OnAvatarChanged;

        // 当前角色暴露给 HandIKController 的骨骼引用
        public Transform LeftShoulder => _rigs[_currentIndex].LeftShoulder;
        public Transform RightShoulder => _rigs[_currentIndex].RightShoulder;
        public Transform LeftArm => _rigs[_currentIndex].LeftArm;
        public Transform RightArm => _rigs[_currentIndex].RightArm;
        public IHandRig LeftHand => _rigs[_currentIndex].LeftHand;
        public IHandRig RightHand => _rigs[_currentIndex].RightHand;

        /// <summary>当前角色的标准 Humanoid 骨架（方案 4.1 / 3.2）。未构建时为 null。</summary>
        public AvatarRigBuilder.Skeleton CurrentSkeleton =>
            (_rigs != null && _currentIndex >= 0 && _currentIndex < _rigs.Count) ? _rigs[_currentIndex].Skeleton : null;

        /// <summary>当前角色的骨架是否为 Unity 认可的合法 Humanoid。</summary>
        public bool HasHumanoidRig => CurrentSkeleton != null && CurrentSkeleton.IsHumanoid;

        /// <summary>取指定角色的骨架（测试与调试用）。索引越界返回 null。</summary>
        public AvatarRigBuilder.Skeleton GetSkeleton(int index) =>
            (_rigs != null && index >= 0 && index < _rigs.Count) ? _rigs[index].Skeleton : null;

        // ==================== 角色定义（方案 5.2 中期 / 9.2 配置与内容分离） ====================

        /// <summary>构建默认的 3 个角色定义（三角色外观不同，但共用同一套键盘标定范围）。</summary>
        private void EnsureDefinitions()
        {
            if (_definitions.Count > 0) return;

            _definitions.Add(MakeDefinition(
                id: "realistic_female", name: "写实女性", height: 1.38f, headScale: 0.94f,
                skin: new Color(0.97f, 0.86f, 0.78f), cloth: new Color(0.62f, 0.27f, 0.33f),
                hair: new Color(0.145f, 0.110f, 0.090f), hairStyle: 0, cartoon: false,
                handHeightOffset: 0.000f, handDepthOffset: 0.000f,
                palmWidthScale: 0.97f, palmThicknessScale: 0.96f,
                fingerLengthScale: 1.00f, fingerRadiusScale: 0.95f, wristPitch: 8f));

            _definitions.Add(MakeDefinition(
                id: "realistic_male", name: "写实男性", height: 1.52f, headScale: 1.00f,
                skin: new Color(0.86f, 0.73f, 0.62f), cloth: new Color(0.20f, 0.29f, 0.45f),
                hair: new Color(0.120f, 0.095f, 0.080f), hairStyle: 1, cartoon: false,
                handHeightOffset: 0.002f, handDepthOffset: -0.004f,
                palmWidthScale: 1.06f, palmThicknessScale: 1.05f,
                fingerLengthScale: 1.04f, fingerRadiusScale: 1.06f, wristPitch: 8f));

            _definitions.Add(MakeDefinition(
                id: "cartoon", name: "卡通角色", height: 1.26f, headScale: 1.42f,
                skin: new Color(1f, 0.92f, 0.82f), cloth: new Color(0.24f, 0.62f, 0.45f),
                hair: new Color(0.22f, 0.58f, 0.92f), hairStyle: 2, cartoon: true,
                handHeightOffset: -0.002f, handDepthOffset: 0.004f,
                palmWidthScale: 1.12f, palmThicknessScale: 1.16f,
                fingerLengthScale: 0.92f, fingerRadiusScale: 1.14f, wristPitch: 11f));
        }

        private static AvatarDefinition MakeDefinition(string id, string name, float height, float headScale,
            Color skin, Color cloth, Color hair, int hairStyle, bool cartoon,
            float handHeightOffset, float handDepthOffset,
            float palmWidthScale, float palmThicknessScale,
            float fingerLengthScale, float fingerRadiusScale, float wristPitch)
        {
            var def = new AvatarDefinition
            {
                Id = id,
                DisplayName = name,
                BodyHeight = height,
                BodyScale = height / BaseHeight,
                HeadScale = headScale,
                SkinColor = skin,
                ClothColor = cloth,
                HairColor = hair,
                DarkColor = new Color(0.16f, 0.16f, 0.19f),
                HairStyle = hairStyle,
                Cartoon = cartoon,
                HandHeightOffset = handHeightOffset,
                HandDepthOffset = handDepthOffset
            };
            def.Quality.FingerSegments = 3;
            def.Quality.MeshDetail = AvatarMeshDetail.Medium;
            def.ApplyHandStyle(palmWidthScale, palmThicknessScale,
                fingerLengthScale, fingerRadiusScale, wristPitch);
            return def;
        }

        /// <summary>在骨架根上绑定 Animator（必须与 Avatar 的根为同一 GameObject）。</summary>
        private static Animator AttachHumanoidAnimator(AvatarRigBuilder.Skeleton skeleton)
        {
            if (skeleton == null || skeleton.Root == null || !skeleton.IsHumanoid) return null;

            var animator = skeleton.Root.gameObject.AddComponent<Animator>();
            animator.avatar = skeleton.Avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        /// <summary>预生成全部角色，默认显示第一个。</summary>
        public void BuildAvatars()
        {
            EnsureDefinitions();
            _lastRigValidationMessage = null;

            for (int i = 0; i < AvatarCount && i < _definitions.Count; i++)
            {
                var def = _definitions[i];
                var rig = BuildAvatarRig(def);

                // 导入校验（方案 5.2 中期）：失败不得让角色失败影响曲目播放 → 回退到默认角色
                string issue = AvatarDefinition.Validate(rig.Skeleton, def);
                if (issue != null)
                {
                    _lastRigValidationMessage = $"角色「{def.DisplayName}」校验失败：{issue}（已回退默认角色）";
                    Debug.LogWarning("[AvatarManager] " + _lastRigValidationMessage);
                    DestroyRig(rig);
                    rig = BuildAvatarRig(AvatarDefinition.CreateDefault());
                }

                rig.Root.name = $"Avatar_{i}_{def.DisplayName}";
                rig.Root.SetActive(i == 0);
                _rigs.Add(rig);
            }
            _currentIndex = 0;
        }

        private static void DestroyRig(AvatarRig rig)
        {
            if (rig?.Root == null) return;
            // Destroy 是延迟到帧末执行的：先停用，避免与回退角色在同一帧内同时可见
            rig.Root.SetActive(false);
            Object.Destroy(rig.Root);
        }

        /// <summary>切换角色：仅替换表现，不重置曲目/进度/摄像机（方案验收标准）。</summary>
        public void SwitchAvatar(int index)
        {
            index = Mathf.Clamp(index, 0, AvatarCount - 1);
            if (index == _currentIndex) return;

            _rigs[_currentIndex].Root.SetActive(false);
            _currentIndex = index;
            _rigs[_currentIndex].Root.SetActive(true);

            // 新角色同样要套用当前的「手部/手臂可见性」（教学模式下切角色不能把手露出来）
            ApplyLimbsVisibility(_currentIndex, _limbsVisible);

            OnAvatarChanged?.Invoke(_currentIndex);
        }

        /// <summary>
        /// 显示 / 隐藏「手部与手臂」的可见网格（教学模式用，见 <c>Core.DisplayMode</c>）。
        /// <para>
        /// 只切换网格所在 GameObject 的激活状态，<b>不动骨骼与 IK</b>：手部仍按时间轴正常解算，
        /// 因此切回展示模式时无需重建任何状态（零残留、零视觉错乱），相机取景也保持连续。
        /// </para>
        /// </summary>
        public void SetHandsAndArmsVisible(bool visible)
        {
            _limbsVisible = visible;
            ApplyLimbsVisibility(_currentIndex, visible);
        }

        /// <summary>当前手部 / 手臂网格是否可见（供测试与诊断）。</summary>
        public bool HandsAndArmsVisible => _limbsVisible;

        private void ApplyLimbsVisibility(int index, bool visible)
        {
            if (_rigs == null || index < 0 || index >= _rigs.Count) return;
            var rig = _rigs[index];

            SetActive(rig.LeftArm, visible);
            SetActive(rig.RightArm, visible);
            if (rig.LeftHand != null) SetActive(rig.LeftHand.Wrist, visible);
            if (rig.RightHand != null) SetActive(rig.RightHand.Wrist, visible);

            // 肩球属于手臂的一部分，隐藏在教学模式里避免留下"断臂"空壳
            if (rig.LeftShoulder != null) SetActive(rig.LeftShoulder.Find("Ball"), visible);
            if (rig.RightShoulder != null) SetActive(rig.RightShoulder.Find("Ball"), visible);
        }

        private static void SetActive(Transform target, bool visible)
        {
            if (target == null) return;
            if (target.gameObject.activeSelf != visible) target.gameObject.SetActive(visible);
        }

        public void SwitchNext() => SwitchAvatar((_currentIndex + 1) % AvatarCount);

        // ==================== 程序化人形构建（统一尺寸表，零外部模型资产） ====================

        private sealed class AvatarRig
        {
            public GameObject Root;
            public Transform LeftShoulder;
            public Transform RightShoulder;
            public Transform LeftArm;
            public Transform RightArm;
            public IHandRig LeftHand;
            public IHandRig RightHand;

            /// <summary>标准 Humanoid 骨架（方案 4.1 / 3.2）。</summary>
            public AvatarRigBuilder.Skeleton Skeleton;

            /// <summary>绑定 Humanoid Avatar 的 Animator（骨架有效时非 null）。</summary>
            public Animator Animator;

            public bool HasHumanoidRig => Skeleton != null && Skeleton.IsHumanoid;
        }

        private AvatarRig BuildAvatarRig(AvatarDefinition def)
        {
            if (def == null) def = AvatarDefinition.CreateDefault();
            if (def.BodyHeight <= 0.01f)
                def.BodyHeight = BaseHeight * (def.BodyScale > 0.01f ? def.BodyScale : 1f);

            var rig = new AvatarRig();
            var root = new GameObject("Avatar");
            root.transform.SetParent(transform, false);

            float s = def.BodyHeight / BaseHeight;   // 体型缩放（相对基准人）
            def.BodyScale = s;
            float headR = AvatarBodyMetrics.HeadRadius * def.HeadScale * s;

            // ---- 标准 Humanoid 骨架 + Avatar（方案 4.1「支持 Humanoid 骨骼的模型」）----
            // 骨架为纯坐标节点（无 Renderer，不参与渲染），视觉仍由下方程序化网格提供；
            // 未来替换为真实 GLB 模型时，只需把外部网格挂到同一套骨骼上。
            rig.Skeleton = AvatarRigBuilder.Build(root.transform, s);
            rig.Animator = AttachHumanoidAnimator(rig.Skeleton);
            if (rig.Skeleton != null) def.Avatar = rig.Skeleton.Avatar;

            var skinMat = CreateMaterial(def.SkinColor, 0.28f, 0f);         // 皮肤：略低 smoothness、无金属度
            var nailMat = CreateMaterial(Color.Lerp(def.SkinColor, Color.white, 0.35f), 0.42f, 0f);
            var clothMat = CreateMaterial(def.ClothColor, 0.15f, 0f);
            var trimMat = CreateMaterial(Color.Lerp(def.ClothColor, Color.black, 0.32f), 0.20f, 0f);
            var darkMat = CreateMaterial(def.DarkColor, 0.25f, 0f);
            var hairMat = CreateMaterial(def.HairColor, 0.35f, 0f);

            BuildStool(root.transform, s, darkMat, trimMat);
            BuildLegs(root.transform, s, clothMat, darkMat);
            BuildTorso(root.transform, s, clothMat, trimMat);
            BuildNeckHead(root.transform, def, s, headR, skinMat, darkMat, hairMat);
            var shoulders = BuildShoulders(root.transform, s, clothMat);

            var lArm = BuildArm(root.transform, "LeftArm", leftHand: true, s, skinMat, clothMat, trimMat);
            var rArm = BuildArm(root.transform, "RightArm", leftHand: false, s, skinMat, clothMat, trimMat);

            // 【左右约定】位置与手性（chirality）是两件事，必须分开给：
            // ① 位置：左手在局部 −X（整体绕 Y 旋转 180° 后面向 −Z，局部 −X → 世界 +X = 角色真正的左手侧 = 低音侧）；
            // ② 手性：拇指朝身体中线 —— 左手在局部 −X 侧，内侧 = +X；右手在 +X 侧，内侧 = −X。
            // 改造前用同一个 side 同时决定位置与网格镜像，导致两只手的手型互换（左手长着右手的拇指侧）。
            var lHand = BuildHand(root.transform, "LeftHand", skinMat, nailMat, s, leftHand: true, def);
            var rHand = BuildHand(root.transform, "RightHand", skinMat, nailMat, s, leftHand: false, def);

            rig.Root = root;
            rig.LeftShoulder = shoulders.L;
            rig.RightShoulder = shoulders.R;
            rig.LeftArm = lArm;
            rig.RightArm = rArm;
            rig.LeftHand = lHand;
            rig.RightHand = rHand;
            return rig;
        }

        // ==================== 手部（附录 A.3 几何重绘 / 方案 5.1 第 1 项） ====================

        /// <summary>
        /// 构建单手：腕部连接 → 掌部（单块连续截面放样）→ 5 指 × 3 关节（MCP/PIP/DIP）。
        /// <para>
        /// <b>几何与关节分离</b>（附录 A.2）：网格由 <see cref="HandMeshFactory"/> 生成
        /// （连续截面、逐段渐缩、圆指尖、可选指甲），关节层级由本方法建立，
        /// 因此后续调整手掌/手指形状不会改变骨骼语义、键位标尺与 IK 目标。
        /// </para>
        /// <para>
        /// <see cref="IHandRig.Fingers"/> 仍是「每指一个根节点」（由姿态控制器对准目标键），
        /// 三关节挂在根的 <c>Chain</c> 子节点下；拇指对掌方向烘在链节点上（约 57°），虎口可见。
        /// </para>
        /// <para>
        /// <b>手性</b>：<paramref name="leftHand"/> 只表示解剖学侧别。腕部位置由它推出（左手局部 −X），
        /// 网格镜像与手指扇形则由「内侧方向」推出（左手内侧 = 局部 +X，右手内侧 = 局部 −X），
        /// 因此两只手的拇指都朝身体中线，不会出现「左手长得像右手」。
        /// </para>
        /// </summary>
        private IHandRig BuildHand(Transform parent, string name, Material skinMat, Material nailMat,
            float s, bool leftHand, AvatarDefinition def)
        {
            // 位置符号：角色面向局部 +Z（整体再旋转 180°），左手在局部 −X。
            float positionSign = leftHand ? -1f : 1f;

            // 内侧符号：局部 +X 指向身体中线的方向（左手在 −X 侧 → 内侧为 +X）。
            float inward = leftHand ? 1f : -1f;

            var quality = def != null ? def.Quality : null;
            var meshDef = HandMeshDefinition.FromMetrics(def != null ? def.Hand : null, s, inward,
                quality != null ? quality.MeshDetail : AvatarMeshDetail.Medium);

            float palmL = meshDef.PalmLength;
            float palmW = meshDef.PalmWidth;
            float palmT = meshDef.PalmThickness;
            float wristR = meshDef.WristRadius;

            var wrist = new GameObject(name).transform;
            wrist.SetParent(parent, false);
            wrist.localPosition = new Vector3(positionSign * 0.20f * s, 0.90f * s, 0.10f * s);

            // 腕部连接：一个比掌根略小的过渡体，把「手臂（圆形）→ 手掌（扁形）」的截面差摊平。
            // 尺寸必须小于掌根截面，否则会在腕部形成一圈可见的凸环（附录 A.3.2 第 6 条）。
            CreatePart(PrimitiveType.Sphere, wrist, "Wrist", new Vector3(0f, 0f, -wristR * 0.55f),
                new Vector3(palmW * 0.62f, palmT * 0.85f, wristR * 1.65f), skinMat);

            // 掌部：单块连续放样网格（腕部收窄 → 掌心变宽 → 掌指关节略收，含大鱼际/小鱼际隆起）
            AttachMesh(wrist, "PalmMesh", HandMeshFactory.BuildPalm(meshDef), skinMat);

            // Palm 节点保留在掌心处（拇指朝向抽查与掌心穿模判定都以它为基准）
            var palm = new GameObject("Palm").transform;
            palm.SetParent(wrist, false);
            palm.localPosition = new Vector3(0f, 0f, palmL * 0.55f);

            var fingers = new List<Transform>(5);
            var fingertips = new Transform[5];
            var joints = new Transform[5][];
            var reach = new float[5];

            for (int f = 0; f < 5; f++)
            {
                var fingerRoot = new GameObject($"Finger{f + 1}").transform;
                fingerRoot.SetParent(wrist, false);

                var knuckle = AvatarBodyMetrics.KnucklePositions[f];
                fingerRoot.localPosition = new Vector3(knuckle.x * inward, knuckle.y * s, knuckle.z * s);

                // 手指根保持单位旋转：朝目标键的对准由 FingerPoseController 旋转本节点（语义与旧版一致）；
                // 解剖朝向（拇指外展/对掌、四指扇形）放在链节点上，避免被姿态控制器覆盖。
                fingerRoot.localRotation = Quaternion.identity;

                float yaw = f == 0
                    ? (def != null ? def.Hand.ThumbAbduction : AvatarBodyMetrics.ThumbSpreadDegrees)
                    : AvatarBodyMetrics.FingerChainYaw[f];
                var chain = new GameObject("Chain").transform;
                chain.SetParent(fingerRoot, false);
                chain.localRotation = Quaternion.Euler(0f, inward * yaw, 0f);

                BuildFingerChain(chain, f, meshDef, skinMat, nailMat, out joints[f], out fingertips[f], out reach[f]);
                fingers.Add(fingerRoot);
            }

            return new HandRig(wrist, palm, fingers, fingertips, joints, reach);
        }

        /// <summary>
        /// 在指链节点下构建 MCP → PIP → DIP 三关节，并把每段网格挂到对应关节上。
        /// 关节间距取段长的 96%（与网格 7% 的重叠区配合，弯曲时不露缝）。
        /// </summary>
        private void BuildFingerChain(Transform chain, int fingerIndex, HandMeshDefinition meshDef,
            Material skin, Material nail, out Transform[] joints, out Transform tip, out float reach)
        {
            joints = new Transform[HandMeshFactory.SegmentCount];

            Transform node = chain;
            float offset = 0f;
            for (int seg = 0; seg < HandMeshFactory.SegmentCount; seg++)
            {
                var joint = new GameObject($"Joint{seg}").transform;
                joint.SetParent(node, false);
                joint.localPosition = new Vector3(0f, 0f, offset);
                joints[seg] = joint;

                AttachMesh(joint, $"Seg{seg}",
                    HandMeshFactory.BuildFingerSegment(meshDef, fingerIndex, seg), skin);

                if (meshDef.IncludeNails && seg == HandMeshFactory.SegmentCount - 1)
                    AttachMesh(joint, "Nail", HandMeshFactory.BuildNail(meshDef, fingerIndex), nail);

                node = joint;
                offset = meshDef.SegmentLength(fingerIndex, seg) * 0.96f;
            }

            // 指尖接触点：末节网格的半球顶端略往回取，作为「指腹落键」的参考点
            float tipRadius = meshDef.SegmentRadius(fingerIndex, 2) * 0.88f;
            float tipOffset = meshDef.SegmentLength(fingerIndex, 2) + tipRadius * 0.82f;

            tip = new GameObject("Tip").transform;
            tip.SetParent(joints[2], false);
            tip.localPosition = new Vector3(0f, 0f, tipOffset);

            reach = meshDef.SegmentLength(fingerIndex, 0) * 0.96f
                  + meshDef.SegmentLength(fingerIndex, 1) * 0.96f
                  + tipOffset;
        }

        /// <summary>挂载运行时网格（统一登记，场景卸载时释放，避免原生网格泄漏）。</summary>
        private Transform AttachMesh(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            if (mesh == null) return t;

            _runtimeMeshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return t;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _runtimeMeshes.Count; i++)
            {
                if (_runtimeMeshes[i] != null) Object.Destroy(_runtimeMeshes[i]);
            }
            _runtimeMeshes.Clear();
        }

        private sealed class HandRig : IHandRig, IFingerJointRig
        {
            public Transform Wrist { get; }
            public Transform Palm { get; }
            public IReadOnlyList<Transform> Fingers { get; }

            private readonly Transform[] _fingertips;
            private readonly Transform[][] _joints;
            private readonly float[] _lengths;

            public HandRig(Transform wrist, Transform palm, IReadOnlyList<Transform> fingers,
                Transform[] fingertips, Transform[][] joints, float[] lengths)
            {
                Wrist = wrist;
                Palm = palm;
                Fingers = fingers;
                _fingertips = fingertips;
                _joints = joints;
                _lengths = lengths;
            }

            public Transform GetFingertip(int fingerIndex)
            {
                if (_fingertips == null || fingerIndex < 0 || fingerIndex >= _fingertips.Length) return null;
                return _fingertips[fingerIndex];
            }

            public bool TryGetJoints(int fingerIndex,
                out Transform proximal, out Transform intermediate, out Transform distal)
            {
                proximal = intermediate = distal = null;
                if (_joints == null || fingerIndex < 0 || fingerIndex >= _joints.Length) return false;

                var chain = _joints[fingerIndex];
                if (chain == null || chain.Length < 3) return false;

                proximal = chain[0];
                intermediate = chain[1];
                distal = chain[2];
                return proximal != null && intermediate != null && distal != null;
            }

            public float GetFingerLength(int fingerIndex)
            {
                if (_lengths == null || fingerIndex < 0 || fingerIndex >= _lengths.Length) return 0f;
                return _lengths[fingerIndex];
            }
        }

        // ==================== 躯干 / 腿部 / 琴凳 ====================

        /// <summary>琴凳：坐面与臀部接触（不悬空），四腿落地（方案 5.2 短期「坐姿与键盘相对位置统一」）。</summary>
        private static void BuildStool(Transform root, float s, Material darkMat, Material trimMat)
        {
            float seatTop = AvatarBodyMetrics.StoolTopHeight * s;
            float seatT = 0.05f * s;
            float seatW = 0.36f * s;
            float seatD = 0.28f * s;
            float seatZ = 0.10f * s;

            CreatePart(PrimitiveType.Cube, root, "Stool",
                new Vector3(0f, seatTop - seatT * 0.5f, seatZ),
                new Vector3(seatW, seatT, seatD), darkMat);

            // 坐面软垫（衣料色），与臀部/大腿形成真实接触
            CreatePart(PrimitiveType.Sphere, root, "StoolCushion",
                new Vector3(0f, seatTop + 0.006f * s, seatZ),
                new Vector3(seatW * 0.98f, 0.022f * s, seatD * 0.98f), trimMat);

            float legH = seatTop - seatT;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    CreatePart(PrimitiveType.Cylinder, root, $"StoolLeg_{sx}_{sz}",
                        new Vector3(sx * seatW * 0.40f, legH * 0.5f, seatZ + sz * seatD * 0.38f),
                        new Vector3(0.022f * s, legH * 0.5f, 0.022f * s), darkMat);
                }
            }
        }

        /// <summary>坐姿腿：大腿前伸 + 小腿垂直 + 脚（不同半径，避免同粗圆柱的机械感）。</summary>
        private static void BuildLegs(Transform root, float s, Material clothMat, Material darkMat)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.105f * s;
                CreatePart(PrimitiveType.Capsule, root, $"Thigh_{side}",
                    new Vector3(x, 0.50f * s, 0.17f * s),
                    new Vector3(0.108f * s, 0.135f * s, 0.108f * s), clothMat,
                    Quaternion.Euler(90f, 0f, 0f));
                CreatePart(PrimitiveType.Sphere, root, $"Knee_{side}",
                    new Vector3(x, 0.475f * s, 0.29f * s),
                    new Vector3(0.098f * s, 0.092f * s, 0.098f * s), clothMat);
                CreatePart(PrimitiveType.Capsule, root, $"Shin_{side}",
                    new Vector3(x, 0.29f * s, 0.32f * s),
                    new Vector3(0.082f * s, 0.205f * s, 0.082f * s), clothMat);
                CreatePart(PrimitiveType.Cube, root, $"Foot_{side}",
                    new Vector3(x, 0.035f * s, 0.37f * s),
                    new Vector3(0.085f * s, 0.07f * s, 0.19f * s), darkMat);
            }
        }

        /// <summary>
        /// 躯干：髋 / 腰 / 胸 / 肩带四段宽度递减递变（不是同宽圆柱），使用平滑椭球 + 重叠，
        /// 并把服装（衣领 / 下摆 / 腰带）作为独立可见层（审查 1.1 方案 A）。
        /// </summary>
        private static void BuildTorso(Transform root, float s, Material clothMat, Material trimMat)
        {
            float hipW = AvatarBodyMetrics.HipWidth;
            float waistW = AvatarBodyMetrics.WaistWidth;
            float chestW = AvatarBodyMetrics.ChestWidth;

            // 身体（衣料色即服装本体，服装另加领/下摆/腰带层）
            CreatePart(PrimitiveType.Sphere, root, "Hip",
                new Vector3(0f, AvatarBodyMetrics.SittingHipHeight * s, 0f),
                new Vector3(hipW * s, 0.136f * s, 0.212f * s), clothMat);

            CreatePart(PrimitiveType.Sphere, root, "Waist",
                new Vector3(0f, 0.660f * s, 0f),
                new Vector3(waistW * s, 0.170f * s, 0.192f * s), clothMat);

            CreatePart(PrimitiveType.Sphere, root, "Chest",
                new Vector3(0f, 0.826f * s, 0f),
                new Vector3(chestW * s, 0.216f * s, 0.210f * s), clothMat);

            CreatePart(PrimitiveType.Sphere, root, "UpperTorso",
                new Vector3(0f, 0.925f * s, 0f),
                new Vector3(chestW * 0.99f * s, 0.130f * s, 0.200f * s), clothMat);

            // 服装独立层：下摆（覆盖髋部，向外微张）+ 腰带（分隔上衣与裤装）+ 衣领（环绕颈部）
            CreatePart(PrimitiveType.Sphere, root, "ClothHem",
                new Vector3(0f, 0.585f * s, 0f),
                new Vector3((hipW + 0.030f) * s, 0.070f * s, (0.212f + 0.026f) * s), trimMat);

            CreatePart(PrimitiveType.Cylinder, root, "WaistBand",
                new Vector3(0f, 0.605f * s, 0f),
                new Vector3(waistW * 1.06f * s, 0.016f * s, 0.196f * s), trimMat);

            CreatePart(PrimitiveType.Cylinder, root, "Collar",
                new Vector3(0f, 1.000f * s, 0.004f * s),
                new Vector3(0.134f * s, 0.013f * s, 0.130f * s), trimMat);
        }

        /// <summary>颈 + 头（椭球 + 下颌 + 耳 + 鼻 + 眼 + 眉）+ 发型；风格统一但不写实（审查 1.1 方案 A）。</summary>
        private static void BuildNeckHead(Transform root, AvatarDefinition def, float s, float headR,
            Material skinMat, Material darkMat, Material hairMat)
        {
            CreatePart(PrimitiveType.Capsule, root, "Neck",
                new Vector3(0f, AvatarBodyMetrics.NeckHeight * s, 0f),
                new Vector3(0.068f * s, 0.042f * s, 0.066f * s), skinMat);

            float headY = AvatarBodyMetrics.HeadCenterHeight * s;
            float cheek = def.Cartoon ? 1.06f : 1.00f;

            // 头部主体：略窄于深度的椭球（比纯球体更接近人头轮廓）
            CreatePart(PrimitiveType.Sphere, root, "Head",
                new Vector3(0f, headY, 0f),
                new Vector3(headR * 1.84f * cheek, headR * 2.02f, headR * 1.94f), skinMat);

            // 下颌 / 下巴过渡（消除「球体下缘」的突兀感）
            CreatePart(PrimitiveType.Sphere, root, "Jaw",
                new Vector3(0f, headY - headR * 0.70f, headR * 0.10f),
                new Vector3(headR * 1.42f * cheek, headR * 0.86f, headR * 1.56f), skinMat);

            // 耳朵
            for (int side = -1; side <= 1; side += 2)
            {
                CreatePart(PrimitiveType.Sphere, root, $"Ear_{side}",
                    new Vector3(side * headR * 0.90f * cheek, headY + headR * 0.02f, -headR * 0.04f),
                    new Vector3(headR * 0.20f, headR * 0.42f, headR * 0.30f), skinMat);
            }

            // 鼻（鼻梁 + 鼻头）
            CreatePart(PrimitiveType.Capsule, root, "NoseBridge",
                new Vector3(0f, headY + headR * 0.02f, headR * 0.86f),
                new Vector3(headR * 0.15f, headR * 0.24f, headR * 0.15f), skinMat);
            CreatePart(PrimitiveType.Sphere, root, "Nose",
                new Vector3(0f, headY - headR * 0.16f, headR * 0.96f),
                new Vector3(headR * 0.22f, headR * 0.18f, headR * 0.22f), skinMat);

            // 眼睛（眼白 + 瞳孔）与眉毛
            float eyeY = headY + headR * 0.14f;
            float eyeX = headR * 0.38f * cheek;
            for (int side = -1; side <= 1; side += 2)
            {
                CreatePart(PrimitiveType.Sphere, root, $"EyeWhite_{side}",
                    new Vector3(side * eyeX, eyeY, headR * 0.84f),
                    new Vector3(headR * 0.30f, headR * 0.20f, headR * 0.16f), skinMat);
                CreatePart(PrimitiveType.Sphere, root, $"Eye_{side}",
                    new Vector3(side * eyeX, eyeY, headR * 0.90f),
                    new Vector3(headR * 0.16f, headR * 0.16f, headR * 0.12f), darkMat);

                var brow = CreatePart(PrimitiveType.Sphere, root, $"Brow_{side}",
                    new Vector3(side * eyeX, eyeY + headR * 0.30f, headR * 0.84f),
                    new Vector3(headR * 0.38f, headR * 0.08f, headR * 0.12f), darkMat);
                brow.localRotation = Quaternion.Euler(0f, 0f, side * -7f);
            }

            // 嘴（轻微的一笔，不做写实口型）
            CreatePart(PrimitiveType.Sphere, root, "Mouth",
                new Vector3(0f, headY - headR * 0.52f, headR * 0.84f),
                new Vector3(headR * 0.30f, headR * 0.06f, headR * 0.10f), darkMat);

            BuildHair(root, def, headY, headR, hairMat);
        }

        /// <summary>发型：清楚的大轮廓 + 少量发束（不使用大量细长圆柱，避免噪点与穿插）。</summary>
        private static void BuildHair(Transform root, AvatarDefinition def, float headY, float headR, Material hairMat)
        {
            // 基础发帽（覆盖头顶与后部，露出面部）
            CreatePart(PrimitiveType.Sphere, root, "HairCap",
                new Vector3(0f, headY + headR * 0.30f, -headR * 0.16f),
                new Vector3(headR * 1.94f, headR * 1.64f, headR * 1.94f), hairMat);

            // 刘海（覆盖额头，明确「发型轮廓」而不是只戴一个帽子）
            var fringe = CreatePart(PrimitiveType.Sphere, root, "HairFringe",
                new Vector3(0f, headY + headR * 0.62f, headR * 0.56f),
                new Vector3(headR * 1.76f, headR * 0.56f, headR * 0.80f), hairMat);
            fringe.localRotation = Quaternion.Euler(16f, 0f, 0f);

            if (def.HairStyle == 0)
            {
                // 长发：两侧披肩发 + 背部发束（各为整块，不做细丝）
                for (int i = -1; i <= 1; i += 2)
                {
                    CreatePart(PrimitiveType.Capsule, root, $"HairSide_{i}",
                        new Vector3(i * headR * 0.92f, headY - headR * 0.42f, -headR * 0.34f),
                        new Vector3(headR * 0.42f, headR * 0.66f, headR * 0.44f), hairMat);
                }
                CreatePart(PrimitiveType.Capsule, root, "HairBack",
                    new Vector3(0f, headY - headR * 0.58f, -headR * 0.82f),
                    new Vector3(headR * 1.22f, headR * 0.78f, headR * 0.40f), hairMat);
            }
            else if (def.HairStyle == 1)
            {
                // 短发：鬓角 + 后颈短发（轮廓清楚）
                for (int i = -1; i <= 1; i += 2)
                {
                    CreatePart(PrimitiveType.Capsule, root, $"HairSideburn_{i}",
                        new Vector3(i * headR * 0.88f, headY - headR * 0.10f, -headR * 0.26f),
                        new Vector3(headR * 0.24f, headR * 0.34f, headR * 0.34f), hairMat);
                }
                CreatePart(PrimitiveType.Sphere, root, "HairNape",
                    new Vector3(0f, headY - headR * 0.46f, -headR * 0.72f),
                    new Vector3(headR * 1.18f, headR * 0.62f, headR * 0.46f), hairMat);
            }
            else
            {
                // 卡通呆毛（单束，避免噪点）
                var ahoge = CreatePart(PrimitiveType.Capsule, root, "Ahoge",
                    new Vector3(0f, headY + headR * 1.48f, 0f),
                    new Vector3(headR * 0.16f, headR * 0.62f, headR * 0.16f), hairMat);
                ahoge.localRotation = Quaternion.Euler(18f, 0f, 0f);
            }
        }

        // ==================== 肩 / 臂 ====================

        /// <summary>
        /// 肩：空节点供 IK 使用 + 视觉肩球 + 覆盖「躯干↔手臂」接缝的肩带过渡体块（审查 1.1 方案 A）。
        /// </summary>
        private static (Transform L, Transform R) BuildShoulders(Transform root, float s, Material clothMat)
        {
            // 【左右约定】人物整体绕 Y 旋转 180°（面向 -Z 朝钢琴），因此「角色的左手」位于世界 +X 侧
            // （面向 -Z 时，左手在 +X；低音键也在 +X 侧）。这里必须在局部坐标写 −X，
            // 旋转后才落到世界 +X。改造前写成 +X 会让整只左手落到世界 −X，即角色真正的右手侧，
            // 造成「左右手反向」：画面右侧的手才是左手，而低音键也在画面右侧。
            var lShoulder = new GameObject("LeftShoulder").transform;
            lShoulder.SetParent(root, false);
            lShoulder.localPosition = new Vector3(-AvatarBodyMetrics.ShoulderHalfWidth * s, AvatarBodyMetrics.ShoulderHeight * s, 0f);
            CreatePart(PrimitiveType.Sphere, lShoulder, "Ball", Vector3.zero, Vector3.one * (0.098f * s), clothMat);

            var rShoulder = new GameObject("RightShoulder").transform;
            rShoulder.SetParent(root, false);
            rShoulder.localPosition = new Vector3(AvatarBodyMetrics.ShoulderHalfWidth * s, AvatarBodyMetrics.ShoulderHeight * s, 0f);
            CreatePart(PrimitiveType.Sphere, rShoulder, "Ball", Vector3.zero, Vector3.one * (0.098f * s), clothMat);

            // 肩带：从胸廓延伸到肩球，遮住躯干与手臂之间的接缝
            for (int side = -1; side <= 1; side += 2)
            {
                CreatePart(PrimitiveType.Sphere, root, $"ShoulderCap_{side}",
                    new Vector3(side * 0.152f * s, 0.952f * s, 0f),
                    new Vector3(0.140f * s, 0.108f * s, 0.166f * s), clothMat);
            }

            return (lShoulder, rShoulder);
        }

        /// <summary>
        /// 手臂：单段胶囊（由 HandIKController.StretchArm 逐帧拉伸肩→腕，无肘部接缝），
        /// 叠加「袖口 + 袖筒」两个沿臂轴对齐的服装子体块 —— 它们只沿臂轴被等比拉伸，
        /// 因此袖子长度始终与手臂长度成比例，不会失真。
        /// </summary>
        private static Transform BuildArm(Transform root, string name, bool leftHand, float s,
            Material skinMat, Material clothMat, Material trimMat)
        {
            var arm = CreatePart(PrimitiveType.Capsule, root, name,
                new Vector3((leftHand ? -1f : 1f) * 0.20f * s, 0.90f * s, 0.10f * s),
                new Vector3(AvatarBodyMetrics.ArmRadius * s, 0.28f * s, AvatarBodyMetrics.ArmRadius * s), skinMat);

            // 袖筒（覆盖上臂到肘部，服装独立可见层）：局部尺寸经父级 x/y 缩放换算后≈ 33 mm 半径
            CreatePart(PrimitiveType.Capsule, arm, "Sleeve",
                new Vector3(0f, -0.55f, 0f), new Vector3(1.25f, 0.55f, 1.25f), clothMat);

            // 袖口（比袖筒略粗，形成收口的层次）
            CreatePart(PrimitiveType.Capsule, arm, "Cuff",
                new Vector3(0f, -0.03f, 0f), new Vector3(1.42f, 0.075f, 1.42f), trimMat);

            return arm;
        }

        // ==================== 构建辅助 ====================

        private static Transform CreatePart(PrimitiveType type, Transform parent, string name,
            Vector3 localPos, Vector3 localScale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localScale = localScale;
            if (rot.HasValue) t.localRotation = rot.Value;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.Destroy(go.GetComponent<Collider>()); // 纯视觉件，无碰撞需求
            return t;
        }

        /// <summary>创建材质：皮肤用略低 smoothness + 零金属度，避免「塑料积木感」（方案 5.1 第 5 项 / 5.4）。</summary>
        private static Material CreateMaterial(Color c, float smoothness, float metallic)
        {
            var shader = Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader) { color = c };
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness); // Standard
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness); // URP Lit
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }
    }
}
