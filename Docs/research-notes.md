# 开源资源调研与提供器接口状态（research-notes.md）

> 对应方案第 10 节「首个迭代的执行顺序」与第 6 章「GitHub 开源资源调研」。
> 调研方式说明：本机环境网络受限（`git clone` 返回 `fetch-pack: invalid index-pack output`，直连与代理均失败），
> 故通过仓库公开页面 / README / 提交历史核对信息，**未实际 clone 运行**；结论已标注证据来源。

---

## 1. Unity-MIDI-Piano（方案 6.1 / 10-2 点名调研项）

| 项 | 内容 |
|---|---|
| 地址 | https://github.com/catdevpete/Unity-MIDI-Piano |
| 许可证 | **MIT**（仓库 README 明示 "Provided under MIT License"） |
| 最后更新 | 2021-08-20（升级到 Unity **2020.3.14f1**），此后未再更新 |
| 仓库结构 | `Assets/` `Packages/` `ProjectSettings/` `UserSettings/`（标准 Unity 工程） |
| 功能 | 3D 钢琴 + MIDI 文件播放（含琴键联动、彩色键、多音同时触发） |
| 演示曲目 | Chopin《幻想即兴曲》、《Gymnopédie No.1》、Joplin《The Entertainer》 |

### 1.1 关键实现（据 README 与提交历史）

1. **MIDI 解析：使用第三方库 NAudio**（`Markheath/NAudio`，.NET 库），非自研（提交记录 "NAudio library updated"）。
2. **音频：真实钢琴采样**（`Piano Key samples by Beskhu on Freesound.org`），即"每键一个采样文件"。
3. **同时刻多音处理**：2020-11-12 提交修复 "MIDI keys at the same timescale will now play simultaneously"——说明早期版本存在**和弦逐帧播发**问题。
4. **彩色键**："Coloured keys added"——用颜色标识琴键。

### 1.2 与本项目的对比

| 维度 | Unity-MIDI-Piano | 本项目（Piano Teacher 3D） | 判定 |
|---|---|---|---|
| MIDI 解析 | 依赖 NAudio（Windows .NET DLL） | **自研纯 C# `MidiParser`**（无第三方依赖、可单测） | 本项目更优，**不移植** |
| 音频 | Freesound 采样（许可需逐项核对） | **程序化波形合成**（零资产、零许可风险） | 符合方案「遵守资源许可证」，**不移植** |
| 和弦处理 | 修复后支持同帧同时触发 | `MidiEventScheduler` 按时间戳排序 + 同帧批量触发 | 设计一致 |
| Unity 版本 | 2020.3 LTS（已停更） | 2022.3.22f1c1 | 本项目更新 |
| 琴键高亮 | 有"彩色键"实现 | **已实现**（`PianoKeyHighlighter`，暖金 / 冷蓝 / 暖橙语义色） | 已完成 |

### 1.3 结论

**不集成其代码**，理由：① 依赖 NAudio（Windows 专有 DLL，跨平台与许可证均不友好）；
② 音频采样来源为 Freesound（各文件 CC 许可需逐项核对），违反本项目"零第三方资产"原则；
③ 其 Unity 版本（2020.3）低于本项目基准，迁移成本大于收益。
**仅借鉴其"彩色键 / 音符高亮"的产品思路**，该功能本轮已用程序化高亮实现。

---

## 2. 方案第 6 章其余资源的初步评估（未集成）

| 项目 | 用途 | 许可证 | 本项目判定 |
|---|---|---|---|
| PianoMotion10M | 钢琴手部动作生成数据集 / 基线 | 代码 Apache-2.0（**数据集与权重需单独核对**） | 后续研究方向；MVP 明确不依赖（方案 2.3） |
| piano-gl | 88 键渲染 / Blender 与 glTF 资源参考 | MIT（第三方依赖需另查） | C++/OpenGL 项目，仅作琴键建模参考；本项目已程序化建模 |
| Blender MIDI Motion | Blender 内按 MIDI 生成关键帧 | 需核对 | 离线动画辅助工具，非运行时依赖 |

---

## 3. 提供器接口实现状态（方案 10）

方案第 10 章建议把「照片生成 3D 角色、音频转谱、AI 动作生成」等**不确定性能力**封装为可选的提供器接口。
以下是四个接口在**本项目的实际落地状态**：

| 接口（方案 10） | 是否已定义 | 是否已实现 | 说明 |
|---|---|---|---|
| `IFingeringProvider`（启发式 / 人工编辑 / 模型推断的指法建议） | **已定义**（`Avatar/IFingeringProvider.cs`） | **已实现**：`Avatar/HeuristicFingeringProvider.cs`（规则化序列分配，输出置信度） | **可替换**：`FingerPoseController.Provider` 支持注入其它实现；置 `null` 回退默认启发式 |
| `IAvatarProvider`（预设角色 / 外观配置 / 导入模型） | 计划中 | **计划中** | 当前由 `AvatarManager` + `AvatarDefinition`（纯数据类）承担，**尚未抽象为独立接口** |
| `IHandMotionProvider`（基础 IK / 缓存动画 / AI 生成动作） | 计划中 | **计划中** | 手部动作当前由 `HandIKController` 直接实现，**尚未抽出提供器** |
| `IAudioToScoreProvider`（音频转音符候选 + 置信度） | 计划中 | **计划中** | 对应方案 2.2 P2「MP3/WAV 转谱」，需独立「音频→谱」管线与校对流程 |

### 3.1 当前已生效的降级 / 回退路径（不影响课程播放）

方案 10 明确要求：**AI 模块不可用、超时或生成异常时，系统必须回退到基础 IK / 预设人物 / 已导入 MIDI，而不是让课程无法播放。**
当前已实现的回退（与未来接入提供器时保持一致）：

| 能力 | 回退路径 |
|---|---|
| 指法（`IFingeringProvider`） | `FingerPoseController.Provider` 为 `null` 时自动回退 `HeuristicFingeringProvider`；低置信度时 `LastEstimated = true`，UI 标注「估算指法」 |
| 人物（`AvatarManager` + `AvatarDefinition.Validate`） | 骨架校验失败回退 `AvatarDefinition.CreateDefault()`（默认角色），**不影响曲目播放** |
| 手部动作（`HandIKController`） | 缺手部骨骼 / 目标不可达时使用备用构图与默认手位，不抛异常 |
| 音频（`PianoAudioSynth`） | 初始化失败置 `Initialized = false`，`OnNoteOn` 静默返回，上层提示「音频不可用」并允许**静音继续** |
| MIDI（`MidiParser` / `MidiEventScheduler`） | 解析失败保留当前已加载曲目（有旧曲）或进入明确 `Error` 态，绝不进入半加载状态 |
| 相机（`CameraModeController`） | 场景资源缺失时使用备用构图；角色 / 切曲后 `InvalidateFraming` 重算，不追旧目标 |

> **接入约定（未来实现其它提供器时务必遵守）**：AI / 外部提供器只能作为**可选增强**，不得作为播放主时钟或唯一动作来源；
> 任何提供器调用都必须有超时与异常兜底，失败时**回退到上表的默认实现**，且不得阻塞统一时间轴。

---

## 4. 后续建议

1. **若后续接入真实音色**：优先选择**明确公有领域或 CC0** 的音源，并按 [`Docs/asset-licenses.md`](asset-licenses.md) 第 3.3 节登记，
   同时评估**延迟 / 长音衰减 / 力度响应 / 同时发声数 / 许可条件**五项；**不得直接沿用 Freesound 上许可不明的样本**。
2. **若后续接入真实人物模型**：评估带 Humanoid 骨骼的 GLB 资源，接入点已在 `AvatarManager` / `AvatarDefinition` 预留
   （把外部网格挂到同一套 Humanoid 骨架上并填好 `AvatarDefinition`）。
3. **若后续接入 AI 手部动作**：先抽象 `IHandMotionProvider`，保留「当前按下键」与 `IHandRig` 接口；
   提供器失败时回退基础 IK（见第 3.1 节约定）。
4. **若后续接入音频转谱**：先抽象 `IAudioToScoreProvider`，明确识别置信度与人工校对流程，并作为独立能力设计，
   **不要与 MIDI 文件导入混为一谈**（方案 1.3 场景 D 的说明）。
