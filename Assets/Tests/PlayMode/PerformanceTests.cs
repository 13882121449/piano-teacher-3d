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
    public IEnumerator OpeningChord_UsesPreparedWaveformsOnFirstPlayback()
    {
        var go = new GameObject("开场和弦音频");
        AudioClip[] clips = null;
        try
        {
            var synth = go.AddComponent<PianoTeacher3D.Piano.PianoAudioSynth>();
            synth.Initialize();
            Assert.IsTrue(synth.Initialized, synth.LastInitError);
            var notes = new[]
            {
                new PianoTeacher3D.MIDI.MidiNoteEvent(60, 0f, 1f, 80, 0, 0),
                new PianoTeacher3D.MIDI.MidiNoteEvent(64, 0f, 1f, 80, 0, 0),
                new PianoTeacher3D.MIDI.MidiNoteEvent(67, 0f, 1f, 80, 0, 0)
            };
            synth.PrepareSong(notes);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var prepared = (float[][])synth.GetType().GetField("_preparedWaveforms", flags).GetValue(synth);
            clips = (AudioClip[])synth.GetType().GetField("_clips", flags).GetValue(synth);
            const int MinPitch = PianoTeacher3D.Piano.PianoKeyMap.MinPitch;
            float deadline = Time.realtimeSinceStartup + 5f;
            foreach (var note in notes)
            {
                int index = note.Pitch - MinPitch;
                while (System.Threading.Volatile.Read(ref prepared[index]) == null && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.IsNotNull(prepared[index], $"音高 {note.Pitch} 的波形应在后台完成");
                Assert.IsNull(clips[index], "发声前只准备波形数组");
                synth.OnNoteOn(note);
                Assert.IsNull(prepared[index], "首次发声后应释放临时波形数组");
                Assert.IsNotNull(clips[index]);
                Assert.AreEqual(110250, clips[index].samples);
            }
            Assert.AreEqual(3, synth.ActiveVoiceCount);
        }
        finally
        {
            Object.Destroy(go);
            if (clips != null)
                foreach (var clip in clips)
                    if (clip != null) Object.Destroy(clip);
        }
    }

    [TestCase(21)]
    [TestCase(48)]
    [TestCase(60)]
    [TestCase(84)]
    [TestCase(108)]
    public void PianoSynthesis_PreservesRecordedWaveform(int pitch)
    {
        // 取自 2026-11-12 优化前的真实波形，覆盖起音、衰减中段和尾部。
        float[] expected;
        switch (pitch)
        {
            case 21: expected = new[] { -0.006668034f, 0.00928271748f, 0.156380355f, 0.6664283f, 0.414327681f, -0.259228021f, -0.08311998f, 0.410030961f, 0.02479161f, 0.0405580066f, -0.130864009f }; break;
            case 48: expected = new[] { 0.0052630296f, 0.006452345f, 0.244112223f, 0.178337216f, 0.337355763f, 0.572235465f, 0.509651f, -0.192109346f, -0.318861365f, -0.141415417f, 0.06381238f }; break;
            case 60: expected = new[] { 0.00638947124f, 0.00707941f, 0.186084926f, -0.463384956f, -0.220866144f, 0.616406143f, 0.327872723f, 0.3882953f, -0.243867576f, 0.133263692f, 0.0598523431f }; break;
            case 84: expected = new[] { 0.009125811f, 0.009804709f, -0.1361764f, 0.429552853f, 0.0364985429f, 0.389288783f, 0.505245149f, -0.4939262f, -0.198068634f, 0.156493291f, 0.0388729423f }; break;
            default: expected = new[] { 0.01233202f, 0.0156393461f, 0.143679857f, 0.7396274f, -0.7816452f, 0.510044158f, -0.8469312f, 0.170095384f, 0.262473255f, 0.158306554f, 0.143633723f }; break;
        }
        var method = typeof(PianoTeacher3D.Piano.PianoAudioSynth).GetMethod("CreatePianoClip",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var clip = (AudioClip)method.Invoke(null, new object[] { pitch, 2.5f });
        try
        {
            Assert.AreEqual(110250, clip.samples);
            Assert.AreEqual(44100, clip.frequency);
            Assert.AreEqual(1, clip.channels);
            var samples = new float[clip.samples];
            Assert.IsTrue(clip.GetData(samples, 0));
            var points = new[] { 0, 1, 32, 128, 441, 1024, 4096, 16384, 44100, 88200, 110249 };
            for (int i = 0; i < points.Length; i++)
                Assert.That(samples[points[i]], Is.EqualTo(expected[i]).Within(0.0015f), $"音高 {pitch}、采样点 {points[i]} 偏离原波形");
            float peak = 0f;
            foreach (float value in samples)
            {
                Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value));
                peak = Mathf.Max(peak, Mathf.Abs(value));
            }
            Assert.That(peak, Is.EqualTo(0.85f).Within(0.0001f));
        }
        finally { Object.DestroyImmediate(clip); }
    }

    [TestCase(21)]
    [TestCase(48)]
    [TestCase(60)]
    [TestCase(84)]
    [TestCase(108)]
    [Explicit("本机性能诊断：首次合成应在 20 ms 内完成，需显式运行")]
    public void ColdPianoSynthesis_WithinFrameBudget(int pitch)
    {
        var method = typeof(PianoTeacher3D.Piano.PianoAudioSynth).GetMethod("CreatePianoClip",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var clip = (AudioClip)method.Invoke(null, new object[] { pitch, 2.5f });
        watch.Stop();
        try
        {
            Assert.IsNotNull(clip);
            var samples = new float[clip.samples];
            Assert.IsTrue(clip.GetData(samples, 0));
            var points = new[] { 0, 1, 32, 128, 441, 1024, 4096, 16384, 44100, 88200, 110249 };
            var values = new System.Text.StringBuilder();
            foreach (int point in points)
                values.Append(samples[point].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            Debug.Log($"[AUDIO-COLD] pitch={pitch} ms={watch.Elapsed.TotalMilliseconds:0.00} samples={values}");
            Assert.LessOrEqual(watch.Elapsed.TotalMilliseconds, 20.0, "首次合成不应单独耗尽一帧预算");
        }
        finally
        {
            Object.DestroyImmediate(clip);
        }
    }

    [Test]
    public void PlayerFrameTrace_PreservesChronologicalMilliseconds()
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var samplerType = typeof(PlayerPerfSampler).GetNestedType("Sampler", System.Reflection.BindingFlags.NonPublic);
        var go = new GameObject("逐帧采样回归");
        try
        {
            var sampler = go.AddComponent(samplerType);
            var frames = (System.Collections.Generic.List<float>)samplerType.GetField("_frames", flags).GetValue(sampler);
            frames.AddRange(new[] { 0.01f, 0.08f, 0.02f });
            var result = (PlayerPerfSampler.Result)samplerType.GetMethod("BuildResult", flags).Invoke(sampler, null);
            var traceField = typeof(PlayerPerfSampler.Result).GetField("frameTimesMs");
            Assert.IsNotNull(traceField, "必须保留逐帧顺序，才能定位长帧发生时间");
            var trace = (float[])traceField.GetValue(result);
            Assert.That(trace, Is.EqualTo(new[] { 10f, 80f, 20f }).Within(0.001f));
            Assert.AreEqual(3, result.frameCount);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

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
