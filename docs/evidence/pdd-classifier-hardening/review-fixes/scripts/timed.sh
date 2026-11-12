#!/usr/bin/env bash
# usage: timed.sh <evidence-prefix> <command...>
# Records the process list (names only) just before the command, under the shared timing lock.
prefix="$1"; shift
exec flock "$HOME/worktrees/.timing.lock" bash -c '
prefix="$1"; shift
{ date -u +"%Y-%m-%dT%H:%M:%SZ lock acquired"; ps -eo pid,etime,comm | grep -E "claude|dotnet" | grep -v grep; } > "$prefix.ps.txt"
start=$(date +%s%N)
"$@" > "$prefix.log" 2>&1
code=$?
end=$(date +%s%N)
echo "exit=$code elapsed=$(( (end - start) / 1000000 ))ms" >> "$prefix.log"
date -u +"%Y-%m-%dT%H:%M:%SZ lock released" >> "$prefix.ps.txt"
exit $code
' _ "$prefix" "$@"
