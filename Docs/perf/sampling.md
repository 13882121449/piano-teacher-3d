# 性能采样报告（Docs/perf/sampling.md）

> 本文件由 PlayMode 性能 / 稳定性测试**自动写入或覆盖对应小节**（见 `.cs` 里的小节标记），
> 请勿手工编辑「小节」以外的结构；手工修改小节内容会在下次运行采样时被覆盖。
>
> **诚实性声明（方案 0 节 / 审查 0.1）**：批处理（`-batchmode`）与无图形设备（`-nographics`）下采集的
> 帧率 / 帧时间**不含渲染负载，不能代表真实设备体验**；本文件所有数值都是「本机、当前构建、当前场景」的
> 实测值，任何未在目标电脑上采集的数据都不得写成「已达标」（方案 11.3 / 11.4 要求真机测量后定稿）。
>
> 对应方案章节：**7.2 性能预算与排查顺序**（记录帧时间百分位与帧率，只记录平均 FPS 不足以发现卡顿）、
> **11.3 建议验收门槛**、**11.4 自动化与真实设备验证**。

---

<!-- SECTION:frame-timing:BEGIN -->
### 帧时间采样（纯播放）

- 时间：2026-10-16 18:19:37
- Unity：2022.3.22f1c1
- 图形设备：Null（Null Device）
- 分辨率：640×480 @ 96 dpi
- 是否批处理（-batchmode）：是；是否 -nographics：是
- 平台 / 系统：WindowsEditor / Windows 11  (10.0.26200) 64bit

- 场景口径：播放 + 全景视角，无额外切换

| 指标 | 数值 |
|---|---|
| 采样帧数 | 480 |
| 平均帧时间 | 16.7 ms |
| p50 帧时间 | 16.7 ms |
| p95 帧时间 | 16.8 ms |
| p99 帧时间 | 17 ms |
| 最大帧时间 | 17.3 ms |
| 平均 FPS | 60 |
| 1% low FPS | 58.4 |
| 托管内存增量（GC.GetTotalMemory(false)） | 0.01 MB |
| 总分配内存（Profiler.GetTotalAllocatedMemoryLong） | 132.84 MB → 132.83 MB（峰值 132.86 MB） |

**口径说明**

- 帧时间取 `Time.unscaledDeltaTime`（不受 `Time.timeScale` 影响），逐帧采样，前 30 帧为预热、不计入统计。
- p50/p95/p99 为线性插值百分位；1% low FPS = 最慢 1% 帧的平均帧时间的倒数。
- `Profiler.GetTotalAllocatedMemoryLong()` 在非开发构建 / 未启用 Profiler 时可能返回 0，此时该行数值不可信（本次读数为 非 0，可用）。
- 本次运行**无图形设备**：帧时间不含渲染负载，**不代表真实设备体验**。
<!-- SECTION:frame-timing:END -->

<!-- SECTION:switching-load:BEGIN -->
### 切换负载（每 20 帧切换视角/角色/速度）

- 时间：2026-10-16 18:19:47
- Unity：2022.3.22f1c1
- 图形设备：Null（Null Device）
- 分辨率：640×480 @ 96 dpi
- 是否批处理（-batchmode）：是；是否 -nographics：是
- 平台 / 系统：WindowsEditor / Windows 11  (10.0.26200) 64bit

- 场景口径：播放中每 20 帧切换一次，共触发 24 次切换

| 指标 | 数值 |
|---|---|
| 采样帧数 | 480 |
| 平均帧时间 | 16.7 ms |
| p50 帧时间 | 16.7 ms |
| p95 帧时间 | 16.7 ms |
| p99 帧时间 | 16.9 ms |
| 最大帧时间 | 17 ms |
| 平均 FPS | 60 |
| 1% low FPS | 59.1 |
| 托管内存增量（GC.GetTotalMemory(false)） | 1.23 MB |
| 总分配内存（Profiler.GetTotalAllocatedMemoryLong） | 170.32 MB → 170.31 MB（峰值 170.41 MB） |

**口径说明**

- 帧时间取 `Time.unscaledDeltaTime`（不受 `Time.timeScale` 影响），逐帧采样，前 30 帧为预热、不计入统计。
- p50/p95/p99 为线性插值百分位；1% low FPS = 最慢 1% 帧的平均帧时间的倒数。
- `Profiler.GetTotalAllocatedMemoryLong()` 在非开发构建 / 未启用 Profiler 时可能返回 0，此时该行数值不可信（本次读数为 非 0，可用）。
- 本次运行**无图形设备**：帧时间不含渲染负载，**不代表真实设备体验**。
<!-- SECTION:switching-load:END -->

<!-- SECTION:soak:BEGIN -->
### 长跑稳定性（Soak，方案 11.4「定时/循环播放 10 分钟以上的稳定性测试」）

- 时间：2026-10-16 18:21:56
- Unity：2022.3.22f1c1
- 图形设备：Null（Null Device）
- 分辨率：640×480 @ 96 dpi
- 是否批处理（-batchmode）：是；是否 -nographics：是
- 平台 / 系统：WindowsEditor / Windows 11  (10.0.26200) 64bit

- 目标时长：90 s（环境变量 `PT3D_SOAK_SECONDS`，默认 600）；另有 15 s 无切换测量窗口
- 循环切换次数：44
- 错误级日志（Error/Exception/Assert）：0
- 姿态越界（PoseIssueCount）：0
- 托管内存（`GC.GetTotalMemory(false)`）窗口均值：第 2 四分位 341.73 MB → 第 4 四分位 343.03 MB，增长 0.4%（阈值 < 25%）
- 总分配内存（`Profiler.GetTotalAllocatedMemoryLong`）：132.73 MB → 132.95 MB
- 定时器漂移（无切换窗口）：最大偏差比例 0.8%（阈值 < 15%）

**口径说明**

- 漂移 = |Σmax(0, ΔCurrentTime) − ΣunscaledDeltaTime| / ΣunscaledDeltaTime，在末尾无切换、原速、循环播放的窗口内采样（每 2 s 取一次检查点），循环回退帧不计入实际进度。
- 内存增长对比第 2 与第 4 四分位窗口均值，避免被启动期缓存增长误判为泄漏；分母下限 1 MB。
- 角色切换边界会重置手部动作指标（另一套骨架首次就位会产生一次非连续跳变），姿态越界仍在每次切换后即时检查。
- 本节数值为本机批处理环境实测，**不代表真实设备体验**（方案 0 节 / 审查 0.1）。
<!-- SECTION:soak:END -->

<!-- SECTION:resource-load:BEGIN -->
### 资源加载采样（方案 11.4「资源加载耗时和失败率采样」）

- 时间：2026-10-18 23:05:08
- Unity：2022.3.22f1c1
- 图形设备：Null（Null Device）
- 分辨率：640×480 @ 96 dpi
- 是否批处理（-batchmode）：是；是否 -nographics：是
- 平台 / 系统：WindowsEditor / Windows 11  (10.0.26200) 64bit

- 曲目目录：`StreamingAssets/Songs`；曲目数：1
- 最慢单曲：7.1 ms；失败数：0

| 曲目 | 加载耗时 (ms) | 结果 |
|---|---|---|
| demo.mid | 7.1 | 成功 |

**口径说明**：耗时用 `System.Diagnostics.Stopwatch` 包裹 `LessonSessionController.LoadSong`，包含校验 + 解析 + 一次性提交；示例曲为毫秒级，5 秒上限只用于捕获卡死，不代表目标设备上的加载性能。
<!-- SECTION:resource-load:END -->
