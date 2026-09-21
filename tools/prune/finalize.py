# -*- coding: utf-8 -*-
"""Phase 5: 收尾配置清理。

1. 从平台工程中移除已无任何代码引用的 NuGet 包（Gameloop.Vdf —— 随 Steam 本地
   读写模块一并删除）。
2. 从 Directory.Packages.props 中移除 AutoMapper 系列条目（映射已改为显式代码）。
3. 按「文件是否仍然存在」过滤 SteamToolsV2+.sln，自动删除指向已移除工程的条目
   及其构建配置，避免手工维护遗漏。
4. 删除与裁剪后仓库无关的其他解决方案/筛选器文件。

Run:  python tools/prune/finalize.py <repo-root> [--dry-run]
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

# NuGet 包 -> 需要清理的工程文件（按包含该 PackageReference 判断）
DEAD_PACKAGES: tuple[str, ...] = (
    "Gameloop.Vdf",
)

# Directory.Packages.props 中要整段移除的包版本条目
DEAD_PACKAGE_VERSIONS: tuple[str, ...] = (
    "AutoMapper",
    "AutoMapper.Collection",
    "AutoMapper.Collection.EntityFrameworkCore",
    "AutoMapper.Extensions.ExpressionMapping",
    "AutoMapper.Extensions.Microsoft.DependencyInjection",
    "JustArchiNET.Madness",
)

# 一并删除的解决方案/筛选器文件（裁剪后不再需要）
DEAD_SOLUTION_FILES: tuple[str, ...] = (
    "Avalonia.Ref.sln",
    "SteamTools.Blazor.BackManage.sln",
    "SteamToolsV2+.Linux.slnf",
    "SteamToolsV2+.Mac.slnf",
)

MAIN_SLN = "SteamToolsV2+.sln"

SOLUTION_FOLDER_TYPE = "2150E333-8FDC-42A3-9474-1A3956D46DE8"

PROJECT_LINE = re.compile(
    r'^Project\("\{(?P<type>[0-9A-Fa-f\-]+)\}"\)\s*=\s*"(?P<name>[^"]*)"\s*,\s*'
    r'"(?P<path>[^"]*)"\s*,\s*"\{(?P<guid>[0-9A-Fa-f\-]+)\}"\s*$'
)

CONFIG_LINE = re.compile(r"^\s*\{(?P<guid>[0-9A-Fa-f\-]+)\}\..*$")


def strip_package(root: Path, package: str, dry: bool, log: list[str]) -> None:
    for csproj in sorted(root.glob("src/**/*.csproj")):
        text = csproj.read_text(encoding="utf-8-sig")
        if f'Include="{package}"' not in text:
            continue

        new_lines = [
            line for line in text.splitlines(keepends=True)
            if f'Include="{package}"' not in line
        ]
        log.append(f"  -{package}  {csproj.relative_to(root).as_posix()}")
        if not dry:
            csproj.write_text("".join(new_lines), encoding="utf-8-sig")


def strip_package_versions(root: Path, dry: bool, log: list[str]) -> None:
    props = root / "Directory.Packages.props"
    if not props.exists():
        return

    text = props.read_text(encoding="utf-8-sig")
    lines = text.splitlines(keepends=True)
    out: list[str] = []
    removed = 0

    for line in lines:
        is_dead = False
        for package in DEAD_PACKAGE_VERSIONS:
            if re.search(rf'Include="{re.escape(package)}"', line):
                is_dead = True
                break
        if is_dead:
            removed += 1
            log.append(f"  Directory.Packages.props: -{line.strip()[:90]}")
            continue
        out.append(line)

    if removed and not dry:
        props.write_text("".join(out), encoding="utf-8-sig")


def remaining_submodules(root: Path) -> set[str]:
    """解析 .gitmodules，返回仍然保留的子模块路径（如 references/Titanium-Web-Proxy）。"""
    gm = root / ".gitmodules"
    if not gm.exists():
        return set()

    return {
        line.split("=", 1)[1].strip().replace("\\", "/")
        for line in gm.read_text(encoding="utf-8").splitlines()
        if line.strip().startswith("path")
    }


def should_keep_project(root: Path, path: str, submodules: set[str]) -> bool:
    """判断解决方案中的工程是否仍然存在。

    子模块目录在未 `git submodule update --init` 时是空的，因此不能仅凭
    「路径存在」判断 —— 还要看该子模块是否仍保留在 .gitmodules 中。
    """
    normalized = path.replace("\\", "/")

    if normalized.startswith("references/"):
        parts = normalized.split("/")
        top = "/".join(parts[:2])  # references/<name>
        return top in submodules

    return (root / normalized).exists()


def filter_solution(root: Path, dry: bool, log: list[str]) -> None:
    sln = root / MAIN_SLN
    if not sln.exists():
        return

    submodules = remaining_submodules(root)
    lines = sln.read_text(encoding="utf-8-sig").splitlines(keepends=True)

    kept: dict[str, str] = {}      # guid -> name
    dropped: list[str] = []

    # 第一遍：确定保留哪些 Project 块
    for line in lines:
        m = PROJECT_LINE.match(line.strip())
        if not m:
            continue
        guid = m.group("guid").lower()
        type_guid = m.group("type").upper()
        path = m.group("path").replace("\\", "/")

        if type_guid == SOLUTION_FOLDER_TYPE:
            kept[guid] = m.group("name")
            continue

        if should_keep_project(root, path, submodules):
            kept[guid] = m.group("name")
        else:
            dropped.append(path)

    if not dropped:
        return

    out: list[str] = []
    in_config_section = False

    for line in lines:
        stripped = line.strip()

        m = PROJECT_LINE.match(stripped)
        if m:
            guid = m.group("guid").lower()
            path = m.group("path").replace("\\", "/")
            if guid not in kept:
                log.append(f"  .sln: -项目 {m.group('name')} ({path})")
                continue

        if stripped.startswith("GlobalSection(ProjectConfigurationPlatforms"):
            in_config_section = True
            out.append(line)
            continue

        if in_config_section:
            if stripped.startswith("EndGlobalSection"):
                in_config_section = False
                out.append(line)
                continue

            cm = CONFIG_LINE.match(line)
            if cm and cm.group("guid").lower() not in kept:
                continue

        if stripped.startswith("GlobalSection(NestedProjects"):
            out.append(line)
            # 逐行过滤紧随其后的映射
            continue

        out.append(line)

    if not dry:
        sln.write_text("".join(out), encoding="utf-8-sig")


def clean_nested_projects(root: Path, dry: bool, log: list[str]) -> None:
    """移除 NestedProjects 中指向已删除 GUID 的行。"""
    sln = root / MAIN_SLN
    if not sln.exists():
        return

    text = sln.read_text(encoding="utf-8-sig")
    existing_guids = {
        m.group("guid").lower()
        for line in text.splitlines()
        if (m := PROJECT_LINE.match(line.strip()))
    }

    out_lines: list[str] = []
    in_nested = False

    for line in text.splitlines(keepends=True):
        stripped = line.strip()

        if stripped.startswith("GlobalSection(NestedProjects"):
            in_nested = True
            out_lines.append(line)
            continue

        if in_nested:
            if stripped.startswith("EndGlobalSection"):
                in_nested = False
                out_lines.append(line)
                continue

            m = CONFIG_LINE.match(line)
            if m:
                refs = re.findall(r"\{([0-9A-Fa-f\-]+)\}", stripped)
                if any(r.lower() not in existing_guids for r in refs):
                    continue

        out_lines.append(line)

    if not dry and "".join(out_lines) != text:
        sln.write_text("".join(out_lines), encoding="utf-8-sig")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    root = Path(args.root).resolve()
    if not (root / "src").is_dir():
        print(f"not a repo root: {root}", file=sys.stderr)
        return 2

    log: list[str] = []

    print("== Phase 5a: 移除已无代码引用的 NuGet 包 ==")
    for package in DEAD_PACKAGES:
        strip_package(root, package, args.dry_run, log)

    print("== Phase 5b: 清理 Directory.Packages.props ==")
    strip_package_versions(root, args.dry_run, log)

    print("== Phase 5c: 过滤主解决方案 ==")
    filter_solution(root, args.dry_run, log)
    clean_nested_projects(root, args.dry_run, log)

    print("== Phase 5d: 删除无关解决方案文件 ==")
    for name in DEAD_SOLUTION_FILES:
        p = root / name
        if p.exists():
            if not args.dry_run:
                p.unlink()
            log.append(f"  rm {name}")

    print("\n== Manifest ==")
    for item in log:
        print(item)
    print(f"\n共 {len(log)} 项变更。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
