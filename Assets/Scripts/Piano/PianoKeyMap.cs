using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 维护 MIDI 音高与琴键对象的映射关系（方案 4.2）。
    /// MVP 采用程序化生成的 88 键（比例准确的白键/黑键），零外部模型资产。
    /// </summary>
    public class PianoKeyMap : MonoBehaviour
    {
        public const int MinPitch = 21;  // A0
        public const int MaxPitch = 108; // C8
        public const int KeyCount = 88;

        [Header("键体几何（单位：米）")]
        public float WhiteWidth = 1.0f;
        public float WhiteLength = 5.0f;
        public float WhiteHeight = 0.12f;
        public float BlackWidth = 0.6f;
        public float BlackLength = 3.0f;
        public float BlackHeight = 0.3f;

        private readonly Dictionary<int, Transform> _keyRoots = new();

        /// <summary>pitch → 键根 Transform（pivot 位于键后端，按下绕其 X 轴旋转）。</summary>
        public IReadOnlyDictionary<int, Transform> KeyRoots => _keyRoots;

        public static bool IsInRange(int pitch) => pitch >= MinPitch && pitch <= MaxPitch;

        /// <summary>是否为黑键。</summary>
        public static bool IsBlackKey(int pitch)
        {
            int pc = pitch % 12;
            return pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
        }

        public bool TryGetKey(int pitch, out Transform key) => _keyRoots.TryGetValue(pitch, out key);

        /// <summary>程序化生成 88 键并建立映射。可重复调用（先清空旧键）。</summary>
        public void BuildKeys()
        {
            ClearKeys();

            Material whiteMat = CreateMaterial(new Color(0.95f, 0.95f, 0.95f));
            Material blackMat = CreateMaterial(new Color(0.08f, 0.08f, 0.08f));

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
                root.localPosition = new Vector3(x, 0f, -keyLength * 0.5f);

                // 键身：从根向前（+Z）延伸
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.name = "Mesh";
                var meshT = mesh.transform;
                meshT.SetParent(root, false);
                meshT.localPosition = new Vector3(0f, keyHeight * 0.5f, keyLength * 0.5f);
                meshT.localScale = new Vector3(keyWidth, keyHeight, keyLength);
                mesh.GetComponent<Renderer>().sharedMaterial = black ? blackMat : whiteMat;
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
        }

        // ---- 白键索引计算（从 A0 起，每 8 度 7 个白键） ----

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
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader)
            {
                name = $"KeyMat_{color}",
                color = color
            };
            return mat;
        }
    }
}
