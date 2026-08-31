#!/usr/bin/env bash
# Chroma Drop — 랭킹 서버(Firestore) 설정 파일 생성.
#
#   export CHROMADROP_FIREBASE_PROJECT="<프로젝트 ID>"
#   export CHROMADROP_FIREBASE_APIKEY="<웹 API 키>"
#   ./Tools/make-leaderboard-config.sh
#
# .env 가 있으면 거기서 projectId/apiKey 를 읽어 쓴다:
#   ./Tools/make-leaderboard-config.sh .env
#
# 생성 위치: Assets/Resources/leaderboard.json (.gitignore 등록됨 — 커밋하지 않는다)
# 파일이 없으면 게임은 정상 동작하고 랭킹 기능만 꺼진다.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/Assets/Resources/leaderboard.json"

PROJ="${CHROMADROP_FIREBASE_PROJECT:-}"
KEY="${CHROMADROP_FIREBASE_APIKEY:-}"

# .env 를 넘기면 거기서 채운다 (값은 출력하지 않는다)
if [ $# -ge 1 ] && [ -f "$1" ]; then
  # BSD sed 는 \? 를 지원하지 않아 python 으로 뽑는다. KEY=VALUE 와 "key": "value" 둘 다 받는다.
  val() {
    python3 -c '
import re,sys
key,path=sys.argv[1],sys.argv[2]
pat=re.compile(r"^\s*[\"\x27]?"+re.escape(key)+r"[\"\x27]?\s*[:=]\s*(.+?)\s*,?\s*$")
for line in open(path):
    m=pat.match(line)
    if m: print(m.group(1).strip().strip("\"").strip("\x27")); break
' "$1" "$2"
  }
  [ -n "$PROJ" ] || PROJ="$(val projectId "$1")"
  [ -n "$KEY" ]  || KEY="$(val apiKey "$1")"
fi

[ -n "$PROJ" ] || { echo "projectId 를 찾을 수 없다 (CHROMADROP_FIREBASE_PROJECT 또는 .env)." >&2; exit 1; }
[ -n "$KEY" ]  || { echo "apiKey 를 찾을 수 없다 (CHROMADROP_FIREBASE_APIKEY 또는 .env)." >&2; exit 1; }

mkdir -p "$(dirname "$OUT")"
cat > "$OUT" <<JSON
{
  "projectId": "$PROJ",
  "apiKey": "$KEY"
}
JSON
echo "생성됨: Assets/Resources/leaderboard.json (projectId=$PROJ, apiKey=<${#KEY}자>)"
echo "(키 값은 출력하지 않는다. .gitignore 에 등록돼 있으니 커밋되지 않는다.)"
