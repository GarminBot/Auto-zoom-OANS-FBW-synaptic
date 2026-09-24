#!/usr/bin/env python3
"""Writes layout.json for the MSFS package at the repository root and updates
total_package_size in manifest.json.

Only the files below PACKAGE_DIRS belong to the package; everything else in the
repository (README, docs, sources, tests, tools) is not listed and therefore not
mounted by the simulator.
"""

import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
PACKAGE_DIRS = ["modules"]

# Windows FILETIME: 100 ns ticks since 1601-01-01.
FILETIME_EPOCH_OFFSET = 116444736000000000


def package_files():
    for directory in PACKAGE_DIRS:
        for path in sorted((ROOT / directory).rglob("*")):
            if path.is_file():
                yield path


def main():
    content = []
    total_size = 0
    for path in package_files():
        stat = path.stat()
        content.append(
            {
                "path": path.relative_to(ROOT).as_posix(),
                "size": stat.st_size,
                "date": stat.st_mtime_ns // 100 + FILETIME_EPOCH_OFFSET,
            }
        )
        total_size += stat.st_size

    if not content:
        raise SystemExit("no package files found - build the module first (tools/build.sh)")

    layout_path = ROOT / "layout.json"
    layout_path.write_text(json.dumps({"content": content}, indent=2) + "\n", encoding="utf-8", newline="\n")

    manifest_path = ROOT / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["total_package_size"] = str(total_size)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")

    for entry in content:
        print(f"{entry['path']}  {entry['size']} bytes")
    print(f"total_package_size = {total_size}")


if __name__ == "__main__":
    main()
