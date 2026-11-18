using NUnit.Framework;
using PianoTeacher3D.Characters;
using UnityEngine;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 角色库（对应测试计划 EditMode「角色库加载与去重」）。
    /// 构造时关闭 Resources 扫描，得到确定的 4 个内置档案。
    /// </summary>
    public class CharacterRegistryTests
    {
        private CharacterRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new CharacterRegistry(includeResourceProfiles: false);
        }

        [TearDown]
        public void TearDown()
        {
            _registry?.Dispose();
            _registry = null;
        }

        [Test]
        public void BuiltInCount_IsOne()
        {
            // 2026-11-18：内置角色只剩 genki（Rocketbox 三位写实角色已移除）
            Assert.AreEqual(1, _registry.Count);
            Assert.AreEqual(1, _registry.All.Count);
        }

        [Test]
        public void BuiltInIds_MatchDeclaredOrder()
        {
            Assert.AreEqual(CharacterRegistry.VroidGenkiId, _registry.All[0].CharacterId);
        }

        [Test]
        public void BuiltInProfiles_ArePrefabSourcedVrmAvatars()
        {
            for (int i = 0; i < CharacterRegistry.BuiltInIds.Length; i++)
            {
                Assert.IsTrue(_registry.TryGet(CharacterRegistry.BuiltInIds[i], out var profile));
                Assert.AreEqual(CharacterSource.Prefab, profile.Source);
                Assert.AreEqual(CharacterStyle.Anime, profile.Style);
                Assert.IsTrue(profile.Vrm);
                Assert.AreEqual(-1, profile.ProceduralIndex);
                Assert.IsFalse(string.IsNullOrEmpty(profile.ResourcesPath));
                Assert.IsFalse(string.IsNullOrEmpty(profile.LicenseName));
            }
        }

        [Test]
        public void GenkiProfile_HasResourcePathAndVrmFlag()
        {
            Assert.IsTrue(_registry.TryGet(CharacterRegistry.VroidGenkiId, out var profile));
            Assert.AreEqual(CharacterSource.Prefab, profile.Source);
            Assert.IsNull(profile.Prefab);
            Assert.AreEqual(CharacterRegistry.VroidGenkiResourcesPath, profile.ResourcesPath);
            Assert.IsTrue(profile.Vrm);
            Assert.AreEqual(CharacterStyle.Anime, profile.Style);
        }

        [Test]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(_registry.TryGet("does.not.exist", out var profile));
            Assert.IsNull(profile);
            Assert.IsFalse(_registry.TryGet(null, out _));
            Assert.IsFalse(_registry.TryGet(string.Empty, out _));
        }

        [Test]
        public void IsRegistered_ReflectsContents()
        {
            Assert.IsTrue(_registry.IsRegistered(CharacterRegistry.VroidGenkiId));
            Assert.IsFalse(_registry.IsRegistered("nope"));
        }

        [Test]
        public void Register_DuplicateId_IsRejected()
        {
            var duplicate = CharacterRegistry.CreateBuiltIn(0);   // 与已注册的内置 ID 相同
            try
            {
                var result = _registry.Register(duplicate);
                Assert.IsFalse(result.Ok);
                StringAssert.Contains("已注册", result.Message);
                Assert.AreEqual(1, _registry.Count);   // 只有内置的 genki
            }
            finally
            {
                Object.DestroyImmediate(duplicate);
            }
        }

        [Test]
        public void Register_NewValidProfile_Succeeds()
        {
            var profile = ScriptableObject.CreateInstance<CharacterProfile>();
            profile.CharacterId = "test.extra";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.Prefab;
            profile.ResourcesPath = "Characters/Test/Model";
            profile.Style = CharacterStyle.Anime;
            profile.QualityTier = 2;

            try
            {
                var result = _registry.Register(profile);
                Assert.IsTrue(result.Ok, result.Message);
                Assert.AreEqual(2, _registry.Count);   // genki + 新注册的一个
                Assert.IsTrue(_registry.TryGet("test.extra", out var found));
                Assert.AreSame(profile, found);
            }
            finally
            {
                _registry.Dispose();   // 清理（外部注册的资料不会被 Dispose 销毁，这里手动销毁）
                Object.DestroyImmediate(profile);
                _registry = new CharacterRegistry(includeResourceProfiles: false);
            }
        }

        [Test]
        public void Register_InvalidProfile_IsRejected()
        {
            var profile = ScriptableObject.CreateInstance<CharacterProfile>();
            profile.CharacterId = "BadId";   // 大写开头
            profile.DisplayName = "非法";

            try
            {
                var result = _registry.Register(profile);
                Assert.IsFalse(result.Ok);
                Assert.AreEqual(1, _registry.Count);   // 只有内置的 genki
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Register_NullProfile_IsRejectedWithoutThrowing()
        {
            var result = _registry.Register(null);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void FilterByStyle_Realistic_ReturnsEmpty_AfterVrmSwitch()
        {
            // 2026-11-18：写实角色已全部移除
            Assert.AreEqual(0, _registry.FilterByStyle(CharacterStyle.Realistic).Count);
        }

        [Test]
        public void FilterByStyle_Cartoon_ReturnsEmpty_AfterRealisticUpgrade()
        {
            // 三个内置槽位全部换成真人模型后，卡通风格不再有任何资料
            Assert.AreEqual(0, _registry.FilterByStyle(CharacterStyle.Cartoon).Count);
        }

        [Test]
        public void FilterByStyle_Anime_ReturnsGenki()
        {
            var list = _registry.FilterByStyle(CharacterStyle.Anime);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(CharacterRegistry.VroidGenkiId, list[0].CharacterId);
        }

        [Test]
        public void FilterByQualityTier_Zero_ReturnsEmpty()
        {
            Assert.AreEqual(0, _registry.FilterByQualityTier(0).Count);
        }

        [Test]
        public void FilterByQualityTier_One_ReturnsGenki()
        {
            // genki 是标准档角色（QualityTier 1），低配设备也能用
            Assert.AreEqual(1, _registry.FilterByQualityTier(1).Count);
        }

        [Test]
        public void FilterByQualityTier_Two_ReturnsAll()
        {
            Assert.AreEqual(1, _registry.FilterByQualityTier(2).Count);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var registry = new CharacterRegistry(includeResourceProfiles: false);
            registry.Dispose();
            Assert.DoesNotThrow(() => registry.Dispose());
        }

        [Test]
        public void BuiltInProfiles_HaveDistinctIds()
        {
            var ids = new System.Collections.Generic.HashSet<string>(CharacterRegistry.BuiltInIds);
            Assert.AreEqual(CharacterRegistry.BuiltInIds.Length, ids.Count);
            Assert.AreEqual(CharacterRegistry.BuiltInIds.Length, CharacterRegistry.BuiltInDisplayNames.Length);
        }
    }
}
