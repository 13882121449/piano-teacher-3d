# 总规划：手部贴键重做 + 项目后续优化（2026-11-05）

> 状态：**规划稿，待排期**。上一轮（v4 全方位升级）已收尾：EditMode 485/485、PlayMode 130 过 / 0 败 / 4 跳、静态门禁 4 条 0 违规。
> 本文回答两个问题：① 手部为什么还是“悬在键面上”；② 除手部之外，接下来按什么顺序做什么。

---

## 0. 摘要

| 维度 | 现状 | 判断 |
|---|---|---|
| 手部贴键 | 键面距离 P50 3.6–9.1 mm、P95 10.2–17.7 mm，悬空帧最多 332/曲 | **仍然是画面里最扎眼的问题**，且当前门禁量错了东西 |
| 手型姿态 | 三角色静止 C 形手、掌心朝下、左右对称 | 已达可交付，不需再动 |
| 判定口径 | `HitRate` 用 35 mm 容差的三维距离，键面距离只进日志 | **必须先改口径**，否则改完也说不清有没有变好 |
| 其余模块 | 渲染 / UI / 测试 / 静态门禁较稳 | 可以并行做，但优先级低于手部 |

一句话结论：**手部不是“再调几个参数”能解决的，缺的是两件事——量对的东西（键面距离与悬空帧进门禁）、以及让手指能真正落到键面的求解方式（现在只有“绕固定轴弯曲”一个自由度）。**

---

## 1. 为什么仍然不贴键：根因链

### 1.1 三个错位

```text
真实目标：指腹贴住键面（Y 向误差 ≤ 2–3 mm，且不穿键）
   ↓
现有门禁：手腕水平距离 ≤ 0.09 m（不看高度）        ← 错位 1
   ↓
次级门禁：指尖三维距离 ≤ 0.035 m（比白键宽 24 mm 还大） ← 错位 2
   ↓
结果：HitRate 99% 报“达标”，屏幕上 P95 仍悬空 10–18 mm
```

代码位置：`Assets/Scripts/Avatar/HandIKController.cs:195`（`FingertipErrorTolerance = 0.035f`）、
`Assets/Tests/PlayMode/HandBenchmarkTests.cs:119-125`（键面 P50/P95 只打日志）、
`:156-159`（分组门禁只看 HitRate）。

### 1.2 求解器只有“绕固定轴弯曲”一个自由度

`Assets/Scripts/Avatar/FingerPoseController.cs:633` 的 `SolveJointAngles` 是 CCD：
逐关节绕 `JointAxes.WorldCurlAxis(joint, j)` 旋转，角度被钳在 `[0, Max*Degrees]`。

这带来两个结构性限制：

1. **无法改变弯曲平面**。手指只能在自己的矢状面里卷；目标若在平面外侧向偏离，无论怎么卷都差一个固定横向距离——这正是“指尖悬在键面上方”的直接来源。
2. **末端是骨骼末端，不是指腹**。目标点由 `HandIKController.cs:1059-1065` 生成：`touch.Press + FingertipPadRadius(+黑键抬升)`，是“把骨骼末端抬到指腹高度”，但手指绕轴卷到位时指腹**朝向**并不保证贴着键面。

### 1.3 腕/掌位置是启发式，不是解出来的

`Assets/Scripts/Avatar/HandIKController.cs:1031` 的 `UpdateFingertipTargets` 按指法分配给出目标，腕部则由 `UpdateHand`（同文件 759 起）用一个“目标点 + 弹簧”驱动。
两者不闭环：腕部不知道“这根手指够不够得到”，手指也不知道“腕要不要挪一点”。
历史实验也印证了单点参数救不了：`Docs/perf/hand-baseline-2026-10-31.md` 记录腕高自适应（均值 / 极值+中位两版）在噪声内无收益，已默认关闭。

### 1.4 键下沉与指尖是两条独立动画

`Assets/Scripts/Piano/PianoKeyAnimator.cs:19` 的 `_pressAngle = 3f` 让琴键下沉，指尖则由手指 IK 驱动，二者只共享时间轴、不共享接触约束 → 视觉上“键在动、手指在键上方飘”。

---

## 2. 手部方案（H1–H5）

> 顺序即依赖顺序：**H0 先立口径**（否则后面无法验收），再 H1/H2 改求解，H3 改掌位，H4 做接触约束，H5 收尾评审。

### H0 口径重建（0.5 人日，P0）

| 项 | 做法 |
|---|---|
| 新主指标 | `PadToKeySurfaceMm`：指腹中心到**键顶面**的有向距离（负值 = 压入），逐帧逐指采样，报 P50 / P95 / Max |
| 悬空定义 | `PadToKeySurfaceMm > 6` 且该指有分配音 → 计一帧悬空 |
| 穿键定义 | `< -4` → 计一帧穿键（现有 `FingerPenetrationFrames` 的语义改为“指腹压入”，不是骨骼末端） |
| 门禁 | `HandBenchmarkTests` 新增断言：P50 ≤ 3 mm、P95 ≤ 8 mm、悬空帧 ≤ 总帧数 2%、穿键帧 = 0；`FingertipErrorTolerance` 30 mm → **10 mm** |
| 产出 | 重跑 `Docs/perf/hand-baseline-2026-11-XX.md`，三首基准曲 A/B 表 |

**风险**：口径收紧后会大面积“变红”。这正是要的——先把红灯挂上，再逐项清零。

### H0 结论（2026-11-05 落地）

已完成：门禁 + 诊断工具 + 一次假说证伪。原计划的「指腹中心到键顶面有向距离」指标**早已存在**
（`HandMotionMetrics.FingertipSurfaceDistance`，别名 `KeyTopDistance`），缺的是门禁，因此 H0 实际成本远低于估的 0.5 人日。

**新增门禁** `HandBenchmarkTests.Bench_KeySurfaceDistance_NotAboveRecordedBaseline`：
三首基准曲逐曲断言 `KeyTopDistance.P50 ≤ 10 mm` / `P95 ≤ 18.5 mm`（2026-10-31 上包络 + 1 mm），
同时打印悬空率与穿键帧。作用是**防回退**——方案目标（P50 ≤ 3 / P95 ≤ 8 / 悬空 ≤ 2% / 穿键 0）要等 H2+H3 之后才能收紧。

**实测（2026-11-05，本机 -batchmode）**

| 曲目 | P50 | P95 | 悬空率 | 穿键帧 | 命中率 |
|---|---|---|---|---|---|
| bench_black_keys | +1.8 mm | 9.9 mm | 1.6% | 174 | 99.6% |
| bench_chords | −2.6 mm | 5.7 mm | 50.0% | 241 | 100% |
| bench_fast_scale | −4.9 mm | 6.8 mm | 15.8% | 183 | 95.7% |

注：`P50` 为负说明**多数帧其实略微压进键面**，真正的观感问题是「悬空帧集中在和弦曲的换位期」，
与之前「一直浮在键上」的印象不同——这条口径差正是 H0 要修的。

**诊断用例** `Assets/Tests/PlayMode/HandContactDiagnosticTests.cs`（`[Explicit]`，只在手动过滤时跑）：
播放 `bench_chords`，运动中采 5 帧 + 暂停后再等 0.8 s 采 1 帧，逐指打印
`above / horiz / up / right / d / rootDist / reach`，用于区分「跟随滞后」「够不到」「解不出」。

**三个结论（静置帧）**

1. **拇指系统性够不到**：L0 `rootDist=138.9 mm` / `reach=68.6 mm`，R0 `105.5 / 68.6` ——目标在
   1.5–2 倍指长之外。这是**拇指基准点与指法分配**的问题，不是铰链平面问题，靠 IK 无法修。
2. **黑键目标解不出**：R2（pitch 71，黑键）静置后仍 `above=+20.6 mm`；同帧 R4（白键）`−0.1 mm`。
   黑键比白键高 22 mm，而 `BlackKeyTargetLift` 默认为 0，指腹没有抬升补偿。
3. **滞后只是小头**：R4 运动帧 11.5 mm → 静置 −0.1 mm，滞后约 8 mm，不足以解释 20 mm 级残差。

**被证伪的假说**：曾怀疑「手掌滚转使铰链平面偏离竖直面，导致目标在面外」。
改法 `FingerPoseController.AimPlaneUpBlend`（`aimUp = Slerp(palmUp, Vector3.up, blend)`），
A/B 实测 0 → 1 后 R2 残差 20.6 → 20.9 mm，**无收益**；参数保留、默认 `0f`（不改既有行为），注释里记下这组数据。
关节限幅也不是瓶颈（`MaxMcp 82° / MaxPip 95° / MaxDip 62°`）。

**因此下一步优先级调整为**：H4（黑键抬升，0.5 人日，改动最小、直接消掉 20 mm 那一类） →
H3（掌位闭环，修拇指够不到）→ H2（解析 IK）→ H1（弯曲平面，降级为可选）。
`FingertipErrorTolerance` 35 → 10 mm **暂缓**：它会整体改变 HitRate 口径，必须与基线重采同一次提交。

### H0 追加：再证伪两条「便宜杠杆」（2026-11-05 晚）

H0 的结论指向「手掌位置解不出来」，于是先试了两个成本最低的手腕侧杠杆，**都无效**：

| 尝试 | 做法 | 实测（bench_chords 静置帧） | 结论 |
|---|---|---|---|
| 掌位竖直修正 | `UpdatePalmCorrection` 原本 `want.y = 0f`（只修 XZ），放开为 ±22 mm | R2 20.9 → **24.6 mm**、R4 −0.3 → **+5.9 mm** | 更差。掌位一抬，指骨局部解跟着重算，两者互相打架 |
| 黑键抬手 | 目标键为黑键时抬腕 11 mm（半键高） | R2 20.9 → **21.0 mm** | 无效 |

加上前面两条，一共**四个假说被实测否掉**：铰链平面（20.9）、黑键指尖目标抬升（21.1）、掌位竖直修正（24.6，更差）、黑键抬手（21.0）。

**这四条共同说明**：问题不在「往哪儿推多少」这种比例控制层面，而在
**「先有手型、再反推掌位」的顺序反了**。现在每帧是：键位 → 腕目标（启发式）→ 手臂 IK → 逐指 CCD。
腕目标一旦不准，手指只能在不合理的手掌位姿里拼命弯曲，任何事后微调都会被指骨解算抵消。

因此 H3（掌位闭环）必须按**解析式**做，而不是增益式：对当前指法分组，先求「让所有目标都落在各自可达球面内」
的手掌六自由度最小二乘解（位置 3 + 朝向里的 2 个有效自由度，滚转由按指约束），再固定手掌解指骨。
增益式闭环（本次实测）在闭环内会与 CCD 形成正反馈，越修越偏。

`AimPlaneUpBlend`（默认 0）、`BlackKeyHandLift`（默认 0）、`MaxPalmCorrectionY`（已删除）三个开关保留与否：
前两个留作后续 H1/H4 的试验开关并在注释里写明实测数据，`MaxPalmCorrectionY` 随实验一起回退。

### H1 手指弯曲平面可调（1.0 人日，P0）

给 MCP 增加一个**外展/内收自由度**（绕掌心法线旋转 0–12°），让“手指的弯曲平面”能穿过目标点：

- 在 `FingerPoseController.SolveJointAngles` 之前，先算 `planeYaw = SignedAngle(当前平面法线, 目标方向, 掌心法线)`，钳在 ±`MaxAbductionDegrees`（默认 10°），写到 MCP 的 yaw；
- 关节轴向仍用 `JointAxes.WorldCurlAxis`，但轴随 yaw 一起转，保证后面仍是平面内 CCD；
- 指尖末端改用**指腹中心**（骨骼末端沿指尖方向偏移 `PadRadius`），与 H0 的指标同一位置。

**验收**：H0 五项指标中 P50/P95 至少降 30%；三角色静止手型不退化。

### H2 解析 IK 取代 CCD（1.5 人日，P1）

CCD 的收敛靠迭代，容易在“目标略远”时把手指摊平（看起来像伸手）。换成两段解析：

- 三关节合并为“近节 + 其余”两段，用余弦定理直接解 MCP/PIP，DIP 按 `DipPipRatio` 耦合；
- 无解（目标距离 > 臂长 × 0.98）时**不摊平**，改为“保持 C 形 + 报告不可达”，交给 H3 的掌位调整；
- 保留 `SolveJointAngles` 作为回退开关，便于 A/B。

**验收**：P95 再降；`Fingers_NearestFingertipIsTheAssignedFinger` 类断言不退化；快速音阶 P95 ≤ 12 mm。

### H3 掌位闭环（1.0 人日，P1）

把“腕在哪”从启发式改成**解一次**：

1. 对每个已分配音，算出“指腹需要落在键面”的**可达区域**（以掌心为球心、指尖可达半径的壳层）；
2. 取所有目标的可行域交集，若有解则把腕目标移到交集中心（限幅：单帧 ≤ 8 mm，累计 ≤ 30 mm）；
3. 无解时按“优先级最低的音”放弃，并在指标里记一次 `UnreachableTarget`（不静默摊平手指）。

**验收**：悬空帧占比 ≤ 2%；`FingertipReachAdjustment` 等“靠偏移骗过可达性”的参数可以删掉。

### H3 第一版（解析掌位最小二乘）实测：中性偏负，默认关闭（2026-11-06）

按上一节的结论，把掌位改成**闭式最小二乘**而不是增益闭环：对每根按住的指头取 <c>u</c> = 指向目标的单位向量、
<c>e</c> = |target − mcp| − 指长 × 0.92（只取 &gt; 0 的「够不到」量），解 <c>min ‖Aδ − e‖²</c> + Tikhonov 阻尼，
再把 δ 加到腕目标上。实现见 `HandIKController.SolvePalmReachOffset`（`PalmReachSolve`）。

| 指标 | 关闭（基线） | 开启 | 判定 |
|---|---|---|---|
| bench_black_keys P95 | 9.9 mm | **6.1 mm** | 改善 |
| bench_fast_scale P95 | 6.8 mm | **2.8 mm** | 改善 |
| bench_chords P95 | 5.7 mm | 6.7 mm | 变差 |
| 三曲 P50 | +1.8 / −2.6 / −4.9 | −1.6 / −5.5 / −6.2 | 整体压得更深 |
| 和弦穿键帧 | 241 | 245 | 略差 |

即「**用穿透换悬空**」，不是干净收益 → `PalmReachSolve` 默认 `false`，实现保留。

两条附带发现：

1. **必须排除「远到没救」的目标**。不加限制时，拇指 70 mm 的亏空会把整只手拽向拇指，
   其余手指反而更差（R2 21.0 → 34.8 mm）；把「亏空 &gt; 手掌可移动范围（60 mm）」的指头排除后才得到上表。
   这也说明**拇指的问题是分配问题**：`HeuristicFingeringProvider` 完全按旋律轮廓分配，没有任何几何可达性约束。
2. 排除后仍有 40 mm 级亏空（R0 105 → 89 mm），说明单靠 ±60 mm 掌位移动补不上，
   必须先让**目标本身**落在可达范围内。

**因此 H3b（下一步）**：把可达性约束前移到指法分配——`HeuristicFingeringProvider` 只拿到音高与旋律，
没有骨骼信息，所以约束要加在 `HandIKController.UpdateFingertipTargets` 的分配后校验里：
对每根被分配的指头算 |target − mcp| / 指长，超出 1.15 倍就尝试与同手空闲指头交换，
仍不行则该指头不设目标（保持自然手型），并计入既有的 `UnreachableTargetSamples`。
这样「够不到」在**数据层**被消掉，而不是靠掌位/指骨硬解。

### H4 接触约束与键下沉联动（0.5 人日，P2）

- 指腹压住键面后，指尖跟随琴键下沉（`PianoKeyAnimator` 的当前角度 × 键杠杆），而不是各走各的；
- 加 3 mm 迟滞，避免起音/收声瞬间抖；
- 黑键抬升 `BlackKeyTargetLift` 由“固定 3 mm”改为“按黑键实际键高 10 mm − 白键高”推导。

**验收**：逐帧截图里“键动、手指不动”的帧为 0；`bench_black_keys` 穿键帧 = 0。

### H5 视觉评审与收口（0.5 人日，P2）

- 新增特写 3 张（静止 / 单音 / 和弦），进 `Docs/screenshots` 基线；
- 人工评分表 5 项（指腹接触、手指弧度、腕高、左右对称、无明显穿模），每项 1–5 分，≤ 3 分即打回；
- 更新 `Docs/hand-alignment.md` 与 `Docs/animation-pipeline.md` §6.4 的指标定义。

**手部总投入**：约 5 人日；预期把 P95 从 10–18 mm 降到 ≤ 8 mm、悬空帧清零。

---

## 3. 手部以外的优化（按性价比排序）

### P1 音频时序与采样率（1 人日）

- `Assets/Scripts/Piano/PianoAudioSynth.cs:57` 把采样率写死 `44100`，而 `AudioSettings.outputSampleRate` 在多数 Windows 设备是 48000 → 音高与时长有系统性偏差；
- 改成初始化时读 `AudioSettings.outputSampleRate`，并在不一致时重建 `AudioClip`；
- 起音改用 `AudioSource.PlayScheduled(dspTime + 2 帧)`，把 30 fps 下的 ±16 ms 起音抖动压到 ±5 ms 内；
- 验收：录一段 10 s 输出，做起始点检测，与 MIDI 时间戳的 P95 偏差 ≤ 10 ms。

### P2 性能预算与门禁（1 人日）

- 现在有“Update 路径零分配”的静态门禁，但没有**帧时间预算**；
- 用 `ProfilerRecorder` 采 `Main Thread` / `Render Thread` / `GC.Alloc`，跑 30 分钟 soak，写 `Docs/perf/frame-budget.md`（目标：1080p p95 ≤ 16.6 ms）；
- 把“p95 超标”变成可失败的测试（阈值放宽 20% 防抖动）。

### P3 练习闭环（3–5 人日，产品价值最高）

现状是“只能看，不能练”。最小闭环：

1. **等待模式**：时间轴在该音处停住，直到玩家按下正确键（目前**没有**键盘演奏输入模块，只有 `LessonControlsUI` 里的快捷键读取，需要新建一个「键位 → MIDI 音高」的输入层）；
2. **即时反馈**：错音标红 + 轻微音效，正确音标绿；
3. **段落循环 + 变速**：0.5×–1.0×，循环区间选段；
4. **结束评分**：准确率 / 漏音 / 多余音 / 节奏偏差四项，存本地记录。

### P4 健壮性与可维护性（1 人日）

- `static_checks` 报告的 99 处“无出口 catch”分批收敛：先给 `CharacterRegistry` / `CharacterSwitchService` 这两处高频路径加 `ErrorReporter`，其余按模块清；
- 15 处只记 Warning 的降级点补上用户可见提示（或明确注释“可接受”）。

### P5 内容与文档（1–2 人日）

- 曲库只有 `demo.mid` 与 3 首基准曲，主菜单“曲库（1 首）”很空 → 补 5–8 首公版练习曲（拜厄 / 车尔尼片段），带难度标签；
- `Docs/` 里 `optimization-2026-10-19.md`、`optimization-report.md` 等历史文档已与现状脱节，做一次“现状索引”页，避免后来者读到过期结论。

---

## 4. 排期建议

| 阶段 | 内容 | 人日 | 依赖 |
|---|---|---|---|
| 阶段 A | H0 口径 + 基线重测 | 0.5 | — |
| 阶段 B | H1 平面可调 → H2 解析 IK | 2.5 | A |
| 阶段 C | H3 掌位闭环 + H4 接触联动 | 1.5 | B |
| 阶段 D | H5 视觉评审 + 文档收口 | 0.5 | C |
| 阶段 E | P1 音频时序（可并行） | 1.0 | — |
| 阶段 F | P2 性能门禁（可并行） | 1.0 | — |
| 阶段 G | P3 练习闭环 | 3–5 | A–D 完成后 |

阶段 A–D 是“手部专项”，完成后应立即重出三张特写与一段录屏做人工确认；阶段 E/F 与手部无耦合，可穿插。

---

## 5. 不做的事（明确边界）

- **不引入生成式 3D 资产**（Tripo / Blender 自动绑定）：手部问题是求解与控制问题，不是网格精度问题；换模型不会让指腹落到键面。
- **不改 Humanoid 骨架结构**：Rocketbox 骨架是标准 Humanoid，改骨架会破坏现有 40+ 用例与三个角色的复用。
- **不在本轮做多指独立力反馈**：没有力反馈硬件，做到“指腹贴键 + 键下沉联动”即可。
- **不为手部单独引入物理引擎**：键与手的接触是确定的动画约束，用 PhysX 只会带来不稳定性与性能开销。

---

## 6. 验收清单（可复跑）

```powershell
# 1) 手部专项指标（必须全绿，阈值见 H0）
& $unity -batchmode -projectPath $proj -runTests -testPlatform PlayMode `
         -testFilter HandBenchmark -testResults "D:\DeepSeek Project\tmp\hand.xml" -logFile "D:\DeepSeek Project\tmp\hand.log"

# 2) 手感回归（静止手型 / 指法对应 / 键下沉）
& $unity -batchmode -projectPath $proj -runTests -testPlatform PlayMode `
         -testFilter "RocketboxRestPose|Finger|PianoKey" -testResults "D:\DeepSeek Project\tmp\hand2.xml"

# 3) 全量回归 + 静态门禁
& $unity -batchmode -projectPath $proj -runTests -testPlatform EditMode -testResults "D:\DeepSeek Project\tmp\edit.xml"
& $unity -batchmode -projectPath $proj -runTests -testPlatform PlayMode -testResults "D:\DeepSeek Project\tmp\play.xml"
python Tools/static_checks.py
```

> 注意：跑测试**不要加 `-quit`**，否则 Unity 导入完就退出、用例根本没执行。

## 暂停点快照（2026-11-06 00:50）

### 本轮已提交

| 提交 | 内容 |
|---|---|
| `9e2b6c2` | H0 键面距离门禁 + 指尖接触诊断用例；证伪铰链平面 / 黑键抬升两个假说 |
| `4126643` | 四条假说证伪记录 |
| `703479a` | H3 掌位解析解 `SolvePalmReachOffset`（默认关闭：用穿透换悬空，不划算） |
| 本轮待提交 | 腕部朝向改用「手背朝上」扭转约束 + 三角色腕高重标定 |

### 本轮视觉上的真进展

腕部朝向的老实现用「拇指朝内」做扭转约束（此前记录为能避免左右手镜像），但它并不唯一：拇指朝内时手掌仍可侧立。实测右手被拧成 90°，特写里五指横向伸出去；把扭转硬钳到 ±25° 又翻成掌心朝镜头。改为按 `IHandOrientationRig.BackNormalLocal`（手背法线，无左右手差异）对齐世界 +Y 后，两只手都自然掌心朝下、左右对称。

同时按新朝向重标定 `RocketboxCharacterView.HandHeightOffsetFor`：female −0.022 / male +0.016 / child −0.029（旧值 0.016 / 0.008 / 0.001 是在侧立朝向下标的）。三只手的静止食指指尖落到 +2.3 / +2.1 / +4.8 mm。

### 验证到哪一步

- `RocketboxRestPoseTests`：10/10
- `CaptureLessonViews`：5/5（V17/V18 已重采）
- `EditMode`：485/485（本轮改动不涉 UI，沿用上一轮结果）
- **PlayMode 全量未在本轮最终代码上重跑**（改朝向后的全量曾报 13 败，全部是三条静止手型用例，最终代码已改用例通过，但整包回归仍是待办）

### 下次接手第一件事

1. 跑一次全量 `-runTests -testPlatform PlayMode`，确认 130+ 通过 / 0 失败。
2. 若绿：提交本轮改动（`HandIKController.ApplyWristOrientation` 的 back-normal 扭转 + `IdleHandDrop` 字段默认 0 + 三角色腕高表 + 重采的截图）。
3. 再继续 H1（MCP 外展）/ H2（解析 IK）/ H4（黑键抬升的真实键高推导）。

### 两条已知遗留

- `IdleHandDrop`（空闲时整手下沉 34 mm）实现后默认设为 0：男性指腹会压到 −39.7 mm（他腕高本就低），说明常量下沉不够，应改成「按食指指尖高度闭环」。
- 演奏帧的 P50/P95 键面距离仍在门禁内但不算达标（chords 悬空率 50%），要真正解决需要 H1/H2。

