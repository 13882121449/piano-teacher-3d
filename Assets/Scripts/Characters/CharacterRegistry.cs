using System;
using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 角色库（方案 7.1）：登记所有可切换角色，并提供按 ID / 风格 / 质量级别的查询。
    /// <para>
    /// <b>三点约定</b>：
    /// ① 内置 3 个程序化角色与 1 个 Rocketbox 导入角色由<b>代码构造</b>，不依赖任何场景对象，
    ///    因此 EditMode 测试可以直接 <c>new CharacterRegistry()</c>；
    /// ② 外部角色资料通过 <c>Resources.LoadAll&lt;CharacterProfile&gt;("Characters")</c> 合并，
    ///    目录不存在时返回空数组（Unity 不报错，本类也不视为错误）；
    /// ③ 角色 ID 唯一且大小写敏感，重复注册会被拒绝并给出原因。
    /// </para>
    /// </summary>
    public sealed class CharacterRegistry : IDisposable
    {
        /// <summary>Resources 下的角色资料目录（相对 <c>Resources/</c>）。</summary>
        public const string ResourceProfilesFolder = "Characters";

        /// <summary>内置写实女性 ID。</summary>
        public const string BuiltInRealisticFemaleId = "builtin.realistic.female";

        /// <summary>内置写实男性 ID。</summary>
        public const string BuiltInRealisticMaleId = "builtin.realistic.male";

        /// <summary>内置卡通角色 ID。</summary>
        public const string BuiltInCartoonId = "builtin.cartoon";

        /// <summary>内置 Rocketbox 导入角色 ID。</summary>
        public const string RocketboxFemaleId = "rocketbox.female01";

        /// <summary>Rocketbox 预制体的 Resources 路径（不含扩展名）。</summary>
        public const string RocketboxResourcesPath = "Characters/Rocketbox/Female_Adult_01";

        /// <summary>内置角色的 ID 表（索引与 <c>AvatarManager</c> 内置顺序一致）。</summary>
        public static readonly string[] BuiltInIds =
        {
            BuiltInRealisticFemaleId,
            BuiltInRealisticMaleId,
            BuiltInCartoonId
        };

        /// <summary>内置角色的显示名（索引与 <see cref="BuiltInIds"/> 一致）。</summary>
        public static readonly string[] BuiltInDisplayNames = { "写实女性", "写实男性", "卡通角色" };

        private readonly List<CharacterProfile> _all = new List<CharacterProfile>();
        private readonly Dictionary<string, CharacterProfile> _byId =
            new Dictionary<string, CharacterProfile>(StringComparer.Ordinal);

        /// <summary>由本类 <c>CreateInstance</c> 创建的资料（释放时销毁；Resources 资产不在此列）。</summary>
        private readonly List<CharacterProfile> _selfCreated = new List<CharacterProfile>();

        private bool _disposed;

        /// <summary>
        /// 构造角色库。
        /// </summary>
        /// <param name="includeResourceProfiles">
        /// 是否合并 <c>Resources/Characters</c> 下的外部资料。测试里传 false 可得到确定数量的内置集合。
        /// </param>
        public CharacterRegistry(bool includeResourceProfiles = true)
        {
            AddBuiltIns();

            if (includeResourceProfiles) LoadResourceProfiles();
        }

        /// <summary>全部已登记角色（顺序：内置 3 个 → Rocketbox → 外部资料）。</summary>
        public IReadOnlyList<CharacterProfile> All => _all;

        /// <summary>已登记数量。</summary>
        public int Count => _all.Count;

        /// <summary>按 ID 查找。<paramref name="id"/> 为空或未注册时返回 false。</summary>
        public bool TryGet(string id, out CharacterProfile profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(id)) return false;
            return _byId.TryGetValue(id, out profile);
        }

        /// <summary>ID 是否已注册。</summary>
        public bool IsRegistered(string id) => !string.IsNullOrEmpty(id) && _byId.ContainsKey(id);

        /// <summary>
        /// 注册一个角色资料。失败（ID 非法 / 资料不完整 / ID 重复）时返回原因，且不改变现有集合。
        /// </summary>
        public CharacterValidation Register(CharacterProfile profile)
        {
            if (profile == null) return CharacterValidation.Fail("角色资料为空。");

            var valid = CharacterAssetValidator.ValidateProfile(profile);
            if (!valid.Ok) return valid;

            if (_byId.ContainsKey(profile.CharacterId))
                return CharacterValidation.Fail($"角色 ID「{profile.CharacterId}」已注册，不能重复。");

            _all.Add(profile);
            _byId.Add(profile.CharacterId, profile);
            return CharacterValidation.Pass;
        }

        /// <summary>按风格筛选（返回新列表，顺序与 <see cref="All"/> 一致）。</summary>
        public IReadOnlyList<CharacterProfile> FilterByStyle(CharacterStyle style)
        {
            var result = new List<CharacterProfile>();
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].Style == style) result.Add(_all[i]);
            }
            return result;
        }

        /// <summary>
        /// 按质量级别筛选：返回 <c>QualityTier &lt;= maxTier</c> 的角色（低配设备传 1 可跳过 2 级角色）。
        /// </summary>
        public IReadOnlyList<CharacterProfile> FilterByQualityTier(int maxTier)
        {
            var result = new List<CharacterProfile>();
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].QualityTier <= maxTier) result.Add(_all[i]);
            }
            return result;
        }

        /// <summary>
        /// 按内置索引创建资料（每次调用都是新实例，调用方负责销毁）。
        /// 索引越界时返回 null。UI 与 <c>AvatarManager</c> 共用本方法以保证 ID 一致。
        /// </summary>
        public static CharacterProfile CreateBuiltIn(int index)
        {
            if (index < 0 || index >= BuiltInIds.Length) return null;

            int tier = index == 2 ? 0 : 1;
            int memory = index == 0 ? 9 : (index == 1 ? 10 : 7);

            var profile = ScriptableObject.CreateInstance<CharacterProfile>();
            profile.name = BuiltInIds[index];
            profile.CharacterId = BuiltInIds[index];
            profile.DisplayName = BuiltInDisplayNames[index];
            profile.Style = index == 2 ? CharacterStyle.Cartoon : CharacterStyle.Realistic;
            profile.Source = CharacterSource.ProceduralBuiltIn;
            profile.ProceduralIndex = index;
            profile.Prefab = null;
            profile.ResourcesPath = string.Empty;
            profile.SupportedAppearanceSlots = new[] { AppearanceSlot.Thumbnail, AppearanceSlot.BodyTint };
            profile.RootScale = 1f;
            profile.RootYawDegrees = 0f;
            profile.SeatHeightOffset = 0f;
            profile.QualityTier = tier;
            profile.EstimatedMemoryMb = memory;
            profile.LicenseName = "MIT";
            profile.LicenseUrl = string.Empty;
            profile.SourceUrl = string.Empty;
            profile.AssetVersion = "procedural-1";
            profile.Notes = "由项目自有代码程序化生成的几何与材质（Unity 内置图元 + 自写放样网格），无第三方模型资产。";
            return profile;
        }

        /// <summary>创建 Rocketbox 导入角色的资料（每次调用都是新实例，调用方负责销毁）。</summary>
        public static CharacterProfile CreateRocketbox()
        {
            var profile = ScriptableObject.CreateInstance<CharacterProfile>();
            profile.name = RocketboxFemaleId;
            profile.CharacterId = RocketboxFemaleId;
            profile.DisplayName = "Rocketbox 女性";
            profile.Style = CharacterStyle.Realistic;
            profile.Source = CharacterSource.Prefab;
            profile.Prefab = null;                      // 走 ResourcesPath，避免资料资产反向引用预制体
            profile.ResourcesPath = RocketboxResourcesPath;
            profile.ProceduralIndex = -1;
            profile.SupportedAppearanceSlots = new[] { AppearanceSlot.Thumbnail, AppearanceSlot.BodyTint };
            profile.RootScale = 1f;
            profile.RootYawDegrees = 0f;
            profile.SeatHeightOffset = 0f;
            profile.QualityTier = 2;
            profile.EstimatedMemoryMb = 42;
            profile.LicenseName = "MIT";
            profile.LicenseUrl = "https://github.com/microsoft/Microsoft-Rocketbox";
            profile.SourceUrl = "https://github.com/microsoft/Microsoft-Rocketbox";
            profile.AssetVersion = "Female_Adult_01（FBX，Humanoid 导入）";
            profile.Notes = "站位 T-Pose 绑定，需坐姿适配：SittingPoseApplier 的默认角度来自人体测量估算，" +
                            "尚未在 Unity 内标定，需人工核对髋/膝/踝与肩/肘角度。手部骨骼为 Biped 命名，" +
                            "指尖位置由绑定姿态推导，不满足内置角色的 +Z/+Y/+X 局部基约定。";
            return profile;
        }

        private void AddBuiltIns()
        {
            for (int i = 0; i < BuiltInIds.Length; i++)
            {
                var profile = CreateBuiltIn(i);
                if (profile == null) continue;
                _selfCreated.Add(profile);
                _all.Add(profile);
                _byId[profile.CharacterId] = profile;
            }

            var rocketbox = CreateRocketbox();
            _selfCreated.Add(rocketbox);
            _all.Add(rocketbox);
            _byId[rocketbox.CharacterId] = rocketbox;
        }

        private void LoadResourceProfiles()
        {
            CharacterProfile[] loaded;
            try
            {
                loaded = Resources.LoadAll<CharacterProfile>(ResourceProfilesFolder);
            }
            catch (Exception)
            {
                // 目录不存在 / 资源损坏时不让角色库整体失败（方案 6.4：不允许连锁崩溃）
                return;
            }
            if (loaded == null || loaded.Length == 0) return;

            for (int i = 0; i < loaded.Length; i++)
            {
                var profile = loaded[i];
                if (profile == null) continue;
                // 外部资料与内置同 ID 时保留内置（内置经过代码校验，行为可预期）
                if (_byId.ContainsKey(profile.CharacterId)) continue;
                if (!CharacterAssetValidator.ValidateProfile(profile).Ok) continue;

                _all.Add(profile);
                _byId.Add(profile.CharacterId, profile);
            }
        }

        /// <summary>销毁由本类创建的临时资料（Resources 资产不会被销毁）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = 0; i < _selfCreated.Count; i++)
            {
                var profile = _selfCreated[i];
                if (profile == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(profile);
                else UnityEngine.Object.DestroyImmediate(profile);
            }
            _selfCreated.Clear();
            _all.Clear();
            _byId.Clear();
        }
    }
}
