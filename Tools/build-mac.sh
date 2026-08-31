#!/usr/bin/env bash
# Chroma Drop — 맥 로컬 확인용 빌드 (배치모드).
#
#   ./Tools/build-mac.sh          # 빌드만
#   ./Tools/build-mac.sh --run    # 빌드 후 실행
#
# 서명·공증을 하지 않으므로 로컬 확인 전용이다. 배포하면 Gatekeeper 가 막는다.
# Unity 에디터가 열려 있으면 배치모드가 실패한다.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

RUN=0
for arg in "$@"; do [ "$arg" = "--run" ] && RUN=1; done

UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt | tr -d '\r')"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}"
[ -x "$UNITY" ] || { echo "Unity $UNITY_VERSION 를 찾을 수 없다: $UNITY" >&2; exit 1; }

# 사내 TLS 프록시 뒤에서는 패키지 매니저가 막힌다 (COMMANDS.md §6).
if [ -z "${NODE_EXTRA_CA_CERTS:-}" ] && [ -f "$HOME/.certs/ptkroea.pem" ]; then
  export NODE_EXTRA_CA_CERTS="$HOME/.certs/ptkroea.pem"
fi

mkdir -p Builds/Mac Logs
LOG="$PROJECT_ROOT/Logs/build-mac.log"

echo "▶ 맥 빌드 시작 (Unity $UNITY_VERSION) — 로그: $LOG"
set +e
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PROJECT_ROOT" \
  -buildTarget OSXUniversal \
  -logFile "$LOG" \
  -executeMethod MacBuild.App
STATUS=$?
set -e

if [ $STATUS -ne 0 ]; then
  echo "✗ 빌드 실패 (exit $STATUS). 로그 마지막 40줄:" >&2
  tail -40 "$LOG" >&2
  exit $STATUS
fi

OUTPUT="$(sed -n 's/^CHROMADROP_BUILD_OUTPUT=//p' "$LOG" | tail -1)"
echo "✓ 빌드 성공: ${OUTPUT:-Builds/Mac/ChromaDrop.app}"
[ "$RUN" = "1" ] && open "${OUTPUT:-Builds/Mac/ChromaDrop.app}"
exit 0
