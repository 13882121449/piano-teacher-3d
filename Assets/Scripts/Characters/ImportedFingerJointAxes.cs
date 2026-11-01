using System.Collections.Generic;
using PianoTeacher3D.Avatar;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 导入模型（Biped / Humanoid 骨架）的关节轴策略：从**绑定姿态**推导铰链轴，
    /// 让 <see cref="FingerPoseController"/> 的 CCD 能弯曲任意骨架的手指。
    ///
    /// <para>
    /// 推导方式（不硬编码任何轴常量）：
    /// 对每个关节，取「骨方向」<c>childPos − jointPos</c> 与「屈曲方向」<c>−手背法线</c>，
    /// 两者叉乘得到铰链轴 <c>axis = dir × curl</c>。由罗德里格斯公式，绕该轴正向旋转
    /// 会让骨方向朝掌心转动，因此弯曲角（≥ 0）可直接使用，不需要额外符号判断。
    /// 轴保存在**父关节的局部空间**，这样父骨一动，轴随之旋转，符合铰链的物理语义。
    /// </para>
    ///
    /// <para>
    /// 退化处理：骨方向与屈曲方向接近平行时叉乘退化为零向量，改用「骨方向 × 世界 up」
    /// 作为替代轴；仍退化则退回默认策略（绕局部 X），保证不会产生 NaN 或不动。
    /// </para>
    /// </summary>
    public sealed class ImportedFingerJointAxes : IFingerJointAxes
    {
        private struct JointAxis
        {
            /// <summary>绑定姿态的局部旋转（必须保留，否则会丢掉骨架自带的朝向）。</summary>
            public Quaternion Rest;

            /// <summary>铰链轴（父关节局部空间，单位向量，方向已保证「正向 = 屈曲」）。</summary>
            public Vector3 Axis;
        }

        private readonly Dictionary<Transform, JointAxis> _joints = new Dictionary<Transform, JointAxis>();
        private readonly Dictionary<Transform, Vector3> _boneDirectionLocal = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Vector3> _upLocal = new Dictionary<Transform, Vector3>();

        /// <summary>成功登记的关节数（测试与诊断用；0 表示这套策略实际上不生效）。</summary>
        public int JointCount => _joints.Count;

        /// <summary>登记的指根数（用于验证左右手各 5 指都拿到了瞄准数据）。</summary>
        public int FingerCount => _boneDirectionLocal.Count;

        /// <summary>
        /// 从一只手的绑定姿态构建轴策略。
        /// </summary>
        /// <param name="hand">手部 rig，需同时实现 <see cref="IFingerJointRig"/>。</param>
        /// <param name="worldBackNormal">绑定姿态下「手背朝外」的世界方向。</param>
        /// <param name="error">失败原因（成功时为 null）。</param>
        public static ImportedFingerJointAxes Build(IHandRig hand, Vector3 worldBackNormal, out string error)
        {
            var axes = new ImportedFingerJointAxes();
            if (!axes.AddHand(hand, worldBackNormal, out error)) return null;
            return axes;
        }

        /// <summary>
        /// 追加一只手。同一个实例可以同时登记左右手（策略按 <see cref="Transform"/> 索引，
        /// 两只手的骨骼互不重名），因此 <c>FingerPoseController</c> 只需要一份策略。
        /// </summary>
        public bool AddHand(IHandRig hand, Vector3 worldBackNormal, out string error)
        {
            error = null;
            if (hand == null)
            {
                error = "手部 rig 为空。";
                return false;
            }

            if (!(hand is IFingerJointRig jointRig))
            {
                error = "手部 rig 未提供三关节信息（IFingerJointRig），无法推导铰链轴。";
                return false;
            }

            if (worldBackNormal.sqrMagnitude < 1e-8f)
            {
                error = "手背法线为零向量，无法推导铰链轴。";
                return false;
            }

            int before = _joints.Count;
            Vector3 backNormal = worldBackNormal.normalized;
            Vector3 curlDirection = -backNormal;   // 屈曲 = 朝掌心

            for (int f = 0; f < hand.Fingers.Count; f++)
            {
                Transform finger = hand.Fingers[f];
                if (finger == null) continue;
                if (!jointRig.TryGetJoints(f, out Transform proximal, out Transform intermediate, out Transform distal))
                    continue;

                Transform tip = jointRig.GetFingertip(f);
                if (tip == null) continue;

                Transform[] joints = { proximal, intermediate, distal };
                Transform[] nexts = { intermediate, distal, tip };

                for (int j = 0; j < joints.Length; j++)
                {
                    Transform joint = joints[j];
                    if (joint == null || joint.parent == null) continue;

                    Vector3 worldDirection = nexts[j] != joint
                        ? nexts[j].position - joint.position
                        : Vector3.zero;

                    if (worldDirection.sqrMagnitude < 1e-8f && j > 0)
                    {
                        // 末节常常没有独立「指尖节点」（GetFingertip 直接返还 distal），
                        // 此时沿上一段的方向延伸，避免 DIP 关节拿不到轴而退回默认轴。
                        worldDirection = joint.position - joints[j - 1].position;
                    }

                    if (worldDirection.sqrMagnitude < 1e-8f) continue;

                    Register(joint, joint.parent, worldDirection.normalized, curlDirection);
                }

                // 指根瞄准：记录「骨方向」与「手背」在该骨骼局部空间下的表示
                Vector3 boneWorld = intermediate.position - proximal.position;
                if (boneWorld.sqrMagnitude < 1e-8f) continue;

                Vector3 boneLocal = proximal.InverseTransformDirection(boneWorld.normalized);
                Vector3 upLocal = proximal.InverseTransformDirection(backNormal);
                if (boneLocal.sqrMagnitude < 1e-6f) continue;

                _boneDirectionLocal[proximal] = boneLocal.normalized;
                _upLocal[proximal] = upLocal.sqrMagnitude > 1e-6f ? upLocal.normalized : Vector3.up;
            }

            if (_joints.Count == before)
            {
                error = "这只手没有任何手指关节可用于推导铰链轴。";
                return false;
            }

            return true;
        }

        private void Register(Transform joint, Transform parent, Vector3 worldDirection, Vector3 worldCurl)
        {
            Vector3 directionLocal = parent.InverseTransformDirection(worldDirection);
            Vector3 curlLocal = parent.InverseTransformDirection(worldCurl);

            Vector3 axis = Vector3.Cross(directionLocal, curlLocal);
            if (axis.sqrMagnitude < 1e-6f)
            {
                // 退化一：骨方向与屈曲方向平行 → 换世界 up 作为参考
                axis = Vector3.Cross(directionLocal, parent.InverseTransformDirection(Vector3.up));
            }

            if (axis.sqrMagnitude < 1e-6f)
            {
                // 退化二：连 up 都平行 → 用右手系补一个确定的垂直轴
                axis = Vector3.Cross(directionLocal, Vector3.forward);
            }

            if (axis.sqrMagnitude < 1e-6f) return;   // 仍退化则放弃该关节，走默认策略

            // Unity 的 Vector3.Cross 与 Quaternion.AngleAxis 都按左手规则，两者叠乘后符号相反：
            // 取 cross(dir, curl) 会让正向弯曲把手指抬起来（实测指尖朝 +Y 而非掌心）。
            // 因此轴取反，保证 degrees ≥ 0 恒为屈曲方向。
            axis = -axis.normalized;

            _joints[joint] = new JointAxis
            {
                Rest = joint.localRotation,
                Axis = axis
            };
        }

        /// <inheritdoc />
        public Quaternion CurlLocalRotation(Transform joint, int jointIndex, float degrees)
        {
            if (joint == null) return Quaternion.identity;
            if (!_joints.TryGetValue(joint, out JointAxis entry))
                return DefaultFingerJointAxes.Instance.CurlLocalRotation(joint, jointIndex, degrees);

            // 父空间轴 → 左乘；Rest 保留骨架自带的局部朝向
            return Quaternion.AngleAxis(degrees, entry.Axis) * entry.Rest;
        }

        /// <inheritdoc />
        public Quaternion RootAimLocalRotation(Transform finger, Vector3 worldDirection, Vector3 worldUp)
        {
            if (finger == null || finger.parent == null) return Quaternion.identity;

            if (!_boneDirectionLocal.TryGetValue(finger, out Vector3 boneLocal))
                return DefaultFingerJointAxes.Instance.RootAimLocalRotation(finger, worldDirection, worldUp);

            Vector3 aim = worldDirection;
            if (aim.sqrMagnitude < 1e-8f) return finger.localRotation;
            aim.Normalize();

            Vector3 up = worldUp;
            if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
            up.Normalize();
            if (Mathf.Abs(Vector3.Dot(up, aim)) > 0.999f)
            {
                // up 与瞄准方向共线 → 取任意垂直向量，避免 LookRotation 退化
                up = Vector3.Cross(aim, Vector3.forward);
                if (up.sqrMagnitude < 1e-6f) up = Vector3.Cross(aim, Vector3.right);
                up.Normalize();
            }

            Vector3 bindUp = _upLocal.TryGetValue(finger, out Vector3 storedUp) ? storedUp : Vector3.up;

            // 求 Q 使 Q * boneLocal = aim（滚转由 up 定），再把 Q 换算成父空间局部旋转
            Quaternion desiredWorld = Quaternion.LookRotation(aim, up) *
                                     Quaternion.Inverse(Quaternion.LookRotation(boneLocal, bindUp));
            return Quaternion.Inverse(finger.parent.rotation) * desiredWorld;
        }

        /// <inheritdoc />
        public Vector3 WorldCurlAxis(Transform joint, int jointIndex)
        {
            if (joint == null) return Vector3.right;
            if (!_joints.TryGetValue(joint, out JointAxis entry))
                return DefaultFingerJointAxes.Instance.WorldCurlAxis(joint, jointIndex);

            // 轴存在父关节局部空间：父骨一动，轴跟着转
            return joint.parent != null ? joint.parent.rotation * entry.Axis : entry.Axis;
        }
    }
}
