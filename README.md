# 钢琴 3D 教学

Windows 桌面应用。把 MIDI 曲目变成可播放的 3D 演奏，显示按键、指法、左右手动作和琴键高亮。支持暂停、变速、分手练习、区间循环和视角切换。

## 运行

- 本地 Windows v3 位于 `Builds/Windows/1112_优化版_v3/`。双击 `PianoTeacher3D.exe`，移动时保留整个目录。
- 从 GitHub 获取源码后，用 Unity Hub 打开本仓库目录。编辑器版本以 `ProjectSettings/ProjectVersion.txt` 为准，目前为 **2022.3.22f1c1**。在 Unity 中打开 `Assets/Scenes/MainMenu.unity` 后点击播放。
- Windows 构建命令、验证参数和常见问题见[运行与验证指南](Docs/how-to-run.md)。

## 项目目录

- `Assets/` 存放 Unity 场景、脚本、资源和 `Assets/Tests/` 中的 Unity 测试。
- `Packages/` 和 `ProjectSettings/` 存放 Unity 依赖和工程设置。
- `Docs/` 存放[项目文档](Docs/index.md)，历次优化验收集中在 `Docs/reports/`。
- `Tools/` 存放验证与辅助脚本，Python 脚本测试位于 `Tools/tests/`。
- `Builds/` 和 `_verify_out/` 分别存放本地交付包和验证产物，已从 Git 中排除。历史验证归档在 `_verify_out/history/`。

## 开发验证

在项目目录执行：

```powershell
python -B Tools/static_checks.py
python -B -m unittest discover -s Tools/tests
python -B Tools/verify.py
```

本机 Windows v3 的普通播放和切换测试均通过原性能门槛，1% low 分别为 **58.77 FPS** 和 **59.12 FPS**。内置和写实女性角色的三首基准曲均通过穿键检查，和弦与快速音阶的离键率仍高于 2% 的目标。测试范围、异常记录与数据见[最新优化验收](Docs/reports/1112_优化验证_v3.md)。
