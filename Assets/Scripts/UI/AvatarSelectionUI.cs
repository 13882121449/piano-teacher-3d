using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 角色选择 UI（方案 3.2 / 4.2）：右上角 3 个预设形象按钮，
    /// 点击切换角色并高亮当前项。切换只改表现层，不影响时间轴。
    /// </summary>
    public class AvatarSelectionUI : MonoBehaviour
    {
        private static readonly string[] AvatarNames = { "写实女性", "写实男性", "卡通角色" };

        private Core.LessonSessionController _session;
        private readonly List<Image> _buttonImages = new();

        public void Initialize(Core.LessonSessionController session)
        {
            _session = session;
            _session.OnAvatarChanged += OnAvatarChanged;
            BuildUI();
            Highlight(0);
        }

        private void OnDestroy()
        {
            if (_session != null) _session.OnAvatarChanged -= OnAvatarChanged;
        }

        private void OnAvatarChanged(int index) => Highlight(index);

        private void BuildUI()
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("AvatarSelectionCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasGo.AddComponent<GraphicRaycaster>();

            float startX = 380f;
            float y = 330f;
            float width = 170f;
            float gap = 190f;

            for (int i = 0; i < AvatarNames.Length; i++)
            {
                int index = i;
                var img = CreateAvatarButton(canvasGo.transform, AvatarNames[i],
                    new Vector2(startX + index * gap, y), new Vector2(width, 46f),
                    () => _session?.SwitchAvatar(index));
                _buttonImages.Add(img);
            }
        }

        private void Highlight(int active)
        {
            for (int i = 0; i < _buttonImages.Count; i++)
            {
                if (_buttonImages[i] == null) continue;
                _buttonImages[i].color = i == active
                    ? new Color(0.9f, 0.6f, 0.2f, 1f)
                    : new Color(0.2f, 0.22f, 0.3f, 1f);
            }
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

        private static Image CreateAvatarButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Avatar_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.22f, 0.3f, 1f);
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
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            return img;
        }
    }
}
