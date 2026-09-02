#!/usr/bin/env bash
# Chroma Drop — 고친 코드를 확인하기까지 한 번에.
#
#   ./Tools/dev.sh            # 코어 테스트 → PlayMode → 빌드 → 실행
#   ./Tools/dev.sh --fast     # 테스트 건너뛰고 빌드+실행만
#   ./Tools/dev.sh --test     # 테스트만 (빌드 안 함)
#
# 테스트가 깨지면 빌드하지 않는다 — 깨진 걸 띄워놓고 보는 게 제일 헷갈린다.
# Unity 에디터가 열려 있으면 배치모드가 실패하므로 미리 닫을 것.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt | tr -d '\r')"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}"
MONO="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
[ -x "$UNITY" ] || { echo "Unity $UNITY_VERSION 를 찾을 수 없다: $UNITY" >&2; exit 1; }

# 사내 TLS 프록시 뒤에서는 패키지 매니저가 막힌다 (COMMANDS.md §6)
if [ -z "${NODE_EXTRA_CA_CERTS:-}" ] && [ -f "$HOME/.certs/ptkroea.pem" ]; then
  export NODE_EXTRA_CA_CERTS="$HOME/.certs/ptkroea.pem"
fi

RUN_TESTS=1; RUN_BUILD=1
for a in "$@"; do
  case "$a" in
    --fast) RUN_TESTS=0 ;;
    --test) RUN_BUILD=0 ;;
    *) echo "사용법: $0 [--fast|--test]" >&2; exit 2 ;;
  esac
done

# 같은 프로젝트에서 Unity 가 이미 돌고 있으면 붙지 않는다.
# 두 인스턴스가 한 프로젝트를 잡으면 조용히 꼬여서, 실패한 것처럼 보이는데 실제로는
# 앞의 것이 아직 도는 중인 상황이 된다.
if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null; then
  echo "✗ Unity 가 이미 실행 중이다. 끝나기를 기다리거나 에디터를 닫을 것:" >&2
  pgrep -fl "Unity.app/Contents/MacOS/Unity" | head -3 >&2
  exit 1
fi

mkdir -p Logs
pkill -f "Builds/Mac/ChromaDrop.app" 2>/dev/null

if [ "$RUN_TESTS" = "1" ]; then
  echo "▶ 코어 규칙 테스트"
  if ! "$MONO/mcs" Assets/Scripts/ColorMatcherCore.cs Tests/CoreTests.cs -out:/tmp/core_tests.exe 2>&1 | grep -v warning; then :; fi
  if ! "$MONO/mono" /tmp/core_tests.exe | tail -3; then
    echo "✗ 코어 테스트 실패" >&2; exit 1
  fi

  echo "▶ PlayMode 테스트 (컴파일 검증 겸)"
  "$UNITY" -batchmode -runTests -testPlatform PlayMode -projectPath . \
    -testResults /tmp/dev_results.xml -logFile Logs/dev-test.log >/dev/null 2>&1

  if grep -q "error CS" Logs/dev-test.log; then
    echo "✗ 컴파일 에러:" >&2
    grep "error CS" Logs/dev-test.log | sort -u | head -10 >&2
    exit 1
  fi
  python3 - <<'PY' || exit 1
import sys, xml.etree.ElementTree as ET
try:
    r = ET.parse('/tmp/dev_results.xml').getroot()
except Exception as e:
    print("✗ 테스트 결과를 읽지 못했다:", e); sys.exit(1)
bad = [tc for tc in r.iter('test-case') if tc.get('result') not in ('Passed', 'Skipped')]
print(f"  {r.get('passed')} 통과 / {r.get('failed')} 실패 / {r.get('skipped')} 건너뜀")
for tc in bad:
    m = tc.find('.//message')
    print("  ✗", tc.get('name'), "—", (m.text or '').strip()[:120] if m is not None else '')
sys.exit(1 if bad else 0)
PY
fi

if [ "$RUN_BUILD" = "1" ]; then
  echo "▶ 맥 빌드"
  "$UNITY" -batchmode -nographics -quit -projectPath . \
    -buildTarget OSXUniversal -logFile Logs/build-mac.log -executeMethod MacBuild.App
  if [ $? -ne 0 ]; then
    echo "✗ 빌드 실패. 로그 마지막 40줄:" >&2
    tail -40 Logs/build-mac.log >&2
    exit 1
  fi
  OUT="$(sed -n 's/^CHROMADROP_BUILD_OUTPUT=//p' Logs/build-mac.log | tail -1)"
  echo "✓ ${OUT:-Builds/Mac/ChromaDrop.app}"
  open "${OUT:-Builds/Mac/ChromaDrop.app}"
fi
