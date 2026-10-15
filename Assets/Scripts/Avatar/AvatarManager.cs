using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 统一手部控制接口（方案 3.2）：手腕、手掌、手指。
    /// 各角色共用此接口，使 IK 与姿态控制与具体模型解耦。
    /// </summary>
    public interface IHandRig
    {
        Transform Wrist { get; }
        Transform Palm { get; }
        IReadOnlyList<Transform> Fingers { get; }
    }

    /// <summary>预设角色外观参数。</summary>
    [System.Serializable]
    public class AvatarPreset
    {
        public string Name;
        public float Height = 1.45f;
        public float HeadScale = 1f;
        public Color SkinColor = new Color(0.96f, 0.85f, 0.74f);
        public Color ClothColor = new Color(0.3f, 0.5f, 0.9f);
        public int HairStyle; // 0 长发 / 1 短发 / 2 卡通呆毛
        public bool Cartoon;
    }

    /// <summary>
    /// 管理人物加载、选择与切换（方案 4.2 / 3.2）。
    /// 预生成 3 个程序化人形（写实女性 / 写实男性 / 卡通），采用真实人体比例
    /// （头身比约 1:7，含肩、躯干收腰、坐姿腿部、五官与琴凳），零外部模型资产。
    /// 切换仅替换外观与手部引用，不触碰时间轴与演奏状态（方案「人物与演奏解耦」）。
    /// </summary>
    public class AvatarManager : MonoBehaviour
    {
        public const int AvatarCount = 3;

        /// <summary>基准人体身高（其余预设按 Height/基准 等比缩放）。</summary>
        private const float BaseHeight = 1.45f;

        private readonly List<AvatarPreset> _presets = new()
        {
            new AvatarPreset
            {
                Name = "写实女性", Height = 1.38f, HeadScale = 0.94f,
                SkinColor = new Color(0.97f, 0.86f, 0.78f), ClothColor = new Color(0.62f, 0.27f, 0.33f),
                HairStyle = 0, Cartoon = false
            },
            new AvatarPreset
            {
                Name = "写实男性", Height = 1.52f, HeadScale = 1.0f,
                SkinColor = new Color(0.86f, 0.73f, 0.62f), ClothColor = new Color(0.20f, 0.29f, 0.45f),
                HairStyle = 1, Cartoon = false
            },
            new AvatarPreset
            {
                Name = "卡通角色", Height = 1.26f, HeadScale = 1.42f,
                SkinColor = new Color(1f, 0.92f, 0.82f), ClothColor = new Color(0.24f, 0.62f, 0.45f),
                HairStyle = 2, Cartoon = true
            }
        };

        private readonly List<AvatarRig> _rigs = new();
        private int _currentIndex;

        public int CurrentIndex => _currentIndex;
        public string CurrentName => _presets[_currentIndex].Name;
        public event System.Action<int> OnAvatarChanged;

        // 当前角色暴露给 HandIKController 的骨骼引用
        public Transform LeftShoulder => _rigs[_currentIndex].LeftShoulder;
        public Transform RightShoulder => _rigs[_currentIndex].RightShoulder;
        public Transform LeftArm => _rigs[_currentIndex].LeftArm;
        public Transform RightArm => _rigs[_currentIndex].RightArm;
        public IHandRig LeftHand => _rigs[_currentIndex].LeftHand;
        public IHandRig RightHand => _rigs[_currentIndex].RightHand;

        /// <summary>当前角色的标准 Humanoid 骨架（方案 4.1 / 3.2）。未构建时为 null。</summary>
        public AvatarRigBuilder.Skeleton CurrentSkeleton =>
            (_rigs != null && _currentIndex >= 0 && _currentIndex < _rigs.Count) ? _rigs[_currentIndex].Skeleton : null;

        /// <summary>当前角色的骨架是否为 Unity 认可的合法 Humanoid。</summary>
        public bool HasHumanoidRig => CurrentSkeleton != null && CurrentSkeleton.IsHumanoid;

        /// <summary>取指定角色的骨架（测试与调试用）。索引越界返回 null。</summary>
        public AvatarRigBuilder.Skeleton GetSkeleton(int index) =>
            (_rigs != null && index >= 0 && index < _rigs.Count) ? _rigs[index].Skeleton : null;

        /// <summary>在骨架根上绑定 Animator（必须与 Avatar 的根为同一 GameObject）。</summary>
        private static Animator AttachHumanoidAnimator(AvatarRigBuilder.Skeleton skeleton)
        {
            if (skeleton == null || skeleton.Root == null || !skeleton.IsHumanoid) return null;

            var animator = skeleton.Root.gameObject.AddComponent<Animator>();
            animator.avatar = skeleton.Avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        /// <summary>预生成全部角色，默认显示第一个。</summary>
        public void BuildAvatars()
        {
            for (int i = 0; i < AvatarCount; i++)
            {
                var rig = BuildAvatarRig(_presets[i]);
                rig.Root.name = $"Avatar_{i}_{_presets[i].Name}";
                rig.Root.SetActive(i == 0);
                _rigs.Add(rig);
            }
            _currentIndex = 0;
        }

        /// <summary>切换角色：仅替换表现，不重置曲目/进度/摄像机（方案验收标准）。</summary>
        public void SwitchAvatar(int index)
        {
            index = Mathf.Clamp(index, 0, AvatarCount - 1);
            if (index == _currentIndex) return;

            _rigs[_currentIndex].Root.SetActive(false);
            _currentIndex = index;
            _rigs[_currentIndex].Root.SetActive(true);
            OnAvatarChanged?.Invoke(_currentIndex);
        }

        public void SwitchNext() => SwitchAvatar((_currentIndex + 1) % AvatarCount);

        // ==================== 程序化人形构建（真实比例，零外部模型资产） ====================

        private sealed class AvatarRig
        {
            public GameObject Root;
            public Transform LeftShoulder;
            public Transform RightShoulder;
            public Transform LeftArm;
            public Transform RightArm;
            public IHandRig LeftHand;
            public IHandRig RightHand;

            /// <summary>标准 Humanoid 骨架（方案 4.1 / 3.2）。</summary>
            public AvatarRigBuilder.Skeleton Skeleton;

            /// <summary>绑定 Humanoid Avatar 的 Animator（骨架有效时非 null）。</summary>
            public Animator Animator;

            public bool HasHumanoidRig => Skeleton != null && Skeleton.IsHumanoid;
        }

        private AvatarRig BuildAvatarRig(AvatarPreset p)
        {
            var rig = new AvatarRig();
            var root = new GameObject("Avatar");
            root.transform.SetParent(transform, false);

            float s = p.Height / BaseHeight;          // 体型缩放（相对基准人）
            float headR = 0.105f * p.HeadScale * s;   // 头半径（基准头身比约 1:6.9）

            // ---- 标准 Humanoid 骨架 + Avatar（方案 4.1「支持 Humanoid 骨骼的模型」）----
            // 骨架为纯坐标节点（无 Renderer，不参与渲染），视觉仍由下方程序化网格提供；
            // 未来替换为真实 GLB 模型时，只需把外部网格挂到同一套骨骼上。
            rig.Skeleton = AvatarRigBuilder.Build(root.transform, s);
            rig.Animator = AttachHumanoidAnimator(rig.Skeleton);

            var skinMat = CreateMaterial(p.SkinColor);
            var clothMat = CreateMaterial(p.ClothColor);
            var darkMat = CreateMaterial(new Color(0.16f, 0.16f, 0.19f));
            var hairMat = CreateMaterial(p.HairStyle == 2
                ? new Color(0.22f, 0.58f, 0.92f)
                : new Color(0.155f, 0.125f, 0.105f));

            // ---- 琴凳 ----
            CreatePart(PrimitiveType.Cube, root.transform, "Stool",
                new Vector3(0f, 0.42f * s, 0.10f * s),
                new Vector3(0.34f * s, 0.05f * s, 0.26f * s), darkMat);

            // ---- 腿（坐姿：大腿前伸 + 小腿垂直 + 脚）----
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.105f * s;
                CreatePart(PrimitiveType.Capsule, root.transform, $"Thigh_{side}",
                    new Vector3(x, 0.50f * s, 0.17f * s),
                    new Vector3(0.105f * s, 0.14f * s, 0.105f * s), clothMat,
                    Quaternion.Euler(90f, 0f, 0f));
                CreatePart(PrimitiveType.Capsule, root.transform, $"Shin_{side}",
                    new Vector3(x, 0.29f * s, 0.31f * s),
                    new Vector3(0.095f * s, 0.21f * s, 0.095f * s), clothMat);
                CreatePart(PrimitiveType.Cube, root.transform, $"Foot_{side}",
                    new Vector3(x, 0.035f * s, 0.37f * s),
                    new Vector3(0.085f * s, 0.07f * s, 0.19f * s), darkMat);
            }

            // ---- 躯干（方体：髋 → 收腰 → 胸腔，比球体更接近着衣人体）----
            CreatePart(PrimitiveType.Cube, root.transform, "Hip",
                new Vector3(0f, 0.52f * s, 0f), new Vector3(0.31f * s, 0.13f * s, 0.21f * s), clothMat);
            CreatePart(PrimitiveType.Cube, root.transform, "Waist",
                new Vector3(0f, 0.66f * s, 0f), new Vector3(0.27f * s, 0.15f * s, 0.19f * s), clothMat);
            CreatePart(PrimitiveType.Cube, root.transform, "Chest",
                new Vector3(0f, 0.83f * s, 0f), new Vector3(0.34f * s, 0.21f * s, 0.21f * s), clothMat);

            // ---- 肩（空节点供 IK 使用 + 视觉肩球）----
            // 注意：人物在装配时整体旋转 180°（面向 -Z 朝钢琴），故 local +X 的节点旋转后位于世界 -X，
            // 即演奏者左手侧（负责低音键）。左右手必须按此映射，否则双臂会交叉。
            var lShoulder = new GameObject("LeftShoulder").transform;
            lShoulder.SetParent(root.transform, false);
            lShoulder.localPosition = new Vector3(0.185f * s, 0.95f * s, 0f);
            CreatePart(PrimitiveType.Sphere, lShoulder, "Ball", Vector3.zero, Vector3.one * (0.10f * s), clothMat);

            var rShoulder = new GameObject("RightShoulder").transform;
            rShoulder.SetParent(root.transform, false);
            rShoulder.localPosition = new Vector3(-0.185f * s, 0.95f * s, 0f);
            CreatePart(PrimitiveType.Sphere, rShoulder, "Ball", Vector3.zero, Vector3.one * (0.10f * s), clothMat);

            // ---- 颈 + 头 + 五官 ----
            CreatePart(PrimitiveType.Capsule, root.transform, "Neck",
                new Vector3(0f, 1.02f * s, 0f), new Vector3(0.065f * s, 0.04f * s, 0.065f * s), skinMat);

            float headY = 1.14f * s;
            CreatePart(PrimitiveType.Sphere, root.transform, "Head",
                new Vector3(0f, headY, 0f), Vector3.one * (headR * 2f), skinMat);
            float eyeR = headR * 0.13f;
            CreatePart(PrimitiveType.Sphere, root.transform, "Eye_L",
                new Vector3(-headR * 0.40f, headY + headR * 0.06f, headR * 0.86f),
                Vector3.one * (eyeR * 2f), darkMat);
            CreatePart(PrimitiveType.Sphere, root.transform, "Eye_R",
                new Vector3(headR * 0.40f, headY + headR * 0.06f, headR * 0.86f),
                Vector3.one * (eyeR * 2f), darkMat);
            CreatePart(PrimitiveType.Sphere, root.transform, "Nose",
                new Vector3(0f, headY - headR * 0.14f, headR * 0.97f),
                Vector3.one * (headR * 0.15f), skinMat);

            BuildHair(root.transform, p, headY, headR, hairMat);

            // ---- 手臂（单段胶囊，由 HandIKController 逐帧拉伸肩→腕）----
            var lArm = CreatePart(PrimitiveType.Capsule, root.transform, "LeftArm",
                new Vector3(-0.20f * s, 0.90f * s, 0.10f * s),
                new Vector3(0.048f * s, 0.28f * s, 0.048f * s), skinMat);
            var rArm = CreatePart(PrimitiveType.Capsule, root.transform, "RightArm",
                new Vector3(0.20f * s, 0.90f * s, 0.10f * s),
                new Vector3(0.048f * s, 0.28f * s, 0.048f * s), skinMat);

            // ---- 手 ----
            var lHand = BuildHand(root.transform, "LeftHand", skinMat, s, +1f);
            var rHand = BuildHand(root.transform, "RightHand", skinMat, s, -1f);

            rig.Root = root;
            rig.LeftShoulder = lShoulder;
            rig.RightShoulder = rShoulder;
            rig.LeftArm = lArm;
            rig.RightArm = rArm;
            rig.LeftHand = lHand;
            rig.RightHand = rHand;
            return rig;
        }

        /// <summary>构建单手：手腕 → 手掌 → 5 指（手指沿 +Z 延伸，可由姿态控制器弯曲）。</summary>
        private static IHandRig BuildHand(Transform parent, string name, Material skinMat, float s, float side)
        {
            float handScale = 0.052f * s;

            var wrist = new GameObject(name).transform;
            wrist.SetParent(parent, false);
            wrist.localPosition = new Vector3(side * 0.20f * s, 0.90f * s, 0.10f * s);

            var palm = CreatePart(PrimitiveType.Cube, wrist, "Palm",
                new Vector3(0f, 0f, handScale * 0.62f),
                new Vector3(handScale * 1.18f, handScale * 0.38f, handScale * 1.30f), skinMat);

            var fingers = new List<Transform>(5);
            for (int f = 0; f < 5; f++)
            {
                var fingerRoot = new GameObject($"Finger{f + 1}").transform;
                fingerRoot.SetParent(wrist, false);
                fingerRoot.localPosition = new Vector3((f - 2) * handScale * 0.38f, 0f, handScale * 1.32f);

                float len = handScale * (1.85f - Mathf.Abs(f - 2) * 0.12f);
                CreatePart(PrimitiveType.Capsule, fingerRoot, "Mesh",
                    new Vector3(0f, 0f, len * 0.5f),
                    new Vector3(handScale * 0.26f, len * 0.5f, handScale * 0.26f), skinMat,
                    Quaternion.Euler(90f, 0f, 0f)); // 胶囊默认沿 Y，转 90° 使其沿 Z（手指伸出方向）

                fingers.Add(fingerRoot);
            }

            return new HandRig(wrist, palm, fingers);
        }

        private sealed class HandRig : IHandRig
        {
            public Transform Wrist { get; }
            public Transform Palm { get; }
            public IReadOnlyList<Transform> Fingers { get; }

            public HandRig(Transform wrist, Transform palm, IReadOnlyList<Transform> fingers)
            {
                Wrist = wrist;
                Palm = palm;
                Fingers = fingers;
            }
        }

        private static void BuildHair(Transform parent, AvatarPreset p, float headY, float headR, Material hairMat)
        {
            // 基础发帽（覆盖头顶与后部，露出面部）
            CreatePart(PrimitiveType.Sphere, parent, "HairCap",
                new Vector3(0f, headY + headR * 0.30f, -headR * 0.16f),
                new Vector3(headR * 1.92f, headR * 1.62f, headR * 1.92f), hairMat);

            if (p.HairStyle == 0)
            {
                // 长发：贴头两侧的披肩发（短而靠后，避免夸张的"双马尾"感）
                for (int i = -1; i <= 1; i += 2)
                {
                    CreatePart(PrimitiveType.Capsule, parent, $"HairSide_{i}",
                        new Vector3(i * headR * 0.82f, headY - headR * 0.42f, -headR * 0.38f),
                        new Vector3(headR * 0.40f, headR * 0.62f, headR * 0.40f), hairMat);
                }
                CreatePart(PrimitiveType.Capsule, parent, "HairBack",
                    new Vector3(0f, headY - headR * 0.55f, -headR * 0.80f),
                    new Vector3(headR * 1.20f, headR * 0.75f, headR * 0.38f), hairMat);
            }
            else if (p.HairStyle == 2)
            {
                // 卡通呆毛
                var ahoge = CreatePart(PrimitiveType.Capsule, parent, "Ahoge",
                    new Vector3(0f, headY + headR * 1.45f, 0f),
                    new Vector3(headR * 0.16f, headR * 0.60f, headR * 0.16f), hairMat);
                ahoge.localRotation = Quaternion.Euler(18f, 0f, 0f);
            }
        }

        // ==================== 构建辅助 ====================

        private static Transform CreatePart(PrimitiveType type, Transform parent, string name,
            Vector3 localPos, Vector3 localScale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localScale = localScale;
            if (rot.HasValue) t.localRotation = rot.Value;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.Destroy(go.GetComponent<Collider>()); // 纯视觉件，无碰撞需求
            return t;
        }

        private static Material CreateMaterial(Color c)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            return new Material(shader) { color = c };
        }
    }
}
