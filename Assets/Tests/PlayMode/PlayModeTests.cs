using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PianoTeacher3D.Core;

/// <summary>
/// LessonTimeline 状态机 PlayMode 测试（对应方案 4.3 / 8.1 的播放、暂停、停止、Seek）。
/// </summary>
public class LessonTimelineTests
{
    private GameObject _go;
    private LessonTimeline _timeline;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("TestTimeline");
        _timeline = _go.AddComponent<LessonTimeline>();
        _timeline.SetDuration(10f);
    }

    [TearDown]
    public void TearDown()
    {
        Object.Destroy(_go);
    }

    [Test]
    public void Initial_StateIsStopped_TimeZero()
    {
        Assert.AreEqual(PlaybackState.Stopped, _timeline.State);
        Assert.AreEqual(0f, _timeline.CurrentTime);
    }

    [Test]
    public void Play_ChangesStateToPlaying()
    {
        _timeline.Play();
        Assert.AreEqual(PlaybackState.Playing, _timeline.State);
    }

    [Test]
    public void Pause_ChangesStateToPaused()
    {
        _timeline.Play();
        _timeline.Pause();
        Assert.AreEqual(PlaybackState.Paused, _timeline.State);
    }

    [Test]
    public void Stop_ResetsTimeAndState()
    {
        _timeline.Play();
        _timeline.Stop();
        Assert.AreEqual(PlaybackState.Stopped, _timeline.State);
        Assert.AreEqual(0f, _timeline.CurrentTime);
    }

    [Test]
    public void Seek_ClampsToDuration()
    {
        _timeline.Seek(100f);
        Assert.AreEqual(10f, _timeline.CurrentTime, 0.001f);
    }

    [UnityTest]
    public IEnumerator Time_AdvancesWhilePlaying()
    {
        _timeline.Play();
        yield return new WaitForSeconds(0.2f);
        _timeline.Pause();
        Assert.Greater(_timeline.CurrentTime, 0f);
    }

    [UnityTest]
    public IEnumerator PlayingToEnd_PausesAtEndWithoutLoop()
    {
        _timeline.SetDuration(0.3f);
        _timeline.Play();
        yield return new WaitForSeconds(0.5f);
        Assert.AreEqual(PlaybackState.Paused, _timeline.State);
        Assert.AreEqual(0.3f, _timeline.CurrentTime, 0.01f);
    }

    [UnityTest]
    public IEnumerator Loop_JumpBackToLoopStart()
    {
        _timeline.SetDuration(10f);
        _timeline.SetLoop(2f, 3f);
        _timeline.Seek(2.9f);
        _timeline.Play();
        yield return new WaitForSeconds(0.3f); // 越过 3.0 → 跳回 2.0
        Assert.AreEqual(PlaybackState.Playing, _timeline.State);
        Assert.Less(_timeline.CurrentTime, 3.0f, "应跳回循环起点，不应越过 loopEnd");
        Assert.GreaterOrEqual(_timeline.CurrentTime, 2.0f);
    }

    [Test]
    public void SetLoop_InvalidRange_DisablesLooping()
    {
        _timeline.SetDuration(10f);
        _timeline.SetLoop(5f, 3f); // end < start → 无效区间
        Assert.IsFalse(_timeline.IsLooping);
    }

    [Test]
    public void SetLoop_ClampsToDuration()
    {
        _timeline.SetDuration(10f);
        _timeline.SetLoop(-1f, 100f); // clamp 到 [0, 10]
        Assert.AreEqual(0f, _timeline.LoopStart);
        Assert.AreEqual(10f, _timeline.LoopEnd);
        Assert.IsTrue(_timeline.IsLooping);
    }

    [Test]
    public void ClearLoop_ResetsAndDisables()
    {
        _timeline.SetDuration(10f);
        _timeline.SetLoop(2f, 5f);
        _timeline.ClearLoop();
        Assert.IsFalse(_timeline.IsLooping);
        Assert.AreEqual(0f, _timeline.LoopStart);
        Assert.AreEqual(0f, _timeline.LoopEnd);
    }
}
