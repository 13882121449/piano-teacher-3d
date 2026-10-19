# Piano Teacher 3D — 钢琴 3D 教学应用

> 把「抽象的 MIDI 音符事件」翻译成「可观察的演奏动作」：一台真实比例的 88 键钢琴、一双会移动的手，
> 以及每个音符对应的琴键起落、高亮与声音。

| 项目信息 | 值 |
|---|---|
| 项目代号 | Piano Teacher 3D |
| 首版平台 | Windows 桌面端（x86_64） |
| 引擎 / 语言 | Unity **2022.3.22f1c1**（中国版，revision `c3cbd310e76e`）/ C# |
| 程序集 | `PianoTeacher3D`（运行时代码）、`PianoTeacher3D.EditModeTests`、`PianoTeacher3D.PlayModeTests` |
| 第三方资源 | **无**：钢琴 / 人物 / 材质 / 音频 / 曲目 / 字体全部程序化生成或使用引擎内置资源 |
| 代码规模 | `Assets/Scripts` **48 个脚本 / 17,302 行**；`Assets/Tests` **33 个测试文件 / 10,215 行** |
| 已验证状态 | 编译 **0 错误 0 警告**；EditMode **301/301**；PlayMode **90 通过 + 5 跳过 + 0 失败**；Stress 3/3、Perf 4/4、Soak 1/1 |

> 数字来源：2026-10-16 本机实测（`python Tools/verify.py --stress --perf --soak --soak-seconds 90`）。
> 口径与逐条验收见 [`Docs/completion-review.md`](Docs/completion-review.md)、[`Docs/how-to-run.md`](Docs/how-to-run.md)。

---

## 1. 项目简介

### 1.1 是什么

一个用 **Unity + C#** 编写的 Windows 桌面教学应用。运行时由代码**程序化搭建**整个场景
（钢琴、人物、材质、灯光、UI），不依赖任何外部模型或音频文件，因此仓库体积小、零许可风险、开箱即跑。

### 1.2 解决什么问题

看谱练琴时，学习者很难把「谱面上的音符」和「手该怎么动」对应起来。本项目把这条链路完整可视化：

```text
MIDI 文件 → 解析为音符事件 → 统一时间轴驱动
                                 ├─ 琴键按下 / 抬起 + 发声
                                 ├─ 人物手腕与手指移动（IK）
                                 ├─ 琴键高亮（预提示 / 发声，按左右手分色）
                                 └─ UI 进度、节拍器、曲目分析
```

### 1.3 设计原则

1. **统一时间轴** — `LessonTimeline` 是唯一时间源，音乐、琴键、人物动作、UI 进度都由它驱动。
2. **视角与演奏解耦** — 切换视角不暂停、不重启音乐、不重置进度。
3. **人物与演奏解耦** — 切换角色只替换表现，不重置曲目、进度或相机模式。
4. **错误从会话层统一呈现** — 底层模块只产出数据（`AppError`），由会话层决定继续 / 回滚 / 禁用。
5. **遵守资源许可证** — 代码、模型、动作数据、权重、音乐与音频样本分别核对（见 [`Docs/asset-licenses.md`](Docs/asset-licenses.md)）。

### 1.4 当前状态

| 维度 | 现状 |
|---|---|
| 功能完整度 | 播放 / 视角 / 角色 / 分手 / 循环 / 节拍器 / 高亮 / 曲目导入 / 偏好持久化 / 错误恢复 均已可用 |
| 测试 | EditMode 301 条纯逻辑用例（含手部网格几何 18 条）；PlayMode 95 项（含 9 个视角可用性场景、手部三关节姿态 7 条、压力与视觉快照） |
| 性能（批处理采样） | 锁 60 FPS：纯播放 p99 **17 ms**、1% low **58.4 FPS**、GC **0.01 MB**；90 s 长跑内存 **+0.4%** |
| 视觉基线 | **20 张**截图（6 张旧基线 + V01–V14），清单由测试自动生成 |
| 未实现 | AI 动作生成、照片生成 3D 人物、MP3/WAV 转谱、移动端 / Web（均为计划项） |

---

## 2. 核心功能

### 2.1 功能总览

| 模块 | 能力 | 入口 |
|---|---|---|
| 曲目播放 | 播放 / 暂停 / 继续 / 重播、拖动进度、0.5× / 1.0× / 1.5× 变速（`Stop()` 由会话 API 提供，界面不单设停止按钮） | 主播放栏、`空格`、`[` |
| 三种视角 | 手部特写 / 全景 / 自由观察，0.35 s 缓动切换，切换不重启曲目 | 主播放栏「视角」分段、`R` 重置 |
| 显示模式 | **展示**（完整手部动画）/ **教学**（隐藏手、只高亮被按下的琴键） | 练习工具栏「展示 \| 教学」、`T` |
| 分手练习 | 双手 / 左手 / 右手，切换保持进度连续 | 练习手分段选择器 |
| A-B 循环 | 设置 A / B、整曲循环、清除；跳转后自动重建按键状态 | 「循环」开关 + A/B 按钮 |
| 节拍器 | 由曲目 tempo map 生成拍点，强弱拍不同音高与视觉指示 | 「节拍器」开关、`M` |
| 琴键高亮 | 关闭 / 预提示 / 发声 / 全部；预提示暖金，左手冷蓝、右手暖橙 | 「高亮」分段、`H` |
| 角色切换 | 3 个预设形象（写实女性 / 写实男性 / 卡通），切换不重置课程状态 | 顶栏「人物」、角色面板 |
| 曲目导入 | 应用内文件选择器 + 导入校验 + 复制到用户目录 + 最近曲目 | 主菜单「导入 MIDI」 |
| 曲目分析 | 音符数、左右手分布、黑键比例、和弦与最大跨度、音域、最快音间隔、练习建议 | 「曲目分析」、`Tab` |
| 错误恢复 | 结构化错误 + 三段式文案 + 恢复动作按钮 + 日志脱敏 | 顶部错误横幅 |
| 偏好持久化 | 视角 / 速度 / 练习手 / 高亮 / 角色 / 节拍器 / 画质等自动保存 | `persistentDataPath` |

### 2.2 三种视角

相机是**显式三状态机**（`CameraModeController`），每个状态各自持有目标点、相机姿态、视野、裁剪、阻尼与输入规则。

| 模式 | 用途 | 取景规则 |
|---|---|---|
| **手部特写 `Closeup`** | 看清键面与手指 | 焦点由「活动练习手范围 ∪ 当前及即将发生的琴键范围」求解；斜俯视 60°，距离 0.20–0.88 m，FOV 30–48°，阻尼跟随并限速 |
| **全景 `Full`** | 看完整演奏姿态 | **前侧 3/4**（偏航 34°、俯角 16°），按「人物包围盒 ∪ 钢琴包围盒」构图，保证 88 键完整入画、人物头脚不出框 |
| **自由观察 `Free`** | 用户自行查看 | 右键拖动旋转 + 滚轮缩放；有距离 / 俯仰 / 高度 / 穿模限制；指针悬停在 UI 上时不旋转场景 |

- 进入自由观察时记录进入前的教学预设，`ReturnToPreset()` 可返回；`R` 回到**当前模式的标准构图**。
- 连续快速切换时以「相机当前实际姿态」为新过渡起点，不叠加协程，因此不会抖动。
- 全景默认**不**把琴体透明化；「键盘无遮挡」子视角是显式开关，默认关闭。

### 2.3 展示模式与教学模式

| 模式 | 表现 |
|---|---|
| **展示（默认）** | 完整手部与手臂动画；琴键高亮按用户设置（关闭 / 预提示 / 发声 / 全部） |
| **教学** | 隐藏手部与手臂，**只高亮被按下的琴键**，左手冷蓝、右手暖橙 |

- 教学模式下「高亮」控件置灰并说明原因（该模式已固定为「仅高亮被按下的琴键」），切回展示模式后恢复原选择。
- 隐藏手部时**只关网格、不动骨骼与 IK**，因此切回时无需重建状态、不会跳变；切换时按当前时间轴重建按键 / 手部 / 高亮，播放状态与进度不变。

### 2.4 左右约定（全项目唯一）

角色坐在 `+Z` 侧、面向 `-Z` 弹奏，因此：

```text
低音键  ↔  角色左手  ↔  世界 +X  ↔  画面左侧  ↔  冷蓝
高音键  ↔  角色右手  ↔  世界 -X  ↔  画面右侧  ↔  暖橙
```

这条链的每一环都有自动化断言（世界坐标、屏幕投影、琴键实际渲染色、哪只手在移动），见
`HandFingerCorrespondenceTests`。改动键盘坐标或骨架左右前必须先读
[`Docs/hand-correspondence-and-display-modes.md`](Docs/hand-correspondence-and-display-modes.md)。

### 2.5 曲库与导入

- **应用内文件选择器**（`FileBrowserUI`）：驱动器 / 上级目录导航、只显示 `.mid` / `.midi`（可切换显示全部）、分页与截断提示；不使用系统对话框或第三方包。
- **导入校验**（`MidiFileValidator`）：路径存在 → 可读 → 大小上限 8 MB → 扩展名初筛 → **真实解析** → 无音符 → 时长为零 → 复杂度上限（20 万音符 / 1 小时）。
- **导入结果**：复制到用户目录，生成稳定内部 ID（FNV-1a 64），写入最近曲目（去重、上限 20 条）。
- **失效处理**：文件被移动或删除时返回可操作提示，不崩溃；`Forget` 只删除用户目录内的副本，内置示例曲不可删。

### 2.6 错误提示与降级

统一错误结构 `AppError` = 稳定错误码 + 严重度 + 面向用户的中文说明 + 诊断详情 + 恢复动作 + 恢复按钮文案 + 脱敏上下文。
文案固定三段式：**发生了什么 + 为什么可能发生 + 下一步做什么**。

- 音频初始化失败 → 静音继续，不阻断播放，横幅提示可换设备或继续静音。
- 人物骨架校验失败 → 回退默认角色，课程不受影响。
- 循环区间非法 → 自动关闭循环并提示重新设置。
- 无曲目时播放按钮禁用并说明原因，而不是点了没反应。
- 完整错误码表见 [`Docs/error-codes.md`](Docs/error-codes.md)。

### 2.7 快捷键

| 操作 | 快捷键 | 说明 |
|---|---|---|
| 播放 / 暂停 | `空格` | 按钮获得焦点时交给 EventSystem，避免一次按键触发两个动作 |
| 重播 | `[` | 从头开始 |
| 重置视角 | `R` | 回到当前模式标准构图 |
| 显示模式切换 | `T` | 展示 ⇄ 教学 |
| 高亮模式循环 | `H` | 关闭 → 预提示 → 发声 → 全部 |
| 节拍器 | `M` | 开 / 关 |
| 曲目分析面板 | `Tab` | 展开 / 收起 |
| 帮助 | `F1` | 显示快捷键与使用说明 |
| 关闭浮层 / 取消 | `Esc` | 优先关闭最上层浮层 |

> 关键状态（当前视角 / 练习手 / 速度 / 循环 / 高亮）必须直接显示在界面上，悬停提示只作补充。
> 文本输入或数值编辑获得焦点时，全局快捷键让位。

---

## 3. 技术栈与依赖

### 3.1 运行环境

| 依赖 | 要求 | 说明 |
|---|---|---|
| 操作系统 | Windows 10 / 11 x86_64 | 首版仅验证 Windows 桌面 |
| Unity Editor | **2022.3.22f1c1** | 版本锁定于 `ProjectSettings/ProjectVersion.txt` |
| Unity Hub | 3.x | 用于添加并打开项目 |
| Python | **3.8+**（仅一键验证脚本需要） | 纯标准库，无需 `pip install` |

### 3.2 Unity 包依赖

`Packages/manifest.json` 中只有官方包，无第三方插件：

| 包 | 版本 | 用途 |
|---|---|---|
| `com.unity.ugui` | 1.0.0 | UGUI：所有界面由代码程序化生成 |
| `com.unity.test-framework` | 1.1.33 | EditMode / PlayMode 测试 |
| `com.unity.timeline` | 1.7.6 | 引擎默认依赖（本项目未直接使用时间轴资产） |
| `com.unity.animation.rigging` | 1.2.1 | 引擎依赖（IK 为自研实现，未使用该包的约束组件） |
| `com.unity.textmeshpro` | 3.0.6 | 引擎默认依赖；**UI 实际使用 legacy `Text` + 内置字体** |
| `com.unity.ide.rider` / `com.unity.ide.visualstudio` | 3.0.31 / 2.0.22 | IDE 集成 |

### 3.3 零外部资源策略

| 资产类型 | 实现方式 |
|---|---|
| 钢琴与琴体 | `PianoMeshFactory` 程序化倒角网格 + 立式琴体几何 |
| 人物与手部 | `AvatarRigBuilder` 程序化 Humanoid 骨架 + 程序化网格（三段指节、拇指对掌） |
| 材质 / 贴图 | 运行时 `new Material(Shader.Find("Standard"))`，无贴图 |
| 音频 | 波形合成（基频 + 谐波 + 指数衰减）+ 程序化节拍器击拍 |
| 示例曲目 | `Assets/StreamingAssets/Songs/demo.mid`（**运行时资源中唯一的二进制内容资产**；`Docs/screenshots/` 的 PNG 只用于文档） |
| 字体 | `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` |

### 3.4 开发与验证工具

| 工具 | 位置 | 用途 |
|---|---|---|
| 一键验证脚本 | `Tools/verify.py` | 编译检查 → EditMode → PlayMode → 可选阶段，输出汇总与退出码 |
| 截图比较工具 | `Tools/visual_diff.py` | 纯标准库 PNG 逐像素比较（容差 / ROI / 差异比例） |
| 架构不变式检查 | `Tools/check_architecture.py` | 静态规则：UI 不直连底层、唯一时间轴、禁 WinForms、禁同名类型、Update 无 LINQ |
| Unity Test Framework | `Assets/Tests/` | 纯逻辑单测 + 场景级集成测试 |
| Git | — | `Library/`、`_verify_out/` 等生成物已排除 |

---

## 4. 目录与代码结构

### 4.1 顶层目录树

```text
piano-teacher-3d/
├── Assets/
│   ├── Scenes/
│   │   ├── MainMenu.unity              # 启动场景：只挂 MainMenuUI
│   │   └── PianoLesson.unity           # 教学场景：只挂 LessonSessionController
│   ├── Scripts/
│   │   ├── PianoTeacher3D.asmdef       # 运行时程序集（autoReferenced）
│   │   ├── Core/                       # 会话、时间轴、曲库、偏好、错误、画质（15 个）
│   │   ├── MIDI/                       # 解析、校验、调度、事件、过滤（6 个）
│   │   ├── Piano/                      # 88 键、琴体、动画、高亮、音频、环境（7 个）
│   │   ├── Avatar/                     # 骨架、手部 IK、指法、指标（10 个）
│   │   ├── Camera/                     # 取景数学 + 三状态机（2 个）
│   │   └── UI/                         # 主题、控制栏、状态栏、选择器、文件浏览器（8 个）
│   ├── StreamingAssets/
│   │   └── Songs/demo.mid              # 内置示例曲（只读）
│   └── Tests/
│       ├── EditMode/                   # 19 个测试文件 + asmdef（只跑 Editor）
│       └── PlayMode/                   # 16 个测试文件 + asmdef（场景级）
├── Docs/                               # 架构、动画、UI、错误码、验收、截图、性能
├── Tools/
│   ├── verify.py                       # 一键验证
│   └── visual_diff.py                  # PNG 基线比较
├── Packages/manifest.json              # 官方包依赖
├── ProjectSettings/                    # Unity 项目设置（含版本锁定、构建场景列表）
├── Tests/                              # 方案目录镜像（仅 .gitkeep，实际测试在 Assets/Tests）
├── _verify_out/                        # 验证中间产物（gitignore）
├── Piano/                              # 历史遗留的独立空工程（不参与构建 / 测试）
└── README.md
```

> `Piano/` 是一个自带 `Library/`、`ProjectSettings/`、只有 `SampleScene` 的嵌套空工程，
> 与本项目构建和测试无关；确认无用途后可自行删除。`Tests/` 只是为了对齐需求文档的目录约定，
> Unity 只识别 `Assets/Tests/`（见 `Tests/README.md`）。

### 4.2 `Assets/Scripts/Core` — 会话与全局服务

| 文件 | 职责 |
|---|---|
| `LessonSessionController.cs` | **会话门面**：装配整个场景、维护 `LessonState`、执行切曲事务、聚合错误出口、持久化偏好、连接所有子模块 |
| `LessonTimeline.cs` | **唯一时间源**：`Stopped / Playing / Paused`、A-B 循环、变速、Seek |
| `PlaybackState.cs` | 播放状态枚举 |
| `LessonState.cs` | 会话状态枚举：`NoSong / Loading / Ready / Playing / Paused / Error` |
| `PracticeHand.cs` | 练习手枚举：`Both / LeftOnly / RightOnly`（以 C4=60 分界） |
| `DisplayMode.cs` | 显示模式枚举：`Showcase`（展示）/ `Teaching`（教学） |
| `Metronome.cs` | 由 tempo map 生成拍点表、程序化击拍音、强弱拍回调 |
| `SongAnalysis.cs` / `SongAnalyzer.cs` | 曲目分析数据容器与纯静态统计（音符、和弦、跨度、音域、左右手负担） |
| `SongMetadata.cs` | 曲目元数据、`ImportRejection`、`ImportCheck`、`ImportResult` |
| `SongLibraryService.cs` | 曲库服务：内置 / 用户目录、导入复制、最近曲目、失效解析、清除缓存 |
| `UserPreferences.cs` | 用户偏好读写、默认值、范围钳制、损坏容错 |
| `AppError.cs` | `ErrorSeverity` / `ErrorCode` / `RecoveryAction` / `AppError` 结构 / `PathSanitizer` 脱敏 |
| `ErrorReporter.cs` | 统一上报：写日志 + 广播，并提供文案映射助手 |
| `QualityProfile.cs` | 画质档位（Standard / High）、设备推荐、落地到 `QualitySettings` |

### 4.3 `Assets/Scripts/MIDI` — 解析与事件调度

| 文件 | 职责 |
|---|---|
| `MidiParser.cs` | 自研 SMF 解析：VLQ、running status、tempo map、Format 0/1/2、缺 Note Off 补全、复杂度上限 |
| `MidiFileValidator.cs` | 导入前的纯静态校验（存在 / 可读 / 大小 / 扩展名 / 真实解析 / 音符 / 时长 / 复杂度） |
| `MidiEventScheduler.cs` | 按统一时间轴派发 `OnNoteOn` / `OnNoteOff`；二分查找区间内音符 |
| `MidiNoteEvent.cs` | 音符事件（音高 / 开始 / 结束 / 力度 / 轨道） |
| `PianoNoteFilter.cs` | 左右手判定与过滤（`SplitPitch = 60`），纯静态 |
| `TempoChange.cs` | tempo 变化点（tick / 微秒每拍） |

### 4.4 `Assets/Scripts/Piano` — 钢琴、音频、场景

| 文件 | 职责 |
|---|---|
| `PianoKeyMap.cs` | 88 键（A0=21 … C8=108）与琴体的程序化构建；键根坐标、键面高度、真实尺度常量 |
| `PianoMeshFactory.cs` | 程序化倒角立方体 / 圆角面板网格与倒角钳制（可单测） |
| `PianoKeyAnimator.cs` | 按键旋转动画（绕键根 X 轴），预分配缓存，`ReleaseAll` 立即复位 |
| `PianoKeyHighlighter.cs` | 琴键着色：预提示暖金、左手冷蓝、右手暖橙；预提示窗口刷新与清理 |
| `PianoAudioSynth.cs` | 24 声部受控发声池：波形合成、力度映射、Note Off 淡出、初始化失败可降级静音（内含 `PianoVoiceAllocator`） |
| `PianoSceneEnvironment.cs` | 低干扰音乐室：地面 / 背景 / 踢脚线 / 三点光照 + 键盘点光 / 接触阴影近似 / 画质落地 |
| `HighlightMode.cs` | 高亮模式枚举：`Off / Upcoming / Active / Both` |

### 4.5 `Assets/Scripts/Avatar` — 人物、手部、指法

| 文件 | 职责 |
|---|---|
| `AvatarManager.cs` | 3 个预设角色的构建与切换；`IHandRig` 手部引用、`IFingerJointRig` 三关节、Humanoid 骨架校验、手部可见性、运行时网格生命周期 |
| `AvatarRigBuilder.cs` | 程序化 Humanoid 骨架（躯干 20 节点 + 每指 MCP/PIP/DIP 三节点）+ `BuildHumanAvatar` 生成合法 `Avatar`；骨骼局部位置表 |
| `AvatarDefinition.cs` | 纯数据角色定义、`AvatarBodyMetrics` 人体标尺（以 24 mm 键宽为参照）、`HandMetrics` 每角色手部比例、校验逻辑 |
| `HandMeshFactory.cs` | **程序化手部网格工厂**：掌部连续截面放样（超椭圆 + 大鱼际/小鱼际）、手指逐段渐缩、半球指尖、指甲薄片；纯函数、可单测 |
| `HandIKController.cs` | 手腕 IK（换位抬升 / 自适应速度 / 单帧位移硬约束 / 姿态钳制）、受限手腕俯仰滚转、指尖目标、手掌几何反馈、指标采样 |
| `FingerPoseController.cs` | 手指姿态：MCP/PIP/DIP 关节级受限指尖 IK、Approach/Press/Hold/Rest 四阶段、关节限位计数、低置信度标注 |
| `FingerAssignmentService.cs` | **双手共用的「手指 ↔ 音高」分配**；`IFingertipTargetSource` 接口；同帧幂等 |
| `IFingeringProvider.cs` / `HeuristicFingeringProvider.cs` | 可替换指法提供器接口 + 规则化启发式实现（音域 / 邻近 / 黑键 / 跨度 / 和弦 / 拇指穿越） |
| `HandPoseValidator.cs` | 姿态边界、越界钳制、双手交叉判定、指尖误差、关节角限位、键面法向距离（纯静态） |
| `HandMotionMetrics.cs` | 动作指标累计与报告：命中率、被分配指尖误差、键面距离、换位峰值、P50/P95/P99 分组、关节限位与穿模计数 |

### 4.6 `Assets/Scripts/Camera` — 取景

| 文件 | 职责 |
|---|---|
| `CameraFraming.cs` | **纯数学取景策略**：`CameraTarget` / `CameraPose` / `HandRegion` / 联合包围、FOV 距离限幅、过渡插值（可 EditMode 单测） |
| `CameraModeController.cs` | 三状态机：`Closeup / Full / Free`，预设姿态、阻尼跟随、过渡中断、自由观察输入与 UI 悬停隔离 |

### 4.7 `Assets/Scripts/UI` — 界面

| 文件 | 职责 |
|---|---|
| `UITheme.cs` | 设计令牌（颜色 / 字号 / 间距 / 圆角 / 控件高度 / 布局断点）+ 控件工厂 + 按钮七态 |
| `LessonControlsUI.cs` | 教学主界面三层结构：主播放栏、练习工具栏、分析面板 + 开发诊断；快捷键与状态文案 |
| `UIStatusBar.cs` | 顶栏（返回 / 曲名 / 状态 / 人物 / 帮助）、错误横幅 + 恢复按钮、Toast、二次确认 |
| `UISegmentedControl.cs` | 分段选择器：直接显示当前选中项（强调底 + 描边 + `▍` 标记，不只靠颜色） |
| `UITooltip.cs` | 悬停提示（含快捷键），关键信息不依赖悬停 |
| `FileBrowserUI.cs` | 应用内 MIDI 文件选择器（驱动器 / 目录 / 扩展名过滤 / 分页） |
| `MainMenuUI.cs` | 主菜单：曲目卡片、最近曲目、失效提示、空状态、导入入口、跨场景传参 |
| `AvatarSelectionUI.cs` | 角色选择面板：选中态 + 角色名 + 顶栏同步，切换不影响课程状态 |

### 4.8 场景与资源

| 路径 | 说明 |
|---|---|
| `Assets/Scenes/MainMenu.unity` | 启动场景，仅挂 `MainMenuUI`；选中曲目通过静态字段传给课程场景 |
| `Assets/Scenes/PianoLesson.unity` | 教学场景，仅挂 `LessonSessionController`；钢琴 / 人物 / UI / 灯光全部在 `Awake` 中装配 |
| `Assets/StreamingAssets/Songs/demo.mid` | 内置示例曲（只读，用户数据绝不写回此处） |

两个场景都已写入 `ProjectSettings/EditorBuildSettings.asset`，可直接构建。

### 4.9 测试

| 目录 | 文件 | 覆盖 |
|---|---|---|
| `Assets/Tests/EditMode` | `MidiParserTests` / `MidiParserFileTests` / `MidiCompatibilityTests` / `PianoNoteFilterTests` | SMF 解析、Format 0/1/2、running status、VLQ、损坏文件、左右手分界 |
| | `SongLibraryTests` / `SongAnalyzerTests` / `TempoAndMetronomeTests` | 导入校验、曲库复制与失效、偏好容错、分析统计、拍点生成 |
| | `CameraFramingTests` / `PianoGeometryTests` / `QualityProfileTests` / `SceneEnvironmentTests` | 取景数学、倒角网格与键位、画质单调性、环境幂等与包围盒 |
| | `FingeringProviderTests` / `FingerAssignmentServiceTests` / `HandPoseValidatorTests` / `HandMotionMetricsTests` | 指法规则、共用分配、姿态边界、指标口径 |
| | `UIThemeTokenTests` / `AudioReleaseTests` / `EdgeCaseTests` | 设计令牌与按钮状态、声部池释放、边界输入 |
| `Assets/Tests/PlayMode` | `PlayModeTests` / `PlayModeIntegrationTests` / `PlayModeErrorHandlingTests` / `TeachingFeatureTests` | 时间轴、切歌与切换不断流、错误恢复、教学功能 |
| | `CameraVisibilityTests` / `HandFingerCorrespondenceTests` / `DisplayModeTests` / `HandMotionTests` | 9 个视角可用性场景、手指↔琴键一致性、模式切换无残留、动作质量 |
| | `HumanoidRigTests` / `HumanoidSceneTests` / `PerformanceTests` | Humanoid 合法性、场景装配、基础性能 |
| | `StressAndStabilityTests` / `PerformanceSamplingTests` / `VisualSnapshotTests` / `VisualBaselineCompareTests` | 压力 / 长跑 / 帧时间采样 / 视觉基线 |

测试分类：`Stress`、`Perf`、`Soak`、`Visual`。长跑与性能用例标记 `[Explicit]`，不拖慢常规验证。

### 4.10 `Tools`

| 文件 | 职责 |
|---|---|
| `verify.py` | 一键验证：编译检查 → EditMode → PlayMode → 可选阶段；结果 XML 生成即继续（处理 Unity 退出挂起）；退出码 `0/1/2` |
| `visual_diff.py` | PNG 逐像素比较：`--tolerance` / `--roi` / `--exclude-roi` / `--max-diff-ratio` / `--json` |

### 4.11 `Docs`

| 文件 | 内容 |
|---|---|
| `architecture.md` | 模块职责、依赖方向、状态机与时序图、配置与内容分离 |
| `animation-pipeline.md` | 手部几何、IK 管线、指尖/手腕分离、指标口径、指法提供器 |
| `hand-correspondence-and-display-modes.md` | 左右手 / 手指↔琴键 / 展示与教学模式的根因与验证 |
| `ui-design-system.md` | 设计令牌、按钮七态、层级、焦点与快捷键、响应式断点 |
| `error-codes.md` | 错误码 / 严重度 / 恢复动作 / 文案 / 日志与隐私 |
| `visual-regression.md` | 截图用例、视角可用性场景、基线维护、人工看图记录 |
| `quality-and-roadmap.md` | 画质档位、性能排查顺序、验收门槛、路线图、设计取舍 |
| `completion-review.md` | 需求 → 实现逐条核对表与完成度 |
| `how-to-run.md` | 运行、CLI 全选项、产物、基线更新、FAQ |
| `asset-licenses.md` | 许可证清单与外部资源登记流程 |
| `optimization-report.md` | 两轮优化的交付报告与实测数据 |
| `research-notes.md` | 提供器接口与开源资源调研 |
| `screenshots/manifest.md` | 截图清单（测试运行时自动生成） |
| `perf/sampling.md` | 性能与长跑采样记录（测试运行时写入） |
| `perf/environments.md` | 发布前环境矩阵记录 |

### 4.12 生成物与排除目录

| 路径 | 说明 |
|---|---|
| `Library/`、`Temp/`、`Logs/`、`obj/` | Unity 生成，可删除重建（已 gitignore） |
| `_verify_out/` | 验证中间产物：`compile*.log`、`editmode.xml`、`playmode.xml`、截图日志 |
| `UserSettings/` | 本机编辑器布局（已 gitignore） |
| `Docs/screenshots/*.png` | 视觉基线，**入库**，用于人工评审与像素比较 |

---

## 5. 安装与运行

### 5.1 环境准备

1. 安装 **Unity Hub**；
2. 通过 Hub 安装 **Unity 2022.3.22f1c1**（中国版 / 团结引擎均可），勾选 **Windows Build Support (IL2CPP 或 Mono)**；
3. 安装 **Python 3.8+**（仅命令行验证需要，不装任何第三方库）。

### 5.2 获取并打开项目

```bash
# 任选一种方式获取项目目录
git clone <仓库地址> piano-teacher-3d
# 或直接把已有目录拷贝到本机
```

Unity Hub → `Add project from disk` → 选择 `piano-teacher-3d` 目录 → 打开。

> 首次打开会导入资源并生成 `Library/`，约 1–3 分钟。项目已锁定引擎版本，请勿用更高版本升级项目，
> 否则 `ProjectSettings` 可能被改写。

### 5.3 在编辑器中运行

1. Project 窗口双击 `Assets/Scenes/MainMenu.unity`；
2. 点击顶部 **▶ Play**；
3. 在主菜单点曲目卡片（或「导入 MIDI」）进入教学场景；
4. 用主播放栏播放，`Tab` 打开曲目分析，`T` 切换展示 / 教学。

> 教学场景在编辑器里看起来是「空场景」——钢琴、人物、UI、灯光都在运行时由代码装配，这是预期设计。

### 5.4 一键命令行验证

```bash
cd "D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d"

python Tools/verify.py                    # 编译 + EditMode + PlayMode
python Tools/verify.py --edit-only        # 只跑纯逻辑测试（最快）
python Tools/verify.py --stress           # 追加压力 / 资源加载 / 环境矩阵自检
python Tools/verify.py --perf             # 追加帧时间 p50/p95/p99、1% low、GC 采样
python Tools/verify.py --visual           # 追加视觉截图（需 GPU，不加 -nographics）
python Tools/verify.py --soak             # 追加长跑（默认 600 秒）
python Tools/verify.py --soak --soak-seconds 120
python Tools/verify.py --compare-visual   # 与 baseline 比较（需人工确认的基线）
python Tools/verify.py --all              # = --visual --stress --perf
python Tools/verify.py --timeout 600      # 单阶段超时（秒，默认 420）
```

| 选项 | 用途 | 耗时量级（本机 RTX 4060 笔记本） |
|---|---|---|
| 无参数 | 编译 + EditMode + PlayMode | 3–4 分钟 |
| `--edit-only` | 只跑 EditMode | 约 1 分钟 |
| `--visual` | 生成 20 张截图（需 GPU） | 分钟级 |
| `--stress` | ≥ 200 次快速切换 / 资源加载 / 环境自检 | 分钟级 |
| `--perf` | 帧时间与 GC 采样 | 分钟级 |
| `--soak` | 长跑稳定性 | 长跑时长 + 约 4 分钟 |
| `--compare-visual` | 基线逐张比较 | 分钟级 |

脚本退出码：`0` 全部通过 / `1` 有失败 / `2` 环境问题（找不到 Unity、结果文件缺失等）。

若 Unity 不在默认路径，设置环境变量：

```bash
export UNITY_PATH="D:/Unity/Unity 2022.3.22f1c1/Editor/Unity.exe"   # Git Bash
# PowerShell: $env:UNITY_PATH = "D:\Unity\Unity 2022.3.22f1c1\Editor\Unity.exe"
```

### 5.5 构建 Windows 可执行

1. `File > Build Settings`；
2. 平台选 **Windows, Mac, Linux** → Target Platform **Windows**、Architecture **x86_64**；
3. 确认 `Scenes In Build` 已包含 `MainMenu` 与 `PianoLesson`（项目已配置）；
4. `Build`，选择输出目录（如 `Build/`）。

> `Build/` 已在 `.gitignore` 中排除。构建产物中的 `StreamingAssets/Songs/` 会随包携带示例曲。

### 5.6 预期结果

```text
====================================================================
结果汇总
====================================================================
[EditMode] 通过 — 总计 285，通过 285，失败 0，跳过 0
[PlayMode] 通过 — 总计 87，通过 82，失败 0，跳过 5
    SKIP: CaptureLessonViews            ← 无图形设备时预期跳过
    SKIP: SampleFrameTiming             ← [Explicit]
    SKIP: SampleFrameTiming_UnderSwitchingLoad
    SKIP: Soak_PlaysContinuously
    SKIP: CompareAgainstBaseline

✓ 全部通过
```

| 指标 | 预期值 | 来源 |
|---|---|---|
| 编译 | 0 错误 0 警告 | `_verify_out/compile*.log` |
| EditMode | 285 / 285 | `_verify_out/editmode.xml` |
| PlayMode | 82 通过 + 5 跳过 + 0 失败（总计 87） | `_verify_out/playmode.xml` |
| Stress / Perf / Soak | 3/3、4/4、1/1 | `_verify_out/{stress,perf,soak}.xml` |
| 动作质量 | 手腕命中率 93.6%（左 94.7 / 右 92.7）、单帧位移 ≤ 35 mm、穿模 0 | `HandMotionTests` |
| 视觉 | 20 张截图 + 自动清单 | `Docs/screenshots/` |

---

## 6. 配置项说明

### 6.1 项目级配置

| 文件 | 关键项 | 说明 |
|---|---|---|
| `ProjectSettings/ProjectVersion.txt` | `2022.3.22f1c1` | 引擎版本锁定，改动会导致项目升级 |
| `ProjectSettings/EditorBuildSettings.asset` | `MainMenu`、`PianoLesson` | 构建场景列表 |
| `Packages/manifest.json` | 官方包版本 | 无第三方包；新增依赖前先评估许可证 |
| `Assets/Scripts/PianoTeacher3D.asmdef` | `autoReferenced: true` | 运行时代码统一程序集 |
| `Assets/Tests/*/**.asmdef` | `overrideReferences` + `nunit.framework.dll` | 测试程序集，`UNITY_INCLUDE_TESTS` 约束 |
| `ProjectSettings/QualitySettings.asset` | 由 `QualityProfile.Apply()` 运行时覆盖 | 阴影分辨率 / 距离 / 软硬 / 抗锯齿 / 目标帧率 |

### 6.2 运行时可调参数（Inspector）

教学场景中的组件均由 `LessonSessionController` 在运行时 `AddComponent`，默认值写在字段初始化器中。
需要在编辑器里调参时，可临时把组件挂到场景对象上调整后回填默认值。

#### 相机 `CameraModeController`

| 参数 | 默认值 | 作用 |
|---|---|---|
| `OverviewYaw` / `OverviewPitch` / `OverviewFov` | 34° / 16° / 50° | 全景前侧 3/4 角度与基准视野 |
| `OverviewMinFov` / `OverviewMaxFov` | 40° / 62° | 全景 FOV 上下限 |
| `OverviewMinDistance` / `OverviewMaxDistance` | 1.2 / 4.5 m | 全景距离范围 |
| `OverviewFrameMargin` | 1.12 | 构图边界余量 |
| `OverviewFocusOffset` | (0, 0.02, 0) | 焦点微调 |
| `OverviewReferenceHeight` / `OverviewHeightCompensation` | 1.45 m / 0.35 | 角色身高轻量校正 |
| `OverviewFollowDamping` / `OverviewMaxFovStepPerSecond` | 6 / 20 | 跟随阻尼与 FOV 变化限速 |
| `KeyboardUnobstructedSubView` | `false` | 「键盘无遮挡」教学子视角（显式开关，默认关闭） |
| `CloseupBaseFov` / `CloseupMaxFov` | 30° / 48° | 特写 FOV 上下限 |
| `CloseupMinDistance` / `CloseupMaxDistance` | 0.20 / 0.88 m | 特写距离范围 |
| `CloseupFrameMargin` / `CloseupHandPadding` | 1.28 / 0.045 m | 边界余量与手部外扩 |
| `CloseupFingerTipReach` | 0.05 m | 指根 → 指尖延伸估计 |
| `CloseupVerticalFollowLimit` | 0.035 m | 焦点垂直跟随幅度上限（防俯仰抖动） |
| `CloseupViewPitch` / `CloseupViewYaw` | **60°** / 0° | 特写观察角。**不要低于 41°**：否则相机后移量会让角色头部挡在相机与双手之间 |
| `CloseupPrimaryShare` | 0.7 | 单手练习时主手最小宽度占比 |
| `CloseupKeyPredictSeconds` / `CloseupMaxPredictSpeed` | 0.12 s / 2.5 m/s | 「即将发生的琴键」轻量预测与限速 |
| `TransitionDuration` / `ResetTransitionDuration` | 0.35 / 0.30 s | 模式切换与重置的缓动时长 |
| `OrbitSensitivity` / `ZoomSensitivity` | 4 / 3 | 自由观察灵敏度 |
| `MinDistance` / `MaxDistance` | 0.5 / 6 m | 自由观察距离范围 |
| `MinPitch` / `MaxPitch` | 5° / 85° | 俯仰限制（防翻转） |
| `MinHeight` | 0.15 m | 防穿地 |
| `FreeCollisionPadding` | 0.12 m | 防穿琴体 / 人物 |
| `KeySpanHoldSeconds` | 0.5 s | 琴键范围提示的保持时间 |

#### 手部 IK `HandIKController`

| 参数 | 默认值 | 作用 |
|---|---|---|
| `HandHeight` / `HandDepth` | 0.768 m / 0.12 m | 手腕目标高度与深度（键面约 0.742 m） |
| `MoveSpeed` | 12 | 基准移动平滑速度 |
| `MinSpeedScale` / `MaxSpeedScale` | 0.55 / 1.8 | 音符稀疏 / 密集时的速度倍率 |
| `SlowGapSeconds` / `FastGapSeconds` | 0.6 / 0.12 s | 速度倍率对应的平均音符间隔 |
| `GlideThreshold` / `GlideLift` | 0.07 m / 0.055 m | 换位判定与抬升高度 |
| `MaxStepPerFrame` | **0.035 m** | 单帧位移硬约束（防瞬移），**不建议放宽** |
| `HandLeadSpeedScale` | 0.7 | 预置（音符到来前移动）的速度倍率 |
| `MaxPalmCorrection` / `PalmCorrectionSpeed` / `PalmCorrectionGain` | 0.11 m / 14 / 0.8 | 手掌几何反馈的幅度、阻尼与增益（让指尖贴上键） |
| `TargetMaxSpeed` / `TargetJumpThreshold` | 4 m/s / 0.20 m | 目标变化限速与「同手位内直接对齐」阈值 |
| `EnablePoseClamp` | `true` | 越界姿态钳制（防穿模） |
| `EnableMetrics` / `HitRadius` / `MovingThreshold` | `true` / 0.09 m / 0.0006 m | 指标采样、命中判定半径、移动判定 |
| `EnableFingertipMetrics` / `FingertipErrorTolerance` | `true` / 0.035 m | 指尖误差统计与命中容差 |
| `FingertipTargetForward` / `FingertipReachEstimate` | 0.80 / 0.085 m | 按压区沿键长比例与指尖长度估计 |
| `HoverHeight` | 0.045 m | 高于键面多少算「离键悬空」 |
| `EnableWristOrientation` / `DefaultWristPitch` | `true` / 8° | 手腕受限俯仰（角色可用 `HandMetrics.WristPitch` 覆盖） |
| `MaxWristRollDegrees` / `WristOrientationSpeed` | 6° / 7 | 键盘两端的手腕滚转上限与跟随速度 |
| `FingertipPenetrationTolerance` | 0.004 m | 指尖压入键面的判穿模容差 |
| `TransitionStepThreshold` | 0.010 m | 判定「换位过渡帧」的手腕单帧位移阈值 |

#### 手指姿态 `FingerPoseController`

| 参数 | 默认值 | 作用 |
|---|---|---|
| `PressAngle` / `RestAngle` | 32° / 18° | **兜底**按下 / 自然弯曲角度（手部无三关节时使用） |
| `SlerpSpeed` | 15 | 指根对准的插值速度 |
| `SpreadAngle` | 4° | 兜底和弦手型张开角 |
| `RestCurlDegrees` | 26° | 静止时的自然弯曲总量（三关节路径） |
| `ApproachCurlScale` / `HoldSettleDegrees` | 0.62 / 2.5° | 接近阶段的手型比例 / 保持阶段的额外下压 |
| `MaxMcpDegrees` / `MaxPipDegrees` / `MaxDipDegrees` | 82° / 95° / 62° | 各关节角度上限（超限钳制并计数） |
| `JointSlerpSpeed` / `HoldSettleSeconds` | 20 / 0.12 s | 关节插值速度 / Press→Hold 的分界 |
| `McpWeight` / `PipWeight` / `DipWeight` | 0.46 / 0.34 / 0.20 | **静止姿态**的弯曲分配权重（落键姿态由 CCD 求解） |
| `MelodyHistoryLength` | 8 | 指法提供器使用的旋律历史长度 |

#### 钢琴几何 `PianoKeyMap`

| 参数 | 默认值 | 作用 |
|---|---|---|
| `WhiteWidth` / `WhiteLength` / `WhiteHeight` | 0.024 / 0.15 / 0.022 m | 白键尺寸（24 mm 键宽是全项目人体标尺） |
| `BlackWidth` / `BlackLength` / `BlackHeight` | 0.014 / 0.10 / 0.032 m | 黑键尺寸（黑键面高于白键 10 mm） |
| `WhiteKeyChamfer` / `BlackKeyChamfer` | 0.0008 / 0.0006 m | 键面倒角 |
| `KeybedHeight` | 0.72 m | 地面至键面下沿 |

> 这些常量被手部 IK、姿态校验、相机取景共同依赖。**改键宽 / 键高必须同步复核**
> `AvatarBodyMetrics`、`HandPoseValidator` 与 `CameraVisibilityTests`。

#### 时间轴、节拍器与音频

| 组件 | 参数 | 说明 |
|---|---|---|
| `LessonTimeline` | `PlaybackSpeed`（钳制 ≥ 0.1；偏好保存范围 0.1–4）、`IsLooping`、`LoopStart/LoopEnd` | 由会话层对外暴露；直接改它不会同步 UI |
| `Metronome` | `Enabled`、`BeatsPerBar`（1–16） | 拍点表由 tempo map 生成 |
| `PianoAudioSynth` | `MaxVoices = 24`、`DefaultFadeSeconds = 0.12`、`Muted`、`SetMasterVolume()` | 声部池上限与淡出时长；`Muted` 可静音继续 |

#### UI 令牌 `UITheme`

| 类别 | 关键值 |
|---|---|
| 背景 / 面板 | `SceneBg (0.075,0.082,0.098)`、`PanelBg`、`PanelBgRaised`、`PanelBgDeep` |
| 主操作色 | `Accent #D9A441`、`AccentBright #F0C674`、`AccentDim` |
| 语义色 | `Error`、`Warning`、`Success`、`LeftHandColor`（冷蓝）、`RightHandColor`（暖橙）、`UpcomingColor`（暖金） |
| 字号 | 页面标题 34 / 标题 24 / 曲名 22 / 主操作 20 / 按钮 18 / 正文 16 / 小字 15 / 说明 13 |
| 间距 | 4 / 8 / 12 / 16 / 24 |
| 圆角 | 8 / 14 / 22 |
| 控件高度 | 主按钮 48 / 普通 40 / 输入 36 / 小 32 |
| 布局断点 | 参考分辨率 1280×720；顶栏 44；主栏 92（紧凑 60）；工具栏 124（折叠 36）；窄窗阈值 1180 |

> **规则**：任何 UI 脚本不得硬编码颜色 / 圆角 / 控件高度，只能引用 `UITheme` 令牌
> （`UIThemeTokenTests` 会检查关键令牌的对比度与状态差异）。

### 6.3 画质档位

| 档位 | 阴影分辨率 | 阴影距离 | 阴影类型 | 强度 | 接触阴影 | 目标帧率 |
|---|---|---|---|---|---|---|
| **Standard（标准）** | 1024 | 8 m | 硬阴影 | 0.55 | 近似开启 | 60 |
| **High（高画质）** | 2048 | 18 m | 软阴影 | 0.72 | 近似开启 | 60 |

- 设备推荐由 `QualityProfile.DetectRecommended()` 集中判断（无 GPU / SM < 4.5 / 显存 < 2 GB → Standard）。
- 切换档位**只降渲染质量**：不会关闭琴键高亮、不会移除补光与键盘点光、不会禁用阴影。
- 默认偏好为 `QualityTier = 1`（High）；可在偏好文件中改为 `0`。

### 6.4 用户偏好文件

位置：`Application.persistentDataPath/PianoTeacher3D/user-prefs.json`（`JsonUtility` 序列化）。

```json
{
  "LastSongId": "a1b2c3d4e5f60718",
  "LastCameraMode": 1,
  "Speed": 1.0,
  "PracticeHand": 0,
  "HighlightMode": 3,
  "AvatarIndex": 0,
  "MetronomeEnabled": false,
  "BeatsPerBar": 4,
  "HighlightLookAhead": 0.6,
  "PracticeToolsCollapsed": false,
  "DisplayMode": 0,
  "QualityTier": 1,
  "RecentSongIds": []
}
```

| 字段 | 含义 | 合法范围 / 默认 |
|---|---|---|
| `LastSongId` | 上次曲目内部 ID | 空字符串表示无 |
| `LastCameraMode` | 视角 | 0–2，默认 1（全景） |
| `Speed` | 速度倍率 | 0.1–4，默认 1.0 |
| `PracticeHand` | 练习手 | 0–2，默认 0（双手） |
| `HighlightMode` | 高亮模式 | 0–3，默认 3（全部） |
| `AvatarIndex` | 角色下标 | 0–2，默认 0 |
| `MetronomeEnabled` | 节拍器 | 默认 `false` |
| `BeatsPerBar` | 每小节拍数 | 1–16，默认 4 |
| `HighlightLookAhead` | 预提示提前量 | 0.05–3 s，默认 0.6 |
| `PracticeToolsCollapsed` | 练习工具栏折叠 | 默认 `false` |
| `DisplayMode` | 显示模式 | 0 展示 / 1 教学，默认 0 |
| `QualityTier` | 画质档位 | 0 标准 / 1 高画质，默认 1 |
| `RecentSongIds` | 最近曲目 ID | 最多 20 条 |

**容错**：文件缺失 / 空 / JSON 损坏 / 数值越界都会回落默认值（`LoadDiagnostic` 保留诊断），
解析失败时**不覆盖**用户原文件；下次成功保存才重写。

> `-batchmode` 下**故意不套用**用户偏好（`Application.isBatchMode`），保证测试从确定性初始状态出发。

### 6.5 数据目录与文件

| 内容 | 路径 |
|---|---|
| 用户数据根 | `Application.persistentDataPath/PianoTeacher3D/` |
| 用户导入曲目副本 | `…/PianoTeacher3D/Songs/` |
| 最近曲目索引 | `…/PianoTeacher3D/library.json` |
| 用户偏好 | `…/PianoTeacher3D/user-prefs.json` |
| 内置示例曲（只读） | `Assets/StreamingAssets/Songs/` |

Windows 下 `persistentDataPath` 通常为 `C:\Users\<用户>\AppData\LocalLow\<公司名>\<产品名>\`。

### 6.6 环境变量

| 变量 | 作用 | 默认 |
|---|---|---|
| `UNITY_PATH` | 指定 Unity Editor 可执行文件 | 脚本内置常见安装路径探测 |
| `PT3D_SOAK_SECONDS` | 长跑测试时长（秒），由 `--soak-seconds` 写入 | 600 |

### 6.7 编译期常量（不可运行时修改）

| 常量 | 值 | 位置 |
|---|---|---|
| `PianoKeyMap.MinPitch` / `MaxPitch` / `KeyCount` | 21 / 108 / 88 | 键盘音域 |
| `WhiteKeyCount` | 52 | 白键数 |
| `PianoNoteFilter.SplitPitch` | 60（C4） | 左右手分界 |
| `PianoAudioSynth.MaxVoices` | 24 | 声部池上限 |
| `MidiFileValidator.MaxFileBytes` | 8 MB | 导入大小上限 |
| `MidiFileValidator.MaxNoteCount` / `MaxDurationSeconds` | 200,000 / 3600 s | 复杂度上限 |
| `UserPreferences.Defaults.MaxRecentSongIds` | 20 | 最近曲目上限 |
| `Metronome.MaxBeats` | 20,000 | 拍点表上限 |

---

## 7. 主要模块调用关系

### 7.1 分层与依赖方向

```text
┌──────────────────────────── 表现层（只改表现，不碰时间轴） ────────────────────────────┐
│  LessonControlsUI   UIStatusBar   AvatarSelectionUI   FileBrowserUI   UIShader/Theme    │
│  CameraModeController   AvatarManager   PianoKeyAnimator   PianoKeyHighlighter          │
│  PianoSceneEnvironment                                                                  │
└───────────────────────────────────────┬─────────────────────────────────────────────────┘
                                        │ 只读状态 / 只调用会话公开接口
┌───────────────────────────────────────▼─────────────────────────────────────────────────┐
│                        会话层 LessonSessionController（门面）                            │
│  状态机 · 切曲事务 · 错误出口 · 偏好持久化 · 显示模式 · 模块接线                          │
└───────┬───────────────────────┬───────────────────────┬─────────────────────────────────┘
        │                       │                       │
┌───────▼────────┐   ┌──────────▼─────────┐   ┌─────────▼──────────────┐
│  LessonTimeline │   │ MidiEventScheduler │   │ HandIKController       │
│  （唯一时间源）  │   │ （唯一事件源）      │   │ FingerPoseController   │
└────────────────┘   └──────────┬─────────┘   └─────────┬──────────────┘
                                │ OnNoteOn / OnNoteOff  │
        ┌───────────────────────┼───────────────────────┼────────────────────┐
        ▼                       ▼                       ▼                    ▼
 PianoKeyAnimator        PianoAudioSynth        PianoKeyHighlighter   HandIK / FingerPose
                                                                      （共用指法分配）
┌──────────────────────────── 服务层（不依赖场景，可单测） ───────────────────────────────┐
│ MidiParser · MidiFileValidator · SongAnalyzer · PianoNoteFilter · SongLibraryService     │
│ UserPreferences · QualityProfile · CameraFraming · HandPoseValidator · PianoMeshFactory  │
│ HeuristicFingeringProvider · FingerAssignmentService                                     │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```

**硬规则**：

- 只有 `LessonTimeline` 计时；其他模块不得自建计时器。
- 只有 `MidiEventScheduler` 发音符事件；表现层不得自行推断「现在该按哪个键」。
- 表现层不得写回时间轴 / 播放状态；相机与角色切换不得触发重新加载曲目。
- 底层模块只产出数据（`AppError` / `ImportCheck` / `FingeringPlan`），不做 UI 决策。

### 7.2 启动装配顺序

`LessonSessionController.Awake()` → `BuildScene()` 按固定顺序装配（顺序有依赖，不可随意调整）：

| 步骤 | 创建 / 动作 | 为什么在这个位置 |
|---|---|---|
| 1 | `LessonTimeline` | 后续所有模块都要读时间 |
| 2 | `ErrorReporter`（注入状态提供者） | 后续初始化失败要能上报 |
| 3 | `MidiEventScheduler` | 事件源先就位 |
| 4 | `PianoKeyMap.BuildKeys()` + 居中 | 琴键坐标是 IK / 取景 / 高亮的基础 |
| 5 | `PianoSceneEnvironment.Build(keyMap, quality)` + 关闭场景残留灯光 | 依赖钢琴包围盒；画质在此落地 |
| 6 | `PianoKeyAnimator` / `PianoAudioSynth` / `PianoKeyHighlighter` / `Metronome` | 都依赖 `PianoKeyMap` |
| 7 | `AvatarManager.BuildAvatars()`（+Z 侧、旋转 180° 面向 −Z） | 手部 IK 依赖骨架 |
| 8 | `FingerAssignmentService` → `HandIKController.Initialize` → `FingerPoseController.Initialize` | 两个控制器必须共享同一份分配 |
| 9 | 相机设置 + `CameraModeController.Initialize(cam, avatar, keyMap)` | 依赖人物与琴键包围盒 |
| 10 | `LessonControlsUI.Initialize(this)` + `AvatarSelectionUI` | UI 只读会话公开接口 |
| 11 | 事件连线（见 7.4） | 所有订阅者都已创建 |

`Start()`：若有主菜单待导入标记 → 打开文件选择器；否则加载初始曲目 → `ApplyPreferences()`。

### 7.3 每帧数据流

```text
LessonSessionController.Update()
  │
  ├─ current = LessonTimeline.CurrentTime                ← 唯一时间源
  │
  ├─ if (current < lastTime)   // 循环回跳 / Seek 回退
  │     ResetPlaybackState() → scheduler.SeekTo(current) → camera.InvalidateFraming()
  │  else
  │     scheduler.Tick(current)  ──→ OnNoteOn / OnNoteOff
  │
  ├─ UpdateHighlightUpcoming(current)
  │     scheduler.CollectNotesInRange(current, current + lookAhead, buffer)  // 二分
  │     highlighter.RefreshUpcoming(buffer)
  │     camera.SetFramingKeySpan(minPitch, maxPitch)
  │
  ├─ UpdateHandLeadTargets(current)     // 音符到来前手腕先移向该键
  ├─ metronome.Tick(current)            // 拍点 → 击拍音 + OnMetronomeBeat
  ├─ handIK.Tick(current)               // 目标解算 + 姿态钳制 + 指标采样
  ├─ if (teaching) camera.SetFramingKeySpan(按下琴键范围)
  ├─ OnProgressChanged(current, total)  // UI 进度条
  └─ 节流保存偏好（脏标记 + 时间间隔）
```

### 7.4 事件连线

`LessonSessionController` 在 `BuildScene()` 第 11 步完成全部订阅：

| 事件 | 订阅者 | 作用 |
|---|---|---|
| `scheduler.OnNoteOn` | `PianoKeyAnimator` | 琴键下压 |
| | `PianoAudioSynth` | 发声（力度映射） |
| | `HandIKController` | 记录按下音高、更新目标 |
| | `FingerPoseController` | 弯曲对应手指 |
| | `PianoKeyHighlighter` | 发声高亮（按左右手分色） |
| | `LessonSessionController.TrackPressedNote` | 教学模式取景 / 一致性验证 |
| `scheduler.OnNoteOff` | 上述五者 + `UntrackPressedNote` | 抬键、Note Off 淡出、复位手指与高亮 |
| `timeline.OnStateChanged` | `HandleTimelineStateChanged` | 同步 `LessonState` 与 UI |
| `metronome.OnBeat` | 会话转发 `OnMetronomeBeat` | 拍点圆点 |
| 会话事件 | `LessonControlsUI` / `UIStatusBar` / `AvatarSelectionUI` | 进度、状态、错误、视角、练习手等 |

UI 只订阅会话公开事件；订阅必须在 `OnDestroy` 中退订（项目已按此实现）。

### 7.5 状态机

| 状态机 | 取值 | 迁移要点 |
|---|---|---|
| 会话 `LessonState` | `NoSong → Loading → Ready → Playing ⇄ Paused → Error` | 切曲走事务：加载中可取消；失败时若有旧曲则回滚到旧状态，否则进入 `Error` |
| 播放 `PlaybackState` | `Stopped / Playing / Paused` | 视角、角色、分手、速度切换**不改变**该状态 |
| 相机 `CameraMode` | `Closeup / Full / Free` | 三状态各自持有姿态与取景策略；过渡可被新切换中断（以当前实际姿态为起点） |
| 显示模式 `DisplayMode` | `Showcase / Teaching` | 切换时重建按键 / 手部 / 高亮，但不动时间轴 |
| 导入 `ImportRejection` | `None / Missing / Unreadable / TooLarge / BadExtension / UnsupportedFormat / HeaderInvalid / NoNotes / ZeroDuration / TooComplex` | 每个分支都有用户文案与恢复动作 |

### 7.6 切曲事务

```text
LoadSong(path)
  ├─ 置 Loading（禁用控件、防重复点击）
  ├─ MidiFileValidator.Validate(path)          ← 只校验，不落盘
  │    └─ 失败 → AppError → 横幅 + 恢复动作；有旧曲则回滚，无旧曲进入 Error/NoSong
  ├─ 复制到用户目录（导入路径时）+ 生成 SongMetadata
  ├─ ResetPlaybackState()（清空按键 / 手部 / 高亮 / 按下音高集合）
  ├─ timeline.SetDuration() + scheduler.Load(notes)
  ├─ metronome.Load(tempoChanges, duration)
  ├─ SongAnalyzer.Analyze() → OnAnalysisReady
  ├─ camera.InvalidateFraming()（按新曲目范围重算）
  ├─ 置 Ready（或保留用户偏好中的速度 / 练习手 / 视角）
  └─ 失败路径绝不产生「半新半旧」的混合状态
```

### 7.7 扩展点

| 扩展点 | 接口 / 类型 | 现状 |
|---|---|---|
| 指法算法 | `IFingeringProvider` | 已落地 `HeuristicFingeringProvider`，可替换为人工编辑或模型推断 |
| 指尖目标来源 | `IFingertipTargetSource` | 由 `HandIKController` 实现，`FingerPoseController` 消费 |
| 角色与体型 | `AvatarDefinition` / `AvatarBodyMetrics` | 纯数据驱动，新增角色只需加一条定义 |
| 画质档位 | `QualityProfile` | 新增档位只需加一个静态实例并扩展枚举 |
| 错误文案 | `ErrorReporter` 映射助手 | 集中管理，避免散落在业务代码 |
| 视觉基线 | `VisualSnapshotTests` + `visual_diff.py` | 新增截图用例后按流程更新 `baseline/` |

---

## 8. 常见问题与注意事项

### 8.1 常见问题（FAQ）

| 问题 | 原因与处理 |
|---|---|
| 打开项目提示 Unity 版本不符 | 必须使用 **2022.3.22f1c1**；`ProjectVersion.txt` 已锁定，不建议升级 |
| `verify.py` 报「未找到 Unity Editor」 | 设置 `UNITY_PATH` 环境变量指向 `Unity.exe`（见 5.4） |
| PlayMode 里 `CaptureLessonViews` 被跳过 | 无图形设备（`-nographics`）时自动跳过；用 `--visual`（不加 `-nographics`）或在编辑器里跑 |
| 画面里看不到手 | 切到**手部特写**；教学模式下会**故意隐藏**手部，按 `T` 切回展示模式 |
| 点了播放没反应 | 顶部错误横幅会说明原因（无曲目 / 加载失败 / 音频不可用）；无曲目时按钮本就禁用 |
| 没有声音 | 检查横幅是否提示「音频不可用」；`PianoAudioSynth.LastInitError` 会说明原因，可静音继续 |
| 想查看动作质量指标 | `Tab` 打开曲目分析 → 展开「开发诊断」（默认折叠，不与学习操作混排） |
| 测试里为什么没套用我的偏好 | `-batchmode` 下**故意**跳过偏好套用，保证用例确定性（见 6.4） |
| 琴键高亮颜色代表什么 | 暖金 = 即将发生（预提示），冷蓝 = 左手发声，暖橙 = 右手发声 |
| 导入的曲目在哪 | `persistentDataPath/PianoTeacher3D/Songs/`，索引在同目录 `library.json` |
| 最近曲目打不开 | 原文件可能被移动 / 删除；界面会提示失效，需重新导入 |
| 教学场景在编辑器里是空的 | 场景元素在运行时由代码装配，`.unity` 文件只是极简骨架，属预期设计 |
| `-runTests` 之后 Unity 不退出 | 已知引擎行为；`verify.py` 按「结果 XML 生成即继续」处理，等脚本返回即可 |
| 截图比较全部超差 | 光照 / 显卡差异会导致像素变化；基线需人工确认后更新，像素差异只作辅助 |

### 8.2 使用注意

1. **不要修改键宽 / 键高而不复核下游**：`PianoKeyMap` 的几何常量被手部 IK、姿态校验、相机取景共同依赖。
2. **不要把 `CloseupViewPitch` 调到 41° 以下**：会让角色头部挡在相机与双手之间（已修过一次的回归）。
3. **不要放宽 `MaxStepPerFrame`**：它是防瞬移的硬约束；放宽会重新引入「手瞬移」观感。
4. **不要依赖"循环点击"表达状态**：新增控件优先用分段选择器 / 开关，让当前值可直接读出。
5. **不要在 UI 脚本硬编码颜色 / 尺寸**：统一走 `UITheme` 令牌。
6. **不要往 `StreamingAssets` 写用户数据**：内置资源只读，用户数据一律进 `persistentDataPath`。
7. **引入外部资源前先读** [`Docs/asset-licenses.md`](Docs/asset-licenses.md)：模型 / 贴图 / 动画 / 权重 / 音色 / 字体需分别登记。
8. **改左右方向前先读** [`Docs/hand-correspondence-and-display-modes.md`](Docs/hand-correspondence-and-display-modes.md)：左右约定牵动键盘坐标、骨架、判定与测试断言。

### 8.3 已知限制（诚实清单）

| 限制 | 说明 | 状态 |
|---|---|---|
| 音频是波形合成 | 基频 + 2/3/4 次谐波 + 指数衰减，零采样依赖的**降级模式**，不是真实钢琴采样 | 已支持（非最终音色） |
| 人物 / 钢琴是程序化网格 | 手部已到单块放样网格 + 三关节（MCP/PIP/DIP）+ 拇指对掌，但不是外部高质量模型；躯干侧影仍偏几何化 | 已支持（非最终模型） |
| 手指为三关节 + 铰链 IK | 每指 MCP/PIP/DIP 独立旋转，指尖由 CCD 送到键面；**无蒙皮权重**，极限弯曲时指节背面可能有轻微折痕 | 已实现 |
| 短指 / 大跨度够不到 | 小指（63 mm）与拇指（69 mm）在跨度 &gt; 95 mm 的组合下仍会差 25–35 mm；已计入 `JointLimitHits` 与 `FingertipSurfaceDistance` | 已记录（物理限制） |
| 指尖贴键精度 | 静态和弦中指 **7.4 mm**、拇指 24.8 mm、小指 34.5 mm（被分配指尖口径）；播放期与换位峰值需按 `Docs/hand-alignment.md` 的新口径重采基线 | 已改进（基线待重采） |
| 手部特写俯角被几何约束为 60° | 相机必须位于头部之前，否则头会挡住双手；代价是特写偏俯视 | 已支持（受限） |
| 接触阴影是近似 | 主光软阴影 + 贴地极淡渐变薄片；**没有**真实 SSAO / 后处理（内置管线 + 零第三方包） | 已支持（近似） |
| 「环境遮蔽 AO」不是后处理 | `QualityProfile.EnableAmbientOcclusion` 只表达观感，由贴地薄片近似 | 实验性 |
| 指法是规则化启发式 | 输出置信度并标注「估算指法」，不承诺专业指法 | 实验性 |
| 性能数据来自批处理 | 不含真实渲染负载，**未在集显 / 中档独显真机定稿** | 待真机 |
| 视觉回归靠人工 + 像素辅助 | 像素差异只作辅助，不能替代人工看图 | 实验性 |
| AI 动作生成 / 照片生成人物 / 音频转谱 | 方案 P2 阶段，未实现 | 计划中 |
| 移动端 / Web | 未验证，仅 Windows 桌面 | 计划中 |

### 8.4 开发约定与坑

| 约定 | 原因 |
|---|---|
| 命名空间用 `PianoTeacher3D.CameraControl`，不要用 `.Camera` | 与 `UnityEngine.Camera` 同名会导致类型解析冲突（CS0118 / CS0234） |
| 不要在 `PianoTeacher3D.*` 下定义 `Light` / `Material` / `Avatar` 等与引擎同名的类型 | 同上；`UnityEngine.Avatar` 需完全限定名 |
| Unity 原生对象（`Material` / `Mesh` / `AudioClip` / `Sprite`）不要写在字段初始化器里 | Unity 禁止在构造期创建原生对象 |
| `Update` 路径禁止 LINQ、每帧 `new`、字符串拼接、`GetComponentsInChildren` | 性能预算；容器预分配复用 |
| `Shader.Find("Standard")` 必须有回落 | 无图形设备或裁剪构建时可能返回 null |
| 修改场景后不必保存运行时对象 | 场景元素在运行时装配，保存场景只会留下空壳 |
| 测试里的临时探针文件用完即删 | 避免进入测试程序集拖慢验证 |
| 数据指标通过 ≠ 视觉正确 | 曾出现「投影点都在视口内，但画面被头发挡住」；遮挡类问题必须几何或像素级检查 |

### 8.5 许可证

首版全部为程序化生成或引擎内置资源（钢琴 / 人物 / 材质 / 音频 / 曲目；字体使用
`Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`），不含第三方代码、模型、贴图、动作数据或音频采样。
详见 [`Docs/asset-licenses.md`](Docs/asset-licenses.md)。

---

## 9. 文档索引

| 文档 | 内容 |
|---|---|
| [`Docs/项目说明.md`](Docs/项目说明.md) | **完整项目说明**（定位 / 目录 / 流程 / 依赖 / 环境 / 配置 / 命令 / 接口 / 限制，含目录） |
| [`Docs/how-to-run.md`](Docs/how-to-run.md) | 运行与验证详细指南（环境 / CLI 全选项 / 产物 / 基线更新 / FAQ） |
| [`Docs/architecture.md`](Docs/architecture.md) | 架构、模块职责与边界、状态机 / 时序 / 事件连线、配置与内容分离 |
| [`Docs/animation-pipeline.md`](Docs/animation-pipeline.md) | 动画管线、手部几何、指尖/手腕分离、指标口径、指法提供器 |
| [`Docs/hand-mesh-design.md`](Docs/hand-mesh-design.md) | **手部网格与关节契约**：局部坐标、`HandMetrics` 比例表、放样规则、三关节映射、铰链 IK、验收用例 |
| [`Docs/hand-alignment.md`](Docs/hand-alignment.md) | **手部指标口径**：五个指标的精确定义、已知口径缺陷、分组与阶段目标 |
| [`Docs/hand-correspondence-and-display-modes.md`](Docs/hand-correspondence-and-display-modes.md) | 左手/右手 · 手指↔琴键 · 展示/教学：现象、根因、改动与验证 |
| [`Docs/ui-design-system.md`](Docs/ui-design-system.md) | 设计令牌、按钮七态、层级、焦点与快捷键、响应式断点、对比度 |
| [`Docs/error-codes.md`](Docs/error-codes.md) | 错误码表、恢复动作表、异常覆盖表、日志与隐私规则 |
| [`Docs/visual-regression.md`](Docs/visual-regression.md) | 截图用例、视角可用性 9 场景、基线维护、人工看图记录 |
| [`Docs/quality-and-roadmap.md`](Docs/quality-and-roadmap.md) | 画质档位、性能排查顺序、验收门槛、路线图、设计取舍 |
| [`Docs/asset-licenses.md`](Docs/asset-licenses.md) | 资源许可证清单与外部资源登记流程 |
| [`Docs/completion-review.md`](Docs/completion-review.md) | 需求 → 实现逐条核对表与完成度小结 |
| [`Docs/optimization-report.md`](Docs/optimization-report.md) | 两轮优化的交付报告与实测数据 |
| [`Docs/research-notes.md`](Docs/research-notes.md) | 提供器接口与开源资源调研 |
| [`Docs/screenshots/manifest.md`](Docs/screenshots/manifest.md) | 截图清单（测试运行时自动生成） |
| [`Docs/perf/sampling.md`](Docs/perf/sampling.md) | 性能与长跑采样记录（测试运行时写入） |
| [`Docs/perf/environments.md`](Docs/perf/environments.md) | 发布前环境矩阵记录 |
