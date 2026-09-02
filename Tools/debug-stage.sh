#!/usr/bin/env bash
# debug-stage.sh — 임의 스테이지를 즉시 열어 목표 진행도와 연결 영역을 본다.
#   ./Tools/debug-stage.sh 12 --play 30
set -uo pipefail
cd "$(dirname "$0")/.."
MONO="/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
"$MONO/mcs" Assets/Scripts/src/*.cs Verify/Debug.cs Verify/StageJson.cs -out:/tmp/chromadrop_debug.exe 2>/tmp/debug_build.log
if [ $? -ne 0 ]; then echo "BUILD FAIL"; grep "error CS" /tmp/debug_build.log | head -10; exit 2; fi
"$MONO/mono" /tmp/chromadrop_debug.exe "$@"
