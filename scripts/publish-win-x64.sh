#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/src/HsAsrDictation/HsAsrDictation.csproj"
OUTPUT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/artifacts/publish/win-x64"

dotnet publish "$PROJECT" \
  -c "$CONFIGURATION" \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:DebugType=None \
  /p:DebugSymbols=false \
  -o "$OUTPUT"

echo "Publish finished: $OUTPUT"
