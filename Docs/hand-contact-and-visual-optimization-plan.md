# 手部接触与视觉呈现优化方案

> 版本：2026-10-19 · 状态：待评审 · 适用范围：`piano-teacher-3d`（Unity 2022.3.22f1c1 / C#）
>
> **本文档的定位**：在既有 `Docs/optimization-report.md`（工程与性能）之外，专门处理
> **「手指是否真的落在琴键上」与「镜头里看起来是否像在弹琴」** 这两个尚未收敛的问题。
> 所有数字均标注采集口径；未实测的项目明确写「未测」，不做推算。

---

## 0. 摘要

| 项 | 结论 |
|---|---|
| 核心判断 | 问题**不是**模型精度不足，而是**验收口径与视觉目标错位**：手腕命中率 95% 的判定只看水平距离，掩盖了指尖垂直悬空 |
| 已量化证据 | 键面距离 `KeyTopDistance` 均值 6.3 mm / **P95 46.0 mm** / 峰值 116.3 mm；指尖误差 P95 72.8 mm，三项均未达方案 §8 门槛 |
| 主攻方向 | 用「指尖到键面的垂直距离」替换「手腕水平命中」作为接触门禁，再分级修复求解链路 |
| 生成式资产 | **不引入 Tripo / Blender**；保留 `IAvatarProvider` 作为未来后门，若启用优先 MakeHuman 而非生成式 |
| 总投入 | 约 **5.5 人日**，零现金支出；分 5 批，每批独立可回滚 |

---

## 1. 现状与问题分析

### 1.1 已有基线（实测，勿与目标混淆）

工程基线（`Docs/optimization-2026-10-19.md`）：

| 项 | 值 |
|---|---|
| 编译 | 0 错误 0 警告 |
| EditMode | 311/311 通过 |
| PlayMode | 90 通过 / 5 跳过 / 0 失败（共 95） |
| 性能 | 纯播放 p99 17 ms、1% low 58.4 FPS、GC 0.01 MB |
| 视觉基线 | 20 张（01–06 + V01–V14），基线版本 **2026-10-18 23:01**、渲染尺寸 **1101×656**（请求 1280×720）；本次在 640×480 下抽查复现了 §1.2 的 D 项，**正式重采须在 GUI 前台按原尺寸进行**（见风险 8） |

手部质量基线（`Docs/animation-pipeline.md` §6，口径：`demo.mid` 播放 **3 s**、
`-batchmode -nographics`、202 个指尖样本 / 366 个位移样本 / 44 个过渡帧）：

| 指标 | 均值 | P50 | P95 | P99 | 峰值 | 方案 §8 门槛 | 判定 |
|---|---:|---:|---:|---:|---:|---|---|
| 腕部误差 `WristTargetError`（XZ） | 13.7 mm | 1.4 | 86.1 | 207.3 | 257.1 | — | 跟随延迟，不代表对键精度 |
| 指尖误差 `FingertipTargetError`（3D） | **26.7 mm** | 20.3 | **72.8** | 188.5 | 259.4 | 均值 ≤25 / P95 ≤50 | **双项未达标** |
| 键面距离 `KeyTopDistance`（Y 差值） | **6.3 mm** | 1.2 | **46.0** | 105.0 | 116.3 | 未设门槛 | **无门禁** |
| 逐帧位移 `PerFrameDisplacement` | 5.3 mm/帧 | 1.3 | 29.5 | 35.0 | 35.0 | — | 受 `MaxStepPerFrame` 约束 |
| 过渡峰值 `TransitionPeakError` | 50.5 mm | 32.8 | 168.5 | 222.4 | 222.4 | ≤100 | **未达标（超 122 mm）** |

同期计数：手腕命中率 95%（192/202）、穿模 0、交叉手 0、拇指方向异常 0、
**悬空 17 帧**、速度异常 12、关节限位 1。

### 1.2 问题清单

| 编号 | 问题 | 证据 | 严重度 |
|---|---|---|---|
| **A** | **指标口径错位**：`HitRate` 只算手腕 XZ 水平距离（≤0.09 m），文档自述"与手腕高度无关"；`FingertipErrorTolerance` 是 35 mm 的 3D 距离，比白键宽 24 mm 还大 | `animation-pipeline.md` §6 定义行；`HandIKController.FingertipErrorTolerance = 0.035f` | **P0** |
| **B** | **键面距离无门禁**：`KeyTopDistance` P95 46 mm 已采集，但只进日志不进断言，UI/README 均不体现 | 同上；`README` §1.4「已验证状态」未列该项 | **P0** |
| **C** | **难帧退化**：和弦 / 黑键 / 大跨度 / 快速音阶四类未分组设门槛，整体 95% 掩盖了局部塌陷；换位峰值 222 mm 集中在 44 个过渡帧 | 基线表；文档自述"需先用标注集定位到具体换位点" | **P0** |
| **D** | **特写构图被躯干遮挡**：`CloseupMinDistance = 0.20 m` + 斜俯 60°，演奏者头部/肩部大块进入画面（基线 V01/V02/V09 三张均复现，本次 640×480 抽查同样复现） | `Docs/screenshots/`（2026-10-18 基线） | **P0** |
| **E** | **无音符时手型偏"张"**：`RestAngle = 18°` + `SpreadAngle = 4°` 呈扇形，接近"举手"而非弹琴的 C 形手；短音符上 `ChordApproachHeight` 的 blend 可能来不及走完，导致起音瞬间仍悬空 | 源码常量；V01（00:00 暂停）实拍 | **P1** |
| **F** | **真机视觉评审缺失**：现有基线部分在 `-nographics` 下采集，且无评分表；`completion-review.md` 明确"指尖贴键精度与真机视觉评审待做" | `completion-review.md` 第 329 行 | **P1** |

### 1.3 根因归纳

```text
真实目标：指尖贴住键面（Y 方向误差 ≤ 数 mm）
     ↓
现有门禁：手腕水平位置 ≤ 0.09 m（Y 完全不看）  ← 错位 1
     ↓
次级门禁：指尖 3D 距离 ≤ 0.035 m（比键宽还宽）  ← 错位 2
     ↓
结果：HitRate 95% 显示"达标"，屏幕上却是手悬空 4.6 cm（P95）
```

**一句话**：不是做不出来，是**没在量对的东西**。修复顺序必须是「先量对，再改对」——
这也与文档"本轮不调整 IK 参数，需先用标注集定位"的既有纪律一致。

### 1.4 关于引入生成式 3D 资产（Tripo / Blender）的结论

| 组件 | 判定 | 依据 |
|---|---|---|
| Tripo 生成手部 | **不采用** | ① 指缝粘连是生成式 3D 已知缺陷，Tripo 官方博客承认拇指鞍状关节需手工精修；② quad 拓扑不保证沿关节环形布线，而本项目需 MCP/PIP/DIP 三关节独立旋转；③ 换模型会使 `HandMetrics`（掌宽 76 mm ≈ 3.2 白键）这一"网格/关节/IK/校验"的唯一真源失效，18 条手部几何测试 + 7 条三关节姿态测试需全部重标定 |
| Tripo 生成钢琴 | **不采用** | 88 键是规则参数化几何，`PianoMeshFactory`（335 行）+ `PianoKeyMap` 直接驱动按键与落点；生成模型键宽对不上会崩掉整套 IK 落点 |
| Tripo 生成装饰道具 | **暂不采用** | 教学零价值、增加 draw call（本轮刚删两个零引用包）、破坏"零第三方资产"承诺 |
| Blender + Auto-Rig Pro | **当前无场景** | 本机未安装 Blender；项目零外部模型，无模可修 |
| `DS4.1Flash` 承担编码 | **继续使用** | 17,302 行 C# + 10,215 行测试已在该模型下维护；场景由代码生成，`verify.py` 批处理即可出图，不依赖 Unity/Blender MCP |

**保留后门**：`research-notes.md` §3 规划的 `IAvatarProvider` 仍按计划抽象，未来若引入写实人物，
优先 **MakeHuman**（开源、CC0 输出、参数化人体、自带 15 指骨、直接匹配 Unity Humanoid），
而非生成式模型——因为本项目需要的是**比例可控**，不是"看着像"。

---

## 2. 优化目标与预期效果

### 2.1 量化目标

| 指标 | 当前 | 目标 | 说明 |
|---|---:|---:|---|
| 键面距离 `KeyTopDistance` P50 | 1.2 mm | ≤ **3 mm** | 保持 |
| 键面距离 `KeyTopDistance` P95 | 46.0 mm | ≤ **15 mm** | 主目标 |
| 键面距离 峰值 | 116.3 mm | ≤ **60 mm** | 防个别帧飞掉 |
| 指尖误差 均值 | 26.7 mm | ≤ **25 mm** | 方案 §8 原门槛 |
| 指尖误差 P95 | 72.8 mm | ≤ **50 mm** | 方案 §8 原门槛 |
| 换位过渡峰值 | 222.4 mm | ≤ **100 mm** | 方案 §8 原门槛 |
| 悬空帧数（3 s 曲） | 17 | **≤ 3** | 计数口径不变 |
| 穿模帧数 | 0 | **0** | 不得回退 |
| 手腕命中率 | 95% / 93.6% | **≥ 93.6%** | 不得下降（防"修了精度丢了跟随"） |
| 特写躯干入画 | 有 | **0 张** | V01/V02/V09 人工+断言 |
| 测试总数 | 311 + 95 | **不减少** | 回退即拒绝合并 |

### 2.2 预期观感（人工评审项）

1. 特写视角下，按下音符的指尖**看得见接触键面**，不再浮空；
2. 未按键的手指保持**微屈准备位**，不是五指扇开；
3. 画面中**不再出现演奏者躯干大块遮挡**；
4. 和弦起音时**先成形、再落键**，没有"先亮键后落指"的空档；
5. 大跨度换位走"抬腕—平移—落键"弧线，不是贴键滑铲。

### 2.3 明确不做

- 不引入 Tripo / Blender / Auto-Rig Pro；
- 不升级 Unity 6（`TMP 3.0.6` / `uGUI 1.0.0` 兼容风险，且本项目不需要 Unity CLI/MCP）；
- 不做真实 SSAO / 后处理（`completion-review.md` 已声明受 Built-in 管线限制，不承诺）；
- 不做移动端（Sprint 5 保持"未开始"）；
- 不为达标而放宽 `MaxStepPerFrame`（0.035 m）、`FingertipErrorTolerance`（0.035 m）、
  `FingertipPenetrationTolerance`（0.004 m）任一阈值。

---

## 3. 具体优化措施与实施步骤

> 每批独立提交、独立验收、独立可回滚。**先补测量，再改实现**是硬顺序。

### 批次 1 — 评测基线可归因化（P0，1 人日）

**目的**：让"哪一类帧、哪一根手指、哪一段过渡"出错可定位。没有这步，后续所有参数调整都是盲调。

| 步骤 | 动作 | 落点 |
|---|---|---|
| 1.1 | 建标注曲目集：3 首，覆盖 黑键 / 和弦 / 快速音阶 / 大跨度 四类；用现有 `demo.mid` + 2 首公有领域练习曲（自制 MIDI，避免版权） | `Assets/StreamingAssets/Songs/bench/` |
| 1.2 | 采样窗口 3 s → **全曲**，单曲 → **3 曲三种子** | `HandMotionTests` 参数化 |
| 1.3 | 把 `KeyTopDistance` 的 P50/P95、`TransitionPeakError`、左右手分项写进 `HandMotionReport.ToLogString()` 固定输出 | `Avatar/HandMotionMetrics.cs` |
| 1.4 | 新增分组门禁断言：`BlackKeyHitRate` / `ChordHitRate` / `FastScaleHitRate` / `LargeMotionHitRate` 各自不低于组内基线 −3% | `HandMotionTests` |
| 1.5 | 输出基线报告：四类分组 × 五项指标 | `Docs/perf/hand-baseline-2026-10-19.md` |

**验收**：`python Tools/verify.py --edit-only` 通过；新增 6 条 EditMode 用例；报告落地。
**风险控制**：本批**只测量不改行为**，`git diff` 应只含指标与测试。

---

### 批次 2 — 指尖接触闭环（P0，2 人日）

**靶子**：`KeyTopDistance` P95 46 → ≤15 mm；`FingertipTargetError` P95 72.8 → ≤50 mm。

| 步骤 | 动作 | 落点 |
|---|---|---|
| 2.1 | **先诊断**：在 `UpdateFingertipTargets` 加临时计数，区分三类失败——(a) `Assignment.GetPitch < 0`（指法未分配）、(b) `_keyTouches` 缺键（按键表未建）、(c) 有目标但 IK 解不到（可达性）。跑批次 1 的曲目集出占比 | `Avatar/HandIKController.cs` |
| 2.2 | **修 (a)**：`HeuristicFingeringProvider` 在跨度过大 / 黑键夹白键时返回 -1 的分支，补兜底分配（最近可用手指 + 低置信度标记），保证"按下的音必有手指指向" | `Avatar/HeuristicFingeringProvider.cs` |
| 2.3 | **修 (c)**：加可达性检测——指尖目标到肩距离 > 臂长时置 `Unreachable`，走"抬腕—平移—落键"（复用 `ApplyGlideLift`），而不是硬拉导致悬空 | `HandIKController` |
| 2.4 | **手腕高度自适应**：当某手 `KeyTopDistance` 连续 N 帧 > 15 mm 且在下降沿，按误差反向修正该手 `HandHeight`（用 `AvatarDefinition.Hand.HandHeightOffset`，**不改默认常量**）；带 ±8 mm 限幅与 0.06 s 平滑 | `HandIKController` + `AvatarDefinition` |
| 2.5 | **和弦起音**：`ChordFormationSeconds` 按当前和弦最短音符时值自适应（≤ 时值的 40%），避免短音符上 blend 走不完导致起音悬空 | `HandIKController` |
| 2.6 | 固化门禁：`KeyTopDistance` P95 ≤15 mm、悬空帧 ≤3、穿模帧 = 0 写成断言 | `HandMotionTests` |

**验收**：批次 1 报告重跑，五项指标全部达标；`verify.py --edit-only` + PlayMode 全绿；V01/V02/V09 重出图。
**回滚点**：2.2–2.5 各自独立 commit，任一指标回退即回滚该步。

---

### 批次 3 — 特写构图排除躯干遮挡（P0，0.5 人日）

| 步骤 | 动作 | 落点 |
|---|---|---|
| 3.1 | 构图求解时纳入 `AvatarManager` 躯干包围盒作为**负空间**：若与视锥相交，沿视线方向后移相机至不相交（上限 `CloseupMaxDistance = 0.88 m`） | `Camera/CameraModeController.cs`、`Camera/FramingPolicy.cs` |
| 3.2 | 若后移会丢失手部细节，则改为抬高俯角（`CloseupViewPitch` 60° → 65–70°），并在两种候选间取"手部占屏更大"者 | 同上 |
| 3.3 | 新增断言：特写模式下躯干包围盒中心**不在视口内**；手部核心点仍在视口内（保持既有 `AssertPointInViewport`） | `Tests/PlayMode/CameraVisibilityTests.cs` |

**验收**：V01/V02/V09 三张重出图无躯干大块入画；`CameraVisibilityTests` 由 9 场景扩到 11 场景全绿。

---

### 批次 4 — 手型自然化（P1，1 人日）

| 步骤 | 动作 | 落点 |
|---|---|---|
| 4.1 | 无音符 Rest 位从"五指独立 `RestAngle=18°` + 张开 `SpreadAngle=4°`"改为**基础 C 形手**：MCP 25° / PIP 30° / DIP 15°，拇指侧倾 20°，四指张开角降到 2° | `Avatar/FingerPoseController.cs`、`Avatar/AvatarDefinition.cs`（`HandMetrics` 增 `RestCurlProfile`） |
| 4.2 | 落键后手指保持微屈，不回弹到伸直；`ApproachCurlScale`（0.62）与 `RestCurlDegrees`（26°）重新标定 | 同上 |
| 4.3 | 拇指外展 `ThumbAbduction = 57°` 复核，避免特写下"虎口朝天"的翻手观感 | `AvatarDefinition` |

**验收**：`HandJointPoseTests` 增 2 条（Rest 位三关节角度区间、拇指不外翻）；V01/V05/V06 重出图人工过。

---

### 批次 5 — 视觉评审机制与材质（P1，1 人日）

| 步骤 | 动作 | 落点 |
|---|---|---|
| 5.1 | 建 5 项评分表：接触感 / 手型自然 / 遮挡 / 材质 / 整体，每项 1–5 分，**差 1 分即不通过** | `Docs/visual-regression.md` 追加章节 |
| 5.2 | 每次改动后 `python Tools/verify.py --visual`（**不加 `-nographics`**）重出 20 张，**先核对 `manifest.md` 的「窗口尺寸」列与上一版一致**（应为 1101×656），不一致整批作废重跑；随后隔天盲评（与上一版并排，不知哪张是新版） | 流程 + `Docs/screenshots/manifest.md` |
| 5.3 | 材质：琴体粗糙度分层、键面高光收敛、接触阴影默认开启（`EnableContactShadow` 已有，确认默认值） | `Piano/PianoSceneEnvironment.cs` |
| 5.4 | 在 `README` §1.4「已验证状态」补上手部接触指标，注明采集口径与窗口 | `README.md` |

**验收**：评分表 5 项全过；README 数字与实测一致。

---

## 4. 所需资源与时间安排

### 4.1 资源

| 类别 | 需求 | 现状 | 成本 |
|---|---|---|---|
| 引擎 | Unity 2022.3.22f1c1（revision `c3cbd310e76e`） | 已装 `D:\Unity\Unity 2022.3.22f1c1` | ¥0 |
| 运行时 | Python 3.13（`verify.py` 仅标准库） | 已装 | ¥0 |
| 曲目 | 3 首标注 MIDI（黑键/和弦/快速音阶/大跨度） | 需自制或取公有领域 | ¥0 |
| 3D 工具 | **不需要** Blender / Tripo / Auto-Rig Pro | — | ¥0 |
| 硬件 | 本机 RTX 4060 Laptop（`--visual` 需真实 GPU，不加 `-nographics`） | 已有 | ¥0 |
| **现金合计** | | | **¥0** |

### 4.2 时间（按"有效人日"计，非日历日）

| 批次 | 内容 | 人日 | 依赖 |
|---|---|---:|---|
| 1 | 评测基线可归因化 | 1.0 | 无 |
| 2 | 指尖接触闭环 | 2.0 | 批次 1 |
| 3 | 特写构图 | 0.5 | 可与批次 2 并行 |
| 4 | 手型自然化 | 1.0 | 批次 2（避免参数互相干扰） |
| 5 | 视觉评审与材质 | 1.0 | 批次 2–4 |
| **合计** | | **5.5** | 按每天 3 h 有效投入 ≈ **2 周** |

**里程碑**：
- M1（批次 1 完成）→ 有一份"错在哪"的分组报告；
- M2（批次 2–3 完成）→ 三项 P0 指标达标，特写图可给人看；
- M3（批次 4–5 完成）→ 视觉评审 5 项全过，README 数字对得上。

---

## 5. 潜在风险与应对

| # | 风险 | 触发信号 | 应对 |
|---|---|---|---|
| 1 | 改 IK 打翻既有断言（311 + 95） | EditMode 出现 >5 条失败 | 批次 2 拆成 2.2–2.5 四个独立 commit，逐个回滚；先跑 `--edit-only` 再跑 PlayMode |
| 2 | **指法分配本身不唯一**，误差里有"正确但不同"的样本 | 某类帧误差高但肉眼正确 | 用批次 1 的标注集，只对"分配与标注一致"的样本计误差；不一致样本单列"待裁定" |
| 3 | 手腕高度自适应导致**手沉进键盘** | 穿模帧 >0 | `FingertipPenetrationTolerance`（4 mm）作为硬断言，超限即回滚 2.4 |
| 4 | 特写后移导致手部过小、细节丢失 | 手部占屏面积下降 >30% | 改走高俯角方案（3.2），并在两种候选间取占屏更大者 |
| 5 | 视觉评审主观化 | 结论反复 | 5 项表 + 隔天盲评 + 固定机位固定曲目；每次评审保留原始图与评分记录 |
| 6 | 时间碎片导致半成品堆积 | 一批超过 3 天未合 | 强制"一批一提交"，未达标不进 master |
| 7 | 为达标偷偷放宽阈值 | diff 里出现 `MaxStepPerFrame` / `Tolerance` 变大 | `static_checks.py` 增一条"阈值只许收紧不许放宽"的检查 |
| 8 | **视觉基线分辨率退化**：`verify.py --visual` 在无前台 GUI 时实际渲染 **640×480**（请求 1280×720），生成的图与既有 **1101×656** 基线不可比，一旦提交即污染视觉回归 | `Docs/screenshots/manifest.md` 的「窗口尺寸」列与历史不一致 | ① 出图后**先核对 manifest 尺寸列**，与上一版不一致则整批丢弃；② 必须在 GUI 前台运行，不让终端抢焦点；③ 2026-10-19 20:17 的一次重采即因此整批回滚，未入库 |

---

## 6. 衡量成效的评估指标

### 6.1 主指标（验收门禁）

| 指标 | 当前 | 目标 | 采集方式 | 命令 |
|---|---:|---:|---|---|
| 键面距离 P95 | 46.0 mm | **≤15 mm** | `KeyTopDistance` 分位 | `verify.py --edit-only` + PlayMode |
| 键面距离 峰值 | 116.3 mm | ≤60 mm | 同上 | 同上 |
| 指尖误差 均值 / P95 | 26.7 / 72.8 mm | ≤25 / ≤50 mm | `FingertipTargetError` | 同上 |
| 换位过渡峰值 | 222.4 mm | ≤100 mm | `TransitionPeakError` | 同上 |
| 悬空帧数 | 17 | ≤3 | `HoverFrameCount` | 同上 |
| 穿模帧数 | 0 | 0 | `PenetrationFrameCount` | 同上 |
| 手腕命中率 | 95%（192/202） | ≥93.6% | `HitRate` | 同上 |
| 躯干入画（特写） | 3/3 张 | 0 张 | 包围盒投影断言 + 人工 | `CameraVisibilityTests` |

### 6.2 护栏指标（不得回退）

| 指标 | 门槛 |
|---|---|
| EditMode / PlayMode | 总数不减少，0 失败 |
| 纯播放 p99 帧时间 | ≤17 ms |
| GC Alloc | ≤0.01 MB |
| 90 s 长跑内存增长 | ≤1% |
| 视觉基线 | 20 张全部重出且人工过 5 项表 |
| 依赖 | `manifest.json` 不新增第三方包 |

### 6.3 过程指标

- 每批交付：1 份 commit + 1 份重采样报告 + 1 组重出图；
- 每周一次"指标漂移检查"：`verify.py --all` 全跑，对比上次数值；
- 指标恶化 >10% 且无说明 → 视为回归，阻塞下一批。

---

## 7. 优先级总表

| 优先级 | 编号 | 措施 | 人日 | 前置 | 达标信号 |
|---|---|---|---:|---|---|
| **P0** | 批次 1 | 评测基线可归因化（标注集 + 分组门禁） | 1.0 | — | 分组报告落地，能指名道姓说"哪类帧错" |
| **P0** | 批次 2 | 指尖接触闭环（指法兜底 / 可达性 / 手腕自适应 / 和弦起音） | 2.0 | 批次 1 | 键面距离 P95 ≤15 mm |
| **P0** | 批次 3 | 特写构图排除躯干遮挡 | 0.5 | — | 3 张特写无躯干入画 |
| **P1** | 批次 4 | 手型自然化（C 形手 / 回弹 / 拇指） | 1.0 | 批次 2 | 手型评审 4 分以上 |
| **P1** | 批次 5 | 视觉评审机制 + 材质 + README 对齐 | 1.0 | 批次 2–4 | 5 项表全过 |
| **P2** | — | `IAvatarProvider` 抽象（为将来写实人物留口） | 0.5 | — | 接口存在且默认实现不变 |
| **不做** | — | Tripo / Blender / Auto-Rig Pro / Unity 6 / URP / 移动端 | — | — | — |

---

## 附录 A：本文引用的关键文件

| 文件 | 作用 |
|---|---|
| `Assets/Scripts/Avatar/HandIKController.cs` | 手腕/指尖目标、可达性与换位过渡（`HandHeight 0.799` / `HandDepth 0.232` / `MaxStepPerFrame 0.035`） |
| `Assets/Scripts/Avatar/FingerPoseController.cs` | 四阶段手指姿态（`PressAngle 32` / `RestAngle 18` / `RestCurlDegrees 26`） |
| `Assets/Scripts/Avatar/HandMotionMetrics.cs` | 全部手部指标的唯一定义处 |
| `Assets/Scripts/Avatar/AvatarDefinition.cs` | `HandMetrics`（掌长 58 mm / 掌宽 76 mm / 指长 68–90 mm） |
| `Assets/Scripts/Camera/CameraModeController.cs` | 三态相机与特写构图参数 |
| `Assets/Tests/PlayMode/HandMotionTests.cs` | 手部质量门禁用例 |
| `Assets/Tests/PlayMode/CameraVisibilityTests.cs` | 视口投影断言（扩躯干覆盖） |
| `Tools/verify.py` | 编译 / 测试 / 截图 / 性能的一键入口 |
| `Docs/animation-pipeline.md` | 全部指标口径的权威定义 |

## 附录 B：复现命令

```bash
# 编译 + EditMode + PlayMode（不含渲染）
python Tools/verify.py --edit-only

# 重出 20 张视觉基线（必须真实 GPU，脚本内部不加 -nographics）
python Tools/verify.py --visual

# 全量（视觉 + 压力 + 性能）
python Tools/verify.py --all

# 90 s 长跑（内存回归）
python Tools/verify.py --soak --soak-seconds 90
```
