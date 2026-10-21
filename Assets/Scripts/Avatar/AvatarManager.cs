using System.Collections.Generic;
using PianoTeacher3D.Characters;
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

        // ---- 角色库增量（方案 7.1 / 7.3）：外部角色以「视图」形式挂接，内置角色仍走 _rigs ----
        private ICharacterView _currentView;
        private CharacterProfile _currentProfile;
        private CharacterSwitchService _switchService;
        private AvatarDefinition _externalFallbackDefinition;

        /// <summary>运行时生成的网格（手部放样网格），场景销毁时统一释放，避免原生对象泄漏。</summary>
        private readonly List<Mesh> _runtimeMeshes = new List<Mesh>();

        /// <summary>全部角色定义（只读；索引与 <see cref="SwitchAvatar"/> 一致）。</summary>
        public IReadOnlyList<AvatarDefinition> Definitions => _definitions;

        /// <summary>
        /// 当前角色定义；未构建时为 null。
        /// <para>
        /// 外部激活角色（预制体视图）优先：返回它提供的 IK 参数兜底定义，
        /// 因此 <c>HandIKController</c> 读取 <c>HandMetrics</c> 时不会拿到 null。
        /// </para>
        /// </summary>
        public AvatarDefinition CurrentDefinition
        {
            get
            {
                var external = ExternalRigSource;
                if (external != null) return external.Definition ?? EnsureExternalFallbackDefinition();

                return (_definitions.Count > 0 && _currentIndex >= 0 && _currentIndex < _definitions.Count)
                    ? _definitions[_currentIndex]
                    : null;
            }
        }

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

        /// <summary>当前角色视图（内置角色也有包装；未构建时为 null）。供角色库 UI 读取缩略图与支持槽位。</summary>
        public ICharacterView CurrentView => _currentView;

        /// <summary>
        /// 当前角色适用的手臂解算策略。
        /// 内置程序化角色用胶囊式（平移 + 拉伸视觉胶囊，与改造前一致）；
        /// 外部蒙皮角色必须用纯旋转式——否则会拉伸骨骼、让前臂与手断开、破坏蒙皮，
        /// 表现为「人物破面 + 手不跟键」。
        /// </summary>
        public IArmChainSolver ArmSolver
        {
            get
            {
                var source = _currentView as ICharacterRigSource;
                bool skinned = source != null && !source.IsBuiltInBacked;
                return skinned
                    ? (IArmChainSolver)RotationalArmChainSolver.Instance
                    : CapsuleArmChainSolver.Instance;
            }
        }

        /// <summary>当前角色的资料（内置角色通过 <see cref="SwitchAvatar"/> 切换时可能为 null）。</summary>
        public CharacterProfile CurrentProfile => _currentProfile;

        /// <summary>视图变化时触发（外部角色切换、或切回内置角色）。</summary>
        public event System.Action<ICharacterView> OnCharacterViewChanged;

        /// <summary>角色切换事务终结时触发（成功 / 回滚 / 取消）。</summary>
        public event System.Action<CharacterSwitchResult> OnCharacterSwitchFinished;

        /// <summary>
        /// 外部激活角色提供的骨骼来源。
        /// <para>
        /// 内置角色的包装视图 <c>IsBuiltInBacked == true</c>，这里返回 null，
        /// 于是所有骨骼属性继续走原有 <c>_rigs</c> 路径——这是「3 个内置角色行为逐位不变」的关键。
        /// </para>
        /// </summary>
        private ICharacterRigSource ExternalRigSource
        {
            get
            {
                var source = _currentView as ICharacterRigSource;
                return source != null && !source.IsBuiltInBacked ? source : null;
            }
        }

        // 当前角色暴露给 HandIKController 的骨骼引用（外部角色存在时优先取它的骨骼）
        public Transform LeftShoulder => ExternalRigSource != null ? ExternalRigSource.LeftShoulder : _rigs[_currentIndex].LeftShoulder;
        public Transform RightShoulder => ExternalRigSource != null ? ExternalRigSource.RightShoulder : _rigs[_currentIndex].RightShoulder;
        public Transform LeftArm => ExternalRigSource != null ? ExternalRigSource.LeftArm : _rigs[_currentIndex].LeftArm;
        public Transform RightArm => ExternalRigSource != null ? ExternalRigSource.RightArm : _rigs[_currentIndex].RightArm;
        /// <summary>左前臂（肘 → 腕）。与 <see cref="LeftArm"/>（上臂）配合做两段解析 IK。</summary>
        public Transform LeftForeArm => ExternalRigSource != null ? ExternalRigSource.LeftForeArm : _rigs[_currentIndex].LeftForeArm;
        public Transform RightForeArm => ExternalRigSource != null ? ExternalRigSource.RightForeArm : _rigs[_currentIndex].RightForeArm;
        public Transform LeftElbow => ExternalRigSource != null ? ExternalRigSource.LeftElbow : _rigs[_currentIndex].LeftElbow;
        public Transform RightElbow => ExternalRigSource != null ? ExternalRigSource.RightElbow : _rigs[_currentIndex].RightElbow;
        public IHandRig LeftHand => ExternalRigSource != null ? ExternalRigSource.LeftHand : _rigs[_currentIndex].LeftHand;
        public IHandRig RightHand => ExternalRigSource != null ? ExternalRigSource.RightHand : _rigs[_currentIndex].RightHand;

        /// <summary>当前角色的标准 Humanoid 骨架（方案 4.1 / 3.2）。未构建时为 null。</summary>
        public AvatarRigBuilder.Skeleton CurrentSkeleton
        {
            get
            {
                var external = ExternalRigSource;
                if (external != null) return external.Skeleton;

                return (_rigs != null && _currentIndex >= 0 && _currentIndex < _rigs.Count)
                    ? _rigs[_currentIndex].Skeleton
                    : null;
            }
        }

        /// <summary>当前角色的骨架是否为 Unity 认可的合法 Humanoid。</summary>
        public bool HasHumanoidRig => CurrentSkeleton != null && CurrentSkeleton.IsHumanoid;

        /// <summary>取指定角色的骨架（测试与调试用）。索引越界返回 null。</summary>
        public AvatarRigBuilder.Skeleton GetSkeleton(int index) =>
            (_rigs != null && index >= 0 && index < _rigs.Count) ? _rigs[index].Skeleton : null;

        /// <summary>
        /// 当前角色的躯干包围盒（髋 → 颈 + 双肩，外扩体积）。
        /// <para>
        /// 供相机特写做「负空间」判断（接触方案批次 3.1）：骨骼点连线本身很细，
        /// 因此按固定余量外扩成体块；不含手臂与头，避免把抬手的正常动作误判成遮挡。
        /// </para>
        /// </summary>
        public bool TryGetTorsoBounds(out Bounds bounds)
        {
            bounds = default;
            var skeleton = CurrentSkeleton;
            if (skeleton == null || skeleton.Hips == null) return false;

            bool any = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            Encapsulate(skeleton.Hips);
            Encapsulate(skeleton.Spine);
            Encapsulate(skeleton.Chest);
            Encapsulate(skeleton.UpperChest);
            Encapsulate(skeleton.Neck);
            Encapsulate(skeleton.LeftShoulder);
            Encapsulate(skeleton.RightShoulder);

            if (!any) return false;

            bounds.SetMinMax(min, max);
            // 骨骼点没有体积：按胸廓厚度/高度外扩，得到「看得见会挡画」的体块
            bounds.Expand(new Vector3(0.10f, 0.05f, 0.10f));
            return true;

            void Encapsulate(Transform t)
            {
                if (t == null) return;
                Vector3 p = t.position;
                if (!any)
                {
                    min = p;
                    max = p;
                    any = true;
                    return;
                }
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

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

            // 骨架就绪后立刻建立内置角色的视图包装，保证 CurrentView 从场景装配完成起就非 null。
            // 之前是首次 SwitchAvatar 才惰性创建，导致启动阶段「照片外观绑定」「角色库高亮」
            // 都拿到 null（CurrentCharacterView 为 null 已被 PlayMode 用例抓到）。
            EnsureBuiltInView();
        }

        private static void DestroyRig(AvatarRig rig)
        {
            if (rig?.Root == null) return;
            // Destroy 是延迟到帧末执行的：先停用，避免与回退角色在同一帧内同时可见
            rig.Root.SetActive(false);
            Object.Destroy(rig.Root);
        }

        /// <summary>
        /// 切换角色：仅替换表现，不重置曲目/进度/摄像机（方案验收标准）。
        /// <para>
        /// 若当前是外部激活角色（预制体），先停用并释放它——否则两套模型会同时驱动同一双手。
        /// 内置角色之间的切换路径与改造前完全一致（<see cref="SetBuiltInActive"/> 内部实现同一套操作）。
        /// </para>
        /// </summary>
        public void SwitchAvatar(int index)
        {
            index = Mathf.Clamp(index, 0, AvatarCount - 1);

            ReleaseExternalView();

            if (index == _currentIndex)
            {
                EnsureBuiltInView();
                return;
            }

            SetBuiltInActive(index, true);
            EnsureBuiltInView();
        }

        /// <summary>
        /// 显示 / 停用某个内置角色的根节点。停用用于角色切换事务的「锁状态」步骤，
        /// 由 <c>ProceduralCharacterView</c> 调用；激活时会同步 <see cref="CurrentIndex"/>、套用四肢可见性并派发事件。
        /// </summary>
        public void SetBuiltInActive(int index, bool active)
        {
            if (_rigs == null || index < 0 || index >= _rigs.Count) return;

            if (!active)
            {
                if (_rigs[index].Root != null) _rigs[index].Root.SetActive(false);
                return;
            }

            bool changed = index != _currentIndex;
            for (int i = 0; i < _rigs.Count; i++)
            {
                if (_rigs[i].Root == null) continue;
                _rigs[i].Root.SetActive(i == index);
            }
            _currentIndex = index;

            // 新角色同样要套用当前的「手部/手臂可见性」（教学模式下切角色不能把手露出来）
            ApplyLimbsVisibility(index, _limbsVisible);

            if (changed) OnAvatarChanged?.Invoke(index);
        }

        /// <summary>取指定内置角色的根节点（角色库 UI 与视图包装用）。索引越界返回 null。</summary>
        public Transform GetAvatarRoot(int index) =>
            (_rigs != null && index >= 0 && index < _rigs.Count && _rigs[index].Root != null)
                ? _rigs[index].Root.transform
                : null;

        /// <summary>取指定内置角色绑定的 Animator（无 Humanoid 骨架时为 null）。</summary>
        public Animator GetAnimator(int index) =>
            (_rigs != null && index >= 0 && index < _rigs.Count) ? _rigs[index].Animator : null;

        /// <summary>取指定内置角色的材质列表（整体色调外观槽用）。索引越界返回空列表。</summary>
        public IReadOnlyList<Material> GetMaterials(int index)
        {
            if (_rigs != null && index >= 0 && index < _rigs.Count) return _rigs[index].Materials;
            return System.Array.Empty<Material>();
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
            SetActive(rig.LeftForeArm, visible);
            SetActive(rig.RightForeArm, visible);
            SetActive(rig.LeftElbow, visible);
            SetActive(rig.RightElbow, visible);
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

        // ==================== 角色库接线（方案 7.1 / 7.3，增量部分） ====================

        /// <summary>
        /// 按资料激活角色。
        /// <para>
        /// 程序化内置角色走<b>原有</b> <see cref="SwitchAvatar"/> 路径（不引入事务开销，行为与改造前一致）；
        /// 预制体角色走 <see cref="CharacterSwitchService"/> 的 8 步事务（加载 → 校验 → 锁 → 激活 → 换引用 → 确认 → 卸载旧角色）。
        /// 事务内含回滚：失败时旧角色保持可驱动，不会出现「两套骨架都停了」的空窗。
        /// </para>
        /// <para>
        /// 本方法<b>不触碰</b>曲目、播放位置、速度、循环与音频——它只换表现层。
        /// </para>
        /// </summary>
        public CharacterSwitchResult TryActivate(CharacterProfile profile)
        {
            string currentId = _currentView != null ? _currentView.CharacterId : string.Empty;

            var valid = CharacterAssetValidator.ValidateProfile(profile);
            if (!valid.Ok)
                return new CharacterSwitchResult(CharacterSwitchState.RolledBack, valid.Message, currentId, currentId);

            if (profile.Source == CharacterSource.ProceduralBuiltIn)
                return ActivateBuiltIn(profile, currentId);

            if (_currentView == null) EnsureBuiltInView();

            var service = EnsureSwitchService();
            service.SetCurrent(_currentView);   // 内置包装或上一个外部角色：事务回滚的锚点

            if (!service.Request(profile)) return service.LastResult;
            while (service.Tick()) { }          // 纯 C# 步进：无真实 IO，单帧内跑完

            var result = service.LastResult;
            if (result.Succeeded && service.CurrentView != null)
            {
                _currentView = service.CurrentView;
                _currentProfile = profile;
                OnCharacterViewChanged?.Invoke(_currentView);
            }

            OnCharacterSwitchFinished?.Invoke(result);
            return result;
        }

        private CharacterSwitchResult ActivateBuiltIn(CharacterProfile profile, string currentId)
        {
            if (_currentView != null && string.Equals(_currentView.CharacterId, profile.CharacterId, System.StringComparison.Ordinal))
                return new CharacterSwitchResult(CharacterSwitchState.Committed, null, currentId, currentId);

            int index = Mathf.Clamp(profile.ProceduralIndex, 0, AvatarCount - 1);
            ReleaseExternalView();
            SetBuiltInActive(index, true);

            var view = new ProceduralCharacterView(this, index, profile);
            view.OnCharacterActivated();
            _currentView = view;
            _currentProfile = profile;
            OnCharacterViewChanged?.Invoke(view);

            var result = new CharacterSwitchResult(CharacterSwitchState.Committed, null, currentId, profile.CharacterId);
            OnCharacterSwitchFinished?.Invoke(result);
            return result;
        }

        /// <summary>停用并释放外部角色（切回内置角色时调用）。可重复调用。</summary>
        private void ReleaseExternalView()
        {
            var source = _currentView as ICharacterRigSource;
            if (source == null || source.IsBuiltInBacked) return;

            var view = _currentView;
            _currentView = null;
            _currentProfile = null;

            try { view.OnCharacterDeactivated(); }
            catch (System.Exception e) { Debug.LogWarning("[AvatarManager] 停用外部角色失败：" + e.Message); }
            try { view.DisposeCharacterResources(); }
            catch (System.Exception e) { Debug.LogWarning("[AvatarManager] 释放外部角色资源失败：" + e.Message); }

            OnCharacterViewChanged?.Invoke(null);
        }

        /// <summary>
        /// 确保当前内置角色有一个包装视图（供角色库 UI 读取缩略图/风格/支持槽位）。
        /// <para>
        /// 两个必须区分的分支：
        /// ① 当前是<b>外部激活角色</b> → 不动它（外部角色的生命周期由切换事务负责）；
        /// ② 当前是<b>内置包装</b> → 必须校验包装里的索引与 <see cref="CurrentIndex"/> 一致。
        ///    否则 <c>SwitchAvatar(0→1)</c> 后视图还停在索引 0，<c>CurrentView.CharacterId</c> 会报错角色，
        ///    角色库卡片的选中态也会跟着错。
        /// </para>
        /// </summary>
        private void EnsureBuiltInView()
        {
            var external = _currentView as ICharacterRigSource;
            if (external != null && !external.IsBuiltInBacked) return;

            if (_currentView is ProceduralCharacterView current && current.ProceduralIndex == _currentIndex) return;

            _currentView = new ProceduralCharacterView(this, _currentIndex, null);
            _currentProfile = null;
            OnCharacterViewChanged?.Invoke(_currentView);
        }

        private CharacterSwitchService EnsureSwitchService()
        {
            if (_switchService != null) return _switchService;

            _switchService = new CharacterSwitchService(new AvatarCharacterViewFactory(this));
            return _switchService;
        }

        private AvatarDefinition EnsureExternalFallbackDefinition()
        {
            if (_externalFallbackDefinition == null) _externalFallbackDefinition = AvatarDefinition.CreateDefault();
            return _externalFallbackDefinition;
        }

        // ==================== 程序化人形构建（统一尺寸表，零外部模型资产） ====================

        private sealed class AvatarRig
        {
            public GameObject Root;
            public Transform LeftShoulder;
            public Transform RightShoulder;
            public Transform LeftArm;
            public Transform RightArm;
            /// <summary>前臂（肘 → 腕）。与 <see cref="LeftArm"/>（上臂）配合做两段解析 IK。</summary>
            public Transform LeftForeArm;
            public Transform RightForeArm;
            /// <summary>肘球：遮住上臂与前臂的接缝，跟随两段 IK 求出的肘位置。</summary>
            public Transform LeftElbow;
            public Transform RightElbow;
            public IHandRig LeftHand;
            public IHandRig RightHand;

            /// <summary>标准 Humanoid 骨架（方案 4.1 / 3.2）。</summary>
            public AvatarRigBuilder.Skeleton Skeleton;

            /// <summary>绑定 Humanoid Avatar 的 Animator（骨架有效时非 null）。</summary>
            public Animator Animator;

            /// <summary>
            /// 本角色创建的基础材质（皮肤/指甲/衣料/镶边/深色/头发）。
            /// 仅用于「整体色调」外观槽；所有权仍归 AvatarManager（随场景卸载由 Unity 回收）。
            /// </summary>
            public readonly List<Material> Materials = new List<Material>();

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

            // 皮肤：调低 smoothness 是去塑料感的关键 —— 真人皮肤几乎不产生锐利镜面反射，
            // 0.28 会在颧骨与鼻尖留下明显高光斑（塑料积木感的主要来源）；0.12 接近哑光、仍保留微弱光泽。
            // 再掺入 7% 暖红模拟皮下毛细血管透光（Built-in Standard 无 SSS 时的廉价替代）。
            var skinBase = Color.Lerp(def.SkinColor, new Color(0.86f, 0.42f, 0.38f), 0.07f);
            var skinMat = CreateMaterial(skinBase, 0.12f, 0f);
            var nailMat = CreateMaterial(Color.Lerp(def.SkinColor, Color.white, 0.35f), 0.22f, 0f);
            var clothMat = CreateMaterial(def.ClothColor, 0.15f, 0f);
            var trimMat = CreateMaterial(Color.Lerp(def.ClothColor, Color.black, 0.32f), 0.20f, 0f);
            var darkMat = CreateMaterial(def.DarkColor, 0.25f, 0f);
            var hairMat = CreateMaterial(def.HairColor, 0.22f, 0f);

            // 登记基础材质，供「整体色调」外观槽（角色库 UI）使用；不改变任何渲染行为
            if (skinMat != null) rig.Materials.Add(skinMat);
            if (nailMat != null) rig.Materials.Add(nailMat);
            if (clothMat != null) rig.Materials.Add(clothMat);
            if (trimMat != null) rig.Materials.Add(trimMat);
            if (darkMat != null) rig.Materials.Add(darkMat);
            if (hairMat != null) rig.Materials.Add(hairMat);

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
            rig.LeftArm = lArm.Upper;
            rig.LeftForeArm = lArm.Fore;
            rig.LeftElbow = lArm.Elbow;
            rig.RightArm = rArm.Upper;
            rig.RightForeArm = rArm.Fore;
            rig.RightElbow = rArm.Elbow;
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
            // 外部角色是 Instantiate 出来的实例，必须显式释放（内含材质实例）；内置角色随本对象销毁
            ReleaseExternalView();
            if (_switchService != null)
            {
                _switchService.Dispose();
                _switchService = null;
            }

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

            // 下颌 / 下巴过渡：更宽、更扁、略靠前 —— 让正面看得到下颌线，而不是"球体下缘"
            CreatePart(PrimitiveType.Sphere, root, "Jaw",
                new Vector3(0f, headY - headR * 0.72f, headR * 0.13f),
                new Vector3(headR * 1.50f * cheek, headR * 0.80f, headR * 1.62f), skinMat);

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

            // ---- 眼周结构：先铺眼窝暗部，再压上下眼睑 ----
            // 目的：消除「两颗完整球贴在球面上」的观感。眼窝比眼白大一圈且略暗，
            // 在眼白四周形成一圈凹陷阴影；上下眼睑用皮肤色压住眼白边缘，使眼睛呈杏仁形而非正圆。
            var socketMat = CreateMaterial(Color.Lerp(def.SkinColor, Color.black, 0.18f), 0.08f, 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                CreatePart(PrimitiveType.Sphere, root, $"EyeSocket_{side}",
                    new Vector3(side * eyeX, eyeY + headR * 0.01f, headR * 0.78f),
                    new Vector3(headR * 0.46f, headR * 0.34f, headR * 0.16f), socketMat);

                CreatePart(PrimitiveType.Sphere, root, $"UpperLid_{side}",
                    new Vector3(side * eyeX, eyeY + headR * 0.095f, headR * 0.86f),
                    new Vector3(headR * 0.34f, headR * 0.13f, headR * 0.15f), skinMat);

                CreatePart(PrimitiveType.Sphere, root, $"LowerLid_{side}",
                    new Vector3(side * eyeX, eyeY - headR * 0.085f, headR * 0.855f),
                    new Vector3(headR * 0.30f, headR * 0.07f, headR * 0.13f), skinMat);
            }

            // ---- 颧骨：面部中段唯一的横向隆起。缺了它，脸就是一个椭球 ----
            for (int side = -1; side <= 1; side += 2)
            {
                CreatePart(PrimitiveType.Sphere, root, $"Cheekbone_{side}",
                    new Vector3(side * headR * 0.50f * cheek, headY - headR * 0.10f, headR * 0.54f),
                    new Vector3(headR * 0.34f, headR * 0.26f, headR * 0.30f), skinMat);
            }

            // ---- 鼻翼：鼻头两侧的小球，让鼻子有「面」而不是一个光锥 ----
            for (int side = -1; side <= 1; side += 2)
            {
                CreatePart(PrimitiveType.Sphere, root, $"Nostril_{side}",
                    new Vector3(side * headR * 0.105f, headY - headR * 0.225f, headR * 0.90f),
                    new Vector3(headR * 0.105f, headR * 0.085f, headR * 0.10f), skinMat);
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
        /// 手臂：<b>上臂（衣料）+ 前臂（裸露皮肤）+ 肘球</b> 的三段结构，由
        /// <see cref="HandIKController.StretchArm"/> 按两段解析 IK 逐帧摆位（肩 → 肘 → 腕）。
        /// <para>
        /// 改造前是「单段胶囊从肩一直拉到腕」：整条手臂永远是直线，没有肘关节。
        /// 与已经做好三段指节的手放在一起，会出现「精密的手 + 面条一样的手臂」的割裂感。
        /// 改成两段后，肘部由余弦定理求出、朝向由「外下方」hint 决定，
        /// 符合坐姿弹奏时「大臂自然下垂、前臂前伸」的真实姿态。
        /// </para>
        /// <para>
        /// 不生成袖子子物体：子物体会被父级的非均匀缩放（x = 半径、y = 半长）压扁，
        /// 因此直接把上臂本体设为衣料色，视觉上等同「卷起袖子的衬衫」。
        /// </para>
        /// </summary>
        private static (Transform Upper, Transform Fore, Transform Elbow) BuildArm(
            Transform root, string name, bool leftHand, float s,
            Material skinMat, Material clothMat, Material trimMat)
        {
            float side = leftHand ? -1f : 1f;
            float upperR = AvatarBodyMetrics.ArmRadius * 1.10f;   // ≈40 mm，接近成人上臂半径
            float foreR = AvatarBodyMetrics.ArmRadius * 0.90f;    // ≈32 mm，前臂略细
            float upperLen = AvatarBodyMetrics.UpperArmLength;
            float foreLen = AvatarBodyMetrics.LowerArmLength;

            // 初始摆位只是占位；位置 / 朝向 / 长度每帧由 StretchArm 的两段 IK 覆盖。
            var upper = CreatePart(PrimitiveType.Capsule, root, name,
                new Vector3(side * 0.20f * s, 0.86f * s, 0.06f * s),
                new Vector3(upperR * s, upperLen * 0.5f * s, upperR * s), clothMat);

            var fore = CreatePart(PrimitiveType.Capsule, root, name + "Forearm",
                new Vector3(side * 0.21f * s, 0.70f * s, 0.10f * s),
                new Vector3(foreR * s, foreLen * 0.5f * s, foreR * s), skinMat);

            // 肘球：独立节点（不做前臂子物体，避免父级非均匀缩放把球压扁）
            var elbow = CreatePart(PrimitiveType.Sphere, root, name + "Elbow",
                new Vector3(side * 0.205f * s, 0.80f * s, 0.08f * s),
                Vector3.one * (foreR * 2.20f * s), skinMat);

            return (upper, fore, elbow);
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
