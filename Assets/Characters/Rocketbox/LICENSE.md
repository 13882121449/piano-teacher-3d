# Microsoft Rocketbox — 素材许可与来源

本目录包含的 `Female_Adult_01.fbx` 及 `f001_head_color.tga` / `f001_head_normal.tga`
取自 **Microsoft Rocketbox Avatar Library**。

- 上游仓库：<https://github.com/microsoft/Microsoft-Rocketbox>
- 许可：**MIT License**（微软于 2020 年 11 月将整库 115 个角色改为 MIT 许可，
  允许商用、修改与再分发）
- 引用建议：Gonzalez-Franco, M., et al. *Microsoft Rocketbox Avatar Library* (2020)

## MIT License

Copyright (c) Microsoft Corporation.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## 本地适配说明

- 骨骼为 3ds Max Biped 命名（`Bip01 L Hand`、`Bip01 L Finger0/01/02`…），
  每只手 5 指 × 3 节，导入类型为 Humanoid（`animationType: 2`）。
- 模型为**站位 T-Pose**；接入演奏需要运行时坐姿适配（见
  `Docs/character-asset-requirements.md`）。
- 纹理为独立 TGA，材质由 Unity 按默认材质描述导入，正式发布前需按项目风格调整。
