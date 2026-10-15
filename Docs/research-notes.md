# 开源资源调研记录（research-notes.md）

> 对应方案第 10 节「首个迭代的执行顺序」第 2 步与第 6 章「GitHub 开源资源调研」。
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

1. **MIDI 解析：使用第三方库 NAudio**（`Markheath/NAudio`，.NET 库），非自研。
   - 提交记录明确记载 "NAudio library updated"。
2. **音频：使用真实钢琴采样**（`Piano Key samples by Beskhu on Freesound.org`）。
   - 即"每键一个采样文件"的播放方案。
3. **同时刻多音处理**：2020-11-12 的提交专门修复了
   "MIDI keys at the same timescale will now play simultaneously (rather than 1 frame at a time)"
   —— 说明其早期版本存在**和弦逐帧播发**的问题，后改为同帧同时触发。
4. **彩色键**：提交 "Coloured keys added" —— 用颜色标识琴键。

### 1.2 与本项目的对比

| 维度 | Unity-MIDI-Piano | 本项目（Piano Teacher 3D） | 判定 |
|---|---|---|---|
| MIDI 解析 | 依赖 NAudio（Windows .NET DLL） | **自研纯 C# `MidiParser`**（无第三方依赖、可单测） | 本项目更优，**不移植** |
| 音频 | Freesound 采样（许可需逐项核对） | **程序化波形合成**（零资产、零许可风险） | 符合方案「遵守资源许可证」，**不移植** |
| 和弦处理 | 修复后支持同帧同时触发 | `MidiEventScheduler` 按时间戳排序 + 同帧批量触发 | 设计一致 ✅ 无需改动 |
| Unity 版本 | 2020.3 LTS（已停更） | 2022.3.22f1c1 | 本项目更新 |
| 琴键高亮 | 有"彩色键"实现 | 仅下压动画，**无高亮** | ⚠️ **可借鉴**（见第 3 节） |

### 1.3 结论

**不集成其代码**，理由：① 依赖 NAudio（Windows 专有 DLL，跨平台与许可证均不友好）；
② 音频采样来源为 Freesound（各文件 CC 许可需逐项核对），违反本项目"零第三方资产"原则；
③ 其 Unity 版本（2020.3）低于本项目基准，迁移成本大于收益。
**唯一值得借鉴的是"彩色键/音符高亮"的产品思路**（对应本项目阶段五待办项）。

---

## 2. 方案第 6 章其余资源的初步评估（未集成）

| 项目 | 用途 | 许可证 | 本项目判定 |
|---|---|---|---|
| PianoMotion10M | 钢琴手部动作生成数据集/基线 | 代码 Apache-2.0（**数据集与权重需单独核对**） | 阶段五研究方向；MVP 明确不依赖（方案 2.3） |
| piano-gl | 88 键渲染 / Blender 与 glTF 资源参考 | MIT（第三方依赖需另查） | C++/OpenGL 项目，仅作琴键建模参考；本项目已程序化建模 |
| Blender MIDI Motion | Blender 内按 MIDI 生成关键帧 | 需核对 | 离线动画辅助工具，非运行时依赖 |

---

## 3. 后续建议（供阶段五决策）

1. **音符/琴键高亮提示**（方案 2.2 待办）：可参考 Unity-MIDI-Piano 的"彩色键"思路，
   在本项目 `PianoKeyAnimator` 上增加"待按键高亮/已按键高亮"通道（改动局部，不影响时间轴）。
2. **若后续接入真实音色**：优先选择**明确公有领域或 CC0** 的音源（如 Salamander Grand Piano 的 CC 授权版本），
   并按下 `asset-licenses.md` 的登记模板逐项记录，**不得直接沿用 Freesound 上许可不明的样本**。
3. **若后续接入真实人物模型**：评估 RPM（Ready Player Me）等带 Humanoid 骨骼的 GLB 资源，
   接入点已在 `AvatarManager` 预留（`IHandRig` 接口）。
