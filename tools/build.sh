#!/usr/bin/env bash
# Builds modules/oans_autozoom.wasm and refreshes layout.json/manifest.json.
#
# Uses the compiler and linker that ship with the MSFS 2024 SDK (WASM\llvm\bin: clang-cl.exe,
# wasm-ld.exe) with the options of the official "MSFS2024" Visual Studio platform toolset:
# WASM\vs\2022\Microsoft.Cpp.MSFS.Common.targets plus the Release settings of the SDK's
# StandaloneModule sample. That includes --stack-guard-page and MSFS_WasmVersions.a, which
# only the SDK's own linker supports and which mark the module as an MSFS 2024 module.
#
# Windows (Git Bash): runs the SDK tools directly. Linux: runs them with Wine.
#
# Usage: MSFS_SDK="/path/to/MSFS 2024 SDK" tools/build.sh
#        (tools/get-msfs-sdk.sh downloads and extracts the SDK on Linux/WSL)
set -euo pipefail

: "${MSFS_SDK:?Set MSFS_SDK to the 'MSFS 2024 SDK' folder}"
root="$(cd "$(dirname "$0")/.." && pwd)"
bin="$MSFS_SDK/WASM/llvm/bin"

case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*)
    run() { "$@"; }
    win() { cygpath -w "$1"; }
    ;;
  *)
    wine="${WINE:-$(command -v wine64 || command -v wine || echo /usr/lib/wine/wine64)}"
    export WINEDEBUG="${WINEDEBUG:--all}"
    run() { "$wine" "$@"; }
    win() { printf 'Z:%s' "$(printf '%s' "$(cd "$(dirname "$1")" && pwd)/$(basename "$1")" | tr '/' '\\')"; }
    ;;
esac

sysroot="$(win "$MSFS_SDK/WASM/wasi-sysroot")"
libdir="$sysroot\\lib\\wasm32-wasi"

mkdir -p "$root/build" "$root/modules"
cd "$root"

run "$bin/clang-cl.exe" /c /nologo \
  /O2 /Oi /Gy /GS- /permissive- /std:c++17 /W4 /WX -Wno-deprecated-declarations \
  /DNDEBUG /D_MSFS_WASM /D_STRING_H_CPLUSPLUS_98_CONFORMANCE_ /D_WCHAR_H_CPLUSPLUS_98_CONFORMANCE_ \
  /D_LIBCPP_NO_EXCEPTIONS /D_LIBCPP_HAS_NO_THREADS /D_MBCS \
  /Zc:__cplusplus /clang:-fstack-size-section /clang:-mbulk-memory --target=wasm32-unknown-wasi \
  "/clang:--sysroot=$sysroot" /clang:-fvisibility=hidden /clang:-ffunction-sections /clang:-fdata-sections \
  /clang:-fno-stack-protector /clang:-fno-exceptions /clang:-fms-extensions /clang:-fwritable-strings \
  -Werror=return-type -Wno-unused-command-line-argument -m32 \
  "/clang:-isystem$sysroot\\include" "/clang:-isystem$sysroot\\include\\c++\\v1" \
  "/clang:-isystem$(win "$MSFS_SDK/WASM/include")" "/clang:-isystem$(win "$MSFS_SDK/SimConnect SDK/include")" \
  "/Fobuild\\OansAutoZoom.o" "src\\OansAutoZoom.cpp"

run "$bin/wasm-ld.exe" --no-entry \
  --stack-guard-page --allow-undefined --export-dynamic --export malloc --export free \
  --export __wasm_call_ctors --export-table --export mallinfo --export mchunkit_begin --export mchunkit_next \
  --export get_pages_state --export mark_decommit_pages \
  -L "$libdir" -lc++ -lc++abi -lc "$libdir\\libclang_rt.builtins-wasm32.a" \
  -lc "$(win "$MSFS_SDK/WASM/WasmVersions/MSFS_WasmVersions.a")" \
  --export GetSimConnectVersion --export GetExtensionVersion --export GetExtensionVersionBuffer \
  --strip-debug --gc-sections -O3 --lto-O3 \
  "build\\OansAutoZoom.o" -o "modules\\oans_autozoom.wasm"

python3 "$root/tools/make_layout.py"
