# Chroma-Drop

color pop! — 칼라 매쳐 퍼즐 게임. HTML 프로토타입 v5에서 확정된 규칙의 Unity C# 이식.

## 프로젝트 구조

```
Assets/
  Scripts/
    ColorMatcherCore.cs   # 게임 규칙 (UnityEngine 비의존). 테스트 완료.
    GameManager.cs        # Unity 표현 계층. 컴파일만 검증(에디터 실행 미검증).
Tests/                    # Assets 밖 — Unity가 임포트하지 않음
  CoreTests.cs            # 코어 검증 테스트 (콘솔 러너)
  UnityStubs.cs           # GameManager 컴파일 검증용 UnityEngine 스텁
```

로직/표현 분리 구조. `ColorMatcherCore`는 엔진과 무관하므로 서버 검증·리플레이·엔진 교체에도
재사용 가능. `Tests/`는 Unity 프로젝트 폴더(`Assets/`) 밖에 있어야 한다 — UnityStubs가
실제 UnityEngine과 충돌하기 때문.

## 테스트 실행

Unity 없이 콘솔에서 실행한다:

```bash
csc Assets/Scripts/ColorMatcherCore.cs Tests/CoreTests.cs -out:core_tests.exe && mono core_tests.exe
# GameManager 컴파일 검증:
csc -target:library Assets/Scripts/ColorMatcherCore.cs Assets/Scripts/GameManager.cs Tests/UnityStubs.cs -out:compile_check.dll
```

## 검증 상태 (정확히)

- **검증됨 (컴파일+테스트)**: `ColorMatcherCore.cs` 전체 — 조각 세트(13종, 전 회전 2x2 미포함),
  페인트 스탬프, 정사각형 DP 판정, 아이템 5종 효과 범위, 발동 BFS 종결성(아이템 30개 최악배치 100회),
  점수/배수, 초기 보드 무매칭(시드 200개 전수 0건), 무작위 30수 불변식, 회전, 난이도/타이머 규칙.
  테스트 23/23 통과.
- **컴파일만 검증됨**: `GameManager.cs` — UnityEngine 스텁으로 문법/타입 확인.
  실제 에디터 실행은 못 했으므로 카메라 배치·HUD 위치·연출 타이밍은 실행 후 조정 필요.
- **미검증/미구현 (아래 TODO)**: 낙하 애니메이션, 아이템 아이콘, 사운드,
  화면 전환(홈/게임/랭킹), 랭킹·공유 UI, 저장.

## Unity 셋업

1. Unity Hub → **Add project from disk** 로 이 저장소 루트를 열기 (2D Built-in Render Pipeline,
   2021 LTS 이상 권장). `ProjectSettings/`·`Packages/`가 없으면 Unity가 기본값으로 생성한다 —
   생성된 후 커밋할 것.
2. 빈 GameObject 생성 → `GameManager` 컴포넌트 추가 → Play
3. 인스펙터에서 `difficulty`(easy/normal/hard), `timeAttack`(체크 시 60초 모드), `seed` 조정

## 조작 (프로토타입 임시)

- 마우스 이동: 조각 위치 프리뷰(고스트가 조각 색으로 표시)
- 클릭: 스탬프
- R 키: 회전
- 모바일 빌드 시 드래그/탭 입력으로 교체 필요 (아래 TODO)

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

## iOS/Android 배포 개요 (검증 아님, 일반 절차)

- 한 Unity 프로젝트에서 Build Settings의 플랫폼만 전환해 양쪽 빌드.
- iOS: Build → Xcode 프로젝트 생성 → Xcode에서 `.ipa` 아카이브 → App Store Connect 업로드.
  Mac + Xcode + Apple Developer 계정 필요(연 비용 발생, 금액은 신청 시점 확인).
- Android: Build → `.aab` 생성 → Google Play Console 업로드. 등록비 일회성(금액은 확인 필요).
- 이 빌드 산출물(.ipa/.aab)은 각자의 빌드 환경에서 생성해야 하며, 이 저장소에는 소스만 포함됨.
