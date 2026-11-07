#!/usr/bin/env bash
# Standalone tooling: no dependency on the production solution.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
# Build output stays outside source identity; exec forwards cancellation directly.
dotnet build "$root/tools/Pdd/Pdd.csproj" -c Release --nologo -v quiet >&2
exec dotnet "$root/tools/Pdd/bin/Release/net10.0/Pdd.dll" "$@"
