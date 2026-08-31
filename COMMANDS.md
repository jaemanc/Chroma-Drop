# 커맨드 모음 — Chroma Drop

이 저장소에서 실제로 쓴 명령들. 복붙해서 쓰면 된다.
프로젝트 규칙은 [CLAUDE.md](CLAUDE.md) 참고.

## 0. 이 머신의 전제

| 항목 | 값 |
|---|---|
| Unity | `6000.5.2f1` (`/Applications/Unity/Hub/Editor/6000.5.2f1`) |
| 설치된 플랫폼 모듈 | AndroidPlayer, MacStandaloneSupport, WebGLSupport |
| dotnet/mono | 없음 — **Unity 내장 mono** 를 쓴다 |
| adb | PATH 에 없음 — **Unity 내장 adb** 를 쓴다 |
| 네트워크 | 사내 TLS 검사 프록시 뒤 (§6 참고) |

자주 쓰는 경로는 셸 프로파일(`~/.zshrc`)에 넣어두면 편하다.

```bash
export UNITY_ROOT="/Applications/Unity/Hub/Editor/6000.5.2f1"
export UNITY="$UNITY_ROOT/Unity.app/Contents/MacOS/Unity"
export MONO="$UNITY_ROOT/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
export ADB="$UNITY_ROOT/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"
export NODE_EXTRA_CA_CERTS="$HOME/.certs/ptkroea.pem"   # §6 — 없으면 빌드 실패
```

아래 예시는 이 변수들이 있다고 가정한다.

---

## 1. 테스트

### 코어 규칙 테스트 (가장 빠름 — 코어를 건드렸으면 필수)

```bash
"$MONO/mcs" Assets/Scripts/ColorMatcherCore.cs Tests/CoreTests.cs -out:/tmp/core_tests.exe
"$MONO/mono" /tmp/core_tests.exe
```

### PlayMode 스모크 테스트 (표현 계층을 건드렸으면 필수)

```bash
"$UNITY" -batchmode -runTests -testPlatform PlayMode -projectPath . \
  -testResults /tmp/playmode_results.xml
```

> 배치모드는 **Unity 에디터가 열려 있으면 실패한다.** 먼저 닫을 것.
> 에디터가 떠 있는지 확인: `pgrep -fl "Unity.app/Contents/MacOS/Unity"`

---

## 2. 맥 빌드 (로컬 육안 확인용)

가장 빠르게 게임을 눈으로 보는 방법. Mono 백엔드라 빌드가 20초쯤 걸린다.

```bash
"$UNITY" -batchmode -nographics -quit -projectPath . \
  -buildTarget OSXUniversal \
  -logFile Logs/build-mac.log \
  -executeMethod MacBuild.App

open Builds/Mac/ChromaDrop.app
```

인자: `-output <경로>` 로 결과 위치를 바꿀 수 있다 (기본 `Builds/Mac/ChromaDrop.app`).
에디터에서는 메뉴 `ChromaDrop > Build > Mac 앱 (로컬 확인용)`.

**조작**: 마우스 클릭 = 스탬프, `R` = 회전, `⌘Q` = 종료.

```bash
# 실행 여부 / 런타임 로그 확인
pgrep -fl "Chroma Drop"
tail -f ~/Library/Logs/jaemanc/"Chroma Drop"/Player.log
```

> 서명·공증을 하지 않으므로 **로컬 확인 전용**이다. 남에게 전달하면 Gatekeeper 가 막는다.
> 안드로이드로 되돌아갈 때 플랫폼 전환 재임포트가 한 번 더 걸린다 (수 분).

---

## 3. 안드로이드 빌드

```bash
./Tools/build-android.sh apk              # 사이드로드용 APK (디버그 서명)
./Tools/build-android.sh apk --install    # 빌드 후 연결된 기기에 설치
./Tools/build-android.sh apk --fast       # Mono/ARMv7 — 빠른 확인 전용, 스토어 업로드 불가
./Tools/build-android.sh apk --dev        # 개발 빌드 (프로파일러 연결)
./Tools/build-android.sh aab              # Play Console 업로드용

# 버전을 올릴 때는 둘을 함께 준다
./Tools/build-android.sh aab -appVersion 1.0.1 -versionCode 2
```

실패하면 로그를 본다: `tail -40 Logs/build-android-apk.log`

### 기기에 설치 / 확인

```bash
"$ADB" devices                                        # USB 디버깅 켜고 기기 인식 확인
"$ADB" install -r Builds/Android/ChromaDrop-1.0.apk    # 이미 빌드된 APK 설치
"$ADB" logcat -s Unity                                 # 런타임 로그
"$ADB" uninstall com.jaemanc.chromadrop                # 제거
```

### AAB 확인

AAB 는 `adb install` 이 **안 된다.** 둘 중 하나를 쓴다.

```bash
# (a) bundletool 로 기기에 설치 — 사전 설치 필요: brew install bundletool
bundletool build-apks --bundle=Builds/Android/ChromaDrop-1.0.aab \
  --output=/tmp/chromadrop.apks --local-testing
bundletool install-apks --apks=/tmp/chromadrop.apks

# (b) Play Console 내부 테스트 트랙 업로드 — 실제 배포 경로와 동일해서 가장 정확
#     단, 디버그 서명 AAB 는 업로드가 거부된다. 릴리즈 키로 다시 빌드할 것.
```

빌드된 AAB 가 어떤 키로 서명됐는지 확인:

```bash
unzip -l Builds/Android/ChromaDrop-1.0.aab | grep -E "META-INF/.*\.(RSA|SF)"
# ANDROIDD.RSA  → Unity 디버그 키 (사이드로드 전용, 스토어 업로드 불가)
```

---

## 4. 릴리즈 서명

```bash
./Tools/make-keystore.sh          # 최초 1회. ~/.keystores/chromadrop.keystore 생성

export CHROMADROP_KEYSTORE=~/.keystores/chromadrop.keystore
export CHROMADROP_KEYSTORE_PASS=...
export CHROMADROP_KEYALIAS=chromadrop
export CHROMADROP_KEYALIAS_PASS=...

./Tools/build-android.sh aab      # 환경변수 4개가 다 있으면 릴리즈 키로 서명된다
```

> - 키스토어와 비밀번호는 **절대 커밋하지 않는다.**
> - **키스토어를 분실하면 같은 앱으로 업데이트를 올릴 수 없다.** 별도 백업 필수.
> - 환경변수 4개 중 하나라도 없으면 조용히 디버그 키로 서명된다. 위 `unzip` 으로 확인할 것.

---

## 5. 프로젝트 상태 확인

```bash
ls -lh Builds/Android Builds/Mac                 # 산출물
find . -name "*.apk" -o -name "*.aab" | grep -v Library
ls "$UNITY_ROOT/PlaybackEngines/"                # 설치된 플랫폼 모듈
ls Library/PackageCache | wc -l                  # 캐시된 패키지 수

# Android Build Support 모듈이 없을 때 설치
'/Applications/Unity Hub.app/Contents/MacOS/Unity Hub' -- --headless install-modules \
  --version 6000.5.2f1 --module android --childModules
```

---

## 6. 사내 TLS 프록시 대응 ⚠️

이 머신은 사내 TLS 검사 프록시 뒤에 있다. 모든 HTTPS 인증서가 사내 루트 CA
`CN=PTKROEA` 로 교체된다. macOS 키체인에는 등록돼 있어 curl·브라우저는 정상이지만,
**자체 truststore 를 쓰는 도구(JDK, Node)는 실패한다.**

```bash
# 진단 — issuer 에 PTKROEA 가 나오면 프록시가 가로챈 것
echo | openssl s_client -connect packages.unity.com:443 2>/dev/null | grep issuer=
```

### (a) Unity 패키지 매니저 — 맥/WebGL 빌드 시 발생

증상: `Project has invalid dependencies: ... self-signed certificate in certificate chain`

```bash
mkdir -p ~/.certs
security find-certificate -a -c PTKROEA -p /Library/Keychains/System.keychain > ~/.certs/ptkroea.pem
export NODE_EXTRA_CA_CERTS="$HOME/.certs/ptkroea.pem"
```

이 변수를 걸고 Unity 를 실행해야 한다. `~/.zshrc` 에 넣어두는 것을 권한다.

### (b) Gradle / JDK — 안드로이드 빌드 시 발생

증상: `Plugin ... was not found`, 또는 "연결은 되는데 응답이 없음" 형태.
디버그 로그의 `Starting handshake` → 즉시 `Shutdown connection` 이 결정적 단서.

```bash
JDK="$UNITY_ROOT/PlaybackEngines/AndroidPlayer/OpenJDK"
cp -n "$JDK/lib/security/cacerts" "$JDK/lib/security/cacerts.backup"
"$JDK/bin/keytool" -importcert -noprompt -trustcacerts -alias ptkroea \
  -file ~/.certs/ptkroea.pem -keystore "$JDK/lib/security/cacerts" -storepass changeit
```

> Unity 를 재설치하거나 에디터 버전을 올리면 cacerts 가 초기화되므로 **다시 등록해야 한다.**
> TLS 검증을 끄는 우회보다 이 방법이 낫다 — 신뢰 범위가 CA 하나로 한정된다.

---

## 7. 랭킹 서버 (Firebase)

SDK 없이 REST 만 쓴다 (`Assets/Scripts/Leaderboard.cs`). 설정 파일이 없으면
랭킹 기능만 꺼지고 게임은 정상 동작한다.

### 최초 1회 — Firebase 콘솔에서

1. 프로젝트 생성 → **Realtime Database** 만들기
2. Authentication → Sign-in method → **익명 로그인** 활성화
3. 데이터베이스 **규칙**을 아래로 교체 (인덱스 + 남의 점수 못 건드리게)

```json
{
  "rules": {
    "boards": {
      "$board": {
        ".read": "auth != null",
        ".indexOn": ["score"],
        "$uid": { ".write": "auth != null && auth.uid == $uid" }
      }
    }
  }
}
```

### 설정 파일 생성

```bash
export CHROMADROP_FIREBASE_URL="https://<프로젝트>-default-rtdb.firebaseio.com"
export CHROMADROP_FIREBASE_APIKEY="<웹 API 키>"
./Tools/make-leaderboard-config.sh     # → Assets/Resources/leaderboard.json
```

> - 생성된 파일은 `.gitignore` 에 등록돼 있다. **커밋하지 않는다.**
> - 값을 채팅·로그·문서에 되출력하지 않는다. 키 **이름**으로만 지칭한다.

### 데이터 모델

```
/boards/{boardId}/{uid} = { name, country, score, diff, seed, updated }
boardId = "ta" | "score_easy" | "score_normal" | "score_hard"
```

> ⚠ **점수는 클라이언트가 올린다.** 위 규칙은 "남의 칸에 못 쓴다"까지만 막고,
> 자기 칸에 임의의 점수를 쓰는 것은 막지 못한다. 막으려면 서버(Cloud Functions 등)에서
> `seed` + 입력 로그로 `ColorMatcherCore` 를 재실행해 검증해야 한다. 아직 미구현.

---

## 8. Git

```bash
git status
git add Assets/Editor/MacBuild.cs COMMANDS.md     # 파일을 명시적으로 지정한다
git commit -m "메시지"
```

> - `git add .` 은 쓰지 않는다. 빌드 산출물과 키 파일이 쓸려 들어간다.
> - `git push` 는 사용자가 직접 한다.
