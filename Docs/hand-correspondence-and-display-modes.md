# 左手 / 右手 · 手指 ↔ 琴键 · 显示模式（本轮修复与新增）

> 本文记录本轮针对三个具体问题的修复：**① 手指与琴键不对应；② 新增展示 / 教学双显示模式；
> ③ 人物左右手反向**。每一项都包含：现象、根因（实测依据）、改动、以及**如何验证对应关系是否正确**。

---

## 0. 统一左右约定（本轮确立，全项目唯一）

这次三个问题里有两个都源于"左右"没有唯一定义，因此先把这个约定钉死：

| 概念 | 约定 |
|---|---|
| 演奏者朝向 | 坐在 `+Z` 侧、面向 `-Z` 弹奏（`AvatarManager` 整体绕 Y 旋转 180°） |
| 角色的**左手** | 世界 **+X** 侧（`Skeleton.LeftHand` / 可见 `LeftHand.Wrist` 都在 +X） |
| **低音键** | 世界 **+X** 侧（A0 在 `x ≈ +0.600`，C8 在 `x ≈ -0.624`） |
| 左手负责的音区 | `pitch < 60`（`PianoNoteFilter.IsLeftHand`，未变） |
| 教学相机的**画面左侧** | 世界 **+X** 侧（相机在 `+Z` 看向 `-Z`，`transform.right = -X`） |
| 颜色语义 | 低音 / 左手 = **冷蓝**；高音 / 右手 = **暖橙**；预提示 = 暖金（未变） |

**一条链**：低音键 ↔ 角色左手 ↔ 世界 +X ↔ 画面左侧 ↔ 冷蓝。任何一环反过来都会立刻被测试抓到
（见第 4 节）。

> 实测对照（修复前 / 修复后，`Camera.WorldToViewportPoint`）：
> | 键 | 修复前 viewportX | 修复后 viewportX |
> |---|---|---|
> | A0（21，低音） | **1.047**（画面右外） | **-0.026 → 0.238**（画面左） |
> | C8（108，高音） | -0.026（画面左） | 1.047 → 0.608（画面右） |

---

## 1. 手指与琴键不对应

### 1.1 实测现象（修复前）

| 被测项 | 修复前实测 |
|---|---|
| 左手和弦 48/52/55 的指法分配 | 指 0（拇指）/ 指 2（中指）/ 指 4（小指） |
| 同一和弦下 `HandIKController` 的指尖目标 | `-` / `-` / 指 4 ⇒ 与指法分配**不一致** |
| 右手 64 的指法 | 指 3，但指尖目标是**指 4** ⇒ 错配 |
| 指尖到键面按压点的误差 | **95.3 / 130.3 / 107.7 / 112.4 mm**（容差 35 mm） |
| 左手腕位置 | `x = 0.219`（停在**最低音键** 0.216 处），而三音和弦跨 96 mm ⇒ 另两指根本够不到 |

### 1.2 根因

1. **两套独立的手指归属**：`FingerPoseController` 按指法提供器决定"哪根手指弯下去"，
   而 `HandIKController` 用「离键最近的指尖」**自行配对**。两套结果会互相矛盾 ⇒
   "弯下去的手指不是落在键上的手指"。
2. **手掌定位取的是极端音**：手腕目标用该手"最低音键（左手）/ 最高音键（右手）"，
   于是手掌停在和弦的一端，另一端的指头离键越来越远。
3. **指尖只被"弯曲"，没有被"对准"**：手指只有固定按压角 + 张开角，方向与目标键无关。

### 1.3 改动

| 改动 | 说明 |
|---|---|
| 新增 `Avatar/FingerAssignmentService.cs` | 双手共用的「手指 ↔ 音高」分配缓存；两个控制器读**同一份**结果；同帧幂等（谁先跑谁算，另一处复用），因此不存在 1 帧错配 |
| `FingerPoseController` | 改为读共用分配；手指姿态由"固定角度"改为**朝指尖目标对准**（`TryAimAt` 在手腕空间解出局部旋转，拇指对掌偏航被正确保留） |
| `HandIKController` | 指尖目标 = 「分配给第 f 根手指的那个音高」的键面按压点（删除"最近指尖"配对）；新增**手掌几何反馈**：把各指尖到各自琴键的平均误差作为手腕 XZ 修正量（有界 + 指数阻尼），让指尖真正贴上键 |
| `LessonSessionController` | 创建一个 `FingerAssignmentService` 实例，注入 `FingerPoseController.Assignment` 与 `HandIKController.Assignment`，并把 `HandIKController` 作为 `FingerPoseController.TargetSource` |

### 1.4 修复后实测

| 被测项 | 修复后 |
|---|---|
| 指法分配 / 指尖目标 / 最近指尖 | **三者完全一致**（0/2/4 与 3 —— 逐项相等） |
| 指尖误差（静态和弦，收敛后） | **17.7 / 3.3 / 31.8 / 6.1 mm**（全部 < 40 mm 容差） |
| 指尖误差（播放期间平均） | **≈ 38–41 mm**（含换位过渡帧；修复前 ≈ 99 mm） |
| 手腕目标命中率 | 93.6%（左 94.7 / 右 92.7，未放宽约束） |
| 单帧最大位移 | 35 mm（硬约束未动） |
| 穿模告警 | 0 |

### 1.5 怎么验证

- **自动化**：`HandFingerCorrespondenceTests`
  - `Fingers_EachPressedKeyHasExactlyOneFingerPointingAtIt`
    —— 对和弦每个音断言：恰好一根手指被分配；该指的分配表 == 该音；该指的指尖目标 == 该音的键面按压点；没有第二根手指重复认领。
  - `Fingers_NearestFingertipIsTheAssignedFinger`
    —— 断言"实际离键最近的指尖"就是"被分配的那根"，并要求收敛后每指误差 < 40 mm。
  - `Fingers_ActuallyReachTheKeys_WhilePlaying` —— 播放期间平均指尖误差 < 50 mm。
- **人工**：截图 `V01_closeup_chord.png` / `V13_teaching_mode.png` —— 手指应各自落在自己按的键上（不再整只手停在一端）。

---

## 2. 新增两种可切换显示模式

### 2.1 需求与实现

| 模式 | 行为 |
|---|---|
| **展示模式（Showcase，默认）** | 保持完整手部动画；琴键高亮按用户设置（关闭 / 预提示 / 发声 / 全部） |
| **教学模式（Teaching）** | **隐藏手部与手臂**动画；只高亮**被按下的琴键**（`HighlightMode.Active`），左手冷蓝 / 右手暖橙 |

- 新增 `Core/DisplayMode.cs`；`LessonSessionController` 新增
  `DisplayMode` / `SetDisplayMode` / `ToggleDisplayMode` / `IsTeachingMode` / `OnDisplayModeChanged`。
- UI：练习工具栏第二行新增 **`展示 | 教学` 分段选择器**（直接显示当前模式），
  3D 区快捷键提示补上 **`T`**，快捷键 `T` 一键切换。
- 教学模式期间「高亮」分段控件被**置灰并说明原因**（该模式已固定为"仅高亮被按下的琴键"），
  避免用户以为设置失效；切回展示模式后恢复用户原来的选择。
- 教学模式下手部不可见，相机取景范围改由**当前按下的琴键音高范围**提供（会话跟踪按下音高）。
- 偏好持久化：`UserPreferences.DisplayMode`（重开应用仍保持上次模式）。

### 2.2 为什么"切换后不残留、不视觉错乱"

关键设计：**隐藏手部时只关掉网格 GameObject，不动骨骼与 IK**。

- 手部 IK 仍按时间轴正常解算 ⇒ 切回展示模式**不需要重建任何状态**，也不会出现"手突然跳到某个位置"。
- 切换时执行一次 `ResetPlaybackState() + SeekTo(当前时间)` ⇒ 按键高亮 / 手部姿态 / 指法全部
  **按当前时间轴重建**，因此不会残留上一模式的高亮或姿态。
- 教学模式强制高亮 = `Active`（只亮被按下的键）；退出时按进入前的用户偏好还原。
- 时间轴完全不受影响（切换只改表现层）——`PlaybackState` 不变、`CurrentTime` 不回退。

### 2.3 怎么验证

- **自动化**：`DisplayModeTests`
  - `DefaultMode_IsShowcase_WithHandsVisible`
  - `Teaching_HidesHandsAndForcesPressedKeyHighlight` —— 手部隐藏 + 高亮被固定为 `Active`
  - `Teaching_HighlightedKeysMatchActuallyPressedKeys` —— 逐帧断言
    **高亮琴键数 == 当前按下琴键数**，且预提示高亮数为 0（"不许多亮、不许少亮"）
  - `SwitchRoundTrip_LeavesNoResidue` —— 教学 → 展示往返后：手部恢复可见、高亮恢复用户偏好、
    播放状态与时间不变、高亮集合不超出"按下 + 预提示"
  - `ToggleDisplayMode_FlipsBothWays`、`Teaching_SurvivesAvatarSwitch_HandsStayHidden`
- **人工**：`V13_teaching_mode.png`（教学模式）与 `V14_showcase_mode.png`（**同机位、同曲目时刻**的展示模式）
  —— 两张图应只有"手部是否可见"的差别：键盘、被按下的琴键、左右手颜色完全一致。

---

## 3. 人物左右手反向

### 3.1 实测现象（修复前）

| 被测项 | 修复前 |
|---|---|
| `Skeleton.LeftHand`（名为 Left 的骨骼） | world `x = -0.595` ⇒ 落在角色的**右手侧** |
| 可见 `LeftHand.Wrist` | world `x = -0.300` ⇒ 同上 |
| 低音键 A0 | 位于 world `-X` ⇒ 在**画面右侧** |
| 结果 | 低音（冷蓝）键与"左手"一起出现在**画面右侧**，与钢琴/乐谱的通用方向完全相反；且名为 Left 的手其实是角色的右手 |

### 3.2 根因

`AvatarManager` 的注释把"局部 `+X` 旋转 180° 后落在世界 `-X`"直接等同于"演奏者左手侧"，
但**角色面向 `-Z` 时其真正的左手在世界 `+X`**（面向 `-Z`，左手在 +X；可自行代入：转身 180° 后左右互换）。
于是"左/右"从根上就被定义反了；键盘的低音端也放在了世界 `-X`，两处错误互相"抵消"，
所以功能上看不出问题（低音仍由名为 Left 的手弹），但从画面与人体解剖上看整体是镜像的。

### 3.3 改动

| 文件 | 改动 |
|---|---|
| `PianoKeyMap.BuildKeys` | 键的世界 X 镜像：`x = 键盘总宽 − 原x − 键宽`（**低音移到世界 +X**）。索引算法 `WhiteIndex` / `BlackKeyLeftWhiteIndex` 语义不变，便于单测 |
| `AvatarRigBuilder.LocalPositionOf` | `Left*` 骨骼写局部 `−X`（旋转后 = 世界 `+X` = 角色真正左手侧）：肩、上臂、前臂、手、上腿 |
| `AvatarRigBuilder.BuildFinger` | 拇指横向偏移的左右符号取反 ⇒ 拇指回到**内侧**（朝身体中线），虎口方向正确 |
| `AvatarManager.BuildShoulders` | 左右肩球位置对调（并改写原本错误的注释） |
| `AvatarManager.BuildHand` | 左手 `side = −1`、右手 `side = +1` ⇒ 手部网格位置与左右手型镜像一并摆正 |
| `HandIKController.DefaultHandPos` | 无目标时：左手默认在世界 `+X` |
| `HandPoseValidator.AreHandsCrossed` | 判定方向取反：**左手 X 反而小于右手**才是交叉（原方向会把正常手位误报为交叉） |
| 测试 | `CameraFramingTests` 的单手取景用例、`HandPoseValidatorTests` 的交叉判定用例同步按新约定更新 |

### 3.4 修复后实测

- `Skeleton.LeftHand` → world **`+0.595`**；可见 `LeftHand.Wrist` → **`+0.300`**（都在 +X）
- A0 → viewportX **-0.026（特写）/ 0.238（全景）** ⇒ **低音在画面左侧**；C8 在右侧
- 按低音 45：动的是**左手**（`+X` 侧，画面左）；按高音 76：动的是**右手**（画面右）
- 低音键高亮 = **左手冷蓝**；高音键 = **右手暖橙**（同帧读取 `MaterialPropertyBlock._Color` 验证）

### 3.5 怎么验证

- **自动化**：`HandFingerCorrespondenceTests`
  - `Hands_LeftHandIsOnLowNoteSide` —— 可见手 + Humanoid 骨骼的左手都在世界 `+X`
  - `Hands_LowNotesArePlayedByLeftHand` —— 低音 → 左手腕移到该键；高音 → 右手腕移到该键
  - `Screen_LowNotesAppearOnTheLeft` —— 特写与全景下 `viewportX(A0) < viewportX(C8)`
  - `Highlight_LeftHandColorOnLowKeys_RightHandColorOnHighKeys` —— 读琴键实际渲染色，断言
    低音 = `LeftHandColor`（冷蓝）、高音 = `RightHandColor`（暖橙），且低音键在 `+X`、高音键在 `-X`
- **人工**：`V01/V02/V05/V06`（特写：左手练习时应框住画面左侧的手）与 `V03/V04`（全景）看键盘方向是否符合"低音在左"。

---

## 4. 一键复现

```bash
cd "D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d"

python Tools/verify.py --stress                 # 编译 + EditMode + PlayMode + 压力
python Tools/verify.py --visual                 # 生成截图（需 GPU，勿加 -nographics）

# 只跑本轮新增的两组对应关系/显示模式用例
Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode \
          -testFilter "HandFingerCorrespondenceTests|DisplayModeTests"
Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode \
          -testFilter "FingerAssignmentServiceTests"
```

**预期结果**：编译 0 错误 0 警告；`FingerAssignmentServiceTests` 7/7；`HandFingerCorrespondenceTests` 6/6；
`DisplayModeTests` 7/7；全量 PlayMode 与 Stress 全绿；截图目录新增
`V13_teaching_mode.png` 与 `V14_showcase_mode.png`。
