# Chroma Drop

Color pop! — 칼라 매쳐 퍼즐 게임. 조각을 단색으로 찍어(페인트) 같은 색 정사각형을 만들어
터뜨리는 규칙. 로직/표현 분리 구조로, 외부 에셋 의존이 0이다(스프라이트·폰트·사운드·UI 전부 런타임 생성).

## 프로젝트 구조

```
Assets/
  Scenes/Main.unity            # 메인 씬 (카메라 + GameManager 뿐 — 나머지는 런타임 생성)
  Scripts/
    ColorMatcherCore.cs        # 게임 규칙 (UnityEngine 비의존). 코어 테스트 통과.
    GameManager.cs             # 게임 흐름/입력 (터치+마우스), 연출 오케스트레이션
    BoardView.cs               # 보드 렌더링·아이템 아이콘·고스트·파괴 파티클·낙하 연출
    GameUI.cs                  # 홈/HUD/결과 화면 (uGUI 런타임 생성, SafeArea 대응)
    Palette.cs                 # 색상 팔레트 생성 유틸 (HSL→RGB, 순수 함수)
    Sfx.cs                     # 효과음 (절차 생성 PCM — 오디오 에셋 없음)
  Editor/
    ProjectBootstrap.cs        # 씬/빌드/모바일 설정 자동 구성 (ChromaDrop > Setup Project)
    BuildScript.cs             # Android AAB 빌드 자동화 (ChromaDrop > Build Android AAB)
  Tests/PlayMode/              # PlayMode 스모크 테스트 (런타임 실행 검증)
Tests/CoreTests.cs             # Assets 밖 — 코어 콘솔 테스트 (Unity 무관)
```

`ColorMatcherCore`는 엔진과 무관해 서버 검증·리플레이·엔진 교체에 재사용 가능하다.

## 실행

Unity Hub → **Add project from disk** 로 저장소 루트 열기 (Unity 6000.5.4f1) →
`Assets/Scenes/Main.unity` 열고 Play. 씬이 없으면 메뉴 **ChromaDrop > Setup Project** 실행.

## 조작

- **터치(모바일)**: 드래그로 고스트 프리뷰(손가락 위로 띄워 표시), 떼면 스탬프. 회전 버튼.
- **마우스(에디터)**: 이동으로 프리뷰, 클릭으로 스탬프, R 키 회전.

## 게임 흐름 / 구현 상태

- **홈 → 플레이 → 결과** 전체 사이클 동작. 모드(점수/타임어택)·난이도(하/중/상) 선택.
- **점수 기록**: 모드·난이도별 최고 점수를 `PlayerPrefs`에 로컬 저장, 홈/결과에 표시.
- **연출(타격감/디자인)**: 둥근 모서리+그라데이션 타일, 스탬프 팝, 파괴 시 색 파편 파티클 버스트,
  연쇄 깊이·대형 매칭에 비례하는 카메라 셰이크와 점수 팝업, 열별 시차 낙하 애니메이션.
- **사운드**: 스탬프/파괴(연쇄 피치 상승)/아이템/만료/승패 — 전부 절차 생성.

## 테스트

```bash
# 코어 규칙 테스트 (Unity 무관, 어떤 C# 컴파일러든 가능)
MONO=".../6000.5.4f1/Editor/Data/MonoBleedingEdge/bin"
"$MONO/mcs" Assets/Scripts/ColorMatcherCore.cs Tests/CoreTests.cs -out:/tmp/core_tests.exe
"$MONO/mono" /tmp/core_tests.exe

# PlayMode 스모크 테스트 (부팅→홈, 스탬프, 경계 거부, 풀사이클 종료, 타임어택)
Unity -batchmode -runTests -testPlatform PlayMode -projectPath . -testResults results.xml
```

현재 PlayMode 스모크 5/5 통과 (홈 부팅, 스탬프·보드 불변식, 경계 밖 거부, 30수 완주→결과, 타임어택).

## Android AAB 빌드 (Play Store 배포용)

Play Store 요구사항(IL2CPP + ARM64, App Bundle, 서명)을 `BuildScript.cs`가 코드로 강제한다.
서명 자격증명은 **환경변수로만** 주입하며 코드/추적 파일에 하드코딩하지 않는다.

1. **업로드 키스토어 생성** (JDK의 keytool, 한 번만):

   ```bash
   keytool -genkeypair -v -keystore chromadrop-upload.keystore -alias chromadrop \
     -keyalg RSA -keysize 2048 -validity 10000
   ```

2. **자격증명 설정** — `keystore.env` (gitignore됨) 또는 환경변수:

   ```
   CHROMADROP_KEYSTORE=chromadrop-upload.keystore
   CHROMADROP_KEYSTORE_PASS=...
   CHROMADROP_KEY_ALIAS=chromadrop
   CHROMADROP_KEY_PASS=...
   CHROMADROP_VERSION=1.0.0
   CHROMADROP_VERSION_CODE=1        # 업로드마다 증가
   ```

3. **빌드**:
   - 에디터: 메뉴 **ChromaDrop > Build Android AAB**
   - 배치모드(CI 권장):
     ```bash
     Unity -quit -batchmode -nographics -projectPath . -executeMethod BuildScript.BuildAAB
     ```
   - 출력: `build/ChromaDrop.aab` (환경변수 `CHROMADROP_AAB_OUTPUT`로 변경 가능)

4. 생성된 `.aab`를 Google Play Console에 업로드.

> **보안**: `*.keystore`, `keystore.env`, `build/`는 `.gitignore`로 제외된다. 업로드 키스토어와
> 비밀번호는 분실 시 동일 키로 재서명이 불가능하므로 안전하게 백업할 것.
>
> **주의(검증 상태)**: 빌드 파이프라인과 플레이어/서명 설정은 완비돼 있으나, 이 개발 환경에서
> `.aab` 최종 산출은 아직 검증되지 않았다(Android 모듈 전환 직후 `BuildPlayer`가 일시적으로
> `Build target not supported`를 반환한 사례 있음). 에디터가 Android로 완전히 전환된 상태에서
> 재실행 시 정상 산출 예상.

## v5 확정 규칙 (Core에 반영됨)

- 보드 16x16, 3색 랜덤 팔레트(색상환 균등분할+지터)
- 페인트 방식: 조각이 단색을 갖고 찍으면 덮인 칸을 그 색으로 덮어쓰기
- 조각 13종(I,T,S,Z,L,J,I3,V3,PLUS,W5,U5,T5,Z5 — 전부 2x2 미포함)
- 정사각형(2x2+) 매칭 파괴, 크기 배수(2x2=x1,3x3=x2,4x4=x4), 연쇄 +50%/단계
- 아이템 5종: Row/Col/Diag/Bomb9/ColorClear — 매칭에 포함돼 터질 때 발동, 연쇄 발동(BFS)
- 모드: 점수 모드(제한 수 안에 목표 점수) / 타임어택(60초)
- 난이도: 하 30수/15000, 중 25수/25000, 상 20수/40000

> 밸런스 수치(목표/수/확률)는 플레이테스트로 검증되지 않은 추정치다. 실측 후 조정 대상.

## TODO

- 랭킹·공유: 백엔드 필요(Firebase 또는 자체 서버). 국가 대항 집계는 HTML v5 로직을 C#으로 이식.
- AAB 산출 실기기 설치·검증, targetSdk Play 최신 요구 버전 확인.
