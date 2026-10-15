using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 主菜单（对应方案 Assets/Scenes/MainMenu.unity）：列出 StreamingAssets/Songs 下的曲目，
    /// 点击后进入 PianoLesson 场景。选中的曲目路径通过静态字段传递给课程场景。
    /// 说明：此脚本为方案目录树中 MainMenu 场景的入口逻辑补充（方案未单独列出菜单控制器）。
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        public static string SelectedSongPath = "";

        private void Start()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            var canvas = UITheme.CreateCanvas(transform, "MainMenuCanvas");

            // 标题区
            UITheme.CreateText(canvas.transform, "钢琴 3D 教学", new Vector2(0f, 268f), new Vector2(700f, 64f),
                UITheme.FontTitle + 22, UITheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.CreateText(canvas.transform, "选择曲目开始练习", new Vector2(0f, 216f), new Vector2(500f, 32f),
                UITheme.FontButton, UITheme.TextSecondary);

            // 曲目列表
            var songs = ListSongs();
            if (songs.Count == 0)
            {
                UITheme.CreateText(canvas.transform, "StreamingAssets/Songs 下未找到 .mid 曲目",
                    new Vector2(0f, 60f), new Vector2(760f, 32f), UITheme.FontButton, UITheme.Error);
                return;
            }

            for (int i = 0; i < songs.Count; i++)
            {
                string path = songs[i];
                string name = Path.GetFileNameWithoutExtension(path);
                var pos = new Vector2(0f, 150f - i * 76f);
                UITheme.CreateButton(canvas.transform, name, pos, new Vector2(480f, 56f),
                    () => LoadLesson(path), UITheme.FontTitle - 4);
            }

            // 底部操作提示
            UITheme.CreateText(canvas.transform, "空格 播放/暂停　·　R 重置视角　·　[ 重播",
                new Vector2(0f, -310f), new Vector2(760f, 28f), UITheme.FontSmall, UITheme.TextSecondary);
        }

        private static List<string> ListSongs()
        {
            var result = new List<string>();
            string dir = Path.Combine(Application.streamingAssetsPath, "Songs");
            if (!Directory.Exists(dir)) return result;
            result.AddRange(Directory.GetFiles(dir, "*.mid"));
            result.Sort();
            return result;
        }

        private static void LoadLesson(string songPath)
        {
            SelectedSongPath = songPath;
            SceneManager.LoadScene("PianoLesson");
        }
    }
}
