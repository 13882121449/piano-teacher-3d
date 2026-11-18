using System;
using System.Collections.Generic;
using PianoTeacher3D.Avatar;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// VRM 角色（UniVRM VRM-1.0 导入器生成的预制体）的 <see cref="ICharacterView"/> 实现。
    /// <para>
    /// 与写实导入角色走同一套演奏管线（坐姿应用器、旋转式手臂 IK、手指关节轴推导），区别只在三处：
    /// ① 骨骼一律从 Humanoid 映射取，不依赖骨骼命名；
    /// ② 指尖参考点用末节蒙皮顶点标定（VRoid 的末节骨只覆盖指尖一半，等长外推会落到指甲外）；
    /// ③ 材质是导入器生成的 URP MToon10，直接使用，不做管线重映射；表情、视线、头发弹簧骨骼由
    ///    预制体上的 <c>Vrm10Instance</c>（执行顺序 11000，在手臂 IK 与手指之后）处理。
    /// </para>
    /// <para><b>资源所有权</b>：只登记实例化的根节点；预制体、网格、MToon 材质都是导入资产，不登记、不销毁。</para>
    /// </summary>
    public sealed class VrmCharacterView : ICharacterView, ICharacterRigSource, ICharacterJointAxesProvider
    {
        private readonly CharacterProfile _profile;
        private readonly CharacterResourceScope _scope;
        private GameObject _root;
        private readonly Animator _animator;
        private readonly SittingPoseApplier _pose;
        private readonly AvatarRigBuilder.Skeleton _skeleton;
        private readonly AvatarDefinition _definition;
        private readonly HumanoidHandRig _leftHand;
        private readonly HumanoidHandRig _rightHand;
        private readonly string _handError;
        private IFingerJointAxes _jointAxes = DefaultFingerJointAxes.Instance;
        private PianoStool _stool;
        private VrmPerformerLife _life;
        private Texture2D _thumbnail;
        private bool _activated;

        private VrmCharacterView(CharacterProfile profile, CharacterResourceScope scope, GameObject root,
            Animator animator, AvatarRigBuilder.Skeleton skeleton, HumanoidHandRig left, HumanoidHandRig right,
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

        /// <summary>加载并实例化 VRM 角色。失败时返回 null 并给出可读原因（不抛异常）。</summary>
        public static VrmCharacterView TryCreate(CharacterProfile profile, Transform parent, out string error)
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
                error = $"在 Resources/{profile.ResourcesPath} 找不到 VRM 预制体（检查 UniVRM 是否已安装）。";
                return null;
            }

            GameObject root;
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
            scope.Own(root);

            var animator = root.GetComponentInChildren<Animator>(true);
            var left = HumanoidHandRig.CreateFromAnimator(true, animator, out string leftError);
            var right = HumanoidHandRig.CreateFromAnimator(false, animator, out string rightError);
            string handError = leftError ?? rightError;
            if (left != null && right != null)
            {
                var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                left.CalibrateFingertips(skinned);
                right.CalibrateFingertips(skinned);
                left.CalibrateThumbPad(skinned, 0.30f);
                right.CalibrateThumbPad(skinned, 0.30f);
            }

            var skeleton = BuildSkeleton(root.transform, animator, left, right);
            var definition = BuildDefinition(profile);
            var poseSettings = CreatePoseSettings(profile);

            var view = new VrmCharacterView(profile, scope, root, animator, skeleton, left, right,
                handError, poseSettings, definition);
            view.BuildJointAxes();

            if (left == null || right == null)
            {
                error = handError ?? "手部骨骼绑定失败。";
                view.DisposeCharacterResources();
                return null;
            }

            view._stool = new PianoStool(root.transform, scope);
            view.TuneMaterials();
            view._life = root.AddComponent<VrmPerformerLife>();
            view._life.Bind(animator);
            return view;
        }

        /// <summary>坐姿标定（2026-11-18，genki / VRoid AvatarSample_A）。</summary>
        internal static SittingPoseSettings CreatePoseSettings(CharacterProfile profile)
        {
            return new SittingPoseSettings
            {
                SeatHeightOffset = profile != null ? profile.SeatHeightOffset : 0f,
                // VRoid 的髋关节离臀底比写实骨架近（身体更小），坐姿髋—凳面差取 0.068 m
                HipAboveSeat = 0.068f,
                SeatDepth = 0.43f,
                ThighPitchDegrees = 6f,
                ShinPitchDegrees = 4f,
                FootPitchDegrees = 4f,
                Calibrated = true
            };
        }

        public string CharacterId => _profile.CharacterId;

        public string DisplayName => _profile.DisplayName;

        public CharacterStyle Style => _profile.Style;

        public Transform RootTransform => _root != null ? _root.transform : null;

        public Animator Animator => _animator;

        public bool IsActivated => _activated;

        public SittingPoseSettings PoseSettings => _pose.Settings;

        public HumanoidHandRig HumanoidLeftHand => _leftHand;

        public HumanoidHandRig HumanoidRightHand => _rightHand;

        public IFingerJointAxes JointAxes => _jointAxes;

        /// <summary>琴凳（随坐姿落座；诊断 / 测试用）。</summary>
        public Transform Stool => _stool?.Root;

        /// <summary>只支持缩略图槽位：VRM 的外观由 MToon 材质决定，不做整体染色。</summary>
        public bool Supports(AppearanceSlot slot) => slot == AppearanceSlot.Thumbnail;

        public CharacterValidation ValidateRig()
        {
            if (_root == null) return CharacterValidation.Fail("角色实例已被销毁。");
            if (_leftHand == null || !_leftHand.IsValid)
                return CharacterValidation.Fail(_handError ?? "左手骨骼不完整。");
            if (_rightHand == null || !_rightHand.IsValid)
                return CharacterValidation.Fail(_handError ?? "右手骨骼不完整。");
            return CharacterAssetValidator.ValidateHumanoidPrefabRoot(_root);
        }

        public void OnCharacterActivated()
        {
            if (_root == null) return;
            if (!_root.activeSelf) _root.SetActive(true);
            if (_pose.Apply(_animator)) _stool?.FitToPose(_pose);
            _life?.CaptureBase();
            _activated = true;
        }

        public void OnCharacterDeactivated()
        {
            if (_root == null)
            {
                _activated = false;
                return;
            }
            _life?.RestoreBase();
            _pose.Restore(_animator);
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
                _thumbnail = texture;
                return true;
            }
            error = "VRM 角色的外观由模型自带材质决定，只支持缩略图槽位。";
            return false;
        }

        public bool ClearAppearance(AppearanceSlot slot, out string error)
        {
            error = null;
            if (slot == AppearanceSlot.Thumbnail)
            {
                _thumbnail = null;
                return true;
            }
            error = "VRM 角色只支持缩略图槽位。";
            return false;
        }

        public void DisposeCharacterResources()
        {
            _thumbnail = null;
            _activated = false;
            _scope.Dispose();
            _root = null;
        }

        // ==================== ICharacterRigSource ====================

        public bool IsBuiltInBacked => false;

        public Transform LeftShoulder => _skeleton?.LeftShoulder;
        public Transform RightShoulder => _skeleton?.RightShoulder;
        public Transform LeftArm => _skeleton?.LeftUpperArm;
        public Transform RightArm => _skeleton?.RightUpperArm;
        public Transform LeftForeArm => _skeleton?.LeftLowerArm;
        public Transform RightForeArm => _skeleton?.RightLowerArm;
        public Transform LeftElbow => LeftForeArm;
        public Transform RightElbow => RightForeArm;
        public IHandRig LeftHand => _leftHand;
        public IHandRig RightHand => _rightHand;
        public AvatarRigBuilder.Skeleton Skeleton => _skeleton;
        public AvatarDefinition Definition => _definition;

        // ==================== 构建辅助 ====================

        /// <summary>
        /// MToon 材质的水下调校：实例化材质（登记到作用域，随角色销毁），基色压到 0.82 防止白衣与皮肤
        /// 在水下主光 + 泛光下过曝成纯白；加一圈青色边缘光，让角色轮廓融进海水的环境色；描边改成深海蓝。
        /// </summary>
        private void TuneMaterials()
        {
            if (_root == null) return;
            foreach (var renderer in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var instances = renderer.materials;
                foreach (var m in instances)
                {
                    if (m == null) continue;
                    _scope.Own(m);
                    if (m.HasProperty("_Color"))
                    {
                        Color c = m.GetColor("_Color");
                        m.SetColor("_Color", new Color(c.r * 0.82f, c.g * 0.82f, c.b * 0.84f, c.a));
                    }
                    if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(0.20f, 0.46f, 0.56f));
                    if (m.HasProperty("_RimFresnelPower")) m.SetFloat("_RimFresnelPower", 3.2f);
                    if (m.HasProperty("_RimLift")) m.SetFloat("_RimLift", 0.04f);
                    if (m.HasProperty("_RimLightingMix")) m.SetFloat("_RimLightingMix", 0.35f);
                    if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", new Color(0.05f, 0.09f, 0.13f));
                }
                renderer.materials = instances;
            }
        }

        /// <summary>手指铰链轴：必须在坐姿之前推导（T-Pose 下手背朝向确定）。</summary>
        private void BuildJointAxes()
        {
            if (_leftHand == null || _rightHand == null) return;
            var axes = new ImportedFingerJointAxes();
            bool leftOk = axes.AddHand(_leftHand, WorldBackNormal(_leftHand), out string leftError);
            bool rightOk = axes.AddHand(_rightHand, WorldBackNormal(_rightHand), out string rightError);
            if (!leftOk && !rightOk)
            {
                Debug.LogWarning($"角色「{CharacterId}」手指轴推导失败，退回默认轴：{leftError} / {rightError}");
                return;
            }
            _jointAxes = axes;
            if (!leftOk) Debug.LogWarning($"角色「{CharacterId}」左手轴推导失败：{leftError}");
            if (!rightOk) Debug.LogWarning($"角色「{CharacterId}」右手轴推导失败：{rightError}");
        }

        private static Vector3 WorldBackNormal(HumanoidHandRig hand) =>
            hand.Wrist != null ? hand.Wrist.rotation * hand.BackNormal : hand.BackNormal;

        private static AvatarRigBuilder.Skeleton BuildSkeleton(Transform root, Animator animator,
            HumanoidHandRig left, HumanoidHandRig right)
        {
            var skeleton = new AvatarRigBuilder.Skeleton { Root = root };
            Transform Bone(HumanBodyBones id) => animator != null ? animator.GetBoneTransform(id) : null;

            skeleton.Hips = Bone(HumanBodyBones.Hips);
            skeleton.Spine = Bone(HumanBodyBones.Spine);
            skeleton.Chest = Bone(HumanBodyBones.Chest);
            skeleton.UpperChest = Bone(HumanBodyBones.UpperChest);
            skeleton.Neck = Bone(HumanBodyBones.Neck);
            skeleton.Head = Bone(HumanBodyBones.Head);
            skeleton.LeftShoulder = Bone(HumanBodyBones.LeftShoulder);
            skeleton.LeftUpperArm = Bone(HumanBodyBones.LeftUpperArm);
            skeleton.LeftLowerArm = Bone(HumanBodyBones.LeftLowerArm);
            skeleton.LeftHand = Bone(HumanBodyBones.LeftHand);
            skeleton.RightShoulder = Bone(HumanBodyBones.RightShoulder);
            skeleton.RightUpperArm = Bone(HumanBodyBones.RightUpperArm);
            skeleton.RightLowerArm = Bone(HumanBodyBones.RightLowerArm);
            skeleton.RightHand = Bone(HumanBodyBones.RightHand);
            skeleton.LeftUpperLeg = Bone(HumanBodyBones.LeftUpperLeg);
            skeleton.LeftLowerLeg = Bone(HumanBodyBones.LeftLowerLeg);
            skeleton.LeftFoot = Bone(HumanBodyBones.LeftFoot);
            skeleton.RightUpperLeg = Bone(HumanBodyBones.RightUpperLeg);
            skeleton.RightLowerLeg = Bone(HumanBodyBones.RightLowerLeg);
            skeleton.RightFoot = Bone(HumanBodyBones.RightFoot);

            for (int f = 0; f < HumanoidHandRig.FingersPerHand; f++)
            {
                skeleton.LeftFingerJoints[f] = new Transform[HumanoidHandRig.SegmentsPerFinger];
                skeleton.RightFingerJoints[f] = new Transform[HumanoidHandRig.SegmentsPerFinger];
            }
            FillFingers(left, skeleton.LeftFingers, skeleton.LeftFingerJoints);
            FillFingers(right, skeleton.RightFingers, skeleton.RightFingerJoints);
            skeleton.Avatar = animator != null ? animator.avatar : null;
            return skeleton;
        }

        private static void FillFingers(HumanoidHandRig hand, Transform[] roots, Transform[][] joints)
        {
            if (hand == null || !hand.IsValid) return;
            for (int f = 0; f < HumanoidHandRig.FingersPerHand; f++)
            {
                if (!hand.TryGetJoints(f, out var p, out var m, out var d)) continue;
                roots[f] = p;
                joints[f][0] = p;
                joints[f][1] = m;
                joints[f][2] = d;
            }
        }

        /// <summary>IK 参数（2026-11-18 genki 标定，见 <c>VrmPerformerTests</c>）。</summary>
        private static AvatarDefinition BuildDefinition(CharacterProfile profile)
        {
            var def = AvatarDefinition.CreateDefault();
            if (def == null) return null;
            def.Id = profile.CharacterId;
            def.DisplayName = profile.DisplayName;
            def.HandHeightOffset = -0.022f;
            def.HandDepthOffset = 0f;
            def.Hand.WristPitch = 8f;
            def.Cartoon = false;
            def.Hand.WristOrientationFromRest = true;
            return def;
        }
    }
}
