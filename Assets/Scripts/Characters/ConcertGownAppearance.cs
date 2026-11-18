using System;
using System.Collections.Generic;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Piano;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>把新头部、肩颈与缎面礼服绑定到演奏骨架。</summary>
    internal sealed class ConcertGownAppearance
    {
        private readonly Transform _root;
        private readonly AvatarRigBuilder.Skeleton _rig;
        private readonly CharacterResourceScope _scope;
        private Material _satin;
        private Material _silver;
        private SkinnedMeshRenderer _bodice;
        private const int BodiceRows = 64;
        private const int BodiceSides = 104;
        private const int WaistRow = 12;
        private Transform _skirt, _skirtTrim, _standingSkirt;
        private Vector3 _hip, _neck;
        private float _shoulder, _torso;
        private float _front;

        internal ConcertGownAppearance(Transform root, AvatarRigBuilder.Skeleton rig, CharacterResourceScope scope)
        {
            _root = root;
            _rig = rig;
            _scope = scope;
        }

        internal void ApplyToBindPose(Dictionary<string, Transform> bones)
        {
            if (_rig.Head == null || _rig.Hips == null || _rig.Neck == null || _rig.RightUpperArm == null || _rig.LeftUpperArm == null ||
                _rig.RightLowerArm == null || _rig.LeftLowerArm == null || _rig.LeftHand == null || _rig.RightHand == null) return;
            Vector3 head = Local(_rig.Head);
            _hip = Local(_rig.Hips);
            _neck = Local(_rig.Neck);
            _front = bones.TryGetValue("Bip01 MNose", out var nose) && Local(nose).z > head.z ? 1f : -1f;
            _shoulder = Mathf.Max(0.12f, Mathf.Abs(Local(_rig.RightUpperArm).x - _hip.x));
            _torso = Mathf.Max(0.26f, _neck.y - _hip.y);
            _satin = MakeMaterial("银蓝缎面礼服", new Color(0.44f, 0.51f, 0.60f), 0.0f, 0.41f);
            _silver = MakeMaterial("礼服银线", new Color(0.54f, 0.65f, 0.75f), 0.12f, 0.53f);
            var skin = MakeMaterial("肩颈肤色", new Color(0.73f, 0.55f, 0.46f), 0f, 0.33f);
            var headModel = new ConcertPortraitHead(_root, _rig.Head, _front, _scope);
            if (!headModel.TryBuild()) return;

            foreach (var renderer in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                RetainLimbs(renderer);

            var torso = new StageMesh();
            torso.Surface(56, 96, TorsoPoint, (_, _) => Color.white, wrap: true);
            AddSkinnedTorso("PerformerShoulders", torso, skin);
            AddArm("PerformerLeftArm", _rig.LeftUpperArm, _rig.LeftLowerArm, _rig.LeftHand, skin);
            AddArm("PerformerRightArm", _rig.RightUpperArm, _rig.RightLowerArm, _rig.RightHand, skin);
            var bodice = new StageMesh();
            bodice.Surface(BodiceRows, BodiceSides, BodicePoint, (_, _) => Color.white, wrap: true);
            _bodice = AddSkinnedTorso("SatinCowlBodice", bodice, _satin);
            var straps = new StageMesh();
            var embroidery = new StageMesh();
            for (int side = -1; side <= 1; side += 2)
            {
                float sign = side;
                float frontU = side * _front < 0 ? 0.885f : 0.115f;
                float rearU = side * _front < 0 ? 0.615f : 0.385f;
                Vector3 front = BodicePoint(frontU, 1f);
                Vector3 back = BodicePoint(rearU, 1f);
                Vector3 peak = new(_hip.x + sign * _shoulder * 0.73f, _neck.y - 0.049f, _neck.z);
                straps.Tube(56, 9, t =>
                {
                    float u = t * t * (3 - 2 * t);
                    return Vector3.LerpUnclamped(front, back, u) +
                        4f * t * (1f - t) * (peak - (front + back) * 0.5f);
                }, _ => 0.00225f, _ => Color.white);

                for (int line = 0; line < 7; line++)
                {
                    float lane = line;
                    float origin = side < 0 ? 0.79f : 0.21f;
                    embroidery.Tube(32, 7, t =>
                    {
                        float u = origin + sign * (lane - 3f) * 0.008f + sign * 0.048f * t * t;
                        return BodicePoint(u, 0.10f + t * (0.42f + (lane % 3) * 0.067f))
                            + Outward(u) * 0.0016f;
                    }, t => Mathf.Lerp(0.0010f, 0.00032f, t), _ => Color.white);
                }
            }
            AddSkinnedTorso("FineSatinStraps", straps, _satin);
            AddSkinnedTorso("SilverBodiceEmbroidery", embroidery, _silver);
            BuildStandingSkirt();
        }

        private void RetainLimbs(SkinnedMeshRenderer renderer)
        {
            var source = renderer.sharedMesh;
            if (source == null || !source.isReadable) return;
            var materials = renderer.sharedMaterials;
            if (materials.Length != source.subMeshCount) return;
            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = "女演奏者手臂、手部与足部";
            _scope.Own(mesh);
            var vertices = source.vertices;
            Matrix4x4 toRoot = _root.worldToLocalMatrix * renderer.localToWorldMatrix;
            Vector3[] bind = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) bind[i] = toRoot.MultiplyPoint3x4(vertices[i]);
            Vector3 leftElbow = Local(_rig.LeftLowerArm);
            Vector3 rightElbow = Local(_rig.RightLowerArm);
            Vector3 leftForearm = Local(_rig.LeftHand) - leftElbow;
            Vector3 rightForearm = Local(_rig.RightHand) - rightElbow;
            float split = (Local(_rig.LeftUpperArm).x + Local(_rig.RightUpperArm).x) * 0.5f;
            bool leftIsLower = Local(_rig.LeftUpperArm).x < split;
            var skin = Resources.Load<Shader>("StarStage/ConcertLimbs");
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var material = materials[sub];
                var fitted = new List<int>();
                if (material != null && RocketboxTextureLibrary.ClassifySemantic(material.name) == "body")
                {
                    var indices = source.GetIndices(sub);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        Vector3 p = (bind[indices[i]] + bind[indices[i + 1]] + bind[indices[i + 2]]) / 3f;
                        bool left = (p.x < split) == leftIsLower;
                        Vector3 start = left ? leftElbow : rightElbow;
                        Vector3 along = left ? leftForearm : rightForearm;
                        float progress = Vector3.Dot(p - start, along) / along.sqrMagnitude;
                        bool forearmOrHand = progress > 0.15f && Mathf.Abs(p.x - _hip.x) > _shoulder * 1.13f;
                        if ((p.y < 0.155f && Mathf.Abs(p.x - _hip.x) < _shoulder * 1.25f) || forearmOrHand)
                        {
                            fitted.Add(indices[i]);
                            fitted.Add(indices[i + 1]);
                            fitted.Add(indices[i + 2]);
                        }
                    }
                    if (skin != null)
                    {
                        var original = material.mainTexture;
                        material.shader = skin;
                        material.mainTexture = original;
                    }
                }
                mesh.SetTriangles(fitted, sub, false);
            }
            // uv1：x = 绑定姿态高度（区分腿脚与手臂），y = 沿前臂的进度（0 = 肘、1 = 腕），
            // ConcertLimbs 用 y 在肘部附近把真实皮肤纹理渐变回纯肤色，衔接程序化上臂。
            var bindMarker = new List<Vector2>(bind.Length);
            foreach (var point in bind)
            {
                bool left = (point.x < split) == leftIsLower;
                Vector3 start = left ? leftElbow : rightElbow;
                Vector3 along = left ? leftForearm : rightForearm;
                float progress = Vector3.Dot(point - start, along) / along.sqrMagnitude;
                bindMarker.Add(new Vector2(point.y, progress));
            }
            mesh.SetUVs(1, bindMarker);
            renderer.sharedMesh = mesh;
        }

        private void AddArm(string name, Transform upper, Transform lower, Transform wrist, Material skin)
        {
            Vector3 shoulder = Local(upper);
            Vector3 elbow = Local(lower);
            Vector3 hand = Local(wrist);
            Vector3 upperAxis = (elbow - shoulder).normalized;
            Vector3 foreAxis = (hand - elbow).normalized;
            float upperLength = Vector3.Distance(shoulder, elbow);
            float foreLength = Vector3.Distance(elbow, hand);
            float armLength = upperLength + foreLength;
            float newLength = upperLength + foreLength * 0.23f;
            const int rows = 82;
            const int sides = 64;
            var shape = new StageMesh();
            shape.Surface(rows, sides, (u, t) =>
            {
                float along = Mathf.Lerp(-0.047f, newLength, t);
                float toElbow = Mathf.InverseLerp(0f, upperLength, along);
                float toWrist = Mathf.InverseLerp(upperLength, armLength, along);
                float bend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(upperLength - 0.025f, upperLength + 0.025f, along));
                Vector3 axis = Vector3.Slerp(upperAxis, foreAxis, bend);
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
                Vector3 depth = Vector3.Cross(axis, up);
                // 肩袖根部埋入躯干，三角肌宽度沿上臂逐渐增大再收细。
                float deltoid = Mathf.Exp(-Mathf.Pow((toElbow - 0.21f) / 0.23f, 2f));
                float upperRadius = Mathf.Lerp(0.031f, 0.027f, toElbow) + 0.007f * deltoid;
                float lowerRadius = Mathf.Lerp(0.027f, 0.020f, Mathf.InverseLerp(0.10f, 0.23f, toWrist));
                float radius = Mathf.Lerp(upperRadius, lowerRadius, bend) *
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.048f, 0.045f, along));
                Vector3 center = along < upperLength ? shoulder + upperAxis * along
                    : elbow + foreAxis * (along - upperLength);
                center -= Vector3.up * (0.011f * (1f - Mathf.SmoothStep(0f, 1f, toElbow)));
                float angle = u * Mathf.PI * 2f;
                return center + radius * (up * Mathf.Cos(angle) + depth * Mathf.Sin(angle) *
                    Mathf.Lerp(0.90f, 0.72f, toWrist));
            }, (_, _) => Color.white, wrap: true);
            Mesh mesh = shape.Build(name);
            var bones = new[] { _rig.Chest ?? _rig.Spine ?? _rig.Hips, upper, lower };
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bind.Length; i++) bind[i] = bones[i].worldToLocalMatrix * _root.localToWorldMatrix;
            var weights = new BoneWeight[mesh.vertexCount];
            for (int i = 0; i < weights.Length; i++)
            {
                float t = (i / sides) / (float)rows;
                float along = Mathf.Lerp(-0.047f, newLength, t);
                float chest = 0.66f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.036f, 0.075f, along)));
                float forearm = (1f - chest) * Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(upperLength - 0.042f, upperLength + 0.036f, along));
                weights[i] = OrderedWeight((0, chest), (1, 1f - chest - forearm), (2, forearm));
            }
            mesh.bindposes = bind;
            mesh.boneWeights = weights;
            var node = new GameObject(name);
            node.transform.SetParent(_root, false);
            var surface = node.AddComponent<SkinnedMeshRenderer>();
            surface.rootBone = _rig.Hips;
            surface.sharedMesh = mesh;
            surface.bones = bones;
            surface.sharedMaterial = skin;
            surface.localBounds = new Bounds(Vector3.up * (_hip.y + _torso * 0.5f), Vector3.one * 2.2f);
            _scope.Own(mesh);
        }

        private Vector3 Outward(float u)
        {
            float a = u * Mathf.PI * 2f;
            return new Vector3(_front * Mathf.Sin(a), 0f, _front * Mathf.Cos(a));
        }

        private Vector3 TorsoPoint(float u, float t)
        {
            Vector3 radial = Outward(u);
            float front = radial.z * _front;
            float xRadius = BodyWidth(t);
            float depth = Mathf.Lerp(0.048f, 0.105f, Mathf.Cos((t - 0.5f) * Mathf.PI));
            depth += 0.021f * Mathf.Max(0, front) * Mathf.Exp(-Mathf.Pow((t - 0.60f) / 0.16f, 2f));
            depth = Mathf.Lerp(depth, 0.050f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.84f, 1f, t)));
            float z = Mathf.Lerp(_hip.z, _neck.z, t) + depth * radial.z;
            float shoulderSlope = 0.042f * Mathf.Sin(t * Mathf.PI) * Mathf.Pow(Mathf.Abs(radial.x), 3f);
            return new Vector3(Mathf.Lerp(_hip.x, _neck.x, t) + xRadius * radial.x,
                Mathf.Lerp(_hip.y - 0.065f, _neck.y + 0.075f, t) - shoulderSlope, z);
        }

        private float BodyWidth(float t)
        {
            if (t < 0.34f) return Mathf.Lerp(_shoulder * 0.87f, _shoulder * 0.63f,
                Mathf.SmoothStep(0, 1, t / 0.34f));
            if (t < 0.77f) return Mathf.Lerp(_shoulder * 0.63f, _shoulder * 1.16f,
                Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.34f, 0.77f, t)));
            return Mathf.Lerp(_shoulder * 1.16f, 0.046f,
                Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.77f, 0.94f, t)));
        }

        private Vector3 BodicePoint(float u, float t)
        {
            Vector3 radial = Outward(u);
            float front = radial.z * _front;
            float side = Mathf.Abs(radial.x);
            float upper = front >= 0f ? Mathf.Lerp(0.58f, 0.76f, Mathf.Pow(side, 1.85f))
                : Mathf.Lerp(0.31f, 0.76f, side * side);
            float row = Mathf.Lerp(0.022f, upper, t);
            float cowl = Mathf.Max(0f, front) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.93f, t));
            float folds = Mathf.Sin(t * 28f + side * side * 8f) * cowl * 0.00255f;
            return TorsoPoint(u, row) + radial * (0.0042f + 0.004f * cowl + folds);
        }

        private SkinnedMeshRenderer AddSkinnedTorso(string name, StageMesh geometry, Material material)
        {
            var node = new GameObject(name);
            node.transform.SetParent(_root, false);
            var rig = new[] { _rig.Hips, _rig.Spine ?? _rig.Hips, _rig.Chest ?? _rig.Spine ?? _rig.Hips,
                _rig.Neck, _rig.LeftUpperArm ?? _rig.Neck, _rig.RightUpperArm };
            var mesh = geometry.Build(name);
            var positions = mesh.vertices;
            var weights = new BoneWeight[positions.Length];
            float leftX = Local(rig[4]).x;
            float rightX = Local(rig[5]).x;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 point = positions[i];
                float row = Mathf.InverseLerp(_hip.y, _neck.y + 0.048f, point.y);
                float spine = Mathf.Clamp(row * 3f, 0f, 2.999f);
                int lower = Mathf.FloorToInt(spine);
                float blend = spine - lower;
                float upperArm = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.77f, 1.03f,
                    Mathf.Abs(point.x - _hip.x) / _shoulder)) *
                    Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.57f, 0.76f, row)) * 0.78f;
                int arm = Mathf.Abs(point.x - leftX) < Mathf.Abs(point.x - rightX) ? 4 : 5;
                weights[i] = OrderedWeight((lower, (1f - blend) * (1f - upperArm)),
                    (lower + 1, blend * (1f - upperArm)), (arm, upperArm));
            }
            var bindPoses = new Matrix4x4[rig.Length];
            for (int i = 0; i < rig.Length; i++)
                bindPoses[i] = rig[i].worldToLocalMatrix * _root.localToWorldMatrix;
            mesh.bindposes = bindPoses;
            mesh.boneWeights = weights;
            var surface = node.AddComponent<SkinnedMeshRenderer>();
            surface.rootBone = _rig.Hips;
            surface.sharedMesh = mesh;
            surface.bones = rig;
            surface.sharedMaterial = material;
            surface.localBounds = new Bounds(new Vector3(_hip.x, _hip.y + _torso * 0.5f, _hip.z), Vector3.one * 1.8f);
            _scope.Own(mesh);
            return surface;
        }

        private static BoneWeight OrderedWeight((int index, float weight) a,
            (int index, float weight) b, (int index, float weight) c)
        {
            if (a.weight < b.weight) (a, b) = (b, a);
            if (b.weight < c.weight) (b, c) = (c, b);
            if (a.weight < b.weight) (a, b) = (b, a);
            return new BoneWeight { boneIndex0 = a.index, weight0 = a.weight,
                boneIndex1 = b.index, weight1 = b.weight, boneIndex2 = c.index, weight2 = c.weight };
        }

        private void BuildStandingSkirt()
        {
            var skirt = new StageMesh();
            Vector3 SkirtPoint(float u, float t)
            {
                Vector3 radial = Outward(u);
                Vector3 top = BodicePoint(u, WaistRow / (float)BodiceRows);
                float flare = Mathf.Pow(t, 3.65f);
                float angle = u * Mathf.PI * 2f;
                float gathers = (0.004f * t + 0.019f * flare) * Pleats(angle, t);
                float radius = 0.004f + 0.021f * Mathf.Sin(t * Mathf.PI) + 0.15f * flare + gathers;
                float train = 0.17f * flare * Mathf.Max(0f, -radial.z * _front);
                return new Vector3(top.x, Mathf.Lerp(top.y, 0.016f, t) + 0.004f * flare * Mathf.Sin(angle * 5f), top.z)
                    + radial * radius - Vector3.forward * (_front * train);
            }
            skirt.Surface(72, 128, (u, t) => SkirtPoint(1f - u, t), (_, _) => Color.white, wrap: true);
            _standingSkirt = AddMesh("StandingSatinGown", _root, skirt, _satin, p => p);
        }

        internal void RestoreStandingSkirt()
        {
            if (_standingSkirt != null) _standingSkirt.gameObject.SetActive(true);
            if (_skirt != null) _skirt.gameObject.SetActive(false);
            if (_skirtTrim != null) _skirtTrim.gameObject.SetActive(false);
        }


        internal void BuildSeatedSkirt()
        {
            if (_standingSkirt != null) _standingSkirt.gameObject.SetActive(false);
            if (_skirt != null)
            {
                _skirt.gameObject.SetActive(true);
                if (_skirtTrim != null) _skirtTrim.gameObject.SetActive(true);
                return;
            }
            if (_bodice == null || _rig.LeftLowerLeg == null || _rig.RightLowerLeg == null ||
                _rig.LeftFoot == null || _rig.RightFoot == null) return;
            var waist = ReadSeatedWaist();
            Vector3 hip = Local(_rig.Hips);
            Vector3 knee = (Local(_rig.LeftLowerLeg) + Local(_rig.RightLowerLeg)) * 0.5f;
            Vector3 ankle = (Local(_rig.LeftFoot) + Local(_rig.RightFoot)) * 0.5f;
            float halfKneeGap = Mathf.Abs(Local(_rig.LeftLowerLeg).x - Local(_rig.RightLowerLeg).x) * 0.5f;
            Vector3 Cloth(float u, float t)
            {
                float bindU = Mathf.Repeat(1f - u, 1f);
                Vector3 outward = Outward(bindU);
                float ring = bindU * BodiceSides;
                int index = Mathf.FloorToInt(ring) % BodiceSides;
                Vector3 edge = Vector3.LerpUnclamped(waist[index], waist[(index + 1) % BodiceSides], ring - index);
                float join = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.28f, t));
                float lap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.47f, t));
                float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 1f, t));
                float flare = Mathf.Pow(t, 4.3f);
                float angle = u * Mathf.PI * 2f;
                float front = Mathf.Max(0f, outward.z * _front);
                float fold = (0.002f * lap + 0.012f * fall + 0.007f * flare) * Pleats(angle, t);
                float width = Mathf.Lerp(0.124f, halfKneeGap + 0.061f, lap) + 0.064f * flare;
                float depth = 0.094f + 0.068f * lap - 0.044f * fall + 0.026f * flare;
                float centerZ = Mathf.Lerp(hip.z, Mathf.Lerp(knee.z, ankle.z, fall * 0.77f), lap);
                float lapY = Mathf.Lerp(hip.y + 0.018f, knee.y + 0.064f, lap);
                float hem = Mathf.Max(0.016f, ankle.y - 0.028f) + 0.036f * front;
                float y = Mathf.Lerp(lapY, hem, fall) + 0.006f * flare * Mathf.Sin(angle * 5f + 1.6f);
                var drape = new Vector3(hip.x + outward.x * (width + fold), y, centerZ + outward.z * (depth + fold));
                // 取坐姿上衣的实测底环，裙腰从缎面外侧连续过渡到腿上。
                return Vector3.Lerp(edge + outward * 0.003f, drape, join);
            }
            var cloth = new StageMesh();
            cloth.Surface(76, 112, Cloth, (_, _) => Color.white, wrap: true);
            cloth.Surface(4, 112, (u, t) =>
            {
                Vector3 outward = Outward(1f - u);
                float turn = t * Mathf.PI * 0.5f;
                return Cloth(u, 1f) - outward * (0.0035f * (1f - Mathf.Cos(turn)))
                    - Vector3.up * (0.0025f * Mathf.Sin(turn));
            }, (_, _) => Color.white, wrap: true);
            _skirt = AddMesh("SeatedSatinGown", _root, cloth, _satin, p => p);

            var thread = new StageMesh();
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            for (int line = 0; line < 5; line++)
            {
                float angle = (sideIndex == 0 ? 0.25f : 0.75f) + (line - 2f) * 0.014f;
                float length = 0.14f + 0.019f * (line % 3);
                thread.Tube(22, 6, t =>
                {
                    float u = angle + 0.013f * Mathf.Sin(t * 4.4f + angle * 8f);
                    return Cloth(u, 0.045f + length * t) + Outward(1f - u) * 0.0017f;
                }, t => Mathf.Lerp(0.00074f, 0.00031f, t), _ => Color.white);
            }
            _skirtTrim = AddMesh("GownSilverEmbroidery", _root, thread, _silver, p => p);
        }

        private Vector3[] ReadSeatedWaist()
        {
            var baked = new Mesh();
            try
            {
                _bodice.BakeMesh(baked);
                var vertices = baked.vertices;
                var waist = new Vector3[BodiceSides];
                Matrix4x4 toRoot = _root.worldToLocalMatrix * _bodice.localToWorldMatrix;
                for (int i = 0; i < waist.Length; i++)
                    waist[i] = toRoot.MultiplyPoint3x4(vertices[WaistRow * BodiceSides + i]);
                return waist;
            }
            finally
            {
                UnityEngine.Object.Destroy(baked);
            }
        }

        private static float Pleats(float angle, float t) =>
            0.56f * Mathf.Sin(6f * angle + 1.4f * t + 0.61f * Mathf.Sin(2f * angle))
            + 0.33f * Mathf.Sin(9f * angle - 1.9f * t + 0.7f)
            + 0.11f * Mathf.Sin(14f * angle + 2.2f * t);

        private Vector3 Local(Transform bone) => _root.InverseTransformPoint(bone.position);

        private Material MakeMaterial(string name, Color color, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color, mainTexture = Texture2D.whiteTexture };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat(material.HasProperty("_Smoothness") ? "_Smoothness" : "_Glossiness", smoothness);
            _scope.Own(material);
            return material;
        }

        private Transform AddMesh(string name, Transform parent, StageMesh geometry, Material material, Func<Vector3, Vector3> map)
        {
            var node = new GameObject(name);
            node.transform.SetParent(parent, false);
            var mesh = geometry.Build(name);
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = map(vertices[i]);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _scope.Own(mesh);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            node.AddComponent<MeshRenderer>().sharedMaterial = material;
            return node.transform;
        }
    }
}
