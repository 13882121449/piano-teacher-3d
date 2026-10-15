using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 统一 UI 主题：配色 / 尺寸 / 控件工厂。
    /// 深色层次背景 + 暖金强调色，呼应钢琴（木质、优雅）主题。
    /// 三个 UI 脚本共用，保证视觉一致；纯表现层，不涉及业务逻辑。
    /// </summary>
    public static class UITheme
    {
        // ==================== 配色（层次递进） ====================

        /// <summary>场景背景（最深）。</summary>
        public static readonly Color SceneBg = new Color(0.075f, 0.082f, 0.098f, 1f);

        /// <summary>面板底色（带轻微透明，浮于场景之上）。</summary>
        public static readonly Color PanelBg = new Color(0.118f, 0.133f, 0.157f, 0.96f);

        /// <summary>控件底色（按钮常态）。</summary>
        public static readonly Color Control = new Color(0.165f, 0.184f, 0.216f, 1f);

        /// <summary>控件悬停色。</summary>
        public static readonly Color ControlHover = new Color(0.235f, 0.259f, 0.302f, 1f);

        /// <summary>控件按下色。</summary>
        public static readonly Color ControlPressed = new Color(0.106f, 0.118f, 0.141f, 1f);

        // ==================== 强调色（暖金） ====================

        public static readonly Color Accent = new Color(0.851f, 0.643f, 0.255f, 1f);        // #D9A441
        public static readonly Color AccentBright = new Color(0.941f, 0.776f, 0.455f, 1f);  // #F0C674
        public static readonly Color AccentDim = new Color(0.545f, 0.412f, 0.165f, 1f);

        // ==================== 文字 ====================

        public static readonly Color TextPrimary = new Color(0.925f, 0.937f, 0.945f, 1f);
        public static readonly Color TextSecondary = new Color(0.565f, 0.643f, 0.682f, 1f);
        public static readonly Color TextOnAccent = new Color(0.098f, 0.110f, 0.133f, 1f);

        // ==================== 状态色 ====================

        public static readonly Color Error = new Color(0.898f, 0.451f, 0.451f, 1f);
        public static readonly Color Success = new Color(0.298f, 0.686f, 0.314f, 1f);

        // ==================== 尺寸 ====================

        public const int FontTitle = 24;
        public const int FontButton = 18;
        public const int FontSmall = 15;
        public const float ButtonFadeSeconds = 0.08f;

        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return _font;
            }
        }

        private static Sprite _rounded;
        private static Sprite _knob;

        /// <summary>
        /// 圆角九宫格 sprite（程序化生成，不依赖内置资源 —— 内置 UISprite.psd 在部分环境不可用）。
        /// </summary>
        public static Sprite RoundedSprite
        {
            get
            {
                if (_rounded == null) _rounded = CreateRoundedSprite(48, 14);
                return _rounded;
            }
        }

        /// <summary>圆形 sprite（程序化生成，用于滑块手柄）。</summary>
        public static Sprite KnobSprite
        {
            get
            {
                if (_knob == null) _knob = CreateCircleSprite(32);
                return _knob;
            }
        }

        private static Sprite CreateRoundedSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "UITheme_Rounded"
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Mathf.Clamp(px, radius, size - radius);
                    float cy = Mathf.Clamp(py, radius, size - radius);
                    float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f); // 1px 抗锯齿
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        private static Sprite CreateCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "UITheme_Circle"
            };
            float r = size * 0.5f - 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f;
                    float dy = y + 0.5f - size * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ==================== 控件工厂 ====================

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        public static Canvas CreateCanvas(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>创建圆角面板。</summary>
        public static RectTransform CreatePanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = color;
            if (RoundedSprite != null)
            {
                img.sprite = RoundedSprite;
                img.type = Image.Type.Sliced;
            }
            return rt;
        }

        public static Text CreateText(Transform parent, string content, Vector2 pos, Vector2 size,
            int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var text = go.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>创建圆角按钮（含 hover/press 平滑过渡）。</summary>
        public static Button CreateButton(Transform parent, string label, Vector2 pos, Vector2 size,
            UnityEngine.Events.UnityAction onClick, int fontSize = FontButton)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = Control;
            if (RoundedSprite != null)
            {
                img.sprite = RoundedSprite;
                img.type = Image.Type.Sliced;
            }

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            colors.fadeDuration = ButtonFadeSeconds;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);

            CreateText(go.transform, label, Vector2.zero, size, fontSize, TextPrimary);
            return btn;
        }

        /// <summary>切换按钮激活态（激活时用强调色底 + 深色文字）。</summary>
        public static void SetButtonActive(Button btn, bool active)
        {
            if (btn == null) return;
            if (btn.targetGraphic is Image img)
                img.color = active ? Accent : Control;
            var text = btn.GetComponentInChildren<Text>();
            if (text != null)
                text.color = active ? TextOnAccent : TextPrimary;
        }

        /// <summary>创建进度条（圆角轨道 + 强调色填充 + 圆形手柄）。</summary>
        public static Slider CreateSlider(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            // 轨道
            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.09f, 0.10f, 0.12f, 1f);
            if (RoundedSprite != null) { bgImg.sprite = RoundedSprite; bgImg.type = Image.Type.Sliced; }

            // 填充区
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var faRt = fillArea.AddComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
            faRt.offsetMin = new Vector2(4f, 4f); faRt.offsetMax = new Vector2(-4f, -4f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRt = fill.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = new Vector2(10f, 0f);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = Accent;
            if (RoundedSprite != null) { fillImg.sprite = RoundedSprite; fillImg.type = Image.Type.Sliced; }

            // 手柄
            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var haRt = handleArea.AddComponent<RectTransform>();
            haRt.anchorMin = Vector2.zero; haRt.anchorMax = Vector2.one;
            haRt.offsetMin = new Vector2(6f, 0f); haRt.offsetMax = new Vector2(-6f, 0f);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var hRt = handle.AddComponent<RectTransform>();
            hRt.sizeDelta = new Vector2(14f, 14f);
            var hImg = handle.AddComponent<Image>();
            hImg.color = AccentBright;
            hImg.sprite = KnobSprite;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = hRt;
            slider.targetGraphic = hImg;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            return slider;
        }
    }
}
