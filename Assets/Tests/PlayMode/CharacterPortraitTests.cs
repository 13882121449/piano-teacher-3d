using System.Collections;
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.Characters;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PianoTeacher3D.Tests
{
    public sealed class CharacterPortraitTests
    {
        private const int Width = 384;
        private const int Height = 512;
        private const int PortraitLayer = 29;

        [UnityTest, Explicit("需要图形设备，按需更新角色卡缩略图")]
        public IEnumerator CaptureCharacterPortraits()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("角色缩略图需要图形设备");

            string directory = System.Environment.GetEnvironmentVariable("PIANO_PORTRAIT_DIR");
            if (string.IsNullOrWhiteSpace(directory))
                directory = Path.Combine(Application.dataPath, "..", "_verify_out", "portraits");
            Directory.CreateDirectory(directory);

            var cameraObject = new GameObject("角色卡取景");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.19f, 0.17f, 0.155f);
            camera.cullingMask = 1 << PortraitLayer;
            camera.orthographic = true;
            camera.aspect = (float)Width / Height;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 3f;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var key = CreateLight("角色卡主光", new Vector3(29f, 148f, 0f), new Color(1f, 0.92f, 0.82f), 1.10f);
            var fill = CreateLight("角色卡补光", new Vector3(20f, -142f, 0f), new Color(0.85f, 0.92f, 1f), 0.48f);
            var previous = RenderTexture.active;
            camera.targetTexture = target;

            try
            {
                for (int i = 0; i < CharacterRegistry.BuiltInIds.Length; i++)
                {
                    var profile = CharacterRegistry.CreateBuiltIn(i);
                    RocketboxCharacterView view = null;
                    try
                    {
                        view = RocketboxCharacterView.TryCreate(profile, null, out string error);
                        Assert.IsNotNull(view, error);
                        view.OnCharacterActivated();
                        foreach (var node in view.RootTransform.GetComponentsInChildren<Transform>(true))
                            node.gameObject.layer = PortraitLayer;

                        yield return null;
                        yield return null;

                        var head = view.Skeleton.Head;
                        var neck = view.Skeleton.Neck;
                        Assert.IsNotNull(head, "角色缺少头骨");
                        Assert.IsNotNull(neck, "角色缺少颈骨");
                        float scale = Mathf.Clamp(Vector3.Distance(head.position, neck.position) / 0.11f, 0.78f, 1.18f);
                        Vector3 focus = head.position + new Vector3(0f, 0.045f, 0.020f) * scale;
                        camera.orthographicSize = 0.27f * scale;
                        camera.transform.position = focus + new Vector3(0.38f, 0.045f, 0.88f);
                        camera.transform.LookAt(focus, Vector3.up);
                        camera.Render();

                        RenderTexture.active = target;
                        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                        texture.Apply();
                        RenderTexture.active = previous;

                        Color bg = texture.GetPixel(0, 0);
                        Color center = texture.GetPixel(Width / 2, Height / 2);
                        Assert.Greater(Mathf.Abs(bg.r - center.r) + Mathf.Abs(bg.g - center.g) + Mathf.Abs(bg.b - center.b),
                            0.08f, "图像中心应能看到人物");
                        string fileName = Path.GetFileName(CharacterRegistry.PortraitResourcesPath(profile.CharacterId));
                        File.WriteAllBytes(Path.Combine(directory, fileName + "_render.png"), texture.EncodeToPNG());
                    }
                    finally
                    {
                        view?.DisposeCharacterResources();
                        Object.Destroy(profile);
                    }
                    yield return null;
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                Object.Destroy(texture);
                Object.Destroy(target);
                Object.Destroy(cameraObject);
                Object.Destroy(key.gameObject);
                Object.Destroy(fill.gameObject);
            }
        }

        private static Light CreateLight(string name, Vector3 euler, Color color, float intensity)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.cullingMask = 1 << PortraitLayer;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(euler);
            return light;
        }
    }
}
