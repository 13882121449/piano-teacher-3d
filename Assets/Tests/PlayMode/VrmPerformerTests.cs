using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 默认演奏者 genki（VRM）的落座、手部绑定与演奏门禁（2026-11-18）。
    /// </summary>
    public class VrmPerformerTests
    {
        private LessonSessionController _session;
        private HandIKController _handIK;
        private LessonTimeline _timeline;
        private PianoKeyMap _keyMap;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;
            _session = Object.FindFirstObjectByType<LessonSessionController>();
            _handIK = Object.FindFirstObjectByType<HandIKController>();
            _timeline = Object.FindFirstObjectByType<LessonTimeline>();
            _keyMap = Object.FindFirstObjectByType<PianoKeyMap>();
            Assert.IsNotNull(_session);
            _session.RestoreCharacter(CharacterRegistry.VroidGenkiId);
            for (int i = 0; i < 10; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Genki_IsActiveAndSeatedOnStool()
        {
            var view = _session.CurrentCharacterView as VrmCharacterView;
            Assert.IsNotNull(view, "默认演奏者应是 VRM 角色 genki");
            Assert.IsTrue(view.ValidateRig().Ok, view.ValidateRig().Message);
            Assert.IsNotNull(view.Stool);

            var animator = view.Animator;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform ls = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            float seatTop = view.Stool.position.y;
            Debug.Log($"[VRM-SEAT] seatTop={seatTop:0.000} hips={hips.position} lFoot={lf.position} rFoot={rf.position} " +
                      $"lShoulder={ls.position} head={head.position} keySurface={_keyMap.KeySurfaceY:0.000} " +
                      $"lWrist={view.LeftHand.Wrist.position} rWrist={view.RightHand.Wrist.position}");
            Assert.That(hips.position.y - seatTop, Is.InRange(0.04f, 0.11f), "髋部应落在凳面上方");
            Assert.That(lf.position.y, Is.LessThan(0.16f), "左脚应接近地面");
            Assert.That(rf.position.y, Is.LessThan(0.16f), "右脚应接近地面");
            Assert.That(ls.position.y - _keyMap.KeySurfaceY, Is.InRange(0.12f, 0.40f), "肩高应高于键面");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Genki_PlaysBenchSongs_WithinContactGates()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Songs", "bench");
            var files = Directory.GetFiles(dir, "*.mid");
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
            Assert.Greater(files.Length, 0);
            foreach (var file in files)
            {
                _session.LoadSong(file);
                yield return null;
                float duration = Mathf.Clamp(_session.TotalDuration, 1f, 30f);
                _session.Seek(0f);
                _session.Play();
                while (_timeline.IsPreparing) yield return null;
                yield return new WaitForSeconds(duration + 0.3f);
                _session.Pause();
                yield return null;
                var report = _session.HandMotion;
                Assert.IsNotNull(report);
                string song = Path.GetFileNameWithoutExtension(file);
                Debug.Log($"[VRM-BENCH] song={song} 键面P50={report.KeyTopDistance.P50 * 1000f:0.#}mm " +
                          $"键面P95={report.KeyTopDistance.P95 * 1000f:0.#}mm 指尖P95={report.FingertipTargetError.P95 * 1000f:0.#}mm " +
                          $"悬空={report.HoveringFrames} 指尖压键={report.FingerPenetrationFrames} 掌穿模={report.PalmPenetrationFrames} " +
                          $"指节穿键={report.SegmentPenetrationFrames}/{report.SegmentClearanceSamples} " +
                          $"指节最深={(float.IsPositiveInfinity(report.MinSegmentClearance) ? 0f : -report.MinSegmentClearance * 1000f):0.0}mm " +
                          $"命中率={report.HitRate * 100f:0.#}% 样本={report.TargetSamples}");
                Debug.Log($"[VRM-BENCH] song={song} diag 未分配={_handIK.UnassignedFingertipSamples} " +
                          $"缺按压点={_handIK.MissingKeyTouchSamples} 超可达={_handIK.UnreachableTargetSamples} " +
                          $"总={_handIK.FingertipTargetSampleCount}");
                Assert.GreaterOrEqual(report.HitRate, 0.90f, $"{song} 命中率");
                Assert.LessOrEqual(report.KeyTopDistance.P50, 0.006f, $"{song} 键面 P50");
            }
        }
    }
}
