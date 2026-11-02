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
