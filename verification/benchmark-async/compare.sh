#!/usr/bin/env bash
# Builds the loopback benchmark twice, the net11.0 library with compiler state machines
# (WebsocketRuntimeAsync=false) and with Runtime Async (true), then runs the two builds in
# blocks of off, on, on, off. Each run prints its own medians; summarize.py combines them.
# A run fails if its library is not the variant it was meant to be: the "off" build must
# have async state machines and no runtime-async methods, the "on" build the reverse.
#
# Usage: verification/benchmark-async/compare.sh OUTPUT_DIR [BLOCKS, default 10]
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="${1:?usage: compare.sh OUTPUT_DIR [BLOCKS]}"
blocks="${2:-10}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
project="$root/verification/benchmark-async/AsyncBenchmark.csproj"
bin="$root/verification/benchmark-async/bin/Release/net11.0"

for variant in off on; do
  flag=false; [[ "$variant" == on ]] && flag=true
  dotnet build "$project" -c Release --no-incremental --disable-build-servers \
    -p:WebsocketRuntimeAsync="$flag" --nologo -v quiet > "$out/build-$variant.log"
  rm -rf "$out/bin-$variant"
  cp -r "$bin" "$out/bin-$variant"
done

run=0
for _ in $(seq "$blocks"); do
  for variant in off on on off; do
    run=$((run + 1))
    file="$out/run-$(printf '%02d' "$run")-$variant.csv"
    dotnet "$out/bin-$variant/AsyncBenchmark.dll" > "$file"
    if [[ "$variant" == off ]]; then expected='types: [1-9][0-9]*; methods marked async to the runtime: 0$'
    else expected='types: 0; methods marked async to the runtime: [1-9][0-9]*$'; fi
    grep -Eq "$expected" "$file" || { echo "run $run: library is not the $variant variant" >&2; exit 1; }
  done
done
echo "$run runs in $out"
