using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 顶部状态栏 + 错误 / 加载 / 提示反馈 + 浮层容器。
    /// <para>
    /// 顶栏是**单行**结构（高 56、左右内边距 16、垂直居中）：
    /// 返回 32×32 ｜ 分隔线 1×20 ｜ 曲名（点开曲库） …… 状态 chip ｜ 帮助 32×32 ｜ 角色 chip。
    /// 所有控件同高（32）、同内边距、间距统一 8，不再出现「高度各不相同、有的贴边有的留白」的浮块。
    /// </para>
    /// <para>
    /// 反馈按「顶部堆叠」排列：错误横幅（含恢复按钮）→ 加载条（含取消）→ 轻量提示。
    /// 破坏性操作（返回曲库 / 返回主菜单）一律经过 <see cref="ShowConfirm"/> 二次确认。
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
        private Button _songBtn;
        private RectTransform _stateChip;
        private Image _stateDot;
        private Text _stateChipText;
        private Button _helpBtn;
        private Button _avatarBtn;
        private Text _avatarInitial;
        private Text _avatarName;
        private Text _saveHint;
        private float _songLeft;
        private float _topRowY;
        private float _rightClusterInset;

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

        // ---- 顶栏几何（全部为 4 的倍数）----
        private const float BackSize = UITheme.SizeIconButton;       // 32
        private const float BarInnerGap = UITheme.Space8;            // 8
        private const float DividerWidth = 1f;
        private const float DividerHeight = 20f;
        private const float SongMaxWidth = 320f;
        private const float AvatarChipWidth = 132f;
        private const float StateChipWidth = 120f;

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
        /// <paramref name="onAvatarToggle"/> = 点击「角色 chip」时切换角色面板。
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

        /// <summary>刷新曲名 / 会话状态 / 播放状态（三者一起决定顶栏文案与状态圆点颜色）。</summary>
        public void SetSong(string songName, bool hasSong, Core.LessonState state, Core.PlaybackState playback)
        {
            if (_songBtn != null)
            {
                string name = string.IsNullOrEmpty(songName) ? (hasSong ? "未命名曲目" : "未加载曲目") : songName;
                UITheme.SetButtonLabel(_songBtn,
                    "♪ " + UITheme.Truncate(name, UITheme.Layout.TopBarSongNameMaxChars) + "  ▾");
                var label = UITheme.GetLabel(_songBtn);
                if (label != null) label.color = hasSong ? UITheme.TextPrimary : UITheme.TextSecondary;
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

            if (_stateDot != null)
            {
                _stateDot.color = state == Core.LessonState.Error ? UITheme.Error
                    : (state == Core.LessonState.Loading ? UITheme.Accent
                        : playback == Core.PlaybackState.Playing ? UITheme.Success
                        : playback == Core.PlaybackState.Paused ? UITheme.Warning
                        : UITheme.TextDisabled);
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

        /// <summary>刷新顶栏「角色 chip」：16×16 头像位（首字）+ 名称。</summary>
        public void SetAvatarName(string avatarName)
        {
            if (_avatarName == null) return;
            string name = string.IsNullOrEmpty(avatarName) ? "—" : avatarName;
            _avatarName.text = UITheme.Truncate(name, 5);
            if (_avatarInitial != null)
                _avatarInitial.text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);
            if (_avatarBtn != null)
                UITheme.SetButtonTooltip(_avatarBtn, $"角色 / 外观：当前 {name}（切换不会改变播放进度）");
        }

        /// <summary>刷新「偏好：本次会话生效」提示（据实显示，不谎称已持久化）。</summary>
        public void SetSaveHint(string text)
        {
            if (_saveHint == null) return;
            _saveHint.text = string.IsNullOrEmpty(text) ? "" : text;
        }

        /// <summary>
        /// 顶栏自适应：窄窗口隐藏「偏好」这类次要信息，把宽度让给曲名；
        /// 按钮尺寸不缩放（可点区域永远 ≥ 32）。
        /// </summary>
        public void ApplyResponsiveLayout(float referenceWidth)
        {
            bool wide = referenceWidth >= UITheme.Layout.WideWidthThreshold;
            if (_saveHint != null && _saveHint.gameObject.activeSelf != wide)
                _saveHint.gameObject.SetActive(wide);

            // 窄窗口把宽度让给曲名后面的右侧簇，但曲名区不小于 220（仍能读出 8 个汉字）
            float songW = wide ? SongMaxWidth : 220f;
            if (_songBtn != null)
                UITheme.Place(_songBtn.GetComponent<RectTransform>(), _songLeft, _topRowY,
                    songW, UITheme.HeightSmall);
            if (_midArea != null)
                UITheme.StretchX(_midArea, _songLeft + songW, _rightClusterInset, _topRowY,
                    UITheme.HeightSmall);
        }

        /// <summary>
        /// 加载条开关（可取消、防重复点击）。<paramref name="stageLabel"/> 只报阶段，不编造百分比。
        /// </summary>
        public void SetLoading(bool loading, string songName, string stageLabel = null)
        {
            if (_loadingStrip == null) return;
            if (loading)
            {
                if (_loadingText != null)
                {
                    string stage = string.IsNullOrEmpty(stageLabel)
                        ? "正在解析 MIDI 曲目"
                        : stageLabel.TrimEnd('…');
                    _loadingText.text = string.IsNullOrEmpty(songName)
                        ? stage + "…"
                        : $"{stage}《{UITheme.Truncate(songName, 18)}》…";
                }
            }
            if (_loadingStrip.gameObject.activeSelf != loading) _loadingStrip.gameObject.SetActive(loading);
            if (loading) DismissError();
            LayoutTopStack();
        }

        /// <summary>
        /// 错误横幅：显示「发生了什么 + 为什么 + 下一步」，恢复按钮文案取 <c>LastError.RecoveryLabel</c>。
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

            // 诊断信息只写日志，不进 UI
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

        /// <summary>轻量提示（toast）：任何用户可见错误都不会只写进 Console。</summary>
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

        /// <summary>破坏性操作二次确认。<paramref name="onConfirm"/> 在点击确认按钮时执行。</summary>
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
            float barH = UITheme.Layout.StatusBarHeight;   // 56
            float pad = UITheme.Layout.TopBarPadding;      // 16
            float y = Mathf.Round((barH - UITheme.HeightSmall) * 0.5f);   // 32 高控件垂直居中 = 12

            _bar = UITheme.CreatePanel(_canvas.transform, "StatusBar", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, barH), UITheme.PanelBgDeep,
                UITheme.RadiusLarge);
            var barBg = _bar.GetComponent<Image>();
            if (barBg != null) UITheme.SetOutline(barBg, true, UITheme.Divider, 1f);
            UITheme.StretchTop(_bar, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin, barH);

            // —— 左侧：返回 32×32 → 8 → 分隔线 1×20 → 8 → 曲名 ——
            var backBtn = UIKit.IconButton(_bar, "BackToLibrary", UIIcon.ArrowLeft, BackSize,
                () => ShowConfirm("返回曲库？",
                    "将停止当前播放并返回主菜单，当前曲目的播放进度与循环标记不会保留。",
                    "返回主菜单", () => _onBackToLibrary?.Invoke()),
                "返回曲库：停止播放并回到主菜单（需确认）");
            UITheme.Place(backBtn.GetComponent<RectTransform>(), pad, y, BackSize, BackSize);

            float x = pad + BackSize + BarInnerGap;
            var divider = UIKit.Divider(_bar, "BarDivider");
            UITheme.Place(divider, x, Mathf.Round((barH - DividerHeight) * 0.5f), DividerWidth, DividerHeight);
            x += DividerWidth + BarInnerGap;

            _songBtn = UIKit.TextButton(_bar, "♪ 未加载曲目  ▾", SongMaxWidth, UITheme.HeightSmall,
                () => ShowLibrary(!IsLibraryOpen), UITheme.FontBody,
                "曲库：最近曲目、导入 MIDI、返回主菜单");
            _songBtn.gameObject.name = "Btn_曲库";
            _songLeft = x;
            _topRowY = y;
            UITheme.Place(_songBtn.GetComponent<RectTransform>(), x, y, SongMaxWidth, UITheme.HeightSmall);
            var songLabel = UITheme.GetLabel(_songBtn);
            if (songLabel != null) songLabel.alignment = TextAnchor.MiddleLeft;

            // —— 右侧（从右往左）：角色 chip → 8 → 帮助 32×32 → 8 → 状态 chip ——
            float rightX = pad;

            var avatarChip = UITheme.CreateButton(_bar, "", Vector2.zero,
                new Vector2(AvatarChipWidth, UITheme.HeightSmall), () => _onAvatarToggle?.Invoke(),
                UITheme.FontBodySmall, ButtonStyle.Ghost);
            avatarChip.gameObject.name = "Btn_角色";
            _avatarBtn = avatarChip;
            if (avatarChip.targetGraphic is Image avImg)
            {
                avImg.color = UITheme.Transparent;
                var sprite = UITheme.GetRoundedSprite(UITheme.RadiusSmall);
                if (sprite != null) { avImg.sprite = sprite; avImg.type = Image.Type.Sliced; }
                UITheme.SetOutline(avImg, true, UITheme.Divider, 1f);
            }
            UITheme.ApplyFlatLook(avatarChip);
            UIHoverFeedback.Attach(avatarChip, UITheme.Transparent, UITheme.ControlHover);

            // 头部文字由 CreateButton 生成，这里只清空并复用为名称位
            var avatarLabel = UITheme.GetLabel(avatarChip);
            if (avatarLabel != null) avatarLabel.gameObject.SetActive(false);

            var initialBg = UITheme.CreatePanel(avatarChip.transform, "AvatarBadge", Vector2.zero,
                new Vector2(16f, 16f), UITheme.AccentDim, 8);
            UITheme.Place(initialBg, BarInnerGap, Mathf.Round((UITheme.HeightSmall - 16f) * 0.5f), 16f, 16f);
            var initialImg = initialBg.GetComponent<Image>();
            if (initialImg != null) initialImg.raycastTarget = false;
            _avatarInitial = UIKit.Label(initialBg, "写", UITheme.FontStatusLabel, UITheme.TextPrimary,
                TextAnchor.MiddleCenter, FontStyle.Bold, "Initial");

            _avatarName = UIKit.Label(avatarChip.transform, "—", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.MiddleLeft, FontStyle.Normal, "AvatarName");
            UITheme.StretchAll(_avatarName.rectTransform, BarInnerGap + 16f + BarInnerGap, BarInnerGap, 0f, 0f);

            UITheme.PlaceRight(avatarChip.GetComponent<RectTransform>(), rightX, y, AvatarChipWidth,
                UITheme.HeightSmall);
            rightX += AvatarChipWidth + BarInnerGap;
            UITheme.SetButtonTooltip(avatarChip, "角色 / 外观：切换角色不会改变播放进度");

            _helpBtn = UIKit.IconButton(_bar, "Help", UIIcon.Help, BackSize, ToggleHelp,
                "帮助：查看快捷键与操作说明", "F1");
            UITheme.PlaceRight(_helpBtn.GetComponent<RectTransform>(), rightX, y, BackSize, BackSize);
            rightX += BackSize + BarInnerGap;

            _stateChip = UITheme.CreatePanel(_bar, "StateChip", Vector2.zero,
                new Vector2(StateChipWidth, UITheme.HeightSmall), UITheme.PanelBgRaised,
                UITheme.RadiusSmall);
            var stateBg = _stateChip.GetComponent<Image>();
            if (stateBg != null) stateBg.raycastTarget = false;
            UITheme.PlaceRight(_stateChip, rightX, y, StateChipWidth, UITheme.HeightSmall);

            _stateDot = UITheme.CreatePanel(_stateChip, "Dot", Vector2.zero, new Vector2(8f, 8f),
                UITheme.TextDisabled, 4).GetComponent<Image>();
            if (_stateDot != null) _stateDot.raycastTarget = false;
            UITheme.Place(_stateDot.rectTransform, UITheme.Space12, Mathf.Round((UITheme.HeightSmall - 8f) * 0.5f),
                8f, 8f);

            _stateChipText = UIKit.Label(_stateChip, "— 未加载曲目", UITheme.FontBodySmall,
                UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal, "StateText");
            UITheme.StretchAll(_stateChipText.rectTransform, UITheme.Space12 + 8f + UITheme.Space8,
                UITheme.Space8, 0f, 0f);

            // 偏好提示（仅在宽窗口出现，纯文本，不额外占一个浮块）
            _saveHint = UIKit.Label(_bar, "偏好：本次会话生效", UITheme.FontStatusLabel, UITheme.TextSecondary,
                TextAnchor.MiddleRight, FontStyle.Normal, "SaveHint");
            UITheme.PlaceRight(_saveHint.rectTransform, rightX + StateChipWidth + UITheme.Space8, y,
                120f, UITheme.HeightSmall);
            _saveHint.gameObject.SetActive(false);

            // 中部容器（曲名与右侧簇之间的弹性区，供后续状态位扩展）
            _rightClusterInset = rightX + StateChipWidth + UITheme.Space16;
            _midArea = UITheme.CreateRect(_bar, "MidArea", Vector2.zero, Vector2.zero, UITheme.Transparent);
            UITheme.StretchX(_midArea, x + SongMaxWidth, _rightClusterInset, y, UITheme.HeightSmall);

            UITheme.ChainHorizontal(backBtn, _songBtn, _helpBtn, avatarChip);
        }

        private void BuildErrorBanner()
        {
            const float pad = UITheme.Space16;
            float h = UITheme.Layout.ErrorBannerHeight;   // 96
            _errorBanner = UITheme.CreatePanel(_canvas.transform, "ErrorBanner", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, h),
                UITheme.ErrorBannerColor(Core.ErrorSeverity.Recoverable), UITheme.RadiusLarge);
            _errorBannerBg = _errorBanner.GetComponent<Image>();
            UITheme.StretchTop(_errorBanner, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space8, h);

            _errorTag = UIKit.Label(_errorBanner, "✕ 错误", UITheme.FontBodySmall, UITheme.TextError,
                TextAnchor.MiddleLeft, FontStyle.Bold, "ErrorTag");
            UITheme.Place(_errorTag.rectTransform, pad, h - pad - 18f, 96f, 18f);

            _errorBody = UIKit.Label(_errorBanner, "", UITheme.FontBodySmall, UITheme.TextPrimary,
                TextAnchor.UpperLeft, FontStyle.Normal, "ErrorBody");
            _errorBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _errorBody.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.StretchAll(_errorBody.rectTransform, pad, 200f, pad, 34f);

            _errorActionBtn = UIKit.PrimaryButton(_errorBanner, "重试", new Vector2(96f, 32f),
                HandleRecoveryClicked, "执行建议的恢复动作");
            UITheme.PlaceRight(_errorActionBtn.GetComponent<RectTransform>(), pad + 76f + UITheme.Space8,
                Mathf.Round((h - 32f) * 0.5f), 96f, 32f);

            _errorClose = UIKit.TextButton(_errorBanner, "忽略", 76f, 32f, DismissError, UITheme.FontBodySmall,
                "收起提示（不影响播放）");
            UITheme.PlaceRight(_errorClose.GetComponent<RectTransform>(), pad,
                Mathf.Round((h - 32f) * 0.5f), 76f, 32f);

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
            const float pad = UITheme.Space16;
            float h = UITheme.Layout.LoadingStripHeight;   // 40
            _loadingStrip = UITheme.CreatePanel(_canvas.transform, "LoadingStrip", Vector2.zero,
                new Vector2(UITheme.Layout.ReferenceResolution.x, h), UITheme.PanelBgRaised,
                UITheme.RadiusLarge);
            UITheme.StretchTop(_loadingStrip, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space8, h);

            _loadingText = UIKit.Label(_loadingStrip, "正在解析 MIDI 曲目…", UITheme.FontBodySmall,
                UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Normal, "LoadingText");
            UITheme.StretchAll(_loadingText.rectTransform, pad, 120f, 8f, 12f);

            var cancelBtn = UIKit.TextButton(_loadingStrip, "取消加载", 88f, 28f,
                () => _session?.CancelLoading(), UITheme.FontBodySmall, "取消当前曲目加载");
            UITheme.PlaceRight(cancelBtn.GetComponent<RectTransform>(), pad, 6f, 88f, 28f);

            // 轻量进度脉冲条：只表达「正在处理」，不假装知道百分比
            _loadingPulse = UITheme.CreateRect(_loadingStrip, "Pulse", Vector2.zero, new Vector2(120f, 3f),
                UITheme.Accent);
            _loadingPulse.anchorMin = new Vector2(0f, 0f);
            _loadingPulse.anchorMax = new Vector2(0.25f, 0f);
            _loadingPulse.offsetMin = new Vector2(pad, 4f);
            _loadingPulse.offsetMax = new Vector2(pad, 7f);

            _loadingStrip.gameObject.SetActive(false);
        }

        private void UpdateLoadingPulse()
        {
            if (_loadingPulse == null) return;
            // 0.25 宽的脉冲在轨道内来回滑动，避免「卡住」的错觉
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
            var bg = _toast.GetComponent<Image>();
            if (bg != null) bg.raycastTarget = false;
            _toastText = UIKit.Label(_toast, "", UITheme.FontBodySmall, UITheme.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Normal, "ToastText");
            UITheme.StretchAll(_toastText.rectTransform, UITheme.Space12, UITheme.Space12, 0f, 0f);
            _toastText.verticalOverflow = VerticalWrapMode.Truncate;
            _toast.gameObject.SetActive(false);
        }

        private void BuildLibraryPanel()
        {
            const float pad = UITheme.Space16;
            float w = UITheme.Layout.LibraryPanelWidth;
            float h = UITheme.Layout.LibraryPanelHeight;

            _libraryPanel = UITheme.CreatePanel(_canvas.transform, "LibraryPanel", Vector2.zero,
                new Vector2(w, h), UITheme.PanelBg, UITheme.RadiusLarge);
            var panelBg = _libraryPanel.GetComponent<Image>();
            if (panelBg != null) UITheme.SetOutline(panelBg, true, UITheme.Divider, 1f);
            UITheme.PlaceCenter(_libraryPanel, w, h);

            var titleText = UIKit.Label(_libraryPanel, "曲库", UITheme.FontSubtitle, UITheme.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, "LibraryTitle");
            UITheme.PlaceTopLeft(titleText.rectTransform, pad, pad, 240f, 24f);

            var closeBtn = UIKit.TextButton(_libraryPanel, "关闭", 56f, 24f, () => ShowLibrary(false),
                UITheme.FontBodySmall, "关闭曲库面板");
            UITheme.PlaceTopRight(closeBtn.GetComponent<RectTransform>(), pad, pad, 56f, 24f);

            var importBtn = UIKit.PrimaryButton(_libraryPanel, "导入 MIDI…", new Vector2(148f, 40f), () =>
            {
                ShowLibrary(false);
                _session?.ImportMidi();
            }, "打开文件选择器导入 .mid / .midi 文件");
            UITheme.PlaceTopLeft(importBtn.GetComponent<RectTransform>(), pad, pad + 32f, 148f, 40f);

            var menuBtn = UIKit.SecondaryButton(_libraryPanel, "返回主菜单", new Vector2(132f, 40f),
                RequestBackToLibrary, "返回主菜单（需确认）");
            UITheme.PlaceTopLeft(menuBtn.GetComponent<RectTransform>(), pad + 148f + UITheme.Space12,
                pad + 32f, 132f, 40f);

            // 列表区：顶部标题 32 + 操作行 40 + 两处 12 间距
            float listTop = pad + 32f + 12f + 40f + 12f;
            _libraryList = UITheme.CreateRect(_libraryPanel, "RecentList", Vector2.zero, Vector2.zero,
                UITheme.Transparent);
            UITheme.StretchAll(_libraryList, pad, pad, pad, listTop);

            _libraryEmptyText = UIKit.Label(_libraryPanel,
                "本次运行还没有打开过曲目。\n可点「导入 MIDI…」选择本地文件，或返回主菜单选择示例曲。",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.UpperLeft, FontStyle.Normal,
                "LibraryEmpty");
            _libraryEmptyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _libraryEmptyText.verticalOverflow = VerticalWrapMode.Overflow;
            UITheme.StretchAll(_libraryEmptyText.rectTransform, pad, pad, pad, listTop);

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
            float rowWidth = UITheme.Layout.LibraryPanelWidth - UITheme.Space16 * 2f;
            int shown = 0;
            for (int i = 0; i < count && shown < 6; i++)
            {
                var song = recent[i];
                // 用 ReferenceEquals 判断，避免依赖 SongMetadata 是类还是结构体
                if (ReferenceEquals(song, null)) continue;
                hasAny = true;

                bool missing = IsSourceMissing(song.SourcePath);
                string title = string.IsNullOrEmpty(song.Title) ? "未命名曲目" : song.Title;
                string meta = missing
                    ? "! 文件缺失：可能已移动或删除"
                    : $"{UITheme.FormatTime(song.DurationSeconds)} · {song.NoteCount} 音符 · " +
                      $"{song.TrackCount} 轨道";

                string id = string.IsNullOrEmpty(song.SourcePath) ? song.Id : song.SourcePath;
                string display = missing ? "! " + title : title;

                var row = UITheme.CreateButton(_libraryList, display, Vector2.zero,
                    new Vector2(rowWidth, rowHeight), () => OpenRecent(id, title), UITheme.FontBody,
                    missing ? ButtonStyle.Ghost : ButtonStyle.Secondary,
                    missing ? "该曲目文件已不存在，点击后将提示重新导入" : "打开该曲目");
                UITheme.PlaceTopLeft(row.GetComponent<RectTransform>(), 0f,
                    shown * (rowHeight + gap), rowWidth, rowHeight);

                var rowImg = row.targetGraphic as Image;
                if (rowImg != null)
                {
                    var sprite = UITheme.GetRoundedSprite(UITheme.RadiusMedium);
                    if (sprite != null) { rowImg.sprite = sprite; rowImg.type = Image.Type.Sliced; }
                    UITheme.SetOutline(rowImg, !missing, UITheme.Divider, 1f);
                }

                var label = UITheme.GetLabel(row);
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleLeft;
                    UITheme.StretchAll(label.rectTransform, UITheme.Space12, 190f, 0f, 0f);
                }

                var metaText = UIKit.Label(row.transform, meta, UITheme.FontBodySmall,
                    missing ? UITheme.TextWarning : UITheme.TextSecondary,
                    TextAnchor.MiddleRight, FontStyle.Normal, "Meta");
                UITheme.StretchAll(metaText.rectTransform, 320f, UITheme.Space12, 0f, 0f);

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
            const float pad = UITheme.Space16;
            float w = UITheme.Layout.HelpPanelWidth;
            float h = UITheme.Layout.HelpPanelHeight;

            _helpPanel = UITheme.CreatePanel(_canvas.transform, "HelpPanel", Vector2.zero,
                new Vector2(w, h), UITheme.PanelBg, UITheme.RadiusLarge);
            var panelBg = _helpPanel.GetComponent<Image>();
            if (panelBg != null) UITheme.SetOutline(panelBg, true, UITheme.Divider, 1f);
            UITheme.PlaceCenter(_helpPanel, w, h);

            var title = UIKit.Label(_helpPanel, "帮助与快捷键", UITheme.FontSubtitle, UITheme.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, "HelpTitle");
            UITheme.PlaceTopLeft(title.rectTransform, pad, pad, 300f, 24f);

            var closeBtn = UIKit.TextButton(_helpPanel, "关闭", 56f, 24f, () => ShowHelp(false),
                UITheme.FontBodySmall, "关闭帮助面板");
            UITheme.PlaceTopRight(closeBtn.GetComponent<RectTransform>(), pad, pad, 56f, 24f);

            var divider = UITheme.CreateDivider(_helpPanel, "Divider", Vector2.zero, new Vector2(w - pad * 2f, 1f));
            UITheme.StretchTop(divider, pad, pad, pad + 32f, 1f);

            var body = UIKit.Label(_helpPanel,
                "快捷键（辅助入口，相同功能都能在界面上找到）\n" +
                "　空格　播放 / 暂停\n" +
                "　[　　重播（回到开头）\n" +
                "　R　　重置视角到当前模式的标准构图\n" +
                "　H　　切换琴键高亮提示（关闭 → 全部 → 发声 → 预提示）\n" +
                "　T　　展示 / 教学模式\n" +
                "　M　　节拍器开关\n" +
                "　Tab　曲目分析面板\n" +
                "　F1　　本帮助面板\n" +
                "　Esc　关闭当前浮层 / 取消确认\n\n" +
                "视角操作\n" +
                "　手部特写：以当前练习手为中心，优先看清琴键与手指\n" +
                "　全景：观察坐姿、双臂与完整键盘关系\n" +
                "　自由观察：鼠标右键拖动旋转、滚轮缩放，R 回到标准构图\n\n" +
                "界面约定\n" +
                "　· 每个 chip 直接写出当前值（如「循环：A-B」），不靠悬停猜状态\n" +
                "　· 实心金色按钮 = 当前唯一主操作；其余控件挂起时才提亮\n" +
                "　· 播放中切换角色 / 视角 / 分手 / 高亮不会重启曲目",
                UITheme.FontBodySmall, UITheme.TextSecondary, TextAnchor.UpperLeft, FontStyle.Normal, "HelpBody");
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            UITheme.StretchAll(body.rectTransform, pad, pad, pad, pad + 44f);

            _helpPanel.gameObject.SetActive(false);
        }

        private void BuildConfirmPanel()
        {
            const float pad = UITheme.Space16;
            float w = UITheme.Layout.ConfirmPanelWidth;
            float h = UITheme.Layout.ConfirmPanelHeight;

            _confirmPanel = UITheme.CreatePanel(_canvas.transform, "ConfirmPanel", Vector2.zero,
                new Vector2(w, h), UITheme.PanelBgRaised, UITheme.RadiusLarge);
            var panelBg = _confirmPanel.GetComponent<Image>();
            if (panelBg != null) UITheme.SetOutline(panelBg, true, UITheme.Divider, 1f);
            UITheme.PlaceCenter(_confirmPanel, w, h);

            _confirmTitle = UIKit.Label(_confirmPanel, "确认操作？", UITheme.FontSubtitle, UITheme.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, "ConfirmTitle");
            UITheme.PlaceTopLeft(_confirmTitle.rectTransform, pad, pad, w - pad * 2f, 24f);

            _confirmBody = UIKit.Label(_confirmPanel, "", UITheme.FontBodySmall, UITheme.TextSecondary,
                TextAnchor.UpperLeft, FontStyle.Normal, "ConfirmBody");
            _confirmBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _confirmBody.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.StretchAll(_confirmBody.rectTransform, pad, pad, 72f, 56f);

            _confirmCancelBtn = UIKit.SecondaryButton(_confirmPanel, "取消", new Vector2(96f, 40f),
                CloseConfirm, "取消并返回（Esc）");
            UITheme.PlaceRight(_confirmCancelBtn.GetComponent<RectTransform>(), pad + 108f + UITheme.Space12,
                pad, 96f, 40f);

            _confirmOkBtn = UITheme.CreateButton(_confirmPanel, "确认", Vector2.zero, new Vector2(108f, 40f),
                ExecuteConfirm, UITheme.FontBodySmall, ButtonStyle.Danger, "确认执行（不可撤销）");
            UITheme.PlaceRight(_confirmOkBtn.GetComponent<RectTransform>(), pad, pad, 108f, 40f);

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
