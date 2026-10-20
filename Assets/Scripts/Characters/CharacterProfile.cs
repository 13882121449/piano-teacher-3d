using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 角色资料（方案 7.1）。一个 asset 描述一个可进入角色库的角色：
    /// 稳定 ID、风格、来源、预制体、支持的外观槽位、许可与版本、质量级别。
    ///
    /// 放置位置约定：<c>Assets/Characters/Resources/Characters/*.asset</c>，
    /// 由 <see cref="CharacterRegistry"/> 通过 <c>Resources.LoadAll</c> 发现；
    /// 被它引用的预制体即使不在 Resources 目录下也会随构建打包。
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterProfile", menuName = "PianoTeacher3D/角色资料", order = 10)]
    public class CharacterProfile : ScriptableObject
    {
        [Header("身份")]
        [Tooltip("稳定唯一 ID，小写字母/数字开头，允许 a-z 0-9 . _ -")]
        public string CharacterId = "new.character";

        [Tooltip("界面上显示的名称（可改，不参与逻辑判断）")]
        public string DisplayName = "新角色";

        [Tooltip("美术风格，用于角色库标签筛选")]
        public CharacterStyle Style = CharacterStyle.Anime;

        [Tooltip("资源来源：程序化内置 / 外部预制体")]
        public CharacterSource Source = CharacterSource.Prefab;

        [Header("资源引用")]
        [Tooltip("Source=Prefab 时必填。预制体根节点应朝向 -Z（面向钢琴），缩放 1")]
        public GameObject Prefab;

        [Tooltip("Source=ProceduralBuiltIn 时必填，对应 AvatarManager 内置索引 0..2")]
        public int ProceduralIndex;

        [Tooltip("Source=Prefab 且没有直接引用预制体时的 Resources 路径（不含扩展名），例如 Characters/Rocketbox/Female_Adult_01")]
        public string ResourcesPath;

        [Tooltip("角色库卡片缩略图；为空时 UI 用占位图")]
        public Texture2D Thumbnail;

        [Header("外观槽位（方案 9.5）")]
        [Tooltip("允许被照片生成结果替换的槽位；未声明的槽位不会在 UI 上出现「应用」按钮")]
        public AppearanceSlot[] SupportedAppearanceSlots = { AppearanceSlot.Thumbnail };

        [Header("适配参数（方案 8.2）")]
        [Tooltip("导入模型统一缩放；1 表示不缩放")]
        [Range(0.1f, 10f)] public float RootScale = 1f;

        [Tooltip("导入模型朝向修正（度）。模型正面朝 +Z 时填 180")]
        public float RootYawDegrees;

        [Tooltip("坐姿高度偏移（米）。用于把模型臀部对齐到琴凳")]
        public float SeatHeightOffset;

        [Header("质量与许可")]
        [Tooltip("质量级别：0=低 1=标准 2=高。低配设备可跳过 2 级角色")]
        [Range(0, 2)] public int QualityTier = 1;

        [Tooltip("预估常驻内存（MB），用于角色库显示与预算核算")]
        public int EstimatedMemoryMb;

        [Tooltip("许可名称，例如 MIT / CC0 / 商业授权编号")]
        public string LicenseName = "MIT";

        [Tooltip("许可原文地址")]
        public string LicenseUrl = "https://github.com/microsoft/Microsoft-Rocketbox";

        [Tooltip("资产来源地址（下载页/商店页），用于可追溯")]
        public string SourceUrl;

        [Tooltip("资产版本或下载日期，便于比对升级")]
        public string AssetVersion;

        [TextArea(2, 5)]
        [Tooltip("备注：已知限制、适配说明、需人工核对项")]
        public string Notes;

        /// <summary>是否声明支持某个外观槽位。</summary>
        public bool SupportsSlot(AppearanceSlot slot)
        {
            if (SupportedAppearanceSlots == null) return false;
            for (int i = 0; i < SupportedAppearanceSlots.Length; i++)
            {
                if (SupportedAppearanceSlots[i] == slot) return true;
            }
            return false;
        }

        /// <summary>
        /// 校验资料完整性与一致性（方案 7.1 / 测试计划 EditMode「CharacterProfile 必填字段和资源引用验证」）。
        /// 纯数据校验，不访问场景、不抛异常。
        /// </summary>
        public CharacterValidation Validate()
        {
            var result = CharacterIdRules.Validate(CharacterId);
            if (!result.Ok) return result;

            if (string.IsNullOrWhiteSpace(DisplayName))
                return CharacterValidation.Fail($"角色 {CharacterId} 缺少显示名称。");

            if (Source == CharacterSource.Prefab && Prefab == null && string.IsNullOrWhiteSpace(ResourcesPath))
                return CharacterValidation.Fail($"角色 {CharacterId} 声明为预制体来源，但既未指定预制体，也没有 Resources 路径。");

            if (Source == CharacterSource.ProceduralBuiltIn && ProceduralIndex < 0)
                return CharacterValidation.Fail($"角色 {CharacterId} 的内置索引不能为负。");

            if (string.IsNullOrWhiteSpace(LicenseName))
                return CharacterValidation.Fail($"角色 {CharacterId} 缺少许可信息（方案 18：许可与来源必须可追溯）。");

            if (QualityTier < 0 || QualityTier > 2)
                return CharacterValidation.Fail($"角色 {CharacterId} 的质量级别必须在 0–2 之间。");

            if (RootScale <= 0f)
                return CharacterValidation.Fail($"角色 {CharacterId} 的缩放必须为正数。");

            if (SupportedAppearanceSlots != null)
            {
                for (int i = 0; i < SupportedAppearanceSlots.Length; i++)
                {
                    for (int j = i + 1; j < SupportedAppearanceSlots.Length; j++)
                    {
                        if (SupportedAppearanceSlots[i] == SupportedAppearanceSlots[j])
                            return CharacterValidation.Fail(
                                $"角色 {CharacterId} 的外观槽位 {SupportedAppearanceSlots[i]} 重复声明。");
                    }
                }
            }

            return CharacterValidation.Pass;
        }
    }
}
