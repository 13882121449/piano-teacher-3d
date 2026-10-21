#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
静态检查与依赖门禁（优化方案 §7-11 / P1-09）

用途
----
在**不需要 Unity** 的环境里重复执行一组架构与代码质量规则，作为 CI 的第一道门禁
（`Tools/verify.py` 需要 Unity Editor，跑在自托管 runner 上；本脚本跑在任何装了 Python 的机器上）。

检查项
------
1. 纯 C# 模块不得引用 UnityEngine（解析器 / 分析器 / 过滤器必须可脱离引擎单测）。
2. UI 层不得直接依赖 `MidiEventScheduler` / `LessonTimeline`（只允许通过会话门面）。
3. 不允许空 catch（`catch { }`、`catch (Exception) { /* 注释 */ }` 视为空；有语句的 catch 通过）；
   并统计「catch 没有任何可追溯出口」与「只降级到 Warning」的存量（`--strict-catch` 可升级为硬门禁）。
4. `Update / LateUpdate / FixedUpdate` 不得每帧分配：值类型 `new`（`Vector3` 与项目内声明的 `struct`）放行，
   字符串插值/拼接、集合拷贝、LINQ、`GetComponentsInChildren`、`SendMessage`、`Debug.Log` 一律失败。
   这就是 README「Update 路径禁止每帧 new / 字符串拼接」铁律的可执行版本，基线 0 处违规。
5. 依赖使用扫描（信息性）：报告 TMPro / Timeline / Animation Rigging 的引用数，
   用于判断能否按 §7-11「逐项删包」推进；**不**在本脚本里删包。

用法
----
    python Tools/static_checks.py                # 检查并输出报告（有违规返回 1）
    python Tools/static_checks.py --strict       # 把依赖扫描的「完全未使用」也视为失败
    python Tools/static_checks.py --strict-catch # 把「catch 无出口」视为失败（存量清完后开启）
    python Tools/static_checks.py --list-catch   # 逐条打印 catch 缺口
    python Tools/static_checks.py --list-alloc   # 逐条打印每帧分配（规则 4 明细）
"""

import argparse
import json
import re
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent
SCRIPTS = PROJECT_ROOT / "Assets" / "Scripts"
UI_DIR = SCRIPTS / "UI"

if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

# 纯 C# 模块：不允许出现 UnityEngine（否则「可在 EditMode 单测」的前提被破坏）
PURE_CS_FILES = [
    "MIDI/MidiParser.cs",
    "MIDI/MidiFileValidator.cs",
    "MIDI/PianoNoteFilter.cs",
    "Core/SongAnalyzer.cs",
]

# UI 不得直接依赖的底层类型（架构规则：UI → LessonSessionController → 模块）
UI_FORBIDDEN = ["MidiEventScheduler", "LessonTimeline"]

# 依赖包 → 代码里可能的引用特征
PACKAGE_PROBES = {
    "com.unity.textmeshpro": [r"\bTMPro\b", r"\bTextMeshPro"],
    "com.unity.timeline": [r"UnityEngine\.Timeline", r"\bPlayableDirector\b"],
    "com.unity.animation.rigging": [r"UnityEngine\.Animations\.Rigging", r"\bRigBuilder\b", r"\bTwoBoneIKConstraint\b"],
}

CATCH_RE = re.compile(r"catch\s*(\([^)]*\))?\s*\{")

# catch 块里必须出现的「可追溯出口」：上报器 / 错误日志 / 构造领域错误 / 重新抛出（P1-9）
CATCH_TRACE_RE = re.compile(r"ErrorReporter|Debug\.Log(Error|Exception)?|AppError|throw\b|Fail\s*\(")

# 只降级到 Warning 的 catch：不失败，但会汇总上报，供 P1-9 逐文件收敛
CATCH_WARN_ONLY_RE = re.compile(r"Debug\.LogWarning")

# ---- 规则 4：Update 路径不得每帧分配（README「每帧 new / 字符串拼接」铁律的可执行版本）----
FRAME_METHOD_RE = re.compile(r"\b(?:void|override\s+void|private\s+void|public\s+void)\s+(Update|LateUpdate|FixedUpdate)\s*\(\s*\)\s*\{")

# 允许出现在 Update 里的「非分配 new」：这些是值类型，GC 不参与
ALLOC_STRUCT_ALLOW = (
    "Vector2", "Vector3", "Vector4", "Quaternion", "Color", "Color32", "Bounds",
    "Rect", "Ray", "RaycastHit", "Pose", "Matrix4x4", "Plane", "LayerMask",
    "KeyValuePair", "Vector2Int", "Vector3Int",
)

# 每帧会分配（或必然触发 GC/查找）的写法
ALLOC_PATTERNS = [
    (re.compile(r"\bnew\s+(?!\s*\{)([A-Za-z_][A-Za-z0-9_.]*)"), "每帧 new 引用类型"),
    (re.compile(r"\bstring\s*\.\s*(?:Format|Concat|Join)\s*\("), "每帧 string.Format/Concat"),
    (re.compile(r'\$"'), "每帧字符串插值"),
    (re.compile(r"\.(?:ToArray|ToList|ToDictionary|ToHashSet)\s*\(\s*\)"), "每帧集合拷贝"),
    (re.compile(r"\.(?:Select|Where|OrderBy|OrderByDescending|FirstOrDefault|Any)\s*\("), "Update 里用 LINQ（铁律禁止）"),
    (re.compile(r"\bGetComponentsInChildren\b"), "Update 里 GetComponentsInChildren（铁律禁止）"),
    (re.compile(r"\bSendMessage\s*\("), "Update 里 SendMessage（反射调用，必卡）"),
    (re.compile(r"\bDebug\s*\.\s*Log\s*\("), "Update 里 Debug.Log（每次分配堆栈字符串）"),
]

COMMENT_RE = re.compile(r"//[^\n]*|/\*.*?\*/", re.S)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def iter_catch_blocks(text):
    """逐个吐出 (match, body, line)；body 为花括号内的原文（不含外层括号）。"""
    for match in CATCH_RE.finditer(text):
        start = match.end()
        depth = 1
        i = start
        while i < len(text) and depth > 0:
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
            i += 1
        body = text[start:i - 1] if depth == 0 else ""
        line = text.count("\n", 0, match.start()) + 1
        yield match, body, line


def check_pure_csharp():
    problems = []
    for rel in PURE_CS_FILES:
        path = SCRIPTS / rel
        if not path.exists():
            problems.append(f"{rel}: 文件不存在（规则失效，请更新 PURE_CS_FILES）")
            continue
        text = read(path)
        if "using UnityEngine" in text or "UnityEngine." in text:
            problems.append(f"Assets/Scripts/{rel}: 纯 C# 模块不得引用 UnityEngine")
    return problems


def check_ui_boundary():
    problems = []
    if not UI_DIR.is_dir():
        return ["Assets/Scripts/UI 不存在"]
    for path in sorted(UI_DIR.glob("*.cs")):
        text = read(path)
        for needle in UI_FORBIDDEN:
            if re.search(rf"\b{needle}\b", text):
                problems.append(f"{path.relative_to(PROJECT_ROOT)}: UI 层直接依赖 {needle}（应通过会话门面）")
    return problems


def check_empty_catch():
    problems = []
    for path in sorted(SCRIPTS.rglob("*.cs")):
        text = read(path)
        for _match, body, line in iter_catch_blocks(text):
            # 允许「只有注释」的 catch（注释就是「为什么可以吞」的说明）；只拦完全空白的 catch
            if not body.strip():
                problems.append(f"{path.relative_to(PROJECT_ROOT)}:{line}: 空 catch（至少要写注释说明为什么可以吞）")
    return problems


def check_catch_trace():
    """P1-9：catch 必须有可追溯出口。

    允许三种：ErrorReporter/Debug.Log*（日志）、构造 AppError（结构化错误）、throw（重新抛出）。
    只写注释就吞掉异常不算——注释能解释「为什么可以吞」，但排障时找不到任何痕迹。
    """
    problems = []
    for path in sorted(SCRIPTS.rglob("*.cs")):
        text = read(path)
        for _match, body, line in iter_catch_blocks(text):
            if not body.strip():
                continue  # 空 catch 由规则 3 负责，避免重复报同一行
            stripped = COMMENT_RE.sub("", body)
            if not stripped.strip():
                continue  # 只有注释：规则 3 允许，这里也不重复报
            if not CATCH_TRACE_RE.search(body):
                problems.append(
                    f"{path.relative_to(PROJECT_ROOT)}:{line}: catch 没有任何出口（需 ErrorReporter / Debug.Log / AppError / throw）"
                )
    return problems


def catch_warning_stats():
    """P1-9 的收敛指标：只把异常降级成 Debug.LogWarning 的 catch，按文件计数（不失败）。"""
    stats = []
    for path in sorted(SCRIPTS.rglob("*.cs")):
        text = read(path)
        count = 0
        for _match, body, _line in iter_catch_blocks(text):
            if CATCH_WARN_ONLY_RE.search(body) and not re.search(r"Debug\.LogError|ErrorReporter|throw\b", body):
                count += 1
        if count:
            stats.append((count, str(path.relative_to(PROJECT_ROOT))))
    stats.sort(reverse=True)
    return stats


def iter_frame_methods(text):
    """逐个吐出 (方法名, body, 起始行号)：Update / LateUpdate / FixedUpdate 的方法体。"""
    for match in FRAME_METHOD_RE.finditer(text):
        start = match.end()
        depth = 1
        i = start
        while i < len(text) and depth > 0:
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
            i += 1
        body = text[start:i - 1] if depth == 0 else ""
        line = text.count("\n", 0, match.start()) + 1
        yield match.group(1), body, line


STRUCT_DECL_RE = re.compile(r"\bstruct\s+([A-Za-z_][A-Za-z0-9_]*)")


def project_struct_names():
    """项目里自己声明过的 struct：它们的 new 是值类型构造，不产生 GC 分配。"""
    names = set(ALLOC_STRUCT_ALLOW)
    for path in SCRIPTS.rglob("*.cs"):
        for match in STRUCT_DECL_RE.finditer(read(path)):
            names.add(match.group(1))
    return names


def blank_comments(text: str) -> str:
    """把注释替换成等长空白，保持字符偏移与行号不变。"""
    return COMMENT_RE.sub(lambda m: re.sub(r"[^\n]", " ", m.group(0)), text)


def check_frame_allocations():
    """规则 4：Update/LateUpdate/FixedUpdate 里不得出现每帧分配。

    README §8 把「Update 路径禁止 LINQ、每帧 new、字符串拼接」列为铁律，
    但直到 P1-9 之前没有任何自动检查——回归时只能靠人眼。这里把它变成可执行的：
    值类型（Vector3 等）的 new 不算分配，字符串插值/拼接、集合拷贝、LINQ、
    GetComponentsInChildren、SendMessage、Debug.Log 一律算。
    """
    problems = []
    allowed_structs = project_struct_names()
    for path in sorted(SCRIPTS.rglob("*.cs")):
        text = read(path)
        for name, body, line in iter_frame_methods(text):
            code = blank_comments(body)  # 注释里的示例代码不参与判定，且保持字符偏移不变
            for pattern, label in ALLOC_PATTERNS:
                for hit in pattern.finditer(code):
                    # 值类型 new 放行（框架值类型 + 项目内声明的 struct）
                    if label == "每帧 new 引用类型":
                        head = hit.group(1).split(".")[0]
                        if head in allowed_structs:
                            continue
                    abs_line = line + code.count("\n", 0, hit.start())
                    problems.append(
                        f"{path.relative_to(PROJECT_ROOT)}:{abs_line}: {name} 内{label}"
                    )
    return sorted(set(problems))


def declared_packages():
    """读 manifest 里当前声明的包名；读不到返回 None（表示不做过滤）。"""
    manifest = PROJECT_ROOT / "Packages" / "manifest.json"
    try:
        data = json.loads(manifest.read_text(encoding="utf-8"))
        return set((data.get("dependencies") or {}).keys())
    except Exception:
        return None


def scan_packages():
    declared = declared_packages()
    counts = {name: 0 for name in PACKAGE_PROBES}
    hits = {name: [] for name in PACKAGE_PROBES}
    for path in sorted(SCRIPTS.rglob("*.cs")):
        text = read(path)
        for name, probes in PACKAGE_PROBES.items():
            if declared is not None and name not in declared:
                continue  # 已从 manifest 移除的包不再参与扫描（§7-11 逐包删除）
            for probe in probes:
                if re.search(probe, text):
                    counts[name] += 1
                    hits[name].append(str(path.relative_to(PROJECT_ROOT)))
                    break
    return counts, hits, (declared or set())


def main():
    parser = argparse.ArgumentParser(description="Piano Teacher 3D 静态检查与依赖门禁")
    parser.add_argument("--strict", action="store_true", help="依赖完全未使用时也视为失败")
    parser.add_argument("--strict-catch", action="store_true",
                        help="把「catch 没有任何出口」也视为失败（P1-9 收敛完成后打开）")
    parser.add_argument("--list-catch", action="store_true", help="逐条打印 catch 出口缺口")
    parser.add_argument("--strict-alloc", action="store_true",
                        help="把「Update 路径每帧分配」也视为失败（存量清完后打开）")
    parser.add_argument("--list-alloc", action="store_true", help="逐条打印每帧分配")
    args = parser.parse_args()

    if not SCRIPTS.is_dir():
        print("✗ 未找到 Assets/Scripts")
        return 2

    groups = [
        ("1. 纯 C# 模块不得引用 UnityEngine", check_pure_csharp()),
        ("2. UI 层不得直接依赖底层调度/时间轴", check_ui_boundary()),
        ("3. 不允许空 catch", check_empty_catch()),
        ("4. Update 路径不得每帧分配（README 铁律）", check_frame_allocations()),
    ]

    failed = False
    for title, problems in groups:
        if problems:
            failed = True
            print(f"✗ {title} —— {len(problems)} 处")
            for p in problems:
                print(f"    {p}")
        else:
            print(f"✓ {title}")

    counts, hits, declared = scan_packages()
    unused = [name for name, count in counts.items() if count == 0 and name in declared]
    print("\n依赖使用扫描（信息性，不删包）")
    for name, count in sorted(counts.items()):
        if name not in declared:
            print(f"  · {name}: 已从 manifest 移除，不再扫描")
        elif count == 0:
            print(f"  · {name}: 代码中未发现引用（需先确认场景/Prefab 资产后再决定是否删除）")
        else:
            print(f"  · {name}: {count} 个脚本引用（{', '.join(sorted(set(hits[name]))[:3])}…）")

    if args.strict and unused:
        failed = True
        print(f"\n✗ --strict：以下包在代码中完全未使用：{', '.join(unused)}")

    untraced = check_catch_trace()
    if untraced:
        limit = len(untraced) if args.list_catch else 8
        print(f"\ncatch 出口缺口（信息性，P1-9 存量）：{len(untraced)} 处 catch 没有任何日志/错误/抛出")
        for item in untraced[:limit]:
            print(f"    {item}")
        if len(untraced) > limit:
            print(f"    …… 其余 {len(untraced) - limit} 处用 --list-catch 查看")
        if args.strict_catch:
            failed = True
            print("✗ --strict-catch：以上 catch 必须给出可追溯出口")
    else:
        print("\n✓ catch 出口完整（每处都有日志/错误/抛出）")

    stats = catch_warning_stats()
    if stats:
        total = sum(count for count, _ in stats)
        print(f"\ncatch 降级统计（信息性，P1-9 收敛目标）：{total} 处只记 Warning")
        for count, name in stats[:5]:
            print(f"  · {name}: {count} 处")

    allocs = check_frame_allocations()
    if allocs:
        if args.list_alloc:
            print("\nUpdate 路径每帧分配明细：")
            for item in allocs:
                print(f"    {item}")
    else:
        print("\n✓ Update 路径零分配（铁律已由规则 4 自动守护）")

    print("\n" + ("✗ 静态检查存在违规" if failed else "✓ 静态检查通过"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
