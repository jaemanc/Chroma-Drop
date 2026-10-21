# Chroma Drop 작업 규칙

`.claude/CLAUDE.md`를 바탕으로 실제 저장소와 대조한 에이전트 공통 지침이다.
프로젝트 구조·명령·제약이 달라지면 이 파일도 함께 갱신한다. 사용자 지시가 우선한다.

## 소통과 변경 범위

- 설명, 새 주석, 요청받은 커밋 메시지는 한국어로 쓴다.
- 기존 미커밋 변경을 먼저 확인하고 보존한다. 요청과 관련된 오류만 최소 범위로 고친다.
- 불확실한 사실은 코드·실행 결과로 확인한다. 독립적으로 진행 가능한 일은 계속하고,
  대상 서비스나 요구사항처럼 결과를 바꾸는 정보만 질문한다.
- 오류 수정에는 원인과 재현 근거를 확보하고, 해당 회귀 검증을 남긴다.
- 테스트 통과, 컴파일 성공, 화면 확인, 실서비스 연결은 구분해서 보고한다.

## 구조와 기준

- Unity 버전은 `ProjectSettings/ProjectVersion.txt`가 기준이다.
- 시작 씬은 `Assets/_Project/Scenes/Menu.unity`, 게임 씬은 `Game.unity`다.
- `Scripts/Core/`: Unity 비의존 규칙·JSON·국가 순위. UnityEngine 의존성을 추가하지 않는다.
- `Scripts/Gameplay/`, `UI/`, `Services/`: 게임 진행·화면·외부 서비스.
- `ColorMatcherCore.cs`의 게임 규칙은 임의 변경하지 않는다. 오류 수정 요청 범위에서
  결함이 입증되면 최소 수정하고 코어 테스트 전체를 다시 실행한다.
- 보드 크기는 테마에 따라 달라진다. 고정 16×16이라는 옛 문서를 전제로 삼지 않는다.
- 스테이지 원본은 `stages/stages.json`; `Tools/sync-stages.sh`로 StreamingAssets에 복사한다.
- 현재 랭킹 구현은 **Firebase Auth + Firestore REST**다. Supabase 연동은 구현되어 있지 않다.
  `Core/NationRanking.cs`는 이미 구현되어 있으므로 미구현으로 취급하지 않는다.
- 원격 랭킹은 중지했다. 이전 `boards_score_easy/normal/hard`, `boards_ta` 컬렉션을
  재생성하지 않는다. 사용자 요청으로 Firestore는 `pay`, `user`만 유지하는 방향이다.
- `Editor/Build.cs`에 Mac/iOS 진입점이 있다. Android 셸 스크립트가 참조하는
  `AndroidBuild` 클래스는 현재 없으므로 Android 빌드 가능하다고 단정하지 않는다.

## Unity 자산

- 자체 자산은 `Assets/_Project/`, 외부 에셋은 `Assets/ThirdParty/`에 둔다.
- `.meta`는 Unity가 생성하게 한다. 자산 이동 시 기존 `.meta`도 함께 이동한다.
- 런타임 아트는 씬/인스펙터 참조를 사용하고, 누락되면 기존 절차 생성 대안을 보존한다.
- `Resources`는 이름으로 읽는 설정 용도다. 아트를 무분별하게 추가하지 않는다.
- UI 폰트는 `UI/UiTypography.cs`에서 역할별로 지정한다. 타이틀은 Fredoka Bold,
  메뉴·버튼은 Nunito ExtraBold, 설명은 Pretendard Regular, 점수·이동 횟수는 Fredoka SemiBold,
  콤보·파괴 효과는 Baloo 2 ExtraBold, 한국어 강조는 Jua Regular다. 메인 화면은 기존 이미지 스타일을 유지한다.
  `Resources/ui-theme.asset`에는 `ThirdParty/Fonts` 및 공통 UI 아트의 인스펙터 참조만 둔다.
  폰트 굵기에 가짜 Bold/Italic을 중첩하지 않는다. 참조 설정은 `Editor/UiThemeImport.cs`가 생성한다.
- `ProjectSettings/*.asset`는 손으로 수정하지 않고 Unity API/Editor 코드로 설정한다.

## 검증

Unity 설치 경로는 프로젝트 버전을 읽어 정한다. 아래는 macOS 기본 설치 기준이다.

```bash
UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt | tr -d '\r')"
MONO_BIN="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
"$MONO_BIN/mcs" Assets/_Project/Scripts/Core/ColorMatcherCore.cs Tests/CoreTests.cs -out:/tmp/chromadrop-core-tests.exe &&
  "$MONO_BIN/mono" /tmp/chromadrop-core-tests.exe
./Tools/dev.sh --test
```

- 코어 테스트는 `Tests/CoreTests.cs`, 씬 스모크 테스트는 `Assets/_Project/Tests/PlayMode/`다.
- 설정 누락 회귀 검사는 `Tests/StageTableTests.cs`이며 `dev.sh --test`에 포함된다.
  셸 실패 전파 검사는 `python3 Tests/test_dev_script.py`로 실행한다.
- 컴파일 실패 시 중단한다. 이전 실행 파일·XML이나 테스트 0개를 성공 근거로 쓰지 않는다.
- 같은 프로젝트를 연 Unity에 배치 프로세스를 중복 실행하지 않는다. 사용 가능한 Editor
  연결에서 검증하거나, 필요하면 프로젝트 사본을 사용한다. 사용자 에디터를 임의 종료하지 않는다.
- 실패와 실행 불가 원인을 그대로 기록한다. HUD·소리·실기기 조작 품질은 별도 확인이다.
- 현재 `TestServices.Enabled`가 켜져 있어 광고·결제·랭킹은 오프라인 더미 모드다.
  테스트 결제·코인·아이템·스킨은 하나의 로컬 JSON, 랭킹은 별도 테스트 키에 저장한다.
  상점 차감과 지급은 `Wallet.BuyItem`·`Wallet.BuySkin`으로 함께 저장하고 분리 호출하지 않는다.
  실제 결제 SDK는 아직 스텁이며 심볼만 켰다고 실서비스가 동작한다고 설명하지 않는다.

## 서비스·비밀값·Git

- Firebase 설정은 `CHROMADROP_FIREBASE_*` 환경변수 또는
  `Assets/Resources/backend-config.json`이다. 키 값·토큰을 채팅/로그/문서에 출력하지 않는다.
- Supabase 확인 시 프로젝트 URL과 실제 설정 유무부터 확인한다. 다른 백엔드를
  임의로 교체하지 않고, 읽기 전용 확인과 인증/쓰기 검증 범위를 구분한다.
- 서비스 역할 키, 관리자 자격증명, 서명 키/비밀번호를 클라이언트나 Git에 넣지 않는다.
- 배포·원격 데이터 변경은 요청 범위를 확인한다. 연결 점검을 위해 운영 점수를 만들지 않는다.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Builds/`는 생성물이다.
- 요청 없이 커밋하거나 푸시하지 않는다. 스테이징이 필요하면 파일을 명시한다.
