using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 连接播放、暂停、进度、速度和视角按钮（方案 4.2）。
    /// 程序化生成 UGUI 底部控制栏；纯表现层，只调用 <see cref="Core.LessonSessionController"/> 的公开接口。
    /// </summary>
    public class LessonControlsUI : MonoBehaviour
    {
        private Core.LessonSessionController _session;

        private Text _statusText;
        private Text _timeText;
        private Slider _progressSlider;
        private Text _playLabel;
        private Text _speedLabel;
        private Text _cameraLabel;
        private bool _suppressSlider;

        private static Font _cachedFont;

        public void Initialize(Core.LessonSessionController session)
        {
            _session = session;
            _session.OnError += ShowError;
            _session.OnPlaybackStateChanged += OnPlaybackStateChanged;
            _session.OnProgressChanged += OnProgressChanged;
            _session.OnSongLoaded += OnSongLoaded;
            _session.OnCameraModeChanged += OnCameraModeChanged;
            BuildUI();
            RefreshPlayLabel(_session.PlaybackState);
            RefreshSpeedLabel(_session.PlaybackSpeed);
            RefreshCameraLabel(_session.CameraMode);
        }

        private void OnDestroy()
        {
            if (_session == null) return;
            _session.OnError -= ShowError;
            _session.OnPlaybackStateChanged -= OnPlaybackStateChanged;
            _session.OnProgressChanged -= OnProgressChanged;
            _session.OnSongLoaded -= OnSongLoaded;
            _session.OnCameraModeChanged -= OnCameraModeChanged;
        }

        // ==================== 事件回调 ====================

        private void ShowError(string msg)
        {
            if (_statusText != null) _statusText.text = $"⚠ {msg}";
            Debug.LogError($"[PianoTeacher3D] {msg}");
        }

        private void OnSongLoaded()
        {
            if (_statusText != null) _statusText.text = $"曲目：{_session.CurrentSongName}";
        }

        private void OnPlaybackStateChanged(Core.PlaybackState state)
        {
            RefreshPlayLabel(state);
        }

        private void OnProgressChanged(float current, float total)
        {
            if (_timeText != null)
                _timeText.text = $"{FormatTime(current)} / {FormatTime(total)}";
            if (_progressSlider != null && total > 0f)
            {
                _suppressSlider = true;
                _progressSlider.value = Mathf.Clamp01(current / total);
                _suppressSlider = false;
            }
        }

        private void OnCameraModeChanged(CameraControl.CameraMode mode) => RefreshCameraLabel(mode);

        // ==================== UI 构建 ====================

        private void BuildUI()
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("LessonControlsCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasGo.AddComponent<GraphicRaycaster>();

            // 状态文本（顶部）
            _statusText = CreateText(canvasGo.transform, "曲目：加载中…", new Vector2(0f, 330f), new Vector2(1200f, 40f), 22);

            // 底部控制栏
            var bar = CreatePanel(canvasGo.transform, new Vector2(0f, -320f), new Vector2(1240f, 90f));

            // 进度条 + 时间
            _progressSlider = CreateSlider(bar.transform, new Vector2(0f, 24f), new Vector2(1000f, 20f));
            _progressSlider.onValueChanged.AddListener(v =>
            {
                if (!_suppressSlider) _session?.SeekNormalized(v);
            });
            _timeText = CreateText(bar.transform, "00:00 / 00:00", new Vector2(0f, -12f), new Vector2(300f, 24f), 16);

            // 按钮行
            float btnY = -40f;
            float btnW = 150f;
            var playBtn = CreateButton(bar.transform, "播放", new Vector2(-500f, btnY), new Vector2(btnW, 44f), () => _session?.PlayPause());
            _playLabel = playBtn.GetComponentInChildren<Text>();
            CreateButton(bar.transform, "停止", new Vector2(-340f, btnY), new Vector2(btnW, 44f), () => _session?.Stop());
            CreateButton(bar.transform, "重新播放", new Vector2(-180f, btnY), new Vector2(btnW, 44f), () => _session?.Restart());

            var speedBtn = CreateButton(bar.transform, "速度", new Vector2(-20f, btnY), new Vector2(btnW, 44f), () => _session?.CycleSpeed());
            _speedLabel = speedBtn.GetComponentInChildren<Text>();

            var camBtn = CreateButton(bar.transform, "视角", new Vector2(140f, btnY), new Vector2(btnW, 44f), () => _session?.CycleCameraMode());
            _cameraLabel = camBtn.GetComponentInChildren<Text>();

            CreateButton(bar.transform, "重置视角 (R)", new Vector2(300f, btnY), new Vector2(180f, 44f), () => _session?.ResetCamera());
            CreateButton(bar.transform, "切换角色", new Vector2(490f, btnY), new Vector2(btnW, 44f), () => _session?.CycleAvatar());
        }

        private void RefreshPlayLabel(Core.PlaybackState state)
        {
            if (_playLabel == null) return;
            _playLabel.text = state == Core.PlaybackState.Playing ? "暂停" : "播放";
        }

        private void RefreshSpeedLabel(float speed)
        {
            if (_speedLabel != null) _speedLabel.text = $"速度 {speed:0.0}x";
        }

        private void RefreshCameraLabel(CameraControl.CameraMode mode)
        {
            if (_cameraLabel != null) _cameraLabel.text = "视角：" + mode switch
            {
                CameraControl.CameraMode.Closeup => "手部特写",
                CameraControl.CameraMode.Full => "全景",
                _ => "自由观察"
            };
        }

        // ==================== UGUI 辅助 ====================

        private static Font GetFont()
        {
            if (_cachedFont != null) return _cachedFont;
            _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_cachedFont == null) _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _cachedFont;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private static RectTransform CreatePanel(Transform parent, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.08f, 0.08f, 0.1f, 0.9f);
            return rt;
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

        private static Slider CreateSlider(Transform parent, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject("ProgressSlider");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f);

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var faRt = fillArea.AddComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
            faRt.offsetMin = new Vector2(5f, 5f); faRt.offsetMax = new Vector2(-5f, -5f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRt = fill.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = new Vector2(10f, 0f);
            fill.AddComponent<Image>().color = new Color(0.9f, 0.6f, 0.2f);

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var haRt = handleArea.AddComponent<RectTransform>();
            haRt.anchorMin = Vector2.zero; haRt.anchorMax = Vector2.one;
            haRt.offsetMin = new Vector2(5f, 0f); haRt.offsetMax = new Vector2(-5f, 0f);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var hRt = handle.AddComponent<RectTransform>();
            hRt.sizeDelta = new Vector2(14f, 14f);
            handle.AddComponent<Image>().color = Color.white;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = hRt;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            return slider;
        }

        private static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
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
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            return btn;
        }

        private static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m:00}:{s:00}";
        }
    }
}
