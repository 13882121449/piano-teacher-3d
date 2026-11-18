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
    /// 指节穿键诊断（2026-11-18 P4）：播放基准曲，每 0.5 s 打印两手最深的穿模点
    /// （哪根手指、哪个采样点、哪个键、键局部坐标），用来区分真实穿模与口径误差。
    /// </summary>
    public class SegmentClearanceDiagnosticsTests
    {
        [UnityTest, Explicit("诊断：逐 0.5 s 打印指节最深穿模点")]
        public IEnumerator Diagnose_SegmentClearance_OnChords()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<LessonSessionController>();
            var ik = Object.FindFirstObjectByType<HandIKController>();
            var timeline = Object.FindFirstObjectByType<LessonTimeline>();
            var fingers = Object.FindFirstObjectByType<FingerPoseController>();
            Assert.IsNotNull(session);

            foreach (string id in new[] { Characters.CharacterRegistry.RocketboxFemaleId, Characters.CharacterRegistry.RocketboxMaleId })
            {
                session.RestoreCharacter(id);
                for (int i = 0; i < 10; i++) yield return null;
                session.LoadSong(Path.Combine(Application.streamingAssetsPath, "Songs", "bench", "bench_chords.mid"));
                yield return null;
                session.Seek(0f);
                session.Play();
                while (timeline.IsPreparing) yield return null;
                for (int k = 0; k < 8; k++)
                {
                    yield return new WaitForSeconds(0.5f);
                    Debug.Log($"[SEG] {id} t={session.CurrentTime:0.00} 左 {ik.GetSegmentClearance(true) * 1000f:0.0} mm：" +
                              ik.DescribeSegmentClearance(true));
                    Debug.Log($"[SEG] {id} t={session.CurrentTime:0.00} 右 {ik.GetSegmentClearance(false) * 1000f:0.0} mm：" +
                              ik.DescribeSegmentClearance(false));
                    Debug.Log($"[SEG] 抬指角 左 {fingers.GetRestingLift(true, 0):0.0}/{fingers.GetRestingLift(true, 1):0.0}/" +
                              $"{fingers.GetRestingLift(true, 2):0.0}/{fingers.GetRestingLift(true, 3):0.0}/{fingers.GetRestingLift(true, 4):0.0} " +
                              $"右 {fingers.GetRestingLift(false, 0):0.0}/{fingers.GetRestingLift(false, 1):0.0}/" +
                              $"{fingers.GetRestingLift(false, 2):0.0}/{fingers.GetRestingLift(false, 3):0.0}/{fingers.GetRestingLift(false, 4):0.0} " +
                              $"抬腕 左 {ik.GetClearanceLift(true) * 1000f:0.0} 右 {ik.GetClearanceLift(false) * 1000f:0.0} mm");
                }
                session.Stop();
                yield return null;
            }
        }
    }
}
