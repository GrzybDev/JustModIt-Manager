#!/bin/sh
# Installs a release's AppImage for the current user, or replaces it, and starts the app. package.ps1 writes each
# release's copy, with the release's values and the app's texts filled in.
set -eu

release=@Release@
appimage=@AppImage@
unit=@Unit@
folder=$HOME/Applications

say() { printf '%s\n' "$1"; }

download() {
    if command -v curl >/dev/null 2>&1; then
        curl -fL --progress-bar -o "$2" "$1"
    elif command -v wget >/dev/null 2>&1; then
        wget -nv --show-progress -O "$2" "$1"
    else
        say @NoDownloader@
        exit 1
    fi
}

# sh runs a script piped to it as it arrives, so a download cut short must not get as far as a command.
main() {
    [ "$(id -u)" -ne 0 ] || { say @AsRoot@; exit 1; }
    [ "$(uname -m)" = x86_64 ] || { say @Unsupported@; exit 1; }
    command -v fusermount3 >/dev/null 2>&1 || command -v fusermount >/dev/null 2>&1 || { say @NoFuse@; exit 1; }

    mkdir -p "$folder"
    # Downloaded beside its place, so putting it there is a rename, which a running copy survives.
    part=$(mktemp "$folder/.$appimage.XXXXXX")
    trap 'rm -f "$part" "$part.sums"' EXIT
    trap 'exit 1' HUP INT TERM

    say @Downloading@
    download "$release/checksums.txt" "$part.sums"
    download "$release/$appimage" "$part"
    [ "$(awk -v name="$appimage" '$2 == name { print $1 }' "$part.sums")" = "$(sha256sum "$part" | cut -d ' ' -f 1)" ] ||
        { say @Damaged@; exit 1; }
    chmod 755 "$part"
    mv -f "$part" "$folder/$appimage"

    # A running agent goes on from the file it started from, whose version a window of the new one cannot talk to.
    systemctl --user try-restart "$unit" >/dev/null 2>&1 || true
    say @Done@
    setsid -f "$folder/$appimage" </dev/null >/dev/null 2>&1
}

main
