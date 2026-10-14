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
        public float Height = 1.4f;
        public float HeadScale = 1f;
        public Color SkinColor = new Color(0.96f, 0.85f, 0.74f);
        public Color ClothColor = new Color(0.3f, 0.5f, 0.9f);
        public int HairStyle; // 0 长发 / 1 短发 / 2 卡通呆毛
        public bool Cartoon;
    }

    /// <summary>
    /// 管理人物加载、选择与切换（方案 4.2 / 3.2）。
    /// 预生成 3 个程序化人形（写实女性/写实男性/卡通），切换仅替换外观与手部引用，
    /// 不触碰时间轴与演奏状态（方案「人物与演奏解耦」）。
    /// </summary>
    public class AvatarManager : MonoBehaviour
    {
        public const int AvatarCount = 3;

        private readonly List<AvatarPreset> _presets = new()
        {
            new AvatarPreset
            {
                Name = "写实女性", Height = 1.35f, HeadScale = 0.95f,
                SkinColor = new Color(0.98f, 0.88f, 0.78f), ClothColor = new Color(0.78f, 0.35f, 0.4f),
                HairStyle = 0, Cartoon = false
            },
            new AvatarPreset
            {
                Name = "写实男性", Height = 1.5f, HeadScale = 1.0f,
                SkinColor = new Color(0.85f, 0.72f, 0.6f), ClothColor = new Color(0.25f, 0.4f, 0.7f),
                HairStyle = 1, Cartoon = false
            },
            new AvatarPreset
            {
                Name = "卡通角色", Height = 1.25f, HeadScale = 1.45f,
                SkinColor = new Color(1f, 0.92f, 0.8f), ClothColor = new Color(0.3f, 0.8f, 0.5f),
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

        // ---- 程序化人形构建（零外部模型资产） ----

        private sealed class AvatarRig
        {
            public GameObject Root;
            public Transform LeftShoulder;
            public Transform RightShoulder;
            public Transform LeftArm;
            public Transform RightArm;
            public IHandRig LeftHand;
            public IHandRig RightHand;
        }

        private AvatarRig BuildAvatarRig(AvatarPreset p)
        {
            var rig = new AvatarRig();
            var root = new GameObject("Avatar");
            root.transform.SetParent(transform, false);

            float torsoHalf = p.Height * 0.18f;
            float torsoY = p.Height * 0.32f;
            float shoulderY = p.Height * 0.55f;
            float shoulderSpan = p.Height * 0.28f;
            float headY = p.Height * 0.72f;

            // 躯干
            var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "Torso";
            torso.transform.SetParent(root.transform, false);
            torso.transform.localPosition = new Vector3(0f, torsoY, 0f);
            torso.transform.localScale = new Vector3(p.Height * 0.34f, torsoHalf, p.Height * 0.22f);
            Paint(torso, p.ClothColor);

            // 头 + 发型
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, headY, 0f);
            float headR = p.Height * 0.11f * p.HeadScale;
            head.transform.localScale = Vector3.one * (headR * 2f);
            Paint(head, p.SkinColor);
            BuildHair(root.transform, p, headY, headR);

            // 左右肩
            var lShoulder = new GameObject("LeftShoulder").transform;
            lShoulder.SetParent(root.transform, false);
            lShoulder.localPosition = new Vector3(-shoulderSpan, shoulderY, 0f);
            var rShoulder = new GameObject("RightShoulder").transform;
            rShoulder.SetParent(root.transform, false);
            rShoulder.localPosition = new Vector3(shoulderSpan, shoulderY, 0f);

            // 左右手臂（胶囊，由 HandIKController 每帧拉伸肩→腕）
            var lArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            lArm.name = "LeftArm";
            lArm.transform.SetParent(root.transform, false);
            lArm.transform.localScale = new Vector3(p.Height * 0.06f, 0.5f, p.Height * 0.06f);
            Paint(lArm, p.SkinColor);
            var rArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rArm.name = "RightArm";
            rArm.transform.SetParent(root.transform, false);
            rArm.transform.localScale = new Vector3(p.Height * 0.06f, 0.5f, p.Height * 0.06f);
            Paint(rArm, p.SkinColor);

            // 左右手
            var lHand = BuildHand(root.transform, "LeftHand", p, -1f);
            var rHand = BuildHand(root.transform, "RightHand", p, 1f);

            rig.Root = root;
            rig.LeftShoulder = lShoulder;
            rig.RightShoulder = rShoulder;
            rig.LeftArm = lArm.transform;
            rig.RightArm = rArm.transform;
            rig.LeftHand = lHand;
            rig.RightHand = rHand;
            return rig;
        }

        private IHandRig BuildHand(Transform parent, string name, AvatarPreset p, float side)
        {
            float handScale = p.Height * 0.06f;

            var wrist = new GameObject(name).transform;
            wrist.SetParent(parent, false);
            wrist.localPosition = new Vector3(side * p.Height * 0.28f, p.Height * 0.5f, p.Height * 0.1f);

            var palm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            palm.name = "Palm";
            palm.transform.SetParent(wrist, false);
            palm.transform.localPosition = new Vector3(0f, 0f, handScale);
            palm.transform.localScale = new Vector3(handScale * 1.6f, handScale * 0.6f, handScale * 2f);
            Paint(palm, p.SkinColor);
            Object.Destroy(palm.GetComponent<Collider>());

            var fingers = new List<Transform>(5);
            for (int f = 0; f < 5; f++)
            {
                var fingerRoot = new GameObject($"Finger{f + 1}").transform;
                fingerRoot.SetParent(wrist, false);
                float fx = (f - 2) * handScale * 0.45f;
                fingerRoot.localPosition = new Vector3(fx, 0f, handScale * 2f);
                fingerRoot.localRotation = Quaternion.Euler(0f, 0f, 0f);

                var finger = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                finger.name = "Mesh";
                finger.transform.SetParent(fingerRoot, false);
                float len = handScale * (1.6f + (f == 0 ? 0.2f : 0f) - Mathf.Abs(f - 2) * 0.12f);
                finger.transform.localPosition = new Vector3(0f, 0f, len * 0.5f);
                finger.transform.localScale = new Vector3(handScale * 0.35f, len * 0.5f, handScale * 0.35f);
                Paint(finger, p.SkinColor);
                Object.Destroy(finger.GetComponent<Collider>());

                fingers.Add(fingerRoot);
            }

            return new HandRig(wrist, palm.transform, fingers);
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

        private static void BuildHair(Transform parent, AvatarPreset p, float headY, float headR)
        {
            var hair = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hair.name = "Hair";
            hair.transform.SetParent(parent, false);
            hair.transform.localPosition = new Vector3(0f, headY + headR * 0.15f, 0f);
            hair.transform.localScale = Vector3.one * (headR * 2.15f);
            Paint(hair, p.HairStyle == 2 ? new Color(0.2f, 0.6f, 0.95f) : new Color(0.15f, 0.12f, 0.1f));

            if (p.HairStyle == 0)
            {
                // 长发：后侧两个拉长球体
                for (int i = 0; i < 2; i++)
                {
                    var strand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    strand.name = "HairStrand";
                    strand.transform.SetParent(parent, false);
                    strand.transform.localPosition = new Vector3((i == 0 ? -1f : 1f) * headR * 0.5f, headY - headR * 0.7f, -headR * 0.2f);
                    strand.transform.localScale = new Vector3(headR * 0.8f, headR * 1.6f, headR * 0.8f);
                    Paint(strand, new Color(0.15f, 0.12f, 0.1f));
                }
            }
            else if (p.HairStyle == 2)
            {
                // 卡通呆毛：头顶一根细长胶囊
                var ahoge = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                ahoge.name = "Ahoge";
                ahoge.transform.SetParent(parent, false);
                ahoge.transform.localPosition = new Vector3(0f, headY + headR * 1.4f, 0f);
                ahoge.transform.localScale = new Vector3(headR * 0.3f, headR * 0.7f, headR * 0.3f);
                ahoge.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
                Paint(ahoge, new Color(0.2f, 0.6f, 0.95f));
            }
        }

        private static void Paint(GameObject go, Color c)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { color = c };
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
