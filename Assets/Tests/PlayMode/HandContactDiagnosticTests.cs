using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 临时诊断（方案 H0）：把「按键手指为什么离键面 a few mm」拆成可判读的分量。
    /// 只打日志、不做断言；H0 定案后会删除或转为门禁用例。
    /// </summary>
    public class HandContactDiagnosticTests
    {
        private LessonSessionController _session;
        private HandIKController _handIK;
        private AvatarManager _avatar;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;
            _session = Object.FindFirstObjectByType<LessonSessionController>();
            _handIK = Object.FindFirstObjectByType<HandIKController>();
            _avatar = Object.FindFirstObjectByType<AvatarManager>();
            Assert.IsNotNull(_session, "PianoLesson 场景应包含 LessonSessionController");
            Assert.IsNotNull(_handIK, "PianoLesson 场景应包含 HandIKController");
            Assert.IsNotNull(_avatar, "PianoLesson 场景应包含 AvatarManager");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        [Explicit("诊断用例：只在手动 -testFilter HandContactDiagnosticTests 时运行")]
        [UnityTest]
        public IEnumerator Diag_ChordFrame_DumpPerFingerResidual()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Songs", "bench", "bench_chords.mid");
            if (!File.Exists(path)) Assert.Ignore("缺少 bench/bench_chords.mid");

            _session.LoadSong(path);
            yield return null;
            Assert.IsTrue(_session.HasSong, "基准曲应能加载");
            _session.Play();

            // 采集若干帧：和弦曲在前 2 s 内一定有成型的和弦
            for (int i = 0; i < 40; i++)
            {
                yield return new WaitForSeconds(0.05f);
                if (i % 8 == 7) DumpFrame(i);
            }

            _session.Pause();
            yield return null;

            // 静置 0.8 s 后再采一帧：把「跟随滞后」与「解不动（限幅/够不着）」分开
            yield return new WaitForSeconds(0.8f);
            DumpFrame(99);
        }

        private void DumpFrame(int frame)
        {
            Debug.Log($"[DIAG-HAND] frame={frame} t={_session.CurrentTime:0.00}");
            DumpHand(true);
            DumpHand(false);
        }

        private void DumpHand(bool left)
        {
            IHandRig hand = left ? _avatar.LeftHand : _avatar.RightHand;
            if (hand == null) return;

            var tips = hand as IFingertipRig;
            var joints = hand as IFingerJointRig;
            Vector3 palmUp = hand.Palm != null ? hand.Palm.rotation * Vector3.up : Vector3.up;
            Vector3 palmRight = hand.Palm != null ? hand.Palm.rotation * Vector3.right : Vector3.right;

            for (int f = 0; f < hand.Fingers.Count; f++)
            {
                Transform rootT = hand.Fingers[f];
                if (rootT == null) continue;
                if (!_handIK.TryGetFingertipTarget(left, f, out Vector3 target)) continue;

                Transform tipT = tips != null ? tips.GetFingertip(f) : null;
                Vector3 tip = tipT != null ? tipT.position : rootT.position;
                Vector3 residual = target - tip;
                float reach = joints != null ? joints.GetFingerLength(f) : 0f;
                Vector3 rootToTarget = target - rootT.position;

                int assignedPitch = _handIK.Assignment.GetPitch(left, f);
                Debug.Log($"[DIAG-HAND] {(left ? "L" : "R")}{f} pitch={assignedPitch} " +
                          $"above={residual.y * 1000f:0.0}mm " +
                          $"horiz={new Vector2(residual.x, residual.z).magnitude * 1000f:0.0}mm " +
                          $"up={Vector3.Dot(residual, palmUp) * 1000f:0.0} " +
                          $"right={Vector3.Dot(residual, palmRight) * 1000f:0.0} " +
                          $"d={residual.magnitude * 1000f:0.0}mm " +
                          $"rootDist={rootToTarget.magnitude * 1000f:0.0}mm reach={reach * 1000f:0.0}mm");
            }
        }
    }
}
