# 2026-10-19 优化执行记录

> 依据文档：`piano-teacher-3d-optimization-plan.md`（《Piano Teacher 3D — 技术审阅与优化升级方案》）
> 执行口径：**只执行该文档明确列出的条目**（第 7 节清单及其引用的 P0/P1 条目），不新增文档未提及的功能。
> 本轮执行时间窗：2026-10-19 13:08–13:5x（49 分钟）。

## 0. 基线（对应 §7 清单第 1 项）

| 项 | 值 |
|---|---|
| 仓库 / 分支 | `13882121449/piano-teacher-3d` / `master` |
| 起始 commit | `e5aa7fb`（docs: 手部网格设计契约、指标口径更新与完整项目说明） |
| 本轮提交 | `3b73d4d`（feat(core): 原子写入、画质 Auto 优先级与声部抢占计数） |
| Unity | `2022.3.22f1c1`（revision `c3cbd310e76e`） |
| Python | 3.13.14（`Tools/verify.py` 只用标准库） |
| 机器 | i9-14900HX / RTX 4060 Laptop 8GB + Intel UHD / 15.6 GB |

测试基线（本轮实测，Editor `-batchmode -nographics`）：

| 阶段 | 结果 | 产物 |
|---|---|---|
| 编译 | 0 错误 | — |
| EditMode | **311/311 通过**、0 失败、0 跳过（含本轮新增 10 例） | `_verify_out/editmode.xml` |
| PlayMode | **90 通过、0 失败、5 跳过**（95 总数） | `_verify_out/playmode.xml` |

> 这组数字**不能**代表真实 Windows Player 的帧时间/内存表现（文档 P0-01 明确要求区分），见第 3 节。

---

## 1. 本轮已完成的优化

### 1.1 P1-10 / §7 清单第 9 项 — 曲库与偏好保存的事务性与错误恢复

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Core/AtomicFileWrite.cs`（新增） | 原子写入工具：先写同目录 `*.tmp` → `Flush(true)` 落盘 → `File.Replace` 原子替换，替换前保留 `*.bak`；失败清理临时文件、目标文件保持原状；另提供原子复制 `TryCopyFile`（复制后校验长度） |
| `Assets/Scripts/Core/UserPreferences.cs` (`Save`) | `File.WriteAllText` 直接覆盖 → `AtomicFileWrite.TryWriteAllText`，异常退出不再可能写出半截 JSON |
| `Assets/Scripts/Core/SongLibraryService.cs` (`Save`) | 曲库索引 `library.json` 同上，并生成 `library.json.bak` |
| `Assets/Scripts/Core/SongLibraryService.cs` (`Import`) | 曲目复制改为「同目录临时文件 → 长度校验 → 原子替换」；目录创建失败、复制失败仍返回结构化 `ImportResult`（保留原有用户提示语义） |
| `Assets/Scripts/Core/SongLibraryService.cs` (`Load`) | 主索引解析失败时回退 `library.json.bak`；**解析成功即视为有效**（空列表是合法状态，不会误回滚旧备份） |

对应文档原文：「曲目复制先写同目录临时文件，完成大小/解析校验后原子重命名；曲库索引和偏好 JSON 同样先写 `.tmp`，成功后替换，并在必要时保留最近有效备份」（P1-10 改造思路）。

### 1.2 P1-07 / §7 清单第 10 项 — 画质 Auto / 用户覆盖优先级

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Core/UserPreferences.cs` | 新增 `QualityTierUserOverride`（默认 `false` = Auto）；`Defaults.QualityTier` 由 `1`(High) 改为 `0`(Standard)；新增 `ResolveQualityTier()`（Auto → `QualityProfile.DetectRecommended()`）、`SetQualityTierOverride()`、`UseAutomaticQuality()` |
| `Assets/Scripts/Core/LessonSessionController.cs` | `BuildScene()` 的档位来源由 `NormalizeTier(_prefs.QualityTier)` 改为 `_prefs.ResolveQualityTier()`；`SetQualityTier()` 记录覆盖标记；新增 `UseAutomaticQuality()` 与 `IsQualityAutomatic`（恢复自动的入口 + UI 显示依据） |

对应文档原文：「区分 `Auto` / `Standard` / `High` 或增加 `UserOverride`。首次启动走设备建议；用户手动选择后保存覆盖标记」（P1-07）；完成标准「首次启动不盲目应用 High」（§7 第 10 项）。

旧偏好文件没有该字段，反序列化后 `false` → 自动回到 Auto，不需要迁移脚本。

### 1.3 P1-04 — 24 声部池的 voice-stealing 计数

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Piano/PianoAudioSynth.cs` | `PianoVoiceAllocator.StealCount`：池满走 `FindVictim()` 时 +1；同音高复用、有空闲槽位时不计。新增 `PianoAudioSynth.VoiceCapacity` / `VoiceStealCount` 对外暴露 |

对应文档原文：「增加最大同时发声数与 voice-stealing 计数，明确抢占最旧音、最低优先级音或淡出释放的规则」（P1-04 改造思路）。
抢占顺序本身已实现为「正在淡出 → 更弱 → 更旧」，本轮只补可观测计数，**未改变**抢占策略。

### 1.4 §7 清单第 2 项 — 跳过项逐项说明与门禁分层

`Docs/how-to-run.md` 新增第 11 节：本轮实测的 5 个跳过用例逐项列出跳过原因与补跑命令（Perf×2、Soak×1、视觉比对×1、视觉截图×1），并给出 PR / 主分支 / 夜间 / 发布候选四层门禁约定，明确「跳过 ≠ 通过」。

### 1.5 新增自动化测试

| 文件 | 用例 |
|---|---|
| `Assets/Tests/EditMode/AtomicWriteAndQualityTests.cs`（新增） | 8 个：原子写入成功无 `.tmp` 残留、覆盖保留 `.bak`、空路径不抛异常、原子复制覆盖/失败不产生产物、Auto 走设备建议、覆盖值生效、恢复自动 |
| `Assets/Tests/EditMode/AudioReleaseTests.cs`（追加） | 2 个：`StealCount` 只在池满时递增（同音高复用不计）、淡出归还槽位后不计抢占 |

### 1.6 顺带修复（同一文件内的既有缺陷，与「偏好持久化完整性」直接相关）

`UserPreferences.CopyFrom()` / `ResetToDefaults()` **漏掉 `DisplayMode`**：用户切到「教学模式」后，保存再读取会静默回到「展示模式」。已在两处补齐字段拷贝与默认值（`UserPreferences.cs`）。这是 P1-10 目标文件内的数据丢失缺陷，不属于新增功能。

---

## 2. 未完成项与原因

按文档顺序，本轮**未执行**以下条目（时间窗 49 分钟 + 依赖不满足）：

| 文档条目 | 状态 | 原因 |
|---|---|---|
| §7-1 完整基线（真实 Player 帧时间/GC/内存） | 未完成 | 需要 Windows Development Build + GPU runner 与固定测试场景；本轮只有 Editor `-batchmode` 数据（已在上表如实标注） |
| §7-3 手部五项指标统一口径 | 未完成 | `HandMotionMetrics` 现只有 `WristTargetError` 等；补 `FingertipTargetError` / `KeyTopDistance` / `TransitionPeakError` 需配套标注曲目集与运行时采样，成本 L |
| §7-4 真实 Player 性能基线 | 未完成 | 同 §7-1 |
| §7-5 左右手语义映射 | 未改动 | 前序提交 `cb253b5` 已建立 `FingerAssignmentService` + `HandFingerCorrespondenceTests`，本轮 PlayMode 全绿；文档要求「用固定音符 + 骨骼绑定复现根因」，本轮无足够时间做源码级复核，故不臆断改动 |
| §7-6 动态手部 IK 精度 | 未完成 | 依赖 §7-3 指标口径与标注集，且文档要求「先修映射再调曲线」，成本 L |
| §7-7 `Update()` / 高亮 / 取景脏标记 | 未改动 | 文档明确「仅当 Player Profiler 证实是热点时才改」，本轮无 Player Profiler 数据 → 按文档不动 |
| §7-8 可取消分阶段曲目加载 | 未完成 | M–L：涉及加载状态机、线程边界与 UI 取消入口，49 分钟内做不完，强行实现会引入并发风险 |
| §7-11 静态分析 / 依赖门禁 | 部分已有 | `.editorconfig` 前序已存在（67 行，命名/格式规则齐全）；本轮未新增 CI workflow，避免提交无法验证的流水线 |
| §7-12 音源接口 / TMP 迁移 | 未开始 | 文档标注 P2、非第一批硬阻断 |

---

## 3. 验证与残留风险

- 编译 0 错误；EditMode 311/311 通过（含新增 10 个用例）。
- PlayMode 在全部改动落地后复跑：90 通过 / 5 跳过 / 0 失败；5 个跳过项已在 `Docs/how-to-run.md` 第 11 节逐项登记。
- 残留风险：`--perf` / `--soak` / `--visual` 仍是 Editor 内循环，不等于真实 Player 结论；`library.json.bak` 只在主文件**解析失败**时回退，磁盘满导致的写入失败只会保留原文件（符合「不破坏既有数据」要求，但没有用户提示，属 P1-03/P1-10 的 UI 反馈缺口）。

---

## 4. 第二轮（2026-10-19 18:36–19:0x）— 按 §7 清单顺序继续

> 起点：`b4e24cf`（工作区干净）。本轮只执行优化文档第 7 节列出的未完成项，不新增文档未提及的功能。

### 4.1 §7-5 左右手语义映射：先用固定音符 + 骨骼绑定复现，再修三处根因

**复现（不靠读代码猜）**：新增 PlayMode 用例 `Hands_ThumbFacesBodyMidline_OnBothHands`，对**可见手部**与 **Humanoid 骨架**同时断言
「拇指根比小指根更靠近身体中线」「拇指链前向带朝身体中线的分量」。修复前实测（左手）：

```
左手：拇指根应比小指根更靠近身体中线（拇指 -0.030 m，小指 +0.023 m）
  Expected: greater than 0.0228
  But was:  -0.0303
```

即：**手的位置对（左手确实在低音侧）、但手型是右手的手型**（拇指长在小指一侧）。这类缺陷不会被
「哪只手弹哪个键」「哪个手指按哪个键」的测试发现，必须直接量手性。

**三处根因（都已修）**

| # | 位置 | 根因 | 修法 |
|---|---|---|---|
| 1 | `AvatarManager.BuildHand` | 同一个 `side`（−1 = 左手）同时决定腕部位置与网格镜像；位置要 −1、手性要 +1 | 拆成 `positionSign`（左手局部 −X）与 `inward`（拇指朝身体中线：左手 +1 / 右手 −1）；网格 `Side`、掌指关节顺序、拇指偏航全部改用 `inward` |
| 2 | `AvatarRigBuilder.BuildFinger` | 同一个 `mirror` 同时决定横向顺序（需要 −1）与拇指偏航（需要 +1），拇指根在内侧、拇指链却朝外折 | 显式用 `inward`：`lateral=(2-index)*0.014*inward`、`yaw=FingerChainYaw*inward` |
| 3 | `HeuristicFingeringProvider` | 左右手共用「音高越高 → 编号越大」映射：左手低音分到**拇指**（应为小指），C3-E3-G3 得到 1-3-5（应为 5-3-1） | 语义编号仍是 0=拇指…4=小指；左手镜像：单音 `direction=-1`，和弦模板 `4 - template[i]` |

第 3 条是「看起来像左右手反置」的**第二个独立来源**：只修手的几何，左手仍会用右手的指法弹。
文档 P0-02 补充第 1、2、5 条正是要求「统一解剖学语义 + 单一映射链 + 不按 X 轴一刀切」。

**改动文件**：`AvatarManager.cs`、`AvatarRigBuilder.cs`、`HandMeshFactory.cs`（`Side` 语义注释）、
`AvatarDefinition.cs`（`KnucklePositions` 注释）、`HeuristicFingeringProvider.cs`。

**测试**（`HandFingerCorrespondenceTests`，按文档要求补充）

| 用例 | 断言 |
|---|---|
| `Hands_ThumbFacesBodyMidline_OnBothHands` | 两手：拇指根比小指根更靠中线；拇指链方向朝内（点积 > 0.3）；骨架拇指链方向同样朝内 |
| `Hands_LeftHandIsOnLowNoteSide` / `Hands_LowNotesArePlayedByLeftHand` / `Screen_LowNotesAppearOnTheLeft` | 保留：位置与画面方向不因手性修复而翻转 |
| `LeftHand_LowRegister_UsesLittleFinger`（原 `..._UsesThumb`） | 左手低音用**小指**（原断言期望拇指，等于把右手指法套在左手上，已按解剖学更正） |
| `LeftHand_Chord_UsesMirroredTemplate` | 左手 C3-E3-G3 → `{4,2,0}`（5-3-1） |

> 说明：`BlackKey_AvoidsShortFinger` / `WhiteKey_IsNotShiftedByBlackKeyRule` 两条既有用例原先用「左手低音 → 食指」的期望，
> 同步改为左手镜像后的正确编号（D#3 → 中指；A2 → 无名指），规则本身（黑键避开拇指/食指）未改。

`HandJointPoseTests.PressedChord_CurlsAssignedFingersWithinLimits` 的「不可达」分支也做了口径修正：
旧上界是 `reach`（隐含要求 `distance < 2×reach`），现改为物理残差上界 `max(reach, distance − reach) + 10 mm`。
这是把代理断言改成真实几何关系，**没有**放宽 `MaxStepPerFrame` 或去掉姿态钳制。

### 4.2 §7-3 手部指标报告统一口径

| 文件 | 改动 |
|---|---|
| `HandMotionMetrics.cs` | 新增 `PerFrameDisplacement`（每手每帧一个样本的 `MetricStats`，含均值/P50/P95/P99/峰值，与 `MaxStepDistance` 同源）；新增与文档术语对齐的只读别名 `FingertipTargetError`（= 被分配指尖误差）、`KeyTopDistance`（= 指键面法向距离）、`TransitionPeakError`（= 过渡帧误差峰值）；`ToLogString` 输出五项口径 |
| `Docs/animation-pipeline.md` | 新增 §6.4：单位 / 采样频率 / 采样窗口 / 均值与分位算法（含「均值按全部样本、分位按最多 2 万个保留样本」的差异说明）/ 五项指标计算式，以及**所需标注曲目集**（7 类场景、8–16 小节、`annotation/*.mid + 同名 json` 的字段约定） |
| `HandMotionMetricsTests.cs` | 新增 `PerFrameDisplacement_ReportsMeanAndP95_ForBothHands`、`MetricAliases_ShareTheSameUnderlyingStats` |

**如实标注**：标注曲目集**尚未进仓库**，因此历史记录的 38–41 mm 等数字仍来自 `demo.mid`，只能作同机同曲对比，
不能当作优化方案 §8 的验收结论（已写进 §6.4 末尾）。

### 4.3 §7-7 Update / 高亮 / 取景脏标记：数据不支持改动，故不改

按文档要求先取样（`python Tools/verify.py --perf`，本机 Editor `-batchmode -nographics`）：

| 场景 | p50 | p95 | p99 | 最大 | 1% low | GC 增量 |
|---|---|---|---|---|---|---|
| 纯播放（480 帧） | 16.7 ms | 16.8 ms | 16.9 ms | 22.5 ms | 55.3 FPS | +0.02 MB |
| 切换负载（477 帧，24 次切换） | 16.7 ms | 16.8 ms | 17.0 ms | 40.6 ms | 33.9 FPS | +0.99 MB |

判读：**每帧路径没有热点**——p95 与帧预算 16.67 ms 基本重合（vsync 限帧），GC 增量 0.02 MB；
唯一的长帧集中在**切换帧**（max 40.6 ms / 1% low 33.9 FPS），而切换是事件驱动的（`InvalidateFraming` + 换角色），
不在 `UpdateHighlightUpcoming()` / `CameraFraming` 的每帧路径上。

结论：按文档「只有数据证明是热点才改」的口径，**本轮不给 `Update()` / 高亮 / 取景加脏标记**。
切换帧的 40 ms 属另一类问题（切换时的同步重建），应单独用 Player Profiler 定位后再改。

### 4.4 §7-8 可取消、分阶段的曲目加载

**设计（先设计后落地）**

```
主线程                                 后台线程（纯 C#）
--------                               -----------------
LoadSongAsync(path)
  ├─ 防重入（_loading/_loadJob）
  ├─ SetLoading(true) + Queued 事件
  └─ SongLoadJob.Start() ─────────────▶ Reading   : FileInfo（存在性/大小）
                                        Validating: MidiFileValidator.Validate（读取字节+SMF 解析+上限）
                                        Analyzing : SongAnalyzer.Analyze
                                        Ready / Failed / Cancelled（_done 最后写）
Update() 轮询 Stage/IsDone
  └─ CompleteLoadJob()（主线程）
       ├─ 取消 → RestoreIdleState（旧曲、旧播放状态原样保留）
       ├─ 失败 → FailLoad（结构化 AppError）
       └─ Ready → Import（已校验，不重复解析）→ CommitSong（Unity 对象只在这里创建）
```

线程边界：后台只跑 `MidiFileValidator` / `MidiParser` / `SongAnalyzer`（均不依赖 UnityEngine）；
`GameObject`、材质、UI、`JsonUtility` 写索引全部留在主线程。

| 文件 | 改动 |
|---|---|
| `Core/SongLoadJob.cs`（新增） | 阶段枚举 + 后台作业；`_done` 最后写保证结果发布；异常收进 `FailureDetail`（不产生未观察的 Task 异常）；`Cancel()` 幂等 |
| `Core/LessonSessionController.cs` | `LoadSongAsync()` / `BeginLoadJob()` / `CompleteLoadJob()` / `BuildJobError()`；`LoadStage`、`LoadStageLabel`、`OnLoadStageChanged`；`CancelLoading()` 真正取消后台作业；`CommitSong` 支持复用后台分析结果；`ImportMidiFromPath` 改走后台校验 + 主线程复制；同步 `LoadSong` 保留（测试与初始加载语义不变） |
| `Core/SongLibraryService.cs` | `Import(path, ImportCheck? prevalidated)`：复用后台校验结果，避免主线程二次解析；复制仍走 P1-10 的临时文件 + 原子替换 |
| `UI/UIStatusBar.cs` / `UI/LessonControlsUI.cs` | 加载条显示阶段文案（不显示百分比）；订阅 `OnLoadStageChanged` |
| `Tests/PlayMode/AsyncSongLoadTests.cs`（新增） | 4 个用例：正常提交 / 取消后旧曲可播放 / 加载中再次请求被 Busy 拦住（只提交 1 次）/ 文件缺失时保留旧曲 |

### 4.5 §7-1 / §7-4 真实 Windows Player 基线（本轮已取得真实数据）

| 文件 | 改动 |
|---|---|
| `Scripts/Core/PlayerPerfSampler.cs`（新增） | 仅当命令行带 `-pt3dPerf <秒>` 时启用；预热 2 s 后 `Play()`，逐帧采样 `unscaledDeltaTime`，输出 JSON（帧时间分位 / 1% low / GC / Mono / 总分配 / Mesh·Material·AudioClip·GameObject 计数 / 设备与画质信息） |
| `Assets/Editor/PlayerBaselineBuild.cs`（新增） | 只构建 `PianoLesson` 场景的 `BuildOptions.Development` Windows64 Player；输出路径由 `PT3D_PLAYER_OUT` 指定 |
| `Tools/verify.py` | 新增 `--player` / `--player-seconds`：构建 → 运行 `play` 与 `switch` 两个场景 → 把结果按分节写入 `Docs/perf/sampling.md` 与 `Docs/perf/environments.md` |

**实测（2026-10-19 19:01，本机开发机）**

| 项 | 值 |
|---|---|
| 设备 | i9-14900HX（32 线程）/ 16 GB / RTX 4060 Laptop 8 GB / D3D11 |
| 窗口 | 1280×720，VSync 0，targetFrameRate 60，画质 Auto→High |
| 构建 | Development Build，125.5 s，成功 |

| 场景 | 平均 | p95 | p99 | 最大 | 平均 FPS | 1% low | GC 增量 | 总分配 |
|---|---|---|---|---|---|---|---|---|
| play（20 s / 1198 帧） | 16.70 ms | 16.77 ms | 16.93 ms | 34.07 ms | 59.9 | **53.2** | −0.04 MB | 89.9 MB |
| switch（20 s / 1199 帧） | 16.69 ms | 16.73 ms | 16.87 ms | 24.05 ms | 59.9 | **53.4** | +0.06 MB | 89.5 MB |

对文档 §8 目标的判读（**只针对本机这一档设备**）：

- p95 ≤ 16.7 ms：16.77 ms，超出 0.07 ms（0.4%），实质等于帧预算（限帧 60）。
- p99 ≤ 20 ms：16.93 ms，**达标**。
- 1% low ≥ 55 FPS：53.2 / 53.4，**未达标**（最慢 1% 帧平均约 18.8 ms，最长 34 ms）。
- 托管堆 3.5 MB 且 20 s 内基本不增长（−0.04 / +0.06 MB），播放热路径没有持续 GC。

未覆盖：中档机 / 集显两类设备、10–30 分钟 Player Soak、其余代表场景（密集和弦、快速音符、教学模式跟随、连续切曲）。

### 4.6 §7-11 静态分析与依赖门禁 / §7-12 音源接口与 TMP 评估

| 文件 | 改动 |
|---|---|
| `Tools/static_checks.py`（新增） | 三条硬规则：① 纯 C# 模块（MidiParser / MidiFileValidator / PianoNoteFilter / SongAnalyzer）不得引用 UnityEngine；② UI 不得直接依赖 `MidiEventScheduler` / `LessonTimeline`；③ 禁止空 catch（只有注释的 catch 允许，注释即「为什么可以吞」）。外加依赖引用扫描（信息性） |
| `.github/workflows/verify.yml`（新增） | `static` job 跑在 GitHub 托管 runner（无需 Unity）；`unity` job 需 `[self-hosted, windows, unity]` runner 跑 `Tools/verify.py` |

依赖扫描结果（**不自动删包**，符合文档「一次只移除一个、单独 PR 验证」）：

| 包 | 代码引用 | 结论 |
|---|---|---|
| `com.unity.timeline` 1.7.6 | 0 | 与文档记录一致；删包需单独 PR + 全套测试 + Player build |
| `com.unity.animation.rigging` 1.2.1 | 0 | 同上 |
| `com.unity.textmeshpro` 3.0.6 | 仅 `UITheme.cs` 一行注释 | 实际未用；迁移/删除都留到 §7-12 的独立变更 |

**§7-12 评估结论（只评估，不改代码）**

- 音源接口：建议在**真正排期第二套音源时**再抽 `IAudioInstrument`（NoteOn / NoteOff / Muted / Dispose / Status）。
  当前 `PianoAudioSynth` 已是受控声部池（24 槽 + 抢占计数），只有一个实现时抽接口只会增加间接层，没有消费者。
- TMP：UI 文案全部经由 `UITheme.CreateText` 一个入口创建（现用 legacy `Text` + 内置字体），迁移是可行的单点改造；
  但需要生成中文字符集 SDF 字体资产、验证裁剪与包体增量。建议作为独立变更，保留零资源 fallback。

---

## 5. 第二轮验证与未完成项

| 阶段 | 结果 |
|---|---|
| 编译 | 0 错误 |
| EditMode | **314/314 通过**、0 失败、0 跳过（新增 4 例：指标 2 + 指法 2，其中 2 例为按解剖学更正的既有用例） |
| PlayMode | **95 通过、5 跳过、0 失败**（100 总数；新增 5 例：手性 1 + 异步加载 4） |
| Perf（Editor） | 4/4 通过（数据见 §4.3） |
| Player 基线 | 构建成功；play / switch 两个场景各 20 s，数据见 §4.5 |
| 静态检查 | `python Tools/static_checks.py` → 通过（3 条规则 0 违规） |

**仍未完成**

| 文档条目 | 状态 | 原因 |
|---|---|---|
| §7-6 动态手部 IK 精度（指尖均值 ≤ 25 mm 等） | 未做 | 依赖标注曲目集（尚未建立）与 §4.5 的多设备基线；本轮只统一了口径 |
| §7-1 / §7-4 的完整设备矩阵、10–30 分钟 Player Soak | 部分 | 只完成本机独显一档；中档机/集显与长跑需要额外设备与时段 |
| §7-11 删包 | 未做 | 文档要求逐个包单独 PR 并通过全套测试 + Player build；本轮只做到「扫描 + 记录」 |
| §7-12 音源接口 / TMP 迁移 | 只评估 | 文档标注非第一批硬阻断，且需要第二个实现/字体资产才谈得上收益 |
