# 照片个性化：隐私与数据保留说明

对应优化方案 **9.3（照片输入校验）**、**9.5（槽位兼容）**、**9.6（本地存储与删除）**、**9.7（隐私与日志纪律）**。
面向两类读者：使用者（知道自己的照片被怎么处理）与维护者（知道代码边界在哪）。

---

## 1. 一句话结论

照片**只在本机内存与本机磁盘**上处理：不上传、不联网、不写日志、不写入 `Assets/`；
用户随时可以一键删除；未勾选隐私同意时，功能完全不启动。

---

## 2. 数据流

```
用户选择文件（PNG / JPG）
      │  仅读取文件字节（后台线程）
      ▼
PhotoInputValidator：扩展名白名单 → 文件头魔数 → 头部预读宽高 → 真实解码
      │  通过
      ▼
AppearanceStorage：原图原样落盘（原子写）
      │
      ▼
IImageGenerationProvider.StartJob（本地实现 MockImageGenerationProvider：纯 CPU 变换）
      │  ImageGenerationJob：Queued → Running → Succeeded / Failed / Canceled / TimedOut
      ▼
GenerationResultValidator：类型 / 尺寸 / 解码结果 / 内存大小二次校验
      │
      ▼
预览（此阶段**不触碰角色**）
      │  用户点「应用到角色」
      ▼
GeneratedAppearanceApplier：备份上一份 → ApplyAppearance → 失败回滚 → 记录可撤销点
      │
      ▼
AppearanceStorage：结果 + 缩略图 + appearance.json 落盘（原子写）
```

任何一步失败：**当前外观保持不变，演奏层（MIDI / 音频 / 手部 IK）不受影响**。

---

## 3. 允许与拒绝的输入

| 项目 | 值 | 说明 |
| --- | --- | --- |
| 扩展名白名单 | `.png` / `.jpg` / `.jpeg` | 只作为初筛，不作为安全依据 |
| 真实格式 | 文件头魔数（PNG `89 50 4E 47…`、JPEG `FF D8 FF`） | 改扩展名骗不过校验 |
| 单文件大小 | ≤ 8 MB | 防止一次读入超大文件造成内存峰值 |
| 最短边 | ≥ 64 px | 更小的图放大后不可用 |
| 最长边 | ≤ 4096 px | 贴图需求上限 |
| 总像素 | ≤ 16,000,000 | RGBA32 解码约 64 MB，属"防内存耗尽"上限 |

尺寸与像素上限先从**文件头预读宽高**拦截，再走真实解码，避免先分配大纹理再拒绝。

拒绝时返回两段文案：**原因**（发生了什么、为什么）+ **下一步**（换什么图、怎么改）。
不允许只显示"失败"。

---

## 4. 落盘位置与文件

根目录：`Application.persistentDataPath/customization/`
（不写 `Assets/`、不写 `StreamingAssets`，卸载应用即随应用数据一起清理）

```
customization/
├── consent.json              隐私同意记录（版本号 + UTC 时间戳，不含图片信息）
└── {characterId}/            每个角色一个目录（characterId 受 CharacterIdRules 约束）
    ├── original.photo        原图（原始字节，不做转码；格式由魔数判定）
    ├── result.png            生成结果
    ├── thumbnail.png         缩略图（长边 256）
    └── appearance.json       元数据（槽位 / 风格 / 尺寸 / 提供方 ID / 保存时间）
```

设计要点：

- **文件名固定**，不使用用户原始文件名（避免文件名本身泄漏信息，也避免同名覆盖）。
- 全部写入走 `AtomicFileWrite`（同目录临时文件 → `File.Replace` + `.bak`）。
- 字节写入先落 `*.stage` 再原子替换；启动时 `CleanupIncomplete()` 清理残留的 `.tmp` / `.stage`。
- `appearance.json` **不记录绝对路径**，只记录角色 ID 与槽位名称。

---

## 5. 隐私同意（五要素）

`PrivacyConsentController` 内的常量即产品文案，界面直接展示，不允许各界面自写一套：

1. **用途**：把照片变换成角色外观（缩略图 / 脸部贴图等），仅用于本机显示与练习界面。
2. **处理方**：全部处理在本机完成；不上传服务器、不发起网络请求。
3. **是否保存**：仅保存在本机应用数据目录，不写入项目资源目录。
4. **保存时长**：直到用户点「删除照片与结果」。
5. **删除方式**：面板「删除照片与结果」同时删除原图、结果、缩略图。

规则：

- 未同意时 `CharacterCustomizationService.Submit` 直接拒绝，并给出"去勾选同意"的下一步。
- 同意状态带 UTC 时间戳写入 `consent.json`；**文案版本升级后旧记录自动失效**，需要重新同意。
- 同意记录损坏 → 视为未同意（宁可多问一次，也不在用户不知情时处理照片）。
- 撤销同意只清除同意标记；已保存的照片需用户主动点「删除照片与结果」（界面上会明确提示）。

---

## 6. 删除与保留

| 操作 | 覆盖范围 |
| --- | --- |
| 面板「删除照片与结果」 | 该角色目录下的全部文件（`original.photo` / `result.png` / `thumbnail.png` / `appearance.json` 及其 `.bak`、`.tmp`、`.stage` 残留）；随后恢复角色默认外观 |
| `AppearanceStorage.DeleteAll()` | 遍历 `customization/*` 逐角色删除（**不删** `consent.json`） |
| 撤销同意 | 只删 `consent.json` |
| 应用新照片 | 覆盖同一角色的固定文件名（旧图不会残留副本） |
| 重启应用 / 换回同一角色 | 可调用 `CharacterCustomizationService.TryRestoreStoredAppearance()` 从 `result.png` + `appearance.json` 读回并应用（不重新生成、不联网）；不调用则显示默认外观，文件仍保留 |
| 启动 | 清理未完成的临时文件，不动有效数据 |

删除是幂等的：文件本就不存在也算成功。

---

## 7. 日志纪律

- **不记录**：图片字节、Base64、图片像素、完整路径中含用户名的部分、生成结果内容。
- **可以记录**：错误分类（`InvalidInput` / `Network` / `Timeout` / `Decode` …）、像素尺寸、字节数、提供方 ID、角色 ID。
- 需要展示路径时（文件选择器内部）只显示文件名或尾部省略形式；日志场景请使用 `PathSanitizer.Sanitize`。
- 本模块**没有任何网络调用**，代码中不存在任何 URL 或 API Key 字段——密钥一旦出现就必须放在服务端（方案 9.4）。

---

## 8. 失败与降级

| 场景 | 行为 |
| --- | --- |
| 生成失败（任意分类） | 保留当前外观；显示原因 + 下一步；「重试」可用 |
| 生成超时 | 停止等待，不自动重试（避免用户等两倍超时）；可手动重试 |
| 用户取消 | 幂等；取消后不产生结果、不回调 UI；当前外观不变 |
| 提供方不可用（离线） | 提交前就拒绝，不进入流程 |
| 角色不支持该槽位 | 应用被拒并说明原因（方案 9.5：不兼容角色不显示「应用」） |
| 应用失败 | 回滚到上一份外观（`GeneratedAppearanceApplier` 备份 → 还原引用） |
| 结果不合格 | 按 `Decode` 失败处理，不重试（同样输入重跑结果一样） |
| 磁盘写入失败 | 生成/应用流程给出可读提示；已应用的外观不回滚（外观已生效，仅持久化失败） |

重试边界：**仅 `Network` / `Server` 自动重试，最多 2 次**；
`InvalidInput` / `ContentRejected` / `Decode` 不重试；`Timeout` 不自动重试。

---

## 9. 资源所有权（维护者必读）

- 校验器产出的 `Texture2D` 所有权交给调用方，用完必须 `Destroy`。
- `GeneratedAppearanceApplier` 只销毁**自己接过所有权**的纹理，且必须"先换引用、再释放旧资源"；
  从不销毁角色视图原本的默认资源（那部分用 `ClearAppearance` 交还给视图）。
- 编排层只在预览**未被视图引用**时才销毁预览纹理；应用成功后所有权转移给应用器。
- **角色切换（视图实例不稳定，角色 ID 稳定）**：
  - 同一实例重复绑定 → 不做任何事；
  - 同一角色的新实例（内置包装会被重建）→ `TryRetarget`：先让旧视图 `ClearAppearance`（同帧解引用），
    再把同一批纹理贴到新视图，所有权与撤销点都保留，不复制也不销毁；
  - 换到别的角色 / 解绑 → `ReleaseForCharacterSwitch`：旧视图先解引用，再回收全部自有纹理
    （**不依赖**角色视图的 `DisposeCharacterResources`：它按契约不会销毁应用器持有的贴图）。

---

## 10. 已知边界

- 当前两个真实角色视图（程序化角色 / Rocketbox）只支持 **缩略图** 与 **整体色调** 两个槽位；
  `FaceTexture` / `HeadMaterial` 恒返回 `false`（没有可替换的面部 UV / 材质槽）。
  面板按 `ICharacterView.Supports()` 禁用「应用」并提示改用缩略图或整体色调，
  不会出现「点了应用什么都没发生」。
- 「整体色调」只取纹理平均色，传一张普通的可读贴图即可（无需专门做 1×1），但纹理必须 `isReadable`。
- 图片解码使用 Unity 的 `Texture2D.LoadImage`，必须在主线程调用；只有"读文件字节 + 魔数嗅探"放在后台线程。
- 缩略图 / 生成结果均为 CPU 采样（区域平均），不依赖 GPU，EditMode 可直接测试。
- 当前仅内置本地模拟提供方；接入真实服务端时，密钥与出网逻辑必须放在服务端，客户端接口保持不变。
