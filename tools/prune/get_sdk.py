# -*- coding: utf-8 -*-
"""Download and unpack the .NET SDK that global.json pins, into a local dir.

No administrator rights required. Nothing outside the target directory is touched.
"""
from __future__ import annotations

import os
import shutil
import sys
import time
import urllib.request
import zipfile
from pathlib import Path

VERSION = "6.0.101"
URL = f"https://builds.dotnet.microsoft.com/dotnet/Sdk/{VERSION}/dotnet-sdk-{VERSION}-win-x64.zip"
DEST = Path(r"C:\Users\yangy\.dotnet6")
ZIP = Path(r"C:\Users\yangy\dotnet-sdk-6.0.101-win-x64.zip")


def human(n: float) -> str:
    for unit in ("B", "KB", "MB", "GB"):
        if n < 1024:
            return f"{n:.1f}{unit}"
        n /= 1024
    return f"{n:.1f}TB"


def main() -> int:
    if (DEST / "dotnet.exe").exists():
        print(f"already installed at {DEST}")
        return 0

    if not ZIP.exists() or ZIP.stat().st_size < 100_000_000:
        print(f"downloading {URL}")
        start = time.time()
        with urllib.request.urlopen(URL, timeout=120) as rsp, ZIP.open("wb") as out:
            total = int(rsp.headers.get("Content-Length") or 0)
            done = 0
            last = 0.0
            while True:
                chunk = rsp.read(1024 * 256)
                if not chunk:
                    break
                out.write(chunk)
                done += len(chunk)
                now = time.time()
                if now - last > 5:
                    last = now
                    pct = f"{done / total * 100:5.1f}%" if total else "  ?  "
                    speed = human(done / max(now - start, 0.1)) + "/s"
                    print(f"  {pct}  {human(done)}  {speed}", flush=True)
        print(f"downloaded {human(ZIP.stat().st_size)} in {time.time() - start:.0f}s")

    print(f"extracting to {DEST} ...")
    start = time.time()
    DEST.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(ZIP) as zf:
        zf.extractall(DEST)
    print(f"extracted in {time.time() - start:.0f}s")

    dotnet = DEST / "dotnet.exe"
    print("dotnet.exe exists:", dotnet.exists())
    return 0 if dotnet.exists() else 1


if __name__ == "__main__":
    raise SystemExit(main())
