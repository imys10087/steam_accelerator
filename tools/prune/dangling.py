# -*- coding: utf-8 -*-
"""Find references to types removed by the prune.

Type names are derived from the basenames of the deleted rule set
(see prune.py), which is reliable for this codebase because every file
declares one type named after the file.

Run:  python tools/prune/dangling.py <repo-root> [--full]
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from prune import DROP_DIRS, DROP_FILES  # noqa: E402

EXCLUDE_PARTS = {".git", "bin", "obj", "node_modules", "tools"}
SCAN_SUFFIX = {".cs", ".axaml", ".csproj", ".props", ".targets", ".sln", ".json", ".sh"}

# basenames that are plainly not type names
SKIP_STEMS = {"Properties", "SR", "AppResources", "AssemblyInfo", "Constants"}


def type_names_from(stems: set[str]) -> dict[str, str]:
    out: dict[str, str] = {}
    for stem in stems:
        if stem in SKIP_STEMS or not stem[:1].isupper():
            continue
        out[stem] = stem
    return out


def collect_pruned_stems(root: Path) -> dict[str, str]:
    """Map candidate type name -> originating path, for both file and dir rules."""
    stems: dict[str, str] = {}

    for rel in DROP_FILES:
        p = root / rel
        stem = Path(rel).stem
        # '.axaml.cs' -> '.axaml'
        stem = Path(stem).stem if stem.endswith(".axaml") else stem
        if stem and stem[:1].isupper():
            stems.setdefault(stem, rel)

    for rel in DROP_DIRS:
        base = root / rel
        # recover the type names of every .cs that used to live in this dir
        pass  # handled by DIR_STEMS below

    return stems


# Explicit type names for the pruned *directories* (their files are gone, so we
# list the public surface that other modules could still be referring to).
DIR_STEMS: tuple[str, ...] = (
    # ST.Client / Pages
    "ArchiSteamFarmPlusPageViewModel", "ASF_AddBotWindowViewModel",
    "GameRelatedPageViewModel", "GameRelated_BorderlessPageViewModel",
    "SteamAccountPageViewModel", "ShareManageViewModel",
    "GameListPageViewModel", "AchievementWindowViewModel", "EditAppInfoWindowViewModel",
    "HideAppWindowViewModel", "IdleAppWindowViewModel", "SteamShutdownWindowViewModel",
    "LocalAuthPageViewModel", "AddAuthWindowViewModel", "AuthTradeWindowViewModel",
    "EncryptionAuthWindowViewModel", "ExportAuthWindowViewModel",
    "MyAuthenticatorWindowViewModel", "ShowAuthWindowViewModel",
    "UserProfileWindowViewModel", "LoginOrRegisterWindowViewModel",
    "BindPhoneNumberWindowViewModel", "ChangeBindPhoneNumberWindowViewModel",
    # ST.Client / Steam models
    "AchievementInfo", "FloatStatInfo", "IntStatInfo", "StatInfo", "SteamApp",
    "SteamAppInfo", "SteamAppList", "SteamAppProperty", "SteamAppPropertyTable",
    "SteamApps", "SteamMiniProfile", "SteamUser", "TradeCard", "LocalDlssDll",
    "KeyValue",
    # CloudService
    "GAPAuthenticatorDTO", "IGAPAuthenticatorDTO", "GAPAuthenticatorValueDTO",
    "IGAPAuthenticatorValueDTO", "BattleNetAuthenticator", "GoogleAuthenticator",
    "HOTPAuthenticator", "MicrosoftAuthenticator", "SteamAuthenticator",
    "WinAuthBase32", "WinAuthSteamClient", "AuthenticatorException",
    "IGAPAuthenticatorDTOExtensions",
    # Avalonia views/controls
    "ASF_BotPage", "ASF_GlobalConfigPage", "DebugPage", "DebugWebViewPage",
    "GameListPage", "GameRelatedPage", "GameRelated_BorderlessPage",
    "LocalAuthPage", "StartPage", "SteamAccountPage", "Settings_Steam",
    "ShareManageWindow", "AchievementWindow", "EditAppInfoWindow", "HideAppWindow",
    "IdleAppWindow", "SteamShutdownWindow", "AddAuthWindow", "AuthTradeWindow",
    "EncryptionAuthWindow", "ExportAuthWindow", "ShowAuthWindow",
    "BindPhoneNumberWindow", "ChangeBindPhoneNumberWindow", "LoginOrRegisterWindow",
    "UserProfileWindow", "DebugWindow", "NewVersionWindow", "WebView3Window",
    "WebView3", "WebViewBase", "ConsoleShell", "EmbedSample", "CefNetApp",
    "BotStatusConverter", "AvaloniaApplicationUpdateServiceImpl",
)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("--full", action="store_true")
    ap.add_argument("--max", type=int, default=8)
    args = ap.parse_args()

    root = Path(args.root).resolve()

    names = set(collect_pruned_stems(root)) | set(DIR_STEMS)

    # keep only names that are NOT declared anywhere in the remaining tree
    declared: set[str] = set()
    decl_re = re.compile(
        r"\b(?:class|interface|struct|enum|record|delegate)\s+([A-Za-z_][A-Za-z0-9_]*)"
    )
    for f in root.rglob("*.cs"):
        if any(part in EXCLUDE_PARTS for part in f.parts):
            continue
        try:
            declared |= set(decl_re.findall(f.read_text(encoding="utf-8-sig", errors="replace")))
        except OSError:
            continue

    targets = sorted(n for n in names if n not in declared)
    print(f"# 待检查的已删除类型名: {len(targets)}")
    print(f"# 当前树中已声明的类型总数: {len(declared)}\n")

    if not targets:
        return 0

    pattern = re.compile(r"\b(" + "|".join(sorted(map(re.escape, targets), key=len, reverse=True)) + r")\b")

    hits: dict[str, list[tuple[str, int, str]]] = {}
    for f in root.rglob("*"):
        if not f.is_file() or f.suffix not in SCAN_SUFFIX:
            continue
        if any(part in EXCLUDE_PARTS for part in f.parts):
            continue
        try:
            text = f.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        for i, line in enumerate(text.splitlines(), 1):
            for m in pattern.finditer(line):
                hits.setdefault(m.group(1), []).append(
                    (f.relative_to(root).as_posix(), i, line.strip())
                )

    total = sum(len(v) for v in hits.values())
    print(f"# 残留引用: {len(hits)} 个类型, {total} 处\n")
    for name in sorted(hits, key=lambda n: (-len(hits[n]), n)):
        where = hits[name]
        files = sorted({w[0] for w in where})
        print(f"### {name}   ({len(where)} 处 / {len(files)} 文件)")
        shown = where if args.full else where[: args.max]
        for path, line, txt in shown:
            print(f"    {path}:{line}: {txt[:160]}")
        if not args.full and len(where) > len(shown):
            print(f"    ... 另有 {len(where) - len(shown)} 处")
        print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
