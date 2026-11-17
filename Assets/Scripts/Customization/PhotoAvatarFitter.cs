using System;
using UnityEngine;

namespace PianoTeacher3D.Customization
{
    /// <summary>把照片上手动标出的五官和脸型转为角色脸部外观。</summary>
    public static class PhotoAvatarFitter
    {
        public const int LandmarkCount = 8;
        public const int AtlasSize = 512;
        private static readonly string[] Names =
        {
            "额头中间", "画面左侧脸颊边缘", "画面右侧脸颊边缘", "下巴尖",
            "画面左边眼睛中心", "画面右边眼睛中心", "鼻尖", "嘴唇中间"
        };
        private static readonly int[] Triangles =
        {
            0, 1, 4,   0, 4, 5,   0, 5, 2,   1, 4, 6,   4, 5, 6,
            5, 2, 6,   1, 6, 7,   6, 2, 7,   1, 7, 3,   7, 2, 3
        };

        public static string LandmarkName(int index) => Names[index];

        public static bool TryFit(Texture2D photo, Vector2[] points, bool shortHair,
            out Texture2D atlas, out Vector2 faceScale, out string error)
        {
            atlas = null;
            faceScale = Vector2.one;
            error = null;
            if (photo == null || !photo.isReadable || points == null || points.Length != LandmarkCount)
            {
                error = "请选一张单人正脸照片，并依次标完八个位置。";
                return false;
            }
            foreach (var point in points)
            {
                if (!(point.x > 0f && point.x < 1f && point.y > 0f && point.y < 1f))
                {
                    error = "标记要落在照片内。";
                    return false;
                }
            }

            var dest = Landmarks(shortHair);
            var eyes = points[5] - points[4];
            var aspect = photo.width / (float)photo.height;
            var eyeWidth = eyes.x * aspect;
            var faceWidth = (points[2].x - points[1].x) * aspect;
            var faceHeight = points[3].y - points[0].y;
            if (eyes.x <= 0f || eyes.x * photo.width < 36f || Mathf.Abs(eyes.y) > eyeWidth * 0.25f ||
                faceWidth < 1.4f * eyeWidth || faceWidth > 4f * eyeWidth ||
                faceHeight < eyeWidth || faceHeight > 4.8f * eyeWidth)
            {
                error = "请使用清晰、摆正的正脸。两眼要接近同一高度，脸部在画面里要足够大。";
                return false;
            }

            for (int i = 0; i < Triangles.Length; i += 3)
            {
                int a = Triangles[i], b = Triangles[i + 1], c = Triangles[i + 2];
                float sourceArea = Cross(points[b] - points[a], points[c] - points[a]);
                float targetArea = Cross(dest[b] - dest[a], dest[c] - dest[a]);
                if (Mathf.Abs(sourceArea) < 0.00005f || sourceArea * targetArea <= 0f)
                {
                    error = "五官与脸框的次序不合理。请撤销标记或重选单人正脸。";
                    return false;
                }
            }

            faceScale = new Vector2(Mathf.Clamp(faceWidth / eyeWidth / 2.65f, .88f, 1.12f),
                Mathf.Clamp(faceHeight / eyeWidth / 2.7f, .92f, 1.09f));
            var pixels = new Color32[AtlasSize * AtlasSize];
            float minX = dest[1].x, maxX = dest[2].x, minY = dest[0].y, maxY = dest[3].y;
            for (int y = 0; y < AtlasSize; y++)
            {
                float v = 1f - (y + .5f) / AtlasSize;
                if (v < minY || v > maxY) continue;
                for (int x = 0; x < AtlasSize; x++)
                {
                    float u = (x + .5f) / AtlasSize;
                    if (u < minX || u > maxX) continue;
                    var uv = new Vector2(u, v);
                    if (!TryProject(uv, dest, points, out var sample)) continue;
                    var color = photo.GetPixelBilinear(sample.x, 1f - sample.y);
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(OutlineDistance(uv, dest) / .040f));
                    // 保留模板自身的三维眼球，仅替换眼周皮肤与眉毛。
                    for (int eye = 4; eye <= 5; eye++)
                    {
                        Vector2 delta = uv - dest[eye];
                        float distance = new Vector2(delta.x / .029f, delta.y / .013f).magnitude;
                        alpha *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1.2f, distance));
                    }
                    color.a *= alpha * .93f;
                    pixels[y * AtlasSize + x] = color;
                }
            }

            atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false)
            {
                name = "照片角色脸部贴图",
                wrapMode = TextureWrapMode.Clamp
            };
            atlas.SetPixels32(pixels);
            atlas.Apply();
            return true;
        }

        private static Vector2[] Landmarks(bool shortHair) => new[]
        {
            new Vector2(.5f, shortHair ? .175f : .150f), new Vector2(.294f, .347f),
            new Vector2(.706f, .347f), new Vector2(.5f, .491f),
            new Vector2(.430f, .285f), new Vector2(.570f, .285f),
            new Vector2(.5f, .355f), new Vector2(.5f, .410f)
        };

        private static bool TryProject(Vector2 p, Vector2[] target, Vector2[] source, out Vector2 sample)
        {
            for (int i = 0; i < Triangles.Length; i += 3)
            {
                int a = Triangles[i], b = Triangles[i + 1], c = Triangles[i + 2];
                var toB = target[b] - target[a];
                var toC = target[c] - target[a];
                var relative = p - target[a];
                float area = Cross(toB, toC);
                float s = Cross(relative, toC) / area;
                float t = Cross(toB, relative) / area;
                if (s < 0f || t < 0f || s + t > 1f) continue;
                sample = source[a] * (1f - s - t) + source[b] * s + source[c] * t;
                return true;
            }
            sample = default;
            return false;
        }

        private static float OutlineDistance(Vector2 v, Vector2[] points)
        {
            return Mathf.Min(Mathf.Min(Distance(v, points[0], points[1]), Distance(v, points[1], points[3])),
                Mathf.Min(Distance(v, points[3], points[2]), Distance(v, points[2], points[0])));
        }

        private static float Distance(Vector2 v, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(v - a, edge) / edge.sqrMagnitude);
            return (v - a - edge * t).magnitude;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
