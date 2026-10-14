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
}
