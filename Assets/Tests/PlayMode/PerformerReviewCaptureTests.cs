using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 演奏者验收截图（2026-11-18）：播放基准曲时从多个固定机位截 3D 画面（不含界面），
    /// 用来人工检查坐姿、手型与穿模。需要图形设备；输出目录由 <c>PT3D_REVIEW_CAPTURE_DIR</c> 指定。
    /// </summary>
    public class PerformerReviewCaptureTests
    {
        private const int Width = 1280;
        private const int Height = 720;

        private struct Shot
        {
            public string Name;
            public Vector3 Position;
            public Vector3 LookAt;
            public float Fov;
        }

        [UnityTest, Explicit("需要图形设备：多机位截取演奏者")]
        public IEnumerator CapturePerformerViews()
        {
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType);
            string directory = Environment.GetEnvironmentVariable("PT3D_REVIEW_CAPTURE_DIR");
            Assert.IsNotEmpty(directory, "请设置 PT3D_REVIEW_CAPTURE_DIR");
            Directory.CreateDirectory(directory);

            SceneManager.LoadScene("PianoLesson");
            for (int i = 0; i < 30; i++) yield return null;
            var session = UnityEngine.Object.FindFirstObjectByType<LessonSessionController>();
            var timeline = UnityEngine.Object.FindFirstObjectByType<LessonTimeline>();
            var keyMap = UnityEngine.Object.FindFirstObjectByType<PianoKeyMap>();
            Assert.IsNotNull(session);
            session.RestoreCharacter(CharacterRegistry.VroidGenkiId);
            for (int i = 0; i < 20; i++) yield return null;

            string song = Environment.GetEnvironmentVariable("PT3D_REVIEW_SONG");
            if (string.IsNullOrEmpty(song)) song = Path.Combine("bench", "bench_chords.mid");
            session.LoadSong(Path.Combine(Application.streamingAssetsPath, "Songs", song));
            yield return null;
            session.Seek(0f);
            session.Play();
            while (timeline.IsPreparing) yield return null;

            string seconds = Environment.GetEnvironmentVariable("PT3D_REVIEW_SECONDS");
            float wait = float.TryParse(seconds, out var s) ? s : 1.6f;
            yield return new WaitForSeconds(wait);
            session.Pause();
            for (int i = 0; i < 3; i++) yield return null;

            Transform root = session.CurrentCharacterView.RootTransform;
            Animator animator = session.CurrentCharacterView.Animator;
            Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            Vector3 lw = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
            Vector3 rw = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            Vector3 hands = (lw + rw) * 0.5f;
            float key = keyMap.KeySurfaceY;
            Debug.Log($"[REVIEW] root={root.position} hips={hips} hands={hands} key={key:0.000}");

            var shots = new[]
            {
                new Shot { Name = "a_front_wide", Position = hands + new Vector3(0.9f, 0.55f, -1.5f), LookAt = hips + Vector3.up * 0.25f, Fov = 40f },
                new Shot { Name = "b_back_wide", Position = hips + new Vector3(0.25f, 0.55f, 1.55f), LookAt = hands + Vector3.up * 0.15f, Fov = 42f },
                new Shot { Name = "c_side_body", Position = hips + new Vector3(1.6f, 0.35f, -0.15f), LookAt = hips + new Vector3(0f, 0.3f, -0.2f), Fov = 40f },
                new Shot { Name = "d_hands_top", Position = hands + new Vector3(0f, 0.42f, 0.18f), LookAt = new Vector3(hands.x, key, hands.z - 0.05f), Fov = 48f },
                new Shot { Name = "e_hands_side_r", Position = new Vector3(rw.x - 0.32f, key + 0.06f, rw.z - 0.12f), LookAt = new Vector3(rw.x + 0.05f, key + 0.02f, rw.z - 0.12f), Fov = 40f },
                new Shot { Name = "g_back_hero", Position = hips + new Vector3(0.45f, 0.62f, 2.9f), LookAt = new Vector3(hips.x - 0.05f, 1.25f, hips.z - 0.55f), Fov = 46f },
                new Shot { Name = "f_hands_front", Position = new Vector3(hands.x, key + 0.10f, hands.z - 0.55f), LookAt = new Vector3(hands.x, key + 0.03f, hands.z), Fov = 38f },
            };
            foreach (var shot in shots) Grab(shot, Path.Combine(directory, shot.Name + ".png"));
            yield return null;
        }

        private static void Grab(Shot shot, string path)
        {
            var go = new GameObject("ReviewCamera");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) cam.CopyFrom(main);
            cam.transform.position = shot.Position;
            cam.transform.LookAt(shot.LookAt);
            cam.fieldOfView = shot.Fov;
            cam.nearClipPlane = 0.01f;
            cam.cullingMask = ~(1 << 5);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            try
            {
                cam.targetTexture = rt;
                PianoTeacher3D.Piano.StagePlanarReflection.RenderFor(cam);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                Debug.Log($"[REVIEW] {path}");
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                UnityEngine.Object.Destroy(go);
            }
        }
    }
}
