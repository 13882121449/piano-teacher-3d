# 角色资产要求与接入说明

本文档面向「往角色库里加一个新角色」的人：描述资料卡（`CharacterProfile`）的必填项、
预制体的技术要求、外观槽位的能力矩阵，以及当前导入角色（Rocketbox）已知的待标定项。

> 相关代码：`Assets/Scripts/Characters/`（角色库 / 校验 / 切换事务 / 视图实现）、
> `Assets/Scripts/Avatar/AvatarManager.cs`（骨骼属性转述）。

---

## 1. 角色库目录结构

```
Assets/Characters/
  Resources/Characters/                 ← 角色资料资产（CharacterProfile）放这里
    *.asset                             由 Resources.LoadAll<CharacterProfile>("Characters") 发现
  Rocketbox/                            ← 导入的模型资产（不参与资料发现，只通过路径引用）
    Resources/Characters/Rocketbox/
      Female_Adult_01.fbx                Resources 路径：Characters/Rocketbox/Female_Adult_01
```

约定：

- **资料资产的路径固定为 `Resources/Characters/*.asset`**，不要嵌套子目录（`LoadAll` 只扫一层）。
- 预制体可以直接被资料引用（`Prefab` 字段），也可以只写 `ResourcesPath`（不含扩展名）。
  被直接引用的预制体即使不在 Resources 下也会随构建打包。
- 内置 3 个程序化角色与 Rocketbox 角色由代码构造（`CharacterRegistry.CreateBuiltIn/CreateRocketbox`），
  不依赖场景对象，EditMode 测试可直接 `new CharacterRegistry(false)`。

## 2. CharacterProfile 必填项

| 字段 | 规则 | 校验失败提示 |
| --- | --- | --- |
| `CharacterId` | 小写字母/数字开头，仅 `a-z 0-9 . _ -`，长度 1–64 | 「角色 ID 必须…」/「含非法字符」 |
| `DisplayName` | 非空白 | 「缺少显示名称」 |
| `Source` | `ProceduralBuiltIn` / `Prefab` | — |
| `Prefab` 或 `ResourcesPath` | `Source=Prefab` 时**至少有一个** | 「既未指定预制体，也没有 Resources 路径」 |
| `ProceduralIndex` | `Source=ProceduralBuiltIn` 时必须在 0–2 | 「内置索引 … 超出范围」 |
| `LicenseName` | 非空（MIT / CC0 / 商业授权编号） | 「缺少许可信息」 |
| `QualityTier` | 0–2 | 「质量级别必须在 0–2 之间」 |
| `RootScale` | > 0 | 「缩放必须为正数」 |
| `SupportedAppearanceSlots` | 不得重复声明同一槽位 | 「外观槽位 … 重复声明」 |

> **注意**：`CharacterProfile.Validate()` 目前把「`Prefab` 为空」一律判失败，
> 但字段注释允许用 `ResourcesPath` 承载。`CharacterAssetValidator.ValidateProfile()`
> 对这一条做单条放宽并补跑后续规则（见方法内 `TODO(主控)`）。若 `CharacterProfile.Validate`
> 改为「`Prefab` 或 `ResourcesPath` 至少一个」，该补丁分支可直接删除。

## 3. 预制体技术要求

1. **Rig 类型必须是 Humanoid**（`ModelImporter.animationType = 2`），
   根节点需带 `Animator` 且 `avatar != null && avatar.isHuman`。
2. **朝向**：角色面向 **−Z**（朝向钢琴）。模型若正面朝 +Z，请在资料里把 `RootYawDegrees` 设为 `180`。
3. **缩放**：模型自身缩放为 1，体型差异用资料的 `RootScale` 调整（不要改 FBX 的 import scale）。
4. **必须的骨骼**（Rocketbox / 3ds Max Biped 命名，每手 19 根）：

   | 部位 | 左手 | 右手 |
   | --- | --- | --- |
   | 锁骨 | `Bip01 L Clavicle` | `Bip01 R Clavicle` |
   | 上臂 | `Bip01 L UpperArm` | `Bip01 R UpperArm` |
   | 前臂 | `Bip01 L Forearm` | `Bip01 R Forearm` |
   | 手 | `Bip01 L Hand` | `Bip01 R Hand` |
   | 五指 × 3 节 | `Bip01 L Finger{0..4}` / `…1` / `…2` | `Bip01 R Finger{0..4}` / `…1` / `…2` |

   缺任何一根都会在校验阶段被拦下，并直接给出缺失的骨骼名（例：`缺少骨骼 Bip01 L Finger31`）。
5. **下肢**：`Bip01 L/R Thigh / Calf / Foot`（坐姿适配与相机取景会用到）。
6. **材质**：每角色建议 ≤ 3 个材质槽；超过 10 个会在切换时产生可感知的加载抖动。
7. **许可**：`LicenseName` 必填，`LicenseUrl` / `SourceUrl` / `AssetVersion` 建议填写，
   角色库面板会显示「许可 · 来源 · 版本」，用于资产可追溯（方案 18）。

## 4. 外观槽位能力矩阵

| 槽位 | 程序化内置 3 角色 | Rocketbox 导入角色 |
| --- | --- | --- |
| `Thumbnail`（卡片缩略图） | ✅ | ✅ |
| `BodyTint`（整体色调） | ✅ | ✅（材质实例化后染色） |
| `FaceTexture`（脸部贴图） | ❌ 无面部 UV（五官是图元拼接） | ❌ 尚未适配 UV |
| `HeadMaterial`（头部材质槽） | ❌ 没有独立头部材质 | ❌ 没有独立头部材质 |

`ICharacterView.Supports(slot)` 是外观应用器的**唯一准入判据**：

- `Thumbnail` 是「卡片图片」，与模型的 UV/材质能力无关，因此**对所有角色恒为 true**（方案 9.5 最安全槽位），
  即使某个 `CharacterProfile` 忘记声明该槽位也不影响应用；
- `BodyTint` 要求资料声明过该槽位；`FaceTexture` / `HeadMaterial` 恒为 false。
- 「做不到就如实返回 false」——UI 上的 `照片✓/✗` 标记读的是 `CharacterProfile.SupportsSlot`（声明），
  而应用是否真的执行读的是 `ICharacterView.Supports`（能力），两者不应互相冒充。

### 外观应用的三条硬约束（与 `GeneratedAppearanceApplier` 的契约）

1. **失败 = 外观原样**：返回 `false` 时视图不得改动贴图引用与材质颜色；错误文案写「原因 + 下一步」。
2. **纹理所有权**：`ApplyAppearance` 成功的纹理归调用方所有，视图只持有引用、**不 Destroy**；
   视图自己的默认资源只能通过 `ClearAppearance` 交还。
3. **`ClearAppearance` 必须同步生效**：调用后立刻把引用换掉（不是延后到帧末），
   因为调用方紧接着就会销毁自有旧纹理，否则会出现「销毁了视图仍在用的贴图」。


## 5. 切换事务（8 步）与失败语义

```
① 检查存在/兼容 → ② 加载 → ③ 校验 → ④ 锁状态（停用旧角色）
→ ⑤ 激活接管 → ⑥ 切换引用 → ⑦ 确认可驱动 → ⑧ 卸载旧角色
```

- 任一步失败：释放新角色临时资源（`DisposeCharacterResources` **恰好一次**）、
  恢复旧角色激活状态、状态置 `RolledBack`，旧角色保持可驱动。
- 切换进行中收到新请求：旧请求置 `Cancelled` 并当场释放其临时视图，
  然后开始新请求；**同一 ID 的重复请求直接返回已提交结果，不创建第二个实例**。
- 切换服务不引用 `LessonSessionController`、时间轴、音频与播放位置，
  因此切换期间曲目进度、速度、循环不可能被重置。

## 6. Rocketbox 适配现状

| 项 | 状态 |
| --- | --- |
| 预制体加载（Resources） | ✅ 已验证路径 `Characters/Rocketbox/Female_Adult_01` |
| Humanoid Animator / Avatar | ✅ 结构可用（PlayMode 测试断言） |
| 手部 5 指 × 3 节绑定 | ✅ 结构可用（PlayMode 测试断言） |
| 手部规范基推导 | ✅ 由绑定姿态推导（见 `RocketboxHandRig` 类注释） |
| **坐姿角度** | ⚠️ **未标定**（`SittingPoseSettings.Calibrated = false`） |
| **手指驱动视觉正确性** | ⚠️ **未验证**：Biped 骨骼局部轴不满足 `FingerPoseController` 的「+Z 弯曲轴」假设 |
| **手臂 IK 视觉正确性** | ⚠️ **未验证**：`StretchArm` 直接改骨骼 Transform，蒙皮模型上会拉伸网格 |
| **演奏流程接通** | ⚠️ 会话控制器尚无「按资料切换角色」的入口，切到外部角色后 `FingerPoseController` 未重新初始化 |

### 手部规范基推导（为什么不能硬编码轴）

内置程序化手部约定：`+Z` = 手指方向、`+Y` = 手背、`+X·Side` = 拇指侧。
Biped 骨骼不满足该约定，且同一根手指的近节/末节局部轴可能不同。因此 `RocketboxHandRig`
在**创建时**从绑定姿态推导（全部在腕部局部空间计算）：

1. `Forward` = 中指末节位置 − 中指近节位置（归一化）；
2. `Thumb` = 腕 → 拇指根，去掉 `Forward` 分量后归一化；
3. `CanonicalX` = `Thumb × SideSign`（左手 +1 / 右手 −1，因为内置约定里两只手的 X 轴都指向身体中线外侧）；
4. `BackNormal` = `Cross(Forward, CanonicalX)`，再反推 `CanonicalX = Cross(BackNormal, Forward)` 做正交化；
5. `CanonicalToWrist = Quaternion.LookRotation(Forward, BackNormal)`。

退化（三点共线、骨长为零）时退回腕部自身轴，保证不产生 NaN。

## 7. 坐姿标定步骤（待人工执行）

1. 在场景里实例化 `Female_Adult_01`，`RootScale = 1`、朝向按需 180°。
2. 选中模型根，观察 T-Pose 与琴凳的相对位置。
3. 在 `SittingPoseApplier.Settings` 里逐项微调（顺序：髋 → 膝 → 踝 → 肩 → 肘）：
   - `ThighEuler` / `CalfEuler`：让大腿水平、小腿垂直、双脚落地；
   - `UpperArmEuler` / `ForeArmEuler`：让手腕自然落在键面上方；
   - `SeatHeightOffset`：让臀部贴合坐面，不悬空也不陷入；
   - 若某个关节往反方向弯，把对应分量的正负号取反即可（这是轴向未知的典型表现）。
4. 视觉确认后把 `Calibrated` 置为 `true`，并把最终数值写回 `CharacterRegistry.CreateRocketbox()`
   的默认值（或做成 `CharacterProfile` 上的预设字段）。
5. 重新跑 `CharacterRocketboxTests`，确认结构断言仍然通过。

## 8. 验证清单

EditMode（纯内存，不加载场景）：

- `PianoTeacher3D.Tests.CharacterProfileTests`
- `PianoTeacher3D.Tests.CharacterRegistryTests`
- `PianoTeacher3D.Tests.CharacterSwitchServiceTests`

PlayMode（需要导入的 FBX 可用）：

- `PianoTeacher3D.Tests.CharacterRocketboxTests`

人工核对（无法自动化）：

- [ ] 坐姿角度是否自然（第 7 节）
- [ ] 切到外部角色后是否仍能正常弹奏、手部是否穿模
- [ ] 角色库面板 4 张卡片的标签与许可信息是否可读
