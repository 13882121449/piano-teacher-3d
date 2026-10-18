# 完成度核对报告（completion-review.md）

> 核对对象：方案《APP《产品与工程综合优化方案》》与审查《PianoTeacher3D-项目审查与优化建议》
> 核对时间：2026-10-16
> 核对方式：逐条对照源码实现位置 + 测试用例名；**状态只有三种：已实现 / 部分实现 / 计划中**
> 实测数据来源：2026-10-16 本机实测（`python Tools/verify.py`）——编译 0 错误 0 警告、EditMode **278/278**、PlayMode **69 通过 + 5 跳过 + 0 失败**、Stress 3/3、Perf 4/4、Soak 1/1（跳过项均为 `[Explicit]` 或需 GPU 的视觉用例）

> **诚实性说明**：本表不把「计划中」写成「已实现」；无自动化测试的功能标注为「人工」验收。
> 「验收方式」列给出可执行的证据（测试类名 / 截图 / 人工核对点）。

---

## 一、总览

| 维度 | 结论 |
|---|---|
| 核心学习闭环（方案 2.0 P0） | **已实现**：选曲 / 导入 / 播放控制 / 统一时间轴 / 三视角 / 慢速分手循环 / 节拍器 / 高亮 / 统一错误 |
| 视觉与学习体验（方案 2.0 P1） | **部分实现**：程序化精修已做；替换为许可证清晰的高质量模型、真实音色采样仍**计划中** |
| 高级能力（方案 2.0 P2） | **计划中**：AI 动作生成 / 照片生成 3D 人物 / MP3-WAV 转谱 / 跨平台 |
| 工程治理 | **已实现**：状态机 / 切曲事务 / 错误码 / 曲库服务 / 偏好持久化 / CLI 与测试矩阵 |

---

## 二、方案第 2 章「功能范围与优先级」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 2-P0-1 | 曲目选择 / 导入 / 信息 | `UI/MainMenuUI`、`Core/SongLibraryService`、`Core/SongMetadata` | 已实现 | `SongLibraryTests`、`V10_mainmenu.png` |
| 2-P0-2 | 播放 / 暂停 / 停止 / 重播 / 拖动进度 | `Core/LessonSessionController.PlayPause/Pause/Stop/Restart/Seek`、`UI/LessonControlsUI` | 已实现 | `LessonTimelineTests` + 人工 |
| 2-P0-3 | 同一时间轴同步音乐 / 琴键 / 手部 / 拍点 / UI | `Core/LessonTimeline` | 已实现 | `LessonTimelineTests`、`PlayModeIntegrationTests` |
| 2-P0-4 | 三视角稳定切换语义 | `Camera/CameraModeController` | 已实现 | `CameraFramingTests`、`CameraVisibilityTests` |
| 2-P0-5 | 慢速 + 分手 + A-B + 节拍器 + 高亮可组合不残留 | `LessonSessionController.SetSpeed/SetPracticeHand/SetLoop/SetMetronomeEnabled/SetHighlightMode` | 已实现 | `TeachingFeatureTests` + 人工 |
| 2-P0-6 | 统一错误提示 / 加载状态 / 无曲目状态 | `Core/ErrorReporter`、`UI/UIStatusBar` | 已实现 | `PlayModeErrorHandlingTests`、`[Docs/error-codes.md]` |
| 2-P1-1 | 替换程序化低模人物为质量合格模型 | — | **计划中**（Sprint 3 之后，方案 5.2 中期） | 人工 |
| 2-P1-2 | 改善钢琴材质 / 灯光 / 阴影 / 构图 | `Piano/PianoKeyMap`、`Piano/PianoMeshFactory`、`Piano/PianoSceneEnvironment` | 部分实现（程序化精修；无真实 AO/后处理） | `PianoGeometryTests`、`SceneEnvironmentTests`、`V03/V04` |
| 2-P1-3 | 角色 / 练习 / 分析面板统一视觉语言 | `UI/UITheme`、`UI/UISegmentedControl` | 已实现 | `UIThemeTokenTests`、`[Docs/ui-design-system.md]` |
| 2-P1-4 | 最近曲目 / 播放位置记忆 / 练习设置记忆 | `Core/SongLibraryService`、`Core/UserPreferences` | 已实现 | `SongLibraryTests`、`UserPreferences` 测试 |
| 2-P1-5 | 评估音色采样替代波形合成 | 文档：`[Docs/asset-licenses.md]` 第 3.3 节 | 计划中（已给出评估口径） | 人工 |
| 2-P2-1 | 照片生成 / 定制虚拟人物 | — | 计划中 | 人工 |
| 2-P2-2 | AI 手部动作生成 | — | 计划中（提供器接口已就绪，见 10 章） | 人工 |
| 2-P2-3 | MP3 / WAV 转谱 | — | 计划中 | 人工 |
| 2-P2-4 | Android / iOS / Web 适配 | — | 计划中（Sprint 5） | 人工 |

---

## 三、方案第 3 章「信息架构与 UI 交互方案」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 3.1 | 四区结构（顶栏 / 3D 演奏区 / 进度区 / 主控制 / 次级工具） | `UI/LessonControlsUI`（主播放栏 + 练习工具栏 + 分析面板）、`UI/UIStatusBar` | 已实现 | `UIThemeTokenTests.LayoutTokens*`、人工看图 |
| 3.2 | 主操作始终可见；次级工具可折叠 | `LessonControlsUI.BuildMainBar/BuildPracticeToolbar` | 已实现 | 人工 + `ShouldFoldPracticeTools_FoldsOnNarrowOrCloseup` |
| 3.3 | 统一色板 / 按钮七态 / 字号层级 / 间距 / 圆角 / 控件高度 | `UI/UITheme`（`ButtonState` 七态、`FontPageTitle…`、`Space4/8/12/16/24`、`RadiusSmall/Medium/Large`、`HeightPrimary…`） | 已实现 | `UIThemeTokenTests`（`ButtonState_HasExactlySevenDocumentedStates` 等） |
| 3.3 | 非颜色区分（文字 / 图标 / 边框 / 标记） | `UISegmentedControl`（`▍` + 描边）、`SetOutline`、`StateGlyph` | 已实现 | `StateGlyph_DistinguishesStatesWithoutColor`、`ErrorSeverityLabels_AreDistinguishableByTextNotOnlyColor` |
| 3.4 | 每个开关显示当前状态（「节拍器：开」） | `UITheme.CreateToggleButton/SetToggleState` | 已实现 | `UIThemeTokenTests` + 人工 |
| 3.4 | 破坏性操作二次确认 | `UIStatusBar.ShowConfirm`、`MainMenuUI.RequestRemoveRecent` | 已实现 | 人工 |
| 3.4 | 切换不重启曲目 | `LessonSessionController.AfterCameraModeChanged/AfterAvatarChanged/SetPracticeHand` | 已实现 | `PlayModeIntegrationTests` |
| 3.4 | 键盘焦点顺序 + 鼠标 / 键盘触发 | `UITheme.ChainHorizontal/SetExplicitNavigation` | 已实现 | 人工 |
| 3.4 | 文本输入聚焦时快捷键让位 | `UITheme.IsTextInputFocused/IsSelectableFocused` | 已实现 | 人工 + `SetOutline` 相关断言 |
| 3.4 | 窄窗口优先收纳次级工具 | `UITheme.ShouldFoldPracticeTools`、`UIStatusBar.ApplyResponsiveLayout` | 已实现 | `LayoutTokens_FitAtAllRequiredResolutions`、`NarrowWidthThreshold_SitsBetweenCommonWindowSizes` |

---

## 四、方案第 4 章「三种视角的统一设计」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 4.1 | 相机显式状态机（Closeup / Overview / FreeLook） | `Camera/CameraModeController`（`CameraMode.Closeup/Full/Free`、`CameraModePreset`） | 已实现 | `CameraFramingTests`、`CameraVisibilityTests` |
| 4.2.1 | 特写焦点由「练习手范围 ∪ 琴键范围」计算（非两腕中心平均） | `FramingPolicy.ComputeCloseupTarget/TryCombineCloseupRegion` | 已实现 | `ComputeCloseupTarget_BothHands_UsesUnionRangeNotWristCenterAverage` |
| 4.2.2 | 距离 / FOV 上下限（避免剧烈拉远） | `FramingPolicy.SolveFraming`、`CloseupFramingTuning` | 已实现 | `ComputeCloseupTarget_HandsApart_IncreasesDistanceAndFov_WithinLimits`、`SolveFraming_ExtremeSpan_RespectsMaxLimits` |
| 4.2.3 | 轻量预测 + 阻尼跟随 | `CameraModeController.TryPredictUpcomingRegion`、`FramingPolicy.Damp` | 已实现 | `Damp_ConvergesMonotonically_NoDivergence` |
| 4.2.4 | 单手练习以主手为主并保留次手可见性 | `FramingPolicy.MergeWithShare` | 已实现 | `MergeWithShare_KeepsPrimaryDominant_AndPartiallyIncludesSecondary` |
| 4.2.7 | 限制垂直跟随 | `FramingPolicy.FocusWithVerticalLimit` | 已实现 | `ComputeCloseupTarget_VerticalFollow_IsLimited` |
| 4.3 | 全景改为前侧 3/4，修复低音端遮挡 | `CameraModeController.OverviewYaw = 34f`、`FramingPolicy.ComputeFullTarget` | 已实现 | `ComputeFullTarget_KeepsA0AndC8InsideFrustum_AndSubjectNotCropped`、`V03_full_arm_motion.png` |
| 4.3 | 不默认把琴体透明化；提供「键盘无遮挡」教学子视角 | `CameraModeController.KeyboardUnobstructedSubView`（默认关闭） | 已实现 | `ComputeFullTarget_KeyboardSubView_KeepsKeyboardInsideFrustum` |
| 4.4 | 自由观察进入 / 返回 / 重置规则 | `CameraModeController.SetMode/ReturnToPreset/ResetView`、`PreviousPreset` | 已实现 | `CameraFramingTests` + 人工 |
| 4.4 | UI 悬停不旋转场景 | `FramingPolicy.ShouldAcceptOrbitInput`、`HandleFreeInput` | 已实现 | `ShouldAcceptOrbitInput_BlocksSceneRotationOverUi` |
| 4.4 | 过渡中断以当前姿态为新起点 | `CameraPoseTransition.Begin` + `CameraModeController.Update`（单过渡实例） | 已实现 | `Transition_Interrupted_StartsFromCurrentPose` |
| 4.5 | 切换期间播放状态 / 曲目 / 时间不变 | `LessonSessionController.AfterCameraModeChanged`（只改表现层） | 已实现 | `PlayModeIntegrationTests` |

---

## 五、方案第 5 章「人物、手部、钢琴与场景画面质量」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 5.1-1 | 手部轮廓与关节（掌部体积 / 拇指连接 / 三段指节 / 左右手差异） | `Avatar/AvatarManager.BuildHand/BuildFingerSegments`、`Avatar/AvatarDefinition.AvatarBodyMetrics` | 已实现（程序化精修） | `V01_closeup_chord.png`、人工 |
| 5.1-2 | 手与琴键接触（指尖落点 / 手腕高度 / 不穿键面） | `Avatar/HandIKController`（指尖目标 + 黑键独立高度）、`Avatar/HandPoseValidator` | 已实现 | `HandMotionTests`、`HandPoseValidatorTests`、`V01/V02` |
| 5.1-3 | 人物轮廓与姿态（肩 / 臂 / 坐姿） | `Avatar/AvatarManager.BuildShoulders/BuildArm/BuildTorso/BuildStool` | 已实现（程序化精修） | `V03/V04`、人工 |
| 5.1-4 | 钢琴结构（键面 / 黑键 / 侧板 / 琴盖 / 底座 / 踏板） | `Piano/PianoKeyMap.BuildCase`、`Piano/PianoMeshFactory` | 已实现 | `PianoGeometryTests` |
| 5.1-5 | 材质与灯光（黑白键反射差异 / 清漆感 / 主光 + 补光） | `PianoKeyMap.CreateMaterial`（材质分层）、`Piano/PianoSceneEnvironment`（三点光） | 已实现 | `SceneEnvironmentTests`、`V03` |
| 5.1-6 | 环境与后处理（接触阴影 / 避免景深泛光） | `PianoSceneEnvironment.BuildContactShadowDecals` | 部分实现（**接触阴影是几何近似，非真实 AO/后处理**） | 人工 + `ApplyQuality_TiersControlShadows_ButKeyLightStaysEnabled` |
| 5.2 短期 | 保留程序化生成精修几何 | `Avatar/AvatarManager` | 已实现 | 设计审阅 |
| 5.2 中期 | 接入许可清晰的高质量模型 + 资源适配层 | `Avatar/AvatarDefinition`（适配层已就绪） | 部分实现（适配层就绪，模型接入**计划中**） | `AvatarDefinition.Validate` 测试 |
| 5.2 中期 | 导入校验（Humanoid / 骨骼 / 比例 / 手部可达） | `AvatarDefinition.Validate` | 已实现 | `PlayModeErrorHandlingTests` / 人工 |
| 5.3 | `FingerPoseController` 升级为可替换指法提供器 | `Avatar/IFingeringProvider`、`Avatar/HeuristicFingeringProvider` | 已实现 | `FingeringProviderTests` |
| 5.3 | 输出指法置信度、低置信度标注「估算指法」 | `FingeringPlan.Confidence/IsEstimated`、`FingerPoseController.LastEstimated` | 已实现 | `OutOfComfortRange_MarksEstimatedAndLowersConfidence` |
| 5.3 | 人工编辑 / 覆盖指法与原始 MIDI 分开保存 | — | 计划中 | 人工 |
| 5.3 | 手部质量评估（指尖距离 / 关节 / 手腕速度 / 手掌方向 / 碰撞 / 穿模） | `Avatar/HandMotionMetrics`、`Avatar/HandPoseValidator` | 已实现 | `HandMotionMetricsTests`、`HandPoseValidatorTests` |
| 5.4 | 保持 88 键与真实尺度；倒角 / 黑键层次 / 材质分层 | `PianoKeyMap`（`WhiteWidth=0.024`、`BlackHeight=0.032`、`WhiteKeyChamfer`） | 已实现 | `PianoGeometryTests`（`DefaultKeybedHeightAndSurfaceMatchDocumentedValues`） |
| 5.4 | 单主光 + 柔和填充 + 有限轮廓光 | `PianoSceneEnvironment.BuildLights` | 已实现 | `Build_CreatesFourManagedLights_AsChildren` |
| 5.4 | 开启阴影前测性能；低端设备降阴影而非移除高亮 | `Core/QualityProfile.Apply`、`PianoSceneEnvironment.ApplyQuality` | 已实现 | `ApplyQuality_TiersControlShadows_ButKeyLightStaysEnabled` |

---

## 六、方案第 6 章「核心功能与数据流优化」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 6.1 | 统一时间轴为唯一时间源 | `Core/LessonTimeline` | 已实现 | `LessonTimelineTests` |
| 6.1 | 区分权威时间 / MIDI 事件时间 / 音频时钟 / 表现插值时间 | `LessonTimeline` + `MidiParser`（tick→秒）+ `PianoAudioSynth`（不推进时间） | 已实现 | 设计审阅 + [`Docs/architecture.md`](architecture.md) 第 2 节 |
| 6.2 | 会话状态机 NoSong/Loading/Ready/Playing/Paused/Error | `Core/LessonState`、`LessonSessionController` | 已实现 | 状态图见架构文档；`UIThemeTokenTests.PlayButtonLabel_ReflectsStateAndLoading` |
| 6.2 | 加载新曲事务（校验 → 临时解析 → 一次性提交 → 清理 → 更新 UI → 失败回滚） | `LessonSessionController.LoadSong/CommitSong/FailLoad` | 已实现 | `PlayModeErrorHandlingTests` + [`Docs/architecture.md`](architecture.md) 第 5 节时序图 |
| 6.3 | 慢速 + 节拍器按课程速度缩放 | `Core/Metronome`（由时间轴驱动） | 已实现 | `TempoAndMetronomeTests` |
| 6.3 | 分手 + 高亮（未练声部不发声且不提示） | `LessonSessionController.UpdateHighlightUpcoming` | 已实现 | `TeachingFeatureTests` |
| 6.3 | A-B 循环边界释放遗留 Note On | `LessonSessionController.ResetPlaybackState`、`LessonTimeline.Update` | 已实现 | `AudioReleaseTests`、`TeachingFeatureTests` |
| 6.3 | 拖动进度后重建按键与提示 | `LessonSessionController.Update`（时间回退检测）+ `MidiEventScheduler.SeekTo` | 已实现 | `PlayModeIntegrationTests` |
| 6.3 | 角色切换 + 视角跟随 | `LessonSessionController.AfterAvatarChanged`（`InvalidateFraming`） | 已实现 | `PlayModeIntegrationTests` |
| 6.3 | 切曲保留速度/视角、重置 A-B | `LessonSessionController.CommitSong`（`ClearLoop`） | 已实现 | 设计审阅 |
| 6.4 | 应用内文件选择器（不要求手动复制） | `UI/FileBrowserUI`、`LessonSessionController.ImportMidi` | 已实现 | `V10_mainmenu.png`、人工 |
| 6.4 | 解析前检查大小 / 扩展名 / 头部 / 事件数 / 时长上限 | `MIDI/MidiFileValidator.Validate` | 已实现 | `SongLibraryTests`（`Validate_*`） |
| 6.4 | 导入成功复制到用户目录 + 安全内部 ID | `SongLibraryService.Import/StableId` | 已实现 | `Import_ValidFile_CopiesIntoUserDirectoryAndRecordsRecent` |
| 6.4 | 最近列表只存必要元数据；失效提示不崩溃 | `SongLibraryService.TryResolve/TryResolveRecent` | 已实现 | `TryResolve_AfterFileDeleted_ReturnsProblemInsteadOfThrowing` |
| 6.4 | Format 2 / 空音符 / 极端 tempo / 缺 Note Off 兼容 + 测试 | `MidiParser`（`Warnings` / `DanglingNoteCount`） | 已实现 | `MidiCompatibilityTests` |
| 6.4 | 不执行导入文件中的命令或脚本 | `MidiParser`（只解析 MIDI 数据） | 已实现 | 设计审阅 |
| 6.4 | MP3/WAV 导入管线 | — | 计划中 | 人工 |

---

## 七、方案第 7 章「性能与跨设备体验」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 7.1 | 设备档位（Desktop High / Standard） | `Core/QualityProfile`（`QualityTier.Standard/High`） | 已实现 | `QualityProfileTests` |
| 7.1 | 低端档只降阴影，不移除重要琴键高亮 | `QualityProfile.Apply`（不触碰高亮 / 灯光） | 已实现 | `ApplyQuality_TiersControlShadows_ButKeyLightStaysEnabled` |
| 7.1 | Mobile / Web 目标 | — | 计划中（需真机验证） | 人工 |
| 7.2 | 可复现性能测试场景 | `PerformanceSamplingTests`（纯播放 / 切换负载） | 已实现（`[Explicit]`） | `--perf` |
| 7.2 | 记录帧时间百分位 / GC（不只平均 FPS） | `PerformanceSamplingTests` 写 `Docs/perf/sampling.md` | 已实现（`[Explicit]`） | `--perf` |
| 7.2 | 高频路径零分配 / 对象池 | `PianoAudioSynth`（预生成 clip、零分配）、`PianoKeyHighlighter`（`MaterialPropertyBlock`） | 已实现 | 代码审阅 + `AudioReleaseTests` |
| 7.2 | MIDI 区间查询用二分 / 游标 | `MidiEventScheduler.CollectNotesInRange` | 已实现 | `TeachingFeatureTests` |
| 7.2 | 视觉质量用质量档位配置，不散落脚本 | `QualityProfile`（唯一判断点） | 已实现 | `For_ReturnsSameCachedInstancePerTier` |
| 7.3 | 音频路径零分配 / 统一处理 Note On/Off、暂停、拖动、切曲、循环 | `PianoAudioSynth.OnNoteOn/OnNoteOff/ReleaseAll`、`PianoVoiceAllocator` | 已实现 | `AudioReleaseTests` |
| 7.3 | 波形合成作为降级模式保留 | `PianoAudioSynth.CreatePianoClip` | 已实现 | 代码审阅 |
| 7.3 | 音色评估五项（延迟 / 衰减 / 力度 / 并发 / 许可） | 文档：[`Docs/asset-licenses.md`](asset-licenses.md) 第 3.3 节 | 计划中（口径已定） | 人工 |
| 7.4 | 桌面输入（鼠标 / 键盘 / 可点击 UI） | `UI/LessonControlsUI.HandleShortcuts`、`CameraModeController.HandleFreeInput` | 已实现 | 人工 |
| 7.4 | 触控 / 真实 MIDI 键盘输入抽象 | — | 计划中（Sprint 5） | 人工 |

---

## 八、方案第 8 章「错误处理与状态反馈」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 8.1 | 统一错误结构 AppError（Code / UserMessage / Detail / RecoveryAction / Severity / Context） | `Core/AppError.cs` | 已实现 | `[Docs/error-codes.md]` |
| 8.1 | 底层只产出数据，会话层决定，UI 只展示 | `Core/ErrorReporter`、`LessonSessionController.Report`、`UI/UIStatusBar.ShowError` | 已实现 | `PlayModeErrorHandlingTests` |
| 8.2 | 必须覆盖的 10 类异常 | 见 [`Docs/error-codes.md`](error-codes.md) 表二 | 已实现（10/10） | 逐条测试名见 error-codes.md |
| 8.3 | 文案三段式（发生了什么 + 为什么 + 下一步） | `ErrorReporter.*` 静态助手、`UITheme.ComposeErrorBody` | 已实现 | `ComposeErrorBody_AlwaysCarriesNextStep` |
| 8.3 | 不只显示 Error / 不只写 Console；不显示堆栈 | `UIStatusBar.ShowError/ShowToast`、`LessonControlsUI.OnErrorFallback` | 已实现 | `PlayModeErrorHandlingTests` |
| 8.4 | 日志含时间 / 模块 / 错误码 / 课程状态 / 上下文 | `ErrorReporter.Report`（`StateProvider`） | 已实现 | `[Docs/error-codes.md]` 表三 |
| 8.4 | 默认不上传；最小保留；支持删除与清除缓存 | `ErrorReporter`（无网络）、`SongLibraryService.ClearCache/Forget` | 已实现 | `ClearCache_RemovesUserCopiesAndRecentList` |
| 8.4 | 路径脱敏 | `Core/AppError.PathSanitizer` | 已实现 | `[Docs/error-codes.md]` 表三 |

---

## 九、方案第 9 章「架构与代码组织的关键建议」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 9.1 | SongLibraryService（导入 / 元数据 / 最近列表 / 失效处理） | `Core/SongLibraryService` | 已实现 | `SongLibraryTests` |
| 9.1 | LessonSessionController（会话协调 + 公开命令） | `Core/LessonSessionController` | 已实现 | 见架构文档 |
| 9.1 | LessonTimeline（唯一时间与状态推进） | `Core/LessonTimeline` | 已实现 | `LessonTimelineTests` |
| 9.1 | MidiParser / MidiEventScheduler | `MIDI/MidiParser`、`MIDI/MidiEventScheduler` | 已实现 | `MidiParserTests`、`MidiCompatibilityTests` |
| 9.1 | AudioPlaybackService（音频策略 / 音色 / Note On-Off 清理） | `Piano/PianoAudioSynth`（+ `PianoVoiceAllocator`） | 已实现（命名保留为 PianoAudioSynth） | `AudioReleaseTests` |
| 9.1 | AvatarManager（角色生命周期与替换） | `Avatar/AvatarManager` | 已实现 | `HumanoidSceneTests` |
| 9.1 | AvatarDefinition（模型 / 骨骼 / 材质 / IK 偏移 / 质量配置） | `Avatar/AvatarDefinition` | 已实现 | `AvatarDefinition.Validate` 测试 |
| 9.1 | CameraModeController（状态机 / 过渡 / 取景约束） | `Camera/CameraModeController` | 已实现 | `CameraVisibilityTests` |
| 9.1 | UIStatePresenter（状态→展示，不改内部字段） | `UI/UIStatusBar` + `UI/LessonControlsUI` | 已实现（职责由两组件分担） | 代码审阅 |
| 9.1 | ErrorReporter（错误编码 / 日志 / 映射） | `Core/ErrorReporter` | 已实现 | `PlayModeErrorHandlingTests` |
| 9.2 | 配置与内容分离 | `QualityProfile` / `AvatarDefinition` / `UITheme` / `UserPreferences.Defaults` 等**纯数据类 + 常量** | 部分实现（**未引入 ScriptableObject**，见架构文档第 6 节取舍） | 代码审阅 |
| 9.2 | 用户偏好与产品默认分离；独立持久化目录 | `Core/UserPreferences` | 已实现 | `UserPreferences_SaveThenLoad_RoundTrips` |
| 9.3 | 保留程序化装配；场景只含入口与全局设置 | `LessonSessionController.BuildScene` | 已实现 | 见架构文档第 8 节 |
| 9.3 | 关键展示场景增加可重复测试曲 / 固定相机 / 固定窗口 | `VisualSnapshotTests`（固定窗口 1280×720） | 已实现 | `--visual` |
| 9.3 | 自动测试判断逻辑，截图 / 人工判断视觉 | `VisualSnapshotTests` + `Docs/screenshots/manifest.md` | 已实现 | `--visual` + 人工 |

---

## 十、方案第 10 章「角色照片定制与 AI 功能的正确位置」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 10 | `IAvatarProvider` 提供器接口 | — | 计划中（当前由 `AvatarManager` + `AvatarDefinition` 承担） | 人工 |
| 10 | `IFingeringProvider` 提供器接口 | `Avatar/IFingeringProvider` + `Avatar/HeuristicFingeringProvider` | **已实现**（可替换） | `FingeringProviderTests`、`Provider_ExposesName` |
| 10 | `IHandMotionProvider` 提供器接口 | — | 计划中（手部动作仍由 `HandIKController` 直接实现） | 人工 |
| 10 | `IAudioToScoreProvider` 提供器接口 | — | 计划中 | 人工 |
| 10 | AI 模块不可用 / 超时 / 异常时回退基础 IK / 预设人物 / 已导入 MIDI | `FingerPoseController.Provider`（null 回退启发式）、`AvatarManager`（校验失败回退默认角色）、`PianoAudioSynth`（初始化失败静音继续） | 部分实现（现有回退已就绪；AI 提供器本身未接入） | `OutOfComfortRange_*`、`AvatarManager` 回退、`Initialize_WithoutAudioAvailability_*` |
| 10 | 照片定制阶段顺序（参数化外观 → 头像贴图 → 完整 3D） | — | 计划中 | 人工 |

---

## 十一、方案第 11 章「测试、验收与质量门禁」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 11.1 | MIDI Format 0/1/2、running status、VLQ、tempo、和弦、空 / 损坏文件 | `MidiParserTests`、`MidiCompatibilityTests` | 已实现 | EditMode |
| 11.1 | 播放 / 暂停 / 继续 / 停止 / 重播 / 拖动 / 切曲状态转换 | `LessonTimelineTests`、`PlayModeIntegrationTests` | 已实现 | PlayMode |
| 11.1 | A-B 循环边界、长音释放、跳转后重建 | `AudioReleaseTests`、`TeachingFeatureTests` | 已实现 | EditMode + PlayMode |
| 11.1 | 速度 / 节拍器 / 高亮 / 分手组合 | `TeachingFeatureTests`、`TempoAndMetronomeTests` | 已实现 | PlayMode |
| 11.1 | 角色 / 相机连续切换不重置时间轴、不重复协程 | `PlayModeIntegrationTests`、`CameraVisibilityTests` | 已实现 | PlayMode |
| 11.1 | 文件删除 / 无权限 / 模型加载失败 / 音频失败恢复 | `PlayModeErrorHandlingTests`、`SongLibraryTests` | 已实现 | PlayMode + EditMode |
| 11.2 | 视觉回归固定截图用例（10+ 场景） | `VisualSnapshotTests`（V01–V12 + 保留 6 张） | 已实现 | `--visual` |
| 11.2 | 自动像素差异作辅助 + 容差 / 关注区域 | `Tools/visual_diff.py`、`VisualBaselineCompareTests`（`[Explicit]`） | 已实现 | `--compare-visual` |
| 11.3 | 验收门槛表（核心功能 / 时序 / 切换 / 手部 / 帧率 / 内存 / 错误恢复 / 可用性） | 见 [`Docs/quality-and-roadmap.md`](quality-and-roadmap.md) | 部分实现（部分门槛待真机定稿） | 人工 + `--perf` |
| 11.4 | 10 分钟以上长跑稳定性 | `StressAndStabilityTests.Soak_PlaysContinuously`（`[Explicit]`） | 已实现 | `--soak` |
| 11.4 | 快速切换压力测试 | `StressAndStabilityTests.Stress_RapidSwitching` | 已实现 | `--stress` |
| 11.4 | 资源加载耗时与失败率采样 | `StressAndStabilityTests.ResourceLoad_Sampling` | 已实现 | `--stress` |
| 11.4 | 集显笔记本 + 中档独显性能测量 | — | **计划中**（未在真机定稿） | `Docs/perf/environments.md` 人工填 |
| 11.4 | 无网络 / 中文空格路径 / 非管理员 / 不同 DPI 测试 | `StressAndStabilityTests.Environment_Matrix_Checklist`（本机自检）+ 人工 | 部分实现（本机自检已做，真机矩阵待测） | `--stress` + 人工 |

---

## 十二、方案第 12 章「分阶段实施路线图」（Sprint 0–5 完成度）

| 阶段 | 完成标准 | 本轮完成情况 | 证据 |
|---|---|---|---|
| **Sprint 0 建立基线** | 可复现的功能与视觉基线；新增优化有验收方法 | **已完成** | 6 张基线截图 + `EditMode 278` / `PlayMode 58`；状态机、错误码表、功能交互表（见相关文档） |
| **Sprint 1 相机与手部可见性（P0）** | 固定测试曲目与三角色下，视角清晰、切换平滑、不影响播放 | **已完成** | `CameraModeController` 重写；`CameraVisibilityTests` 9 场景；前侧 3/4 全景；V02/V03/V08/V09 |
| **Sprint 2 教学主 UI 与状态反馈（P0）** | 新用户能完成主流程；无关键按钮状态不明；错误不只写 Console | **已完成** | 三层控制结构；`UITheme` 七态令牌；`UIStatusBar` 错误横幅 + 恢复按钮；`UIThemeTokenTests` |
| **Sprint 3 手部视觉与动作质量（P1）** | 固定曲目质量指标可对比；手部视觉评审通过；未靠放宽阈值掩盖问题 | **部分完成** | 手部三段指节 + 拇指对掌 + 指尖/手腕分离 + 黑键独立高度 + 和弦成形；`HandMotionMetrics` 扩充分组指标；**质量指标的真机定稿仍待做** |
| **Sprint 4 曲库与音频体验（P1）** | 用户不需手工复制文件即可管理 MIDI；异常文件不卡死 | **已完成** | `SongLibraryService` + `FileBrowserUI` + 导入事务 + 失效提示 + 偏好；`PianoAudioSynth` 受控声部池 |
| **Sprint 5 跨设备可行性验证（P2）** | 有真实设备数据与支持矩阵 | **未开始（计划中）** | 输入命令抽象、移动端渲染 / 音频 / 内存验证均待做 |

---

## 十三、方案第 13 章「关键设计取舍」落实情况

| # | 取舍 | 项目实际选择 | 证据 |
|---|---|---|---|
| 1 | 优先保证音符与播放正确，而非照片级画面 | 先做相机 / 手部 / 状态反馈，材质精修务实推进 | Sprint 1/2 已完成；无真实 AO |
| 2 | 保留统一时间轴，不允许模块各自计时 | `LessonTimeline` 唯一时间源；音频 / 节拍器不自建计时 | 架构文档第 2 节；`LessonTimelineTests` |
| 3 | 当前用规则与 IK，AI 放在可替换接口之后 | `IFingeringProvider` 默认启发式；AI 未接入 | `FingeringProviderTests` |
| 4 | 功能分层，而非全部塞进一条控制栏 | 主播放栏 / 练习工具栏 / 分析面板三层 | `LessonControlsUI`；设计系统文档 |
| 5 | 桌面优先，移动端响应式适配 | 仅 Windows；输入抽象未做 | 计划中 |
| 6 | 提供降级与恢复，而非隐藏异常 | `AppError` + `RecoveryAction` + `HandleRecovery` | `[Docs/error-codes.md]` |
| 7 | 本地优先，用户数据可控 | 曲库 / 偏好本地持久化；无网络访问 | `UserPreferences`、`SongLibraryService` |

---

## 十四、方案第 14 章「建议先修改的文件与模块」

| 顺序 | 目标 | 完成情况 | 证据 |
|---|---|---|---|
| 1 | `CameraModeController.cs` 显式状态机 / 过渡中断 / 动态取景 / 返回重置 | 已完成 | `CameraFramingTests`、`CameraVisibilityTests` |
| 2 | `LessonControlsUI.cs` / `UITheme.cs` 主次分区 / 状态可见 / 响应式 | 已完成 | `UIThemeTokenTests`、`UITheme` 七态 |
| 3 | `LessonSessionController.cs` / `LessonTimeline.cs` 会话状态 / 切曲事务 / 边界 | 已完成 | `LessonState`、`CommitSong` |
| 4 | `HandIKController.cs` / `HandPoseValidator.cs` 低命中采样 / 跨度 / 异常姿态 | 已完成（指标扩充；命中案例的人工专项仍在推进） | `HandMotionMetricsTests` |
| 5 | `FingerPoseController.cs` 改为调用可替换指法提供器 | 已完成 | `FingeringProviderTests` |
| 6 | `AvatarManager.cs` / `AvatarRigBuilder.cs` 生命周期 / 校验 / 每角色 IK 偏移 | 已完成 | `AvatarDefinition.Validate` |
| 7 | 新增 `SongLibraryService.cs` | 已完成 | `SongLibraryTests` |
| 8 | 新增 `ErrorReporter.cs` / `AppError.cs` | 已完成 | `PlayModeErrorHandlingTests` |
| 9 | `Tools/verify.py` 与测试目录（相机可见性 / 连续切换 / 长跑 / 视觉 / 性能） | 已完成 | `--stress/--perf/--soak/--visual/--compare-visual` |

---

## 十五、方案第 15 章「发布前检查清单」

| 检查项 | 状态 | 证据 / 说明 |
|---|---|---|
| 新用户无需读文档即可选 / 导入曲目并播放 | 部分实现 | 空状态 + 导入入口 + 最近曲目已就绪；**可用性研究未做**（人工） |
| 播放 / 暂停 / 进度 / 速度状态清楚可见 | 已实现 | 主播放栏状态标签 + 分段选择器 |
| 手部特写能看清关键按键；全景不再持续遮挡低音端 | 部分实现 | 前侧 3/4 已修复遮挡；**人工看图仍需在真机确认** |
| 视角 / 角色切换课程状态连续，无抖动 / 跳姿 | 已实现 | `CameraVisibilityTests`、`PlayModeIntegrationTests` |
| A-B / 分手 / 速度 / 节拍器 / 高亮组合经过测试 | 已实现 | `TeachingFeatureTests` |
| 损坏 MIDI / 空文件 / 丢失路径 / 无音符 / 音频不可用可恢复 | 已实现 | `[Docs/error-codes.md]` 表二 |
| 高 DPI / 窄窗口 / 中文空格路径 / 无网络测试通过 | 部分实现 | 中文空格路径有测试（`Import_PathWithChineseAndSpaces_Works`）；**DPI / 无网络待真机** |
| 在目标电脑上测量帧时间 / GC / 内存 / 加载 / 同步 | 计划中 | 采样工具就绪（`--perf`），**真机定稿待做** |
| 第三方模型 / 音色 / 数据全部登记许可证 | 已实现（当前无第三方） | [`Docs/asset-licenses.md`](asset-licenses.md) |
| README 与帮助准确区分已支持 / 实验 / 计划 | 已实现 | README 第 0 节 + 本文 |

---

## 十六、审查第 1 章「人物模型、手部模型与场景精细度」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 1.1 方案 A | 头部 / 躯干 / 肩臂 / 服装 / 发型 / 比例一致性精修 | `Avatar/AvatarManager.BuildNeckHead/BuildTorso/BuildShoulders/BuildHair`、`AvatarBodyMetrics` | 已实现（程序化精修） | `V03/V04/V01`、人工 |
| 1.1 方案 B | 保留骨架、替换为许可清晰角色网格 | `Avatar/AvatarDefinition`（适配层就绪） | 计划中（模型接入） | 人工 |
| 1.2 A | 手掌（掌根/掌背/虎口）、三段指节、圆指尖、拇指对掌、五指差异、以 24mm 键宽为标尺 | `AvatarManager.BuildHand/BuildFingerSegments`、`AvatarBodyMetrics`（`ThumbSpreadDegrees=57`） | 已实现 | `[Docs/animation-pipeline.md]` 第 3 节、`V01` |
| 1.2 B 第一步 | 音高→手指升级为序列规则分配，输出置信度 | `HeuristicFingeringProvider` | 已实现 | `FingeringProviderTests` |
| 1.2 B 第二步 | 指尖真实对准琴键目标；手腕与指尖分离；黑键独立高度；和弦先成形 | `HandIKController.UpdateFingertipTargets/BuildKeyTouches` | 已实现 | `[Docs/animation-pipeline.md]` 第 4 节 |
| 1.2 C | 扩充动作质量指标（左右手分项 / 指尖误差 / 速度异常 / 黑键和弦快速音阶大跳分组 / 视觉抽查） | `Avatar/HandMotionMetrics` | 已实现 | `HandMotionMetricsTests` |
| 1.2 验收目标 | 命中率 ≥ 90%（第一轮） | 实测 **93.6%**（左 94.7% / 右 92.7%），口径见 `[Docs/animation-pipeline.md]` 第 6 节 | **已达成**（口径仍依赖 7.5s 示例曲，真机与长曲复核待做） | `HandMotionTests` + 人工 |
| 1.3 A | 琴键倒角 / 黑键层次 / 琴身外壳 / 材质分层 / 接触关系 | `PianoKeyMap.BuildKeys/BuildCase`、`PianoMeshFactory` | 已实现 | `PianoGeometryTests`、`V03` |
| 1.3 B | 小而完整的教学场景（地面 / 背景 / 琴凳 / 灯光） | `Piano/PianoSceneEnvironment`、`AvatarManager.BuildStool` | 已实现 | `SceneEnvironmentTests` |
| 1.3 C | 为教学目标优化（特写清晰 / 全景完整 / 颜色语义克制） | `CameraModeController`、`PianoSceneEnvironment` | 已实现 | `CameraVisibilityTests`、`V01/V03` |

---

## 十七、审查第 2 章「界面 UI」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 2.1 第一层 | 主播放栏（播放/暂停 + 进度 + 时间 + 重播 + 视角） | `UI/LessonControlsUI.BuildMainBar` | 已实现 | `V10` + 人工 |
| 2.1 第二层 | 练习工具栏（分段选择器显示当前值） | `LessonControlsUI.BuildPracticeToolbar`、`UI/UISegmentedControl` | 已实现 | `UIThemeTokenTests` + 人工 |
| 2.1 第三层 | 分析与配置面板（动作质量放「开发诊断」折叠区） | `LessonControlsUI.BuildAnalysisPanel` | 已实现 | `V12_analysis_expanded.png` |
| 2.2 A | 完整设计令牌 | `UI/UITheme` | 已实现 | `UIThemeTokenTests` |
| 2.2 B | 视觉层级（主 / 次 / 切换 / 低频 / 危险） | `UITheme.ButtonStyle`（Primary/Secondary/Ghost/Danger/Toggle） | 已实现 | `ButtonStyle_CoversVisualHierarchy`、`PrimaryStyle_IsTheOnlyStrongAccentStyle` |
| 2.2 C | 每个交互可见反馈（当前值 / 禁用原因 / 就近提示 / tooltip / 错误恢复） | `UITheme.SetButtonEnabled/SetButtonTooltip`、`LessonControlsUI.RefreshHandHint`、`UIStatusBar` | 已实现 | 人工 + `HandEmptyHint_TellsUserWhatToDoNext` |
| 2.3 | 教学反馈 / 强调色 / 开发指标分层 | `PianoKeyHighlighter` 语义色、分析面板「开发诊断」 | 已实现 | `TeachingSemanticColors_KeepDocumentedMeaning` |

---

## 十八、审查第 3 章「手部视角 / 相机」

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 3.1 A | 三状态明确（特写 / 全景 / 自由观察） | `Camera/CameraModeController` | 已实现 | `CameraVisibilityTests` |
| 3.1 B | 特写目标点 / 平滑跟随 / 动态取景 / 边界余量 / 单手 / 异常状态 / 垂直限制 | `FramingPolicy.ComputeCloseupTarget`、`CameraModeController` | 已实现 | `CameraFramingTests`（多条） |
| 3.1 C | 全景前侧 3/4、人物与钢琴联合包围盒、头脚不出框 | `FramingPolicy.ComputeFullTarget`、`OverviewYaw=34f` | 已实现 | `ComputeFullTarget_*`、`V03` |
| 3.1 D | 状态可见 / 过渡时长统一 / 切换不重置 / R 语义一致 / UI 悬停不旋转 / 返回进入前预设 | `ModeLabel`、`CameraPoseTransition`、`ShouldAcceptOrbitInput`、`ReturnToPreset` | 已实现 | `CameraFramingTests`、人工 |
| 3.2 | 视角可用性 9 个必测场景 | `PlayMode/CameraVisibilityTests`（Scene1–Scene9） | 已实现 | `--visual`/`verify`（不依赖渲染） |

---

## 十九、审查第 4/5/6 章

| # | 要点 | 实现位置 | 状态 | 验收方式 |
|---|---|---|---|---|
| 4 | 建议改动的代码位置（AvatarRigBuilder / HandIK / FingerPose / HandPoseValidator / HandMotionMetrics / CameraModeController / LessonControlsUI / UITheme / AvatarSelectionUI / MainMenuUI / PianoKeyMap / animation-pipeline / screenshots） | 见各小节 | 已实现 | 逐项见上 |
| 5 阶段 A | 可见性与交互结构（三状态 / 全景修正 / 特写取景 / 控制栏分层 / 基线） | `CameraModeController`、`LessonControlsUI`、`VisualSnapshotTests` | 已完成 | `CameraVisibilityTests`、`--visual` |
| 5 阶段 B | 手部与人物质量（网格 / 坐标校准 / 专项测试） | `AvatarManager`、`HandIKController`、`[Docs/animation-pipeline.md]` | 部分完成（网格已精修 + 命中率 93.6% 达标；**指尖贴键精度与真机视觉评审待做**） | `HandMotionTests` + 人工看图 |
| 5 阶段 C | 统一 UI 设计系统 | `UITheme`、设计系统文档 | 已完成 | `UIThemeTokenTests` |
| 5 阶段 D | 场景与材质精修 | `PianoSceneEnvironment`、`PianoKeyMap` | 部分完成（程序化精修；**真实 AO / 后处理未做**） | `SceneEnvironmentTests`、人工 |
| 6.1 | 画面基线组合 V01–V09 | `VisualSnapshotTests` + `manifest.md` | 已实现 | `--visual` |
| 6.2 | 最终验收清单（模型/场景 / UI / 相机） | `[Docs/ui-design-system.md]`、`[Docs/visual-regression.md]` | 部分实现（大量「人工核对」项待真机） | 人工 |

---

## 二十、尚未实现清单（供协调者确认）

以下条目在需求文档中出现但**本轮未实现**，均为「计划中」，不得在文档 / UI 中声称可用：

| 项 | 方案 / 审查出处 | 计划阶段 |
|---|---|---|
| `IAvatarProvider` 提供器接口 | 方案 10 | Sprint 5 之后 |
| `IHandMotionProvider` 提供器接口 | 方案 10 | 后续能力 |
| `IAudioToScoreProvider`（MP3/WAV 转谱） | 方案 2.2 P2 / 10 | 后续能力 |
| AI 手部动作生成 | 方案 2.2 P2 | 后续能力 |
| 照片生成 / 定制 3D 人物 | 方案 2.2 P2 / 10 | 后续能力 |
| 替换为许可清晰的高质量人物 / 钢琴模型 | 方案 2.1 P1 / 5.2 中期 | Sprint 5 之后 |
| 真实采样音色替代波形合成 | 方案 2.1 P1 / 7.3 | P1 |
| 人工编辑 / 覆盖指法并与原 MIDI 分开保存 | 方案 5.3 | P1 |
| 真实 SSAO / 后处理环境遮蔽 | 方案 5.1-6 | 受内置管线限制，暂不承诺 |
| 移动端 / Web 适配与输入命令抽象 | 方案 2.2 P2 / Sprint 5 | Sprint 5 |
| 集显笔记本与中档独显真机性能定稿 | 方案 11.4 | 发布前 |
| 可用性研究（新用户无文档完成主流程） | 方案 15 | 发布前 |
| `ScriptableObject` 配置资产 | 方案 9.2 | 视需要 |

> 上文所有「已实现」项均可通过 `python Tools/verify.py`（及其附加阶段）复现；「部分实现」项在「验收方式」列注明了尚未完成的具体部分。
