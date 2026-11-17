using System;
using System.Collections;
using System.IO;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PianoTeacher3D.Core
{
    /// <summary>主菜单示范曲入口，场景装配完成后开始演奏。</summary>
    internal sealed class CinematicShowcaseLaunch : MonoBehaviour
    {
        internal static void Open(string path)
        {
            MainMenuUI.SelectedSongPath = path;
            MainMenuUI.ResumeSongRequested = false;
            SceneManager.sceneLoaded -= SchedulePlayback;
            SceneManager.sceneLoaded += SchedulePlayback;
            SceneManager.LoadScene("PianoLesson");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OpenCommandLinePreview()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-piano-cinematic-preview") < 0) return;
            if (SceneManager.GetActiveScene().name != "MainMenu") return;
            string path = Path.Combine(Application.streamingAssetsPath, "Songs", "cinematic", "cinematic_tide.mid");
            if (File.Exists(path)) Open(path);
        }

        private static void SchedulePlayback(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= SchedulePlayback;
            if (scene.name != "PianoLesson") return;
            new GameObject("CinematicShowcaseLaunch").AddComponent<CinematicShowcaseLaunch>();
        }

        private IEnumerator Start()
        {
            // 会话组件在自己的 Start 中加载菜单传来的曲目。
            yield return null;
            var session = FindFirstObjectByType<LessonSessionController>();
            if (session != null && session.HasSong)
            {
                session.SetDisplayMode(DisplayMode.Showcase);
                session.SetCameraMode(CameraMode.Full);
                session.SetAutomaticStageCamera(true);
                session.SetSpeed(1f);
                session.Play();
            }
            Destroy(gameObject);
        }
    }
}
