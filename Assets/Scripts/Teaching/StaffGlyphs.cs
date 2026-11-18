using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Teaching
{
    /// <summary>
    /// 五线谱记号的程序化贴图（2026-11-18）：界面字体（思源黑体）不含谱号字形，
    /// 这里用 Catmull-Rom 曲线把高音谱号、低音谱号画成带抗锯齿的白色贴图，着色由 Image 颜色决定。
    /// 坐标单位：五线谱一个线间距 = 1，第一线（最下）y = 0，第五线 y = 4。
    /// </summary>
    public static class StaffGlyphs
    {
        private static Sprite _treble;
        private static Sprite _bass;
        private static Sprite _noteHead;

        /// <summary>高音谱号：贴图覆盖 y ∈ [−2.6, 6.6]、x ∈ [−0.2, 3.4]（线间距单位）。</summary>
        public static Sprite Treble => _treble != null ? _treble : _treble = Build("TrebleClef", new Rect(-0.2f, -2.6f, 3.6f, 9.2f), 0.30f,
            new[]
            {
                new[]
                {
                    // 下端小钩 → 竖干 → 顶端回环 → 斜下穿过 → 绕第二线（G 线）盘旋收尾
                    new Vector2(0.75f, -1.75f), new Vector2(1.15f, -2.15f), new Vector2(1.75f, -1.85f), new Vector2(1.80f, -0.9f),
                    new Vector2(1.62f, 1.5f), new Vector2(1.55f, 3.8f), new Vector2(1.85f, 5.6f), new Vector2(2.35f, 6.15f),
                    new Vector2(2.55f, 5.3f), new Vector2(2.05f, 3.9f), new Vector2(1.05f, 2.6f), new Vector2(0.35f, 1.5f),
                    new Vector2(0.45f, 0.2f), new Vector2(1.45f, -0.45f), new Vector2(2.55f, -0.05f), new Vector2(2.85f, 1.0f),
                    new Vector2(2.25f, 1.85f), new Vector2(1.45f, 1.7f), new Vector2(1.20f, 1.05f), new Vector2(1.55f, 0.65f)
                }
            },
            new[] { (new Vector2(0.78f, -1.62f), 0.36f) });

        /// <summary>低音谱号：贴图覆盖 y ∈ [−0.6, 4.6]、x ∈ [−0.2, 3.6]。</summary>
        public static Sprite Bass => _bass != null ? _bass : _bass = Build("BassClef", new Rect(-0.2f, -0.6f, 3.8f, 5.2f), 0.30f,
            new[]
            {
                new[]
                {
                    new Vector2(0.45f, 3.0f), new Vector2(0.75f, 3.75f), new Vector2(1.55f, 4.05f), new Vector2(2.35f, 3.6f),
                    new Vector2(2.55f, 2.6f), new Vector2(2.05f, 1.35f), new Vector2(1.05f, 0.45f), new Vector2(0.15f, -0.15f)
                }
            },
            new[] { (new Vector2(0.55f, 3.0f), 0.42f), (new Vector2(3.15f, 3.5f), 0.22f), (new Vector2(3.15f, 2.5f), 0.22f) });

        /// <summary>符头：略倾斜的实心椭圆（白色）。</summary>
        public static Sprite NoteHead => _noteHead != null ? _noteHead : _noteHead = BuildEllipse();

        private static Sprite Build(string name, Rect bounds, float thickness, Vector2[][] strokes, (Vector2 c, float r)[] dots)
        {
            const float pxPerUnit = 22f;
            int w = Mathf.CeilToInt(bounds.width * pxPerUnit);
            int h = Mathf.CeilToInt(bounds.height * pxPerUnit);
            var field = new float[w * h];
            for (int i = 0; i < field.Length; i++) field[i] = float.MaxValue;

            var segments = new List<(Vector2 a, Vector2 b, float t)>();
            foreach (var stroke in strokes)
            {
                var pts = CatmullRom(stroke, 12);
                for (int i = 1; i < pts.Count; i++)
                {
                    // 笔画中段粗、两端细，像手写谱号
                    float u = i / (float)(pts.Count - 1);
                    float t = thickness * (0.55f + 0.45f * Mathf.Sin(u * Mathf.PI));
                    segments.Add((pts[i - 1], pts[i], t));
                }
            }

            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(bounds.xMin + (x + 0.5f) / pxPerUnit, bounds.yMin + (y + 0.5f) / pxPerUnit);
                    float d = float.MaxValue;
                    foreach (var s in segments) d = Mathf.Min(d, SegmentDistance(p, s.a, s.b) - s.t * 0.5f);
                    foreach (var dot in dots) d = Mathf.Min(d, Vector2.Distance(p, dot.c) - dot.r);
                    float a = Mathf.Clamp01(0.5f - d * pxPerUnit);
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return MakeSprite(name, w, h, pixels);
        }

        private static Sprite BuildEllipse()
        {
            const int w = 40, h = 30;
            var pixels = new Color32[w * h];
            float angle = -20f * Mathf.Deg2Rad;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float px = (x + 0.5f) / w * 2f - 1f;
                    float py = (y + 0.5f) / h * 2f - 1f;
                    float rx = px * Mathf.Cos(angle) - py * Mathf.Sin(angle);
                    float ry = px * Mathf.Sin(angle) + py * Mathf.Cos(angle);
                    float d = Mathf.Sqrt(rx * rx / 0.80f + ry * ry / 0.52f) - 1f;
                    float a = Mathf.Clamp01(0.5f - d * 14f);
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return MakeSprite("NoteHead", w, h, pixels);
        }

        private static Sprite MakeSprite(string name, int w, int h, Color32[] pixels)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static List<Vector2> CatmullRom(Vector2[] p, int perSegment)
        {
            var result = new List<Vector2>();
            for (int i = 0; i < p.Length - 1; i++)
            {
                Vector2 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, p.Length - 1)];
                for (int k = 0; k < perSegment; k++)
                {
                    float t = k / (float)perSegment;
                    result.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t +
                                       (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
                }
            }
            result.Add(p[p.Length - 1]);
            return result;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
