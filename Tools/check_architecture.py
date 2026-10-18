#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Piano Teacher 3D — 轻量架构不变式检查（对应评估文档 P1-11）。

只做**静态文本规则**检查，不替代编译与测试；目的是让「模块边界」在 CI 里可重复执行，
而不是靠人工记忆。所有规则都支持行内豁免注释：`// arch-ok: 原因`。

退出码：0 全部通过；1 存在违规；2 环境问题（找不到源码目录）。
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, "Assets", "Scripts")

# (规则 ID, 说明, 相对目录或文件, 禁止出现的正则, 豁免说明)
RULES = [
    (
        "ARCH-01",
        "UI 不得直接依赖底层调度器 / 时间轴 / 琴键 / IK（必须经 LessonSessionController 门面）",
        "UI",
        r"\b(MidiEventScheduler|LessonTimeline|PianoKeyMap|HandIKController)\b",
        "UI 只允许调用会话公开接口",
    ),
    (
        "ARCH-02",
        "除 Core 外不得直接引用 LessonTimeline（唯一时间源只能由会话层持有）",
        None,  # 全目录，排除 Core
        r"\bLessonTimeline\b",
        "表现层读时间应经会话属性",
    ),
    (
        "ARCH-03",
        "不得引入 System.Windows.Forms（零第三方 / 跨平台边界）",
        None,
        r"^\s*using\s+System\.Windows\.Forms\s*;",
        "文件选择器必须用 UGUI 自建",
    ),
    (
        "ARCH-04",
        "不得在 PianoTeacher3D 命名空间下定义与 UnityEngine 同名的类型（Camera / Avatar / Light / Material）",
        None,
        r"^\s*(public|internal)\s+(sealed\s+|static\s+|abstract\s+)?(class|struct|enum)\s+(Camera|Avatar|Light|Material)\b",
        "同名会导致类型解析冲突（CS0118/CS0234）",
    ),
    (
        "ARCH-05",
        "表现层不得出现 Update 内的 LINQ（热路径零分配约定）",
        None,
        r"^\s*(public|private|internal)?\s*void\s+Update\s*\([^)]*\)[\s\S]{0,2000}?\.(Where|Select|OrderBy|ToList|Any)\s*\(",
        "热路径禁止 LINQ，改用预分配容器",
    ),
]

EXEMPT_MARK = "arch-ok"


def iter_cs_files(subdir=None):
    base = os.path.join(SCRIPTS, subdir) if subdir else SCRIPTS
    for dirpath, _dirnames, filenames in os.walk(base):
        for name in filenames:
            if name.endswith(".cs"):
                yield os.path.join(dirpath, name)


def rel(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")


def check(rule_id, desc, subdir, pattern, hint):
    regex = re.compile(pattern, re.MULTILINE)
    violations = []
    for path in iter_cs_files(subdir):
        # ARCH-02 排除 Core（Core 内部允许持有时间轴）
        if rule_id == "ARCH-02" and "/Core/" in rel(path):
            continue
        try:
            text = open(path, encoding="utf-8").read()
        except OSError as exc:  # 环境问题按违规上报，避免静默跳过
            violations.append((rel(path), 0, "读取失败：%s" % exc))
            continue
        for match in regex.finditer(text):
            line_no = text.count("\n", 0, match.start()) + 1
            lines = text.splitlines()
            line = lines[line_no - 1] if line_no - 1 < len(lines) else ""
            stripped = line.strip()
            # 注释行不算依赖（XML doc 里提到类型名是说明，不是引用）
            if stripped.startswith(("//", "///", "*", "/*")):
                continue
            if EXEMPT_MARK in line:
                continue
            violations.append((rel(path), line_no, stripped[:120]))
    return violations


def main():
    if not os.path.isdir(SCRIPTS):
        print("[ARCH] 找不到 Assets/Scripts，无法检查：%s" % SCRIPTS)
        return 2

    print("=" * 62)
    print("架构不变式检查（静态规则，共 %d 条）" % len(RULES))
    print("=" * 62)

    failed = 0
    for rule_id, desc, subdir, pattern, hint in RULES:
        violations = check(rule_id, desc, subdir, pattern, hint)
        if violations:
            failed += len(violations)
            print("\n[FAIL] %s  %s" % (rule_id, desc))
            for path, line_no, snippet in violations:
                print("       %s:%s  %s" % (path, line_no, snippet))
            print("       处理建议：%s（如确属例外，在该行加 `// arch-ok: 原因`）" % hint)
        else:
            print("[ OK ] %s  %s" % (rule_id, desc))

    print("\n" + "-" * 62)
    if failed:
        print("架构检查未通过：%d 处违规" % failed)
        return 1
    print("架构检查通过：全部规则满足")
    return 0


if __name__ == "__main__":
    sys.exit(main())
