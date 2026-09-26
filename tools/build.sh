#!/usr/bin/env bash
# Runs the unit tests and builds AIRAC-Updater.exe (.NET Framework 4.8) into the repository root.
# Needs only the .NET 8 SDK; works on Windows (Git Bash), Linux and macOS.
#
# Usage: tools/build.sh
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

dotnet test "$root/tests/AiracUpdater.Tests" -c Release
dotnet build "$root/src/AiracUpdater" -c Release
cp "$root/src/AiracUpdater/bin/Release/net48/AIRAC-Updater.exe" "$root/AIRAC-Updater.exe"
echo "AIRAC-Updater.exe: $(wc -c < "$root/AIRAC-Updater.exe") bytes"
