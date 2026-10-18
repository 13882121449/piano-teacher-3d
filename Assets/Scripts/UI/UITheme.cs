using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 按钮语义样式（对应方案 3.3「强调色只用于真正重要的状态」/ 审查 2.2B 视觉层级）：
    /// Primary = 主操作（全屏唯一实心强调）、Secondary = 次操作、Ghost = 低频/顶栏弱化、
    /// Danger = 危险或破坏性操作、Toggle = 开关（必须同时显示「：开 / ：关」文字）。
    /// </summary>
    public enum ButtonStyle
    {
        Primary,
        Secondary,
        Ghost,
        Danger,
        Toggle
    }

    /// <summary>
    /// 按钮七个状态（对应方案 3.3「按钮至少定义默认、悬停、按下、选中、禁用、加载、错误状态」
    /// / 审查 2.2A 状态令牌）。
    /// <para>悬停/按下由 <see cref="Selectable"/> 的 ColorBlock 承担；其余状态由
    /// <see cref="UITheme.ApplyButtonState"/> 显式设置，选中态同时改变边框与文字，避免只靠颜色区分。</para>
    /// </summary>
    public enum ButtonState
    {
        Default = 0,
        Hover = 1,
        Pressed = 2,
        Selected = 3,
        Disabled = 4,
        Loading = 5,
        Error = 6
    }

    /// <summary>
    /// 统一 UI 主题：设计令牌（颜色 / 字号 / 间距 / 圆角 / 控件高度 / 状态）+ 程序化控件工厂。
    /// 深色层次背景 + 暖金强调色，呼应钢琴（木质、优雅）主题。
    /// <para>
    /// 对应方案 3.3「建立统一的主色、辅助色、背景色、边框色、危险色和禁用色」与
    /// 审查 2.2A「在 UITheme.cs 中定义完整的设计令牌」：所有 UI 脚本只允许引用本类令牌，
    /// 不得自行硬编码颜色 / 圆角 / 控件高度。
    /// </para>
    /// <para>纯表现层，不涉及业务逻辑；会话层只读取 <see cref="SceneBg"/>。</para>
    /// </summary>
    public static class UITheme
    {
        // ==================== 配色（层次递进） ====================

        /// <summary>场景背景（最深）。由 <c>Core/LessonSessionController</c> 直接使用，务必保留。</summary>
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

        // ==================== 新增令牌：层次 / 分割 / 状态（方案 3.3 / 审查 2.2A） ====================

        /// <summary>抬升面板底色：次级功能卡片、分段控件轨道之上的一级。</summary>
        public static readonly Color PanelBgRaised = new Color(0.157f, 0.176f, 0.204f, 1f);

        /// <summary>顶栏 / 底栏底色（比普通面板更深，压住 3D 画面又不抢眼）。</summary>
        public static readonly Color PanelBgDeep = new Color(0.094f, 0.106f, 0.125f, 0.97f);

        /// <summary>分割线（半透明，不与强调色争抢注意力）。</summary>
        public static readonly Color Divider = new Color(0.850f, 0.878f, 0.918f, 0.14f);

        /// <summary>
        /// 选中态底色（分段选择器选中项 / 开关开启项）。
        /// 比 <see cref="AccentDim"/> 更深，保证与 <see cref="TextPrimary"/> 的对比度 ≥ 4.5:1。
        /// </summary>
        public static readonly Color Selected = new Color(0.435f, 0.325f, 0.125f, 1f);

        /// <summary>警告色（功能降级但可继续）。</summary>
        public static readonly Color Warning = new Color(0.960f, 0.741f, 0.365f, 1f);

        /// <summary>禁用底色。</summary>
        public static readonly Color Disabled = new Color(0.145f, 0.161f, 0.184f, 1f);

        /// <summary>禁用文字色。</summary>
        public static readonly Color TextDisabled = new Color(0.435f, 0.478f, 0.510f, 1f);

        /// <summary>错误文字色（比底色更亮，保证暗色面板上的对比度）。</summary>
        public static readonly Color TextError = new Color(0.949f, 0.639f, 0.639f, 1f);

        /// <summary>警告文字色。</summary>
        public static readonly Color TextWarning = new Color(0.976f, 0.827f, 0.545f, 1f);

        /// <summary>危险 / 破坏性操作底色（需配合确认弹窗）。</summary>
        public static readonly Color DangerBg = new Color(0.545f, 0.216f, 0.216f, 1f);

        /// <summary>进度条轨道底色。</summary>
        public static readonly Color TrackBg = new Color(0.090f, 0.100f, 0.120f, 1f);

        /// <summary>完全透明（纯布局容器，不参与渲染与射线检测）。</summary>
        public static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        /// <summary>按钮/面板边框色（选中态、焦点态共用；颜色之外的第二重区分）。</summary>
        public static readonly Color BorderSelected = new Color(0.941f, 0.776f, 0.455f, 1f);

        // ==================== 教学语义色（审查 2.3「颜色语义不变」） ====================

        /// <summary>左手发声提示色（冷蓝，保持既有语义）。</summary>
        public static readonly Color LeftHandColor = new Color(0.396f, 0.663f, 0.933f, 1f);

        /// <summary>右手发声提示色（暖橙，保持既有语义）。</summary>
        public static readonly Color RightHandColor = new Color(0.949f, 0.612f, 0.318f, 1f);

        /// <summary>预提示色（暖金，与 <see cref="Accent"/> 同源，但语义只用于琴键提示）。</summary>
        public static readonly Color UpcomingColor = new Color(0.851f, 0.643f, 0.255f, 1f);

        // ==================== 字号层级（审查 2.2A） ====================

        /// <summary>字号：页面标题（主菜单 / 空状态）。</summary>
        public const int FontPageTitle = 34;

        /// <summary>字号：曲目标题 / 区块标题（兼容旧代码的通用标题层级）。</summary>
        public const int FontTitle = 24;

        /// <summary>字号：曲目标题（面板内）。</summary>
        public const int FontSongTitle = 22;

        /// <summary>字号：主要操作按钮。</summary>
        public const int FontAction = 20;

        /// <summary>字号：主要操作（兼容旧代码：普通按钮）。</summary>
        public const int FontButton = 18;

        /// <summary>字号：正文。</summary>
        public const int FontBody = 16;

        /// <summary>字号：辅助说明（兼容旧代码）。</summary>
        public const int FontSmall = 15;

        /// <summary>字号：辅助说明 / 状态标签。</summary>
        public const int FontCaption = 13;

        /// <summary>字号：状态标签（顶栏状态、模式标签）。</summary>
        public const int FontStatusLabel = 13;

        // ==================== 间距等级（审查 2.2A：4 / 8 / 12 / 16 / 24） ====================

        public const float Space4 = 4f;
        public const float Space8 = 8f;
        public const float Space12 = 12f;
        public const float Space16 = 16f;
        public const float Space24 = 24f;

        // ==================== 圆角等级（审查 2.2A：小 / 中 / 大） ====================

        /// <summary>圆角：小型输入 / 标签 / 分段项。</summary>
        public const int RadiusSmall = 8;

        /// <summary>圆角：中型按钮 / 卡片。</summary>
        public const int RadiusMedium = 14;

        /// <summary>圆角：大型面板。</summary>
        public const int RadiusLarge = 22;

        // ==================== 控件高度（审查 2.2A） ====================

        /// <summary>主按钮高度（播放 / 暂停）。</summary>
        public const float HeightPrimary = 48f;

        /// <summary>普通按钮高度。</summary>
        public const float HeightButton = 40f;

        /// <summary>输入类控件高度（分段选择器 / 滑块行）。</summary>
        public const float HeightInput = 36f;

        /// <summary>小按钮 / 状态标签高度。</summary>
        public const float HeightSmall = 32f;

        // ==================== 动画 / 交互常量 ====================

        public const float ButtonFadeSeconds = 0.08f;

        /// <summary>悬停提示延迟显示时间（秒）。</summary>
        public const float TooltipDelaySeconds = 0.4f;

        /// <summary>轻量提示（toast）自动消失时间（秒）。</summary>
        public const float ToastSeconds = 4f;

        /// <summary>分析面板滑出 / 收起速度（像素/秒，参考分辨率下）。</summary>
        public const float PanelSlideSpeed = 3200f;

        // ==================== 布局令牌（各 UI 脚本只引用这里，不自行写死尺寸） ====================

        /// <summary>
        /// 三层结构的布局令牌（方案 3.1：顶栏 / 3D 演奏区 / 进度区 / 主控制 / 次级工具）。
        /// 数值均在 CanvasScaler 参考分辨率（1280×720）下有效。
        /// </summary>
        public static class Layout
        {
            /// <summary>参考分辨率（与 CanvasScaler.referenceResolution 一致）。</summary>
            public static readonly Vector2 ReferenceResolution = new Vector2(1280f, 720f);

            /// <summary>屏幕四周统一外边距。</summary>
            public const float ScreenMargin = Space12;

            /// <summary>顶栏高度。</summary>
            public const float StatusBarHeight = 44f;

            /// <summary>主播放栏高度（常规）。</summary>
            public const float MainBarHeightNormal = 92f;

            /// <summary>主播放栏高度（手部特写：尽量矮，避免遮挡琴键与双手）。</summary>
            public const float MainBarHeightCompact = 60f;

            /// <summary>练习工具栏高度（展开：标题行 + 两行控件）。</summary>
            public const float ToolbarHeightExpanded = 124f;

            /// <summary>练习工具栏高度（折叠：仅保留「练习设置」标题行，入口不丢失）。</summary>
            public const float ToolbarHeightCollapsed = 36f;

            /// <summary>练习工具栏标题行高度。</summary>
            public const float ToolbarHeaderHeight = 28f;

            /// <summary>提示条（toast）高度。</summary>
            public const float ToastHeight = 36f;

            /// <summary>错误横幅高度（容纳「错误标签 + 两三行说明 + 恢复按钮」）。</summary>
            public const float ErrorBannerHeight = 96f;

            /// <summary>加载条高度。</summary>
            public const float LoadingStripHeight = 40f;

            /// <summary>分析面板宽度（从右侧滑出）。</summary>
            public const float AnalysisPanelWidth = 420f;

            /// <summary>角色 / 外观面板尺寸。</summary>
            public const float AvatarPanelWidth = 368f;

            public const float AvatarPanelHeight = 160f;

            /// <summary>曲库浮层尺寸。</summary>
            public const float LibraryPanelWidth = 560f;

            public const float LibraryPanelHeight = 320f;

            /// <summary>曲库 / 最近曲目行高与行距。</summary>
            public const float LibraryRowHeight = 44f;

            public const float LibraryRowGap = 8f;

            /// <summary>帮助浮层尺寸。</summary>
            public const float HelpPanelWidth = 620f;

            public const float HelpPanelHeight = 384f;

            /// <summary>确认弹窗尺寸。</summary>
            public const float ConfirmPanelWidth = 460f;

            public const float ConfirmPanelHeight = 180f;

            /// <summary>提示条（toast）宽度。</summary>
            public const float ToastWidth = 600f;

            /// <summary>角色卡片宽度（三张卡片 + 8px 间距恰好铺满角色面板内容宽）。</summary>
            public const float AvatarCardWidth = 104f;

            /// <summary>窄窗口阈值：参考分辨率宽度小于该值时自动折叠练习工具栏（不缩小按钮硬塞）。</summary>
            public const float NarrowWidthThreshold = 1100f;

            /// <summary>宽窗口阈值：≥ 该值时顶栏显示「保存状态」等次要状态位。</summary>
            public const float WideWidthThreshold = 1180f;

            /// <summary>主播放栏内进度条行高（常规 / 手部特写紧凑）。</summary>
            public const float ProgressRowHeightNormal = 18f;

            public const float ProgressRowHeightCompact = 14f;

            /// <summary>曲名 / 状态标签在顶栏中的最大字符数（超出截断为省略号）。</summary>
            public const int TopBarSongNameMaxChars = 22;

            /// <summary>主播放栏曲名最大字符数。</summary>
            public const int MainBarSongNameMaxChars = 18;

            // ---- 悬停提示（tooltip）尺寸 ----

            /// <summary>提示最大宽度（超出自动换行）。</summary>
            public const float TooltipMaxWidth = 320f;

            /// <summary>提示最小高度。</summary>
            public const float TooltipMinHeight = 28f;

            public const float TooltipPadX = 10f;

            public const float TooltipPadY = 5f;

            /// <summary>提示与被指向控件之间的垂直间距。</summary>
            public const float TooltipGap = 8f;

            /// <summary>提示距屏幕边缘的最小留白。</summary>
            public const float TooltipScreenMargin = 6f;

            /// <summary>提示所在层级的 sortingOrder（永远画在其它 UI 之上）。</summary>
            public const int TooltipSortingOrder = 1000;

            // ---- UI 层级 sortingOrder（决定了浮层叠放顺序，方案 3.1「不遮挡核心手部区域」） ----

            public const int LessonCanvasOrder = 0;

            public const int AvatarCanvasOrder = 10;

            public const int StatusCanvasOrder = 20;
        }

        // ==================== 字体 / 程序化 Sprite ====================

        private static Font _font;

        /// <summary>
        /// 中文字体：沿用内置 LegacyRuntime.ttf（不引入 TextMeshPro，不下载任何素材）。
        /// </summary>
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
        private static readonly Dictionary<int, Sprite> _roundedByRadius = new Dictionary<int, Sprite>();

        /// <summary>
        /// 圆角九宫格 sprite（程序化生成，不依赖内置资源 —— 内置 UISprite.psd 在部分环境不可用）。
        /// 对应 <see cref="RadiusMedium"/>。
        /// </summary>
        public static Sprite RoundedSprite
        {
            get
            {
                if (_rounded == null) _rounded = GetRoundedSprite(RadiusMedium);
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

        /// <summary>按圆角半径取得（并缓存）圆角九宫格 sprite。</summary>
        public static Sprite GetRoundedSprite(int radius)
        {
            radius = Mathf.Clamp(radius, 2, 32);
            if (_roundedByRadius.TryGetValue(radius, out var cached) && cached != null) return cached;

            int size = Mathf.Max(48, radius * 3);
            var sprite = CreateRoundedSprite(size, radius);
            _roundedByRadius[radius] = sprite;
            return sprite;
        }

        /// <summary>圆形 sprite（程序化生成）。</summary>
        public static Sprite CreateCircleSprite(int size) => BuildCircleSprite(size);

        /// <summary>
        /// 圆环 sprite（程序化生成）：用于拍点 / 选中指示等需要「非纯色」标识的位置。
        /// </summary>
        public static Sprite CreateRingSprite(int size, float thickness)
        {
            size = Mathf.Clamp(size, 8, 128);
            thickness = Mathf.Clamp(thickness, 1f, size * 0.5f);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "UITheme_Ring"
            };
            float outer = size * 0.5f - 0.5f;
            float inner = outer - thickness;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f;
                    float dy = y + 0.5f - size * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>纯白 1×1 sprite：需要「纯色块 / 分割线」时使用，避免依赖内置资源。</summary>
        private static Sprite _white;
        private static Sprite WhiteSprite
        {
            get
            {
                if (_white == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                    {
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Point,
                        name = "UITheme_White"
                    };
                    var pixels = new Color32[16];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(pixels);
                    tex.Apply();
                    _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                }
                return _white;
            }
        }

        private static Sprite CreateRoundedSprite(int size, int radius)
        {
            radius = Mathf.Clamp(radius, 1, size / 2);
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

        private static Sprite BuildCircleSprite(int size)
        {
            size = Mathf.Clamp(size, 8, 128);
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

        // ==================== 画布 / 事件系统 ====================

        /// <summary>确保场景中存在可用的 EventSystem（含输入模块）。</summary>
        public static void EnsureEventSystem()
        {
            var existing = Object.FindFirstObjectByType<EventSystem>();
            if (existing != null)
            {
                // 已有 EventSystem 但缺少输入模块时补齐，避免「点了没反应」
                if (existing.GetComponent<BaseInputModule>() == null)
                    existing.gameObject.AddComponent<StandaloneInputModule>();
                return;
            }
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        /// <summary>
        /// 创建屏幕空间 Overlay 画布。
        /// <para>
        /// 对应方案 3.4 末条 / 15 章「窗口缩放」：ScaleWithScreenSize + 参考分辨率 1280×720，
        /// 匹配模式取高度（<see cref="Layout.ReferenceResolution"/>），保证纵向空间永不被压缩，
        /// 宽度方向按比例扩展后由锚点布局自适应。
        /// </para>
        /// </summary>
        public static Canvas CreateCanvas(Transform parent, string name) => CreateCanvas(parent, name, 0);

        /// <summary>创建画布并指定 sortingOrder（多层 UI 的叠放顺序：工具栏 &lt; 角色面板 &lt; 顶栏/错误横幅）。</summary>
        public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Layout.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // 匹配高度：窄窗口优先折行/折叠，不压缩按钮高度
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>参考分辨率下的画布宽度（用于响应式折叠判断）。</summary>
        public static float GetReferenceWidth(Canvas canvas)
        {
            // 参考分辨率下的画布宽度（ScaleWithScreenSize + 匹配高度时，宽度随窗口比例变化）
            if (canvas == null) return Layout.ReferenceResolution.x;
            if (!(canvas.transform is RectTransform rt)) return Layout.ReferenceResolution.x;
            float w = rt.rect.width;
            return w > 1f ? w : Layout.ReferenceResolution.x;
        }

        // ==================== 锚点布局助手（响应式布局的唯一入口） ====================

        /// <summary>以左下角为原点放置固定尺寸元素（容器内边距语义）。</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.offsetMin = new Vector2(x, y);
            rt.offsetMax = new Vector2(x + w, y + h);
            return rt;
        }

        /// <summary>以右下角为原点放置固定尺寸元素（右对齐控件组）。</summary>
        public static RectTransform PlaceRight(RectTransform rt, float rightMargin, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = new Vector2(-rightMargin - w, y);
            rt.offsetMax = new Vector2(-rightMargin, y + h);
            return rt;
        }

        /// <summary>以左上角为原点放置固定尺寸元素（3D 区域内的浮层）。</summary>
        public static RectTransform PlaceTopLeft(RectTransform rt, float left, float top, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -top - h);
            rt.offsetMax = new Vector2(left + w, -top);
            return rt;
        }

        /// <summary>以右上角为原点放置固定尺寸元素。</summary>
        public static RectTransform PlaceTopRight(RectTransform rt, float right, float top, float w, float h)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(-right - w, -top - h);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>左右拉伸、纵向固定的元素（进度条 / 通栏栏体）。</summary>
        public static RectTransform StretchX(RectTransform rt, float left, float right, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = new Vector2(left, y);
            rt.offsetMax = new Vector2(-right, y + h);
            return rt;
        }

        /// <summary>顶部通栏、纵向固定的元素（顶栏 / 错误横幅）。</summary>
        public static RectTransform StretchTop(RectTransform rt, float left, float right, float top, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(left, -top - h);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>四边拉伸（面板内容区）。</summary>
        public static RectTransform StretchAll(RectTransform rt, float left, float right, float bottom, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>屏幕居中、固定尺寸（浮层 / 弹窗）。</summary>
        public static RectTransform PlaceCenter(RectTransform rt, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-w * 0.5f, -h * 0.5f);
            rt.offsetMax = new Vector2(w * 0.5f, h * 0.5f);
            return rt;
        }

        /// <summary>顶部居中、固定尺寸（提示条 toast）。</summary>
        public static RectTransform PlaceTopCenter(RectTransform rt, float top, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(-w * 0.5f, -top - h);
            rt.offsetMax = new Vector2(w * 0.5f, -top);
            return rt;
        }

        /// <summary>
        /// 按锚点比例横向等分（分段选择器用）。<paramref name="inset"/> 为四周内缩量。
        /// </summary>
        public static RectTransform StretchFraction(RectTransform rt, float minX, float maxX, float inset)
        {
            rt.anchorMin = new Vector2(minX, 0f);
            rt.anchorMax = new Vector2(maxX, 1f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>右边缘锚定、纵向拉伸（右侧滑出面板）。
        /// <paramref name="gap"/> 为距屏幕右边缘的距离；<paramref name="slideOffset"/> 为滑出位移（0 = 貼边显示）。
        /// </summary>
        public static RectTransform StretchRightColumn(RectTransform rt, float width, float top, float bottom,
            float gap, float slideOffset = 0f)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.offsetMin = new Vector2(-width - gap + slideOffset, bottom);
            rt.offsetMax = new Vector2(-gap + slideOffset, -top);
            return rt;
        }

        // ==================== 控件工厂 ====================

        /// <summary>创建圆角面板（默认居中锚点，可用 <see cref="Place"/> 等助手重新锚定）。</summary>
        public static RectTransform CreatePanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var rt = CreateEmpty(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = RoundedSprite;
            img.type = Image.Type.Sliced;
            return rt;
        }

        /// <summary>创建指定圆角的面板（面板 / 卡片 / 标签使用不同圆角等级）。</summary>
        public static RectTransform CreatePanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color, int radius)
        {
            var rt = CreateEmpty(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var sprite = GetRoundedSprite(radius);
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            return rt;
        }

        /// <summary>创建纯色块（无 sprite，用作分割线 / 进度标记 / 纯布局容器）。默认不接收射线。</summary>
        public static RectTransform CreateRect(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var rt = CreateEmpty(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = WhiteSprite;
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>创建分割线（细线，用于分组，不承担信息表达）。</summary>
        public static RectTransform CreateDivider(Transform parent, string name, Vector2 pos, Vector2 size)
            => CreateRect(parent, name, pos, size, Divider);

        private static RectTransform CreateEmpty(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
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
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 创建圆角按钮（含 hover/press 平滑过渡、tooltip、键盘可达）。
        /// <para>
        /// 对应方案 3.3 / 审查 2.2B：<paramref name="style"/> 决定视觉层级，
        /// <paramref name="tooltip"/> / <paramref name="shortcut"/> 生成「节拍器 · M」形式的提示。
        /// </para>
        /// </summary>
        public static Button CreateButton(Transform parent, string label, Vector2 pos, Vector2 size,
            UnityEngine.Events.UnityAction onClick, int fontSize = FontButton,
            ButtonStyle style = ButtonStyle.Secondary, string tooltip = null, string shortcut = null)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = BaseColor(style);
            var sprite = GetRoundedSprite(RadiusFor(style));
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = BlockFor(style);
            if (onClick != null) btn.onClick.AddListener(onClick);

            var text = CreateText(go.transform, label, Vector2.zero, size, fontSize, LabelColor(style),
                TextAnchor.MiddleCenter, style == ButtonStyle.Primary ? FontStyle.Bold : FontStyle.Normal);
            text.raycastTarget = false;

            ApplyButtonState(btn, ButtonState.Default, style);
            SetButtonTooltip(btn, string.IsNullOrEmpty(tooltip) ? null : ComposeTooltip(tooltip, shortcut));
            return btn;
        }

        /// <summary>
        /// 创建开关按钮（方案 3.4 / 审查 2.2C：每个开关都必须显示当前状态）。
        /// 文案形如「节拍器：开」，选中态同时使用强调底 + 边框 + 文案，不只靠颜色。
        /// </summary>
        public static Button CreateToggleButton(Transform parent, string label, Vector2 pos, Vector2 size,
            bool initialOn, UnityEngine.Events.UnityAction onClick, string tooltip = null, string shortcut = null,
            int fontSize = FontSmall)
        {
            var btn = CreateButton(parent, label, pos, size, onClick, fontSize,
                ButtonStyle.Toggle, tooltip, shortcut);
            SetToggleState(btn, label, initialOn);
            return btn;
        }

        /// <summary>刷新开关按钮的文案与选中态（只与状态有关，不做业务判断）。</summary>
        public static void SetToggleState(Button btn, string label, bool on)
            => SetToggleState(btn, label, on, true);

        /// <summary>
        /// 刷新开关按钮的文案、选中态与可用性：文案始终写「：开 / ：关」，
        /// 禁用时保持禁用视觉（不会因为刷新状态而误恢复为可点击）。
        /// </summary>
        public static void SetToggleState(Button btn, string label, bool on, bool enabled)
        {
            if (btn == null) return;
            SetButtonLabel(btn, $"{label}：{(on ? "开" : "关")}");
            if (!enabled)
            {
                ApplyButtonState(btn, ButtonState.Disabled, ButtonStyle.Toggle);
                return;
            }
            ApplyButtonState(btn, on ? ButtonState.Selected : ButtonState.Default, ButtonStyle.Toggle);
        }

        /// <summary>
        /// 应用按钮状态（方案 3.3 七态 / 审查 2.2A）。
        /// <paramref name="style"/> 由创建该按钮的界面持有（本类不保存每个按钮的样式，避免额外组件）。
        /// </summary>
        public static void ApplyButtonState(Button btn, ButtonState state, ButtonStyle style = ButtonStyle.Secondary)
        {
            if (btn == null) return;

            bool interactable = state != ButtonState.Disabled && state != ButtonState.Loading;
            if (btn.interactable != interactable) btn.interactable = interactable;

            Color background;
            Color label;
            bool border;

            switch (state)
            {
                case ButtonState.Selected:
                    background = SelectedColor(style);
                    label = SelectedLabelColor(style);
                    border = true;
                    break;
                case ButtonState.Disabled:
                    background = style == ButtonStyle.Primary ? Disabled : BaseColor(style);
                    label = TextDisabled;
                    border = false;
                    break;
                case ButtonState.Loading:
                    background = Disabled;
                    label = AccentBright;
                    border = true;
                    break;
                case ButtonState.Error:
                    background = DangerBg;
                    label = TextPrimary;
                    border = true;
                    break;
                default: // Default / Hover / Pressed 走 ColorBlock 的乘算
                    background = BaseColor(style);
                    label = LabelColor(style);
                    border = false;
                    break;
            }

            if (btn.targetGraphic is Image img) img.color = background;
            var text = GetLabel(btn);
            if (text != null)
            {
                text.color = label;
                text.fontStyle = (style == ButtonStyle.Primary || state == ButtonState.Selected)
                    ? FontStyle.Bold
                    : FontStyle.Normal;
            }
            SetOutline(btn, border, state == ButtonState.Error ? Error : BorderSelected);
        }

        /// <summary>开关 / 选中项的样式是否为实心强调（供界面判断排版，不影响业务）。</summary>
        public static bool IsStrongStyle(ButtonStyle style)
            => style == ButtonStyle.Primary || style == ButtonStyle.Danger;

        /// <summary>切换按钮激活态（兼容旧接口：按次按钮语义处理）。</summary>
        public static void SetButtonActive(Button btn, bool active)
            => ApplyButtonState(btn, active ? ButtonState.Selected : ButtonState.Default, ButtonStyle.Secondary);

        /// <summary>启用 / 禁用按钮；禁用时把原因写进 tooltip（方案 3.4 / 审查 2.2C「禁用并显示原因」）。</summary>
        public static void SetButtonEnabled(Button btn, bool enabled, string disabledReason = null,
            ButtonStyle style = ButtonStyle.Secondary)
        {
            if (btn == null) return;
            if (enabled)
            {
                ApplyButtonState(btn, ButtonState.Default, style);
                btn.interactable = true;
            }
            else
            {
                ApplyButtonState(btn, ButtonState.Disabled, style);
                btn.interactable = false;
                if (!string.IsNullOrEmpty(disabledReason)) SetButtonTooltip(btn, disabledReason);
            }
        }

        /// <summary>安全获取按钮内文本（UI 结构异常时返回 null，避免 NRE）。</summary>
        public static Text GetLabel(Button btn)
        {
            return btn != null ? btn.GetComponentInChildren<Text>() : null;
        }

        /// <summary>安全设置按钮文本（按钮或文本缺失时静默跳过）。</summary>
        public static void SetButtonLabel(Button btn, string content)
        {
            var label = GetLabel(btn);
            if (label != null) label.text = content;
        }

        /// <summary>设置按钮悬停提示（悬停显示用途，快捷键写在提示里，例如「节拍器 · M」）。</summary>
        public static void SetButtonTooltip(Button btn, string tooltip)
        {
            if (btn == null || string.IsNullOrEmpty(tooltip)) return;
            UITooltip.SetText(btn.gameObject, tooltip);
        }

        /// <summary>给控件加（或去掉）高亮描边：选中 / 焦点 / 错误状态的颜色之外的第二重区分。</summary>
        public static void SetOutline(Graphic graphic, bool on, Color color)
        {
            if (graphic == null) return;
            var outline = graphic.GetComponent<Outline>();
            if (!on)
            {
                if (outline != null) outline.enabled = false;
                return;
            }
            if (outline == null) outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;
            outline.enabled = true;
        }

        private static void SetOutline(Button btn, bool on, Color color)
        {
            if (btn == null) return;
            SetOutline(btn.targetGraphic, on, color);
        }

        /// <summary>创建进度条（圆角轨道 + 强调色填充 + 圆形手柄 + 可拖动）。</summary>
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
            bgImg.color = TrackBg;
            bgImg.sprite = GetRoundedSprite(RadiusSmall);
            bgImg.type = Image.Type.Sliced;

            // 填充区
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var faRt = fillArea.AddComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
            faRt.offsetMin = new Vector2(Space4, Space4); faRt.offsetMax = new Vector2(-Space4, -Space4);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRt = fill.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = new Vector2(10f, 0f);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = Accent;
            fillImg.sprite = GetRoundedSprite(RadiusSmall);
            fillImg.type = Image.Type.Sliced;

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

        /// <summary>
        /// 在进度条轨道上创建标记（A / B 循环点）。
        /// 标记只做辅助定位，具体时间文字始终同时显示在循环行里（关键信息不放进悬停提示）。
        /// </summary>
        public static RectTransform CreateTrackMarker(Slider slider, Color color, float width)
        {
            var host = slider != null ? slider.transform : null;
            if (host == null) return null;

            var go = new GameObject("TrackMarker");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.05f);
            rt.anchorMax = new Vector2(0f, 0.95f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, 0f);
            var img = go.AddComponent<Image>();
            img.sprite = WhiteSprite;
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>把轨道标记移动到归一化位置（0..1）。</summary>
        public static void SetTrackMarker(RectTransform marker, float normalized, bool visible)
        {
            if (marker == null) return;
            if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
            if (!visible) return;
            float t = Mathf.Clamp01(normalized);
            marker.anchorMin = new Vector2(t, marker.anchorMin.y);
            marker.anchorMax = new Vector2(t, marker.anchorMax.y);
            marker.anchoredPosition = Vector2.zero;
        }

        // ==================== 样式 → 颜色映射 ====================

        private static int RadiusFor(ButtonStyle style)
            => style == ButtonStyle.Ghost ? RadiusSmall : RadiusMedium;

        /// <summary>样式基调色（Default 状态）。</summary>
        public static Color BaseColor(ButtonStyle style) => style switch
        {
            ButtonStyle.Primary => Accent,
            ButtonStyle.Danger => DangerBg,
            ButtonStyle.Ghost => new Color(Control.r, Control.g, Control.b, 0.35f),
            ButtonStyle.Toggle => Control,
            _ => Control
        };

        /// <summary>样式文字色（Default 状态）。</summary>
        public static Color LabelColor(ButtonStyle style) => style switch
        {
            ButtonStyle.Primary => TextOnAccent,
            ButtonStyle.Danger => TextPrimary,
            ButtonStyle.Ghost => TextSecondary,
            ButtonStyle.Toggle => TextSecondary,
            _ => TextPrimary
        };

        /// <summary>样式选中态底色。</summary>
        public static Color SelectedColor(ButtonStyle style) => style switch
        {
            ButtonStyle.Primary => AccentBright,
            ButtonStyle.Danger => Error,
            ButtonStyle.Ghost => Selected,
            ButtonStyle.Toggle => Selected,
            _ => Accent
        };

        /// <summary>样式选中态文字色。</summary>
        public static Color SelectedLabelColor(ButtonStyle style) => style switch
        {
            ButtonStyle.Primary => TextOnAccent,
            ButtonStyle.Danger => TextOnAccent,
            ButtonStyle.Ghost => TextPrimary,
            ButtonStyle.Toggle => TextPrimary,
            _ => TextOnAccent
        };

        /// <summary>样式的 ColorBlock（悬停 / 按下 / 禁用由 Selectable 负责）。</summary>
        public static ColorBlock BlockFor(ButtonStyle style)
        {
            float hover = style == ButtonStyle.Ghost ? 1.35f : 1.18f;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(hover, hover, hover, 1f);
            colors.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.60f);
            colors.fadeDuration = ButtonFadeSeconds;
            return colors;
        }

        // ==================== 键盘 / 焦点（方案 3.4） ====================

        /// <summary>
        /// 是否正在文本输入 / 数值编辑：为 true 时全局快捷键必须让位（方案 3.4）。
        /// 项目当前未使用文本输入控件，但保留守卫，避免未来加曲名搜索后快捷键抢按键。
        /// </summary>
        public static bool IsTextInputFocused()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var go = es.currentSelectedGameObject;
            if (go == null) return false;
            if (go.GetComponent<InputField>() != null) return true;
            return go.GetComponentInParent<InputField>() != null;
        }

        /// <summary>
        /// 是否有 UI 控件（<see cref="Selectable"/>）持有键盘焦点。
        /// <para>
        /// 用于避免「空格」双重触发：本项目 InputManager 的 Submit 轴包含空格，
        /// 当某个按钮获得焦点时空格会触发该按钮的 onClick；此时全局的空格快捷键应让位
        /// （方案 3.4「快捷键只在合理焦点下生效」）。
        /// </para>
        /// </summary>
        public static bool IsSelectableFocused()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var go = es.currentSelectedGameObject;
            if (go == null) return false;
            if (go.GetComponent<Selectable>() != null) return true;
            return go.GetComponentInParent<Selectable>() != null;
        }

        private static int _escapeFrame = -1;

        /// <summary>
        /// 取走本帧的 Esc 按键（同一帧只有第一个调用者返回 true）。
        /// 多脚本共存时避免一次 Esc 关闭多个面板。
        /// </summary>
        public static bool ConsumeEscape()
        {
            if (_escapeFrame == Time.frameCount) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            _escapeFrame = Time.frameCount;
            return true;
        }

        private static int _toggleAnalysisFrame = -1;

        /// <summary>取走本帧的 Tab 按键（分析面板开关，方案 3.4「快捷键只是补充」）。</summary>
        public static bool ConsumeTab()
        {
            if (_toggleAnalysisFrame == Time.frameCount) return false;
            if (!Input.GetKeyDown(KeyCode.Tab)) return false;
            _toggleAnalysisFrame = Time.frameCount;
            return true;
        }

        /// <summary>设置控件的显式导航（方向键焦点顺序，方案 3.4「控件应有键盘焦点顺序」）。</summary>
        public static void SetExplicitNavigation(Selectable self, Selectable up, Selectable down,
            Selectable left, Selectable right)
        {
            if (self == null) return;
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnUp = up;
            nav.selectOnDown = down;
            nav.selectOnLeft = left;
            nav.selectOnRight = right;
            self.navigation = nav;
        }

        /// <summary>把一组控件串成纵向 / 横向焦点链（空元素自动跳过）。</summary>
        public static void ChainHorizontal(params Selectable[] chain)
        {
            if (chain == null || chain.Length == 0) return;
            for (int i = 0; i < chain.Length; i++)
            {
                var self = chain[i];
                if (self == null) continue;
                var prev = PrevNonNull(chain, i - 1);
                var next = NextNonNull(chain, i + 1);
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnLeft = prev != null ? prev : self;
                nav.selectOnRight = next != null ? next : self;
                nav.selectOnUp = self;
                nav.selectOnDown = self;
                self.navigation = nav;
            }
        }

        private static Selectable PrevNonNull(Selectable[] chain, int from)
        {
            for (int i = from; i >= 0; i--) if (chain[i] != null) return chain[i];
            return null;
        }

        private static Selectable NextNonNull(Selectable[] chain, int from)
        {
            for (int i = from; i < chain.Length; i++) if (chain[i] != null) return chain[i];
            return null;
        }

        // ==================== 纯逻辑助手（状态机 / 文案，EditMode 可测） ====================

        /// <summary>播放按钮文案（方案 6.2 状态机 / 审查 2.2C：禁用与加载都要看得见）。</summary>
        public static string PlayButtonLabel(LessonState state, PlaybackState playback)
        {
            if (state == LessonState.Loading) return "加载中…";
            if (state == LessonState.NoSong) return "播放";
            return playback == PlaybackState.Playing ? "暂停" : "播放";
        }

        /// <summary>该状态下是否允许「播放 / 重播 / 循环」等需要曲目的操作。</summary>
        public static bool CanTriggerSongActions(LessonState state, bool hasSong)
            => hasSong && !state.BlocksSongActions();

        /// <summary>练习手文案（分段选择器选项文字，非颜色区分）。</summary>
        public static string DescribePracticeHand(PracticeHand hand) => hand switch
        {
            PracticeHand.LeftOnly => "左手",
            PracticeHand.RightOnly => "右手",
            _ => "双手"
        };

        /// <summary>视角文案（审查 3.1D：当前模式必须始终可见）。</summary>
        public static string DescribeCameraMode(CameraMode mode) => mode switch
        {
            CameraMode.Closeup => "手部特写",
            CameraMode.Full => "全景",
            _ => "自由观察"
        };

        /// <summary>琴键高亮模式文案。</summary>
        public static string DescribeHighlight(HighlightMode mode) => mode switch
        {
            HighlightMode.Upcoming => "预提示",
            HighlightMode.Active => "发声",
            HighlightMode.Both => "全部",
            _ => "关闭"
        };

        /// <summary>播放状态文案（顶栏状态标签）。</summary>
        public static string DescribePlayback(PlaybackState state) => state switch
        {
            PlaybackState.Playing => "播放中",
            PlaybackState.Paused => "已暂停",
            _ => "已停止"
        };

        /// <summary>会话状态文案（转发 Core 的 <c>LessonState</c> 扩展，避免 UI 各自实现一套文案）。</summary>
        public static string DescribeLessonState(LessonState state) => state.ToDisplayText();

        /// <summary>状态标签前缀符号：颜色之外再给一重可辨识标识。</summary>
        public static string StateGlyph(PlaybackState state) => state switch
        {
            PlaybackState.Playing => "▶",
            PlaybackState.Paused => "❚❚",
            _ => "■"
        };

        /// <summary>会话状态前缀符号（方案 8.2 / 审查 2.2C）。</summary>
        public static string StateGlyph(LessonState state) => state switch
        {
            LessonState.Loading => "…",
            LessonState.Error => "✕",
            LessonState.NoSong => "—",
            LessonState.Ready => "●",
            _ => "●"
        };

        /// <summary>
        /// 「某手在本曲目中没有音符」的就近提示文案（方案 3.4 / 审查 2.2C）。
        /// 返回空串表示无需提示。
        /// </summary>
        public static string HandEmptyHint(PracticeHand hand, SongAnalysis analysis)
        {
            if (analysis == null || analysis.IsEmpty) return "";
            if (hand == PracticeHand.LeftOnly && analysis.LeftHandCount == 0)
                return "！左手在本曲目中没有音符：将静音，请改练右手或双手";
            if (hand == PracticeHand.RightOnly && analysis.RightHandCount == 0)
                return "！右手在本曲目中没有音符：将静音，请改练左手或双手";
            return "";
        }

        /// <summary>是否需要把手部特写下的练习工具栏收进折叠面板（方案 3.4 末条）。</summary>
        public static bool ShouldFoldPracticeTools(float referenceWidth, bool closeupMode)
            => referenceWidth < Layout.NarrowWidthThreshold || closeupMode;

        /// <summary>窄窗口判断（参考分辨率宽度）。</summary>
        public static bool IsNarrowReferenceWidth(float referenceWidth)
            => referenceWidth < Layout.NarrowWidthThreshold;

        /// <summary>提示文案组装：把快捷键附在用途之后，如「节拍器 · M」。</summary>
        public static string ComposeTooltip(string label, string shortcut)
        {
            if (string.IsNullOrEmpty(label)) return "";
            return string.IsNullOrEmpty(shortcut) ? label : $"{label} · {shortcut}";
        }

        /// <summary>错误严重程度标签（颜色之外用文字区分，方案 8.3）。</summary>
        public static string ErrorSeverityLabel(ErrorSeverity severity) => severity switch
        {
            ErrorSeverity.Info => "提示",
            ErrorSeverity.Warning => "警告",
            ErrorSeverity.Recoverable => "错误",
            ErrorSeverity.Fatal => "严重错误",
            _ => "提示"
        };

        /// <summary>错误严重程度前缀符号。</summary>
        public static string ErrorSeverityGlyph(ErrorSeverity severity) => severity switch
        {
            ErrorSeverity.Info => "i",
            ErrorSeverity.Warning => "!",
            ErrorSeverity.Recoverable => "✕",
            ErrorSeverity.Fatal => "✕",
            _ => "i"
        };

        /// <summary>错误横幅底色（按严重程度，但仍以文字标签为主）。</summary>
        public static Color ErrorBannerColor(ErrorSeverity severity) => severity switch
        {
            ErrorSeverity.Info => PanelBgRaised,
            ErrorSeverity.Warning => new Color(0.216f, 0.180f, 0.098f, 0.97f),
            _ => new Color(0.278f, 0.129f, 0.129f, 0.97f)
        };

        /// <summary>错误文字色（按严重程度）。</summary>
        public static Color ErrorTextColor(ErrorSeverity severity) => severity switch
        {
            ErrorSeverity.Info => TextPrimary,
            ErrorSeverity.Warning => TextWarning,
            _ => TextError
        };

        /// <summary>用户提示是否已经包含「下一步做什么」（方案 8.3 三要素判定）。</summary>
        public static bool MessageContainsNextStep(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            return message.Contains("请") || message.Contains("下一步") || message.Contains("可以");
        }

        /// <summary>
        /// 组装错误横幅正文：发生了什么 + 为什么可能发生 + 下一步做什么。
        /// 会话层文案已按 8.3 编写时原样展示；缺少下一步动作时补上恢复动作提示（不编造原因）。
        /// </summary>
        public static string ComposeErrorBody(AppError error)
        {
            string message = error.UserMessage ?? "";
            if (string.IsNullOrEmpty(message)) message = "操作未完成。";
            if (error.Recovery == RecoveryAction.None) return message;
            if (MessageContainsNextStep(message)) return message;
            string label = string.IsNullOrEmpty(error.RecoveryLabel) ? "重试" : error.RecoveryLabel;
            return $"{message}\n下一步：{label}";
        }

        /// <summary>错误横幅恢复按钮文案（取会话层给出的 RecoveryLabel）。</summary>
        public static string ComposeErrorActionLabel(AppError error)
        {
            if (error.Recovery == RecoveryAction.None) return "知道了";
            return string.IsNullOrEmpty(error.RecoveryLabel) ? "重试" : error.RecoveryLabel;
        }

        /// <summary>时间格式化（mm:ss）。</summary>
        public static string FormatTime(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 0f;
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m:00}:{s:00}";
        }

        /// <summary>百分比整数（0..100）。</summary>
        public static int Percent(float ratio) => Mathf.RoundToInt(Mathf.Clamp01(ratio) * 100f);

        /// <summary>超长文案截断（窄窗口下曲名不撑破布局）。</summary>
        public static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (maxChars <= 0) return "";
            return text.Length <= maxChars ? text : text.Substring(0, Mathf.Max(1, maxChars - 1)) + "…";
        }

        /// <summary>把播放状态转为可读标签（如「播放中」「已暂停」）。</summary>
        public static string PlaybackLabel(PlaybackState state) => DescribePlayback(state);

        /// <summary>相对亮度（WCAG 2.x，用于对比度自检）。</summary>
        public static float RelativeLuminance(Color color)
        {
            float r = Linearize(color.r);
            float g = Linearize(color.g);
            float b = Linearize(color.b);
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        /// <summary>两色对比度（1..21；正文建议 ≥ 4.5，辅助文字建议 ≥ 3）。</summary>
        public static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float hi = Mathf.Max(la, lb);
            float lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        private static float Linearize(float channel)
        {
            channel = Mathf.Clamp01(channel);
            return channel <= 0.04045f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>主播放栏进度条上 A / B 循环标记的颜色（与非颜色文案配合使用）。</summary>
        public static Color LoopStartColor => Accent;

        /// <summary>A / B 循环标记文字颜色。</summary>
        public static Color LoopEndColor => LeftHandColor;
    }
}
