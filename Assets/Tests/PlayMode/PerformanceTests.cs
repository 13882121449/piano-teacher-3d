using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Core;

/// <summary>
/// 性能采样测试（方案 8.3）：播放期间记录平均帧率、最差帧与托管内存变化。
/// <para>
/// 在日志中输出 <c>[PERF]</c> 前缀的采样结果，供人工记录为性能基线。
/// 手动单独运行：<c>-runTests -testPlatform PlayMode -testFilter "PlaybackPerformance"</c>。
/// 注意：`-nographics` 下帧率不含渲染负载，仅验证逻辑层不卡顿；带 GPU 运行即为渲染性能基线。
/// </para>
/// </summary>
[Category("Perf")]
public class PerformanceTests
{
    [UnityTest]
    public IEnumerator PlaybackPerformance_5Seconds()
    {
        SceneManager.LoadScene("PianoLesson");
        yield return null;
        yield return new WaitForSeconds(3f);

        var session = Object.FindFirstObjectByType<LessonSessionController>();
        Assert.IsNotNull(session, "LessonSessionController 未装配");

        // 构造高密度场景：双手练习 + 播放 + 循环（覆盖和弦/连续音符）
        session.Play();
        yield return new WaitForSeconds(0.5f);

        const float sampleSeconds = 5f;
        float elapsed = 0f;
        int frames = 0;
        float worstFrame = 0f;
        long allocBefore = System.GC.GetTotalMemory(false);

        while (elapsed < sampleSeconds)
        {
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            frames++;
            if (dt > worstFrame) worstFrame = dt;
            yield return null;
        }

        long allocAfter = System.GC.GetTotalMemory(false);
        float avgFps = frames / elapsed;

        Debug.Log($"[PERF] 播放 {sampleSeconds:F0}s：帧数 {frames}，平均 {avgFps:F1} FPS，" +
                  $"最差帧 {worstFrame * 1000f:F1} ms，托管内存 {allocAfter / 1024 / 1024} MB" +
                  $"（增量 {(allocAfter - allocBefore) / 1024f:F0} KB）");

        Assert.Greater(avgFps, 5f, "逻辑层不应出现卡顿（宽松下限，绝对帧率依赖硬件）");
        Assert.Less(worstFrame, 0.5f, "单帧耗时不应超过 500ms（无长阻塞）");
    }

    [UnityTest]
    public IEnumerator ManyNoteChord_NoFrameSpike()
    {
        SceneManager.LoadScene("PianoLesson");
        yield return null;
        yield return new WaitForSeconds(3f);

        var session = Object.FindFirstObjectByType<LessonSessionController>();
        // 全曲快速 seek + 播放，模拟高密度音符触发
        session.Play();
        for (int i = 0; i < 10; i++)
        {
            session.SeekNormalized(i / 10f);
            yield return null;
        }

        float worst = 0f;
        for (int i = 0; i < 60; i++)
        {
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            yield return null;
        }

        Debug.Log($"[PERF] 连续 seek + 播放：最差帧 {worst * 1000f:F1} ms");
        Assert.Less(worst, 0.5f, "连续跳转不应产生长阻塞帧");
    }
}
