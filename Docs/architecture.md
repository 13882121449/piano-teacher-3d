# 钢琴 3D 教学 APP — 系统架构设计

> 项目代号：Piano Teacher 3D
> 载体：Windows 桌面端（Unity + C#）
> 对应方案文档：《钢琴3D教学APP-项目设计方案.md》v0.1

本文档记录首版（MVP）的架构落地，严格遵循方案第 4 章「推荐技术方案」的模块划分与第 3 章数据流。

---

## 1. 技术栈（锁定）

| 模块 | 方案 | 说明 |
|---|---|---|
| 引擎 | Unity 2022.3 LTS（ProjectVersion.txt 锁定 2022.3.22f1） | 首版 Windows 桌面端 |
| 语言 | C#（Unity 2022.3 对应 C# 9.0） | 脚本统一命名空间 `PianoTeacher3D.*` |
| 3D 人物 | 程序化人形（Humanoid 思路的简化占位） | 零外部模型，后续可替换为带手指骨骼的 Humanoid 模型 |
| 手部控制 | 自定义 IK（手腕目标 + 手臂拉伸 + 手指弯曲） | 首版基础 IK，方案 4.1 建议 Animation Rigging，可后续替换 |
| 钢琴模型 | 程序化 88 键（白键/黑键，比例准确） | 零外部模型 |
| MIDI | 自研纯 C# `MidiParser`（SMF 解析） | 不依赖 UnityEngine，可单元测试 |
| 音频 | `PianoAudioSynth` 波形合成 | 零音频资产，后续可替换合法音色采样 |
| 测试 | Unity Test Framework（1.1.33） | EditMode + PlayMode |
| 版本管理 | Git + GitHub | `.gitignore` 已排除 Library/Temp/构建输出 |

> Unity 版本锁定说明：本机尚无 Unity Editor，故选取最稳定的 2022.3 LTS 作为基准并写入 `ProjectVersion.txt`。正式开发前请用 Unity Hub 安装 2022.3.22f1 并核对各 Package 版本（见 `Packages/manifest.json`）。

---

## 2. 架构原则（对应方案 4.2）

模块化结构，摄像机、人物、音频、琴键模块互不直接依赖。核心组件与方案 4.2 一一对应：

| 方案组件 | 实现文件 | 职责 |
|---|---|---|
| `LessonSessionController` | `Scripts/Core/LessonSessionController.cs` | 课程状态（曲目/播放状态/速度）+ 场景装配门面 + 模块连线 |
| `LessonTimeline` | `Scripts/Core/LessonTimeline.cs` | **唯一权威时间源**，维护 Stopped/Playing/Paused 状态机 |
| `MidiParser` | `Scripts/MIDI/MidiParser.cs` | 解析 MIDI → 标准化音符事件（纯 C#，可单测） |
| `MidiEventScheduler` | `Scripts/MIDI/MidiEventScheduler.cs` | 按统一时间调度 Note On/Off |
| `PianoKeyMap` | `Scripts/Piano/PianoKeyMap.cs` | MIDI 音高 ↔ 琴键对象映射 + 程序化生成 88 键 |
| `PianoKeyAnimator` | `Scripts/Piano/PianoKeyAnimator.cs` | 琴键按下/回弹动画 |
| `AvatarManager` | `Scripts/Avatar/AvatarManager.cs` | 人物加载/选择/切换（3 预设角色） |
| `HandIKController` | `Scripts/Avatar/HandIKController.cs` | 音符 → 手腕/手部目标（左右手 C4 分界） |
| `FingerPoseController` | `Scripts/Avatar/FingerPoseController.cs` | 基础手指姿态（按下/抬起） |
| `CameraModeController` | `Scripts/Camera/CameraModeController.cs` | 手部特写/全景/自由观察 + 平滑切换 |
| `LessonControlsUI` | `Scripts/UI/LessonControlsUI.cs` | 播放/暂停/进度/速度/视角按钮 |
| `AvatarSelectionUI` | `Scripts/UI/AvatarSelectionUI.cs` | 角色选择按钮（高亮当前项） |

**方案目录树之外的合理补充**（方案未明确说明、为实现所需而新增，均已标注）：
- `PianoAudioSynth.cs`：方案 3.3 数据流第 5 步「音频模块」的具体实现（方案未单列类名）。
- `MainMenuUI.cs`：方案 5 节 `MainMenu.unity` 场景的入口逻辑（方案未列出菜单控制器）。

---

## 3. 统一时间轴（对应方案 4.3）

`LessonTimeline` 是唯一权威播放时间来源：

```text
用户选择曲目（MainMenuUI）
    ↓
MidiParser 读取音符事件 → MidiNoteEvent[]（秒制）
    ↓
LessonSessionController 加载 → LessonTimeline.SetDuration + MidiEventScheduler.Load
    ↓
LessonTimeline 播放并输出 CurrentTime（唯一时间源）
    ↓
LessonSessionController.Update 每帧调用 MidiEventScheduler.Tick(CurrentTime)
    ├── PianoKeyAnimator 控制琴键按下/回弹
    ├── PianoAudioSynth 合成发声
    ├── HandIKController 更新手腕目标
    ├── FingerPoseController 更新手指姿态
    └── LessonControlsUI 更新进度

视角切换 / 角色切换：只修改表现层（相机/可见性），不重置时间轴
```

播放状态：`Stopped` / `Playing` / `Paused`（`Core/PlaybackState.cs`）。切换角色或视角不改变该状态。

---

## 4. 三轴解耦（对应方案 1 设计原则）

- **视角与演奏解耦**：`CameraModeController.SetMode` 只改相机，不触碰 `LessonTimeline`。
- **人物与演奏解耦**：`AvatarManager.SwitchAvatar` 只改外观与手部引用（`IHandRig`），不重置曲目/进度/场景状态。
- **数据源可替换**：`HandIKController` 消费统一的「当前按下键集合」，后续可替换为动捕/模型生成数据而不改接口。

---

## 5. 场景装配策略

`.unity` 场景仅包含：场景设置 + `Main Camera` + `Directional Light` + 一个装配入口对象
（`PianoLesson` 场景挂 `LessonSessionController`，`MainMenu` 场景挂 `MainMenuUI`）。

钢琴、人物、UI 等元素均由 `LessonSessionController.BuildScene()` 在运行时程序化生成。理由：
1. 符合方案「首版可先使用简化但比例准确的琴键模型」；
2. 避免手摆大量 GameObject 的 `.unity` 序列化脆弱性；
3. 装配逻辑集中在 C#，便于测试与版本管理。

---

## 6. 关键数据结构（对应方案 3.3）

`MidiNoteEvent`（`Scripts/MIDI/MidiNoteEvent.cs`）含方案要求的最小字段集：
`pitch` / `startTime`(秒) / `duration`(秒) / `velocity` / `track` / `channel`。

---

## 7. 测试策略（对应方案 8）

- **EditMode**：`MidiParser` 纯逻辑（正常/空/非法/和弦/配对/tempo）——不依赖场景。
- **PlayMode**：`LessonTimeline` 状态机（Play/Pause/Stop/Seek/播完停止）。

测试程序集位于 `Assets/Tests/EditMode` 与 `Assets/Tests/PlayMode`（Unity Test Framework 要求测试程序集在 `Assets` 下）。
根目录 `Tests/` 为方案第 5 节「推荐 Git 仓库结构」约定的镜像目录（保留以忠实方案），
实际可被 Test Runner 识别的是 `Assets/Tests/` 下的程序集。
