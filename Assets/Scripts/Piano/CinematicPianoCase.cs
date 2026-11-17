using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PianoTeacher3D.Piano
{
    /// <summary>展示舞台使用的低轮廓三角琴外壳。</summary>
    internal sealed class CinematicPianoCase
    {
        private readonly Transform _root;
        private readonly float _width;
        private readonly float _keybed;
        private readonly Func<Mesh, Mesh> _track;
        private readonly Func<string, Color, float, float, Material> _finish;
        private Material _lacquer;
        private Material _trim;
        private Material _base;

        internal CinematicPianoCase(Transform root, float width, float keybed, Func<Mesh, Mesh> track,
            Func<string, Color, float, float, Material> finish)
        {
            _root = root;
            _width = width;
            _keybed = keybed;
            _track = track;
            _finish = finish;
        }

        internal void Build()
        {
            // 保留漆面的介质反射，光束扫过弧面时能读出连续的高光。
            _lacquer = _finish("深潮 · 墨蓝亮漆", new Color(0.035f, 0.105f, 0.156f), 0.18f, 0.88f);
            _trim = _finish("深潮 · 青蓝金属嵌边", new Color(0.08f, 0.37f, 0.44f), 0.37f, 0.79f);
            _base = _finish("深潮 · 亚光深蓝", new Color(0.029f, 0.062f, 0.087f), 0.12f, 0.43f);
            BuildShell();
            BuildKeyboard();
            BuildSupports();
        }

        private void BuildShell()
        {
            var center = new Vector2(_width * 0.43f, -0.60f);
            // 各相邻段的切线连续。键后是平直前缘，尾端偏向低音侧。
            var outline = new[]
            {
                new Vector2(-0.018f, -0.115f),
                new Vector2(_width + 0.016f, -0.115f),
                new Vector2(_width + 0.074f, -0.218f),
                new Vector2(_width * 0.79f, -0.782f),
                new Vector2(_width * 0.36f, -1.429f),
                new Vector2(-0.046f, -0.834f),
                new Vector2(-0.075f, -0.218f)
            };
            var incoming = new[]
            {
                outline[0] + new Vector2(-0.042f, 0f),
                outline[1] + new Vector2(-0.20f, 0f),
                outline[2] + new Vector2(0f, 0.058f),
                outline[3] + new Vector2(0.116f, 0.185f),
                outline[4] + new Vector2(0.25f, 0f),
                outline[5] + new Vector2(0.049f, -0.27f),
                outline[6] + new Vector2(0f, -0.13f)
            };
            var outgoing = new[]
            {
                outline[0] + new Vector2(0.20f, 0f),
                outline[1] + new Vector2(0.042f, 0f),
                outline[2] + new Vector2(0f, -0.24f),
                outline[3] + new Vector2(-0.113f, -0.18f),
                outline[4] + new Vector2(-0.32f, 0f),
                outline[5] + new Vector2(-0.032f, 0.18f),
                outline[6] + new Vector2(0f, 0.06f)
            };
            const int samplesPerSegment = 18;
            var edge = new Vector2[outline.Length * samplesPerSegment];
            for (int i = 0; i < edge.Length; i++)
            {
                int a = i / samplesPerSegment;
                int b = (a + 1) % outline.Length;
                edge[i] = Bezier(outline[a], outgoing[a], incoming[b], outline[b],
                    i % samplesPerSegment / (float)samplesPerSegment);
            }

            Vector3 ShellPoint(int i, Vector2 section)
            {
                var p = Vector2.LerpUnclamped(center, edge[i], section.x);
                float rise = 0.026f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.15f, -1.42f, p.y));
                return new Vector3(p.x, _keybed + section.y + rise, p.y);
            }

            var shell = new LoftMesh();
            var profile = new List<Vector2>
            {
                new(0f, -0.105f), new(0.65f, -0.113f), new(0.90f, -0.108f),
                new(0.949f, -0.096f), new(0.976f, -0.075f), new(0.993f, -0.045f),
                new(1f, -0.009f), new(1f, 0.018f), new(0.997f, 0.037f),
                new(0.990f, 0.054f), new(0.974f, 0.070f), new(0.953f, 0.083f)
            };
            // 椭圆弧封合顶部，边沿和中心都连续过渡。
            for (int i = 1; i <= 13; i++)
            {
                float angle = i * Mathf.PI / 28f;
                profile.Add(new Vector2(0.953f * Mathf.Cos(angle), 0.083f + 0.061f * Mathf.Sin(angle)));
            }
            profile.Add(new Vector2(0f, 0.144f));
            shell.Append(edge.Length, profile, ShellPoint);
            Add("TideShell", shell.Build("深潮琴体"), _lacquer);

            var inlay = new LoftMesh();
            // 高光线沿琴尾连续走一圈，略高出漆面避免深度冲突。
            inlay.Append(edge.Length, new[] { new Vector2(1.0014f, 0.009f), new Vector2(1.0018f, 0.012f) },
                ShellPoint);
            Add("TideInlay", inlay.Build("深潮琴身窄镶边"), _trim, castShadows: false);
        }

        private void BuildKeyboard()
        {
            float mid = _width * 0.5f;
            Box("Keyframe", new Vector3(mid, _keybed - 0.048f, -0.078f),
                new Vector3(_width + 0.148f, 0.086f, 0.284f), 0.015f, _lacquer);
            Box("Fallboard", new Vector3(mid, _keybed + 0.019f, -0.102f),
                new Vector3(_width + 0.067f, 0.048f, 0.043f), 0.009f, _lacquer);
            Box("FrontSlip", new Vector3(mid, _keybed - 0.018f, 0.062f),
                new Vector3(_width + 0.091f, 0.019f, 0.012f), 0.004f, _base);
            foreach (float x in new[] { -0.041f, _width + 0.041f })
                Box("KeyboardCheek", new Vector3(x, _keybed + 0.011f, -0.018f),
                    new Vector3(0.068f, 0.057f, 0.163f), 0.015f, _lacquer);
        }

        private void BuildSupports()
        {
            var legs = new LoftMesh();
            AddLeg(legs,
                new Vector2(-0.014f, -0.12f), new Vector2(0.035f, -0.14f),
                new Vector2(0.084f, -0.18f), new Vector2(0.037f, -0.193f), 1.0f);
            AddLeg(legs,
                new Vector2(_width + 0.016f, -0.12f), new Vector2(_width - 0.028f, -0.14f),
                new Vector2(_width - 0.075f, -0.18f), new Vector2(_width - 0.030f, -0.193f), 1.0f);
            AddLeg(legs,
                new Vector2(_width * 0.33f, -1.168f), new Vector2(_width * 0.34f, -1.08f),
                new Vector2(_width * 0.33f, -1.03f), new Vector2(_width * 0.33f, -1.11f), 0.89f);
            Add("TaperedLegs", legs.Build("深潮三点琴脚"), _lacquer);

            float mid = _width * 0.5f;
            // 沿用三个踏板的实际铰链位置，将支承收在键台后。
            Box("PedalLyreHousing", new Vector3(mid, 0.046f, -0.122f),
                new Vector3(0.237f, 0.062f, 0.100f), 0.013f, _base);
            var supports = new StageMesh();
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 Brace(float t) => new Vector3(
                    mid + sign * Mathf.LerpUnclamped(0.074f, 0.103f, t),
                    Mathf.LerpUnclamped(0.061f, _keybed - 0.068f, t),
                    Mathf.LerpUnclamped(-0.136f, -0.232f, t) - 0.034f * Mathf.Sin(t * Mathf.PI));
                supports.Tube(16, 12, Brace, t => Mathf.Lerp(0.010f, 0.014f, t), _ => Color.white);
            }
            Add("PedalLyreSupports", supports.Build("深潮踏板支架"), _base);
        }

        private void AddLeg(LoftMesh mesh, Vector2 toe, Vector2 low, Vector2 high, Vector2 shoulder, float scale)
        {
            const int rings = 20;
            const int sides = 24;
            var profile = new List<Vector2> { new(0f, 0f) };
            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings;
                profile.Add(new Vector2(scale * Mathf.Lerp(0.020f, 0.062f, t * t), t));
            }
            profile.Add(new Vector2(0f, 1f));
            mesh.Append(sides, profile, (i, section) =>
            {
                float angle = i * Mathf.PI * 2f / sides;
                var point = Bezier(toe, low, high, shoulder, section.y);
                return new Vector3(point.x + section.x * Mathf.Sin(angle),
                    Mathf.Lerp(0.004f, _keybed - 0.057f, section.y),
                    point.y + section.x * 1.28f * Mathf.Cos(angle));
            });
        }

        private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float s = 1f - t;
            return s * s * s * a + 3f * s * s * t * b + 3f * s * t * t * c + t * t * t * d;
        }

        private void Box(string name, Vector3 center, Vector3 size, float bevel, Material material) =>
            Add(name, PianoMeshFactory.CreateChamferedBox(size.x, size.y, size.z, bevel, ChamferMode.AllEdges), material, center);

        private void Add(string name, Mesh mesh, Material material, Vector3 position = default, bool castShadows = true)
        {
            var node = new GameObject(name);
            node.transform.SetParent(_root, false);
            node.transform.localPosition = position;
            node.AddComponent<MeshFilter>().sharedMesh = _track(mesh);
            var surface = node.AddComponent<MeshRenderer>();
            surface.sharedMaterial = material;
            if (!castShadows) surface.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>连接一系列闭合剖面。尺寸为零的剖面只生成一个封口点。</summary>
        private sealed class LoftMesh
        {
            private readonly List<Vector3> _vertices = new();
            private readonly List<int> _triangles = new();

            internal void Append(int sides, IReadOnlyList<Vector2> profile, Func<int, Vector2, Vector3> point)
            {
                int previous = _vertices.Count;
                bool previousPole = profile[0].x == 0f;
                for (int row = 0; row < profile.Count; row++)
                {
                    int current = _vertices.Count;
                    bool pole = profile[row].x == 0f;
                    for (int i = 0; i < (pole ? 1 : sides); i++)
                        _vertices.Add(point(i, profile[row]));

                    if (row > 0)
                    {
                        for (int i = 0; i < sides; i++)
                        {
                            int j = (i + 1) % sides;
                            if (previousPole)
                                Triangle(previous, current + j, current + i);
                            else if (pole)
                                Triangle(previous + i, previous + j, current);
                            else
                            {
                                Triangle(previous + i, previous + j, current + i);
                                Triangle(previous + j, current + j, current + i);
                            }
                        }
                    }

                    previous = current;
                    previousPole = pole;
                }
            }

            private void Triangle(int a, int b, int c)
            {
                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
            }

            internal Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}
