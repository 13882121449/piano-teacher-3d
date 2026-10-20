using PianoTeacher3D.Avatar;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 默认的视图工厂：程序化角色交给 <see cref="ProceduralCharacterView"/> 包装，
    /// 预制体角色交给 <see cref="RocketboxCharacterView"/> 加载。
    /// <para>
    /// 之所以把它放在 <c>Characters</c> 而不是 <c>Avatar</c>：角色来源的知识属于角色库，
    /// <c>AvatarManager</c> 只负责持有工厂实例并把骨骼属性转述出去。
    /// </para>
    /// </summary>
    public sealed class AvatarCharacterViewFactory : ICharacterViewFactory
    {
        private readonly AvatarManager _avatar;
        private readonly Transform _parent;

        public AvatarCharacterViewFactory(AvatarManager avatar)
        {
            _avatar = avatar;
            _parent = avatar != null ? avatar.transform : null;
        }

        public bool Supports(CharacterProfile profile)
        {
            if (profile == null) return false;
            // 程序化角色必须有 AvatarManager 才有 rig 可激活；预制体角色独立于场景
            return profile.Source != CharacterSource.ProceduralBuiltIn || _avatar != null;
        }

        public ICharacterView Create(CharacterProfile profile, out string error)
        {
            error = null;
            if (profile == null)
            {
                error = "角色资料为空。";
                return null;
            }

            if (profile.Source == CharacterSource.ProceduralBuiltIn)
            {
                if (_avatar == null)
                {
                    error = "缺少 AvatarManager，无法激活程序化角色。";
                    return null;
                }
                return new ProceduralCharacterView(_avatar, profile.ProceduralIndex, profile);
            }

            return RocketboxCharacterView.TryCreate(profile, _parent, out error);
        }
    }
}
