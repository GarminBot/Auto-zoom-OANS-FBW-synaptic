#!/usr/bin/env bash
# Downloads the official MSFS 2024 SDK (core installer) from Microsoft and extracts it
# with msiextract, so the WASM module can be built on Linux/WSL without Visual Studio.
#
# Usage: tools/get-msfs-sdk.sh [target-dir]      (default: .msfs-sdk next to this repo)
# Needs: curl, unzip, msiextract (Debian/Ubuntu: apt install msitools)
# Afterwards: export MSFS_SDK="<target-dir>/MSFS 2024 SDK"
set -euo pipefail

version="${MSFS_SDK_VERSION:-1.7.3}"
root="$(cd "$(dirname "$0")/.." && pwd)"
target="${1:-$root/.msfs-sdk}"
url="https://sdk.flightsimulator.com/msfs2024/files/installers/${version}/MSFS2024_SDK_Core_Installer_${version}.zip"

mkdir -p "$target"
cd "$target"
if [ ! -f "core-${version}.zip" ]; then
  echo "Downloading $url"
  curl -fL -o "core-${version}.zip.part" "$url"
  mv "core-${version}.zip.part" "core-${version}.zip"
fi
rm -rf installer
unzip -q "core-${version}.zip" -d installer
msi="$(find installer -name '*.msi' | head -n 1)"
msiextract -C "$target" "$msi" > /dev/null
rm -rf installer
echo "SDK extracted. Use: export MSFS_SDK=\"$target/MSFS 2024 SDK\""
