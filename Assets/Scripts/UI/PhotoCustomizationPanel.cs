using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Customization;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 照片个性化面板（方案 9.2 流程的界面层 / 审查 12.2 / 12.3）。
    /// <para>
    /// 步骤顺序与业务顺序一致：上传 → 格式说明 → 隐私说明 + 同意 → 风格 → 生成进度 + 取消 →
    /// 预览 → 应用 / 重新生成 / 恢复默认 / 删除 → 错误提示 + 重试。
    /// </para>
    /// <para>
    /// 约束：只用 <see cref="UITheme"/> 的公开成员搭界面（暗色面板 + 琥珀强调）；
    /// 状态全部来自 <see cref="CharacterCustomizationService"/>，本类不做业务判断；
    /// 运行中禁用冲突操作（上传 / 应用 / 删除），按钮一律带文字，键盘可达、焦点可见；
    /// 内容区可滚动，小窗口下按钮不会被裁掉。
    /// </para>
    /// </summary>
    public class PhotoCustomizationPanel : MonoBehaviour
    {
        private const int CanvasSortingOrder = 300;
        private const float PanelWidth = 420f;
        private const float HeaderHeight = 44f;
        private const float PreviewHeight = 168f;
        private const float ProgressRowHeight = 28f;

        private const string ConsentToggleLabel = "我已阅读并同意隐私说明";

        private CharacterCustomizationService _service;
        private PrivacyConsentController _consent;
        private AppearanceSlot _slot = AppearanceSlot.Thumbnail;
        private PhotoFilePickerUI _picker;

        private RectTransform _panel;
        private RectTransform _content;

        private Text _photoText;
        private Text _statusText;
        private Text _errorText;
        private Text _progressText;
        private Text _storageWarningText;

        private RawImage _previewImage;
        private Slider _progressSlider;
        private Button _uploadButton;
        private Button _generateButton;
        private Button _cancelButton;
        private Button _applyButton;
        private Button _regenerateButton;
        private Button _restoreButton;
        private Button _undoButton;
        private Button _deleteButton;
        private Button _retryButton;
        private Button _consentToggle;
        private readonly List<Button> _styleButtons = new List<Button>();
        private readonly List<CharacterStyle> _styles = new List<CharacterStyle>
        {
            CharacterStyle.Realistic,
            CharacterStyle.Cartoon,
            CharacterStyle.Anime
        };

        private CharacterStyle _selectedStyle = CharacterStyle.Realistic;
        private string _pickedPath = string.Empty;
        private string _pickedName = string.Empty;
        private bool _showingServiceError;
        private static string _lastPickerDirectory = string.Empty;
        private RectTransform _progressRow;
        private RectTransform _previewRow;
        private RectTransform _errorRow;

        private int _lastSignature = int.MinValue;
        private int _lastPercent = -1;
        private string _lastStatusText = string.Empty;

        /// <summary>面板是否展开。</summary>
        public bool IsPanelOpen => _panel != null && _panel.gameObject.activeSelf;

        /// <summary>当前「应用」作用的槽位。</summary>
        public AppearanceSlot AppliedSlot => _slot;

        /// <summary>
        /// 切换应用槽位。<b>只看运行时能力</b>（<see cref="ICharacterView.Supports"/>），
        /// 不支持时不切换并返回可读原因（方案 9.5：不兼容角色不提供「应用」）。
        /// </summary>
        public bool SetSlot(AppearanceSlot slot, out string error)
        {
            ICharacterView view = _service != null && _service.Applier != null ? _service.Applier.View : null;
            if (view != null && !view.Supports(slot))
            {
                error = $"该角色不支持「{DescribeSlot(slot)}」槽位，请改用「缩略图」或「整体色调」。";
                return false;
            }

            _slot = slot;
            _lastSignature = int.MinValue;   // 强制刷新按钮可用性
            Refresh(true);
            error = string.Empty;
            return true;
        }

        /// <summary>当前槽位是否被角色能力支持（无角色时按不支持处理）。</summary>
        private bool IsSlotSupported()
        {
            ICharacterView view = _service != null && _service.Applier != null ? _service.Applier.View : null;
            return view != null && view.Supports(_slot);
        }

        /// <summary>
        /// 依赖注入入口（不使用 <c>FindObjectOfType</c>）。
        /// <paramref name="slot"/> 决定「应用」作用到哪个外观槽位，角色不支持时应用会给出可读原因。
        /// </summary>
        public void Initialize(CharacterCustomizationService service, PrivacyConsentController consent,
            AppearanceSlot slot = AppearanceSlot.Thumbnail)
        {
            _service = service;
            _consent = consent;
            _slot = slot;

            BuildUI();
            ShowPanel(false);
        }

        private void OnDestroy()
        {
            if (_picker != null)
            {
                _picker.Close();
                _picker = null;
            }
        }

        /// <summary>展开 / 收起（供顶栏或角色面板按钮调用）。</summary>
        public void TogglePanel() => ShowPanel(!IsPanelOpen);

        /// <summary>显示 / 隐藏面板。展开时把键盘焦点放到第一个可操作按钮上。</summary>
        public void ShowPanel(bool show)
        {
            if (_panel == null) return;
            if (show)
            {
                UITooltip.Hide();
                Refresh(true);
            }
            _panel.gameObject.SetActive(show);

            if (show && _uploadButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_uploadButton.gameObject);
        }

        private void Update()
        {
            if (!IsPanelOpen) return;

            // 文件选择器是模态的：打开时不推进业务，也不响应其它面板的快捷键
            if (_picker != null)
            {
                if (!_picker.Tick())
                {
                    _picker = null;
                    if (EventSystem.current != null && _uploadButton != null)
                        EventSystem.current.SetSelectedGameObject(_uploadButton.gameObject);
                }
                return;
            }

            if (UITheme.ConsumeEscape())
            {
                ShowPanel(false);
                return;
            }

            if (_service != null) _service.Tick(Time.unscaledTime);
            Refresh(false);
        }

        // ==================== 构建界面 ====================

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            var canvas = UITheme.CreateCanvas(transform, "PhotoCustomizationCanvas", CanvasSortingOrder);

            _panel = UITheme.CreatePanel(canvas.transform, "PhotoCustomizationPanel", Vector2.zero,
                new Vector2(PanelWidth, 600f), UITheme.PanelBg, UITheme.RadiusLarge);
            UITheme.StretchRightColumn(_panel, PanelWidth,
                UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin, UITheme.Layout.ScreenMargin);

            var title = UITheme.CreateText(_panel, "照片个性化", Vector2.zero, new Vector2(200f, 28f),
                UITheme.FontTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(title.rectTransform, UITheme.Space16, UITheme.Space12, 200f, 28f);

            var closeBtn = UITheme.CreateButton(_panel, "收起", Vector2.zero, new Vector2(64f, 28f),
                () => ShowPanel(false), UITheme.FontCaption, ButtonStyle.Ghost, "收起照片个性化面板");
            UITheme.PlaceTopRight(closeBtn.GetComponent<RectTransform>(), UITheme.Space12, UITheme.Space12, 64f, 28f);

            BuildScrollArea();
            BuildContent();
        }

        private void BuildScrollArea()
        {
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(_panel, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            UITheme.StretchAll(scrollRt, UITheme.Space12, UITheme.Space12, UITheme.Space12, HeaderHeight);
            var scroll = scrollGo.AddComponent<ScrollRect>();

            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewportRt.pivot = new Vector2(0f, 1f);
            var viewportImage = viewportGo.AddComponent<Image>();
            viewportImage.color = UITheme.Transparent;
            viewportImage.raycastTarget = true;   // 接收滚轮 / 拖拽
            viewportGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            _content = contentGo.AddComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(2, 2, 2, 12);
            layout.spacing = UITheme.Space8;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = _content;
            scroll.viewport = viewportRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
        }

        private void BuildContent()
        {
            AddText("照片只在本机处理，用于生成角色外观；不会上传到任何服务器。",
                UITheme.FontCaption, UITheme.TextSecondary, 34f);

            _uploadButton = AddButton(null, "选择照片…", OnUploadClicked, ButtonStyle.Secondary,
                "选择一张 PNG / JPG 照片（不会上传）");

            _photoText = AddText("尚未选择照片。", UITheme.FontCaption, UITheme.TextSecondary, 20f);

            AddText($"支持 .png / .jpg / .jpeg；不超过 "
                    + $"{PhotoInputValidator.MaxBytes / 1024 / 1024} MB；"
                    + $"短边 ≥ {PhotoInputValidator.MinDimension}、长边 ≤ {PhotoInputValidator.MaxDimension} 像素。",
                UITheme.FontCaption, UITheme.TextDisabled, 34f);

            AddDivider();

            AddText("隐私说明（提交前必须同意）", UITheme.FontSmall, UITheme.TextPrimary, 22f,
                FontStyle.Bold);
            AddText(PrivacyConsentController.ComposeConsentText(), UITheme.FontCaption,
                UITheme.TextSecondary, 132f);

            _consentToggle = UITheme.CreateToggleButton(_content, ConsentToggleLabel, Vector2.zero,
                new Vector2(300f, UITheme.HeightSmall), false, OnConsentToggled,
                "同意后才会开始处理照片；可随时撤销");
            AttachLayout(_consentToggle.gameObject, UITheme.HeightSmall);

            AddDivider();

            AddText("角色风格", UITheme.FontSmall, UITheme.TextPrimary, 22f, FontStyle.Bold);
            var styleRow = AddRow(UITheme.HeightSmall);
            for (int i = 0; i < _styles.Count; i++)
            {
                CharacterStyle style = _styles[i];
                var btn = AddButton(styleRow, DescribeStyle(style), () => SelectStyle(style),
                    ButtonStyle.Secondary, $"使用「{DescribeStyle(style)}」风格生成");
                _styleButtons.Add(btn);
            }
            UITheme.ChainHorizontal(_styleButtons.ToArray());

            _generateButton = AddButton(null, "生成外观", OnGenerateClicked, ButtonStyle.Primary,
                "用当前照片生成外观预览（生成后可先预览再决定是否应用）");

            // ---- 进度行（仅任务运行中显示） ----
            _progressRow = AddRow(ProgressRowHeight);
            _progressSlider = UITheme.CreateSlider(_progressRow, "Progress", Vector2.zero,
                new Vector2(200f, 10f));
            _progressSlider.interactable = false;
            _progressSlider.minValue = 0f;
            _progressSlider.maxValue = 1f;
            _progressSlider.value = 0f;
            AttachLayout(_progressSlider.gameObject, ProgressRowHeight, flexibleWidth: 1f);

            _progressText = UITheme.CreateText(_progressRow, "0%", Vector2.zero, new Vector2(52f, 20f),
                UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.MiddleRight);
            _progressText.horizontalOverflow = HorizontalWrapMode.Overflow;
            AttachLayout(_progressText.gameObject, 20f, preferredWidth: 52f, flexibleWidth: 0f);

            _cancelButton = AddButton(_progressRow, "取消", OnCancelClicked, ButtonStyle.Ghost,
                "取消本次生成，当前外观保持不变");

            // ---- 预览 ----
            _previewRow = AddBlock("PreviewRow", PreviewHeight + 20f);
            var previewBg = UITheme.CreatePanel(_previewRow, "PreviewBg", Vector2.zero,
                new Vector2(300f, PreviewHeight), UITheme.TrackBg, UITheme.RadiusMedium);
            UITheme.StretchAll(previewBg, 0f, 0f, 0f, 20f);
            var previewImageGo = new GameObject("PreviewImage");
            previewImageGo.transform.SetParent(previewBg, false);
            var previewRt = previewImageGo.AddComponent<RectTransform>();
            UITheme.StretchAll(previewRt, UITheme.Space8, UITheme.Space8, UITheme.Space8, UITheme.Space8);
            _previewImage = previewImageGo.AddComponent<RawImage>();
            _previewImage.raycastTarget = false;
            _previewImage.color = Color.white;

            var previewCaption = UITheme.CreateText(_previewRow, "预览（尚未应用）", Vector2.zero,
                new Vector2(240f, 18f), UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(previewCaption.rectTransform, 0f, 0f, 240f, 18f);

            var applyRow = AddRow(UITheme.HeightPrimary);
            _applyButton = AddButton(applyRow, "应用到角色", OnApplyClicked, ButtonStyle.Primary,
                "把预览应用到当前角色（会记录可撤销点）");
            _regenerateButton = AddButton(applyRow, "重新生成", OnRegenerateClicked, ButtonStyle.Secondary,
                "用同一张照片与同一风格重新生成，旧预览在新结果就绪前保留");

            var manageRow = AddRow(UITheme.HeightButton);
            _restoreButton = AddButton(manageRow, "恢复默认", OnRestoreClicked, ButtonStyle.Secondary,
                "恢复该槽位的角色默认外观");
            _undoButton = AddButton(manageRow, "撤销上次", OnUndoClicked, ButtonStyle.Ghost,
                "回到上一次应用之前的外观");
            _deleteButton = AddButton(manageRow, "删除照片与结果", OnDeleteClicked, ButtonStyle.Danger,
                "删除本机保存的原图、生成结果与缩略图，并恢复默认外观");

            // ---- 错误区 ----
            _errorRow = AddBlock("ErrorRow", 62f);
            _errorText = UITheme.CreateText(_errorRow, string.Empty, Vector2.zero, new Vector2(300f, 40f),
                UITheme.FontCaption, UITheme.TextError, TextAnchor.UpperLeft);
            _errorText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _errorText.verticalOverflow = VerticalWrapMode.Truncate;
            UITheme.PlaceTopLeft(_errorText.rectTransform, 0f, 24f, 300f, 36f);

            _retryButton = UITheme.CreateButton(_errorRow, "重试", Vector2.zero, new Vector2(88f, 24f),
                OnRetryClicked, UITheme.FontCaption, ButtonStyle.Secondary, "重试上一次失败的操作");
            UITheme.PlaceRight(_retryButton.GetComponent<RectTransform>(), 0f, 0f, 88f, 24f);

            _storageWarningText = AddText(string.Empty, UITheme.FontCaption, UITheme.TextWarning, 18f);
            _statusText = AddText("尚未生成外观。", UITheme.FontCaption, UITheme.TextSecondary, 18f);

            if (_errorRow != null) _errorRow.gameObject.SetActive(false);
            Refresh(true);
        }

        // ==================== 布局助手 ====================

        private RectTransform AddBlock(string name, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_content, false);
            var rt = go.AddComponent<RectTransform>();
            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = Mathf.Min(height, 20f);
            return rt;
        }

        private Text AddText(string content, int fontSize, Color color, float height,
            FontStyle style = FontStyle.Normal)
        {
            RectTransform host = AddBlock("TextRow", height);
            var text = UITheme.CreateText(host, content, Vector2.zero, new Vector2(300f, height),
                fontSize, color, TextAnchor.UpperLeft, style);
            UITheme.StretchAll(text.rectTransform, 0f, 0f, 0f, 0f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private void AddDivider()
        {
            RectTransform host = AddBlock("DividerRow", 9f);
            var divider = UITheme.CreateDivider(host, "Divider", Vector2.zero, new Vector2(300f, 1f));
            UITheme.StretchX(divider, 0f, 0f, 4f, 1f);
        }

        private RectTransform AddRow(float height)
        {
            RectTransform row = AddBlock("Row", height);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = UITheme.Space8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return row;
        }

        private Button AddButton(RectTransform row, string label, UnityEngine.Events.UnityAction onClick,
            ButtonStyle style, string tooltip)
        {
            Transform parent = row != null ? row : _content;
            var button = UITheme.CreateButton(parent, label, Vector2.zero,
                new Vector2(140f, UITheme.HeightButton), onClick, UITheme.FontSmall, style, tooltip);
            AttachLayout(button.gameObject, UITheme.HeightButton);
            return button;
        }

        private static void AttachLayout(GameObject go, float preferredHeight,
            float preferredWidth = -1f, float flexibleWidth = 1f)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            if (preferredWidth >= 0f) element.preferredWidth = preferredWidth;
            element.flexibleWidth = flexibleWidth;
        }

        // ==================== 交互 ====================

        private void OnUploadClicked()
        {
            if (_service == null || _service.IsBusy) return;

            _picker = new PhotoFilePickerUI(transform, ResolvePickerDirectory(), OnPhotoPicked, OnPickerCanceled);
        }

        private void OnPickerCanceled()
        {
            _picker = null;
            if (_statusText != null) _statusText.text = "未选择照片。";
        }

        /// <summary>
        /// 选完照片只记下路径：真正提交放在「生成外观」，这样隐私同意在提交前完成
        /// （否则用户先选图就会撞上「未同意」的拦截，体验很差）。
        /// </summary>
        private void OnPhotoPicked(string path)
        {
            _picker = null;
            if (_service == null) return;

            _pickedPath = path ?? string.Empty;
            _pickedName = SafeFileName(_pickedPath);
            _lastPickerDirectory = SafeDirectory(_pickedPath);

            ClearInlineError();
            if (_photoText != null)
                _photoText.text = "已选择：" + UITheme.Truncate(_pickedName, 42);
            if (_statusText != null)
                _statusText.text = _consent != null && _consent.IsGranted
                    ? "照片已就绪：点「生成外观」开始生成。"
                    : "照片已就绪：请先勾选上方的隐私同意，再点「生成外观」。";
            Refresh(true);
        }

        private void OnConsentToggled()
        {
            if (_consent == null) return;

            if (_consent.IsGranted)
            {
                _consent.TryRevoke(out string revokeError);
                if (!string.IsNullOrEmpty(revokeError))
                {
                    ShowInlineError(revokeError);
                }
                else
                {
                    ShowInlineError("已撤销同意。已保存的照片仍在本机，可用下方「删除照片与结果」清除。");
                }
            }
            else if (!_consent.TryGrant(out string grantError))
            {
                ShowInlineError(grantError);
            }
            else
            {
                ClearInlineError();
            }

            Refresh(true);
        }

        private void SelectStyle(CharacterStyle style)
        {
            _selectedStyle = style;
            RefreshStyleButtons();
        }

        private void OnGenerateClicked()
        {
            if (_service == null) return;

            if (string.IsNullOrEmpty(_pickedPath))
            {
                ShowInlineError("请先点「选择照片…」挑一张 PNG 或 JPG，再点「生成外观」。");
                return;
            }

            if (!_service.Submit(_pickedPath, _selectedStyle, out string error))
            {
                ShowInlineError(error);
                Refresh(true);
                return;
            }

            ClearInlineError();
            Refresh(true);
        }

        private void OnCancelClicked()
        {
            if (_service == null) return;
            if (!_service.Cancel(out string error)) ShowInlineError(error);
            else ClearInlineError();
            Refresh(true);
        }

        private void OnApplyClicked()
        {
            if (_service == null) return;
            if (!_service.ApplyPreview(_slot, out string error)) ShowInlineError(error);
            else
            {
                ClearInlineError();
                if (!string.IsNullOrEmpty(_service.StorageWarning)) ShowInlineError(_service.StorageWarning);
            }
            Refresh(true);
        }

        private void OnRegenerateClicked()
        {
            if (_service == null) return;
            if (!_service.Regenerate(out string error)) ShowInlineError(error);
            else ClearInlineError();
            Refresh(true);
        }

        private void OnRestoreClicked()
        {
            if (_service == null) return;
            if (!_service.RestoreDefault(_slot, out string error)) ShowInlineError(error);
            else ClearInlineError();
            Refresh(true);
        }

        private void OnUndoClicked()
        {
            if (_service == null) return;
            if (!_service.UndoLastApply(out string error)) ShowInlineError(error);
            else ClearInlineError();
            Refresh(true);
        }

        private void OnDeleteClicked()
        {
            if (_service == null) return;
            if (!_service.DeletePhotoAndResult(out string error))
            {
                ShowInlineError(error);
            }
            else
            {
                ClearInlineError();
                _pickedPath = string.Empty;
                _pickedName = string.Empty;
                if (_photoText != null) _photoText.text = "尚未选择照片。";
                if (_statusText != null) _statusText.text = "照片与生成结果已从本机删除。";
            }
            Refresh(true);
        }

        private void OnRetryClicked()
        {
            if (_service == null) return;

            string error = string.Empty;
            string submitError = string.Empty;
            if (_service.HasSourcePhoto && _service.Regenerate(out error))
            {
                ClearInlineError();
                Refresh(true);
                return;
            }

            if (!string.IsNullOrEmpty(_pickedPath) && _service.Submit(_pickedPath, _selectedStyle, out submitError))
            {
                ClearInlineError();
            }
            else
            {
                if (string.IsNullOrEmpty(error)) error = submitError;
                ShowInlineError(string.IsNullOrEmpty(error) ? "没有可重试的操作，请重新选择照片。" : error);
            }
            Refresh(true);
        }

        private void ShowInlineError(string message)
        {
            _showingServiceError = false;
            if (_errorText != null) _errorText.text = message ?? string.Empty;
            if (_errorRow != null) _errorRow.gameObject.SetActive(true);
        }

        private void ClearInlineError()
        {
            _showingServiceError = false;
            if (_errorText != null) _errorText.text = string.Empty;
            if (_errorRow != null) _errorRow.gameObject.SetActive(false);
        }

        // ==================== 刷新 ====================

        private void Refresh(bool force)
        {
            if (_service == null) return;

            int signature = ComputeSignature();
            if (force || signature != _lastSignature)
            {
                _lastSignature = signature;
                RefreshControls();
            }

            int percent = UITheme.Percent(_service.Progress01);
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                if (_progressText != null) _progressText.text = percent + "%";
                if (_progressSlider != null) _progressSlider.value = _service.Progress01;
            }

            string status = ComposeStatus();
            if (status != _lastStatusText)
            {
                _lastStatusText = status;
                if (_statusText != null) _statusText.text = status;
            }

            if (_previewImage != null && !ReferenceEquals(_previewImage.texture, _service.Preview))
                _previewImage.texture = _service.Preview;

            if (_previewRow != null && _previewRow.gameObject.activeSelf != _service.HasPreview)
                _previewRow.gameObject.SetActive(_service.HasPreview);

            string warning = _service.StorageWarning ?? string.Empty;
            if (_storageWarningText != null && _storageWarningText.text != warning)
                _storageWarningText.text = warning;

            string errorReason = _service.LastErrorReason ?? string.Empty;
            if (!string.IsNullOrEmpty(errorReason))
            {
                string body = errorReason + (string.IsNullOrEmpty(_service.LastErrorGuidance)
                    ? string.Empty
                    : "\n下一步：" + _service.LastErrorGuidance);
                if (_errorText != null && _errorText.text != body) _errorText.text = body;
                if (_errorRow != null && !_errorRow.gameObject.activeSelf)
                    _errorRow.gameObject.SetActive(true);
                _showingServiceError = true;
            }
            else if (_showingServiceError)
            {
                // 服务端错误已恢复（例如重试成功）：自动收掉错误区，不留下过期文案
                _showingServiceError = false;
                if (_errorText != null) _errorText.text = string.Empty;
                if (_errorRow != null) _errorRow.gameObject.SetActive(false);
            }
        }

        private int ComputeSignature()
        {
            int signature = (int)_service.Phase;
            if (_service.IsBusy) signature |= 1 << 8;
            if (_service.HasPreview) signature |= 1 << 9;
            if (_service.HasSourcePhoto) signature |= 1 << 10;
            if (_consent != null && _consent.IsGranted) signature |= 1 << 11;
            if (_service.CanUndo) signature |= 1 << 12;
            if (_service.Applier != null && _service.Applier.HasApplied(_slot)) signature |= 1 << 13;
            if (!string.IsNullOrEmpty(_service.LastErrorReason)) signature |= 1 << 14;
            if (!string.IsNullOrEmpty(_service.SelectedPhotoFileName)) signature |= 1 << 15;
            if (IsSlotSupported()) signature |= 1 << 16;
            return signature;
        }

        private void RefreshControls()
        {
            bool busy = _service.IsBusy;
            bool granted = _consent != null && _consent.IsGranted;
            bool hasPreview = _service.HasPreview;
            bool hasSource = _service.HasSourcePhoto;
            bool hasPhotoChosen = !string.IsNullOrEmpty(_pickedPath) || hasSource
                || !string.IsNullOrEmpty(_service.SelectedPhotoFileName);

            UITheme.SetToggleState(_consentToggle, ConsentToggleLabel, granted, !busy);

            UITheme.SetButtonEnabled(_uploadButton, !busy, "生成进行中：完成或取消后才能更换照片");
            UITheme.SetButtonEnabled(_generateButton, !busy && granted && hasPhotoChosen,
                granted ? "请先选择照片，或等待当前生成结束" : "请先同意隐私说明");

            for (int i = 0; i < _styleButtons.Count; i++)
            {
                UITheme.SetButtonEnabled(_styleButtons[i], !busy, "生成进行中不能切换风格");
            }
            RefreshStyleButtons();

            bool slotSupported = IsSlotSupported();
            UITheme.SetButtonEnabled(_applyButton, hasPreview && !busy && slotSupported,
                !slotSupported
                    ? "当前角色不支持此槽位：请改用「缩略图」或「整体色调」"
                    : (hasPreview ? "生成尚未结束，请等待完成" : "请先生成预览，确认后再应用"));
            UITheme.SetButtonEnabled(_regenerateButton, hasSource && !busy,
                hasSource ? "生成进行中，请稍候" : "请先选择照片");
            UITheme.SetButtonEnabled(_restoreButton,
                !busy && _service.Applier != null && _service.Applier.HasApplied(_slot),
                "当前槽位没有可恢复的自定义外观");
            UITheme.SetButtonEnabled(_undoButton, !busy && _service.CanUndo, "没有可撤销的外观修改");
            UITheme.SetButtonEnabled(_deleteButton, !busy, "生成进行中：完成或取消后才能删除");

            bool hasRetryableError = !busy && (hasSource || hasPhotoChosen);
            UITheme.SetButtonEnabled(_retryButton, hasRetryableError,
                hasRetryableError ? null : "请先选择照片");

            if (_progressRow != null && _progressRow.gameObject.activeSelf != busy)
                _progressRow.gameObject.SetActive(busy);
            if (_cancelButton != null) _cancelButton.gameObject.SetActive(busy);

            if (_photoText != null)
            {
                string name = string.IsNullOrEmpty(_pickedName) ? _service.SelectedPhotoFileName : _pickedName;
                _photoText.text = string.IsNullOrEmpty(name)
                    ? "尚未选择照片。"
                    : "已选择：" + UITheme.Truncate(name, 42);
            }
        }

        private void RefreshStyleButtons()
        {
            bool busy = _service != null && _service.IsBusy;

            for (int i = 0; i < _styleButtons.Count; i++)
            {
                bool on = _styles[i] == _selectedStyle;

                // 生成中必须保持禁用：这里不能用「选中/默认」覆盖掉禁用态
                UITheme.ApplyButtonState(_styleButtons[i],
                    busy ? ButtonState.Disabled : (on ? ButtonState.Selected : ButtonState.Default),
                    ButtonStyle.Secondary);

                UITheme.SetButtonLabel(_styleButtons[i],
                    (on ? "✓ " : "") + DescribeStyle(_styles[i]));
            }
        }

        private string ComposeStatus()
        {
            switch (_service.Phase)
            {
                case CustomizationPhase.ValidatingPhoto:
                    return "正在读取并校验照片…";
                case CustomizationPhase.Generating:
                    return "正在生成外观…可随时点「取消」，当前外观保持不变。";
                case CustomizationPhase.PreviewReady:
                    return IsSlotSupported()
                        ? "预览已就绪：确认满意后再点「应用到角色」。"
                        : $"预览已就绪，但当前角色不支持「{DescribeSlot(_slot)}」槽位：请改用「缩略图」或「整体色调」。";
                case CustomizationPhase.Applied:
                    return "已应用到角色。可随时「恢复默认」或「撤销上次」。";
                default:
                    if (_service.HasSourcePhoto && string.IsNullOrEmpty(_service.LastErrorReason))
                        return "可以点「重新生成」再试一次，或重新选择照片。";
                    return "尚未生成外观。";
            }
        }

        private static string DescribeSlot(AppearanceSlot slot) => slot switch
        {
            AppearanceSlot.Thumbnail => "缩略图",
            AppearanceSlot.FaceTexture => "脸部贴图",
            AppearanceSlot.HeadMaterial => "头部材质",
            AppearanceSlot.BodyTint => "整体色调",
            _ => "外观"
        };

        private static string DescribeStyle(CharacterStyle style) => style switch
        {
            CharacterStyle.Realistic => "写实",
            CharacterStyle.Cartoon => "卡通",
            CharacterStyle.Anime => "动漫",
            _ => "自定义"
        };

        // ==================== 小助手 ====================

        /// <summary>选择器的起始目录：上次选过就用上次的目录，否则「图片」文件夹。</summary>
        private static string ResolvePickerDirectory()
        {
            if (!string.IsNullOrEmpty(_lastPickerDirectory) && Directory.Exists(_lastPickerDirectory))
                return _lastPickerDirectory;

            try
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (!string.IsNullOrEmpty(pictures) && Directory.Exists(pictures)) return pictures;
            }
            catch (Exception)
            {
                // 忽略：回落到用户数据目录
            }
            return Application.persistentDataPath;
        }

        private static string SafeFileName(string path)
        {
            try { return Path.GetFileName(path) ?? string.Empty; }
            catch (ArgumentException) { return string.Empty; }
        }

        private static string SafeDirectory(string path)
        {
            try { return Path.GetDirectoryName(path) ?? string.Empty; }
            catch (ArgumentException) { return string.Empty; }
        }
    }

    /// <summary>
    /// 最小照片文件选择器（不依赖原生对话框、不引入第三方包）。
    /// <para>
    /// 为什么不用 <see cref="FileBrowserUI"/>：它按 MIDI 扩展名写死过滤与文案，
    /// 复用会让用户看到「选择 MIDI 文件」；这里只复制最少的目录导航逻辑。
    /// </para>
    /// <para>只列出 PNG / JPG；分页代替滚动条；Esc 取消；回调只会触发一次。</para>
    /// </summary>
    internal sealed class PhotoFilePickerUI
    {
        private const int CanvasSortingOrder = 700;
        private const int RowsPerPage = 9;
        private const float RowHeight = 30f;
        private const float RowStep = 32f;
        private const int MaxEntriesPerDirectory = 400;
        private const float DoubleClickSeconds = 0.4f;

        private struct Entry
        {
            public string Path;
            public string Name;
            public bool IsDirectory;
        }

        private readonly Action<string> _onPick;
        private readonly Action _onCancel;
        private readonly List<Entry> _entries = new List<Entry>();

        private GameObject _root;
        private RectTransform _listHost;
        private Text _pathText;
        private Text _messageText;
        private Text _selectionText;
        private Button _prevButton;
        private Button _nextButton;

        private string _directory;
        private int _page;
        private string _selected;
        private int _lastClickIndex = -1;
        private float _lastClickTime;
        private bool _closed;

        public PhotoFilePickerUI(Transform parent, string startDirectory, Action<string> onPick, Action onCancel)
        {
            _onPick = onPick;
            _onCancel = onCancel;

            Build(parent);
            Open(startDirectory);
        }

        /// <summary>每帧调用。返回 false 表示已关闭。</summary>
        public bool Tick()
        {
            if (_closed) return false;
            if (UITheme.ConsumeEscape())
            {
                Finish(null);
                return false;
            }
            return true;
        }

        /// <summary>静默关闭并销毁（幂等，不触发任何回调；用于宿主销毁时的清理）。</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        private void Build(Transform parent)
        {
            UITheme.EnsureEventSystem();

            _root = new GameObject("PhotoFilePicker");
            _root.transform.SetParent(parent, false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = UITheme.Layout.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            _root.AddComponent<GraphicRaycaster>();

            var overlay = UITheme.CreatePanel(_root.transform, "Overlay", Vector2.zero,
                new Vector2(64f, 64f), UITheme.SceneBg);
            UITheme.StretchAll(overlay, 0f, 0f, 0f, 0f);

            var panel = UITheme.CreatePanel(overlay, "Panel", Vector2.zero, new Vector2(720f, 540f),
                UITheme.PanelBg, UITheme.RadiusLarge);
            UITheme.PlaceCenter(panel, 720f, 540f);

            var title = UITheme.CreateText(panel, "选择照片", Vector2.zero, new Vector2(420f, 28f),
                UITheme.FontTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.PlaceTopLeft(title.rectTransform, 20f, 14f, 420f, 28f);

            var hint = UITheme.CreateText(panel, "只列出 PNG / JPG；单击选中，双击或点「选择」确认。",
                Vector2.zero, new Vector2(680f, 18f), UITheme.FontCaption, UITheme.TextSecondary,
                TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(hint.rectTransform, 20f, 44f, 680f, 18f);

            _pathText = UITheme.CreateText(panel, "", Vector2.zero, new Vector2(680f, 20f),
                UITheme.FontCaption, UITheme.Accent, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_pathText.rectTransform, 20f, 66f, 680f, 20f);

            _listHost = UITheme.CreatePanel(panel, "List", Vector2.zero, new Vector2(680f, 288f),
                UITheme.Transparent);
            UITheme.PlaceTopLeft(_listHost, 20f, 90f, 680f, 288f);
            var listImage = _listHost.GetComponent<Image>();
            if (listImage != null) listImage.raycastTarget = false;

            _messageText = UITheme.CreateText(panel, "", Vector2.zero, new Vector2(680f, 18f),
                UITheme.FontCaption, UITheme.TextWarning, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_messageText.rectTransform, 20f, 384f, 680f, 18f);

            _selectionText = UITheme.CreateText(panel, "尚未选择文件", Vector2.zero, new Vector2(440f, 20f),
                UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(_selectionText.rectTransform, 20f, 406f, 440f, 20f);

            var drivesBtn = UITheme.CreateButton(panel, "驱动器", Vector2.zero, new Vector2(96f, 30f),
                () => Open(null), UITheme.FontCaption, ButtonStyle.Ghost, "回到驱动器列表");
            UITheme.Place(drivesBtn.GetComponent<RectTransform>(), 20f, 46f, 96f, 30f);

            var upBtn = UITheme.CreateButton(panel, "上一级", Vector2.zero, new Vector2(96f, 30f),
                GoUp, UITheme.FontCaption, ButtonStyle.Ghost, "返回上一级文件夹");
            UITheme.Place(upBtn.GetComponent<RectTransform>(), 124f, 46f, 96f, 30f);

            _prevButton = UITheme.CreateButton(panel, "上一页", Vector2.zero, new Vector2(88f, 30f),
                () => ChangePage(-1), UITheme.FontCaption, ButtonStyle.Ghost, "查看上一页");
            UITheme.Place(_prevButton.GetComponent<RectTransform>(), 236f, 46f, 88f, 30f);

            _nextButton = UITheme.CreateButton(panel, "下一页", Vector2.zero, new Vector2(88f, 30f),
                () => ChangePage(1), UITheme.FontCaption, ButtonStyle.Ghost, "查看下一页");
            UITheme.Place(_nextButton.GetComponent<RectTransform>(), 332f, 46f, 88f, 30f);

            var cancelBtn = UITheme.CreateButton(panel, "取消", Vector2.zero, new Vector2(120f, 36f),
                () => Finish(null), UITheme.FontSmall, ButtonStyle.Secondary, "关闭选择器，不更换照片");
            UITheme.PlaceRight(cancelBtn.GetComponent<RectTransform>(), 156f, 18f, 120f, 36f);

            var pickBtn = UITheme.CreateButton(panel, "选择", Vector2.zero, new Vector2(136f, 36f),
                Confirm, UITheme.FontSmall, ButtonStyle.Primary, "使用已选中的照片");
            UITheme.PlaceRight(pickBtn.GetComponent<RectTransform>(), 20f, 18f, 136f, 36f);
        }

        private void Open(string directory)
        {
            _directory = string.IsNullOrEmpty(directory) || !Directory.Exists(directory) ? null : directory;
            _page = 0;
            _selected = null;
            _lastClickIndex = -1;
            if (_selectionText != null) _selectionText.text = "尚未选择文件";
            Reload();
        }

        private void GoUp()
        {
            if (string.IsNullOrEmpty(_directory)) return;
            string parent = null;
            try { parent = Path.GetDirectoryName(_directory); }
            catch (ArgumentException) { parent = null; }
            Open(parent);
        }

        private void ChangePage(int delta)
        {
            int totalPages = Mathf.Max(1, (_entries.Count + RowsPerPage - 1) / RowsPerPage);
            int page = Mathf.Clamp(_page + delta, 0, totalPages - 1);
            if (page == _page) return;
            _page = page;
            _lastClickIndex = -1;
            RebuildRows();
        }

        private void Reload()
        {
            _entries.Clear();
            string problem = null;

            if (string.IsNullOrEmpty(_directory)) LoadDrives();
            else problem = LoadDirectory(_directory);

            if (_pathText != null)
                _pathText.text = string.IsNullOrEmpty(_directory)
                    ? "此电脑（驱动器列表）"
                    : Ellipsize(_directory, 88);

            RebuildRows();
            if (_messageText != null)
            {
                if (!string.IsNullOrEmpty(problem))
                {
                    _messageText.color = UITheme.TextError;
                    _messageText.text = problem;
                }
                else
                {
                    int totalPages = Mathf.Max(1, (_entries.Count + RowsPerPage - 1) / RowsPerPage);
                    _messageText.color = UITheme.TextSecondary;
                    _messageText.text = _entries.Count == 0
                        ? "这个文件夹里没有 PNG / JPG 照片。"
                        : $"第 {_page + 1}/{totalPages} 页　·　共 {_entries.Count} 项";
                }
            }
        }

        private void LoadDrives()
        {
            string[] drives;
            try { drives = Directory.GetLogicalDrives(); }
            catch (Exception) { drives = new string[0]; }

            Array.Sort(drives, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < drives.Length; i++)
                _entries.Add(new Entry { Path = drives[i], Name = drives[i], IsDirectory = true });
        }

        private string LoadDirectory(string directory)
        {
            string[] directories;
            string[] files;
            try { directories = Directory.GetDirectories(directory); }
            catch (UnauthorizedAccessException)
            {
                return "没有权限访问这个文件夹。请返回上一级或换一个位置。";
            }
            catch (DirectoryNotFoundException)
            {
                return "这个文件夹已不存在，可能被移动或删除。请返回上一级。";
            }
            catch (Exception)
            {
                return "无法打开这个文件夹。请返回上一级或换一个位置。";
            }

            try { files = Directory.GetFiles(directory); }
            catch (Exception) { files = new string[0]; }

            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < directories.Length && _entries.Count < MaxEntriesPerDirectory; i++)
            {
                string name = SafeName(directories[i]);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                _entries.Add(new Entry { Path = directories[i], Name = name, IsDirectory = true });
            }

            for (int i = 0; i < files.Length && _entries.Count < MaxEntriesPerDirectory; i++)
            {
                if (!PhotoInputValidator.HasAllowedExtension(files[i])) continue;
                string name = SafeName(files[i]);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                _entries.Add(new Entry { Path = files[i], Name = name, IsDirectory = false });
            }

            return null;
        }

        private void RebuildRows()
        {
            if (_listHost == null) return;

            for (int i = _listHost.childCount - 1; i >= 0; i--)
            {
                Transform child = _listHost.GetChild(i);
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(child.gameObject);
            }

            int totalPages = Mathf.Max(1, (_entries.Count + RowsPerPage - 1) / RowsPerPage);
            _page = Mathf.Clamp(_page, 0, totalPages - 1);

            int start = _page * RowsPerPage;
            for (int row = 0; row < RowsPerPage; row++)
            {
                int index = start + row;
                if (index >= _entries.Count) break;

                Entry entry = _entries[index];
                string label = entry.IsDirectory ? "▸ " + entry.Name : entry.Name;
                var button = UITheme.CreateButton(_listHost, label, Vector2.zero, new Vector2(680f, RowHeight),
                    () => OnRowClicked(index), UITheme.FontCaption,
                    entry.IsDirectory ? ButtonStyle.Ghost : ButtonStyle.Secondary,
                    entry.IsDirectory ? "进入文件夹" : "选中这张照片");

                var rt = button.GetComponent<RectTransform>();
                if (rt != null) UITheme.PlaceTopLeft(rt, 0f, row * RowStep, 680f, RowHeight);

                var text = UITheme.GetLabel(button);
                if (text != null) text.alignment = TextAnchor.MiddleLeft;
            }

            if (_prevButton != null) _prevButton.interactable = _page > 0;
            if (_nextButton != null) _nextButton.interactable = _page < totalPages - 1;
        }

        private void OnRowClicked(int index)
        {
            if (_closed || index < 0 || index >= _entries.Count) return;
            Entry entry = _entries[index];

            float now = Time.unscaledTime;
            bool doubleClick = _lastClickIndex == index && (now - _lastClickTime) <= DoubleClickSeconds;
            _lastClickIndex = index;
            _lastClickTime = now;

            if (entry.IsDirectory)
            {
                Open(entry.Path);
                return;
            }

            _selected = entry.Path;
            if (_selectionText != null)
                _selectionText.text = "已选择：" + UITheme.Truncate(entry.Name, 44);

            if (doubleClick) Confirm();
        }

        private void Confirm()
        {
            if (string.IsNullOrEmpty(_selected))
            {
                if (_messageText != null)
                {
                    _messageText.color = UITheme.TextWarning;
                    _messageText.text = "请先在列表中选中一张照片。";
                }
                return;
            }

            string picked = _selected;
            Finish(picked);
        }

        private void Finish(string pickedPath)
        {
            if (_closed) return;
            _closed = true;

            var pick = _onPick;
            var cancel = _onCancel;

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }

            // 回调放在销毁之后：回调里可以安全地打开新的面板
            if (!string.IsNullOrEmpty(pickedPath))
            {
                if (pick != null) pick(pickedPath);
            }
            else
            {
                if (cancel != null) cancel();
            }
        }

        private static string SafeName(string path)
        {
            try { return Path.GetFileName(path) ?? string.Empty; }
            catch (ArgumentException) { return string.Empty; }
        }

        private static string Ellipsize(string path, int maxChars)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            if (path.Length <= maxChars) return path;
            if (maxChars <= 4) return "…";
            return "…" + path.Substring(path.Length - (maxChars - 1));
        }
    }
}
