using System;
using System.Collections.Generic;
using PianoTeacher3D.Avatar;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// Rocketbox 导入角色的 <see cref="ICharacterView"/> 实现（P2）。
    /// <para>
    /// <b>生命周期</b>：<see cref="TryCreate"/> 加载并实例化预制体 → 建立骨骼索引与手部 rig →
    /// 由切换服务调用 <see cref="OnCharacterActivated"/> / <see cref="OnCharacterDeactivated"/> →
    /// 最终 <see cref="DisposeCharacterResources"/> 销毁实例（含它创建的所有材质实例）。
    /// </para>
    /// <para>
    /// <b>资源所有权</b>：实例化的根节点、坐姿调整产生的材质实例登记在
    /// <see cref="CharacterResourceScope"/> 里；<c>Resources.Load</c> 拿到的预制体与贴图属于共享资产，
    /// 一律不登记、不销毁。
    /// </para>
    /// <para>
    /// <b>已知限制</b>（见 <c>Docs/character-asset-requirements.md</c>）：
    /// ① 模型是站位 T-Pose，坐姿角度未标定；
    /// ② 手部骨骼为 Biped 命名，<c>FingerPoseController</c> 的「+Z 弯曲轴」假设不成立，
    ///    因此手指姿态驱动的视觉正确性未验证——本类只保证<b>结构与读数</b>可用；
    /// ③ 手臂 IK 会直接改骨骼 Transform，蒙皮模型上的视觉效果未验证。
    /// </para>
    /// </summary>
    public sealed class RocketboxCharacterView : ICharacterView, ICharacterRigSource, ICharacterJointAxesProvider
    {
        private readonly CharacterProfile _profile;
        private readonly CharacterResourceScope _scope;
        private GameObject _root;
        private readonly Animator _animator;
        private readonly SittingPoseApplier _pose;
        private readonly AvatarRigBuilder.Skeleton _skeleton;
        private readonly AvatarDefinition _definition;
        private readonly RocketboxHandRig _leftHand;
        private readonly RocketboxHandRig _rightHand;
        private readonly string _handError;

        /// <summary>由绑定姿态推导的手指关节轴策略（左右手合一；推导失败时退回默认策略）。</summary>
        private IFingerJointAxes _jointAxes = DefaultFingerJointAxes.Instance;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Material> _ownMaterials = new List<Material>();
        private readonly Dictionary<Material, Color> _originalColors = new Dictionary<Material, Color>();

        private Texture2D _thumbnail;
        private bool _tinted;
        private bool _activated;

        private RocketboxCharacterView(CharacterProfile profile, CharacterResourceScope scope, GameObject root,
            Animator animator, AvatarRigBuilder.Skeleton skeleton, RocketboxHandRig left, RocketboxHandRig right,
            string handError, SittingPoseSettings poseSettings, AvatarDefinition definition)
        {
            _profile = profile;
            _scope = scope;
            _root = root;
            _animator = animator;
            _skeleton = skeleton;
            _leftHand = left;
            _rightHand = right;
            _handError = handError;
            _definition = definition;
            _pose = new SittingPoseApplier(poseSettings);
        }

        /// <summary>
        /// 加载并实例化角色。失败时返回 null 并通过 <paramref name="error"/> 给出可读原因（不抛异常）。
        /// </summary>
        public static RocketboxCharacterView TryCreate(CharacterProfile profile, Transform parent, out string error)
        {
            error = null;
            if (profile == null)
            {
                error = "角色资料为空。";
                return null;
            }

            GameObject prefab = profile.Prefab;
            try
            {
                if (prefab == null && !string.IsNullOrWhiteSpace(profile.ResourcesPath))
                    prefab = Resources.Load<GameObject>(profile.ResourcesPath);
            }
            catch (Exception e)
            {
                error = "预制体加载异常：" + e.Message;
                return null;
            }

            if (prefab == null)
            {
                error = string.IsNullOrWhiteSpace(profile.ResourcesPath)
                    ? $"角色「{profile.DisplayName}」没有可加载的预制体。"
                    : $"在 Resources/{profile.ResourcesPath} 找不到预制体。";
                return null;
            }

            GameObject root = null;
            try
            {
                root = UnityEngine.Object.Instantiate(prefab);
                root.name = "Character_" + profile.CharacterId;
                if (parent != null) root.transform.SetParent(parent, false);

                float scale = profile.RootScale > 0.01f ? profile.RootScale : 1f;
                root.transform.localScale = Vector3.one * scale;
                root.transform.localRotation = Quaternion.Euler(0f, profile.RootYawDegrees, 0f);
                root.transform.localPosition = Vector3.zero;
            }
            catch (Exception e)
            {
                error = "角色实例化异常：" + e.Message;
                return null;
            }

            var scope = new CharacterResourceScope(profile.CharacterId);
            scope.Own(root);   // 只有这个根节点是「我创建的」，它的材质实例稍后按需登记

            var animator = root.GetComponentInChildren<Animator>(true);
            var bones = CharacterAssetValidator.BuildTransformIndex(root);

            var left = RocketboxHandRig.Create(true, bones, out string leftError);
            var right = RocketboxHandRig.Create(false, bones, out string rightError);
            string handError = leftError ?? rightError;

            var skeleton = BuildSkeleton(root.transform, animator, bones, left, right);
            var definition = BuildDefinition(profile);

            var poseSettings = new SittingPoseSettings
            {
                SeatHeightOffset = profile.SeatHeightOffset,
                Calibrated = false
            };

            var view = new RocketboxCharacterView(profile, scope, root, animator, skeleton,
                left, right, handError, poseSettings, definition);
            view.CollectRenderers();
            view.BuildJointAxes();

            if (left == null || right == null)
            {
                // 结构不完整时不留下半成品：直接释放，让切换服务走回滚路径
                error = handError ?? "手部骨骼绑定失败。";
                view.DisposeCharacterResources();
                return null;
            }

            return view;
        }

        public string CharacterId => _profile.CharacterId;

        public string DisplayName => _profile.DisplayName;

        public CharacterStyle Style => _profile.Style;

        public Transform RootTransform => _root != null ? _root.transform : null;

        public Animator Animator => _animator;

        public bool IsActivated => _activated;

        /// <summary>坐姿参数（可在运行时调整后重新激活生效；默认值待标定）。</summary>
        public SittingPoseSettings PoseSettings => _pose.Settings;

        public RocketboxHandRig RocketboxLeftHand => _leftHand;

        public RocketboxHandRig RocketboxRightHand => _rightHand;

        /// <summary>手指关节轴策略（左右手合一）。推导失败时返回默认策略，演奏层无需分支。</summary>
        public IFingerJointAxes JointAxes => _jointAxes;

        /// <summary>
        /// 用绑定姿态推导铰链轴。必须在坐姿应用**之前**取到手背法线：
        /// 站姿 T-Pose 下手背朝向是确定的，摆成坐姿后再算会被手臂旋转污染。
        /// </summary>
        private void BuildJointAxes()
        {
            if (_leftHand == null || _rightHand == null) return;

            var axes = new ImportedFingerJointAxes();
            bool leftOk = axes.AddHand(_leftHand, WorldBackNormal(_leftHand), out string leftError);
            bool rightOk = axes.AddHand(_rightHand, WorldBackNormal(_rightHand), out string rightError);

            if (!leftOk && !rightOk)
            {
                _jointAxes = DefaultFingerJointAxes.Instance;
                Debug.LogWarning($"角色「{CharacterId}」手指轴推导失败，退回默认轴：" +
                                 $"{leftError} / {rightError}");
                return;
            }

            _jointAxes = axes;

            if (!leftOk) Debug.LogWarning($"角色「{CharacterId}」左手轴推导失败：{leftError}");
            if (!rightOk) Debug.LogWarning($"角色「{CharacterId}」右手轴推导失败：{rightError}");
        }

        /// <summary>
        /// <see cref="RocketboxHandRig.BackNormal"/> 是**腕骨局部空间**的向量
        /// （在 <c>DeriveCanonicalBasis</c> 里用 <c>Wrist.InverseTransformPoint</c> 推出，
        /// 配套的 <c>CanonicalToWrist</c> 也印证了这一点）。铰链轴推导需要世界向量，
        /// 直接拿局部向量当世界用会让整只手指的弯曲平面偏转约 90°（实测指尖只横移 1–3 mm）。
        /// </summary>
        private static Vector3 WorldBackNormal(RocketboxHandRig hand)
        {
            if (hand == null) return Vector3.up;
            return hand.Wrist != null ? hand.Wrist.rotation * hand.BackNormal : hand.BackNormal;
        }

        /// <summary>
        /// 槽位准入判据（外观应用器只认它，不认资料声明）。
        /// <para>
        /// 缩略图是「卡片图片」，与模型的 UV/材质能力无关，因此<b>对所有角色恒为 true</b>；
        /// 整体色调要求资料声明过该槽位；脸部贴图与头部材质槽本实现做不到，恒为 false。
        /// </para>
        /// </summary>
        public bool Supports(AppearanceSlot slot)
        {
            if (slot == AppearanceSlot.Thumbnail) return true;
            if (slot != AppearanceSlot.BodyTint) return false;
            return _profile != null && _profile.SupportsSlot(slot);
        }

        public CharacterValidation ValidateRig()
        {
            if (_root == null) return CharacterValidation.Fail("角色实例已被销毁。");

            if (_leftHand == null || !_leftHand.IsValid)
                return CharacterValidation.Fail(_handError ?? "左手骨骼不完整。");
            if (_rightHand == null || !_rightHand.IsValid)
                return CharacterValidation.Fail(_handError ?? "右手骨骼不完整。");

            return CharacterAssetValidator.ValidatePrefabRoot(_root);
        }

        public void OnCharacterActivated()
        {
            if (_root == null) return;
            if (!_root.activeSelf) _root.SetActive(true);
            _pose.Apply(_animator);
            _activated = true;
        }

        public void OnCharacterDeactivated()
        {
            if (_root == null)
            {
                _activated = false;
                return;
            }
            if (_root.activeSelf) _root.SetActive(false);
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
                // 只换引用，不销毁旧纹理（所有权在 applier 一侧）
                _thumbnail = texture;
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
                if (!HasTintableMaterial())
                {
                    error = "该角色没有可染色的材质（渲染器缺失）：请重新导入角色或改用缩略图槽位。";
                    return false;
                }
                ApplyTint(tint);
                return true;
            }

            error = slot == AppearanceSlot.FaceTexture
                ? "该角色尚未适配脸部 UV 替换：请改用缩略图或整体色调槽位。"
                : "该角色没有独立的头部材质槽：请改用缩略图或整体色调槽位。";
            return false;
        }

        public bool ClearAppearance(AppearanceSlot slot, out string error)
        {
            error = null;
            if (slot == AppearanceSlot.Thumbnail)
            {
                _thumbnail = null;   // 立即换掉引用：调用方随后就会销毁自有旧纹理
                return true;
            }
            if (slot == AppearanceSlot.BodyTint)
            {
                RestoreTint();
                return true;
            }
            error = "该槽位对该角色不适用：该角色的外观槽位只有缩略图与整体色调。";
            return false;
        }

        public void DisposeCharacterResources()
        {
            RestoreTint();
            _thumbnail = null;
            _activated = false;
            _scope.Dispose();

            // 材质实例已被作用域销毁，这里只清引用，避免后续误用销毁后的对象
            _ownMaterials.Clear();
            _originalColors.Clear();
            _renderers.Clear();

            _root = null;   // 避免外部再访问已销毁 GameObject 的 .transform（会抛 MissingReferenceException）
        }

        // ==================== ICharacterRigSource ====================

        public bool IsBuiltInBacked => false;

        public Transform LeftShoulder => _skeleton != null ? _skeleton.LeftShoulder : null;
        public Transform RightShoulder => _skeleton != null ? _skeleton.RightShoulder : null;
        public Transform LeftArm => _skeleton != null ? _skeleton.LeftUpperArm : null;
        public Transform RightArm => _skeleton != null ? _skeleton.RightUpperArm : null;
        public Transform LeftForeArm => _skeleton != null ? _skeleton.LeftLowerArm : null;
        public Transform RightForeArm => _skeleton != null ? _skeleton.RightLowerArm : null;
        public Transform LeftElbow => LeftForeArm;
        public Transform RightElbow => RightForeArm;
        public IHandRig LeftHand => _leftHand;
        public IHandRig RightHand => _rightHand;

        public AvatarRigBuilder.Skeleton Skeleton => _skeleton;

        public AvatarDefinition Definition => _definition;

        // ==================== 构建辅助 ====================

        private void CollectRenderers()
        {
            _renderers.Clear();
            if (_root == null) return;
            var found = _root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null) _renderers.Add(found[i]);
            }
        }

        /// <summary>
        /// 用导入模型的真实骨骼拼一个 <see cref="AvatarRigBuilder.Skeleton"/>，
        /// 让相机取景（<c>CurrentSkeleton != null</c>）、躯干包围盒与 IK 校验都能复用同一套接口。
        /// </summary>
        private static AvatarRigBuilder.Skeleton BuildSkeleton(Transform root, Animator animator,
            Dictionary<string, Transform> bones, RocketboxHandRig left, RocketboxHandRig right)
        {
            var skeleton = new AvatarRigBuilder.Skeleton { Root = root };

            skeleton.Hips = Find(bones, animator, "Bip01 Pelvis", HumanBodyBones.Hips);
            skeleton.Spine = Find(bones, animator, "Bip01 Spine", HumanBodyBones.Spine);
            skeleton.Chest = Find(bones, animator, "Bip01 Spine1", HumanBodyBones.Chest);
            skeleton.UpperChest = Find(bones, animator, "Bip01 Spine2", HumanBodyBones.UpperChest);
            skeleton.Neck = Find(bones, animator, "Bip01 Neck", HumanBodyBones.Neck);
            skeleton.Head = Find(bones, animator, "Bip01 Head", HumanBodyBones.Head);

            skeleton.LeftShoulder = Find(bones, animator, "Bip01 L Clavicle", HumanBodyBones.LeftShoulder);
            skeleton.LeftUpperArm = Find(bones, animator, "Bip01 L UpperArm", HumanBodyBones.LeftUpperArm);
            skeleton.LeftLowerArm = Find(bones, animator, "Bip01 L Forearm", HumanBodyBones.LeftLowerArm);
            skeleton.LeftHand = Find(bones, animator, "Bip01 L Hand", HumanBodyBones.LeftHand);

            skeleton.RightShoulder = Find(bones, animator, "Bip01 R Clavicle", HumanBodyBones.RightShoulder);
            skeleton.RightUpperArm = Find(bones, animator, "Bip01 R UpperArm", HumanBodyBones.RightUpperArm);
            skeleton.RightLowerArm = Find(bones, animator, "Bip01 R Forearm", HumanBodyBones.RightLowerArm);
            skeleton.RightHand = Find(bones, animator, "Bip01 R Hand", HumanBodyBones.RightHand);

            skeleton.LeftUpperLeg = Find(bones, animator, "Bip01 L Thigh", HumanBodyBones.LeftUpperLeg);
            skeleton.LeftLowerLeg = Find(bones, animator, "Bip01 L Calf", HumanBodyBones.LeftLowerLeg);
            skeleton.LeftFoot = Find(bones, animator, "Bip01 L Foot", HumanBodyBones.LeftFoot);
            skeleton.RightUpperLeg = Find(bones, animator, "Bip01 R Thigh", HumanBodyBones.RightUpperLeg);
            skeleton.RightLowerLeg = Find(bones, animator, "Bip01 R Calf", HumanBodyBones.RightLowerLeg);
            skeleton.RightFoot = Find(bones, animator, "Bip01 R Foot", HumanBodyBones.RightFoot);

            for (int f = 0; f < RocketboxHandRig.FingersPerHand; f++)
            {
                skeleton.LeftFingerJoints[f] = new Transform[RocketboxHandRig.SegmentsPerFinger];
                skeleton.RightFingerJoints[f] = new Transform[RocketboxHandRig.SegmentsPerFinger];
            }

            if (left != null && left.IsValid)
            {
                for (int f = 0; f < RocketboxHandRig.FingersPerHand; f++)
                {
                    if (!left.TryGetJoints(f, out var p, out var m, out var d)) continue;
                    skeleton.LeftFingers[f] = p;
                    skeleton.LeftFingerJoints[f][0] = p;
                    skeleton.LeftFingerJoints[f][1] = m;
                    skeleton.LeftFingerJoints[f][2] = d;
                }
            }
            if (right != null && right.IsValid)
            {
                for (int f = 0; f < RocketboxHandRig.FingersPerHand; f++)
                {
                    if (!right.TryGetJoints(f, out var p, out var m, out var d)) continue;
                    skeleton.RightFingers[f] = p;
                    skeleton.RightFingerJoints[f][0] = p;
                    skeleton.RightFingerJoints[f][1] = m;
                    skeleton.RightFingerJoints[f][2] = d;
                }
            }

            skeleton.Avatar = animator != null ? animator.avatar : null;
            return skeleton;
        }

        private static Transform Find(Dictionary<string, Transform> bones, Animator animator,
            string boneName, HumanBodyBones fallback)
        {
            if (bones != null && bones.TryGetValue(boneName, out var t) && t != null) return t;
            if (animator != null)
            {
                try { return animator.GetBoneTransform(fallback); }
                catch (Exception) { return null; }
            }
            return null;
        }

        /// <summary>
        /// IK 参数兜底：导入模型的掌宽/指长比例与程序化角色不同，这里先用默认定义，
        /// 每角色偏移保持 0（不影响 <c>HandHeight</c>/<c>HandDepth</c> 的既有语义）。
        /// </summary>
        private static AvatarDefinition BuildDefinition(CharacterProfile profile)
        {
            var def = AvatarDefinition.CreateDefault();
            if (def == null) return null;
            def.Id = profile.CharacterId;
            def.DisplayName = profile.DisplayName;
            def.HandHeightOffset = 0f;
            def.HandDepthOffset = 0f;
            def.Cartoon = false;
            return def;
        }

        // ==================== 整体色调（材质实例化 + 登记） ====================

        private void ApplyTint(Color tint)
        {
            if (_renderers.Count == 0) return;

            if (_ownMaterials.Count == 0)
            {
                for (int i = 0; i < _renderers.Count; i++)
                {
                    var renderer = _renderers[i];
                    if (renderer == null) continue;

                    var shared = renderer.sharedMaterials;
                    if (shared == null || shared.Length == 0) continue;

                    var instances = new Material[shared.Length];
                    for (int m = 0; m < shared.Length; m++)
                    {
                        var src = shared[m];
                        if (src == null) continue;
                        // 必须实例化：直接改 sharedMaterial 会污染工程里的共享资产
                        var inst = new Material(src) { name = src.name + " (Character)" };
                        _scope.Own(inst);
                        _ownMaterials.Add(inst);
                        instances[m] = inst;
                        if (inst.HasProperty("_Color")) _originalColors[inst] = inst.color;
                    }
                    renderer.sharedMaterials = instances;
                }
            }

            const float strength = 0.35f;
            for (int i = 0; i < _ownMaterials.Count; i++)
            {
                var mat = _ownMaterials[i];
                if (mat == null || !_originalColors.TryGetValue(mat, out var baseColor)) continue;
                var target = Color.Lerp(baseColor, tint, strength);
                target.a = baseColor.a;
                mat.color = target;
            }
            _tinted = true;
        }

        /// <summary>是否存在可染色的材质（渲染器缺失时不该谎报成功）。</summary>
        private bool HasTintableMaterial()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;
                var shared = renderer.sharedMaterials;
                if (shared == null) continue;
                for (int m = 0; m < shared.Length; m++)
                {
                    if (shared[m] != null) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 恢复材质原色。<b>故意保留</b>已实例化的材质与被替换的材质槽——
        /// 否则「清除后再次应用」会重新 new 一批材质并再次登记，导致同一次会话里材质实例越积越多。
        /// 实例本身由资源作用域在 Dispose 时统一销毁。
        /// </summary>
        private void RestoreTint()
        {
            if (!_tinted) return;

            for (int i = 0; i < _ownMaterials.Count; i++)
            {
                var mat = _ownMaterials[i];
                if (mat == null || !_originalColors.TryGetValue(mat, out var baseColor)) continue;
                mat.color = baseColor;
            }
            _tinted = false;
        }

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
