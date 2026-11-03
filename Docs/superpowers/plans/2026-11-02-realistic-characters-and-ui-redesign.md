# 写实角色替换与界面重做 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把三个内置程序化角色换成 Rocketbox 真人模型，并按「暖木古典」方向重做界面。

**Architecture:** 角色走「脚本下载资产 → 放进 Resources → 注册表指向预制体」的静态管线，运行时复用现有 `RocketboxCharacterView` / `RocketboxHandRig` / `RocketboxTextureLibrary`；界面把 `UITheme` 拆成「令牌层 + `UIKit` 组件层 + 三个界面脚本」，用 8pt 栅格与 4 级字号统一。

**Tech Stack:** Unity 2022.3.22f1c1 / URP 14 / uGUI / Python 3（Pillow）/ GitHub blobs API

---

## 文件结构

| 文件 | 职责 |
|---|---|
| `Tools/fetch_rocketbox_avatar.py` | 按模型名下载 FBX + 贴图，TGA→PNG 降采样，git blob SHA-1 校验 |
| `Assets/Characters/Rocketbox/Resources/Characters/Rocketbox/*` | 三个角色的 FBX 与贴图（平铺，与现有 Female_Adult_01 同构）|
| `Assets/Scripts/Characters/CharacterRegistry.cs` | 三个内置槽位改指预制体，显示名改「写实女性 / 写实男性 / 儿童」|
| `Assets/Scripts/Characters/RocketboxCharacterView.cs` | 每角色一套标定参数（已有 per-definition 字段，只补条目）|
| `Assets/Scripts/UI/UITheme.cs` | 只留令牌（颜色/字号/间距/圆角）|
| `Assets/Scripts/UI/UIKit.cs`（新）| 组件构建：Panel / 三种 Button / Segmented / Slider / Chip / FocusRing |
| `Assets/Scripts/UI/LessonControlsUI.cs` 等 | 只做布局与数据绑定 |

---

### Task 1: 下载脚本与两个新角色资产

**Files:**
- Create: `Tools/fetch_rocketbox_avatar.py`
- Create（产物）: `Assets/Characters/Rocketbox/Resources/Characters/Rocketbox/Male_Adult_01.fbx`、`f002_*.png`、`Female_Child_01.fbx`、`cf001_*.png`

- [ ] **Step 1: 写下载脚本**

```python
"""按名字拉取 Rocketbox 角色：FBX + 贴图（TGA→PNG 2048），git blob SHA-1 校验。"""
import argparse, base64, hashlib, json, sys, urllib.request
from pathlib import Path

REPO = "microsoft/Microsoft-Rocketbox"
API = f"https://api.github.com/repos/{REPO}"
DEST = Path(__file__).resolve().parents[1] / "Assets/Characters/Rocketbox/Resources/Characters/Rocketbox"

def api(url: str) -> dict:
    req = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)

def blob(sha: str) -> bytes:
    req = urllib.request.Request(f"{API}/git/blobs/{sha}", headers={"Accept": "application/vnd.github.raw"})
    with urllib.request.urlopen(req, timeout=300) as r:
        return r.read()

def tree() -> list:
    return api(f"{API}/git/trees/master?recursive=1")["tree"]

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("name")
    ap.add_argument("--group", default="Adults")
    ap.add_argument("--max-size", type=int, default=2048)
    a = ap.parse_args()
    entries = {t["path"]: t for t in tree() if t["type"] == "blob"}
    stem = a.name.lower()
    DEST.mkdir(parents=True, exist_ok=True)
    want = {f"Assets/Avatars/{a.group}/{a.name}/Export/{a.name}.fbx": DEST / f"{a.name}.fbx"}
    for path in entries:
        low = path.lower()
        if f"/{a.name}/textures/" in low and low.endswith(".tga"):
            want[path] = DEST / (Path(path).stem.lower() + ".png")
    ok = 0
    for path, out in want.items():
        if out.exists():
            print(f"skip {out.name}"); ok += 1; continue
        data = blob(entries[path]["sha"])
        if hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest() != entries[path]["sha"]:
            print(f"FAIL sha1 {path}"); return 1
        if out.suffix == ".png":
            from io import BytesIO
            from PIL import Image
            img = Image.open(BytesIO(data)); img.thumbnail((a.max_size, a.max_size), Image.LANCZOS)
            img.save(out, "PNG", optimize=True)
        else:
            out.write_bytes(data)
        print(f"ok {out.name} {out.stat().st_size/1048576:.1f} MB"); ok += 1
    print(f"{a.name}: {ok} files"); return 0
```

- [ ] **Step 2: 拉两个角色**

Run: `python Tools/fetch_rocketbox_avatar.py Male_Adult_01 && python Tools/fetch_rocketbox_avatar.py Female_Child_01 --group Children`
Expected: 每个角色 1 个 FBX + 5 张 PNG，单角色总大小 < 12 MB

- [ ] **Step 3: 校验并提交**

Run: `git status --porcelain Assets/Characters/Rocketbox | head`
Expected: 新增 12 个文件（含 .meta 由 Unity 生成，首次打开编辑器后补）

```bash
git add Tools/fetch_rocketbox_avatar.py Assets/Characters/Rocketbox
git commit -m "feat(asset): 下载器与两名新 Rocketbox 角色"
```

---

### Task 2: 注册表切换到三个真人槽位

**Files:**
- Modify: `Assets/Scripts/Characters/CharacterRegistry.cs:28-47`
- Test: `Assets/Tests/EditMode/CharacterRegistryTests.cs`（若已存在则追加用例）

- [ ] **Step 1: 写失败用例**

```csharp
[Test]
public void BuiltIns_AreAllPrefabSourced()
{
    using (var registry = CharacterRegistry.CreateInstance())
    {
        foreach (var p in registry.All)
            Assert.That(p.Source, Is.EqualTo(CharacterSource.Prefab), p.Id);
    }
}

[Test]
public void DisplayNames_MatchRealisticSlots()
{
    CollectionAssert.AreEqual(new[] { "写实女性", "写实男性", "儿童" }, CharacterRegistry.BuiltInDisplayNames);
}
```

- [ ] **Step 2: 跑用例确认失败**

Run: `Unity.exe -batchmode -projectPath <p> -runTests -testPlatform EditMode -testFilter CharacterRegistry -testResults <xml> -logFile <log>`（不要加 `-quit`）
Expected: FAIL（当前 `Source` 是 `ProceduralBuiltIn`）

- [ ] **Step 3: 改注册表**

```csharp
public const string RocketboxMaleId = "rocketbox.male01";
public const string RocketboxChildId = "rocketbox.child01";

public static readonly string[] BuiltInIds =
{
    RocketboxFemaleId, RocketboxMaleId, RocketboxChildId
};

public static readonly string[] BuiltInDisplayNames = { "写实女性", "写实男性", "儿童" };
```

`CreateRocketboxProfile` 增加 `name`/`resourcesPath` 参数，三条 profile 分别指向 `Female_Adult_01` / `Male_Adult_01` / `Female_Child_01`。

- [ ] **Step 4: 跑用例确认通过并提交**

```bash
git add Assets/Scripts/Characters/CharacterRegistry.cs Assets/Tests/EditMode/CharacterRegistryTests.cs
git commit -m "feat(character): 三个内置槽位改为真人预制体"
```

---

### Task 3: 儿童角色的比例标定

**Files:**
- Modify: `Assets/Scripts/Characters/RocketboxCharacterView.cs`（per-definition 参数表）
- Test: `Assets/Tests/PlayMode/RocketboxRestPoseTests.cs`（按角色参数化）

- [ ] **Step 1: 让静止手型用例跑遍三个角色**

把现有的 `RocketboxRestPose` 用例参数化：`[Values("rocketbox.female01","rocketbox.male01","rocketbox.child01")]`，断言五指 `pipBend > 10°` 且 `|tipAboveKeys| <= 15 mm`。

- [ ] **Step 2: 跑用例，记录儿童的实际误差**

Run: 同上 PlayMode 批处理，`grep "tipAboveKeys" log`
Expected: 女性/男性通过；儿童大概率超差（比例不同）

- [ ] **Step 3: 加儿童条目并只调两个标量**

```csharp
case "rocketbox.child01":
    def.HandHeightOffset = 0.014f;   // 成人 0.028 的一半左右，按实测微调
    def.Hand.WristPitch = 8f;
    break;
```

- [ ] **Step 4: 重跑用例，误差 ≤ 15 mm 后提交**

```bash
git commit -am "feat(character): 儿童角色的手部标定"
```

---

### Task 4: 头像选择界面用真人缩略图

**Files:**
- Modify: `Assets/Scripts/UI/AvatarSelectionUI.cs`

- [ ] **Step 1: 缩略图加载**

```csharp
private static Sprite LoadPortrait(string profileId)
{
    string path = CharacterRegistry.PortraitResourcesPath(profileId); // "Characters/Rocketbox/Female_Adult_01"
    var tex = Resources.Load<Texture2D>(path);
    return tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
}
```

- [ ] **Step 2: 卡片改用缩略图 + 名字 + 属性标签**

名字取 `profile.DisplayName`，标签取「成人 / 儿童」。

- [ ] **Step 3: 采一张新基线 `V21_avatar_select.png` 并提交**

---

### Task 5: UI 令牌层与组件层拆分

**Files:**
- Modify: `Assets/Scripts/UI/UITheme.cs`（删到只剩令牌）
- Create: `Assets/Scripts/UI/UIKit.cs`

- [ ] **Step 1: 定义令牌**

```csharp
public static class UITheme
{
    public static readonly Color Bg        = new Color32(0x1A, 0x13, 0x10, 0xFF);
    public static readonly Color Panel     = new Color32(0x26, 0x1A, 0x14, 0xEB);
    public static readonly Color Border    = new Color32(0x6B, 0x4D, 0x35, 0xFF);
    public static readonly Color Text      = new Color32(0xF2, 0xE4, 0xCC, 0xFF);
    public static readonly Color TextMuted = new Color32(0xBD, 0xA5, 0x8A, 0xFF);
    public static readonly Color Accent    = new Color32(0xD9, 0xB8, 0x7E, 0xFF);
    public static readonly Color Track     = new Color32(0x4A, 0x35, 0x27, 0xFF);

    public const int Space1 = 8, Space2 = 16, Space3 = 24, Space4 = 32;
    public const int Radius = 6, BarHeight = 56, PlayBarHeight = 72;

    public const int FontTitle = 32, FontSubtitle = 20, FontBody = 15, FontLabel = 12;
    public static int Font(int size) => size;
}
```

- [ ] **Step 2: `UIKit` 提供 Panel/Button/Segmented/Chip/Slider/FocusRing**

每个构建方法返回 `RectTransform`，内部统一设置 `ColorBlock`（normal/hover/pressed/disabled 四态）与描边。

- [ ] **Step 3: 编译并跑 static_checks**

Run: `python Tools/static_checks.py`
Expected: 4 条规则 0 违规（`UI` 层不得引用 `MidiEventScheduler/LessonTimeline` 仍然成立）

- [ ] **Step 4: 提交**

---

### Task 6: 顶栏与播放条改造

**Files:**
- Modify: `Assets/Scripts/UI/LessonControlsUI.cs`、`UIStatusBar.cs`

- [ ] **Step 1: 顶栏 56 px**：返回 / 曲名（标题字号）/ 调号胶囊 / 弹性空白 / 状态 / 角色 / 帮助，左右内边距 24。
- [ ] **Step 2: 播放条 72 px**：播放圆钮（强调色）/ 时间等宽 / 进度轨 / 三个胶囊 / 更多。
- [ ] **Step 3: 低频项移入「更多」面板**（节拍器、分析、全屏）。
- [ ] **Step 4: 采 1280×720 与 1920×1080 两档截图，人工核对无贴边/重叠。**

---

### Task 7: 主菜单与角色选择重做

**Files:**
- Modify: `Assets/Scripts/UI/MainMenuUI.cs`、`AvatarSelectionUI.cs`

- [ ] **Step 1: 主菜单改卡片式四入口**：继续练习 / 选曲 / 角色 / 设置。
- [ ] **Step 2: 角色选择三张人像卡**（缩略图 + 名字 + 标签）。
- [ ] **Step 3: 采 `V22_mainmenu.png`、`V23_avatar_select.png`。**

---

### Task 8: 收尾（氛围 / Canvas / 贴图）

- [ ] **Step 1: URP Volume**（Bloom 0.15 / Vignette 0.2 / Color Adjustments），主光色温 4800 K + 冷侧光。
- [ ] **Step 2: Canvas 拆分**：播放条一个动态 Canvas，其余静态。
- [ ] **Step 3: 全量回归**：

```bash
python Tools/static_checks.py && python Tools/verify.py
```

Expected: 静态 4 条 0 违规；EditMode 522+、PlayMode 162+ 全绿；视觉基线 26 张重采 + 3 张新增；`git status` 干净。

---

## 暂停点（2026-11-02 21:32）

用户暂停项目。当前状态与下次接手的入口，按顺序写在这里。

### 已完成

- Task 1 资产：`Tools/fetch_rocketbox_avatar.py` 抓下 `Male_Adult_01`、`Female_Child_01`（FBX + 贴图 + 人像），Rocketbox 目录 23 MB → 52 MB。
- Task 2 注册表：三个内置角色改为 Prefab 真人（`rocketbox.female01` / `rocketbox.male01` / `rocketbox.child01`，显示名 写实女性 / 写实男性 / 儿童）；`RocketboxTextureLibrary` 支持按前缀绑定；新增 `RocketboxModelImportPostprocessor` 强制 Humanoid；`BuildTransformIndex` 兼容 `Bip02` 前缀。
- 验证：EditMode 522 通过；PlayMode 162 通过 / 4 跳过（加三个角色手型测试之前的基线）。

### 两个卡点（下次开工先解这两个）

1. **男性角色缺 `m002_opacity_color` 贴图**：资产门禁报「缺少主贴图 → 拒绝激活」。该文件走 GitHub blobs 反复 `IncompleteRead`（约 1.1 MB，三次重试都被截断），需要换获取方式（分段 Range 请求，或改用 `?ref=master` 的 HTML blob 页面）。
2. **女性角色手型回归**：`RocketboxRestPoseTests` 实测指尖 −11.3/−19.0/−23.6/−19.2/−6.9 mm（原标定 −21/+14/0/+12/+18）。根因已定位：这段 FBX 之前的 meta 里 `rigImportErrors` 写着 *duplicate transform 'Bip01 L Finger0'*、`human: []`（等于**没有 Humanoid 映射**，全靠名字查骨骼）；现在 `avatarSetup: 1` 生成了合法 Avatar，蒙皮/骨骼解析路径变了，静止掌位跟着偏移，`HandHeightOffset = 0.028` 不再适用。**需要按角色标定**，不要再用一个全局常量。

### 接手步骤

```bash
# 1. 补齐男性/儿童贴图（脚本已支持按前缀补抓）
py Tools/fetch_rocketbox_avatar.py Male_Adult_01 --group Adults  --prefix m002
py Tools/fetch_rocketbox_avatar.py Female_Child_01 --group Children --prefix cf001

# 2. 三个角色的静止手型实测（会打印 L0..L4 的 tip/prox/mid/dist）
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode \
  -testFilter "RocketboxRestPose" -testResults <abs>.xml -logFile <abs>.log   # 不要加 -quit

# 3. 按实测值给每个角色单独的 HandHeightOffset（改 RocketboxCharacterView.BuildDefinition 的字典），
#    目标：最低指尖落在 +2 mm 左右，且五指 PIP > 10°。
# 4. 给 RocketboxRestPoseTests 加断言：minTip ∈ [-3, +10] mm，防止再次静默回归。
```

之后才是 Task 4（头像缩略图）到 Task 8（氛围 / Canvas / 贴图）。

## 进度快照（2026-11-03 00:50，暂停点）

| 任务 | 状态 | 说明 |
|---|---|---|
| Task 1 资产 | ✅ | `m`ale01 / `child01` 的 FBX、贴图、人像已入库；`Tools/fetch_rocketbox_avatar.py` 支持分块下载（大贴图走 206 Range） |
| Task 2 注册表 | ✅ | `BuiltInIds` 改为三个预制体 id，显示名「写实女性 / 写实男性 / 儿童」 |
| Task 3 标定 | ✅ | 腕高按角色标定（女 0.016 / 男 0.008 / 童 0.001）；三角色静止手型用例全过，食指指尖 2.1 / 5.1 / 5.1 mm |
| Task 4 头像缩略图 | ⬜ | 未开始 |
| Task 5-7 UI 令牌 / 组件 / 三界面重做 | ⬜ | 未开始（方向 B 暖木古典已定案） |
| Task 8 氛围 / Canvas / 降采样 | ⬜ | 未开始 |

### 本轮修复的关键 bug

`CharacterSwitchService` 的 no-op 判定只比较 `CharacterId`，而 `ProceduralCharacterView.CharacterId`
在内置索引下回落到 `BuiltInIds[_index]`（就是 `rocketbox.female01`）。于是「请求真人女性」被判定为
重复请求并 `CommitNoOp` 短路，画面永远停在方块人——这正是用户反复说「还是假写实」的根因。
修复方式：同 id 时再加一层「视图种类」判定（`SameViewKind`，比较 `IsBuiltInBacked` 与
`CharacterSource.ProceduralBuiltIn`）。

### 下次接手

1. 先重跑全量 PlayMode 确认这两个失败是否偶发：`PlaybackPerformance_5Seconds`、`CancelLoad_KeepsPreviousSongPlayable`
   （本轮 128 过 / 2 败 / 4 跳；两者与角色改动无关，疑为负载下的计时抖动）。
2. 跑 `CaptureLessonViews` 重采视觉基线，确认三个角色在首屏/特写里都换成了真人。
3. 按 Task 4 → Task 5（UI 令牌）→ Task 6/7（三界面）顺序继续。

## 进度快照（2026-11-03 14:43，暖木 UI 第一轮）

| 任务 | 状态 | 说明 |
|---|---|---|
| Task 4 头像缩略图 | ✅ | 三张人像卡显示真人脸；`V21` 复采后不再是纯色占位 |
| Task 5 UI 令牌 | ✅ | `UITheme` 调色板整体换成暖木古典 |
| Task 6 顶栏 / 播放条 | ⬜ | 顶栏已是 56 单行；播放条仍是旧结构，留待单独一轮 |
| Task 7 主菜单 / 角色选择 | ✅ | 卡片式四入口 + 选曲/角色/设置三个浮层 |
| Task 8 氛围 / Canvas / 降采样 | ⬜ | 未开始 |

### 本轮实际改动

- **暖木令牌**（`Assets/Scripts/UI/UITheme.cs`）：只改数值不动令牌名 —— `SceneBg=(0.086,0.063,0.055)`、`PanelBg=(0.149,0.106,0.082)`、`Control=(0.204,0.145,0.110)`、`TextPrimary=#F2E4CC`、`TextSecondary=#BDA58A`、`PanelBgRaised=(0.180,0.129,0.098)`、`PanelBgDeep=(0.118,0.086,0.067)`、`Divider=(0.910,0.835,0.706,.16)`、`Selected=(0.416,0.290,0.133)`；Accent 系列不动。
- **主菜单四入口**（`Assets/Scripts/UI/MainMenuUI.cs`）：`HeroBand`（88 高，标题 + 副标题 + 右侧「曲库 N 首 · 最近 M 首」）→ 四张 216×96 入口卡（继续练习 / 选曲 / 角色 / 设置）→ 原有曲库网格与最近播放整体下移 `EntriesBlock=120`。三个浮层：`OpenSongPicker`（行高随曲目数伸缩，最多 8 行 + 导入按钮）、`OpenCharacterPreview`（三张 120×124 人像卡 + 成人/儿童标签）、`OpenSettings`（6 档画质中文标签 + 全屏开关 + 快捷键表，`PlayerPrefs` 持久化）。
- **全屏暖底**：主菜单此前没有底座，露出的是场景相机的冷色天空盒，暖木配色只生效一半；现在先铺 `SceneBg` 再叠内容。
- **人像比例**（`Assets/Editor/RocketboxTextureSetup.cs`）：源图 384×512 被 Unity 默认 `npotScale: ToNearest` 拉成 512×512，脸横向变宽；新增 `_portrait` 规则（`npotScale = None`、`maxSize = 512`）。
- **新增截图**：`V24_menu_characters.png`、`V25_menu_settings.png`、`V26_menu_songs.png`（由 `VisualSnapshotTests.CaptureMenuOverlay` 采集，点入口卡 → 截浮层 → 点「关闭」）。

### 本轮踩到的坑

1. **`Start()` 里读不到参考分辨率**：`RectTransform.width` 此时等于窗口像素（640），不是 1280，四张入口卡因「宽度不够」整排隐藏。改成跨帧判断：`Update()` 里比较 `Width(_content)` 与上次布局值，变化超过 1 px 就 `RebuildContent()`。
2. **`Image.color` 会与 sprite 相乘**：给缩略图挂上人像后必须把 `color` 回白，否则整张脸被面板底色染成暗棕。
3. **画质档位有 6 档**：`ProjectSettings/QualitySettings.asset` 是 Very Low…Ultra，`m_CurrentQuality: 5`；只显示 3 档会让当前档位落在范围外，一个 chip 都高亮不出来。
4. **`UIKit.Card` 的 onClick 是 `UnityEngine.Events.UnityAction`**，`UITheme` 也没有 `StretchBottom`（只有 `StretchX/StretchTop/StretchAll`）。

### 下次接手

1. Task 6：播放条压成 72 单行 + 「更多」面板。必须同时改 `LessonControlsUI.LayoutPlayerRows()`、14 个控件的位置/命名、以及 `VisualSnapshotTests` 里**按对象名点击**的用例（`Btn_曲目分析` 等），否则回归会红。
2. Task 8：URP Volume（Bloom 0.15 / Vignette 0.2）、Canvas 拆分、贴图降采样；再跑 `python Tools/static_checks.py && python Tools/verify.py` 全量回归。
3. Task 6 Step 4 还欠 1920×1080 那一档截图（现有 26 张都是 1280×720）。

## 进度快照（2026-11-03 19:10，底栏单行 + 「更多」浮层）

| 任务 | 状态 | 说明 |
|---|---|---|
| Task 6 顶栏 / 播放条 | ✅ | 播放条 132 → **72 单行**；视角 / 高亮 / 节拍器 / 展示 / 曲目分析 / 重播 / 循环范围收进「更多」浮层 |
| Task 6 Step 4 两档截图 | ✅ | 1280×720（正式基线 26 张 + 新增 `V27_more_panel.png`）与 1920×1080 都采过 |
| Task 8 氛围 / Canvas / 降采样 | ⬜ | 未开始 |
| 已知问题：V12/V27 里的「粉红气球人」 | ⚠️ | 未定位，见下 |

### 本轮实际改动

- **底栏单行**（`Assets/Scripts/UI/UITheme.cs`）：`Layout.MainBarHeightNormal` 132 → `72f`（16 上边距 + 40 控制行 + 16 下边距），`MainBarHeightCompact` 也是 72（紧凑档只隐藏时间标签）。
- **单行布局**（`Assets/Scripts/UI/LessonControlsUI.cs`）：`LayoutPlayerRows()` 重写——左簇 `播放(96) + 时间 44 + 进度轨`，右簇从右往左 `更多(64) / 循环(68) / 速度(72) / 手型(144)`；`_progress` 用 `StretchX` 吃掉中间全部余量。
- **「更多」浮层**：`_morePanel`（468×96，两行 32 高 chip）挂在底栏右上角，`anchorMin=anchorMax=(1,1)`、`pivot=(1,1)`、`anchoredPosition=(-16, 96+8)`；`ReparentInto()` 把 `_cameraSeg / _restartBtn / _highlightChip / _metronomeChip / _displayChip / _analysisBtn / _beatDots / _loopRangeGroup` 搬进浮层，`ToggleMore()` 控制显隐并把「更多」chip 置为按下态；对象名 `Btn_更多` / `Btn_曲目分析` 保持不变，PlayMode 用例仍按名点击。
- **截图可配置**（`Assets/Tests/PlayMode/VisualSnapshotTests.cs`）：`W/H` 改为可由 `PIANO_SNAPSHOT_W` / `PIANO_SNAPSHOT_H` 覆盖，输出目录可由 `PIANO_SNAPSHOT_DIR` 覆盖——采宽屏一档时写到 `_verify_out/shots_1920/`，**不覆盖** `Docs/screenshots` 的正式基线。
- **新增快照**：`V27_more_panel.png`（浮层展开态），采集点插在 V12 之前，采完再点「更多」收起，后续用例状态不变。

### 本轮踩到的坑

1. **`UITheme.Place` 的原点在左下角**（`anchorMin=anchorMax=(0,0)`、`offsetMin=(x,y)`），`PlaceRight` 是右下角，只有 `PlaceTopLeft/PlaceTopRight` 才是「从顶上往下算」。第一次写浮层时按「从上往下」排两行，结果两行顺序反了——`MorePanelHeight` 定死 96 后，第一行用 `rowTop = 96 - 12 - 32 = 52`、第二行 `rowBottom = 12` 才对。
2. **1920×1080 一档暴露了胶囊过窄**：「速度: 1.0×」的 `×` 被挤出右边（`SpeedWidth=60` 在 1.5 倍缩放下不够，`50%/150%/200%` 更危险），改成 72 后两档都放得下。
3. **`Session.SwitchAvatar(int)` / `CycleAvatar()` 是「程序化内置角色」的旧下标路径**，不经过 `CharacterSwitchService` 的 8 步事务；截图用例里先 `CycleAvatar` 切到人偶、再 `ActivateCharacter` 切回写实角色时，两者状态会不同步。

### 已知问题（下次接手第一条）

`V12_analysis_expanded.png` / `V27_more_panel.png` 里显示的是**粉红气球人（程序化 rig）**，而同一场景、同一角色的 `V15_rocketbox_character.png` 是写实真人。已排除/已确认：

- `[V12] 归位角色 rocketbox.female01：成功=True` —— 切换事务本身返回成功；
- `V15` 的 `[DIAG] view=rocketbox.female01 … avatar=Female_Adult_01Avatar` 说明写实视图确实存在；
- 对三个内置 rig 调 `SetBuiltInActive(i, false)` 也压不下去（那条试验代码已撤掉，只留 `[V12]` 一行日志）；
- `06_avatar_cartoon.png` 里程序化卡通是绿色 + 蓝帽子，V12/V27 的粉红人是另一个 rig。

下一步怀疑方向：`SetDisplayMode(DisplayMode.Showcase)` 之后 `AfterAvatarChanged()` 的调用顺序，或 `AvatarManager.ExternalRigSource` 在展示模式下被清空导致 `_rigs[_currentIndex]` 重新可见。

### 另外两条遗留

1. Task 8：URP Volume（Bloom 0.15 / Vignette 0.2 / Color Adjustments）、主光 4800K + 冷侧光、Canvas 拆分（播放条单独动态画布）、贴图降采样。
2. 全量回归：`python Tools/static_checks.py`（已过：4 条 0 违规）→ `python Tools/verify.py`（EditMode 522+ / PlayMode 162+），再 `git status` 收尾。

