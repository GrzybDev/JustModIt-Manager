#!/bin/sh
# The Linux half of measure-agent.ps1, run on the measured machine from the folder the app was copied to. It starts the
# agent with an empty data folder, prints its memory once the first library scan and catalogue refresh are saved and
# again at the given moments, and fails when it maps one of the window's libraries. However it ends, it stops the agent
# and removes the agent's data and runtime files and the copied folder.
#
# Usage: measure-agent.sh <executable> <vendor id> <instance name> <timeout s> <second reading s> <last reading s>
#        [<window library>...]
# Each reading is a line "@reading <private> <private working set> <working set> <threads> <handles>", sizes in kB.
set -u

dir=$(cd "$(dirname "$0")" && pwd -P)
exe=$1 vendor=$2 instance=$3 timeout=$4 second=$5 last=$6
shift 6
libraries=$*

# The folders AppDataLocations gives on Linux, which ignore a relative XDG variable as the specification says.
xdg() { case "$1" in /*) echo "$1" ;; *) echo "$HOME/$2" ;; esac }
data=$(xdg "${XDG_DATA_HOME:-}" .local/share)/${vendor:?}/${instance:?}
cache=$(xdg "${XDG_CACHE_HOME:-}" .cache)/$vendor/$instance
state=$(xdg "${XDG_STATE_HOME:-}" .local/state)/$vendor/$instance
runtime=${XDG_RUNTIME_DIR:-}
# Velopack's library logs here when the app does not run from an AppImage; a log that was there stays.
velopackLog=${TMPDIR:-/tmp}/velopack.log
[ ! -e "$velopackLog" ] || velopackLog=
pid=

clean() {
    if [ -n "$pid" ]; then kill -KILL "$pid" 2>/dev/null; wait "$pid" 2>/dev/null; fi
    # The agent may have started its window, from the same executable.
    for process in /proc/[0-9]*; do
        [ "$(readlink "$process/exe" 2>/dev/null)" != "$dir/$exe" ] || kill -KILL "${process#/proc/}" 2>/dev/null
    done
    rm -rf "$data" "$cache" "$state"
    rmdir "${data%/*}" "${cache%/*}" "${state%/*}" 2>/dev/null
    [ -z "$runtime" ] || rm -f "$runtime/$instance.Agent.lock" "$runtime/$instance.Ui.lock" "$runtime/$instance.sock"
    [ -z "$velopackLog" ] || rm -f "$velopackLog"
    # Only the copy measure-agent.ps1 made, never a folder this script was copied to by hand.
    case "${dir##*/}" in jmi-footprint-*) rm -rf "$dir" ;; esac
}
trap clean EXIT
trap 'exit 130' HUP INT TERM

running() { [ -e "/proc/$pid" ] && ! grep -q '^State:[[:space:]]*Z' "/proc/$pid/status" 2>/dev/null; }

stopped() {
    wait "$pid"
    echo "The agent exited with code $?; another development build of $exe may be running. Its output and logs:"
    pid=
    # clean() removes the logs, which say why the agent stopped.
    cat "$dir/agent.out"
    tail -n 20 "$state"/logs/* 2>/dev/null
    exit 3
}

reading() {
    running || stopped
    loaded=$(awk 'NF > 5 { print $6 }' "/proc/$pid/maps" | sort -u | while read -r path; do
        for library in $libraries; do
            case "${path##*/}" in "$library".so*) echo "${path##*/}" ;; esac
        done
    done)
    if [ -n "$loaded" ]; then
        echo "The agent loaded the window's native libraries: $(echo $loaded | sed 's/ /, /g')."
        exit 3
    fi
    awk -v handles="$(ls "/proc/$pid/fd" | wc -l)" '
        /^(RssAnon|VmSwap):/ { private += $2 }
        /^Private_(Clean|Dirty):/ { privateSet += $2 }
        /^VmRSS:/ { set = $2 }
        /^Threads:/ { threads = $2 }
        END { print "@reading", private + 0, privateSet + 0, set + 0, threads + 0, handles + 0 }
    ' "/proc/$pid/status" "/proc/$pid/smaps_rollup"
}

# A shell runs a trap only once its foreground command ends, so short sleeps let a hangup stop the agent at once.
wait_until() {
    while [ "$(date +%s)" -lt "$1" ]; do sleep 1; done
}

rm -rf "$data" "$cache" "$state"
chmod u+x "$dir/$exe"
# Its own session keeps the agent out of the terminal's signals, as a service is; clean() stops it.
setsid "$dir/$exe" --agent </dev/null >"$dir/agent.out" 2>&1 &
pid=$!

deadline=$(($(date +%s) + timeout))
until [ -e "$data/state/library.json" ] && [ -e "$cache/catalogue/index.json" ]; do
    running || stopped
    if [ "$(date +%s)" -gt "$deadline" ]; then
        echo "The first scan and catalogue refresh took longer than $timeout s."
        exit 3
    fi
    sleep 1
done

ready=$(date +%s)
reading
wait_until $((ready + second))
reading
wait_until $((ready + last))
reading
