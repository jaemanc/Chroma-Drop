#!/usr/bin/env bash
# Chroma Drop — 안드로이드 빌드 (배치모드)
#
#   ./Tools/build-android.sh apk              # 사이드로드용 APK (디버그 서명)
#   ./Tools/build-android.sh aab              # 스토어 업로드용 AAB
#   ./Tools/build-android.sh apk --install    # 빌드 후 연결된 기기에 adb install
#   ./Tools/build-android.sh apk --fast       # Mono/ARMv7 — IL2CPP 없이 빠르게 확인만
#
# 릴리즈 서명은 환경변수로 (Tools/make-keystore.sh 로 생성):
#   export CHROMADROP_KEYSTORE=~/.keystores/chromadrop.keystore
#   export CHROMADROP_KEYSTORE_PASS=... CHROMADROP_KEYALIAS=chromadrop CHROMADROP_KEYALIAS_PASS=...
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

FORMAT="${1:-apk}"; shift || true
case "$FORMAT" in
  apk) METHOD="AndroidBuild.Apk" ;;
  aab) METHOD="AndroidBuild.Aab" ;;
  *) echo "사용법: $0 [apk|aab] [--install] [--fast] [--dev] [-appVersion X] [-versionCode N]" >&2; exit 2 ;;
esac

INSTALL=0
EXTRA_ARGS=()
for arg in "$@"; do
  case "$arg" in
    --install) INSTALL=1 ;;
    --fast)    EXTRA_ARGS+=(-scriptingBackend mono) ;;
    --dev)     EXTRA_ARGS+=(-development) ;;
    *)         EXTRA_ARGS+=("$arg") ;;
  esac
done

# --- Unity 에디터 찾기 -------------------------------------------------------
UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt | tr -d '\r')"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}"
[ -x "$UNITY" ] || { echo "Unity $UNITY_VERSION 를 찾을 수 없다: $UNITY (UNITY_PATH 로 지정 가능)" >&2; exit 1; }

# .../<version>/Unity.app/Contents/MacOS/Unity → .../<version>
EDITOR_ROOT="$(cd "$(dirname "$UNITY")/../../.." && pwd)"
if [ ! -d "$EDITOR_ROOT/PlaybackEngines/AndroidPlayer" ]; then
  echo "Android Build Support 모듈이 없다. 아래로 설치할 것:" >&2
  echo "  '/Applications/Unity Hub.app/Contents/MacOS/Unity Hub' -- --headless install-modules \\" >&2
  echo "      --version $UNITY_VERSION --module android --childModules" >&2
  exit 1
fi

mkdir -p Builds/Android Logs
LOG="$PROJECT_ROOT/Logs/build-android-$FORMAT.log"

echo "▶ $FORMAT 빌드 시작 (Unity $UNITY_VERSION) — 로그: $LOG"
set +e
"$UNITY" \
  -batchmode -nographics -quit \
  -projectPath "$PROJECT_ROOT" \
  -buildTarget Android \
  -logFile "$LOG" \
  -executeMethod "$METHOD" \
  ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}
STATUS=$?
set -e

if [ $STATUS -ne 0 ]; then
  echo "✗ 빌드 실패 (exit $STATUS). 로그 마지막 40줄:" >&2
  tail -40 "$LOG" >&2
  exit $STATUS
fi

OUTPUT="$(sed -n 's/^CHROMADROP_BUILD_OUTPUT=//p' "$LOG" | tail -1)"
echo "✓ 빌드 성공: ${OUTPUT:-Builds/Android}"
[ -n "$OUTPUT" ] && ls -lh "$OUTPUT" | awk '{print "  크기:", $5}'

if [ "$INSTALL" = "1" ]; then
  [ "$FORMAT" = "apk" ] || { echo "AAB 는 adb install 로 설치할 수 없다 (bundletool 필요)." >&2; exit 1; }
  ADB="${ADB_PATH:-$EDITOR_ROOT/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb}"
  [ -x "$ADB" ] || ADB="$(command -v adb || true)"
  [ -n "$ADB" ] || { echo "adb 를 찾을 수 없다." >&2; exit 1; }
  echo "▶ 기기에 설치 중..."
  "$ADB" install -r "$OUTPUT"
  echo "✓ 설치 완료 — 기기에서 Chroma Drop 실행"
fi
