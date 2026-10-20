using PianoTeacher3D.Avatar;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 可选能力：角色能告诉演奏层「我的手指关节该怎么弯」。
    ///
    /// <para>
    /// 为什么不放进 <see cref="ICharacterView"/>：这是**可选**能力，程序化角色没有额外信息
    /// （用默认的局部 X 轴即可），把它塞进主契约会逼所有实现都返回一个恒等对象，
    /// 反而让契约变模糊。演奏层用 <c>view is ICharacterJointAxesProvider</c> 判断即可。
    /// </para>
    /// </summary>
    public interface ICharacterJointAxesProvider
    {
        /// <summary>该角色的手指关节轴策略；左右手共用同一份实例。</summary>
        IFingerJointAxes JointAxes { get; }
    }
}
