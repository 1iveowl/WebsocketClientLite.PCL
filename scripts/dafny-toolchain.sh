#!/usr/bin/env bash
# Shared checksum-pinned Linux x64 toolchain bootstrap. Prints its cache directory.
set -euo pipefail
version=4.11.0
checksum=a46a9ff7cdd720f7955854c78e95df13f4cfe6b80691b05f8654fe19e8267179
tool_dir="${XDG_CACHE_HOME:-$HOME/.cache}/websocketclientlite/dafny-$version"
if [[ ! -f "$tool_dir/.verified-archive" ]]; then
  [[ "$(uname -s)-$(uname -m)" == Linux-x86_64 ]] || { echo 'Pinned generator requires Linux x64 (use the dev container).' >&2; exit 1; }
  temporary=$(mktemp -d)
  trap 'rm -rf "$temporary"' EXIT
  curl --fail --location --silent --show-error "https://github.com/dafny-lang/dafny/releases/download/v$version/dafny-$version-x64-ubuntu-22.04.zip" -o "$temporary/dafny.zip"
  echo "$checksum  $temporary/dafny.zip" | sha256sum --check >&2
  mkdir -p "$tool_dir"
  unzip -q -o "$temporary/dafny.zip" -d "$tool_dir"
  touch "$tool_dir/.verified-archive"
fi
printf '%s\n' "$tool_dir"
