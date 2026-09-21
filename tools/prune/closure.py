# -*- coding: utf-8 -*-
"""Compute the MSBuild ProjectReference closure of the acceleration desktop app.

Usage:
    python closure.py <repo-root> [root-project ...]

Prints the projects that are part of the closure (keep set) and those that are
unreachable from all roots (candidates for removal).
"""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

PR_REF = re.compile(
    r"""<ProjectReference\b[^>]*?\bInclude\s*=\s*["']([^"']+)["']""",
    re.IGNORECASE,
)


def norm(path: Path) -> Path:
    return Path(os.path.normcase(os.path.abspath(str(path))))


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    root = Path(sys.argv[1]).resolve()
    roots = [root / r for r in sys.argv[2:]]

    projs: list[Path] = sorted(root.glob("src/**/*.csproj"))
    projs += sorted(root.glob("references/**/*.csproj"))
    by_dir = {}
    for p in projs:
        by_dir.setdefault(norm(p.parent), p)

    edges: dict[Path, list[Path]] = {}
    for p in projs:
        text = p.read_text(encoding="utf-8-sig", errors="replace")
        deps = []
        for inc in PR_REF.findall(text):
            inc = inc.replace("\\", os.sep).replace("/", os.sep)
            target = (p.parent / inc).resolve()
            if target.exists():
                deps.append(norm(target))
            else:
                deps.append(norm(target))  # keep dangling for reporting
        edges[norm(p)] = deps

    # BFS closure
    keep: set[Path] = set()
    queue = [norm(r) for r in roots]
    missing: set[Path] = set()
    while queue:
        cur = queue.pop()
        if cur in keep:
            continue
        keep.add(cur)
        for dep in edges.get(cur, []):
            if dep not in keep:
                if dep in edges or dep.exists():
                    queue.append(dep)
                else:
                    missing.add(dep)

    all_p = sorted(edges)
    unreachable = [p for p in all_p if p not in keep]

    def rel(p: Path) -> str:
        try:
            return str(Path(p).relative_to(norm(root)))
        except ValueError:
            return str(p)

    print(f"== 项目总数: {len(all_p)}")
    print(f"== 保留(闭包内): {len(keep)}")
    for p in sorted(keep):
        print(f"   KEEP  {rel(p)}")
    print(f"== 可移除(闭包外): {len(unreachable)}")
    for p in unreachable:
        print(f"   DROP  {rel(p)}")
    if missing:
        print(f"== 缺失的引用目标(未 clone 的子模块): {len(missing)}")
        for p in sorted(missing):
            print(f"   MISS  {rel(p)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
