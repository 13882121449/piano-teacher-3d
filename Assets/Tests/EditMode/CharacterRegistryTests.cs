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
        public void BuiltInCount_IsFour()
        {
            Assert.AreEqual(4, _registry.Count);
            Assert.AreEqual(4, _registry.All.Count);
        }

        [Test]
        public void BuiltInIds_MatchDeclaredOrder()
        {
            Assert.AreEqual(CharacterRegistry.BuiltInRealisticFemaleId, _registry.All[0].CharacterId);
            Assert.AreEqual(CharacterRegistry.BuiltInRealisticMaleId, _registry.All[1].CharacterId);
            Assert.AreEqual(CharacterRegistry.BuiltInCartoonId, _registry.All[2].CharacterId);
            Assert.AreEqual(CharacterRegistry.RocketboxFemaleId, _registry.All[3].CharacterId);
        }

        [Test]
        public void BuiltInProceduralIndexes_AreZeroOneTwo()
        {
            for (int i = 0; i < CharacterRegistry.BuiltInIds.Length; i++)
            {
                Assert.IsTrue(_registry.TryGet(CharacterRegistry.BuiltInIds[i], out var profile));
                Assert.AreEqual(CharacterSource.ProceduralBuiltIn, profile.Source);
                Assert.AreEqual(i, profile.ProceduralIndex);
                Assert.AreEqual("MIT", profile.LicenseName);
            }
        }

        [Test]
        public void RocketboxProfile_HasResourcePathAndHighQualityTier()
        {
            Assert.IsTrue(_registry.TryGet(CharacterRegistry.RocketboxFemaleId, out var profile));
            Assert.AreEqual(CharacterSource.Prefab, profile.Source);
            Assert.IsNull(profile.Prefab);
            Assert.AreEqual(CharacterRegistry.RocketboxResourcesPath, profile.ResourcesPath);
            Assert.AreEqual(2, profile.QualityTier);
            Assert.AreEqual(CharacterStyle.Realistic, profile.Style);
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
            Assert.IsTrue(_registry.IsRegistered(CharacterRegistry.BuiltInCartoonId));
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
                Assert.AreEqual(4, _registry.Count);
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
                Assert.AreEqual(5, _registry.Count);
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
                Assert.AreEqual(4, _registry.Count);
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
        public void FilterByStyle_Realistic_ReturnsThree()
        {
            var list = _registry.FilterByStyle(CharacterStyle.Realistic);
            Assert.AreEqual(3, list.Count);   // 写实女性 + 写实男性 + Rocketbox
        }

        [Test]
        public void FilterByStyle_Cartoon_ReturnsOne()
        {
            var list = _registry.FilterByStyle(CharacterStyle.Cartoon);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(CharacterRegistry.BuiltInCartoonId, list[0].CharacterId);
        }

        [Test]
        public void FilterByStyle_Anime_ReturnsEmpty()
        {
            Assert.AreEqual(0, _registry.FilterByStyle(CharacterStyle.Anime).Count);
        }

        [Test]
        public void FilterByQualityTier_Zero_KeepOnlyCartoon()
        {
            var list = _registry.FilterByQualityTier(0);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(CharacterRegistry.BuiltInCartoonId, list[0].CharacterId);
        }

        [Test]
        public void FilterByQualityTier_One_ExcludesRocketbox()
        {
            Assert.AreEqual(3, _registry.FilterByQualityTier(1).Count);
        }

        [Test]
        public void FilterByQualityTier_Two_ReturnsAll()
        {
            Assert.AreEqual(4, _registry.FilterByQualityTier(2).Count);
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
            Assert.AreNotEqual(CharacterRegistry.BuiltInIds[0], CharacterRegistry.BuiltInIds[1]);
            Assert.AreNotEqual(CharacterRegistry.BuiltInIds[1], CharacterRegistry.BuiltInIds[2]);
            Assert.AreNotEqual(CharacterRegistry.BuiltInIds[2], CharacterRegistry.RocketboxFemaleId);
        }
    }
}
