using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 连接播放、暂停、进度、速度、循环、练习模式与视角按钮（方案 4.2）。
    /// 布局：顶部左侧曲目信息栏 + 底部控制栏（进度条 + 9 个分组按钮）。
    /// 按钮带激活态高亮（播放中/循环开/分手练习/非原速/非全景）。
    /// 纯表现层，只调用 <see cref="Core.LessonSessionController"/> 的公开接口。
    /// </summary>
    public class LessonControlsUI : MonoBehaviour
    {
        private Core.LessonSessionController _session;

        private Text _songText;
        private Text _timeText;
        private Slider _progressSlider;
        private Button _playBtn;
        private Button _loopBtn;
        private Button _handBtn;
        private Button _speedBtn;
        private Button _cameraBtn;
        private bool _suppressSlider;

        public void Initialize(Core.LessonSessionController session)
        {
            _session = session;
            _session.OnError += ShowError;
            _session.OnPlaybackStateChanged += OnPlaybackStateChanged;
            _session.OnProgressChanged += OnProgressChanged;
            _session.OnSongLoaded += OnSongLoaded;
            _session.OnCameraModeChanged += OnCameraModeChanged;
            _session.OnPracticeHandChanged += OnPracticeHandChanged;

            BuildUI();

            RefreshPlayLabel(_session.PlaybackState);
            RefreshSpeedLabel(_session.PlaybackSpeed);
            RefreshCameraLabel(_session.CameraMode);
            RefreshLoopState(_session.IsLooping);
            RefreshHandLabel(_session.PracticeHand);
            OnSongLoaded();
        }

        private void OnDestroy()
        {
            if (_session == null) return;
            _session.OnError -= ShowError;
            _session.OnPlaybackStateChanged -= OnPlaybackStateChanged;
            _session.OnProgressChanged -= OnProgressChanged;
            _session.OnSongLoaded -= OnSongLoaded;
            _session.OnCameraModeChanged -= OnCameraModeChanged;
            _session.OnPracticeHandChanged -= OnPracticeHandChanged;
        }

        private void Update()
        {
            if (_session == null) return;
            // 键盘快捷键：空格 播放/暂停，R 重置视角，[ 重播
            if (Input.GetKeyDown(KeyCode.Space)) _session.PlayPause();
            if (Input.GetKeyDown(KeyCode.R)) _session.ResetCamera();
            if (Input.GetKeyDown(KeyCode.LeftBracket)) _session.Restart();
        }

        // ==================== 事件回调 ====================

        private void ShowError(string msg)
        {
            if (_songText != null)
            {
                _songText.text = $"⚠ {msg}";
                _songText.color = UITheme.Error;
            }
            Debug.LogError($"[PianoTeacher3D] {msg}");
        }

        private void OnSongLoaded()
        {
            if (_songText == null) return;
            _songText.color = UITheme.TextPrimary;
            _songText.text = _session.HasSong ? $"♪ {_session.CurrentSongName}" : "尚未加载曲目";
        }

        private void OnPlaybackStateChanged(Core.PlaybackState state) => RefreshPlayLabel(state);

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

        private void OnPracticeHandChanged(Core.PracticeHand hand) => RefreshHandLabel(hand);

        // ==================== UI 构建 ====================

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            var canvas = UITheme.CreateCanvas(transform, "LessonControlsCanvas");

            // ---- 顶部左侧：曲目信息栏 ----
            var infoBar = UITheme.CreatePanel(canvas.transform, "InfoBar",
                new Vector2(-390f, 322f), new Vector2(460f, 46f), UITheme.PanelBg);
            _songText = UITheme.CreateText(infoBar, "♪ 加载中…", Vector2.zero, new Vector2(424f, 40f),
                UITheme.FontButton, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            _songText.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _songText.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _songText.rectTransform.pivot = new Vector2(0f, 0.5f);
            _songText.rectTransform.anchoredPosition = new Vector2(18f, 0f);

            // ---- 底部控制栏 ----
            var bar = UITheme.CreatePanel(canvas.transform, "ControlBar",
                new Vector2(0f, -296f), new Vector2(1240f, 136f), UITheme.PanelBg);

            _progressSlider = UITheme.CreateSlider(bar, "Progress", new Vector2(0f, 44f), new Vector2(1150f, 16f));
            _progressSlider.onValueChanged.AddListener(v =>
            {
                if (!_suppressSlider) _session?.SeekNormalized(v);
            });
            _timeText = UITheme.CreateText(bar, "00:00 / 00:00", new Vector2(0f, 20f), new Vector2(320f, 20f),
                UITheme.FontSmall, UITheme.TextSecondary);

            // ---- 按钮行（9 个均分，间距 138）----
            const float y = -36f;
            const float w = 130f;
            const float step = 138f;

            _playBtn = UITheme.CreateButton(bar, "播放", new Vector2(-4 * step, y), new Vector2(w, 42f),
                () => _session?.PlayPause());
            UITheme.CreateButton(bar, "停止", new Vector2(-3 * step, y), new Vector2(w, 42f),
                () => _session?.Stop());
            UITheme.CreateButton(bar, "重播", new Vector2(-2 * step, y), new Vector2(w, 42f),
                () => _session?.Restart());

            _loopBtn = UITheme.CreateButton(bar, "循环", new Vector2(-1 * step, y), new Vector2(w, 42f), () =>
            {
                _session?.ToggleLoop();
                RefreshLoopState(_session != null && _session.IsLooping);
            });

            _handBtn = UITheme.CreateButton(bar, "双手练习", new Vector2(0f, y), new Vector2(w, 42f),
                () => _session?.CyclePracticeHand());

            _speedBtn = UITheme.CreateButton(bar, "速度", new Vector2(1 * step, y), new Vector2(w, 42f), () =>
            {
                _session?.CycleSpeed();
                RefreshSpeedLabel(_session != null ? _session.PlaybackSpeed : 1f);
            });

            _cameraBtn = UITheme.CreateButton(bar, "视角", new Vector2(2 * step, y), new Vector2(w, 42f),
                () => _session?.CycleCameraMode());

            UITheme.CreateButton(bar, "重置视角", new Vector2(3 * step, y), new Vector2(w, 42f),
                () => _session?.ResetCamera());
            UITheme.CreateButton(bar, "切换角色", new Vector2(4 * step, y), new Vector2(w, 42f),
                () => _session?.CycleAvatar());
        }

        // ==================== 状态刷新（含激活态） ====================

        private void RefreshPlayLabel(Core.PlaybackState state)
        {
            if (_playBtn == null) return;
            bool playing = state == Core.PlaybackState.Playing;
            UITheme.SetButtonLabel(_playBtn, playing ? "暂停" : "播放");
            UITheme.SetButtonActive(_playBtn, playing);
        }

        private void RefreshSpeedLabel(float speed)
        {
            if (_speedBtn == null) return;
            UITheme.SetButtonLabel(_speedBtn, $"速度 {speed:0.0}x");
            UITheme.SetButtonActive(_speedBtn, Mathf.Abs(speed - 1f) > 0.01f);
        }

        private void RefreshCameraLabel(CameraControl.CameraMode mode)
        {
            if (_cameraBtn == null) return;
            UITheme.SetButtonLabel(_cameraBtn, mode switch
            {
                CameraControl.CameraMode.Closeup => "视角：特写",
                CameraControl.CameraMode.Full => "视角：全景",
                _ => "视角：自由"
            });
            UITheme.SetButtonActive(_cameraBtn, mode != CameraControl.CameraMode.Full);
        }

        private void RefreshLoopState(bool looping)
        {
            if (_loopBtn == null) return;
            UITheme.SetButtonLabel(_loopBtn, looping ? "循环 ✓" : "循环");
            UITheme.SetButtonActive(_loopBtn, looping);
        }

        private void RefreshHandLabel(Core.PracticeHand hand)
        {
            if (_handBtn == null) return;
            UITheme.SetButtonLabel(_handBtn, hand switch
            {
                Core.PracticeHand.LeftOnly => "左手练习",
                Core.PracticeHand.RightOnly => "右手练习",
                _ => "双手练习"
            });
            UITheme.SetButtonActive(_handBtn, hand != Core.PracticeHand.Both);
        }

        private static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m:00}:{s:00}";
        }
    }
}
