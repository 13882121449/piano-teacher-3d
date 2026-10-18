using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 琴键几何与 88 键映射的纯逻辑测试（对应方案 5.4「钢琴材质与灯光」/ 审查 1.3A 第 1、2 项）。
    /// <para>
    /// 全部为 EditMode 可运行的纯逻辑断言，不加载场景、不依赖渲染设备；
    /// 覆盖 <see cref="PianoMeshFactory"/> 的倒角网格质量与 <see cref="PianoKeyMap"/> 的键位映射算法。
    /// </para>
    /// </summary>
    public class PianoGeometryTests
    {
        private const float BoundsTolerance = 1e-4f;
        private const float SpacingTolerance = 1e-6f;

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        // ==================== 工具 ====================

        private Mesh Box(float w, float h, float l, float c, ChamferMode mode = ChamferMode.TopOnly)
        {
            var mesh = PianoMeshFactory.CreateChamferedBox(w, h, l, c, mode);
            _created.Add(mesh);
            return mesh;
        }

        private static int CountDegenerateTriangles(Mesh mesh)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            int count = 0;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                if (PianoMeshFactory.IsDegenerateTriangle(v[t[i]], v[t[i + 1]], v[t[i + 2]])) count++;
            }
            return count;
        }

        // ==================== CreateChamferedBox ====================

        [Test]
        public void CreateChamferedBox_VertexCountIsWithinBudget()
        {
            var top = Box(0.024f, 0.022f, 0.15f, 0.0008f, ChamferMode.TopOnly);
            var all = Box(0.024f, 0.022f, 0.15f, 0.0008f, ChamferMode.AllEdges);

            Assert.Greater(top.vertexCount, 0, "顶点数必须大于 0");
            Assert.LessOrEqual(top.vertexCount, 64, "顶点数必须 ≤ 64");
            Assert.LessOrEqual(all.vertexCount, 64, "顶点数必须 ≤ 64");
        }

        [Test]
        public void CreateChamferedBox_IndexBufferIsValid()
        {
            var mesh = Box(0.014f, 0.032f, 0.10f, 0.0006f, ChamferMode.AllEdges);
            var tris = mesh.triangles;

            Assert.Greater(tris.Length, 0);
            Assert.AreEqual(0, tris.Length % 3, "三角形索引数必须是 3 的倍数");

            for (int i = 0; i < tris.Length; i++)
            {
                Assert.GreaterOrEqual(tris[i], 0, $"索引 {i} 越界（下界）");
                Assert.Less(tris[i], mesh.vertexCount, $"索引 {i} 越界（上界）");
            }
        }

        [Test]
        public void CreateChamferedBox_HasNoNaNVerticesOrNormals()
        {
            var mesh = Box(0.024f, 0.022f, 0.15f, 0.0008f, ChamferMode.AllEdges);

            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(verts[i].x) || float.IsNaN(verts[i].y) || float.IsNaN(verts[i].z),
                    $"顶点 {i} 含 NaN");
            }

            var normals = mesh.normals;
            Assert.AreEqual(verts.Length, normals.Length, "法线数量必须与顶点一致");
            for (int i = 0; i < normals.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(normals[i].x) || float.IsNaN(normals[i].y) || float.IsNaN(normals[i].z),
                    $"法线 {i} 含 NaN");
                Assert.Greater(normals[i].sqrMagnitude, 0.5f, $"法线 {i} 未归一化");
            }
        }

        [Test]
        public void CreateChamferedBox_BoundsMatchRequestedSize()
        {
            const float w = 0.024f, h = 0.022f, l = 0.15f;
            var mesh = Box(w, h, l, 0.0008f, ChamferMode.AllEdges);

            var size = mesh.bounds.size;
            Assert.AreEqual(w, size.x, BoundsTolerance, "包围盒 X 应等于键宽");
            Assert.AreEqual(h, size.y, BoundsTolerance, "包围盒 Y 应等于键高");
            Assert.AreEqual(l, size.z, BoundsTolerance, "包围盒 Z 应等于键长");
        }

        [Test]
        public void CreateChamferedBox_TrianglesFaceOutward()
        {
            var mesh = Box(0.024f, 0.022f, 0.15f, 0.0008f, ChamferMode.AllEdges);

            var v = mesh.vertices;
            var t = mesh.triangles;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 centroid = (a + b + c) / 3f;
                Vector3 normal = Vector3.Cross(b - a, c - a);

                // 凸体以原点为中心：三角形法线必须与"由中心指向面心"同向，否则面会内翻
                Assert.Greater(Vector3.Dot(normal, centroid), 0f,
                    $"三角形 {i / 3} 的绕序导致法线朝内");
            }
        }

        [Test]
        public void CreateChamferedBox_HasNoDuplicateVertices()
        {
            var mesh = Box(0.024f, 0.022f, 0.15f, 0.0008f, ChamferMode.AllEdges);
            var v = mesh.vertices;

            for (int i = 0; i < v.Length; i++)
            {
                for (int j = i + 1; j < v.Length; j++)
                {
                    Assert.Greater(Vector3.Distance(v[i], v[j]), 1e-7f,
                        $"顶点 {i} 与 {j} 位置重复");
                }
            }
        }

        [Test]
        public void CreateChamferedBox_ZeroChamfer_HasNoDegenerateTriangles()
        {
            var mesh = Box(0.024f, 0.022f, 0.15f, 0f);

            Assert.AreEqual(0, CountDegenerateTriangles(mesh), "chamfer=0 不得产生退化三角形");
            Assert.Greater(mesh.triangles.Length, 0, "退化形态仍必须是有效网格（不能是空网格）");
        }

        [Test]
        public void CreateChamferedBox_NegativeChamfer_FallsBackToPlainBox()
        {
            var negative = Box(0.024f, 0.022f, 0.15f, -1f);
            var zero = Box(0.024f, 0.022f, 0.15f, 0f);

            Assert.AreEqual(0, CountDegenerateTriangles(negative), "负倒角不得产生退化三角形");
            Assert.AreEqual(zero.vertexCount, negative.vertexCount, "负倒角应按 0 处理");
        }

        [Test]
        public void CreateChamferedBox_OverLimitChamfer_IsClampedWithoutDegenerateTriangles()
        {
            // 传入远超最小边一半的倒角（最小边 = 0.022）→ 必须被钳制且不产生退化三角形
            var mesh = Box(0.024f, 0.022f, 0.15f, 5f, ChamferMode.AllEdges);

            Assert.AreEqual(0, CountDegenerateTriangles(mesh), "超限倒角被钳制后不得产生退化三角形");
            Assert.Greater(mesh.vertexCount, 0);
        }

        [Test]
        public void CreateChamferedBox_ModesProduceDifferentVertexCounts()
        {
            var top = Box(0.024f, 0.022f, 0.15f, 0.0008f, ChamferMode.TopOnly);
            var all = Box(0.024f, 0.022f, 0.15f, 0.0008f, ChamferMode.AllEdges);

            Assert.AreNotEqual(top.vertexCount, all.vertexCount,
                "仅顶面倒角与全边倒角的顶点数必须不同");
        }

        [Test]
        public void CreateRoundedPanel_ProducesValidMesh()
        {
            var mesh = PianoMeshFactory.CreateRoundedPanel(0.024f, 0.03f, 0.30f, 0.006f);
            _created.Add(mesh);

            Assert.Greater(mesh.vertexCount, 0);
            Assert.Greater(mesh.triangles.Length, 0);
            Assert.AreEqual(0, CountDegenerateTriangles(mesh), "圆角面板不得产生退化三角形");
            Assert.AreEqual(0.30f, mesh.bounds.size.z, BoundsTolerance, "圆角面板深度应等于传入值");
        }

        // ==================== ClampChamfer ====================

        [Test]
        public void ClampChamfer_NormalValueIsUnchanged()
        {
            Assert.AreEqual(0.0008f, PianoMeshFactory.ClampChamfer(0.0008f, 0.024f, 0.022f, 0.15f), 1e-7f);
        }

        [Test]
        public void ClampChamfer_OverLimitIsClampedToAtMostHalfOfSmallestEdge()
        {
            float clamped = PianoMeshFactory.ClampChamfer(5f, 0.024f, 0.022f, 0.15f);

            // 最小边 = 0.022 → 0.5 × 0.022 = 0.011
            Assert.LessOrEqual(clamped, 0.011f + 1e-6f, "超限倒角必须钳制到 ≤ 最小边的一半");
            Assert.Greater(clamped, 0f, "钳制结果仍应为正数（保留倒角）");
        }

        [Test]
        public void ClampChamfer_NegativeOrInvalidReturnsZero()
        {
            Assert.AreEqual(0f, PianoMeshFactory.ClampChamfer(-0.001f, 0.024f, 0.022f, 0.15f), 1e-7f);
            Assert.AreEqual(0f, PianoMeshFactory.ClampChamfer(0f, 0.024f, 0.022f, 0.15f), 1e-7f);
            Assert.AreEqual(0f, PianoMeshFactory.ClampChamfer(0.0008f, 0f, 0.022f, 0.15f), 1e-7f);
            Assert.AreEqual(0f, PianoMeshFactory.ClampChamfer(float.NaN, 0.024f, 0.022f, 0.15f), 1e-7f);
        }

        // ==================== 88 键映射（纯逻辑，不需要 GameObject） ====================

        [Test]
        public void WhiteIndex_CoversAllFiftyTwoWhiteKeysUniquely()
        {
            var seen = new bool[PianoKeyMap.WhiteKeyCount];
            int whiteCount = 0;
            int blackCount = 0;

            for (int pitch = PianoKeyMap.MinPitch; pitch <= PianoKeyMap.MaxPitch; pitch++)
            {
                if (PianoKeyMap.IsBlackKey(pitch)) { blackCount++; continue; }

                int index = PianoKeyMap.WhiteIndex(pitch);
                Assert.GreaterOrEqual(index, 0, $"pitch={pitch} 白键索引越界（下界）");
                Assert.Less(index, PianoKeyMap.WhiteKeyCount, $"pitch={pitch} 白键索引越界（上界）");
                Assert.IsFalse(seen[index], $"pitch={pitch} 白键索引重复：{index}");

                seen[index] = true;
                whiteCount++;
            }

            Assert.AreEqual(PianoKeyMap.WhiteKeyCount, whiteCount, "白键数量应为 52");
            Assert.AreEqual(PianoKeyMap.KeyCount, whiteCount + blackCount, "黑白键总数应为 88");
        }

        [Test]
        public void BlackKeyLeftWhiteIndex_StaysInValidRange()
        {
            for (int pitch = PianoKeyMap.MinPitch; pitch <= PianoKeyMap.MaxPitch; pitch++)
            {
                if (!PianoKeyMap.IsBlackKey(pitch)) continue;

                int left = PianoKeyMap.BlackKeyLeftWhiteIndex(pitch);
                Assert.GreaterOrEqual(left, 0, $"pitch={pitch} 左侧白键索引越界（下界）");
                Assert.LessOrEqual(left, PianoKeyMap.WhiteKeyCount - 2, $"pitch={pitch} 左侧白键索引越界（上界）");
            }
        }

        [Test]
        public void WhiteIndex_BoundariesAreA0ZeroAndC8FiftyOne()
        {
            Assert.AreEqual(0, PianoKeyMap.WhiteIndex(PianoKeyMap.MinPitch), "A0(21) 的白键索引应为 0");
            Assert.AreEqual(PianoKeyMap.WhiteKeyCount - 1, PianoKeyMap.WhiteIndex(PianoKeyMap.MaxPitch),
                "C8(108) 的白键索引应为 51");
            Assert.AreEqual(0, PianoKeyMap.WhiteIndex(21));
            Assert.AreEqual(51, PianoKeyMap.WhiteIndex(108));
        }

        [Test]
        public void AdjacentWhiteKeys_SpacingEqualsWhiteWidth()
        {
            int previousIndex = -1;

            for (int pitch = PianoKeyMap.MinPitch; pitch <= PianoKeyMap.MaxPitch; pitch++)
            {
                if (PianoKeyMap.IsBlackKey(pitch)) continue;

                int index = PianoKeyMap.WhiteIndex(pitch);
                float x = index * PianoKeyMap.DefaultWhiteWidth;

                if (previousIndex >= 0)
                {
                    float previousX = previousIndex * PianoKeyMap.DefaultWhiteWidth;
                    Assert.AreEqual(PianoKeyMap.DefaultWhiteWidth, x - previousX, SpacingTolerance,
                        $"pitch={pitch} 与前一个白键的中心距应等于键宽（键间不重叠）");
                }

                previousIndex = index;
            }

            Assert.AreEqual(PianoKeyMap.WhiteKeyCount, previousIndex + 1, "白键索引应连续覆盖 0..51");
        }

        [Test]
        public void DefaultKeybedHeightAndSurfaceMatchDocumentedValues()
        {
            Assert.AreEqual(0.72f, PianoKeyMap.DefaultKeybedHeight, 1e-6f, "键床高度应为 0.72m");
            Assert.AreEqual(0.022f, PianoKeyMap.DefaultWhiteHeight, 1e-6f);
            Assert.AreEqual(0.032f, PianoKeyMap.DefaultBlackHeight, 1e-6f);
            Assert.AreEqual(0.024f, PianoKeyMap.DefaultWhiteWidth, 1e-6f);
            Assert.AreEqual(0.15f, PianoKeyMap.DefaultWhiteLength, 1e-6f);
            Assert.AreEqual(0.014f, PianoKeyMap.DefaultBlackWidth, 1e-6f);
            Assert.AreEqual(0.10f, PianoKeyMap.DefaultBlackLength, 1e-6f);
        }
    }
}
