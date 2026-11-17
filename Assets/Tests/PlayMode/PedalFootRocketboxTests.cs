using System.Collections;
using NUnit.Framework;
using PianoTeacher3D.Characters;
using UnityEngine;
using UnityEngine.TestTools;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 延音踏板 → 真实骨骼的驱动验证：在 Rocketbox 角色上绑定 <see cref="PedalFootAnimator"/>，
    /// 断言踩下时右脚确实旋转、抬起后回到静止姿态。
    /// <para>
    /// 只断言「有没有动、动多少」，不断言好看与否——手型与踏板角度的观感靠
    /// <c>Docs/screenshots</c> 的视觉基线人工确认。
    /// </para>
    /// </summary>
    public class PedalFootRocketboxTests
    {
        private const float Dt = 1f / 60f;

        [UnityTest]
        public IEnumerator Pedal_FollowsTheCurrentAvatarAfterSwitching()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("PianoLesson");
            yield return null;
            var session = Object.FindFirstObjectByType<PianoTeacher3D.Core.LessonSessionController>();
            var driver = Object.FindFirstObjectByType<PedalFootAnimator>();
            Assert.IsNotNull(session);
            Assert.IsNotNull(driver);
            session.RestoreCharacter(CharacterRegistry.RocketboxFemaleId);
            yield return null;
            var first = session.CurrentCharacterView.Animator;
            Assert.IsNotNull(first);
            Assert.AreSame(first, driver.BoundAnimator);
            session.SwitchAvatar(0);
            yield return null;
            var second = session.CurrentCharacterView.Animator;
            Assert.AreNotSame(first, driver.BoundAnimator, "换角色后必须解除原骨架");
            Assert.AreSame(second != null && second.isHuman ? second : null, driver.BoundAnimator);
            session.RestoreCharacter(CharacterRegistry.RocketboxMaleId);
            yield return null;
            Assert.AreSame(session.CurrentCharacterView.Animator, driver.BoundAnimator);
            Assert.IsTrue(driver.IsBound);
        }

        [UnityTest]
        public IEnumerator Pedal_Cc64SeekSetsContactAndPauseKeepsAudioSilent()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("PianoLesson");
            yield return null;
            var session = Object.FindFirstObjectByType<PianoTeacher3D.Core.LessonSessionController>();
            var driver = Object.FindFirstObjectByType<PedalFootAnimator>();
            var audio = Object.FindFirstObjectByType<PianoTeacher3D.Piano.PianoAudioSynth>();
            session.RestoreCharacter(CharacterRegistry.RocketboxFemaleId);
            string song = System.IO.Path.Combine(Application.streamingAssetsPath, "Songs", "bench",
                "articulation", "pedal_cc64.mid");
            session.LoadSong(song);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(session.HasSong);
            session.Play();
            session.Pause();
            session.Seek(0.45f);
            yield return null;
            Assert.Greater(driver.CurrentAngleDegrees, 8f);
            Assert.LessOrEqual(driver.ContactErrorMeters, 0.01f, "鞋掌到踏板接触点应在 10 mm 内");
            Assert.AreEqual(0, audio.ActiveVoiceCount, "暂停拖动只重建画面");
        }

        [UnityTest]
        public IEnumerator PedalAudio_ReleasesOnlyNotesWithNoFingersDown()
        {
            var host = new GameObject("延音声部测试");
            try
            {
                var audio = host.AddComponent<PianoTeacher3D.Piano.PianoAudioSynth>();
                audio.Initialize();
                Assert.IsTrue(audio.Initialized, audio.LastInitError);
                var first = new PianoTeacher3D.MIDI.MidiNoteEvent(60, 0f, 0.3f, 70, 0, 0);
                var second = new PianoTeacher3D.MIDI.MidiNoteEvent(64, 0f, 1f, 80, 0, 0);

                audio.SetSustain(true);
                audio.OnNoteOn(first);
                audio.OnNoteOn(second);
                audio.OnNoteOff(first);
                Assert.AreEqual(2, audio.ActiveVoiceCount);

                audio.SetSustain(false);
                Assert.AreEqual(1, audio.ActiveVoiceCount);
                audio.OnNoteOff(second);
                Assert.AreEqual(0, audio.ActiveVoiceCount);
            }
            finally
            {
                Object.Destroy(host);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pedal_DrivesRightFootAndReturnsToRest()
        {
            var profile = CharacterRegistry.CreateRocketbox();
            var view = RocketboxCharacterView.TryCreate(profile, null, out string error);
            Assert.IsNotNull(view, "角色实例化失败：" + error);

            var host = new GameObject("PedalFootHost");
            try
            {
                var animator = view.Animator;
                Assert.IsNotNull(animator, "Rocketbox 视图必须暴露 Humanoid Animator");
                Assert.IsTrue(animator.isHuman, "Animator 必须是 Humanoid 才能取 RightFoot 骨骼");

                var pedal = host.AddComponent<PedalFootAnimator>();
                pedal.Bind(animator);
                Assert.IsTrue(pedal.IsBound, "绑定失败：Humanoid 骨架里没有 RightFoot");

                var foot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Assert.IsNotNull(foot);
                Quaternion rest = foot.localRotation;

                var toes = animator.GetBoneTransform(HumanBodyBones.RightToes);
                Assert.IsNotNull(toes, "Humanoid 骨架里没有 RightToes，无法判断脚尖朝向");
                float toeHeightBefore = toes.position.y;

                // 踩下：约 0.2 秒内应当到达接近满角度（平滑而不是瞬移）
                for (int i = 0; i < 20; i++) pedal.Tick(true, Dt);
                float pressedAngle = pedal.CurrentAngleDegrees;
                Assert.Greater(pressedAngle, 8f, "踩下后脚尖角度过小，脚看起来没动");
                Assert.Greater(Quaternion.Angle(rest, foot.localRotation), 5f,
                    "骨骼旋转没有变化：角度算出来了但没写回 RightFoot");
                Assert.Greater(toes.position.y, toeHeightBefore + 0.002f,
                    "踩下踏板应该脚尖抬起（脚跟着地）；这里脚尖反而下降，说明角度符号反了");

                // 抬起：回到静止，且姿态复原
                for (int i = 0; i < 20; i++) pedal.Tick(false, Dt);
                Assert.Less(pedal.CurrentAngleDegrees, 0.6f, "抬起后应回到静止姿态");
                Assert.Less(Quaternion.Angle(rest, foot.localRotation), 1f, "静止姿态没有复原");

                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(host);
                view.DisposeCharacterResources();
                Object.DestroyImmediate(profile);
            }
        }

        [UnityTest]
        public IEnumerator Pedal_IsInertWhenAnimatorIsNotHumanoid()
        {
            var host = new GameObject("PedalFootHost_NoHumanoid");
            try
            {
                var pedal = host.AddComponent<PedalFootAnimator>();
                pedal.Bind(null);                     // 程序化角色没有 Humanoid：必须静默
                Assert.IsFalse(pedal.IsBound);
                for (int i = 0; i < 10; i++) pedal.Tick(true, Dt);
                Assert.AreEqual(0f, pedal.CurrentAngleDegrees, 1e-4f);
                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
