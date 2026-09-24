#!/usr/bin/env sh
# Builds src/OansAutoZoom.cpp together with a fake simulator for the host and runs the scenario test.
# Needs any C++17 compiler (g++ or clang++). Does not need the MSFS SDK.
set -eu

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="${TMPDIR:-/tmp}/oans-autozoom-host-test"
cxx="${CXX:-c++}"

"$cxx" -std=c++17 -Wall -Wextra -Werror -I "$here/stubs" \
  "$root/src/OansAutoZoom.cpp" "$here/scenario_test.cpp" -o "$out"
"$out"
