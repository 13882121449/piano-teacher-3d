using System.Collections.Generic;
using NUnit.Framework;
using PianoTeacher3D.Characters;
using UnityEngine;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 角色资料与 ID 规则校验（对应测试计划 EditMode「CharacterProfile 必填字段和资源引用验证」）。
    /// 纯内存：只创建 ScriptableObject，不加载场景、不实例化预制体。
    /// </summary>
    public class CharacterProfileTests
    {
        private readonly List<CharacterProfile> _created = new List<CharacterProfile>();

        private CharacterProfile Make()
        {
            var profile = ScriptableObject.CreateInstance<CharacterProfile>();
            _created.Add(profile);
            return profile;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        // ==================== ID 规则 ====================

        [Test]
        public void CharacterId_ValidLowercaseWithPunctuation_Passes()
        {
            Assert.IsTrue(CharacterIdRules.Validate("builtin.realistic.female").Ok);
            Assert.IsTrue(CharacterIdRules.Validate("rocketbox.female01").Ok);
            Assert.IsTrue(CharacterIdRules.Validate("a").Ok);
            Assert.IsTrue(CharacterIdRules.Validate("char_01-beta.v2").Ok);
        }

        [Test]
        public void CharacterId_Uppercase_Fails()
        {
            var result = CharacterIdRules.Validate("Builtin.Female");
            Assert.IsFalse(result.Ok);
            Assert.IsNotEmpty(result.Message);

            // 首字母大写走「必须以小写字母或数字开头」，中间大写走「非法字符」；
            // 两条路径都必须失败，但不把提示文案钉死（文案会随体验优化调整）。
            var middle = CharacterIdRules.Validate("builtin.Female");
            Assert.IsFalse(middle.Ok);
            Assert.IsNotEmpty(middle.Message);
        }

        [Test]
        public void CharacterId_WithSpace_Fails()
        {
            Assert.IsFalse(CharacterIdRules.Validate("builtin female").Ok);
        }

        [Test]
        public void CharacterId_TooLong_Fails()
        {
            var id = new string('a', CharacterIdRules.MaxLength + 1);
            var result = CharacterIdRules.Validate(id);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("超过", result.Message);
        }

        [Test]
        public void CharacterId_ExactlyMaxLength_Passes()
        {
            Assert.IsTrue(CharacterIdRules.Validate(new string('a', CharacterIdRules.MaxLength)).Ok);
        }

        [Test]
        public void CharacterId_EmptyOrNull_Fails()
        {
            Assert.IsFalse(CharacterIdRules.Validate(string.Empty).Ok);
            Assert.IsFalse(CharacterIdRules.Validate(null).Ok);
        }

        [Test]
        public void CharacterId_StartingWithDigit_Passes_ButPunctuation_Fails()
        {
            Assert.IsTrue(CharacterIdRules.Validate("1st.character").Ok);
            Assert.IsFalse(CharacterIdRules.Validate(".leading.dot").Ok);
            Assert.IsFalse(CharacterIdRules.Validate("-leading.dash").Ok);
        }

        // ==================== 资料必填项 ====================

        [Test]
        public void BuiltInProfile_PassesValidation()
        {
            var profile = CharacterRegistry.CreateBuiltIn(0);
            _created.Add(profile);

            var result = profile.Validate();
            Assert.IsTrue(result.Ok, result.Message);
            Assert.AreEqual(CharacterSource.ProceduralBuiltIn, profile.Source);
            Assert.AreEqual(0, profile.ProceduralIndex);
        }

        [Test]
        public void MissingDisplayName_Fails()
        {
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "  ";

            var result = CharacterAssetValidator.ValidateProfile(profile);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("显示名称", result.Message);
        }

        [Test]
        public void PrefabSource_WithoutPrefabAndPath_Fails()
        {
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.Prefab;
            profile.Prefab = null;
            profile.ResourcesPath = string.Empty;

            var result = CharacterAssetValidator.ValidateProfile(profile);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("未指定预制体", result.Message);
        }

        [Test]
        public void PrefabSource_WithOnlyResourcesPath_Passes()
        {
            // 说明：CharacterProfile.Validate 目前把「Prefab 为空」一律判失败，
            // 但该字段注释允许用 ResourcesPath 承载（Rocketbox 档案即如此）。
            // CharacterAssetValidator 对这种情况做单条放宽，并补跑后续规则。
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.Prefab;
            profile.Prefab = null;
            profile.ResourcesPath = "Characters/Test/Model";
            profile.LicenseName = "MIT";

            var result = CharacterAssetValidator.ValidateProfile(profile);
            Assert.IsTrue(result.Ok, result.Message);
        }

        [Test]
        public void PrefabSource_WithResourcesPath_ButMissingLicense_Fails()
        {
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.Prefab;
            profile.Prefab = null;
            profile.ResourcesPath = "Characters/Test/Model";
            profile.LicenseName = string.Empty;

            var result = CharacterAssetValidator.ValidateProfile(profile);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("许可", result.Message);
        }

        [Test]
        public void MissingLicense_Fails()
        {
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.ProceduralBuiltIn;
            profile.ProceduralIndex = 0;
            profile.LicenseName = null;

            var result = CharacterAssetValidator.ValidateProfile(profile);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("许可", result.Message);
        }

        [Test]
        public void DuplicateAppearanceSlot_Fails()
        {
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.ProceduralBuiltIn;
            profile.ProceduralIndex = 0;
            profile.SupportedAppearanceSlots = new[] { AppearanceSlot.Thumbnail, AppearanceSlot.Thumbnail };

            var result = CharacterAssetValidator.ValidateProfile(profile);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("重复", result.Message);
        }

        [Test]
        public void NegativeProceduralIndex_Fails()
        {
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.ProceduralBuiltIn;
            profile.ProceduralIndex = -3;

            var result = CharacterAssetValidator.ValidateProfile(profile);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void ProceduralIndex_OutOfRange_Fails()
        {
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.ProceduralBuiltIn;
            profile.ProceduralIndex = 9;

            var result = CharacterAssetValidator.ValidateAssetResolvable(profile);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("超出范围", result.Message);
        }

        [Test]
        public void QualityTierOutOfRange_Fails()
        {
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.ProceduralBuiltIn;
            profile.ProceduralIndex = 0;
            profile.QualityTier = 5;

            Assert.IsFalse(CharacterAssetValidator.ValidateProfile(profile).Ok);
        }

        [Test]
        public void NonPositiveRootScale_Fails()
        {
            var profile = Make();
            profile.CharacterId = "test.character";
            profile.DisplayName = "测试角色";
            profile.Source = CharacterSource.ProceduralBuiltIn;
            profile.ProceduralIndex = 0;
            profile.RootScale = 0f;

            Assert.IsFalse(CharacterAssetValidator.ValidateProfile(profile).Ok);
        }

        [Test]
        public void ValidateProfile_NullProfile_FailsWithoutThrowing()
        {
            var result = CharacterAssetValidator.ValidateProfile(null);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void SupportsSlot_ReflectsDeclaredSlots()
        {
            var profile = CharacterRegistry.CreateBuiltIn(1);
            _created.Add(profile);

            Assert.IsTrue(profile.SupportsSlot(AppearanceSlot.Thumbnail));
            Assert.IsTrue(profile.SupportsSlot(AppearanceSlot.BodyTint));
            Assert.IsFalse(profile.SupportsSlot(AppearanceSlot.FaceTexture));
        }

        [Test]
        public void RocketboxProfile_DeclaresLicenseAndResourcePath()
        {
            var profile = CharacterRegistry.CreateRocketbox();
            _created.Add(profile);

            Assert.AreEqual(CharacterSource.Prefab, profile.Source);
            Assert.AreEqual("MIT", profile.LicenseName);
            Assert.IsFalse(string.IsNullOrEmpty(profile.LicenseUrl));
            StringAssert.Contains("Rocketbox", profile.ResourcesPath);
            StringAssert.Contains("坐姿", profile.Notes);
            Assert.IsTrue(CharacterAssetValidator.ValidateProfile(profile).Ok);
        }
    }
}
