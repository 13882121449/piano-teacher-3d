#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
钢琴 3D 教学 APP（Piano Teacher 3D）— 视觉基线像素比较工具

对应文档
--------
* APP《产品与工程综合优化方案》**11.2 视觉回归**：
  「自动像素差异可作为辅助，但不能因为光照细微变化导致所有截图测试失去意义；需设定容差或关注区域」
* 《PianoTeacher3D-项目审查与优化建议》**6.1 画面基线组合**：
  「像素差异只能作为辅助，不能完全取代人工看图」

因此本工具刻意提供 `--tolerance`（逐像素容差）、`--roi` / `--exclude-roi`（关注区域）
与 `--max-diff-ratio`（允许的超差像素比例）三个「降噪」开关：
像素比较只是**人工看图之后的第二道护栏**，像素通过不代表画面对。

依赖
----
**纯 Python 3.8+ 标准库**（`zlib` + `struct` 手写 PNG 解码），不需要 numpy / Pillow。

支持范围
--------
只支持 **8 位、非隔行、color type 2(RGB) / 6(RGBA)** 的 PNG（Unity `Texture2D.EncodeToPNG()`
的输出即属此列）。其余格式（16 位、调色板、隔行 Adam7 等）会**明确报错**，不做猜测。

用法
----
    python Tools/visual_diff.py <基线目录> <当前目录> [选项]

    python Tools/visual_diff.py Docs/screenshots/baseline Docs/screenshots
    python Tools/visual_diff.py Docs/screenshots/baseline Docs/screenshots --tolerance 8
    python Tools/visual_diff.py Docs/screenshots/baseline Docs/screenshots \
        --roi 320,180,640,360 --exclude-roi 0,600,1280,120 --max-diff-ratio 0.01
    python Tools/visual_diff.py Docs/screenshots/baseline Docs/screenshots --json _verify_out/visual_diff.json

选项
----
    --tolerance N          逐像素容差（0-255，默认 6）：比较 RGB 三通道的**平均绝对差**，
                           超过该值即判定该像素「超差」。
    --roi x,y,w,h          只比较该矩形区域，可重复指定（多个 ROI 取并集）。
    --exclude-roi x,y,w,h  排除该矩形区域，可重复指定（在 ROI 之后生效）。
    --max-diff-ratio R     允许的超差像素比例（0-1，默认 0.02）；超差比例 ≤ R 即通过。
    --json PATH            把机器可读结果写入该文件。
    --max-images N         只比较排序后的前 N 张（调试用，默认 0 = 全部）。
    --quiet                不打印逐张明细（仍打印汇总）。

坐标约定
--------
ROI 使用**图像坐标**：原点在左上角，x 向右、y 向下（与 PNG 存储一致）。

退出码
------
    0 = 全部通过
    1 = 存在超差图片
    2 = 环境问题（基线/当前目录缺失、同名文件缺失、PNG 格式不支持或解码失败、参数非法）
        —— 「同时存在超差与环境问题」时以 2 优先（环境问题更需要人先处理）。

"""

import argparse
import json
import os
import struct
import sys
import zlib

# Windows 控制台默认可能不是 UTF-8，中文输出会报 UnicodeEncodeError
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"

EXIT_OK = 0
EXIT_DIFF = 1
EXIT_ENV = 2


class PngError(Exception):
    """PNG 解码失败或不支持的格式。"""


class RectError(Exception):
    """ROI 参数非法。"""


# ==================== PNG 解码（纯标准库） ====================


def read_png(path):
    """解码 8 位 RGB / RGBA、非隔行 PNG，返回 (width, height, channels, pixels: bytes)。"""
    try:
        with open(path, "rb") as fh:
            data = fh.read()
    except OSError as exc:
        raise PngError("无法读取文件：%s" % exc)

    if data[:8] != PNG_SIGNATURE:
        raise PngError("不是 PNG 文件（文件签名不匹配）")

    pos = 8
    idat = bytearray()
    width = 0
    height = 0
    bit_depth = None
    color_type = None
    interlace = None

    while pos + 8 <= len(data):
        (length,) = struct.unpack(">I", data[pos:pos + 4])
        ctype = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        pos += 12 + length

        if ctype == b"IHDR":
            if length < 13:
                raise PngError("IHDR 块长度异常（%d）" % length)
            (width, height, bit_depth, color_type,
             _compression, _filter, interlace) = struct.unpack(">IIBBBBB", body[:13])
        elif ctype == b"IDAT":
            idat += body
        elif ctype == b"IEND":
            break

    if bit_depth is None:
        raise PngError("缺少 IHDR 块，不是合法 PNG")

    if bit_depth != 8:
        raise PngError("只支持 8 位深度 PNG（本图 bit depth=%s）" % bit_depth)
    if interlace != 0:
        raise PngError("不支持隔行（Adam7）PNG（只支持非隔行）")
    if color_type == 2:
        channels = 3
    elif color_type == 6:
        channels = 4
    else:
        raise PngError(
            "只支持 8 位 RGB(color type=2) / RGBA(color type=6) PNG（本图 color type=%s）"
            % color_type)

    if width <= 0 or height <= 0:
        raise PngError("图像尺寸非法：%dx%d" % (width, height))
    if not idat:
        raise PngError("缺少 IDAT 数据块")

    try:
        raw = zlib.decompress(bytes(idat))
    except zlib.error as exc:
        raise PngError("IDAT zlib 解压失败：%s" % exc)

    stride = width * channels
    expected = (stride + 1) * height
    if len(raw) < expected:
        raise PngError("IDAT 数据长度不足：%d < %d" % (len(raw), expected))

    out = bytearray(stride * height)
    prev = bytearray(stride)
    src = 0
    out_view = out
    for y in range(height):
        ftype = raw[src]
        src += 1
        line = bytearray(raw[src:src + stride])
        src += stride

        if ftype == 0:
            pass
        elif ftype == 1:  # Sub
            for i in range(channels, stride):
                line[i] = (line[i] + line[i - channels]) & 0xFF
        elif ftype == 2:  # Up
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ftype == 3:  # Average
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif ftype == 4:  # Paeth
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                b = prev[i]
                c = prev[i - channels] if i >= channels else 0
                p = a + b - c
                pa = p - a
                if pa < 0:
                    pa = -pa
                pb = p - b
                if pb < 0:
                    pb = -pb
                pc = p - c
                if pc < 0:
                    pc = -pc
                if pa <= pb and pa <= pc:
                    pr = a
                elif pb <= pc:
                    pr = b
                else:
                    pr = c
                line[i] = (line[i] + pr) & 0xFF
        else:
            raise PngError("未知行过滤类型 %d（第 %d 行）" % (ftype, y))

        base = y * stride
        out_view[base:base + stride] = line
        prev = line

    return width, height, channels, bytes(out)


# ==================== ROI ====================


def parse_rect(text, what):
    parts = str(text).split(",")
    if len(parts) != 4:
        raise RectError("%s 需要 4 个整数（x,y,w,h），实际为 %r" % (what, text))
    try:
        values = [int(p.strip()) for p in parts]
    except ValueError:
        raise RectError("%s 含非整数：%r" % (what, text))
    x, y, w, h = values
    if w <= 0 or h <= 0:
        raise RectError("%s 的 w/h 必须为正：%r" % (what, text))
    return x, y, w, h


def _fill(mask, width, height, rect, value):
    x, y, w, h = rect
    x0 = max(0, x)
    y0 = max(0, y)
    x1 = min(width, x + w)
    y1 = min(height, y + h)
    if x1 <= x0 or y1 <= y0:
        return
    row = bytes([value]) * (x1 - x0)
    for yy in range(y0, y1):
        base = yy * width
        mask[base + x0:base + x1] = row


def build_mask(width, height, rois, excludes):
    """0 = 不比较，1 = 比较。ROI 先取并集，再扣除 exclude。"""
    mask = bytearray(width * height)
    if rois:
        for rect in rois:
            _fill(mask, width, height, rect, 1)
    else:
        mask[:] = b"\x01" * (width * height)
    for rect in excludes:
        _fill(mask, width, height, rect, 0)
    return mask


# ==================== 比较 ====================


def compare_pair(baseline_path, actual_path, tolerance, rois, excludes, max_diff_ratio):
    """比较两张 PNG，返回结果 dict（不抛异常，异常转成 status='error'）。"""
    result = {
        "name": os.path.basename(baseline_path),
        "baseline": baseline_path,
        "actual": actual_path,
        "status": "pass",
        "diff_ratio": 0.0,
        "mean_abs_diff": 0.0,
        "over_threshold_pixels": 0,
        "roi_pixels": 0,
        "diff_bbox": None,
        "width": None,
        "height": None,
        "message": "",
    }

    if not os.path.isfile(actual_path):
        result["status"] = "error"
        result["message"] = "当前目录缺少同名截图：%s" % actual_path
        return result

    try:
        wa, ha, ca, da = read_png(baseline_path)
        result["width"] = wa
        result["height"] = ha
    except PngError as exc:
        result["status"] = "error"
        result["message"] = "基线图解码失败：%s" % exc
        return result

    try:
        wb, hb, cb, db = read_png(actual_path)
    except PngError as exc:
        result["status"] = "error"
        result["message"] = "当前截图解码失败：%s" % exc
        return result

    if (wa, ha) != (wb, hb):
        result["status"] = "fail"
        result["diff_ratio"] = 1.0
        result["message"] = "尺寸不一致：基线 %dx%d，当前 %dx%d（窗口尺寸变了？）" % (wa, ha, wb, hb)
        return result

    mask = build_mask(wa, ha, rois, excludes)
    stride_a = wa * ca
    stride_b = wb * cb
    tol_sum = tolerance * 3.0

    over = 0
    total = 0
    sum_abs = 0
    min_x = wa
    min_y = ha
    max_x = -1
    max_y = -1

    for y in range(ha):
        row_base = y * wa
        row = mask[row_base:row_base + wa]
        if not any(row):
            continue
        base_a = y * stride_a
        base_b = y * stride_b
        for x in range(wa):
            if not row[x]:
                continue
            ia = base_a + x * ca
            ib = base_b + x * cb
            d = abs(da[ia] - db[ib]) + abs(da[ia + 1] - db[ib + 1]) + abs(da[ia + 2] - db[ib + 2])
            total += 1
            sum_abs += d
            if d > tol_sum:
                over += 1
                if x < min_x:
                    min_x = x
                if x > max_x:
                    max_x = x
                if y < min_y:
                    min_y = y
                if y > max_y:
                    max_y = y

    if total == 0:
        result["status"] = "error"
        result["message"] = "ROI 与排除区域过滤后没有任何像素参与比较（检查 --roi / --exclude-roi）"
        return result

    result["roi_pixels"] = total
    result["over_threshold_pixels"] = over
    result["diff_ratio"] = over / float(total)
    result["mean_abs_diff"] = sum_abs / float(total * 3)
    if max_x >= 0:
        result["diff_bbox"] = [min_x, min_y, max_x - min_x + 1, max_y - min_y + 1]

    if result["diff_ratio"] <= max_diff_ratio:
        result["status"] = "pass"
    else:
        result["status"] = "fail"
        bbox = result["diff_bbox"]
        result["message"] = "超差像素比例 %.4f%% > 允许 %.4f%%（超差区域 bbox=%s）" % (
            result["diff_ratio"] * 100.0, max_diff_ratio * 100.0,
            ("%d,%d,%d,%d" % tuple(bbox)) if bbox else "无")

    return result


# ==================== 主流程 ====================


def _print_result(r, quiet):
    if r["status"] == "pass":
        if not quiet:
            print("  [PASS] %-28s diff_ratio=%.4f%% mean_abs_diff=%.2f"
                  % (r["name"], r["diff_ratio"] * 100.0, r["mean_abs_diff"]))
    elif r["status"] == "fail":
        print("  [FAIL] %-28s diff_ratio=%.4f%% mean_abs_diff=%.2f  %s"
              % (r["name"], r["diff_ratio"] * 100.0, r["mean_abs_diff"], r["message"]))
    else:
        print("  [ERR ] %-28s %s" % (r["name"], r["message"]))


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="视觉基线像素比较（纯标准库；像素差异只作辅助，不能替代人工看图）")
    parser.add_argument("baseline_dir", help="基线目录（通常 Docs/screenshots/baseline）")
    parser.add_argument("actual_dir", help="当前截图目录（通常 Docs/screenshots）")
    parser.add_argument("--tolerance", type=float, default=6.0,
                        help="逐像素容差 0-255（RGB 通道平均绝对差，默认 6）")
    parser.add_argument("--roi", action="append", default=[],
                        help="只比较该区域 x,y,w,h（可重复）")
    parser.add_argument("--exclude-roi", action="append", default=[],
                        help="排除该区域 x,y,w,h（可重复）")
    parser.add_argument("--max-diff-ratio", type=float, default=0.02,
                        help="允许的超差像素比例 0-1（默认 0.02）")
    parser.add_argument("--json", default=None, help="把机器可读结果写入该文件")
    parser.add_argument("--max-images", type=int, default=0, help="只比较前 N 张（0 = 全部）")
    parser.add_argument("--quiet", action="store_true", help="不打印逐张明细")
    args = parser.parse_args(argv)

    baseline_dir = os.path.abspath(args.baseline_dir)
    actual_dir = os.path.abspath(args.actual_dir)

    if not 0.0 <= args.tolerance <= 255.0:
        print("[错误] --tolerance 必须在 0-255 之间，实际为 %s" % args.tolerance)
        return EXIT_ENV
    if not 0.0 <= args.max_diff_ratio <= 1.0:
        print("[错误] --max-diff-ratio 必须在 0-1 之间，实际为 %s" % args.max_diff_ratio)
        return EXIT_ENV

    if not os.path.isdir(baseline_dir):
        print("[环境问题] 基线目录不存在：%s" % baseline_dir)
        print("           请先（在有 GPU 的机器上，且不要加 -nographics）运行 CaptureLessonViews 生成截图，")
        print("           人工核对无误后再复制到该目录建立基线。")
        return EXIT_ENV
    if not os.path.isdir(actual_dir):
        print("[环境问题] 当前截图目录不存在：%s" % actual_dir)
        print("           请先运行 CaptureLessonViews 生成当前截图。")
        return EXIT_ENV

    try:
        rois = [parse_rect(t, "--roi") for t in args.roi]
        excludes = [parse_rect(t, "--exclude-roi") for t in args.exclude_roi]
    except RectError as exc:
        print("[错误] %s" % exc)
        return EXIT_ENV

    names = sorted(n for n in os.listdir(baseline_dir) if n.lower().endswith(".png"))
    if not names:
        print("[环境问题] 基线目录里没有任何 PNG：%s" % baseline_dir)
        return EXIT_ENV
    if args.max_images and args.max_images > 0:
        names = names[:args.max_images]

    print("=" * 68)
    print("视觉基线像素比较（方案 11.2 / 审查 6.1）")
    print("=" * 68)
    print("基线目录 : %s" % baseline_dir)
    print("当前目录 : %s" % actual_dir)
    print("容差     : %g / 255（RGB 通道平均绝对差）" % args.tolerance)
    print("允许超差 : %.4f%% 像素" % (args.max_diff_ratio * 100.0))
    print("关注区域 : %s" % (", ".join(args.roi) if args.roi else "全图"))
    if args.exclude_roi:
        print("排除区域 : %s" % ", ".join(args.exclude_roi))
    print("比较张数 : %d" % len(names))
    print("-" * 68)

    results = []
    for name in names:
        r = compare_pair(os.path.join(baseline_dir, name),
                         os.path.join(actual_dir, name),
                         args.tolerance, rois, excludes, args.max_diff_ratio)
        results.append(r)
        _print_result(r, args.quiet)

    passed = sum(1 for r in results if r["status"] == "pass")
    failed = [r for r in results if r["status"] == "fail"]
    errors = [r for r in results if r["status"] == "error"]

    print("-" * 68)
    print("汇总：共 %d 张，通过 %d，超差 %d，环境/格式问题 %d"
          % (len(results), passed, len(failed), len(errors)))
    for r in failed:
        print("  超差：%s  diff_ratio=%.4f%%  允许 %.4f%%  %s"
              % (r["name"], r["diff_ratio"] * 100.0, args.max_diff_ratio * 100.0, r["message"]))
    for r in errors:
        print("  问题：%s  %s" % (r["name"], r["message"]))

    payload = {
        "baseline_dir": baseline_dir,
        "actual_dir": actual_dir,
        "tolerance": args.tolerance,
        "max_diff_ratio": args.max_diff_ratio,
        "rois": args.roi,
        "exclude_rois": args.exclude_roi,
        "images": results,
        "summary": {
            "total": len(results),
            "passed": passed,
            "failed": len(failed),
            "errors": len(errors),
            "passed_all": len(failed) == 0 and len(errors) == 0,
        },
    }

    if args.json:
        try:
            json_path = os.path.abspath(args.json)
            parent = os.path.dirname(json_path)
            if parent and not os.path.isdir(parent):
                os.makedirs(parent, exist_ok=True)
            with open(json_path, "w", encoding="utf-8") as fh:
                json.dump(payload, fh, ensure_ascii=False, indent=2)
            print("JSON 结果：%s" % json_path)
        except OSError as exc:
            print("[环境问题] 无法写入 JSON 结果：%s" % exc)
            return EXIT_ENV

    print("=" * 68)
    print("提醒：像素差异只作辅助，不能替代人工看图（方案 11.2 / 审查 6.1）。")
    print("=" * 68)

    if errors:
        return EXIT_ENV
    if failed:
        return EXIT_DIFF
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
