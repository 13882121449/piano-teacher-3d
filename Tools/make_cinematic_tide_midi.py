#!/usr/bin/env python3
"""生成海底展示用的原创 MIDI《潮光》，全曲约 24 秒。"""

from pathlib import Path

from make_bench_midi import write_midi


def main():
    # 每小节固定四拍，三小节一个画面段落。旋律与伴奏分处默认分手点两侧。
    score = [
        ((45, 52, 57), (64, 67, 69, 67)),
        ((41, 48, 53), (65, 69, 72, 69)),
        ((48, 55, 59), (67, 64, 62, 64)),
        ((43, 50, 55), (67, 71, 74, 71, 67, 62)),
        ((45, 52, 57), (64, 67, 69, 72, 74, 76)),
        ((41, 48, 53), (77, 76, 72, 69, 72, 74)),
        ((48, 55, 59), (76, 79, 81, 79, 76, 72)),
        ((43, 50, 55), (74, 71, 74, 79, 81, 79)),
        ((45, 52, 57), (76, 74, 72, 69, 67, 64)),
        ((41, 48, 53), (65, 69, 72, 69)),
        ((43, 50, 55), (67, 71, 74, 71)),
        ((48, 55, 59), (67, 64, 60)),
    ]
    intensity = [49, 52, 54, 59, 64, 66, 73, 75, 68, 54, 48, 43]
    notes, sustain = [], []
    for index, ((root, fifth, top), melody) in enumerate(score):
        start = index * 4
        velocity = intensity[index]
        notes.extend([(root, start, 2.75, velocity - 10), (fifth, start + 1, 0.77, velocity - 14),
                      (top, start + 2, 1.59, velocity - 15)])
        sustain.extend([(start + 0.11, True), (start + 3.72, False)])
        offsets = (0, .5, 1, 1.5, 2, 3) if len(melody) == 6 else (0, 1, 2, 3)
        for j, (offset, pitch) in enumerate(zip(offsets, melody)):
            end = offsets[j + 1] if j + 1 < len(melody) else 3.94
            notes.append((pitch, start + offset, end - offset - 0.035, velocity + (j % 3) * 2))

    out = (Path(__file__).resolve().parents[1] / "Assets" / "StreamingAssets" / "Songs"
           / "cinematic" / "cinematic_tide.mid")
    print(write_midi(out, notes, "Tidal Light", sustain))


if __name__ == "__main__":
    main()
