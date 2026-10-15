# 动画管线设计（animation-pipeline.md）

> 描述「MIDI → 琴键 → 手部 → 手指」的完整链路，对应方案 3.3 基础数据流与 7 节分阶段计划。

---

## 1. 首版（MVP）管线

```text
MIDI 文件（.mid）
   │  MidiParser（纯 C#，SMF 解析：VLQ/running status/tempo map）
   ▼
MidiNoteEvent[]（pitch / startTime / duration / velocity / track / channel，秒制）
   │  MidiEventScheduler.Load → 展开为 (time, on/off, note) 事件流
   ▼
LessonTimeline 推进 CurrentTime（唯一时间源）
   │  MidiEventScheduler.Tick(CurrentTime) 触发到期的 on/off
   ├──▶ PianoKeyAnimator   ──▶ 琴键下压/回弹（绕键后端 X 轴旋转插值）
   ├──▶ PianoAudioSynth    ──▶ 基频+谐波+指数衰减合成，velocity 控音量
   ├──▶ HandIKController   ──▶ 左右手（C4=60 分界）手腕目标 → 琴键上方
   └──▶ FingerPoseController──▶ 按下键按 pitch 映射到手指，弯曲/伸直
```

---

## 2. 各环节说明

### 2.1 MIDI 解析（`MidiParser`）
- 支持 Format 0/1/2、PPQ 时间基；拒绝 SMPTE。
- 支持 running status、tempo 变化（`tick → 秒` 分段累积）。
- Note On（velocity>0）与 Note Off（0x8n 或 0x9n vel=0）配对；同一时刻多音符（和弦）天然支持。
- 悬空 Note Off 忽略；未关闭 Note On 首版丢弃。

### 2.2 琴键动画（`PianoKeyAnimator`）
- 88 键程序化生成（白键宽 1、黑键宽 0.6，黑键更短更高，位置按钢琴标准布局）。
- 键根 pivot 位于键后端，按下 = 绕 X 轴负角旋转；`Quaternion.Slerp` 平滑下压/回弹。
- `ReleaseAll()` 在 Stop/Seek/重载时立即复位所有琴键。

### 2.3 音频（`PianoAudioSynth`）
- 程序化合成：三角基频 + 2/3/4 次谐波 + 指数衰减包络，零外部音频资产（符合许可证原则）。
- 每个 pitch 预生成 `AudioClip`，Note On 时 `AudioSource.PlayOneShot(clip, velocity/127)` 叠加发声（支持和弦）。
- 已知限制：合成音为固定衰减（约 2.5s），不精确对应长音符时长（MVP 简化，后续可换合法音色采样或每音符独立 AudioSource 精确控制）。

### 2.4 手部 IK（`HandIKController`）
- 左右手以 C4（pitch=60）分界：`<60` 左手，`>=60` 右手。
- 每帧求「当前按下键」：左手取最低音、右手取最高音，作为手腕目标（键上方固定高度/深度）。
- 手臂为肩→腕单段胶囊，`StretchArm` 按距离拉伸并对准方向（简化两段 IK，MVP 够用）。
- 无按键时手腕回到键盘两侧准备位置。

### 2.5 手指姿态（`FingerPoseController`）
- 按下键按 pitch 排序映射到手指（指1..指5 对应低..高），按下弯曲 -35°、抬起伸直。
- **首版不承诺专业指法**（方案 2.3）：五指跨度启发式仅为视觉占位，真实指法需规则/标注数据/模型。

---

## 3. Seek / 停止 的状态重建

`LessonSessionController.Seek(t)` 顺序：
1. `LessonTimeline.Seek(t)`
2. `PianoKeyAnimator.ReleaseAll()` / `HandIKController.ReleaseAll()` / `FingerPoseController.ReleaseAll()`（清空状态）
3. `MidiEventScheduler.SeekTo(t)`（重放 seek 点之前已开始且仍在持续的 Note On，重建按键/手部状态）

---

## 4. 后续增强（方案 2.2 / 阶段五）

- 更自然手腕移动、手指按压、双手协同 → 替换 `HandIKController`/`FingerPoseController` 实现。
- 慢速/循环/分手练习/节拍器 → 扩展 `LessonTimeline` 与 `LessonControlsUI`。
- 音符高亮提示 → `PianoKeyAnimator` 加高亮通道。
- AI 动作生成（PianoMotion10M / MANO）→ 替换 `HandIKController` 数据源，保留 `IHandRig` 与「当前按下键」接口。

---

## 5. 阶段五已落地的手部动作改进

### 5.1 手腕路径：换位抬升

`HandIKController.ApplyGlideLift()`：当手腕与目标的**水平位移**超过 `GlideThreshold`（默认 0.07m）时，
把目标 Y 抬升 `GlideLift × t`（最多 0.055m），接近后自然回落。
效果：手腕走「抬起 — 平移 — 落下」弧线，避免贴键直线横扫造成的穿模与跳姿。

**单帧位移硬约束**：`MaxStepPerFrame`（默认 0.035m，约 2.1 m/s @60fps）——
超过则按比例截断，杜绝瞬移观感（对应方案阶段五「避免动作失控」）。

### 5.2 移动速度：按音符间隔自适应

按最近音符间隔的指数平滑值在 `[MinSpeedScale, MaxSpeedScale]` 之间插值：
间隔 ≤ 0.12s → 1.8×；间隔 ≥ 0.6s → 0.55×。稀疏片段优雅移动、密集片段跟得上。
时间基准来自 `LessonSessionController` 每帧传入的 `LessonTimeline.CurrentTime`（`Tick(currentTime)`），
不自行计时（方案 4.3）；`ReleaseAll()` 会复位间隔统计，避免 Seek 后沿用旧节奏。

### 5.3 手指姿态：按音高比例分配

`FingerPoseController` 由「按音高排序取前 N 根手指」改为**按音高比例映射手指索引**：

| 场景 | 行为 |
|---|---|
| 单音 | 落到中间手指（更接近自然手型） |
| 和弦（跨度大） | 手指自然展开（指 1 与指 5 分别对应最低/最高音） |
| 和弦（跨度小） | 相邻手指收拢 |
| 索引冲突 | 顺延到最近空闲手指 |
| 按键数 ≥ 手指数 | 复用理想索引，不越界 |

另按音高比例给出**左右张开角**（`SpreadAngle`，绕 Y 轴），形成和弦手型。

### 5.4 异常姿态检查（方案阶段五验收第 2 条）

- `HandPoseValidator`（纯静态）+ `HandPoseLimits`：校验手腕是否低于键面、超出键盘两端、
  深入琴体或过度悬空；`Clamp()` 在运行时把越界位置钳回。
- **钳制与校验必须自洽**：`Validate` 的所有边界比较都带 `Tolerance`（0.002m）浮点容差——
  否则「刚被钳到边界值的位置」会因浮点舍入再次被判越界。
- 允许范围由钢琴实际几何推导（`HandIKController.BuildLimits()`）。
  **注意**：键根世界 Y 已包含 `KeybedHeight`，键面高度应为 `键根Y + WhiteHeight`，
  不可再叠加 `KeySurfaceY`（否则范围整体抬高约 0.7m，全部位置误报）。
- 深度上界刻意放宽（`keyRootZ + 0.9m`）：手腕从人物侧就位时会经过琴键前方空间，
  属正常过渡路径；真正的穿模风险在深度**下界**（深入琴体）。

### 5.5 动作质量量化（方案阶段五验收第 1 条）

`HandMotionAccumulator`（无场景依赖，可 EditMode 测试）+ `HandMotionReport`：

| 指标 | 含义 | 实测（demo.mid） |
|---|---|---|
| 命中率 | 手腕**水平对准**目标键的采样占比 | **86.5%** |
| 最大帧位移 | 单帧最大位移（米），用于发现瞬移 | **35 mm** |
| 平滑度 | 净位移 / 路径长度（1 = 直线） | 0.19 |
| 双手协调 | 双手同时移动的采样占比 | 3.6% |

命中判定使用**水平（XZ）距离**而非 3D 距离——换位抬升会临时抬高手腕，不应因此判为未命中。
运行时结果在「曲目分析」面板底部实时显示（每 0.25s 刷新）。

---

## 6. 缺陷修复记录（本轮）

| # | 缺陷 | 位置 | 根因 | 修复 |
|---|---|---|---|---|
| B1 | 左右手共用一个 `_fingerPitch` 数组，右手分配会清空左手结果 | `FingerPoseController` | `AssignFingers` 开头整体清零 + 两手复用同一数组 | 拆为 `_leftFingerPitch` / `_rightFingerPitch`，按手传入目标数组 |
| B2 | 和弦的 gap=0 掩盖真实最快音符间隔 → "快速片段"建议永久失效 | `SongAnalyzer:76` | `gap >= 0f` 允许同起点音符参与取最小 | 新增 `MinDistinctStartGap`，只统计**不同起始时刻**之间的间隔 |
| B3 | （见下方实测结论）目标分级限速 | `HandIKController.LimitTargetRate` | 保留为**防御性机制** | 小变化（≤0.20m）直接精确对齐；仅大跳变以 4.0 m/s 限速过渡 |
| B4 | 高亮每帧对全部预提示键重写 `MaterialPropertyBlock` | `PianoKeyHighlighter.RefreshUpcoming` | 无条件 `ClearUpcoming()` + 重新着色 | 先用暂存集合构建并与旧集合 `SetEquals`，相同则直接返回（零 GPU 操作） |
| B5 | 未关闭的 Note On 被静默丢弃 | `MidiParser` | 注释"首版丢弃" | 以全曲最大 tick 收尾补全 + `MidiParseResult.DanglingNoteCount` 上报 |

### 6.1 B3 的实测结论（重要，避免后续重复踩坑）

初始假设："分解和弦 / 快速音阶导致手腕目标跳变 → 视觉抖动"（依据是平滑度仅 0.19）。
经四轮迭代（LPF 平滑 → 速率限制 → 分级限速 → 参数修正）后，用 `LeftSmoothedTarget` 实测得到：

> **目标单帧最大变化 = 0 mm**（阈值 200 mm）

即：在真实曲目中，手腕目标**从未发生跳变**（旋律跳进均在同一手位内，变化量 < 0.20m）。
**初始假设被实测数据否证** —— 平滑度 0.19 反映的是"手腕跟随移动的旋律"，不是抖动。

因此 `LimitTargetRate` 的定位调整为**防御性机制**（应对将来接入 AI 动作或极端跨手位曲目），
参数取值使其在正常曲目中不生效（不拖累命中率：实测 82.9%）。**教训：先用数据验证假设，再动手改。**
