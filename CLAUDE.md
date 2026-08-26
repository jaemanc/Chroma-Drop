# CLAUDE.md — Chroma Drop

이 저장소에서 작업하는 에이전트를 위한 규칙. 일반 원칙은
[harness-playbook](https://github.com/jaemanc/harness-playbook) 을 따르고,
여기에는 그 원칙을 이 프로젝트에 적용한 형태와 프로젝트 고유 사실만 적는다.

- 사용자와의 소통은 **한국어**로 한다.
- 주석과 커밋 메시지도 한국어로 쓴다 (기존 코드 스타일과 동일하게).

---

## 1. 프로젝트 개요

16x16 페인트-스탬프 퍼즐. HTML 프로토타입 v5에서 확정된 규칙을 Unity C# 으로 이식했다.
Unity **6000.5.2f1**, 최종 목표는 iOS/Android 배포.

```
Assets/
  Scripts/
    ColorMatcherCore.cs   # 게임 규칙 — UnityEngine 비의존. 수정 금지 자산 (§2)
    GameManager.cs        # 게임 흐름/입력
    BoardView.cs          # 보드 렌더링/연출
    GameUI.cs             # 홈/HUD/결과 (uGUI 런타임 생성)
    Sfx.cs                # 절차 생성 PCM 효과음
  Editor/
    ProjectBootstrap.cs   # 씬/빌드 설정 구성 (멱등)
    AndroidBuild.cs       # APK/AAB 빌드 진입점
  Tests/PlayMode/         # PlayMode 스모크 테스트
Tests/CoreTests.cs        # Assets 밖 — 코어 콘솔 테스트 (Unity 무관)
Tools/                    # 빌드/키스토어 스크립트
```

**로직/표현 분리**가 이 구조의 핵심이다. `ColorMatcherCore` 는 엔진에 의존하지 않으므로
서버 검증·리플레이·엔진 교체에 재사용 가능하다. 이 경계를 무너뜨리지 말 것 —
코어에 `UnityEngine` using 을 추가하는 변경은 거절하고 사용자에게 되물어라.

---

## 2. 건드리면 안 되는 것

| 대상 | 규칙 |
|---|---|
| `Assets/Scripts/ColorMatcherCore.cs` | **수정 금지 자산.** 테스트 23/23 로 검증된 규칙 엔진. 사용자가 명시적으로 요청할 때만 수정하고, 수정했으면 코어 테스트 전체를 다시 돌려 결과를 보고한다. |
| `ProjectSettings/*.asset` | 손으로 편집하지 말 것. 플레이어 설정은 `ProjectBootstrap.cs` / `AndroidBuild.cs` 에 **코드로** 넣는다. |
| `*.meta` 파일 | 직접 만들거나 지우지 말 것. Unity 가 임포트할 때 생성한다. 스크립트를 옮기면 `.meta` 도 같이 옮긴다. |
| `Library/`, `Logs/`, `UserSettings/`, `Builds/` | 생성물. 커밋 대상 아님. |

**외부 에셋 의존 0** 이 유지되어야 한다. 스프라이트·폰트·오디오 파일을 추가하지 말고,
필요한 리소스는 기존 코드처럼 런타임에 생성한다. 모바일 빌드에서 에셋 누락이 나지 않는 이유가 이것이다.

---

## 3. 코드 작성 (code-level-playbook 적용)

**단순함이 먼저.** 요청된 것만, 최소한으로 쓴다. 요청되지 않은 추상화·설정 지점·
일어날 수 없는 예외 처리를 넣지 않는다. 200줄이 50줄로 될 수 있으면 다시 쓴다.

**수술적 변경.** 요청과 직접 연결되지 않는 줄은 바꾸지 않는다.
- 주변 코드·주석·포매팅을 "개선"하지 않는다.
- 내가 다르게 짤 것 같아도 기존 스타일(한국어 주석, 네이밍, `//` 섹션 구분)을 따른다.
- 무관한 죽은 코드를 발견하면 지우지 말고 **말로 보고**한다.
- 내 변경이 만들어낸 미사용 using·변수·메서드만 정리한다.

기준: 바뀐 모든 줄이 요청으로 곧장 추적되는가?

---

## 4. 검증 (test-level-playbook 적용)

명령형 지시를 **검증 가능한 목표**로 바꾼 뒤 통과할 때까지 돌린다.
다단계 작업은 시작 전에 `단계 → 검증 방법` 형태로 짧게 계획을 밝힌다.

이 저장소의 머신에는 dotnet/mono 가 없다. Unity 내장 mono 를 쓴다.

```bash
# (1) 코어 규칙 테스트 — 가장 빠른 확인. 코어를 건드렸으면 필수.
MONO="/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
"$MONO/mcs" Assets/Scripts/ColorMatcherCore.cs Tests/CoreTests.cs -out:/tmp/core_tests.exe
"$MONO/mono" /tmp/core_tests.exe

# (2) PlayMode 스모크 테스트 — 표현 계층을 건드렸으면 필수.
/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -runTests -testPlatform PlayMode -projectPath . \
  -testResults /tmp/playmode_results.xml
```

- 배치모드는 **에디터가 열려 있으면 실패**한다. 실패하면 사용자에게 Unity 종료를 요청한다.
- 컴파일 에러/테스트 실패는 그대로 보고한다. 통과했다고 말하려면 실제 출력이 있어야 한다.
- HUD 배치·연출 타이밍·사운드 톤 같은 감성 품질은 자동 검증 대상이 아니다.
  "확인했다"고 말하지 말고 **사용자 육안 확인이 필요한 항목으로 넘긴다.**
- 밸런스 수치(난이도 목표/수/아이템 확률)는 미검증 추정치다. 근거 없이 "적절하다"고 단정하지 않는다.

---

## 5. 빌드/배포 (infra-level-playbook 적용)

```bash
./Tools/build-android.sh apk              # 사이드로드용 APK (디버그 서명)
./Tools/build-android.sh apk --install    # 빌드 후 연결된 기기에 adb install
./Tools/build-android.sh apk --fast       # Mono/ARMv7 — 빠른 확인 전용
./Tools/build-android.sh aab              # Play Console 업로드용
```

- 빌드 설정 변경은 `AndroidBuild.Configure()` 안에서 한다. 에디터 GUI 로 바꾼 값은
  코드가 덮어쓰므로 코드가 유일한 출처다.
- Play Console 업로드, 스토어 등록 정보 변경처럼 **되돌리기 어렵고 외부로 나가는 동작**은
  실행하지 말고 명령만 제시한다. 버전코드를 올리는 변경도 먼저 알린다.
- 앱 버전을 올릴 때는 `-appVersion` / `-versionCode` 를 함께 준다.

### 비밀값

- 키스토어(`*.keystore`, `*.jks`)와 비밀번호는 **절대 커밋하지 않는다.** `.gitignore` 에 등록돼 있다.
- 서명 정보는 환경변수(`CHROMADROP_KEYSTORE`, `CHROMADROP_KEYSTORE_PASS`,
  `CHROMADROP_KEYALIAS`, `CHROMADROP_KEYALIAS_PASS`)로만 주입한다. 코드·문서에 값을 박지 않는다.
- 값을 채팅·로그·예시에 되출력하지 않는다. 키 **이름**으로만 지칭한다.
- 키스토어를 분실하면 같은 앱으로 업데이트할 수 없다는 점을 항상 함께 알린다.
- 랭킹 백엔드(Firebase 등)를 붙일 때 인증 없는 엔드포인트가 생기면 반드시 짚고 넘어간다.

---

## 6. Git

- **묻지 않고 커밋하지 않는다.** 커밋할 시점이라고 판단되면 제안 메시지를 보여주고 승인을 받는다.
- **`git push` 는 절대 실행하지 않는다.** 푸시는 사용자의 몫이다.
- `git add .` 대신 파일을 명시적으로 스테이징한다. 빌드 산출물과 키 파일이 쓸려 들어가는 것을 막는다.

---

## 7. 코딩 전 태도 (agent-coding-playbook 적용)

- **가정을 밝힌다.** 불확실하면 추측하지 말고 묻는다.
- **해석이 갈리면 나열한다.** 조용히 하나 고르고 진행하지 않는다.
- **더 단순한 길이 있으면 말한다.** 요청받은 대로 하되 이견은 한두 문장으로 남긴다.
- **막히면 멈춘다.** 무엇이 불분명한지 이름 붙여 묻는다.
- 오타 수정 같은 사소한 작업까지 이 절차를 밟을 필요는 없다. 판단해서 적용한다.

잘 되고 있다는 신호: diff 에 요청된 변경만 있고, 지나가는 김에 한 리팩터링이 없고,
질문이 실수 뒤가 아니라 구현 전에 나온다.

---

## 8. 현재 미구현

- 랭킹·공유 백엔드 (Firebase Realtime DB/Firestore 후보). 국가 대항 집계는
  HTML v5 의 `dedupeBest`/`nationRanking` 을 C# 으로 옮기면 된다.
- iOS 빌드 파이프라인 (Android 만 스크립트화돼 있음).
- 밸런스 실측 조정.
