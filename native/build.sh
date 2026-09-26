#!/usr/bin/env sh
set -eu

root=$(cd "$(dirname "$0")" && pwd)
rid=${1:-}
if [ -z "$rid" ]; then
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) rid=linux-x64 ;;
    Linux-aarch64) rid=linux-arm64 ;;
    Darwin-arm64) rid=osx-arm64 ;;
    Darwin-x86_64) rid=osx-x64 ;;
    *) echo "unsupported platform: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
  esac
fi

if [ ! -f "$root/xdelta/xdelta3/xdelta3.h" ] || [ ! -f "$root/xz/CMakeLists.txt" ]; then
  git -C "$root" submodule update --init --depth 1 -- xdelta xz
fi

cmake -S "$root/xd3shim" -B "$root/build/$rid" -DCMAKE_BUILD_TYPE=Release
cmake --build "$root/build/$rid"

mkdir -p "$root/bin/$rid"
case "$rid" in
  osx-*) cp "$root/build/$rid/libxd3shim.dylib" "$root/bin/$rid/" ;;
  *) cp "$root/build/$rid/libxd3shim.so" "$root/bin/$rid/" ;;
esac
echo "xd3shim -> $root/bin/$rid"
