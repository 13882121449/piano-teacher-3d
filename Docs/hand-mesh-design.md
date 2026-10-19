# 手部网格与关节设计（Hand Mesh & Joint Contract）

> 对应评估文档 **附录 A**（手部重绘与姿态自然化专项）与正文 **P0-02 / P1-05**。
> 本文描述**代码当前实际实现的契约**（附文件与方法），不是待办方案；
> 尚未达成的部分单独列在第 9 节「已知限制」。

---

## 1. 目标与边界

本轮要解决的不是「把手画得更细」，而是三件事同时成立：

1. **形状**：手掌不是几个椭球的并集，手指不是等粗圆柱；
2. **关节**：每根手指的 MCP / PIP / DIP 能**独立旋转**，指尖能形成弧线；
3. **落点**：按压时指尖确实落在目标琴键的按压区，而不是「手腕到了就算命中」。

约束（不得违反）：

- 保留**唯一时间轴**与统一音符事件源：姿态只消费 `MidiEventScheduler` / `LessonTimeline` 产生的事件与当前时间；
- 保留**左右手约定**：低音 ↔ 左手 ↔ 世界 +X ↔ 画面左侧；
- 保留**单帧位移硬约束** `MaxStepPerFrame = 0.035 m`：不通过放宽它来「追上」琴键；
- 不引入任何外部模型/贴图资产，构建仍是零资源依赖。

---

## 2. 手部局部坐标约定

所有手部网格与关节都在**同一套手部局部坐标**下构建（`HandMeshFactory` 与 `AvatarManager.BuildHand` 共用）：

| 轴 | 含义 | 说明 |
|---|---|---|
| `+Z` | 手指伸出方向 | 指向钢琴（角色整体绕 Y 旋转 180°，因此局部 +Z = 世界 −Z） |
| `+Y` | 手背方向 | 局部 +Y = 世界 +Y（旋转只绕 Y 轴） |
| `+X · Side` | 拇指侧 | `Side = +1` 为右手（局部 +X 是拇指侧），`Side = −1` 为左手 |

> **注意**：左右手是**镜像**（`KnucklePositions.x * side`、指链偏航 `side * yaw`），不是旋转。
> 因此不能把同一块掌部网格直接复用到另一只手（会变成反手）；手指网格因截面近似轴对称，可共用同一形状参数。

---

## 3. 比例参数（`HandMetrics`）

位置：`Assets/Scripts/Avatar/AvatarDefinition.cs`。**同一份数据同时驱动网格、关节长度、姿态解算与校验**，避免「网格一套手长、IK 另一套手长」。

| 字段 | 默认值 | 用途 |
|---|---|---|
| `Scale` | 1 | 手部整体缩放（叠加在体型系数之外） |
| `PalmLength` | 0.058 m | 腕横纹 → 掌指关节；决定掌部网格长度与指根位置（×0.98） |
| `PalmWidth` | 0.076 m | ≈ 3.2 个白键宽（白键 24 mm） |
| `PalmThickness` | 0.030 m | 掌厚（掌背略薄于掌心） |
| `WristRadius` | 0.026 m | 掌根收窄与腕部接缝 |
| `FingerLengths[5]` | 0.068 / 0.082 / 0.090 / 0.084 / 0.064 | 拇指→小指总长（中指最长、小指最短） |
| `FingerRadii[5]` | 0.0115 / 0.0096 / 0.0099 / 0.0091 / 0.0080 | 近节根部半径 |
| `FingerSegmentRatios[5][3]` | {0.42, 0.33, 0.25} | 近节/中节/末节长度占比，之和须 ≈ 1 |
| `ThumbAbduction` | 57° | 拇指外展：决定虎口宽度 |
| `ThumbOpposition` | 14° | 拇指对掌内旋（配置项，供后续接入更细的手型） |
| `WristPitch` | 8° | 手腕静息俯仰（掌心略朝键面） |
| `PalmKeyPlaneOffset` | 0 | 掌心到键面的额外高度补偿 |
| `FingertipReachAdjustment` | 0 | 指尖目标高度校准（正值抬高） |

`HandMetrics.Validate()` 会检查掌长/掌宽/掌厚/指长/指半径范围，以及**三段占比之和是否为 1**；失败时 `AvatarDefinition.Validate` 返回问题描述，`AvatarManager` 回退默认角色（不影响曲目播放）。

### 3.1 三个角色的实际取值

`AvatarManager.MakeDefinition` 调用 `AvatarDefinition.ApplyHandStyle(...)`：

| 角色 | 身高 | 掌宽 | 掌厚 | 指长 | 指粗 | 腕俯仰 |
|---|---:|---:|---:|---:|---:|---:|
| 写实女性 | 1.38 m | ×0.97 | ×0.96 | ×1.00 | ×0.95 | 8° |
| 写实男性 | 1.52 m | ×1.06 | ×1.05 | ×1.04 | ×1.06 | 8° |
| 卡通角色 | 1.26 m | ×1.12 | ×1.16 | ×0.92 | ×1.14 | 11° |

> 比例只影响**外观**；MIDI 音高 → 琴键世界坐标的映射不受影响（`PianoKeyMap` 是唯一来源）。

---

## 4. 网格生成规则（`HandMeshFactory`）

位置：`Assets/Scripts/Avatar/HandMeshFactory.cs`。全部为**纯函数**：无场景依赖、无随机数、无时间，同样输入必然得到同样网格，可在 EditMode 单测。

### 4.1 掌部：沿掌长的连续截面放样

`BuildPalm(HandMeshDefinition)` 生成**一块**网格（不再是 `PalmRoot` / `PalmCore` / `KnuckleRidge` / `Thenar` / `Hypothenar` 五块椭球）：

- 沿 `+Z` 从 `−WristRadius × 0.55`（回退进手臂胶囊，藏住接缝）到 `PalmLength × 1.02`；
- 截面为**超椭圆**（指数 3.2，即 `x = sign(cos)·|cos|^(2/3.2)`），因此掌背/掌心是略平的圆角矩形，而不是椭球；
- 宽度剖面：腕端 0.74 → 掌长 70% 处 1.00 → 掌指关节 0.94；
- 厚度剖面：腕端 0.86 → 掌长 40% 处 1.00 → 掌指 0.80；
- **拇指侧半宽 ×1.06、小指侧 ×0.94**，形成轻微的掌骨扇形；
- **大鱼际/小鱼际**只在掌侧（`y < 0`）叠加有界隆起：
  大鱼际 `0.85 × Gaussian(t, 0.30, 0.26)`，小鱼际 `0.50 × Gaussian(t, 0.44, 0.24)`，
  同时把该侧半宽最多推开 5%、掌侧最多压低 22% 的半厚 —— 于是「拇指根隆起」是轮廓的一部分，而不是贴一个球；
- 两端用三角扇封口（腕端藏在手臂内，掌指端藏在指根下）。

### 4.2 手指：逐段渐缩 + 圆指尖

`BuildFingerSegment(d, finger, segment)`：

- 局部原点在**关节**处，沿 `+Z` 延伸一个段长；
- 起点向 `−Z` 回退 **7% 段长**，与上一段重叠，弯曲时不露缝；
- 半径按 `SegmentRadius(finger, seg) = FingerRadii[f] × 0.88^seg` 逐段递减，
  段内再从 `r0 → r0 × 0.88` 线性渐缩；
- `StartTaper`（前 18% 从 0.90 渐入）与 `JointBulge`（起始处 +5.5%）让指节有起伏而不是等粗圆柱；
- 截面为椭圆（横向 ×0.95），近节/中节两端封口；
- **末节**追加半球形指尖（`AddHemisphereCap`），因此 `bounds.max.z > 段长`，不是平切圆柱；
- 指甲（高精度档）是一块独立的浅色薄椭球，贴在末节**手背侧**，只作视觉提示，**不参与**指尖目标点。

### 4.3 精度档与顶点预算

| 档位 | 圆周顶点 | 掌部环数 | 指节环数 | 指甲 | 圆指尖 |
|---|---:|---:|---:|---|---|
| Low | 8 | 6 | 3 | 否 | 否 |
| Medium（默认） | 10 | 9 | 5 | 否 | 是 |
| High | 14 | 12 | 7 | 是 | 是 |

单手掌部 ≈ 90–170 顶点，单指节 ≈ 40–100 顶点，全部手指约 1–2 k 顶点/手；三个角色各两只手，总量在万级以内，
远低于「用球体拼接」时的 GameObject / DrawCall 数量（掌部从 5 个 Renderer 降为 1 个）。

### 4.4 资源生命周期

- 网格在 `AvatarManager.AttachMesh` 中登记到 `_runtimeMeshes`，`OnDestroy` 统一 `Destroy`；
- 材质仍由 `AvatarManager` 每次构建角色时创建（与既有实现一致），使用 `sharedMaterial` 共享给同角色的所有部件，不给每根手指实例化材质。

---

## 5. 骨骼映射契约

### 5.1 Humanoid 骨架（仅坐标，无 Renderer）

`AvatarRigBuilder.Build` 为每指构建 **三个节点**：

```text
LeftHand
└── LeftIndexProximal        （MCP，直接挂在 Hand 下）
    └── LeftIndexIntermediate（PIP，位于近节 96% 处）
        └── LeftIndexDistal   （DIP，位于中节 96% 处）
```

- 命名严格使用 Unity Humanoid 标准手指骨骼名（`LeftThumbProximal` … `RightLittleDistal`），
  因此 `Avatar.isValid` / `isHuman` 仍为 true（`HumanoidRigTests` + `HandJointPoseTests` 双回归）；
- `Skeleton.LeftFingers[f]` 仍指向 **Proximal**，`Skeleton.TryGetFingerJoints(left, f, out mcp, out pip, out dip)`
  返回三关节 —— 既有调用方（`HandFingerCorrespondenceTests`、`HumanoidSceneTests`）无需改动；
- 关节间距取段长的 **96%**（4% 重叠，与可见网格的 7% 回退配合）。

### 5.2 可见手部（被 IK 驱动）

可见手是独立的一棵树（骨架只是坐标规范，见 `Docs/architecture.md`）：

```text
LeftHand（腕，位置由 HandIKController 每帧写）
├── Wrist            （球体，遮住手臂接缝）
├── PalmMesh         （HandMeshFactory.BuildPalm 的连续网格）
├── Palm             （空节点，位于掌心，供拇指朝向抽查 / 穿模判定）
└── Finger1..5       （指根，FingerPoseController 旋转它对准目标键）
    └── Chain        （解剖朝向：拇指外展 57°、四指扇形 −12°…+6°，不会被姿态控制器覆盖）
        └── Joint0   （MCP，网格段 0）
            └── Joint1（PIP，网格段 1）
                └── Joint2（DIP，网格段 2 + 指甲）
                    └── Tip（指尖接触点）
```

`IHandRig.Fingers[f]` 的语义保持不变（仍是「每指一个根节点」）；
新增可选接口 `IFingerJointRig : IFingertipRig` 暴露 `TryGetJoints(...)` 与 `GetFingerLength(...)`，
未实现该接口的手部会自动退回旧路径（单关节 + 固定按压角）。

---

## 6. 姿态解算（`FingerPoseController`）

### 6.1 关节分配

总弯曲 `C` 按权重分配到三个关节，并各自限幅：

| 关节 | 权重 | 上限 |
|---|---:|---:|
| MCP | 0.46 | 82° |
| PIP | 0.34 | 95° |
| DIP | 0.20 | 62° |

超限时钳制并累加 `JointLimitClampCount`（经 `IHandJointIssueSource` 进入动作指标）。
角度以**静止姿态为基准**、用 `Quaternion.Slerp` 插值，不做欧拉角累加。

### 6.2 受限指尖 IK（铰链轴 CCD）

**分工**：指根朝向由手掌决定（只取目标方向的水平分量、俯角限制在 10°–36°），
MCP / PIP / DIP 三个铰链负责把指尖送到键面。这是手型自然与否的关键。

三种做法与实测对比（同一和弦，左手 48/52/55）：

| 方案 | 指尖误差 | 手型 | 结论 |
|---|---|---|---|
| 指根对准目标 + 固定按压角 | 15–40 mm | 指尖低于键面 2–4 cm；指根指向后下方 | 不可用 |
| 按距离二分总弯曲 + 均匀权重 | 49 mm（距离对、方向错） | 总弯曲 204°、卷成拳、指根翻转 137° | 不可用 |
| **指根限俯角 + 铰链轴 CCD** | **5–35 mm**（短指/拇指受臂展限制） | 近节水平偏下、中节为主、末节微调 | 采用 |

算法：

1. 指根方向 `flat` 取 `normalize(target − base)` 的水平分量，俯角取
   `clamp(asin(−dir.y), 10°, 36°)`，即「大致指向目标、但不会为了够键而翻转」；
2. 指根姿态用 `LookRotation(flat@俯角, 手掌上方向)` 构造，再左乘链节点解剖偏航的逆 → 拇指 57° 保留；
3. 关节角初值取自然弯曲（全直状态下若目标正好在手指射线上，旋转梯度为 0 会卡住）；
4. 从 DIP → PIP → MCP，每个关节只绕**自身局部 X 轴**屈伸：
   `delta = SignedAngle(ProjectOnPlane(tip − joint, axis), ProjectOnPlane(target − joint, axis), axis)`，
   `angle = clamp(angle + delta, 0, max)`；立即写回，供下一个关节使用；迭代 6 次。

关节角一律 `≥ 0`（不允许反关节），并分别受 `MaxMcpDegrees / MaxPipDegrees / MaxDipDegrees` 约束。
`distance > 可达长度` 且有关节到上限时累加 `JointLimitClampCount`（短指够不到长距离键位是物理事实，
记录而不掩饰）。

### 6.3 四阶段模型

| 阶段 | 触发条件 | 弯曲 |
|---|---|---|
| `Approach` | 有指尖目标但该音尚未按下（预置/即将到来） | IK 解的 62%，先形成手型 |
| `Press` | 音符已按下且 &lt; 0.12 s | IK 解（指尖落到键面） |
| `Hold` | 音符已按下且 ≥ 0.12 s | IK 解 + 2.5°（保持"按住"的视觉，不抖） |
| `Rest` | 无目标 | 26° 总弯曲（自然悬停，不僵直） |

阶段只由 `_pressed`（Note On/Off）与 `Time.deltaTime` 累积决定，**不自建计时器**；`GetPhase(left, finger)` 供测试与诊断读取。

---

## 7. 与 IK / 相机 / 指标的关系

- **手腕**（`HandIKController`）：位置仍受 `MaxStepPerFrame = 0.035 m`、换位抬升、姿态钳制约束；
  新增受限**俯仰**（角色 `HandMetrics.WristPitch`）与**滚转**（键盘两端最多 ±6°），只改姿态不改位置；
  `PalmKeyPlaneOffset` / `FingertipReachAdjustment` 分别补偿掌心高度与指尖目标高度。
- **指尖目标**：严格来自「手指 ↔ 音高」的共用分配（`FingerAssignmentService`），
  因此「弯的手指 == 指向该键的手指 == 按下该键的手指」；
- **相机**：不依赖手部网格形状，仍按 `CameraFraming` 的包围盒取景；手部特写俯角 60°、FOV 30–48°、距离 0.20–0.88 m 未变；
- **指标**（`HandMotionMetrics`）：见 `Docs/hand-alignment.md` 第 3 节的新增口径。

---

## 8. 验证方式

| 层次 | 用例 | 断言 |
|---|---|---|
| 几何（EditMode） | `HandMeshFactoryTests` | 网格非空、索引有效、顶点/法线有限、包围盒符合尺寸表、左右镜像、逐段渐缩、末节有圆指尖、无退化三角形、三档精度都能构建、三角色比例独立 |
| 关节（PlayMode） | `HandJointPoseTests.EveryFinger_HasThreeIndependentlyDrivenJoints` | 每手 5 指 × 3 关节，父子关系与长度正确 |
| 骨架（PlayMode） | `HumanoidRigTests` + `HandJointPoseTests.HumanoidSkeleton_ExposesThreeJointsPerFinger` | `Avatar.isValid` / `isHuman` 仍为 true，三关节齐全 |
| 姿态（PlayMode） | `HandJointPoseTests.PressedChord_CurlsAssignedFingersWithinLimits` | 和弦收敛后每指关节角在限位内、MCP 有可见弯曲、指尖误差 &lt; 45 mm |
| 静态（PlayMode） | `RestingHand_ReturnsToNaturalCurl` | 无音符时回到自然弯曲（3° &lt; MCP &lt; 40°） |
| 组合（PlayMode） | `ThreeAvatarsAndThreeViews_KeepHandRigValid` | 3 角色 × Full/Closeup/Free 下骨架与关节链始终有效 |
| 教学模式（PlayMode） | `TeachingMode_HidesHandsWithoutBreakingRig` | 隐藏手部不破坏关节链，切回立即恢复 |
| 对应关系（PlayMode） | `HandFingerCorrespondenceTests` | 最近指尖 == 被分配手指，误差 &lt; 40 mm |

命令：

```bash
python Tools/verify.py --edit-only     # 几何 + 逻辑
python Tools/verify.py                 # 编译 + EditMode + PlayMode
python Tools/verify.py --visual        # 追加视觉截图（需 GPU）
```

---

## 9. 已知限制（诚实清单）

1. **没有蒙皮权重**：每个指节是一块独立网格挂在关节下，弯曲时靠 7% 重叠量遮缝；
   极限弯曲（&gt; 70°）时指节背面仍可能出现轻微折痕，不如真正的 SkinnedMesh 平滑。
2. **指尖接触点近似**：`Tip` 取末节半球顶端的 82% 处，未按真实指腹做软接触；
   `FingertipReachAdjustment` 只能整体升降，不能逐指校准。
3. **拇指只有一维弯曲**：`ThumbOpposition` 目前是配置项而不是被姿态控制器驱动的自由度，
   对掌动作主要由链节点的固定偏航体现。
4. **没有手掌/琴键的连续碰撞检测**：`HandPoseValidator` 只做手腕包围范围与指尖法向距离判定，
   不做网格级穿透检测。
5. **手指插值是一阶的**：`Quaternion.Slerp` + 每帧目标重算，快速音阶下仍可能出现 1 帧的过冲（见 `Docs/hand-alignment.md` 的峰值口径）。
6. 视觉真实度上限仍受「无贴图、无骨骼动画剪辑、程序化网格」约束，与商业级手部资产有差距。
