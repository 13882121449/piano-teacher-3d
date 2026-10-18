# 手部对键指标口径（统一说明）

> 对应评估文档 **P0-02 第 1 条**「先统一指标定义」与附录 **A.6.3「指标必须分开统计」**。
> 本文只描述**代码当前实际测量的是什么**（附文件与方法），以及已知口径缺陷；
> 文中「建议目标」是工程门槛，不是已达成结果。
>
> 数据基线：2026-10-16 本机实测。自测命令见文末。

---

## 1. 五个核心指标的精确定义

| 指标 | 代码字段 | 定义（精确） | 单位 | 采样方式 | 统计范围 | 排除规则 |
|---|---|---|---|---|---|---|
| **手腕命中率** `WristHitRate` | `HandMotionReport.HitRate` / `LeftHitRate` / `RightHitRate` | 当前帧该手**手腕世界坐标**与 `<手>Target` 的**水平（XZ）距离** ≤ `HitRadius`（默认 0.09 m）记为命中 | % | 每帧 × 每只「有目标」的手各 1 次 | 整个采样窗口 | 该手无目标（`LeftHasTarget=false`）的帧不参与 |
| **手腕目标误差** `WristTargetError` | 由 `HitRate` 间接表达（未单独存均值） | 同上距离，但**不落盘为独立指标**，只用于命中判定 | m | 同上 | 同上 | 同上 |
| **指尖误差** `FingertipError` | `HandMotionReport.FingertipMeanError` / `FingertipMaxError` / `FingertipHitRate` | 该手**每个按下的音**，取「五指中**离该键按压点最近**的指尖」的 **3D 距离**；再按音取平均，得到该手本帧误差 | m | 每帧 × 每只「有按下音」的手 | 整个采样窗口 | 该手无按下音时返回 `-1`，不计入样本 |
| **单帧位移** `PerFrameDisplacement` | `HandMotionReport.MaxStepDistance` | **相邻两帧手腕**的三维距离峰值（左右手取大） | m | 每帧（需上一帧） | 整个窗口取最大值 | 首帧无前值，不参与 |
| **换位峰值误差** `TransitionPeakError` | **当前没有独立实现** | 现只能从 `FingertipMaxError` 里间接看到（含换位过渡帧） | m | 每帧 | 全窗口 | 无 |

### 1.1 关键代码位置

| 逻辑 | 文件 | 方法 / 字段 |
|---|---|---|
| 手腕命中判定（水平距离 ≤ 半径） | `Assets/Scripts/Avatar/HandMotionMetrics.cs` | `HandMotionAccumulator.AddSample` → `HorizontalDistance(...) <= s.HitRadius` |
| 手腕目标来自哪里 | `Assets/Scripts/Avatar/HandIKController.cs` | `SampleMetrics` → `LeftTarget = _leftTargetPos`（**已限速/平滑后的手腕目标**） |
| 指尖误差（最近指尖） | 同上 | `MeanFingertipError(...)`：`best = min over f of Distance(FingertipWorld(f), touch.Press)`，再 `sum / samples` |
| 按压点定义 | 同上 | `KeyTouchPoint.Press` = `(key.x, 键面 y, key.z + length * 0.80)`（键面前段 80%，真实键面高度） |
| 单帧位移 | `HandMotionMetrics.cs` | `dl = Distance(_prevLeft, s.LeftWrist)`，取 `max` |
| 指尖命中容差 | `HandIKController.cs` | `FingertipErrorTolerance = 0.035 m` |

---

## 2. 已知口径缺陷（有代码依据，必须先修口径再谈优化）

### 2.1 `HitRate` 的目标不是琴键，而是「平滑后的手腕目标」

`LeftTarget = _leftTargetPos` 是 `HandIKController` 自己算出的**手腕期望位置**（已做过限速与手掌修正）。
因此 `HitRate` 实质是「手腕是否跟上了自己的目标」——它衡量**跟随延迟**，不能当作「手是否到了琴键」。

**影响**：93.6% 是「跟随达成率」，不能与「指尖对键精度」相提并论；文档里若把两者并列会误导。

**建议**：新增 `WristToKeyError`，目标点用 `KeyTouchPoint.Press` 对应到手腕平面后的坐标，并单独统计。

### 2.2 `FingertipMeanError` 取「最近指尖」，不是「被分配指尖」

`MeanFingertipError` 对每个按下的音，遍历**五指**取最小距离。这意味着：即使真正该按该键的手指够不到，
只要旁边任何一根指尖接近键面，误差就会变小 —— 这是**乐观下界**。

**与测试口径不一致**：`HandFingerCorrespondenceTests` 断言的是「**被分配**的手指」到该键按压点的距离
（静态收敛 < 40 mm），两者不是同一个量。

**建议**：新增 `AssignedFingertipError`，取值来自 `FingerAssignmentService.GetFinger(left, pitch)`，
与视觉一致性测试使用同一口径；原「最近指尖」保留为 `NearestFingertipError` 作对照。

### 2.3 `MaxStepDistance` 是手腕位移，不能解释指尖峰值

`MaxStepDistance` 只比较手腕的相邻帧三维距离（受 `MaxStepPerFrame = 0.035 m` 硬约束，峰值必然 ≤ 35 mm 量级）。
文档中曾出现的「260–280 mm 换位峰值」来自**指尖误差**序列，不是手腕位移 —— 两者不可混称。

**建议**：把 `FingertipMaxError` 明确标注为「指尖对键误差峰值」，并补一个 `TransitionPeakError`：
在检测到目标跳变（`SeekJumpThreshold = 0.45 m`）后的 N 帧窗口内单独统计。

### 2.4 统计频率与「音符级」不对齐

所有指标都是**帧级**（每帧最多贡献 1 个手腕样本 / 每手 1 个指尖样本），而教学关心的是「每个音符是否按对」。
高帧率下长音会显著稀释单音误差，低帧率则相反。跨设备比较前必须固定帧率（本项目锁 60 FPS）。

---

## 3. 建议新增的指标（当前未实现，标「计划中」）

| 指标 | 定义 | 用途 | 状态 |
|---|---|---|---|
| `WristToKeyError` | 手腕位置到「目标键对应手腕点」的距离（不做平滑） | 衡量真实对键，替代 `HitRate` 的误导 | 计划中 |
| `AssignedFingertipError` | **被分配**手指的指尖到该键按压点 3D 距离 | 与视觉一致性断言同口径 | 计划中 |
| `KeyTopDistance` | 指尖到键面的**法向**距离（只用 Y 分量） | 判断「悬空」而非「位置偏移」 | 计划中（现用 `Hovering` 布尔近似） |
| `TransitionPeakError` | 目标跳变后 N 帧窗口内的指尖误差峰值 | 分离「换位过渡」与「稳态」 | 计划中 |
| `JointLimitHits` | 关节角度限位触发次数（按指/关节分类） | 定位「够不到」是分配问题还是可达性问题 | 计划中（需先有多关节骨骼，见附录 A.4） |
| `P50 / P95 / P99` | 上述误差的分位数（按分组） | 避免均值掩盖尾部 | 计划中 |

---

## 4. 分组口径（已实现）

`HandMotionSample` 的 4 个分组标记（`HandMotionMetrics.cs`），命中率各自独立统计：

| 分组 | 判定条件（代码） | 当前字段 |
|---|---|---|
| 黑键 | 任一按下音为黑键（`PianoKeyMap.IsBlackKey`） | `BlackKeyHitRate` |
| 和弦 | 任一只手同时按下 ≥ 2 个音 | `ChordHitRate` |
| 快速音阶 | 平滑后的音符平均间隔 ≤ `FastScaleGapSeconds`（0.18 s） | `FastScaleHitRate` |
| 跨手/大跳 | 单帧目标位移 ≥ `LargeMotionDistance`（0.10 m） | `LargeMotionHitRate` |

> 注意：这 4 组的命中判定仍基于 **2.1 所述的手腕-平滑目标距离**，因此同样不能当作「对键精度」。

---

## 5. 视觉抽查计数（已实现，非百分比）

| 字段 | 判定 |
|---|---|
| `CrossedHandFrames` | `HandPoseValidator.AreHandsCrossed`：左手 X 小于右手（角色面向 -Z 时左手在 +X） |
| `ThumbOrientationIssues` | 拇指轴与掌心前向夹角 > `ThumbOrientationMaxDegrees`（75°） |
| `PalmPenetrationFrames` | 手掌/手腕低于键面或深入琴体（`HandPoseValidator` 钳制触发） |
| `HoveringFrames` | 最近指尖高于键面 > `HoverHeight`（0.045 m） |

---

## 6. 标注曲目分组（待建立）

评估文档要求为固定曲目建立标注集合。当前仓库只有 `Assets/StreamingAssets/Songs/demo.mid`（7.5 s），
**不足以覆盖**下列场景，属本轮未完成项：

- [ ] 单音级进（慢速）
- [ ] 跨八度跳跃
- [ ] 黑白键混合
- [ ] 三音 / 四音和弦
- [ ] 左右手交叉
- [ ] 快速重复音
- [ ] 低音区（A0–C3）/ 高音区（C6–C8）

---

## 7. 阶段目标（工程门槛，非行业标准）

| 指标 | 当前记录（旧口径） | 第一阶段目标 | 说明 |
|---|---:|---:|---|
| 播放期平均指尖误差 | 38–41 mm（最近指尖口径） | ≤ 25 mm（**改用被分配指尖口径后重测**） | 换口径会变大，需先建新基线 |
| 换位过渡峰值 | 260–280 mm（指尖） | ≤ 100 mm（`TransitionPeakError`） | 需先区分目标跳变与指尖偏离 |
| 手腕命中率 | 93.6% | 保持 ≥ 93%（跟随达成率） | 不能替代对键精度 |
| 单帧手腕位移 | ≤ 35 mm | 保持 ≤ 35 mm | 不放宽 `MaxStepPerFrame` |
| 姿态穿模告警 | 0 | 0 | 需配合近景人工审核 |

---

## 8. 复现方式

```bash
cd "D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d"

# 动作指标与对应关系（EditMode + PlayMode 子集）
"D:/Unity/Unity 2022.3.22f1c1/Editor/Unity.exe" -batchmode -nographics \
  -projectPath "%CD%" -runTests -testPlatform PlayMode \
  -testFilter "HandMotionTests|HandFingerCorrespondenceTests" \
  -testResults "_verify_out/hand-align.xml" -logFile "_verify_out/hand-align.log"

# 指标行来自 HandMotionReport.ToLogString()，日志中搜索 "[MOTION]"
```

> **下一步（按评估文档执行顺序）**：先实现 `AssignedFingertipError` 与 `WristToKeyError` 两个字段并重建基线，
> 再讨论动作优化；否则新旧数据不可比。
