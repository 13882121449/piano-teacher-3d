using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Profiles = PianoTeacher3D.Piano.SweepMeshBuilder.Profiles;
using Paths = PianoTeacher3D.Piano.SweepMeshBuilder.Paths;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 竖琴钢琴外壳（2026-11-18，按用户给的参考图）：键盘后方立起一面外露琴弦的竖琴，
    /// 低音端一根带刻槽的立柱，顶部一条双轨琴颈弯向高音端，琴颈下一排珍珠弦钉；
    /// 弦面前方一道细弧梁斜穿琴弦；两端琴颊带涡卷装饰，四条 C 形涡卷腿落地。
    /// <para>
    /// 坐标与 <see cref="PianoKeyMap"/> 本地坐标一致：键盘 x ∈ [0, 宽度]，低音在 +x（演奏者左手侧），
    /// 白键 z ∈ [−0.075, 0.075]，演奏者在 +z。立柱放在低音端：最长的弦在低音侧，符合乐器结构。
    /// </para>
    /// <para>
    /// 88 根弦与 88 个音一一对应（低音在 +x），合并成一个网格，顶点 uv.x 存弦号、uv.y 存沿弦位置，
    /// 由 <see cref="HarpStringVibration"/> 每帧写入振幅，着色器做横向振动和发光。
    /// </para>
    /// </summary>
    internal sealed class HarpPianoCase
    {
        /// <summary>琴弦所在平面（本地 z）。</summary>
        internal const float StringPlaneZ = -0.112f;

        /// <summary>琴弦下端（挂弦珠）高度。</summary>
        internal const float StringBottomY = 0.817f;

        private readonly Transform _root;
        private readonly float _w;
        private readonly float _keybed;
        private readonly Func<Mesh, Mesh> _track;
        private readonly Func<string, Color, float, float, Material> _finish;
        private readonly List<Material> _ownedMaterials;
        private Material _lacquer;
        private Material _pearl;

        internal HarpPianoCase(Transform root, float width, float keybed, Func<Mesh, Mesh> track,
            Func<string, Color, float, float, Material> finish, List<Material> ownedMaterials)
        {
            _root = root;
            _w = width;
            _keybed = keybed;
            _track = track;
            _finish = finish;
            _ownedMaterials = ownedMaterials;
        }

        /// <summary>琴弦网格所在节点（振动组件挂在这里）。</summary>
        internal Transform Strings { get; private set; }

        internal void Build()
        {
            _lacquer = PianoSharedMaterials.Get("HarpLacquer")
                       ?? _finish("竖琴钢琴 · 深海蓝漆", new Color(0.03f, 0.10f, 0.22f), 0.70f, 0.88f);
            _pearl = PianoSharedMaterials.Get("HarpPearl")
                     ?? _finish("竖琴钢琴 · 珍珠银", new Color(0.82f, 0.85f, 0.88f), 1.0f, 0.85f);

            var body = new SweepMeshBuilder();
            var beads = new SweepMeshBuilder();
            BuildKeyboardFrame(body, beads);
            BuildEndBlock(body, -1f);
            BuildEndBlock(body, 1f);
            BuildColumn(body);
            var stringTops = BuildNeck(body, beads);
            BuildArch(body);
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                BuildLeg(body, sx, sz);

            Add("HarpBody", _track(body.ToMesh("HarpBody")), _lacquer, true);
            Add("HarpPearls", _track(beads.ToMesh("HarpPearls")), _pearl, true);
            BuildStrings(stringTops);
        }

        // ==================== 键盘框 ====================

        private void BuildKeyboardFrame(SweepMeshBuilder body, SweepMeshBuilder beads)
        {
            float mid = _w * 0.5f;
            // 键床：键底下的整块台面，两端略出键盘
            body.RoundedBox(new Vector3(mid, _keybed - 0.0325f, -0.0065f), new Vector3(_w + 0.04f, 0.065f, 0.177f), 0.008f);
            // 前沿线脚（键前挡条）：两道刻线
            body.Sweep(Line(new Vector3(-0.03f, _keybed - 0.022f, 0.089f), new Vector3(_w + 0.03f, _keybed - 0.022f, 0.089f)),
                Profiles.Fluted(0.016f, 0.062f, 0.006f, 2, 0.0018f), null, Vector3.forward);
            // 键床底下的裙边
            body.Sweep(Line(new Vector3(-0.02f, _keybed - 0.075f, 0.072f), new Vector3(_w + 0.02f, _keybed - 0.075f, 0.072f)),
                Profiles.RoundedRect(0.03f, 0.024f, 0.010f, 4), null, Vector3.forward);
            // 键后的挂弦横梁 + 顶部线脚
            body.RoundedBox(new Vector3(mid, _keybed + 0.045f, StringPlaneZ), new Vector3(_w + 0.04f, 0.09f, 0.05f), 0.010f);
            body.Sweep(Line(new Vector3(-0.02f, _keybed + 0.093f, StringPlaneZ), new Vector3(_w + 0.02f, _keybed + 0.093f, StringPlaneZ)),
                Profiles.RoundedRect(0.062f, 0.010f, 0.0045f, 3), null, Vector3.forward);

            // 挂弦珠：每根弦一颗，落在横梁顶面
            var sphere = SphereMesh(8, 6);
            for (int i = 0; i < PianoKeyMap.KeyCount; i++)
            {
                float x = StringX(i);
                beads.Append(sphere, Matrix4x4.TRS(new Vector3(x, StringBottomY, StringPlaneZ + 0.004f),
                    Quaternion.identity, Vector3.one * 0.0105f));
            }
            Release(sphere);
        }

        // ==================== 两端琴颊与涡卷 ====================

        private void BuildEndBlock(SweepMeshBuilder body, float sx)
        {
            // 琴颊收窄成「立柱底座」的比例：高 0.17、深 0.21，主体靠涡卷撑出装饰感
            float xc = sx > 0f ? _w + 0.095f : -0.095f;
            body.RoundedBox(new Vector3(xc, 0.735f, -0.02f), new Vector3(0.12f, 0.17f, 0.21f), 0.035f);
            body.RoundedBox(new Vector3(xc, 0.83f, -0.02f), new Vector3(0.14f, 0.02f, 0.23f), 0.009f);
            body.RoundedBox(new Vector3(xc, 0.643f, -0.02f), new Vector3(0.135f, 0.02f, 0.225f), 0.009f);

            // 正面涡卷（面向演奏者，螺线在 xy 平面）：外圈从顶部起向外、向下卷入
            var front = Paths.Spiral(new Vector3(xc + sx * 0.008f, 0.738f, 0.101f), new Vector3(sx, 0f, 0f), Vector3.up,
                Mathf.PI * 0.5f, -sx * Mathf.PI * 2.1f, 0.056f, 0.20f, 0.005f);
            body.Sweep(front, Profiles.RoundedRect(0.030f, 0.024f, 0.010f, 4), u => Mathf.Lerp(1f, 0.42f, u), Vector3.forward);

            // 外侧涡卷（侧面看是一对上下相背的螺线，螺线在 yz 平面）
            float xo = xc + sx * 0.074f;
            var upper = Paths.Spiral(new Vector3(xo, 0.772f, 0.005f), Vector3.forward, Vector3.up,
                -Mathf.PI * 0.5f, Mathf.PI * 1.9f, 0.048f, 0.22f, 0.005f);
            body.Sweep(upper, Profiles.RoundedRect(0.028f, 0.022f, 0.009f, 4), u => Mathf.Lerp(1f, 0.45f, u), Vector3.right);
            var lower = Paths.Spiral(new Vector3(xo, 0.690f, -0.060f), Vector3.forward, Vector3.up,
                Mathf.PI * 0.5f, Mathf.PI * 1.9f, 0.042f, 0.22f, 0.005f);
            body.Sweep(lower, Profiles.RoundedRect(0.028f, 0.022f, 0.009f, 4), u => Mathf.Lerp(1f, 0.45f, u), Vector3.right);
        }

        // ==================== 低音立柱 ====================

        private void BuildColumn(SweepMeshBuilder body)
        {
            float xc = _w + 0.095f;
            body.RoundedBox(new Vector3(xc, 0.885f, StringPlaneZ), new Vector3(0.145f, 0.042f, 0.15f), 0.013f);
            body.Sweep(Line(new Vector3(xc, 0.90f, StringPlaneZ), new Vector3(xc, 1.69f, StringPlaneZ)),
                Profiles.Fluted(0.112f, 0.10f, 0.022f, 3, 0.009f), null, Vector3.forward);
            body.RoundedBox(new Vector3(xc, 1.705f, StringPlaneZ), new Vector3(0.150f, 0.040f, 0.155f), 0.014f);
            body.RoundedBox(new Vector3(xc + 0.006f, 1.755f, StringPlaneZ), new Vector3(0.128f, 0.06f, 0.132f), 0.026f);
            // 柱头正面的小涡卷
            var scroll = Paths.Spiral(new Vector3(xc + 0.02f, 1.775f, StringPlaneZ + 0.072f), Vector3.right, Vector3.up,
                Mathf.PI, -Mathf.PI * 2.0f, 0.040f, 0.25f, 0.005f);
            body.Sweep(scroll, Profiles.RoundedRect(0.026f, 0.020f, 0.008f, 4), u => Mathf.Lerp(1f, 0.45f, u), Vector3.forward);
        }

        // ==================== 琴颈（双轨 + 珍珠） ====================

        /// <summary>生成琴颈，返回「弦顶」折线（x 单调递减），供琴弦取顶端高度。</summary>
        private List<Vector3> BuildNeck(SweepMeshBuilder body, SweepMeshBuilder beads)
        {
            float z = StringPlaneZ;
            var controls = new[]
            {
                new Vector3(_w + 0.075f, 1.765f, z), new Vector3(_w - 0.10f, 1.875f, z),
                new Vector3(_w * 0.58f, 1.69f, z), new Vector3(_w * 0.33f, 1.37f, z),
                new Vector3(_w * 0.33f - 0.156f, 1.21f, z), new Vector3(0.035f, 1.005f, z),
                new Vector3(-0.095f, 0.868f, z)
            };
            var path = Paths.Bezier(controls, 0.010f);
            Func<float, float> taper = u => Mathf.Lerp(1f, 0.72f, u);
            body.Sweep(path, Profiles.Fluted(0.078f, 0.072f, 0.019f, 2, 0.0065f), taper, Vector3.forward);

            var inner = new List<Vector3>(path.Count);
            var tops = new List<Vector3>(path.Count);
            var pearls = new List<Vector3>();
            float travelled = 0f;
            float nextPearl = 0.02f;
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i], path[i - 1]);
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) travelled += Vector3.Distance(path[i], path[i - 1]);
                Vector3 t = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 up = new Vector3(t.y, -t.x, 0f).normalized;   // 曲线在 xy 平面内的「上侧」法向
                float s = taper(travelled / total);
                inner.Add(path[i] - up * 0.066f * s);
                tops.Add(path[i] - up * (0.066f + 0.019f) * s);
                if (travelled >= nextPearl && i < path.Count - 2)
                {
                    pearls.Add(path[i] - up * 0.034f * s);
                    nextPearl += 0.028f;
                }
            }
            body.Sweep(inner, Profiles.RoundedRect(0.046f, 0.030f, 0.012f, 4), taper, Vector3.forward);

            var sphere = SphereMesh(10, 8);
            foreach (var p in pearls)
                beads.Append(sphere, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * 0.019f));
            // 弦顶的调音钉
            for (int i = 0; i < PianoKeyMap.KeyCount; i++)
            {
                float x = StringX(i);
                beads.Append(sphere, Matrix4x4.TRS(new Vector3(x, TopY(tops, x) + 0.003f, z + 0.004f),
                    Quaternion.identity, Vector3.one * 0.0095f));
            }
            Release(sphere);
            return tops;
        }

        // ==================== 弧梁 ====================

        private void BuildArch(SweepMeshBuilder body)
        {
            float z = StringPlaneZ + 0.026f;
            var controls = new[]
            {
                new Vector3(_w + 0.05f, 1.02f, z), new Vector3(_w * 0.80f, 1.27f, z),
                new Vector3(_w * 0.36f, 1.20f, z), new Vector3(0.11f, StringBottomY - 0.004f, z)
            };
            body.Sweep(Paths.Bezier(controls, 0.010f), Profiles.RoundedRect(0.022f, 0.030f, 0.009f, 4),
                u => Mathf.Lerp(1f, 0.8f, u), Vector3.forward);
        }

        // ==================== 涡卷腿 ====================

        /// <summary>
        /// C 形涡卷腿：从琴颊底面向外鼓出、收回，末端在地面上卷成螺旋脚。
        /// <paramref name="sx"/>：−1 高音端 / +1 低音端；<paramref name="sz"/>：+1 前腿（鼓向演奏者）/ −1 后腿。
        /// </summary>
        private void BuildLeg(SweepMeshBuilder body, float sx, float sz)
        {
            float xc = sx > 0f ? _w + 0.095f : -0.095f;
            float z0 = sz > 0f ? 0.030f : -0.075f;
            Vector3 origin = new Vector3(xc, 0f, z0);
            Vector3 outward = new Vector3(sx * 0.42f, 0f, sz).normalized;
            Vector3 plane = Vector3.Cross(outward, Vector3.up).normalized;
            Vector3 P(Vector2 uv) => origin + outward * uv.x + Vector3.up * uv.y;

            // 螺旋脚：中心 O，起点在 135°，逆时针（经过底部再向外、向上）卷 1.75 圈
            Vector2 o = new Vector2(0.088f, 0.077f);
            const float r0 = 0.074f, k = 0.14f, start = Mathf.PI * 0.75f;
            Vector2 e = o + new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * r0;
            Vector2 tangent = new Vector2(-Mathf.Sin(start) - k * Mathf.Cos(start), Mathf.Cos(start) - k * Mathf.Sin(start)).normalized;
            var upper = Paths.Bezier(new[]
            {
                P(new Vector2(0f, 0.638f)), P(new Vector2(0.15f, 0.52f)),
                P(e - tangent * 0.11f), P(e)
            }, 0.008f);
            var foot = Paths.Spiral(P(o), outward, Vector3.up, start, Mathf.PI * 1.75f, r0, k, 0.006f);
            var path = new List<Vector3>(upper);
            for (int i = 1; i < foot.Count; i++) path.Add(foot[i]);

            int split = upper.Count;
            int count = path.Count;
            body.Sweep(path, Profiles.RoundedRect(0.058f, 0.062f, 0.022f, 4), u =>
            {
                float uSplit = (float)split / count;
                return u < uSplit ? Mathf.Lerp(1.0f, 0.74f, u / uSplit) : Mathf.Lerp(0.74f, 0.40f, (u - uSplit) / (1f - uSplit));
            }, plane);
        }

        // ==================== 琴弦 ====================

        /// <summary>第 <paramref name="index"/> 根弦（0 = A0 … 87 = C8）的本地 x：88 根等距，低音在 +x。</summary>
        internal float StringX(int index) => _w - (index + 0.5f) * (_w / PianoKeyMap.KeyCount);

        private static float TopY(List<Vector3> tops, float x)
        {
            for (int i = 1; i < tops.Count; i++)
            {
                float x0 = tops[i - 1].x, x1 = tops[i].x;
                if ((x <= x0 && x >= x1) || (x >= x0 && x <= x1))
                {
                    float t = Mathf.Abs(x1 - x0) > 1e-6f ? (x - x0) / (x1 - x0) : 0f;
                    return Mathf.Lerp(tops[i - 1].y, tops[i].y, t);
                }
            }
            return tops[tops.Count - 1].y;
        }

        private void BuildStrings(List<Vector3> tops)
        {
            const int sides = 6;
            const int rings = 14;
            var bottoms = new Vector3[PianoKeyMap.KeyCount];
            var topsLocal = new Vector3[PianoKeyMap.KeyCount];
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i < PianoKeyMap.KeyCount; i++)
            {
                float x = StringX(i);
                float top = TopY(tops, x);
                float radius = Mathf.Lerp(0.0017f, 0.0006f, i / (float)(PianoKeyMap.KeyCount - 1));
                bottoms[i] = new Vector3(x, StringBottomY, StringPlaneZ);
                topsLocal[i] = new Vector3(x, top, StringPlaneZ);
                int start = vertices.Count;
                for (int r = 0; r < rings; r++)
                {
                    float t = r / (float)(rings - 1);
                    float y = Mathf.Lerp(StringBottomY, top, t);
                    for (int s = 0; s < sides; s++)
                    {
                        float a = Mathf.PI * 2f * s / sides;
                        var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        vertices.Add(new Vector3(x, y, StringPlaneZ) + n * radius);
                        normals.Add(n);
                        uvs.Add(new Vector2(i, t));
                    }
                }
                for (int r = 0; r < rings - 1; r++)
                {
                    for (int s = 0; s < sides; s++)
                    {
                        int a = start + r * sides + s;
                        int b = start + r * sides + (s + 1) % sides;
                        int c = a + sides;
                        int d = b + sides;
                        // Unity 以 Cross(b−a, c−a) 为正面法线：(a, c, b) 让它指向外法线 (cos, 0, sin)
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(b); triangles.Add(c); triangles.Add(d);
                    }
                }
            }

            var mesh = new Mesh { name = "HarpStrings" };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            // 振动会把弦横向推开几毫米，放大包围盒避免被视锥误剔除
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(0.02f, 0f, 0.02f));
            mesh.bounds = bounds;
            _track(mesh);

            var shader = Shader.Find("PianoTeacher3D/HarpString");
            Material material;
            if (shader != null)
            {
                material = new Material(shader) { name = "竖琴钢琴 · 琴弦" };
                _ownedMaterials?.Add(material);
            }
            else
            {
                material = _finish("竖琴钢琴 · 琴弦", new Color(0.80f, 0.83f, 0.86f), 1f, 0.8f);
            }
            Strings = Add("HarpStrings", mesh, material, false);
            Strings.gameObject.AddComponent<HarpStringVibration>().SetStringGeometry(bottoms, topsLocal);
        }

        // ==================== 工具 ====================

        private static List<Vector3> Line(Vector3 a, Vector3 b) => new() { a, b };

        private Transform Add(string name, Mesh mesh, Material material, bool castShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        private static void Release(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        /// <summary>单位直径的经纬球（只作合并用的模板，调用方用完即销毁）。</summary>
        private static Mesh SphereMesh(int segments, int stacks)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int st = 0; st <= stacks; st++)
            {
                float phi = Mathf.PI * st / stacks;
                for (int sg = 0; sg <= segments; sg++)
                {
                    float theta = Mathf.PI * 2f * sg / segments;
                    vertices.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)) * 0.5f);
                }
            }
            for (int st = 0; st < stacks; st++)
            {
                for (int sg = 0; sg < segments; sg++)
                {
                    int a = st * (segments + 1) + sg;
                    int b = a + segments + 1;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                    triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = vertices[i].normalized;
            mesh.normals = normals;
            return mesh;
        }
    }
}
