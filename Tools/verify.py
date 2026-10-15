#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
钢琴 3D 教学 APP（Piano Teacher 3D）— 一键验证脚本

用途
----
在命令行完成「编译 → EditMode 测试 → PlayMode 测试 →（可选）视觉基线截图」，
并输出结果汇总。无需打开 Unity 编辑器。

依赖
----
1. Python 3.8+（本机已带，无需 pip 安装任何第三方包）
2. Unity Editor 2022.3.22f1c1（本机默认路径 D:\\Unity\\Unity 2022.3.22f1c1\\Editor\\Unity.exe；
   也可通过环境变量 UNITY_PATH 指定）

用法
----
    python Tools/verify.py                # 编译 + EditMode + PlayMode（无 GPU 要求）
    python Tools/verify.py --visual       # 追加视觉基线截图（需要 GPU，勿加 -nographics）
    python Tools/verify.py --edit-only    # 只跑 EditMode（最快）
    python Tools/verify.py --timeout 600  # 单个阶段超时（秒，默认 420）

退出码
------
    0 = 全部通过；1 = 有失败；2 = 环境问题（找不到 Unity / 项目）
"""

import argparse
import os
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

# Windows 控制台默认可能不是 UTF-8，中文输出会报 UnicodeEncodeError
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent

UNITY_CANDIDATES = [
    os.environ.get("UNITY_PATH", ""),
    r"D:\Unity\Unity 2022.3.22f1c1\Editor\Unity.exe",
    r"C:\Program Files\Unity\Hub\Editor\2022.3.22f1c1\Editor\Unity.exe",
    r"C:\Program Files\Unity\Editor\Unity.exe",
]

OUT_DIR = PROJECT_ROOT / "_verify_out"


def find_unity():
    for candidate in UNITY_CANDIDATES:
        if candidate and Path(candidate).is_file():
            return Path(candidate)
    return None


def run_unity(unity, extra_args, log_name, timeout_s, expect_xml: Path = None):
    """执行一次 Unity 批处理。返回 (状态字符串, 退出码)。

    已知行为：Unity 在 -runTests 后有时会卡在退出阶段（不返回），
    此时若结果 XML 已生成，即视为本次运行成功完成。
    """
    log_path = OUT_DIR / f"{log_name}.log"
    if expect_xml and expect_xml.exists():
        expect_xml.unlink()

    cmd = [str(unity), "-batchmode", "-nographics",
           "-projectPath", str(PROJECT_ROOT)]
    cmd += extra_args
    if expect_xml:
        cmd += ["-testResults", str(expect_xml)]
    cmd += ["-logFile", str(log_path)]

    print(f"\n$ {' '.join(cmd[:6])} ...")
    started = time.time()

    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    deadline = started + timeout_s
    status = "timeout"

    # 轮询：Unity 在 -runTests 后常在退出阶段挂住，但只要结果 XML 已生成即可继续，
    # 无需白等到超时。
    while time.time() < deadline:
        code = proc.poll()
        if code is not None:
            status = "completed" if code == 0 else f"exit={code}"
            break
        if expect_xml is not None and expect_xml.exists():
            time.sleep(2)                 # 给 Unity 一点自然退出的时间
            if proc.poll() is None:
                proc.kill()
            status = "completed"
            break
        time.sleep(2)
    else:
        try:
            proc.kill()
        except Exception:
            pass

    elapsed = time.time() - started
    print(f"  用时 {elapsed:0.1f}s → {status}")
    return status, log_path


def parse_results(xml_path: Path):
    """解析 Unity Test Framework 的 NUnit XML 结果。"""
    if not xml_path.exists():
        return None
    root = ET.parse(xml_path).getroot()
    failed = [tc.get("name") for tc in root.iter("test-case")
              if tc.get("result") not in ("Passed", "Skipped")]
    skipped = [tc.get("name") for tc in root.iter("test-case")
               if tc.get("result") == "Skipped"]
    return {
        "total": int(root.get("total", 0)),
        "passed": int(root.get("passed", 0)),
        "failed": int(root.get("failed", 0)),
        "skipped": int(root.get("skipped", 0)),
        "failed_names": failed,
        "skipped_names": skipped,
    }


def grep_compile_errors(log_path: Path):
    if not log_path.exists():
        return []
    errors = set()
    with log_path.open("r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if "error CS" in line:
                errors.add(line.strip())
    return sorted(errors)


def print_summary(title, result):
    if result is None:
        print(f"[{title}] 未生成结果文件（可能编译失败或超时）")
        return False
    ok = result["failed"] == 0
    flag = "通过" if ok else "失败"
    print(f"[{title}] {flag} — 总计 {result['total']}，通过 {result['passed']}，"
          f"失败 {result['failed']}，跳过 {result['skipped']}")
    for name in result["failed_names"]:
        print(f"    FAIL: {name}")
    if result["skipped_names"]:
        for name in result["skipped_names"]:
            print(f"    SKIP: {name}")
    return ok


def main():
    parser = argparse.ArgumentParser(description="Piano Teacher 3D 一键验证")
    parser.add_argument("--visual", action="store_true", help="追加视觉基线截图（需要 GPU）")
    parser.add_argument("--edit-only", action="store_true", help="只跑 EditMode")
    parser.add_argument("--timeout", type=int, default=420, help="单个阶段超时秒数")
    args = parser.parse_args()

    print("=" * 68)
    print("钢琴 3D 教学 APP — 一键验证")
    print(f"项目路径: {PROJECT_ROOT}")
    print("=" * 68)

    if not (PROJECT_ROOT / "Assets" / "Scripts").is_dir():
        print("✗ 未找到 Assets/Scripts —— 请把本脚本放在项目的 Tools/ 目录下运行")
        return 2

    unity = find_unity()
    if unity is None:
        print("✗ 未找到 Unity Editor。请安装 Unity 2022.3.22f1c1，"
              "或设置环境变量 UNITY_PATH 指向 Unity.exe")
        return 2
    print(f"Unity: {unity}")

    OUT_DIR.mkdir(exist_ok=True)
    results = {}

    # ---- 1. 编译检查 ----
    print("\n" + "-" * 68)
    print("[1/3] 编译检查")
    status, log = run_unity(unity, ["-quit"], "compile", args.timeout)
    errors = grep_compile_errors(log)
    if errors:
        print(f"✗ 编译失败，共 {len(errors)} 条错误：")
        for e in errors[:10]:
            print(f"    {e}")
        return 1
    if status != "completed":
        print(f"✗ 编译阶段异常：{status}（详见 {log}）")
        return 1
    print("✓ 编译通过（0 错误）")

    # ---- 2. EditMode ----
    print("\n" + "-" * 68)
    print("[2/3] EditMode 测试（纯逻辑：MIDI 解析 / 时间轴 / 分析器 / 姿态校验）")
    xml = OUT_DIR / "editmode.xml"
    run_unity(unity, ["-runTests", "-testPlatform", "EditMode"], "editmode", args.timeout, xml)
    results["EditMode"] = parse_results(xml)

    # ---- 3. PlayMode ----
    if not args.edit_only:
        print("\n" + "-" * 68)
        print("[3/3] PlayMode 测试（运行时：时间轴状态机 / 视角与角色切换 / 教学功能 / 动作质量）")
        xml = OUT_DIR / "playmode.xml"
        run_unity(unity, ["-runTests", "-testPlatform", "PlayMode"], "playmode", args.timeout, xml)
        results["PlayMode"] = parse_results(xml)

    # ---- 4. 视觉快照（可选）----
    if args.visual:
        print("\n" + "-" * 68)
        print("[附加] 视觉基线截图（需要 GPU，不加 -nographics）")
        xml = OUT_DIR / "visual.xml"
        run_unity(unity, ["-runTests", "-testPlatform", "PlayMode",
                         "-testFilter", "CaptureLessonViews"], "visual", args.timeout * 2, xml)
        shots = PROJECT_ROOT / "Docs" / "screenshots"
        if shots.is_dir():
            names = sorted(p.name for p in shots.glob("*.png"))
            print(f"  已生成 {len(names)} 张截图: {', '.join(names)}")

    # ---- 汇总 ----
    print("\n" + "=" * 68)
    print("结果汇总")
    print("=" * 68)
    all_ok = True
    for title, result in results.items():
        all_ok = print_summary(title, result) and all_ok

    print("\n" + ("✓ 全部通过" if all_ok else "✗ 存在失败项，详见上方 FAIL 列表"))
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
