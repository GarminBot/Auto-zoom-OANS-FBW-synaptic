#!/usr/bin/env bash
# End-to-end test on Linux: runs the real AIRAC-Updater.exe under Wine against a pretend Windows PC.
#
#   1. builds a Wine prefix with wine-mono (once) and maps drive D:
#   2. scenario.py setup: MSFS 2024 (Store, packages on D:), PMDG, Fenix, all on AIRAC 2508
#   3. AIRAC-Updater.exe --install "AIRAC 2510.zip" (command line), then scenario.py verify
#   4. opens the window with "AIRAC 2511.zip", presses Alt+A ("Alle aktualisieren") and saves
#      screenshots to $OUT (default: build/e2e)
#
# Needs: wine64 (Debian/Ubuntu: apt install wine64), xvfb, xdotool, imagemagick, curl, python3.
# Usage: tests/e2e/run-wine.sh [path/to/AIRAC-Updater.exe]
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
exe="${1:-$root/AIRAC-Updater.exe}"
out="${OUT:-$root/build/e2e}"
wine="${WINE:-$(command -v wine64 || echo /usr/lib/wine/wine64)}"
export WINEPREFIX="${WINEPREFIX:-$root/build/wineprefix}" WINEARCH=win64 WINEDEBUG=-all
drive_d="$root/build/drive_d"
mkdir -p "$out" "$drive_d"

wait_s() { read -rt "$1" <> <(:) || true; }

if [ ! -d "$WINEPREFIX/drive_c/windows/mono" ]; then
  echo "creating Wine prefix with wine-mono"
  WINEDLLOVERRIDES="mscoree,mshtml=" xvfb-run -a "$wine" wineboot -i > /dev/null 2>&1
  mono_msi="$root/build/wine-mono-8.1.0-x86.msi"
  [ -f "$mono_msi" ] || curl -fsSL -o "$mono_msi" https://dl.winehq.org/wine/wine-mono/8.1.0/wine-mono-8.1.0-x86.msi
  xvfb-run -a "$wine" msiexec /i "$mono_msi" /qn > /dev/null 2>&1
fi
ln -sfn "$drive_d" "$WINEPREFIX/dosdevices/d:"
cp "$exe" "$WINEPREFIX/drive_c/AIRAC-Updater.exe"

python3 "$here/scenario.py" setup "$WINEPREFIX" "$drive_d"
xvfb-run -a "$wine" 'C:\AIRAC-Updater.exe' --install 'C:\users\root\Downloads\AIRAC 2510.zip' --report 'C:\install.txt' > /dev/null 2>&1 || true
tr -d '\r' < "$WINEPREFIX/drive_c/install.txt" | tee "$out/install.txt"
python3 "$here/scenario.py" verify "$WINEPREFIX" "$drive_d"

# The window: load the next cycle, press Alt+A, take screenshots.
export DISPLAY=:79
Xvfb "$DISPLAY" -screen 0 1280x860x24 > /dev/null 2>&1 &
xvfb=$!
for _ in $(seq 50); do xdpyinfo > /dev/null 2>&1 && break; wait_s 0.1; done
"$wine" 'C:\AIRAC-Updater.exe' 'C:\users\root\Downloads\AIRAC 2511.zip' > /dev/null 2>&1 &
app=$!
wid=$(timeout 60 xdotool search --sync --onlyvisible --name '^AIRAC Updater' | head -n1)
wait_s 6
import -window root "$out/1-geladen.png"
xdotool windowfocus --sync "$wid" 2> /dev/null || true
xdotool key --window "$wid" alt+a
wait_s 6
import -window root "$out/2-fertig.png"
xdotool key Return
wait_s 3
import -window root "$out/3-danach.png"
kill "$app" 2> /dev/null || true
"$(dirname "$wine")/wineserver" -k 2> /dev/null || true
kill "$xvfb" 2> /dev/null || true
python3 "$here/scenario.py" verify "$WINEPREFIX" "$drive_d" 2511
echo "screenshots in $out"
