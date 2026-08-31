# Chroma Drop

Color pop! — 칼라 매쳐 퍼즐. 조각을 단색으로 찍어(페인트) 같은 색 정사각형을 만들어 터뜨린다.
**로직/표현이 분리**돼 있고 **외부 에셋 의존이 0**이다 — 스프라이트·폰트·사운드·UI를 전부 런타임에 생성한다.

명령 모음은 [COMMANDS.md](COMMANDS.md), 작업 규칙은 [CLAUDE.md](CLAUDE.md) 참고.

## 게임 규칙

- **보드 14x14**, 4색 랜덤 팔레트 (색상환 균등분할 + 지터)
- **페인트 방식** — 조각이 단색을 갖고, 찍으면 덮인 칸을 그 색으로 덮어쓴다
- **정사각형 매칭** — 같은 색 2x2 이상이 모이면 터진다.
  크기 배수 2x2=x1, 3x3=x2, 4x4=x4 / 연쇄 단계마다 +50%
- **조각 13종** — I, T, S, Z, L, J, I3, V3, PLUS, W5, U5, T5, Z5 (전부 2x2 미포함)
- **아이템 5종** — Row(가로 14칸) / Col(세로 14칸) / Diag(양 대각) /
  Bomb5(5x5) / ColorClear(같은 색 전부).
  매칭에 포함돼 터질 때 발동하고, 발동으로 터진 칸의 아이템도 연쇄 발동한다(BFS)
- **암석 블록** — 내구도 5. 위에 조각을 놓을 수 없고, **옆 칸이 터질 때만** 금이 간다.
  한 연쇄 단계에 1씩만 깎이므로 최소 5번의 별개 파괴가 필요하다.
  중력을 받지 않아 제자리에 고정되고, 다른 블록은 그 위에 얹힌다.
  3수째부터 한 수 걸러 1~3개씩, 진행할수록 많아진다
- **모드 2종** — 횟수 모드(50수 안에 목표 점수) / 타임어택(3분)

> 밸런스 수치(목표 점수·암석 등장량·아이템 확률)는 플레이테스트로 확정되지 않은 값이다.
> `Rules.Table`, `Rules.BricksAfterMove` 에서 조정한다.

## 프로젝트 구조

```
Assets/
  Scenes/Main.unity            # 메인 씬 (카메라 + GameManager 뿐 — 나머지는 런타임 생성)
  Scripts/
    ColorMatcherCore.cs        # 게임 규칙 (UnityEngine 비의존)
    GameManager.cs             # 게임 흐름/입력, 연출 오케스트레이션
    BoardView.cs               # 보드 렌더링·아이템 아이콘·고스트·파괴 파티클·낙하
    GameUI.cs                  # 홈/HUD/결과/랭킹 (uGUI 런타임 생성, SafeArea 대응)
    Palette.cs                 # 색상 팔레트 생성 (HSL→RGB, 순수 함수)
    Sfx.cs                     # 효과음 (절차 생성 PCM)
    Leaderboard.cs             # Firestore REST 랭킹 (SDK 미사용)
    NationRanking.cs           # 랭킹 집계 (UnityEngine 비의존)
    PlayerAccount.cs           # 게스트 계정·국가 판별·배지 색
    Json.cs                    # 최소 JSON 파서
  Editor/
    ProjectBootstrap.cs        # 씬/빌드 설정 구성 (ChromaDrop > Setup Project)
    AndroidBuild.cs            # APK/AAB 빌드 진입점
    MacBuild.cs                # 맥 로컬 확인용 빌드 진입점
  Tests/PlayMode/              # PlayMode 스모크 + 랭킹 CRUD 테스트
Tests/CoreTests.cs             # Assets 밖 — 코어 콘솔 테스트 (Unity 무관)
Tools/                         # 빌드·키스토어·랭킹 설정·더미 시드 스크립트
```

**`ColorMatcherCore` 와 `NationRanking` 은 엔진에 의존하지 않는다.** 서버 검증·리플레이·엔진 교체에
그대로 재사용할 수 있고, 이 경계를 지키는 것이 이 구조의 핵심이다.

## 실행

Unity Hub → **Add project from disk** 로 저장소 루트 열기 → `Assets/Scenes/Main.unity` → Play.
씬이 없으면 메뉴 **ChromaDrop > Setup Project** 를 먼저 실행한다.

에디터를 열지 않고 확인하려면 맥 앱으로 빌드하는 쪽이 빠르다 (약 6초):

```bash
./Tools/build-mac.sh          # 또는 COMMANDS.md §2 의 배치모드 명령
open Builds/Mac/ChromaDrop.app
```

## 조작

- **터치(모바일)** — 드래그로 고스트 프리뷰(손가락 위로 띄워 표시), 떼면 스탬프. 회전 버튼.
- **마우스(데스크톱)** — 이동으로 프리뷰, 클릭으로 스탬프, `R` 키 회전.

## 구현 상태

- **홈 → 플레이 → 결과** 전체 사이클 동작
- **랭킹** — Firebase Firestore REST(SDK 미사용). 익명 로그인으로 게스트 계정을 만들고,
  게임이 끝나면 자동 제출한다. 개인 순위와 국가별 순위(상위 3명 합산)를 보여준다.
  설정이 없으면 랭킹만 꺼지고 게임은 정상 동작한다
- **최고 점수** — 모드별로 `PlayerPrefs` 에 로컬 저장
- **연출** — 둥근 모서리 타일, 스탬프 팝, 연쇄 단계별 순차 폭발(직접 파괴는 흰색,
  연계는 황금색), 단계마다 낙하 재생 + 새로 채워진 칸 반짝임, 색 파편 버스트,
  연쇄 깊이에 비례하는 카메라 셰이크
- **사운드** — 스탬프/파괴(연쇄 피치 상승)/아이템/승패, 전부 절차 생성

## 테스트

```bash
# 코어 규칙 테스트 (Unity 무관)
MONO="/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
"$MONO/mcs" Assets/Scripts/ColorMatcherCore.cs Tests/CoreTests.cs -out:/tmp/core_tests.exe
"$MONO/mono" /tmp/core_tests.exe

# PlayMode 스모크 (에디터가 닫혀 있어야 한다)
Unity -batchmode -runTests -testPlatform PlayMode -projectPath . -testResults results.xml
```

랭킹 CRUD 테스트는 목 서버나 실제 Firebase 를 가리킬 때만 돌고, 아니면 건너뛴다.
자세한 명령은 [COMMANDS.md](COMMANDS.md) §1.

## 빌드 / 배포

```bash
./Tools/build-android.sh apk              # 사이드로드용 APK (디버그 서명)
./Tools/build-android.sh apk --install    # 빌드 후 연결된 기기에 설치
./Tools/build-android.sh aab              # Play Console 업로드용
```

빌드 설정은 `AndroidBuild.Configure()` 안에 코드로 들어 있다 — 에디터 GUI 로 바꾼 값은 덮어쓰이므로
**코드가 유일한 출처**다.

릴리즈 서명 키스토어 생성과 자격증명 주입은 [COMMANDS.md](COMMANDS.md) §4 를 따른다.
키스토어와 비밀번호는 저장소에 넣지 않는다.

iOS 는 아직 스크립트화되지 않았다. Build Settings 에서 플랫폼을 전환해 Xcode 프로젝트를 생성한 뒤
Xcode 에서 아카이브한다.

## 미구현

- 서버측 점수 검증. 지금은 클라이언트가 점수를 직접 올리므로 조작이 가능하다.
  제출 시 `seed` 를 함께 저장해 두었으니, 입력 로그를 붙이면 서버에서 `ColorMatcherCore` 를
  재실행해 검증할 수 있다
- iOS 빌드 파이프라인
- 밸런스 실측 조정
