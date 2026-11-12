#!/usr/bin/env bash
# Line counts for the core boundary baseline. Run from the repository root.
# total = wc -l; code = lines that are not blank and do not start with // (after whitespace).
set -eu
count() { for f in "$@"; do
  t=$(wc -l < "$f"); c=$(grep -cvE '^\s*(//.*)?$' "$f" || true)
  printf '%-70s total=%5d code=%5d\n' "$f" "$t" "$c"; done; }
echo "# Production files with a call into the generated core"
count src/main/WebsocketClientLite/Helper/DataframeParsing.cs \
      src/main/WebsocketClientLite/Service/WebsocketParserHandler.cs \
      src/main/WebsocketClientLite/Service/WebsocketSenderHandler.cs \
      src/main/WebsocketClientLite/Helper/ProtocolPayload.cs \
      src/main/WebsocketClientLite/Parser/HandshakeParser.cs \
      src/main/WebsocketClientLite/Helper/WebsocketMasking.cs
echo "# Hand-written extern implementation called by the generated core"
count src/main/WebsocketClientLite/Helper/VerifiedNativeCopy.cs
echo "# Generated core and Dafny sources"
count verification/generated/Masking.g.cs verification/Protocol.dfy verification/Masking.dfy
echo "# Benchmark"
count verification/benchmark/Program.cs
echo "# Test files that call the generated core directly"
count src/main/WebsocketClientLiteTest/ProtocolCoreTests.cs \
      src/main/WebsocketClientLiteTest/ProtocolStateTests.cs \
      src/main/WebsocketClientLiteTest/PayloadBoundaryTests.cs \
      src/main/WebsocketClientLiteTest/MaskingTests.cs
echo "# Totals (*.cs, excluding obj/ and bin/)"
for d in src/main/WebsocketClientLite src/main/WebsocketClientLiteTest; do
  files=$(find "$d" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | sort)
  n=$(echo "$files" | wc -l); t=$(cat $files | wc -l); c=$(cat $files | grep -cvE '^\s*(//.*)?$')
  printf '%-70s files=%3d total=%6d code=%6d\n' "$d" "$n" "$t" "$c"; done
echo "# Production call statements into the generated core (one line per call expression start)"
grep -rnE '\b(Protocol|Verified\.Masking)\.(Admit|Step|Merge|CloseStep|SendFailed|Outgoing|WriteHeader|CopyMasked|MaskSlice|SendStep|CloseValid|HandshakeValid|Apply)\(' \
  --include=*.cs src/main/WebsocketClientLite | grep -v '/obj/' | sort
echo "count=$(grep -rnE '\b(Protocol|Verified\.Masking)\.(Admit|Step|Merge|CloseStep|SendFailed|Outgoing|WriteHeader|CopyMasked|MaskSlice|SendStep|CloseValid|HandshakeValid|Apply)\(' --include=*.cs src/main/WebsocketClientLite | grep -vc '/obj/')"
echo "# Direct test calls into the generated core, per file"
grep -rnoE '\b(Protocol|Verified\.Masking|VerifiedProtocol\.Protocol|NativeCopy)\.[A-Z][A-Za-z]+\(' --include=*.cs src/main/WebsocketClientLiteTest | grep -v '/obj/' \
  | awk -F: '{split($3,a,"."); m=a[length(a)]; sub(/\(/,"",m); print $1" "m}' | sort | uniq -c
