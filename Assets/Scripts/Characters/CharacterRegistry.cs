using System;
using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 角色库（方案 7.1）：登记所有可切换角色，并提供按 ID / 风格 / 质量级别的查询。
    /// <para>
    /// <b>三点约定</b>：
    /// ① 内置角色（2026-11-18 起只有 VRM 角色 genki）由<b>代码构造</b>，不依赖任何场景对象，
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

        /// <summary>默认演奏者 genki（VRoid AvatarSample_A，VRM）的 ID。</summary>
        public const string VroidGenkiId = "vroid.genki";

        /// <summary>genki 的 Resources 路径（UniVRM 导入器生成的预制体，不含扩展名）。</summary>
        public const string VroidGenkiResourcesPath = "Characters/VRoid/Genki";

        /// <summary>
        /// 人像缩略图的 Resources 路径（不含扩展名）。2026-11-18 起唯一的演奏者 genki 没有单独的人像图，
        /// 返回空串，界面显示文字卡片。
        /// </summary>
        public static string PortraitResourcesPath(string profileId) => string.Empty;

        /// <summary>内置角色的 ID 表（2026-11-18 起只有 genki；三个 Rocketbox 写实角色与照片角色已移除）。</summary>
        public static readonly string[] BuiltInIds = { VroidGenkiId };

        /// <summary>内置角色的显示名（索引与 <see cref="BuiltInIds"/> 一致）。</summary>
        public static readonly string[] BuiltInDisplayNames = { "Genki" };

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

        /// <summary>全部已登记角色（顺序：内置 → 外部资料）。</summary>
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
            return CreateVroidGenkiProfile();
        }

        /// <summary>
        /// 创建 genki 的资料（每次调用都是新实例，调用方负责销毁）。
        /// <para>
        /// 模型：VRoid Studio 1.26 导出的官方样例 AvatarSample_A（VRM 0.x，导入时迁移为 VRM 1.0）。
        /// 授权：VRoid Hub 条款——任何人可用、可商用（个人 / 法人）、可修改、可再分发，无需署名。
        /// RootScale 1.08：样例身高约 1.5 m，放大到约 1.62 m，肩高与臂长才够得着标准键盘。
        /// </para>
        /// </summary>
        public static CharacterProfile CreateVroidGenkiProfile()
        {
            var profile = ScriptableObject.CreateInstance<CharacterProfile>();
            profile.name = VroidGenkiId;
            profile.CharacterId = VroidGenkiId;
            profile.DisplayName = "Genki";
            profile.Style = CharacterStyle.Anime;
            profile.Source = CharacterSource.Prefab;
            profile.Vrm = true;
            profile.Prefab = null;
            profile.ResourcesPath = VroidGenkiResourcesPath;
            profile.ProceduralIndex = -1;
            profile.SupportedAppearanceSlots = new[] { AppearanceSlot.Thumbnail };
            profile.RootScale = 1.08f;
            profile.RootYawDegrees = 0f;
            profile.SeatHeightOffset = 0f;
            profile.QualityTier = 1;
            profile.EstimatedMemoryMb = 36;
            profile.LicenseName = "VRoid Hub 条款（可商用 / 可修改 / 可再分发 / 无需署名）";
            profile.LicenseUrl = "https://hub.vroid.com/license?allowed_to_use_user=everyone&characterization_allowed_user=everyone&corporate_commercial_use=allow&credit=unnecessary&modification=allow&personal_commercial_use=profit&redistribution=allow&sexual_expression=allow&version=1&violent_expression=allow";
            profile.SourceUrl = "https://vroid.com/en/studio";
            profile.AssetVersion = "AvatarSample_A 1.0（VRoid Studio 1.26.0，UniVRM 0.131.4 迁移导入）";
            profile.Notes = "VRM 0.x 由 UniVRM VRM-1.0 导入器迁移为 1.0，材质为 URP MToon10；" +
                            "指尖参考点用末节蒙皮顶点标定，头发 / 兜帽绳为 SpringBone。";
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
                DestroyProfile(profile);
            }
            _selfCreated.Clear();
            _all.Clear();
            _byId.Clear();
        }

        private static void DestroyProfile(CharacterProfile profile)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(profile);
            else UnityEngine.Object.DestroyImmediate(profile);
        }
    }
}
