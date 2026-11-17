using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;
using PianoTeacher3D.Customization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PianoTeacher3D.Tests
{
    public class PhotoAvatarLessonTests
    {
        [UnityTest]
        public IEnumerator ThreeLocalFacesReloadSwitchAndPlayWithPedal()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_verify_out",
                "1115_照片角色_v1", "play_" + Guid.NewGuid().ToString("N")));
            var storage = new PhotoAvatarStorage(folder);
            var encodedAtlases = new HashSet<string>();
            var registered = new List<CharacterProfile>();
            LessonSessionController session = null;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    bool shortHair = i == 1;
                    var source = MakeSyntheticPortrait(i);
                    float cheek = .28f + i * .013f;
                    var anchors = new[]
                    {
                        new Vector2(.5f, .22f), new Vector2(.5f - cheek, .52f),
                        new Vector2(.5f + cheek, .52f), new Vector2(.5f, .80f),
                        new Vector2(.39f, .43f), new Vector2(.61f, .43f),
                        new Vector2(.5f, .53f), new Vector2(.5f, .65f)
                    };
                    bool ok = PhotoAvatarFitter.TryFit(source, anchors, shortHair, out var atlas, out var scale, out var error);
                    Object.Destroy(source);
                    Assert.IsTrue(ok, error);
                    string encoded = Convert.ToBase64String(atlas.EncodeToPNG());
                    Object.Destroy(atlas);
                    Assert.IsTrue(encodedAtlases.Add(encoded), "不同输入应保留不同的角色表面纹理");
                    var data = new PhotoAvatarData
                    {
                        Id = "photo." + Guid.NewGuid().ToString("N"),
                        DisplayName = "合成测试人像 " + i,
                        ShortHair = shortHair,
                        Outfit = i,
                        FaceScale = scale,
                        FittedAtlas = encoded
                    };
                    Assert.IsTrue(storage.Save(data, out error), error);
                }

                // 销毁场景上下文后只从本地生成档案读回，不再保留任何原图纹理。
                SceneManager.LoadScene("PianoLesson");
                yield return null;
                yield return null;
                session = Object.FindFirstObjectByType<LessonSessionController>();
                var driver = Object.FindFirstObjectByType<PedalFootAnimator>();
                Assert.IsNotNull(session);
                Assert.IsNotNull(driver);

                var freshStorage = new PhotoAvatarStorage(folder);
                var entries = freshStorage.LoadAll();
                Assert.AreEqual(3, entries.Length);

                session.LoadSong(Path.Combine(Application.streamingAssetsPath, "Songs", "bench",
                    "articulation", "pedal_cc64.mid"));
                Assert.IsTrue(session.HasSong);
                foreach (var data in entries)
                {
                    Assert.IsTrue(encodedAtlases.Contains(data.FittedAtlas));
                    var profile = data.CreateProfile();
                    registered.Add(profile);
                    Assert.IsTrue(session.CharacterRegistry.Register(profile).Ok);
                    var result = session.ActivateCharacter(profile);
                    Assert.IsTrue(result.Succeeded, result.Error);
                    Assert.AreEqual(data.Id, session.CurrentCharacterView.CharacterId);
                    // 踏板驱动在课程下一次 Update 时读取新的角色视图。
                    yield return null;
                    Assert.AreSame(session.CurrentCharacterView.Animator, driver.BoundAnimator);
                    session.Seek(0f);
                    session.Play();
                    yield return null;
                    session.Pause();
                    session.Seek(.45f);
                    yield return null;
                    Assert.Greater(driver.CurrentAngleDegrees, 8f);
                    Assert.LessOrEqual(driver.ContactErrorMeters, .012f);
                    Assert.IsTrue(session.CurrentCharacterView.IsActivated);
                    Assert.IsTrue(freshStorage.Archive(data, out var error), error);
                }
                Assert.IsEmpty(new PhotoAvatarStorage(folder).LoadAll());
            }
            finally
            {
                if (session != null)
                {
                    session.Stop();
                    session.RestoreCharacter(CharacterRegistry.RocketboxFemaleId);
                }
                foreach (var profile in registered) if (profile != null) Object.Destroy(profile);
            }
        }

        private static Texture2D MakeSyntheticPortrait(int variant)
        {
            const int size = 240;
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var skin = new[] { new Color(.78f, .55f, .44f), new Color(.59f, .42f, .32f),
                new Color(.82f, .66f, .58f) }[variant];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var uv = new Vector2(x / (float)size, 1f - y / (float)size);
                var face = new Vector2((uv.x - .5f) / (.28f + variant * .013f), (uv.y - .51f) / .32f);
                var color = face.sqrMagnitude < 1f ? skin * (1f - .20f * face.sqrMagnitude) : Color.black;
                if (Vector2.Distance(uv, new Vector2(.39f, .43f)) < .024f ||
                    Vector2.Distance(uv, new Vector2(.61f, .43f)) < .024f) color = new Color(.14f, .10f, .08f);
                if (Vector2.Scale(uv - new Vector2(.5f, .65f), new Vector2(1f, 4f)).magnitude < .065f)
                    color = new Color(.54f, .24f, .24f);
                color.a = 1f;
                pixels[y * size + x] = color;
            }
            image.SetPixels32(pixels);
            image.Apply();
            return image;
        }
    }
}
