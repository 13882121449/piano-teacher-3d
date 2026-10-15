using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Core;
using PianoTeacher3D.CameraControl;

/// <summary>
/// 视觉快照测试（方案 8.2）：渲染课程场景的多个视角/角色并保存 PNG，供人工核对视觉回归。
/// <para>
/// 在无图形设备时（例如 `-nographics`）自动跳过；带 GPU 运行时用
/// <c>-runTests -testPlatform PlayMode -testFilter "CaptureLessonViews"</c>（去掉 -nographics）手动执行。
/// </para>
/// 输出目录：<c>Docs/screenshots/</c>
/// </summary>
[Category("Visual")]
public class VisualSnapshotTests
{
    private const int W = 1280;
    private const int H = 720;

    [UnityTest]
    public IEnumerator CaptureLessonViews()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Assert.Ignore("视觉快照需要图形设备（请去掉 -nographics 运行）");
            yield break;
        }

        string dir = Path.Combine(Application.dataPath, "..", "Docs", "screenshots");
        Directory.CreateDirectory(dir);

        SceneManager.LoadScene("PianoLesson");
        yield return null;
        yield return new WaitForSeconds(4f);

        var session = Object.FindFirstObjectByType<LessonSessionController>();
        var cam = Camera.main;
        Assert.IsNotNull(session, "LessonSessionController 未装配");
        Assert.IsNotNull(cam, "主摄像机缺失");

        yield return Capture(cam, Path.Combine(dir, "01_full.png"));

        session.SetCameraMode(CameraMode.Closeup);
        yield return new WaitForSeconds(2.5f);
        yield return Capture(cam, Path.Combine(dir, "02_closeup.png"));

        session.SetCameraMode(CameraMode.Full);
        session.Play();
        yield return new WaitForSeconds(3f);
        yield return Capture(cam, Path.Combine(dir, "03_playing.png"));

        session.CyclePracticeHand(); // 左手练习
        yield return new WaitForSeconds(1.5f);
        yield return Capture(cam, Path.Combine(dir, "04_practice_left.png"));

        session.CycleAvatar(); // 写实男性
        session.CyclePracticeHand(); // 回到右手/双手循环
        yield return new WaitForSeconds(2f);
        yield return Capture(cam, Path.Combine(dir, "05_avatar_man.png"));

        session.CycleAvatar(); // 卡通
        yield return new WaitForSeconds(2f);
        yield return Capture(cam, Path.Combine(dir, "06_avatar_cartoon.png"));
    }

    /// <summary>同步渲染当前相机视图（含 Overlay UI）到 PNG。</summary>
    private static IEnumerator Capture(Camera cam, string path)
    {
        // Overlay Canvas 临时转为 Screen Space Camera，使其纳入相机渲染
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        var saved = new List<(Canvas c, RenderMode mode, Camera wcam, float pd)>();
        foreach (var c in canvases)
        {
            if (c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                saved.Add((c, c.renderMode, c.worldCamera, c.planeDistance));
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 0.5f;
            }
        }
        Canvas.ForceUpdateCanvases();
        yield return null;
        Canvas.ForceUpdateCanvases();

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;

        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());

        cam.targetTexture = prev;
        RenderTexture.active = null;
        Object.Destroy(rt);
        Object.Destroy(tex);

        foreach (var s in saved)
        {
            s.c.renderMode = s.mode;
            s.c.worldCamera = s.wcam;
            s.c.planeDistance = s.pd;
        }
        Canvas.ForceUpdateCanvases();
    }
}
