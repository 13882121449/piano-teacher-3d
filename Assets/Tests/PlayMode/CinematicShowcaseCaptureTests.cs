#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;
using PianoTeacher3D.UI;
using UnityEditor.Media;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class CinematicShowcaseCaptureTests
{
    [UnityTest]
    public IEnumerator MenuShowcase_StartsTidalLightAndEnablesAutoCamera()
    {
        SceneManager.LoadScene("MainMenu");
        yield return null;
        var entry = GameObject.Find("CinematicShowcasePrimary");
        Assert.IsNotNull(entry);
        try
        {
            entry.GetComponent<Button>().onClick.Invoke();
            for (int i = 0; i < 6; i++) yield return null;
            var session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(session);
            Assert.IsTrue(session.HasSong);
            Assert.That(session.TotalDuration, Is.InRange(23f, 25f));
            Assert.AreEqual(DisplayMode.Showcase, session.DisplayMode);
            Assert.AreEqual(CameraMode.Full, session.CameraMode);
            Assert.AreEqual(PlaybackState.Playing, session.PlaybackState);
            Assert.IsTrue(session.AutomaticStageCamera);
            GameObject.Find("Btn_更多").GetComponent<Button>().onClick.Invoke();
            var autoCamera = GameObject.Find("StageCameraChip")?.GetComponent<Button>();
            Assert.IsNotNull(autoCamera);
            Assert.IsTrue(autoCamera.interactable);
            session.SetCameraMode(CameraMode.Closeup);
            Assert.AreEqual(CameraMode.Closeup, session.CameraMode);
            Assert.IsFalse(autoCamera.interactable);
            session.SetCameraMode(CameraMode.Full);
            session.SetDisplayMode(DisplayMode.Teaching);
            Assert.IsFalse(autoCamera.interactable);
            session.SetDisplayMode(DisplayMode.Showcase);
            Assert.IsTrue(autoCamera.interactable);
            autoCamera.onClick.Invoke();
            Assert.IsFalse(session.AutomaticStageCamera);
        }
        finally
        {
            MainMenuUI.SelectedSongPath = "";
        }
    }

    [UnityTest, Explicit("需要实际 GPU 和 Windows 视频编解码器")]
    public IEnumerator RenderTidalLightComparison()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("需要图形设备");
        string output = Environment.GetEnvironmentVariable("PT3D_CINEMATIC_CAPTURE_DIR");
        Assert.IsNotEmpty(output);
        Directory.CreateDirectory(output);
        int width = 1920, height = 1080;
        int fps = 24;
        if (Environment.GetEnvironmentVariable("PT3D_CINEMATIC_DRAFT") == "1")
        {
            width = 960;
            height = 540;
            fps = 8;
        }
        string oldSelection = MainMenuUI.SelectedSongPath;
        string midiPath = Path.Combine(Application.streamingAssetsPath, "Songs", "cinematic", "cinematic_tide.mid");
        var song = MidiParser.ParseFile(midiPath);
        Assert.IsTrue(song.Success, song.Error);
        int previousCaptureRate = Time.captureFramerate;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            foreach (bool legacy in new[] { false, true })
            {
                MainMenuUI.SelectedSongPath = midiPath;
                SceneManager.LoadScene("PianoLesson");
                yield return null;
                var session = Object.FindFirstObjectByType<LessonSessionController>();
                var map = Object.FindFirstObjectByType<PianoKeyMap>();
                var camera = Camera.main;
                Assert.IsNotNull(camera);
                Assert.IsTrue(session.HasSong);
                Assert.That(session.TotalDuration, Is.InRange(23f, 25f));
                session.RestoreCharacter(CharacterRegistry.RocketboxFemaleId);
                session.SetQualityTier(QualityTier.High);
                session.SetDisplayMode(DisplayMode.Showcase);
                session.SetCameraMode(CameraMode.Full);
                session.SetAutomaticStageCamera(true);
                session.SetStageDirectionEnabled(!legacy);
                session.SetMetronomeEnabled(false);
                if (session.IsLooping) session.ToggleLoop();
                session.SetSpeed(1f);
                map.SetUnderwaterAppearance(true, legacy: legacy);
                Time.captureFramerate = fps;
                for (int i = 0; i < 10; i++) yield return null;

                var track = new VideoTrackAttributes
                {
                    width = (uint)width, height = (uint)height,
                    frameRate = new MediaRational(fps), includeAlpha = false
                };
                string title = legacy ? "v17_同机位对照" : "v18_潮光实时样片";
                var frames = Mathf.FloorToInt(session.TotalDuration * fps);
                session.Play();
                while (session.CurrentTime <= 0f && session.PlaybackState == PlaybackState.Playing)
                    yield return null;
                var audio = new AudioTrackAttributes
                {
                    sampleRate = new MediaRational(PianoAudioSynth.SampleRate), channelCount = 1, language = "und"
                };
                using (var encoder = new MediaEncoder(Path.Combine(output, title + ".mp4"), track, audio))
                {
                    using (var samples = new NativeArray<float>(RenderSoundtrack(song, (float)frames / fps), Allocator.Persistent))
                        Assert.IsTrue(encoder.AddSamples(samples), "编码器未接收钢琴音轨");
                    for (int frame = 0; frame < frames; frame++)
                    {
                        ReadFrame(camera, target, pixels);
                        Assert.IsTrue(encoder.AddFrame(pixels), "编码器未接收视频帧");
                        if (!legacy && (frame == fps || frame == fps * 8 || frame == fps * 14 || frame == fps * 21))
                            File.WriteAllBytes(Path.Combine(output, $"shot_{frame / fps:D2}.png"), pixels.EncodeToPNG());
                        yield return null;
                    }
                }
                session.Pause();
                if (!legacy)
                {
                    var ctrl = Object.FindFirstObjectByType<CameraModeController>();
                    session.SetAutomaticStageCamera(false);
                    if (ctrl != null) ctrl.enabled = false;
                    session.Seek(6.9f);
                    var shots = new (string name, Vector3 position, Vector3 look)[]
                    {
                        ("piano_rim", new Vector3(1.45f,1.45f,0.77f), new Vector3(0.05f,0.98f,-0.48f)),
                        ("piano_strings", new Vector3(-0.32f,1.68f,-1.68f), new Vector3(0.02f,0.88f,-0.52f)),
                        ("piano_pedals", new Vector3(1.08f,0.42f,0.71f), new Vector3(0.02f,0.28f,-0.05f)),
                    };
                    foreach (var shot in shots)
                    {
                        camera.transform.position = shot.position;
                        camera.transform.LookAt(shot.look);
                        yield return null;
                        ReadFrame(camera, target, pixels);
                        File.WriteAllBytes(Path.Combine(output, shot.name + ".png"), pixels.EncodeToPNG());
                    }
                }
            }
        }
        finally
        {
            Time.captureFramerate = previousCaptureRate;
            MainMenuUI.SelectedSongPath = oldSelection;
            Object.Destroy(target);
            Object.Destroy(pixels);
        }
    }

    [UnityTest, Explicit("需要图形设备与 Windows 视频编码")]
    public IEnumerator RenderFixedCameraSeaLife()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("需要图形设备");
        string output = Environment.GetEnvironmentVariable("PT3D_CINEMATIC_CAPTURE_DIR");
        Assert.IsNotEmpty(output);
        Directory.CreateDirectory(output);
        string oldSelection = MainMenuUI.SelectedSongPath;
        int oldRate = Time.captureFramerate;
        const int fps = 24;
        const int duration = 17;
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Songs", "cinematic", "cinematic_tide.mid");
            MainMenuUI.SelectedSongPath = path;
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<LessonSessionController>();
            var controller = Object.FindFirstObjectByType<CameraModeController>();
            var camera = Camera.main;
            session.RestoreCharacter(CharacterRegistry.RocketboxFemaleId);
            session.SetDisplayMode(DisplayMode.Showcase);
            session.SetQualityTier(QualityTier.High);
            session.SetAutomaticStageCamera(false);
            session.SetStageDirectionEnabled(true);
            session.SetMetronomeEnabled(false);
            session.SetSpeed(1f);
            controller.enabled = false;
            camera.transform.position = new Vector3(2.72f, 2.02f, 3.08f);
            camera.transform.LookAt(new Vector3(-0.89f, 1.06f, -0.67f));
            camera.fieldOfView = 45f;
            Time.captureFramerate = fps;
            session.Seek(0f);
            session.Play();
            while (session.CurrentTime <= 0f && session.PlaybackState == PlaybackState.Playing)
                yield return null;

            var track = new VideoTrackAttributes
            {
                width = (uint)target.width, height = (uint)target.height, frameRate = new MediaRational(fps)
            };
            var audio = new AudioTrackAttributes
            {
                sampleRate = new MediaRational(PianoAudioSynth.SampleRate), channelCount = 1, language = "und"
            };
            using (var encoder = new MediaEncoder(Path.Combine(output, "固定镜头海底舞台.mp4"), track, audio))
            {
                var song = MidiParser.ParseFile(path);
                Assert.IsTrue(song.Success, song.Error);
                using (var samples = new NativeArray<float>(RenderSoundtrack(song, duration), Allocator.Persistent))
                    Assert.IsTrue(encoder.AddSamples(samples));
                for (int frame = 0; frame < fps * duration; frame++)
                {
                    ReadFrame(camera, target, pixels);
                    Assert.IsTrue(encoder.AddFrame(pixels));
                    if (frame % (2 * fps) == 0)
                        File.WriteAllBytes(Path.Combine(output, $"stage_{frame / fps:D2}.png"), pixels.EncodeToPNG());
                    yield return null;
                }
            }
            session.Pause();
            session.Seek(8f);
            camera.transform.position = new Vector3(2.1f, 1.36f, 1.58f);
            camera.transform.LookAt(new Vector3(0f, 0.74f, 0.20f));
            yield return null;
            ReadFrame(camera, target, pixels);
            File.WriteAllBytes(Path.Combine(output, "performer.png"), pixels.EncodeToPNG());
            camera.transform.position = new Vector3(-0.20f, 1.36f, -2.56f);
            camera.transform.LookAt(new Vector3(0.02f, 0.82f, -0.26f));
            yield return null;
            ReadFrame(camera, target, pixels);
            File.WriteAllBytes(Path.Combine(output, "piano.png"), pixels.EncodeToPNG());
        }
        finally
        {
            Time.captureFramerate = oldRate;
            MainMenuUI.SelectedSongPath = oldSelection;
            Object.Destroy(target);
            Object.Destroy(pixels);
        }
    }

    [UnityTest, Explicit("需要图形设备，录制返工前后的固定视角")]
    public IEnumerator RenderRevisionViews()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("需要图形设备");
        string directory = Environment.GetEnvironmentVariable("PT3D_CINEMATIC_CAPTURE_DIR");
        Assert.IsNotEmpty(directory);
        Directory.CreateDirectory(directory);
        SceneManager.LoadScene("PianoLesson");
        yield return null;
        yield return null;
        var session = Object.FindFirstObjectByType<LessonSessionController>();
        var map = Object.FindFirstObjectByType<PianoKeyMap>();
        session.RestoreCharacter(CharacterRegistry.RocketboxFemaleId);
        session.SetQualityTier(QualityTier.High);
        session.SetDisplayMode(DisplayMode.Teaching);
        session.SetAutomaticStageCamera(false);
        map.SetUnderwaterAppearance(true);
        var view = session.CurrentCharacterView;
        var head = ((ICharacterRigSource)view).Skeleton.Head.position;
        var camera = Camera.main;
        Object.FindFirstObjectByType<CameraModeController>().enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.30f, 0.28f, 0.27f);
        camera.orthographic = true;
        const int studio = 29;
        var target = new RenderTexture(1280, 850, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        var studioRoot = new GameObject("中性光检查");
        try
        {
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                light.enabled = false;
            foreach (var euler in new[] { new Vector3(30, 146, 0), new Vector3(23, -27, 0), new Vector3(46, 52, 0) })
            {
                var lamp = new GameObject("中性灯").AddComponent<Light>();
                lamp.transform.SetParent(studioRoot.transform);
                lamp.transform.rotation = Quaternion.Euler(euler);
                lamp.type = LightType.Directional;
                lamp.intensity = euler.y == 146 ? 1.15f : 0.61f;
            }
            camera.cullingMask = 1 << studio;
            var root = view.RootTransform;
            var profile = CharacterRegistry.CreateBuiltIn(0);
            var standing = RocketboxCharacterView.TryCreate(profile, null, out var error);
            Assert.IsNotNull(standing, error);
            studioRoot.transform.position = Vector3.zero;
            try
            {
                foreach (var node in standing.RootTransform.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = studio;
                foreach (var skin in standing.RootTransform.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    skin.updateWhenOffscreen = true;
                yield return null;
                var top = standing.Skeleton.Head.position;
                var midpoint = new Vector3(top.x, top.y * 0.5f + 0.092f, top.z);
                camera.orthographicSize = 0.98f;
                // 先让摄影机从侧面预渲染一次，等原骨架上的前臂蒙皮完成上传。
                camera.transform.position = midpoint + Vector3.left * 3f;
                camera.transform.LookAt(midpoint);
                ReadFrame(camera, target, pixels);
                yield return null;
                foreach (var shot in new[] { ("side", Vector3.left), ("rear", Vector3.back), ("front", Vector3.forward) })
                {
                    camera.transform.position = midpoint + shot.Item2 * 3f;
                    camera.transform.LookAt(midpoint);
                    ReadFrame(camera, target, pixels);
                    File.WriteAllBytes(Path.Combine(directory, "performer_" + shot.Item1 + ".png"), pixels.EncodeToPNG());
                }
            }
            finally
            {
                standing.RootTransform.gameObject.SetActive(false);
                standing.DisposeCharacterResources();
                Object.Destroy(profile);
            }
            foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = studio;
            Vector3 look = head + Vector3.up * 0.025f;
            string[] names = { "front", "side", "rear" };
            Vector3[] directions = { Vector3.back, Vector3.right, Vector3.forward };
            camera.orthographicSize = 0.235f;
            for (int i = 0; i < directions.Length; i++)
            {
                camera.transform.position = look + directions[i] * 2f;
                camera.transform.LookAt(look);
                ReadFrame(camera, target, pixels);
                File.WriteAllBytes(Path.Combine(directory, "face_" + names[i] + ".png"), pixels.EncodeToPNG());
            }
            camera.orthographicSize = 0.85f;
            look = head - Vector3.up * 0.58f;
            camera.transform.position = look + new Vector3(-1.5f, 0.24f, -1.7f);
            camera.transform.LookAt(look);
            ReadFrame(camera, target, pixels);
            File.WriteAllBytes(Path.Combine(directory, "performer_seated.png"), pixels.EncodeToPNG());

            foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 0;
            var piano = map.transform.Find("Case") ?? map.KeyRoots[60].parent;
            var shell = map.GetComponentsInChildren<Transform>(true);
            foreach (var node in shell) node.gameObject.layer = studio;
            var middle = (map.KeyRoots[21].position + map.KeyRoots[108].position) * 0.5f;
            look = new Vector3(middle.x, 0.76f, middle.z - 0.28f);
            camera.orthographicSize = 0.96f;
            for (int i = 0; i < directions.Length; i++)
            {
                camera.transform.position = look + (i == 1 ? Vector3.left : directions[2 - i]) * 3.5f;
                camera.transform.LookAt(look);
                ReadFrame(camera, target, pixels);
                File.WriteAllBytes(Path.Combine(directory, "piano_" + names[i] + ".png"), pixels.EncodeToPNG());
            }
        }
        finally
        {
            Object.Destroy(studioRoot);
            Object.Destroy(target);
            Object.Destroy(pixels);
        }
    }

    private static float[] RenderSoundtrack(MidiParseResult song, float duration)
    {
        const int rate = PianoAudioSynth.SampleRate;
        const float release = 0.20f;
        var mix = new float[Mathf.RoundToInt(duration * rate)];
        var waves = new Dictionary<int, float[]>();
        foreach (var note in song.Notes)
        {
            if (!waves.TryGetValue(note.Pitch, out var wave))
            {
                wave = PianoAudioSynth.GeneratePianoWaveform(note.Pitch, 4f);
                waves.Add(note.Pitch, wave);
            }
            float end = note.EndTime;
            foreach (var pedal in song.SustainSpans)
                if (pedal.Contains(end)) { end = pedal.End; break; }
            int start = Mathf.RoundToInt(note.StartTime * rate);
            int held = Mathf.RoundToInt((end - note.StartTime) * rate);
            int count = Mathf.Min(mix.Length - start, wave.Length, held + (int)(release * rate));
            float gain = PianoVoiceAllocator.VelocityGain(note.Velocity) * 0.68f;
            for (int i = 0; i < count; i++)
                mix[start + i] += wave[i] * gain * Mathf.Clamp01(1f - (i - held) / (release * rate));
        }
        float peak = 0.85f;
        foreach (float value in mix) peak = Mathf.Max(peak, Mathf.Abs(value));
        for (int i = 0; i < mix.Length; i++) mix[i] *= 0.85f / peak;
        return mix;
    }

    private static void ReadFrame(Camera camera, RenderTexture target, Texture2D pixels)
    {
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            pixels.Apply(false);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
        }
    }
}
#endif
