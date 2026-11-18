using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PianoTeacher3D.Piano
{
    /// <summary>舞台外侧的海底景物，坐标以圆台为中心。生物和海床使用独立的三维网格。</summary>
    internal sealed class UnderwaterStage : MonoBehaviour
    {
        private static readonly Vector3 Facing = new(0.9135f, 0f, 0.4067f);
        private static readonly Vector3 Right = new(0.4067f, 0f, -0.9135f);
        private static readonly Vector3 SunOrigin = new(1.32f, 6.6f, -0.75f);
        private static readonly Vector3 SunTarget = new(0.12f, -0.02f, -0.28f);
        private static readonly int SunPosition = Shader.PropertyToID("_Sun");
        private static readonly int SunFocus = Shader.PropertyToID("_SunTarget");
        private static readonly int Sequence = Shader.PropertyToID("_StageSequence");
        private static readonly int WaterTint = Shader.PropertyToID("_WaterTint");
        private static readonly int Glow = Shader.PropertyToID("_Glow");
        private Transform _whale;
        private Vector3 _platformCenter;
        private readonly Transform[] _shoals = new Transform[2];
        private Transform _jellyfish;
        // 2026-11-18 参考图二：再加两只小水母；鲸鱼在琴后缓慢绕竖直圆环上升；生物随音乐律动
        private Transform[] _jellies = System.Array.Empty<Transform>();
        private Transform _piano;
        private readonly float[] _shoalPhase = new float[2];
        private float _jellyPhase;
        private float _lastMoveTime = -1f;
        private Material _sun;
        private Material _habitat;
        private Material _amber;
        private Material[] _scenery;
        private bool _hasCue;
        private StageCue _cue;

        internal void Build(Shader sceneryShader, Shader sunShader, Renderer platform, Func<Mesh, Mesh> keepMesh,
            Func<Material, Material> keepMaterial)
        {
            _habitat = keepMaterial(new Material(sceneryShader) { name = "海底生物" });
            var sediment = keepMaterial(new Material(sceneryShader) { name = "海床与礁石" });
            sediment.SetFloat("_Sediment", 1f);
            var whaleSkin = keepMaterial(new Material(sceneryShader) { name = "座头鲸灰蓝皮肤" });
            whaleSkin.SetFloat("_Whale", 1f);
            whaleSkin.SetFloat("_Shine", 0.13f);
            _amber = keepMaterial(new Material(sceneryShader) { name = "水母" });
            _amber.SetFloat(Glow, 0.85f);
            _amber.SetFloat("_Shine", 0.75f);
            var stone = keepMaterial(new Material(sceneryShader) { name = "湿玄武岩琴台" });
            stone.SetColor("_BaseTint", new Color(0.035f, 0.078f, 0.105f));
            stone.SetFloat("_UseVertexColor", 0f);
            stone.SetFloat("_Shine", 1.05f);
            stone.SetFloat("_Sediment", 0.14f);
            stone.SetFloat("_StageSurface", 1f);
            if (platform != null)
            {
                platform.sharedMaterial = stone;
                _platformCenter = transform.InverseTransformPoint(platform.transform.position);
            }
            _scenery = new[] { _habitat, sediment, _amber, stone, whaleSkin };
            foreach (var mat in _scenery)
            {
                mat.SetVector(SunPosition, transform.TransformPoint(SunOrigin));
                mat.SetVector(SunFocus, transform.TransformPoint(SunTarget));
            }

            var bed = new StageMesh();
            bed.Surface(68, 68, (u, v) =>
            {
                float x = (u - 0.5f) * 42f;
                float z = (v - 0.5f) * 42f;
                float shelf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.25f, 4.5f, new Vector2(x, z + 0.35f).magnitude));
                float sand = Mathf.PerlinNoise(x * 0.39f + 71f, z * 0.42f + 18f);
                return new Vector3(x, -0.26f - shelf * (0.60f + sand * 0.65f) + sand * 0.075f, z);
            }, (_, _) => new Color(0.078f, 0.104f, 0.101f));
            BuildReef(bed);
            Part("SeabedAndReef", keepMesh(bed.Build("SeabedAndReef")), sediment);
            Part("CoralGarden", keepMesh(BuildCoral()), _habitat);
            Part("KelpForest", keepMesh(BuildKelp()), _habitat);

            _whale = Part("HumpbackWhale", keepMesh(BuildWhale()), whaleSkin);
            for (int i = 0; i < _shoals.Length; i++)
                _shoals[i] = Part("FishShoal" + (i + 1), keepMesh(BuildShoal(i)), _habitat);
            var jellyMesh = keepMesh(BuildJellyfish());
            _jellyfish = Part("AmberJellyfish", jellyMesh, _amber);
            _jellies = new[]
            {
                _jellyfish,
                Part("AmberJellyfish2", jellyMesh, _amber),
                Part("AmberJellyfish3", jellyMesh, _amber)
            };
            Part("WaterParticles", keepMesh(BuildParticles()), _habitat);
            BuildSun(sunShader, keepMaterial);
            Move(0f);
        }

        internal void SetQuality(bool high)
        {
            if (_sun != null) _sun.SetFloat("_Effects", high ? 1f : 0.62f);
            if (_habitat != null) _habitat.SetFloat("_Effects", high ? 1f : 0.76f);
        }

        /// <summary>按曲目时间采样场景。空值恢复闲置展示时的场景循环。</summary>
        internal void ApplyStageCue(StageCue? cue)
        {
            if (_sun == null || (!cue.HasValue && !_hasCue)) return;
            _hasCue = cue.HasValue;
            _cue = cue.GetValueOrDefault();
            float offset = _hasCue ? _cue.SunOffset : 0f;
            Vector3 source = transform.TransformPoint(SunOrigin + Right * (offset * 1.3f));
            Vector3 target = transform.TransformPoint(SunTarget + Right * (offset * 0.45f));
            var sequence = _hasCue
                ? new Vector4(_cue.Seconds, _cue.SunStrength, _cue.FogDensity, 0f)
                : new Vector4(-1f, 1f, 1f, 0f);
            Vector4 tint = _hasCue ? (Vector4)_cue.WaterTint : Vector4.one;
            foreach (var material in _scenery)
            {
                ApplySunAndTime(material, source, target, sequence);
                material.SetVector(WaterTint, tint);
            }
            ApplySunAndTime(_sun, source, target, sequence);
            _sun.SetVector(WaterTint, tint);
            _amber.SetFloat(Glow, _hasCue ? _cue.JellyGlow : 0.85f);
            Move(_hasCue ? _cue.Seconds : Time.time);
        }

        private static void ApplySunAndTime(Material material, Vector3 source, Vector3 target, Vector4 sequence)
        {
            material.SetVector(SunPosition, source);
            material.SetVector(SunFocus, target);
            material.SetVector(Sequence, sequence);
        }

        private Transform Part(string label, Mesh mesh, Material material)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return go.transform;
        }

        private void BuildSun(Shader shader, Func<Material, Material> keepMaterial)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "SunlightVolume";
            box.transform.SetParent(transform, false);
            box.transform.localPosition = new Vector3(0f, 2.6f, -0.6f);
            box.transform.localScale = new Vector3(14f, 8.4f, 14f);
            var collider = box.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
            _sun = keepMaterial(new Material(shader) { name = "水下日光散射" });
            _sun.SetVector("_Sun", transform.TransformPoint(SunOrigin));
            _sun.SetVector("_SunTarget", transform.TransformPoint(SunTarget));
            var renderer = box.GetComponent<Renderer>();
            renderer.sharedMaterial = _sun;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Vector3 Across(float side, float depth, float height) => Right * side - Facing * depth + Vector3.up * height;

        private static void BuildReef(StageMesh mesh)
        {
            var random = new System.Random(4248);
            for (int i = 0; i < 49; i++)
            {
                float angle = Range(random, 0f, Mathf.PI * 2f);
                float ring = Range(random, 2.45f, 11.5f);
                Vector3 center = new(Mathf.Cos(angle) * ring, -1.04f, Mathf.Sin(angle) * ring);
                float size = Range(random, 0.26f, 1.06f);
                var dim = new Vector3(size * Range(random, 0.85f, 1.65f), size * Range(random, 0.39f, 0.90f), size);
                var color = Color.Lerp(new Color(0.032f, 0.063f, 0.075f), new Color(0.12f, 0.16f, 0.15f),
                    Range(random, 0f, 1f));
                mesh.Ellipsoid(center, dim, Quaternion.Euler(15f, angle * Mathf.Rad2Deg, -11f), color, 18, 28, 0.29f, i);
            }
            // 左右岩脊形成开口。靠琴台的一侧更低，留出演奏者和琴键的视线。
            for (int i = 0; i < 13; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float depth = 3.1f + i * 0.72f;
                Vector3 center = Across(side * (2.76f + 0.19f * i), depth, -0.88f);
                mesh.Ellipsoid(center, new Vector3(1.19f + 0.081f * i, 0.93f + i * 0.085f, 1.53f),
                    Quaternion.Euler(i * 6f, i * 17f, i * -6f), new Color(0.049f, 0.070f, 0.085f), 20, 28, 0.28f, i + 62f);
            }
        }

        private static Mesh BuildCoral()
        {
            var coral = new StageMesh();
            var random = new System.Random(5673);
            var warm = new Color(0.56f, 0.177f, 0.100f);
            var tip = new Color(0.85f, 0.48f, 0.24f);
            var violet = new Color(0.28f, 0.095f, 0.17f);
            for (int i = 0; i < 17; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Vector3 foot = Across(side * Range(random, 1.66f, 3.45f), Range(random, -1.65f, 4.5f),
                    Range(random, -0.90f, -0.55f));
                float height = Range(random, 0.30f, 0.86f);
                Color hue = Color.Lerp(violet, warm, Range(random, 0.18f, 1f));
                for (int j = 0; j < 6; j++)
                {
                    float theta = j * 2.39f + i;
                    var fan = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta)) * height * Range(random, 0.35f, 0.63f);
                    var trunk = fan * 0.35f + Vector3.up * height * Range(random, 0.53f, 0.84f);
                    Vector3 Path(float t) => foot + trunk * t + fan * (t * t) + Right * (height * 0.10f * Mathf.Sin(t * 3.2f));
                    coral.Tube(12, 8, Path, t => Mathf.Lerp(height * 0.076f, height * 0.024f, t),
                        t => Color.Lerp(hue * 0.61f, tip, t * t * 0.86f));
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 fork = Path(0.42f + k * 0.17f);
                        var branch = Quaternion.Euler(0f, 38f * (k - 1), 0f) * fan * (0.49f - k * 0.08f);
                        Vector3 Fork(float t) => fork + branch * (t + 0.25f * t * t) + Vector3.up * height * t * (0.46f - k * 0.05f);
                        coral.Tube(9, 7, Fork, t => Mathf.Lerp(height * 0.037f, 0.006f, t), t => Color.Lerp(hue, tip, t));
                        coral.Ellipsoid(Fork(1f), Vector3.one * (height * 0.022f), Quaternion.identity, tip, 5, 8);
                    }
                }
            }
            for (int i = 0; i < 10; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Vector3 root = Across(side * Range(random, 1.75f, 3.1f), Range(random, -1.0f, 3.5f), Range(random, -0.82f, -0.48f));
                float size = Range(random, 0.24f, 0.49f);
                Color red = Color.Lerp(new Color(0.32f, 0.085f, 0.115f), new Color(0.61f, 0.26f, 0.13f), Range(random, 0f, 1f));
                for (int n = 0; n < 12; n++)
                {
                    float rotation = n * 2.4f;
                    float radius = size * (0.12f + 0.08f * n);
                    var offset = new Vector3(Mathf.Cos(rotation) * radius, size * (0.39f + 0.24f * Mathf.Cos(n)), Mathf.Sin(rotation) * radius);
                    coral.Ellipsoid(root + offset, size * new Vector3(0.36f, 0.59f, 0.36f),
                        Quaternion.Euler(14f, n * 41f, 19f), red, 10, 14, 0.23f, i * 14f + n);
                }
            }
            return coral.Build("CoralGarden");
        }

        private static Mesh BuildKelp()
        {
            var kelp = new StageMesh();
            var random = new System.Random(5789);
            // 参考图二：海带由暗琥珀渐变到金黄，逆光下像发光的金色缎带
            Color dark = new(0.16f, 0.10f, 0.025f);
            Color gold = new(0.86f, 0.58f, 0.14f);
            for (int i = 0; i < 22; i++)
            {
                float sign = i % 2 == 0 ? -1f : 1f;
                float edge = Range(random, 2.2f, 3.9f);
                var foot = Across(edge * sign, Range(random, -0.7f, 4.5f), -0.89f);
                float height = Range(random, 1.7f, 4.65f);
                var veer = Across(sign * Range(random, 0.32f, 0.59f), Range(random, -0.25f, 0.30f), 0f);
                Vector3 Stem(float v) => foot + Vector3.up * height * v + veer * (v * v - 0.32f * v) +
                    Right * (0.13f * Mathf.Sin(v * 9.6f + i) * v);
                kelp.Tube(22, 6, Stem, v => Mathf.Lerp(0.023f, 0.005f, v), v => Color.Lerp(dark, gold, v),
                    v => new Vector2(v, 2f));
                int leaves = 14 + i % 4;
                for (int leaf = 0; leaf < leaves; leaf++)
                {
                    float h = (leaf + 1f) / (leaves + 1f);
                    Vector3 node = Stem(h);
                    float size = Range(random, 0.29f, 0.65f) * Mathf.Lerp(1f, 0.43f, h);
                    float twist = Range(random, -23f, 23f);
                    Vector3 side = Quaternion.Euler(0f, twist, 0f) * (Right * (leaf % 2 == 0 ? -1f : 1f));
                    Vector3 width = Vector3.Cross(Vector3.up, side);
                    kelp.Surface(12, 5, (u, v) =>
                    {
                        float wave = Mathf.Sin(v * 8.4f + h * 16f);
                        float blade = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(v * Mathf.PI)), 0.78f);
                        return node + side * size * v + Vector3.up * size * (v * 0.47f + 0.15f * wave * v) +
                            width * blade * size * 0.28f * (2f * u - 1f);
                    }, (u, v) => Color.Lerp(dark, gold, (0.34f + 0.66f * h) * (0.68f + 0.32f * v)) *
                        (0.87f + 0.13f * (1f - Mathf.Abs(2f * u - 1f))), (_, v) => new Vector2(h + (1f - h) * 0.24f * v, 2f));
                }
            }
            return kelp.Build("KelpForest");
        }

        private static readonly Vector3[] WhaleSections =
        {
            new(-3.65f, 0.045f, 0.13f), new(-3.17f, 0.14f, 0.19f), new(-2.6f, 0.31f, 0.28f),
            new(-1.9f, 0.49f, 0.46f), new(-1.05f, 0.67f, 0.68f), new(-0.03f, 0.79f, 0.77f),
            new(0.93f, 0.74f, 0.71f), new(1.71f, 0.55f, 0.65f), new(2.25f, 0.44f, 0.61f),
            new(2.65f, 0.32f, 0.50f), new(2.93f, 0.20f, 0.31f), new(3.07f, 0.018f, 0.030f)
        };

        private static Vector3 WhaleSection(float t)
        {
            float at = t * (WhaleSections.Length - 1);
            int a = Mathf.Min((int)at, WhaleSections.Length - 2);
            float x = at - a;
            Vector3 p0 = WhaleSections[Mathf.Max(a - 1, 0)];
            Vector3 p1 = WhaleSections[a];
            Vector3 p2 = WhaleSections[a + 1];
            Vector3 p3 = WhaleSections[Mathf.Min(a + 2, WhaleSections.Length - 1)];
            return 0.5f * ((2f * p1) + (-p0 + p2) * x +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * x * x + (-p0 + 3f * p1 - 3f * p2 + p3) * x * x * x);
        }

        private static Mesh BuildWhale()
        {
            var mesh = new StageMesh();
            Color back = new(0.050f, 0.077f, 0.110f);
            Color flank = new(0.12f, 0.168f, 0.21f);
            Color throat = new(0.44f, 0.49f, 0.53f);
            mesh.Surface(85, 64, (u, v) =>
            {
                Vector3 s = WhaleSection(v);
                float y = Mathf.Cos(u * Mathf.PI * 2f);
                float z = Mathf.Sin(u * Mathf.PI * 2f);
                float pleat = Mathf.Sin(u * Mathf.PI * 66f) * 0.020f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.24f, -0.73f, y)) *
                    Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-1.1f, 0.62f, s.x)) * Mathf.InverseLerp(3f, 1.65f, s.x);
                return new Vector3(s.x, y * (s.y + pleat) + 0.13f * Mathf.SmoothStep(0f, 1f, v),
                    z * (s.z + pleat));
            }, (u, v) =>
            {
                float underside = Mathf.InverseLerp(-0.28f, -0.86f, Mathf.Cos(u * Mathf.PI * 2f));
                float mottling = Mathf.PerlinNoise(u * 57f, v * 24f + 2.6f);
                var skin = Color.Lerp(back, flank, 0.30f + 0.69f * mottling);
                var ventral = Color.Lerp(skin, throat, underside * (0.33f + 0.63f * Mathf.InverseLerp(0.24f, 0.75f, v)));
                return ventral * (0.75f + 0.24f * mottling);
            }, wrap: true);

            // 左右胸鳍很长，呈座头鲸特有的狭长倒翼，前缘带圆形起伏。
            foreach (int sign in new[] { -1, 1 })
            {
                mesh.Surface(30, 26, (u, v) =>
                {
                    float around = u * Mathf.PI * 2f;
                    float reach = Mathf.Sin(v * Mathf.PI * 0.98f);
                    float x = 0.67f - v * 1.59f - 0.76f * v * v;
                    float width = (0.28f + 0.19f * reach) * (1f - 0.975f * v);
                    float notch = 1f + Mathf.Cos(v * 56f) * 0.11f * Mathf.InverseLerp(0f, 0.3f, Mathf.Cos(around));
                    return new Vector3(x + Mathf.Cos(around) * width * notch,
                        -0.39f - 0.70f * v + 0.068f * Mathf.Sin(around) * reach, sign * (0.49f + 2.04f * v));
                }, (u, v) => Color.Lerp(flank, throat, Mathf.InverseLerp(0.2f, -0.82f, Mathf.Sin(u * Mathf.PI * 2f)) *
                    (0.56f + 0.25f * Mathf.Sin(v * 6.3f))), (_, v) => new Vector2(v, 6f), wrap: true);

                mesh.Surface(28, 22, (u, v) =>
                {
                    float around = u * Mathf.PI * 2f;
                    float width = Mathf.Lerp(0.51f, 0.019f, v) * (1f + 0.25f * Mathf.Sin(v * Mathf.PI));
                    return new Vector3(-3.61f - v * 0.39f - v * v * 0.42f + Mathf.Cos(around) * width,
                        0.13f + 0.23f * v * v + 0.095f * Mathf.Sin(around) * (1f - 0.94f * v),
                        sign * (0.05f + 1.34f * v));
                }, (u, v) => Color.Lerp(back, flank, Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2f)) * v),
                    wrap: true);

                // 唇线和眼睛都有体积，侧镜头不会沿背景球漂移。
                mesh.Tube(24, 6, t => new Vector3(1.52f + 1.42f * t, -0.13f + 0.195f * t, sign * (0.651f - 0.36f * t * t * t)),
                    t => Mathf.Lerp(0.017f, 0.008f, t), _ => new Color(0.024f, 0.054f, 0.072f));
                mesh.Ellipsoid(new Vector3(1.58f, 0.18f, sign * 0.653f), new Vector3(0.066f, 0.053f, 0.028f),
                    Quaternion.identity, new Color(0.008f, 0.020f, 0.027f), 11, 16);
            }
            for (int i = 0; i < 11; i++)
            {
                float bias = i / 10f;
                float x = Mathf.Lerp(1.95f, 2.78f, bias);
                float y = Mathf.Lerp(0.55f, 0.429f, bias);
                float z = 0.24f * Mathf.Sin(i * 3.20f) + 0.21f * ((i % 3) - 1);
                mesh.Ellipsoid(new Vector3(x, y - 0.10f * Mathf.Abs(z), z), new Vector3(0.071f, 0.041f, 0.056f),
                    Quaternion.identity, (i % 3 == 1 ? flank : back) * 1.15f, 8, 13);
            }

            // 小型背鳍从背脊上缓慢抬起，区别于鲨鱼的高三角背鳍。
            mesh.Surface(21, 20, (u, v) =>
            {
                float a = u * Mathf.PI * 2f;
                return new Vector3(-1.47f - 0.51f * v + (0.53f * (1f - v) + 0.015f) * Mathf.Cos(a),
                    0.52f + v * 0.44f, Mathf.Sin(a) * (0.14f * (1f - v) + 0.01f));
            }, (u, v) => Color.Lerp(back, flank, v * 0.3f), wrap: true);
            return mesh.Build("HumpbackWhale");
        }

        private static Mesh BuildShoal(int school)
        {
            var fish = new StageMesh();
            var random = new System.Random(617 + school);
            int count = school == 0 ? 32 : 42;
            for (int i = 0; i < count; i++)
            {
                float x = Range(random, -1.85f, 1.85f);
                float band = 1f - Mathf.Abs(x) / 2.55f;
                Vector3 center = new(x, Range(random, -0.58f, 0.58f) * band + Mathf.Sin(x * 1.4f) * 0.16f,
                    Range(random, -0.50f, 0.50f) * band);
                float length = Range(random, 0.124f, 0.26f) * (school == 0 ? 1f : 0.85f);
                float motion = 3f + Mathf.Repeat(i * 0.618034f, 1f);
                Color hue = Color.Lerp(new Color(0.18f, 0.35f, 0.36f), new Color(0.54f, 0.67f, 0.61f), Range(random, 0f, 1f));
                fish.Surface(12, 14, (u, v) =>
                {
                    float a = u * Mathf.PI * 2f;
                    float section = Mathf.Pow(Mathf.Sin(v * Mathf.PI) + 0.0001f, 0.67f) *
                        Mathf.Lerp(0.58f, 1.08f, Mathf.SmoothStep(0f, 1f, v));
                    return center + length * new Vector3(v - 0.50f,
                        Mathf.Cos(a) * 0.23f * section, Mathf.Sin(a) * 0.13f * section);
                }, (u, v) =>
                {
                    float back = Mathf.Cos(u * Mathf.PI * 2f);
                    float stripe = Mathf.Exp(-Mathf.Pow((back + 0.14f) * 5.0f, 2));
                    return hue * Mathf.Lerp(1.16f, 0.48f, (back + 1f) * 0.50f) +
                        stripe * new Color(0.10f, 0.20f, 0.19f);
                }, (_, v) => new Vector2((1f - v) * (1f - v) * length * 0.57f, motion), wrap: true);
                foreach (int side in new[] { -1, 1 })
                {
                    fish.Ellipsoid(center + length * new Vector3(0.30f, 0.07f, side * 0.099f),
                        length * new Vector3(0.045f, 0.045f, 0.026f), Quaternion.identity,
                        new Color(0.026f, 0.041f, 0.049f), 3, 6, motion: new Vector2(0f, motion));
                    fish.Surface(4, 4, (u, v) => center + length * new Vector3(0.13f - v * 0.23f - u * 0.11f,
                        -0.054f - 0.097f * v, side * (0.10f + v * 0.16f * Mathf.Sin(u * Mathf.PI))),
                        (_, v) => hue * Mathf.Lerp(0.80f, 0.50f, v), (_, v) => new Vector2(v * length * 0.12f, motion));
                }
                fish.Surface(5, 5, (u, v) =>
                    center + new Vector3(-length * (0.45f + v * (0.35f - 0.14f * (1f - Mathf.Abs(2f * u - 1f)))),
                        length * (2f * u - 1f) * (0.044f + 0.18f * v), 0f),
                    (_, v) => Color.Lerp(hue * 0.49f, hue * 0.85f, v),
                    (_, v) => new Vector2(Mathf.Lerp(0.54f, 1f, v) * length, motion));
            }
            return fish.Build("School" + school);
        }

        private static Mesh BuildJellyfish()
        {
            var jelly = new StageMesh();
            // 参考图二：金黄色发光水母（伞盖内亮外暗）
            Color bronze = new(0.52f, 0.30f, 0.05f);
            Color peach = new(1.0f, 0.84f, 0.34f);
            jelly.Surface(20, 38, (u, v) =>
            {
                float a = u * Mathf.PI * 2f;
                float r = Mathf.Sin(v * 1.59f) * 0.221f * (1f + Mathf.Cos(a * 10f) * 0.047f);
                return new Vector3(r * Mathf.Cos(a), 0.146f * Mathf.Cos(v * 1.59f), r * Mathf.Sin(a));
            }, (u, v) => Color.Lerp(bronze, peach, (0.54f + 0.46f * Mathf.Cos(u * Mathf.PI * 20f)) *
                Mathf.SmoothStep(0.20f, 1f, v)), wrap: true);
            for (int i = 0; i < 13; i++)
            {
                float angle = i * 2.4f;
                var edge = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float scale = 0.31f + (i % 5) * 0.086f;
                jelly.Tube(19, 5, t => edge * (0.17f - t * 0.12f) +
                    Vector3.up * (-t * scale) + Vector3.right * (0.051f * t * Mathf.Sin(t * 5.9f + angle)),
                    t => Mathf.Lerp(0.0075f, 0.0019f, t), t => Color.Lerp(peach * 0.85f, bronze * 0.65f, t),
                    t => new Vector2(t, 4f));
            }
            return jelly.Build("AmberJellyfish");
        }

        private static Mesh BuildParticles()
        {
            var particles = new StageMesh();
            var random = new System.Random(2914);
            for (int i = 0; i < 120; i++)
            {
                var center = Across(Range(random, -7f, 7f), Range(random, -1f, 12f), Range(random, -0.55f, 4.1f));
                float size = Range(random, 0.003f, 0.011f);
                particles.Ellipsoid(center, Vector3.one * size, Quaternion.identity, new Color(0.30f, 0.48f, 0.46f),
                    4, 6, motion: new Vector2(i * 0.618034f, 5f));
            }
            return particles.Build("WaterParticles");
        }

        private void Update()
        {
            if (!_hasCue) Move(Time.time);
        }

        private void Move(float time)
        {
            if (_whale == null) return;
            if (!_hasCue)
            {
                MoveIdle(time);
                return;
            }
            float turn = time * 0.072f;
            float side = 5.4f * Mathf.Sin(turn);
            float depth = 7.5f + 2.9f * (1f - Mathf.Cos(turn));
            Vector3 heading = Across(5.4f * Mathf.Cos(turn), 2.9f * Mathf.Sin(turn), 0f);
            if (_hasCue)
            {
                float crossing = _cue.WhaleCrossing;
                side = Mathf.Lerp(-4.17f, 4.6f, crossing);
                depth = 7.15f + (1f - Mathf.Sin(crossing * Mathf.PI)) * 1.55f;
                heading = Across(8.77f, -1.55f * Mathf.PI * Mathf.Cos(crossing * Mathf.PI), 0f);
            }
            float stroke = time * 1.38f;
            _whale.localPosition = Across(side, depth, 2.05f + Mathf.Sin(time * 0.24f) * 0.18f + Mathf.Sin(stroke) * 0.055f);
            _whale.localRotation = SwimRotation(heading, 2.8f * Mathf.Sin(stroke - 0.6f),
                6f * Mathf.Sin(time * 0.38f));
            for (int i = 0; i < _shoals.Length; i++)
            {
                float speed = i == 0 ? 0.36f : -0.23f;
                float arc = time * speed + i * 2.71f;
                float radius = 4.15f + i * 3.2f + (_hasCue ? (1f - _cue.ShoalApproach) * 0.37f : 0f);
                float rise = 2.48f + i * 0.64f + 0.28f * Mathf.Sin(arc * 2f);
                _shoals[i].localPosition = _platformCenter + Across(radius * Mathf.Sin(arc), radius * Mathf.Cos(arc), rise);
                Vector3 tangent = Across(radius * Mathf.Cos(arc), -radius * Mathf.Sin(arc),
                    0.56f * Mathf.Cos(arc * 2f)) * Mathf.Sign(speed);
                _shoals[i].localRotation = SwimRotation(tangent, 0f, Mathf.Sign(speed) * 7f);
            }
            float drift = time * 0.69f;
            float pulse = Mathf.Sin(time * 2.25f);
            _jellyfish.localPosition = Across(1.19f + Mathf.Sin(drift) * 0.49f,
                2.95f + Mathf.Cos(drift) * 0.37f, 1.87f + Mathf.Sin(drift + 0.8f) * 0.21f);
            _jellyfish.localScale = new Vector3(1f + pulse * 0.16f, 1f - pulse * 0.11f, 1f + pulse * 0.16f);
        }

        /// <summary>
        /// 闲置 / 教学时的生物运动（2026-11-18，参考图二）：
        /// 鲸鱼在竖琴背后 5–6 m 的竖直圆环上 60 s 一圈（从演奏者背后看：在画面右侧抬头上升、越过顶端、在左侧下潜）；
        /// 鱼群与水母随音乐能量（<see cref="OceanAmbience.MusicEnergy"/>）加快游动、增强发光。
        /// </summary>
        private void MoveIdle(float time)
        {
            float dt = _lastMoveTime < 0f ? 0f : Mathf.Clamp(time - _lastMoveTime, 0f, 0.1f);
            _lastMoveTime = time;
            float energy = OceanAmbience.MusicEnergy;

            if (_piano == null)
            {
                var keys = FindFirstObjectByType<PianoKeyMap>();
                if (keys != null) _piano = keys.transform;
            }
            if (_piano != null)
            {
                float a = time * Mathf.PI * 2f / 60f;
                // 距琴 5–6 m：再远会被水雾吃掉（雾密度 0.078/m，10 m 外只剩四成对比度）
                Vector3 local = new Vector3(0.62f - 2.6f * Mathf.Sin(a), 2.45f - 2.45f * Mathf.Cos(a), -5.4f - 0.9f * Mathf.Sin(a));
                Vector3 velocity = new Vector3(-2.6f * Mathf.Cos(a), 2.45f * Mathf.Sin(a), -0.9f * Mathf.Cos(a));
                Vector3 world = _piano.TransformPoint(local);
                Vector3 heading = transform.InverseTransformDirection(_piano.TransformDirection(velocity.normalized));
                float stroke = time * 1.1f;
                _whale.localPosition = transform.InverseTransformPoint(world) + Vector3.up * (Mathf.Sin(stroke) * 0.05f);
                _whale.localRotation = SwimRotation(heading, 2.4f * Mathf.Sin(stroke - 0.6f), 5f * Mathf.Sin(time * 0.31f));
            }
            else
            {
                // 场景里没有钢琴（单独搭建环境时）：沿用原来绕舞台的巡游路线
                float turn = time * 0.072f;
                float stroke = time * 1.38f;
                Vector3 heading = Across(5.4f * Mathf.Cos(turn), 2.9f * Mathf.Sin(turn), 0f);
                _whale.localPosition = Across(5.4f * Mathf.Sin(turn), 7.5f + 2.9f * (1f - Mathf.Cos(turn)),
                    2.05f + Mathf.Sin(time * 0.24f) * 0.18f + Mathf.Sin(stroke) * 0.055f);
                _whale.localRotation = SwimRotation(heading, 2.8f * Mathf.Sin(stroke - 0.6f), 6f * Mathf.Sin(time * 0.38f));
            }

            for (int i = 0; i < _shoals.Length; i++)
            {
                float speed = (i == 0 ? 0.36f : -0.23f) * (1f + energy * 0.9f);
                _shoalPhase[i] += speed * dt;
                float arc = _shoalPhase[i] + i * 2.71f;
                float radius = 4.15f + i * 3.2f - energy * 0.35f;
                float rise = 2.48f + i * 0.64f + 0.28f * Mathf.Sin(arc * 2f);
                _shoals[i].localPosition = _platformCenter + Across(radius * Mathf.Sin(arc), radius * Mathf.Cos(arc), rise);
                Vector3 tangent = Across(radius * Mathf.Cos(arc), -radius * Mathf.Sin(arc),
                    0.56f * Mathf.Cos(arc * 2f)) * Mathf.Sign(speed);
                _shoals[i].localRotation = SwimRotation(tangent, 0f, Mathf.Sign(speed) * 7f);
            }

            _jellyPhase += dt * (2.25f + energy * 1.6f);
            float drift = time * 0.69f;
            for (int j = 0; j < _jellies.Length; j++)
            {
                if (_jellies[j] == null) continue;
                float pulse = Mathf.Sin(_jellyPhase + j * 1.9f);
                float size = j == 0 ? 1f : (j == 1 ? 0.55f : 0.42f);
                Vector3 home = j == 0 ? Across(1.19f, 2.95f, 1.87f) : (j == 1 ? Across(-2.1f, 3.6f, 1.25f) : Across(2.6f, 4.4f, 2.7f));
                _jellies[j].localPosition = home + Across(Mathf.Sin(drift + j) * 0.45f, Mathf.Cos(drift + j * 0.7f) * 0.32f,
                    Mathf.Sin(drift * 0.8f + j * 2.1f) * 0.24f);
                _jellies[j].localScale = new Vector3(1f + pulse * 0.16f, 1f - pulse * 0.11f, 1f + pulse * 0.16f) * size;
            }
            _amber.SetFloat(Glow, 0.85f + energy * 0.75f);
        }

        // 所有生物模型的头沿本地 X 轴。俯仰和滚转使用转向后的局部坐标。
        private static Quaternion SwimRotation(Vector3 heading, float pitch, float roll) =>
            Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Euler(0f, -90f, 0f) *
            Quaternion.AngleAxis(pitch, Vector3.forward) * Quaternion.AngleAxis(roll, Vector3.right);

        private static float Range(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    }
}
