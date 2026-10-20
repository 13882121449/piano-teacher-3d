using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 统一角色契约（方案 7.2）。演奏层只依赖本接口，不直接引用具体角色类型，
    /// 因此换角色不需要改写 MIDI、音频、指法分配或手部 IK。
    ///
    /// 实现约定（三条硬约束）：
    /// 1. <see cref="ValidateRig"/> 必须在**不抛异常**的前提下返回可读原因；
    ///    缺骨骼时返回失败，调用方保留原角色，不得产生 NullReferenceException 连锁错误（方案 6.4）。
    /// 2. <see cref="ApplyAppearance"/> 只能替换角色自己声明支持的槽位（<see cref="Supports"/> 为真），
    ///    且必须**先建立新引用、再释放旧资源**，失败时回退到上一份外观（方案 9.6 / 11.2）。
    /// 3. <see cref="DisposeCharacterResources"/> 由切换服务在**新角色确认可驱动之后**调用，
    ///    实现方负责自己创建（new）的纹理/材质/网格；不得销毁共享资源（方案 11.2 / P1-5）。
    /// </summary>
    public interface ICharacterView
    {
        /// <summary>稳定唯一 ID（见 <see cref="CharacterIdRules"/>）。</summary>
        string CharacterId { get; }

        /// <summary>美术风格。</summary>
        CharacterStyle Style { get; }

        /// <summary>角色根节点。未激活时可能为 null。</summary>
        Transform RootTransform { get; }

        /// <summary>Humanoid Animator；内置程序化角色可能为 null。</summary>
        Animator Animator { get; }

        /// <summary>是否已完成激活（<see cref="OnCharacterActivated"/> 之后、<see cref="OnCharacterDeactivated"/> 之前）。</summary>
        bool IsActivated { get; }

        /// <summary>该角色是否支持某个外观槽位。</summary>
        bool Supports(AppearanceSlot slot);

        /// <summary>校验骨骼、Animator、材质是否齐全可用。</summary>
        CharacterValidation ValidateRig();

        /// <summary>激活：接入手部控制、开启渲染、恢复姿态。必须幂等。</summary>
        void OnCharacterActivated();

        /// <summary>停用：停止驱动、关闭渲染。必须幂等，且不得销毁资源（销毁走 Dispose）。</summary>
        void OnCharacterDeactivated();

        /// <summary>
        /// 应用外观到指定槽位。仅当 <see cref="Supports"/> 为真时才允许调用。
        /// 失败时 <paramref name="error"/> 给出可读原因，且当前外观保持不变。
        /// </summary>
        bool ApplyAppearance(AppearanceSlot slot, Texture2D texture, out string error);

        /// <summary>恢复该槽位的默认外观（方案 12.2「恢复默认」）。</summary>
        bool ClearAppearance(AppearanceSlot slot, out string error);

        /// <summary>释放角色独占资源。调用后该实例不再可用。</summary>
        void DisposeCharacterResources();
    }
}
