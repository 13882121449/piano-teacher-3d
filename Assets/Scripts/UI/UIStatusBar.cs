using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 顶部状态栏 + 错误 / 加载 / 提示反馈 + 浮层容器（对应方案 3.1 顶栏、
    /// 方案 8.3「错误提示结构 = 发生了什么 + 为什么可能发生 + 下一步做什么」、
    /// 审查 2.2C「错误提示应提供恢复动作」）。
    /// <para>
    /// 顶栏结构：返回曲库 ｜ 曲库 ｜ 曲名 · 状态 ｜ 保存状态 · 帮助 ｜ 当前人物。
    /// 反馈按「顶部堆叠」排列：错误横幅（含恢复按钮）→ 加载条（含取消）→ 轻量提示。
    /// </para>
    /// <para>
    /// 破坏性操作（返回曲库 / 返回主菜单）一律经过 <see cref="ShowConfirm"/> 二次确认
    /// （方案 3.4「提供适量确认」）。
    /// </para>
    /// </summary>
    public class UIStatusBar : MonoBehaviour
    {
        private Core.LessonSessionController _session;
        private Action _onBackToLibrary;
        private Action _onAvatarToggle;
        private Canvas _canvas;

        // ---- 顶栏 ----
        private RectTransform _bar;
        private RectTransform _midArea;
        private Text _songText;
        private RectTransform _stateChip;
        private Text _stateChipText;
        private Button _libraryBtn;
        private Button _helpBtn;
        private Button _avatarBtn;
        private Text _saveHint;

        // ---- 顶部堆叠 ----
        private RectTransform _errorBanner;
        private Image _errorBannerBg;
        private Text _errorTag;
        private Text _errorBody;
        private Button _errorActionBtn;
        private Button _errorClose;
        private Core.AppError _lastError;

        private RectTransform _loadingStrip;
        private Text _loadingText;
        private RectTransform _loadingPulse;

        private RectTransform _toast;
        private Text _toastText;
        private float _toastUntil;

        // ---- 浮层 ----
        private RectTransform _libraryPanel;
        private RectTransform _libraryList;
        private Text _libraryEmptyText;
        private RectTransform _helpPanel;
        private RectTransform _confirmPanel;
        private Text _confirmTitle;
        private Text _confirmBody;
        private Button _confirmOkBtn;
        private Button _confirmCancelBtn;
        private Action _confirmAction;

        private float _animTime;

        /// <summary>帮助浮层是否可见。</summary>
        public bool IsHelpOpen => _helpPanel != null && _helpPanel.gameObject.activeSelf;

        /// <summary>曲库浮层是否可见。</summary>
        public bool IsLibraryOpen => _libraryPanel != null && _libraryPanel.gameObject.activeSelf;

        /// <summary>确认弹窗是否可见。</summary>
        public bool IsConfirmOpen => _confirmPanel != null && _confirmPanel.gameObject.activeSelf;

        /// <summary>是否有任何浮层打开（快捷键 / Esc 需要让位）。</summary>
        public bool HasOpenOverlay => IsHelpOpen || IsLibraryOpen || IsConfirmOpen;

        /// <summary>
        /// 初始化顶栏。<paramref name="onBackToLibrary"/> = 确认后的返回动作；
        /// <paramref name="onAvatarToggle"/> = 点击「当前人物」时切换角色面板。
        /// </summary>
        public void Initialize(Core.LessonSessionController session, Action onBackToLibrary, Action onAvatarToggle)
        {
            _session = session;
            _onBackToLibrary = onBackToLibrary;
            _onAvatarToggle = onAvatarToggle;
            BuildUI();
        }

        private void Update()
        {
            _animTime += Time.unscaledDeltaTime;

            // Esc：先关确认弹窗，再关其它浮层（同一帧只消费一次按键；无浮层时不消费，留给主控制层）
            if (HasOpenOverlay && UITheme.ConsumeEscape()) CloseTopOverlay();

            if (_loadingStrip != null && _loadingStrip.gameObject.activeSelf) UpdateLoadingPulse();
            if (_toast != null && _toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil) HideToast();
        }

        // ==================== 对外状态接口 ====================

        /// <summary>刷新曲名 / 会话状态 / 播放状态（三者一起决定顶栏文案）。</summary>
        public void SetSong(string songName, bool hasSong, Core.LessonState state, Core.PlaybackState playback)
        {
            if (_songText != null)
            {
                string name = string.IsNullOrEmpty(songName) ? (hasSong ? "未命名曲目" : "未加载曲目") : songName;
                _songText.text = "♪ " + UITheme.Truncate(name, UITheme.Layout.TopBarSongNameMaxChars);
                _songText.color = hasSong ? UITheme.TextPrimary : UITheme.TextSecondary;
            }

            if (_stateChipText != null)
            {
                string glyph = state == Core.LessonState.Loading
                    ? UITheme.StateGlyph(state)
                    : UITheme.StateGlyph(playback);
                string text = state == Core.LessonState.Loading
                    ? UITheme.DescribeLessonState(state)
                    : (state == Core.LessonState.NoSong
                        ? UITheme.DescribeLessonState(state)
                        : UITheme.DescribePlayback(playback));
                _stateChipText.text = $"{glyph} {text}";
                _stateChipText.color = state == Core.LessonState.Error ? UITheme.TextError
                    : (state == Core.LessonState.NoSong ? UITheme.TextSecondary : UITheme.TextPrimary);
            }

            if (_stateChip != null)
            {
                var chipBg = _stateChip.GetComponent<Image>();
                if (chipBg != null)
                {
                    chipBg.color = state == Core.LessonState.Error
                        ? UITheme.ErrorBannerColor(Core.ErrorSeverity.Recoverable)
                        : UITheme.PanelBgRaised;
                }
            }
        }

        /// <summary>刷新顶栏「当前人物」。</summary>
        public void SetAvatarName(string avatarName)
        {
            if (_avatarBtn == null) return;
            UITheme.SetButtonLabel(_avatarBtn, $"人物：{UITheme.Truncate(avatarName, 6)}");
        }

        /// <summary>刷新顶栏「保存状态」位置（本项目偏好仅在本次会话内生效，据实显示，不谎称已持久化）。</summary>
        public void SetSaveHint(string text)
        {
            if (_saveHint == null) return;
            _saveHint.text = string.IsNullOrEmpty(text) ? "" : text;
        }

        /// <summary>
        /// 顶栏自适应（方案 3.4 末条 / 15 章窗口缩放）：
        /// 窄窗口或窄参考宽度下隐藏「保存状态」这类次要信息，把宽度让给曲名与状态标签，
        /// 而不是压缩按钮尺寸。
        /// </summary>
        public void ApplyResponsiveLayout(float referenceWidth)
        {
            bool showSaveHint = referenceWidth >= UITheme.Layout.WideWidthThreshold;
            if (_saveHint != null)
            {
                if (_saveHint.gameObject.activeSelf != showSaveHint)
                    _saveHint.gameObject.SetActive(showSaveHint);
            }
            // 右侧簇固定由 帮助 + 当前人物 构成；保存状态隐藏后中部曲名区自然变宽
            if (_midArea != null) UITheme.StretchX(_midArea, 216f, 288f, 8f, 28f);
        }

        /// <summary>加载条开关（方案 6.2 Loading 状态：可取消、防重复点击）。</summary>
        public void SetLoading(bool loading, string songName)
        {
            if (_loadingStrip == null) return;
            if (loading)
            {
                if (_loadingText != null)
                    _loadingText.text = string.IsNullOrEmpty(songName)
                        ? "正在解析 MIDI 曲目…"
                        : $"正在解析《{UITheme.Truncate(songName, 18)}》…";
            }
            if (_loadingStrip.gameObject.activeSelf != loading) _loadingStrip.gameObject.SetActive(loading);
            if (loading) DismissError();
            LayoutTopStack();
        }

        /// <summary>
        /// 错误横幅（方案 8.3 / 8.1）：显示「发生了什么 + 为什么 + 下一步」，
        /// 恢复按钮文案取 <c>LastError.RecoveryLabel</c>，点击调用 <c>HandleRecovery()</c>。
        /// </summary>
        public void ShowError(Core.AppError error)
        {
            if (_errorBanner == null) return;
            _lastError = error;

            var severity = error.Severity;
            if (_errorTag != null)
            {
                _errorTag.text = $"{UITheme.ErrorSeverityGlyph(severity)} {UITheme.ErrorSeverityLabel(severity)}";
                _errorTag.color = UITheme.ErrorTextColor(severity);
            }
            if (_errorBannerBg != null) _errorBannerBg.color = UITheme.ErrorBannerColor(severity);
            if (_errorBody != null)
            {
                _errorBody.text = UITheme.ComposeErrorBody(error);
                _errorBody.color = UITheme.TextPrimary;
            }

            // Recovery == None 时按钮降级为「知道了」（关闭横幅），不隐藏，避免用户只看到红条不知怎么办
            UITheme.SetButtonLabel(_errorActionBtn, UITheme.ComposeErrorActionLabel(error));
            UITheme.SetButtonLabel(_errorClose, error.Recovery == Core.RecoveryAction.None ? "关闭" : "忽略");

            if (!_errorBanner.gameObject.activeSelf) _errorBanner.gameObject.SetActive(true);
            LayoutTopStack();

            // 诊断信息只写日志，不进 UI（方案 8.3「不要把堆栈信息直接显示给普通用户」）
            Debug.LogWarning($"[PianoTeacher3D][UI] {error.ToLogString()}");
        }

        /// <summary>收起错误横幅。</summary>
        public void DismissError()
        {
            if (_errorBanner == null) return;
            if (!_errorBanner.gameObject.activeSelf) return;
            _errorBanner.gameObject.SetActive(false);
            LayoutTopStack();
        }

        /// <summary>
        /// 轻量提示（toast）：用于 <c>OnError</c>（string）兜底与操作反馈，
        /// 保证任何用户可见错误都不会只写进 Console（方案 8.3）。
        /// </summary>
        public void ShowToast(string message, Core.ErrorSeverity severity = Core.ErrorSeverity.Info)
        {
            if (_toast == null || string.IsNullOrEmpty(message)) return;
            if (_toastText != null)
            {
                _toastText.text = $"{UITheme.ErrorSeverityGlyph(severity)} {UITheme.Truncate(message, 60)}";
                _toastText.color = UITheme.ErrorTextColor(severity);
            }
            var toastBg = _toast.GetComponent<Image>();
            if (toastBg != null) toastBg.color = UITheme.ErrorBannerColor(severity);
            _toastUntil = Time.unscaledTime + UITheme.ToastSeconds;
            if (!_toast.gameObject.activeSelf) _toast.gameObject.SetActive(true);
            LayoutTopStack();
            Debug.Log($"[PianoTeacher3D][UI] {message}");
        }

        private void HideToast()
        {
            if (_toast == null) return;
            _toast.gameObject.SetActive(false);
            LayoutTopStack();
        }

        // ==================== 浮层 ====================

        /// <summary>切换帮助浮层（F1 / 顶栏「帮助」）。</summary>
        public void ToggleHelp() => ShowHelp(!IsHelpOpen);

        /// <summary>显示 / 隐藏帮助浮层。</summary>
        public void ShowHelp(bool show)
        {
            if (_helpPanel == null) return;
            if (show) UITooltip.Hide();
            _helpPanel.gameObject.SetActive(show);
        }

        /// <summary>显示 / 隐藏曲库（最近曲目）浮层。</summary>
        public void ShowLibrary(bool show)
        {
            if (_libraryPanel == null) return;
            if (show)
            {
                UITooltip.Hide();
                RefreshLibraryList();
            }
            _libraryPanel.gameObject.SetActive(show);
        }

        /// <summary>关闭最上层浮层；返回是否关闭了内容。</summary>
        public bool CloseTopOverlay()
        {
            if (IsConfirmOpen) { CloseConfirm(); return true; }
            if (IsLibraryOpen) { ShowLibrary(false); return true; }
            if (IsHelpOpen) { ShowHelp(false); return true; }
            return false;
        }

        /// <summary>
        /// 破坏性操作二次确认（方案 3.4：停止 / 切歌 / 覆盖外观等需要确认）。
        /// <paramref name="onConfirm"/> 在用户点击确认按钮时执行。
        /// </summary>
        public void ShowConfirm(string title, string body, string confirmLabel, Action onConfirm,
            string cancelLabel = "取消")
        {
            if (_confirmPanel == null) return;
            _confirmAction = onConfirm;
            if (_confirmTitle != null) _confirmTitle.text = title;
            if (_confirmBody != null) _confirmBody.text = body;
            UITheme.SetButtonLabel(_confirmOkBtn, confirmLabel);
            UITheme.SetButtonLabel(_confirmCancelBtn, cancelLabel);
            UITooltip.Hide();
            _confirmPanel.gameObject.SetActive(true);
        }

        /// <summary>关闭确认弹窗（不执行动作）。</summary>
        public void CloseConfirm()
        {
            if (_confirmPanel == null) return;
            _confirmAction = null;
            _confirmPanel.gameObject.SetActive(false);
        }

        private void ExecuteConfirm()
        {
            var action = _confirmAction;
            CloseConfirm();
            action?.Invoke();
        }

        // ==================== 界面构建 ====================

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            _canvas = UITheme.CreateCanvas(transform, "StatusAndFeedbackCanvas", UITheme.Layout.StatusCanvasOrder);

            BuildTopBar();
            BuildErrorBanner();
            BuildLoadingStrip();
            BuildToast();
            BuildLibraryPanel();
            BuildHelpPanel();
            BuildConfirmPanel();

            LayoutTopStack();
        }

        private void BuildTopBar()
        {
            _bar = UITheme.CreatePanel(_canvas.transform, "StatusBar", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, UITheme.Layout.StatusBarHeight),
                UITheme.PanelBgDeep, UITheme.RadiusMedium);
            UITheme.StretchTop(_bar, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin, UITheme.Layout.StatusBarHeight);

            var backBtn = UITheme.CreateButton(_bar, "← 返回曲库", Vector2.zero,
                new Vector2(108f, UITheme.HeightSmall),
                () => ShowConfirm("返回曲库？",
                    "将停止当前播放并返回主菜单，当前曲目的播放进度与循环标记不会保留。",
                    "返回主菜单", () => _onBackToLibrary?.Invoke()),
                UITheme.FontCaption, ButtonStyle.Ghost,
                "返回曲库：停止播放并回到主菜单（需确认）");
            UITheme.Place(backBtn.GetComponent<RectTransform>(), 8f, 6f, 108f, UITheme.HeightSmall);

            _libraryBtn = UITheme.CreateButton(_bar, "曲库 ▾", Vector2.zero, new Vector2(84f, UITheme.HeightSmall),
                () => ShowLibrary(!IsLibraryOpen), UITheme.FontCaption, ButtonStyle.Ghost,
                "打开曲库：最近曲目、导入 MIDI、返回主菜单");
            UITheme.Place(_libraryBtn.GetComponent<RectTransform>(), 124f, 6f, 84f, UITheme.HeightSmall);

            // 中部：曲名 + 状态标签
            _midArea = UITheme.CreateRect(_bar, "MidArea", Vector2.zero, Vector2.zero, UITheme.Transparent);
            UITheme.StretchX(_midArea, 216f, 288f, 8f, 28f);

            _songText = UITheme.CreateText(_midArea, "♪ 加载中…", Vector2.zero, new Vector2(300f, 28f),
                UITheme.FontBody, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            UITheme.StretchAll(_songText.rectTransform, 0f, 164f, 0f, 0f);

            _stateChip = UITheme.CreatePanel(_midArea, "StateChip", Vector2.zero, new Vector2(156f, 26f),
                UITheme.PanelBgRaised, UITheme.RadiusSmall);
            UITheme.PlaceRight(_stateChip, 0f, 1f, 156f, 26f);
            _stateChipText = UITheme.CreateText(_stateChip, "— 未加载曲目", Vector2.zero, new Vector2(148f, 24f),
                UITheme.FontStatusLabel, UITheme.TextSecondary);
            UITheme.StretchAll(_stateChipText.rectTransform, 6f, 6f, 0f, 0f);

            // 保存状态 + 帮助 + 当前人物
            _saveHint = UITheme.CreateText(_bar, "偏好：本次会话生效", Vector2.zero, new Vector2(152f, 28f),
                UITheme.FontStatusLabel, UITheme.TextSecondary, TextAnchor.MiddleRight);
            UITheme.PlaceRight(_saveHint.rectTransform, 268f, 8f, 152f, 28f);

            _helpBtn = UITheme.CreateButton(_bar, "帮助", Vector2.zero, new Vector2(84f, UITheme.HeightSmall),
                ToggleHelp, UITheme.FontCaption, ButtonStyle.Ghost,
                "帮助：查看快捷键与操作说明", "F1");
            UITheme.PlaceRight(_helpBtn.GetComponent<RectTransform>(), 176f, 6f, 84f, UITheme.HeightSmall);

            _avatarBtn = UITheme.CreateButton(_bar, "人物：—", Vector2.zero, new Vector2(168f, UITheme.HeightSmall),
                () => _onAvatarToggle?.Invoke(), UITheme.FontCaption, ButtonStyle.Ghost,
                "打开角色 / 外观面板：切换角色不会改变播放进度");
            UITheme.PlaceRight(_avatarBtn.GetComponent<RectTransform>(), 8f, 6f, 168f, UITheme.HeightSmall);

            UITheme.ChainHorizontal(backBtn, _libraryBtn, _helpBtn, _avatarBtn);
        }

        private void BuildErrorBanner()
        {
            _errorBanner = UITheme.CreatePanel(_canvas.transform, "ErrorBanner", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, UITheme.Layout.ErrorBannerHeight),
                UITheme.ErrorBannerColor(Core.ErrorSeverity.Recoverable), UITheme.RadiusMedium);
            _errorBannerBg = _errorBanner.GetComponent<Image>();
            UITheme.StretchTop(_errorBanner, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                64f, UITheme.Layout.ErrorBannerHeight);

            // 第一行：严重程度标签 + 恢复 / 忽略按钮；第二行：完整说明（可换行）
            _errorTag = UITheme.CreateText(_errorBanner, "✕ 错误", Vector2.zero, new Vector2(96f, 24f),
                UITheme.FontCaption, UITheme.TextError, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Place(_errorTag.rectTransform, 12f, 60f, 96f, 24f);

            _errorBody = UITheme.CreateText(_errorBanner, "", Vector2.zero, new Vector2(400f, 44f),
                UITheme.FontStatusLabel, UITheme.TextPrimary, TextAnchor.UpperLeft);
            _errorBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _errorBody.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.StretchAll(_errorBody.rectTransform, 16f, 16f, 14f, 38f);

            _errorActionBtn = UITheme.CreateButton(_errorBanner, "重试", Vector2.zero,
                new Vector2(120f, 26f), HandleRecoveryClicked, UITheme.FontCaption, ButtonStyle.Primary,
                "执行建议的恢复动作");
            UITheme.PlaceRight(_errorActionBtn.GetComponent<RectTransform>(), 116f, 62f, 120f, 26f);

            _errorClose = UITheme.CreateButton(_errorBanner, "忽略", Vector2.zero, new Vector2(96f, 26f),
                DismissError, UITheme.FontCaption, ButtonStyle.Ghost, "收起提示（不影响播放）");
            UITheme.PlaceRight(_errorClose.GetComponent<RectTransform>(), 12f, 62f, 96f, 26f);

            UITheme.ChainHorizontal(_errorActionBtn, _errorClose);
            _errorBanner.gameObject.SetActive(false);
        }

        private void HandleRecoveryClicked()
        {
            if (_session == null) return;

            // 优先使用会话层当前错误（事件与 LastError 内容一致，但 LastError 是权威来源）
            var error = _session.LastError.IsValid ? _session.LastError : _lastError;
            if (error.Recovery == Core.RecoveryAction.None)
            {
                DismissError();
                return;
            }
            // 由会话层执行恢复动作（UI 只负责触发，不做业务判断）
            _session.HandleRecovery();
            DismissError();
        }

        private void BuildLoadingStrip()
        {
            _loadingStrip = UITheme.CreatePanel(_canvas.transform, "LoadingStrip", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, UITheme.Layout.LoadingStripHeight),
                UITheme.PanelBgRaised, UITheme.RadiusMedium);
            UITheme.StretchTop(_loadingStrip, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                64f, UITheme.Layout.LoadingStripHeight);

            _loadingText = UITheme.CreateText(_loadingStrip, "正在解析 MIDI 曲目…", Vector2.zero,
                new Vector2(420f, 26f), UITheme.FontCaption, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            UITheme.StretchAll(_loadingText.rectTransform, 16f, 120f, 12f, 10f);

            var cancelBtn = UITheme.CreateButton(_loadingStrip, "取消加载", Vector2.zero,
                new Vector2(104f, 26f), () => _session?.CancelLoading(), UITheme.FontCaption,
                ButtonStyle.Secondary, "取消当前曲目加载");
            UITheme.PlaceRight(cancelBtn.GetComponent<RectTransform>(), 12f, 7f, 104f, 26f);

            // 轻量进度脉冲条：只表达「正在处理」，不假装知道百分比
            _loadingPulse = UITheme.CreateRect(_loadingStrip, "Pulse", Vector2.zero, new Vector2(120f, 3f),
                UITheme.Accent);
            _loadingPulse.anchorMin = new Vector2(0f, 0f);
            _loadingPulse.anchorMax = new Vector2(0.25f, 0f);
            _loadingPulse.offsetMin = new Vector2(16f, 6f);
            _loadingPulse.offsetMax = new Vector2(16f, 9f);

            _loadingStrip.gameObject.SetActive(false);
        }

        private void UpdateLoadingPulse()
        {
            if (_loadingPulse == null) return;
            // 0.25 宽的脉冲在轨道内来回滑动，避免"卡住"的错觉
            float phase = Mathf.PingPong(_animTime * 0.6f, 1f);
            float min = Mathf.Lerp(0f, 0.7f, phase);
            _loadingPulse.anchorMin = new Vector2(min, 0f);
            _loadingPulse.anchorMax = new Vector2(min + 0.25f, 0f);
        }

        private void BuildToast()
        {
            _toast = UITheme.CreatePanel(_canvas.transform, "Toast", Vector2.zero,
                new Vector2(UITheme.Layout.ToastWidth, UITheme.Layout.ToastHeight),
                UITheme.PanelBgRaised, UITheme.RadiusSmall);
            _toastText = UITheme.CreateText(_toast, "", Vector2.zero,
                new Vector2(UITheme.Layout.ToastWidth - 24f, UITheme.Layout.ToastHeight - 8f),
                UITheme.FontCaption, UITheme.TextPrimary);
            UITheme.StretchAll(_toastText.rectTransform, 12f, 12f, 4f, 4f);
            _toastText.verticalOverflow = VerticalWrapMode.Truncate;
            _toast.gameObject.SetActive(false);
        }

        private void BuildLibraryPanel()
        {
            _libraryPanel = UITheme.CreatePanel(_canvas.transform, "LibraryPanel", Vector2.zero,
                new Vector2(UITheme.Layout.LibraryPanelWidth, UITheme.Layout.LibraryPanelHeight),
                UITheme.PanelBg, UITheme.RadiusLarge);
            UITheme.PlaceCenter(_libraryPanel, UITheme.Layout.LibraryPanelWidth, UITheme.Layout.LibraryPanelHeight);

            var titleText = UITheme.CreateText(_libraryPanel, "曲库", Vector2.zero, new Vector2(160f, 30f),
                UITheme.FontSongTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Place(titleText.rectTransform, 20f,
                UITheme.Layout.LibraryPanelHeight - 46f, 160f, 30f);

            var closeBtn = UITheme.CreateButton(_libraryPanel, "关闭", Vector2.zero, new Vector2(80f, 28f),
                () => ShowLibrary(false), UITheme.FontCaption, ButtonStyle.Ghost, "关闭曲库面板");
            UITheme.PlaceRight(closeBtn.GetComponent<RectTransform>(), 16f,
                UITheme.Layout.LibraryPanelHeight - 46f, 80f, 28f);

            var importBtn = UITheme.CreateButton(_libraryPanel, "导入 MIDI…", Vector2.zero,
                new Vector2(150f, UITheme.HeightSmall), () =>
                {
                    ShowLibrary(false);
                    _session?.ImportMidi();
                }, UITheme.FontCaption, ButtonStyle.Primary,
                "打开文件选择器导入 .mid / .midi 文件");
            UITheme.Place(importBtn.GetComponent<RectTransform>(), 20f,
                UITheme.Layout.LibraryPanelHeight - 88f, 150f, UITheme.HeightSmall);

            var menuBtn = UITheme.CreateButton(_libraryPanel, "返回主菜单", Vector2.zero,
                new Vector2(130f, UITheme.HeightSmall), RequestBackToLibrary, UITheme.FontCaption,
                ButtonStyle.Secondary, "返回主菜单（需确认）");
            UITheme.Place(menuBtn.GetComponent<RectTransform>(), 178f,
                UITheme.Layout.LibraryPanelHeight - 88f, 130f, UITheme.HeightSmall);

            _libraryList = UITheme.CreateRect(_libraryPanel, "RecentList", Vector2.zero, Vector2.zero,
                UITheme.Transparent);
            UITheme.StretchAll(_libraryList, 20f, 20f, 20f, 104f);

            _libraryEmptyText = UITheme.CreateText(_libraryPanel,
                "本次运行还没有打开过曲目。\n可点「导入 MIDI…」选择本地文件，或返回主菜单选择示例曲。",
                Vector2.zero, new Vector2(500f, 60f), UITheme.FontCaption, UITheme.TextSecondary,
                TextAnchor.UpperLeft);
            _libraryEmptyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _libraryEmptyText.verticalOverflow = VerticalWrapMode.Overflow;
            UITheme.StretchAll(_libraryEmptyText.rectTransform, 20f, 20f, 20f, 104f);

            UITheme.ChainHorizontal(importBtn, menuBtn, closeBtn);
            _libraryPanel.gameObject.SetActive(false);
        }

        private void RequestBackToLibrary()
        {
            ShowConfirm("返回曲库？",
                "将停止当前播放并返回主菜单，当前曲目的播放进度与循环标记不会保留。",
                "返回主菜单", () => _onBackToLibrary?.Invoke());
        }

        /// <summary>刷新最近曲目列表（元数据来自会话层，UI 不再查询文件系统以外的来源）。</summary>
        private void RefreshLibraryList()
        {
            if (_libraryList == null) return;
            for (int i = _libraryList.childCount - 1; i >= 0; i--)
                Destroy(_libraryList.GetChild(i).gameObject);

            var recent = _session != null ? _session.RecentSongs : null;
            int count = recent != null ? recent.Count : 0;
            bool hasAny = false;

            float rowHeight = UITheme.Layout.LibraryRowHeight;
            float gap = UITheme.Layout.LibraryRowGap;
            int shown = 0;
            for (int i = 0; i < count && shown < 5; i++)
            {
                var song = recent[i];
                // 用 ReferenceEquals 判断，避免依赖 SongMetadata 是类还是结构体
                if (ReferenceEquals(song, null)) continue;
                hasAny = true;

                bool missing = IsSourceMissing(song.SourcePath);
                string title = string.IsNullOrEmpty(song.Title) ? "未命名曲目" : song.Title;
                string meta = missing
                    ? "! 文件缺失：可能已移动或删除，点击后需重新导入"
                    : $"{UITheme.FormatTime(song.DurationSeconds)} · {song.NoteCount} 音符 · " +
                      $"{song.TrackCount} 轨道 · tempo 变化 {song.TempoChangeCount}";

                string id = string.IsNullOrEmpty(song.SourcePath) ? song.Id : song.SourcePath;
                string display = missing ? "! " + title : title;

                var row = UITheme.CreateButton(_libraryList, display, Vector2.zero,
                    new Vector2(500f, rowHeight), () => OpenRecent(id, title), UITheme.FontCaption,
                    missing ? ButtonStyle.Ghost : ButtonStyle.Secondary,
                    missing ? "该曲目文件已不存在，点击后将提示重新导入" : "打开该曲目");
                // 列表自上而下排列（Place 系列以左下角为原点，这里用顶部锚点逐行下移）
                UITheme.PlaceTopLeft(row.GetComponent<RectTransform>(), 0f,
                    shown * (rowHeight + gap), 520f, rowHeight);

                var label = UITheme.GetLabel(row);
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleLeft;
                    UITheme.StretchAll(label.rectTransform, 12f, 190f, 0f, 0f);
                }

                var metaText = UITheme.CreateText(row.transform, meta, Vector2.zero, new Vector2(180f, rowHeight),
                    UITheme.FontStatusLabel, missing ? UITheme.TextWarning : UITheme.TextSecondary,
                    TextAnchor.MiddleRight);
                UITheme.StretchAll(metaText.rectTransform, 320f, 12f, 0f, 0f);

                shown++;
            }

            if (_libraryEmptyText != null) _libraryEmptyText.gameObject.SetActive(!hasAny);
        }

        private static bool IsSourceMissing(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            try
            {
                return !File.Exists(path);
            }
            catch (Exception)
            {
                // 路径非法（例如非法字符）时按「需要重新定位」处理，不抛给用户
                return true;
            }
        }

        private void OpenRecent(string idOrPath, string displayName)
        {
            if (_session == null) return;
            bool opened = _session.TryOpenRecentSong(idOrPath);
            if (opened)
            {
                ShowLibrary(false);
                ShowToast($"已打开《{displayName}》");
                return;
            }
            ShowToast($"无法打开《{displayName}》：文件可能已被移动、删除或格式不受支持。请用「导入 MIDI…」重新选择文件。",
                Core.ErrorSeverity.Warning);
        }

        private void BuildHelpPanel()
        {
            _helpPanel = UITheme.CreatePanel(_canvas.transform, "HelpPanel", Vector2.zero,
                new Vector2(UITheme.Layout.HelpPanelWidth, UITheme.Layout.HelpPanelHeight),
                UITheme.PanelBg, UITheme.RadiusLarge);
            UITheme.PlaceCenter(_helpPanel, UITheme.Layout.HelpPanelWidth, UITheme.Layout.HelpPanelHeight);

            var title = UITheme.CreateText(_helpPanel, "帮助与快捷键", Vector2.zero, new Vector2(300f, 30f),
                UITheme.FontSongTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(title.rectTransform, 20f, 16f, 300f, 30f);

            var closeBtn = UITheme.CreateButton(_helpPanel, "关闭", Vector2.zero, new Vector2(80f, 28f),
                () => ShowHelp(false), UITheme.FontCaption, ButtonStyle.Ghost, "关闭帮助面板");
            UITheme.PlaceTopRight(closeBtn.GetComponent<RectTransform>(), 16f, 16f, 80f, 28f);

            var body = UITheme.CreateText(_helpPanel,
                "快捷键（辅助入口，相同功能都能在界面上找到）\n" +
                "　空格　播放 / 暂停\n" +
                "　[　　重播（回到开头）\n" +
                "　R　　重置视角到当前模式的标准构图\n" +
                "　H　　切换琴键高亮提示（关闭 → 全部 → 发声 → 预提示）\n" +
                "　M　　节拍器开关\n" +
                "　Tab　曲目分析面板\n" +
                "　F1　　本帮助面板\n" +
                "　Esc　关闭当前浮层 / 取消确认\n\n" +
                "视角操作\n" +
                "　手部特写：以当前练习手为中心，优先看清琴键与手指\n" +
                "　全景：观察坐姿、双臂与完整键盘关系\n" +
                "　自由观察：鼠标右键拖动旋转、滚轮缩放，R 回到标准构图\n\n" +
                "说明\n" +
                "　· 文本输入获得焦点时，全局快捷键不会抢按键\n" +
                "　· 播放中切换角色 / 视角 / 分手 / 高亮不会重启曲目\n" +
                "　· 当前速度、视角、练习手、循环状态始终显示在界面上，不依赖悬停提示",
                Vector2.zero, new Vector2(560f, 300f), UITheme.FontCaption, UITheme.TextSecondary,
                TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            UITheme.StretchAll(body.rectTransform, 20f, 20f, 16f, 56f);

            _helpPanel.gameObject.SetActive(false);
        }

        private void BuildConfirmPanel()
        {
            _confirmPanel = UITheme.CreatePanel(_canvas.transform, "ConfirmPanel", Vector2.zero,
                new Vector2(UITheme.Layout.ConfirmPanelWidth, UITheme.Layout.ConfirmPanelHeight),
                UITheme.PanelBgRaised, UITheme.RadiusLarge);
            UITheme.PlaceCenter(_confirmPanel, UITheme.Layout.ConfirmPanelWidth, UITheme.Layout.ConfirmPanelHeight);

            _confirmTitle = UITheme.CreateText(_confirmPanel, "确认操作？", Vector2.zero, new Vector2(400f, 28f),
                UITheme.FontAction, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(_confirmTitle.rectTransform, 20f, 16f, 400f, 28f);

            _confirmBody = UITheme.CreateText(_confirmPanel, "", Vector2.zero, new Vector2(400f, 64f),
                UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.UpperLeft);
            _confirmBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _confirmBody.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.StretchAll(_confirmBody.rectTransform, 20f, 20f, 60f, 52f);

            _confirmCancelBtn = UITheme.CreateButton(_confirmPanel, "取消", Vector2.zero,
                new Vector2(110f, UITheme.HeightSmall), CloseConfirm, UITheme.FontCaption,
                ButtonStyle.Secondary, "取消并返回（Esc）");
            UITheme.PlaceRight(_confirmCancelBtn.GetComponent<RectTransform>(), 20f + 110f + 8f, 16f,
                110f, UITheme.HeightSmall);

            _confirmOkBtn = UITheme.CreateButton(_confirmPanel, "确认", Vector2.zero,
                new Vector2(110f, UITheme.HeightSmall), ExecuteConfirm, UITheme.FontCaption,
                ButtonStyle.Danger, "确认执行（不可撤销）");
            UITheme.PlaceRight(_confirmOkBtn.GetComponent<RectTransform>(), 20f, 16f, 110f, UITheme.HeightSmall);

            UITheme.ChainHorizontal(_confirmCancelBtn, _confirmOkBtn);
            _confirmPanel.gameObject.SetActive(false);
        }

        /// <summary>顶部堆叠重排：错误横幅 → 加载条 → 轻量提示，互不重叠。</summary>
        private void LayoutTopStack()
        {
            float y = UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space8;

            if (_errorBanner != null && _errorBanner.gameObject.activeSelf)
            {
                UITheme.StretchTop(_errorBanner, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                    y, UITheme.Layout.ErrorBannerHeight);
                y += UITheme.Layout.ErrorBannerHeight + UITheme.Space8;
            }
            if (_loadingStrip != null && _loadingStrip.gameObject.activeSelf)
            {
                UITheme.StretchTop(_loadingStrip, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                    y, UITheme.Layout.LoadingStripHeight);
                y += UITheme.Layout.LoadingStripHeight + UITheme.Space8;
            }
            if (_toast != null && _toast.gameObject.activeSelf)
            {
                UITheme.PlaceTopCenter(_toast, y, UITheme.Layout.ToastWidth, UITheme.Layout.ToastHeight);
            }
        }
    }
}
