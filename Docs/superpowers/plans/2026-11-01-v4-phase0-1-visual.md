# Piano Teacher 3D v4 阶段 0–1（真实基线 + 视觉与首屏）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 先把用户真实看到的画面拍下来并修正默认观感：真人角色特写、琴房环境、乐谱细节、信息完整的主菜单；同时保持既有测试与架构约束全绿。

**Architecture:** 在现有 `LessonSessionController` 门面与唯一时间轴之上做“表现层增强”，不改演奏核心。新增能力按“纯逻辑可单测 + 薄表现层”拆分；所有 UI 只调用会话公开接口，环境与琴体扩展只新增几何/材质，不改变 88 键坐标与 IK 常量。

**Tech Stack:** Unity 2022.3.22f1c1、C# 9、URP 14.0.10、UGUI（legacy Text）、Unity Test Framework、Python 3（仅 `Tools/` 脚本）。

---

## 0. 执行约定

- 项目根：`D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d`
- 分支：`feature/v4-full-upgrade`
- Unity：`D:\Unity\Unity 2022.3.22f1c1\Editor\Unity.exe`（或 `UNITY_PATH`）
- 快速验证：`python Tools/verify.py --edit-only`
- 全量验证：`python Tools/verify.py`
- 视觉验证：`python Tools/verify.py --visual --timeout 900`（**不加 `-nographics`**，需要 GPU）
- 静态门禁：`python Tools/static_checks.py` 与 `python Tools/check_architecture.py`
- 每个任务结束必须提交；提交信息用中文，前缀按模块 `feat(visual):` / `feat(ui):` / `test(visual):` / `docs:`。
- 任何视觉改动后必须人工看图；像素比较只作辅助。

---

## Task 1: 采集真实默认体验基线（阶段 0）

**Files:**
- Modify: `Assets/Tests/PlayMode/VisualSnapshotTests.cs`
- Create: `Docs/reports/upgrade-2026-11-01.md`

**目的：** 现有 01–06 / V01–V14 在 batchmode 下拍的是内置“气球人”，而生产默认是 Rocketbox。先拍真人特写，确认手部、坐姿、遮挡是否真的可用。

- [ ] **Step 1: 在 `VisualSnapshotTests` 中新增真人特写/全景截图**

在 `V15_rocketbox_character.png` 的 `records.Add(...)` 之后、`session.SwitchAvatar(0);` 之前插入：

```csharp
        // ---- V17 / V18 / V19：真实默认角色（Rocketbox）的特写与全景 ----
        // 为什么单独拍：生产默认 SelectedCharacterId = rocketbox.female01，但 batchmode
        // 按设计跳过偏好，01–06 / V01–V14 拍的仍是内置角色。真人特写从未有过基线，
        // 手部弯曲、指尖贴键、头/躯干遮挡只能靠这三张图判断。
        session.SwitchAvatar(0);                 // 先离开外部角色，确保事务可重入
        yield return new WaitForSeconds(0.4f);
        if (foundProfile)
        {
            var reActivate = session.ActivateCharacter(rocketboxProfile);
            Debug.Log(string.Format("[V17] Rocketbox 再次激活：成功={0} 原因={1}",
                reActivate.Succeeded, string.IsNullOrEmpty(reActivate.Error) ? "(无)" : reActivate.Error));
            yield return new WaitForSeconds(1.0f);
        }

        session.Stop();
        session.ClearLoop();
        session.SetPracticeHand(PracticeHand.Both);
        session.SetHighlightMode(HighlightMode.Off);
        session.SetCameraMode(CameraMode.Closeup);
        session.Seek(0f);
        yield return new WaitForSeconds(2.0f);
        yield return Capture(cam, Path.Combine(dir, "V17_rocketbox_closeup_idle.png"));
        records.Add(Snapshot("V17_rocketbox_closeup_idle.png", "v4 阶段 0 / 真实默认体验",
            "真人角色手部特写（空闲）：手掌、五指、琴键同时可见；头/躯干不遮挡键面",
            "Rocketbox 默认真人角色，停止态；用于判断手型与遮挡",
            session, cameraCtrl, avatar, timeline));

        float v18Time = notes.Count > 0 && NoteScan.TryFindChordTime(notes, 2, 48, 76, 0.06f, out float chord18, out _)
            ? chord18
            : total * 0.25f;
        session.Seek(v18Time);
        session.Play();
        yield return new WaitForSeconds(0.30f);
        yield return Capture(cam, Path.Combine(dir, "V18_rocketbox_closeup_playing.png"));
        records.Add(Snapshot("V18_rocketbox_closeup_playing.png", "v4 阶段 0 / 真实默认体验",
            "真人角色手部特写（演奏中，不暂停）：指节弯曲与指尖接触可见",
            string.Format("t={0:F2}s，播放中抓帧", v18Time),
            session, cameraCtrl, avatar, timeline));
        session.Pause();

        session.SetCameraMode(CameraMode.Full);
        session.Stop();
        session.Seek(0f);
        yield return new WaitForSeconds(1.5f);
        yield return Capture(cam, Path.Combine(dir, "V19_rocketbox_full.png"));
        records.Add(Snapshot("V19_rocketbox_full.png", "v4 阶段 0 / 真实默认体验",
            "真人角色全景：坐姿、琴凳、琴体、键面完整；无穿模与悬空",
            "Rocketbox 默认真人角色，停止态",
            session, cameraCtrl, avatar, timeline));
```

- [ ] **Step 2: 运行视觉截图**

Run:

```powershell
cd "D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d"
python Tools/verify.py --visual --timeout 900
```

Expected: `已生成 25 张截图`（原 22 张 + V17/V18/V19；如果 V15 激活失败则仍会生成但备注会说明）。

- [ ] **Step 3: 人工看图并记录结论**

用图片查看器打开：

- `Docs/screenshots/V17_rocketbox_closeup_idle.png`
- `Docs/screenshots/V18_rocketbox_closeup_playing.png`
- `Docs/screenshots/V19_rocketbox_full.png`

逐项记录：手型是否自然、指尖是否贴键、头部/肩膀是否进入画面、坐姿是否穿模、琴凳接触是否可信。

- [ ] **Step 4: 写升级记录**

创建 `Docs/reports/upgrade-2026-11-01.md`：

```markdown
# v4 全面升级执行记录（2026-11-01）

> 分支：feature/v4-full-upgrade
> 设计：docs/superpowers/specs/2026-11-01-v4-full-upgrade-design.md

## 阶段 0：真实默认体验基线

### 新增截图

| 文件 | 内容 |
|---|---|
| V17_rocketbox_closeup_idle.png | 真人角色手部特写（停止） |
| V18_rocketbox_closeup_playing.png | 真人角色手部特写（播放中） |
| V19_rocketbox_full.png | 真人角色全景 |

### 人工核对结论

| 项 | 结论 | 证据 |
|---|---|---|
| 手型自然度 | （按实际填写） | V17/V18 |
| 指尖贴键 | （按实际填写） | V18 |
| 头/肩遮挡 | （按实际填写） | V17 |
| 坐姿/琴凳接触 | （按实际填写） | V19 |
| 下一步 | 若手部不合格，阶段 1 先修手部链路；否则进入场景与主菜单 | — |
```

- [ ] **Step 5: 提交**

```powershell
git add Assets/Tests/PlayMode/VisualSnapshotTests.cs Docs/screenshots Docs/reports/upgrade-2026-11-01.md
git commit -m "test(visual): 新增真人角色特写/全景真实默认基线"
```

---

## Task 2: 特写隐藏内置角色身体（保留手臂与手）

**Files:**
- Modify: `Assets/Scripts/Avatar/AvatarManager.cs`
- Modify: `Assets/Scripts/Core/LessonSessionController.cs`
- Test: `Assets/Tests/PlayMode/CloseupPresentationTests.cs`（新建）

**说明：** Rocketbox 是一张蒙皮网格，不能只隐藏身体；此任务只对内置程序化角色生效。真人角色仍靠相机构图（Task 3）。

- [ ] **Step 1: 写失败测试**

创建 `Assets/Tests/PlayMode/CloseupPresentationTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>特写模式：内置角色隐藏身体、保留手臂与手；切回全景恢复。</summary>
    public class CloseupPresentationTests
    {
        [UnityTest]
        public IEnumerator Closeup_HidesBuiltInBody_ButKeepsArmsAndHands()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<LessonSessionController>();
            var avatar = Object.FindFirstObjectByType<AvatarManager>();
            Assert.IsNotNull(session);
            Assert.IsNotNull(avatar);

            session.SwitchAvatar(0);
            yield return null;

            Transform root = avatar.GetAvatarRoot(0);
            Assert.IsNotNull(root);
            Transform head = FindDeep(root, "Head");
            Transform leftHand = FindDeep(root, "LeftHand");
            Assert.IsNotNull(head);
            Assert.IsNotNull(leftHand);

            session.SetCameraMode(CameraMode.Closeup);
            yield return new WaitForSeconds(0.6f);

            Assert.IsFalse(head.gameObject.activeInHierarchy, "特写下内置角色的 Head 应隐藏");
            Assert.IsFalse(head.gameObject.activeSelf, "Head 对象本身应被停用");
            Assert.IsTrue(leftHand.gameObject.activeInHierarchy,
                "特写下手臂与手必须保留，否则无法示范指法");

            session.SetCameraMode(CameraMode.Full);
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(head.gameObject.activeInHierarchy, "切回全景后身体应恢复");
        }

        private static Transform FindDeep(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == name) return all[i];
            return null;
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
python Tools/verify.py --edit-only
```

Expected: 编译失败或测试失败，提示 `AvatarManager` 没有对应行为 / Head 仍可见。

- [ ] **Step 3: 实现 `AvatarManager` 的身体可见性**

在 `AvatarManager` 字段区新增：

```csharp
        /// <summary>内置角色身体（非手臂/手）是否可见；特写时隐藏。</summary>
        private bool _bodyVisible = true;
```

在 `AvatarRig` 内部类新增：

```csharp
            /// <summary>本角色的身体部件（头/躯干/腿/琴凳/肩球），特写时整组隐藏。</summary>
            public readonly List<GameObject> BodyParts = new List<GameObject>();
```

在 `BuildAvatarRig` 返回前（`rig.Root = root;` 之前）调用：

```csharp
            CollectBodyParts(rig, root);
```

新增方法：

```csharp
        /// <summary>
        /// 内置程序化角色的身体可见性：隐藏头/躯干/腿/琴凳，保留肩臂与双手。
        /// <para>外部蒙皮角色（Rocketbox）是一张整体网格，本方法<b>不</b>拆分或隐藏它；
        /// 真人特写由相机构图负责，避免破坏蒙皮。</para>
        /// </summary>
        public void SetBodyVisible(bool visible)
        {
            _bodyVisible = visible;
            if (ExternalRigSource != null) return;   // 外部角色：单张蒙皮，不能只隐藏身体
            ApplyBodyVisibility(_currentIndex, visible);
        }

        /// <summary>当前身体是否可见（外部角色恒为 true）。</summary>
        public bool BodyVisible => ExternalRigSource != null || _bodyVisible;

        private void ApplyBodyVisibility(int index, bool visible)
        {
            if (_rigs == null || index < 0 || index >= _rigs.Count) return;
            var parts = _rigs[index].BodyParts;
            for (int i = 0; i < parts.Count; i++)
            {
                var go = parts[i];
                if (go == null) continue;
                if (go.activeSelf != visible) go.SetActive(visible);
            }
        }

        /// <summary>
        /// 收集“身体”部件：按构建时使用的对象名；手臂/手及其子节点必须保留。
        /// 名字来自 <see cref="BuildStool"/> / <see cref="BuildLegs"/> / <see cref="BuildTorso"/> /
        /// <see cref="BuildNeckHead"/> / <see cref="BuildShoulders"/>。
        /// </summary>
        private static void CollectBodyParts(AvatarRig rig, Transform root)
        {
            rig.BodyParts.Clear();
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == root) continue;
                if (IsBodyPartName(t.name)) rig.BodyParts.Add(t.gameObject);
            }
        }

        private static bool IsBodyPartName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n == "Stool" || n == "StoolCushion" ||
                   n.StartsWith("StoolLeg_") ||
                   n.StartsWith("Thigh_") || n.StartsWith("Knee_") ||
                   n.StartsWith("Shin_") || n.StartsWith("Foot_") ||
                   n == "Hip" || n == "Waist" || n == "Chest" || n == "UpperTorso" ||
                   n == "ClothHem" || n == "WaistBand" || n == "Collar" ||
                   n == "Neck" || n == "Head" || n == "Jaw" ||
                   n.StartsWith("Ear_") || n == "NoseBridge" || n == "Nose" ||
                   n.StartsWith("EyeWhite_") || n.StartsWith("Eye_") ||
                   n.StartsWith("Brow_") || n.StartsWith("EyeSocket_") ||
                   n.StartsWith("UpperLid_") || n.StartsWith("LowerLid_") ||
                   n.StartsWith("Cheekbone_") || n.StartsWith("Nostril_") || n == "Mouth" ||
                   n == "HairCap" || n == "HairFringe" ||
                   n.StartsWith("HairSide_") || n == "HairBack" ||
                   n.StartsWith("HairSideburn_") || n == "HairNape" || n == "Ahoge" ||
                   n.StartsWith("ShoulderCap_") || n == "Ball";
        }
```

在 `SetBuiltInActive` 末尾（设置 `_currentIndex` 之后）追加：

```csharp
            ApplyBodyVisibility(index, _bodyVisible);
```

- [ ] **Step 4: 会话层在视角切换时应用**

在 `LessonSessionController` 新增：

```csharp
        /// <summary>特写隐藏内置角色身体（外部蒙皮角色不处理）。</summary>
        private void ApplyCloseupPresentation()
        {
            if (_avatar == null) return;
            bool closeup = _camera != null && _camera.Mode == CameraMode.Closeup;
            _avatar.SetBodyVisible(!closeup);
        }
```

在 `AfterCameraModeChanged()` 内调用 `ApplyCloseupPresentation();`；在 `AfterAvatarChanged()` 与 `ActivateCharacter` 成功后也调用；在 `BuildScene` 初始化相机后调用一次。

- [ ] **Step 5: 运行测试**

Run:

```powershell
python Tools/verify.py --edit-only
python Tools/verify.py --timeout 900
```

Expected: 新增用例通过；既有 EditMode 473/473、PlayMode 124 通过 + 5 跳过不变。

- [ ] **Step 6: 视觉确认并提交**

Run:

```powershell
python Tools/verify.py --visual --timeout 900
```

Expected: `02_closeup` 等内置角色特写不再出现头/躯干大面积入画；Rocketbox 特写保持原样（Task 3 再调）。

```powershell
git add Assets/Scripts/Avatar/AvatarManager.cs Assets/Scripts/Core/LessonSessionController.cs Assets/Tests/PlayMode/CloseupPresentationTests.cs Docs/screenshots
git commit -m "feat(visual): 特写隐藏内置角色身体并保留手臂与手"
```

---

## Task 3: 真人角色特写构图 A/B 调参

**Files:**
- Modify: `Assets/Scripts/Camera/CameraModeController.cs`
- Inspect: `Docs/screenshots/V17_rocketbox_closeup_idle.png`、`V18_rocketbox_closeup_playing.png`

**决策规则：**

- 若 V17/V18 中头/肩占据画面右下或下缘超过约 15%，把 `CloseupViewPitch` 从 `60f` 试到 `66f` / `70f`，
  并把 `CloseupMaxDistance` 从 `0.88f` 收到 `0.72f`，重拍对比；取“手部占屏更大且头肩更少”的一组。
- 若头/肩不明显，**不改参数**，只记录结论。
- 不允许把 `MaxStepPerFrame`、`FingertipErrorTolerance` 等 IK 阈值放宽。

- [ ] **Step 1: 改参数**

示例（仅当决策规则判定需要时）：

```csharp
        public float CloseupViewPitch = 66f;
        public float CloseupMaxDistance = 0.72f;
```

- [ ] **Step 2: 重拍并人工对比**

```powershell
python Tools/verify.py --visual --timeout 900
```

对比 V17/V18 新旧图；若无改善则回退参数。

- [ ] **Step 3: 跑相机可见性用例**

```powershell
& "D:\Unity\Unity 2022.3.22f1c1\Editor\Unity.exe" -batchmode -nographics -projectPath "D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d" -runTests -testPlatform PlayMode -testFilter CameraVisibility -testResults "_verify_out\cam_v4.xml" -logFile "_verify_out\cam_v4.log" -quit
```

Expected: 9 场景全通过。

- [ ] **Step 4: 提交**

```powershell
git add Assets/Scripts/Camera/CameraModeController.cs Docs/screenshots Docs/reports/upgrade-2026-11-01.md
git commit -m "feat(camera): 真人角色特写构图调参（头肩避让）"
```

---

## Task 3B: 真人角色静止手型实测与修复

**Files:**
- Create: `Assets/Tests/PlayMode/RocketboxRestPoseTests.cs`
- Modify（按诊断结果）: `Assets/Scripts/Avatar/FingerPoseController.cs`、`Assets/Scripts/Avatar/HandIKController.cs`、
  `Assets/Scripts/Characters/ImportedFingerJointAxes.cs`

**背景：** 阶段 0 的 V17 显示真人双手空闲时五指张开、几乎伸直并悬空。先用实测把问题归因到
「相位 / 关节轴 / 收拢 / 腕高」中的哪一层，禁止直接调观感参数掩盖。

- [ ] **Step 1: 写实测用例**

创建 `Assets/Tests/PlayMode/RocketboxRestPoseTests.cs`：加载课程场景 → 激活 Rocketbox → `Stop()` → 等 1 s →
读取左手三关节，输出每指 `FingerPhase`、PIP 弯曲角、指尖高于键面的毫米数；断言
「空闲时 5 指均为 Rest」且「至少 3 指 PIP > 10°」。

- [ ] **Step 2: 运行并读取日志**

```powershell
& "D:\Unity\Unity 2022.3.22f1c1\Editor\Unity.exe" -batchmode -nographics -projectPath "D:\WorkBuddy Project\钢琴3D教学\piano-teacher-3d" -runTests -testPlatform PlayMode -testFilter RocketboxRestPose -testResults "_verify_out\rocket_rest.xml" -logFile "_verify_out\rocket_rest.log" -quit
```

从 `_verify_out/rocket_rest.log` 读取 `[RocketboxRestPose]` 行，按结果分支：

- **相位不是 Rest**：说明空闲时仍有指尖目标。检查 `HandIKController.UpdateFingertipTargets`
  是否在无按下音高时仍保留 `has[f]`，以及 `FingerPoseController` 是否读到上一帧目标。
- **PIP 弯曲 ≈ 0**：说明 `RestCurlDegrees` 没有作用到导入骨架。检查
  `FingerPoseController.JointAxes` 是否为 `ImportedFingerJointAxes`，以及
  `CurlLocalRotation` 的 `Rest` 是否被覆盖。
- **PIP 弯曲 > 10° 但观感仍差**：说明问题在「指间收拢 / 腕高 / 相机俯角」。进入 Step 3 的收拢与腕高调整。

- [ ] **Step 3: 最小修复**

优先只改以下三类，不改 IK 阈值：

1. **静止相位**：保证无按下音高时 `TryGetFingertipTarget` 返回 false（必要时在
   `HandIKController.UpdateFingertipTargets` 的 `pitches.Count == 0` 分支显式清空并同步 reset 标志）。
2. **静止收拢**：为导入手增加「静止指间收拢」——在 `FingerPoseController` 的 Rest 分支，
   把每指指根绕「腕部规范基的 Y 轴」朝中指方向旋转一个小角度（默认 2°–4°，拇指单独 0°），
   用 `ImportedFingerJointAxes.RootAimLocalRotation` 或新增 `RestAdductionLocalRotation` 实现；
   不改按压阶段的 CCD。
3. **静止腕高**：若指尖离键面 > 60 mm，按实际值把空闲腕高下调（仅空闲分支），
   幅度不超过 20 mm，且不影响 `HandKeyPos` 的按键目标。

- [ ] **Step 4: 复跑用例与视觉**

```powershell
python Tools/verify.py --timeout 900
python Tools/verify.py --visual --timeout 900
```

Expected: `RocketboxRestPoseTests` 通过；V17/V18 中至少 3 指呈可见弯曲，指尖不再整体悬空；
既有 473 + 124 用例不减少。

- [ ] **Step 5: 提交**

```powershell
git add Assets/Tests/PlayMode/RocketboxRestPoseTests.cs Assets/Scripts/Avatar Docs/screenshots Docs/reports/upgrade-2026-11-01.md
git commit -m "fix(hand): 真人角色静止 C 形手与空闲相位修复"
```

---

## Task 4: 琴房环境（墙、窗、地毯、装饰）

**Files:**
- Modify: `Assets/Scripts/Piano/PianoSceneEnvironment.cs`
- Modify: `Assets/Scripts/Piano/ProceduralTextures.cs`
- Test: `Assets/Tests/EditMode/SceneEnvironmentTests.cs`

**目标：** 把“黑色虚空 + 一块背景板”变成小型琴房，同时保持低对比、低 draw call、唯一投影灯。

- [ ] **Step 1: 写失败测试**

在 `SceneEnvironmentTests.cs` 追加（**不要调用 `PianoKeyMap.BuildKeys()`**，该方法的 `Destroy` 在 EditMode 非法）：

```csharp
        [Test]
        public void Build_CreatesRoomShell_AndKeepsEnvironmentToggleIdempotent()
        {
            var go = new GameObject("Env", typeof(PianoSceneEnvironment));
            try
            {
                var env = go.GetComponent<PianoSceneEnvironment>();
                env.Build(null, QualityProfile.For(QualityTier.Standard));

                var names = new System.Collections.Generic.HashSet<string>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) names.Add(t.name);

                Assert.IsTrue(names.Contains("RoomBackWall"), "缺少后墙");
                Assert.IsTrue(names.Contains("RoomSideWall_L"), "缺少左侧墙");
                Assert.IsTrue(names.Contains("RoomSideWall_R"), "缺少右侧墙");
                Assert.IsTrue(names.Contains("RoomRug"), "缺少地毯");
                Assert.IsTrue(names.Contains("RoomWindow"), "缺少窗户");
                Assert.IsTrue(names.Contains("RoomCeiling"), "缺少顶棚");

                env.SetEnvironmentVisible(false);
                env.SetEnvironmentVisible(false);   // 幂等
                env.SetEnvironmentVisible(true);
                Assert.IsNotNull(env.MainLight);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
python Tools/verify.py --edit-only
```

Expected: FAIL，缺少 `RoomBackWall` 等对象。

- [ ] **Step 3: 扩展环境几何**

在 `PianoSceneEnvironment.BuildGeometry()` 末尾调用 `BuildRoom();`。

新增常量与方法（放在现有几何装配区）：

```csharp
        private static readonly Color WallColor = new Color(0.185f, 0.175f, 0.165f);
        private static readonly Color CeilingColor = new Color(0.135f, 0.132f, 0.128f);
        private static readonly Color RugColor = new Color(0.235f, 0.180f, 0.150f);
        private static readonly Color WindowGlow = new Color(0.72f, 0.80f, 0.92f);

        /// <summary>琴房外壳：后墙/侧墙/顶棚/窗/地毯/墙板。全部低对比、无碰撞、不改地面高度。</summary>
        private void BuildRoom()
        {
            var wallMat = CreateLitMaterial("EnvWall", WallColor, 0.08f, 0f);
            var ceilingMat = CreateLitMaterial("EnvCeiling", CeilingColor, 0.06f, 0f);
            var rugMat = CreateLitMaterial("EnvRug", RugColor, 0.04f, 0f);

            Register(CreateBox("RoomBackWall", new Vector3(0f, 1.5f, -2.90f),
                new Vector3(7f, 3f, 0.10f), wallMat, castShadows: false, receiveShadows: true));
            Register(CreateBox("RoomSideWall_L", new Vector3(-3.45f, 1.5f, 0.2f),
                new Vector3(0.10f, 3f, 6.2f), wallMat, castShadows: false, receiveShadows: true));
            Register(CreateBox("RoomSideWall_R", new Vector3(3.45f, 1.5f, 0.2f),
                new Vector3(0.10f, 3f, 6.2f), wallMat, castShadows: false, receiveShadows: true));
            Register(CreateBox("RoomCeiling", new Vector3(0f, 3.02f, 0.2f),
                new Vector3(7f, 0.08f, 6.2f), ceilingMat, castShadows: false, receiveShadows: false));

            Register(CreateBox("RoomWindowFrame", new Vector3(1.15f, 2.10f, -2.845f),
                new Vector3(1.55f, 1.20f, 0.05f), wallMat, castShadows: false, receiveShadows: false));
            var glow = CreateUnlitMaterial("EnvWindowGlow", WindowGlow);
            Register(CreateBox("RoomWindow", new Vector3(1.15f, 2.10f, -2.815f),
                new Vector3(1.38f, 1.04f, 0.01f), glow, castShadows: false, receiveShadows: false));
            Register(CreateBox("RoomWindowBarV", new Vector3(1.15f, 2.10f, -2.805f),
                new Vector3(0.03f, 1.04f, 0.02f), wallMat, castShadows: false, receiveShadows: false));
            Register(CreateBox("RoomWindowBarH", new Vector3(1.15f, 2.10f, -2.805f),
                new Vector3(1.38f, 0.03f, 0.02f), wallMat, castShadows: false, receiveShadows: false));

            Register(CreateBox("RoomRug", new Vector3(0f, 0.004f, 0.25f),
                new Vector3(2.70f, 0.006f, 1.90f), rugMat, castShadows: false, receiveShadows: true));

            var panelMat = CreateLitMaterial("EnvWallPanel", new Color(0.135f, 0.125f, 0.118f), 0.10f, 0f);
            Register(CreateBox("RoomPanel_L", new Vector3(-3.38f, 1.55f, -0.9f),
                new Vector3(0.04f, 0.90f, 1.20f), panelMat, castShadows: false, receiveShadows: true));
            Register(CreateBox("RoomPanel_R", new Vector3(3.38f, 1.55f, -0.9f),
                new Vector3(0.04f, 0.90f, 1.20f), panelMat, castShadows: false, receiveShadows: true));
        }

        /// <summary>发光窗格材质：URP/Unlit 优先，找不到时退回 Sprites/Default。</summary>
        private static Material CreateUnlitMaterial(string name, Color color)
        {
            var shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Sprites/Default")
                : Shader.Find("Unlit/Color")
                    ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }
```

同时修改既有 `Build_IsIdempotent_AndKeepsGeometryMinimal`：

```csharp
            // 琴房外壳新增约 12 个 Renderer；仍远低于 88 键规模（方案 7.2）
            Assert.Less(rendererCount, 40, "环境只应包含少量程序化几何，远低于 88 键规模");
```

- [ ] **Step 4: 运行测试与视觉验证**

```powershell
python Tools/verify.py --edit-only
python Tools/verify.py --visual --timeout 900
```

Expected: 新用例通过；01/03/V03/V19 等全景图中出现墙面、窗、地毯，整体不再像黑色虚空；`ContentBounds` 仍覆盖钢琴与地面。

- [ ] **Step 5: 提交**

```powershell
git add Assets/Scripts/Piano/PianoSceneEnvironment.cs Assets/Tests/EditMode/SceneEnvironmentTests.cs Docs/screenshots
git commit -m "feat(visual): 琴房环境（墙/窗/地毯/墙板）"
```

---

## Task 5: 谱架乐谱纸张

**Files:**
- Modify: `Assets/Scripts/Piano/PianoKeyMap.cs`
- Modify: `Assets/Scripts/Piano/ProceduralTextures.cs`
- Test: `Assets/Tests/PlayMode/SheetMusicTests.cs`（新建）

- [ ] **Step 1: 写失败测试**

创建 `Assets/Tests/PlayMode/SheetMusicTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>谱架乐谱：两页白纸在键面上方、键根后方，不遮挡键面。</summary>
    public class SheetMusicTests
    {
        [UnityTest]
        public IEnumerator Scene_HasTwoSheetMusicPages_AboveKeyboard()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var map = Object.FindFirstObjectByType<PianoKeyMap>();
            Assert.IsNotNull(map, "场景缺少 PianoKeyMap");

            Transform l = map.transform.Find("PianoCase/SheetMusic_L");
            Transform r = map.transform.Find("PianoCase/SheetMusic_R");
            Assert.IsNotNull(l, "缺少乐谱左页");
            Assert.IsNotNull(r, "缺少乐谱右页");
            Assert.Greater(l.position.y, map.KeySurfaceY + 0.20f, "乐谱应在键面上方");
            Assert.Less(l.position.z, 0.0f, "乐谱应在键根后方，不遮挡键面");
        }
    }
}
```

- [ ] **Step 2: 实现乐谱纹理**

在 `ProceduralTextures` 新增（放在 `Felt()` 之后）：

```csharp
        private const int SheetMusicSize = 256;
        private static Texture2D _sheetMusic;

        /// <summary>乐谱纸：暖白底 + 五条水平谱线 + 固定位置的少量音符点。</summary>
        public static Texture2D SheetMusic()
        {
            if (_sheetMusic != null) return _sheetMusic;

            int n = SheetMusicSize;
            var px = new Color32[n * n];
            var paper = new Color(0.935f, 0.918f, 0.875f);
            var ink = new Color(0.22f, 0.21f, 0.20f);

            for (int y = 0; y < n; y++)
            {
                float v = y / (float)n;
                bool staffLine = false;
                for (int k = 0; k < 5; k++)
                {
                    float lineY = 0.22f + k * 0.055f;
                    if (Mathf.Abs(v - lineY) < 0.004f) staffLine = true;
                }

                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n;
                    float grain = Fbm(u * 8f, v * 8f, 2) * 0.05f;
                    var c = Color.Lerp(paper, new Color(0.88f, 0.86f, 0.81f), grain);
                    if (staffLine) c = Color.Lerp(c, ink, 0.55f);

                    // 固定音符点：每 22 像素一格，落在某条谱线上
                    if (staffLine && (x / 22) % 7 == 0 &&
                        Mathf.Abs(v - (0.22f + ((x / 22) % 5) * 0.055f)) < 0.02f)
                        c = ink;

                    px[y * n + x] = c;
                }
            }

            _sheetMusic = Build(px, n, "ProceduralSheetMusic");
            _sheetMusic.wrapMode = TextureWrapMode.Clamp;
            return _sheetMusic;
        }
```

并在 `ReleaseAll()` 增加：

```csharp
            if (_sheetMusic != null) { Object.Destroy(_sheetMusic); _sheetMusic = null; }
```

- [ ] **Step 3: 在谱架上放两页乐谱**

在 `PianoKeyMap.BuildCase()` 的谱架之后加入：

```csharp
            // ---- 乐谱纸张：谱架上两页白纸，程序化五线谱纹理；不参与碰撞、不遮挡键面 ----
            var paperMat = CreateMaterial("SheetPaper", new Color(0.93f, 0.91f, 0.86f), 0f, 0.05f);
            ApplyProceduralTexture(paperMat, ProceduralTextures.SheetMusic(), 1f);
            var pageMesh = Track(PianoMeshFactory.CreateChamferedBox(0.30f, 0.36f, 0.004f, 0.001f, ChamferMode.AllEdges));
            var pageL = CreateCaseMeshPart(c, "SheetMusic_L", pageMesh,
                new Vector3(mid - 0.155f, 1.245f, -0.004f), paperMat);
            pageL.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            var pageR = CreateCaseMeshPart(c, "SheetMusic_R", pageMesh,
                new Vector3(mid + 0.155f, 1.245f, -0.004f), paperMat);
            pageR.localRotation = Quaternion.Euler(-14f, 0f, 0f);
```

- [ ] **Step 4: 测试与视觉**

```powershell
python Tools/verify.py --timeout 900
python Tools/verify.py --visual --timeout 900
```

Expected: `SheetMusicTests` 通过；全景/特写中谱架不再是纯黑板。

- [ ] **Step 5: 提交**

```powershell
git add Assets/Scripts/Piano/PianoKeyMap.cs Assets/Scripts/Piano/ProceduralTextures.cs Assets/Tests/PlayMode/SheetMusicTests.cs Docs/screenshots
git commit -m "feat(visual): 谱架乐谱纸张与五线谱纹理"
```

---

## Task 6: 曲目目录（catalog）与主菜单信息完整化

**Files:**
- Create: `Assets/Scripts/Core/SongCatalog.cs`
- Create: `Assets/StreamingAssets/Songs/catalog.json`
- Modify: `Assets/Scripts/UI/MainMenuUI.cs`
- Test: `Assets/Tests/EditMode/SongCatalogTests.cs`

**说明：** 先给现有 `demo.mid` 建目录；阶段 3 再扩充 6–8 首公版曲。

- [ ] **Step 1: 写失败测试**

创建 `Assets/Tests/EditMode/SongCatalogTests.cs`：

```csharp
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests
{
    public class SongCatalogTests
    {
        [Test]
        public void Load_ReadsSongsWrapper_AndFallsBackForUnknownFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pt3d_catalog_test");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "catalog.json"),
                    "{\"songs\":[{\"file\":\"demo.mid\",\"title\":\"示例曲\",\"composer\":\"原创\"," +
                    "\"difficulty\":\"入门\",\"durationSeconds\":7.5,\"noteCount\":39,\"description\":\"演示\"}]}");

                var catalog = SongCatalog.Load(dir);
                Assert.IsNotNull(catalog);
                Assert.IsTrue(catalog.TryGet(Path.Combine(dir, "demo.mid"), out var entry));
                Assert.AreEqual("示例曲", entry.Title);
                Assert.AreEqual("入门", entry.Difficulty);

                Assert.IsFalse(catalog.TryGet(Path.Combine(dir, "missing.mid"), out _));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
```

- [ ] **Step 2: 实现 `SongCatalog`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PianoTeacher3D.Core
{
    [Serializable]
    public sealed class SongCatalogEntry
    {
        public string file;
        public string title;
        public string composer;
        public string difficulty;
        public float durationSeconds;
        public int noteCount;
        public string description;

        public string Title => string.IsNullOrEmpty(title) ? file : title;
        public string Composer => string.IsNullOrEmpty(composer) ? "未知作曲" : composer;
        public string Difficulty => string.IsNullOrEmpty(difficulty) ? "未分级" : difficulty;
        public string Description => description ?? string.Empty;
    }

    [Serializable]
    internal sealed class SongCatalogFile
    {
        public List<SongCatalogEntry> songs = new List<SongCatalogEntry>();
    }

    /// <summary>内置曲目目录：只读 catalog.json；缺失/损坏时返回空目录，不抛异常。</summary>
    public sealed class SongCatalog
    {
        private readonly List<SongCatalogEntry> _entries = new List<SongCatalogEntry>();
        private readonly Dictionary<string, SongCatalogEntry> _byFile =
            new Dictionary<string, SongCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<SongCatalogEntry> Entries => _entries;

        public static SongCatalog Load(string songsDirectory)
        {
            var catalog = new SongCatalog();
            try
            {
                string path = Path.Combine(songsDirectory ?? string.Empty, "catalog.json");
                if (!File.Exists(path)) return catalog;
                var file = JsonUtility.FromJson<SongCatalogFile>(File.ReadAllText(path));
                if (file?.songs == null) return catalog;
                for (int i = 0; i < file.songs.Count; i++)
                {
                    var e = file.songs[i];
                    if (e == null || string.IsNullOrEmpty(e.file)) continue;
                    catalog._entries.Add(e);
                    catalog._byFile[e.file] = e;
                }
            }
            catch (Exception)
            {
                // 目录损坏不影响曲库：回落文件名展示
            }
            return catalog;
        }

        public bool TryGet(string path, out SongCatalogEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(path)) return false;
            string name;
            try { name = Path.GetFileName(path); }
            catch (Exception) { return false; }
            return _byFile.TryGetValue(name ?? string.Empty, out entry);
        }
    }
}
```

- [ ] **Step 3: 建 `catalog.json`**

```json
{
  "songs": [
    { "file": "demo.mid", "title": "示例曲", "composer": "原创", "difficulty": "入门", "durationSeconds": 7.5, "noteCount": 39, "description": "内置演示曲，用于熟悉界面与视角" }
  ]
}
```

- [ ] **Step 4: 改主菜单**

在 `MainMenuUI.BuildLibrarySection()` 中：

```csharp
            string dir = SongsDirectory;
            var catalog = SongCatalog.Load(dir);
            ...
            string songName = Path.GetFileNameWithoutExtension(songPath);
            string title = songName, meta = DescribeSongMeta(songPath);
            if (catalog.TryGet(songPath, out var entry))
            {
                title = entry.Title;
                string duration = UITheme.FormatTime(Mathf.Max(0f, entry.durationSeconds));
                meta = $"{entry.Composer}　·　{entry.Difficulty}　·　{duration}　·　{entry.noteCount} 音符";
            }
            var card = UIKit.Card(_content, title, meta, new Vector2(cardW, cardH),
                () => LoadLesson(songPath), $"开始学习《{title}》");
```

标题区维持现有布局；不硬编码颜色/字号。

- [ ] **Step 5: 测试与视觉**

```powershell
python Tools/verify.py --edit-only
python Tools/verify.py --visual --timeout 900
```

Expected: `SongCatalogTests` 通过；V10 主菜单卡片显示中文标题/作曲/难度/时长。

- [ ] **Step 6: 提交**

```powershell
git add Assets/Scripts/Core/SongCatalog.cs Assets/StreamingAssets/Songs/catalog.json Assets/Scripts/UI/MainMenuUI.cs Assets/Tests/EditMode/SongCatalogTests.cs Docs/screenshots
git commit -m "feat(ui): 曲目目录与主菜单信息完整化"
```

---

## Task 6B: 主菜单“继续练习”入口

**Files:**
- Modify: `Assets/Scripts/UI/MainMenuUI.cs`

- [ ] **Step 1: 增加继续练习按钮**

在 `MainMenuUI.BuildUI()` 的副标题之后调用 `BuildContinueButton();`，并新增：

```csharp
        private void BuildContinueButton()
        {
            var btn = UIKit.PrimaryButton(_canvas.transform, "继续练习", new Vector2(148f, 40f),
                ContinueLast, "打开最近一次练习的曲目；没有记录时打开曲库第一首");
            UITheme.PlaceTopRight(btn.GetComponent<RectTransform>(), UITheme.Layout.ScreenMargin,
                UITheme.Layout.ScreenMargin, 148f, 40f);
        }

        private void ContinueLast()
        {
            if (RecentPaths.Count > 0 && SafeFileExists(RecentPaths[0]))
            {
                LoadLesson(RecentPaths[0]);
                return;
            }
            var songs = ListSongs(SongsDirectory);
            if (songs.Count > 0) LoadLesson(songs[0]);
        }
```

- [ ] **Step 2: 验证**

```powershell
python Tools/verify.py --timeout 900
python Tools/verify.py --visual --timeout 900
```

Expected: `MainMenuLibraryTests` 仍通过；V10 右上角出现“继续练习”。

- [ ] **Step 3: 提交**

```powershell
git add Assets/Scripts/UI/MainMenuUI.cs Docs/screenshots
git commit -m "feat(ui): 主菜单继续练习入口"
```

---

## Task 7: 主镜头切换为真人角色并重采基线

**Files:**
- Modify: `Assets/Tests/PlayMode/VisualSnapshotTests.cs`
- Update: `Docs/screenshots/*.png`、`Docs/screenshots/baseline/*.png`、`Docs/screenshots/manifest.md`
- Update: `Docs/visual-regression.md`、`Docs/reports/upgrade-2026-11-01.md`

- [ ] **Step 1: 主镜头使用 Rocketbox**

在 `VisualSnapshotTests` 加载场景后、拍 `01_full` 之前加入：

```csharp
        // v4：主镜头使用生产默认角色（Rocketbox），内置角色仍由 V03/V04 覆盖
        PianoTeacher3D.Characters.CharacterProfile defaultProfile = null;
        bool hasDefaultProfile = session.CharacterRegistry != null &&
            session.CharacterRegistry.TryGet(
                PianoTeacher3D.Characters.CharacterRegistry.RocketboxFemaleId, out defaultProfile);
        if (hasDefaultProfile) session.ActivateCharacter(defaultProfile);
        yield return new WaitForSeconds(1.0f);
```

- [ ] **Step 2: 重采全部截图**

```powershell
python Tools/verify.py --visual --timeout 900
```

Expected: 25 张全部生成；01–06 与 V17–V19 为真人角色；V01–V14 仍是内置角色
（测试在中段显式 `SwitchAvatar(0)`），作为内置风格覆盖保留。

- [ ] **Step 3: 人工看图**

逐张检查：真人手部是否穿模、坐姿是否悬空、琴房是否过暗、UI 是否被遮挡、教学色是否可辨。

- [ ] **Step 4: 更新基线**

```powershell
Copy-Item "Docs\screenshots\*.png" "Docs\screenshots\baseline\" -Force
```

仅复制人工确认过的图；如某张不合格，先修再复制，不要把坏图写进基线。

- [ ] **Step 5: 更新文档与提交**

更新 `Docs/visual-regression.md` 的基线数量与“主镜头角色 = Rocketbox”说明；
更新 `Docs/reports/upgrade-2026-11-01.md` 的阶段 1 记录。

```powershell
git add Docs/screenshots Docs/visual-regression.md Docs/reports/upgrade-2026-11-01.md Assets/Tests/PlayMode/VisualSnapshotTests.cs
git commit -m "test(visual): 主镜头切换真人角色并重采 v4 基线"
```

---

## 验收清单（阶段 0–1）

- [ ] 全新启动：真人角色 + 琴房 + 乐谱 + 完整主菜单，无黑色虚空。
- [ ] 特写：内置角色隐藏身体只留手臂与手；真人角色无大面积头肩遮挡（或已记录残余）。
- [ ] 主菜单卡片显示中文标题、作曲、难度、时长；有“继续练习”入口。
- [ ] 编译 0 错误 0 警告；EditMode 473+ 全绿；PlayMode 124+ 通过 + 5 跳过。
- [ ] `python Tools/static_checks.py` 与 `check_architecture.py` 全绿。
- [ ] `--visual` 生成 25 张图并人工确认；`baseline/` 更新。
- [ ] `Docs/reports/upgrade-2026-11-01.md` 如实记录未达标项。
