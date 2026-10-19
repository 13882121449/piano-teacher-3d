# 手部接触评测基线（2026-10-19）

> 依据：`Docs/hand-contact-and-visual-optimization-plan.md` 批次 1「评测基线可归因化」。
> 本文件只记录**测量结果**，不含任何行为改动；所有数字均来自可复现命令，未做推算。

## 1. 采集口径

| 项 | 值 |
|---|---|
| 曲目集 | `Assets/StreamingAssets/Songs/bench/`（3 首，由 `Tools/make_bench_midi.py` 生成，无版权风险） |
| 采样窗口 | **整曲**（不是固定 3 s 窗口） |
| 引擎 / 机器 | Unity 2022.3.22f1c1 / i9-14900HX + RTX 4060 Laptop（`-batchmode -nographics`） |
| 用例 | `Assets/Tests/PlayMode/HandBenchmarkTests.cs` → `Bench_ThreeSongs_LogFullReport` |
| 命令 | Unity `-runTests -testPlatform PlayMode -testFilter "HandBenchmarkTests"` |
| 指标定义 | `Docs/animation-pipeline.md` §6.4（单位 m，日志按 mm 显示） |
| 采样来源 | `HandMotionReport`（`HandIKController.BuildMotionReport()`），逐帧、每手每帧最多 1 个指尖样本 |

曲目设计（120 BPM、4 小节 4/4）：

| 文件 | 覆盖类别 | 内容 |
|---|---|---|
| `bench_black_keys.mid` | 黑键 | 黑键旋律 + 左手八度根音，16 拍 |
| `bench_chords.mid` | 和弦 | 右手三和弦 + 左手根音/八度，8 个和弦 × 2 拍 |
| `bench_fast_scale.mid` | 快速音阶 + 大跨度 | 16 分音符音阶上下行 + A2↔C6 交替大跳 |

## 2. 单项指标（整曲）

| 曲目 | 时长 | 键面 P50 | 键面 P95 | 键面峰值 | 指尖均值 | 指尖 P95 | 换位峰值 | 悬空帧 | 指尖压键帧 | 命中率 | 目标样本 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `bench_black_keys` | 8.0 s | 9.1 | 10.3 | 65.4 | 24.7 | 35.9 | 69.9 | 4 | 141 | 99.9% | 868 |
| `bench_chords` | 7.9 s | 5.1 | 14.3 | 48.8 | 32.3 | 45.9 | 138.9 | **434** | **382** | 99.3% | 868 |
| `bench_fast_scale` | 5.7 s | 3.6 | 17.1 | 44.4 | 21.8 | 50.3 | **250.7** | 65 | 31 | 96.1% | 413 |

单位：mm；「悬空帧」= `HoveringFrames`，「指尖压键帧」= `FingerPenetrationFrames`（压入键面 > 4 mm），
「掌穿模」= `PalmPenetrationFrames`（三首均为 0）。

## 3. 分组命中率（方案 1.4 的门禁基线）

| 曲目 | 黑键 | 和弦 | 快速音阶 | 跨手 |
|---|---|---|---|---|
| `bench_black_keys` | 99.9%（856） | 样本 0 | 样本 0 | 100%（1） |
| `bench_chords` | 样本 0 | 99.3%（868） | 99.3%（868） | 样本 0 |
| `bench_fast_scale` | 样本 0 | 100%（96） | 97.8%（372） | 100%（1） |

门禁（`HandBenchmarkTests.Bench_GroupHitRates_NotBelowRecordedBaseline`）= 基线 − 3pp：
黑键 96.9% / 和弦 96.3% / 快速音阶 94.8% / 跨手 95%（跨手样本仅 1，取 95% 防单样本抖动）。

## 4. 左右手分项（批次 1.3 新增输出）

| 曲目 | 左手 指尖 均值 / P95 | 左手 键面 均值 / P95 | 右手 指尖 均值 / P95 | 右手 键面 均值 / P95 |
|---|---|---|---|---|
| `bench_black_keys` | 22.1 / 33.4 | 1.3 / 10.3 | 27.3 / 36.1 | 8.3 / 9.7 |
| `bench_chords` | **38.4 / 47.4** | 6.6 / 15.3 | 26.3 / 44.3 | 3.6 / 8.4 |
| `bench_fast_scale` | 27.0 / **52.3** | 6.7 / 16.6 | 19.9 / 50.3 | 4.9 / 20.0 |

## 5. 结论：错在哪一类帧

1. **和弦类最差，且是双向失控**：`bench_chords` 同时出现 434 个悬空帧和 382 个指尖压键帧，键面 P95 14.3 mm 已接近目标，
   但峰值 48.8 mm 说明「成形阶段抬高 → 落键」的过程中既有抬得过高、也有压得过深。对应方案 2.5（和弦成形时长自适应）
   与批次 4.2（落键后保持微屈、不回弹）。
2. **大跨度换位是换位峰值的唯一来源**：`bench_fast_scale` 的 250.7 mm 峰值全部落在 A2↔C6 交替跳进处（35 个过渡帧），
   对应方案 2.3（可达性检测 + 抬腕—平移—落键）。
3. **黑键不是主要矛盾**：`bench_black_keys` 键面 P50 9.1 mm、P95 10.3 mm，已达到方案 2.1 的 P95 ≤15 mm 目标；
   但 141 个指尖压键帧说明黑键按压深度偏大（黑键键面更高）。
4. **左手在和弦上明显更差**（38.4 vs 26.3 mm），与第二轮修完的「左手镜像指法」有关：低音根音由左手 4/5 指承担，
   拇指/小指的可达性差异被放大，是批次 2.2（指法兜底）需要重点看的样本。
5. **手腕命中率（96–99.9%）与指尖接触质量没有相关性**：三首曲子命中率都接近 1，但键面/指尖误差差距很大，
   印证方案 §1.3 的判断——现行门禁量的是手腕水平位置，掩盖了垂直悬空。

## 6. 复跑方式

```bash
python Tools/make_bench_midi.py          # 重新生成曲目（幂等）
python Tools/verify.py                   # 全套（含 PlayMode，会跑基准用例）
# 只跑基准：Unity -runTests -testPlatform PlayMode -testFilter "HandBenchmarkTests"
```

调整曲目或改动指法/IK 后，必须同步更新第 2–4 节数值与 `HandBenchmarkTests` 里的基线常量。
