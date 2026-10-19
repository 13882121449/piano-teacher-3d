# 动画管线设计（animation-pipeline.md）

> 描述「MIDI → 琴键 → 音频 → 手腕 → 指法 → 指尖」的完整链路，对应方案 **3.3 基础数据流**、
> **5.1–5.3 人物/手部质量**、**5.3 手指动作与教学诚实性**、**7 阶段五**、**11.3 验收门槛**；
> 审查 **1.2 手部模型与姿态**、**1.2C 扩充动作质量指标**。
>
> **口径一致性要求**（审查 1.2 末段 / 方案 11.3）：所有量化指标必须使用**同一组 MIDI 测试曲**与**同一套测量方式**比较，
> 否则优化前后不可对比。本文把每个指标的计算口径写清楚。

---

## 1. 管线总览

```text
MIDI 文件（.mid / .midi）
   │  MidiParser（纯 C#，SMF：VLQ / running status / tempo map / Format 0-2）
   ▼
MidiNoteEvent[]（pitch / StartTime / Duration / Velocity / Track / Channel，秒制）
   │  MidiEventScheduler.Load → 展开为 (time, on/off, note) 事件流
   ▼
LessonTimeline 推进 CurrentTime（唯一时间源）
   │  MidiEventScheduler.Tick(CurrentTime) 触发到期的 on/off
   ├──▶ PianoKeyAnimator    ──▶ 琴键下压/回弹（绕键后端局部 X 轴旋转插值）
   ├──▶ PianoAudioSynth     ──▶ 受控声部池发声（力度→音量/音色，Note Off 淡出释放）
   ├──▶ HandIKController    ──▶ 手腕目标（左右手 C4 分界） + 指尖目标（独立求解）
   ├──▶ FingerPoseController──▶ 指法提供器 IFingeringProvider → 手指弯曲/张开
   └──▶ PianoKeyHighlighter ──▶ 高亮（MaterialPropertyBlock，不动角度）
```

**新增/升级点**（相对改造前）：音频改为受控声部池（不再 `PlayOneShot`）；手部由「按音高比例直接映射手指」升级为
「调用可替换指法提供器」；手腕目标与指尖目标分离求解；琴键加倒角、黑键三层次、琴体补齐与材质分层。

---

## 2. 各环节说明

### 2.1 MIDI 解析（`MidiParser` + `MidiFileValidator`）

- 支持 Format 0/1/2、PPQ 时间基；**拒绝 SMPTE**（返回 Fail）。
- 支持 running status、tempo 变化（`tick → 秒` 分段累积）。
- Note On（velocity > 0）与 Note Off（`0x8n` 或 `0x9n` vel=0）配对；同一时刻多音符（和弦）天然支持。
- **Format 2（独立序列）兼容策略**：为保证教学只有一条时间轴，按**轨道顺序串接**为一条时间轴（不并行演奏各 Pattern），并写入 `Warnings`。
- **极端 tempo 防御**：tempo ≤ 0 或 > 6,000,000 µs/四分音符（= 10 BPM）一律回落 120 BPM，并写入一次 `Warnings`。
- **未关闭的 Note On**：以全曲最大 tick 收尾补全（不再静默丢弃），`MidiParseResult.DanglingNoteCount` 上报数量。
- **导入前校验**（`MidiFileValidator.Validate`，方案 6.4）：存在 → 可读 → 大小上限（8 MB）→ 扩展名初筛 → 真实解析 → 无音符 → 时长为零 → 事件数 / 时长上限（20 万 / 1 小时）。

### 2.2 琴键动画（`PianoKeyAnimator`）

- 88 键程序化生成；键根 pivot 位于键后端，按下 = 绕局部 X 轴旋转；`Quaternion.Slerp` 平滑下压/回弹。
- `ReleaseAll()` 在 Stop / Seek / 重载时立即复位所有琴键。

### 2.3 音频（`PianoAudioSynth` + `PianoVoiceAllocator`）

- 程序化合成：基频 + 2/3/4 次谐波 + 指数衰减（约 2.5 s 固定波形），零外部音频资产（符合许可证原则）。
- **发声方式**：由 `AudioSource.PlayOneShot` 改为**受控发声池**（24 个 `AudioSource`，每个槽位持有一个 `AudioLowPassFilter`）。
  - 每音高预生成 1 份 `AudioClip` 并复用；`OnNoteOn` 从池取槽位发声。
  - **力度响应**：Volumelinear 映射（`VelocityGain` 含 0.10 最小底噪）；**音色**由声部低通截止频率随力度升降（1400 Hz 弱奏 → 20000 Hz 强奏）。
  - **Note Off 淡出释放**：0.12 s 余弦淡出（`DefaultFadeSeconds` / `CosineFade`），无爆音。
  - **释放语义**：`ReleaseAll()` 立即把 `ActiveCount` 归零（满足「切曲/暂停后不再有在发声部」），声音在 0.12 s 内自然淡出。
  - 池满抢占顺序：**正在淡出 → 最弱 → 最旧**。
  - **初始化失败降级**：设备不可用 / `AudioClip.Create` 失败时不抛异常，置 `Initialized = false` 并写中文 `LastInitError`，上层提示「音频不可用」并允许**静音继续**。
- 已知限制：合成音为**固定衰减波形**，不精确对应长音符时长（方案 7.3 允许其作为长期降级模式）。

### 2.4 手腕 IK（`HandIKController`）

- 左右手以 C4（pitch = 60）分界：`< 60` 左手，`>= 60` 右手。
- 每帧求「当前按下键」：左手取最低音、右手取最高音，作为手腕目标（键上方固定高度 / 深度，`HandHeight = 0.768 m`、`HandDepth = 0.12 m`）。
- 手臂为肩→腕单段胶囊，`StretchArm` 按距离拉伸并对准方向。
- 无按键时手腕回到键盘两侧准备位置。
- **保留的两项硬约束**（方案阶段五「避免动作失控」）：
  - `ApplyGlideLift`（**换位抬升**）：手腕与目标的水平位移 > `GlideThreshold`（0.07 m）时，目标 Y 抬升最多 `GlideLift`（0.055 m）再回落，走「抬起—平移—落下」弧线。
  - `MaxStepPerFrame`（**单帧位移硬约束**，0.035 m/帧，约 2.1 m/s @60fps）：超过则按比例截断，杜绝瞬移。
- **间隔自适应速度**（`CurrentSpeedScale`）：按最近音符平均间隔在 `[0.55×, 1.8×]` 之间插值（间隔 ≤ 0.12 s → 1.8×；≥ 0.6 s → 0.55×）。
- **分级限速**（`LimitTargetRate`，防御性）：同手位内的音程移动（≤ `TargetJumpThreshold` = 0.20 m）直接精确对齐；仅大跳变以 `TargetMaxSpeed`（4.0 m/s）限速过渡。

### 2.5 指法（`FingerPoseController` → `IFingeringProvider`）

`FingerPoseController` 不再直接按音高比例映射手指，而是**调用可替换的指法提供器**
`IFingeringProvider`（默认 `HeuristicFingeringProvider`，方案 5.3 / 10 / 取舍 3）：

- **输入**（`FingeringContext`）：是否为左手、该手当前按下的音高（升序）、最近的旋律音高序列（`RecentMelody`）、统一时间轴时间。
- **输出**（`FingeringPlan`）：`FingerByPitch`（每音一个手指编号，0 = 拇指 … 4 = 小指，-1 = 未分配）、`Confidence`（0..1）、`IsEstimated`、中文 `Reason`。
- **规则集**（`HeuristicFingeringProvider`）：① 音域就近（锚点 = 中间指）；② 相邻音落相邻/同指（单调映射保证）；③ 跨度大向外展开；④ 黑键避免短指（拇指/食指）；⑤ 最近旋律跨度 ≥ 1 个八度用拇指穿越；⑥ 重复音沿用同指；⑦ 和弦用固定模板（thumb-middle-little 等）保证手型一致；⑧ 左右手分界校验（落在对手侧降置信度并标注）。
- **教学诚实性**：`Confidence < LowConfidenceThreshold`（0.55）时 `IsEstimated = true`，UI 应标注「**估算指法**」而不是确定编号（方案 2.3）。
- **保留语义**：按下角 `PressAngle` / 静止角 `RestAngle` / 张开角 `SpreadAngle` 仍是**无三关节手部**时的兜底姿态；
  弯曲通过旋转 `IHandRig.Fingers[f]` 根节点实现，**不移动手腕**，不破坏单帧位移硬约束与防穿模钳制。
- **性能**：指法只在「按键集合发生变化」时重新求解（`OnNoteOn/OnNoteOff/ReleaseAll` 置脏标记），`Update` 路径无 LINQ / 无字符串拼接 / 无每帧 `new`。

### 2.5.1 关节级姿态（附录 A.4，2026-10-18）

手部实现 `IFingerJointRig` 时，`FingerPoseController` 走**三关节受限 IK**（详见 `Docs/hand-mesh-design.md` 第 6 节）：

| 项 | 取值 | 说明 |
|---|---|---|
| 关节分工 | MCP / PIP / DIP 各自独立 | 平面内 CCD，远端先弯、近端后弯 |
| 求解 | 8 次迭代 | 平面坐标（+X = 目标方向，+Y = 掌侧下垂），目标 (d, 0) |
| 限位 | 82° / 95° / 62° | 关节角 ≥ 0（不允许反关节），超限钳制并计入 `JointLimitClampCount` |
| 指根朝向 | `LookRotation(dir, −perp)` | 再左乘链节点的解剖偏航的逆，拇指 57° 不被覆盖 |
| 阶段 | `Approach` / `Press` / `Hold` / `Rest` | 由 Note On/Off + `HoldSettleSeconds`（0.12 s）判定，不另建计时器 |

> 没有三关节的手部（或自定义资产）自动退回 2.5 的单关节路径，行为与改造前一致。

---

## 3. 本轮手部几何升级（方案 5.1 / 5.2 短期 + 审查 1.2A + 附录 A.3）

手部由「球体/胶囊拼接」升级为**单块连续截面放样网格 + 三关节骨骼**（`HandMeshFactory` + `AvatarManager.BuildHand`）。
**所有手部尺寸以真实白键宽 `WhiteKeyWidth = 0.024 m`（24 mm）为标尺**，而不是凭屏幕比例调手（审查 1.2A）：

| 部位 | 几何处理 | 关键参数（`HandMetrics`，单位米） |
|---|---|---|
| **掌部** | **单块放样网格**：沿掌长的超椭圆截面（圆角矩形），宽度剖面 0.74 → 1.00 → 0.94、厚度剖面 0.86 → 1.00 → 0.80；拇指侧半宽 ×1.06、小指侧 ×0.94；大鱼际/小鱼际作为**轮廓隆起**叠在掌侧 | `PalmWidth = 0.076`（≈ 3.2 个白键宽）、`PalmThickness = 0.030`、`PalmLength = 0.058`、`WristRadius = 0.026` |
| **指节** | 每指三段网格，半径按 `0.88^段号` 渐缩、段内再渐缩；起始 18% 渐入 + 关节处 +5.5% 隆起；起点回退 7% 段长与上一段重叠；末节带**半球形指尖** | `FingerSegmentRatios = { 0.42, 0.33, 0.25 }` |
| **五指差异** | 中指最长、小指最短、食指/无名指不等长；粗细不等；卡通角色掌更宽、指更短 | `FingerLengths = { 0.068, 0.082, 0.090, 0.084, 0.064 }`；`FingerRadii = { 0.0115, 0.0096, 0.0099, 0.0091, 0.0080 }` |
| **拇指对掌** | 拇指链相对四指偏航 57°（45°–60° 区间），与四指形成虎口；掌部网格的拇指侧半宽再 ×1.06 形成大鱼际轮廓 | `ThumbAbduction = 57°`；`FingerChainYaw = { 57, 6, 1, -5, -12 }` |
| **掌指关节位置** | 拇指靠近腕部、偏向外侧（`KnucklePositions`）；掌部网格从腕后 0.55×腕半径一直包到掌指关节外 2% | 见源码 |
| **关节链** | 每指 MCP → PIP → DIP，关节间距 = 段长 × 0.96；Humanoid 骨架同步扩展为三节点（`LeftIndexProximal/Intermediate/Distal`） | `AvatarRigBuilder.JointsPerFinger = 3` |
| **精度档** | `High` 增加指甲薄片与更多截面环（14 / 12 / 7）；`Low` 减少环数（8 / 6 / 3）且不做圆指尖 | `AvatarQualityConfig.MeshDetail` |

> 语义不变：`IHandRig.Fingers` 仍是「每指一个**根** Transform」，由姿态控制器旋转该根节点对准目标键；
> 三关节挂在根的 `Chain` 子节点下，解剖朝向（拇指对掌方向、四指扇形）烘在 `Chain` 上，**不被姿态控制器覆盖**。
> 网格与关节分离：改手掌形状不会改变骨骼语义、键位标尺与 IK 目标（见 `Docs/hand-mesh-design.md`）。

人物其余部分（轮本轮）：头部由纯球体升级为椭球 + 下颌 + 耳 + 鼻 + 眼 + 眉；躯干用不同宽度的平滑过渡体块（髋/腰/胸/肩带）；
服装（衣领 / 袖口 / 下摆 / 腰带）作为独立可见层；发型为「大轮廓 + 少量发束」（不做大量细长圆柱，避免噪点与穿插）。

---

## 4. 指尖目标与手腕目标分离求解（方案 5.3 第二阶段 / 审查 1.2B 第二步）

`HandIKController` 在同一帧内做两件**互不影响**的事：

1. **手腕 / 掌心**：仍按第 2.4 节的移动规则（换位抬升、自适应速度、单帧位移硬约束、分级限速、防穿模钳制）移动。
2. **指尖目标**（`UpdateFingertipTargets`）：
   - 为每个按下音高取**可复用的键面按压目标点**（`KeyTouchPoint`，在 `Initialize` 时按 88 键预计算）。
   - 按压点落在**键面前半段**（沿键长方向的比例 `FingertipTargetForward` 默认 0.80），**不是**键位中心的任意高度。
   - 把每个音高分配给「离它最近的指尖」（比较 `FingertipWorld` 与按压点的 3D 距离）。
   - 指尖目标只用于「指尖到目标键面误差 / 离键悬空」统计与和弦成形过渡，**不反向移动手腕**。

### 4.1 黑键独立目标高度

- 黑键与白键的**键面高度不同**，因此按压目标高度分别取：
  - 白键：`key.position.y + WhiteHeight`（`WhiteHeight = 0.022 m`）
  - 黑键：`key.position.y + BlackHeight`（`BlackHeight = 0.032 m`）
- `HandIKController.BlackWhiteSurfaceDelta = BlackHeight − WhiteHeight`（0.010 m）供诊断与验证使用。
- **不改变琴键映射的真实坐标**（方案 5.4）：高度差只用于目标与误差统计。

### 4.2 和弦「先成形再下压」

- 当该手同时按下 ≥ 2 个音（和弦）时，指尖目标从「手型上方」在 `ChordFormationSeconds`（默认 0.10 s）内过渡到键面，
  避免所有指尖在同一帧突然到位。
- 过渡高度为 `ChordApproachHeight`（默认 0.022 m），成形进度 `blend` 为跨帧累积量（只清「是否有目标」标志，不清 blend，否则过渡永远走不完）。
- 单音时 `blend` 直接置 1（无过渡）。

### 4.3 与 MIDI 时间的一致性

- 指尖目标与手腕目标的更新都由 `HandIKController.Update` 每帧驱动；手指按压角与 MIDI Note On/Off 保持一致。
- Seek / 循环回跳 / 换曲 / 暂停恢复时，`ReleaseAll()` / `ResetMetrics()` 会复位 `_pressed`、间隔统计、目标平滑、指尖目标与成形进度，
  避免沿用旧手型（方案 6.3）。

---

## 5. Seek / 停止 / 暂停恢复 的状态重建（方案 6.3 / 7.3）

`LessonSessionController` 在需要「按当前时间点重建瞬时状态」时，统一调用 `ResetPlaybackState()`，顺序：

1. `PianoKeyAnimator.ReleaseAll()`（琴键复位）
2. `HandIKController.ReleaseAll()`（手腕 / 手指 / 指尖目标复位）
3. `FingerPoseController.ReleaseAll()`（指法与旋律历史复位）
4. `PianoKeyHighlighter.ClearAll()`（高亮颜色还原）
5. `PianoAudioSynth.ReleaseAll()`（所有声部淡出释放）
6. `Metronome.Reset(currentTime)`（节拍游标重定位，不补发历史拍点）

之后由 `MidiEventScheduler.SeekTo(t)` 重放 seek 点之前已开始且仍在持续的 Note On，重建按键与手部状态。
触发场景：`Seek` / `SeekNormalized` / `Restart` / `Stop` / 切曲 / 暂停后 `Play`（从暂停恢复）/ 循环或时间回退检测（`Update` 中 `current < _lastTime - 0.001`）。

---

## 6. 动作质量指标：口径与计算（方案阶段五验收 / 审查 1.2C）

指标由 `HandMotionAccumulator`（无场景依赖，可在 EditMode 单测）逐帧累加，`Build()` 产出 `HandMotionReport`。
**所有指标的采样输入是 `HandMotionSample`**，由 `HandIKController.SampleMetrics` 每帧组装（`EnableMetrics` 打开时）。

### 6.1 指标清单与计算口径

| 指标 | 字段 | 计算口径 |
|---|---|---|
| **目标命中率** | `HitRate` | 有目标帧中「手腕对准目标键」的占比。**命中判定使用手腕的 XZ 平面（水平）距离 ≤ `HitRadius`（0.09 m）**，与手腕高度无关（换位抬升会临时抬高手腕，不应因此判为未命中） |
| **左右手分项命中率** | `LeftHitRate` / `RightHitRate` | 分别对 `LeftHasTarget` / `RightHasTarget` 的目标帧统计；**该手无目标帧时按 1 计**（与总命中率口径一致），同时暴露 `LeftTargetSamples` / `RightTargetSamples`（UI 可显示「样本 0」而非伪造 100%） |
| **指尖到目标键面误差** | `FingertipMeanError` / `FingertipMaxError` | 每手每帧对每个按下音高，取「离它的最近指尖到该键按压点」的 3D 距离（`FingertipWorld` 优先用 `IFingertipRig.GetFingertip`，否则按指根 + 前向 + `FingertipReachEstimate` 估计）；每手每帧最多计 1 个样本 |
| **指尖命中率** | `FingertipHitRate` | 误差 ≤ `FingertipErrorTolerance`（0.035 m）的样本占比；无样本为 1 |
| **最大单帧位移** | `MaxStepDistance` | 手腕单帧位移的最大值（米），用于发现瞬移 |
| **平滑度** | `Smoothness` | 净位移 / 路径长度（1 = 完全直线），左右手分别计算后取平均 |
| **双手协调** | `CoordinationRatio` | 左右手**同时移动**（单帧位移 > `MovingThreshold`）的采样占比 |
| **速度变化异常** | `SpeedAnomalyCount` | 连续帧位移变化超过 `SpeedAnomalyStepDelta`（0.020 m）的次数（加速度突变 / 顿挫） |
| **分组命中率** | `BlackKeyHitRate` / `ChordHitRate` / `FastScaleHitRate` / `LargeMotionHitRate` | 在该帧被标记为「黑键 / 和弦 / 快速音阶 / 跨手大位移」时统计命中率；无样本为 1，并暴露对应样本数 |
| **视觉抽查计数** | `CrossedHandFrames` / `ThumbOrientationIssues` / `PalmPenetrationFrames` / `HoveringFrames` | 左右手交叉帧数 / 拇指方向异常次数 / 手掌穿模帧数 / 离键悬空帧数 |

### 6.2 分组与抽查的判定阈值（`HandIKController` Inspector）

| 标记 | 判定 |
|---|---|
| `BlackKeyFrame` | 该手按下音包含黑键（`PianoKeyMap.IsBlackKey`） |
| `ChordFrame` | 该手同时按下 ≥ 2 个音 |
| `FastScaleFrame` | 平滑后的平均音符间隔 ≤ `FastScaleGapSeconds`（0.18 s） |
| `LargeMotionFrame` | 该帧目标位移 ≥ `LargeMotionDistance`（0.10 m） |
| `HandsCrossed` | 左手腕 X > 右手腕 X（`HandPoseValidator.AreHandsCrossed`） |
| `ThumbOrientationIssue` | 拇指轴与掌心前向夹角 > `ThumbOrientationMaxDegrees`（75°；正常手型约 45°–60°） |
| `PalmPenetration` | 掌心低于允许范围 `MinY` |
| `Hovering` | 指尖高于目标键面 > `HoverHeight`（0.045 m） |

> 诊断文本 `HandMotionReport.ToLogString()` 一次输出上述全部维度（命中率 / 最大帧位移 / 平滑度 / 双手协调 / 采样 / 左右手分项 / 指尖误差 / 四个分组 / 四个抽查 / 速度异常），便于日志对照。

### 6.3 失败案例记录位置

- **可视化失败案例**：`Docs/screenshots/V*.png`（12 张，由 `VisualSnapshotTests.CaptureLessonViews` 生成）+ `01_full.png` 等 6 张保留基线；
  清单与观察要点见 `Docs/screenshots/manifest.md`；V02（大跨度）与 V09（A-B 循环后构图）专门覆盖「跨手位」「循环回跳」两类高风险场景。
- **近景人工抽查项**：手指交叉、拇指方向错误、手掌穿模、离键悬空 —— 对应 `HandMotionReport` 的四个抽查计数 + 人工看图。
- **异常姿态日志**：`HandIKController.PoseIssues`（最近 8 条异常姿态描述）与 `PoseIssueCount`。

---

## 7. 缺陷修复记录（承接上一轮，仍未回退）

| # | 缺陷 | 位置 | 修复 |
|---|---|---|---|
| B1 | 左右手共用一个 `_fingerPitch` 数组，右手分配会清空左手结果 | `FingerPoseController` | 拆为 `_leftFingerPitch` / `_rightFingerPitch`，按手传入目标数组 |
| B2 | 和弦的 gap = 0 掩盖真实最快音符间隔 → "快速片段"建议永久失效 | `SongAnalyzer` | 新增 `MinDistinctStartGap`，只统计不同起始时刻之间的间隔 |
| B3 | 目标分级限速 | `HandIKController.LimitTargetRate` | 保留为**防御性机制**（小变化直接精确对齐；仅大跳变限速） |
| B4 | 高亮每帧对全部预提示键重写 `MaterialPropertyBlock` | `PianoKeyHighlighter.RefreshUpcoming` | 先用暂存集合构建并与旧集合 `SetEquals`，相同则直接返回（零 GPU 操作） |
| B5 | 未关闭的 Note On 被静默丢弃 | `MidiParser` | 以全曲最大 tick 收尾补全 + `DanglingNoteCount` 上报 |
| B6 | 全景纯侧视遮挡键盘低音端 | `CameraModeController` | 改为前侧 3/4 角度（`OverviewYaw = 34°`）；详见 [`Docs/architecture.md`](architecture.md) 与视觉回归文档 |

### 7.1 B3 的实测结论（重要，避免后续重复踩坑）

初始假设「分解和弦 / 快速音阶导致手腕目标跳变 → 视觉抖动」（依据是平滑度偏低）。
经四轮迭代后实测得到：**目标单帧最大变化 = 0 mm**（阈值 200 mm）——在真实曲目中手腕目标**从未发生跳变**。
**初始假设被实测数据否证**。因此 `LimitTargetRate` 定位为防御性机制（应对未来接入 AI 动作或极端跨手位曲目），参数取值使其在正常曲目中不生效。
**教训：先用数据验证假设，再动手改。**

---

## 8. 后续增强（计划中，方案 2.2 / 10 章）

- `IHandMotionProvider`（基础 IK / 缓存动画 / AI 生成动作）——**计划中**；手部动作当前由 `HandIKController` 直接实现，未抽出提供器。
- 真实采样音色替换波形合成——**计划中**；需先评估延迟 / 长音衰减 / 力度响应 / 同时发声数 / 许可条件（方案 7.3）。
- 人工编辑 / 覆盖指法并与原始 MIDI 分开保存——**计划中**；当前 `IFingeringProvider` 已可替换，但尚无「人工覆盖」实现（方案 5.3）。
