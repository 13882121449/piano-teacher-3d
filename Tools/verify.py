#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
钢琴 3D 教学 APP（Piano Teacher 3D）— 一键验证脚本

用途
----
在命令行完成「编译 → EditMode 测试 → PlayMode 测试 →（可选）压力 / 长跑 / 性能 / 视觉基线」，
并输出统一结果汇总与产物清单。无需打开 Unity 编辑器。

依赖
----
1. Python 3.8+（本机已带，无需 pip 安装任何第三方包 —— 纯标准库）
2. Unity Editor 2022.3.22f1c1（本机默认路径 D:\\Unity\\Unity 2022.3.22f1c1\\Editor\\Unity.exe；
   也可通过环境变量 UNITY_PATH 指定）

用法
----
    python Tools/verify.py                    # 编译 + EditMode + PlayMode（无 GPU 要求）
    python Tools/verify.py --visual           # 追加视觉基线截图（需要 GPU，勿加 -nographics）
    python Tools/verify.py --edit-only        # 只跑 EditMode（最快）
    python Tools/verify.py --timeout 600      # 单个阶段超时（秒，默认 420）

    python Tools/verify.py --stress           # 追加压力测试（-testCategory Stress）
    python Tools/verify.py --perf             # 追加性能采样（-testCategory Perf）
    python Tools/verify.py --player           # 追加真实 Windows Player 基线（Development Build；需要图形设备）
    python Tools/verify.py --player --player-seconds 30
                                              # 每个场景采样 30 秒
    python Tools/verify.py --soak             # 追加长跑稳定性（-testCategory Soak，默认 10 分钟）
    python Tools/verify.py --soak --soak-seconds 120
                                              # 长跑时长改为 120 秒（环境变量 PT3D_SOAK_SECONDS）
    python Tools/verify.py --compare-visual   # 追加视觉基线比较（-testFilter CompareAgainstBaseline，需 GPU）
    python Tools/verify.py --all              # = --visual --stress --perf（不含 --soak / --compare-visual）

关于分类与 [Explicit]
---------------------
Soak / Perf 用例标注了 [NUnit.Framework.Explicit]，默认「全部测试」不会执行它们；
Unity Test Framework 的官方约定是：**选中其所属 Category 时即会运行**，
因此这里统一用 -testCategory 选择（对应方案 11.4 的稳定性与性能采样要求）。

退出码
------
    0 = 全部通过（含「如实跳过」的阶段）；1 = 有失败；2 = 环境问题（找不到 Unity / 项目）
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

# 长跑默认时长（秒）：与 StressAndStabilityTests 内的默认值保持一致（方案 11.4：10 分钟以上）
SOAK_DEFAULT_SECONDS = 600


def find_unity():
    for candidate in UNITY_CANDIDATES:
        if candidate and Path(candidate).is_file():
            return Path(candidate)
    return None


def run_unity(unity, extra_args, log_name, timeout_s, expect_xml: Path = None,
              nographics: bool = True, extra_env: dict = None):
    """执行一次 Unity 批处理。返回 (状态字符串, 日志路径)。

    已知行为：Unity 在 -runTests 后有时会卡在退出阶段（不返回），
    此时若结果 XML 已生成，即视为本次运行成功完成。

    nographics=False 用于 --visual / --compare-visual 阶段（需要真实 GPU 渲染）。
    extra_env 里的键值会叠加到当前进程环境后传给 Unity（例如 PT3D_SOAK_SECONDS）。
    """
    log_path = OUT_DIR / f"{log_name}.log"
    if expect_xml and expect_xml.exists():
        expect_xml.unlink()

    cmd = [str(unity), "-batchmode"]
    if nographics:
        cmd += ["-nographics"]
    cmd += ["-projectPath", str(PROJECT_ROOT)]
    cmd += extra_args
    if expect_xml:
        cmd += ["-testResults", str(expect_xml)]
    cmd += ["-logFile", str(log_path)]

    env = None
    if extra_env:
        env = dict(os.environ)
        env.update({k: str(v) for k, v in extra_env.items()})

    print(f"\n$ {' '.join(cmd[:6])} ...")
    started = time.time()

    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, env=env)
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


def report_stage_special(title, result):
    """补充说明：0 个用例 / 全部跳过时如实报告，而不是当成成功（方案 0 节诚实性要求）。"""
    if result is None:
        return
    if result["total"] == 0:
        print(f"  ⚠ [{title}] 未匹配到任何用例 —— 请确认该分类下的测试已编译"
              f"（[Explicit] 用例需按 Category 选择运行）")
    elif result["passed"] == 0 and result["skipped"] > 0:
        print(f"  ⚠ [{title}] 本次全部用例已跳过（{result['skipped']} 个），并未真正执行；"
              f"不代表已通过，请人工确认（例如缺少视觉基线或缺少示例曲目）")


# ==================== 真实 Windows Player 基线（P0-01 / §7-1 / §7-4） ====================

PLAYER_DIR = OUT_DIR / "player"
PLAYER_EXE = PLAYER_DIR / "PianoTeacher3D.exe"
PLAYER_BUILD_METHOD = "PianoTeacher3D.EditorTools.PlayerBaselineBuild.Build"


def build_player(unity, timeout_s):
    """构建 Development Build（只含 PianoLesson 场景）。返回 (成功?, 说明)。"""
    if PLAYER_EXE.exists():
        try:
            PLAYER_EXE.unlink()
        except OSError:
            pass

    log_path = OUT_DIR / "player_build.log"
    cmd = [str(unity), "-batchmode", "-quit", "-projectPath", str(PROJECT_ROOT),
           "-executeMethod", PLAYER_BUILD_METHOD, "-logFile", str(log_path)]

    env = dict(os.environ)
    env["PT3D_PLAYER_OUT"] = str(PLAYER_EXE)

    print(f"\n$ Unity -executeMethod {PLAYER_BUILD_METHOD} ...")
    started = time.time()
    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, env=env)
    status = "timeout"
    try:
        proc.wait(timeout=timeout_s * 3)
        status = "completed" if proc.returncode == 0 else f"exit={proc.returncode}"
    except subprocess.TimeoutExpired:
        proc.kill()
    print(f"  用时 {time.time() - started:0.1f}s → {status}")

    if not PLAYER_EXE.exists():
        detail = ""
        if log_path.exists():
            text = log_path.read_text(encoding="utf-8", errors="replace")
            for line in text.splitlines():
                if "error CS" in line or "[PT3D][Build]" in line:
                    detail += "    " + line.strip() + "\n"
        return False, f"未生成可执行文件（{status}）\n{detail}".rstrip()

    return True, "构建成功"


def run_player(exe, seconds, scenario, timeout_s):
    """运行 Player 采样并返回解析后的 JSON（失败返回 None + 原因）。"""
    out_json = OUT_DIR / f"player_{scenario}.json"
    if out_json.exists():
        try:
            out_json.unlink()
        except OSError:
            pass

    log_path = OUT_DIR / f"player_{scenario}.log"
    cmd = [str(exe),
           "-pt3dPerf", str(seconds),
           "-pt3dPerfScenario", scenario,
           "-pt3dPerfOut", str(out_json),
           "-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0",
           "-logFile", str(log_path)]

    print(f"\n$ {exe.name} -pt3dPerf {seconds} -pt3dPerfScenario {scenario} ...")
    started = time.time()
    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    try:
        proc.wait(timeout=timeout_s)
        status = "completed" if proc.returncode == 0 else f"exit={proc.returncode}"
    except subprocess.TimeoutExpired:
        proc.kill()
        status = "timeout"
    print(f"  用时 {time.time() - started:0.1f}s → {status}")

    if not out_json.exists():
        return None, f"Player 未输出采样结果（{status}），详见 {log_path.name}"

    try:
        import json
        return json.loads(out_json.read_text(encoding="utf-8")), None
    except (OSError, ValueError) as exc:
        return None, f"采样结果无法解析：{exc.__class__.__name__}"


def upsert_section(path: Path, section_id: str, body: str, preamble: str = "") -> bool:
    """与 Unity 侧 PerfReportWriter 相同的分节写入：只替换自己的小节。"""
    begin = f"<!-- SECTION:{section_id}:BEGIN -->"
    end = f"<!-- SECTION:{section_id}:END -->"
    block = f"{begin}\n{body.strip()}\n{end}"

    try:
        existing = path.read_text(encoding="utf-8") if path.exists() else preamble
    except OSError:
        return False

    b = existing.find(begin)
    e = existing.find(end)
    if b >= 0 and e > b:
        updated = existing[:b] + block + existing[e + len(end):]
    else:
        updated = existing.rstrip() + "\n\n" + block + "\n"

    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(updated, encoding="utf-8")
        return True
    except OSError:
        return False


def player_section_markdown(data, scenario):
    lines = [
        f"### 真实 Windows Player 基线（scenario = {scenario}）",
        "",
        f"- 机器：{data.get('processor', '?')}（{data.get('processorCount', 0)} 线程 / "
        f"{data.get('systemMemoryMb', 0)} MB）",
        f"- 系统：{data.get('operatingSystem', '?')}",
        f"- GPU / 图形 API：{data.get('graphicsDevice', '?')} / {data.get('graphicsApi', '?')}",
        f"- 分辨率 / VSync / targetFrameRate：{data.get('screenWidth')}×{data.get('screenHeight')}"
        f" / {data.get('vSyncCount')} / {data.get('targetFrameRate')}",
        f"- Unity / 画质档：{data.get('unityVersion', '?')} / {data.get('qualityTier', '?')}",
        f"- 采样窗口：预热 {data.get('warmupSeconds')} s + 采样 {data.get('sampleSeconds')} s"
        f"（{data.get('frameCount')} 帧）",
        "",
        "| 指标 | 数值 |",
        "|---|---|",
        f"| 平均帧时间 | {data.get('meanMs', 0):.2f} ms |",
        f"| p50 帧时间 | {data.get('p50Ms', 0):.2f} ms |",
        f"| p95 帧时间 | {data.get('p95Ms', 0):.2f} ms |",
        f"| p99 帧时间 | {data.get('p99Ms', 0):.2f} ms |",
        f"| 最大帧时间 | {data.get('maxMs', 0):.2f} ms |",
        f"| 平均 FPS | {data.get('avgFps', 0):.1f} |",
        f"| 1% low FPS | {data.get('low1Fps', 0):.1f} |",
        f"| 托管内存（GC.GetTotalMemory） | {data.get('gcManagedMbStart', 0):.1f} → "
        f"{data.get('gcManagedMbEnd', 0):.1f} MB（增量 {data.get('gcAllocMb', 0):+.2f} MB） |",
        f"| Mono 已用 / 总分配 | {data.get('monoUsedMb', 0):.1f} / {data.get('totalAllocatedMb', 0):.1f} MB |",
        f"| Mesh / Material / AudioClip / GameObject | {data.get('meshCount', 0)} / "
        f"{data.get('materialCount', 0)} / {data.get('audioClipCount', 0)} / {data.get('gameObjectCount', 0)} |",
        "",
        "**口径说明**",
        "",
        "- 本表来自 **Development Build 的独立 Player**（真实图形设备），不是 Editor 批处理数据。",
        "- 帧时间取 `Time.unscaledDeltaTime`，逐帧采样；p50/p95/p99 取排序后的最近秩。",
        "- 1% low FPS = 最慢 1% 帧的平均帧时间的倒数。",
        "- 资源计数为 `Resources.FindObjectsOfTypeAll` 的运行时总数（含内置对象），只用于同机版本间趋势对比。",
        f"- 结论边界：{data.get('note', '')}",
    ]
    return "\n".join(lines)


def run_player_stage(unity, seconds, scenarios, timeout_s):
    """构建 + 采样 + 归档。返回 (是否可用, 摘要行列表)。"""
    ok, message = build_player(unity, timeout_s)
    if not ok:
        print(f"  ✗ Player 构建失败：{message}")
        return False, [f"Player 构建失败：{message}"]

    print(f"  ✓ {message} → {PLAYER_EXE}")

    summary = []
    any_data = False
    for scenario in scenarios:
        data, err = run_player(PLAYER_EXE, seconds, scenario, seconds + 180)
        if data is None:
            print(f"  ⚠ scenario={scenario} 未取得数据：{err}")
            summary.append(f"{scenario}: 未取得数据（{err}）")
            continue

        any_data = True
        print(f"  ✓ scenario={scenario}：p95 {data.get('p95Ms', 0):.2f} ms、"
              f"p99 {data.get('p99Ms', 0):.2f} ms、1% low {data.get('low1Fps', 0):.1f} FPS、"
              f"GC 增量 {data.get('gcAllocMb', 0):+.2f} MB")
        upsert_section(PROJECT_ROOT / "Docs" / "perf" / "sampling.md",
                       f"player-{scenario}",
                       player_section_markdown(data, scenario))
        summary.append(f"{scenario}: p95 {data.get('p95Ms', 0):.2f} ms / "
                       f"p99 {data.get('p99Ms', 0):.2f} ms / 1% low {data.get('low1Fps', 0):.1f} FPS")

    if any_data:
        env_body = "\n".join([
            "### 真实 Player 运行环境（本地自检）",
            "",
            f"- 采样时间：{time.strftime('%Y-%m-%d %H:%M:%S')}",
            f"- Player：{PLAYER_EXE.name}（Development Build，1280×720 窗口）",
            "- 场景：PianoLesson + StreamingAssets/Songs/demo.mid",
            "- 数据文件：`_verify_out/player_<scenario>.json`",
            "- 说明：本机为开发机（独显），不代表低配/集显设备；三类设备矩阵仍需分别采集。",
        ])
        upsert_section(PROJECT_ROOT / "Docs" / "perf" / "environments.md",
                       "player-runtime", env_body)

    return any_data, summary


def print_artifacts():
    print("\n" + "-" * 68)
    print("产物清单")
    print("-" * 68)

    items = []
    shots = PROJECT_ROOT / "Docs" / "screenshots"
    if shots.is_dir():
        items.extend(sorted(shots.glob("*.png")))
    items.append(PROJECT_ROOT / "Docs" / "perf" / "sampling.md")
    items.append(PROJECT_ROOT / "Docs" / "perf" / "environments.md")

    any_missing = False
    for path in items:
        rel = path.relative_to(PROJECT_ROOT)
        if path.exists():
            stat = path.stat()
            stamp = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(stat.st_mtime))
            print(f"  ✓ {rel}  （最后修改 {stamp}，{stat.st_size} 字节）")
        else:
            any_missing = True
            print(f"  ✗ {rel}  （不存在）")
    if any_missing:
        print("  提示：缺失的产物需要对应阶段真正执行后才会生成"
              "（截图见 --visual，采样与矩阵记录见 --stress / --perf / --soak）")


def main():
    parser = argparse.ArgumentParser(description="Piano Teacher 3D 一键验证")
    parser.add_argument("--visual", action="store_true",
                        help="追加视觉基线截图（需要 GPU；该阶段不加 -nographics）")
    parser.add_argument("--edit-only", action="store_true", help="只跑 EditMode")
    parser.add_argument("--timeout", type=int, default=420, help="单个阶段超时秒数（默认 420）")

    parser.add_argument("--stress", action="store_true",
                        help="追加压力与稳定性采样：-testCategory Stress（快速切换 / 资源加载 / 环境矩阵自检）")
    parser.add_argument("--soak", action="store_true",
                        help=f"追加长跑稳定性：-testCategory Soak（默认 {SOAK_DEFAULT_SECONDS} 秒，耗时长）")
    parser.add_argument("--soak-seconds", type=int, default=None, metavar="N",
                        help=f"长跑时长（秒），通过环境变量 PT3D_SOAK_SECONDS 传给 Unity（默认 {SOAK_DEFAULT_SECONDS}）")
    parser.add_argument("--perf", action="store_true",
                        help="追加性能采样：-testCategory Perf（帧时间 p50/p95/p99 与 1%% low FPS）")
    parser.add_argument("--player", action="store_true",
                        help="追加真实 Windows Player 基线：Development Build + 帧时间/GC/内存采样"
                             "（需要图形设备；构建与运行都在本机完成）")
    parser.add_argument("--player-seconds", type=float, default=20.0, metavar="N",
                        help="每个 Player 场景的采样秒数（默认 20）")
    parser.add_argument("--compare-visual", action="store_true",
                        help="追加视觉基线比较：-testFilter CompareAgainstBaseline（需要 GPU 与人工确认的基线；"
                             "无基线时用例会 Ignore，脚本按「已跳过」如实报告）")
    parser.add_argument("--all", action="store_true",
                        help="等价于 --visual --stress --perf（不含 --soak 与 --compare-visual）")
    args = parser.parse_args()

    if args.all:
        args.visual = True
        args.stress = True
        args.perf = True

    if args.soak_seconds is None:
        args.soak_seconds = SOAK_DEFAULT_SECONDS

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

    # ---- 4. 视觉快照（可选，需要 GPU）----
    if args.visual:
        print("\n" + "-" * 68)
        print("[附加] 视觉基线截图（需要 GPU，不加 -nographics）")
        xml = OUT_DIR / "visual.xml"
        run_unity(unity, ["-runTests", "-testPlatform", "PlayMode",
                          "-testFilter", "CaptureLessonViews"], "visual", args.timeout * 2, xml,
                  nographics=False)
        shots = PROJECT_ROOT / "Docs" / "screenshots"
        if shots.is_dir():
            names = sorted(p.name for p in shots.glob("*.png"))
            print(f"  已生成 {len(names)} 张截图: {', '.join(names)}")

    # ---- 5. 压力测试（可选）----
    if args.stress:
        print("\n" + "-" * 68)
        print("[附加] 压力与稳定性采样（快速切换 ≥200 次 / 资源加载采样 / 环境矩阵自检）")
        xml = OUT_DIR / "stress.xml"
        run_unity(unity, ["-runTests", "-testPlatform", "PlayMode",
                          "-testCategory", "Stress"], "stress", args.timeout * 2, xml)
        results["Stress"] = parse_results(xml)

    # ---- 6. 性能采样（可选）----
    if args.perf:
        print("\n" + "-" * 68)
        print("[附加] 性能采样（帧时间 p50/p95/p99、1% low FPS、GC 分配；写 Docs/perf/sampling.md）")
        xml = OUT_DIR / "perf.xml"
        run_unity(unity, ["-runTests", "-testPlatform", "PlayMode",
                          "-testCategory", "Perf"], "perf", args.timeout * 2, xml)
        results["Perf"] = parse_results(xml)

    # ---- 7. 真实 Windows Player 基线（可选，需要图形设备）----
    player_summary = None
    if args.player:
        print("\n" + "-" * 68)
        print("[附加] 真实 Windows Player 基线（Development Build，不含 -nographics）")
        _, player_summary = run_player_stage(
            unity, args.player_seconds, ["play", "switch"], args.timeout)

    # ---- 8. 长跑稳定性（可选，耗时长）----
    if args.soak:
        soak_timeout = max(args.timeout, args.soak_seconds + 240)
        print("\n" + "-" * 68)
        print(f"[附加] 长跑稳定性 Soak：{args.soak_seconds} 秒（PT3D_SOAK_SECONDS），"
              f"阶段超时 {soak_timeout} 秒")
        xml = OUT_DIR / "soak.xml"
        run_unity(unity, ["-runTests", "-testPlatform", "PlayMode",
                          "-testCategory", "Soak"], "soak", soak_timeout, xml,
                  extra_env={"PT3D_SOAK_SECONDS": args.soak_seconds})
        results["Soak"] = parse_results(xml)

    # ---- 8. 视觉基线比较（可选，需要 GPU 与人工确认的基线）----
    if args.compare_visual:
        print("\n" + "-" * 68)
        print("[附加] 视觉基线比较（需要 GPU，不加 -nographics；无基线时用例会 Ignore）")
        xml = OUT_DIR / "comparevisual.xml"
        run_unity(unity, ["-runTests", "-testPlatform", "PlayMode",
                          "-testFilter", "CompareAgainstBaseline"], "comparevisual",
                  args.timeout * 2, xml, nographics=False)
        results["VisualBaseline"] = parse_results(xml)

    # ---- 汇总 ----
    print("\n" + "=" * 68)
    print("结果汇总")
    print("=" * 68)
    all_ok = True
    for title, result in results.items():
        all_ok = print_summary(title, result) and all_ok
        report_stage_special(title, result)

    if player_summary is not None:
        print("[Player] 真实 Windows Player 基线：")
        if not player_summary:
            print("    未采集到任何数据（见上方构建/运行日志）")
        for line in player_summary:
            print(f"    - {line}")

    print_artifacts()

    print("\n" + ("✓ 全部通过" if all_ok else "✗ 存在失败项，详见上方 FAIL 列表"))
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
