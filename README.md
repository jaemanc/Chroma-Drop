# Chroma Drop

Color pop! — 칼라 매쳐 퍼즐. 조각을 단색으로 찍어(페인트) 같은 색 정사각형을 만들어 터뜨린다.
**로직/표현이 분리**돼 있다 — 규칙 엔진은 `UnityEngine` 에 의존하지 않는다.
블록·판 그림은 에셋으로 넣고, 아트가 빠진 자리는 런타임 절차 생성으로 메운다.

명령 모음은 [COMMANDS.md](COMMANDS.md), 작업 규칙은 [CLAUDE.md](CLAUDE.md) 참고.

## 게임 규칙

- **보드 14x14**, 4색 고정 팔레트 4세트 중 하나를 시드로 뽑는다 (`Palette.Sets`)
- **페인트 방식** — 조각이 단색을 갖고, 찍으면 덮인 칸을 그 색으로 덮어쓴다
- **정사각형 매칭** — 같은 색 2x2 이상이 모이면 터진다.
  크기 배수 2x2=x1, 3x3=x2, 4x4=x4 / 연쇄 단계마다 +50%
- **조각 13종** — I, T, S, Z, L, J, I3, V3, PLUS, W5, U5, T5, Z5 (전부 2x2 미포함)
- **아이템 5종** — Row(가로 14칸) / Col(세로 14칸) / Diag(양 대각) /
  Bomb5(5x5) / ColorClear(같은 색 전부).
  매칭에 포함돼 터질 때 발동하고, 발동으로 터진 칸의 아이템도 연쇄 발동한다(BFS)
- **얼음 블록** — 내구도 2. 위에 조각을 놓을 수 없고, **옆 칸이 터질 때만** 금이 간다.
  한 연쇄 단계에 1씩만 깎이므로 최소 2번의 별개 파괴가 필요하다.
  중력을 받지 않아 제자리에 고정되고, 다른 블록은 그 위에 얹힌다.
  3수째부터 한 수 걸러 1~3개씩, 진행할수록 많아진다
- **모드 2종** — 횟수 모드(20수) / 타임어택(3분). 목표 점수는 없고 최대한 많이 얻는 방식이다
- **조각 제한 시간 4초** — 다 지나가면 그 조각은 버려지고 기회도 한 번 쓴다

> 밸런스 수치(수·얼음 등장량·아이템 확률)는 플레이테스트로 확정되지 않은 값이다.
> `Rules.Table`, `Rules.BricksAfterMove` 에서 조정한다.

## 프로젝트 구조

역할별로 나눈다. `Assets/_Project/` 아래가 직접 만든 것 전부고, 그 밖은 생성물이거나 외부 것이다.

```
Assets/
  _Project/
    Scenes/                    # Menu.unity(시작) · Game.unity
    Scripts/
      ChromaDrop.asmdef        # 런타임 어셈블리 (하위 폴더가 전부 여기 들어간다)
      Core/                    # UnityEngine 비의존 — 서버 검증·리플레이·엔진 교체에 재사용
        ColorMatcherCore.cs    #   게임 규칙 엔진 (수정 금지 자산)
        NationRanking.cs       #   국가 랭킹 집계
        Json.cs                #   최소 JSON 파서
      Gameplay/                # 판 진행과 보드 표현
        GameManager.cs         #   흐름·입력·연출 지휘. 아트를 인스펙터로 받는다
        BoardView.cs           #   보드 렌더링·파티클·낙하
        BlockTheme.cs          #   블록 테마 데이터 (그림·색·판 크기·배경)
        ObstacleStyle.cs  StageTable.cs  StageTargets.cs
        Progress.cs  Shop.cs  Wallet.cs  Sfx.cs  Palette.cs
      UI/                      # 화면
        GameUI.cs              #   HUD/결과/랭킹 (uGUI 런타임 생성, SafeArea 대응)
        MainMenu.cs  UiTheme.cs  LeaderboardSkin.cs
      Services/                # 바깥과 통신하는 것
        Leaderboard.cs         #   Firestore REST (SDK 미사용)
        PlayerAccount.cs  CountryFlags.cs
    Editor/                    # 에디터 전용 — 플레이어 빌드에 안 들어간다
      Build.cs                 #   MacBuild.App · IosBuild.Xcode (배치모드 진입점)
      ChromaDrop.Editor.asmdef
    Sprites/                   # Blocks/{gem_blocks,Obstacle} · Boards · Flags · Icons · UI · arrows_x
    Prefabs/Effects/           # 블록 파괴 파티클
    Animations/                # 메뉴 버튼 애니메이션
    Design/                    # 게임에 안 쓰는 시안·원본
  ThirdParty/                  # 에셋 스토어 팩 (git 미추적)
  Resources/backend-config.json   # 코드가 이름으로 읽는 설정만 둔다 (git 미추적)
  StreamingAssets/stages.json  # 스테이지 100개 + 타임어택 설정
Tests/CoreTests.cs             # Assets 밖 — 코어 콘솔 테스트 (Unity 무관)
Tools/                         # 빌드·키스토어·랭킹 설정·더미 시드 스크립트
docs/                          # 설계 메모
Builds/ Library/ Logs/         # 생성물. 커밋 대상 아님
```

경계 규칙 두 가지:

- **`Core/` 는 `UnityEngine` 을 쓰지 않는다.** 규칙 엔진을 엔진 밖에서 그대로 돌릴 수 있어야
  서버 검증·리플레이가 가능하다. 이 폴더에 `using UnityEngine` 이 들어가면 설계가 깨진 것이다.
- **런타임 아트는 `Resources` 에 두지 않는다.** `Resources` 는 참조 여부와 상관없이 통째로
  빌드에 들어간다. 그림은 Game 씬 `GameManager` 인스펙터로 연결한다.

## 실행

Unity Hub → **Add project from disk** 로 저장소 루트 열기 → `Assets/_Project/Scenes/Menu.unity` → Play.
씬이 없으면 메뉴 **ChromaDrop > Setup Project** 를 먼저 실행한다.

에디터를 열지 않고 확인하려면 맥 앱으로 빌드하는 쪽이 빠르다 (약 6초):

```bash
./Tools/build-mac.sh          # 또는 COMMANDS.md §2 의 배치모드 명령
open Builds/Mac/ChromaDrop.app
```

## 조작

- **터치(모바일)** — 드래그로 고스트 프리뷰(손가락 위로 띄워 표시), 떼면 스탬프.
- **마우스(데스크톱)** — 이동으로 프리뷰, 클릭으로 스탬프.
- 회전 조작은 없다. 조각은 나올 때 방향이 무작위로 정해진다.

## 구현 상태

- **홈 → 플레이 → 결과** 전체 사이클 동작
- **랭킹** — 현재 오프라인 더미 기록과 국가별 순위(상위 3명 합산)를 보여준다.
  기존 Firebase Firestore REST 경로는 중지했으며, 삭제 대상인 이전 난이도별
  `boards_*` 컬렉션을 다시 생성하지 않는다.
- **최고 점수** — 모드별로 `PlayerPrefs` 에 로컬 저장
- **연출** — 둥근 모서리 타일, 스탬프 팝, 연쇄 단계별 순차 폭발(직접 파괴는 흰색,
  연계는 황금색), 단계마다 낙하 재생 + 새로 채워진 칸 반짝임, 색 파편 버스트,
  연쇄 깊이에 비례하는 카메라 셰이크
- **사운드** — 스탬프/파괴(연쇄 피치 상승)/아이템/승패, 전부 절차 생성

## 테스트

```bash
# 코어 규칙 테스트 (Unity 무관)
MONO="/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
"$MONO/mcs" Assets/_Project/Scripts/Core/ColorMatcherCore.cs Tests/CoreTests.cs -out:/tmp/core_tests.exe
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

```bash
./Tools/build-mac.sh --run                # 맥 .app (로컬 확인용, 서명 없음)
./Tools/build-ios.sh                      # Builds/iOS 에 Xcode 프로젝트 생성
```

빌드 진입점은 `Assets/_Project/Editor/Build.cs` 다 (`MacBuild.App`, `IosBuild.Xcode`).
에디터 GUI 로 바꾼 값은 코드가 덮어쓰므로 **코드가 유일한 출처**다.
배치모드는 Unity 에디터가 열려 있으면 실패한다.

릴리즈 서명 키스토어 생성과 자격증명 주입은 [COMMANDS.md](COMMANDS.md) §4 를 따른다.
키스토어와 비밀번호는 저장소에 넣지 않는다.

**iOS** — Unity 는 Xcode 프로젝트까지만 만든다. `.app`/`.ipa` 는 그 프로젝트를 Xcode 로
빌드·아카이브해야 나오고, 실기기용은 Apple 개발자 서명이 필요하다. 필요한 것:
Unity iOS Build Support 모듈, 전체 Xcode(Command Line Tools 만으로는 안 된다).

**Android** — `Tools/build-android.sh` 가 부르는 `AndroidBuild` 진입점이 아직 없다.
스크립트만 있고 코드가 비어 있는 상태다.

## 미구현

- 서버측 점수 검증. 지금은 클라이언트가 점수를 직접 올리므로 조작이 가능하다.
  제출 시 `seed` 를 함께 저장해 두었으니, 입력 로그를 붙이면 서버에서 `ColorMatcherCore` 를
  재실행해 검증할 수 있다
- iOS 빌드 파이프라인 — Unity 쪽(Xcode 프로젝트 생성)만 있고, Xcode 아카이브·서명은 수동
- Android 빌드 진입점(`AndroidBuild`) 복구
- 밸런스 실측 조정

### 홈 메뉴와 하트

홈 메뉴는 `UI/Home/home-menu-v2.png` 아트와 실제 숫자·버튼 텍스트를 함께 사용한다.
PLAY는 일반 게임, RANKING·SHOP은 기존 화면, 코인 +는 기존 코인 구매 화면을 연다.
설정에서 사운드와 타임어택을 선택할 수 있으며 개발 빌드에는 피버 미리보기가 있다.

하트는 처음 20개, 최대 20개이며 UTC 기준 10분마다 1개 충전한다. 앱 종료 중에도
충전하고 일반 게임 시작·재시작·다음 스테이지 진입 시 1개 소비한다. 랭킹·상점 열기와
피버 미리보기는 소비하지 않는다. 충전과 저장은 로컬이며 서버 시간 검증은 없다.
홈 광고 보상은 코인 100개, 하트 + 메뉴에서는 광고로 하트 1개를 받을 수 있다.
iOS 광고는 Google Mobile Ads Unity 11.6.0을 사용한다. `.env`에 `iosAppId`,
`scoreSubmitAdUnitId`, `coinRewardAdUnitId`를 지정하면 에디터 로드와 iOS 빌드 때 자동 반영한다.
수동 재적용은 Unity 메뉴 `Chroma Drop > Apply AdMob .env`를 사용한다.
생성된 `Assets/Resources/admob-config.json`과 SDK 설정은 Git에서 제외한다.
점수 등록과 코인 보상은 별도 광고 슬롯이며, 시청 완료 콜백에서만 한 번 처리한다.
기본값 `useTestAds=true` 및 에디터·개발 빌드에서는 Google 테스트 광고를 사용한다.
iOS에서 UMP 동의를 확인하고, 필요한 경우 설정의 AD PRIVACY에서 변경할 수 있다.
설정이 없는 플랫폼과 배치 검사는 기존 더미 광고를 사용한다.
`.env`에 `useTestAds=false`를 지정하면 일반 출시 빌드는 운영 광고를 사용하며,
생략하면 계속 테스트 광고를 사용한다. 에디터·개발 빌드는 항상 테스트 광고다.
출시 빌드에서는 미구현 코인 결제와 개발용 지급을 차단하고, 기록은 기기 내 최고 점수만 표시한다.
에디터·개발 빌드에서는 기존 더미 구매와 테스트 랭킹을 유지한다.
공개한 개인정보처리방침 HTTPS URL을 `Assets/Resources/app-info.json`의
`privacyPolicyUrl`에 넣으면 홈 설정에 PRIVACY POLICY 버튼이 표시된다.
공개 전 준비 사항은 `docs/app-store-readiness.md`와 `docs/privacy-policy-draft.md`를 확인한다.
컴파일 및 설정·보상 콜백 테스트만 확인했으며 iPhone 광고 수신·시청은 별도 확인이 필요하다.

하트 계산만 검사하려면 프로젝트 Unity 버전의 Mono `mcs`로
`Assets/_Project/Scripts/Core/HeartClock.cs`와 `Tests/HeartClockTests.cs`를 함께 컴파일한다.

상점은 게임 코인으로 아이템 네 종류만 판매한다. 신규 지갑은 100코인으로 시작하며
기존 저장 잔액은 초기화하지 않는다. 기존 21~30개 하트 기록은 최대 20개로 전환한다.
스킨 판매·현금 코인 구매 화면과 개발용 코인 자동 지급은 제거했다.
실제 App Store 인앱결제 SDK는 여전히 미연동이다.

홈 버튼 문구는 메뉴 아트 높이에 비례해 크기를 맞추고 버튼 안에서 축소한다.
플레이 화면 아이템 네 개는 하단 중앙에 정렬하고 현재 던질 조각만 오른쪽에 표시한다.
다음 조각은 표시하지 않지만 게임 내부 조각 생성·순서는 유지한다.
