using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 界面动效的公共部分（2026-11-18）：弹簧积分、「减少动效」开关与动效用的柔光贴图。
    /// <para>
    /// 所有动效都用弹簧而不是固定时长的缓动：中途改目标（悬停 → 按下 → 松开）时速度连续，
    /// 不会出现「从头重播一遍」的生硬感，这是 iOS 动效「跟手」的关键。
    /// 动效只改 transform 与透明度，不改尺寸，不触发布局重建。
    /// </para>
    /// </summary>
    public static class UIMotion
    {
        private const string PrefReduceMotion = "piano.reduceMotion";
        private static bool? _reduced;
        private static Sprite _softDot;
        private static Sprite _ring;

        /// <summary>测试用覆盖：非 null 时直接决定 <see cref="Reduced"/>。</summary>
        public static bool? Override;

        /// <summary>
        /// 「减少动效」：开启后所有界面动效直接跳到终点（与主菜单的同名开关共用一个偏好键）。
        /// 命令行批处理（自动化测试）默认视为开启，界面立即处于终态，布局断言不受动画影响。
        /// </summary>
        public static bool Reduced
        {
            get
            {
                if (Override.HasValue) return Override.Value;
                if (Application.isBatchMode) return true;
                if (!_reduced.HasValue)
                {
                    try { _reduced = PlayerPrefs.GetInt(PrefReduceMotion, 0) == 1; }
                    catch (UnityException) { _reduced = false; }
                }
                return _reduced.Value;
            }
            set => _reduced = value;
        }

        /// <summary>
        /// 阻尼弹簧一步（半隐式欧拉，按 1/120 s 细分，帧率波动时稳定）。
        /// <paramref name="frequency"/> = 固有频率（Hz），<paramref name="damping"/> = 阻尼比（1 无回弹，0.5 轻微回弹）。
        /// </summary>
        public static void Spring(ref float value, ref float velocity, float target, float frequency, float damping, float dt)
        {
            float omega = 2f * Mathf.PI * frequency;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 8);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float accel = omega * omega * (target - value) - 2f * damping * omega * velocity;
                velocity += accel * h;
                value += velocity * h;
            }
        }

        public static void Spring(ref Vector2 value, ref Vector2 velocity, Vector2 target, float frequency, float damping, float dt)
        {
            Spring(ref value.x, ref velocity.x, target.x, frequency, damping, dt);
            Spring(ref value.y, ref velocity.y, target.y, frequency, damping, dt);
        }

        /// <summary>是否已基本静止（位置与速度都足够小）。</summary>
        public static bool Settled(float value, float velocity, float target, float epsilon = 0.0015f)
            => Mathf.Abs(value - target) < epsilon && Mathf.Abs(velocity) < epsilon * 10f;

        /// <summary>柔光圆点（中心白、边缘透明），悬停光斑与按压涟漪共用。</summary>
        public static Sprite SoftDot => _softDot != null ? _softDot : _softDot = CreateRadial("UIMotionSoftDot", false);

        /// <summary>柔和圆环（按压涟漪的外圈）。</summary>
        public static Sprite Ring => _ring != null ? _ring : _ring = CreateRadial("UIMotionRing", true);

        private static Sprite CreateRadial(string name, bool ring)
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = ring
                        ? Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.16f) * (r < 1f ? 1f : 0f)
                        : Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>创建不接收射线的装饰图片（动效层专用）。</summary>
        internal static Image CreateFx(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0f);
            return image;
        }
    }
}
