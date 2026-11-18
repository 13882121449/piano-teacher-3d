using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 沿路径扫掠截面生成网格（竖琴钢琴的琴颈、立柱、弧梁、涡卷腿都由它生成）。
    /// <para>
    /// 标架用「旋转最小化」双反射法（Wang et al. 2008）逐段传递，长曲线上截面不会自转；
    /// 截面是闭合的二维轮廓（x 沿标架法向 N，y 沿副法向 B = T × N），首尾环共享顶点所以没有法线接缝。
    /// 每段扫掠按首个三角形的几何法线自动校正绕序，调用方不用关心左右手坐标。
    /// </para>
    /// </summary>
    internal sealed class SweepMeshBuilder
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<int> _triangles = new();

        public int VertexCount => _vertices.Count;

        /// <summary>扫掠一段。<paramref name="scale"/> 输入沿路径的归一化弧长 0–1，输出截面缩放。</summary>
        public void Sweep(IReadOnlyList<Vector3> path, IReadOnlyList<Vector2> profile, Func<float, float> scale,
            Vector3 upHint, bool capStart = true, bool capEnd = true)
        {
            int n = path.Count;
            int m = profile.Count;
            if (n < 2 || m < 3) return;

            var tangents = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 d = path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)];
                tangents[i] = d.sqrMagnitude > 1e-14f ? d.normalized : (i > 0 ? tangents[i - 1] : Vector3.forward);
            }

            var normals = new Vector3[n];
            Vector3 n0 = Vector3.ProjectOnPlane(upHint, tangents[0]);
            if (n0.sqrMagnitude < 1e-8f) n0 = Vector3.ProjectOnPlane(Vector3.right, tangents[0]);
            if (n0.sqrMagnitude < 1e-8f) n0 = Vector3.ProjectOnPlane(Vector3.up, tangents[0]);
            normals[0] = n0.normalized;
            for (int i = 1; i < n; i++)
            {
                Vector3 v1 = path[i] - path[i - 1];
                float c1 = Vector3.Dot(v1, v1);
                if (c1 < 1e-14f)
                {
                    normals[i] = normals[i - 1];
                    continue;
                }
                Vector3 rL = normals[i - 1] - (2f / c1) * Vector3.Dot(v1, normals[i - 1]) * v1;
                Vector3 tL = tangents[i - 1] - (2f / c1) * Vector3.Dot(v1, tangents[i - 1]) * v1;
                Vector3 v2 = tangents[i] - tL;
                float c2 = Vector3.Dot(v2, v2);
                normals[i] = (c2 < 1e-14f ? rL : rL - (2f / c2) * Vector3.Dot(v2, rL) * v2).normalized;
            }

            // 截面法线：闭合轮廓逐点用相邻点差分，逆时针轮廓的外法线是 (dy, −dx)
            var profileNormals = new Vector2[m];
            float area = 0f;
            for (int j = 0; j < m; j++)
            {
                Vector2 a = profile[j];
                Vector2 b = profile[(j + 1) % m];
                area += a.x * b.y - b.x * a.y;
            }
            float orientation = area >= 0f ? 1f : -1f;
            for (int j = 0; j < m; j++)
            {
                Vector2 d = profile[(j + 1) % m] - profile[(j - 1 + m) % m];
                profileNormals[j] = new Vector2(d.y, -d.x).normalized * orientation;
            }

            var lengths = new float[n];
            for (int i = 1; i < n; i++) lengths[i] = lengths[i - 1] + Vector3.Distance(path[i], path[i - 1]);
            float total = Mathf.Max(lengths[n - 1], 1e-6f);

            int start = _vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 bin = Vector3.Cross(tangents[i], normals[i]).normalized;
                float u = lengths[i] / total;
                float s = scale != null ? scale(u) : 1f;
                for (int j = 0; j < m; j++)
                {
                    Vector2 p = profile[j] * s;
                    _vertices.Add(path[i] + normals[i] * p.x + bin * p.y);
                    Vector2 pn = profileNormals[j];
                    _normals.Add((normals[i] * pn.x + bin * pn.y).normalized);
                    _uvs.Add(new Vector2((float)j / m, lengths[i]));
                }
            }

            int triStart = _triangles.Count;
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    int a = start + i * m + j;
                    int b = start + i * m + (j + 1) % m;
                    int c = a + m;
                    int d = b + m;
                    _triangles.Add(a); _triangles.Add(c); _triangles.Add(b);
                    _triangles.Add(b); _triangles.Add(c); _triangles.Add(d);
                }
            }
            OrientRange(triStart);

            if (capStart) AddCap(start, m, path[0], -tangents[0]);
            if (capEnd) AddCap(start + (n - 1) * m, m, path[n - 1], tangents[n - 1]);
        }

        /// <summary>追加一个现成网格（例如珠饰小球），按矩阵变换。</summary>
        public void Append(Mesh mesh, Matrix4x4 matrix)
        {
            if (mesh == null) return;
            int start = _vertices.Count;
            var v = mesh.vertices;
            var nrm = mesh.normals;
            for (int i = 0; i < v.Length; i++)
            {
                _vertices.Add(matrix.MultiplyPoint3x4(v[i]));
                _normals.Add(nrm.Length == v.Length ? matrix.MultiplyVector(nrm[i]).normalized : Vector3.up);
                _uvs.Add(Vector2.zero);
            }
            foreach (int t in mesh.triangles) _triangles.Add(start + t);
        }

        /// <summary>加一个圆角盒（12 段倒角近似成扫掠圆角矩形，两端平封）。</summary>
        public void RoundedBox(Vector3 center, Vector3 size, float radius)
        {
            float r = Mathf.Min(radius, Mathf.Min(size.y, size.z) * 0.49f);
            var profile = Profiles.RoundedRect(size.z, size.y, r, 4);
            var path = new[]
            {
                center + new Vector3(-size.x * 0.5f, 0f, 0f),
                center + new Vector3(size.x * 0.5f, 0f, 0f)
            };
            // 截面 x 轴取世界 +Z（深度），y 轴即副法向（高度）
            Sweep(path, profile, null, Vector3.forward);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private void AddCap(int ringStart, int m, Vector3 center, Vector3 outward)
        {
            int c = _vertices.Count;
            _vertices.Add(center);
            _normals.Add(outward);
            _uvs.Add(Vector2.zero);
            int first = _vertices.Count;
            for (int j = 0; j < m; j++)
            {
                _vertices.Add(_vertices[ringStart + j]);
                _normals.Add(outward);
                _uvs.Add(Vector2.zero);
            }
            int triStart = _triangles.Count;
            for (int j = 0; j < m; j++)
            {
                _triangles.Add(c);
                _triangles.Add(first + j);
                _triangles.Add(first + (j + 1) % m);
            }
            OrientRange(triStart);
        }

        /// <summary>按首个非退化三角形的几何法线与顶点法线对比，反了就整段翻转绕序。</summary>
        private void OrientRange(int triStart)
        {
            for (int t = triStart; t + 2 < _triangles.Count; t += 3)
            {
                Vector3 a = _vertices[_triangles[t]];
                Vector3 b = _vertices[_triangles[t + 1]];
                Vector3 c = _vertices[_triangles[t + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-16f) continue;
                Vector3 vn = _normals[_triangles[t]] + _normals[_triangles[t + 1]] + _normals[_triangles[t + 2]];
                if (Vector3.Dot(face, vn) >= 0f) return;
                for (int k = triStart; k + 2 < _triangles.Count; k += 3)
                    (_triangles[k + 1], _triangles[k + 2]) = (_triangles[k + 2], _triangles[k + 1]);
                return;
            }
        }

        /// <summary>常用截面与路径。</summary>
        internal static class Profiles
        {
            /// <summary>圆角矩形（逆时针），每个圆角 <paramref name="segments"/> 段。</summary>
            public static Vector2[] RoundedRect(float width, float height, float radius, int segments)
            {
                float hw = width * 0.5f, hh = height * 0.5f;
                float r = Mathf.Clamp(radius, 0.0001f, Mathf.Min(hw, hh));
                var points = new List<Vector2>();
                var centers = new[]
                {
                    new Vector2(hw - r, hh - r), new Vector2(-hw + r, hh - r),
                    new Vector2(-hw + r, -hh + r), new Vector2(hw - r, -hh + r)
                };
                for (int c = 0; c < 4; c++)
                {
                    float a0 = c * Mathf.PI * 0.5f;
                    for (int s = 0; s <= segments; s++)
                    {
                        float a = a0 + Mathf.PI * 0.5f * s / segments;
                        points.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                    }
                }
                return points.ToArray();
            }

            /// <summary>
            /// 带凹槽的线脚截面：圆角矩形的两条长边（±x 面）各刻 <paramref name="grooves"/> 道浅槽，
            /// 近看像柱身与琴颈上的「平行刻线」。
            /// </summary>
            public static Vector2[] Fluted(float width, float height, float radius, int grooves, float depth, int samples = 96)
            {
                var basePoints = RoundedRect(width, height, radius, 6);
                var resampled = Resample(basePoints, samples);
                float hw = width * 0.5f, hh = height * 0.5f;
                for (int i = 0; i < resampled.Length; i++)
                {
                    Vector2 p = resampled[i];
                    if (Mathf.Abs(Mathf.Abs(p.x) - hw) > 1e-4f) continue;
                    float t = Mathf.InverseLerp(-hh + radius, hh - radius, p.y);
                    if (t <= 0f || t >= 1f) continue;
                    float g = Mathf.Pow(Mathf.Abs(Mathf.Sin(t * Mathf.PI * grooves)), 6f);
                    p.x -= Mathf.Sign(p.x) * depth * g;
                    resampled[i] = p;
                }
                return resampled;
            }

            public static Vector2[] Circle(float radius, int segments)
            {
                var points = new Vector2[segments];
                for (int i = 0; i < segments; i++)
                {
                    float a = Mathf.PI * 2f * i / segments;
                    points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                }
                return points;
            }

            /// <summary>闭合折线按周长等距重采样。</summary>
            public static Vector2[] Resample(IReadOnlyList<Vector2> loop, int count)
            {
                int n = loop.Count;
                var cumulative = new float[n + 1];
                for (int i = 0; i < n; i++) cumulative[i + 1] = cumulative[i] + Vector2.Distance(loop[i], loop[(i + 1) % n]);
                float total = cumulative[n];
                var result = new Vector2[count];
                int seg = 0;
                for (int k = 0; k < count; k++)
                {
                    float target = total * k / count;
                    while (seg < n - 1 && cumulative[seg + 1] < target) seg++;
                    float len = cumulative[seg + 1] - cumulative[seg];
                    float t = len > 1e-9f ? (target - cumulative[seg]) / len : 0f;
                    result[k] = Vector2.Lerp(loop[seg], loop[(seg + 1) % n], t);
                }
                return result;
            }
        }

        internal static class Paths
        {
            /// <summary>三次贝塞尔链（每段 4 个控制点，相邻段共享端点）按弧长等距采样。</summary>
            public static List<Vector3> Bezier(IReadOnlyList<Vector3> controls, float spacing)
            {
                var dense = new List<Vector3>();
                for (int s = 0; s + 3 < controls.Count; s += 3)
                {
                    for (int i = 0; i <= 64; i++)
                    {
                        if (s > 0 && i == 0) continue;
                        float t = i / 64f;
                        float u = 1f - t;
                        dense.Add(u * u * u * controls[s] + 3f * u * u * t * controls[s + 1] +
                                  3f * u * t * t * controls[s + 2] + t * t * t * controls[s + 3]);
                    }
                }
                return ResampleOpen(dense, spacing);
            }

            /// <summary>
            /// 平面对数螺线：中心 <paramref name="center"/>，平面由 <paramref name="axisU"/> / <paramref name="axisV"/> 张成，
            /// 角度从 <paramref name="startAngle"/> 起转过 <paramref name="sweep"/> 弧度，半径 r0·e^(−kθ)。
            /// </summary>
            public static List<Vector3> Spiral(Vector3 center, Vector3 axisU, Vector3 axisV, float startAngle, float sweep,
                float r0, float k, float spacing)
            {
                var dense = new List<Vector3>();
                for (int i = 0; i <= 256; i++)
                {
                    float d = sweep * i / 256f;
                    float a = startAngle + d;
                    float r = r0 * Mathf.Exp(-k * Mathf.Abs(d));
                    dense.Add(center + (axisU * Mathf.Cos(a) + axisV * Mathf.Sin(a)) * r);
                }
                return ResampleOpen(dense, spacing);
            }

            public static List<Vector3> ResampleOpen(IReadOnlyList<Vector3> dense, float spacing)
            {
                var result = new List<Vector3> { dense[0] };
                float carried = 0f;
                for (int i = 1; i < dense.Count; i++)
                {
                    Vector3 a = dense[i - 1];
                    Vector3 b = dense[i];
                    float len = Vector3.Distance(a, b);
                    float pos = spacing - carried;
                    while (pos <= len)
                    {
                        result.Add(Vector3.Lerp(a, b, pos / len));
                        pos += spacing;
                    }
                    carried = len - (pos - spacing);
                }
                if (Vector3.Distance(result[result.Count - 1], dense[dense.Count - 1]) > spacing * 0.25f)
                    result.Add(dense[dense.Count - 1]);
                else
                    result[result.Count - 1] = dense[dense.Count - 1];
                return result;
            }
        }
    }
}
