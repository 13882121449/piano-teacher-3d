using System;
using System.Collections.Generic;
using PianoTeacher3D.Avatar;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// Rocketbox（3ds Max Biped 命名）手部 rig，实现 <see cref="IHandRig"/> 与 <see cref="IFingerJointRig"/>。
    /// <para>
    /// <b>为什么不能硬编码轴</b>：内置程序化手部约定「+Z = 手指方向、+Y = 手背、+X·Side = 拇指侧」，
    /// 而 Biped 骨骼的局部轴由绑定矩阵决定，每根骨头都不一样（同一根手指的近节与末节甚至可能相反）。
    /// 因此这里在<b>创建时从绑定姿态推导</b>一套规范基：
    /// ① 手指方向 = 中指末节世界位置 − 中指近节世界位置（投影到腕部局部空间）；
    /// ② 拇指侧 = 腕 → 拇指根（去手指方向分量后归一化）；
    /// ③ 手背法线 = Cross(手指方向, 拇指侧 × SideSign)，其中 SideSign 左手 +1 / 右手 −1，
    ///    因为内置约定里「拇指侧」是 X 轴乘以 Side，两只手的 X 轴其实都指向身体中线外侧。
    /// 基向量只用于「读数与坐标变换」，不会被写回骨骼。
    /// </para>
    /// <para>
    /// <b>本类创建的对象</b>：每指一个指尖参考节点（<c>Tip</c>）与一个掌心中继节点（<c>Palm</c>）。
    /// 它们都是模型根的子节点，随模型根一起销毁，因此不单独登记到资源作用域。
    /// </para>
    /// </summary>
    public sealed class RocketboxHandRig : IHandRig, IFingerJointRig
    {
        /// <summary>每只手的手指数（0 = 拇指 … 4 = 小指）。</summary>
        public const int FingersPerHand = 5;

        /// <summary>每指关节数（MCP / PIP / DIP）。</summary>
        public const int SegmentsPerFinger = 3;

        private readonly Transform[] _proximal = new Transform[FingersPerHand];
        private readonly Transform[] _intermediate = new Transform[FingersPerHand];
        private readonly Transform[] _distal = new Transform[FingersPerHand];
        private readonly Transform[] _tips = new Transform[FingersPerHand];
        private readonly float[] _lengths = new float[FingersPerHand];
        private readonly Transform[] _fingerList = new Transform[FingersPerHand];

        private RocketboxHandRig(bool leftHand, Transform wrist, Transform palm)
        {
            LeftHand = leftHand;
            Wrist = wrist;
            Palm = palm;
        }

        /// <summary>是否左手。</summary>
        public bool LeftHand { get; }

        public Transform Wrist { get; }

        public Transform Palm { get; }

        /// <summary>每指一个根（近节骨骼），语义与内置 <see cref="IHandRig"/> 一致。</summary>
        public IReadOnlyList<Transform> Fingers => _fingerList;

        /// <summary>腕部局部空间中的手指方向（规范基 +Z）。</summary>
        public Vector3 FingerDirection { get; private set; } = Vector3.forward;

        /// <summary>腕部局部空间中的手背法线（规范基 +Y）。</summary>
        public Vector3 BackNormal { get; private set; } = Vector3.up;

        /// <summary>腕部局部空间中的拇指侧（规范基 +X，已乘 SideSign）。</summary>
        public Vector3 ThumbSide { get; private set; } = Vector3.right;

        /// <summary>左手 +1 / 右手 −1（用于把「拇指侧」换算成规范 X 轴）。</summary>
        public float SideSign => LeftHand ? 1f : -1f;

        /// <summary>规范基 → 腕部局部空间的旋转。用它把 +Z/+Y/+X 约定下的方向换算到真实骨骼空间。</summary>
        public Quaternion CanonicalToWrist { get; private set; } = Quaternion.identity;

        /// <summary>是否已成功绑定 5 指 × 3 节与指尖节点。</summary>
        public bool IsValid
        {
            get
            {
                for (int f = 0; f < FingersPerHand; f++)
                {
                    if (_proximal[f] == null || _intermediate[f] == null || _distal[f] == null) return false;
                }
                return Wrist != null;
            }
        }

        public Transform GetFingertip(int fingerIndex)
        {
            if (fingerIndex < 0 || fingerIndex >= FingersPerHand) return null;
            return _tips[fingerIndex];
        }

        public bool TryGetJoints(int fingerIndex, out Transform proximal, out Transform intermediate, out Transform distal)
        {
            proximal = intermediate = distal = null;
            if (fingerIndex < 0 || fingerIndex >= FingersPerHand) return false;

            proximal = _proximal[fingerIndex];
            intermediate = _intermediate[fingerIndex];
            distal = _distal[fingerIndex];
            return proximal != null && intermediate != null && distal != null;
        }

        /// <summary>绑定姿态下「指根 → 指尖」的三段长度之和（米）。索引越界返回 0。</summary>
        public float GetFingerLength(int fingerIndex)
        {
            if (fingerIndex < 0 || fingerIndex >= FingersPerHand) return 0f;
            return _lengths[fingerIndex];
        }

        /// <summary>
        /// 从骨骼索引创建单手 rig。缺骨骼时返回 null 并给出「缺少骨骼 X」的可读原因。
        /// </summary>
        public static RocketboxHandRig Create(bool leftHand, Dictionary<string, Transform> bones, out string error)
        {
            error = null;
            if (bones == null)
            {
                error = "骨骼索引为空。";
                return null;
            }

            string side = leftHand ? "Bip01 L " : "Bip01 R ";
            if (!bones.TryGetValue(side + "Hand", out var wrist) || wrist == null)
            {
                error = "缺少骨骼 " + side + "Hand";
                return null;
            }

            var palm = CreatePalmNode(wrist);
            var rig = new RocketboxHandRig(leftHand, wrist, palm);

            for (int f = 0; f < FingersPerHand; f++)
            {
                string n0 = BoneName(leftHand, f, 0);
                string n1 = BoneName(leftHand, f, 1);
                string n2 = BoneName(leftHand, f, 2);

                if (!bones.TryGetValue(n0, out var p) || p == null) { error = "缺少骨骼 " + n0; return null; }
                if (!bones.TryGetValue(n1, out var m) || m == null) { error = "缺少骨骼 " + n1; return null; }
                if (!bones.TryGetValue(n2, out var d) || d == null) { error = "缺少骨骼 " + n2; return null; }

                rig._proximal[f] = p;
                rig._intermediate[f] = m;
                rig._distal[f] = d;
                rig._fingerList[f] = p;
                rig._tips[f] = CreateTipNode(d, m);
                rig._lengths[f] = Vector3.Distance(p.position, m.position)
                                + Vector3.Distance(m.position, d.position)
                                + Vector3.Distance(d.position, rig._tips[f].position);
            }

            rig.DeriveCanonicalBasis();
            return rig;
        }

        /// <summary>
        /// Biped 骨骼名：近节 <c>Bip01 L Finger3</c>、中节 <c>Finger31</c>、末节 <c>Finger32</c>。
        /// </summary>
        public static string BoneName(bool leftHand, int fingerIndex, int segment)
        {
            string side = leftHand ? "Bip01 L " : "Bip01 R ";
            string suffix = segment <= 0 ? string.Empty : segment.ToString();
            return side + "Finger" + fingerIndex + suffix;
        }

        /// <summary>
        /// 从绑定姿态推导规范基（见类注释的算法）。骨骼退化（三点共线 / 长度为零）时退回腕部自身轴并保持可用。
        /// </summary>
        private void DeriveCanonicalBasis()
        {
            Vector3 wristLocalThumbRoot = Wrist.InverseTransformPoint(_proximal[0].position);
            Vector3 wristLocalMin = Wrist.InverseTransformPoint(_proximal[2].position);
            Vector3 wristLocalMax = Wrist.InverseTransformPoint(_distal[2].position);
            Vector3 origin = Wrist.InverseTransformPoint(Wrist.position);

            Vector3 forward = wristLocalMax - wristLocalMin;
            if (forward.sqrMagnitude < 1e-10f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 thumb = wristLocalThumbRoot - origin;
            thumb -= forward * Vector3.Dot(thumb, forward);
            if (thumb.sqrMagnitude < 1e-10f)
            {
                // 退化（拇指根与手心共线）：用腕部局部 +X 兜底，保证后续数学不产生 NaN
                thumb = Vector3.right;
                thumb -= forward * Vector3.Dot(thumb, forward);
                if (thumb.sqrMagnitude < 1e-10f) thumb = Vector3.up;
            }
            thumb.Normalize();

            // 规范 X 轴 = 拇指侧 × SideSign（左右手的 X 轴都指向身体中线外侧）
            Vector3 canonicalX = thumb * SideSign;

            Vector3 up = Vector3.Cross(forward, canonicalX);
            if (up.sqrMagnitude < 1e-10f)
            {
                up = Vector3.up;
                up -= forward * Vector3.Dot(up, forward);
                if (up.sqrMagnitude < 1e-10f) up = Vector3.Cross(forward, Vector3.right);
            }
            up.Normalize();

            // 再正交化 X，消除拇指方向与手指方向不完全垂直带来的误差
            canonicalX = Vector3.Cross(up, forward).normalized;

            FingerDirection = forward;
            BackNormal = up;
            ThumbSide = canonicalX;
            CanonicalToWrist = Quaternion.LookRotation(forward, up);
        }

        /// <summary>掌心中继节点：放在腕点（与内置「Palm 位于掌心、由腕节点派生」的语义对齐）。</summary>
        private static Transform CreatePalmNode(Transform wrist)
        {
            var go = new GameObject("Palm");
            var t = go.transform;
            t.SetParent(wrist, true);
            t.position = wrist.position;
            return t;
        }

        /// <summary>
        /// 指尖参考节点：沿「中节 → 末节」方向再延伸一段（末节没有更下一节骨骼，用等长近似），
        /// 与内置手部「指尖接触点 = 末节末端略往回」的语义对齐。
        /// </summary>
        private static Transform CreateTipNode(Transform distal, Transform intermediate)
        {
            Vector3 dir = distal.position - intermediate.position;
            float len = dir.magnitude;
            Vector3 tipPos = distal.position + (len > 1e-6f ? dir / len * len : distal.forward * 0.02f);

            var go = new GameObject("Tip");
            var t = go.transform;
            t.SetParent(distal, true);
            t.position = tipPos;
            return t;
        }
    }
}
