using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PianoTeacher3D.Core;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Avatar;

/// <summary>
/// PlayMode 集成测试：验证「视角切换 / 角色切换不中断演奏」这一核心解耦验收点（方案 3.1 / 3.2）。
/// </summary>
public class PlayModeIntegrationTests
{
    private GameObject _root;
    private LessonTimeline _timeline;
    private CameraModeController _camera;
    private AvatarManager _avatar;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("TestRoot");

        _timeline = _root.AddComponent<LessonTimeline>();
        _timeline.SetDuration(100f);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(_root.transform);
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();

        var avatarGo = new GameObject("Avatar");
        avatarGo.transform.SetParent(_root.transform);
        _avatar = avatarGo.AddComponent<AvatarManager>();
        _avatar.BuildAvatars();

        _camera = _root.AddComponent<CameraModeController>();
        _camera.Initialize(cam, _avatar);
    }

    [TearDown]
    public void TearDown()
    {
        if (_root != null) Object.Destroy(_root);
    }

    [UnityTest]
    public IEnumerator SwitchingCameraMode_DoesNotInterruptPlayback()
    {
        _timeline.Play();
        yield return new WaitForSeconds(0.2f);
        float t0 = _timeline.CurrentTime;
        Assert.Greater(t0, 0f, "播放后时间应已推进");

        _camera.CycleMode(); // Full -> Free
        _camera.CycleMode(); // Free -> Closeup

        yield return new WaitForSeconds(0.1f);

        Assert.AreEqual(PlaybackState.Playing, _timeline.State, "切换视角不应改变播放状态");
        Assert.GreaterOrEqual(_timeline.CurrentTime, t0, "切换视角不应导致时间回退");
        Assert.AreEqual(CameraMode.Closeup, _camera.Mode);
    }

    [UnityTest]
    public IEnumerator SwitchingAvatar_DoesNotResetProgress()
    {
        _timeline.Play();
        yield return new WaitForSeconds(0.2f);
        float t0 = _timeline.CurrentTime;
        Assert.Greater(t0, 0f, "播放后时间应已推进");

        _avatar.SwitchNext(); // 角色 0 -> 1

        yield return new WaitForSeconds(0.1f);

        Assert.AreEqual(PlaybackState.Playing, _timeline.State, "切换角色不应改变播放状态");
        Assert.GreaterOrEqual(_timeline.CurrentTime, t0, "切换角色不应导致进度重置");
        Assert.AreEqual(1, _avatar.CurrentIndex);
    }

    [UnityTest]
    public IEnumerator SwitchingAvatar_HandTargetsStayAvailable()
    {
        _avatar.SwitchNext();
        yield return null;

        Assert.IsNotNull(_avatar.LeftHand, "切换后左手接口应可用");
        Assert.IsNotNull(_avatar.RightHand, "切换后右手接口应可用");
        Assert.IsNotNull(_avatar.LeftHand.Wrist, "切换后左手腕应可用");
        Assert.AreEqual(1, _avatar.CurrentIndex);
    }
}
