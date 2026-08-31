#!/usr/bin/env bash
# Chroma Drop — 랭킹 서버 설정 파일 생성.
#
#   export CHROMADROP_FIREBASE_URL="https://<프로젝트>-default-rtdb.firebaseio.com"
#   export CHROMADROP_FIREBASE_APIKEY="<웹 API 키>"
#   ./Tools/make-leaderboard-config.sh
#
# 생성 위치: Assets/Resources/leaderboard.json (.gitignore 등록됨 — 커밋하지 않는다)
# 파일이 없으면 게임은 정상 동작하고 랭킹 기능만 꺼진다.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/Assets/Resources/leaderboard.json"

URL="${CHROMADROP_FIREBASE_URL:-}"
KEY="${CHROMADROP_FIREBASE_APIKEY:-}"
[ -n "$URL" ] || { echo "CHROMADROP_FIREBASE_URL 이 비어 있다." >&2; exit 1; }
[ -n "$KEY" ] || { echo "CHROMADROP_FIREBASE_APIKEY 가 비어 있다." >&2; exit 1; }

mkdir -p "$(dirname "$OUT")"
cat > "$OUT" <<JSON
{
  "databaseUrl": "${URL%/}",
  "apiKey": "$KEY"
}
JSON
echo "생성됨: Assets/Resources/leaderboard.json"
echo "(값은 출력하지 않는다. 확인이 필요하면 파일을 직접 열어볼 것.)"
