using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 手指关节轴策略：把「这根手指弯曲 N 度」翻译成该骨骼真正该做的局部旋转。
    ///
    /// <para>
    /// 为什么需要这层抽象：<see cref="FingerPoseController"/> 的 CCD 与写回都用
    /// <c>Quaternion.Euler(deg, 0, 0)</c>，隐含假设「骨骼局部 +X 是弯曲轴、+Z 是骨骼方向」。
    /// 程序化手部（<c>AvatarRigBuilder</c> 生成）满足该假设；但导入的 Biped 模型骨架是
    /// 3ds Max Biped 的局部轴约定，直接套用会把手指拧成麻花而不是弯曲。
    /// 在蒙皮骨骼链中间插「规范基 wrapper 节点」不可行——<c>SkinnedMeshRenderer.bones</c>
    /// 是硬引用，改层级等于换骨骼，必然破面。所以只能把轴映射做成策略对象。
    /// </para>
    ///
    /// <para>
    /// 字符串与实现约定：<see cref="DefaultFingerJointAxes"/> 必须与改造前的行为**逐位一致**，
    /// 现有 460 条 EditMode 与 109 条 PlayMode 用例都建立在此假设上。
    /// </para>
    /// </summary>
    public interface IFingerJointAxes
    {
        /// <summary>
        /// 返回「该关节弯曲 <paramref name="degrees"/> 度」时应写入的 <c>localRotation</c>。
        /// 实现必须包含该关节的绑定（rest）姿态，不能只返回旋转增量。
        /// </summary>
        /// <param name="joint">关节骨（MCP / PIP / DIP）。</param>
        /// <param name="jointIndex">0 = 指根 MCP，1 = PIP，2 = DIP。</param>
        /// <param name="degrees">弯曲角，调用方已保证 ≥ 0（不允许反关节）。</param>
        Quaternion CurlLocalRotation(Transform joint, int jointIndex, float degrees);

        /// <summary>
        /// 返回「指根链的前向指向 <paramref name="worldDirection"/>、滚转让手背朝
        /// <paramref name="worldUp"/>」时 <paramref name="finger"/> 应有的 <c>localRotation</c>。
        /// </summary>
        Quaternion RootAimLocalRotation(Transform finger, Vector3 worldDirection, Vector3 worldUp);

        /// <summary>
        /// 返回该关节铰链轴的世界方向，供 CCD 在垂直平面上求角。
        /// </summary>
        Vector3 WorldCurlAxis(Transform joint, int jointIndex);
    }

    /// <summary>
    /// 程序化手部使用的默认策略：与 <c>FingerPoseController</c> 改造前的算法完全等价。
    /// 无状态，可安全共享。
    /// </summary>
    public sealed class DefaultFingerJointAxes : IFingerJointAxes
    {
        /// <summary>共享实例（无状态）。</summary>
        public static readonly DefaultFingerJointAxes Instance = new DefaultFingerJointAxes();

        private DefaultFingerJointAxes() { }

        /// <inheritdoc />
        public Quaternion CurlLocalRotation(Transform joint, int jointIndex, float degrees)
        {
            // 绕局部 X：正角 = 指尖下压（与改造前逐位一致）
            return Quaternion.Euler(degrees, 0f, 0f);
        }

        /// <inheritdoc />
        public Quaternion RootAimLocalRotation(Transform finger, Vector3 worldDirection, Vector3 worldUp)
        {
            if (finger == null || finger.parent == null) return Quaternion.identity;

            Transform chain = finger.childCount > 0 ? finger.GetChild(0) : null;
            Quaternion desiredChainWorld = Quaternion.LookRotation(worldDirection, worldUp);
            Quaternion rootWorld = desiredChainWorld *
                                   Quaternion.Inverse(chain != null ? chain.localRotation : Quaternion.identity);
            return Quaternion.Inverse(finger.parent.rotation) * rootWorld;
        }

        /// <inheritdoc />
        public Vector3 WorldCurlAxis(Transform joint, int jointIndex)
        {
            return joint != null ? joint.rotation * Vector3.right : Vector3.right;
        }
    }
}
