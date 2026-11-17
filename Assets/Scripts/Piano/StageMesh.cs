using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PianoTeacher3D.Piano
{
    /// <summary>供舞台组装使用的曲面网格。按材质合并景物，并保留顶点颜色及局部变形坐标。</summary>
    internal sealed class StageMesh
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Color> _colors = new();
        private readonly List<Vector2> _motion = new();
        private readonly List<int> _triangles = new();

        internal void Surface(int rows, int columns, Func<float, float, Vector3> point,
            Func<float, float, Color> color, Func<float, float, Vector2> motion = null, bool wrap = false)
        {
            int start = _vertices.Count;
            int stride = wrap ? columns : columns + 1;
            for (int y = 0; y <= rows; y++)
            {
                float v = y / (float)rows;
                for (int x = 0; x < stride; x++)
                {
                    float u = x / (float)columns;
                    _vertices.Add(point(u, v));
                    _colors.Add(color(u, v));
                    _motion.Add(motion == null ? Vector2.zero : motion(u, v));
                    if (y == rows || (!wrap && x == columns)) continue;
                    int a = start + y * stride + x;
                    int b = start + y * stride + (x + 1) % stride;
                    _triangles.Add(a);
                    _triangles.Add(b);
                    _triangles.Add(a + stride);
                    _triangles.Add(b);
                    _triangles.Add(b + stride);
                    _triangles.Add(a + stride);
                }
            }
        }

        internal void Tube(int length, int sides, Func<float, Vector3> path,
            Func<float, float> radius, Func<float, Color> color, Func<float, Vector2> motion = null)
        {
            Surface(length, sides, (u, v) =>
            {
                Vector3 direction = (path(v + 0.001f) - path(v - 0.001f)).normalized;
                Vector3 normal = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.93f ? Vector3.forward : Vector3.up).normalized;
                Vector3 binormal = Vector3.Cross(direction, normal);
                float angle = u * Mathf.PI * 2f;
                return path(v) + radius(v) * (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle));
            }, (_, v) => color(v), motion == null ? null : (_, v) => motion(v), wrap: true);
        }

        internal void Ellipsoid(Vector3 center, Vector3 size, Quaternion rotation, Color baseColor,
            int rings, int sides, float uneven = 0f, float seed = 0f, Vector2 motion = default)
        {
            Surface(rings, sides, (u, v) =>
            {
                float angle = u * Mathf.PI * 2f;
                float tilt = Mathf.Lerp(-1.568f, 1.568f, v);
                var p = new Vector3(Mathf.Cos(angle) * Mathf.Cos(tilt), Mathf.Sin(tilt), Mathf.Sin(angle) * Mathf.Cos(tilt));
                float noise = Mathf.PerlinNoise(seed + p.x * 2.7f + p.y, 5f + p.z * 2.7f);
                return center + rotation * Vector3.Scale(size, p * (1f + uneven * (2f * noise - 1f)));
            }, (u, v) => baseColor * Mathf.Lerp(0.50f, 1.10f, v), (_, _) => motion, wrap: true);
        }

        internal Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetUVs(0, _motion);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (_motion.Exists(value => value.y > 0f))
            {
                var bounds = mesh.bounds;
                bounds.Expand(1.35f);
                mesh.bounds = bounds;
            }
            return mesh;
        }
    }
}
