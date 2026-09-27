#!/usr/bin/env python3
"""Builds a pretend Windows PC inside a Wine prefix and checks it after AIRAC-Updater ran.

  scenario.py setup  <wineprefix> <drive_d>   create simulators, add-ons (cycle 2508) and the input ZIPs
  scenario.py verify <wineprefix> <drive_d>   check that every installed add-on now has cycle 2510

MSFS 2024 (Microsoft Store) keeps its packages on drive D:, like many real installs.
"""

import json
import pathlib
import shutil
import sys
import zipfile

USER = "root"


def paths(prefix, drive_d):
    c = pathlib.Path(prefix) / "drive_c"
    local = c / "users" / USER / "AppData" / "Local"
    store = local / "Packages" / "Microsoft.Limitless_8wekyb3d8bbwe"
    return {
        "c": c,
        "d": pathlib.Path(drive_d),
        "local": local,
        "store": store,
        "localstate": store / "LocalState",
        "packages": pathlib.Path(drive_d) / "MSFS2024",
        "programdata": c / "ProgramData",
        "downloads": c / "users" / USER / "Downloads",
    }


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="")


def sim_packages(folder, cycle, revision=1):
    base = folder / "navigraph-nav-base"
    write(base / "manifest.json", json.dumps({"title": "AIRAC Cycle Base", "package_order_hint": "CUSTOM_NAVDATA"}))
    write(base / "layout.json", '{"content":[]}')
    write(base / "ContentInfo" / "navigraph-navdata" / "cycle.json", json.dumps({"Cycle": cycle, "Revision": revision}))
    write(base / "scenery" / "fs-base-jep" / "scenery" / "world" / "base.bgl", "BGL " + cycle)
    jepp = folder / "navigraph-nav-jepp"
    write(jepp / "manifest.json", json.dumps({"title": "AIRAC Cycle %s rev.%d" % (cycle, revision), "package_order_hint": "CUSTOM_NAVDATA_PATCH"}))
    write(jepp / "layout.json", '{"content":[]}')
    write(jepp / "scenery" / "fs-base-jep" / "scenery" / "world" / "AIRACCycle.bgl", "BGL " + cycle)


def pmdg_data(folder, cycle):
    write(folder / "e_dfd_PMDG.s3db", "SQLite format 3\0 PMDG " + cycle)
    write(folder / "cycle.json", json.dumps({"cycle": cycle, "revision": "1", "name": "PMDG (all compatible products)"}))
    write(folder / "cycle_info.txt", "AIRAC cycle    : %s\r\nVersion        : 1\r\n" % cycle)


def fenix_data(folder, cycle, imported=False):
    write(folder / "nd.db3", "SQLite format 3\0 Fenix " + cycle)
    write(folder / "cycle_info.txt", "AIRAC cycle    : %s\r\nVersion        : 1\r\n" % cycle)
    write(folder / "cycle.json", json.dumps({"cycle": cycle, "revision": "1"}))
    if imported:
        write(folder / "imported.db3", "imported " + cycle)
        write(folder / "imported_cycle_hash.bin", "hash")


def package(folder, name, title):
    write(folder / name / "manifest.json", json.dumps({"title": title, "content_type": "AIRCRAFT"}))


def zip_folder(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(source.rglob("*")):
            archive.write(file, file.relative_to(source).as_posix())


def setup(p):
    for key in ("store", "packages"):
        shutil.rmtree(p[key], ignore_errors=True)
    shutil.rmtree(p["programdata"] / "Fenix", ignore_errors=True)
    shutil.rmtree(p["local"] / "AIRAC-Updater", ignore_errors=True)
    shutil.rmtree(p["downloads"], ignore_errors=True)

    write(p["store"] / "LocalCache" / "UserCfg.opt", '{Graphics\r\n}\r\nInstalledPackagesPath "D:\\MSFS2024"\r\n')
    community = p["packages"] / "Community"
    (p["packages"] / "Community2024").mkdir(parents=True)
    sim_packages(community, "2508")
    package(community, "fnx-aircraft-320", "Fenix A320")
    package(community, "fnx-aircraft-319-321", "Fenix A319/A321")
    fenix_data(p["programdata"] / "Fenix" / "Navdata", "2508", imported=True)
    for name in ("pmdg-aircraft-738", "pmdg-aircraft-77w", "pmdg-aircraft-77f"):
        package(community, name, name)
    pmdg_data(p["localstate"] / "WASM" / "MSFS2024" / "pmdg-aircraft-738" / "work" / "NavigationData", "2508")
    pmdg_data(p["localstate"] / "WASM" / "MSFS2024" / "pmdg-aircraft-77w" / "work" / "NavigationData", "2508")
    # The 777F was never loaded: no work folder yet.
    # The Airbus fleet: FBW reads the simulator's navdata; iniBuilds and Synaptic do in their
    # "sim default"/"native" mode (the A380 comes from the Marketplace).
    package(community, "flybywire-aircraft-a380-842", "FlyByWire A380X")
    package(community, "inibuilds-aircraft-a350", "iniBuilds A350")
    package(community, "inibuilds-aircraft-a340", "iniBuilds A340")
    package(p["packages"] / "Official2024" / "OneStore", "inibuilds-aircraft-a380", "iniBuilds A380")
    package(community, "inibuilds-aircraft-a220", "A220")
    (community / "inibuilds-aircraft-a220" / "SimObjects" / "Airplanes" / "Synaptic_A220").mkdir(parents=True)

    # Input: one folder per add-on, the PMDG data as a ZIP inside the ZIP.
    staging = p["downloads"] / "staging"
    pmdg_data(staging / "pmdg" / "NavigationData", "2510")
    zip_folder(staging / "pmdg", staging / "zip" / "PMDG" / "navigraph_pmdg_2510.zip")
    fenix_data(staging / "zip" / "Fenix" / "Navdata", "2510")
    sim_packages(staging / "zip" / "MSFS 2024 Standard", "2510")
    write(staging / "zip" / "Unbekannt" / "liesmich.txt", "keine Navdaten")
    zip_folder(staging / "zip", p["downloads"] / "AIRAC 2510.zip")

    # A second ZIP with the next cycle, for the window screenshot after the first update.
    sim_packages(staging / "zip2511" / "MSFS 2024", "2511")
    fenix_data(staging / "zip2511" / "Fenix", "2511")
    pmdg_data(staging / "zip2511" / "PMDG", "2511")
    zip_folder(staging / "zip2511", p["downloads"] / "AIRAC 2511.zip")
    shutil.rmtree(staging)
    print("setup done")


def read(path):
    return path.read_text(encoding="utf-8")


def verify(p, cycle="2510"):
    community = p["packages"] / "Community"
    checks = {
        "sim jepp": "AIRAC Cycle %s rev.1" % cycle in read(community / "navigraph-nav-jepp" / "manifest.json"),
        "sim base": '"Cycle": "%s"' % cycle in read(community / "navigraph-nav-base" / "ContentInfo" / "navigraph-navdata" / "cycle.json"),
        "fenix": cycle in read(p["programdata"] / "Fenix" / "Navdata" / "cycle_info.txt"),
        "fenix re-import": not (p["programdata"] / "Fenix" / "Navdata" / "imported.db3").exists(),
        "pmdg 738": cycle in read(p["localstate"] / "WASM" / "MSFS2024" / "pmdg-aircraft-738" / "work" / "NavigationData" / "cycle.json"),
        "pmdg 77w": cycle in read(p["localstate"] / "WASM" / "MSFS2024" / "pmdg-aircraft-77w" / "work" / "NavigationData" / "cycle.json"),
        "pmdg 77f untouched": not (p["localstate"] / "WASM" / "MSFS2024" / "pmdg-aircraft-77f").exists(),
        "community clean": sorted(x.name for x in community.iterdir()) == sorted([
            "navigraph-nav-base", "navigraph-nav-jepp", "fnx-aircraft-320", "fnx-aircraft-319-321",
            "pmdg-aircraft-738", "pmdg-aircraft-77w", "pmdg-aircraft-77f",
            "flybywire-aircraft-a380-842", "inibuilds-aircraft-a350", "inibuilds-aircraft-a340",
            "inibuilds-aircraft-a220"]),
        "sim backup on D:": (p["packages"] / "AIRAC-Updater" / "backup" / "msfs2024-navdata" / "navigraph-nav-jepp" / "manifest.json").exists(),
        "pmdg/fenix backups": len(list((p["local"] / "AIRAC-Updater" / "Backups").iterdir())) == 3,
        "no leftovers": not list(p["localstate"].rglob("*.airac-new")) and not list(p["localstate"].rglob("*.airac-old"))
                         and not list((p["programdata"] / "Fenix").glob("*.airac-*")),
    }
    for name, ok in checks.items():
        print(("OK    " if ok else "FAIL  ") + name)
    if not all(checks.values()):
        sys.exit(1)
    print("verify done: all add-ons on %s" % cycle)


if __name__ == "__main__":
    command, prefix, drive_d = sys.argv[1:4]
    p = paths(prefix, drive_d)
    if command == "setup":
        setup(p)
    else:
        verify(p, *(sys.argv[4:5] or []))
