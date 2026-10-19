using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.Avatar;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 手部网格几何单测（评估文档附录 A.6.1）。
    /// <para>
    /// 覆盖：网格非空、顶点/法线均为有限值、包围盒符合角色配置、指尖收口、逐段渐缩、
    /// 索引与法线有效、无退化三角形、三个角色都能独立构建、关节限位校验。
    /// 这些断言不依赖场景、相机或播放状态，可在 EditMode 直接运行。
    /// </para>
    /// </summary>
    public class HandMeshFactoryTests
    {
        private readonly List<Mesh> _meshes = new List<Mesh>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _meshes.Count; i++)
            {
                if (_meshes[i] != null) Object.DestroyImmediate(_meshes[i]);
            }
            _meshes.Clear();
        }

        private Mesh Track(Mesh mesh)
        {
            if (mesh != null) _meshes.Add(mesh);
            return mesh;
        }

        private static HandMeshDefinition Definition(float side = 1f,
            AvatarMeshDetail detail = AvatarMeshDetail.Medium, float bodyScale = 1f)
        {
            return HandMeshDefinition.FromMetrics(HandMetrics.CreateDefault(), bodyScale, side, detail);
        }

        // ==================== 掌部 ====================

        [Test]
        public void Palm_IsNonEmptyAndHasValidIndices()
        {
            var def = Definition();
            var mesh = Track(HandMeshFactory.BuildPalm(def));

            Assert.IsNotNull(mesh, "掌部网格不应为 null");
            Assert.Greater(mesh.vertexCount, 3 * def.RingSegments, "掌部应有至少 4 圈截面");
            Assert.Greater(mesh.triangles.Length, 0, "掌部应生成三角面");
            Assert.AreEqual(0, mesh.triangles.Length % 3, "索引数量必须是 3 的倍数");

            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.IsTrue(IsFinite(vertices[i]), $"顶点 {i} 含 NaN/Infinity：{vertices[i]}");
            }

            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i++)
            {
                Assert.GreaterOrEqual(triangles[i], 0);
                Assert.Less(triangles[i], mesh.vertexCount, "索引越界");
            }
        }

        [Test]
        public void Palm_BoundsMatchMetrics()
        {
            var metrics = HandMetrics.CreateDefault();
            var def = Definition();
            var mesh = Track(HandMeshFactory.BuildPalm(def));
            var size = mesh.bounds.size;

            // 宽度含大鱼际隆起，允许区间放宽；厚度/长度必须落在合理比例内。
            // 长度上界放宽到 1.6 倍：掌部网格向腕后回退（与手臂胶囊重叠）并带两端封口。
            Assert.That(size.x, Is.InRange(metrics.PalmWidth * 0.55f, metrics.PalmWidth * 1.45f),
                "掌宽与尺寸表不符");
            Assert.That(size.y, Is.InRange(metrics.PalmThickness * 0.60f, metrics.PalmThickness * 1.60f),
                "掌厚与尺寸表不符");
            Assert.That(size.z, Is.InRange(metrics.PalmLength * 0.90f, metrics.PalmLength * 1.60f),
                "掌长与尺寸表不符");
        }

        [Test]
        public void Palm_IsMirroredBetweenHands()
        {
            var right = Track(HandMeshFactory.BuildPalm(Definition(side: 1f)));
            var left = Track(HandMeshFactory.BuildPalm(Definition(side: -1f)));

            Assert.AreEqual(right.vertexCount, left.vertexCount, "左右掌部顶点数应一致（镜像）");

            // 大鱼际在拇指侧：右手质心偏向 +X，左手偏向 −X
            Assert.Greater(MeanX(right), 0.001f, "右手掌的质心应偏向拇指侧（+X）");
            Assert.Less(MeanX(left), -0.001f, "左手掌的质心应偏向拇指侧（−X）");
        }

        // ==================== 手指 ====================

        [Test]
        public void FingerSegments_TaperTowardTip()
        {
            var def = Definition();

            for (int finger = 0; finger < HandMeshFactory.FingerCount; finger++)
            {
                float previous = float.MaxValue;
                for (int segment = 0; segment < HandMeshFactory.SegmentCount; segment++)
                {
                    float radius = def.SegmentRadius(finger, segment);
                    Assert.Less(radius, previous, $"第 {finger} 指第 {segment} 段应比上一段更细");
                    previous = radius;

                    var mesh = Track(HandMeshFactory.BuildFingerSegment(def, finger, segment));
                    Assert.IsNotNull(mesh, $"第 {finger} 指第 {segment} 段网格为空");
                    Assert.Less(mesh.bounds.size.x, def.PalmWidth * 0.5f, "指节宽度不应超过掌宽的一半");
                }
            }
        }

        [Test]
        public void FingerSegments_HaveDistinctLengths()
        {
            var def = Definition();

            float thumb = def.FingerLength(0);
            float index = def.FingerLength(1);
            float middle = def.FingerLength(2);
            float ring = def.FingerLength(3);
            float little = def.FingerLength(4);

            Assert.Greater(middle, index, "中指应长于食指");
            Assert.Greater(middle, ring, "中指应长于无名指");
            Assert.Greater(index, little, "食指应长于小指");
            Assert.Greater(ring, little, "无名指应长于小指");
            Assert.Greater(thumb, little, "拇指应长于小指");
        }

        [Test]
        public void DistalSegment_HasRoundedTipBeyondJointLength()
        {
            var def = Definition();
            var mesh = Track(HandMeshFactory.BuildFingerSegment(def, 2, 2));
            float length = def.SegmentLength(2, 2);

            Assert.Greater(mesh.bounds.max.z, length, "末节应有超出关节长度的圆指尖（不是平切圆柱）");
            Assert.Less(mesh.bounds.max.z, length + def.SegmentRadius(2, 2) * 1.6f,
                "圆指尖的额外长度应约等于末端半径");
        }

        [Test]
        public void Meshes_HaveNoDegenerateTriangles()
        {
            var def = Definition(detail: AvatarMeshDetail.High);
            var meshes = new List<Mesh>
            {
                Track(HandMeshFactory.BuildPalm(def)),
                Track(HandMeshFactory.BuildFingerSegment(def, 1, 0)),
                Track(HandMeshFactory.BuildFingerSegment(def, 1, 2)),
                Track(HandMeshFactory.BuildNail(def, 1))
            };

            for (int m = 0; m < meshes.Count; m++)
            {
                var mesh = meshes[m];
                Assert.IsNotNull(mesh, "网格不应为空：" + m);

                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]];
                    Vector3 b = vertices[triangles[i + 1]];
                    Vector3 c = vertices[triangles[i + 2]];
                    float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                    Assert.Greater(area, 1e-10f, $"网格 {mesh.name} 存在退化三角形（索引 {i}）");
                }
            }
        }

        [Test]
        public void Meshes_HaveFiniteUnitNormals()
        {
            var def = Definition();
            var mesh = Track(HandMeshFactory.BuildPalm(def));
            var normals = mesh.normals;

            Assert.AreEqual(mesh.vertexCount, normals.Length, "每个顶点都应有法线");
            for (int i = 0; i < normals.Length; i++)
            {
                Assert.IsTrue(IsFinite(normals[i]), $"法线 {i} 含 NaN/Infinity");
                Assert.That(normals[i].magnitude, Is.InRange(0.5f, 1.5f), "法线应近似单位长度");
            }
        }

        [Test]
        public void Nail_OnlyGeneratedForHighDetail()
        {
            var high = Definition(detail: AvatarMeshDetail.High);
            var medium = Definition(detail: AvatarMeshDetail.Medium);

            Assert.IsTrue(high.IncludeNails, "高精度档应生成指甲");
            Assert.IsFalse(medium.IncludeNails, "标准档不生成指甲（省绘制批次）");

            var nail = Track(HandMeshFactory.BuildNail(high, 1));
            Assert.IsNotNull(nail);
            Assert.Greater(nail.vertexCount, 8, "指甲应有可辨识的曲面");
        }

        [Test]
        public void AllDetailLevels_Build()
        {
            foreach (AvatarMeshDetail detail in new[]
                     { AvatarMeshDetail.Low, AvatarMeshDetail.Medium, AvatarMeshDetail.High })
            {
                var def = Definition(detail: detail);
                Assert.IsNotNull(Track(HandMeshFactory.BuildPalm(def)), $"掌部网格构建失败：{detail}");

                for (int finger = 0; finger < HandMeshFactory.FingerCount; finger++)
                {
                    for (int segment = 0; segment < HandMeshFactory.SegmentCount; segment++)
                    {
                        Assert.IsNotNull(Track(HandMeshFactory.BuildFingerSegment(def, finger, segment)),
                            $"手指网格构建失败：{detail} / 指 {finger} / 段 {segment}");
                    }
                }
            }
        }

        // ==================== 三个角色 ====================

        [Test]
        public void ThreeCharacters_HaveIndependentHandProportions()
        {
            var female = AvatarDefinition.CreateDefault();

            var male = new AvatarDefinition { BodyHeight = 1.52f, BodyScale = 1.52f / AvatarBodyMetrics.ReferenceHeight };
            male.ApplyHandStyle(1.06f, 1.05f, 1.04f, 1.06f, 8f);

            var cartoon = new AvatarDefinition { BodyHeight = 1.26f, BodyScale = 1.26f / AvatarBodyMetrics.ReferenceHeight };
            cartoon.ApplyHandStyle(1.12f, 1.16f, 0.92f, 1.14f, 11f);

            Assert.Greater(male.Hand.PalmWidth, female.Hand.PalmWidth, "男性掌宽应大于女性");
            Assert.Greater(cartoon.Hand.PalmWidth, female.Hand.PalmWidth, "卡通角色掌宽应更大");
            Assert.Less(cartoon.Hand.FingerLength(2), female.Hand.FingerLength(2), "卡通角色手指应更短");

            var defs = new[] { female, male, cartoon };
            for (int i = 0; i < defs.Length; i++)
            {
                var meshDef = HandMeshDefinition.FromMetrics(defs[i].Hand, defs[i].BodyScale, 1f,
                    AvatarMeshDetail.Medium);
                Assert.IsNotNull(Track(HandMeshFactory.BuildPalm(meshDef)), $"角色 {i} 掌部构建失败");
                Assert.Greater(meshDef.SegmentLength(2, 0), 0.005f, $"角色 {i} 中指近节过短");
            }
        }

        // ==================== 比例校验 ====================

        [Test]
        public void HandMetrics_DefaultsAreValid()
        {
            Assert.IsNull(HandMetrics.CreateDefault().Validate());
        }

        [Test]
        public void HandMetrics_RejectsInvalidRatios()
        {
            var metrics = HandMetrics.CreateDefault();
            metrics.PalmWidth = 0.5f;
            Assert.IsNotNull(metrics.Validate(), "异常掌宽应被拒绝");

            var badRatios = HandMetrics.CreateDefault();
            badRatios.FingerSegmentRatios[0][0] = 0.9f;
            badRatios.FingerSegmentRatios[0][1] = 0.9f;
            badRatios.FingerSegmentRatios[0][2] = 0.9f;
            Assert.IsNotNull(badRatios.Validate(), "三段占比之和不为 1 应被拒绝");
        }

        [Test]
        public void HandMetrics_SegmentsSumToFingerLength()
        {
            var metrics = HandMetrics.CreateDefault();
            for (int finger = 0; finger < 5; finger++)
            {
                float sum = 0f;
                for (int segment = 0; segment < 3; segment++) sum += metrics.SegmentLength(finger, segment);
                Assert.AreEqual(metrics.FingerLength(finger), sum, 1e-5f,
                    $"第 {finger} 指三段长度之和应等于总长");
            }
        }

        // ==================== 关节限位 ====================

        [Test]
        public void JointLimits_DetectOverExtensionAndCurl()
        {
            var limits = FingerJointLimits.Default;

            Assert.IsNull(HandPoseValidator.ValidateJointAngles(30f, 20f, 10f, limits), "正常角度应通过");
            Assert.IsNotNull(HandPoseValidator.ValidateJointAngles(-8f, 0f, 0f, limits), "过伸应被拒绝");
            Assert.IsNotNull(HandPoseValidator.ValidateJointAngles(limits.MaxMcp + 5f, 0f, 0f, limits),
                "MCP 超上限应被拒绝");

            Assert.AreEqual(1, HandPoseValidator.CountJointLimitHits(limits.MaxMcp + 5f, 10f, 10f, limits));
            Assert.AreEqual(0, HandPoseValidator.CountJointLimitHits(10f, 10f, 10f, limits));
        }

        [Test]
        public void SurfaceDistance_IsSigned()
        {
            Assert.Greater(HandPoseValidator.SignedDistanceToSurface(0.80f, 0.75f), 0f, "高于键面应为正值");
            Assert.Less(HandPoseValidator.SignedDistanceToSurface(0.74f, 0.75f), 0f, "压入键面应为负值");
            Assert.IsTrue(HandPoseValidator.IsBelowSurface(0.740f, 0.750f, 0.004f), "低于键面超过容差应判为穿模");
            Assert.IsFalse(HandPoseValidator.IsBelowSurface(0.748f, 0.750f, 0.004f), "容差内不应判为穿模");
        }

        // ==================== 辅助 ====================

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }

        private static float MeanX(Mesh mesh)
        {
            var vertices = mesh.vertices;
            float sum = 0f;
            for (int i = 0; i < vertices.Length; i++) sum += vertices[i].x;
            return sum / Mathf.Max(1, vertices.Length);
        }
    }
}
