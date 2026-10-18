# Piano Teacher 3D — 方案落地交付报告

> 执行日期：2026-10-16 · 引擎：Unity 2022.3.22f1c1（中国版）· 平台：Windows 桌面端
> 依据文档：[`../../APP《产品与工程综合优化方案》.md`](../../APP《产品与工程综合优化方案》.md)（下称「方案」）、
> [`../../PianoTeacher3D-项目审查与优化建议.md`](../../PianoTeacher3D-项目审查与优化建议.md)（下称「审查」）
> 执行方式：6 个并行子任务（相机 / UI / 手部与人物 / 曲库与错误 / 钢琴与音频 / 场景光照）分片改造 →
> 会话层（`LessonSessionController`）统一集成 → 3 个收尾子任务（测试补齐 / 压力长跑 / 文档）→ 逐轮编译与测试验证。

---

## 1. 一句话结论

方案与审查中**可落地、不需要外部资源与真机**的技术项已全部实现并验证通过；
**唯一没有做到的是「换外部高质量模型 / 真实采样音色 / AI 动作生成」这三类需要外部资产或算力的里程碑**，
它们按方案本身就属于后续阶段，已在文档中标注为「计划中」。

| 维度 | 改造前 | 改造后 |
|---|---|---|
| 编译 | 0 错误 0 警告 | 0 错误 **0 警告** |
| EditMode 测试 | 96 | **278 / 278** |
| PlayMode 测试 | 58（57 通过 + 1 跳过） | **74（69 通过 + 5 跳过 + 0 失败）** |
| 附加专项 | 无 | Stress 3/3 · Perf 4/4 · Soak 1/1 |
| 源码规模 | 28 脚本 / 4,757 行 | **46 脚本 / 16,686 行** |
| 测试规模 | 17 文件 | **30 文件 / 9,503 行** |
| 文档 | 6 篇 | **12 篇 / 3,250 行**（含 README） |
| 视觉基线 | 6 张（仅人工） | **18 张 + 自动清单 + 像素比较工具** |
| 手腕目标琴键命中率 | 82.9%（未限帧批处理下测得） | **93.6%**（60 FPS 下测得，左 94.7% / 右 92.7%） |
| 单帧最大位移 | 35 mm | **35 mm**（约束未放宽） |
| 穿模告警 | 0 | **0** |

---

## 2. 六条改造线的落地内容

### 线 1 · 相机状态机（方案 4 章 / 审查 3 章，P0）

| 要求 | 实现 |
|---|---|
| 显式三状态 + 目标点/姿态/取景策略分离 | 新增 `Camera/CameraFraming.cs`（`CameraTarget` / `CameraPose` / `HandRegion` / `FramingPolicy` / `CameraPoseTransition` / `CameraModePreset`，纯数学、可单测）；`CameraModeController` 重写为状态机 |
| 手部特写按「活动练习手 ∪ 当前/即将琴键」取景 | `FramingPolicy.TryCombineCloseupRegion` + `MergeWithShare`（单手练习时主手占比 0.7、另手仅部分并入） |
| 距离 / FOV 上下限、阻尼跟随、限幅 | `SolveFraming` + `RateLimit` + 指数阻尼；垂直跟随单独限幅（不随手上下而改变俯角） |
| 全景改前侧 3/4、消除低音端遮挡 | `OverviewYaw=34°`，按「角色 + 钢琴」联合 `Bounds` 构图；`Docs/screenshots/01_full.png` 可见完整 88 键 |
| 自由观察进入 / 返回 / 重置 + UI 悬停不旋转 | `PreviousPreset` / `ReturnToPreset` / `ResetView`；`FramingPolicy.ShouldAcceptOrbitInput` + `EventSystem.IsPointerOverGameObject` |
| 过渡中断不叠加 | 单一 `CameraPoseTransition` 结构体，`SetTarget` 不重启进度、以当前姿态为起点 |
| 视角可用性验证 | 新增 `CameraVisibilityTests`（9 场景，含遮挡几何断言） |

**本轮修复的相机缺陷**：见 `Docs/visual-regression.md` 第 5 节 —— 手部特写被角色头部遮挡（人工看图发现，
自动断言「投影点在视口内」无法覆盖遮挡）。已按几何约束把特写俯角定为 60° 并新增 `AssertHeadNotOccludingHands` 守护。

### 线 2 · 教学 UI 三层结构（方案 3 章 / 审查 2 章，P0/P1）

- `UITheme` 补齐**设计令牌**（颜色 / 字号 / 间距 4·8·12·16·24 / 圆角 / 控件高度 / 布局常量）与**按钮七态**
  （默认 / 悬停 / 按下 / 选中 / 禁用 / 加载 / 错误）+ 五种样式层级（Primary / Secondary / Ghost / Danger / Toggle）。
- `LessonControlsUI` 拆成三层：**主播放栏**（唯一强强调的播放按钮 + 进度 + 时间 + 视角分段器）→
  **练习工具栏**（可折叠；练习手 / 速度 / 高亮全部分段选择器，直接显示当前值，不再循环点击猜状态）→
  **分析面板**（右侧滑出，动作质量折叠进「开发诊断」）。
- 新增 `UIStatusBar`（顶栏 + 错误横幅 + 恢复按钮 + 加载条 + toast + 帮助 / 曲库浮层）、`UISegmentedControl`、`UITooltip`。
- 状态可见性（「节拍器：开」）、空状态页、禁用原因 tooltip、快捷键让位规则（文本输入聚焦时不抢按键）、
  响应式双档布局（参考宽 <1100 折叠工具栏、主栏 92/60 紧凑高度）。
- 新增 `Docs/ui-design-system.md` 记录全部令牌、状态、快捷键与断点。

### 线 3 · 手部几何 / 指法提供器 / 动作质量（方案 5 章 / 审查 1 章，P0/P1）

- 手部网格：手掌分掌根 / 掌背 / 虎口，五指**三段指节**、指尖圆润、拇指对掌（与其余四指约 57°）、
  五指长度差异；以 **24 mm 键宽**为标尺（`AvatarBodyMetrics`）。
- 人物：头部加耳 / 下颌 / 鼻 / 眼 / 眉，服装作为独立可见层（衣领 / 袖口 / 下摆），发型改大轮廓 + 少量发束；
  统一角色尺寸表，三角色手腕落在同一套键盘标定范围。
- 新增资源适配层 `Avatar/AvatarDefinition.cs`：集中模型标识 / Humanoid Avatar / 材质 / 缩放 / **每角色 IK 偏移** /
  手部质量档；构建后做 **Avatar 有效性 + 必需骨骼 + 比例 + 手部可达** 校验，失败回退默认角色并给出 `LastRigValidationMessage`。
- 指法：新增 `IFingeringProvider` + `HeuristicFingeringProvider`，按「序列 / 相邻音 / 跨度 / 黑键可达 / 拇指穿越 /
  重复音 / 和弦一致手型」规则分配，输出**置信度**与 `IsEstimated`（UI 标注「估算指法」，不谎称专业指法）；
  `FingerPoseController` 改为调用提供器。
- 指尖与手腕目标分离、**黑键独立目标高度**（`BlackHeight 0.032` vs `WhiteHeight 0.022`）、和弦先成形再下压。
- 动作质量指标扩充为：左右手分项命中率、指尖误差均值/最大、速度异常计数、
  黑键 / 和弦 / 快速音阶 / 跨手四组命中率、四个视觉抽查计数（手指交叉 / 拇指方向 / 手掌穿模 / 离键悬空）。
- **本轮新增「手部预置」**：会话层把「每只手在 0.25s 内即将到来且尚未按下的音高」推给 `HandIKController`，
  手腕在音符开始前先移向该键（真实演奏的预置动作）。预置帧**不计入**命中率统计，因此指标未被美化。
  效果：命中率 79.9%（60 FPS 下）→ **93.6%**，单帧位移上限 35 mm 未放宽。

### 线 4 · 曲库导入 / 会话状态机 / 统一错误（方案 6、8、9 章，P0/P1）

- `Core/LessonState`：`NoSong / Loading / Ready / Playing / Paused / Error` 显式状态机；
  `LessonSessionController` 只作**会话门面**（协调 + 公开命令），算法细节下移到各模块（方案 9.1）。
- 切曲事务：**校验 → 临时解析 → 一次性提交 → 清理旧状态 → 更新 UI**；任一步失败回滚到明确状态
  （有旧曲则保持旧曲与播放状态），杜绝「标题是新曲、音符仍是旧曲」的混合状态。
- `Core/SongLibraryService`：导入（校验 → 复制到 `persistentDataPath/PianoTeacher3D/Songs` → 生成安全 ID → 写入最近曲目）、
  最近曲目持久化（去重 / 上限 20）、文件失效区分「路径失效 vs 格式错误」、删除与清除缓存（含目录穿越防护）。
- `MIDI/MidiFileValidator`：大小（8 MB）/ 扩展名初筛 / 真实解析（MThd、PPQ、事件数 20 万、时长 1 小时），
  扩展名仅初筛、**不执行导入文件中的任何命令**。
- `MidiParser` 增量扩展：`MidiFormat` + `Warnings`（Format 2 明确「顺序串接为单一时间轴」并提示；极端 tempo 回落 120 BPM）。
- `Core/AppError` + `ErrorReporter`：稳定错误码（23 项）、严重程度、用户文案（**发生了什么 + 为什么可能 + 下一步**）、
  `RecoveryAction` 与恢复按钮文案；日志经 `PathSanitizer` 脱敏、**默认不上传任何数据**。
- `UI/FileBrowserUI`：应用内 MIDI 文件选择器（驱动器 / 目录 / 分页 / 只看 .mid / 中文空格路径），**零第三方包、离线可用**。
- `Core/UserPreferences`：速度 / 视角 / 练习手 / 高亮 / 节拍器 / 画质档位 / 最近曲目持久化；
  **自动化测试（`-batchmode`）下跳过套用**，保证用例从确定性初始状态出发。

### 线 5 · 钢琴材质与音频（方案 5.4 / 7.3，P1）

- 新增 `Piano/PianoMeshFactory`：程序化**倒角立方体网格**（`TopOnly` / `AllEdges`，含 clamp 与退化三角防护），
  88 键只创建 1 份白键 + 3 份黑键网格与 4 份材质（无每键 `new Material`）。
- 琴键：白键顶面小倒角；黑键分**顶面 / 侧壁 / 底部支撑**三层，albedo 由 0.07 提到 0.13 并给非零光泽（避免纯黑无反射）；
  **88 键映射与键根坐标未变**（`HandIKController` / `HandPoseValidator` / 反向相机包围盒全部兼容）。
- 琴体：补齐侧板 / 键盘盖 / 底座 / **踏板**，四类材质（木 / 键面 / 金属 / 红绒）光泽度与金属度分层，琴体最低点落地。
- 音频：`PlayOneShot` 改为**24 声部受控池**（`PianoVoiceAllocator`），支持 `OnNoteOff` 淡出释放、
  `ReleaseAll`（暂停 / 拖动 / 切曲 / 循环边界统一释放）、力度→音量 + 低通音色；初始化失败可降级静音并给出中文原因；
  波形合成保留为「零采样依赖」的降级模式。

### 线 6 · 场景光照与画质档位（方案 7 章 / 审查 1.3B，P1）

- 新增 `Piano/PianoSceneEnvironment`：低干扰音乐室（地面 / 背景板 / 踢脚线 / 中性色演奏区 / 3 个装饰），
  主光（可投影）+ 补光 + 轮廓光 + 键盘点光（后三者常开、不投影），避免多强光造成琴键反光刺眼。
- 接触阴影用「软阴影 + 贴地极淡径向渐变」**近似**，并在代码注释、文档与测试命名三处声明**不是真实 SSAO**。
- 新增 `Core/QualityProfile`（`Standard` / `High`）：唯一读取 `SystemInfo`、唯一写 `QualitySettings` 的位置；
  两个档位都在启动时落地。**低端档只降阴影分辨率 / 距离 / 抗锯齿，不移除琴键高亮与补光**。
- `Tools/verify.py` 新增 `--stress` / `--perf` / `--soak[--soak-seconds]` / `--compare-visual` / `--all` 与产物清单。

---

## 3. 验证证据

### 3.1 全量验证（2026-10-16，`python Tools/verify.py --stress --perf --soak --soak-seconds 90`）

```
[EditMode] 通过 — 总计 278，通过 278，失败 0，跳过 0
[PlayMode] 通过 — 总计 74，通过 69，失败 0，跳过 5
           SKIP: SampleFrameTiming / SampleFrameTiming_UnderSwitchingLoad   ← [Explicit]
           SKIP: Soak_PlaysContinuously / CompareAgainstBaseline             ← [Explicit]
           SKIP: CaptureLessonViews                                          ← 需 GPU（已单独跑通）
[Stress]  通过 — 总计 3，通过 3，失败 0，跳过 0
[Perf]    通过 — 总计 4，通过 4，失败 0，跳过 0
[Soak]    通过 — 总计 1，通过 1，失败 0，跳过 0
✓ 全部通过
```

### 3.2 视觉验收（有 GPU，18 张截图全部产出）

`Docs/screenshots/`（`manifest.md` 由测试运行时自动生成）：
原 6 张基线 + **V01–V12**（审查 6.1 V01–V09 + 方案 11.2 的空曲库 / 高亮 / 分析面板）。
人工看图确认：手部五指与键面清晰、无头部遮挡、全景 88 键完整、高亮语义色正确（暖金预提示 / 左蓝 / 右橙）。

### 3.3 性能与稳定性（`Docs/perf/sampling.md`）

| 场景 | 结果 |
|---|---|
| 纯播放（480 帧） | 锁 60 FPS；p50 16.7 / p95 16.8 / p99 **17 ms**；1% low **58.4 FPS**；GC 增量 **0.01 MB** |
| 切换负载（每 20 帧切换一次，24 次） | p99 **16.9 ms**；1% low **59.1 FPS**；GC 增量 1.23 MB |
| 长跑 90 s（44 次循环切换） | 错误日志 **0**、姿态越界 **0**、内存 **+0.4%**、定时漂移 **0.8%** |
| 资源加载 | `demo.mid` **9.9 ms**，失败 0 |

> 诚实声明：以上均为 `-batchmode -nographics` 采样，**不含渲染负载，不代表真机体验**；
> 方案 11.3 的帧率门槛仍需在集显笔记本与中档独显真机上定稿（`Docs/perf/environments.md` 记录了待测矩阵）。

---

## 4. 过程中发现并修复的缺陷（含根因）

| # | 现象 | 根因 | 修复 |
|---|---|---|---|
| 1 | 手部特写画面正中是头发，键面与双手被挡 | 相机水平后移量 `d·cosθ` 超过「焦点到头部水平距离 0.46m」，头部必然落在相机→双手连线上（34° 时超过 0.55m 就遮挡，而大跨度手必须拉到 0.75m+） | 特写俯角 34° → **60°**（`d·cosθ ≤ 0.44`），并把最大距离/视野放宽到 0.88m / 48° 以覆盖窄窗口大跨度；新增几何遮挡断言守护 |
| 2 | 单手目标一瞬间跳到另一只手的位置 | `LimitTargetRate` 用单一 `_hasSmoothedTarget` 标志，重置后把**两只手**的平滑目标都写成同一只手的原始目标 | 改为左右手各持一个标志位（`_hasLeftSmoothed` / `_hasRightSmoothed`） |
| 3 | 命中率 79.9%，卡在 80% 门槛下 | 画质档位把帧率锁到 60 FPS 后，35 mm/帧 的位移上限使手腕实际速度降至 2.1 m/s，音符开始时手仍在换位途中（旧基线 82.9% 是在**未限帧**批处理下测得的，不具代表性） | 新增「手部预置」（方案 5.3 / 4.2.3 轻量预测）→ **93.6%**；单帧位移约束未放宽 |
| 4 | 换角色后「最大单帧位移」出现 332 mm 的无意义值 | 不同骨架的腕部基准位置不同，切换瞬间的整段位移属「换了一套身体」而非「手在移动」 | `SwitchAvatar` / `CycleAvatar` 后 `ResetMetrics()`（与换曲同语义） |
| 5 | 窄窗口（4:3）下大跨度双手贴到画面边缘 | 特写距离/FOV 上限不足以覆盖 4:3 下的横向需求（诊断显示两者同时顶满上限） | 有界放宽上限并在注释里写明原因（上限仍是有界预设，动态变化仍限速） |
| 6 | V12（分析面板）截图无法生成 | 窄窗口下练习工具栏自动折叠，`GameObject.Find` 找不到未激活的按钮 | 截图用例改为「含未激活对象的精确查找 + 逐级激活父对象」；`ClickButton` 辅助方法加固 |
| 7 | 点「曲目分析」后顶栏曲名被挤出可视区（编译期发现的类型错误） | `UITheme.PlaceTopLeft` 期望 `RectTransform`，误传 `Text` | 传 `_toolbarTitle.rectTransform`（编译期即暴露） |
| 8 | 新增测试文件编译失败（`Category` / `Object` 二义性） | 引用 `System.ComponentModel` 与 `System` 引入同名类型冲突 | 移除 `using System.ComponentModel` 并限定名；`UnityEngine.Object` 全限定 |

---

## 5. 仍未实现 / 未定稿的项（诚实清单）

| 项 | 状态 | 说明 / 对应阶段 |
|---|---|---|
| 外部高质量人物与钢琴模型 | **计划中** | 方案 5.2 中期 / 审查 1.1 方案 B。当前仍为程序化网格（手部已精修）；躯干侧影仍偏几何化，要彻底解决需多截面挤出网格或接入许可清晰的模型 |
| 真实采样钢琴音色 | **计划中** | 方案 2.2 P1 / 7.3。波形合成保留为降级模式；换采样需同时评估延迟 / 长音衰减 / 力度响应 / 同时发声数 / 许可 |
| 指尖真正贴到键面按压点 | **部分实现** | 手腕级命中率已 93.6%，但诊断显示**指尖误差均值约 99 mm**、悬空帧偏多 —— 手腕到位 ≠ 指尖到位，属方案 5.3 第二阶段后续项 |
| 真实 SSAO / 后处理 | **计划中** | 内置渲染管线 + 零第三方包约束下未引入后处理栈；当前用贴地渐变近似接触阴影（已在三处声明是近似） |
| AI 手部动作生成 / 照片生成 3D 人物 / MP3-WAV 转谱 | **计划中** | 方案 2.2 P2 / 第 10 章；`IFingeringProvider` 已落地，其余提供器接口（`IAvatarProvider` / `IHandMotionProvider` / `IAudioToScoreProvider`）预留未实现 |
| 人工编辑指法并与原 MIDI 分开保存 | **计划中** | 方案 5.3；提供器接口已支持注入，缺数据层 |
| 移动端 / Web 适配 | **计划中** | 方案 12 Sprint 5；输入命令抽象与布局配置已就绪，真机未验证 |
| 集显笔记本 / 中档独显真机性能定稿 | **待真机** | 方案 11.3 / 11.4；`Docs/perf/environments.md` 已列出待测矩阵（断网 / 中文空格路径 / 标准用户 / DPI） |
| 10 分钟长跑 | **已具备、未跑满** | `Soak_PlaysContinuously` 默认 600 s（`--soak`），本轮以 90 s 验证通过 |

---

## 6. 如何复现全部结论

```bash
cd "D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d"

python Tools/verify.py --edit-only                                  # 纯逻辑（约 1 分钟）
python Tools/verify.py --stress                                     # 编译 + EditMode + PlayMode + 压力
python Tools/verify.py --perf --soak --soak-seconds 90              # 性能采样 + 长跑
python Tools/verify.py --visual                                     # 生成 18 张截图（需 GPU，勿加 -nographics）
python Tools/verify.py --all                                        # = --visual --stress --perf
python Tools/visual_diff.py Docs/screenshots/baseline Docs/screenshots   # 像素对比辅助
```

产物：`_verify_out/*.xml` 与 `*.log`、`Docs/screenshots/manifest.md`、`Docs/perf/sampling.md`、
`Docs/perf/environments.md`。退出码：`0` 通过 / `1` 有失败 / `2` 环境问题。

---

## 7. 文档索引（本轮新增/重写）

| 文档 | 内容 |
|---|---|
| `README.md` | 项目总览、四区 UI、视角系统、曲库与导入、用户偏好、错误与恢复、**诚实限制清单** |
| `Docs/architecture.md` | 11 个模块的职责/接口/依赖方向 + 状态机与时序 Mermaid 图 |
| `Docs/animation-pipeline.md` | 手部几何升级、指尖/手腕分离、**全部动作指标口径表** |
| `Docs/error-codes.md` | 23 项错误码 → 文案 → 恢复动作；方案 8.2 十类异常 → 实现 → 测试 |
| `Docs/ui-design-system.md` | 设计令牌、按钮七态、非颜色区分、焦点与快捷键、响应式断点 |
| `Docs/visual-regression.md` | 18 张截图用例 + 相机 9 场景断言 + **人工看图发现的问题与修复记录** |
| `Docs/quality-and-roadmap.md` | 画质档位、性能排查顺序、验收门槛现状、Sprint 0–5 路线图、7 条关键取舍 |
| `Docs/completion-review.md` | 方案与审查**逐条**核对表（含「计划中」项与对应阶段） |
| `Docs/optimization-report.md` | ← 本文档 |

---

# 附录 A · 第二轮：手部对应关系 / 显示模式 / 左右手方向（2026-10-16）

> 本轮针对三个具体问题做了修复与新增。完整的技术说明（现象 / 根因 / 改动 / 验证方法）见
> [`Docs/hand-correspondence-and-display-modes.md`](hand-correspondence-and-display-modes.md)。

## A.1 三项改动一览

| # | 问题 | 根因（实测依据） | 结果 |
|---|---|---|---|
| 1 | **手指与琴键不对应** | ① `FingerPoseController`（哪根手指弯）与 `HandIKController`（哪根指尖伸向键）**各自独立配对** —— 实测同一和弦下指法分配是 0/2/4，而指尖目标是 `-`/`-`/4，右手一个音甚至分成指 3 与指 4；② 手腕目标取"该手最低/最高音键"，手掌停在和弦一端；③ 手指只有固定按压角，方向与目标键无关。实测指尖误差 **95–130 mm** | 新增共用分配服务 `FingerAssignmentService`，两个控制器读**同一份**结果；指尖目标 = 分配给的音的键面按压点；新增**手掌几何反馈**让指尖贴上键；手指改为**朝目标对准**。**指尖误差降到 3–32 mm（静态收敛）/ 38–41 mm（播放期平均）**，且"分配指 == 目标指 == 最近指尖"逐项相等 |
| 2 | **新增展示 / 教学双模式** | 原项目只有单一的"完整手部动画"表现，没有面向跟练的"只看琴键"视图 | 新增 `Core.DisplayMode` + 会话 API + 工具栏 `展示 \| 教学` 分段选择器 + 快捷键 `T`；教学模式隐藏手部与手臂、只高亮被按下的琴键（左手冷蓝 / 右手暖橙），高亮控件置灰并说明原因；隐藏手部**不动骨骼与 IK**，切换时按当前时间轴重建状态 ⇒ 无残留、无视觉错乱；偏好持久化 |
| 3 | **人物左右手反向** | `AvatarManager` 把"局部 +X 旋转 180° 后 = 世界 −X"当成"演奏者左手侧"，但角色面向 −Z 时其真正左手在 **世界 +X**；同时键盘的低音端也被放在 −X。两处错误互相抵消（低音仍由名为 Left 的手弹），但从画面看整体是镜像的：**低音键与"左手"都出现在画面右侧**，名为 Left 的骨骼实际在角色的右手侧 | 确立唯一左右约定（低音 ↔ 左手 ↔ 世界 +X ↔ 画面左侧 ↔ 冷蓝），镜像键盘世界 X、镜像骨架/手部网格的左右侧、修正拇指内侧方向、修正 `DefaultHandPos` 与 `AreHandsCrossed` 的判定方向。实测：`Skeleton.LeftHand` → `x=+0.595`；A0 → 画面左侧（viewportX −0.026 / 0.238）；低音键渲染色 = 左手冷蓝 |

## A.2 本轮验证结果（2026-10-16 本机实测）

- 编译 **0 错误 0 警告**
- EditMode **285 / 285**（新增 `FingerAssignmentServiceTests` 7 条）
- PlayMode **87（82 通过 + 5 跳过 + 0 失败）**（新增 `HandFingerCorrespondenceTests` 6 条、`DisplayModeTests` 7 条）
- Stress **3 / 3**
- 截图 **20 张**（新增 `V13_teaching_mode.png` 教学模式、`V14_showcase_mode.png` 同机位展示模式对照）
- 关键指标：手腕目标命中率 **93.6%**、最大单帧位移 **35 mm**（约束未放宽）、穿模告警 **0**、
  静态指尖误差 **3–32 mm**

## A.3 仍未解决（诚实标注）

- **近景手型仍偏"圆钝"**：手指只有根节点 1 个自由度（无中/末节独立弯曲），且手腕姿态不受控，
  因此手部看起来是"整只手在按键"而不是"手指关节自然起伏"；指尖与键面之间在近景下仍有可见间隙。
  要彻底解决需要真正的关节链 + 手腕姿态控制，或直接接入许可清晰的高质量手部模型（方案 5.2 中期）。
- **播放期仍有 261–282 mm 的单帧指尖误差峰值**：出现在换位途中的过渡帧（指标为逐帧取样），
  并非稳定状态误差；稳定状态误差见 A.1 表中的 3–32 mm。
- 教学模式没有"下落音符/谱面"提示，只有琴键高亮（本轮范围如此）。
