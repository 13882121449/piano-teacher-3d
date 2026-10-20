using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 角色资源校验（方案 5.2 / 7.2）：资料侧 + 预制体侧。
    /// <para>
    /// 设计原则：<b>永不抛异常</b>。所有失败都折算成一句面向用户的中文原因，
    /// 调用方（切换服务）据此保留旧角色并提示，而不是让 NullReferenceException 连锁到演奏流程（方案 6.4）。
    /// </para>
    /// </summary>
    public static class CharacterAssetValidator
    {
        /// <summary>
        /// 资料侧校验：字段完整性 + 资源引用是否可解析。
        /// </summary>
        public static CharacterValidation ValidateProfile(CharacterProfile profile)
        {
            if (profile == null) return CharacterValidation.Fail("角色资料为空。");

            CharacterValidation result;
            try
            {
                result = profile.Validate();
            }
            catch (Exception e)
            {
                return CharacterValidation.Fail("角色资料校验异常：" + e.Message);
            }

            if (result.Ok) return result.And(ValidateAssetResolvable(profile));

            // CharacterProfile.Validate 目前把「Prefab 为空」一律判失败，但同一字段的注释
            // 明确允许用 ResourcesPath 承载资源（本项目的 Rocketbox 档案就是这种形态）。
            // 这里只放宽这一条，并补跑它之后的检查项（Validate 只返回第一个失败原因）。
            // TODO(主控)：若 CharacterProfile.Validate 改为「Prefab 或 ResourcesPath 至少一个」，
            //             本分支即为死代码，可直接删除 ValidateAfterPrefabRequirement。
            if (profile.Source == CharacterSource.Prefab
                && profile.Prefab == null
                && !string.IsNullOrWhiteSpace(profile.ResourcesPath))
            {
                return ValidateAfterPrefabRequirement(profile).And(ValidateAssetResolvable(profile));
            }

            return result;
        }

        /// <summary>
        /// 资源引用是否可解析：程序化角色要求内置索引合法；预制体角色要求「有引用」或「有 Resources 路径」。
        /// </summary>
        public static CharacterValidation ValidateAssetResolvable(CharacterProfile profile)
        {
            if (profile == null) return CharacterValidation.Fail("角色资料为空。");

            if (profile.Source == CharacterSource.ProceduralBuiltIn)
            {
                int max = CharacterRegistry.BuiltInIds.Length - 1;
                if (profile.ProceduralIndex < 0 || profile.ProceduralIndex > max)
                    return CharacterValidation.Fail(
                        $"角色 {profile.CharacterId} 的内置索引 {profile.ProceduralIndex} 超出范围 0–{max}。");
                return CharacterValidation.Pass;
            }

            if (profile.Prefab != null) return CharacterValidation.Pass;
            if (!string.IsNullOrWhiteSpace(profile.ResourcesPath)) return CharacterValidation.Pass;

            return CharacterValidation.Fail(
                $"角色 {profile.CharacterId} 既未指定预制体，也没有 Resources 路径。");
        }

        /// <summary>视图侧校验：转调 <see cref="ICharacterView.ValidateRig"/> 并兜住异常。</summary>
        public static CharacterValidation ValidateView(ICharacterView view)
        {
            if (view == null) return CharacterValidation.Fail("角色视图为空。");
            try
            {
                return view.ValidateRig();
            }
            catch (Exception e)
            {
                return CharacterValidation.Fail("角色骨架校验异常：" + e.Message);
            }
        }

        /// <summary>
        /// 预制体根节点校验：Animator（Humanoid） + 手部骨骼完整性。
        /// <paramref name="requiredBoneNames"/> 为 null 时使用 Biped（Rocketbox）骨骼表。
        /// </summary>
        public static CharacterValidation ValidatePrefabRoot(GameObject root, string[] requiredBoneNames = null)
        {
            if (root == null) return CharacterValidation.Fail("角色预制体为空。");

            try
            {
                var animator = root.GetComponentInChildren<Animator>(true);
                if (animator == null) return CharacterValidation.Fail("角色缺少 Animator 组件。");

                var avatar = animator.avatar;
                if (avatar == null) return CharacterValidation.Fail("角色缺少 Humanoid Avatar（检查 FBX 的 Rig 导入设置）。");
                if (!avatar.isValid) return CharacterValidation.Fail("角色的 Humanoid Avatar 无效（骨骼映射不完整）。");
                if (!avatar.isHuman) return CharacterValidation.Fail("角色的 Avatar 不是 Humanoid 类型。");

                return ValidateHandSkeleton(root, requiredBoneNames ?? BuildBipedHandBoneNames());
            }
            catch (Exception e)
            {
                return CharacterValidation.Fail("角色预制体校验异常：" + e.Message);
            }
        }

        /// <summary>
        /// 检查手部骨骼是否齐全：左右各「肩 / 上臂 / 前臂 / 手」+ 每指 3 节。
        /// 缺少时返回前若干个具体骨骼名，便于直接定位导入问题。
        /// </summary>
        public static CharacterValidation ValidateHandSkeleton(GameObject root, string[] requiredBoneNames)
        {
            if (root == null) return CharacterValidation.Fail("角色预制体为空。");
            if (requiredBoneNames == null || requiredBoneNames.Length == 0) return CharacterValidation.Pass;

            try
            {
                var index = BuildTransformIndex(root);
                var missing = new List<string>(4);
                for (int i = 0; i < requiredBoneNames.Length; i++)
                {
                    var name = requiredBoneNames[i];
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!index.ContainsKey(name)) missing.Add(name);
                }
                if (missing.Count == 0) return CharacterValidation.Pass;

                var sb = new StringBuilder("缺少骨骼 ");
                int shown = Mathf.Min(missing.Count, 3);
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0) sb.Append('、');
                    sb.Append(missing[i]);
                }
                if (missing.Count > shown) sb.Append($" 等 {missing.Count} 根");
                return CharacterValidation.Fail(sb.ToString());
            }
            catch (Exception e)
            {
                return CharacterValidation.Fail("手部骨骼校验异常：" + e.Message);
            }
        }

        /// <summary>把层级里的所有 Transform 按名字建索引（重名取先出现的那个，含未激活节点）。</summary>
        public static Dictionary<string, Transform> BuildTransformIndex(GameObject root)
        {
            var index = new Dictionary<string, Transform>(StringComparer.Ordinal);
            if (root == null) return index;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                if (!index.ContainsKey(t.name)) index.Add(t.name, t);
            }
            return index;
        }

        /// <summary>
        /// 3ds Max Biped（Rocketbox 使用）的手部骨骼名表：
        /// 每指 3 节命名为 <c>Bip01 L Finger0 / Finger01 / Finger02</c>，四指为 Finger1..Finger4。
        /// </summary>
        public static string[] BuildBipedHandBoneNames()
        {
            var names = new List<string>(48);
            for (int side = 0; side < 2; side++)
            {
                string prefix = side == 0 ? "Bip01 L " : "Bip01 R ";
                names.Add(prefix + "Clavicle");
                names.Add(prefix + "UpperArm");
                names.Add(prefix + "Forearm");
                names.Add(prefix + "Hand");

                for (int f = 0; f < 5; f++)
                {
                    names.Add(prefix + "Finger" + f);
                    names.Add(prefix + "Finger" + f + "1");
                    names.Add(prefix + "Finger" + f + "2");
                }
            }
            return names.ToArray();
        }

        /// <summary>
        /// 补跑 <see cref="CharacterProfile.Validate"/> 中位于「预制体检查」之后的规则。
        /// 仅在「Prefab 为空但给了 ResourcesPath」的放宽分支里使用。
        /// </summary>
        private static CharacterValidation ValidateAfterPrefabRequirement(CharacterProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.LicenseName))
                return CharacterValidation.Fail($"角色 {profile.CharacterId} 缺少许可信息。");
            if (profile.QualityTier < 0 || profile.QualityTier > 2)
                return CharacterValidation.Fail($"角色 {profile.CharacterId} 的质量级别必须在 0–2 之间。");
            if (profile.RootScale <= 0f)
                return CharacterValidation.Fail($"角色 {profile.CharacterId} 的缩放必须为正数。");

            var slots = profile.SupportedAppearanceSlots;
            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    for (int j = i + 1; j < slots.Length; j++)
                    {
                        if (slots[i] == slots[j])
                            return CharacterValidation.Fail(
                                $"角色 {profile.CharacterId} 的外观槽位 {slots[i]} 重复声明。");
                    }
                }
            }
            return CharacterValidation.Pass;
        }
    }
}
