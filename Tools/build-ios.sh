#!/usr/bin/env bash
# Chroma Drop — iOS 빌드 (배치모드).
#
#   ./Tools/build-ios.sh          # Unity → Builds/iOS 에 Xcode 프로젝트 생성
#   ./Tools/build-ios.sh --open   # 생성 후 Xcode 로 열기
#
# Unity 는 Xcode 프로젝트까지만 만든다. .app/.ipa 는 그 프로젝트를 Xcode 로 빌드해야 나오고,
# 실기기 설치·배포에는 Apple 개발자 서명이 필요하다 (여기서는 하지 않는다).
# Unity 에디터가 열려 있으면 배치모드가 실패한다.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

OPEN=0
for arg in "$@"; do [ "$arg" = "--open" ] && OPEN=1; done

UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt | tr -d '\r')"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}"
[ -x "$UNITY" ] || { echo "Unity $UNITY_VERSION 를 찾을 수 없다: $UNITY (UNITY_PATH 로 지정 가능)" >&2; exit 1; }

EDITOR_ROOT="$(cd "$(dirname "$UNITY")/../../.." && pwd)"
if [ ! -d "$EDITOR_ROOT/PlaybackEngines/iOSSupport" ]; then
  echo "iOS Build Support 모듈이 없다. 아래로 설치할 것:" >&2
  echo "  '/Applications/Unity Hub.app/Contents/MacOS/Unity Hub' -- --headless install-modules \\" >&2
  echo "      --version $UNITY_VERSION --module ios" >&2
  exit 1
fi

mkdir -p Builds Logs
LOG="$PROJECT_ROOT/Logs/build-ios.log"

echo "▶ iOS Xcode 프로젝트 생성 (Unity $UNITY_VERSION) — 로그: $LOG"
set +e
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PROJECT_ROOT" \
  -buildTarget iOS \
  -logFile "$LOG" \
  -executeMethod IosBuild.Xcode
STATUS=$?
set -e

if [ $STATUS -ne 0 ]; then
  echo "✗ 빌드 실패 (exit $STATUS). 로그 마지막 40줄:" >&2
  tail -40 "$LOG" >&2
  exit $STATUS
fi

echo "✓ Xcode 프로젝트: Builds/iOS"
echo "  다음 단계 (전체 Xcode 필요):"
echo "    open Builds/iOS/Unity-iPhone.xcworkspace"
echo "    Xcode에서 Unity-iPhone → Any iOS Device → Product > Archive 선택"
[ "$OPEN" = "1" ] && open Builds/iOS/Unity-iPhone.xcworkspace
exit 0
