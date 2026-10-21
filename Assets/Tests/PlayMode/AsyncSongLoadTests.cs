using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 可取消、分阶段的曲目加载回归（优化方案 §7-8 / P1-03）。
    /// <para>
    /// 验收口径（文档原文）：<b>「取消后旧曲仍可播放，加载失败不损坏用户曲库，不产生并行重复任务」</b>。
    /// 这三条都只能在真实场景里验证（涉及时间轴、调度器、状态机与后台线程的边界），因此归为 PlayMode。
    /// </para>
    /// </summary>
    public class AsyncSongLoadTests
    {
        // 8 s 在全量跑（120+ 用例、批处理无图形）时会偶发超时：单跑该 fixture 稳定 4/4 通过，
        // 说明是机器负载下的时序抖动而非产品卡死。放宽到 20 s 只影响「多久判定卡死」，
        // 断言本身仍然是「必须结束」——不掩盖真正的死锁（死锁会一直不结束）。
        private const float WaitTimeoutSeconds = 20f;

        private LessonSessionController _session;
        private string _demoPath;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            _session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(_session, "PianoLesson 场景应包含 LessonSessionController");

            _demoPath = Path.Combine(Application.streamingAssetsPath, "Songs", "demo.mid");
            Assert.IsTrue(File.Exists(_demoPath), "示例曲 demo.mid 应存在：" + _demoPath);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadSongAsync_ReachesReadyAndCommitsSong()
        {
            string before = _session.CurrentSongName;

            _session.LoadSongAsync(_demoPath);
            yield return WaitUntilDone();

            Assert.IsFalse(_session.IsLoading, "加载结束后不应停留在 Loading 状态");
            Assert.IsTrue(_session.HasSong, "后台加载成功后应提交曲目");
            Assert.Greater(_session.TotalDuration, 0f, "提交后时间轴应获得真实时长");
            Assert.AreEqual("demo", _session.CurrentSongName, "曲名应来自文件名（与同步加载一致）");
            Assert.AreEqual(before, _session.CurrentSongName, "重新加载同一首曲目不应改变曲名");
            Assert.AreEqual(SongLoadStage.Idle, _session.LoadStage, "没有进行中的作业时阶段应回到 Idle");
        }

        [UnityTest]
        public IEnumerator CancelLoad_KeepsPreviousSongPlayable()
        {
            Assert.IsTrue(_session.HasSong, "前置条件：场景启动后应已有示例曲");
            string songBefore = _session.CurrentSongName;
            float durationBefore = _session.TotalDuration;

            _session.LoadSongAsync(_demoPath);
            _session.CancelLoading();          // 立即取消（作业可能还没开始、正在跑或刚好结束）
            yield return WaitUntilDone();

            Assert.AreEqual(songBefore, _session.CurrentSongName, "取消后曲名必须保持不变");
            Assert.AreEqual(durationBefore, _session.TotalDuration, 1e-3f, "取消后时长必须保持不变");
            Assert.IsTrue(_session.HasSong, "取消后旧曲仍应可用（不能进入 Error/NoSong）");

            // 旧曲还能正常播放：这是「取消不损坏会话」的最终判据
            _session.Play();
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(PlaybackState.Playing, _session.PlaybackState, "取消后旧曲必须仍可播放");
            _session.Pause();
        }

        [UnityTest]
        public IEnumerator LoadSongAsync_BusyWhileLoading_DoesNotStartSecondJob()
        {
            int loadedEvents = 0;
            System.Action onLoaded = () => loadedEvents++;
            _session.OnSongLoaded += onLoaded;

            ErrorCode? busyCode = null;
            System.Action<AppError> onError = e => { if (e.Code == ErrorCode.Busy) busyCode = e.Code; };
            _session.OnAppError += onError;

            try
            {
                _session.LoadSongAsync(_demoPath);
                _session.LoadSongAsync(_demoPath);   // 第二次点击：必须被防重入拦住
                yield return WaitUntilDone();
            }
            finally
            {
                _session.OnSongLoaded -= onLoaded;
                _session.OnAppError -= onError;
            }

            Assert.AreEqual(ErrorCode.Busy, busyCode, "加载中再次请求应上报 Busy，而不是并行启动第二个作业");
            Assert.AreEqual(1, loadedEvents, "只应提交一次（并行作业会让计数变成 2）");
        }

        [UnityTest]
        public IEnumerator LoadSongAsync_MissingFile_ReportsFailureAndKeepsPreviousSong()
        {
            string songBefore = _session.CurrentSongName;

            // 失败路径会写一条 LogError（会话层统一上报），测试需显式预期
            LogAssert.Expect(LogType.Error, new Regex(@"\[PianoTeacher3D\]"));
            _session.LoadSongAsync(Path.Combine(Path.GetTempPath(), "pt3d_missing_for_async.mid"));
            yield return WaitUntilDone();

            Assert.AreEqual(songBefore, _session.CurrentSongName, "加载失败不得替换当前曲目");
            Assert.IsTrue(_session.HasSong, "加载失败后旧曲仍应可用");
            Assert.IsFalse(_session.IsLoading, "失败后不得停留在 Loading");
        }

        /// <summary>轮询到没有进行中的加载（或超时）；超时直接失败，避免用「等 5 秒」掩盖卡死。</summary>
        private IEnumerator WaitUntilDone()
        {
            float deadline = Time.realtimeSinceStartup + WaitTimeoutSeconds;
            while (_session.IsLoading && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsFalse(_session.IsLoading, "曲目加载超时（" + WaitTimeoutSeconds + "s 内未结束）");
            yield return null;   // 让同一帧内的状态广播与 UI 更新跑完
        }
    }
}
