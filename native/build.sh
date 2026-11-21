#!/usr/bin/env sh
# Builds the native shims into native/bin/<rid>: build.sh [rid] [shim...], by default every shim the system ships.
set -eu

root=$(cd "$(dirname "$0")" && pwd)
rid=${1:-}
[ $# -eq 0 ] || shift
if [ -z "$rid" ]; then
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) rid=linux-x64 ;;
    Linux-aarch64) rid=linux-arm64 ;;
    Darwin-arm64) rid=osx-arm64 ;;
    Darwin-x86_64) rid=osx-x64 ;;
    *) echo "unsupported platform: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
  esac
fi
case "$rid" in linux-*) every="xd3shim avifshim" ;; *) every=xd3shim ;; esac
shims=${*:-$every}
build=$root/build/$rid
bin=$root/bin/$rid
mkdir -p "$bin"

# A shim that exported the static libraries inside it would let a system copy of one take their place.
exports_only() {
  if nm -D --defined-only "$bin/$1" | awk '{print $3}' | grep -v "^$2" | grep -q .; then
    echo "$1 exports more than its $2 functions" >&2; exit 1
  fi
}

for shim in $shims; do
  case "$shim" in
    xd3shim)
      if [ ! -f "$root/xdelta/xdelta3/xdelta3.h" ] || [ ! -f "$root/xz/CMakeLists.txt" ]; then
        git -C "$root" submodule update --init --depth 1 -- xdelta xz
      fi
      cmake -S "$root/xd3shim" -B "$build/xd3shim" -DCMAKE_BUILD_TYPE=Release
      cmake --build "$build/xd3shim"
      case "$rid" in
        osx-*) cp "$build/xd3shim/libxd3shim.dylib" "$bin/" ;;
        *) cp "$build/xd3shim/libxd3shim.so" "$bin/"; exports_only libxd3shim.so xds_ ;;
      esac ;;
    avifshim)
      if [ ! -f "$root/libavif/CMakeLists.txt" ] || [ ! -f "$root/dav1d/meson.build" ]; then
        git -C "$root" submodule update --init --depth 1 -- libavif dav1d
      fi
      # dav1d's x86 assembly needs nasm; without it dav1d decodes in plain C, slower but enough for stills.
      asm=true
      case "$rid" in *-x64) command -v nasm >/dev/null 2>&1 || asm=false ;; esac
      [ -f "$build/dav1d/build.ninja" ] || meson setup "$build/dav1d" "$root/dav1d" --buildtype=release \
        --default-library=static -Denable_asm=$asm -Denable_tools=false -Denable_tests=false -Denable_examples=false
      meson compile -C "$build/dav1d"
      cmake -S "$root/avifshim" -B "$build/avifshim" -DCMAKE_BUILD_TYPE=Release -DDAV1D_BUILD_DIR="$build/dav1d"
      cmake --build "$build/avifshim"
      cp "$build/avifshim/libavifshim.so" "$bin/"
      exports_only libavifshim.so avs_ ;;
    *) echo "unknown shim: $shim" >&2; exit 1 ;;
  esac
  echo "$shim -> $bin"
done
