# 运行与验证指南（how-to-run.md）

> 项目：钢琴 3D 教学 APP（Piano Teacher 3D）
> 本文档说明**如何在本机把项目跑起来并验证结果**，分「一键命令行验证」「编辑器里体验」「GUI 测试」三种方式。

---

## 1. 环境依赖

| 依赖 | 版本 | 本机状态 | 说明 |
|---|---|---|---|
| Unity Editor | **2022.3.22f1c1**（revision `c3cbd310e76e`） | 已装：`D:\Unity\Unity 2022.3.22f1c1\` | 项目已锁定该版本（`ProjectSettings/ProjectVersion.txt`） |
| Unity Hub | 3.x | 已装：`D:\Unity\Unity HUB\` | 用于打开项目 |
| Python | 3.8+ | 已装（Anaconda 或系统 Python） | 仅一键验证脚本需要，**无需 pip 安装任何包** |
| Git | 2.x | 已装 | 可选，用于查看变更历史 |

**无需安装的**：任何第三方 Unity 包、模型资源、音频素材 —— 项目内所有资产（钢琴、人物、材质、音频、演示曲目）均为程序化生成，零外部依赖。

---

## 2. 项目结构

```
piano-teacher-3d/
├── Assets/
│   ├── Scenes/
│   │   ├── MainMenu.unity            # 主菜单（曲目选择入口）
│   │   └── PianoLesson.unity         # 教学主场景（运行时程序化装配全部内容）
│   ├── Scripts/
│   │   ├── Core/                     # 课程状态与统一时间轴
│   │   │   ├── LessonTimeline.cs         # 唯一时间源（Stopped/Playing/Paused + A-B 循环）
│   │   │   ├── LessonSessionController.cs# 门面：状态守卫 + 场景装配 + 模块连线
│   │   │   ├── Metronome.cs              # 节拍器（由时间轴驱动）
│   │   │   ├── SongAnalyzer.cs           # 曲目分析（统计 + 规则式练习建议）
│   │   │   ├── SongAnalysis.cs
│   │   │   ├── PlaybackState.cs
│   │   │   └── PracticeHand.cs
│   │   ├── MIDI/                     # 解析与调度（纯 C#，可单测）
│   │   │   ├── MidiParser.cs             # SMF 解析（VLQ/running status/tempo map）
│   │   │   ├── MidiEventScheduler.cs     # Note On/Off 调度 + 区间查询
│   │   │   ├── MidiNoteEvent.cs          # 标准化事件（pitch/start/duration/velocity/track/channel）
│   │   │   ├── TempoChange.cs            # 秒制 tempo 变化点
│   │   │   └── PianoNoteFilter.cs        # 左右手过滤 / 黑键判定
│   │   ├── Piano/                    # 钢琴与琴键表现
│   │   │   ├── PianoKeyMap.cs            # 程序化 88 键 + 立式琴体
│   │   │   ├── PianoKeyAnimator.cs       # 下压/回弹动画
│   │   │   ├── PianoKeyHighlighter.cs    # 高亮提示（MaterialPropertyBlock，零材质实例）
│   │   │   ├── PianoAudioSynth.cs        # 波形合成发声
│   │   │   └── HighlightMode.cs
│   │   ├── Avatar/                   # 人物与手部
│   │   │   ├── AvatarManager.cs          # 三角色预设 + 切换（不重置课程状态）
│   │   │   ├── AvatarRigBuilder.cs       # ★ 标准 Humanoid 骨架 + Avatar 生成
│   │   │   ├── HandIKController.cs       # 手腕目标 + 换位抬升 + 自适应速度 + 指标采样
│   │   │   ├── FingerPoseController.cs   # 手指分配（按音高比例）+ 和弦手型
│   │   │   ├── HandPoseValidator.cs      # 异常姿态校验 + 钳制（防穿模）
│   │   │   └── HandMotionMetrics.cs      # 动作质量指标（命中率/平滑度/协调）
│   │   ├── Camera/
│   │   │   └── CameraModeController.cs   # 手部特写 / 全景 / 自由观察
│   │   └── UI/
│   │       ├── UITheme.cs                # 统一色板 + 程序化圆角控件
│   │       ├── LessonControlsUI.cs       # 控制栏（12 按钮 + 进度 + 拍点 + 分析面板）
│   │       ├── AvatarSelectionUI.cs
│   │       └── MainMenuUI.cs
│   ├── StreamingAssets/Songs/demo.mid    # 演示曲目（120BPM，旋律+和弦+力度变化，7.5s）
│   └── Tests/
│       ├── EditMode/                     # 6 个文件：MIDI 解析 / 过滤器 / 拍点 / 分析器 / 边界
│       └── PlayMode/                     # 8 个文件：时间轴 / 视角角色切换 / 教学功能 /
│                                         #          错误处理 / 动作质量 / Humanoid 骨架 / 性能 / 视觉
├── Docs/
│   ├── architecture.md                   # 架构与数据流
│   ├── animation-pipeline.md             # 动画管线 + 阶段五改进 + 缺陷修复记录
│   ├── asset-licenses.md                 # 资源许可证清单
│   ├── research-notes.md                 # 开源资源调研（方案 10-2）
│   ├── how-to-run.md                     # ← 本文档
│   └── screenshots/                      # 视觉基线（6 张，可回归比对）
├── Tools/
│   └── verify.py                         # ★ 一键验证脚本
└── README.md
```

---

## 3. 运行方式 A：一键命令行验证（推荐，最快）

在项目根目录执行：

```bash
# 完整验证：编译 + EditMode + PlayMode
python Tools/verify.py

# 只跑纯逻辑测试（最快，约 1 分钟）
python Tools/verify.py --edit-only

# 追加视觉基线截图（需要 GPU，耗时更久）
python Tools/verify.py --visual
```

脚本会自动：① 定位 Unity（或读环境变量 `UNITY_PATH`）→ ② 编译检查 → ③ 跑 EditMode → ④ 跑 PlayMode → ⑤ 输出汇总。

**中间产物**在 `_verify_out/`（日志与结果 XML），可随时删除。

### 预期输出

```
====================================================================
结果汇总
====================================================================
[EditMode] 通过 — 总计 96，通过 96，失败 0，跳过 0
[PlayMode] 通过 — 总计 58，通过 57，失败 0，跳过 1
    SKIP: CaptureLessonViews

✓ 全部通过
```

**实测耗时**（本机 RTX 4060 笔记本）：编译 ~30s，EditMode ~60s，PlayMode ~110s，合计约 3.5 分钟。

> `CaptureLessonViews` 在无图形设备（`-nographics`）下会自动跳过 —— 这是预期行为；
> 用 `--visual` 或在编辑器里跑即可生成截图。

---

## 4. 运行方式 B：在 Unity 编辑器里体验（看实际效果）

1. 打开 **Unity Hub** → `Add project from disk` → 选择 `D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d`
   （版本已精确匹配 2022.3.22f1c1，不会提示版本不符）
2. 在 **Project 窗口**双击 `Assets/Scenes/MainMenu.unity`
3. 点顶部 **▶ Play** 按钮
4. 主菜单点曲目卡片 → 进入教学场景

### 操作说明

| 操作 | 方式 |
|---|---|
| 播放 / 暂停 | 底部「播放」按钮 或 **空格** |
| 停止 / 重播 | 底部按钮 或 **[** |
| 循环片段 | 「循环」按钮（激活态金色高亮） |
| 分手练习 | 「双手练习」按钮（双手 → 左手 → 右手） |
| 节拍器 | 「节拍器」按钮 或 **M**（控制栏上方出现拍点圆点） |
| 琴键高亮 | 「高亮提示」按钮 或 **H**（关闭 → 全部 → 仅发声 → 仅预提示） |
| 曲目分析 | 「曲目分析」按钮 或 **Tab**（右侧面板：统计 + 练习建议 + 动作质量） |
| 切换视角 | 「视角」按钮（手部特写 → 全景 → 自由观察）；自由观察用**右键旋转 + 滚轮缩放**，**R** 重置 |
| 切换角色 | 「切换角色」按钮 或右上角角色按钮 |
| 速度 | 「速度」按钮（0.5x → 1.0x → 1.5x） |
| 拖动进度 | 拖进度条（有圆形手柄） |

### 建议验证点（对应方案验收标准）

1. **播放中切换视角 / 角色**：音乐与进度**不中断、不重置**；
2. **手部特写**：能看清键盘、手腕与手指按键动作；
3. **全景**：能看清人物坐姿、肩臂位置与演奏姿态；
4. **曲目分析面板**（Tab）：显示命中率、平滑度、最大帧位移、穿模告警、双手协调；
5. **节拍器**（M）：开启后控制栏出现 4 个拍点圆点，强拍闪烁。

---

## 5. 运行方式 C：Unity Test Runner（GUI 跑测试）

1. Unity 编辑器 → 菜单 `Window > General > Test Runner`
2. **EditMode** 标签页 → `Run All`（纯逻辑测试，秒级）
3. **PlayMode** 标签页 → `Run All`（运行时测试，约 1–2 分钟）
4. 绿色 = 通过；点击失败项可看到断言消息

---

## 6. 预期结果一览（本轮基线）

| 指标 | 预期值 | 来源 |
|---|---|---|
| 编译 | 0 错误 0 警告 | `Tools/verify.py` |
| EditMode 测试 | **96 / 96 通过** | 6 个测试文件 |
| PlayMode 测试 | **57 通过 + 1 跳过 + 0 失败** | 8 个测试文件 |
| 目标琴键命中率 | **≥ 80%**（实测 82.9%） | `HandMotionTests.PlayingSession_KeyHitRateAboveThreshold` |
| 最大单帧位移 | < 150 mm（实测 35 mm） | 同上 |
| 穿模告警 | **0** | `HandMotionTests.PlayingSession_ProducesNoPoseViolations` |
| Humanoid 骨架 | `avatar.isValid && avatar.isHuman == true`（3 个角色） | `HumanoidSceneTests` |
| 播放帧率 | 逻辑层 > 2800 FPS（无渲染负载） | `PerformanceTests` |

---

## 7. 常见问题

| 现象 | 原因与处理 |
|---|---|
| `verify.py` 报「未找到 Unity Editor」 | 安装路径不同时，设置环境变量：`set UNITY_PATH=D:\你的路径\Unity.exe` |
| 测试阶段显示「(Unity 未退出，但结果已生成)」 | Unity 已知行为（退出阶段偶发挂住），脚本已自动处理，结果有效 |
| PlayMode 里 `CaptureLessonViews` 被跳过 | 无图形设备时自动跳过；用 `--visual` 或编辑器里跑即可生成截图 |
| 编辑器打开时提示版本不符 | 确认安装的是 **2022.3.22f1c1**（中国版），`ProjectVersion.txt` 已锁定该版本 |
| 画面里看不到人物手部 | 按「视角」切到**手部特写**；全景下手在键盘上方 |
