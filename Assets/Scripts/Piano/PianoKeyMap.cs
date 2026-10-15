using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 维护 MIDI 音高与琴键对象的映射关系（方案 4.2）。
    /// 采用真实钢琴比例（键宽 24mm / 键长 150mm / 键面高 0.72m），
    /// 并程序化生成简化立式琴体（下箱体 / 键盘托板 / 音板 / 顶盖 / 谱架），零外部模型资产。
    /// </summary>
    public class PianoKeyMap : MonoBehaviour
    {
        public const int MinPitch = 21;  // A0
        public const int MaxPitch = 108; // C8
        public const int KeyCount = 88;
        public const int WhiteKeyCount = 52;

        [Header("键体几何（真实钢琴比例，单位：米）")]
        public float WhiteWidth = 0.024f;
        public float WhiteLength = 0.15f;
        public float WhiteHeight = 0.022f;
        public float BlackWidth = 0.014f;
        public float BlackLength = 0.10f;
        public float BlackHeight = 0.032f;

        [Header("键盘高度（地面至键面下沿，立式钢琴约 0.72m）")]
        public float KeybedHeight = 0.72f;

        private readonly Dictionary<int, Transform> _keyRoots = new();
        private Transform _caseRoot;

        /// <summary>pitch → 键根 Transform（pivot 位于键后端，按下绕其 X 轴旋转）。</summary>
        public IReadOnlyDictionary<int, Transform> KeyRoots => _keyRoots;

        /// <summary>钢琴总宽度（52 个白键）。</summary>
        public float TotalWidth => WhiteKeyCount * WhiteWidth;

        /// <summary>键面（键身顶面）的世界 Y 高度，供手部 IK 对齐。</summary>
        public float KeySurfaceY => KeybedHeight + WhiteHeight;

        public static bool IsInRange(int pitch) => pitch >= MinPitch && pitch <= MaxPitch;

        /// <summary>是否为黑键。</summary>
        public static bool IsBlackKey(int pitch)
        {
            int pc = pitch % 12;
            return pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
        }

        public bool TryGetKey(int pitch, out Transform key) => _keyRoots.TryGetValue(pitch, out key);

        /// <summary>程序化生成琴体与 88 键并建立映射。可重复调用（先清空旧对象）。</summary>
        public void BuildKeys()
        {
            ClearKeys();
            BuildCase();

            Material whiteMat = CreateMaterial(new Color(0.96f, 0.96f, 0.94f));
            Material blackMat = CreateMaterial(new Color(0.07f, 0.07f, 0.08f));

            for (int pitch = MinPitch; pitch <= MaxPitch; pitch++)
            {
                bool black = IsBlackKey(pitch);
                float x = black ? (BlackKeyLeftWhiteIndex(pitch) + 0.5f) * WhiteWidth
                                : WhiteIndex(pitch) * WhiteWidth;

                float keyWidth = black ? BlackWidth : WhiteWidth;
                float keyLength = black ? BlackLength : WhiteLength;
                float keyHeight = black ? BlackHeight : WhiteHeight;

                // 键根：位于键后端（-Z），绕 X 轴旋转实现按下
                var root = new GameObject($"Key_{pitch}").transform;
                root.SetParent(transform, false);
                root.localPosition = new Vector3(x, KeybedHeight, -keyLength * 0.5f);

                // 键身：从根向前（+Z）延伸
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.name = "Mesh";
                var meshT = mesh.transform;
                meshT.SetParent(root, false);
                meshT.localPosition = new Vector3(0f, keyHeight * 0.5f, keyLength * 0.5f);
                meshT.localScale = new Vector3(keyWidth, keyHeight, keyLength);
                var keyMat = black ? blackMat : whiteMat;
                if (keyMat != null) mesh.GetComponent<Renderer>().sharedMaterial = keyMat;
                Destroy(mesh.GetComponent<Collider>()); // 无碰撞需求，避免误触

                _keyRoots[pitch] = root;
            }
        }

        public void ClearKeys()
        {
            foreach (var kv in _keyRoots)
            {
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            }
            _keyRoots.Clear();

            if (_caseRoot != null) Destroy(_caseRoot.gameObject);
            _caseRoot = null;
        }

        // ==================== 简化立式琴体 ====================

        private void BuildCase()
        {
            var c = new GameObject("PianoCase").transform;
            c.SetParent(transform, false);
            _caseRoot = c;

            var wood = CreateMaterial(new Color(0.225f, 0.152f, 0.110f));   // 胡桃木
            var woodDark = CreateMaterial(new Color(0.165f, 0.108f, 0.082f));
            var felt = CreateMaterial(new Color(0.72f, 0.14f, 0.14f));      // 键盘后挡红绒

            float w = TotalWidth;
            float mid = w * 0.5f;

            // 键盘托板（键下方，稍微前凸）
            CreateCasePart(c, "Keybed", new Vector3(mid, KeybedHeight - 0.02f, 0.02f),
                new Vector3(w + 0.05f, 0.04f, 0.21f), wood);

            // 键后挡绒（遮挡键尾，经典红绒）
            CreateCasePart(c, "BackFelt", new Vector3(mid, KeybedHeight + 0.012f, -WhiteLength * 0.5f - 0.006f),
                new Vector3(w + 0.05f, 0.030f, 0.012f), felt);

            // 下箱体（键盘下方落地箱）
            CreateCasePart(c, "LowerCase", new Vector3(mid, (KeybedHeight - 0.04f) * 0.5f, -0.15f),
                new Vector3(w + 0.05f, KeybedHeight - 0.04f, 0.30f), woodDark);

            // 上箱体（音板，键盘后方立式箱体）
            CreateCasePart(c, "SoundBoard", new Vector3(mid, 1.08f, -0.17f),
                new Vector3(w + 0.05f, 0.68f, 0.26f), wood);

            // 顶盖
            CreateCasePart(c, "Lid", new Vector3(mid, 1.435f, -0.17f),
                new Vector3(w + 0.09f, 0.03f, 0.30f), woodDark);

            // 谱架（斜放）
            var stand = CreateCasePart(c, "MusicStand", new Vector3(mid, 1.20f, -0.005f),
                new Vector3(w * 0.62f, 0.30f, 0.015f), woodDark);
            stand.localRotation = Quaternion.Euler(-14f, 0f, 0f);

            // 琴腿（键盘两侧）
            CreateCasePart(c, "Leg_L", new Vector3(0.01f, (KeybedHeight - 0.04f) * 0.5f, -0.02f),
                new Vector3(0.045f, KeybedHeight - 0.04f, 0.05f), woodDark);
            CreateCasePart(c, "Leg_R", new Vector3(w - 0.01f, (KeybedHeight - 0.04f) * 0.5f, -0.02f),
                new Vector3(0.045f, KeybedHeight - 0.04f, 0.05f), woodDark);
        }

        private static Transform CreateCasePart(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.Destroy(go.GetComponent<Collider>());
            return t;
        }

        // ==================== 白键索引计算（从 A0 起，每八度 7 个白键） ====================

        // 八度内半音偏移 → 累计白键数（A,A#,B,C,C#,D,D#,E,F,F#,G,G#）
        private static readonly int[] WhiteCountUpTo = { 1, 1, 2, 3, 3, 4, 4, 5, 6, 6, 7, 7 };

        private static int WhiteIndex(int pitch)
        {
            int n = pitch - MinPitch;
            int oct = n / 12;
            int off = n % 12;
            return oct * 7 + WhiteCountUpTo[off] - 1;
        }

        private static int BlackKeyLeftWhiteIndex(int pitch)
        {
            int n = pitch - MinPitch;
            int oct = n / 12;
            int off = n % 12;
            int leftOff = off switch
            {
                1 => 0,   // A# 在 A 与 B 之间
                4 => 3,   // C# 在 C 与 D 之间
                6 => 5,   // D# 在 D 与 E 之间
                9 => 8,   // F# 在 F 与 G 之间
                11 => 10, // G# 在 G 与 A 之间
                _ => 0
            };
            return oct * 7 + WhiteCountUpTo[leftOff] - 1;
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[PianoKeyMap] 未找到可用 shader，琴键材质创建失败");
                return null;
            }
            return new Material(shader)
            {
                name = $"KeyMat_{color}",
                color = color
            };
        }
    }
}
