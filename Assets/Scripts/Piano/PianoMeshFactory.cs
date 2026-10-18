using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 倒角模式（对应方案 5.4「键面边缘增加细小倒角」/ 审查 1.3A 第 1 项）。
    /// </summary>
    public enum ChamferMode
    {
        /// <summary>仅顶面四周倒角：键身侧壁保持平直，只柔化键面边缘高光（白键 / 黑键顶面用）。</summary>
        TopOnly = 0,

        /// <summary>全部棱边 + 棱角倒角：用于琴体面板、踏板等需要柔和轮廓的部件。</summary>
        AllEdges = 1
    }

    /// <summary>
    /// 程序化几何工具（对应方案 5.4 / 审查 1.3A 第 1、3 项）。
    /// <para>
    /// 纯静态、无状态、不依赖任何引擎上下文，可直接被 EditMode 单测覆盖；
    /// 零外部模型资产，符合项目「零第三方资源、零许可风险」基线。
    /// </para>
    /// <para>
    /// 网格约定：以原点为中心，尺寸沿本地 X/Y/Z 轴分别为 width/height/length。
    /// 顶点共享（不重复），法线按 "<b>相邻三角形叉积面积加权</b>" 计算，
    /// 因此倒角面与主面之间得到柔和过渡高光，而不是硬边或过量倾斜的法线。
    /// </para>
    /// <para>
    /// 顶点预算：<see cref="ChamferMode.TopOnly"/> ≤ 12，<see cref="ChamferMode.AllEdges"/> ≤ 24，
    /// 均远低于 64 的约束，可安全地对 88 键复用同一份网格。
    /// </para>
    /// </summary>
    public static class PianoMeshFactory
    {
        /// <summary>倒角上限相对最小边的比例：略小于 0.5，避免倒角把棱面压成零面积（退化三角形）。</summary>
        private const float ChamferLimitRatio = 0.49f;

        /// <summary>最小允许尺寸，防止调用方传入 0/负值时产生退化几何。</summary>
        private const float MinDimension = 1e-4f;

        /// <summary>判定法线/面积是否退化的平方阈值。</summary>
        private const float DegenerateEpsilon = 1e-20f;

        // ==================== 公共 API ====================

        /// <summary>
        /// 把倒角值钳制到合法区间 [0, 最小边 × 0.49]。
        /// <para>NaN / 无穷 / 负值 → 0；超过上限 → 上限；尺寸非法（≤0）→ 0。</para>
        /// </summary>
        /// <param name="chamfer">期望倒角（米）。</param>
        /// <param name="width">X 方向尺寸（米）。</param>
        /// <param name="height">Y 方向尺寸（米）。</param>
        /// <param name="length">Z 方向尺寸（米）。</param>
        public static float ClampChamfer(float chamfer, float width, float height, float length)
        {
            if (float.IsNaN(chamfer) || float.IsInfinity(chamfer) || chamfer <= 0f) return 0f;

            float minEdge = Mathf.Min(width, Mathf.Min(height, length));
            if (minEdge <= 0f) return 0f;

            float max = minEdge * ChamferLimitRatio;
            return chamfer > max ? max : chamfer;
        }

        /// <summary>
        /// 生成带倒角的立方体网格（默认仅顶面倒角）。
        /// 顶点数可控（≤ 24）、无重复顶点、索引三角化正确、包围盒等于传入的 w/h/l。
        /// </summary>
        public static Mesh CreateChamferedBox(float width, float height, float length, float chamfer)
            => CreateChamferedBox(width, height, length, chamfer, ChamferMode.TopOnly);

        /// <summary>
        /// 生成带倒角的立方体网格。
        /// <para>
        /// <paramref name="chamfer"/> 会先经 <see cref="ClampChamfer"/> 钳制；
        /// 钳制结果为 0 时退化为普通立方体（8 顶点 / 12 三角形），仍不产生退化三角形。
        /// </para>
        /// </summary>
        /// <param name="width">X 方向尺寸（米）。</param>
        /// <param name="height">Y 方向尺寸（米）。</param>
        /// <param name="length">Z 方向尺寸（米）。</param>
        /// <param name="chamfer">倒角尺寸（米），按最小边自动钳制。</param>
        /// <param name="mode">倒角模式（仅顶面 / 全部棱边）。</param>
        public static Mesh CreateChamferedBox(float width, float height, float length, float chamfer, ChamferMode mode)
        {
            float w = Mathf.Max(width, MinDimension);
            float h = Mathf.Max(height, MinDimension);
            float l = Mathf.Max(length, MinDimension);
            float c = ClampChamfer(chamfer, w, h, l);

            var mesh = new Mesh { name = $"ChamferedBox_{mode}_{w:F4}x{h:F4}x{l:F4}_c{c:F5}" };

            if (c <= 0f)
                BuildPlainBox(mesh, w, h, l);
            else if (mode == ChamferMode.TopOnly)
                BuildTopChamferedBox(mesh, w, h, l, c);
            else
                BuildAllEdgesChamferedBox(mesh, w, h, l, c);

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 生成轻微圆角面板（低模近似：用全部棱边倒角替代圆弧），用于琴身顶盖 / 底座等需要柔和轮廓的部件。
        /// </summary>
        /// <param name="width">X 方向尺寸（米）。</param>
        /// <param name="height">Y 方向尺寸（米）。</param>
        /// <param name="depth">Z 方向尺寸（米）。</param>
        /// <param name="radius">圆角半径（米），按最小边自动钳制。</param>
        public static Mesh CreateRoundedPanel(float width, float height, float depth, float radius)
            => CreateChamferedBox(width, height, depth, radius, ChamferMode.AllEdges);

        /// <summary>三角形是否退化（面积 ≈ 0）。测试与构建期自检共用。</summary>
        public static bool IsDegenerateTriangle(Vector3 a, Vector3 b, Vector3 c)
            => Vector3.Cross(b - a, c - a).sqrMagnitude <= DegenerateEpsilon;

        // ==================== 网格构建 ====================

        /// <summary>普通立方体：8 顶点 / 12 三角形（chamfer ≤ 0 时的回退形态）。</summary>
        private static void BuildPlainBox(Mesh mesh, float w, float h, float l)
        {
            float hw = w * 0.5f, hh = h * 0.5f, hl = l * 0.5f;

            var v = new[]
            {
                new Vector3(-hw, -hh, -hl), // 0
                new Vector3( hw, -hh, -hl), // 1
                new Vector3( hw, -hh,  hl), // 2
                new Vector3(-hw, -hh,  hl), // 3
                new Vector3(-hw,  hh, -hl), // 4
                new Vector3( hw,  hh, -hl), // 5
                new Vector3( hw,  hh,  hl), // 6
                new Vector3(-hw,  hh,  hl)  // 7
            };

            var tris = new List<int>(36);
            AddQuad(tris, v, 0, 1, 2, 3, Vector3.down);   // -Y
            AddQuad(tris, v, 4, 7, 6, 5, Vector3.up);     // +Y
            AddQuad(tris, v, 0, 4, 5, 1, Vector3.back);   // -Z
            AddQuad(tris, v, 3, 2, 6, 7, Vector3.forward);// +Z
            AddQuad(tris, v, 0, 3, 7, 4, Vector3.left);   // -X
            AddQuad(tris, v, 1, 5, 6, 2, Vector3.right);  // +X

            Apply(mesh, v, tris);
        }

        /// <summary>仅顶面倒角：12 顶点 / 20 三角形。侧壁保持平直，仅顶面四周切角。</summary>
        private static void BuildTopChamferedBox(Mesh mesh, float w, float h, float l, float c)
        {
            float hw = w * 0.5f, hh = h * 0.5f, hl = l * 0.5f;

            var v = new[]
            {
                // 0..3：顶面（四周内缩 c）
                new Vector3(-hw + c,  hh,     -hl + c),
                new Vector3( hw - c,  hh,     -hl + c),
                new Vector3( hw - c,  hh,      hl - c),
                new Vector3(-hw + c,  hh,      hl - c),
                // 4..7：倒角外沿（落在原始外轮廓上，低于顶面 c）
                new Vector3(-hw,      hh - c, -hl),
                new Vector3( hw,      hh - c, -hl),
                new Vector3( hw,      hh - c,  hl),
                new Vector3(-hw,      hh - c,  hl),
                // 8..11：底面
                new Vector3(-hw,     -hh,     -hl),
                new Vector3( hw,     -hh,     -hl),
                new Vector3( hw,     -hh,      hl),
                new Vector3(-hw,     -hh,      hl)
            };

            var tris = new List<int>(60);
            AddQuad(tris, v, 0, 3, 2, 1, Vector3.up);       // 顶面
            AddQuad(tris, v, 0, 1, 5, 4, UpBack);           // 后倒角
            AddQuad(tris, v, 1, 2, 6, 5, UpRight);          // 右倒角
            AddQuad(tris, v, 2, 3, 7, 6, UpFront);          // 前倒角
            AddQuad(tris, v, 3, 0, 4, 7, UpLeft);           // 左倒角
            AddQuad(tris, v, 4, 5, 9, 8, Vector3.back);     // 后侧壁
            AddQuad(tris, v, 5, 6, 10, 9, Vector3.right);   // 右侧壁
            AddQuad(tris, v, 6, 7, 11, 10, Vector3.forward);// 前侧壁
            AddQuad(tris, v, 7, 4, 8, 11, Vector3.left);    // 左侧壁
            AddQuad(tris, v, 8, 11, 10, 9, Vector3.down);   // 底面

            Apply(mesh, v, tris);
        }

        /// <summary>全部棱边 + 棱角倒角：24 顶点 / 44 三角形。</summary>
        private static void BuildAllEdgesChamferedBox(Mesh mesh, float w, float h, float l, float c)
        {
            float hw = w * 0.5f, hh = h * 0.5f, hl = l * 0.5f;

            // 8 个角 × 3 个"贴面"顶点：NX（在 x=±hw 面上）、NY（在 y=±hh 面上）、NZ（在 z=±hl 面上）。
            // 角索引 k 的位含义：bit0=sx，bit1=sy，bit2=sz（1 → +，0 → -）。
            var v = new Vector3[24];
            for (int k = 0; k < 8; k++)
            {
                float sx = (k & 1) != 0 ? 1f : -1f;
                float sy = (k & 2) != 0 ? 1f : -1f;
                float sz = (k & 4) != 0 ? 1f : -1f;

                v[k * 3 + 0] = new Vector3(sx * hw, sy * (hh - c), sz * (hl - c)); // NX
                v[k * 3 + 1] = new Vector3(sx * (hw - c), sy * hh, sz * (hl - c)); // NY
                v[k * 3 + 2] = new Vector3(sx * (hw - c), sy * (hh - c), sz * hl); // NZ
            }

            var tris = new List<int>(132);

            // ---- 6 个主面 ----
            AddQuad(tris, v, NY(2), NY(3), NY(7), NY(6), Vector3.up);    // +Y
            AddQuad(tris, v, NY(0), NY(1), NY(5), NY(4), Vector3.down);  // -Y
            AddQuad(tris, v, NZ(4), NZ(5), NZ(7), NZ(6), Vector3.forward); // +Z
            AddQuad(tris, v, NZ(0), NZ(1), NZ(3), NZ(2), Vector3.back);  // -Z
            AddQuad(tris, v, NX(1), NX(3), NX(7), NX(5), Vector3.right); // +X
            AddQuad(tris, v, NX(0), NX(2), NX(6), NX(4), Vector3.left);  // -X

            // ---- 12 条棱的倒角面 ----
            // 沿 X 的 4 条棱
            AddQuad(tris, v, NY(2), NY(3), NZ(3), NZ(2), UpBack);
            AddQuad(tris, v, NY(6), NY(7), NZ(7), NZ(6), UpFront);
            AddQuad(tris, v, NY(0), NY(1), NZ(1), NZ(0), DownBack);
            AddQuad(tris, v, NY(4), NY(5), NZ(5), NZ(4), DownFront);
            // 沿 Y 的 4 条棱
            AddQuad(tris, v, NX(1), NX(3), NZ(3), NZ(1), RightBack);
            AddQuad(tris, v, NX(5), NX(7), NZ(7), NZ(5), RightFront);
            AddQuad(tris, v, NX(0), NX(2), NZ(2), NZ(0), LeftBack);
            AddQuad(tris, v, NX(4), NX(6), NZ(6), NZ(4), LeftFront);
            // 沿 Z 的 4 条棱
            AddQuad(tris, v, NX(3), NX(7), NY(7), NY(3), UpRight);
            AddQuad(tris, v, NX(1), NX(5), NY(5), NY(1), DownRight);
            AddQuad(tris, v, NX(2), NX(6), NY(6), NY(2), UpLeft);
            AddQuad(tris, v, NX(0), NX(4), NY(4), NY(0), DownLeft);

            // ---- 8 个棱角三角面 ----
            for (int k = 0; k < 8; k++)
            {
                float sx = (k & 1) != 0 ? 1f : -1f;
                float sy = (k & 2) != 0 ? 1f : -1f;
                float sz = (k & 4) != 0 ? 1f : -1f;
                AddTri(tris, v, k * 3 + 0, k * 3 + 1, k * 3 + 2, new Vector3(sx, sy, sz));
            }

            Apply(mesh, v, tris);
        }

        // 棱角顶点索引辅助（保持倒角面写法可读）
        private static int NX(int corner) => corner * 3 + 0;
        private static int NY(int corner) => corner * 3 + 1;
        private static int NZ(int corner) => corner * 3 + 2;

        // 倒角面的外法线参考方向常量
        private static readonly Vector3 UpBack = new Vector3(0f, 1f, -1f);
        private static readonly Vector3 UpFront = new Vector3(0f, 1f, 1f);
        private static readonly Vector3 UpRight = new Vector3(1f, 1f, 0f);
        private static readonly Vector3 UpLeft = new Vector3(-1f, 1f, 0f);
        private static readonly Vector3 DownBack = new Vector3(0f, -1f, -1f);
        private static readonly Vector3 DownFront = new Vector3(0f, -1f, 1f);
        private static readonly Vector3 DownRight = new Vector3(1f, -1f, 0f);
        private static readonly Vector3 DownLeft = new Vector3(-1f, -1f, 0f);
        private static readonly Vector3 RightBack = new Vector3(1f, 0f, -1f);
        private static readonly Vector3 RightFront = new Vector3(1f, 0f, 1f);
        private static readonly Vector3 LeftBack = new Vector3(-1f, 0f, -1f);
        private static readonly Vector3 LeftFront = new Vector3(-1f, 0f, 1f);

        // ==================== 组装与法线 ====================

        /// <summary>写入网格数据，并按相邻三角形叉积做面积加权法线（共享顶点 → 柔和边缘高光）。</summary>
        private static void Apply(Mesh mesh, Vector3[] verts, List<int> triangles)
        {
            var tris = triangles.ToArray();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.normals = ComputeAreaWeightedNormals(verts, tris);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// 面积加权顶点法线：累加相邻三角形的叉积（其模长即两倍面积），再归一化。
        /// 主面面积远大于倒角面，因此主面法线几乎垂直于主面，倒角只带来轻微过渡 —— 正是想要的柔和高光。
        /// </summary>
        private static Vector3[] ComputeAreaWeightedNormals(Vector3[] verts, int[] tris)
        {
            var normals = new Vector3[verts.Length];
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                normals[a] += n;
                normals[b] += n;
                normals[c] += n;
            }

            for (int i = 0; i < normals.Length; i++)
            {
                float sqr = normals[i].sqrMagnitude;
                normals[i] = sqr > DegenerateEpsilon ? normals[i] * (1f / Mathf.Sqrt(sqr)) : Vector3.up;
            }
            return normals;
        }

        /// <summary>
        /// 追加一个四边形（两个三角形）。自动根据 <paramref name="outward"/> 修正绕序，
        /// 保证三角形面法线朝外（凸体），从而不会出现"内翻"面。
        /// </summary>
        private static void AddQuad(List<int> tris, Vector3[] v, int a, int b, int c, int d, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, outward) >= 0f)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
            else
            {
                tris.Add(a); tris.Add(d); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(b);
            }
        }

        /// <summary>追加一个三角形，自动按 <paramref name="outward"/> 修正绕序。</summary>
        private static void AddTri(List<int> tris, Vector3[] v, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, outward) >= 0f)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
            }
            else
            {
                tris.Add(a); tris.Add(c); tris.Add(b);
            }
        }
    }
}
