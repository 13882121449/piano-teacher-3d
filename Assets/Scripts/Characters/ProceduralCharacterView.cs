using System.Collections.Generic;
using PianoTeacher3D.Avatar;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 内置程序化角色的 <see cref="ICharacterView"/> 包装（P1）。
    /// <para>
    /// 它<b>不拥有</b>任何资源：几何与材质由 <c>AvatarManager</c> 在 <c>BuildAvatars</c> 时创建并统一释放。
    /// 视图只负责三件事：把「激活/停用」翻译成对应 rig 根节点的显隐、记录缩略图与整体色调、
    /// 如实回答「这个角色能不能替换脸部贴图」（答案是不能——程序化头部是图元拼接，没有面部 UV）。
    /// </para>
    /// <para>
    /// <see cref="ICharacterRigSource.IsBuiltInBacked"/> 恒为 true，因此 <c>AvatarManager</c>
    /// 的骨骼属性仍然走原有 <c>_rigs</c> 路径，3 个内置角色的行为不受角色库改造影响。
    /// </para>
    /// </summary>
    public sealed class ProceduralCharacterView : ICharacterView, ICharacterRigSource
    {
        private readonly AvatarManager _owner;
        private readonly int _index;
        private readonly CharacterProfile _profile;

        private readonly Dictionary<Material, Color> _originalColors = new Dictionary<Material, Color>();
        private bool _tinted;
        private bool _activated;

        /// <summary>创建包装。索引越界时 <see cref="ValidateRig"/> 会给出原因。</summary>
        public ProceduralCharacterView(AvatarManager owner, int index, CharacterProfile profile)
        {
            _owner = owner;
            _index = index;
            _profile = profile;
        }

        /// <summary>内置索引（0..2）。</summary>
        public int ProceduralIndex => _index;

        /// <summary>当前缩略图（未设置时为 null，UI 用占位图）。</summary>
        public Texture2D ThumbnailTexture { get; private set; }

        public string CharacterId
        {
            get
            {
                if (_profile != null) return _profile.CharacterId;
                if (_index >= 0 && _index < CharacterRegistry.BuiltInIds.Length)
                    return CharacterRegistry.BuiltInIds[_index];
                return string.Empty;
            }
        }

        public string DisplayName
        {
            get
            {
                if (_profile != null) return _profile.DisplayName;
                if (_index >= 0 && _index < CharacterRegistry.BuiltInDisplayNames.Length)
                    return CharacterRegistry.BuiltInDisplayNames[_index];
                return "未知角色";
            }
        }

        public CharacterStyle Style
        {
            get
            {
                if (_profile != null) return _profile.Style;
                return _index == 2 ? CharacterStyle.Cartoon : CharacterStyle.Realistic;
            }
        }

        public Transform RootTransform
        {
            get
            {
                var skeleton = Skeleton;
                return skeleton != null ? skeleton.Root : null;
            }
        }

        public Animator Animator => _owner != null ? _owner.GetAnimator(_index) : null;

        public bool IsActivated => _activated;

        /// <summary>
        /// 槽位准入判据（外观应用器只认它）。
        /// 缩略图与模型能力无关，恒为 true；整体色调有材质就能染；脸部贴图与头部材质槽对程序化角色不存在。
        /// </summary>
        public bool Supports(AppearanceSlot slot)
        {
            if (slot == AppearanceSlot.Thumbnail) return true;
            if (slot == AppearanceSlot.BodyTint) return true;
            return false;
        }

        public CharacterValidation ValidateRig()
        {
            if (_owner == null) return CharacterValidation.Fail("程序化角色缺少 AvatarManager。");

            var skeleton = _owner.GetSkeleton(_index);
            if (skeleton == null) return CharacterValidation.Fail($"内置索引 {_index} 的骨架不存在。");
            if (skeleton.Root == null) return CharacterValidation.Fail($"内置索引 {_index} 的骨架根节点缺失。");
            if (!skeleton.IsHumanoid) return CharacterValidation.Fail($"内置索引 {_index} 的骨架不是合法 Humanoid。");

            return CharacterValidation.Pass;
        }

        public void OnCharacterActivated()
        {
            if (_owner == null) return;
            _owner.SetBuiltInActive(_index, true);
            _activated = true;
        }

        public void OnCharacterDeactivated()
        {
            if (_owner == null)
            {
                _activated = false;
                return;
            }
            _owner.SetBuiltInActive(_index, false);
            _activated = false;
        }

        public bool ApplyAppearance(AppearanceSlot slot, Texture2D texture, out string error)
        {
            error = null;

            if (slot == AppearanceSlot.Thumbnail)
            {
                if (texture == null)
                {
                    error = "缩略图纹理为空：请先完成照片生成，再点「应用」。";
                    return false;
                }
                // 只换引用，不销毁旧纹理（所有权在调用方）
                ThumbnailTexture = texture;
                return true;
            }

            if (slot == AppearanceSlot.BodyTint)
            {
                if (texture == null)
                {
                    error = "整体色调纹理为空：请先完成照片生成，再点「应用」。";
                    return false;
                }
                if (!TryAverageColor(texture, out var tint))
                {
                    error = "纹理不可读，无法取平均色：请把生成的贴图导出为 Texture2D 并勾选 Read/Write Enabled，或直接传 1×1 平均色贴图。";
                    return false;
                }
                if (_owner == null || _owner.GetMaterials(_index).Count == 0)
                {
                    error = "该角色尚未构建材质（AvatarManager 可能未执行 BuildAvatars）：请先完成角色构建，或改用缩略图槽位。";
                    return false;
                }
                ApplyTint(tint);
                return true;
            }

            error = slot == AppearanceSlot.FaceTexture
                ? "程序化角色没有可替换的面部 UV（五官是椭球与图元拼接，换贴图不会生效）：请改用缩略图或整体色调槽位。"
                : "程序化角色没有独立的头部材质槽：请改用缩略图或整体色调槽位。";
            return false;
        }

        public bool ClearAppearance(AppearanceSlot slot, out string error)
        {
            error = null;

            if (slot == AppearanceSlot.Thumbnail)
            {
                ThumbnailTexture = null;
                return true;
            }

            if (slot == AppearanceSlot.BodyTint)
            {
                RestoreTint();
                return true;
            }

            error = "该槽位对程序化角色不适用。";
            return false;
        }

        /// <summary>视图不拥有资源：几何/材质由 AvatarManager 统一管理。这里只撤销自己造成的色调修改。</summary>
        public void DisposeCharacterResources()
        {
            RestoreTint();
            ThumbnailTexture = null;
            _activated = false;
        }

        // ==================== ICharacterRigSource（内置包装：AvatarManager 不会读这些骨骼） ====================

        public bool IsBuiltInBacked => true;

        public Transform LeftShoulder => Skeleton != null ? Skeleton.LeftShoulder : null;
        public Transform RightShoulder => Skeleton != null ? Skeleton.RightShoulder : null;
        public Transform LeftArm => Skeleton != null ? Skeleton.LeftUpperArm : null;
        public Transform RightArm => Skeleton != null ? Skeleton.RightUpperArm : null;
        public Transform LeftForeArm => Skeleton != null ? Skeleton.LeftLowerArm : null;
        public Transform RightForeArm => Skeleton != null ? Skeleton.RightLowerArm : null;
        public Transform LeftElbow => LeftForeArm;
        public Transform RightElbow => RightForeArm;
        public IHandRig LeftHand => _owner != null ? _owner.LeftHand : null;
        public IHandRig RightHand => _owner != null ? _owner.RightHand : null;

        public AvatarRigBuilder.Skeleton Skeleton => _owner != null ? _owner.GetSkeleton(_index) : null;

        public AvatarDefinition Definition => _owner != null ? _owner.CurrentDefinition : null;

        // ==================== 整体色调 ====================

        private void ApplyTint(Color tint)
        {
            if (_owner == null) return;

            var materials = _owner.GetMaterials(_index);
            if (materials == null || materials.Count == 0) return;

            if (!_tinted)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    var mat = materials[i];
                    if (mat == null || _originalColors.ContainsKey(mat)) continue;
                    if (!mat.HasProperty("_Color")) continue;
                    _originalColors.Add(mat, mat.color);
                }
                _tinted = true;
            }

            const float strength = 0.45f;
            foreach (var pair in _originalColors)
            {
                var mat = pair.Key;
                if (mat == null) continue;
                var target = Color.Lerp(pair.Value, tint, strength);
                target.a = pair.Value.a;
                mat.color = target;
            }
        }

        private void RestoreTint()
        {
            if (!_tinted) return;
            foreach (var pair in _originalColors)
            {
                if (pair.Key == null) continue;
                pair.Key.color = pair.Value;
            }
            _originalColors.Clear();
            _tinted = false;
        }

        /// <summary>取纹理的平均色（大图按步长抽样，避免一次读 4K 贴图卡住主线程）。</summary>
        private static bool TryAverageColor(Texture2D texture, out Color color)
        {
            color = Color.white;
            if (texture == null || !texture.isReadable) return false;

            try
            {
                var pixels = texture.GetPixels32();
                if (pixels == null || pixels.Length == 0) return false;

                int stride = pixels.Length > 4096 ? pixels.Length / 4096 : 1;
                double r = 0, g = 0, b = 0;
                int count = 0;
                for (int i = 0; i < pixels.Length; i += stride)
                {
                    r += pixels[i].r;
                    g += pixels[i].g;
                    b += pixels[i].b;
                    count++;
                }
                if (count == 0) return false;

                color = new Color((float)(r / count) / 255f, (float)(g / count) / 255f, (float)(b / count) / 255f);
                return true;
            }
            catch (UnityException)
            {
                return false;
            }
        }
    }
}
