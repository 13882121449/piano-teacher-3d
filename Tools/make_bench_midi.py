#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成手部接触评测用标注曲目集（手部接触与视觉呈现优化方案 · 批次 1.1）

用途
----
方案要求「3 首自制 MIDI，覆盖 黑键 / 和弦 / 快速音阶 / 大跨度 四类」，放
`Assets/StreamingAssets/Songs/bench/`，避免版权问题。本脚本用标准库直接写
SMF（Format 0，PPQ 480，120 BPM），保证曲目可复现、可重新生成。

用法
----
    python Tools/make_bench_midi.py            # 重新生成三首曲目
    python Tools/make_bench_midi.py --check    # 只校验现有文件（存在且可解析头）

约定
----
- 每首 16 拍（4 小节 4/4），120 BPM ≈ 8 秒；窗口足以覆盖「全曲」采样。
- 音高范围 A2–C6，左右手以 C4（60）为界，与项目默认分手规则一致。
"""

import argparse
import struct
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent
OUT_DIR = PROJECT_ROOT / "Assets" / "StreamingAssets" / "Songs" / "bench"

PPQ = 480
TEMPO_US = 500000  # 120 BPM


def vlq(value: int) -> bytes:
    """MIDI 变长数量编码。"""
    if value < 0:
        raise ValueError("VLQ 不支持负数")
    buf = [value & 0x7F]
    value >>= 7
    while value:
        buf.append((value & 0x7F) | 0x80)
        value >>= 7
    return bytes(reversed(buf))


def track_chunk(events):
    """events: [(绝对 tick, 事件字节串)]，自动转 delta 并补 End of Track。"""
    events = sorted(events, key=lambda e: e[0])
    body = bytearray()
    now = 0
    for tick, data in events:
        body += vlq(tick - now) + data
        now = tick
    body += vlq(0) + b"\xFF\x2F\x00"
    return b"MTrk" + struct.pack(">I", len(body)) + bytes(body)


def note(pitch, start_beat, length_beat, velocity=90):
    start = int(round(start_beat * PPQ))
    end = int(round((start_beat + length_beat) * PPQ))
    return [
        (start, bytes([0x90, pitch & 0x7F, velocity & 0x7F])),
        (end, bytes([0x80, pitch & 0x7F, 0x40])),
    ]


def write_midi(path: Path, notes, title: str):
    events = [(0, b"\xFF\x03" + vlq(len(title)) + title.encode("ascii", "replace"))]
    events.append((0, b"\xFF\x51\x03" + TEMPO_US.to_bytes(3, "big")))
    events.append((0, b"\xFF\x58\x04\x04\x02\x18\x08"))  # 4/4
    for pitch, start, length in notes:
        events += note(pitch, start, length)
    header = b"MThd" + struct.pack(">IHHH", 6, 0, 1, PPQ)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(header + track_chunk(events))
    return path


def black_keys():
    """黑键为主的旋律（含白键过渡），16 拍。"""
    notes = []
    seq = [63, 66, 68, 70, 73, 75, 78, 75, 73, 70, 68, 66, 63, 66, 70, 73]
    for i, p in enumerate(seq):
        notes.append((p, i, 0.9))
    # 左手低音（八度根音），每两拍一次
    for i, p in enumerate([39, 42, 45, 47, 45, 42, 39, 36]):
        notes.append((p, i * 2, 1.8))
    return notes


def chords():
    """三/四音和弦（右手）+ 左手根音，16 拍，每和弦 2 拍。"""
    rh = [
        [60, 64, 67], [62, 65, 69], [64, 67, 71], [65, 69, 72],
        [67, 71, 74], [69, 72, 76], [71, 74, 77], [72, 76, 79],
    ]
    lh = [36, 38, 40, 41, 43, 45, 47, 48]
    notes = []
    for i, (chord, root) in enumerate(zip(rh, lh)):
        start = i * 2
        for p in chord:
            notes.append((p, start, 1.8))
        notes.append((root, start, 1.8))
        notes.append((root + 12, start, 1.8))
    return notes


def fast_scale_and_leaps():
    """16 分音符快速音阶 + 跨八度大跳，16 拍。"""
    notes = []
    scale = [60, 62, 64, 65, 67, 69, 71, 72]
    t = 0.0
    for _ in range(2):          # 上行 + 下行各一轮
        for p in scale:
            notes.append((p, t, 0.22))
            t += 0.25
        for p in reversed(scale[:-1]):
            notes.append((p, t, 0.22))
            t += 0.25
    # 大跨度换位：左手 A2 ↔ 右手 C6，交替
    leaps = [(45, 84), (43, 79), (41, 76), (40, 72)]
    for low, high in leaps:
        notes.append((low, t, 0.9))
        notes.append((high, t, 0.9))
        notes.append((low + 12, t + 0.5, 0.4))
        t += 1.0
    return notes


def check():
    ok = True
    for name in ("bench_black_keys.mid", "bench_chords.mid", "bench_fast_scale.mid"):
        path = OUT_DIR / name
        if not path.exists():
            print(f"✗ 缺少 {path}")
            ok = False
            continue
        head = path.read_bytes()[:14]
        if head[:4] != b"MThd":
            print(f"✗ {name}: 不是合法 SMF 头")
            ok = False
            continue
        print(f"✓ {name}: {path.stat().st_size} 字节")
    return 0 if ok else 1


def main():
    parser = argparse.ArgumentParser(description="生成手部接触评测标注曲目集")
    parser.add_argument("--check", action="store_true", help="只校验现有文件")
    args = parser.parse_args()

    if args.check:
        return check()

    todos = [
        ("bench_black_keys.mid", black_keys(), "Bench Black Keys"),
        ("bench_chords.mid", chords(), "Bench Chords"),
        ("bench_fast_scale.mid", fast_scale_and_leaps(), "Bench Fast Scale"),
    ]
    for name, notes, title in todos:
        path = write_midi(OUT_DIR / name, notes, title)
        print(f"✓ {path.relative_to(PROJECT_ROOT)}  ({len(notes)} 个音)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
