# Piano Teacher 3D — 钢琴 3D 教学 APP

以 3D 钢琴演奏可视化为核心的教学应用：加载 MIDI 曲目，观察虚拟人物演奏钢琴，随时切换观察视角与人物形象。

> 首版平台：**Windows 桌面端（Unity + C#）**
> 项目代号：Piano Teacher 3D
> 对应设计方案：`../钢琴3D教学APP-项目设计方案.md`（v0.1）

---

## 技术栈

| 模块 | 方案 |
|---|---|
| 引擎 | Unity 2022.3 LTS（`ProjectVersion.txt` 锁定 2022.3.22f1） |
| 语言 | C#（命名空间 `PianoTeacher3D.*`） |
| 3D | 程序化 88 键钢琴 + 程序化人形（3 预设角色） |
| MIDI | 自研纯 C# `MidiParser`（SMF 解析） |
| 音频 | `PianoAudioSynth` 波形合成（零外部采样） |
| 测试 | Unity Test Framework（EditMode + PlayMode） |
| 版本管理 | Git + GitHub |

---

## 目录结构

```
piano-teacher-3d/
├── Assets/
│   ├── Scenes/                 # MainMenu.unity / PianoLesson.unity
│   ├── Scripts/
│   │   ├── Core/               # LessonSessionController / LessonTimeline / PlaybackState
│   │   ├── MIDI/               # MidiNoteEvent / MidiParser / MidiEventScheduler
│   │   ├── Piano/              # PianoKeyMap / PianoKeyAnimator / PianoAudioSynth
│   │   ├── Avatar/             # AvatarManager / HandIKController / FingerPoseController
│   │   ├── Camera/             # CameraModeController
│   │   └── UI/                 # LessonControlsUI / AvatarSelectionUI / MainMenuUI
│   ├── Prefabs/                # Piano/ Avatars/（预留）
│   ├── Animations/             #（预留）
│   ├── Materials/              #（预留）
│   ├── StreamingAssets/Songs/  # demo.mid 演示曲目
│   └── Tests/                  # EditMode / PlayMode（Unity Test Framework）
├── Packages/manifest.json
├── ProjectSettings/
├── Docs/                       # architecture / animation-pipeline / asset-licenses
├── Tests/                      # 方案约定的仓库级测试目录（见 Tests/README.md）
└── README.md
```

---

## 快速开始

1. 用 **Unity Hub 安装 Unity 2022.3.22f1**（或任意 2022.3 LTS）。
2. Unity Hub → `Add project from disk` → 选择本目录。
3. 等待首次导入（会自动补齐 `Library/`、其余 `ProjectSettings`、`*.meta`）。
4. 打开 `Assets/Scenes/MainMenu.unity`，点击 **Play**。

> 首次打开后，请在 `File > Build Settings` 确认 `MainMenu` 与 `PianoLesson` 两个场景都在构建列表（已写入 `EditorBuildSettings.asset`）。

### 操作说明

| 操作 | 说明 |
|---|---|
| 播放 / 暂停 | 底部控制栏「播放/暂停」按钮 |
| 停止 / 重新播放 | 底部控制栏对应按钮 |
| 进度 | 底部进度条（点击/拖动跳转） |
| 速度 | 「速度」按钮循环 0.5x → 1x → 1.5x |
| 视角 | 「视角」按钮循环 手部特写 → 全景 → 自由观察 |
| 自由观察 | 右键拖动旋转、滚轮缩放 |
| 重置视角 | 「重置视角」按钮或键盘 `R` |
| 切换角色 | 「切换角色」按钮或右上角角色按钮 |

---

## 运行测试

1. 打开工程后，`Window > General > Test Runner`。
2. 切到 **EditMode** 标签，点击 `Run All`（运行 `MidiParser` 纯逻辑测试）。
3. 切到 **PlayMode** 标签，点击 `Run All`（运行 `LessonTimeline` 状态机测试）。

---

## 架构要点

- **统一时间轴**：`LessonTimeline` 是唯一权威时间源，音乐/琴键/人物/UI 全部由它驱动。
- **三轴解耦**：视角切换只改相机、角色切换只换外观，都不触碰时间轴与演奏状态。
- 详见 `Docs/architecture.md`、`Docs/animation-pipeline.md`。

---

## 许可证

首版全部为程序化生成（钢琴/人物/材质/音频/曲目），**不含第三方代码、模型、贴图、动作数据或音频采样**。
详见 `Docs/asset-licenses.md`。后续引入任何外部资源前需逐项核对许可证并登记。

---

## 已知限制（首版 MVP）

- 音频为固定衰减的波形合成，不精确对应长音符时长（后续可换合法音色采样）。
- 手指姿态为五指跨度启发式，**不承诺专业指法**（对应方案 2.3）。
- 手臂为单段简化 IK，更自然的手腕/手指协同属于后续增强。
