using System;
using System.Collections.Generic;
using PianoTeacher3D.Piano;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>根据人物三视图制作头部体积，并把原图五官和发丝投影到新表面。</summary>
    internal sealed class ConcertPortraitHead
    {
        // 各行依次是图像高度、正面半宽、侧面轮廓最前与最后的横坐标。
        private static readonly Vector4[] Profile =
        {
            new(122, 15, 862, 890), new(117, 14, 839, 894), new(111, 20, 832, 902),
            new(101, 27, 831, 910), new(92, 31, 831, 915), new(85, 34, 830, 917),
            new(75, 39, 833, 923), new(67, 42, 832, 923), new(58, 41, 830, 925),
            new(48, 40, 831, 922), new(38, 36, 834, 915), new(28, 29, 841, 904),
            new(21, 21, 850, 895), new(18.5f, 2.1f, 869, 880), new(18.4f, 0.5f, 873, 875)
        };

        private const float Pixel = 0.00222f;
        private readonly Transform _root;
        private readonly Transform _bone;
        private readonly CharacterResourceScope _scope;
        private readonly float _forward;

        internal ConcertPortraitHead(Transform root, Transform bone, float forward, CharacterResourceScope scope)
        {
            _root = root;
            _bone = bone;
            _forward = forward;
            _scope = scope;
        }

        internal bool TryBuild()
        {
            var map = Resources.Load<Texture2D>("StarStage/ConcertHeadReference");
            var shader = Resources.Load<Shader>("StarStage/ConcertHeadReference");
            if (map == null || shader == null) return false;
            var material = new Material(shader)
            {
                name = "女演奏者原图三面投影",
                mainTexture = map
            };
            _scope.Own(material);
            material.SetVector("_HeadForward", _bone.InverseTransformDirection(_root.TransformDirection(_forward * Vector3.forward)));
            var scalp = new StageMesh();
            scalp.Surface(122, 136, HeadPoint, (_, _) => Color.white, wrap: true);
            AddProjected("ReferencePerformerHead", scalp, material, false);
            var locks = new StageMesh();
            // 发髻外形靠后脑头骨，两个带波浪的体积在后方交错。
            AddHairVolume(locks, new Vector3(0, 91, 921), new Vector3(27, 21, 17), 1);
            AddHairVolume(locks, new Vector3(0, 79, 917), new Vector3(35, 13, 14), 2);
            AddProjected("BraidedChignon", locks, material, true);
            BuildWisps();
            return true;
        }

        private void AddHairVolume(StageMesh mesh, Vector3 center, Vector3 radius, int phase)
        {
            mesh.Surface(42, 78, (u, v) =>
            {
                float latitude = Mathf.Lerp(-1.566f, 1.566f, v);
                float azimuth = u * Mathf.PI * 2f;
                float scale = 1 + 0.031f * Mathf.Cos(azimuth * 11 + latitude * 5 + phase);
                return center + scale * Vector3.Scale(radius, new Vector3(
                    _forward * Mathf.Sin(azimuth) * Mathf.Cos(latitude),
                    -Mathf.Sin(latitude), -Mathf.Cos(azimuth) * Mathf.Cos(latitude)));
            }, (_, _) => Color.white, wrap: true);
        }

        private Vector3 HeadPoint(float u, float v)
        {
            float y = Mathf.Lerp(Profile[0].x, Profile[Profile.Length - 1].x, v);
            int row = 0;
            while (row + 2 < Profile.Length && y < Profile[row + 1].x) row++;
            Vector4 a = Profile[row];
            Vector4 b = Profile[row + 1];
            float w = Mathf.InverseLerp(a.x, b.x, y);
            Vector4 previous = Profile[Mathf.Max(0, row - 1)];
            Vector4 next = Profile[Mathf.Min(Profile.Length - 1, row + 2)];
            Vector4 tangent0 = (b - previous) * ((b.x - a.x) / (b.x - previous.x));
            Vector4 tangent1 = (next - a) * ((b.x - a.x) / (next.x - a.x));
            float w2 = w * w;
            float w3 = w2 * w;
            Vector4 p = (2 * w3 - 3 * w2 + 1) * a + (w3 - 2 * w2 + w) * tangent0
                + (-2 * w3 + 3 * w2) * b + (w3 - w2) * tangent1;
            float phi = u * Mathf.PI * 2f;
            float x = p.y * Mathf.Sin(phi) * _forward;
            float facing = Mathf.Cos(phi);
            float sideX = Mathf.Lerp(p.z, p.w, 0.5f - 0.5f * Mathf.Sign(facing) *
                Mathf.Pow(Mathf.Abs(facing), facing >= 0 ? 0.64f : 0.96f));
            if (facing > 0f)
            {
                float nose = 7.6f * Bell(x, 4.8f) * Bell(y - 85.2f, 5.5f);
                float bridge = 2.7f * Bell(x, 3.4f) * Bell(y - 76f, 6.5f);
                float eyeSocket = 1.6f * Bell(Mathf.Abs(x) - 13.1f, 5.2f) * Bell(y - 72.5f, 4.4f);
                float lip = 1.9f * Bell(x, 7.2f) * Bell(y - 100f, 2.4f);
                sideX += eyeSocket - nose - bridge - lip;
            }
            return new Vector3(x, y, sideX);
        }

        private static float Bell(float value, float span) => Mathf.Exp(-value * value / (span * span));

        private void AddProjected(string name, StageMesh geometry, Material material, bool bun)
        {
            var mesh = geometry.Build(name);
            var raw = mesh.vertices;
            var projected = new List<Vector4>(raw.Length);
            var weights = new List<Vector2>(raw.Length);
            var toBone = _bone.worldToLocalMatrix * _root.localToWorldMatrix;
            var origin = _root.InverseTransformPoint(_bone.position);
            for (int i = 0; i < raw.Length; i++)
            {
                Vector3 sample = raw[i];
                projected.Add(new Vector4(386.4f + sample.x * _forward, sample.y, sample.z, 1383.7f - sample.x * _forward));
                float depth = sample.z - 880f;
                float angle = depth / Mathf.Max(0.1f, Mathf.Sqrt(sample.x * sample.x + depth * depth));
                float front = bun ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.54f, -0.87f, angle));
                float rear = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.87f, angle));
                weights.Add(new Vector2(front, rear));
                raw[i] = toBone.MultiplyPoint3x4(origin + new Vector3(sample.x, 105f - sample.y,
                    (880f - sample.z) * _forward) * Pixel);
            }
            mesh.vertices = raw;
            mesh.SetUVs(0, weights);
            mesh.SetUVs(1, projected);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AddRenderer(name, mesh, material);
        }

        private void BuildWisps()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = "深棕碎发",
                color = new Color(0.19f, 0.147f, 0.128f),
                mainTexture = Texture2D.whiteTexture
            };
            material.SetFloat("_Smoothness", 0.27f);
            _scope.Own(material);

            var strands = new StageMesh();
            var origin = _root.InverseTransformPoint(_bone.position);
            for (int side = -1; side <= 1; side += 2)
            for (int strand = 0; strand < 7; strand++)
            {
                float lane = strand / 6f;
                Vector3 Wisp(float t) => origin + new Vector3(
                    side * (0.070f + 0.011f * Mathf.Sin(t * 2.3f * Mathf.PI) + lane * 0.008f),
                    0.086f - t * (0.154f - lane * 0.029f),
                    _forward * (0.046f + 0.019f * Mathf.Sin(0.28f + t * 4.85f) + lane * 0.007f));
                strands.Tube(45, 7, Wisp,
                    t => Mathf.Lerp(0.00117f - 0.00056f * lane, 0.00010f, t), _ => Color.white);
            }
            var mesh = strands.Build("TempleWisps");
            var points = mesh.vertices;
            var matrix = _bone.worldToLocalMatrix * _root.localToWorldMatrix;
            for (int i = 0; i < points.Length; i++) points[i] = matrix.MultiplyPoint3x4(points[i]);
            mesh.vertices = points;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AddRenderer("TempleWisps", mesh, material);
        }

        private void AddRenderer(string name, Mesh mesh, Material material)
        {
            var node = new GameObject(name);
            node.transform.SetParent(_bone, false);
            _scope.Own(mesh);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            node.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
