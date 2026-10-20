# 角色升级与个性化 —— 稳定基线（Phase 0）

> 对应《Piano Teacher 3D：角色升级与照片个性化功能详细实施计划》**5.2 交付物**
> 与 **15 可立即落地清单 第 1 步「建立分支与基线」**。
>
> 规则：本文件只记录**可复核的事实**。凡是无法在本机测出的项，一律写「待测」，
> 不得把未实测的环境或性能写成已通过（方案 1.2 / 18 的诚实性要求）。

---

## 1. 工程与运行环境

| 项 | 实测值 | 来源 |
|---|---|---|
| Unity 版本 | `2022.3.22f1c1`（revision `c3cbd310e76e`，中国版） | `ProjectSettings/ProjectVersion.txt` |
| 渲染管线 | 内置管线（Built-in），无 URP/HDRP 包 | `Packages/manifest.json` |
| 色彩空间 | Gamma（`m_ActiveColorSpace: 0`） | `ProjectSettings/ProjectSettings.asset` |
| 目标平台 | Windows x64 Standalone | `Tools/verify.py --player` 构建产物 |
| 图形 API（编辑器） | Direct3D11 | `Docs/screenshots/manifest.md` 运行记录 |
| 产品名 | `Piano Teacher 3D` / 公司名 `PianoTeacher3D` | `ProjectSettings.asset` |
| 分辨率基线 | 截图请求 1280×720，实际窗口 1101×656 | `Docs/screenshots/manifest.md` |
| 本机硬件 | RTX 4060 笔记本版 8GB；Windows 11 26200 | `Docs/perf/environments.md` |

### 1.1 关键包

| 包 | 版本 | 用途 | 备注 |
|---|---|---|---|
| `com.unity.ugui` | 1.0.0 | 全部 UI | UI 为**代码程序化构建**，仓库内无 UI prefab |
| `com.unity.test-framework` | 1.1.33 | EditMode/PlayMode | `Tools/verify.py` 驱动 |
| `com.unity.textmeshpro` | 3.0.6 | 未使用 | 代码零引用，仅 `UITheme.cs` 一处注释；迁移属 P2 |
| `com.unity.ide.rider` / `visualstudio` | 3.0.31 / 2.0.22 | IDE 集成 | 运行时不参与 |

> 上一轮已移除 `com.unity.timeline@1.7.6` 与 `com.unity.animation.rigging@1.2.1`（代码 0 引用），
> 依赖扫描见 `Tools/static_checks.py`。

---

## 2. 分支与版本控制

| 项 | 值 |
|---|---|
| 基线提交 | `e1deb9a`（wip: 手部归因计数、和弦成形、特写躯干避让与截图重采） |
| 分支策略 | `master` 保持可回归；角色/照片/UI 改造在功能分支累积，验收通过后合并 |
| 工作区要求 | Phase 0 验收要求**干净工作区**；本文件生成时工作区仅含本轮新增文件 |
| 未跟踪内容 | `_verify_out/`（忽略）、`.workbuddy/`（本轮加入 `.gitignore`） |

---

## 3. 测试基线（可重复执行的入口）

```bash
# 编译 + EditMode
python Tools/verify.py --edit-only
# 编译 + EditMode + PlayMode
python Tools/verify.py
# 额外：Player 构建与采样
python Tools/verify.py --player --player-seconds 20
# 架构分层检查（5 条规则）
python Tools/check_architecture.py
# 依赖/静态检查
python Tools/static_checks.py --strict
```

| 层 | 最近结果 | 产物 |
|---|---|---|
| 编译 | 0 错误 | `_verify_out/compile.log` |
| EditMode | **319 / 319 通过**，0 失败 0 跳过 | `_verify_out/editmode.xml` |
| PlayMode | **90 通过 / 5 跳过 / 0 失败**（共 95） | `_verify_out/playmode.xml` |
| 架构检查 | 5 / 5 通过 | 控制台输出 |
| 静态检查 | 0 违规 | 控制台输出 |

5 条 PlayMode 跳过项的原因逐条记录在 `Docs/how-to-run.md` 第 11 节（多数为需要真实鼠标注入、
或需要前台 GUI 的项，批量模式下无法构造）。

---

## 4. 性能基线（真实 Windows Player）

数据来源：`Docs/perf/sampling.md` 的 `player-soak` 分节（Development Build，本机独显档）。

| 指标 | 实测 | 说明 |
|---|---|---|
| 稳态 p95 / p99 / max 帧时间 | 16.71 / 16.87 / 17.66 ms | 10 分钟长跑 |
| 平均 FPS / 1% low | 59.98 / 58.98 | 同上 |
| 托管堆增长 | +0.45 MB / 10 min | 只有端点快照，**不能当作"无泄漏"结论** |
| 20 s 基线中的长帧 | 34 ms（启动期） | 10 分钟窗口未复现，判定为启动期而非稳态问题 |

**未覆盖**：集显与中档独显（需第二台机器）、30 分钟发布候选 Soak、玩家机 DPI 缩放。

---

## 5. 当前角色记录（方案 5.1 第 4 项）

现有 3 个角色**全部由代码程序化构建**，仓库内没有角色 prefab。

| 索引 | 名称 | 风格 | 来源 | 手部网格 | 骨骼 |
|---:|---|---|---|---|---|
| 0 | 写实女性 | `Realistic` | 程序化（`AvatarDefinition` + `AvatarRigBuilder` + `HandMeshFactory`） | 超椭圆截面放样单块网格 | 三关节链（MCP/PIP/DIP） |
| 1 | 写实男性 | `Realistic` | 同上 | 同上 | 同上 |
| 2 | 卡通角色 | `Cartoon` | 同上 | 同上 | 同上 |

### 5.1 骨骼命名与 Humanoid 映射

- 躯干/四肢：`Hips / Spine / Chest / UpperChest / Neck / Head`、
  `LeftShoulder / LeftUpperArm / LeftLowerArm / LeftHand`（右侧同构）。
- 手指：`LeftIndexProximal / LeftIndexIntermediate / LeftIndexDistal`（中/无名/小指同构），
  拇指为 `LeftThumbProximal / LeftThumbIntermediate / LeftThumbDistal`。
- Humanoid 映射在 `AvatarRigBuilder` 中建立，`Animator.avatar.isValid` 与 `isHuman` 均为真
  （由 `AvatarDefinition.Validate` 断言）。

### 5.2 左右手语义（方案 6.1 的唯一约定）

| 层 | 约定 |
|---|---|
| 术语 | `LeftHand` / `RightHand` 指**演奏者解剖学左右**，与画面左右无关 |
| 音高 → 琴键 | 唯一入口 `Piano/PianoKeyMap`（音高 21–108 → 键位索引，单调） |
| 音符 → 手 | 唯一入口 `Piano/PianoNoteFilter`（按分界音高与教学数据分配） |
| 手指编号 | 0 = 拇指 … 4 = 小指（`LeftFingers[f]` / `RightFingers[f]`），不因画面朝向翻转 |
| 角色镜像 | 程序化角色用 `Side = ±1` 乘子表达左右，**不使用负缩放**；导入模型用 `CharacterProfile.RootYawDegrees` 修正朝向，禁止 `localScale` 取负 |
| 相机 | 相机只影响观察，不参与任何手别判断 |
| 指尖目标 | 来自琴键几何（`PianoKeyMap` + 键面高度），**不从屏幕像素推导** |

> 违反上述任一条的实现都视为缺陷：不得在多个脚本里分别推导左右手，
> 也不得用 `transform.position.x` 的正负判断手别（方案 6.1 明文禁止）。

---

## 6. 固定 MIDI 样例（方案 5.1 第 6 项）

| 样例 | 路径 | 覆盖场景 |
|---|---|---|
| `demo` | `Assets/StreamingAssets/Songs/demo.mid` | 内置示例曲，双手 + 和弦 + 跳进 |
| `bench_black_keys` | `Assets/StreamingAssets/Songs/bench/` | 黑键密集 |
| `bench_chords` | 同上 | 双手和弦同时发声 |
| `bench_fast_scale` | 同上 | 快速音阶 + 大跨度换位 |

三首基准曲由 `Tools/make_bench_midi.py` 可复现生成；整曲播放与分组门禁见
`Assets/Tests/PlayMode/HandBenchmarkTests.cs`，基线数值见
`Docs/perf/hand-baseline-2026-10-19.md`。

**左右手交叉样例仍缺失**：现有曲目没有真正跨越左右手分界的乐句，
`bench_*` 三首的「分组·跨手」样本量不足以验收方案 6.4 的交叉用例，列为待补。

---

## 7. 视觉基线（方案 5.1 第 7 项）

20 张固定机位截图位于 `Docs/screenshots/`，清单与检查要点见
`Docs/screenshots/manifest.md`（由 `VisualSnapshotTests` 自动写入）。
UI 评审打分表见 `Docs/visual-regression.md` 第 6 节（五项制，任一项退 1 分即不通过）。

> 注意：批量模式下窗口实际为 1101×656（请求 1280×720）。**重采基线必须与上一版同尺寸**，
> 尺寸退化时整批作废重跑，不得入库。

---

## 8. 本轮改造前的已知限制（必须带进后续验收）

1. 三个程序化角色的外观是**色块 + 几何形体**，视觉可信度低；这是引入外部角色的直接动因。
2. 指尖误差未达验收目标（均值 26.7 mm / P95 72.8 mm，目标 ≤25 / ≤50），根因在 IK 收敛而非指法分配。
3. 短指大跨度（>97 mm）物理够不到；拇指只有屈伸一个自由度。
4. 无蒙皮权重：程序化手部在极限弯曲时指背可能出现折痕。
5. 角色切换目前只有「内置索引 +1」一种方式，没有契约、没有回滚、没有资源释放责任划分 ——
   这正是 Phase 2 要解决的问题。

---

## 9. 验收判定（Phase 0）

| 验收条件（方案 5.3） | 状态 |
|---|---|
| 可从干净工作区构建目标 Player | ✅ 上一轮 `verify.py --player` 成功 |
| 默认角色、MIDI、音频与琴键交互保持原样 | ✅ EditMode 319 / PlayMode 90 全通过 |
| 关键性能指标可重复采集 | ✅ 采样脚本与文档固定；同机可复现 |
| 后续任何改动都能与基线比较 | ✅ 提交号 + 测试数 + 截图 + 性能数字固定在本文件 |
