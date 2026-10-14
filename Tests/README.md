# Tests 目录说明

方案第 5 节「推荐 Git 仓库结构」约定测试位于仓库根目录 `Tests/EditMode` 与 `Tests/PlayMode`。

**实际可被 Unity Test Runner 识别的测试程序集位于 `Assets/Tests/EditMode` 与 `Assets/Tests/PlayMode`**，
这是 Unity Test Framework 的硬性要求（测试程序集必须位于 `Assets` 下，通过 `.asmdef` 的
`defineConstraints: ["UNITY_INCLUDE_TESTS"]` 与 `overrideReferences` + `nunit.framework.dll` 标记）。

本根目录 `Tests/` 为忠实方案第 5 节目录树而保留的镜像目录（空），
测试代码请维护在 `Assets/Tests/` 下。若希望根目录 `Tests/` 直接承载测试，
可将其内容放入 `Assets/Tests/` 后删除本目录，或在 Docs 中记录你的调整决策。
