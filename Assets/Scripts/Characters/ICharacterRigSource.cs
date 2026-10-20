using PianoTeacher3D.Avatar;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 可选扩展接口：让外部角色向 <c>AvatarManager</c> 提供骨骼与 IK 参数。
    /// <para>
    /// <c>HandIKController</c>、<c>FingerPoseController</c>、<c>CameraModeController</c> 只认
    /// <c>AvatarManager</c> 上的公开属性；外部角色无法写进 <c>AvatarManager._rigs</c>，
    /// 因此通过本接口把「肩 / 上臂 / 前臂 / 肘 / 手」与标准骨架交给它转述。
    /// </para>
    /// <para>
    /// <see cref="IsBuiltInBacked"/> 为 true 表示该视图只是内置 <c>_rigs</c> 的包装，
    /// <c>AvatarManager</c> 必须继续走原有 <c>_rigs</c> 路径（保证 3 个内置角色行为逐位不变）。
    /// </para>
    /// </summary>
    public interface ICharacterRigSource
    {
        /// <summary>是否只是内置程序化角色的包装（true 时 AvatarManager 不使用本接口的骨骼）。</summary>
        bool IsBuiltInBacked { get; }

        Transform LeftShoulder { get; }
        Transform RightShoulder { get; }
        Transform LeftArm { get; }
        Transform RightArm { get; }
        Transform LeftForeArm { get; }
        Transform RightForeArm { get; }
        Transform LeftElbow { get; }
        Transform RightElbow { get; }
        IHandRig LeftHand { get; }
        IHandRig RightHand { get; }

        /// <summary>标准 Humanoid 骨架（供相机取景、躯干包围盒与 IK 校验复用）。</summary>
        AvatarRigBuilder.Skeleton Skeleton { get; }

        /// <summary>IK 参数（手部偏移 / 腕部俯仰等）；不会为 null。</summary>
        AvatarDefinition Definition { get; }
    }
}
