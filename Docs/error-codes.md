# 错误码与错误处理（error-codes.md）

> 对应方案 **8.1 统一错误结构**、**8.2 必须覆盖的异常**、**8.3 文案与恢复**、**8.4 日志与隐私**。
> 实现位置：`Core/AppError.cs`（`ErrorSeverity` / `ErrorCode` / `RecoveryAction` / `AppError` / `PathSanitizer`）、
> `Core/ErrorReporter.cs`（上报与文案映射）、`Core/LessonState.cs`（会话状态）、`UI/UIStatusBar.cs`（展示与恢复按钮）、
> `MIDI/MidiFileValidator.cs`（导入校验）、`Core/SongLibraryService.cs`（曲库 IO）。

---

## 表一：错误码 → 严重程度 → 用户文案要点 → 恢复动作 → 恢复按钮文案

`ErrorSeverity` 取值：`Info`（提示）/ `Warning`（降级但可继续）/ `Recoverable`（当前操作失败可恢复）/ `Fatal`（无法继续）。
`RecoveryAction.None` 时 UI 把恢复按钮降级为「知道了」；其余动作的**默认按钮文案**由 `AppError.DefaultRecoveryLabel` 给出。

| ErrorCode | 码值 | ErrorSeverity | 用户文案要点（发生了什么 + 为什么 + 下一步） | RecoveryAction | 恢复按钮文案 |
|---|---|---|---|---|---|
| `SongDirectoryMissing` | 100 | Recoverable | 找不到示例曲目录（StreamingAssets/Songs）；请点击「导入 MIDI」 | ChooseFile | 重新选择文件 |
| `SongListEmpty` | 101 | Recoverable | 示例曲目录里没有可用的 .mid；请导入自己的曲目 | ChooseFile | 重新选择文件 |
| `FileNotFound` | 102 | Recoverable | 找不到这个文件，可能已被移动/重命名/删除；请重新选择文件当前所在位置 | ChooseFile | 重新选择文件 |
| `FileUnreadable` | 103 | Recoverable | 无法读取（无权限 / 被占用）；关闭占用程序或复制到「文档」后再导入 | ChooseFile | 重新选择文件 |
| `FileTooLarge` | 104 | Recoverable | 文件超过 8 MB 导入上限，导入后易卡顿；选更小文件或先裁剪 | ChooseFile | 重新选择文件 |
| `UnsupportedExtension` | 105 | Recoverable | 扩展名不是 .mid / .midi，可能选错文件类型；请选择 MIDI 文件 | ChooseFile | 重新选择文件 |
| `UnsupportedFormat` | 106 | Recoverable | 使用了不支持的时间格式（SMPTE / 非标准结构）；换标准 MIDI 或另存为 Format 0/1 | ChooseFile | 重新选择文件 |
| `MidiHeaderInvalid` | 107 | Recoverable | 无法读取该 MIDI，文件可能损坏或不是标准 MIDI；重新选择或用其他播放器确认 | ChooseFile | 重新选择文件 |
| `MidiNoNotes` | 108 | Recoverable | 文件里没有可弹奏的音符（只有伴奏/控制信息或空文件）；选含钢琴音符的 MIDI | ChooseFile | 重新选择文件 |
| `MidiZeroDuration` | 109 | Recoverable | 时长为零，没有可播放内容；选包含有效演奏的 MIDI | ChooseFile | 重新选择文件 |
| `MidiTooComplex` | 110 | Recoverable | 音符过多或曲目过长，导入后占内存且可能卡顿；选更短/更简单或先精简 | ChooseFile | 重新选择文件 |
| `MidiDanglingNotes` | 111 | Warning | 有 N 个音符缺少结束标记，已按曲目末尾补全，长音可能略长；规范导出的 MIDI 更准确 | None | 知道了 |
| `MidiNoTempo` | 112 | Warning | （保留：无 tempo 事件时已按 120 BPM 处理，通常不单独提示） | None | — |
| `AudioInitFailed` | 200 | Warning | 音频初始化失败，当前无法发声；可静音继续练习，或检查音频设备后重试 | ContinueMuted | 静音继续 |
| `AvatarInvalid` | 201 | Warning | 当前人物缺少可用的手部骨骼，已切回默认人物；曲目播放未受影响 | UseDefaultAvatar | 切回默认人物 |
| `AvatarBonesMissing` | 202 | Warning | （保留：与骨架校验同义，供未来细分使用） | UseDefaultAvatar | 切回默认人物 |
| `CameraTargetLost` | 203 | Warning | （保留：相机焦点丢失时回退安全预设镜头，通常静默处理） | None | — |
| `LoopRangeInvalid` | 204 | Recoverable | 当前循环区间无效，已关闭循环；请重新设置开始点和结束点 | ResetLoop | 重新设置循环 |
| `NoSongLoaded` | 205 | Recoverable | 还没载入曲目，无法执行该操作；先在曲库选示例曲或导入 MIDI | ChooseFile | 重新选择文件 |
| `HandHasNoNotes` | 206 | Warning | 本曲目中该手没有音符，将保持静音；改练另一只手或「双手」，或换曲目 | None | 知道了 |
| `Busy` | 207 | Warning | 正在执行上一个操作，请稍候；长时间无反应可稍后再试 | None | 知道了 |
| `Unknown` | 999 | Recoverable | 兜底：操作未完成，请重试或重新选择文件 | ChooseFile | 重新选择文件 |

> 说明：`Info` / `Fatal` 当前未在业务路径使用（`Info` 由 `ImportCheck.Success().ToError()` 返回「文件可用」；`Fatal` 保留枚举值）。
> 「保留」条目表示枚举已定义但当前没有触发路径，供后续细分；**不声称已使用**。

---

## 表二：方案 8.2「必须覆盖的异常」→ 实现位置 → 覆盖测试

| # | 异常（方案 8.2） | 用户表现 | 实现位置 | 覆盖它的测试 |
|---|---|---|---|---|
| 1 | **没有可用曲目** | 空状态页 +「导入 MIDI / 播放示例曲」 | `LessonSessionController.RequireSong` → `ErrorReporter.NoSong`；`LessonControlsUI.BuildEmptyState` | `UIThemeTokenTests.PlayButtonLabel_ReflectsStateAndLoading`、`CanTriggerSongActions_BlocksWhenNoSongLoadingOrError`；`PlayModeErrorHandlingTests` |
| 2 | **文件格式无效 / 损坏** | 说明支持格式并建议检查文件；保留已加载曲目 | `MidiFileValidator.Validate` → `ImportCheck.ToError`；`ErrorReporter.FromMidiFailure`；`LessonSessionController.FailLoad`（有旧曲则回滚保留） | `SongLibraryTests.Validate_CorruptedMidi_ReturnsHeaderInvalid`、`MidiParserTests.Parse_MissingMThd_ReturnsFail`、`Parse_CorruptedHeaderChunkId_ReturnsFail` |
| 3 | **MIDI 无音符 / 时长为零** | 提示文件无可播放音符；不进入 Playing | `MidiFileValidator.Validate`（第 7、8 步） | `Validate_NoNotes_ReturnsNoNotes`、`Validate_ZeroDuration_ReturnsZeroDuration` |
| 4 | **极端 tempo / 大量事件** | 提示复杂并建议换曲目；限制事件数与时长 | `MidiParser`（tempo 回落 + warning）；`MidiFileValidator.Validate`（TooComplex）；`ErrorReporter.TooComplex` | `MidiCompatibilityTests.Parse_InvalidTempo_FallsBackTo120Bpm_WithWarning`、`Parse_AbsurdlyLargeTempo_FallsBackTo120BpmWithWarning`、`Validate_NoteCountOverLimit_ReturnsTooComplex`、`Validate_DurationOverLimit_ReturnsTooComplex` |
| 5 | **文件被删除 / 无权限** | 提示重新定位；区分「路径失效」与「格式错误」 | `SongLibraryService.TryResolve`（路径失效）；`MidiFileValidator.Validate`（无权限）；`ErrorReporter.FileMissing` | `SongLibraryTests.TryResolve_AfterFileDeleted_ReturnsProblemInsteadOfThrowing`、`TryResolveRecent_UnknownId_ReturnsProblem`、`Validate_MissingFile_ReturnsMissingWithChooseFileRecovery` |
| 6 | **音频初始化失败** | 可选择静音预览或重试；显示音频状态 | `PianoAudioSynth.Initialize`（置 `Initialized=false` + `LastInitError`）；`LessonSessionController.PreflightSubsystems` → `ErrorReporter.AudioInitFailed`；`HandleRecovery` → `ContinueMuted` | `AudioReleaseTests.Initialize_WithoutAudioAvailability_ReportsChineseErrorAndStaysSafe`、`NoteEvents_BeforeInitialize_AreSafeNoOps`、`Muted_NoteOnDoesNotOccupyAnyVoice` |
| 7 | **模型 Avatar 不合法 / 骨骼缺失** | 使用默认角色并提示；不影响曲目播放 | `AvatarDefinition.Validate`；`AvatarManager.BuildAvatars`（失败回退 `CreateDefault()`）；`ErrorReporter.AvatarInvalid` | `PlayModeErrorHandlingTests`（角色校验与回退）；`AvatarDefinition.Validate` 覆盖 Humanoid / 骨骼 / 比例 / 手部可达 |
| 8 | **相机焦点丢失 / 角色正在替换** | 回退到安全预设镜头；不修改时间轴 | `CameraModeController.Initialize`（备用构图）、`InvalidateFraming()`（角色/切曲后重算，不追旧目标） | `CameraVisibilityTests.Scene6_PauseThenSwitchViewThenResume_RecomputesHandTargets`、`Scene9_ThreeAvatars_X_TwoViews_AllFullyFramed`、`CameraFramingTests.InvalidateFraming` 相关 |
| 9 | **用户快速连续点击 / 切换** | 控件呈加载 / 禁用状态；异步加载与角色切换做防重入 | `LessonSessionController.LoadSong`（`_loading` → `ErrorReporter.Busy`）、`CancelLoading`；`UITheme.SetButtonEnabled` + `ButtonState.Loading`；`LessonControlsUI.RefreshInteractable` | `StressAndStabilityTests.Stress_RapidSwitching`（快速切换 ≥ 200 次）；`UIThemeTokenTests.PlayButtonLabel_ReflectsStateAndLoading` |
| 10 | **未捕获异常** | 可恢复错误页 / 重试；记录日志，避免静默吞错 | `LessonSessionController.CommitSong`（`try/finally`，失败回滚到明确状态）、`SavePreferences`（try/catch）；`ErrorReporter.Report` 广播 `OnError` | `PlayModeErrorHandlingTests`；`SongLibraryService` 各 IO 路径（`Import` / `TryResolve` / `Save` / `Load` 全部返回失败而非抛出） |

> 说明：方案 8.2 的 10 类异常**全部有对应实现路径**；部分异常以「返回失败 + 提示」而非抛异常处理（更符合「不静默吞错、不崩溃」的要求）。

---

## 表三：日志与隐私规则（方案 8.4）

| 规则 | 实现 | 说明 |
|---|---|---|
| 日志包含**时间 / 模块 / 错误码 / 课程状态 / 上下文** | `ErrorReporter.Report` 输出 `[PianoTeacher3D][yyyy-MM-dd HH:mm:ss][模块][state=LessonState] [AppError.ToLogString()]` | `state=` 来自 `StateProvider`（会话层注入 `() => _state`） |
| 按严重程度选择日志级别 | `ErrorSeverity.Info → Debug.Log`；`Warning → Debug.LogWarning`；`Recoverable/Fatal → Debug.LogError` | — |
| **路径脱敏** | `PathSanitizer.Sanitize`（压缩为「…\文件名」）；`PathSanitizer.SanitizeMessage`（把异常消息中的绝对路径替换为脱敏形式，超 512 字符截断） | `ErrorReporter.Report` 在写日志前对 `error.Detail` 调用 `SanitizeMessage`；用户文案保持原样 |
| **不记录完整个人文件路径 / 私人歌曲元数据 / 照片内容** | 上下文只放模块名、错误码、会话状态、安全内部 ID（不含用户目录结构） | 导入 ID 为 `StableId`（FNV-1a 64 哈希） |
| **默认不上传**日志 / 曲目 / 照片 | `ErrorReporter` 无任何网络访问 | 无云端服务 |
| **最小保留原则；支持删除与清除缓存** | `SongLibraryService.Forget(id)`（删用户副本 + 移除记录）、`ClearRecent()`、`ClearCache()`（清用户导入副本与最近记录） | 内置示例曲不受影响；只删除用户目录内的文件（含目录穿越防护 `IsInsideUserDirectory`） |
| **不把堆栈直接显示给普通用户** | `UIStatusBar.ShowError` 只展示 `UserMessage`；`Detail` 仅写日志 | — |
| 持久化失败不影响使用 | `SongLibraryService.Save` / `UserPreferences.Save` 失败只记录诊断，不抛异常 | — |

---

## 三段式文案示例（≥ 5 条，全部来自实际实现）

> 结构：**发生了什么 + 为什么可能发生 + 下一步做什么**。以下文案与源码 `ErrorReporter` / `MidiFileValidator` 一致。

1. **MIDI 损坏 / 非标准**（`MidiHeaderInvalid`）
   > 无法读取该 MIDI 文件。文件可能损坏或不是标准 MIDI 文件。请重新选择文件，或先用其他 MIDI 播放器确认文件可用。

2. **人物骨骼不可用**（`AvatarInvalid`）
   > 当前人物缺少可用的手部骨骼，已切回默认人物。曲目播放未受影响。

3. **循环区间无效**（`LoopRangeInvalid`）
   > 当前循环区间无效，已关闭循环。请重新设置开始点和结束点。

4. **没有载入曲目**（`NoSongLoaded`）
   > 还没有载入曲目，无法播放。请先在曲库中选择一首示例曲，或点击「导入 MIDI」导入自己的文件。

5. **音频初始化失败**（`AudioInitFailed`）
   > 音频初始化失败，当前无法发声。你可以选择静音继续练习，或检查音频设备后重试。

6. **文件已不在原位**（`FileNotFound`）
   > 找不到这个文件。它可能已被移动、重命名或删除。请重新点击「导入 MIDI」，选择文件当前所在的位置。

7. **曲目过于复杂**（`MidiTooComplex`）
   > 这首曲目过于复杂（{N} 个音符 / 约 {X} 秒）。导入后会占用大量内存并可能卡顿，请选择更短或更简单的曲目，或先在 MIDI 软件中精简后再导入。

8. **某手无音符**（`HandHasNoNotes`）
   > 本曲目中{左手/右手}没有音符，该手将保持静音。请改用另一只手或「双手」练习，或换一首曲目。

---

## 恢复动作的落地（`LessonSessionController.HandleRecovery`）

UI 错误横幅的恢复按钮点击后调用 `HandleRecovery()`，由**会话层**执行对应动作（UI 只负责触发，不做业务判断）：

| RecoveryAction | 会话层动作 |
|---|---|
| `ChooseFile` | 再次打开应用内文件选择器 `ImportMidi()` |
| `Retry` / `ReloadSong` | 重新载入当前曲目路径；无路径则改为导入 |
| `ResetLoop` | `LessonTimeline.ClearLoop()` |
| `UseDefaultAvatar` | `SwitchAvatar(0)` |
| `ContinueMuted` | `PianoAudioSynth.Muted = true` |
| `None` | 仅关闭横幅（按钮文案「知道了」） |
