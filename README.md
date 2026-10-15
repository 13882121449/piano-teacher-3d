# Piano Teacher 3D — 钢琴 3D 教学 APP

> 以 **3D 钢琴演奏可视化**为核心的 MIDI 教学应用：加载曲目、观察虚拟人物演奏、切换观察视角与人物形象，
> 并提供慢速、循环、分手练习、节拍器、音符高亮与练习反馈等教学功能。

| 项目信息 | 值 |
|---|---|
| 项目代号 | Piano Teacher 3D |
| 首版平台 | Windows 桌面端 |
| 引擎 / 语言 | Unity **2022.3.22f1c1**（中国版，revision `c3cbd310e76e`）/ C# |
| 对应设计方案 | `../钢琴3D教学APP-项目设计方案.md`（v0.1） |
| 当前完成度 | MVP（方案阶段一~四）+ 阶段五教学功能与动作质量量化 |
| 第三方资源 | **无**（钢琴 / 人物 / 材质 / 音频 / 曲目全部程序化生成） |

---

## 1. 主题、目标与用途

### 1.1 主题

用 3D 可视化把「抽象的音符事件」翻译成「可观察的演奏动作」——
用户看到的不再是滚动的五线谱，而是一台真实的钢琴、一双会移动的手，以及每个音符对应的琴键起落。

### 1.2 项目目标（首版 MVP）

| # | 目标 | 状态 |
|---|---|---|
| 1 | 展示可交互的 3D 钢琴场景 | ✅ |
| 2 | 导入/选择 MIDI 曲目并播放、暂停、继续、停止、重新播放 | ✅ |
| 3 | 按 MIDI 事件同步触发琴键动画与对应音符声音 | ✅ |
| 4 | 「手部特写」与「全景」两种主要视角，可平滑切换 | ✅ |
| 5 | 鼠标控制的自由观察（旋转、缩放） | ✅ |
| 6 | 至少 3 个预设虚拟人物形象切换 | ✅ |
| 7 | 角色/视角切换时音乐与演奏进度保持连续 | ✅ |
| 8 | 基础错误提示（解析失败、资源加载失败等） | ✅ |

**后续增强目标**（方案 2.2）：慢速播放 ✅ · 循环片段 ✅ · 左右手单独练习 ✅ · 节拍器 ✅ ·
音符/琴键高亮 ✅ · 基于 MIDI 的练习反馈 ✅ · AI 手部动作生成 ⏳ · 跨平台适配 ⏳

### 1.3 用途与目标用户

| 场景 | 说明 |
|---|---|
| **自学者跟练** | 用「手部特写」观察手型与指法走向，配合 0.5x 慢速与 A-B 循环攻克难点小节 |
| **分手练习** | 用「左手/右手练习」模式单独听、看单声部，先分后合 |
| **节奏训练** | 开节拍器，跟随曲目 tempo 的强弱拍建立稳定拍感 |
| **跟弹提示** | 开「高亮提示」，琴键在音符到来前亮起（金），发声时按左右手分别显示蓝/橙 |
| **曲目预习** | 用「曲目分析」查看左右手负担、和弦数、最大跨度、最快音间隔，并得到规则式练习建议 |
| **开发参考** | 一份可运行、可测试的「MIDI → 3D 表现」参考实现（含 Humanoid 骨架、IK、程序化资产） |

### 1.4 设计原则（方案 1）

1. **统一时间轴** — 音乐、琴键、人物动作、UI 进度由同一套时间控制（`LessonTimeline` 是唯一时间源）。
2. **视角与演奏解耦** — 切换视角不暂停、不重启音乐。
3. **人物与演奏解耦** — 切换角色只替换表现，不重置曲目、进度或场景状态。
4. **先完成可靠的 MVP** — 先做 MIDI 驱动琴键与基础手部 IK，再研究 AI 动作生成。
5. **遵守资源许可证** — 代码、模型、动作数据、权重、音乐与音频样本分别核对许可证。

---

## 2. 项目简介

### 2.1 是什么

一个 **Unity + C#** 的 Windows 桌面应用。运行时由代码**程序化搭建**整个场景（钢琴、人物、材质、灯光、UI），
不依赖任何外部模型或音频文件——因此仓库体积小、零许可风险、开箱即跑。

### 2.2 技术栈

| 模块 | 实现 |
|---|---|
| 引擎 | Unity 2022.3 LTS（版本已锁定于 `ProjectSettings/ProjectVersion.txt`） |
| 语言 | C#，命名空间 `PianoTeacher3D.{Core,MIDI,Piano,Avatar,CameraControl,UI}` |
| MIDI | **自研纯 C# `MidiParser`**（SMF 解析：VLQ / running status / tempo map / Format 0-2），不依赖第三方库 |
| 音频 | `PianoAudioSynth` 波形合成 + `Metronome` 程序化击拍音（零采样资产） |
| 人物 | **标准 Humanoid 骨架**（程序化构建，`AvatarBuilder.BuildHumanAvatar` 生成合法 Avatar）+ 程序化低模网格 |
| 手部 | 自定义 IK（手腕目标 + 换位抬升 + 自适应速度）+ 手指按音高比例分配 |
| 3D | 88 键钢琴（真实尺度：键宽 24mm、键面高 0.72m）+ 立式琴体 |
| 测试 | Unity Test Framework（EditMode 96 条 + PlayMode 58 条） |
| 版本管理 | Git（当前 12 次提交） |

### 2.3 项目状态

| 维度 | 现状 |
|---|---|
| 代码规模 | 28 个脚本 · 17 个测试文件 · 6 篇文档 |
| 编译 | 0 错误 0 警告 |
| 测试 | EditMode **96/96** · PlayMode **57 通过 + 1 跳过 + 0 失败** |
| 质量指标 | 目标琴键命中率 **82.9%** · 最大帧位移 **35mm** · 穿模告警 **0** |
| 验证方式 | 一条命令 `python Tools/verify.py` 端到端跑通 |

---

## 3. 功能说明

### 3.1 播放与曲目

- 从 `Assets/StreamingAssets/Songs/*.mid` 加载曲目；主菜单可选择曲目后进入教学场景。
- 播放控制：**播放 / 暂停 / 继续 / 停止 / 重新播放**；进度条可拖动跳转。
- 速度档位：**0.5x / 1.0x / 1.5x**。
- 解析失败、文件被占用、无曲目目录、曲目无音符等情况均给出**用户可见的错误提示**，不会崩溃。

### 3.2 视角系统

| 模式 | 行为 |
|---|---|
| **手部特写** | 跟随双手中心，从后上方俯视键面，可看清手指按键与手部移动 |
| **全景** | 从演奏者右侧方观察，可看清**坐姿、肩臂位置、手与完整琴身** |
| **自由观察** | 右键拖动旋转 + 滚轮缩放，可 `R` 键重置回预设视角 |

三种模式间**平滑过渡**；连续切换视角**不会**暂停或重置演奏（有自动化测试守护）。

### 3.3 虚拟人物

3 个预设形象（写实女性 / 写实男性 / 卡通），差异体现在头身比、发型（长发 / 短发 / 呆毛）、服装与配色。

每个角色持有一套**标准 Humanoid 骨架**（30 个节点：Hips/Spine/Chest/UpperChest/Neck/Head + 两段手臂 + 腿 + 每手 5 指），
并由 `AvatarBuilder.BuildHumanAvatar` 生成合法 `Avatar`（运行时可断言 `avatar.isValid && avatar.isHuman`）。
骨架为纯坐标节点，视觉由程序化网格提供 —— 未来接入真实 GLB 模型时，只需把外部网格挂到同一套骨骼上。

**演奏中途切换角色不会重置音乐、进度或摄像机模式**（有自动化测试守护）。

### 3.4 教学功能

| 功能 | 用法 | 说明 |
|---|---|---|
| **慢速播放** | 「速度」按钮 | 0.5x / 1.0x / 1.5x 循环切换 |
| **A-B 循环 / 整曲循环** | 「循环」按钮 / `SetLoop(a, b)` | 循环跳转后自动重建按键与手部状态，不残留上一个循环的按下键 |
| **分手练习** | 「双手练习」按钮 | 双手 → 仅左手 → 仅右手；切换**保持播放进度连续**；某手无音符时给出提示 |
| **节拍器** | 「节拍器」按钮 / `M` | 拍点表由曲目 tempo map 生成，**由统一时间轴驱动**（不自建计时器）；强拍 1320Hz、弱拍 880Hz；控制栏显示拍点圆点 |
| **音符/琴键高亮** | 「高亮提示」按钮 / `H` | 4 档：关闭 → 预提示+发声 → 仅发声 → 仅预提示；预提示暖金、左手发声冷蓝、右手发声暖橙；预提示提前量默认 0.6s（可调 0.05–3s） |
| **曲目分析与练习反馈** | 「曲目分析」按钮 / `Tab` | 统计音符数、左右手分布、黑键比例、和弦数与最大跨度、音域、最快音间隔、BPM 范围；按规则给出练习建议（如"左手占 62%，建议先用左手练习模式"） |

### 3.5 动作质量与穿模防护

- **手腕路径**：换位抬升（水平位移 > 0.07m 时目标抬高最多 5.5cm 再回落）形成「抬起—平移—落下」弧线，避免贴键横扫。
- **单帧位移硬约束**：0.035m/帧（约 2.1 m/s），超过按比例截断，杜绝瞬移。
- **间隔自适应速度**：按最近音符平均间隔在 0.55x–1.8x 间插值（稀疏慢、密集快）。
- **异常姿态检查**：每帧校验手腕是否低于键面 / 出界 / 深入琴体，越界即钳回并计数（`PoseIssueCount`）。
- **量化指标**（在「曲目分析」面板实时显示）：命中率、最大帧位移、平滑度、双手协调率。

### 3.6 操作总表

| 操作 | 按钮 | 快捷键 |
|---|---|---|
| 播放 / 暂停 | 「播放」 | `空格` |
| 重新播放 | 「重播」 | `[` |
| 停止 | 「停止」 | — |
| 循环 / 分手练习 / 速度 | 同名按钮 | — |
| 节拍器 | 「节拍器」 | `M` |
| 高亮提示 | 「高亮提示」 | `H` |
| 曲目分析面板 | 「曲目分析」 | `Tab` |
| 视角 / 重置视角 | 「视角」/「重置视角」 | `R`（重置） |
| 切换角色 | 「切换角色」或右上角角色按钮 | — |
| 自由观察 | 右键拖动旋转、滚轮缩放 | — |

---

## 4. 目录结构

```text
piano-teacher-3d/
├── Assets/
│   ├── Scenes/
│   │   ├── MainMenu.unity           # 主菜单（曲目选择入口）
│   │   └── PianoLesson.unity        # 教学主场景（运行时程序化装配全部内容）
│   ├── Scripts/                     # 28 个脚本
│   │   ├── Core/                    # 课程状态与统一时间轴
│   │   │   ├── LessonTimeline.cs        # 唯一时间源（Stopped/Playing/Paused + A-B 循环）
│   │   │   ├── LessonSessionController.cs # 门面：状态守卫 / 场景装配 / 模块连线
│   │   │   ├── Metronome.cs             # 节拍器（时间轴驱动 + 程序化击拍音）
│   │   │   ├── SongAnalyzer.cs          # 曲目统计 + 规则式练习建议（纯 C#）
│   │   │   ├── SongAnalysis.cs
│   │   │   ├── PlaybackState.cs
│   │   │   └── PracticeHand.cs
│   │   ├── MIDI/                    # 解析与调度（纯 C#，可脱离 Unity 单测）
│   │   │   ├── MidiParser.cs            # SMF 解析（VLQ / running status / tempo map）
│   │   │   ├── MidiEventScheduler.cs    # Note On/Off 调度 + 区间查询
│   │   │   ├── MidiNoteEvent.cs         # 标准化事件（pitch/start/duration/velocity/track/channel）
│   │   │   ├── TempoChange.cs           # 秒制 tempo 变化点
│   │   │   └── PianoNoteFilter.cs       # 左右手过滤 / 黑键判定
│   │   ├── Piano/                   # 钢琴与琴键表现
│   │   │   ├── PianoKeyMap.cs           # 程序化 88 键 + 立式琴体
│   │   │   ├── PianoKeyAnimator.cs      # 下压 / 回弹动画
│   │   │   ├── PianoKeyHighlighter.cs   # 高亮提示（MaterialPropertyBlock，零材质实例）
│   │   │   ├── PianoAudioSynth.cs       # 波形合成发声
│   │   │   └── HighlightMode.cs
│   │   ├── Avatar/                  # 人物与手部
│   │   │   ├── AvatarManager.cs         # 三角色预设 + 切换（不重置课程状态）
│   │   │   ├── AvatarRigBuilder.cs      # 标准 Humanoid 骨架 + Avatar 生成
│   │   │   ├── HandIKController.cs      # 手腕目标 / 换位抬升 / 自适应速度 / 指标采样
│   │   │   ├── FingerPoseController.cs  # 手指按音高比例分配 + 和弦手型
│   │   │   ├── HandPoseValidator.cs     # 异常姿态校验 + 钳制（防穿模）
│   │   │   └── HandMotionMetrics.cs     # 动作质量指标（命中率 / 平滑度 / 协调率）
│   │   ├── Camera/
│   │   │   └── CameraModeController.cs  # 手部特写 / 全景 / 自由观察 + 平滑切换
│   │   └── UI/
│   │       ├── UITheme.cs               # 统一色板 + 程序化圆角控件（不依赖内置资源）
│   │       ├── LessonControlsUI.cs      # 控制栏（两行 12 按钮 + 进度 + 拍点 + 分析面板）
│   │       ├── AvatarSelectionUI.cs
│   │       └── MainMenuUI.cs
│   ├── StreamingAssets/Songs/
│   │   └── demo.mid                 # 演示曲目（120BPM，旋律+和弦+力度变化，7.5s）
│   ├── Tests/
│   │   ├── EditMode/                # 8 个文件 · 96 条：解析 / 过滤器 / 拍点 / 分析器 / 边界
│   │   └── PlayMode/                # 9 个文件 · 58 条：时间轴 / 视角角色切换 / 教学功能 /
│   │                                #   错误处理 / 动作质量 / Humanoid 骨架 / 性能 / 视觉
│   ├── Prefabs/                     # 预留（当前全部程序化生成，无需预制体）
│   ├── Animations/                  # 预留
│   ├── Materials/                   # 预留
├── Packages/manifest.json
├── ProjectSettings/                 # 含 ProjectVersion.txt（锁定 Unity 版本）
├── Docs/
│   ├── architecture.md              # 架构与数据流
│   ├── animation-pipeline.md        # 动画管线 + 阶段五改进 + 缺陷修复记录
│   ├── asset-licenses.md            # 资源许可证清单
│   ├── research-notes.md            # 开源资源调研（方案 10-2）
│   ├── how-to-run.md                # 运行与验证指南（详细版）
│   ├── completion-review.md         # 方案完成度核对报告
│   └── screenshots/                 # 视觉基线（6 张，可回归比对）
├── Tools/
│   └── verify.py                    # 一键验证脚本（编译 + EditMode + PlayMode）
├── Tests/                           # 方案约定的仓库级测试目录（见 Tests/README.md）
└── README.md                        # ← 本文档
```

---

## 5. 安装与使用说明

### 5.1 环境依赖

| 依赖 | 要求 | 说明 |
|---|---|---|
| Unity Editor | **2022.3.22f1c1** | 项目已锁定该版本；其他 2022.3 LTS 亦可尝试 |
| Unity Hub | 3.x | 用于打开项目 |
| Python | 3.8+ | 仅一键验证脚本需要，**无需 pip 安装任何包** |

**无需安装任何第三方 Unity 包、模型、音频素材** —— 项目内资产全部程序化生成。

### 5.2 方式 A · 一键命令行验证（推荐，最快）

```bash
cd "D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d"

python Tools/verify.py                # 编译 + EditMode + PlayMode（约 3.5 分钟）
python Tools/verify.py --edit-only    # 只跑纯逻辑测试（约 1 分钟）
python Tools/verify.py --visual       # 追加生成视觉基线截图（需 GPU）
python Tools/verify.py --timeout 600  # 自定义单阶段超时（秒）
```

脚本会自动定位 Unity（也可用环境变量 `UNITY_PATH` 指定），依次执行**编译检查 → EditMode → PlayMode →（可选）视觉截图**，并输出汇总与退出码（`0` 通过 / `1` 有失败 / `2` 环境问题）。中间产物在 `_verify_out/`。

### 5.3 方式 B · 在 Unity 编辑器里体验

1. Unity Hub → `Add project from disk` → 选择 `piano-teacher-3d` 目录；
2. 在 Project 窗口双击 `Assets/Scenes/MainMenu.unity`；
3. 点击顶部 **▶ Play**，在主菜单点曲目卡片进入教学场景。

首次打开后可在 `File > Build Settings` 确认 `MainMenu` 与 `PianoLesson` 均在构建列表（已写入 `EditorBuildSettings.asset`）。
操作说明见 [3.6 操作总表](#36-操作总表)。

### 5.4 方式 C · Unity Test Runner（GUI 跑测试）

`Window > General > Test Runner` → **EditMode** 标签 `Run All` → **PlayMode** 标签 `Run All`。
绿色为通过，点击失败项可查看断言消息。

### 5.5 预期结果

```text
====================================================================
结果汇总
====================================================================
[EditMode] 通过 — 总计 96，通过 96，失败 0，跳过 0
[PlayMode] 通过 — 总计 58，通过 57，失败 0，跳过 1
    SKIP: CaptureLessonViews          ← 无图形设备时预期跳过

✓ 全部通过
```

| 指标 | 预期值 |
|---|---|
| 编译 | 0 错误 0 警告 |
| 目标琴键命中率 | ≥ 80%（实测 82.9%） |
| 最大单帧位移 | < 150 mm（实测 35 mm） |
| 穿模告警 | 0 |
| Humanoid 骨架 | 3 个角色 `isValid && isHuman == true` |

---

## 6. 示例用法

### 6.1 加载并播放曲目

```csharp
using PianoTeacher3D.Core;
using UnityEngine;

public class LoadAndPlayDemo : MonoBehaviour
{
    [SerializeField] private LessonSessionController _session;

    private void Start()
    {
        // 曲目路径：Assets/StreamingAssets/Songs/ 下任意 .mid
        string path = System.IO.Path.Combine(
            Application.streamingAssetsPath, "Songs", "demo.mid");

        _session.LoadSong(path);   // 解析失败会走 OnError，不抛异常
        _session.Play();
    }
}
```

### 6.2 订阅进度与错误

```csharp
private void OnEnable()
{
    _session.OnSongLoaded        += () => Debug.Log($"曲目：{_session.CurrentSongName}");
    _session.OnProgressChanged   += (cur, total) => Debug.Log($"进度 {cur:F1}/{total:F1}s");
    _session.OnPlaybackStateChanged += state => Debug.Log($"状态：{state}");
    _session.OnError             += msg => Debug.LogError($"⚠ {msg}");
    _session.OnMetronomeBeat     += beatInBar => Debug.Log($"第 {beatInBar + 1} 拍");
    _session.OnAnalysisReady     += analysis => Debug.Log($"音符 {analysis.NoteCount} 个");
}

private void OnDisable()
{
    // 生产代码中应逐一退订，避免悬空引用
}
```

### 6.3 切换视角与角色（不影响演奏）

```csharp
_session.CycleCameraMode();      // 手部特写 → 全景 → 自由观察
_session.SetCameraMode(CameraMode.Closeup);
_session.ResetCamera();          // 等同按 R

_session.CycleAvatar();          // 写实女 → 写实男 → 卡通
_session.SwitchAvatar(2);

// 以下状态在切换前后保持不变：PlaybackState / 播放进度 / 当前曲目
```

### 6.4 教学功能调用

```csharp
// 慢速练习
_session.SetSpeed(0.5f);

// 分手练习（保持进度连续）
_session.SetPracticeHand(PracticeHand.LeftOnly);   // Both / LeftOnly / RightOnly

// A-B 循环（区间非法会自动关闭循环）
_session.SetLoop(4.0f, 8.0f);
_session.ToggleLoop();                              // 或整曲循环开关

// 节拍器
_session.SetBeatsPerBar(4);
_session.ToggleMetronome();

// 琴键高亮
_session.HighlightLookAhead = 0.8f;                 // 预提示提前量（clamp 到 0.05–3）
_session.SetHighlightMode(HighlightMode.Both);

// 读取曲目分析与动作质量
SongAnalysis analysis = _session.Analysis;
foreach (string tip in analysis.Suggestions) Debug.Log($"建议：{tip}");

HandMotionReport motion = _session.HandMotion;
Debug.Log(motion.ToLogString());   // 命中率 / 最大帧位移 / 平滑度 / 双手协调
Debug.Log($"穿模告警次数：{_session.PoseIssueCount}");
```

### 6.5 纯逻辑使用 MIDI 解析器（脱离 Unity 场景）

```csharp
using PianoTeacher3D.MIDI;

// 解析文件
MidiParseResult result = MidiParser.ParseFile(@"C:\songs\demo.mid");
if (!result.Success)
{
    Debug.LogError(result.Error);   // 失败原因统一走 Error 字段
    return;
}

Debug.Log($"{result.Notes.Count} 个音符 / {result.TotalDurationSeconds:F1}s / "
        + $"{result.TrackCount} 轨 / {result.TempoChanges.Count} 个 tempo 变化点");

foreach (MidiNoteEvent e in result.Notes)
{
    // 五字段：pitch / StartTime / Duration / Velocity / Track+Channel
    // 另有 result.DanglingNoteCount：缺 Note Off 而被补全的音符数（异常 MIDI 提示）
}

// 或直接解析字节流（可用于内存中的 MIDI 数据）
byte[] data = System.IO.File.ReadAllBytes(@"C:\songs\demo.mid");
MidiParseResult same = MidiParser.Parse(data);
```

### 6.6 单独做曲目分析

```csharp
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;

SongAnalysis a = SongAnalyzer.Analyze(result.Notes, result.TempoChanges, result.TotalDurationSeconds);

Debug.Log($"左手 {a.LeftHandCount}（{a.LeftHandRatio:P0}） / 右手 {a.RightHandCount}（{a.RightHandRatio:P0}）");
Debug.Log($"和弦 {a.ChordCount} 处，最大 {a.MaxChordSize} 音，跨度 {a.MaxSpanSemitones} 半音");
Debug.Log($"音域 {a.LowestPitch}–{a.HighestPitch}（{a.OctaveRange:F1} 个八度）");
// 任何输入（null / 空列表）都返回非 null 结果，Suggestions 永不为 null
```

---

## 7. 架构要点

```text
LessonTimeline（唯一时间源）
    ↓ 每帧 CurrentTime
LessonSessionController
    ├── MidiEventScheduler.Tick ──→ PianoKeyAnimator / PianoAudioSynth
    │                              / HandIKController / FingerPoseController / PianoKeyHighlighter
    ├── UpdateHighlightUpcoming ──→ CollectNotesInRange（二分）──→ PianoKeyHighlighter
    ├── Metronome.Tick ──→ 击拍音 + OnMetronomeBeat ──→ UI 拍点指示
    └── OnProgressChanged ──→ LessonControlsUI

摄像机切换 / 角色切换：只改表现层，不触碰时间轴
```

- **模块化**：摄像机、人物、音频、琴键互不直接依赖，全部经 `LessonSessionController` 门面连线。
- **纯逻辑可测**：`MidiParser` / `PianoNoteFilter` / `SongAnalyzer` / `Metronome.BuildBeatTimes` /
  `HandPoseValidator` / `HandMotionAccumulator` 均不依赖场景，可直接单测。
- 详见 [`Docs/architecture.md`](Docs/architecture.md) 与 [`Docs/animation-pipeline.md`](Docs/animation-pipeline.md)。

---

## 8. 测试与质量

| 层次 | 内容 | 条数 |
|---|---|---|
| **EditMode** | MIDI 解析（VLQ / running status / tempo / 和弦 / 异常）、左右手过滤、拍点生成、曲目分析、姿态校验、动作指标、文件 IO 边界 | 96 |
| **PlayMode** | 时间轴状态机、视角/角色切换不断流、错误处理、教学功能（高亮/节拍器/分析）、动作质量、Humanoid 骨架、性能采样、视觉快照 | 58 |

```bash
python Tools/verify.py          # 一键跑全部
```

> **项目经验**（详见 `Docs/animation-pipeline.md`）：
> ① 数据指标通过 ≠ 视觉正确 —— 需要视觉验收的功能要真的看截图；
> ② 先用数据验证假设再改代码（曾为一个被实测否证的假设迭代四轮）；
> ③ Unity 原生对象不可作 MonoBehaviour 字段初始化器；
> ④ 命名空间勿与 `UnityEngine` 类型同名（如 `Camera` / `Avatar`）。

---

## 9. 注意事项与已知限制

### 9.1 使用注意

1. **Unity 版本**：请使用 **2022.3.22f1c1**。若用其他版本打开，Unity 会提示升级项目，可能改写 `ProjectSettings`。
2. **首次导入耗时**：首次打开会导入资源并补齐 `Library/`，约 1–3 分钟；`Library/`、`Temp/`、`Logs/` 已在 `.gitignore` 中排除。
3. **修改场景后需保存**：教学场景的元素在**运行时**由代码装配，编辑器里看到的 `.unity` 文件是极简骨架（相机 + 灯光 + 装配入口），属预期设计。
4. **命令行走测试**：`-runTests` 后 Unity 偶发卡在退出阶段 —— `Tools/verify.py` 已按「结果文件生成即继续」处理，直接等脚本返回即可。
5. **不要在未读文档的情况下替换资源**：引入任何第三方模型/音频前，请先按 `Docs/asset-licenses.md` 逐项核对许可证并登记。

### 9.2 已知限制

| 限制 | 说明 |
|---|---|
| 音频为合成音 | 波形合成、固定衰减，不精确对应长音符时长；换真实音色需选用明确 CC0/公有领域采样 |
| 手指姿态为启发式 | 按音高比例分配手指，**不承诺专业指法**（方案 2.3 明确声明）|
| 人物为程序化低模 | 骨架已是标准 Humanoid，但网格仍是方块/圆柱组合；接入真实 GLB 网格即可升级 |
| 视觉回归靠人工 | 已有 6 张基线截图，但**自动像素差异判定**尚未实现 |
| 键盘低音端遮挡 | 侧视机位下琴体自身会遮挡键盘低端（物理必然，优先保证"可看清演奏姿态"）|
| AI 动作生成未做 | 属方案 2.3 声明"首版暂不承诺"的研究项 |
| 仅 Windows 平台 | 跨平台适配属方案 2.2 后续目标 |

### 9.3 许可证

首版**全部为程序化生成**（钢琴 / 人物 / 材质 / 音频 / 曲目），不含第三方代码、模型、贴图、动作数据或音频采样。
详见 [`Docs/asset-licenses.md`](Docs/asset-licenses.md)。引入任何外部资源前需逐项核对许可证并登记。

---

## 10. 常见问题（FAQ）

| 问题 | 处理 |
|---|---|
| 提示 Unity 版本不符 | 确认安装 **2022.3.22f1c1**（中国版）；`ProjectVersion.txt` 已锁定该版本 |
| `verify.py` 报「未找到 Unity Editor」 | 设置环境变量 `UNITY_PATH` 指向 `Unity.exe` |
| PlayMode 里 `CaptureLessonViews` 被跳过 | 无图形设备时自动跳过；用 `--visual` 或编辑器里跑即可生成截图 |
| 画面里看不到手 | 切到**手部特写**视角；全景下手在键盘上方 |
| 点了按钮没反应 | 检查顶部信息栏的错误提示（未加载曲目、某手无音符等都会有提示） |
| 想要看到进度/动作质量 | 按 `Tab` 打开「曲目分析」面板 |

---

## 11. 相关文档索引

| 文档 | 内容 |
|---|---|
| [`Docs/how-to-run.md`](Docs/how-to-run.md) | 运行与验证详细指南（环境 / 三种运行方式 / 操作 / FAQ） |
| [`Docs/architecture.md`](Docs/architecture.md) | 架构、模块职责、数据流、教学功能设计 |
| [`Docs/animation-pipeline.md`](Docs/animation-pipeline.md) | 动画管线、阶段五手部改进、缺陷修复记录与经验 |
| [`Docs/asset-licenses.md`](Docs/asset-licenses.md) | 资源许可证清单 |
| [`Docs/research-notes.md`](Docs/research-notes.md) | 开源资源调研（Unity-MIDI-Piano 等） |
| [`Docs/completion-review.md`](Docs/completion-review.md) | 方案完成度核对报告 |
| `../钢琴3D教学APP-项目设计方案.md` | 项目设计方案（v0.1，需求与技术方案的源头） |
