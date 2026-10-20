using UnityEditor;
using UnityEngine;

/// <summary>
/// 把导入模型配置为 Humanoid 并生成 Avatar（方案 8.2「配置 Rig/Avatar，验证 Humanoid 映射」）。
///
/// 背景（有实测依据）：Rocketbox 的 FBX 导入后 <c>ModelImporter.animationType</c> 实际为
/// <c>Generic</c>，因此没有生成 Avatar，实例化后也没有 Animator —— 手写 .meta 里的
/// <c>animationType: 2</c> 不会生效，必须通过导入器接口修改并重导入。
///
/// 用法（批量模式）：
/// <code>
/// Unity.exe -batchmode -quit -projectPath &lt;项目&gt; -executeMethod RocketboxAvatarSetup.Run
/// </code>
/// 幂等：已经是 Humanoid 且 Avatar 有效时只打印结果，不改动设置。
/// </summary>
public static class RocketboxAvatarSetup
{
    private const string ModelPath =
        "Assets/Characters/Rocketbox/Resources/Characters/Rocketbox/Female_Adult_01.fbx";

    /// <summary>执行 Humanoid 配置并打印 Avatar 校验结果。</summary>
    public static void Run()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"AVATARSETUP 找不到模型导入器：{ModelPath}");
            return;
        }

        Debug.Log($"AVATARSETUP 修改前 animationType={importer.animationType} " +
                  $"avatarSetup={(int)importer.avatarSetup} importAnimation={importer.importAnimation}");

        bool changed = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            changed = true;
        }

        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            changed = true;
        }

        if (!importer.importAnimation)
        {
            importer.importAnimation = true;
            changed = true;
        }

        // Biped 的手指命名（Bip01 L Finger0/01/02…）不在 Unity 自动映射规则里，自动映射只到 Hand。
        //
        // 实测结论（2026-10-20）：把 30 根手指显式写入 humanDescription 后重导入，Unity 生成的
        // Avatar 变成 isValid=True / isHuman=False —— 说明 Biped 的手指链不满足 Unity 的 Humanoid
        // 校验，整份描述被降级。因此默认不写手指映射：手指由 RocketboxHandRig 按骨骼名直接驱动，
        // 不依赖 Humanoid 映射；Humanoid 只用于保证 Avatar 可用与未来的动画重定向。
        // 需要重新试验时把下面常量改为 true，并重新执行本方法观察 Report() 的 isHuman。
        const bool EnableFingerMapping = false;
        if (EnableFingerMapping) changed |= MapFingers(importer);

        if (changed)
        {
            importer.SaveAndReimport();
            Debug.Log("AVATARSETUP 已写入 Humanoid 设置并重导入");
        }
        else
        {
            Debug.Log("AVATARSETUP 设置已是 Humanoid，无需修改");
        }

        // 一旦 humanDescription 里残留了不合法的手工映射，Unity 会降级成「isValid 但非 Human」的 Avatar，
        // 而且不会自动重算。此时清空映射并重导入，让 autoGenerateAvatarMappingIfUnspecified 重新生成。
        if (!IsHumanAvatar())
        {
            Debug.Log("AVATARSETUP 当前 Avatar 非 Human，清空人工映射后重新自动生成");
            var description = importer.humanDescription;
            description.human = new HumanBone[0];
            description.skeleton = new SkeletonBone[0];
            importer.humanDescription = description;
            importer.SaveAndReimport();
        }

        Report();
    }

    private static bool IsHumanAvatar()
    {
        var avatar = FindAvatar();
        return avatar != null && avatar.isHuman && avatar.isValid;
    }

    /// <summary>Biped 的 Finger0..Finger4 依次是拇指…小指，每指三节（FingerN / FingerN1 / FingerN2）。</summary>
    private static readonly string[] FingerHumanPrefixes =
        { "Thumb", "Index", "Middle", "Ring", "Little" };

    private static readonly string[] FingerSegmentSuffixes = { "Proximal", "Intermediate", "Distal" };

    /// <summary>把 30 根手指骨骼写入 humanDescription。已经有映射时不动，返回是否修改过。</summary>
    private static bool MapFingers(ModelImporter importer)
    {
        var description = importer.humanDescription;
        var existing = description.human ?? new HumanBone[0];

        bool missing = false;
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i].humanName == "LeftThumbProximal") { missing = false; break; }
            missing = true;
        }

        if (!missing && existing.Length > 0) return false;

        var merged = new System.Collections.Generic.List<HumanBone>(existing.Length + 30);
        for (int i = 0; i < existing.Length; i++)
        {
            // 丢掉可能存在的空映射，避免重复 humanName 导致导入报错
            if (!string.IsNullOrEmpty(existing[i].boneName)) merged.Add(existing[i]);
        }

        string[] sides = { "L", "R" };
        string[] sideNames = { "Left", "Right" };
        for (int s = 0; s < sides.Length; s++)
        {
            for (int f = 0; f < FingerHumanPrefixes.Length; f++)
            {
                for (int seg = 0; seg < FingerSegmentSuffixes.Length; seg++)
                {
                    string boneName = seg == 0
                        ? $"Bip01 {sides[s]} Finger{f}"
                        : $"Bip01 {sides[s]} Finger{f}{seg}";

                    // HumanLimit 没有 Default() 工厂：显式给出「不额外限制」的取值
                    merged.Add(new HumanBone
                    {
                        humanName = sideNames[s] + FingerHumanPrefixes[f] + FingerSegmentSuffixes[seg],
                        boneName = boneName,
                        limit = new HumanLimit
                        {
                            useDefaultValues = true,
                            min = new Vector3(-180f, -180f, -180f),
                            max = new Vector3(180f, 180f, 180f),
                            center = Vector3.zero,
                            axisLength = 0f
                        }
                    });
                }
            }
        }

        description.human = merged.ToArray();
        importer.humanDescription = description;
        return true;
    }

    /// <summary>只读报告：Avatar 是否有效、手部关键骨骼是否映射成功。</summary>
    public static void Report()
    {
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .FirstOrDefaultOfType<Avatar>() ?? FindAvatar();

        if (avatar == null)
        {
            Debug.LogError("AVATARSETUP 仍未生成 Avatar —— 需要人工在 Inspector 里做 Humanoid 映射");
            return;
        }

        Debug.Log($"AVATARSETUP avatar={avatar.name} isValid={avatar.isValid} isHuman={avatar.isHuman}");

        if (!avatar.isHuman)
        {
            Debug.LogError("AVATARSETUP Avatar 不是 Humanoid");
            return;
        }

        var hd = avatar.humanDescription;
        LogBone(hd, HumanBodyBones.Hips);
        LogBone(hd, HumanBodyBones.LeftHand);
        LogBone(hd, HumanBodyBones.RightHand);
        LogBone(hd, HumanBodyBones.LeftThumbProximal);
        LogBone(hd, HumanBodyBones.LeftIndexProximal);
        LogBone(hd, HumanBodyBones.LeftLittleDistal);
        LogBone(hd, HumanBodyBones.RightIndexProximal);
    }

    private static Avatar FindAvatar()
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is Avatar a) return a;
        }
        return null;
    }

    private static void LogBone(HumanDescription hd, HumanBodyBones bone)
    {
        for (int i = 0; i < hd.human.Length; i++)
        {
            if (hd.human[i].humanName != bone.ToString()) continue;
            var b = hd.human[i];
            Debug.Log($"AVATARSETUP bone {bone} -> {(string.IsNullOrEmpty(b.boneName) ? "(未映射)" : b.boneName)}");
            return;
        }
        Debug.LogError($"AVATARSETUP bone {bone} 未映射");
    }
}

/// <summary>避免为一次查找引入 System.Linq（项目热路径禁用 LINQ，工具代码保持一致）。</summary>
internal static class AssetArrayExtensions
{
    public static T FirstOrDefaultOfType<T>(this UnityEngine.Object[] assets) where T : UnityEngine.Object
    {
        if (assets == null) return null;
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is T typed) return typed;
        }
        return null;
    }
}
