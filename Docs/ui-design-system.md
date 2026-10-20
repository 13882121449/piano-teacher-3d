# UI 设计系统（ui-design-system.md）

> 对应方案 **3.3 视觉设计系统**、**3.4 交互规则**、**6.3 组合规则**；审查 **2.1 控制栏分层**、**2.2 状态规范与视觉层级**、**2.3 反馈分层**。
> 实现位置：`UI/UITheme.cs`（设计令牌与控件工厂）、`UI/UISegmentedControl.cs`、`UI/UITooltip.cs`、
> `UI/UIStatusBar.cs`、`UI/LessonControlsUI.cs`、`UI/MainMenuUI.cs`、`UI/AvatarSelectionUI.cs`、`UI/FileBrowserUI.cs`。
>
> **唯一入口约定**：所有 UI 脚本**只允许引用 `UITheme` 的令牌**，不得自行硬编码颜色 / 圆角 / 控件高度 / 间距
> （由 `UIThemeTokenTests` 守护）。

---

## 1. 设计令牌

### 1.1 颜色

| 令牌 | 值（近似） | 用途 |
|---|---|---|
| `SceneBg` | `#131519`（0.075, 0.082, 0.098） | 场景 / 相机背景（最深） |
| `PanelBg` | `#1E2228`（0.96 透明） | 面板底色（浮于场景之上） |
| `PanelBgRaised` | 抬升一级 | 次级功能卡片、状态标签、分段控件轨道 |
| `PanelBgDeep` | 更深 | 顶栏 / 底栏（压住 3D 画面又不抢眼） |
| `Control` | 控件常态底 | 按钮默认 |
| `ControlHover` | 控件悬停 | 悬停 |
| `ControlPressed` | 控件按下 | 按下 |
| `Accent` | `#D9A441` 暖金 | 强调色（主操作、进度填充） |
| `AccentBright` | `#F0C674` | 强调亮色（主操作选中、加载态文字） |
| `AccentDim` | 暗金 | — |
| `TextPrimary` / `TextSecondary` / `TextOnAccent` | 主 / 次 / 强调底上文字 | — |
| `Divider` | 半透明分割线 | 分组（不承担信息表达） |
| `Selected` | 选中态底（比 `AccentDim` 更深） | 分段选中项 / 开关开启项 |
| `Error` / `Success` / `Warning` | 状态色 | — |
| `TextError` / `TextWarning` / `TextDisabled` | 状态文字色 | 保证暗色面板上的对比度 |
| `DangerBg` | 危险底（暗红） | 危险 / 破坏性操作 |
| `Disabled` | 禁用底 | — |
| `TrackBg` | 进度条轨道底 | — |
| `BorderSelected` | 选中 / 焦点描边 | 颜色之外的第二重区分 |
| `Transparent` | 完全透明 | 纯布局容器（不参与渲染与射线） |
| `LeftHandColor` | 冷蓝（0.396, 0.663, 0.933） | 左手发声提示（**语义色**） |
| `RightHandColor` | 暖橙（0.949, 0.612, 0.318） | 右手发声提示（**语义色**） |
| `UpcomingColor` | 暖金（与 Accent 同源） | 预提示（**语义色**） |

> **教学语义色不可挪用**：暖金 = 预提示、冷蓝 = 左手发声、暖橙 = 右手发声（审查 2.3「颜色语义不变」）；
> 场景其它元素保持克制，避免与提示色争抢注意力（由 `TeachingSemanticColors_KeepDocumentedMeaning` 守护）。

### 1.2 字号层级

| 令牌 | 值 | 用途 |
|---|---|---|
| `FontPageTitle` | 28 | 页面标题（主菜单 / 空状态） |
| `FontTitle` | 20 | 区块标题 |
| `FontSongTitle` | 20 | 面板内曲目标题 |
| `FontSubtitle` | 15 | 副标题 / 卡片主文本 |
| `FontAction` | 15 | 主操作按钮 |
| `FontButton` | 15 | 普通按钮 |
| `FontBody` | 13 | 正文（控制行、表单） |
| `FontBodySmall` | 12 | 次级正文 |
| `FontSmall` | 12 | 辅助说明 |
| `FontCaption` | 10 | 分段项 / 小按钮 / 卡片元信息 |
| `FontStatusLabel` | 10 | 状态标签（顶栏状态、模式标签） |

> 行高统一为 `字号 × LineHeightRatio(1.35)`，取整用 `UITheme.LineHeight(size)`。
> 字号只有 10 / 12 / 13 / 15 / 20 / 28 六档，禁止在 UI 脚本里写裸数字。

### 1.3 间距等级（4 · 8 · 12 · 16 · 24）

| 令牌 | 值（像素，参考分辨率） |
|---|---|
| `Space4` | 4 |
| `Space8` | 8 |
| `Space12` | 12 |
| `Space16` | 16 |
| `Space24` | 24 |

### 1.4 圆角等级

| 令牌 | 值 | 用途 |
|---|---|---|
| `RadiusSmall` | 8 | 小型输入 / 标签 / 分段项 / chip |
| `RadiusMedium` | 10 | 中型按钮 / 卡片 |
| `RadiusLarge` | 12 | 大型面板 |
| `RadiusBar` | 3 | 进度条轨道与填充 |

### 1.5 控件高度

| 令牌 | 值 | 用途 |
|---|---|---|
| `HeightPrimary` | 48 | 主按钮（播放 / 暂停） |
| `HeightButton` | 40 | 普通按钮 |
| `HeightInput` | 32 | 输入 / 分段选择器 |
| `HeightSmall` | 32 | 小按钮 / 状态标签 / chip |
| `SizeIconButton` | 32 | 图标按钮（返回 / 帮助 / 重播），正方形 |

### 1.6 布局令牌（参考分辨率 1280×720）

| 令牌 | 值 | 说明 |
|---|---|---|
| `Layout.ReferenceResolution` | 1280 × 720 | 与 `CanvasScaler.referenceResolution` 一致；`matchWidthOrHeight = 1`（匹配高度，窄窗优先折叠而非压缩按钮） |
| `Layout.ScreenMargin` | 16 | 屏幕四周统一外边距 |
| `Layout.StatusBarHeight` | 56 | 顶栏高度（单行：返回 / 曲目 / 状态 / 帮助 / 角色） |
| `Layout.MainBarHeightNormal` | 132 | 播放器面板高度（进度行 44 + 行距 16 + 控制行 40 + 上下内边距 32） |
| `Layout.MainBarHeightCompact` | 72 | 手部特写时的高度（隐藏进度行） |
| `Layout.ProgressBarHeight` | 6 | 进度条轨道高度（圆角 3） |
| `Layout.ChipHeight` | 32 | 底栏 chip / 分段控件高度 |
| `Layout.HintCardWidth` / `Height` | 220 / 88 | 左上提示卡（玻璃底，移出 2.5s 淡到 0.45） |
| `Layout.SongCardWidth` / `Height` | 240 / 96 | 曲库卡片 |
| `Layout.ToolbarHeightExpanded` | 124 | 练习工具栏展开高度（标题行 + 两行控件） |
| `Layout.ToolbarHeightCollapsed` | 36 | 练习工具栏折叠高度（仅标题行，入口不丢失） |
| `Layout.ToolbarHeaderHeight` | 28 | 工具栏标题行高度 |
| `Layout.ErrorBannerHeight` | 96 | 错误横幅高度 |
| `Layout.LoadingStripHeight` | 40 | 加载条高度 |
| `Layout.ToastHeight` / `ToastWidth` | 36 / 600 | 轻量提示 |
| `Layout.AnalysisPanelWidth` | 420 | 分析面板（右侧滑出） |
| `Layout.AvatarPanelWidth/Height` | 368 / 160 | 角色 / 外观面板 |
| `Layout.LibraryPanelWidth/Height` | 560 / 320 | 曲库浮层 |
| `Layout.HelpPanelWidth/Height` | 620 / 384 | 帮助浮层 |
| `Layout.ConfirmPanelWidth/Height` | 460 / 180 | 确认弹窗 |
| `Layout.ProgressRowHeightNormal/Compact` | 18 / 14 | 进度条行高 |
| `Layout.TopBarSongNameMaxChars` / `MainBarSongNameMaxChars` | 22 / 18 | 曲名截断长度 |

---

## 2. 按钮七态与样式层级

### 2.1 按钮七态（`ButtonState`）

方案 3.3 要求「按钮至少定义默认、悬停、按下、选中、禁用、加载、错误状态」。实现：

| 状态 | 视觉表现 | 由谁处理 |
|---|---|---|
| `Default` | 按 `ButtonStyle` 的基调色 | ColorBlock |
| `Hover` | 底 / 文字乘算提亮（Ghost 提亮更多） | `Selectable` ColorBlock |
| `Pressed` | 乘算压暗 | `Selectable` ColorBlock |
| `Selected` | 选中底 + **描边** + 粗体（不只靠颜色） | `ApplyButtonState` + `SetOutline` |
| `Disabled` | 禁用底 + `TextDisabled`；`interactable = false`；**原因写入 tooltip** | `ApplyButtonState` / `SetButtonEnabled` |
| `Loading` | 禁用 + `AccentBright` 文字 + 描边；播放按钮显示「加载中…」 + 点动画 | `ApplyButtonState(Loading)` + `LessonControlsUI.Update` |
| `Error` | `DangerBg` 底 + 描边（错误色） | `ApplyButtonState(Error)` |

> `ButtonState_HasExactlySevenDocumentedStates` 断言枚举恰好含这 7 个值。

### 2.2 样式层级（`ButtonStyle`）

| 样式 | 用途 | 基调 |
|---|---|---|
| `Primary` | **主操作**：全屏唯一实心强调（播放 / 暂停；导入 MIDI） | `Accent` 底 + `TextOnAccent` 字 + 粗体 |
| `Secondary` | **次操作**：重播、设置 A/B、曲库行等 | `Control` 底 |
| `Ghost` | **低频 / 顶栏弱化**：返回曲库、帮助、曲目分析、角色入口 | 半透明 `Control` 底 + 次级文字 |
| `Danger` | **危险 / 破坏性**：确认弹窗的「确认」、最近列表「移除」 | `DangerBg` 底 |
| `Toggle` | **切换项**：循环 / 节拍器 / 开发诊断（必须显示「：开 / ：关」文字） | `Control` 底，选中转 `Selected` |

> `PrimaryStyle_IsTheOnlyStrongAccentStyle` 断言只有 Primary 使用最强实心强调。
> `DangerAndToggleStyles_UseDedicatedColors` 断言危险色与切换色独立。

### 2.3 按「操作优先级」的划分

| 优先级 | 例子 | 呈现策略 |
|---|---|---|
| **主操作** | 播放 / 暂停 | 唯一 `Primary`；固定在主播放栏右侧最显眼处 |
| **次操作** | 重播、设置 A / 设置 B | `Secondary`，常驻 |
| **切换项** | 练习手 / 速度 / 高亮 / 视角 | **分段选择器**，直接显示当前选中项（`▍` + 强调底 + 描边），不只靠点击后的短暂变色 |
| **低频工具** | 曲目分析、角色 / 外观 | `Ghost`，收纳进练习工具栏（可折叠）或面板 |
| **危险操作** | 返回曲库 / 返回主菜单、移除最近记录、覆盖外观（未来） | `Danger` + 二次确认（`UIStatusBar.ShowConfirm` / `MainMenuUI.RequestRemoveRecent` 的「确认移除？」） |

---

## 3. 非颜色区分要求

不能只通过颜色区分状态（方案 3.3 / 审查 2.3，避免色觉差异用户无法分辨）。实现手段：

| 手段 | 例子 |
|---|---|
| **文字** | 开关显示「节拍器：开 / 关」；错误横幅显示「✕ 错误 / ! 警告 / 提示」；练习手显示「双手 / 左手 / 右手」 |
| **图标 / 符号** | 选中标记 `▍`；状态前缀 `▶`（播放中）/ `❚❚`（已暂停）/ `■`（已停止）；错误严重程度前缀 `i` / `!` / `✕` |
| **边框 / 描边** | 选中态与错误态额外加 `Outline`（`BorderSelected` / `Error`） |
| **拍点样式** | 强拍实心点 `●`、弱拍空心点 `○`（不只靠颜色闪烁） |
| **位置** | 「某手无音符」的就近提示出现在练习手选择器旁边，而不是只在远处状态栏 |

> 覆盖测试：`StateGlyph_DistinguishesStatesWithoutColor`、`ErrorSeverityLabels_AreDistinguishableByTextNotOnlyColor`、`HandEmptyHint_TellsUserWhatToDoNext`。

---

## 4. 焦点顺序与快捷键

### 4.1 快捷键表（缺省全局，均为「补充入口」）

| 按键 | 功能 | 可见 UI 入口 |
|---|---|---|
| `空格` | 播放 / 暂停 | 主播放栏「播放」 |
| `[` | 重播（回到开头） | 主播放栏「重播」 |
| `R` | 重置视角到当前模式的标准构图 | 自由观察提示栏 / 帮助 |
| `H` | 切换琴键高亮（关闭 → 全部 → 发声 → 预提示） | 次级工具「高亮」分段 |
| `M` | 节拍器开关 | 次级工具「节拍器」开关 |
| `Tab` | 曲目分析面板开关 | 次级工具「曲目分析」 |
| `F1` | 帮助面板 | 顶栏「帮助」 |
| `Esc` | 关闭当前浮层 / 取消确认弹窗 | 各面板「关闭 / 取消」 |

> 每个快捷键功能都能在界面上找到（方案 3.4「快捷键是辅助途径」）；tooltip 里写明快捷键，如「节拍器 · M」。

### 4.2 让位规则（关键）

| 规则 | 实现 |
|---|---|
| **文本输入 / 数值编辑聚焦时，全局快捷键让位** | `UITheme.IsTextInputFocused()` 为 true 时 `HandleShortcuts` 整段跳过 |
| **按钮获得焦点时，空格交给 EventSystem** | `UITheme.IsSelectableFocused()` 为 true 时空格不进入全局处理（避免同一次按键触发两个动作） |
| **确认弹窗打开时，全局快捷键让位** | `LessonControlsUI.HandleShortcuts` 首行判断 `_status.IsConfirmOpen` |
| **浮层打开时 Esc 优先关浮层** | `UIStatusBar.Update` 先消费 Esc 关最上层浮层；`UITheme.ConsumeEscape()` 保证同一帧只有第一个调用者生效 |
| **Tab 与 Esc 单次消费** | `UITheme.ConsumeTab()` / `ConsumeEscape()` 以 `Time.frameCount` 去重，多脚本共存时不会一次按键关闭多个面板 |

### 4.3 焦点顺序

- 控件通过 `UITheme.ChainHorizontal(...)` 串联成横向链；分段选择器内部 `RefreshFocusChain` 自动串起各分段项。
- 显式导航通过 `UITheme.SetExplicitNavigation(self, up, down, left, right)` 设置（`Navigation.Mode.Explicit`）。
- 顶栏按钮链：返回曲库 → 曲库 → 帮助 → 当前人物。
- 主播放栏链：视角分段（特写 / 全景 / 自由）→ 重播 → 播放。

---

## 5. 响应式布局与断点

参考分辨率 **1280×720**，`CanvasScaler` 使用 `ScaleWithScreenSize` + `matchWidthOrHeight = 1`（**匹配高度**），
因此纵向空间永不被压缩，宽度方向随窗口比例扩展后由锚点布局自适应。

| 断点 / 阈值 | 值 | 行为 |
|---|---|---|
| `Layout.NarrowWidthThreshold` | **1100**（参考分辨率宽度） | `< 1100` 视为窄窗口：**自动折叠练习工具栏**（保留「练习设置」标题行入口，不缩小主按钮） |
| `Layout.WideWidthThreshold` | **1180** | `≥ 1180` 时顶栏显示「保存状态」等次要状态位（其余情况下隐藏，把宽度让给曲名与状态标签） |
| 手部特写（`CameraMode.Closeup`） | — | 主播放栏压缩为紧凑高度；练习工具栏自动折叠；隐藏 3D 区快捷键提示（避免遮挡琴键） |
| `ShouldFoldPracticeTools(refW, closeup)` | — | `refW < 1100` **或** 手部特写 → 折叠 |
| `Layout.MainBarHeightNormal / Compact` | **132 / 72** | 常规 / 特写紧凑高度（占屏 ≤22%，由 `PlayerBar_OccupiesAtMostTwentyTwoPercentOfScreenHeight` 守护） |
| `Layout.ToolbarHeightExpanded / Collapsed` | **124 / 36** | 展开 / 折叠高度 |
| `Layout.ProgressRowHeightNormal / Compact` | 18 / 14 | 进度条行高 |
| 分析面板 | — | 从右侧滑出（`StretchRightColumn`），宽 420；与角色面板**互斥**打开，避免互相遮挡 |

> 用户手动展开 / 收起优先于自动折叠（`_foldTouched`）：一旦用户手动操作，直到断点再次变化前不再自动覆盖。

> 覆盖测试：`LayoutTokens_FitAtAllRequiredResolutions`、`LayoutTokens_KeepMainBarAboveMinimumUsableHeight`、`NarrowWidthThreshold_SitsBetweenCommonWindowSizes`、`ShouldFoldPracticeTools_FoldsOnNarrowOrCloseup`。

---

## 6. 对比度要求（WCAG）

实现 `UITheme.RelativeLuminance` / `ContrastRatio`（WCAG 2.x 相对亮度公式）供自检：

| 要求 | 说明 | 覆盖测试 |
|---|---|---|
| **正文对比度 ≥ 4.5:1** | 主文字（`TextPrimary`）在所有面板背景上均满足 | `TextPrimary_MeetsWcagAaOnAllPanelBackgrounds` |
| **强调底上文字可读** | `TextOnAccent` 在 `Accent` / `Selected` 底上满足 | `TextOnAccent_IsReadableOnAccentAndSelectedStates` |
| **辅助 / 状态文字 ≥ 3:1** | 次级文字与状态标签在对应底色上满足最低对比 | `SecondaryAndStatusText_MeetMinimumContrast` |
| **错误 / 警告文字可读** | 错误 / 警告文字色在其横幅底色上可读 | `ErrorAndWarningText_AreReadableOnTheirBanners` |
| 对比度分级 | `ContrastRatio` 返回 1..21；正文建议 ≥ 4.5，辅助文字建议 ≥ 3 | — |

---

## 7. 交互反馈分层（方案 3.4 / 审查 2.3）

| 层 | 内容 | 位置 |
|---|---|---|
| **教学画面** | 当前音符提示、当前练习手、当前速度、播放进度 | 3D 区 + 主播放栏状态标签 |
| **曲目结构** | 音符数、节奏 / 跨度分析、练习建议 | 分析面板（`Tab`） |
| **高级诊断** | 命中率 / 最大帧位移 / 平滑度 / 双手协调 / 穿模告警 | 分析面板内「开发诊断」**折叠区**（默认关闭，不与学习操作混排） |

- 所有操作都有**即时反馈**（按钮状态、toast、错误横幅），避免用户连续点击判断是否响应。
- 暂无曲目时播放按钮**禁用并显示原因**（`ExplainNoSong`），而不是点了没反应。
- 错误提示**提供恢复动作**（恢复按钮 → `HandleRecovery()`），不只显示技术错误文字。
