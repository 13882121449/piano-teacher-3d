using System.Collections;
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PianoTeacher3D.Tests.PlayMode
{
    public class StageDirectionTests
    {
        [UnityTest]
        public IEnumerator StillCamera_SeaLifeHasRootedAndFreeMovingGeometry()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<LessonSessionController>();
            var habitat = session.transform.Find("SceneEnvironment/UnderwaterHabitat");
            session.SetDisplayMode(DisplayMode.Showcase);
            session.SetCameraMode(CameraMode.Full);
            session.SetAutomaticStageCamera(false);
            session.SetStageDirectionEnabled(false);
            var fish = habitat.Find("FishShoal1");
            var jelly = habitat.Find("AmberJellyfish");
            var fishStart = fish.localPosition;
            var jellyStart = jelly.localPosition;
            yield return new WaitForSeconds(0.9f);

            Assert.Greater(Vector3.Distance(fishStart, fish.localPosition), 0.20f);
            Assert.Greater(Vector3.Distance(jellyStart, jelly.localPosition), 0.12f);

            var kelp = habitat.Find("KelpForest").GetComponent<MeshFilter>().sharedMesh.uv;
            Assert.IsTrue(System.Array.Exists(kelp, motion => motion.y == 2f && motion.x == 0f),
                "海草茎底应固定，其余部分随水流弯曲");
            Assert.IsTrue(System.Array.Exists(kelp, motion => motion.y == 2f && motion.x >= 0.99f));
            var jellyMesh = jelly.GetComponent<MeshFilter>().sharedMesh.uv;
            Assert.IsTrue(System.Array.Exists(jellyMesh, motion => motion.y == 4f && motion.x >= 0.99f));
            var particles = habitat.Find("WaterParticles").GetComponent<MeshFilter>().sharedMesh.uv;
            Assert.IsTrue(System.Array.Exists(particles, motion => motion.y == 5f));
        }

        [UnityTest]
        public IEnumerator SeaLife_WhaleStaysLevelAndShoalsCircleThePlatform()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<LessonSessionController>();
            var environment = session.transform.Find("SceneEnvironment");
            var habitat = environment.Find("UnderwaterHabitat");
            var whale = habitat.Find("HumpbackWhale");
            var platform = environment.Find("MoonlitPlatform");
            var shoal = habitat.Find("FishShoal1");
            session.LoadSong(Path.Combine(Application.streamingAssetsPath, "Songs", "demo.mid"));
            session.SetDisplayMode(DisplayMode.Showcase);
            session.Seek(0.5f);
            float minRadius = float.MaxValue;
            float maxRadius = 0f;
            Vector3 previous = Vector3.zero;
            float sweep = 0f;
            for (int sample = 0; sample < 24; ++sample)
            {
                session.Seek(session.TotalDuration * sample / 24f);
                Assert.Less(Mathf.Abs(Vector3.Dot(whale.right, Vector3.up)), 0.14f, "鲸首需接近水平");
                Vector3 radial = Vector3.ProjectOnPlane(shoal.position - platform.position, Vector3.up);
                minRadius = Mathf.Min(minRadius, radial.magnitude);
                maxRadius = Mathf.Max(maxRadius, radial.magnitude);
                Assert.Less(Mathf.Abs(Vector3.Dot(radial.normalized, shoal.right)), 0.15f, "鱼首沿圆台切线");
                if (sample > 0) sweep += Mathf.Abs(Vector3.SignedAngle(previous, radial, Vector3.up));
                previous = radial;
            }
            Assert.Less(maxRadius - minRadius, 0.95f, "鱼群应围绕舞台中心连续巡游");
            Assert.Greater(sweep, 90f);
        }

        [UnityTest]
        public IEnumerator SeekAndPause_RestoreTheStageAndTheAutomaticCamera()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<LessonSessionController>();
            var environment = session.GetComponent<PianoSceneEnvironment>();
            var habitat = environment.transform.Find("SceneEnvironment/UnderwaterHabitat");
            Assert.IsNotNull(habitat);
            var whale = habitat.Find("HumpbackWhale");
            var sun = habitat.Find("SunlightVolume").GetComponent<Renderer>().sharedMaterial;
            var camera = UnityEngine.Camera.main.transform;
            var originalDisplayMode = session.DisplayMode;
            var originalCameraMode = session.CameraMode;

            try
            {
                session.LoadSong(Path.Combine(Application.streamingAssetsPath, "Songs", "demo.mid"));
                Assert.IsTrue(session.HasSong);
                session.SetDisplayMode(DisplayMode.Showcase);
                session.SetCameraMode(CameraMode.Full);
                session.SetAutomaticStageCamera(true);
                session.Seek(0.5f);
                var openingWhale = whale.localPosition;
                var openingCamera = camera.position;
                var openingSun = sun.GetVector("_StageSequence");
                Assert.AreEqual(StageSection.Opening, session.CurrentStageSection);

                session.Seek(4.8f);
                Assert.AreEqual(StageSection.Whale, session.CurrentStageSection);
                Assert.Greater(Vector3.Distance(openingWhale, whale.localPosition), 1f);
                Assert.Greater(Vector3.Distance(openingCamera, camera.position), 1f);
                Assert.Greater(sun.GetVector("_StageSequence").y, openingSun.y);

                session.Seek(0.5f);
                Assert.AreEqual(openingWhale, whale.localPosition);
                Assert.AreEqual(openingCamera, camera.position);
                Assert.AreEqual(openingSun, sun.GetVector("_StageSequence"));
                session.Play();
                session.Pause();
                Assert.AreEqual(PlaybackState.Paused, session.PlaybackState);
                yield return null;
                yield return null;
                Assert.AreEqual(openingWhale, whale.localPosition);
                Assert.AreEqual(openingCamera, camera.position);
                Assert.AreEqual(openingSun, sun.GetVector("_StageSequence"));

                session.SetStageDirectionEnabled(false);
                Assert.AreEqual(-1f, sun.GetVector("_StageSequence").x);
                Assert.AreEqual(openingCamera, camera.position);
                session.SetStageDirectionEnabled(true);
                session.SetDisplayMode(DisplayMode.Teaching);
                Assert.AreEqual(-1f, sun.GetVector("_StageSequence").x);
                float teachingIntensity = environment.MainLight.intensity;
                session.Seek(4.8f);
                Assert.AreEqual(teachingIntensity, environment.MainLight.intensity);
                Assert.IsEmpty(session.StageSectionLabel);
            }
            finally
            {
                session.SetAutomaticStageCamera(false);
                session.SetDisplayMode(originalDisplayMode);
                session.SetCameraMode(originalCameraMode);
                session.Stop();
            }
        }
    }
}
