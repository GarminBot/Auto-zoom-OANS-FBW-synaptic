#!/usr/bin/env bash
# Builds modules/oans_autozoom.wasm and refreshes layout.json/manifest.json.
#
# Uses clang/wasm-ld (LLVM 15 or newer) with the headers, wasi-sysroot and
# MSFS_WasmVersions.a of the MSFS 2024 SDK. The flags mirror the official
# "MSFS2024" Visual Studio platform toolset (WASM\vs\2022\Microsoft.Cpp.MSFS.Common.targets).
# -mcpu=mvp keeps the code to the instruction set the SDK's own libraries use.
#
# Usage: MSFS_SDK="/path/to/MSFS 2024 SDK" tools/build.sh
#        (tools/get-msfs-sdk.sh downloads and extracts the SDK on Linux/WSL)
set -euo pipefail

: "${MSFS_SDK:?Set MSFS_SDK to the 'MSFS 2024 SDK' folder}"
root="$(cd "$(dirname "$0")/.." && pwd)"
build="$root/build"
cxx="${CXX:-clang++}"
wasm_ld="${WASM_LD:-wasm-ld}"
sysroot="$MSFS_SDK/WASM/wasi-sysroot"
libdir="$sysroot/lib/wasm32-wasi"

mkdir -p "$build" "$root/modules"

"$cxx" --target=wasm32-unknown-wasi --sysroot="$sysroot" -mcpu=mvp -mbulk-memory \
  -std=c++17 -O2 -Wall -Wextra -Werror -Wno-deprecated-declarations \
  -fvisibility=hidden -ffunction-sections -fdata-sections -fno-stack-protector -fno-exceptions \
  -fms-extensions -fstack-size-section \
  -D_MSFS_WASM=1 -D__wasi__ -D_STRING_H_CPLUSPLUS_98_CONFORMANCE_ -D_WCHAR_H_CPLUSPLUS_98_CONFORMANCE_ \
  -D_LIBCPP_HAS_NO_THREADS -D_WINDLL \
  -isystem "$sysroot/include/c++/v1" -isystem "$sysroot/include" \
  -isystem "$MSFS_SDK/WASM/include" -isystem "$MSFS_SDK/SimConnect SDK/include" \
  -c "$root/src/OansAutoZoom.cpp" -o "$build/OansAutoZoom.o"

"$wasm_ld" --no-entry --allow-undefined --export-dynamic --export-table \
  --export malloc --export free --export __wasm_call_ctors --export mallinfo \
  --export mchunkit_begin --export mchunkit_next --export get_pages_state --export mark_decommit_pages \
  --export GetSimConnectVersion --export GetExtensionVersion --export GetExtensionVersionBuffer \
  --gc-sections --strip-debug -O3 \
  "$build/OansAutoZoom.o" \
  -L "$libdir" -lc++ -lc++abi -lc "$libdir/libclang_rt.builtins-wasm32.a" \
  "$MSFS_SDK/WASM/WasmVersions/MSFS_WasmVersions.a" \
  -o "$root/modules/oans_autozoom.wasm"

python3 "$root/tools/make_layout.py"
