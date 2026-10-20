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
    /// 教学主界面控制层（深色专业工作台布局）。
    /// <para>
    /// 结构（三层：底 = 3D 场景 → 面 = 面板 → 浮 = chip / 按钮）：
    /// ① 顶栏（<see cref="UIStatusBar"/>：返回 / 曲名 / 状态 / 帮助 / 角色，单行 56 高）
    /// ② 左上视角提示卡（220×88 玻璃卡，鼠标移出 2.5s 后淡到 0.45，不完全隐藏）
    /// ③ 演奏区（画面中部**不放置**任何常驻面板）
    /// ④ 底栏播放器（单块 132 高：进度行 + 控制行；主按钮是全屏唯一实心强调色）
    /// ⑤ 分析面板（按需从右侧滑出）、空状态卡片（居中）
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

        // ---- 左上提示卡 ----
        private RectTransform _hintCard;
        private CanvasGroup _hintGroup;
        private Text _hintTitle;
        private Text _hintLine2;
        private Text _hintLine3;
        private float _hintIdleSince = -1f;
        private float _hintAlpha = 1f;

        // ---- 底栏（播放器面板）----
        private RectTransform _playerBar;
        private Text _timeText;
        private Text _totalText;
        private Slider _progress;
        private RectTransform _loopAMark;
        private RectTransform _loopBMark;
        private Button _playBtn;
        private Button _restartBtn;
        private UISegmentedControl _cameraSeg;

        // ---- 底栏控制行：练习工具 chips ----
        private UISegmentedControl _handSeg;
        private Button _speedChip;
        private Button _loopChip;
        private RectTransform _loopRangeGroup;
        private Button _setABtn;
        private Button _setBBtn;
        private Button _clearLoopBtn;
        private Button _highlightChip;
        private Button _metronomeChip;
        private Text _beatDots;
        private Button _displayChip;
        private Button _analysisBtn;

        // ---- 警告 / 空状态 ----
        private RectTransform _handHintChip;
        private Text _handHintText;
        private RectTransform _emptyState;

        // ---- 分析面板 ----
        private RectTransform _analysisPanel;
        private Text _analysisText;
        private Text _suggestionText;
        private Button _diagnosticsToggle;
        private RectTransform _diagnosticsBody;
        private Text _motionText;

        // ---- 运行状态（只缓存展示所需的派生值）----
        private bool _suppressSlider;
        private int _flashBeat = -1;
        private float _beatFlashUntil;
        private float _lastMotionRefresh;
        private float _loopA;
        private float _loopB;
        private float _currentTime;
        private float _totalTime;
        private bool _compact;
        private bool _lastNarrow;
        private bool _lastCompact;
        private float _lastRefWidth = -1f;
        private bool _analysisOpen;
        private float _analysisSlide;
        private bool _diagnosticsOpen;
        private float _loadingBlinkUntil;
        private int _loadingPhase;

        // ---- 底栏控制行几何（全部为 4 的倍数，组内 8 / 组间 12）----
        private const float HandWidth = 144f;
        private const float SpeedWidth = 60f;
        private const float LoopWidth = 68f;
        private const float LoopRangeWidth = 140f;
        private const float HighlightWidth = 88f;
        private const float MetronomeWidth = 80f;
        private const float BeatDotsWidth = 52f;
        private const float DisplayWidth = 84f;
        private const float AnalysisWidth = 68f;
        private const float CameraWidth = 180f;

        private static readonly float[] SpeedOptions = { 0.5f, 1f, 1.5f };
        private static readonly string[] SpeedLabels = { "0.5×", "1.0×", "1.5×" };
        private static readonly string[] HandLabels = { "双手", "左手", "右手" };
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
            UpdateHintCardAlpha();

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

            // 分析面板打开时按 4Hz 刷新动作质量（不与学习操作混排）
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
                if (Input.GetKeyDown(KeyCode.T)) _session.ToggleDisplayMode();
                if (Input.GetKeyDown(KeyCode.M)) ToggleMetronome();
                if (Input.GetKeyDown(KeyCode.F1) && _status != null) _status.ToggleHelp();
                if (UITheme.ConsumeTab()) ToggleAnalysis();
            }

            // Esc 只在没有浮层时收起本层的分析面板 / 角色面板
            if ((_status == null || !_status.HasOpenOverlay) && UITheme.ConsumeEscape()) CloseTopPanel();
        }

        /// <summary>
        /// 左上提示卡的「自动让路」：鼠标移出 2.5s 后淡到 0.45（不隐藏），移入立即恢复 1.0。
        /// 目的是让 3D 画面在不受打扰时更干净，同时保证新手随时找得到快捷键。
        /// </summary>
        private void UpdateHintCardAlpha()
        {
            if (_hintCard == null || _hintGroup == null) return;

            bool over = RectTransformUtility.RectangleContainsScreenPoint(_hintCard, Input.mousePosition, null);
            if (over) _hintIdleSince = -1f;
            else if (_hintIdleSince < 0f) _hintIdleSince = Time.unscaledTime;

            bool idle = !over && _hintIdleSince >= 0f &&
                        Time.unscaledTime - _hintIdleSince >= UITheme.Layout.HintCardFadeDelay;
            float target = idle ? UITheme.Layout.HintCardIdleAlpha : 1f;
            if (Mathf.Approximately(_hintAlpha, target)) return;

            _hintAlpha = Mathf.MoveTowards(_hintAlpha, target,
                Time.unscaledDeltaTime * UITheme.Layout.HintCardFadeSpeed);
            _hintGroup.alpha = _hintAlpha;
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

        /// <summary>兜底错误（<c>OnError</c>，string）：走轻量提示，保证不会只写进 Console。</summary>
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
            ApplyResponsiveLayout(); // 手部特写 → 底栏变矮（隐藏进度行），让出琴键与双手
        }

        private void OnPracticeHandChanged(PracticeHand hand)
        {
            if (_handSeg != null) _handSeg.SetSelectedIndex((int)hand);
            RefreshHandHint();
            RefreshStateChips();
        }

        /// <summary>
        /// 显示模式变化：同步 chip；教学模式下手部不可见、高亮被固定为「仅发声」，
        /// 因此把「高亮」chip 置灰并给出原因（避免用户以为设置失效）。
        /// </summary>
        private void OnDisplayModeChanged(DisplayMode mode)
        {
            RefreshStateChips();
            RefreshInteractable();   // 教学模式会锁死高亮 chip，必须立刻反映为禁用态
            if (_status != null)
            {
                _status.ShowToast(mode == DisplayMode.Teaching
                    ? "教学模式：已隐藏手部，只高亮被按下的琴键（左手蓝 / 右手橙）"
                    : "展示模式：手部动画已恢复");
            }
        }

        private void OnHighlightModeChanged(HighlightMode mode) => RefreshStateChips();

        private void OnMetronomeToggled(bool enabled)
        {
            if (!enabled) _flashBeat = -1;
            RefreshBeatDots();
            RefreshStateChips();
            RefreshInteractable(); // 同步 chip 文案与可用态（禁用时不会误变回可点击）
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

            // 顶栏 + 错误/加载/提示反馈（独立画布，sortingOrder 更高，永远压在底栏之上）
            _status = gameObject.AddComponent<UIStatusBar>();
            _status.Initialize(_session, BackToLibrary, ToggleAvatarPanel);

            BuildHintCard();
            BuildPlayerBar();
            BuildHandHint();
            BuildAnalysisPanel();
            BuildEmptyState();
        }

        // ---------- ② 左上视角提示卡（玻璃卡，不再把裸文字压在 3D 上）----------

        private void BuildHintCard()
        {
            const float pad = UITheme.Space12;
            float w = UITheme.Layout.HintCardWidth;
            float h = UITheme.Layout.HintCardHeight;

            _hintCard = UITheme.CreatePanel(_canvas.transform, "ViewHintCard", Vector2.zero, new Vector2(w, h),
                UITheme.PanelBgGlass, UITheme.RadiusLarge);
            var bg = _hintCard.GetComponent<Image>();
            if (bg != null)
            {
                bg.raycastTarget = false;
                UITheme.SetOutline(bg, true, UITheme.Divider, 1f);
            }
            _hintGroup = _hintCard.gameObject.AddComponent<CanvasGroup>();
            _hintGroup.blocksRaycasts = false;
            _hintGroup.interactable = false;

            float top = UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Layout.HintCardTopGap;
            UITheme.PlaceTopLeft(_hintCard, UITheme.Layout.ScreenMargin, top, w, h);

            float inner = w - pad * 2f;
            _hintTitle = UIKit.Label(_hintCard, "视角：全景", UITheme.FontBody, UITheme.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, "ViewTitle");
            UITheme.PlaceTopLeft(_hintTitle.rectTransform, pad, pad, inner, 18f);

            _hintLine2 = UIKit.Label(_hintCard, "空格 播放 / 暂停　·　[ 重播",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal, "Hint2");
            UITheme.PlaceTopLeft(_hintLine2.rectTransform, pad, pad + 22f, inner, 16f);

            _hintLine3 = UIKit.Label(_hintCard, "M 节拍器　·　Tab 分析　·　F1 帮助",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal, "Hint3");
            UITheme.PlaceTopLeft(_hintLine3.rectTransform, pad, pad + 42f, inner, 16f);
        }

        // ---------- ③ 底栏播放器面板（单块，主次分明）----------

        private void BuildPlayerBar()
        {
            _playerBar = UITheme.CreatePanel(_canvas.transform, "MainPlayBar", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, UITheme.Layout.MainBarHeightNormal),
                UITheme.PanelBg, UITheme.RadiusLarge);
            var panelBg = _playerBar.GetComponent<Image>();
            if (panelBg != null) UITheme.SetOutline(panelBg, true, UITheme.Divider, 1f);

            // ---- 进度行：当前时间 ━━━ 总时长 ----
            _timeText = UIKit.Label(_playerBar, "00:00", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.MiddleRight, FontStyle.Normal, "TimeCurrent");
            _totalText = UIKit.Label(_playerBar, "00:00", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.MiddleLeft, FontStyle.Normal, "TimeTotal");

            _progress = UIKit.ProgressBar(_playerBar, "Progress",
                UITheme.Layout.ProgressBarHeight * 2f + UITheme.Space12);
            _progress.onValueChanged.AddListener(v =>
            {
                if (!_suppressSlider) _session.SeekNormalized(v);
            });
            _loopAMark = UITheme.CreateTrackMarker(_progress, UITheme.LoopStartColor, 2f);
            _loopBMark = UITheme.CreateTrackMarker(_progress, UITheme.LoopEndColor, 2f);
            UITheme.SetTrackMarker(_loopAMark, 0f, false);
            UITheme.SetTrackMarker(_loopBMark, 0f, false);

            // ---- 控制行（左）：练习工具 chips，分三组 ----
            _handSeg = UIKit.Segmented(_playerBar, "HandSegmented", new Vector2(HandWidth, UITheme.HeightInput),
                HandLabels, (int)_session.PracticeHand, index =>
                {
                    _session.SetPracticeHand((PracticeHand)index);
                    RefreshHandHint();
                    RefreshStateChips();
                });
            _handSeg.SetOptionTooltip(0, "双手练习：左右手声部都发声");
            _handSeg.SetOptionTooltip(1, "只练左手：右手声部静音");
            _handSeg.SetOptionTooltip(2, "只练右手：左手声部静音");

            _speedChip = UIKit.Chip(_playerBar, "SpeedChip", SpeedWidth, CycleSpeed,
                "速度：点击循环切换 0.5× / 1.0× / 1.5×");

            _loopChip = UIKit.Chip(_playerBar, "LoopChip", LoopWidth, ToggleLoop,
                "循环播放：打开后播放到结尾自动回到开头，并可设置 A / B 片段");

            _loopRangeGroup = UITheme.CreateRect(_playerBar, "LoopRange", Vector2.zero, Vector2.zero,
                UITheme.Transparent);
            _setABtn = UITheme.CreateButton(_loopRangeGroup, "设 A", Vector2.zero, new Vector2(44f, 32f),
                SetLoopPointA, UITheme.FontBodySmall, ButtonStyle.Ghost, "把当前播放位置记为循环起点 A");
            _setBBtn = UITheme.CreateButton(_loopRangeGroup, "设 B", Vector2.zero, new Vector2(44f, 32f),
                SetLoopPointB, UITheme.FontBodySmall, ButtonStyle.Ghost,
                "把当前播放位置记为循环终点 B（必须晚于 A）");
            _clearLoopBtn = UITheme.CreateButton(_loopRangeGroup, "清除", Vector2.zero, new Vector2(44f, 32f),
                ClearLoopRange, UITheme.FontBodySmall, ButtonStyle.Ghost,
                "清除 A / B 标记，循环范围恢复为整曲");
            UITheme.Place(_setABtn.GetComponent<RectTransform>(), 0f, 0f, 44f, 32f);
            UITheme.Place(_setBBtn.GetComponent<RectTransform>(), 48f, 0f, 44f, 32f);
            UITheme.Place(_clearLoopBtn.GetComponent<RectTransform>(), 96f, 0f, 44f, 32f);
            UITheme.ChainHorizontal(_setABtn, _setBBtn, _clearLoopBtn);

            _highlightChip = UIKit.Chip(_playerBar, "HighlightChip", HighlightWidth, CycleHighlight,
                "琴键高亮：点击循环切换 关闭 / 预提示 / 发声 / 全部", "H");
            _metronomeChip = UIKit.Chip(_playerBar, "MetronomeChip", MetronomeWidth, ToggleMetronome,
                "节拍器开关（拍点跟随课程速度缩放）", "M");

            _beatDots = UIKit.Label(_playerBar, "", UITheme.FontBodySmall, UITheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Normal, "BeatDots");

            _displayChip = UIKit.Chip(_playerBar, "DisplayChip", DisplayWidth, () => _session.ToggleDisplayMode(),
                "展示 / 教学模式：教学模式下隐藏手部动画，只高亮被按下的琴键", "T");
            _analysisBtn = UIKit.Chip(_playerBar, "CurAnalysis", AnalysisWidth, ToggleAnalysis,
                "曲目分析：音符数、左右手比例、和弦、音域、跨度与练习建议", "Tab");
            // 保持既有对象名（PlayMode 截图用例按名字点击），同时文案给出完整语义
            _analysisBtn.gameObject.name = "Btn_曲目分析";
            _analysisBtn.name = "Btn_曲目分析";
            UITheme.SetButtonLabel(_analysisBtn, "曲目分析");

            // ---- 控制行（右）：视角分段 → 重播 → 播放 / 暂停（唯一实心强调）----
            _cameraSeg = UIKit.Segmented(_playerBar, "ViewSegmented", new Vector2(CameraWidth, UITheme.HeightInput),
                CameraLabels, (int)_session.CameraMode, index =>
                {
                    _session.SetCameraMode((CameraMode)index);
                    RefreshViewLabel((CameraMode)index);
                });
            _cameraSeg.SetOptionTooltip(0, "手部特写：优先看清琴键与手指");
            _cameraSeg.SetOptionTooltip(1, "全景：观察坐姿、双臂与完整键盘关系");
            _cameraSeg.SetOptionTooltip(2, "自由观察：右键拖动旋转、滚轮缩放，R 重置视角");

            _restartBtn = UIKit.IconButton(_playerBar, "Restart", UIIcon.Replay, UITheme.SizeIconButton,
                Restart, "重播：回到开头（不改变速度与循环设置）", "[");

            _playBtn = UIKit.PrimaryButton(_playerBar, "播放",
                new Vector2(UITheme.Layout.PrimaryActionWidth, UITheme.HeightButton),
                TogglePlay, "播放 / 暂停当前曲目", "空格");

            UITheme.ChainHorizontal(_handSeg.GetSegment(0), _handSeg.GetSegment(1), _handSeg.GetSegment(2),
                _speedChip, _loopChip, _highlightChip, _metronomeChip, _displayChip, _analysisBtn,
                _cameraSeg.GetSegment(0), _cameraSeg.GetSegment(1), _cameraSeg.GetSegment(2),
                _restartBtn, _playBtn);
        }

        /// <summary>底栏控制行布局：左侧 chips 从左往右、右侧主控从右往左，两侧不会互相挤压。</summary>
        private void LayoutPlayerRows()
        {
            if (_playerBar == null) return;

            float pad = UITheme.Layout.PlayerPadding;
            float rowH = UITheme.Layout.PlayerControlRowHeight;
            float chipH = UITheme.Layout.ChipHeight;
            float controlY = pad + Mathf.Round((rowH - chipH) * 0.5f);   // 32 高控件在 40 行里居中

            // ---- 进度行 ----
            if (_compact)
            {
                _timeText.gameObject.SetActive(false);
                _totalText.gameObject.SetActive(false);
                _progress.gameObject.SetActive(false);
            }
            else
            {
                _timeText.gameObject.SetActive(true);
                _totalText.gameObject.SetActive(true);
                _progress.gameObject.SetActive(true);

                float progressY = pad + rowH + UITheme.Layout.PlayerRowGap;
                float rowCenter = progressY + UITheme.Layout.PlayerProgressRowHeight * 0.5f;
                float sliderH = UITheme.Layout.ProgressBarHeight * 2f + UITheme.Space12;

                UITheme.Place(_timeText.rectTransform, pad, progressY,
                    UITheme.Layout.TimeLabelWidth, UITheme.Layout.PlayerProgressRowHeight);
                UITheme.PlaceRight(_totalText.rectTransform, pad, progressY,
                    UITheme.Layout.TimeLabelWidth, UITheme.Layout.PlayerProgressRowHeight);

                float gap = UITheme.Space8;
                float inset = pad + UITheme.Layout.TimeLabelWidth + gap;
                UITheme.StretchX(_progress.GetComponent<RectTransform>(), inset, inset,
                    rowCenter - sliderH * 0.5f, sliderH);
            }

            // ---- 控制行（左）：练习工具分三组 ----
            bool narrow = _lastNarrow;
            float x = pad;
            PlaceRowItem(_handSeg.Rect, ref x, HandWidth, UITheme.Layout.ChipGapInner, controlY, chipH);
            PlaceRowItem(_speedChip.GetComponent<RectTransform>(), ref x, SpeedWidth,
                UITheme.Layout.ChipGapGroup, controlY, chipH);

            PlaceRowItem(_loopChip.GetComponent<RectTransform>(), ref x, LoopWidth,
                UITheme.Layout.ChipGapInner, controlY, chipH);
            if (_loopRangeGroup.gameObject.activeSelf)
            {
                UITheme.Place(_loopRangeGroup, x, controlY, LoopRangeWidth, chipH);
                x += LoopRangeWidth + UITheme.Layout.ChipGapInner;
            }

            PlaceRowItem(_highlightChip.GetComponent<RectTransform>(), ref x, HighlightWidth,
                UITheme.Layout.ChipGapInner, controlY, chipH);
            PlaceRowItem(_metronomeChip.GetComponent<RectTransform>(), ref x, MetronomeWidth,
                UITheme.Layout.ChipGapInner, controlY, chipH);
            if (_beatDots.gameObject.activeSelf)
                PlaceRowItem(_beatDots.rectTransform, ref x, BeatDotsWidth, UITheme.Layout.ChipGapInner,
                    controlY, chipH);

            if (!narrow)
            {
                PlaceRowItem(_displayChip.GetComponent<RectTransform>(), ref x, DisplayWidth,
                    UITheme.Layout.ChipGapGroup, controlY, chipH);
                PlaceRowItem(_analysisBtn.GetComponent<RectTransform>(), ref x, AnalysisWidth,
                    0f, controlY, chipH);
            }

            // ---- 控制行（右）：主按钮 → 重播 → 视角分段 ----
            float rightY = pad + Mathf.Round((rowH - UITheme.HeightButton) * 0.5f);
            float rx = pad;
            UITheme.PlaceRight(_playBtn.GetComponent<RectTransform>(), rx, rightY,
                UITheme.Layout.PrimaryActionWidth, UITheme.HeightButton);
            rx += UITheme.Layout.PrimaryActionWidth + UITheme.Layout.ChipGapGroup;

            UITheme.PlaceRight(_restartBtn.GetComponent<RectTransform>(), rx,
                pad + Mathf.Round((rowH - UITheme.SizeIconButton) * 0.5f),
                UITheme.SizeIconButton, UITheme.SizeIconButton);
            rx += UITheme.SizeIconButton + UITheme.Layout.ChipGapGroup;

            UITheme.PlaceRight(_cameraSeg.Rect, rx, controlY, CameraWidth, chipH);
        }

        private static void PlaceRowItem(RectTransform rt, ref float x, float width, float gapAfter,
            float y, float height)
        {
            UITheme.Place(rt, x, y, width, height);
            x += width + gapAfter;
        }

        // ---------- 就近警告：某只手在本曲没有音符 ----------

        private void BuildHandHint()
        {
            _handHintChip = UITheme.CreatePanel(_canvas.transform, "HandHintChip", Vector2.zero,
                new Vector2(UITheme.Layout.HintCardWidth, 28f),
                UITheme.ErrorBannerColor(ErrorSeverity.Warning), UITheme.RadiusSmall);
            var bg = _handHintChip.GetComponent<Image>();
            if (bg != null) bg.raycastTarget = false;

            _handHintText = UIKit.Label(_handHintChip, "", UITheme.FontBodySmall, UITheme.TextWarning,
                TextAnchor.MiddleLeft, FontStyle.Normal, "HandHintText");
            UITheme.StretchAll(_handHintText.rectTransform, UITheme.Space8, UITheme.Space8, 0f, 0f);
            _handHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _handHintText.verticalOverflow = VerticalWrapMode.Truncate;

            _handHintChip.gameObject.SetActive(false);
        }

        // ---------- ⑤ 分析面板（右侧滑出，动作质量默认折叠）----------

        private void BuildAnalysisPanel()
        {
            const float pad = UITheme.Space16;
            _analysisPanel = UITheme.CreatePanel(_canvas.transform, "AnalysisPanel", Vector2.zero,
                new Vector2(UITheme.Layout.AnalysisPanelWidth, 420f), UITheme.PanelBg, UITheme.RadiusLarge);

            var title = UIKit.Label(_analysisPanel, "曲目分析", UITheme.FontSubtitle, UITheme.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, "AnalysisTitle");
            UITheme.PlaceTopLeft(title.rectTransform, pad, UITheme.Space12, 240f, 24f);

            var closeBtn = UITheme.CreateButton(_analysisPanel, "收起", Vector2.zero, new Vector2(56f, 24f),
                () => ShowAnalysis(false), UITheme.FontBodySmall, ButtonStyle.Ghost, "收起分析面板");
            UITheme.PlaceTopRight(closeBtn.GetComponent<RectTransform>(), pad, UITheme.Space12, 56f, 24f);

            var divider = UITheme.CreateDivider(_analysisPanel, "Divider", Vector2.zero, new Vector2(360f, 1f));
            UITheme.StretchTop(divider, pad, pad, 44f, 1f);

            float inner = UITheme.Layout.AnalysisPanelWidth - pad * 2f;

            _analysisText = UIKit.Label(_analysisPanel, "", UITheme.FontBodySmall, UITheme.TextPrimary,
                TextAnchor.UpperLeft, FontStyle.Normal, "AnalysisBody");
            _analysisText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _analysisText.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(_analysisText.rectTransform, pad, 56f, inner, 150f);

            _suggestionText = UIKit.Label(_analysisPanel, "", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.UpperLeft, FontStyle.Normal, "AnalysisSuggestion");
            _suggestionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _suggestionText.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(_suggestionText.rectTransform, pad, 214f, inner, 88f);

            var divider2 = UITheme.CreateDivider(_analysisPanel, "Divider2", Vector2.zero, new Vector2(360f, 1f));
            UITheme.StretchTop(divider2, pad, pad, 308f, 1f);

            _diagnosticsToggle = UITheme.CreateToggleButton(_analysisPanel, "开发诊断", Vector2.zero,
                new Vector2(120f, 28f), false, ToggleDiagnostics,
                "动作质量指标（命中率 / 最大帧位移 / 平滑度 / 双手协调）：开发与验收用，默认折叠",
                null, UITheme.FontBodySmall);
            UITheme.PlaceTopLeft(_diagnosticsToggle.GetComponent<RectTransform>(), pad, 316f, 120f, 28f);

            _diagnosticsBody = UITheme.CreateRect(_analysisPanel, "DiagnosticsBody", Vector2.zero,
                Vector2.zero, UITheme.Transparent);
            UITheme.PlaceTopLeft(_diagnosticsBody, pad, 350f, inner, 56f);

            _motionText = UIKit.Label(_diagnosticsBody, "", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.UpperLeft, FontStyle.Normal, "MotionBody");
            _motionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _motionText.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.StretchAll(_motionText.rectTransform, 0f, 0f, 0f, 0f);

            _diagnosticsBody.gameObject.SetActive(false);
            _analysisPanel.gameObject.SetActive(false);
            _analysisSlide = 0f;
        }

        // ---------- 空状态（居中卡片：图标 + 说明 + 行动按钮）----------

        private void BuildEmptyState()
        {
            const float w = 480f;
            const float h = 208f;

            _emptyState = UITheme.CreatePanel(_canvas.transform, "EmptyState", Vector2.zero,
                new Vector2(w, h), UITheme.PanelBgRaised, UITheme.RadiusLarge);
            UITheme.PlaceCenter(_emptyState, w, h);

            // 图标：48 圆底 + 音符字形（纯装饰，不承担语义）
            var icon = UITheme.CreatePanel(_emptyState, "Icon", Vector2.zero, new Vector2(48f, 48f),
                UITheme.PanelBgDeep, 24);
            UITheme.PlaceTopLeft(icon, (w - 48f) * 0.5f, UITheme.Space24, 48f, 48f);
            var iconImg = icon.GetComponent<Image>();
            if (iconImg != null) iconImg.raycastTarget = false;
            var glyph = UIKit.Label(icon, "♪", UITheme.FontTitle, UITheme.Accent, TextAnchor.MiddleCenter,
                FontStyle.Bold, "Glyph");
            UITheme.StretchAll(glyph.rectTransform, 0f, 0f, 0f, 0f);

            var title = UIKit.Label(_emptyState, "还没有可练习的曲目", UITheme.FontSubtitle,
                UITheme.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold, "EmptyTitle");
            UITheme.PlaceTopLeft(title.rectTransform, UITheme.Space24, UITheme.Space24 + 48f + UITheme.Space8,
                w - UITheme.Space24 * 2f, 20f);

            var body = UIKit.Label(_emptyState,
                "导入本地 .mid / .midi 即可开始练习，或先播放内置示例曲熟悉界面与视角。",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.UpperCenter, FontStyle.Normal,
                "EmptyBody");
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(body.rectTransform, UITheme.Space24, 108f, w - UITheme.Space24 * 2f, 34f);

            var importBtn = UIKit.PrimaryButton(_emptyState, "导入 MIDI…", new Vector2(148f, 40f),
                () => _session.ImportMidi(), "打开文件选择器导入本地 MIDI 文件");
            UITheme.Place(importBtn.GetComponent<RectTransform>(), UITheme.Space24, UITheme.Space16, 148f, 40f);

            var sampleBtn = UIKit.SecondaryButton(_emptyState, "播放示例曲", new Vector2(148f, 40f),
                PlaySampleSong, "载入 StreamingAssets/Songs 中的第一首示例曲");
            UITheme.Place(sampleBtn.GetComponent<RectTransform>(), UITheme.Space24 + 148f + UITheme.Space12,
                UITheme.Space16, 148f, 40f);

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

        private void CycleSpeed()
        {
            if (_session.IsLoading) return;
            int index = IndexOfSpeed(_session.PlaybackSpeed);
            int next = (index + 1) % SpeedOptions.Length;
            _session.SetSpeed(SpeedOptions[next]);
            RefreshStateChips();
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
                    _status.ShowToast($"已记录起点 A = {UITheme.FormatTime(_loopA)}。请把进度拖到片段结束处，再点「设 B」。");
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
            RefreshStateChips();
        }

        private void CycleHighlight()
        {
            _session.CycleHighlightMode();
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
            if (_analysisBtn != null) UIKit.SetChip(_analysisBtn, "曲目分析", "", show, true);
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
                _session.LoadSongAsync(files[0]);   // 走可取消的分阶段加载
            }
            catch (System.Exception ex)
            {
                if (_status != null) _status.ShowToast("读取示例曲目录失败，请用「导入 MIDI…」选择本地文件。",
                    ErrorSeverity.Warning);
                Debug.LogWarning($"[PianoTeacher3D][UI] 读取示例曲目录失败：{ex.GetType().Name}");
            }
        }

        // ==================== 响应式布局 ====================

        private void ApplyResponsiveLayoutIfNeeded()
        {
            float refW = UITheme.GetReferenceWidth(_canvas);
            bool closeup = _session.CameraMode == CameraMode.Closeup;
            if (Mathf.Abs(refW - _lastRefWidth) < 0.5f && closeup == _lastCompact) return;
            ApplyResponsiveLayout();
        }

        /// <summary>
        /// 参考分辨率（1280×720）下的自适应：
        /// 宽窗口显示全部 chip；窄窗口按优先级收起「显示模式 → 曲目分析」，不缩小主按钮、不折行；
        /// 手部特写隐藏进度行（底栏 132 → 72），把琴键与双手让出来。
        /// </summary>
        private void ApplyResponsiveLayout()
        {
            float refW = UITheme.GetReferenceWidth(_canvas);
            bool narrow = UITheme.IsNarrowReferenceWidth(refW);

            _lastRefWidth = refW;
            _lastNarrow = narrow;
            _lastCompact = _session != null && _session.CameraMode == CameraMode.Closeup;
            _compact = _lastCompact;

            float margin = UITheme.Layout.ScreenMargin;
            float barH = _compact ? UITheme.Layout.MainBarHeightCompact : UITheme.Layout.MainBarHeightNormal;

            UITheme.StretchX(_playerBar, margin, margin, margin, barH);

            // 窄窗口：低频控件先让位（进度行与主按钮始终保留）
            if (_displayChip != null) _displayChip.gameObject.SetActive(!narrow);
            if (_analysisBtn != null) _analysisBtn.gameObject.SetActive(!narrow);

            LayoutPlayerRows();
            LayoutHandHint();
            LayoutAnalysisPanel();

            // 手部特写时提示卡让位给画面（快捷键仍可从帮助面板 F1 查到）
            if (_hintCard != null) _hintCard.gameObject.SetActive(!_compact);

            if (_status != null) _status.ApplyResponsiveLayout(refW);
            RefreshSongAndState();
        }

        /// <summary>就近警告（某只手没有音符）贴在底栏左上角，不压演奏区中心。</summary>
        private void LayoutHandHint()
        {
            if (_handHintChip == null) return;
            float margin = UITheme.Layout.ScreenMargin;
            float barH = _compact ? UITheme.Layout.MainBarHeightCompact : UITheme.Layout.MainBarHeightNormal;
            UITheme.Place(_handHintChip, margin, margin + barH + UITheme.Space8,
                UITheme.Layout.HintCardWidth, 28f);
        }

        private void LayoutAnalysisPanel()
        {
            if (_analysisPanel == null) return;
            float margin = UITheme.Layout.ScreenMargin;
            float barH = _compact ? UITheme.Layout.MainBarHeightCompact : UITheme.Layout.MainBarHeightNormal;
            float top = margin + UITheme.Layout.StatusBarHeight + UITheme.Space8;
            float bottom = margin + barH + UITheme.Space8;
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

            if (_status != null)
                _status.SetSong(_session.CurrentSongName, hasSong, state, _session.PlaybackState);

            // 空状态只在「确实没有可播放曲目」时出现（出错且有旧曲目时保留旧曲目）
            bool showEmpty = !hasSong && (state == LessonState.NoSong || state == LessonState.Error);
            if (_emptyState != null && _emptyState.gameObject.activeSelf != showEmpty)
                _emptyState.gameObject.SetActive(showEmpty);
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

            var label = UITheme.GetLabel(_playBtn);
            if (label != null) label.color = UITheme.TextOnAccent;
        }

        /// <summary>需要曲目的控件在无曲目 / 加载中统一呈禁用态并说明原因（防重复点击）。</summary>
        private void RefreshInteractable()
        {
            if (_session == null) return;
            bool canAct = UITheme.CanTriggerSongActions(_session.State, _session.HasSong);
            bool loading = _session.IsLoading;

            RefreshPlayButton(_session.PlaybackState);

            bool restartOk = canAct && !loading;
            SetRestartEnabled(restartOk);

            if (_handSeg != null) _handSeg.Interactable = !loading && _session.HasSong;
            if (_cameraSeg != null) _cameraSeg.Interactable = true; // 视角随时可切，不改变时间轴

            bool teaching = _session.DisplayMode == DisplayMode.Teaching;
            if (_highlightChip != null)
            {
                bool ok = !loading && canAct && !teaching;
                if (!teaching)
                {
                    UITheme.SetButtonTooltip(_highlightChip,
                        UITheme.ComposeTooltip("琴键高亮：点击循环切换 关闭 / 预提示 / 发声 / 全部", "H"));
                }
                else
                {
                    UITheme.SetButtonTooltip(_highlightChip,
                        "教学模式固定为「仅高亮被按下的琴键」；切回展示模式后恢复你的选择");
                }
                UIKit.SetChip(_highlightChip, "高亮", UITheme.DescribeHighlight(_session.KeyHighlight),
                    _session.KeyHighlight != HighlightMode.Off, ok);
            }

            if (_metronomeChip != null)
            {
                bool metronomeOk = !loading && (canAct || _session.MetronomeEnabled);
                UIKit.SetChip(_metronomeChip, "节拍器", _session.MetronomeEnabled ? "开" : "关",
                    _session.MetronomeEnabled, metronomeOk);
            }

            if (_displayChip != null)
                UIKit.SetChip(_displayChip, "显示", teaching ? "教学" : "展示", teaching, !loading);

            if (_loopChip != null)
                UIKit.SetChip(_loopChip, "循环",
                    _session.IsLooping ? (_loopB > _loopA + 0.2f ? "A-B" : "整曲") : "关",
                    _session.IsLooping, canAct && !loading);

            if (_setABtn != null)
                UITheme.SetButtonEnabled(_setABtn, canAct, "不可用：先加载曲目并打开循环", ButtonStyle.Ghost);
            if (_setBBtn != null)
                UITheme.SetButtonEnabled(_setBBtn, canAct, "不可用：先加载曲目并打开循环", ButtonStyle.Ghost);
            if (_clearLoopBtn != null)
                UITheme.SetButtonEnabled(_clearLoopBtn, canAct, "不可用：先加载曲目", ButtonStyle.Ghost);

            // 进度条本身在无曲目时不可拖动
            if (_progress != null) _progress.interactable = canAct;
        }

        private void RefreshLoopControls()
        {
            if (_session == null) return;
            bool looping = _session.IsLooping;

            bool showRange = looping && !_session.IsLoading;
            if (_loopRangeGroup != null && _loopRangeGroup.gameObject.activeSelf != showRange)
            {
                _loopRangeGroup.gameObject.SetActive(showRange);
                LayoutPlayerRows();   // A/B 组显隐会改变后续 chip 的横坐标
            }

            RefreshLoopMarks();
            RefreshStateChips();
            RefreshInteractable();
        }

        /// <summary>重播按钮是纯图标按钮：禁用时同时把图标压暗（无文字可改）。</summary>
        private void SetRestartEnabled(bool ok)
        {
            if (_restartBtn == null) return;
            _restartBtn.interactable = ok;
            var icon = _restartBtn.transform.Find("Icon");
            if (icon != null) UIKit.SetIconColor(icon, ok ? UITheme.TextPrimary : UITheme.TextDisabled);
            UITheme.SetButtonTooltip(_restartBtn, ok
                ? UITheme.ComposeTooltip("重播：回到开头（不改变速度与循环设置）", "[")
                : "不可用：先加载曲目后再重播");
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
                if (_beatDots.gameObject.activeSelf) _beatDots.gameObject.SetActive(false);
                LayoutPlayerRows();
                return;
            }

            if (!_beatDots.gameObject.activeSelf)
            {
                _beatDots.gameObject.SetActive(true);
                LayoutPlayerRows();
            }

            int beats = Mathf.Max(1, Mathf.Min(4, _session.BeatsPerBar));
            var sb = new StringBuilder(beats * 2);
            for (int i = 0; i < beats; i++)
            {
                // 强拍（第 1 拍）实心、弱拍空心；闪烁拍同样实心：不只靠颜色区分
                char glyph = (i == _flashBeat || i == 0) ? '●' : '○';
                sb.Append(glyph);
            }
            _beatDots.text = sb.ToString();
            _beatDots.color = _flashBeat >= 0 ? UITheme.AccentBright : UITheme.TextSecondary;
        }

        private void RefreshHandHint()
        {
            if (_handHintText == null || _session == null) return;
            string hint = UITheme.HandEmptyHint(_session.PracticeHand, _session.Analysis);
            bool show = !string.IsNullOrEmpty(hint);
            _handHintText.text = hint;
            if (_handHintChip.gameObject.activeSelf != show) _handHintChip.gameObject.SetActive(show);

            // 该手没有音符时同时禁用对应选项，避免「点了没反应」
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
            if (_hintTitle != null)
                _hintTitle.text = "视角：" + UITheme.DescribeCameraMode(mode);

            bool free = mode == CameraMode.Free;
            if (_hintLine2 != null)
                _hintLine2.text = free ? "右键拖动旋转　·　滚轮缩放" : "空格 播放 / 暂停　·　[ 重播";
            if (_hintLine3 != null)
                _hintLine3.text = free ? "R 重置视角　·　Tab 分析　·　F1 帮助" : "M 节拍器　·　Tab 分析　·　F1 帮助";
        }

        /// <summary>底栏与提示卡上的只读状态（每个状态都能直接读出，不依赖悬停）。</summary>
        private void RefreshStateChips()
        {
            if (_session == null) return;

            if (_speedChip != null)
                UIKit.SetChip(_speedChip, "速度", $"{_session.PlaybackSpeed:0.0}×", true, !_session.IsLoading);

            if (_analysisBtn != null)
                UIKit.SetChip(_analysisBtn, "曲目分析", "", _analysisOpen, true);

            RefreshViewLabel(_session.CameraMode);
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

        /// <summary>动作质量指标（开发诊断区，默认折叠）。</summary>
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
