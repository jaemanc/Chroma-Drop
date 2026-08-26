#!/usr/bin/env bash
# Chroma Drop — 릴리즈 서명용 키스토어 생성 (1회만).
#
#   ./Tools/make-keystore.sh                       # ~/.keystores/chromadrop.keystore 생성
#   KEYSTORE_PASS=... KEYALIAS_PASS=... ./Tools/make-keystore.sh   # 비대화식
#
# 생성된 키는 절대 저장소에 커밋하지 말 것. 분실하면 같은 앱으로 업데이트할 수 없다.
set -euo pipefail

KEYSTORE="${KEYSTORE:-$HOME/.keystores/chromadrop.keystore}"
ALIAS="${KEYALIAS:-chromadrop}"

if [ -f "$KEYSTORE" ]; then
  echo "이미 존재한다: $KEYSTORE (덮어쓰지 않음)"; exit 0
fi

UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$(dirname "${BASH_SOURCE[0]}")/../ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
KEYTOOL="/Applications/Unity/Hub/Editor/$UNITY_VERSION/PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool"
[ -x "$KEYTOOL" ] || KEYTOOL="$(command -v keytool || true)"
[ -n "$KEYTOOL" ] || { echo "keytool 을 찾을 수 없다 (Android 모듈 또는 JDK 설치 필요)." >&2; exit 1; }

if [ -z "${KEYSTORE_PASS:-}" ]; then read -rsp "키스토어 비밀번호(6자 이상): " KEYSTORE_PASS; echo; fi
KEYALIAS_PASS="${KEYALIAS_PASS:-$KEYSTORE_PASS}"

mkdir -p "$(dirname "$KEYSTORE")"
"$KEYTOOL" -genkeypair -v \
  -keystore "$KEYSTORE" -storetype PKCS12 \
  -alias "$ALIAS" -keyalg RSA -keysize 2048 -validity 10950 \
  -storepass "$KEYSTORE_PASS" -keypass "$KEYALIAS_PASS" \
  -dname "CN=jaemanc, OU=ChromaDrop, O=jaemanc, L=Seoul, C=KR"
chmod 600 "$KEYSTORE"

cat <<MSG

✓ 생성 완료: $KEYSTORE

빌드 전에 아래를 셸에 export (또는 ~/.zshrc 에 추가):

  export CHROMADROP_KEYSTORE="$KEYSTORE"
  export CHROMADROP_KEYSTORE_PASS='<비밀번호>'
  export CHROMADROP_KEYALIAS="$ALIAS"
  export CHROMADROP_KEYALIAS_PASS='<비밀번호>'
MSG
