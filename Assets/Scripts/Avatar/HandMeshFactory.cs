using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 手部网格生成参数（评估文档附录 A.2 / A.3）。
    /// <para>
    /// 由 <see cref="HandMetrics"/>（每角色比例）+ 体型系数 + 网格精度档推导而来，
    /// 是<b>纯数据</b>：不依赖场景、可在 EditMode 单测中直接构造。
    /// </para>
    /// <para>
    /// <b>局部坐标约定</b>（与现有骨架一致，禁止只改手部坐标）：
    /// <c>+Z</c> = 手指伸出方向，<c>+Y</c> = 手背方向，<c>+X * Side</c> = 拇指侧。
    /// </para>
    /// </summary>
    public sealed class HandMeshDefinition
    {
        /// <summary>手性：+1 = 右手（局部 +X 为拇指侧），−1 = 左手（镜像）。</summary>
        public float Side = 1f;

        /// <summary>腕横纹 → 掌指关节的掌长（米，已含体型缩放）。</summary>
        public float PalmLength = AvatarBodyMetrics.PalmLength;

        /// <summary>掌宽（米）。</summary>
        public float PalmWidth = AvatarBodyMetrics.PalmWidth;

        /// <summary>掌厚（米）。</summary>
        public float PalmThickness = AvatarBodyMetrics.PalmThickness;

        /// <summary>腕部半径（米）：掌根收窄与手臂接缝的依据。</summary>
        public float WristRadius = AvatarBodyMetrics.WristRadius;

        /// <summary>各指近节根部半径（米）。</summary>
        public readonly float[] FingerRadii = new float[5];

        /// <summary>各指三段长度（米），索引 = finger * 3 + segment。</summary>
        public readonly float[] SegmentLengths = new float[15];

        /// <summary>截面环的圆周顶点数（越大越圆，近景质量与顶点数需实测权衡）。</summary>
        public int RingSegments = 10;

        /// <summary>掌部沿掌长的截面环数。</summary>
        public int PalmRings = 9;

        /// <summary>单个指节沿长度的截面环数。</summary>
        public int FingerRings = 5;

        /// <summary>是否生成指甲薄片（高精度档）。</summary>
        public bool IncludeNails;

        /// <summary>是否生成圆指尖半球（低精度档可关掉以省顶点）。</summary>
        public bool RoundTips = true;

        /// <summary>某指某段的长度（米）。</summary>
        public float SegmentLength(int finger, int segment)
        {
            int f = Mathf.Clamp(finger, 0, 4);
            int sg = Mathf.Clamp(segment, 0, 2);
            return SegmentLengths[f * 3 + sg];
        }

        /// <summary>某指某段根部半径（米）：沿指节逐段渐缩。</summary>
        public float SegmentRadius(int finger, int segment)
        {
            int f = Mathf.Clamp(finger, 0, 4);
            int sg = Mathf.Clamp(segment, 0, 2);
            return FingerRadii[f] * Mathf.Pow(0.88f, sg);
        }

        /// <summary>某指总长（米）。</summary>
        public float FingerLength(int finger)
        {
            int f = Mathf.Clamp(finger, 0, 4);
            return SegmentLengths[f * 3] + SegmentLengths[f * 3 + 1] + SegmentLengths[f * 3 + 2];
        }

        /// <summary>
        /// 由角色手部比例构建网格参数。
        /// </summary>
        /// <param name="metrics">角色手部比例（未乘体型系数）。</param>
        /// <param name="bodyScale">体型缩放（1 ≈ 1.45 m 坐姿基准）。</param>
        /// <param name="side">+1 = 右手，−1 = 左手。</param>
        /// <param name="detail">网格精度档。</param>
        public static HandMeshDefinition FromMetrics(HandMetrics metrics, float bodyScale, float side,
            AvatarMeshDetail detail)
        {
            var m = metrics ?? HandMetrics.CreateDefault();
            float s = Mathf.Max(0.1f, bodyScale) * Mathf.Max(0.1f, m.Scale);

            var d = new HandMeshDefinition
            {
                Side = side >= 0f ? 1f : -1f,
                PalmLength = m.PalmLength * s,
                PalmWidth = m.PalmWidth * s,
                PalmThickness = m.PalmThickness * s,
                WristRadius = m.WristRadius * s
            };

            for (int f = 0; f < 5; f++)
            {
                d.FingerRadii[f] = m.FingerRadii[f] * s;
                for (int sg = 0; sg < 3; sg++)
                    d.SegmentLengths[f * 3 + sg] = Mathf.Max(0.001f, m.FingerLengths[f] * m.FingerSegmentRatios[f][sg] * s);
            }

            switch (detail)
            {
                case AvatarMeshDetail.Low:
                    d.RingSegments = 8;
                    d.PalmRings = 6;
                    d.FingerRings = 3;
                    d.IncludeNails = false;
                    d.RoundTips = false;
                    break;
                case AvatarMeshDetail.High:
                    d.RingSegments = 14;
                    d.PalmRings = 12;
                    d.FingerRings = 7;
                    d.IncludeNails = true;
                    d.RoundTips = true;
                    break;
                default:
                    d.RingSegments = 10;
                    d.PalmRings = 9;
                    d.FingerRings = 5;
                    d.IncludeNails = false;
                    d.RoundTips = true;
                    break;
            }

            return d;
        }
    }

    /// <summary>
    /// 程序化手部网格工厂（评估文档附录 A.3「几何重绘」）。
    /// <para>
    /// 与改造前的「多个球体/胶囊拼接」相比，本工厂生成<b>连续截面放样</b>的单一网格：
    /// ① 掌部沿掌长有宽度/厚度剖面（腕部收窄 → 掌心变宽 → 掌指关节略收），截面为超椭圆（圆角矩形），
    ///    并在掌侧叠加有界的大鱼际 / 小鱼际隆起 —— 不再是几个椭球的并集；
    /// ② 每根手指按真实长度/粗细逐段渐缩，关节处轻微加宽，末节带半球形指尖（不是平切圆柱）；
    /// ③ 指甲是独立的浅色薄片，仅作视觉提示，<b>不参与</b>指尖目标点。
    /// </para>
    /// <para>
    /// 全部方法为纯函数（无场景依赖、无随机数、无时间），同样的输入必然生成同样的网格，可单测。
    /// 顶点数在 Low/Medium/High 三档之间由 <see cref="HandMeshDefinition"/> 控制。
    /// </para>
    /// </summary>
    public static class HandMeshFactory
    {
        /// <summary>手指数（0 = 拇指 … 4 = 小指）。</summary>
        public const int FingerCount = 5;

        /// <summary>每指段数（近节 / 中节 / 末节）。</summary>
        public const int SegmentCount = 3;

        /// <summary>掌部超椭圆的圆角指数：2/指数 为幂次，越大越接近矩形。</summary>
        private const float PalmSuperEllipse = 3.2f;

        // ==================== 掌部 ====================

        /// <summary>
        /// 生成掌部网格（含腕部连接段、掌根收窄、掌指关节端）。局部原点在腕横纹处，沿 +Z 延伸。
        /// </summary>
        public static Mesh BuildPalm(HandMeshDefinition d)
        {
            if (d == null) return null;

            int ring = Mathf.Clamp(d.RingSegments, 6, 24);
            int rings = Mathf.Clamp(d.PalmRings, 4, 20);

            var vertices = new List<Vector3>(rings * ring + 2);
            var triangles = new List<int>((rings - 1) * ring * 6 + ring * 6);

            float z0 = -d.WristRadius * 0.55f;      // 向腕后延伸，与手臂胶囊重叠
            float z1 = d.PalmLength * 1.02f;        // 略超掌指关节，被指根覆盖

            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                float z = Mathf.Lerp(z0, z1, t);

                float hw = 0.5f * d.PalmWidth * WidthProfile(t);
                float halfThick = 0.5f * d.PalmThickness * ThicknessProfile(t);

                // 拇指侧略宽（大鱼际），小指侧略窄（小鱼际）
                float hwThumb = hw * 1.06f;
                float hwPinky = hw * 0.94f;

                float thenar = ThenarBulge(t);
                float hypothenar = HypothenarBulge(t);
                float exponent = 2f / PalmSuperEllipse;

                for (int j = 0; j < ring; j++)
                {
                    float ang = (j / (float)ring) * Mathf.PI * 2f;
                    float cx = Mathf.Cos(ang);
                    float sy = Mathf.Sin(ang);

                    bool thumbSide = cx * d.Side >= 0f;
                    float sideHalf = thumbSide ? hwThumb : hwPinky;

                    float x = SignedPow(cx, exponent) * sideHalf;
                    float y = SignedPow(sy, exponent) * halfThick;

                    // 掌侧（−Y）隆起：大鱼际靠拇指根、小鱼际靠小指根
                    float bulge = thumbSide ? thenar : hypothenar;
                    if (bulge > 0f && y < 0f)
                    {
                        float w = -y / Mathf.Max(halfThick, 1e-5f);   // 0（掌缘）… 1（掌心）
                        x += Mathf.Sign(x) * sideHalf * 0.05f * bulge * w;
                        y -= halfThick * 0.22f * bulge * w;
                    }

                    vertices.Add(new Vector3(x, y, z));
                }
            }

            BridgeRings(triangles, ring, rings);
            AddCap(vertices, triangles, ring, 0, -1f, d.WristRadius * 0.18f);
            AddCap(vertices, triangles, ring, (rings - 1) * ring, 1f, d.PalmLength * 0.04f);

            return BuildMesh("HandPalm", vertices, triangles);
        }

        /// <summary>掌部宽度剖面：腕端 0.78 → 掌心 1.0 → 掌指关节 0.94（腕端不突然收细，与手臂连续）。</summary>
        private static float WidthProfile(float t)
        {
            float a = SmoothStep01(t / 0.70f);
            float b = SmoothStep01((t - 0.70f) / 0.30f);
            return 0.78f + 0.22f * a - 0.06f * b;
        }

        /// <summary>掌部厚度剖面：腕端 0.86 → 掌心 1.0 → 掌指 0.80（掌背略薄于掌心）。</summary>
        private static float ThicknessProfile(float t)
        {
            float a = SmoothStep01(t / 0.40f);
            float b = SmoothStep01((t - 0.40f) / 0.60f);
            return 0.86f + 0.14f * a - 0.20f * b;
        }

        /// <summary>大鱼际隆起（拇指根）：峰值在掌长 30% 处。</summary>
        private static float ThenarBulge(float t) => 0.85f * Gaussian(t, 0.30f, 0.26f);

        /// <summary>小鱼际隆起（小指根）：峰值略靠后，幅度更小。</summary>
        private static float HypothenarBulge(float t) => 0.50f * Gaussian(t, 0.44f, 0.24f);

        // ==================== 手指 ====================

        /// <summary>
        /// 生成某一指节的网格。<paramref name="segment"/> 0 = 近节、1 = 中节、2 = 末节（带圆指尖）。
        /// 局部原点在关节处，沿 +Z 延伸；起点向 −Z 略微回退以覆盖关节缝隙。
        /// </summary>
        public static Mesh BuildFingerSegment(HandMeshDefinition d, int finger, int segment)
        {
            if (d == null) return null;

            int ring = Mathf.Clamp(d.RingSegments - 1, 6, 22);
            int rings = Mathf.Clamp(d.FingerRings, 3, 14);

            float length = Mathf.Max(0.004f, d.SegmentLength(finger, segment));
            float baseRadius = Mathf.Max(0.002f, d.SegmentRadius(finger, segment));
            float endRadius = Mathf.Max(0.0015f, d.SegmentRadius(finger, segment) * 0.88f);

            var vertices = new List<Vector3>(rings * ring + ring + 1);
            var triangles = new List<int>((rings - 1) * ring * 6 + ring * 6);

            float z0 = -length * 0.07f;     // 回退：与上一段重叠，弯曲时不露缝
            float z1 = length;              // 与下一段的起点对齐

            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                float z = Mathf.Lerp(z0, z1, t);
                float r = Mathf.Lerp(baseRadius, endRadius, t) * JointBulge(t) * StartTaper(t);
                AddOvalRing(vertices, ring, z, r);
            }

            BridgeRings(triangles, ring, rings);

            bool roundTip = d.RoundTips && segment >= SegmentCount - 1;
            if (roundTip)
            {
                int tipRings = Mathf.Max(3, rings - 1);
                AddHemisphereCap(vertices, triangles, ring, (rings - 1) * ring, z1, endRadius, tipRings);
            }
            else
            {
                AddCap(vertices, triangles, ring, (rings - 1) * ring, 1f, endRadius * 0.30f);
            }

            AddCap(vertices, triangles, ring, 0, -1f, baseRadius * 0.18f);

            return BuildMesh($"HandFinger{finger}Seg{segment}", vertices, triangles);
        }

        /// <summary>指节起始处的渐入（避免与上一段的重叠区出现台阶）。</summary>
        private static float StartTaper(float t) => 0.90f + 0.10f * SmoothStep01(t / 0.18f);

        /// <summary>关节处轻微加宽：让指节有起伏，而不是等粗圆柱。</summary>
        private static float JointBulge(float t) => 1f + 0.055f * Gaussian(t, 0.06f, 0.10f);

        /// <summary>指甲薄片（仅视觉提示，不改变指尖目标点）。</summary>
        public static Mesh BuildNail(HandMeshDefinition d, int finger)
        {
            if (d == null) return null;

            float length = Mathf.Max(0.004f, d.SegmentLength(finger, 2));
            float radius = Mathf.Max(0.002f, d.SegmentRadius(finger, 2));

            var center = new Vector3(0f, radius * 0.74f, length * 0.58f);
            var radii = new Vector3(radius * 0.66f, radius * 0.24f, length * 0.34f);

            var vertices = new List<Vector3>(64);
            var triangles = new List<int>(128);
            AddEllipsoid(vertices, triangles, center, radii, 10, 5);

            return BuildMesh($"HandNail{finger}", vertices, triangles);
        }

        // ==================== 网格拼装辅助 ====================

        /// <summary>添加一圈椭圆截面（手指：横向略窄、屈伸方向略高）。</summary>
        private static void AddOvalRing(List<Vector3> vertices, int ring, float z, float radius)
        {
            for (int j = 0; j < ring; j++)
            {
                float ang = (j / (float)ring) * Mathf.PI * 2f;
                float x = Mathf.Cos(ang) * radius * 0.95f;
                float y = Mathf.Sin(ang) * radius;
                vertices.Add(new Vector3(x, y, z));
            }
        }

        /// <summary>把相邻两圈截面桥接成三角面。调用方保证顶点已按「圈连续」排列。</summary>
        private static void BridgeRings(List<int> triangles, int ring, int rings)
        {
            for (int i = 0; i < rings - 1; i++)
            {
                int start = i * ring;
                int next = (i + 1) * ring;
                for (int j = 0; j < ring; j++)
                {
                    int j1 = (j + 1) % ring;
                    int a = start + j;
                    int b = start + j1;
                    int c = next + j;
                    int dd = next + j1;

                    // 逆时针（自 +Z 看）→ 法线朝外
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(dd); triangles.Add(c);
                }
            }
        }

        /// <summary>在指定圈上封口（加一个中心顶点做三角扇）。<paramref name="direction"/> −1 = 朝 −Z。</summary>
        private static void AddCap(List<Vector3> vertices, List<int> triangles, int ring,
            int startIndex, float direction, float depth)
        {
            float z = vertices[startIndex].z + direction * depth;
            int center = vertices.Count;
            vertices.Add(new Vector3(0f, 0f, z));

            for (int j = 0; j < ring; j++)
            {
                int j1 = (j + 1) % ring;
                if (direction < 0f)
                {
                    triangles.Add(center); triangles.Add(startIndex + j1); triangles.Add(startIndex + j);
                }
                else
                {
                    triangles.Add(center); triangles.Add(startIndex + j); triangles.Add(startIndex + j1);
                }
            }
        }

        /// <summary>在末节末端追加半球形指尖（圆润收口，不是平切圆柱）。</summary>
        private static void AddHemisphereCap(List<Vector3> vertices, List<int> triangles, int ring,
            int startIndex, float z, float radius, int capRings)
        {
            int prevStart = startIndex;
            for (int k = 1; k < capRings; k++)
            {
                float a = (k / (float)capRings) * Mathf.PI * 0.5f;
                float ringRadius = radius * Mathf.Cos(a) * 0.98f;
                float ringZ = z + radius * Mathf.Sin(a);

                int curStart = vertices.Count;
                AddOvalRing(vertices, ring, ringZ, ringRadius);

                for (int j = 0; j < ring; j++)
                {
                    int j1 = (j + 1) % ring;
                    int a0 = prevStart + j;
                    int b0 = prevStart + j1;
                    int c0 = curStart + j;
                    int d0 = curStart + j1;
                    triangles.Add(a0); triangles.Add(b0); triangles.Add(c0);
                    triangles.Add(b0); triangles.Add(d0); triangles.Add(c0);
                }
                prevStart = curStart;
            }

            // 顶点
            int apex = vertices.Count;
            vertices.Add(new Vector3(0f, 0f, z + radius));
            for (int j = 0; j < ring; j++)
            {
                int j1 = (j + 1) % ring;
                triangles.Add(prevStart + j); triangles.Add(prevStart + j1); triangles.Add(apex);
            }
        }

        /// <summary>椭球（用于指甲薄片）：沿 +Y 压扁的 UV 球。</summary>
        private static void AddEllipsoid(List<Vector3> vertices, List<int> triangles,
            Vector3 center, Vector3 radii, int ring, int stacks)
        {
            int top = vertices.Count;
            vertices.Add(center + new Vector3(0f, radii.y, 0f));

            int gridStart = vertices.Count;
            for (int i = 1; i < stacks; i++)
            {
                float phi = (i / (float)stacks) * Mathf.PI;
                float y = Mathf.Cos(phi);
                float r = Mathf.Sin(phi);
                for (int j = 0; j < ring; j++)
                {
                    float theta = (j / (float)ring) * Mathf.PI * 2f;
                    vertices.Add(center + new Vector3(
                        Mathf.Cos(theta) * r * radii.x,
                        y * radii.y,
                        Mathf.Sin(theta) * r * radii.z));
                }
            }

            int bottom = vertices.Count;
            vertices.Add(center + new Vector3(0f, -radii.y, 0f));

            int gridRings = stacks - 1;
            for (int i = 0; i < gridRings - 1; i++)
            {
                int a = gridStart + i * ring;
                int b = gridStart + (i + 1) * ring;
                for (int j = 0; j < ring; j++)
                {
                    int j1 = (j + 1) % ring;
                    triangles.Add(a + j); triangles.Add(a + j1); triangles.Add(b + j);
                    triangles.Add(a + j1); triangles.Add(b + j1); triangles.Add(b + j);
                }
            }

            for (int j = 0; j < ring; j++)
            {
                int j1 = (j + 1) % ring;
                triangles.Add(top); triangles.Add(j + gridStart); triangles.Add(j1 + gridStart);
                int last = gridStart + (gridRings - 1) * ring;
                triangles.Add(bottom); triangles.Add(last + j); triangles.Add(last + j1);
            }
        }

        private static Mesh BuildMesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            if (vertices == null || vertices.Count < 3 || triangles == null || triangles.Count < 3)
                return null;

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ==================== 数学 ====================

        /// <summary>带符号幂：保留符号的 |x|^p（用于超椭圆截面）。</summary>
        private static float SignedPow(float v, float p)
        {
            if (v >= 0f) return Mathf.Pow(v, p);
            return -Mathf.Pow(-v, p);
        }

        /// <summary>平滑阶跃：0 → 1，两端一阶导为 0。</summary>
        private static float SmoothStep01(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            return x * x * (3f - 2f * x);
        }

        /// <summary>高斯隆起：<paramref name="center"/> 处为 1，<paramref name="sigma"/> 控制宽度。</summary>
        private static float Gaussian(float x, float center, float sigma)
        {
            float d = (x - center) / Mathf.Max(1e-4f, sigma);
            return Mathf.Exp(-d * d);
        }
    }
}
