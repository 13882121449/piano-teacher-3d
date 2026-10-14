using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
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
            EnsureEventSystem();

            var canvasGo = new GameObject("MainMenuCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasGo.AddComponent<GraphicRaycaster>();

            CreateText(canvasGo.transform, "钢琴 3D 教学", new Vector2(0f, 280f), new Vector2(600f, 80f), 46);
            CreateText(canvasGo.transform, "选择曲目开始", new Vector2(0f, 220f), new Vector2(400f, 36f), 22);

            var songs = ListSongs();
            if (songs.Count == 0)
            {
                CreateText(canvasGo.transform, "StreamingAssets/Songs 下未找到 .mid 曲目", new Vector2(0f, 0f), new Vector2(700f, 36f), 20);
                return;
            }

            for (int i = 0; i < songs.Count; i++)
            {
                string path = songs[i];
                string name = Path.GetFileNameWithoutExtension(path);
                var pos = new Vector2(0f, 140f - i * 70f);
                CreateButton(canvasGo.transform, name, pos, new Vector2(460f, 52f), () => LoadLesson(path));
            }
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

        private static Font GetFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private static Text CreateText(Transform parent, string content, Vector2 anchoredPos, Vector2 size, int fontSize)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var text = go.AddComponent<Text>();
            text.font = GetFont();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return text;
        }

        private static void CreateButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.22f, 0.3f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero; textRt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.font = GetFont();
            text.text = label;
            text.fontSize = 24;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
        }
    }
}
