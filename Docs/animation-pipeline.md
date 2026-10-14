# 动画管线设计（animation-pipeline.md）

> 描述「MIDI → 琴键 → 手部 → 手指」的完整链路，对应方案 3.3 基础数据流与 7 节分阶段计划。

---

## 1. 首版（MVP）管线

```text
MIDI 文件（.mid）
   │  MidiParser（纯 C#，SMF 解析：VLQ/running status/tempo map）
   ▼
MidiNoteEvent[]（pitch / startTime / duration / velocity / track / channel，秒制）
   │  MidiEventScheduler.Load → 展开为 (time, on/off, note) 事件流
   ▼
LessonTimeline 推进 CurrentTime（唯一时间源）
   │  MidiEventScheduler.Tick(CurrentTime) 触发到期的 on/off
   ├──▶ PianoKeyAnimator   ──▶ 琴键下压/回弹（绕键后端 X 轴旋转插值）
   ├──▶ PianoAudioSynth    ──▶ 基频+谐波+指数衰减合成，velocity 控音量
   ├──▶ HandIKController   ──▶ 左右手（C4=60 分界）手腕目标 → 琴键上方
   └──▶ FingerPoseController──▶ 按下键按 pitch 映射到手指，弯曲/伸直
```

---

## 2. 各环节说明

### 2.1 MIDI 解析（`MidiParser`）
- 支持 Format 0/1/2、PPQ 时间基；拒绝 SMPTE。
- 支持 running status、tempo 变化（`tick → 秒` 分段累积）。
- Note On（velocity>0）与 Note Off（0x8n 或 0x9n vel=0）配对；同一时刻多音符（和弦）天然支持。
- 悬空 Note Off 忽略；未关闭 Note On 首版丢弃。

### 2.2 琴键动画（`PianoKeyAnimator`）
- 88 键程序化生成（白键宽 1、黑键宽 0.6，黑键更短更高，位置按钢琴标准布局）。
- 键根 pivot 位于键后端，按下 = 绕 X 轴负角旋转；`Quaternion.Slerp` 平滑下压/回弹。
- `ReleaseAll()` 在 Stop/Seek/重载时立即复位所有琴键。

### 2.3 音频（`PianoAudioSynth`）
- 程序化合成：三角基频 + 2/3/4 次谐波 + 指数衰减包络，零外部音频资产（符合许可证原则）。
- 每个 pitch 预生成 `AudioClip`，Note On 时 `AudioSource.PlayOneShot(clip, velocity/127)` 叠加发声（支持和弦）。
- 已知限制：合成音为固定衰减（约 2.5s），不精确对应长音符时长（MVP 简化，后续可换合法音色采样或每音符独立 AudioSource 精确控制）。

### 2.4 手部 IK（`HandIKController`）
- 左右手以 C4（pitch=60）分界：`<60` 左手，`>=60` 右手。
- 每帧求「当前按下键」：左手取最低音、右手取最高音，作为手腕目标（键上方固定高度/深度）。
- 手臂为肩→腕单段胶囊，`StretchArm` 按距离拉伸并对准方向（简化两段 IK，MVP 够用）。
- 无按键时手腕回到键盘两侧准备位置。

### 2.5 手指姿态（`FingerPoseController`）
- 按下键按 pitch 排序映射到手指（指1..指5 对应低..高），按下弯曲 -35°、抬起伸直。
- **首版不承诺专业指法**（方案 2.3）：五指跨度启发式仅为视觉占位，真实指法需规则/标注数据/模型。

---

## 3. Seek / 停止 的状态重建

`LessonSessionController.Seek(t)` 顺序：
1. `LessonTimeline.Seek(t)`
2. `PianoKeyAnimator.ReleaseAll()` / `HandIKController.ReleaseAll()` / `FingerPoseController.ReleaseAll()`（清空状态）
3. `MidiEventScheduler.SeekTo(t)`（重放 seek 点之前已开始且仍在持续的 Note On，重建按键/手部状态）

---

## 4. 后续增强（方案 2.2 / 阶段五）

- 更自然手腕移动、手指按压、双手协同 → 替换 `HandIKController`/`FingerPoseController` 实现。
- 慢速/循环/分手练习/节拍器 → 扩展 `LessonTimeline` 与 `LessonControlsUI`。
- 音符高亮提示 → `PianoKeyAnimator` 加高亮通道。
- AI 动作生成（PianoMotion10M / MANO）→ 替换 `HandIKController` 数据源，保留 `IHandRig` 与「当前按下键」接口。
