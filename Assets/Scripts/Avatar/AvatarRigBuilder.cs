using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 程序化构建符合 Unity Humanoid 规范的骨架，并生成合法 <see cref="Avatar"/>。
    /// <para>
    /// 对应方案 4.1「3D 人物：支持 Humanoid 骨骼的模型」与 3.2「角色应尽可能使用统一的人体骨骼规范」。
    /// </para>
    /// <para>
    /// 完全程序化：不引入任何第三方模型资源（零许可风险，符合方案「遵守资源许可证」原则）。
    /// 骨骼命名严格对应 <see cref="HumanBodyBones"/>，因此未来替换为真实 GLB 模型时，
    /// 只需把外部网格挂到同一套骨骼上，其余逻辑（IK / 手指姿态 / 换装）无需改动。
    /// </para>
    /// </summary>
    public static class AvatarRigBuilder
    {
        /// <summary>单手 5 指的 Humanoid 段名（本实现每指 1 段，映射到 Proximal）。</summary>
        public static readonly string[] FingerSuffixes =
            { "ThumbProximal", "IndexProximal", "MiddleProximal", "RingProximal", "LittleProximal" };

        /// <summary>Humanoid 标准骨骼表：(Humanoid 名, 节点名, 父节点名)。根节点父为 null。</summary>
        private static readonly (string human, string node, string parent)[] BoneTable =
        {
            ("Hips",       "Hips",       null),
            ("Spine",      "Spine",      "Hips"),
            ("Chest",      "Chest",      "Spine"),
            ("UpperChest", "UpperChest", "Chest"),
            ("Neck",       "Neck",       "UpperChest"),
            ("Head",       "Head",       "Neck"),

            ("LeftShoulder",  "LeftShoulder",  "UpperChest"),
            ("LeftUpperArm",  "LeftUpperArm",  "LeftShoulder"),
            ("LeftLowerArm",  "LeftLowerArm",  "LeftUpperArm"),
            ("LeftHand",      "LeftHand",      "LeftLowerArm"),

            ("RightShoulder", "RightShoulder", "UpperChest"),
            ("RightUpperArm", "RightUpperArm", "RightShoulder"),
            ("RightLowerArm", "RightLowerArm", "RightUpperArm"),
            ("RightHand",     "RightHand",     "RightLowerArm"),

            ("LeftUpperLeg",  "LeftUpperLeg",  "Hips"),
            ("LeftLowerLeg",  "LeftLowerLeg",  "LeftUpperLeg"),
            ("LeftFoot",      "LeftFoot",      "LeftLowerLeg"),

            ("RightUpperLeg", "RightUpperLeg", "Hips"),
            ("RightLowerLeg", "RightLowerLeg", "RightUpperLeg"),
            ("RightFoot",     "RightFoot",     "RightLowerLeg"),
        };

        /// <summary>骨架节点集合（全部为 Humanoid 标准命名）。</summary>
        public sealed class Skeleton
        {
            public Transform Root;
            public Transform Hips, Spine, Chest, UpperChest, Neck, Head;
            public Transform LeftShoulder, LeftUpperArm, LeftLowerArm, LeftHand;
            public Transform RightShoulder, RightUpperArm, RightLowerArm, RightHand;
            public Transform LeftUpperLeg, LeftLowerLeg, LeftFoot;
            public Transform RightUpperLeg, RightLowerLeg, RightFoot;
            public readonly Transform[] LeftFingers = new Transform[5];
            public readonly Transform[] RightFingers = new Transform[5];

            // 注意：本文件位于命名空间 PianoTeacher3D.Avatar 内，
            // 直接用 `Avatar` 会被解析为命名空间（CS0118），故用完全限定名。
            public UnityEngine.Avatar Avatar;

            /// <summary>骨架是否被 Unity 认可为有效 Humanoid。</summary>
            public bool IsHumanoid => Avatar != null && Avatar.isValid && Avatar.isHuman;

            public Transform Get(bool leftHand, int fingerIndex)
            {
                if (fingerIndex < 0 || fingerIndex >= 5) return null;
                return leftHand ? LeftFingers[fingerIndex] : RightFingers[fingerIndex];
            }

            public IReadOnlyList<Transform> LeftFingerList => LeftFingers;
            public IReadOnlyList<Transform> RightFingerList => RightFingers;
        }

        /// <summary>
        /// 在 <paramref name="parent"/> 下构建标准 Humanoid 骨架并生成 Avatar。
        /// <paramref name="scale"/> 为体型缩放（1 ≈ 1.45m 坐姿高度基准）。
        /// </summary>
        public static Skeleton Build(Transform parent, float scale)
        {
            var rootGo = new GameObject("HumanoidRig");
            rootGo.transform.SetParent(parent, false);

            var skeleton = new Skeleton { Root = rootGo.transform };
            var nodes = new Dictionary<string, Transform>();

            // ---- 1. 躯干 / 四肢骨骼 ----
            foreach (var entry in BoneTable)
            {
                var t = new GameObject(entry.node).transform;
                t.SetParent(entry.parent == null ? rootGo.transform : nodes[entry.parent], false);
                t.localPosition = LocalPositionOf(entry.node, scale);
                nodes[entry.node] = t;
            }

            skeleton.Hips = nodes["Hips"];
            skeleton.Spine = nodes["Spine"];
            skeleton.Chest = nodes["Chest"];
            skeleton.UpperChest = nodes["UpperChest"];
            skeleton.Neck = nodes["Neck"];
            skeleton.Head = nodes["Head"];

            skeleton.LeftShoulder = nodes["LeftShoulder"];
            skeleton.LeftUpperArm = nodes["LeftUpperArm"];
            skeleton.LeftLowerArm = nodes["LeftLowerArm"];
            skeleton.LeftHand = nodes["LeftHand"];

            skeleton.RightShoulder = nodes["RightShoulder"];
            skeleton.RightUpperArm = nodes["RightUpperArm"];
            skeleton.RightLowerArm = nodes["RightLowerArm"];
            skeleton.RightHand = nodes["RightHand"];

            skeleton.LeftUpperLeg = nodes["LeftUpperLeg"];
            skeleton.LeftLowerLeg = nodes["LeftLowerLeg"];
            skeleton.LeftFoot = nodes["LeftFoot"];
            skeleton.RightUpperLeg = nodes["RightUpperLeg"];
            skeleton.RightLowerLeg = nodes["RightLowerLeg"];
            skeleton.RightFoot = nodes["RightFoot"];

            // ---- 2. 手指骨骼（每手 5 指 × 1 段，映射到 Humanoid 的 Proximal）----
            for (int f = 0; f < 5; f++)
            {
                skeleton.LeftFingers[f] = BuildFinger(skeleton.LeftHand, "Left", f, scale, left: true);
                skeleton.RightFingers[f] = BuildFinger(skeleton.RightHand, "Right", f, scale, left: false);
            }

            // ---- 3. 生成 Humanoid Avatar ----
            skeleton.Avatar = BuildAvatar(rootGo, nodes, skeleton);
            return skeleton;
        }

        private static Transform BuildFinger(Transform hand, string side, int index, float scale, bool left)
        {
            var t = new GameObject($"{side}{FingerSuffixes[index]}").transform;
            t.SetParent(hand, false);

            // 手指自手掌前端（+Z）伸出，横向按解剖顺序展开（拇指在一侧、小指在另一侧）
            float lateral = (index - 2) * 0.014f * scale * (left ? 1f : -1f);
            float length = (0.055f - Mathf.Abs(index - 2) * 0.004f) * scale;
            t.localPosition = new Vector3(lateral, 0f, length);
            return t;
        }

        /// <summary>相对父骨骼的局部位置（坐姿人体，比例参照 AvatarManager 的预设）。</summary>
        private static Vector3 LocalPositionOf(string node, float s) => node switch
        {
            "Hips" => new Vector3(0f, 0.52f * s, 0f),
            "Spine" => new Vector3(0f, 0.14f * s, 0f),
            "Chest" => new Vector3(0f, 0.17f * s, 0f),
            "UpperChest" => new Vector3(0f, 0.08f * s, 0f),
            "Neck" => new Vector3(0f, 0.11f * s, 0f),
            "Head" => new Vector3(0f, 0.12f * s, 0f),

            "LeftShoulder" => new Vector3(0.185f * s, 0.04f * s, 0f),
            "LeftUpperArm" => new Vector3(0.030f * s, 0f, 0f),
            "LeftLowerArm" => new Vector3(0.210f * s, 0f, 0f),
            "LeftHand" => new Vector3(0.200f * s, 0f, 0f),

            "RightShoulder" => new Vector3(-0.185f * s, 0.04f * s, 0f),
            "RightUpperArm" => new Vector3(-0.030f * s, 0f, 0f),
            "RightLowerArm" => new Vector3(-0.210f * s, 0f, 0f),
            "RightHand" => new Vector3(-0.200f * s, 0f, 0f),

            "LeftUpperLeg" => new Vector3(0.100f * s, -0.04f * s, 0f),
            "LeftLowerLeg" => new Vector3(0f, -0.26f * s, 0f),
            "LeftFoot" => new Vector3(0f, -0.26f * s, 0.07f * s),

            "RightUpperLeg" => new Vector3(-0.100f * s, -0.04f * s, 0f),
            "RightLowerLeg" => new Vector3(0f, -0.26f * s, 0f),
            "RightFoot" => new Vector3(0f, -0.26f * s, 0.07f * s),

            _ => Vector3.zero
        };

        /// <summary>用 AvatarBuilder 生成 Humanoid Avatar（失败时返回 null，不抛异常）。</summary>
        private static UnityEngine.Avatar BuildAvatar(GameObject rootGo, Dictionary<string, Transform> nodes, Skeleton skeleton)
        {
            var humanBones = new List<HumanBone>();
            foreach (var entry in BoneTable)
            {
                if (!nodes.TryGetValue(entry.node, out var t)) continue;
                humanBones.Add(new HumanBone
                {
                    humanName = entry.human,
                    boneName = t.name,
                    limit = new HumanLimit { useDefaultValues = true }
                });
            }

            for (int f = 0; f < 5; f++)
            {
                humanBones.Add(FingerBone("Left", f, skeleton.LeftFingers[f]));
                humanBones.Add(FingerBone("Right", f, skeleton.RightFingers[f]));
            }

            var skeletonBones = new List<SkeletonBone>();
            foreach (var t in rootGo.GetComponentsInChildren<Transform>(true))
            {
                skeletonBones.Add(new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                });
            }

            var description = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeletonBones.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            };

            return AvatarBuilder.BuildHumanAvatar(rootGo, description);
        }

        private static HumanBone FingerBone(string side, int index, Transform bone)
        {
            return new HumanBone
            {
                humanName = $"{side}{FingerSuffixes[index]}",
                boneName = bone != null ? bone.name : string.Empty,
                limit = new HumanLimit { useDefaultValues = true }
            };
        }
    }
}
