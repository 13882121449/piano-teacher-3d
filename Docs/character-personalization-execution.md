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
