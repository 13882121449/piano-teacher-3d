using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 教学主界面控制层（对应方案 3.1 分区 / 3.2 控件层级 / 3.4 交互规则，
    /// 审查 2.1 三层控制结构、2.2 状态可见性与视觉层级、2.3 反馈分层）。
    /// <para>
    /// 结构：
    /// ① 顶栏（<see cref="UIStatusBar"/>：返回曲库 / 曲库 / 曲名·状态 / 保存状态·帮助 / 当前人物）
    /// ② 3D 演奏区（只放轻量的视角标签与快捷键提示，不常驻大面板）
    /// ③ 主播放栏（始终可见、固定位置：播放/暂停 + 进度 + 时间 + 重播 + 视角）
    /// ④ 练习工具栏（可折叠：练习手 / 速度 / 高亮 / 循环 A-B / 节拍器 / 曲目分析 / 角色外观）
    /// ⑤ 分析面板（按需展开、从右侧滑出，动作质量默认折叠在「开发诊断」区）
    /// </para>
    /// <para>
    /// 纯表现层：只调用 <see cref="LessonSessionController"/> 的公开接口与事件，
    /// 不直接读写任何 Core / MIDI / Piano / Avatar / Camera 模块的内部状态。
    /// </para>
    /// </summary>
    public class LessonControlsUI : MonoBehaviour
    {
        private LessonSessionController _session;
        private UIStatusBar _status;
        private AvatarSelectionUI _avatarUI;
        private Canvas _canvas;

        // ---- 3D 区浮层（轻量：视角标签 + 快捷键提示）----
        private RectTransform _viewChip;
        private Text _viewChipText;
        private Text _viewHintText;

        // ---- 主播放栏 ----
        private RectTransform _mainBar;
        private Text _mainSongText;
        private Text _timeText;
        private Text _totalText;
        private Slider _progress;
        private RectTransform _loopAMark;
        private RectTransform _loopBMark;
        private Button _playBtn;
        private Button _restartBtn;
        private UISegmentedControl _cameraSeg;
        private RectTransform _chipGroup;
        private Text _handChip;
        private Text _speedChip;
        private Text _loopChip;
        private Text _progressHint;

        // ---- 练习工具栏 ----
        private RectTransform _toolbar;
        private RectTransform _toolbarBody;
        private Button _foldBtn;
        private Text _toolbarTitle;
        private Text _toolbarHint;
        private UISegmentedControl _handSeg;
        private UISegmentedControl _speedSeg;
        private UISegmentedControl _highlightSeg;
        private UISegmentedControl _displaySeg;
        private Text _handHint;
        private Button _loopToggle;
        private RectTransform _loopRangeGroup;
        private Text _loopRangeText;
        private Text _loopOffHint;
        private Button _setABtn;
        private Button _setBBtn;
        private Button _clearLoopBtn;
        private Button _metronomeBtn;
        private Text _beatDots;
        private Button _analysisBtn;
        private Button _avatarBtn;

        // ---- 分析面板 ----
        private RectTransform _analysisPanel;
        private Text _analysisText;
        private Text _suggestionText;
        private Button _analysisCloseBtn;
        private Button _diagnosticsToggle;
        private RectTransform _diagnosticsBody;
        private Text _motionText;

        // ---- 空状态 ----
        private RectTransform _emptyState;
        private Text _emptyBody;

        // ---- 运行状态（只缓存展示所需的派生值，不持有业务真值）----
        private bool _suppressSlider;
        private int _flashBeat = -1;
        private float _beatFlashUntil;
        private float _lastMotionRefresh;
        private float _loopA;
        private float _loopB;
        private float _currentTime;
        private float _totalTime;
        private bool _folded;
        private bool _foldTouched;
        private bool _compact;
        private bool _showBarLeft = true;
        private bool _lastNarrow;
        private bool _lastCompact;
        private float _lastRefWidth = -1f;
        private bool _analysisOpen;
        private float _analysisSlide;
        private bool _diagnosticsOpen;
        private float _loadingBlinkUntil;
        private int _loadingPhase;

        private static readonly float[] SpeedOptions = { 0.5f, 1f, 1.5f };
        private static readonly string[] SpeedLabels = { "0.5×", "1.0×", "1.5×" };
        private static readonly string[] HandLabels = { "双手", "左手", "右手" };
        private static readonly string[] HighlightLabels = { "关闭", "预提示", "发声", "全部" };
        private static readonly string[] CameraLabels = { "特写", "全景", "自由" };

        // ==================== 初始化 / 事件连线 ====================

        /// <summary>由 <see cref="LessonSessionController"/> 在场景装配时调用。</summary>
        public void Initialize(LessonSessionController session)
        {
            _session = session;
            if (_session == null)
            {
                Debug.LogError("[PianoTeacher3D][UI] LessonControlsUI 缺少会话控制器，界面不会构建。");
                return;
            }

            _session.OnError += OnErrorFallback;
            _session.OnAppError += OnAppError;
            _session.OnLessonStateChanged += OnLessonStateChanged;
            _session.OnLoadStageChanged += OnLoadStageChanged;
            _session.OnSongLoaded += OnSongLoaded;
            _session.OnPlaybackStateChanged += OnPlaybackStateChanged;
            _session.OnProgressChanged += OnProgressChanged;
            _session.OnCameraModeChanged += OnCameraModeChanged;
            _session.OnPracticeHandChanged += OnPracticeHandChanged;
            _session.OnHighlightModeChanged += OnHighlightModeChanged;
            _session.OnDisplayModeChanged += OnDisplayModeChanged;
            _session.OnMetronomeToggled += OnMetronomeToggled;
            _session.OnMetronomeBeat += OnMetronomeBeat;
            _session.OnAnalysisReady += OnAnalysisReady;
            _session.OnAvatarChanged += OnAvatarChanged;

            BuildUI();
            RefreshAll();

            // 主菜单的「导入 MIDI…」入口：进入课程场景后由控制层调用应用内文件选择器
            if (MainMenuUI.ConsumePendingImport())
            {
                if (_status != null) _status.ShowToast("请选择要导入的 MIDI 文件（支持 .mid / .midi）");
                _session.ImportMidi();
            }
        }

        private void OnDestroy()
        {
            if (_session == null) return;
            _session.OnError -= OnErrorFallback;
            _session.OnAppError -= OnAppError;
            _session.OnLessonStateChanged -= OnLessonStateChanged;
            _session.OnLoadStageChanged -= OnLoadStageChanged;
            _session.OnSongLoaded -= OnSongLoaded;
            _session.OnPlaybackStateChanged -= OnPlaybackStateChanged;
            _session.OnProgressChanged -= OnProgressChanged;
            _session.OnCameraModeChanged -= OnCameraModeChanged;
            _session.OnPracticeHandChanged -= OnPracticeHandChanged;
            _session.OnHighlightModeChanged -= OnHighlightModeChanged;
            _session.OnDisplayModeChanged -= OnDisplayModeChanged;
            _session.OnMetronomeToggled -= OnMetronomeToggled;
            _session.OnMetronomeBeat -= OnMetronomeBeat;
            _session.OnAnalysisReady -= OnAnalysisReady;
            _session.OnAvatarChanged -= OnAvatarChanged;
        }

        private void Update()
        {
            if (_session == null) return;

            ApplyResponsiveLayoutIfNeeded();
            HandleShortcuts();

            // 拍点闪烁淡出（与播放状态无关，停播后也会自动清除）
            if (_flashBeat >= 0 && Time.unscaledTime > _beatFlashUntil)
            {
                _flashBeat = -1;
                RefreshBeatDots();
            }

            // 加载中的按钮防重复点击 + 进度点动画
            if (_session.IsLoading && Time.unscaledTime > _loadingBlinkUntil)
            {
                _loadingBlinkUntil = Time.unscaledTime + 0.4f;
                _loadingPhase = (_loadingPhase + 1) % 4;
                UITheme.SetButtonLabel(_playBtn, "加载中" + new string('.', _loadingPhase));
            }

            // 分析面板打开时按 4Hz 刷新动作质量（方案阶段五验收量化，不与学习操作混排）
            if (_analysisOpen && Time.unscaledTime - _lastMotionRefresh > 0.25f)
            {
                _lastMotionRefresh = Time.unscaledTime;
                RefreshMotion();
            }

            AnimateAnalysisPanel();
        }

        /// <summary>
        /// 全局快捷键（方案 3.4）：仅在非文本输入焦点下生效，浮层 / 确认弹窗打开时让位；
        /// 全部快捷键都有可见 UI 入口，快捷键只是补充。
        /// </summary>
        private void HandleShortcuts()
        {
            if (_status != null && _status.IsConfirmOpen) return;

            if (!UITheme.IsTextInputFocused())
            {
                // 焦点在按钮上时空格交给 EventSystem 的 Submit（避免同一次按键触发两个动作）
                bool spaceAvailable = !UITheme.IsSelectableFocused();
                if (spaceAvailable && Input.GetKeyDown(KeyCode.Space)) TogglePlay();
                if (Input.GetKeyDown(KeyCode.LeftBracket)) Restart();
                if (Input.GetKeyDown(KeyCode.R)) _session.ResetCamera();
                if (Input.GetKeyDown(KeyCode.H)) CycleHighlight();
                if (Input.GetKeyDown(KeyCode.T)) _session.ToggleDisplayMode();   // 展示 / 教学 模式切换
                if (Input.GetKeyDown(KeyCode.M)) ToggleMetronome();
                if (Input.GetKeyDown(KeyCode.F1) && _status != null) _status.ToggleHelp();
                if (UITheme.ConsumeTab()) ToggleAnalysis();
            }

            // Esc 只在没有浮层时收起本层的分析面板 / 角色面板
            if ((_status == null || !_status.HasOpenOverlay) && UITheme.ConsumeEscape()) CloseTopPanel();
        }

        // ==================== 会话事件回调 ====================

        /// <summary>标准错误（<c>OnAppError</c>）：走错误横幅 + 恢复动作按钮（方案 8.3）。</summary>
        private void OnAppError(AppError error)
        {
            if (_status == null) return;
            _status.ShowError(error);
            RefreshSongAndState();
            RefreshInteractable();
        }

        /// <summary>
        /// 兜底错误（<c>OnError</c>，string）：走轻量提示，保证不会只写进 Console（方案 8.3）。
        /// </summary>
        private void OnErrorFallback(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_status != null) _status.ShowToast(message, ErrorSeverity.Warning);
            Debug.LogWarning($"[PianoTeacher3D][UI] {message}");
        }

        private void OnLessonStateChanged(LessonState state)
        {
            RefreshSongAndState();
            RefreshInteractable();
            if (_status != null)
                _status.SetLoading(state == LessonState.Loading, _session.CurrentSongName,
                    state == LessonState.Loading ? _session.LoadStageLabel : null);
        }

        /// <summary>加载阶段变化：只更新加载条文案（阶段由会话层决定，UI 不推断进度）。</summary>
        private void OnLoadStageChanged(SongLoadStage stage)
        {
            if (_status == null || _session.State != LessonState.Loading) return;
            _status.SetLoading(true, _session.CurrentSongName, SongLoadJob.StageLabel(stage));
        }

        private void OnSongLoaded()
        {
            _loopA = 0f;
            _loopB = 0f;
            RefreshSongAndState();
            RefreshLoopControls();
            RefreshHandHint();
            RefreshAnalysisContent();
            RefreshInteractable();
        }

        private void OnPlaybackStateChanged(PlaybackState state)
        {
            RefreshPlayButton(state);
            RefreshSongAndState();
        }

        private void OnProgressChanged(float current, float total)
        {
            _currentTime = current;
            _totalTime = total;
            if (_timeText != null) _timeText.text = UITheme.FormatTime(current);
            if (_totalText != null) _totalText.text = UITheme.FormatTime(total);
            if (_progress != null && total > 0f)
            {
                _suppressSlider = true;
                _progress.value = Mathf.Clamp01(current / total);
                _suppressSlider = false;
            }
            RefreshLoopMarks();
        }

        private void OnCameraModeChanged(CameraMode mode)
        {
            if (_cameraSeg != null) _cameraSeg.SetSelectedIndex((int)mode);
            RefreshViewLabel(mode);
            ApplyResponsiveLayout(); // 手部特写 → 主播放栏变矮并自动折叠练习工具
        }

        private void OnPracticeHandChanged(PracticeHand hand)
        {
            if (_handSeg != null) _handSeg.SetSelectedIndex((int)hand);
            RefreshHandHint();
            RefreshStateChips();
        }

        /// <summary>
        /// 显示模式变化：同步分段选择器；教学模式下手部不可见、高亮被固定为「仅发声」，
        /// 因此把「高亮」分段控件置灰并给出原因（避免用户以为设置失效）。
        /// </summary>
        private void OnDisplayModeChanged(DisplayMode mode)
        {
            if (_displaySeg != null) _displaySeg.SetSelectedIndex((int)mode, false);

            bool teaching = mode == DisplayMode.Teaching;
            if (_highlightSeg != null)
            {
                for (int i = 0; i < _highlightSeg.Count; i++)
                {
                    _highlightSeg.SetOptionEnabled(i, !teaching);
                    if (teaching)
                    {
                        _highlightSeg.SetOptionTooltip(i,
                            "教学模式固定为「仅高亮被按下的琴键」；切回展示模式后恢复你的选择");
                    }
                }
                if (!teaching)
                {
                    // 展示模式：恢复各档说明，并把选中项同步为实际生效值
                    _highlightSeg.SetOptionTooltip(0, "关闭琴键高亮提示");
                    _highlightSeg.SetOptionTooltip(1, "预提示：提前标出即将按下的琴键（跟弹用）", "H");
                    _highlightSeg.SetOptionTooltip(2, "发声：只高亮正在发声的琴键");
                    _highlightSeg.SetOptionTooltip(3, "全部：预提示 + 发声");
                }
                _highlightSeg.SetSelectedIndex((int)_session.KeyHighlight, false);
            }

            RefreshViewLabel(_session.CameraMode);
            RefreshStateChips();
            if (_status != null)
            {
                _status.ShowToast(mode == DisplayMode.Teaching
                    ? "教学模式：已隐藏手部，只高亮被按下的琴键（左手蓝 / 右手橙）"
                    : "展示模式：手部动画已恢复");
            }
        }

        private void OnHighlightModeChanged(HighlightMode mode)
        {
            if (_highlightSeg != null) _highlightSeg.SetSelectedIndex((int)mode);
            RefreshStateChips();
        }

        private void OnMetronomeToggled(bool enabled)
        {
            if (!enabled) _flashBeat = -1;
            RefreshBeatDots();
            RefreshStateChips();
            RefreshInteractable(); // 同步开关文字与可用态（禁用时不会误变回可点击）
        }

        private void OnMetronomeBeat(int beatInBar)
        {
            _flashBeat = beatInBar;
            _beatFlashUntil = Time.unscaledTime + 0.18f;
            RefreshBeatDots();
        }

        private void OnAnalysisReady(SongAnalysis analysis) => RefreshAnalysisContent();

        private void OnAvatarChanged(int index)
        {
            if (_status != null) _status.SetAvatarName(AvatarSelectionUI.GetAvatarName(index));
        }

        // ==================== 界面构建 ====================

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            _canvas = UITheme.CreateCanvas(transform, "LessonControlsCanvas", UITheme.Layout.LessonCanvasOrder);

            // 顶栏 + 错误/加载/提示反馈（独立画布，sortingOrder 更高，保证永远压在工具栏之上）
            _status = gameObject.AddComponent<UIStatusBar>();
            _status.Initialize(_session, BackToLibrary, ToggleAvatarPanel);

            BuildViewOverlay();
            BuildMainBar();
            BuildPracticeToolbar();
            BuildAnalysisPanel();
            BuildEmptyState();
        }

        // ---------- ① 3D 区域轻量浮层 ----------

        private void BuildViewOverlay()
        {
            _viewChip = UITheme.CreatePanel(_canvas.transform, "ViewChip", Vector2.zero, new Vector2(210f, 30f),
                UITheme.PanelBgDeep, UITheme.RadiusSmall);
            UITheme.PlaceTopLeft(_viewChip, UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space8, 210f, 30f);
            var chipBg = _viewChip.GetComponent<Image>();
            if (chipBg != null) chipBg.raycastTarget = false;

            _viewChipText = UITheme.CreateText(_viewChip, "视角：全景", Vector2.zero, new Vector2(198f, 28f),
                UITheme.FontCaption, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            UITheme.StretchAll(_viewChipText.rectTransform, 10f, 10f, 0f, 0f);

            _viewHintText = UITheme.CreateText(_canvas.transform,
                "空格 播放/暂停 · [ 重播 · M 节拍器 · T 展示/教学 · Tab 分析 · F1 帮助", Vector2.zero, new Vector2(560f, 22f),
                UITheme.FontStatusLabel, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_viewHintText.rectTransform, UITheme.Layout.ScreenMargin + 4f,
                UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space8 + 34f, 420f, 22f);
        }

        // ---------- ③ 主播放栏（始终可见、固定位置） ----------

        private void BuildMainBar()
        {
            _mainBar = UITheme.CreatePanel(_canvas.transform, "MainPlayBar", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, UITheme.Layout.MainBarHeightNormal),
                UITheme.PanelBg, UITheme.RadiusLarge);

            // 进度区域：当前时间 ━━━ 总时长（+ A/B 标记）
            _timeText = UITheme.CreateText(_mainBar, "00:00", Vector2.zero, new Vector2(76f, 24f),
                UITheme.FontCaption, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            _totalText = UITheme.CreateText(_mainBar, "00:00", Vector2.zero, new Vector2(76f, 24f),
                UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.MiddleRight);

            _progress = UITheme.CreateSlider(_mainBar, "Progress", Vector2.zero, new Vector2(900f, 18f));
            _progress.onValueChanged.AddListener(v =>
            {
                if (!_suppressSlider) _session.SeekNormalized(v);
            });
            _loopAMark = UITheme.CreateTrackMarker(_progress, UITheme.LoopStartColor, 3f);
            _loopBMark = UITheme.CreateTrackMarker(_progress, UITheme.LoopEndColor, 3f);
            UITheme.SetTrackMarker(_loopAMark, 0f, false);
            UITheme.SetTrackMarker(_loopBMark, 0f, false);

            _progressHint = UITheme.CreateText(_mainBar, "未加载曲目时无法播放", Vector2.zero, new Vector2(240f, 20f),
                UITheme.FontStatusLabel, UITheme.TextWarning, TextAnchor.MiddleLeft);

            // 主控制：曲名 / 状态标签 → 视角（分段）→ 重播 → 播放（唯一最强实心强调）
            _mainSongText = UITheme.CreateText(_mainBar, "♪ 未加载曲目", Vector2.zero, new Vector2(300f, 26f),
                UITheme.FontSmall, UITheme.TextPrimary, TextAnchor.MiddleLeft);

            _chipGroup = UITheme.CreateRect(_mainBar, "StateChips", Vector2.zero, Vector2.zero, UITheme.Transparent);
            _handChip = CreateChip(_chipGroup, "HandChip", 84f);
            _speedChip = CreateChip(_chipGroup, "SpeedChip", 74f);
            _loopChip = CreateChip(_chipGroup, "LoopChip", 78f);
            PlaceChips();

            _cameraSeg = UISegmentedControl.Create(_mainBar, "ViewSegmented", Vector2.zero,
                new Vector2(216f, UITheme.HeightInput), CameraLabels, (int)_session.CameraMode, index =>
                {
                    _session.SetCameraMode((CameraMode)index);
                    RefreshViewLabel((CameraMode)index);
                }, UITheme.FontCaption);
            _cameraSeg.SetOptionTooltip(0, "手部特写：优先看清琴键与手指");
            _cameraSeg.SetOptionTooltip(1, "全景：观察坐姿、双臂与完整键盘关系");
            _cameraSeg.SetOptionTooltip(2, "自由观察：右键拖动旋转、滚轮缩放，R 重置视角");

            _restartBtn = UITheme.CreateButton(_mainBar, "重播", Vector2.zero, new Vector2(92f, UITheme.HeightButton),
                Restart, UITheme.FontCaption, ButtonStyle.Secondary, "回到开头重新播放（不会改变速度与循环设置）", "[");
            _playBtn = UITheme.CreateButton(_mainBar, "播放", Vector2.zero, new Vector2(140f, UITheme.HeightPrimary),
                TogglePlay, UITheme.FontAction, ButtonStyle.Primary, "播放 / 暂停当前曲目", "空格");

            UITheme.ChainHorizontal(_cameraSeg.GetSegment(0), _cameraSeg.GetSegment(1), _cameraSeg.GetSegment(2),
                _restartBtn, _playBtn);
        }

        private Text CreateChip(Transform parent, string name, float width)
        {
            var panel = UITheme.CreatePanel(parent, name, Vector2.zero, new Vector2(width, 26f),
                UITheme.PanelBgRaised, UITheme.RadiusSmall);
            var img = panel.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            var text = UITheme.CreateText(panel, "", Vector2.zero, new Vector2(width - 12f, 24f),
                UITheme.FontStatusLabel, UITheme.TextPrimary);
            UITheme.StretchAll(text.rectTransform, 6f, 6f, 0f, 0f);
            return text;
        }

        private void PlaceChips()
        {
            if (_chipGroup == null) return;
            UITheme.Place(_chipGroup, 328f, 18f, 264f, 26f);
            UITheme.Place(_handChip.rectTransform.parent as RectTransform, 0f, 0f, 84f, 26f);
            UITheme.Place(_speedChip.rectTransform.parent as RectTransform, 92f, 0f, 74f, 26f);
            UITheme.Place(_loopChip.rectTransform.parent as RectTransform, 174f, 0f, 78f, 26f);
        }

        // ---------- ④ 练习工具栏（可折叠、按组归类） ----------

        private void BuildPracticeToolbar()
        {
            _toolbar = UITheme.CreatePanel(_canvas.transform, "PracticeToolbar", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, UITheme.Layout.ToolbarHeightExpanded),
                UITheme.PanelBg, UITheme.RadiusLarge);

            // 标题行：始终可见（折叠时就是「练习设置」入口，窄窗口不会失去入口）
            _toolbarTitle = UITheme.CreateText(_toolbar, "练习工具", Vector2.zero, new Vector2(84f, 24f),
                UITheme.FontCaption, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);

            _toolbarHint = UITheme.CreateText(_toolbar, "", Vector2.zero, new Vector2(600f, 22f),
                UITheme.FontStatusLabel, UITheme.TextSecondary, TextAnchor.MiddleLeft);

            _foldBtn = UITheme.CreateButton(_toolbar, "练习设置 ▾", Vector2.zero, new Vector2(112f, 26f),
                ToggleFold, UITheme.FontCaption, ButtonStyle.Ghost,
                "展开 / 收起练习设置（窄窗口与手部特写会自动收起）");

            _toolbarBody = UITheme.CreateRect(_toolbar, "ToolbarBody", Vector2.zero, Vector2.zero,
                UITheme.Transparent);
            UITheme.StretchAll(_toolbarBody, 0f, 0f, 6f, 40f);

            BuildToolbarRowA();
            BuildToolbarRowB();
        }

        private void BuildToolbarRowA()
        {
            // 练习手：分段选择器直接显示当前项（方案 3.4 / 审查 2.1）
            var handLabel = UITheme.CreateText(_toolbarBody, "练习手", Vector2.zero, new Vector2(56f, 22f),
                UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            UITheme.Place(handLabel.rectTransform, 12f, 55f, 56f, 22f);

            _handSeg = UISegmentedControl.Create(_toolbarBody, "HandSegmented", Vector2.zero,
                new Vector2(196f, UITheme.HeightInput), HandLabels, (int)_session.PracticeHand, index =>
                {
                    _session.SetPracticeHand((PracticeHand)index);
                    RefreshHandHint();
                    RefreshStateChips();
                }, UITheme.FontCaption);
            UITheme.Place(_handSeg.Rect, 72f, 48f, 196f, UITheme.HeightInput);
            _handSeg.SetOptionTooltip(0, "双手练习：左右手声部都发声");
            _handSeg.SetOptionTooltip(1, "只练左手：右手声部静音");
            _handSeg.SetOptionTooltip(2, "只练右手：左手声部静音");

            // 某手无音符时就近提示（审查 2.2C：不要只出现在远处状态栏）
            _handHint = UITheme.CreateText(_toolbarBody, "", Vector2.zero, new Vector2(272f, 34f),
                UITheme.FontStatusLabel, UITheme.TextWarning, TextAnchor.MiddleLeft);
            _handHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            _handHint.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.Place(_handHint.rectTransform, 274f, 48f, 272f, 34f);

            // 速度：分段选择器替代循环切换按钮（方案 3.4）
            var speedLabel = UITheme.CreateText(_toolbarBody, "速度", Vector2.zero, new Vector2(40f, 22f),
                UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            UITheme.Place(speedLabel.rectTransform, 556f, 55f, 40f, 22f);

            _speedSeg = UISegmentedControl.Create(_toolbarBody, "SpeedSegmented", Vector2.zero,
                new Vector2(180f, UITheme.HeightInput), SpeedLabels, IndexOfSpeed(_session.PlaybackSpeed), index =>
                {
                    _session.SetSpeed(SpeedOptions[index]);
                    RefreshStateChips();
                }, UITheme.FontCaption);
            UITheme.Place(_speedSeg.Rect, 600f, 48f, 180f, UITheme.HeightInput);
            _speedSeg.SetOptionTooltip(0, "0.5 倍速：慢练难点片段");
            _speedSeg.SetOptionTooltip(1, "原速（1.0 倍速）");
            _speedSeg.SetOptionTooltip(2, "1.5 倍速：熟悉后提速");

            // 高亮提示：分段选择器（关闭 / 预提示 / 发声 / 全部）
            var highlightLabel = UITheme.CreateText(_toolbarBody, "高亮", Vector2.zero, new Vector2(40f, 22f),
                UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            UITheme.Place(highlightLabel.rectTransform, 794f, 55f, 40f, 22f);

            _highlightSeg = UISegmentedControl.Create(_toolbarBody, "HighlightSegmented", Vector2.zero,
                new Vector2(228f, UITheme.HeightInput), HighlightLabels, (int)_session.KeyHighlight, index =>
                {
                    _session.SetHighlightMode((HighlightMode)index);
                    RefreshStateChips();
                }, UITheme.FontCaption);
            UITheme.Place(_highlightSeg.Rect, 838f, 48f, 228f, UITheme.HeightInput);
            _highlightSeg.SetOptionTooltip(0, "关闭琴键高亮提示");
            _highlightSeg.SetOptionTooltip(1, "预提示：提前标出即将按下的琴键（跟弹用）", "H");
            _highlightSeg.SetOptionTooltip(2, "发声：只高亮正在发声的琴键");
            _highlightSeg.SetOptionTooltip(3, "全部：预提示 + 发声（默认教学模式）");

            // 循环开关（开关 + 状态文字，非颜色区分）
            _loopToggle = UITheme.CreateToggleButton(_toolbarBody, "循环", Vector2.zero,
                new Vector2(96f, UITheme.HeightInput), _session.IsLooping, ToggleLoop,
                "循环播放：打开后播放到结尾自动回到开头，并可设置 A / B 片段");
            UITheme.PlaceRight(_loopToggle.GetComponent<RectTransform>(), 12f, 48f, 96f, UITheme.HeightInput);
        }

        private void BuildToolbarRowB()
        {
            // 节拍器：开关 + 强弱拍视觉指示
            _metronomeBtn = UITheme.CreateToggleButton(_toolbarBody, "节拍器", Vector2.zero,
                new Vector2(112f, UITheme.HeightInput), _session.MetronomeEnabled, ToggleMetronome,
                "节拍器开关（拍点跟随课程速度缩放）", "M");
            UITheme.Place(_metronomeBtn.GetComponent<RectTransform>(), 12f, 6f, 112f, UITheme.HeightInput);

            _beatDots = UITheme.CreateText(_toolbarBody, "", Vector2.zero, new Vector2(150f, 22f),
                UITheme.FontSmall, UITheme.Accent, TextAnchor.MiddleLeft);
            UITheme.Place(_beatDots.rectTransform, 130f, 13f, 150f, 22f);

            // A-B 循环：启用循环后才出现（方案 3.2 / 审查 2.1）
            _loopRangeGroup = UITheme.CreateRect(_toolbarBody, "LoopRange", Vector2.zero, Vector2.zero,
                UITheme.Transparent);
            UITheme.Place(_loopRangeGroup, 288f, 6f, 542f, UITheme.HeightInput);

            _setABtn = UITheme.CreateButton(_loopRangeGroup, "设置 A", Vector2.zero, new Vector2(92f, 32f),
                SetLoopPointA, UITheme.FontCaption, ButtonStyle.Secondary,
                "把当前播放位置记为循环起点 A");
            UITheme.Place(_setABtn.GetComponent<RectTransform>(), 0f, 2f, 92f, 32f);

            _setBBtn = UITheme.CreateButton(_loopRangeGroup, "设置 B", Vector2.zero, new Vector2(92f, 32f),
                SetLoopPointB, UITheme.FontCaption, ButtonStyle.Secondary,
                "把当前播放位置记为循环终点 B（必须晚于 A）");
            UITheme.Place(_setBBtn.GetComponent<RectTransform>(), 100f, 2f, 92f, 32f);

            _clearLoopBtn = UITheme.CreateButton(_loopRangeGroup, "清除 A-B", Vector2.zero, new Vector2(96f, 32f),
                ClearLoopRange, UITheme.FontCaption, ButtonStyle.Ghost,
                "清除 A / B 标记，循环范围恢复为整曲");
            UITheme.Place(_clearLoopBtn.GetComponent<RectTransform>(), 200f, 2f, 96f, 32f);

            _loopRangeText = UITheme.CreateText(_loopRangeGroup, "", Vector2.zero, new Vector2(240f, 22f),
                UITheme.FontStatusLabel, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            UITheme.Place(_loopRangeText.rectTransform, 304f, 13f, 240f, 22f);

            // 循环关闭时的说明（就近解释，不留空白让人猜）
            _loopOffHint = UITheme.CreateText(_toolbarBody, "循环已关闭：打开「循环」后可设置 A / B 片段",
                Vector2.zero, new Vector2(420f, 22f), UITheme.FontStatusLabel, UITheme.TextSecondary,
                TextAnchor.MiddleLeft);
            UITheme.Place(_loopOffHint.rectTransform, 288f, 13f, 420f, 22f);

            // 显示模式：展示（完整手部动画）/ 教学（隐藏手部，仅高亮被按下的琴键）
            _displaySeg = UISegmentedControl.Create(_toolbarBody, "DisplayModeSegmented", Vector2.zero,
                new Vector2(168f, UITheme.HeightInput), new[] { "展示", "教学" }, (int)_session.DisplayMode, index =>
                {
                    _session.SetDisplayMode((DisplayMode)index);
                    RefreshStateChips();
                }, UITheme.FontCaption);
            UITheme.Place(_displaySeg.Rect, 838f, 6f, 168f, UITheme.HeightInput);
            _displaySeg.SetOptionTooltip(0, "展示模式：完整手部动画与演奏动作（高亮提示按你的设置）", "T");
            _displaySeg.SetOptionTooltip(1, "教学模式：隐藏手部动画，仅高亮被按下的琴键（左手冷蓝 · 右手暖橙）", "T");

            // 低频工具：曲目分析 / 角色外观（弱化样式，且说明快捷键）
            _analysisBtn = UITheme.CreateButton(_toolbarBody, "曲目分析", Vector2.zero, new Vector2(108f, 32f),
                ToggleAnalysis, UITheme.FontCaption, ButtonStyle.Ghost,
                "曲目分析：音符数、左右手比例、和弦、音域、跨度与练习建议", "Tab");
            UITheme.PlaceRight(_analysisBtn.GetComponent<RectTransform>(), 128f, 8f, 108f, 32f);

            _avatarBtn = UITheme.CreateButton(_toolbarBody, "角色 / 外观", Vector2.zero, new Vector2(108f, 32f),
                ToggleAvatarPanel, UITheme.FontCaption, ButtonStyle.Ghost,
                "角色 / 外观面板：切换角色不会改变播放进度");
            UITheme.PlaceRight(_avatarBtn.GetComponent<RectTransform>(), 12f, 8f, 108f, 32f);

            UITheme.ChainHorizontal(_metronomeBtn, _setABtn, _setBBtn, _clearLoopBtn, _analysisBtn, _avatarBtn);
        }

        // ---------- ⑤ 分析面板（右侧滑出，动作质量默认折叠） ----------

        private void BuildAnalysisPanel()
        {
            _analysisPanel = UITheme.CreatePanel(_canvas.transform, "AnalysisPanel", Vector2.zero,
                new Vector2(UITheme.Layout.AnalysisPanelWidth, 420f), UITheme.PanelBg, UITheme.RadiusLarge);

            var title = UITheme.CreateText(_analysisPanel, "曲目分析", Vector2.zero, new Vector2(240f, 28f),
                UITheme.FontSongTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(title.rectTransform, 18f, 14f, 240f, 28f);

            _analysisCloseBtn = UITheme.CreateButton(_analysisPanel, "收起", Vector2.zero, new Vector2(72f, 28f),
                () => ShowAnalysis(false), UITheme.FontCaption, ButtonStyle.Ghost, "收起分析面板");
            UITheme.PlaceTopRight(_analysisCloseBtn.GetComponent<RectTransform>(), 14f, 14f, 72f, 28f);

            var divider = UITheme.CreateDivider(_analysisPanel, "Divider", Vector2.zero, new Vector2(360f, 1f));
            UITheme.StretchTop(divider, 18f, 18f, 50f, 1f);

            _analysisText = UITheme.CreateText(_analysisPanel, "", Vector2.zero, new Vector2(360f, 150f),
                UITheme.FontCaption, UITheme.TextPrimary, TextAnchor.UpperLeft);
            _analysisText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _analysisText.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(_analysisText.rectTransform, 18f, 60f, 360f, 150f);

            _suggestionText = UITheme.CreateText(_analysisPanel, "", Vector2.zero, new Vector2(360f, 84f),
                UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.UpperLeft);
            _suggestionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _suggestionText.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(_suggestionText.rectTransform, 18f, 216f, 360f, 84f);

            var divider2 = UITheme.CreateDivider(_analysisPanel, "Divider2", Vector2.zero, new Vector2(360f, 1f));
            UITheme.StretchTop(divider2, 18f, 18f, 306f, 1f);

            _diagnosticsToggle = UITheme.CreateToggleButton(_analysisPanel, "开发诊断", Vector2.zero,
                new Vector2(140f, 28f), false, ToggleDiagnostics,
                "动作质量指标（命中率 / 最大帧位移 / 平滑度 / 双手协调）：开发与验收用，默认折叠",
                null, UITheme.FontCaption);
            UITheme.PlaceTopLeft(_diagnosticsToggle.GetComponent<RectTransform>(), 18f, 316f, 140f, 28f);

            _diagnosticsBody = UITheme.CreateRect(_analysisPanel, "DiagnosticsBody", Vector2.zero,
                Vector2.zero, UITheme.Transparent);
            UITheme.PlaceTopLeft(_diagnosticsBody, 18f, 350f, 360f, 56f);

            _motionText = UITheme.CreateText(_diagnosticsBody, "", Vector2.zero, new Vector2(360f, 56f),
                UITheme.FontStatusLabel, UITheme.TextSecondary, TextAnchor.UpperLeft);
            _motionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _motionText.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.StretchAll(_motionText.rectTransform, 0f, 0f, 0f, 0f);

            _diagnosticsBody.gameObject.SetActive(false);
            _analysisPanel.gameObject.SetActive(false);
            _analysisSlide = 0f;
        }

        // ---------- 空状态（方案 8.2「没有可用曲目 → 空状态页 + 导入 MIDI / 播放示例曲」） ----------

        private void BuildEmptyState()
        {
            _emptyState = UITheme.CreatePanel(_canvas.transform, "EmptyState", Vector2.zero,
                new Vector2(480f, 168f), UITheme.PanelBgRaised, UITheme.RadiusLarge);
            UITheme.PlaceCenter(_emptyState, 480f, 168f);

            var title = UITheme.CreateText(_emptyState, "还没有可练习的曲目", Vector2.zero, new Vector2(432f, 28f),
                UITheme.FontSongTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(title.rectTransform, 24f, 18f, 432f, 28f);

            _emptyBody = UITheme.CreateText(_emptyState,
                "导入本地 .mid / .midi 文件即可开始练习；也可以先播放内置示例曲，熟悉界面与视角。\n" +
                "未加载曲目时播放、重播与循环按钮保持不可用，避免点了没反应。",
                Vector2.zero, new Vector2(432f, 48f), UITheme.FontCaption, UITheme.TextSecondary,
                TextAnchor.UpperLeft);
            _emptyBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _emptyBody.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(_emptyBody.rectTransform, 24f, 52f, 432f, 48f);

            var importBtn = UITheme.CreateButton(_emptyState, "导入 MIDI…", Vector2.zero, new Vector2(150f, 34f),
                () => _session.ImportMidi(), UITheme.FontCaption, ButtonStyle.Primary,
                "打开文件选择器导入本地 MIDI 文件");
            UITheme.Place(importBtn.GetComponent<RectTransform>(), 24f, 12f, 150f, 34f);

            var sampleBtn = UITheme.CreateButton(_emptyState, "播放示例曲", Vector2.zero, new Vector2(150f, 34f),
                PlaySampleSong, UITheme.FontCaption, ButtonStyle.Secondary,
                "载入 StreamingAssets/Songs 中的第一首示例曲");
            UITheme.Place(sampleBtn.GetComponent<RectTransform>(), 186f, 12f, 150f, 34f);

            UITheme.ChainHorizontal(importBtn, sampleBtn);
            _emptyState.gameObject.SetActive(false);
        }

        // ==================== 交互动作（全部转调会话层公开接口） ====================

        private void TogglePlay()
        {
            if (_session.IsLoading) return; // 加载中防重复点击
            if (!UITheme.CanTriggerSongActions(_session.State, _session.HasSong)) return;
            _session.PlayPause();
        }

        private void Restart()
        {
            if (_session.IsLoading) return;
            if (!UITheme.CanTriggerSongActions(_session.State, _session.HasSong)) return;
            _session.Restart();
        }

        private void ToggleLoop()
        {
            if (!UITheme.CanTriggerSongActions(_session.State, _session.HasSong)) return;
            if (_session.IsLooping)
            {
                _session.ToggleLoop(); // 已循环 → 关闭
            }
            else
            {
                _session.SetLoop(0f, Mathf.Max(0f, _totalTime)); // 先整曲循环，再由用户收窄到 A-B
                _loopA = 0f;
                _loopB = 0f;
            }
            RefreshLoopControls();
        }

        private void SetLoopPointA()
        {
            if (!UITheme.CanTriggerSongActions(_session.State, _session.HasSong)) return;
            _loopA = _currentTime;
            if (_loopB > _loopA + 0.2f)
            {
                _session.SetLoop(_loopA, _loopB);
                _session.Seek(_loopA);
                if (_status != null) _status.ShowToast($"循环区间已更新：A {UITheme.FormatTime(_loopA)} → B {UITheme.FormatTime(_loopB)}");
            }
            else
            {
                if (_status != null)
                    _status.ShowToast($"已记录起点 A = {UITheme.FormatTime(_loopA)}。请把进度拖到片段结束处，再点「设置 B」。");
            }
            RefreshLoopControls();
        }

        private void SetLoopPointB()
        {
            if (!UITheme.CanTriggerSongActions(_session.State, _session.HasSong)) return;
            _loopB = _currentTime;
            if (_loopB > _loopA + 0.2f)
            {
                _session.SetLoop(_loopA, _loopB);
                if (_status != null) _status.ShowToast($"循环区间已生效：A {UITheme.FormatTime(_loopA)} → B {UITheme.FormatTime(_loopB)}");
            }
            else
            {
                _loopB = 0f;
                if (_status != null)
                    _status.ShowToast($"循环终点无效：终点必须晚于起点 A（A = {UITheme.FormatTime(_loopA)}）。请先播放到更靠后的位置再设置 B。",
                        ErrorSeverity.Warning);
            }
            RefreshLoopControls();
        }

        private void ClearLoopRange()
        {
            _loopA = 0f;
            _loopB = 0f;
            if (UITheme.CanTriggerSongActions(_session.State, _session.HasSong))
            {
                // 保留整曲循环开关，只清除 A-B 标记
                _session.SetLoop(0f, Mathf.Max(0f, _totalTime));
                if (_status != null) _status.ShowToast("已清除 A / B 标记，循环范围恢复为整曲。");
            }
            RefreshLoopControls();
        }

        private void ToggleMetronome()
        {
            if (_session.IsLoading) return;
            _session.ToggleMetronome();
            RefreshBeatDots();
        }

        private void CycleHighlight()
        {
            _session.CycleHighlightMode();
            if (_highlightSeg != null) _highlightSeg.SetSelectedIndex((int)_session.KeyHighlight);
            RefreshStateChips();
        }

        private void ToggleAnalysis() => ShowAnalysis(!_analysisOpen);

        private void ShowAnalysis(bool show)
        {
            if (_analysisPanel == null) return;
            _analysisOpen = show;
            if (show)
            {
                UITooltip.Hide();
                // 分析面板与角色面板同处右侧：互斥打开，避免互相遮挡
                ResolveAvatarUI();
                if (_avatarUI != null && _avatarUI.IsPanelOpen) _avatarUI.ShowPanel(false);
                _analysisPanel.gameObject.SetActive(true);
                RefreshAnalysisContent();
                RefreshMotion();
            }
            if (_analysisBtn != null)
                UITheme.ApplyButtonState(_analysisBtn, show ? ButtonState.Selected : ButtonState.Default,
                    ButtonStyle.Ghost);
        }

        private void AnimateAnalysisPanel()
        {
            if (_analysisPanel == null) return;
            float target = _analysisOpen ? 1f : 0f;
            if (Mathf.Approximately(_analysisSlide, target))
            {
                if (!_analysisOpen && _analysisPanel.gameObject.activeSelf) _analysisPanel.gameObject.SetActive(false);
                return;
            }

            _analysisSlide = Mathf.MoveTowards(_analysisSlide,
                target, Time.unscaledDeltaTime * (UITheme.PanelSlideSpeed / UITheme.Layout.AnalysisPanelWidth));
            LayoutAnalysisPanel();
        }

        private void ToggleDiagnostics()
        {
            _diagnosticsOpen = !_diagnosticsOpen;
            UITheme.SetToggleState(_diagnosticsToggle, "开发诊断", _diagnosticsOpen);
            if (_diagnosticsBody != null) _diagnosticsBody.gameObject.SetActive(_diagnosticsOpen);
            if (_diagnosticsOpen) RefreshMotion();
        }

        private void ToggleFold()
        {
            _folded = !_folded;
            _foldTouched = true;
            ApplyFoldState();
        }

        private void ToggleAvatarPanel()
        {
            ResolveAvatarUI();
            if (_avatarUI == null) return;
            if (!_avatarUI.IsPanelOpen) ShowAnalysis(false); // 与右侧分析面板互斥
            _avatarUI.TogglePanel();
        }

        private void ResolveAvatarUI()
        {
            if (_avatarUI != null) return;
            _avatarUI = GetComponent<AvatarSelectionUI>();
            if (_avatarUI == null) _avatarUI = FindFirstObjectByType<AvatarSelectionUI>();
        }

        /// <summary>收起本层最上方的面板（Esc）：分析面板 → 角色面板由各自脚本处理。</summary>
        private void CloseTopPanel()
        {
            if (_analysisOpen) { ShowAnalysis(false); return; }
            ResolveAvatarUI();
            if (_avatarUI != null && _avatarUI.IsPanelOpen) _avatarUI.ShowPanel(false);
        }

        private void BackToLibrary()
        {
            SceneManager.LoadScene("MainMenu");
        }

        /// <summary>播放内置示例曲（空状态入口；只调用会话层公开的曲目加载接口）。</summary>
        private void PlaySampleSong()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Songs");
            try
            {
                if (!Directory.Exists(dir))
                {
                    if (_status != null) _status.ShowToast("未找到示例曲目录 StreamingAssets/Songs。请用「导入 MIDI…」选择本地文件。",
                        ErrorSeverity.Warning);
                    return;
                }
                var files = Directory.GetFiles(dir, "*.mid");
                if (files == null || files.Length == 0)
                {
                    if (_status != null) _status.ShowToast("示例曲目录中没有 .mid 文件。请用「导入 MIDI…」选择本地文件。",
                        ErrorSeverity.Warning);
                    return;
                }
                System.Array.Sort(files);
                _session.LoadSongAsync(files[0]);   // 走可取消的分阶段加载（优化方案 §7-8）
            }
            catch (System.Exception ex)
            {
                if (_status != null) _status.ShowToast("读取示例曲目录失败，请用「导入 MIDI…」选择本地文件。",
                    ErrorSeverity.Warning);
                Debug.LogWarning($"[PianoTeacher3D][UI] 读取示例曲目录失败：{ex.GetType().Name}");
            }
        }

        // ==================== 响应式布局（方案 3.4 末条 / 15 章窗口缩放） ====================

        private void ApplyResponsiveLayoutIfNeeded()
        {
            float refW = UITheme.GetReferenceWidth(_canvas);
            bool closeup = _session.CameraMode == CameraMode.Closeup;
            if (Mathf.Abs(refW - _lastRefWidth) < 0.5f && closeup == _lastCompact) return;
            ApplyResponsiveLayout();
        }

        /// <summary>
        /// 参考分辨率（1280×720）下的自适应：
        /// 宽窗口（≥1180）展开练习工具；窄窗口自动折叠（不缩小主按钮）；
        /// 手部特写压缩主播放栏高度并折叠练习工具，避免遮挡琴键与双手。
        /// </summary>
        private void ApplyResponsiveLayout()
        {
            float refW = UITheme.GetReferenceWidth(_canvas);
            bool narrow = UITheme.IsNarrowReferenceWidth(refW);
            bool closeup = _session != null && _session.CameraMode == CameraMode.Closeup;

            // 档位变化时按规则自动折叠；用户手动展开/收起优先（直到档位再次变化）
            if ((narrow != _lastNarrow || closeup != _lastCompact) && !_foldTouched)
                _folded = UITheme.ShouldFoldPracticeTools(refW, closeup);

            _lastRefWidth = refW;
            _lastNarrow = narrow;
            _lastCompact = closeup;
            _compact = closeup;

            float margin = UITheme.Layout.ScreenMargin;
            float mainH = closeup ? UITheme.Layout.MainBarHeightCompact : UITheme.Layout.MainBarHeightNormal;

            // ---- 主播放栏 ----
            UITheme.StretchX(_mainBar, margin, margin, margin, mainH);

            float progressH = closeup
                ? UITheme.Layout.ProgressRowHeightCompact
                : UITheme.Layout.ProgressRowHeightNormal;
            float progressY = mainH - progressH - (closeup ? 6f : 10f);
            float timeW = 76f;
            UITheme.Place(_timeText.rectTransform, 16f, progressY, timeW, progressH + 2f);
            UITheme.PlaceRight(_totalText.rectTransform, 16f, progressY, timeW, progressH + 2f);
            UITheme.StretchX(_progress.GetComponent<RectTransform>(), 16f + timeW + 8f, 16f + timeW + 8f,
                progressY, progressH);

            float ctrlH = closeup ? 38f : 48f;
            float ctrlY = closeup ? 6f : 12f;
            float secH = closeup ? 32f : 40f;
            float secW = closeup ? 76f : 92f;
            float playW = closeup ? 116f : 140f;
            float segW = closeup ? 186f : 216f;

            UITheme.PlaceRight(_playBtn.GetComponent<RectTransform>(), 16f, ctrlY, playW, ctrlH);
            UITheme.PlaceRight(_restartBtn.GetComponent<RectTransform>(), 16f + playW + 8f,
                ctrlY + (ctrlH - secH) * 0.5f, secW, secH);
            UITheme.PlaceRight(_cameraSeg.Rect, 16f + playW + 8f + secW + 8f,
                ctrlY + (ctrlH - UITheme.HeightInput) * 0.5f, segW, UITheme.HeightInput);

            // 窄窗口 / 手部特写：曲名与状态标签已在顶栏与练习工具栏标题行可见，这里收起而不压缩按钮
            _showBarLeft = !narrow && !closeup;
            _mainSongText.gameObject.SetActive(_showBarLeft);
            _chipGroup.gameObject.SetActive(_showBarLeft);
            UITheme.Place(_mainSongText.rectTransform, 16f, ctrlY + (ctrlH - 26f) * 0.5f, 300f, 26f);
            // 无曲目时的原因说明与曲名共用同一位置（互斥显示，就近解释为什么不点不动）
            UITheme.Place(_progressHint.rectTransform, 16f, ctrlY + (ctrlH - 20f) * 0.5f, 300f, 20f);
            if (_showBarLeft) PlaceChips();

            // ---- 练习工具栏（主播放栏之上，固定顺序：进度 → 主控制 → 工具） ----
            ApplyFoldState();

            // ---- 3D 区浮层与空状态 ----
            _viewHintText.gameObject.SetActive(!closeup);
            RefreshSongAndState();

            // ---- 顶栏自适应（隐藏「保存状态」并让曲名区变宽） ----
            if (_status != null) _status.ApplyResponsiveLayout(refW);

            LayoutAnalysisPanel();
        }

        private void ApplyFoldState()
        {
            float margin = UITheme.Layout.ScreenMargin;
            float mainH = _compact ? UITheme.Layout.MainBarHeightCompact : UITheme.Layout.MainBarHeightNormal;
            float toolbarH = _folded ? UITheme.Layout.ToolbarHeightCollapsed : UITheme.Layout.ToolbarHeightExpanded;

            UITheme.StretchX(_toolbar, margin, margin, margin + mainH + UITheme.Space8, toolbarH);

            if (_toolbarTitle != null)
                UITheme.PlaceTopLeft(_toolbarTitle.rectTransform, 12f, 6f, 84f, UITheme.Layout.ToolbarHeaderHeight);
            UITheme.PlaceTopRight(_foldBtn.GetComponent<RectTransform>(), 12f, 5f, 112f, 26f);
            if (_toolbarHint != null) UITheme.StretchX(_toolbarHint.rectTransform, 104f, 132f, toolbarH - 24f, 20f);

            if (_toolbarBody != null)
            {
                bool visible = !_folded;
                if (_toolbarBody.gameObject.activeSelf != visible) _toolbarBody.gameObject.SetActive(visible);
            }
            UITheme.SetButtonLabel(_foldBtn, _folded ? "练习设置 ▸" : "练习设置 ▾");
            RefreshStateChips();
            LayoutAnalysisPanel(); // 工具栏高度变化后，分析面板的可用高度同步更新
        }

        private void LayoutAnalysisPanel()
        {
            if (_analysisPanel == null) return;
            float margin = UITheme.Layout.ScreenMargin;
            float mainH = _compact ? UITheme.Layout.MainBarHeightCompact : UITheme.Layout.MainBarHeightNormal;
            float toolbarH = _folded ? UITheme.Layout.ToolbarHeightCollapsed : UITheme.Layout.ToolbarHeightExpanded;
            float top = margin + UITheme.Layout.StatusBarHeight + UITheme.Space8;
            float bottom = margin + mainH + UITheme.Space8 + toolbarH + UITheme.Space8;
            float slide = (1f - _analysisSlide) * UITheme.Layout.AnalysisPanelWidth;
            UITheme.StretchRightColumn(_analysisPanel, UITheme.Layout.AnalysisPanelWidth, top, bottom, margin, slide);
        }

        // ==================== 状态刷新 ====================

        private void RefreshAll()
        {
            RefreshSongAndState();
            RefreshPlayButton(_session.PlaybackState);
            RefreshLoopControls();
            RefreshBeatDots();
            RefreshHandHint();
            RefreshAnalysisContent();
            RefreshViewLabel(_session.CameraMode);
            RefreshStateChips();
            if (_cameraSeg != null) _cameraSeg.SetSelectedIndex((int)_session.CameraMode);
            if (_handSeg != null) _handSeg.SetSelectedIndex((int)_session.PracticeHand);
            if (_highlightSeg != null) _highlightSeg.SetSelectedIndex((int)_session.KeyHighlight);
            if (_speedSeg != null) _speedSeg.SetSelectedIndex(IndexOfSpeed(_session.PlaybackSpeed));
            if (_status != null)
            {
                _status.SetAvatarName(AvatarSelectionUI.GetAvatarName(_session.CurrentAvatarIndex));
                _status.ApplyResponsiveLayout(UITheme.GetReferenceWidth(_canvas));
            }
            ApplyResponsiveLayout();
            RefreshInteractable();
        }

        private void RefreshSongAndState()
        {
            if (_session == null) return;
            bool hasSong = _session.HasSong;
            var state = _session.State;

            if (_mainSongText != null)
            {
                string name = hasSong ? _session.CurrentSongName : "未加载曲目";
                _mainSongText.text = "♪ " + UITheme.Truncate(name, UITheme.Layout.MainBarSongNameMaxChars);
                _mainSongText.color = hasSong ? UITheme.TextPrimary : UITheme.TextSecondary;
            }

            if (_status != null)
                _status.SetSong(_session.CurrentSongName, hasSong, state, _session.PlaybackState);

            // 空状态只在「确实没有可播放曲目」时出现（出错且有旧曲目时保留旧曲目）
            bool showEmpty = !hasSong && (state == LessonState.NoSong || state == LessonState.Error);
            if (_emptyState != null && _emptyState.gameObject.activeSelf != showEmpty)
                _emptyState.gameObject.SetActive(showEmpty);

            if (_progressHint != null)
            {
                string reason = ExplainNoSong(state, hasSong);
                _progressHint.text = reason;
                bool show = !string.IsNullOrEmpty(reason) && _showBarLeft;
                if (_progressHint.gameObject.activeSelf != show) _progressHint.gameObject.SetActive(show);
                if (_mainSongText != null && _showBarLeft) _mainSongText.gameObject.SetActive(!show);
            }

            RefreshStateChips();
        }

        /// <summary>无曲目时说明「为什么不能播放、下一步做什么」（方案 8.3 / 审查 2.2C）。</summary>
        private string ExplainNoSong(LessonState state, bool hasSong)
        {
            if (hasSong) return "";
            if (state == LessonState.Loading) return "正在解析曲目，完成后自动可用…";
            if (state == LessonState.Error) return "当前曲目不可用：请按上方提示恢复，或重新导入 MIDI。";
            return "未加载曲目：请先「导入 MIDI」或在空状态里播放示例曲。";
        }

        private void RefreshPlayButton(PlaybackState playback)
        {
            if (_playBtn == null || _session == null) return;

            if (_session.IsLoading)
            {
                UITheme.SetButtonLabel(_playBtn, "加载中…");
                UITheme.ApplyButtonState(_playBtn, ButtonState.Loading, ButtonStyle.Primary);
                return;
            }

            UITheme.SetButtonLabel(_playBtn, UITheme.PlayButtonLabel(_session.State, playback));
            bool canPlay = UITheme.CanTriggerSongActions(_session.State, _session.HasSong);
            if (!canPlay)
            {
                UITheme.SetButtonEnabled(_playBtn, false,
                    "不可用：" + ExplainNoSong(_session.State, false), ButtonStyle.Primary);
                return;
            }
            _playBtn.interactable = true;
            UITheme.ApplyButtonState(_playBtn,
                playback == PlaybackState.Playing ? ButtonState.Selected : ButtonState.Default,
                ButtonStyle.Primary);
        }

        /// <summary>需要曲目的控件在无曲目 / 加载中统一呈禁用态并说明原因（防重复点击）。</summary>
        private void RefreshInteractable()
        {
            if (_session == null) return;
            bool canAct = UITheme.CanTriggerSongActions(_session.State, _session.HasSong);
            bool loading = _session.IsLoading;
            string reason = "不可用：" + ExplainNoSong(_session.State, _session.HasSong);

            RefreshPlayButton(_session.PlaybackState);

            if (_restartBtn != null)
                UITheme.SetButtonEnabled(_restartBtn, canAct && !loading, reason, ButtonStyle.Secondary);

            if (_handSeg != null) _handSeg.Interactable = !loading && _session.HasSong;
            if (_speedSeg != null) _speedSeg.Interactable = !loading;
            if (_highlightSeg != null) _highlightSeg.Interactable = !loading;
            if (_cameraSeg != null) _cameraSeg.Interactable = true; // 视角随时可切，不改变时间轴

            if (_loopToggle != null)
            {
                UITheme.SetButtonEnabled(_loopToggle, canAct && !loading,
                    "不可用：" + ExplainNoSong(_session.State, false), ButtonStyle.Toggle);
                UITheme.SetToggleState(_loopToggle, "循环", _session.IsLooping, canAct && !loading);
            }
            if (_setABtn != null)
                UITheme.SetButtonEnabled(_setABtn, canAct, "不可用：先加载曲目并打开循环", ButtonStyle.Secondary);
            if (_setBBtn != null)
                UITheme.SetButtonEnabled(_setBBtn, canAct, "不可用：先加载曲目并打开循环", ButtonStyle.Secondary);
            if (_clearLoopBtn != null)
                UITheme.SetButtonEnabled(_clearLoopBtn, canAct, "不可用：先加载曲目", ButtonStyle.Ghost);
            if (_metronomeBtn != null)
            {
                bool metronomeOk = !loading && (canAct || _session.MetronomeEnabled);
                UITheme.SetButtonEnabled(_metronomeBtn, metronomeOk,
                    "不可用：先加载曲目后再开启节拍器", ButtonStyle.Toggle);
                UITheme.SetToggleState(_metronomeBtn, "节拍器", _session.MetronomeEnabled, metronomeOk);
            }

            // 进度条本身在无曲目时不可拖动
            if (_progress != null) _progress.interactable = canAct;
        }

        private void RefreshLoopControls()
        {
            if (_session == null) return;
            bool looping = _session.IsLooping;
            bool canAct = UITheme.CanTriggerSongActions(_session.State, _session.HasSong) && !_session.IsLoading;
            UITheme.SetToggleState(_loopToggle, "循环", looping, canAct);

            bool hasRange = _loopB > _loopA + 0.2f;
            if (_loopRangeGroup != null && _loopRangeGroup.gameObject.activeSelf != (looping && !_session.IsLoading))
                _loopRangeGroup.gameObject.SetActive(looping && !_session.IsLoading);
            if (_loopOffHint != null && _loopOffHint.gameObject.activeSelf != !looping)
                _loopOffHint.gameObject.SetActive(!looping);

            if (_loopRangeText != null)
            {
                _loopRangeText.text = hasRange
                    ? $"A {UITheme.FormatTime(_loopA)} → B {UITheme.FormatTime(_loopB)}（{UITheme.FormatTime(Mathf.Max(0f, _loopB - _loopA))}）"
                    : "整曲循环（可设置 A / B 收窄范围）";
            }
            RefreshLoopMarks();
            RefreshStateChips();
        }

        private void RefreshLoopMarks()
        {
            if (_progress == null) return;
            bool hasRange = _loopB > _loopA + 0.2f && _totalTime > 0.1f;
            UITheme.SetTrackMarker(_loopAMark, _loopA / _totalTime, hasRange);
            UITheme.SetTrackMarker(_loopBMark, _loopB / _totalTime, hasRange);
        }

        private void RefreshBeatDots()
        {
            if (_beatDots == null) return;
            if (!_session.MetronomeEnabled)
            {
                _beatDots.text = "";
                return;
            }

            int beats = Mathf.Max(1, _session.BeatsPerBar);
            var sb = new StringBuilder(beats * 2);
            for (int i = 0; i < beats; i++)
            {
                // 强拍（第 1 拍）用实心点、弱拍用空心点，闪烁拍同样为实心：不只靠颜色区分
                char glyph = (i == _flashBeat || i == 0) ? '●' : '○';
                sb.Append(glyph).Append(' ');
            }
            _beatDots.text = sb.ToString().TrimEnd();
            _beatDots.color = _flashBeat >= 0 ? UITheme.Accent : UITheme.TextSecondary;
        }

        private void RefreshHandHint()
        {
            if (_handHint == null || _session == null) return;
            string hint = UITheme.HandEmptyHint(_session.PracticeHand, _session.Analysis);
            _handHint.text = hint;
            _handHint.gameObject.SetActive(!string.IsNullOrEmpty(hint));

            // 该手没有音符时同时禁用对应选项，避免"点了没反应"
            if (_handSeg != null)
            {
                var analysis = _session.Analysis;
                bool hasAnalysis = analysis != null && !analysis.IsEmpty;
                _handSeg.SetOptionEnabled(1, !hasAnalysis || analysis.LeftHandCount > 0);
                _handSeg.SetOptionEnabled(2, !hasAnalysis || analysis.RightHandCount > 0);
            }
        }

        private void RefreshViewLabel(CameraMode mode)
        {
            if (_viewChipText != null)
                _viewChipText.text = "视角：" + UITheme.DescribeCameraMode(mode);

            if (_viewHintText != null)
            {
                _viewHintText.text = mode == CameraMode.Free
                    ? "自由观察：右键拖动旋转 · 滚轮缩放 · R 重置视角"
                    : "空格 播放/暂停 · [ 重播 · M 节拍器 · Tab 分析 · F1 帮助";
            }
        }

        /// <summary>主播放栏与工具栏上的只读状态标签（方案 3.4：每个状态都能直接读出）。</summary>
        private void RefreshStateChips()
        {
            if (_session == null) return;

            if (_handChip != null)
                _handChip.text = "手：" + UITheme.DescribePracticeHand(_session.PracticeHand);
            if (_speedChip != null)
                _speedChip.text = $"速 {_session.PlaybackSpeed:0.0}×";
            if (_loopChip != null)
                _loopChip.text = _session.IsLooping
                    ? (_loopB > _loopA + 0.2f ? "循环：A-B" : "循环：整曲")
                    : "循环：关";

            if (_toolbarHint != null)
            {
                string range = _session.IsLooping
                    ? (_loopB > _loopA + 0.2f ? $"A-B {UITheme.FormatTime(_loopA)}–{UITheme.FormatTime(_loopB)}" : "整曲")
                    : "关";
                _toolbarHint.text =
                    $"{UITheme.DescribePracticeHand(_session.PracticeHand)} · {_session.PlaybackSpeed:0.0}× · " +
                    $"循环 {range} · 高亮 {UITheme.DescribeHighlight(_session.KeyHighlight)} · " +
                    $"节拍器 {(_session.MetronomeEnabled ? "开" : "关")} · 视角 {UITheme.DescribeCameraMode(_session.CameraMode)}";
            }
        }

        private void RefreshAnalysisContent()
        {
            if (_analysisText == null || _suggestionText == null) return;
            var analysis = _session != null ? _session.Analysis : null;

            if (analysis == null || analysis.IsEmpty)
            {
                _analysisText.text = "尚未加载曲目，暂无可分析内容。\n可导入 MIDI 或播放示例曲后再查看。";
                _suggestionText.text = "";
                return;
            }

            _analysisText.text =
                $"音符总数 {analysis.NoteCount}　时长 {UITheme.FormatTime(analysis.TotalDuration)}\n" +
                $"左手 {analysis.LeftHandCount}（{UITheme.Percent(analysis.LeftHandRatio)}%）　" +
                $"右手 {analysis.RightHandCount}（{UITheme.Percent(analysis.RightHandRatio)}%）\n" +
                $"黑键 {analysis.BlackKeyCount}（{UITheme.Percent(analysis.BlackKeyRatio)}%）\n" +
                $"和弦 {analysis.ChordCount} 处（最大 {analysis.MaxChordSize} 音 / 跨度 {analysis.MaxSpanSemitones} 半音）\n" +
                $"音域 {PitchName(analysis.LowestPitch)} – {PitchName(analysis.HighestPitch)}" +
                $"（{analysis.OctaveRange:0.0} 个八度 / 最大跨度 {analysis.PitchRange} 半音）\n" +
                $"速度 {analysis.MinBpm:0}–{analysis.MaxBpm:0} BPM　最快音间隔 {analysis.FastestNoteGap * 1000f:0} ms";

            var sb = new StringBuilder("练习建议\n");
            for (int i = 0; i < analysis.Suggestions.Count; i++)
                sb.Append("· ").Append(analysis.Suggestions[i]).Append('\n');
            _suggestionText.text = sb.ToString();
        }

        /// <summary>动作质量指标（开发诊断区，默认折叠；方案阶段五验收量化）。</summary>
        private void RefreshMotion()
        {
            if (_motionText == null || _session == null) return;
            var report = _session.HandMotion;
            if (report == null)
            {
                _motionText.text = "动作质量：当前场景未装配手部 IK，指标不可用。";
                return;
            }
            if (report.Samples == 0)
            {
                _motionText.text = "动作质量：待采样（播放后统计）。";
                return;
            }

            string pose = _session.PoseIssueCount == 0 ? "穿模告警 0" : $"穿模告警 {_session.PoseIssueCount}";
            _motionText.text =
                $"命中率 {UITheme.Percent(report.HitRate)}%　平滑度 {report.Smoothness:0.00}　{pose}\n" +
                $"最大帧位移 {report.MaxStepDistance * 1000f:0.#} mm　" +
                $"双手协调 {UITheme.Percent(report.CoordinationRatio)}%　采样 {report.Samples}";
        }

        private static int IndexOfSpeed(float speed)
        {
            int best = 1;
            float bestDelta = float.MaxValue;
            for (int i = 0; i < SpeedOptions.Length; i++)
            {
                float delta = Mathf.Abs(SpeedOptions[i] - speed);
                if (delta < bestDelta) { bestDelta = delta; best = i; }
            }
            return best;
        }

        private static readonly string[] NoteNames =
            { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        /// <summary>MIDI 音高 → 音名（C4 = 60）。</summary>
        private static string PitchName(int pitch)
        {
            if (pitch < 0) return "-";
            return NoteNames[pitch % 12] + (pitch / 12 - 1);
        }
    }
}
