# Chroma-Drop

color pop! — 칼라 매쳐 퍼즐 게임. HTML 프로토타입 v5에서 확정된 규칙의 Unity C# 이식.

## 프로젝트 구조

```
Assets/
  Scenes/Main.unity       # 메인 씬 (카메라 + GameManager 뿐 — 나머지는 런타임 생성)
  Scripts/
    ColorMatcherCore.cs   # 게임 규칙 (UnityEngine 비의존). 테스트 23/23.
    GameManager.cs        # 게임 흐름/입력 (터치+마우스)
    BoardView.cs          # 보드 렌더링, 아이템 아이콘, 고스트, 파괴/낙하 연출
    GameUI.cs             # 홈/HUD/결과 화면 (uGUI 런타임 생성, SafeArea 대응)
    Sfx.cs                # 효과음 (절차 생성 PCM — 오디오 에셋 없음)
  Editor/ProjectBootstrap.cs  # 씬/빌드 설정/모바일 설정 자동 구성
  Tests/PlayMode/         # PlayMode 스모크 테스트 (실제 런타임 실행 검증)
Tests/                    # Assets 밖 — 코어 콘솔 테스트 (Unity 무관)
  CoreTests.cs
```

로직/표현 분리 구조. `ColorMatcherCore`는 엔진과 무관하므로 서버 검증·리플레이·엔진 교체에도
재사용 가능. 스프라이트/폰트/사운드/UI 전부 코드에서 런타임 생성이라 **외부 에셋 의존이 0** —
모바일 빌드에서 에셋 누락 문제가 없다.

## 실행

Unity Hub → **Add project from disk** 로 저장소 루트 열기 (Unity 6000.5.2f1) →
`Assets/Scenes/Main.unity` 열고 Play. 씬이 없으면 메뉴 **ChromaDrop > Setup Project** 실행.

## 조작

- **터치(모바일)**: 드래그로 고스트 프리뷰(손가락 위로 띄워 표시), 떼면 스탬프. 회전 버튼.
- **마우스(에디터)**: 이동으로 프리뷰, 클릭으로 스탬프, R 키 회전.

## 테스트

```bash
# 코어 규칙 테스트 (Unity 무관, 어떤 C# 컴파일러든 가능. Unity 내장 mono 예시)
MONO="/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
"$MONO/mcs" Assets/Scripts/ColorMatcherCore.cs Tests/CoreTests.cs -out:/tmp/core_tests.exe
"$MONO/mono" /tmp/core_tests.exe

# PlayMode 스모크 테스트 (실제 런타임에서 부팅·스탬프·게임 완주 검증)
/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -runTests -testPlatform PlayMode -projectPath . \
  -testResults /tmp/playmode_results.xml
```

## 검증 상태 (정확히)

- **검증됨 (컴파일+테스트)**: `ColorMatcherCore.cs` 전체 — 조각 세트(13종, 전 회전 2x2 미포함),
  페인트 스탬프, 정사각형 DP 판정, 아이템 5종 효과 범위, 발동 BFS 종결성(아이템 30개 최악배치 100회),
  점수/배수, 초기 보드 무매칭(시드 200개 전수 0건), 무작위 30수 불변식, 회전, 난이도/타이머 규칙.
  테스트 23/23 통과.
- **검증됨 (PlayMode 실행)**: 부팅→홈, 게임 시작, 스탬프 연출 완료 후 보드 불변식,
  경계 밖 배치 거부, 무작위 30수 완주→결과 화면, 타임어택 모드 — 실제 Unity 런타임에서 통과.
- **에디터/기기에서 육안 확인 필요**: HUD 배치·연출 타이밍·사운드 톤 등 감성 품질.
  수치는 인스펙터(`GameManager`)에서 조정 가능.
- **미구현 (아래 TODO)**: 랭킹·공유(백엔드 필요), 화면 회전 대응 외 세부 폴리시.

## v5 확정 규칙 (Core에 반영됨)

- 보드 16x16, **3색 랜덤 팔레트**(색상환 균등분할+지터)
- **페인트 방식**: 조각이 단색을 갖고 찍으면 덮인 칸을 그 색으로 덮어쓰기
- 조각 13종: I,T,S,Z,L,J,I3,V3,PLUS,W5,U5,T5,Z5 — 전부 2x2 미포함
- 정사각형(2x2+) 매칭 파괴, 크기 배수(2x2=x1,3x3=x2,4x4=x4), 연쇄 +50%/단계
- 아이템 5종: Row(가로16), Col(세로16), Diag(X자 양대각선), Bomb9(9x9), ColorClear(같은색 전체)
- 아이템 스폰: 매칭 6타일+ → row/col/diag 랜덤 / 연쇄2 → row / 연쇄3 → bomb9 /
  연쇄5+ 또는 3x3+ → colorclear
- 아이템은 정사각형 매칭에 포함돼 터질 때 발동, 발동이 다른 아이템 건드리면 연쇄 발동(BFS)
- 모드: 점수 모드(제한 수 안에 목표 점수) / 타임어택(60초)
- 조각 타이머: 첫 조각 8초 → 마지막 조각 2.5초(남은 수 선형 비례)
- 난이도: 하 30수/15000, 중 25수/25000, 상 20수/40000

**주의**: 위 밸런스 수치(목표/수/확률)는 플레이테스트로 검증되지 않은 추정치다. 실측 후 조정 대상.

## TODO (Unity에서 추가 구현 — HTML v5엔 있으나 이 코어엔 표현/저장 계층이라 미포함)

1. **낙하 애니메이션**: 파괴 후 새 타일이 위에서 내려오는 연출. `ResolveResult.Destroyed`로 열별
   낙하 거리 계산 가능(HTML v5 `animateFall` 참고). 코루틴 + LeanTween/DOTween 권장.
2. **아이템 타일 아이콘**: `Board.GetItem(x,y)`로 타입 조회 → 스프라이트 오버레이. HTML은 clip-path
   모양이었으나 Unity는 전용 스프라이트 사용.
3. **사운드**: 스탬프/파괴/연쇄음.
4. **화면 전환**: 홈/게임/랭킹. Unity는 Scene 또는 Canvas 그룹 전환.
5. **랭킹·공유**: HTML v5는 아티팩트 공유 저장 API를 썼으나 이는 네이티브 앱에서 동작하지 않음.
   **백엔드 필요** — Firebase Realtime DB/Firestore 또는 자체 서버. 국가 대항 집계 로직은
   HTML v5 `dedupeBest`/`nationRanking`을 C#으로 옮기면 됨(동명동국 최고점 병합, 국가 합산).
6. **모바일 입력**: 현재 마우스 기준. Touch/드래그로 교체.

## 안드로이드 빌드 (Tools/build-android.sh)

사전 준비 — Android Build Support 모듈(SDK/NDK/OpenJDK 포함) 1회 설치:

```bash
'/Applications/Unity Hub.app/Contents/MacOS/Unity Hub' -- --headless install-modules \
    --version 6000.5.2f1 --module android --childModules
```

빌드:

```bash
./Tools/build-android.sh apk              # 사이드로드용 APK (디버그 서명)
./Tools/build-android.sh apk --install    # 빌드 후 연결된 기기에 adb install
./Tools/build-android.sh apk --fast       # Mono/ARMv7 — IL2CPP 없이 빠른 확인용
./Tools/build-android.sh aab              # Play Console 업로드용 AAB
./Tools/build-android.sh aab -appVersion 1.0.1 -versionCode 2
```

산출물은 `Builds/Android/ChromaDrop-<버전>.(apk|aab)`. 에디터에서는 메뉴
`ChromaDrop > Build > Android APK / Android AAB` 로도 같은 빌드가 돈다.

기본 설정: IL2CPP + ARM64, minSdk 26, targetSdk Auto(설치된 최신), 세로 고정,
패키지명 `com.jaemanc.chromadrop`.

### 서명

환경변수 4개가 모두 있으면 릴리즈 키로, 아니면 Unity 디버그 키로 서명한다.
디버그 서명 APK는 기기에 직접 설치(사이드로드)만 가능하고 Play 업로드는 불가.

```bash
./Tools/make-keystore.sh          # ~/.keystores/chromadrop.keystore 생성 (1회)
export CHROMADROP_KEYSTORE="$HOME/.keystores/chromadrop.keystore"
export CHROMADROP_KEYSTORE_PASS='...' CHROMADROP_KEYALIAS=chromadrop CHROMADROP_KEYALIAS_PASS='...'
./Tools/build-android.sh aab
```

키스토어는 `.gitignore` 처리돼 있다. **분실하면 같은 앱으로 업데이트할 수 없으니 따로 백업할 것.**

### 기기에 파일로 떨구기

- USB 디버깅 켠 기기 연결 후 `./Tools/build-android.sh apk --install`
- 또는 `Builds/Android/*.apk` 를 드라이브/에어드롭 등으로 전달 → 기기에서
  "출처를 알 수 없는 앱 설치" 허용 후 실행
- AAB 는 그대로 설치할 수 없다. Play Console 내부 테스트 트랙에 올리거나
  `bundletool build-apks --local-testing` 으로 APK 로 변환해야 한다.

## iOS/Android 배포 개요 (검증 아님, 일반 절차)

- 한 Unity 프로젝트에서 Build Settings의 플랫폼만 전환해 양쪽 빌드.
- iOS: Build → Xcode 프로젝트 생성 → Xcode에서 `.ipa` 아카이브 → App Store Connect 업로드.
  Mac + Xcode + Apple Developer 계정 필요(연 비용 발생, 금액은 신청 시점 확인).
- Android: Build → `.aab` 생성 → Google Play Console 업로드. 등록비 일회성(금액은 확인 필요).
- 이 빌드 산출물(.ipa/.aab)은 각자의 빌드 환경에서 생성해야 하며, 이 저장소에는 소스만 포함됨.
