using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>网格精度档（方案 5.2 中期「以统一的 AvatarDefinition / 配置数据描述模型、材质、缩放和 IK 偏移」）。</summary>
    public enum AvatarMeshDetail
    {
        /// <summary>低精度：省略指尖球与部分过渡体块（面向低端设备降级）。</summary>
        Low = 0,

        /// <summary>标准精度：完整手掌过渡 + 三段手指 + 圆指尖（默认）。</summary>
        Medium = 1,

        /// <summary>高精度：额外的关节/指甲细节（默认开启，成本可控）。</summary>
        High = 2
    }

    /// <summary>
    /// 角色质量配置（骨架上手指段数、网格精度档）。
    /// 属于「质量档位」的一部分：低端设备可只降低这类细节，而不是去掉琴键高亮等教学要素（方案 5.4 / 7.1）。
    /// </summary>
    public sealed class AvatarQualityConfig
    {
        /// <summary>骨骼/可见网格的手指段数（近端/中段/末端 = 3）。</summary>
        public int FingerSegments = 3;

        /// <summary>网格精度档。</summary>
        public AvatarMeshDetail MeshDetail = AvatarMeshDetail.Medium;
    }

    /// <summary>
    /// 统一角色尺寸表（方案 5.2 短期「建立统一的角色尺寸表」/ 审查 1.1 方案 A「比例一致性」）。
    /// <para>
    /// 以基准身高 <see cref="ReferenceHeight"/> 为单位定义全身比例，实际尺寸 = 常量 × 体型缩放 <c>s</c>。
    /// 手腕可达范围由 <c>HandIKController</c> 的同一套 <c>HandPoseLimits</c> 约束，
    /// 因此三个角色外观可以不同，但手腕/指尖必须落在同一套键盘标定范围内。
    /// </para>
    /// <para>
    /// 手掌与手指尺寸以 <see cref="WhiteKeyWidth"/>（真实白键宽 24 mm）为标尺，而不是凭屏幕比例调手
    /// （审查 1.2A「优先保证手掌宽度、手指长度和琴键尺寸协调」）。
    /// </para>
    /// </summary>
    public static class AvatarBodyMetrics
    {
        // ---- 全身（基准身高 1.45 m 的坐姿人物）----
        public const float ReferenceHeight = 1.45f;
        public const float SittingHipHeight = 0.52f;   // 坐姿髋高
        public const float HipWidth = 0.31f;
        public const float WaistWidth = 0.27f;
        public const float ChestWidth = 0.34f;
        public const float ShoulderHalfWidth = 0.185f;
        public const float ShoulderHeight = 0.95f;
        public const float NeckHeight = 1.02f;
        public const float HeadCenterHeight = 1.14f;
        public const float HeadRadius = 0.105f;
        public const float UpperArmLength = 0.21f;
        public const float LowerArmLength = 0.20f;
        public const float ArmRadius = 0.048f;
        public const float StoolTopHeight = 0.445f;    // 琴凳面高度（须与坐姿臀部接触，不悬空）

        // ---- 手部（以琴键为标尺）----
        public const float WhiteKeyWidth = 0.024f;     // 真实白键宽（与 PianoKeyMap.WhiteWidth 一致）
        public const float PalmWidth = 0.076f;         // ≈ 3.2 个白键宽
        public const float PalmThickness = 0.030f;
        public const float PalmLength = 0.058f;        // 腕横纹 → 掌指关节
        public const float WristRadius = 0.026f;

        /// <summary>拇指轴与其余四指轴的目标夹角（度）—— 决定虎口是否可见。</summary>
        public const float ThumbSpreadDegrees = 57f;

        /// <summary>各指（0=拇指 … 4=小指）的总长度：中指最长、小指最短，食指/无名指不等长。</summary>
        public static readonly float[] FingerLengths = { 0.068f, 0.082f, 0.090f, 0.084f, 0.064f };

        /// <summary>各指根部半径（不等粗，符合真实手型）。</summary>
        public static readonly float[] FingerRadii = { 0.0115f, 0.0096f, 0.0099f, 0.0091f, 0.0080f };

        /// <summary>近端 / 中段 / 末端的长度占比（三段之和为 1）。</summary>
        public static readonly float[] FingerSegmentRatios = { 0.42f, 0.33f, 0.25f };

        /// <summary>掌指关节（手指根节点）局部位置，未镜像（side = +1 的左/右手基准，右手按 x 取反）。</summary>
        public static readonly Vector3[] KnucklePositions =
        {
            new Vector3(0.030f, -0.008f, 0.026f), // 拇指：靠近腕部、偏向外侧，形成虎口
            new Vector3(0.018f, 0.002f, 0.056f),
            new Vector3(0.004f, 0.003f, 0.058f),
            new Vector3(-0.010f, 0.001f, 0.056f),
            new Vector3(-0.023f, -0.005f, 0.048f)
        };

        /// <summary>各指链相对掌指关节的解剖朝向偏航（度）：拇指约 57°，与四指形成 45°–60° 夹角。</summary>
        public static readonly float[] FingerChainYaw =
            { ThumbSpreadDegrees, 6f, 1f, -5f, -12f };
    }

    /// <summary>
    /// 角色资源定义（方案 5.2 中期 / 9.1「AvatarDefinition：模型、骨骼、材质、IK 偏移和质量配置」/ 9.2「配置与内容分离」）。
    /// <para>
    /// <b>纯数据类，零资产依赖</b>：不使用 ScriptableObject 资产文件，因此仓库保持轻量、可在测试里直接 new。
    /// 具体模型路径/材质不写死在核心控制器里：<c>AvatarManager</c> 只按本定义构建，未来接入外部 GLB 时
    /// 只需把外部网格挂到同一套 Humanoid 骨架上并填好本定义。
    /// </para>
    /// <para>
    /// <b>资源许可证登记（方案 5.2 中期 / 方案 2.1）</b>：
    /// 当前全部为程序化生成（PrimaryType 组合），<b>无第三方模型 / 贴图 / 动画 / 权重</b>；
    /// 材质使用 Unity 内置 Standard / URP Lit（随 Unity 许可）。
    /// 若后续接入外部模型，必须把「模型、贴图、动画、模型权重」<b>分别</b>登记到 <c>Docs/asset-licenses.md</c>，
    /// 不能把代码许可证（MIT/Apache-2.0）当成资产许可证。
    /// </para>
    /// </summary>
    public sealed class AvatarDefinition
    {
        /// <summary>模型标识（稳定 ID，不随显示名变化）。</summary>
        public string Id;

        /// <summary>显示名（UI 展示，与 <c>AvatarSelectionUI.AvatarNames</c> 顺序一致）。</summary>
        public string DisplayName;

        /// <summary>Humanoid Avatar 引用：构建后由 <c>AvatarManager</c> 回填，用于导入校验与 Animator 绑定。</summary>
        public UnityEngine.Avatar Avatar;

        // ---- 材质（皮肤 / 衣物 / 头发 / 深色配件）----
        public Color SkinColor = new Color(0.96f, 0.85f, 0.74f);
        public Color ClothColor = new Color(0.3f, 0.5f, 0.9f);
        public Color HairColor = new Color(0.155f, 0.125f, 0.105f);
        public Color DarkColor = new Color(0.16f, 0.16f, 0.19f);

        // ---- 体型与外观 ----
        /// <summary>坐姿身高（米），由它推导体型缩放。</summary>
        public float BodyHeight = AvatarBodyMetrics.ReferenceHeight;

        /// <summary>体型缩放（相对基准人；<c>AvatarManager</c> 由 <see cref="BodyHeight"/> 推导）。</summary>
        public float BodyScale = 1f;

        /// <summary>头部比例系数（卡通角色 &gt; 1）。</summary>
        public float HeadScale = 1f;

        /// <summary>0 长发 / 1 短发 / 2 卡通呆毛。</summary>
        public int HairStyle;

        /// <summary>是否为卡通风格（影响五官与服装细节的夸张程度）。</summary>
        public bool Cartoon;

        // ---- 每角色 IK 偏移（方案 5.2 中期「每角色 IK 偏移」）----
        /// <summary>手腕高度偏移（米）：用于补偿不同角色手掌厚度差异，使指尖仍落在同一键面。</summary>
        public float HandHeightOffset;

        /// <summary>手腕深度偏移（米）：用于补偿不同角色手臂长度差异。</summary>
        public float HandDepthOffset;

        /// <summary>质量配置（骨架上手指段数、网格精度档）。</summary>
        public readonly AvatarQualityConfig Quality = new AvatarQualityConfig();

        /// <summary>转为兼容用的 <see cref="AvatarPreset"/>（旧 API 保留，方案 9.1「不一次性重构」）。</summary>
        public AvatarPreset ToPreset()
        {
            return new AvatarPreset
            {
                Name = DisplayName,
                Height = BodyHeight,
                HeadScale = HeadScale,
                SkinColor = SkinColor,
                ClothColor = ClothColor,
                HairStyle = HairStyle,
                Cartoon = Cartoon
            };
        }

        /// <summary>默认安全角色：任何自定义定义校验失败时回退到它（方案 5.2「校验失败不得影响曲目播放」）。</summary>
        public static AvatarDefinition CreateDefault()
        {
            var def = new AvatarDefinition
            {
                Id = "realistic_female",
                DisplayName = "写实女性",
                BodyHeight = 1.38f,
                HeadScale = 0.94f,
                SkinColor = new Color(0.97f, 0.86f, 0.78f),
                ClothColor = new Color(0.62f, 0.27f, 0.33f),
                HairColor = new Color(0.155f, 0.125f, 0.105f),
                HairStyle = 0,
                Cartoon = false
            };
            def.BodyScale = def.BodyHeight / AvatarBodyMetrics.ReferenceHeight;
            return def;
        }

        // ==================== 导入校验（方案 5.2 中期 / 审查 1.1 方案 B 第 3 步） ====================

        /// <summary>
        /// 校验骨架与定义是否可用。返回 null 表示通过；否则返回中文问题描述（供上层上报 Warning）。
        /// <para>纯静态、不依赖场景，可单测。检查项：Humanoid Avatar 有效、必需骨骼存在、比例在允许范围、手部目标可达。</para>
        /// </summary>
        public static string Validate(AvatarRigBuilder.Skeleton skeleton, AvatarDefinition definition)
        {
            if (definition == null) return "角色定义为 null";

            if (skeleton == null) return "骨架未构建（AvatarRigBuilder.Build 返回 null）";
            if (skeleton.Avatar == null) return "Humanoid Avatar 未生成";
            if (!skeleton.Avatar.isValid) return "Humanoid Avatar 无效（avatar.isValid = false）";
            if (!skeleton.Avatar.isHuman) return "骨架未被识别为 Humanoid（avatar.isHuman = false）";

            string missing = MissingRequiredBone(skeleton);
            if (missing != null) return $"缺少必需骨骼：{missing}";

            // 比例在允许范围：坐姿髋 → 头的高度跨度应落在合理区间
            float spineSpan = Vector3.Distance(skeleton.Hips.position, skeleton.Head.position);
            float expected = AvatarBodyMetrics.ReferenceHeight * Mathf.Clamp(definition.BodyScale, 0.5f, 1.6f) * 0.45f;
            if (spineSpan < expected * 0.55f || spineSpan > expected * 1.9f)
                return $"人体比例超出允许范围（髋-头跨度 {spineSpan:F3} m，期望约 {expected:F3} m）";

            // 手部目标可达：肩 → 手骨的直线距离应在手臂总长的合理范围内（含肩带段）
            float reach = Vector3.Distance(skeleton.LeftShoulder.position, skeleton.LeftHand.position);
            float armLimit = (AvatarBodyMetrics.UpperArmLength + AvatarBodyMetrics.LowerArmLength) * definition.BodyScale;
            if (reach < armLimit * 0.7f || reach > armLimit * 1.6f)
                return $"左手目标不可达（肩-手距离 {reach:F3} m，手臂总长约 {armLimit:F3} m）";

            return null;
        }

        private static string MissingRequiredBone(AvatarRigBuilder.Skeleton s)
        {
            if (s.Hips == null) return "Hips";
            if (s.Spine == null) return "Spine";
            if (s.Chest == null) return "Chest";
            if (s.UpperChest == null) return "UpperChest";
            if (s.Neck == null) return "Neck";
            if (s.Head == null) return "Head";
            if (s.LeftShoulder == null) return "LeftShoulder";
            if (s.LeftUpperArm == null) return "LeftUpperArm";
            if (s.LeftLowerArm == null) return "LeftLowerArm";
            if (s.LeftHand == null) return "LeftHand";
            if (s.RightShoulder == null) return "RightShoulder";
            if (s.RightUpperArm == null) return "RightUpperArm";
            if (s.RightLowerArm == null) return "RightLowerArm";
            if (s.RightHand == null) return "RightHand";
            return null;
        }
    }
}
