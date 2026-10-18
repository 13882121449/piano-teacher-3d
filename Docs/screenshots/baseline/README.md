# 视觉基线目录（Docs/screenshots/baseline/）

把**经人工看图确认无误**的截图以**同名**复制到本目录，即成为自动像素比较的基线。

- 采集：运行 `VisualSnapshotTests.CaptureLessonViews`（需 GPU，不要加 `-nographics`）
- 比对：`python Tools/visual_diff.py Docs/screenshots/baseline Docs/screenshots`
  或运行 `VisualBaselineCompareTests`（Explicit）
- 缺基线时比对测试只会 `Assert.Ignore`，**不会**自动把当前截图当基线

本文件仅用于让空目录进入版本库；基线图本身也提交在本目录下（与 `Docs/screenshots/manifest.md` 一起维护）。
