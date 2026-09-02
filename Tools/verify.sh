#!/usr/bin/env bash
# verify.sh — SPEC VERIFY 24항목 실행. 항목번호 + PASS/FAIL 만 출력한다.
set -uo pipefail
cd "$(dirname "$0")/.."

MONO="/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
OUT=/tmp/chromadrop_verify.exe

"$MONO/mcs" -langversion:latest Assets/Scripts/src/*.cs Verify/Verify.cs Verify/StageJson.cs \
  -out:"$OUT" 2>/tmp/verify_build.log
if [ $? -ne 0 ]; then
  echo "BUILD FAIL"
  grep "error CS" /tmp/verify_build.log | head -20
  exit 2
fi

"$MONO/mono" "$OUT"
