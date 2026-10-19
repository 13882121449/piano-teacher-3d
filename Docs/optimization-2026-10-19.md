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
