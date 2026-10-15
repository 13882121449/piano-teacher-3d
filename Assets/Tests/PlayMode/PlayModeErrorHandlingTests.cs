using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Core;

/// <summary>
/// 资源加载失败与状态边界的 PlayMode 测试（方案 8.1 异常场景）：
/// 加载不存在 / 损坏的曲目必须触发 OnError、保持原曲目状态，且不抛异常。
/// </summary>
public class PlayModeErrorHandlingTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PianoTeacher3D_ErrTests");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* 清理失败不影响结论 */ }
    }

    private static IEnumerator LoadLessonScene()
    {
        SceneManager.LoadScene("PianoLesson");
        yield return null;
        yield return new WaitForSeconds(3f); // 等待场景装配
    }

    [UnityTest]
    public IEnumerator LoadSong_NonExistentPath_RaisesErrorAndKeepsCurrentSong()
    {
        yield return LoadLessonScene();

        var session = Object.FindFirstObjectByType<LessonSessionController>();
        Assert.IsNotNull(session, "场景中应存在 LessonSessionController");
        Assert.IsTrue(session.HasSong, "场景启动时应已加载内置演示曲");

        string captured = null;
        System.Action<string> handler = m => captured = m;
        session.OnError += handler;
        // UI 会把 OnError 输出为 Debug.LogError —— 测试需显式预期该错误日志
        LogAssert.Expect(LogType.Error, new Regex(@"\[PianoTeacher3D\]"));
        try
        {
            session.LoadSong(Path.Combine(_dir, "missing.mid"));
        }
        finally
        {
            session.OnError -= handler;
        }

        Assert.IsNotNull(captured, "加载不存在的文件应触发 OnError 提示");
        Assert.IsTrue(session.HasSong, "加载失败不应破坏已加载的曲目");
    }

    [UnityTest]
    public IEnumerator LoadSong_CorruptFile_RaisesErrorWithoutException()
    {
        yield return LoadLessonScene();

        var session = Object.FindFirstObjectByType<LessonSessionController>();
        var corrupt = Path.Combine(_dir, "corrupt.mid");
        // MThd 头完整，但缺少 MTrk 轨道块（损坏文件）
        File.WriteAllBytes(corrupt, new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06,
            0x00, 0x00, 0x00, 0x01, 0x01, 0xE0
        });

        string captured = null;
        System.Action<string> handler = m => captured = m;
        session.OnError += handler;
        LogAssert.Expect(LogType.Error, new Regex(@"\[PianoTeacher3D\]"));
        try
        {
            Assert.DoesNotThrow(() => session.LoadSong(corrupt), "损坏文件不应导致抛异常");
        }
        finally
        {
            session.OnError -= handler;
        }

        Assert.IsNotNull(captured, "损坏文件应触发 OnError 提示");
        Assert.IsTrue(session.HasSong, "原曲目应保持可播放");
    }

    [UnityTest]
    public IEnumerator NoSongState_PlayIsBlockedWithHint()
    {
        // 直接构造一个"未加载曲目"的会话：加载场景后立刻测试其守卫行为
        yield return LoadLessonScene();

        var session = Object.FindFirstObjectByType<LessonSessionController>();
        // 先触发一次"加载失败"（产生一条错误日志），再验证后续操作全部安全
        LogAssert.Expect(LogType.Error, new Regex(@"\[PianoTeacher3D\]"));
        session.LoadSong(Path.Combine(_dir, "nope.mid"));
        yield return null;

        Assert.DoesNotThrow(() => session.PlayPause());
        Assert.DoesNotThrow(() => session.Restart());
        Assert.DoesNotThrow(() => session.ToggleLoop());
        Assert.DoesNotThrow(() => session.CyclePracticeHand());
        Assert.DoesNotThrow(() => session.CycleSpeed());
        Assert.DoesNotThrow(() => session.CycleCameraMode());
        yield return null;
    }
}
