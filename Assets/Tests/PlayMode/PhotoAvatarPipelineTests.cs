using System;
using NUnit.Framework;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Customization;
using UnityEngine;

namespace PianoTeacher3D.Tests
{
    public class PhotoAvatarPipelineTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void FittedCharacterUsesRealRigAndKeepsFingerScale(bool shortHair)
        {
            var pixels = new Color32[PhotoAvatarFitter.AtlasSize * PhotoAvatarFitter.AtlasSize];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(152, 100, 76, (byte)(i % 256));
            var tex = new Texture2D(PhotoAvatarFitter.AtlasSize, PhotoAvatarFitter.AtlasSize,
                TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            var data = new PhotoAvatarData
            {
                Id = "photo." + Guid.NewGuid().ToString("N"),
                DisplayName = "照片测试",
                FaceScale = new Vector2(1.08f, .96f),
                FittedAtlas = Convert.ToBase64String(tex.EncodeToPNG()),
                ShortHair = shortHair
            };
            var profile = data.CreateProfile();
            var baselineProfile = CharacterRegistry.CreateBuiltIn(shortHair ? 1 : 0);
            RocketboxCharacterView avatar = null;
            RocketboxCharacterView baseline = null;
            try
            {
                avatar = RocketboxCharacterView.TryCreate(profile, null, out var error);
                Assert.IsNotNull(avatar, error);
                baseline = RocketboxCharacterView.TryCreate(baselineProfile, null, out error);
                Assert.IsNotNull(baseline, error);
                Assert.IsTrue(avatar.ValidateRig().Ok, avatar.ValidateRig().Message);
                Assert.AreEqual(baseline.Definition.HandHeightOffset, avatar.Definition.HandHeightOffset);
                Assert.AreEqual(baseline.Definition.Hand.PlayingWristHeightOffset,
                    avatar.Definition.Hand.PlayingWristHeightOffset);
                Assert.IsNotNull(avatar.Animator.GetBoneTransform(HumanBodyBones.RightFoot));
                for (int finger = 0; finger < 5; finger++)
                {
                    Assert.AreEqual(baseline.RocketboxLeftHand.GetFingerLength(finger),
                        avatar.RocketboxLeftHand.GetFingerLength(finger), .0001f);
                    Assert.AreEqual(baseline.RocketboxRightHand.GetFingerLength(finger),
                        avatar.RocketboxRightHand.GetFingerLength(finger), .0001f);
                }
                bool avatarFace = false, avatarMesh = false, baselineFace = false;
                foreach (var part in avatar.RootTransform.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    avatarMesh |= part.sharedMesh != null && part.sharedMesh.name == "照片角色脸型";
                    foreach (var material in part.sharedMaterials)
                    {
                        var albedo = material != null ? material.mainTexture : null;
                        avatarFace |= albedo != null && albedo.name == "照片拟合脸部";
                    }
                }
                foreach (var part in baseline.RootTransform.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    foreach (var material in part.sharedMaterials)
                        baselineFace |= material != null && material.mainTexture != null &&
                            material.mainTexture.name == "照片拟合脸部";
                Assert.IsTrue(avatarFace, "生成外观应替换三维模型脸部材质");
                Assert.IsTrue(avatarMesh, "生成外观应有独占的脸型网格");
                Assert.IsFalse(baselineFace, "生成外观不得修改模板材质");
                avatar.OnCharacterActivated();
                Assert.IsTrue(avatar.IsActivated);
            }
            finally
            {
                avatar?.DisposeCharacterResources();
                baseline?.DisposeCharacterResources();
                UnityEngine.Object.Destroy(tex);
                UnityEngine.Object.Destroy(profile);
                UnityEngine.Object.Destroy(baselineProfile);
            }
        }
    }
}
