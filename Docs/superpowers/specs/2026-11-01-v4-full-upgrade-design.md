# Piano Teacher 3D v4 全面升级设计

> 日期：2026-11-01
> 分支：`feature/v4-full-upgrade`（从 `feature/realism-phase2` 的 `e6eaae3` 切出）
> 引擎：Unity 2022.3.22f1c1 / C# 9 / URP 14.0.10 + Linear
> 目标：把「工程完成度很高、产品观感与教学闭环很弱」的现状，升级为**第一眼像产品、上手能练习**的桌面钢琴教学应用。

---

## 1. 背景与诊断

### 1.1 已有优势（不推翻）

- 编译 0 错误 0 警告；EditMode 473/473、PlayMode 124 通过 + 5 跳过；Stress 3/3、Perf 4/4。
- 唯一时间轴（`LessonTimeline`）、唯一音符事件源（`MidiEventScheduler`）、会话门面（`LessonSessionController`）、
  统一错误结构（`AppError`）与三状态相机状态机均已落地。
- URP + Linear + ACES + Bloom + 暗角 + SSAO Renderer Feature + 运行时反射探针已接入。
- 架构不变式由 `Tools/static_checks.py` / `check_architecture.py` 守护（UI 不直连底层、Update 零分配等）。

### 1.2 四个产品级短板（本次要解决）

| 编号 | 短板 | 证据 |
|---|---|---|
| P1 | **默认画面与视觉基线脱节** | 生产默认角色是 Rocketbox 真人（`UserPreferences.Defaults.SelectedCharacterId = "rocketbox.female01"`），但 `--visual` 在 batchmode 下按设计跳过偏好，01–06 / V01–V14 拍的是内置“气球人”；真人角色只有一张全景 V15，**特写从未出过图** |
| P2 | **场景像测试场** | 现有环境只有地面、背景板、踢脚线、灯与接触阴影薄片；没有墙、窗、地毯、乐谱等空间锚点；谱架是一块黑色平板 |
| P3 | **只能看，不能练** | 没有下落音符 / 五线谱、没有电脑键盘或屏幕琴键输入、没有等待模式、没有判定与评分；教学模式下只能看琴键高亮 |
| P4 | **内容单薄** | 内置曲只有 7 秒 `demo.mid`；`Songs/bench/` 三首仅用于指标评测；主菜单只有一张卡片 |

### 1.3 本次选择的路线

采用**方案 C：分阶段全量升级**。先修正默认画面和首屏，再补教学闭环，最后补曲库与音频；
每阶段独立提交、独立验收、独立可回滚。不重写时间轴、状态机与既有测试体系。

---

## 2. 目标与非目标

### 2.1 目标

1. **第一眼是产品**：全新启动即看到真人角色、完整琴房、可读的手部特写、信息完整的主菜单。
2. **能跟练**：下落音符 + 电脑键盘/鼠标输入 + 等待模式 + 练习评分，形成“看 → 跟 → 评”的闭环。
3. **有内容**：内置 6–8 首公版曲，带中文标题、作曲、难度与时长。
4. **不加债**：保持唯一时间轴、UI 门面约束、Update 零分配与现有测试全绿；新增能力全部可单测。

### 2.2 非目标（本次明确不做）

- AI 生成 3D 人物、照片生成真实 3D 模型（保留现有 Mock 与“缩略图/整体色调”槽位）。
- MP3 / WAV 转谱、真实 MIDI 键盘设备输入（仅电脑键盘与鼠标）。
- 移动端 / Web、Unity 版本升级、渲染管线替换。
- 重写 `LessonTimeline` / `MidiEventScheduler` / 相机状态机 / 既有测试体系。
- 未经许可证登记的任何第三方模型、贴图、音色采样或字体。

---

## 3. 阶段划分与验收

### 阶段 0 · 真实基线（约 0.5 天）

**目的**：先看清用户实际看到什么，避免拿批处理截图当产品。

| 步骤 | 动作 | 落点 |
|---|---|---|
| 0.1 | 新增 PlayMode 截图用例：显式 `RestoreCharacter(RocketboxFemaleId)` 后采集真人全景、特写空闲、特写演奏、主菜单 | `Assets/Tests/PlayMode/VisualSnapshotTests.cs` |
| 0.2 | 输出 `V17_rocketbox_closeup_idle.png`、`V18_rocketbox_closeup_playing.png`、`V19_rocketbox_full.png`、`V20_default_mainmenu.png` | `Docs/screenshots/` |
| 0.3 | 人工核对四项：手部是否自然弯曲、指尖是否贴键、头/躯干是否遮挡、坐姿是否穿模；结论写入升级记录 | `Docs/upgrade-2026-11-01.md` |

**验收**：四张新图生成，`manifest.md` 自动更新；文档如实记录真人特写的缺陷清单。
**决策点**：若真人手部/坐姿明显不合格，阶段 1 先修手部链路，再继续场景与主菜单。

---

### 阶段 1 · 视觉与首屏（约 1.5–2 天）

#### 1.1 特写构图

- **内置程序化角色**：特写模式默认隐藏头、躯干、腿、琴凳，只保留手臂与手；提供开关“特写只显示手部”（默认开），
  逻辑放在 `AvatarManager`，只切网格可见性，不动骨骼与 IK。
- **Rocketbox 导入角色**：全身是一张蒙皮网格，**不拆分、不隐藏身体**。优先用相机构图解决：
  提高俯角、拉近、把焦点下压到键面，尽量让头/躯干退出画面；若阶段 0 实拍仍明显入画，再评估
  世界空间遮罩着色器（仅 High 档）作为兜底，且必须保留蒙皮与手部完整。
- 新增断言：特写模式下「双手掌与 88 键中的活动键范围」在视口内；躯干包围盒中心不在视口中心区域。
- 对外参数保持现有 `CameraModeController` 接口；不新增第二个相机状态。

#### 1.2 琴房场景（扩展 `PianoSceneEnvironment`）

在现有地面/背景/三点光基础上增加：

| 元素 | 做法 | 约束 |
|---|---|---|
| 后墙 / 侧墙 / 顶棚 | 低饱和暖灰墙面，程序化微纹理（noise） | 不抢教学语义色；不新增投影灯 |
| 窗与日光 | 墙面开窗 + 发光窗格（emissive）+ 冷白补光 | 沿用唯一主光投影，窗光只做补光 |
| 地毯 | 钢琴与琴凳下的圆角矩形软毯，程序化绒布纹理 | 不改变地面高度与接触阴影 |
| 墙板 / 装饰画 | 2–3 块低对比声学板 | 不形成高对比色块 |
| 绿植 / 落地灯 | 极简体块与叶片，低面数 | 教学模式下可整体隐藏（沿用环境可见性） |
| 乐谱 | 谱架上的白色纸张 + 程序化五线谱纹理 | 不遮挡键面；不参与手部碰撞 |

**性能边界**：环境 Renderer 控制在 30 个以内；全部共享材质；不新增每帧脚本。

#### 1.3 钢琴细节

- 谱架加乐谱纸张（双页、轻微倾斜、五线谱线纹）。
- 顶盖铰链、铭牌、脚轮、踏板已有；补琴盖边缘高光与琴凳软垫的材质区分。
- 保持 `PianoKeyMap` 的 88 键坐标与所有几何常量不变（手部 IK、相机、测试均依赖）。

#### 1.4 主菜单重做（`MainMenuUI`）

- 结构：品牌区（标题 + 一句定位）→ 继续练习卡 → 曲库卡片网格 → 最近播放 → 底部快捷键说明。
- 曲目卡显示：中文标题、作曲/来源、难度、时长、音符数；数据来自 `Songs/catalog.json`（见阶段 3）。
- 新增“设置”入口：画质档位（自动/标准/高）、主音量、下落音符默认显示、键盘映射说明。
- 全部使用 `UITheme` 令牌；不硬编码颜色 / 字号 / 控件高度。
- 空态 / 错误态仍用居中卡片，保留导入入口。

#### 1.5 默认角色与基线

- 生产默认继续使用 Rocketbox；内置 3 角色作为“风格角色”保留。
- 视觉基线的主镜头（01–06）改为显式使用 Rocketbox；内置角色继续由 V03/V04 等覆盖。
- 重采 22 + 4 张截图，人工核对后更新 `Docs/screenshots/baseline/`。

**验收**：新截图无黑色虚空、无大面积头部遮挡；主菜单信息完整可操作；`--visual` 全通过。

---

### 阶段 2 · 教学闭环（约 2–3 天）

#### 2.1 新增模块（`Assets/Scripts/Practice/`）

| 文件 | 类型 | 职责 |
|---|---|---|
| `FallingNotesModel.cs` | 纯 C# | 音符 → 卷帘矩形数据；按时间窗口与练习手过滤；白/黑键横向映射与音高归一化 |
| `PracticeInputMap.cs` | 纯 C# | 电脑键盘 → 音高；八度切换与范围钳制；屏幕点击 → 音高 |
| `PracticeSession.cs` | 纯 C# | 期望音符组、输入判定、等待状态机、计分（命中/错音/漏音/时序偏差/连击） |
| `PracticeReport.cs` | 纯数据 | 练习结束报告（总数、命中率、平均偏差、最长连击） |
| `PracticeInputController.cs` | MonoBehaviour | 读取键盘/鼠标，转成 `PracticeNoteOn/Off` 调会话；不含判定逻辑 |
| `UI/FallingNotesView.cs` | 自定义 `MaskableGraphic` | 屏幕顶部钢琴卷帘：轨背景、音符条、命中线；左右手分色；零每帧分配 |
| `UI/PracticeHudUI.cs` | 表现层 | 跟弹开关、等待模式开关、分数/连击、键位提示、练习报告卡 |

#### 2.2 会话层扩展（`LessonSessionController`）

新增公开只读状态与命令：

```text
bool FallingNotesVisible / bool PracticeInputEnabled / bool WaitModeEnabled
void SetFallingNotesVisible(bool)
void SetPracticeInputEnabled(bool)
void SetWaitMode(bool)
void PracticeNoteOn(int pitch)
void PracticeNoteOff(int pitch)
bool TryCollectFallingNotes(float from, float to, List<FallingNoteLane> result)
事件：OnPracticeJudged(PracticeJudgment) / OnPracticeReport(PracticeReport)
```

约束：

- `FallingNotesView` 只读 `session.CurrentTime` 与 `TryCollectFallingNotes`，**不直接依赖** `MidiEventScheduler`。
- 练习输入只触发琴键动画、音频、高亮与评分，**不驱动示范人物的手**，避免“老师”和“学生”两套动作打架。
- 等待模式通过 `LessonTimeline.Pause()` / `Play()` 实现，不新建计时器。
- 新增 `MidiEventScheduler.SkipTo(float)`：等待模式按下正确键后，跳过该组事件并手动派发一次，
  保证琴键/音频/手部只触发一次，不出现双击或双声。

#### 2.3 下落音符视图

- 位置：屏幕顶部，宽约屏宽 56%，高约屏高 38%，半透明深色底，带左右手图例；可开关（默认教学开启、展示关闭）。
- 坐标：x 按 88 键白键索引 + 黑键偏移归一化；y 按「音符时间 − 当前时间」映射到命中线以上。
- 颜色：左手冷蓝、右手暖橙（与琴键高亮同源语义）；命中窗口内转暖金；已过未命中灰化。
- 性能：单个自定义 Graphic 一次 `OnPopulateMesh` 绘制全部矩形；顶点列表预分配；只在时间/窗口变化时重建。
- 与相机解耦：自由观察时仍固定在屏幕层；不遮挡底栏与顶栏。

#### 2.4 输入与等待模式

- 键盘映射（两行，白键+黑键）：
  - 低八度：`Z S X D C V G B H N J M , L . ; /`
  - 高八度：`Q 2 W 3 E R 5 T 6 Y 7 U I 9 O 0 P`
  - 八度切换：`←` / `→`，范围 C1–C7；`Esc` 退出练习输入。
- 鼠标：`Camera.ScreenPointToRay` + 键面水平面求交，按 x/z 判定音高（`PianoKeyMap.TryPickPitch` 纯几何查表）；
  点击即按下，松开即抬起。文本输入获得焦点时练习输入让位（沿用 `UITheme.IsTextInputFocused`）。
- 等待模式：
  - 播放到达下一组音符起点前 `WaitLeadSeconds = 0.12s` 暂停；
  - 用户按对全部音高（允许 ±80 ms 内先后）→ 会话手动派发该组并恢复播放；
  - 按错 → 高亮红闪 + 提示“再试一次”，不前进；按 `Space` 可跳过该组；
  - 和弦必须全部按对；分手练习只要求当前练习手；
  - 与变速、A-B 循环、节拍器可组合。
- 评分：每个期望音符记录 `Perfect / Good / Miss / Wrong`；报告显示命中率、平均时序偏差、最长连击；
  不宣称“专业演奏评分”。

#### 2.5 偏好与 UI

- `UserPreferences` 新增：`FallingNotesVisible`（默认 true）、`PracticeInputEnabled`（false）、
  `WaitModeEnabled`（false）、`PracticeOctave`（默认 4，范围 1–7）。损坏/越界回落默认。
- 底栏新增 chip：“跟弹：开/关”“等待：开/关”；教学模式下“高亮”仍固定，但练习输入可开。
- 帮助面板（F1）补一节“电脑键盘弹奏与跟弹”。
- 快捷键：练习输入开启时字母键让位给琴键；`Space` 仍是播放/暂停/跳过等待组。

#### 2.6 测试

- EditMode：`FallingNotesModelTests`、`PracticeInputMapTests`、`PracticeSessionTests`、`PracticeReportTests`
  （窗口过滤、左右手、黑键映射、八度钳制、错音/漏音/和弦、时序容差、计分）。
- PlayMode：`PracticeModeTests`（开关不重置时间轴、等待模式暂停/恢复、输入触发琴键动画、下落图存在且随播放推进）。
- 保持既有 473 + 124 全绿；`static_checks.py` 零违规。

**验收**：不用鼠标也能用电脑键盘弹奏；等待模式下按对才继续；报告数字与输入一致；无双击/双声。

---

### 阶段 3 · 内容与音频（约 1 天）

#### 3.1 公版曲库

- 新增 `Tools/make_demo_songs.py`：纯标准库写 SMF，生成 6–8 首公版旋律 + 简易左手伴奏：
  小星星、欢乐颂、致爱丽丝（片段）、卡农（片段）、土耳其进行曲（片段）、小步舞曲、铃儿响叮当、平安夜。
- 输出到 `Assets/StreamingAssets/Songs/`；保持 `demo.mid` 与 `bench/` 不动。
- 新增 `Assets/StreamingAssets/Songs/catalog.json`：顶层为 `{ "songs": [ ... ] }`（`JsonUtility` 不支持顶层数组），
  每项 `file / title / composer / difficulty / durationSeconds / noteCount / description`。
- `Core/SongCatalog.cs`：用 `JsonUtility` 读取 catalog；缺失或损坏时回落文件名，不抛异常。
- `SongLibraryService` 增加 `TryGetCatalogEntry(path, out entry)`；`MainMenuUI` 用 catalog 展示中文标题、作曲、难度、时长。
- `LoadInitialSong` 优先加载 catalog 第一首（非 demo）。

#### 3.2 音频

- 保持程序化合成为默认（零采样依赖）。
- 每个声部增加**轻量房间混响**（`AudioReverbFilter`，小房间、湿声低），制音声同样处理；
  失败时静默降级，不影响发声池。
- 现有非谐分音、独立衰减、击弦瞬态、制音声、按音高声像保留。
- 真实钢琴采样：**本次不引入**；如后续需要，按 `Docs/asset-licenses.md` 五项指标单独评估与登记。

**验收**：主菜单显示 6+ 首带中文名的曲目；首曲可正常解析、分手、循环；音频初始化失败仍可静音继续。

---

### 阶段 4 · 验证与交付（约 1 天）

1. `python Tools/static_checks.py` 零违规。
2. `python Tools/verify.py --stress --perf --timeout 900` 全绿。
3. `python Tools/verify.py --visual --timeout 900` 生成全部基线；人工核对后更新 `baseline/`。
4. `python Tools/verify.py --player --player-seconds 20` 构建 Windows Player 并冒烟（若环境允许）。
5. 更新 README、`Docs/architecture.md`、`Docs/ui-design-system.md`、`Docs/animation-pipeline.md` 的相关节；
   新增 `Docs/upgrade-2026-11-01.md`（改动、数据、未达标项、复现命令）。
6. 逐阶段提交到 `feature/v4-full-upgrade`；**不自动合并 master**。

---

## 4. 架构与数据流

### 4.1 新增数据流

```text
LessonTimeline（唯一时间源）
   │ CurrentTime
   ▼
LessonSessionController
   ├─ MidiEventScheduler.Tick / SkipTo        （唯一音符事件源）
   ├─ PracticeSession（期望组 / 等待 / 判定）  （纯 C#）
   ├─ FallingNotesModel（窗口查询）           （纯 C#）
   └─ 事件：OnPracticeJudged / OnPracticeReport
   │
   ▼
FallingNotesView（只读会话） · PracticeHudUI（只读会话）
   ▲
PracticeInputController（键盘/鼠标）── PracticeNoteOn/Off ──→ 会话
```

### 4.2 硬约束

- 只有 `LessonTimeline` 计时；下落图与练习判定不自建计时器。
- 只有 `MidiEventScheduler` 派发曲目音符；等待模式下由会话调用 `SkipTo` + 手动派发，仍走同一组订阅者。
- UI 不直接引用 `MidiEventScheduler` / `LessonTimeline`（`static_checks.py` 规则 2）。
- `Update` 路径零分配：无 LINQ、无字符串插值、无每帧 `new`、无 `GetComponentsInChildren`。
- 练习输入不改变曲目、进度、相机与角色状态；等待模式只暂停/恢复时间轴。

### 4.3 错误处理

- 下落图 / 键盘输入 / 练习判定均为本地纯逻辑，不产生网络或权限错误。
- 练习输入初始化失败（例如键位冲突）→ `AppError.Warning` + 自动关闭跟弹，不阻断播放。
- 等待模式跳过 / 连续错音 → 用户可见提示，不写错误横幅。
- 仍遵守既有 `AppError` 三段式文案与 `PathSanitizer` 脱敏。

---

## 5. 测试与验收矩阵

| 层 | 内容 | 命令 |
|---|---|---|
| 静态 | 架构 5 条 + 零分配 + 纯 C# 边界 | `python Tools/static_checks.py` |
| EditMode | 新增 4 组纯逻辑用例 + 既有 473 | `python Tools/verify.py --edit-only` |
| PlayMode | 新增练习用例 + 既有 124 通过/5 跳过 | `python Tools/verify.py` |
| 压力 / 性能 | 既有 Stress 3/3、Perf 4/4；新增下落图与练习输入不劣化 | `python Tools/verify.py --stress --perf` |
| 视觉 | 22 + 4 张新基线；主镜头切换为真人；人工核对 | `python Tools/verify.py --visual` |
| Player | Development Build 冒烟（可选） | `python Tools/verify.py --player` |

**成功标准**：

1. 全新启动：真人角色 + 琴房 + 可读特写 + 完整主菜单，无黑色虚空。
2. 用电脑键盘或鼠标可弹奏；下落音符随播放滚动；等待模式按对才继续；练习结束有报告。
3. 内置曲 ≥ 6 首，中文标题/作曲/难度/时长可读。
4. 编译 0 错误 0 警告；既有测试不减少；新增用例全绿；静态检查零违规。
5. 如实记录未达标项（例如真人手部接触指标、特写遮挡残余）。

---

## 6. 风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| Rocketbox 单张蒙皮网格无法只隐藏躯干 | 特写仍有头/肩入画 | 先拍真实基线；优先相机构图（俯角/距离/焦点）；必要时世界空间遮罩着色器，且不拆蒙皮 |
| 等待模式与调度器重复发声 | 双声/双击 | `SkipTo` + 会话手动派发一次；PlayMode 用例断言发声次数与琴键状态 |
| 下落图遮挡 3D 画面 | 观感变差 | 半透明、可开关、自由观察自动收起；不覆盖底栏/顶栏 |
| 视觉基线全量变化 | 回归比较失效 | 人工核对后整批更新 `baseline/`；旧基线保留在 git 历史 |
| 新增 6 首 MIDI 质量差 | 像测试音阶 | 用公版旋律 + 简单和声，限制音域与速度；主菜单标注“练习编配” |
| 范围膨胀 | 延期 | 阶段独立提交；每阶段结束跑全量验证；未完成项写入文档而不是硬塞 |

---

## 7. 提交策略

- 分支：`feature/v4-full-upgrade`（基于 `feature/realism-phase2`）。
- 每阶段至少一个提交，提交信息用中文，格式：
  `feat(practice): 下落音符与跟弹等待模式` / `feat(visual): 琴房环境与主菜单重做` 等。
- 每个阶段结束保存验证产物（`_verify_out/*.xml` 不入库，截图与文档入库）。
- 不自动合并 `master`；合并前由用户确认。

---

## 8. 附：本设计对应的验收产物

- `Docs/screenshots/V17_…` ~ `V20_…` 与更新后的 `manifest.md`
- `Docs/screenshots/baseline/`（更新后的基线）
- `Docs/upgrade-2026-11-01.md`（执行记录与实测数据）
- 新增测试文件与测试报告
- `Assets/StreamingAssets/Songs/catalog.json` 与 6–8 首 MIDI
