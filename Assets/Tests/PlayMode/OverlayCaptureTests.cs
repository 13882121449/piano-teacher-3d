using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.UI;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 按运行时的叠加方式截取界面（2026-11-18 液态玻璃调参用）。
    /// <para>
    /// <c>VisualSnapshotTests</c> 把 Overlay 画布转成 Screen Space Camera 后与 3D 一起渲染，界面会经过
    /// 后处理（色调映射、泛光），毛玻璃的亮度与边缘高光和运行时不同。这里分两步：
    /// ① 主相机带后处理渲染 3D 画面到纹理；② 临时 UI 相机（不清屏、不做后处理）只画界面叠在上面——
    /// 与运行时「后处理之后再画 Overlay 界面」的顺序一致。项目未启用 ScreenCapture 模块，因此不用它。
    /// </para>
    /// <para>需要图形设备；输出目录由环境变量 <c>PT3D_OVERLAY_CAPTURE_DIR</c> 指定。</para>
    /// </summary>
    public class OverlayCaptureTests
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const int UiLayer = 5;

        [UnityTest, Explicit("需要图形设备，按运行时叠加方式截取界面（含毛玻璃）")]
        public IEnumerator CaptureOverlayInterface()
        {
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType);
            string directory = Environment.GetEnvironmentVariable("PT3D_OVERLAY_CAPTURE_DIR");
            Assert.IsNotEmpty(directory, "请设置 PT3D_OVERLAY_CAPTURE_DIR");
            Directory.CreateDirectory(directory);

            SceneManager.LoadScene("MainMenu");
            for (int i = 0; i < 90; i++) yield return null;
            yield return Grab(Path.Combine(directory, "menu.png"));

            MainMenuUI.SelectedSongPath = Path.Combine(Application.streamingAssetsPath, "Songs", "demo.mid");
            MainMenuUI.ResumeSongRequested = false;
            SceneManager.LoadScene("PianoLesson");
            for (int i = 0; i < 120; i++) yield return null;
            var session = UnityEngine.Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(session);
            // batchmode 下会话跳过偏好恢复，这里显式换成默认演奏者 genki，截图与实际运行一致
            session.RestoreCharacter(PianoTeacher3D.Characters.CharacterRegistry.VroidGenkiId);
            for (int i = 0; i < 30; i++) yield return null;
            session.SetCameraMode(CameraMode.Full);
            for (int i = 0; i < 60; i++) yield return null;
            yield return Grab(Path.Combine(directory, "lesson_full.png"));

            // 背影构图（参考图二）：播放中截，琴弦在振动
            session.SetCameraMode(CameraMode.Back);
            session.Play();
            for (int i = 0; i < 150; i++) yield return null;
            yield return Grab(Path.Combine(directory, "lesson_back.png"));

            session.SetCameraMode(CameraMode.Closeup);
            for (int i = 0; i < 70; i++) yield return null;
            yield return Grab(Path.Combine(directory, "lesson_closeup.png"));

            // 教学叠加：五线谱 + 下落音符 + 音名 + 教学面板
            var teaching = session.Teaching;
            if (teaching != null)
            {
                teaching.SetStaffVisible(true);
                if (teaching.NoteNames == PianoTeacher3D.Teaching.NoteNameStyle.Off) teaching.CycleNoteNames();
                var chip = GameObject.Find("Btn_教学");
                if (chip != null) chip.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                for (int i = 0; i < 60; i++) yield return null;
                yield return Grab(Path.Combine(directory, "lesson_teaching.png"));
                teaching.SetStaffVisible(false);
                while (teaching.NoteNames != PianoTeacher3D.Teaching.NoteNameStyle.Off) teaching.CycleNoteNames();
            }
            session.Pause();
        }

        private static IEnumerator Grab(string path)
        {
            yield return null;
            var main = Camera.main;
            Assert.IsNotNull(main);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var uiGo = new GameObject("OverlayCaptureCamera");
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var saved = new List<(Canvas canvas, RenderMode mode, Camera camera, float distance)>();
            var layers = new List<(GameObject go, int layer)>();
            try
            {
                // ① 3D 画面（带后处理，毛玻璃模糊源也在这一步写入）
                main.targetTexture = rt;
                PianoTeacher3D.Piano.StagePlanarReflection.RenderFor(main);
                main.Render();
                main.targetTexture = null;

                // ② 只画界面：不清屏、不做后处理
                var ui = uiGo.AddComponent<Camera>();
                ui.CopyFrom(main);
                ui.clearFlags = CameraClearFlags.Nothing;
                ui.cullingMask = 1 << UiLayer;
                ui.nearClipPlane = 0.01f;
                ui.targetTexture = rt;
                var data = ui.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderType = CameraRenderType.Base;

                foreach (var c in canvases)
                {
                    if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    saved.Add((c, c.renderMode, c.worldCamera, c.planeDistance));
                    foreach (var t in c.GetComponentsInChildren<Transform>(true))
                    {
                        layers.Add((t.gameObject, t.gameObject.layer));
                        t.gameObject.layer = UiLayer;
                    }
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = ui;
                    c.planeDistance = 0.5f;
                }
                Canvas.ForceUpdateCanvases();
                ui.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                Debug.Log($"[OVERLAY] {path}");
            }
            finally
            {
                RenderTexture.active = null;
                main.targetTexture = null;
                foreach (var s in saved)
                {
                    s.canvas.renderMode = s.mode;
                    s.canvas.worldCamera = s.camera;
                    s.canvas.planeDistance = s.distance;
                }
                foreach (var l in layers)
                    if (l.go != null) l.go.layer = l.layer;
                Canvas.ForceUpdateCanvases();
                UnityEngine.Object.Destroy(uiGo);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }
    }
}
