# 质量、性能与路线图（quality-and-roadmap.md）

> 对应方案 **7 性能与跨设备体验**、**11 测试、验收与质量门禁**、**12 分阶段实施路线图**、**13 关键设计取舍**。
> 实现位置：`Core/QualityProfile.cs`（画质档位）、`Piano/PianoSceneEnvironment.cs`（档位落地到灯光 / 阴影）、
> `Assets/Tests/PlayMode/PerformanceSamplingTests.cs`、`Assets/Tests/PlayMode/StressAndStabilityTests.cs`、
> `Docs/perf/sampling.md`、`Docs/perf/environments.md`。
>
> **数据口径声明**：`-batchmode` / `-nographics` 下采集的帧率 / 帧时间**不含渲染负载，不能代表真实设备体验**；
> 任何未在目标电脑上采集的数据**不得**写成「已达标」（方案 11.3 / 11.4）。

---

## 1. 画质档位（方案 7.1）

`Core/QualityTier` 定义两档：`Standard`（Desktop Standard，集显 / 旧款笔记本）、`High`（Desktop High，中档以上 PC）。
`Core/QualityProfile` 是**设备 / 画质判断的唯一判断点**（方案 7.2：「不把设备判断散落到各个脚本中」），
`For(tier)` 返回**静态缓存实例**（每帧调用零分配）。

| 档位字段 | Standard | High | 说明 |
|---|---|---|---|
| 阴影贴图分辨率 (`ShadowResolution`) | 1024 | 2048 | 落到 `QualitySettings.shadowResolution`（`Medium` / `High`） |
| 阴影距离 (`ShadowDistance`) | 8 m | 18 m | 实时阴影有效范围 |
| 阴影强度 (`ShadowStrength`) | 0.55 | 0.72 | 接触处暗度 |
| 软阴影 (`SoftShadows`) | 否（硬阴影） | 是 | 落到 `Light.shadows`（仅主光投影） |
| 级联数 | 2 | 4 | `QualitySettings.shadowCascades` |
| 抗锯齿 (`antiAliasing`) | 2× | 4× | `QualitySettings.antiAliasing` |
| 目标帧率 (`TargetFrameRate`) | 60 | 60 | 先关 VSync 再设 `Application.targetFrameRate` |
| 环境遮蔽观感 (`EnableAmbientOcclusion`) | 否 | **是（仅观感近似，非真实 AO）** | 见第 3 节诚实性说明 |
| 接触阴影 (`EnableContactShadow`) | 是 | 是 | 主光软阴影 + 贴地极淡渐变薄片（几何近似） |

### 1.1 可降级内容边界（关键）

`QualityProfile.Apply()` **只触碰 `QualitySettings` 与 `Application.targetFrameRate`**，并遵守方案 7.1 / 5.4 的边界：

- **只降低阴影分辨率 / 阴影距离 / 软硬阴影 / 级联 / 抗锯齿**；阴影永不置为 `ShadowQuality.Disable`。
- **不得关闭琴键高亮**（`PianoKeyHighlighter` 的暖金 / 冷蓝 / 暖橙教学语义色）。
- **不得移除补光或「键盘上方点光」**——手部与键面可读性属于教学下限（`PianoSceneEnvironment.ApplyQuality` 恒定启用补光 / 轮廓光 / 键盘点光）。
- **不得改变任何 MIDI / 时间轴 / 教学状态**。

> 覆盖测试：`ApplyQuality_TiersControlShadows_ButKeyLightStaysEnabled`、`High_ShadowBudgetIsAtLeastStandard`、`TierFields_AreWithinSaneTeachingSafeRanges`、`For_InvalidEnumValue_FallsBackToStandardAndNeverThrows`。

### 1.2 设备推荐（保守）

`QualityProfile.DetectRecommended()` 纯读取、不阻塞、不抛异常；信息不足时**宁可保守**：

- 无图形设备（`GraphicsDeviceType.Null`）→ Standard；
- 着色器能力 < SM4.5 → Standard；
- 显存 < 2 GB（含读取失败返回 0）→ Standard；
- 否则 High。

---

## 2. 性能预算与排查顺序（方案 7.2 逐条 → 实现位置）

| 方案 7.2 条目 | 实现位置 / 落地 |
|---|---|
| 建立可复现性能测试场景（88 键、高速跳音、复杂和弦、分析面板打开、连续相机切换） | `PerformanceSamplingTests.SampleFrameTiming`（纯播放）、`SampleFrameTiming_UnderSwitchingLoad`（每 20 帧切换视角 / 角色 / 速度） |
| 记录 CPU / GPU / GC Alloc / 音频回调 / 内存 / 帧时间百分位与帧率（不只平均 FPS） | `PerformanceSamplingTests` 写 `Docs/perf/sampling.md`（平均 / p50 / p95 / p99 / 最大帧时间、平均 FPS、1% low FPS、GC 增量） |
| 每帧避免无必要的 LINQ / 临时 List / 字符串拼接 / 反复查找组件 / 重复材质实例化 | 代码审阅：`LessonSessionController.Update`、`CameraModeController.Update`、`HandIKController.Update` 均为零分配；`PianoKeyMap` 复用 4 份网格与数份材质 |
| 高频路径使用预分配缓存 / 对象池；琴键高亮用 `MaterialPropertyBlock` | `PianoAudioSynth`（88 份 clip 预生成 + 24 声部池）、`PianoKeyHighlighter`（`MaterialPropertyBlock`，零材质实例） |
| MIDI 事件区间查询用排序 + 二分 / 游标；拖动进度才重建区间 | `MidiEventScheduler.CollectNotesInRange`（二分，O(log n + k)）；`LessonSessionController.Update` 只在时间回退时 `SeekTo` 重建 |
| 手部 IK 与姿态验证分开采样：核心高频计算持续、昂贵诊断按固定频率 | `HandIKController.Tick`（高频核心）vs `EnableMetrics` 采样的分组 / 抽查统计；`LessonControlsUI` 按 4 Hz 刷新动作质量 |
| 视觉质量使用质量档位配置，不把设备判断散落到各脚本 | `QualityProfile`（唯一判断点）；`PianoSceneEnvironment.ApplyQuality` |
| 使用 Unity Profiler 与目标设备实测后再优化；不要仅凭代码直觉过早重写算法 | **计划中**（真机测量未做）；已有 `--perf` 采样工具与 `Docs/perf/` 记录位 |

### 2.1 稳定性与资源加载采样

| 项目 | 实现 | 命令 |
|---|---|---|
| 快速切换压力（≥ 200 次） | `StressAndStabilityTests.Stress_RapidSwitching`（`[Category("Stress")]`） | `--stress` |
| 资源加载耗时与成功率 | `StressAndStabilityTests.ResourceLoad_Sampling`（`[Category("Stress")]`） | `--stress` |
| 环境矩阵自检 | `StressAndStabilityTests.Environment_Matrix_Checklist`（`[Category("Stress")]` + `[Test]`） | `--stress` |
| 10 分钟以上长跑稳定性 | `StressAndStabilityTests.Soak_PlaysContinuously`（`[Explicit]` + `[Category("Soak")]`，时长由 `PT3D_SOAK_SECONDS` 控制） | `--soak` / `--soak --soak-seconds N` |

---

## 3. 验收门槛（方案 11.3）→ 当前状态

> 方案 11.3 明确：这些是**后续建议目标**，应在目标电脑上测量后正式定稿。下表如实标注本机实测值 / 待真机项。

| 指标 | 方案建议门槛 | 当前状态 | 证据 / 备注 |
|---|---|---|---|
| 核心 MIDI 功能 | 支持范围内文件加载成功；失败给出可操作提示，不崩溃 | **已支持** | `MidiParserTests` / `MidiCompatibilityTests` / `SongLibraryTests` |
| 播放时序 | 同一曲目、相同操作序列结果可复现；音画偏差需建立实测目标并记录 | **部分实现**（逻辑可复现；**音频 / 视觉偏差的实测目标待建立**） | `LessonTimelineTests`；偏差测量计划中 |
| 场景切换 | 视角 / 角色切换不暂停、不重置时间；无明显跳姿和持续相机抖动 | **已支持** | `CameraVisibilityTests` 9 场景；`PlayModeIntegrationTests` |
| 手部动作 | 持续跟踪指尖命中率、位移、平滑度与穿模告警；不只看平均值 | **已达成（本机）**：手腕目标命中率 93.6%（左 94.7% / 右 92.7%）、最大帧位移 35 mm、穿模 0；指尖误差均值约 99 mm 仍偏大 | `HandMotionMetricsTests`、`HandMotionTests`；口径见 [`animation-pipeline.md`](animation-pipeline.md) |
| 帧率 | Desktop High 1080p 目标 60 FPS；标准档至少稳定 30 FPS | **部分实现**：锁 60 FPS、批处理 p99 17 ms / 1% low 58.4 FPS；**渲染负载下的真机数据待定稿** | `--perf` → `Docs/perf/sampling.md` |
| 内存 / GC | 长曲播放与反复切换无持续增长；高频更新无显著 GC 峰值 | **已达成（90s 长跑）**：内存 +0.4%、定时漂移 0.8%、错误日志 0、纯播放 GC 0.01 MB；**10 分钟长跑与真机待定稿** | `Soak_PlaysContinuously` + `Docs/perf/sampling.md` |
| 错误恢复 | 每个 P0 错误场景都有自动化或手动用例与明确恢复路径 | **已支持** | [`error-codes.md`](error-codes.md) 表二（10/10） |
| 可用性 | 初次用户不看开发文档即可完成选曲 / 播放 / 慢速 / 分手 / 循环 / 切换视角 | **部分实现**（交互已就绪；**可用性研究未做**） | 人工 |

### 3.1 画质与环境的诚实性说明

- **「环境遮蔽 AO」不是真实 AO**：内置渲染管线没有后处理栈，`EnableAmbientOcclusion` 只表达「希望物体接触处变暗」的观感，
  由 `PianoSceneEnvironment` 的**贴地极淡径向渐变薄片**近似；它**不是** SSAO，也不是后处理（由源码 `AmbientOcclusionApprox` 与文档共同标注）。
- **接触阴影是近似**：主光软阴影 + 贴地渐变薄片；找不到透明 shader 时**宁可不加薄片，也不制造不透明硬边矩形**（诚实降级）。
- **批处理帧率不代表真实体验**：`Docs/perf/sampling.md` 顶部已写入诚实性声明；真机数据需人工回填 `Docs/perf/environments.md` 的 E07 / E08。

---

## 4. 分阶段路线图（方案 12：Sprint 0–5）

```text
Sprint 0 ██████████ 完成   基线 / 状态机 / 错误码 / 交互表
Sprint 1 ██████████ 完成   相机与手部可见性（P0）
Sprint 2 ██████████ 完成   教学主 UI 与状态反馈（P0）
Sprint 3 ███████░░░ 部分   手部视觉与动作质量（P1；真机质量定稿待做）
Sprint 4 ██████████ 完成   曲库与音频体验（P1）
Sprint 5 ░░░░░░░░░░ 未开始 跨设备可行性验证（P2）
```

### Sprint 0：建立基线与定义状态（最高优先级）——**完成**

- 固定版本（Unity 2022.3.22f1c1）、测试曲目、目标窗口尺寸（1280×720）与视觉截图（12 + 6 张）。
- 为会话（`LessonState`）、相机（`CameraMode`）、曲目加载（`Loading` + 切曲事务）定义显式状态机。
- 汇总现有测试结果、错误码表（[`error-codes.md`](error-codes.md)）与功能交互表（[`ui-design-system.md`](ui-design-system.md)）。
- **完成标准达成**：功能与视觉有可复现基线；新增优化有验收方法。

### Sprint 1：相机与手部可见性（P0）——**完成**

- 修复全景键盘低音端遮挡 → 前侧 3/4 角度。
- 重写 `CameraModeController` 的状态切换与中断逻辑（单过渡实例、以当前姿态为新起点）。
- 手部特写用「活动手范围 ∪ 琴键范围」稳定取景，并设距离 / FOV 限幅。
- 明确自由观察返回、重置、单手练习时的行为。
- **证据**：`CameraFramingTests`、`CameraVisibilityTests`（9 场景）、V02 / V03 / V08 / V09。

### Sprint 2：教学主 UI 与状态反馈（P0）——**完成**

- 播放与进度放入主播放栏；次级工具收纳到可折叠练习工具栏 / 弹出面板。
- 添加当前状态可见性、加载反馈、空状态与统一错误展示。
- 统一主题、按钮七态、间距、焦点与窗口缩放行为。
- **证据**：`UIThemeTokenTests`、`UIStatusBar`、`LessonControlsUI`。

### Sprint 3：手部视觉与动作质量（P1）——**部分完成**

- 已做：手掌 / 拇指 / 三段指节几何精修；启发式指法封装为可替换提供器并标注「估算指法」；指尖目标 / 手腕抬升弧线 / 和弦先成形；黑键独立高度；动作质量分组指标。
- **已做**：固定曲目下的命中率校准（93.6%，已过 ≥ 90% 目标）与 18 张截图的人工看图（发现并修复「手部特写被头部遮挡」回归）。
- **未做**：**指尖贴键精度**（指尖误差均值约 99 mm）、**真机性能与视觉评审**、人物躯干轮廓的进一步建模。
- **完成标准（未完全达成）**：质量指标可对比（已具备口径），手部视觉评审通过（**待人工 / 真机**），不靠放宽阈值掩盖问题。

### Sprint 4：曲库与音频体验（P1）——**完成**

- 用户主动导入 MIDI（应用内文件选择器）、最近曲目与曲目摘要。
- 导入事务、文件失效与错误恢复机制。
- 音频降级方案（波形合成 + 受控声部池 + 静音降级）已就绪；**合法采样音色的评估与替换仍计划中**。
- 用户数据存储与删除逻辑（`persistentDataPath` + `ClearCache` / `Forget`）。

### Sprint 5：跨设备可行性验证（P2）——**未开始**

- 将输入操作抽象为命令（播放、暂停、旋转、缩放等），分离鼠标和触控实现。
- 用目标移动设备验证渲染、音频延迟、模型与内存成本。
- 决定移动端是否同一工程适配或单独 UI / 产品形态。
- 明确不支持的功能与降级策略，再公开承诺设备范围。
- **完成标准（未达成）**：有真实设备数据和支持矩阵。

### 4.1 下一步（优先级建议）

1. **手部动作质量专项校准**：在固定测试曲目上建立命中率 / 指尖误差基线，按音域、和弦、跨度、循环边界分组核对（Sprint 3 收尾）。
2. **真机性能与环境矩阵定稿**：在集显笔记本 + 中档独显上跑 `--perf`，回填 `Docs/perf/environments.md`。
3. **真实 SSAO / 后处理的可行性评估**：受内置管线限制，需先评估是否切换到 URP（会牵涉大量改动，慎重）。
4. **跨平台输入抽象**（Sprint 5 前置）。
5. **可选能力提供器接入**（`IAvatarProvider` / `IHandMotionProvider` / `IAudioToScoreProvider`），并保持「失败回退基础能力」的约定。

---

## 5. 关键设计取舍（方案 13 的 7 条）→ 项目实际选择与理由

| # | 方案取舍 | 项目实际选择 | 理由 / 代价 |
|---|---|---|---|
| 1 | 优先保证音符与播放状态正确，而非立即追求照片级画面 | 先做相机取景、手部几何、状态反馈；材质精修务实推进，**不做**真实 AO / 后处理 | 教学核心是「看得懂、跟得上」；代价是画面仍为程序化渲染 |
| 2 | 保留统一时间轴，不允许模块各自计时 | `LessonTimeline` 唯一时间源；音频（事件驱动）与节拍器（`Tick`）都不自建计时 | 音符 / 拍点 / 琴键 / 高亮 / 手部必须一致；代价是音频 DSP 时钟与帧时钟需专门同步（本项目音频由事件驱动，天然规避独立时钟） |
| 3 | 当前用规则与 IK，AI 放在可替换接口之后 | `IFingeringProvider` 默认启发式；`IHandMotionProvider` / `IAvatarProvider` **计划中** | AI 有推理延迟 / 姿态异常 / 硬件与许可问题，不能成为基本播放的单点故障；代价是动作不达真人演奏级别 |
| 4 | 功能分层，而非全部塞进一条控制栏 | 主播放栏 / 练习工具栏 / 分析面板三层 | 高频需求集中、次级工具按需出现；代价是部分工具多一次点击 |
| 5 | 桌面优先，移动端响应式适配 | 仅 Windows；输入抽象**计划中** | 当前产品、鼠标右键与渲染验证均以 Windows 为基线；代价是跨平台分阶段 |
| 6 | 出现问题时提供降级与恢复，而非隐藏异常 | `AppError` + `RecoveryAction` + `HandleRecovery`；音频 / 角色可降级继续 | 用户导入的 MIDI、外部模型与音频环境无法完全控制；代价是需要状态机、错误码与测试用例 |
| 7 | 本地优先，用户数据可控 | 曲库 / 偏好本地持久化；无网络访问；支持删除与清除缓存 | 曲目可能具个人属性；代价是暂无云同步 / 跨设备接续 / 云端 AI |

---

## 6. 已知局限（与质量相关的诚实清单）

| 局限 | 影响 | 计划 |
|---|---|---|
| 性能数据仅在批处理 / 本机采样 | 不能代表真实设备帧率 | 真机测量后定稿（`Docs/perf/environments.md` E07/E08） |
| 环境遮蔽为几何近似 | 与真实 SSAO 观感有差距 | 受内置管线限制，暂不承诺 |
| 音频为波形合成 | 非真实钢琴音色 | 评估合法采样后替换（方案 7.3 五项） |
| 人物 / 钢琴为程序化网格 | 非外部高质量模型 | 接入许可清晰模型（适配层已就绪） |
| 指法为启发式 | 不承诺专业指法 | 保持「估算指法」标注；人工覆盖计划中 |
| 可用性研究未做 | 「新用户无文档完成主流程」未经验证 | 发布前补做 |
