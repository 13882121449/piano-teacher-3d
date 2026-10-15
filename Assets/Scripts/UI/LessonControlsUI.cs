using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 连接播放、暂停、进度、速度、循环、练习模式、视角与教学提示按钮（方案 4.2）。
    /// 布局：顶部左侧曲目信息栏 + 底部控制栏（进度条 + 两行共 12 个按钮）+ 可折叠曲目分析面板。
    /// 按钮带激活态高亮（播放中/循环开/分手练习/非原速/非全景/高亮开/节拍器开）。
    /// 纯表现层，只调用 <see cref="Core.LessonSessionController"/> 的公开接口。
    /// </summary>
    public class LessonControlsUI : MonoBehaviour
    {
        private Core.LessonSessionController _session;

        private Text _songText;
        private Text _timeText;
        private Text _beatText;
        private Slider _progressSlider;
        private Button _playBtn;
        private Button _loopBtn;
        private Button _handBtn;
        private Button _speedBtn;
        private Button _cameraBtn;
        private Button _highlightBtn;
        private Button _metronomeBtn;
        private GameObject _analysisPanel;
        private Text _analysisText;
        private Text _suggestionText;
        private Text _motionText;
        private bool _suppressSlider;
        private int _flashBeat = -1;
        private float _beatFlashUntil;
        private float _lastMotionRefresh;

        public void Initialize(Core.LessonSessionController session)
        {
            _session = session;
            _session.OnError += ShowError;
            _session.OnPlaybackStateChanged += OnPlaybackStateChanged;
            _session.OnProgressChanged += OnProgressChanged;
            _session.OnSongLoaded += OnSongLoaded;
            _session.OnCameraModeChanged += OnCameraModeChanged;
            _session.OnPracticeHandChanged += OnPracticeHandChanged;
            _session.OnHighlightModeChanged += OnHighlightModeChanged;
            _session.OnMetronomeToggled += OnMetronomeToggled;
            _session.OnMetronomeBeat += OnMetronomeBeat;
            _session.OnAnalysisReady += RefreshAnalysis;

            BuildUI();

            RefreshPlayLabel(_session.PlaybackState);
            RefreshSpeedLabel(_session.PlaybackSpeed);
            RefreshCameraLabel(_session.CameraMode);
            RefreshLoopState(_session.IsLooping);
            RefreshHandLabel(_session.PracticeHand);
            RefreshHighlightLabel(_session.KeyHighlight);
            RefreshMetronomeLabel(_session.MetronomeEnabled);
            OnSongLoaded();
            RefreshAnalysis(_session.Analysis);
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
            _session.OnHighlightModeChanged -= OnHighlightModeChanged;
            _session.OnMetronomeToggled -= OnMetronomeToggled;
            _session.OnMetronomeBeat -= OnMetronomeBeat;
            _session.OnAnalysisReady -= RefreshAnalysis;
        }

        private void Update()
        {
            if (_session == null) return;
            // 键盘快捷键：空格 播放/暂停，R 重置视角，[ 重播，H 高亮，M 节拍器，Tab 分析面板
            if (Input.GetKeyDown(KeyCode.Space)) _session.PlayPause();
            if (Input.GetKeyDown(KeyCode.R)) _session.ResetCamera();
            if (Input.GetKeyDown(KeyCode.LeftBracket)) _session.Restart();
            if (Input.GetKeyDown(KeyCode.H)) _session.CycleHighlightMode();
            if (Input.GetKeyDown(KeyCode.M)) _session.ToggleMetronome();
            if (Input.GetKeyDown(KeyCode.Tab)) ToggleAnalysis();

            // 拍点指示闪烁淡出（不依赖播放状态，停播后也会自动清除）
            if (_flashBeat >= 0 && Time.unscaledTime > _beatFlashUntil)
            {
                _flashBeat = -1;
                RefreshBeatIndicator();
            }

            // 分析面板可见时，定期刷新动作质量（方案阶段五验收量化）
            if (_analysisPanel != null && _analysisPanel.activeSelf &&
                Time.unscaledTime - _lastMotionRefresh > 0.25f)
            {
                _lastMotionRefresh = Time.unscaledTime;
                if (_motionText != null) _motionText.text = BuildMotionText();
            }
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

        private void OnHighlightModeChanged(Piano.HighlightMode mode) => RefreshHighlightLabel(mode);

        private void OnMetronomeToggled(bool enabled)
        {
            RefreshMetronomeLabel(enabled);
            if (!enabled)
            {
                _flashBeat = -1;
                RefreshBeatIndicator();
            }
        }

        private void OnMetronomeBeat(int beatInBar)
        {
            _flashBeat = beatInBar;
            _beatFlashUntil = Time.unscaledTime + 0.18f;
            RefreshBeatIndicator();
        }

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

            // ---- 底部控制栏（两行按钮）----
            var bar = UITheme.CreatePanel(canvas.transform, "ControlBar",
                new Vector2(0f, -274f), new Vector2(1240f, 180f), UITheme.PanelBg);

            _progressSlider = UITheme.CreateSlider(bar, "Progress", new Vector2(0f, 66f), new Vector2(1150f, 16f));
            _progressSlider.onValueChanged.AddListener(v =>
            {
                if (!_suppressSlider) _session?.SeekNormalized(v);
            });

            _timeText = UITheme.CreateText(bar, "00:00 / 00:00", new Vector2(-420f, 48f), new Vector2(300f, 20f),
                UITheme.FontSmall, UITheme.TextSecondary, TextAnchor.MiddleLeft);

            // 节拍指示（每小节拍点圆点，强拍闪烁金色）
            _beatText = UITheme.CreateText(bar, "", new Vector2(-140f, 48f), new Vector2(340f, 20f),
                UITheme.FontSmall, UITheme.Accent, TextAnchor.MiddleLeft);

            // ---- 第一行：播放 / 循环 / 练习（6 个均分，间距 186）----
            const float w = 168f;
            const float step = 186f;
            const float row1 = 8f;
            const float row2 = -42f;

            _playBtn = UITheme.CreateButton(bar, "播放", new Vector2(-2.5f * step, row1), new Vector2(w, 42f),
                () => _session?.PlayPause());
            UITheme.CreateButton(bar, "停止", new Vector2(-1.5f * step, row1), new Vector2(w, 42f),
                () => _session?.Stop());
            UITheme.CreateButton(bar, "重播", new Vector2(-0.5f * step, row1), new Vector2(w, 42f),
                () => _session?.Restart());

            _loopBtn = UITheme.CreateButton(bar, "循环", new Vector2(0.5f * step, row1), new Vector2(w, 42f), () =>
            {
                _session?.ToggleLoop();
                RefreshLoopState(_session != null && _session.IsLooping);
            });

            _handBtn = UITheme.CreateButton(bar, "双手练习", new Vector2(1.5f * step, row1), new Vector2(w, 42f),
                () => _session?.CyclePracticeHand());

            _speedBtn = UITheme.CreateButton(bar, "速度", new Vector2(2.5f * step, row1), new Vector2(w, 42f), () =>
            {
                _session?.CycleSpeed();
                RefreshSpeedLabel(_session != null ? _session.PlaybackSpeed : 1f);
            });

            // ---- 第二行：视角 / 角色 / 教学提示（6 个均分）----
            _cameraBtn = UITheme.CreateButton(bar, "视角", new Vector2(-2.5f * step, row2), new Vector2(w, 42f),
                () => _session?.CycleCameraMode());

            UITheme.CreateButton(bar, "重置视角", new Vector2(-1.5f * step, row2), new Vector2(w, 42f),
                () => _session?.ResetCamera());

            UITheme.CreateButton(bar, "切换角色", new Vector2(-0.5f * step, row2), new Vector2(w, 42f),
                () => _session?.CycleAvatar());

            _highlightBtn = UITheme.CreateButton(bar, "高亮提示", new Vector2(0.5f * step, row2), new Vector2(w, 42f),
                () => _session?.CycleHighlightMode());

            _metronomeBtn = UITheme.CreateButton(bar, "节拍器", new Vector2(1.5f * step, row2), new Vector2(w, 42f),
                () => _session?.ToggleMetronome());

            UITheme.CreateButton(bar, "曲目分析", new Vector2(2.5f * step, row2), new Vector2(w, 42f),
                ToggleAnalysis);

            BuildAnalysisPanel(canvas.transform);
        }

        /// <summary>构建曲目分析面板（默认隐藏，点击「曲目分析」或 Tab 显示）。</summary>
        private void BuildAnalysisPanel(Transform canvas)
        {
            _analysisPanel = UITheme.CreatePanel(canvas, "AnalysisPanel",
                new Vector2(370f, 20f), new Vector2(480f, 500f), UITheme.PanelBg).gameObject;

            UITheme.CreateText(_analysisPanel.transform, "曲目分析", new Vector2(0f, 214f), new Vector2(440f, 34f),
                UITheme.FontTitle, UITheme.Accent);

            _analysisText = UITheme.CreateText(_analysisPanel.transform, "", new Vector2(0f, 96f),
                new Vector2(440f, 200f), UITheme.FontSmall, UITheme.TextPrimary, TextAnchor.UpperLeft);

            _suggestionText = UITheme.CreateText(_analysisPanel.transform, "", new Vector2(0f, -56f),
                new Vector2(440f, 160f), UITheme.FontSmall, UITheme.TextSecondary, TextAnchor.UpperLeft);

            _motionText = UITheme.CreateText(_analysisPanel.transform, "", new Vector2(0f, -200f),
                new Vector2(440f, 70f), UITheme.FontSmall, UITheme.Accent, TextAnchor.UpperLeft);

            _analysisPanel.SetActive(false);
        }

        private void ToggleAnalysis()
        {
            if (_analysisPanel == null) return;
            bool show = !_analysisPanel.activeSelf;
            _analysisPanel.SetActive(show);
            if (show) RefreshAnalysis(_session != null ? _session.Analysis : null);
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

        private void RefreshHighlightLabel(Piano.HighlightMode mode)
        {
            if (_highlightBtn != null)
            {
                UITheme.SetButtonLabel(_highlightBtn, mode switch
                {
                    Piano.HighlightMode.Both => "高亮：全部",
                    Piano.HighlightMode.Active => "高亮：发声",
                    Piano.HighlightMode.Upcoming => "高亮：预提示",
                    _ => "高亮提示"
                });
            }
            UITheme.SetButtonActive(_highlightBtn, mode != Piano.HighlightMode.Off);
        }

        private void RefreshMetronomeLabel(bool enabled)
        {
            if (_metronomeBtn != null)
                UITheme.SetButtonLabel(_metronomeBtn, enabled ? "节拍器 ✓" : "节拍器");
            UITheme.SetButtonActive(_metronomeBtn, enabled);
            RefreshBeatIndicator();
        }

        /// <summary>刷新拍点指示（每小节拍点圆点；无闪烁时显示空心圆点）。</summary>
        private void RefreshBeatIndicator()
        {
            if (_beatText == null) return;

            bool enabled = _session != null && _session.MetronomeEnabled;
            if (!enabled)
            {
                _beatText.text = "";
                return;
            }
            _beatText.text = BeatDots(_flashBeat);
        }

        private string BeatDots(int active)
        {
            int beats = Mathf.Max(1, _session != null ? _session.BeatsPerBar : 4);
            var sb = new System.Text.StringBuilder(beats * 2);
            for (int i = 0; i < beats; i++) sb.Append(i == active ? "● " : "○ ");
            return sb.ToString().TrimEnd();
        }

        /// <summary>刷新曲目分析面板（方案 2.2「根据 MIDI…提供练习反馈」）。</summary>
        private void RefreshAnalysis(Core.SongAnalysis analysis)
        {
            if (_analysisText == null || _suggestionText == null) return;

            if (analysis == null || analysis.IsEmpty)
            {
                _analysisText.text = "尚未加载曲目，暂无可分析内容。";
                _suggestionText.text = "";
                return;
            }

            _analysisText.text =
                $"音符总数：{analysis.NoteCount}    时长：{FormatTime(analysis.TotalDuration)}\n" +
                $"左手 {analysis.LeftHandCount}（{Percent(analysis.LeftHandRatio)}）    " +
                $"右手 {analysis.RightHandCount}（{Percent(analysis.RightHandRatio)}）\n" +
                $"黑键 {analysis.BlackKeyCount}（{Percent(analysis.BlackKeyRatio)}）\n" +
                $"和弦 {analysis.ChordCount} 处（最大 {analysis.MaxChordSize} 音 / 跨度 {analysis.MaxSpanSemitones} 半音）\n" +
                $"音域 {PitchName(analysis.LowestPitch)} – {PitchName(analysis.HighestPitch)}（{analysis.OctaveRange:0.0} 个八度）\n" +
                $"速度 {analysis.MinBpm:0}–{analysis.MaxBpm:0} BPM    最快音间隔 {(int)(analysis.FastestNoteGap * 1000f + 0.5f)} ms";

            var sb = new System.Text.StringBuilder("练习建议\n");
            for (int i = 0; i < analysis.Suggestions.Count; i++)
                sb.Append("· ").Append(analysis.Suggestions[i]).Append('\n');
            _suggestionText.text = sb.ToString();

            if (_motionText != null) _motionText.text = BuildMotionText();
        }

        /// <summary>动作质量文本（方案阶段五验收：动作连续性 / 目标琴键命中率 / 左右手协调）。</summary>
        private string BuildMotionText()
        {
            if (_session == null) return "";
            var report = _session.HandMotion;
            if (report == null) return "";
            if (report.Samples == 0) return "动作质量：待采样（播放后统计）";

            string pose = _session.PoseIssueCount == 0 ? "穿模 0" : $"穿模告警 {_session.PoseIssueCount}";
            return $"动作质量：命中率 {report.HitRate * 100f:0}% · 平滑度 {report.Smoothness:0.00} · {pose}\n" +
                   $"最大帧位移 {report.MaxStepDistance * 1000f:0.#} mm · 双手协调 {report.CoordinationRatio * 100f:0}% · 采样 {report.Samples}";
        }

        private static int Percent(float ratio) => (int)(ratio * 100f + 0.5f);

        private static readonly string[] NoteNames =
            { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        /// <summary>MIDI 音高 → 音名（C4 = 60）。</summary>
        private static string PitchName(int pitch)
        {
            if (pitch < 0) return "-";
            return NoteNames[pitch % 12] + (pitch / 12 - 1);
        }

        private static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m:00}:{s:00}";
        }
    }
}
