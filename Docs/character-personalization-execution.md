# 角色个性化与 UI 重设计 —— 本轮执行记录（2026-10-20）

> 依据：`Docs/hand-contact-and-visual-optimization-plan.md` 之后的《角色升级与照片个性化功能详细实施计划》。
> 本文件只记录**已实测**的结论；未验证项集中列在第 5 节，不得当成已完成。

## 1. 落地范围（对照方案阶段）

| 阶段 | 本轮落地 | 状态 |
|---|---|---|
| Phase 0 基线 | `Docs/character-upgrade-baseline.md`（环境/包/测试/性能/骨骼与左右手约定/固定 MIDI/截图基线） | 完成 |
| Phase 1 手部与左右手 | 全部手别判断收敛到 `PianoNoteFilter.IsLeftHand`（清掉 `HandIKController` 里重复的 `pitch < SplitPitch`） | 完成（既有修复延续） |
| Phase 2 统一角色架构 | `Assets/Scripts/Characters/`：`ICharacterView` / `CharacterProfile` / `CharacterRegistry` / `CharacterAssetValidator` / `CharacterSwitchService`（8 步事务 + 取消/回滚）/ `CharacterResourceScope` / `ProceduralCharacterView` | 完成 |
| Phase 3 新角色 MVP | Rocketbox `Female_Adult_01`（MIT）导入、Humanoid Avatar 修复、`RocketboxCharacterView` + `RocketboxHandRig`（基从绑定姿态推导）+ 坐姿应用器 | 结构可用，**演奏驱动未验证** |
| Phase 4 照片个性化 MVP | `Assets/Scripts/Customization/`：输入校验 / 生成任务状态机 / Mock 提供方 / 外观应用与撤销 / 本地存储 / 隐私同意 + `UI/PhotoCustomizationPanel` | 逻辑层完成，**UI 未挂到场景** |
| UI/UX | 深色工作台重设计：令牌、`UIKit`、顶栏单行、玻璃提示卡、底部播放器面板、曲库卡片页 | 完成（见第 3 节截图核对） |

## 2. 关键实测结论（有据，可用于后续判断）

1. **Rocketbox 导入根因**：`.meta` 里写着 `animationType: 2`，但 `ModelImporter.animationType` 实际是 `Generic`
   → 没有 Avatar、实例化后也没有 Animator（PlayMode 两条用例曾因此失败）。
   手改 .meta 无效，必须走导入器接口：`Assets/Editor/RocketboxAvatarSetup.cs`。
2. **Biped 手指映射会破坏 Humanoid**：把 30 根手指显式写进 `humanDescription` 后重导入，
   Unity 生成 `isValid=True / isHuman=False` 的降级 Avatar。已回退；手指由 `RocketboxHandRig`
   按骨骼名直接驱动，不依赖 Humanoid 映射（`EnableFingerMapping` 常量保留复现开关）。
3. **撤销语义**：首份应用也压一个 `null` 撤销点，撤销链终点是「角色默认外观」；
   否则撤销一次后 `CanUndo` 仍指向「恢复默认」，与独立按钮重复。
4. **环境坑**：宿主注入的 `ACC_PRODUCT_CONFIG_V3`（约 523 KB）会撑爆 Bee/tundra 的环境块，
   报 `tundra: error: env block error; too big?`。跑 Unity 前需 `env -u ACC_PRODUCT_CONFIG_V3`。

## 3. 验证结果（Editor batchmode，本机）

| 层 | 结果 |
|---|---|
| 编译 | 0 错误 |
| EditMode | **460 / 460 通过**（基线 319 + 新增 141） |
| PlayMode | **104 通过 / 5 跳过 / 0 失败**（共 109） |
| 视觉 | 20 张截图重采并人工核对；顶栏单行、提示卡带底、底栏合并为单块播放器面板、曲库卡片页均符合预期 |
| 播放性能 | 5 s 播放 301 帧、平均 60.0 FPS、最差 16.9 ms、托管内存增量 300 KB |

## 4. 仍待确认（需产品/用户拍板）

1. **A→B→A 后 A 的照片外观是否要保留**：当前实现是切换即回收（避免显存无界增长）。
   更正确的解法是「按 CharacterId 把已应用外观落盘，激活时重新应用」，属于 P2。
2. **图标按钮的语义**（返回/帮助/重播）目前靠 tooltip 承担，没有常驻文字，
   与「按钮必须有文字」有一条明确妥协，需验收确认可接受。
3. **照片面板入口位置**：顶栏角色 chip 与练习栏入口在重设计后合并，
   照片入口尚未挂载，需要确认放「顶栏角色 chip 内」还是「角色库面板内」。

## 5. 未完成（诚实清单）

1. Rocketbox 角色的**演奏驱动未验证**：模型能加载、校验通过、骨骼读数可用，
   但 `FingerPoseController` 假设的「+Z 弯曲轴」对 Biped 不成立，需要一层轴修正 wrapper 才能跟谱。
2. **坐姿角度未标定**（髋 80°/膝 −85°/肩 55°/肘 −70° 为解剖学估算，`Calibrated=false`）。
3. 照片面板未挂场景（`PhotoCustomizationPanel.Initialize(service, consent, slot)` 待接线）。
4. 无「启动时恢复上次已应用外观」；无 A→B→A 的外观持久化。
5. 角色库仅 4 张卡（3 内置 + 1 Rocketbox），方案 P1-1 要求的「动漫风格角色」尚未引入
   —— 需要一份许可清晰、含完整手指骨骼的动漫模型。
6. 真实图像生成服务未接（方案 9.4 要求密钥只在服务端，需后端代理）。

---

## 6. 第二轮（2026-10-20 20:04–20:52）：清剩余清单

| 优先级 | 任务 | 结果 |
|---|---|---|
| P0 | Rocketbox 手指能正确屈曲 | **完成并验证** |
| P1 | 照片面板挂到角色库入口 | **完成并验证** |
| P2 | A→B→A / 重启后恢复已应用外观 | **完成并验证** |
| P4 | UI 设计文档令牌同步 | **完成** |
| P3 | Rocketbox 坐姿角度标定 | **未做**（需逐帧看图迭代，见 §7） |

### 6.1 P0：手指关节轴策略（`IFingerJointAxes`）

问题：`FingerPoseController` 的 CCD 与写回都用 `Quaternion.Euler(deg,0,0)`，隐含
「骨骼局部 +X 是弯曲轴、+Z 是骨骼方向」。Biped 骨架套用后手指是扭转/横移，不是屈曲。

为什么不能插节点解决：`SkinnedMeshRenderer.bones` 是硬引用，在骨骼链中间插「规范基
wrapper 节点」等于换骨骼层级，必然破面。所以只能把轴映射做成策略对象。

- `IFingerJointAxes` + `DefaultFingerJointAxes`（默认实现与改造前逐位一致）
- 三个写回点（`ApplyJoint`、CCD 内写回、CCD 轴、指根瞄准）改走策略
- `ImportedFingerJointAxes`：`axis = −cross(骨方向, 屈曲方向)`，轴存父关节局部空间

两个坑都是**实测**出来的，不是推理出来的：

1. Unity 的 `Vector3.Cross` 与 `Quaternion.AngleAxis` 都按左手规则，叉乘结果必须取反；
   否则正向弯曲会把手指抬起来（合成骨架用例直接抓到：指尖朝 +Y 而非掌心）。
2. `RocketboxHandRig.BackNormal` 是**腕骨局部空间**向量（`DeriveCanonicalBasis` 用
   `Wrist.InverseTransformPoint` 推出）。当成世界向量用会让弯曲平面偏转约 90°，
   实测左手第 2 指只横移 1.04 mm。修正后 10 根手指全部正常。

**验收数据**（真实 Biped 骨骼，MCP 弯曲 30°）：

| 手 | 拇指 | 食指 | 中指 | 无名 | 小指 |
|---|---|---|---|---|---|
| 左 | 51.9 | 42.5 | 51.4 | 42.4 | 32.0 mm |
| 右 | 51.9 | 42.5 | 51.4 | 42.4 | 32.0 mm |

且 `dot(位移, 掌心方向) = −位移`（100% 沿掌心移动）。

### 6.2 P1：照片入口

角色库面板底部新增「用照片定制外观…」（456×32），按当前角色**运行时能力**选槽位
（缩略图 > 整体色调 > 贴图类）；管线未装配时按钮禁用。面板首次点击才创建、之后复用。

顺带修掉一个真缺陷：`AvatarManager.CurrentView` 之前要等首次 `SwitchAvatar` 才惰性创建，
导致启动阶段 `CurrentCharacterView` 为 null（外观绑定与角色库高亮都拿不到）。
改为 `BuildAvatars()` 结束时建立包装。

### 6.3 P2：外观按角色恢复

`OnCharacterViewChanged` 里 `BindCharacter` 之后调 `TryRestoreStoredAppearance`：
只读本地 `result.png` + `appearance.json`，不重新生成、不联网；清单只在**用户确认应用**
时落盘（`PersistApplied`），所以恢复的内容一定是用户确认过的。没保存过就静默跳过。

### 6.4 P4：文档令牌

`Docs/ui-design-system.md` 的字号/圆角/控件高度/布局令牌全部按 `UITheme` 实际值重写
（28/20/15/13/12/10 六档字号、圆角 8/10/12/3、顶栏 56、播放器面板 132、进度条 6、提示卡 220×88、曲库卡 240×96）。

### 6.5 本轮验证

| 层 | 结果 |
|---|---|
| 编译 | 0 错误 |
| EditMode | **466 / 466**（新增 6 例轴策略用例） |
| PlayMode | **109 通过 / 5 跳过 / 0 失败**（共 114，新增 2 例真实骨骼轴验证 + 3 例照片入口） |

提交：`bdf1bde`（P0）、`49f2450`（P1）、第三段（P2 + 文档）。

## 7. 仍未做（下一步）

1. **P3 坐姿标定**：`SittingPoseSettings` 仍是解剖学估算（髋 80°/膝 −85°/肩 55°/肘 −70°，
   `Calibrated=false`）。需要「激活 Rocketbox → 截图 → 调角 → 再截图」的循环，
   每轮约 4 分钟，且要看图判断，不适合盲改。
2. **动漫/额外写实角色**：用户要求「写实一点、不要代码生成人物、可接入其他模型」。
   Rocketbox 全库 115 个角色都是 MIT，同一 Biped 骨架，接入成本最低（当前只导入了
   `Female_Adult_01`）；是否需要再导入若干（不同性别/年龄）待确认。
3. **真实图像生成服务**：需要后端代理与预算，客户端刻意只保留 3 个成员且无密钥。

---

## 8. 缺陷排查：白模 / 手不跟键 / UI 错位 / 特写遮挡（2026-10-20 21:10–21:33）

用户提供截图后按四条逐一取证（全部为实测，非推断）：

| 现象 | 实测证据 | 结论 |
|---|---|---|
| 人物白模 + 白色碎片 | 运行时 dump：`mat[0] f001_body shader=Standard tex=none`、`mat[2] f001_opacity tex=none`；只有 `f001_head` 有 2048² 贴图。仓库内该角色只有 `f001_head_color.tga` / `f001_head_normal.tga` 两张 | **资产不全**：body/opacity 贴图从未入仓。身体无 `_MainTex` → 白模；头发/睫毛薄片用透明材质却无 alpha 贴图 → 渲染成白色尖片（截图里手边的碎片） |
| 手不跟键 | dump：`Hips y=0.923`、`Head y=1.522`（站姿），左手 `(0.082,1.055,0.001)` 与右手 `(-0.367,1.264,0.277)` 完全不对称；角色朝向 / 站位未对齐琴凳 | Rocketbox 是**站姿 T-Pose**，坐姿适配未标定；手自然够不到键。内置程序化角色不受影响（基准：命中率 95%，指尖均值 26.7 mm） |
| UI 错位 | 新增不变量用例：全景 / 特写 × 全部 Canvas 的每个 Button 外接矩形必须完整落在画布内 → **通过**（960×720 参考宽，最窄分支） | 现有布局在测试窗口内无越界；`02_closeup` 里"底栏只剩 chips"是底栏收缩后仍按 132 布局的旧版截图，已随 UI 重设计修正 |
| 特写头部挡键盘 | 新增几何用例：相机→键盘 5 个采样点的视线到头部球心距离必须 > 11 cm → 全景与特写**均通过** | 内置角色不遮挡；截图中的遮挡来自站姿 Rocketbox（头 y=1.52 正好在相机与琴键之间） |

### 8.1 处理方式

1. `CharacterAssetValidator.ValidateMaterials`：网格任一材质缺主贴图 → **校验失败**，
   原因写明"模型会渲染成白模，请补齐贴图或换资产完整的角色"。
   宁可不让用户切过去，也不让他看到破面白模。
2. `AvatarSelectionUI.Select`：切换失败时把原因显示在角色面板（超长截断 + 完整原因进日志），
   并保持高亮在实际生效的角色上——避免"点了没反应"。
3. 角色面板高度 300→324，许可/失败文本改为两行换行，避免长文案溢出造成新的错位。
4. 新增 `CloseupOcclusionTests`（3 例）：特写遮挡、全景遮挡、控件不越界，
   把"看起来错位/挡住"变成可复现的几何断言。

### 8.2 启用 Rocketbox 角色还差什么（硬约束）

- 该角色 `Textures/` 下与 `f001_body`、`f001_opacity` 材质对应的**颜色/法线/透明贴图**；
  当前仓库只有 head 两张（各约 10 MB），GitHub 在本机不可达（代理 502、直连超时），
  无法自行补齐。
- 补齐后仍需做坐姿标定（髋/膝/踝/肩/肘角度 + 站位偏移），当前 `Calibrated=false`。
