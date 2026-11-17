# 写实角色替换与界面重做 — 设计

日期：2026-11-02
分支：`feature/v4-full-upgrade`
状态：待用户复核

## 1. 背景与问题

当前三个内置角色（`builtin.realistic.female` / `builtin.realistic.male` / `builtin.cartoon`）是运行期用方块、胶囊、球拼出来的程序化人形，菜单上却标着「写实女性 / 写实男性 / 卡通角色」。用户看到的画面与名字对不上，这是"项目还是很丑"的主要来源之一。另外界面整体被用户判为"全部都很丑"：配色缺乏品牌感、控件贴边、字号只有两三档、无 hover/pressed 区分、一屏塞满。

## 2. 目标与非目标

**目标**

1. 三个角色槽位全部换成有完整 Humanoid 骨架与贴图的真人模型，名字与画面一致。
2. 界面按「暖木古典」方向重做，与琴房场景同源，同时引入 8pt 栅格与 4 级字号纪律。
3. 顺带解决扫出来的其它明显短板（场景氛围、主菜单信息密度、特写构图、贴图内存）。

**非目标**

- 不引入付费资源、不做运行时下载（模型随仓库走）。
- 不改 `LessonTimeline` / `MidiEventScheduler` / 相机状态机的核心逻辑。
- 不做面部动画、不做口型、不做 IK 手指逐帧物理（沿用现有解析式手型）。

## 3. 角色方案

### 3.1 素材来源

Microsoft Rocketbox（MIT，`Docs/asset-licenses.md` 已有记载），仓库 `microsoft/Microsoft-Rocketbox`：
40 个成人、4 个儿童模型，每个含 `Xxx.fbx`、`Xxx_facial.fbx`、`Assets/Avatars/<Group>/<Name>/Textures/*.tga` 与人像预览 `Xxx.png`。

**下载通道实测**：`raw.githubusercontent.com` 不可达（连接被拒）；`api.github.com` 可达，`GET /repos/{owner}/{repo}/git/blobs/{sha}` 带 `Accept: application/vnd.github.raw` 能取原始二进制——已用 `Female_Child_01.fbx`（521,968 B，Kaydara FBX Binary 头）验证。

### 3.2 三个槽位

| 槽位 | 模型 | 依据 |
|---|---|---|
| 写实女性 | `Female_Adult_01` | 已在仓库，管线跑通 |
| 写实男性 | `Male_Adult_01` | 同尺寸级（FBX 0.50 MB），复用同一套导入与手部绑定 |
| 儿童 | `Female_Child_01` | 用户指定；FBX 0.52 MB |

儿童的比例与成人不同，现有腕高/指尖/坐姿参数全部按成人标定。**决策**：儿童先按身高比例（`AvatarBodyMetrics` 已有的骨骼长度）整体缩放，跑一版视觉基线；若指尖误差 > 15 mm，再做一轮针对性标定（只调 `HandHeightOffset` 与 `WristPitch` 两个 per-definition 参数，不改算法）。若两轮仍不达标，退化方案是把第三槽位换成 `Male_Adult_02`，儿童单独立项。

### 3.3 管线

1. `Tools/fetch_rocketbox_avatar.py`：按模型名从 blobs 接口拉 FBX 与人像预览；贴图用 Pillow 从 TGA 转 2048 PNG（仓库里 TGA 是 4096/12 MB 一张，5 张/人 ≈ 57 MB，转 PNG 后 ≈ 8–10 MB/人）。校验 git blob SHA-1，失败即报错退出，保证可复现。下载物落在 `Assets/Characters/Rocketbox/<Name>/`，与现有 `Female_Adult_01` 同构。
2. 导入：复用 `Assets/Editor/RocketboxTextureSetup.cs`（NormalMap 标记 + BC5）与 `RocketboxTextureLibrary`（运行时 `_NORMALMAP` 关键字）。
3. 绑定：`CharacterProfile` 走 `CharacterSource.Prefab`，预制体上挂 `RocketboxCharacterView` + `RocketboxHandRig` + `Animator`（Humanoid）。
4. 注册：`CharacterRegistry.BuiltInIds` 换成三个预制体 ID；`BuiltInDisplayNames` 改为「写实女性 / 写实男性 / 儿童」；程序化拼装路径保留为加载失败兜底，不再出现在菜单。
5. 头像选择界面改用仓库自带 `Xxx.png` 作缩略图。

### 3.4 风险

- 贴图内存：2K RGBA + BC7 约 5.6 MB/人，三人 17 MB，可接受；4K 原图直接进项目会让编辑器导入时间与包体翻倍，故必须在脚本里降采样。
- 儿童标定（见 3.2）。
- Rocketbox 骨架的 `canonicalX = thumb * SideSign` 这类左右手差异已经在 `RocketboxHandRig` 处理过，新模型必须逐个验证手型（复用 `RocketboxRestPoseTests`）。

## 4. 界面方案（风格 B：暖木古典）

### 4.1 设计令牌

- **色板**：底 `#1A1310`；面板 `rgba(38,26,20,.92)`；描边 `#6B4D35`；正文 `#F2E4CC`；次要 `#BDA58A`；强调 `#D9B87E`；轨 `#4A3527`；危险 `#C2603F`；成功 `#8FA46B`。
- **字体**：标题衬线（`Noto Serif SC` / 系统衬线回退），正文无衬线；数字等宽（`font-variant-numeric: tabular-nums`）。
- **字号阶**（4 级）：32 标题 / 20 小标题 / 15 正文 / 12 标签；数字另用等宽 15。
- **间距**：8pt 栅格（8/16/24/32），控件内边距 8–12，卡片圆角 6，按钮圆角 6。
- **层级**：靠描边与底透明度分区，不用重阴影；唯一实心强调色只出现在主操作与进度条。

### 4.2 布局（1280×720 基准，16:9）

- **顶栏** 56 px：返回 / 曲名（衬线）/ 调号胶囊 / 弹性空白 / 播放状态 / 当前角色 / 帮助。
- **左侧视角卡** 200 px 宽、可折叠：视角名 + 快捷键两行。
- **底部播放条** 72 px：播放圆钮（强调色）/ 时间「当前 / 总长」等宽 / 进度轨 / 双手·速度·视角三个胶囊 / 更多。
- **更多面板**：把节拍器、分析、全屏等低频项从主条移入，避免一屏塞满。

### 4.3 实现结构

现有 `Assets/Scripts/UI/` 7669 行、8 个脚本，`UITheme.cs` 已 1725 行、121 个 `public static`，属于"令牌 + 组件 + 布局"混在一起。拆成三层：

1. `UITheme.cs` → 只留令牌（颜色/字号/间距/圆角/字体），删掉构建方法与状态机。
2. 新增 `UIKit.cs` 收敛组件构建：`Panel`、`PrimaryButton`、`SecondaryButton`、`GhostButton`、`Segmented`、`Slider`、`Chip`、`Tooltip`、`FocusRing`，统一 hover/pressed/disabled 三态与键盘焦点。
3. 三个界面脚本只负责布局与数据绑定：`MainMenuUI`、`LessonControlsUI`、`UIStatusBar`（`AvatarSelectionUI` 与 `PhotoCustomizationPanel` 跟随同一套组件）。

`UISegmentedControl` 与 `UITooltip`、`UIHoverFeedback` 保留，接线改为调用 `UIKit`。

### 4.4 验收（界面）

- 1280×720 / 1920×1080 / 2560×1440 三档截图无贴边、无重叠、字号不低于 12。
- 每个可交互元素具备 hover、pressed、disabled 三态，键盘焦点可见。
- 视觉基线新增 3 张：主菜单、角色选择、新播放条。

## 5. 顺带优化项（同批做）

| 项 | 现状 | 做法 |
|---|---|---|
| 场景氛围 | 主光单点、无后处理 | 加 URP Volume（Bloom 0.15 / Vignette 0.2 / Color Adjustments），主光色温 4800K、补一盏冷侧光 |
| 主菜单 | 只有标题与开始按钮 | 改成卡片式：继续练习 / 选曲 / 角色 / 设置 |
| 琴凳与地板 | 纯色材质 | 复用程序化噪声法线（前端那套思路），木质粗糙度 0.55 |
| 特写构图 | 特写机位是硬编码偏移 | 按 Task 3 的 A/B 两套机位，用 `CameraModeController` 的 preset 参数 |
| 贴图内存 | 4K TGA 直进项目 | 随 §3.3 一起降采样到 2K PNG |
| UI Canvas | 单 Canvas 全量重建 | 拆静态/动态两个 Canvas（播放条动态、其余静态） |

## 6. 分阶段与验收

1. **阶段 A**：下载脚本 + 两个新角色导入 + 注册表切换 + 头像缩略图（可独立验证：三个槽位都能加载、手型测试全绿）。
2. **阶段 B**：UI 令牌与 `UIKit` 组件层（先只做播放条 + 顶栏，出对比图）。
3. **阶段 C**：主菜单 / 角色选择 / 更多面板重做。
4. **阶段 D**：场景氛围 + 特写构图 + 贴图降采样收尾。

每阶段单独提交、单独出视觉基线；任阶段回退不影响其它阶段。

**总验收**：`python Tools/verify.py` 全绿（EditMode 522+、PlayMode 162+）、`python Tools/static_checks.py` 4 条规则 0 违规、视觉基线 26 张重采 + 3 张新增、`git status` 干净。
