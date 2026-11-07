#!/bin/sh
# herdr-devcontainer-via.sh [--ssh <ssh-target>] <checkout path> <shell line>
#
# Connection helper for the Herdr desktop app: lets herdr-core (hc) use this
# repository's dev container as a machine. hc runs the helper with the shell
# line it wants to run as the last argument; the helper runs that line inside
# the dev container with docker exec, either on this machine or, with --ssh, on
# a remote Docker host. The container needs no sshd and no published port.
#
# <checkout path> is the absolute path of the checkout on the Docker host, the
# folder the dev container was opened from. A stopped container is started with
# the devcontainer CLI when the Docker host has it.
#
# Example (see docs/development.md):
#   hc machine add --name wscl --install \
#     --via "$PWD/scripts/herdr-devcontainer-via.sh $PWD"
#   hc machine add --name wscl-devbox --install \
#     --via "$PWD/scripts/herdr-devcontainer-via.sh --ssh me@devbox /home/me/src/WebsocketClientLite.PCL"
#
# Paths with spaces are not supported: hc splits the --via command on spaces.
set -eu

usage() { echo "usage: herdr-devcontainer-via.sh [--ssh <ssh-target>] <checkout path> <shell line>" >&2; exit 2; }

target=""
if [ "${1:-}" = "--ssh" ]; then
    [ $# -ge 2 ] || usage
    target=$2
    shift 2
fi
[ $# -eq 2 ] || usage
dir=$1 line=$2
case $dir in /*) ;; *) echo "checkout path must be absolute: $dir" >&2; exit 2 ;; esac
case $dir in *[!A-Za-z0-9._/-]*) echo "unsupported characters in checkout path: $dir" >&2; exit 2 ;; esac

# Single-quote a value for a POSIX shell.
q() { printf "'%s'" "$(printf '%s' "$1" | sed "s/'/'\\\\''/g")"; }

# Runs on the Docker host. docker exec sets only HOME, so SHELL (the container
# user's login shell), USER and a UTF-8 LANG are passed in: herdr-core starts
# each terminal with $SHELL and would otherwise fall back to /bin/sh.
remote="dir=$(q "$dir") line=$(q "$line")"'
[ -d "$dir" ] || { echo "no checkout at $dir" >&2; exit 1; }
find_c() { docker ps -q --filter "label=devcontainer.local_folder=$dir"; }
c=$(find_c)
if [ -z "$c" ]; then
    if command -v devcontainer >/dev/null 2>&1; then
        log=${TMPDIR:-/tmp}/devcontainer-up-$(basename "$dir").log
        devcontainer up --workspace-folder "$dir" >"$log" 2>&1 \
            || { echo "starting the dev container failed; see $log" >&2; exit 1; }
        c=$(find_c)
    else
        echo "no running dev container for $dir; open it in VS Code first" >&2; exit 1
    fi
fi
[ "$(printf "%s\n" "$c" | grep -c .)" -eq 1 ] || { echo "expected one dev container for $dir" >&2; exit 1; }
user=$(docker inspect -f "{{index .Config.Labels \"devcontainer.metadata\"}}" "$c" \
    | grep -o "\"remoteUser\":\"[^\"]*\"" | tail -n 1 | cut -d "\"" -f 4)
user=${user:-vscode}
shell=$(docker exec -u "$user" "$c" sh -c "getent passwd \"\$(id -un)\" | cut -d: -f7")
shell=${shell:-/bin/bash}
folder=$(docker inspect -f "{{range .Mounts}}{{if eq .Source \"$dir\"}}{{.Destination}}{{end}}{{end}}" "$c")
exec docker exec -i -u "$user" -w "${folder:-/}" \
    -e SHELL="$shell" -e USER="$user" -e LANG=C.UTF-8 "$c" "$shell" -c "$line"
'

if [ -n "$target" ]; then
    # sh -c, so the remote user's login shell (zsh, fish, ...) does not matter.
    exec ssh -T -o BatchMode=yes -o ConnectTimeout=10 -o ServerAliveInterval=15 "$target" "sh -c $(q "$remote")"
else
    exec sh -c "$remote"
fi
