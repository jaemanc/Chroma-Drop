# Chroma Drop 스테이지/토폴로지/아이템 시스템

## 루프 프로토콜
1. SPEC 구현 → 2. VERIFY 전체 실행 → 3. FAIL 항목만 수정 → 4. 2로 복귀.
   VERIFY 전항목 PASS면 종료.

출력 형식 (매 루프 이것만):
LOOP n | FAIL: V2,V17 | ATTEMPT: V2=1 V17=3
코드 설명, 진행 상황 서술, 리팩터링 제안 금지.

규칙:
- PASS 항목 재작업 금지.
- 항목별 실패 횟수를 카운트한다. 같은 항목이 3회 연속 FAIL이면
  그 항목 수정을 중단하고 BLOCKED로 표시한다.
- BLOCKED 항목이 생겨도 나머지 항목은 계속 진행한다.
- 남은 항목이 전부 PASS 또는 BLOCKED가 되면 루프를 종료하고
  아래 형식으로 보고한다:

  BLOCKED: V18
  시도: 1) 보정계수 0.8→0.7  2) maxCells 상한 추가  3) 축별 가중 분리
  결과: 편차 34%~41%, 30% 미달성
  원인: hex 축별 길이 편차가 커서 단일 계수로 정규화 불가
  선택지:
  a) 임계값 30%→40% 완화
  b) 축별 개별 보정계수 도입 (설정 필드 추가 필요)
  c) hex line을 고정 길이 소거로 변경 (게임 규칙 변경)
  → 판단 요청. 임의로 하나 골라 진행하지 마라.

- 같은 수정을 반복하지 마라. 3회 시도는 서로 다른 접근이어야 한다.
- 한 항목을 고치다 다른 PASS 항목이 깨지면, 깨진 항목의
  카운트를 리셋하지 말고 이어서 센다.
- 임계값이나 SPEC 자체를 임의로 완화해서 PASS 시키지 마라.
  완화가 필요하면 BLOCKED로 올려 판단을 요청한다.

## SPEC

### 규칙 (고정, 변경 금지)
- 조각 배치 게임. 스왑/이동 없음. ROTATE는 조각 회전만.
- 중력: 위→아래 고정. 상수. 설정값/변수로 만들지 말 것.
- 소거: 인접 같은 색 연결 그룹 크기 >= minGroupSize. 선/대각 매칭 없음.
- 색: 이름 없음. 인덱스 0..paletteSize-1. 코드에 색 리터럴 금지.

### 구조
stages/  stages.json  stage-schema.json  curve.config.json
src/     TopologyGen  PaletteGen  StageLoader  StageValidator
CurveGen  ItemSystem  GameEngine

- src/ 에 스테이지 번호, 목표 수치, 색상값 리터럴 금지.
- stages.json 교체만으로 반영(빌드 불필요).
- GameEngine은 x,y,width,height 미참조.

### 보드 = 그래프
Cell { id, neighbors[], fallTarget|null, isSpawn, poly[] }
Topology { name, neighborCount, axes[] }
Axis { index, label, forward, backward }  // neighbors 인덱스

토폴로지 3종:
- square   이웃4, 축2 (가로/세로)
- triangle 이웃3, 축3. 위향/아래향 교대. 방향별 매핑 분리
- hex      이웃6, 축3 (수직/대각2). flat-top 고정. 수평축 없음
  정오각형 미지원.

fallTarget: square=바로아래, hex=같은열 아래, triangle=교대 매핑.
계산은 각 생성기 담당. GameEngine 미관여. 순환 금지.

### 시드
스테이지당 seed 1개. 스트림 분리:
topologyStream / paletteStream / boardStream / spawnStream / refillStream
한 스트림 소비가 타 스트림 결과에 영향 주면 안 됨. 시드 동일 = 결과 동일.

### 팔레트 (절차 생성)
palette { size, hueSpread, satRange, lightRange, minDeltaE:22, minContrast:1.8 }
- 색 간 CIELAB ΔE < minDeltaE 이면 재생성. 무한루프 금지(hueSpread 자동 확대).
- 색맹 시뮬(적록/청황) ΔE 검사 실패 시 경고 로그.

### 스테이지 스키마
{
stageId, seed,
topology: { mode:"random", allowed:["square","triangle","hex"], weights:[] },
palette: {...},
grid: { size, initialFillRatio, fillPattern },      // width/height 아님
pieces: { shapes[], colorsPerPiece, weights[], rerollCount },
refill: { mode:"drip|instant|none", blocksPerClear, delayMs, colorWeights[] },
obstaclePlacement: { ratio, pattern:"scattered|wall|cluster|ring", avoidSpawnCells },
items: { available[], itemCostsMove, dropFromMatch{} },
objectives: [ {type,target,...} ], objectiveMode:"all|any",
limits: { moves, timeSeconds },
matchRule: { minGroupSize, chainReaction }
}
- obstacles는 좌표 아닌 셀 id 또는 절차 배치 규칙.
- objectives: clear_count | clear_color(colorIndex) | break_obstacle |
  reach_score | clear_group_size
- 스테이지 1~5는 topology 강제 square.

### 기믹
1. initialFillRatio<1.0 → 부분 충전 시작. 소거 시 blocksPerClear 만큼만 보충.
   mode=none이면 소모전. 초기 보드에 즉시 소거 그룹 생성 금지.
2. 장애물: brick(hitsToBreak 후 제거) / locked(영구, 낙하 차단) /
   frozen(인접 소거 1회로 해제). 장애물 칸 배치 불가.
3. locked의 보드 분단 허용. 분단 영역·사장 영역은 에러 아닌 로그.
   신규 블록 진입 불가 영역은 refill 제외.

### 아이템
spatial: { id, effect, axisMode:"player_choice|random|fixed", fixedAxis,
blockedBy:["locked"], damages:["brick","frozen"], maxCells }
effect: line | burst(BFS 반경) | ring(BFS 정확거리) | color | cross(전축)
meta:   reroll | add_moves | add_time  (보드 무관, 별도 경로)

- 가로/세로 아이템 분리 금지. line 하나 + 축 인덱스.
- 축 선택 UI는 topology.axes 길이로 생성. 버튼 수 하드코딩 금지.
- 반경은 그래프 홉 수. 좌표 거리 금지.
- 프리뷰와 실행은 동일 함수 호출.
- 아이템 소거는 minGroupSize 무시. clear_group_size 목표엔 미카운트.
- line은 locked에서 정지. 분단 시 기준 셀 영역 내에서만 작동.

### 난이도 (curve.config.json)
축: initialFillRatio 1.0→0.5 / blocksPerClear 5→2 / 장애물비 0→20% /
paletteSize 4→6 / minGroupSize 3→4 / rerollCount 5→1 /
objectives 1→3개 / moves 여유 감소
- 스테이지당 변화 축 1~2개. paletteSize와 minGroupSize 동시 상향 금지.
  topologyModifiers: 이웃 많을수록 minGroupSize/paletteSize 상향, moves 하향.
  itemModifiers: hex는 lineMaxCellsRatio 0.8, burstRadius -1.
  수동 수정 스테이지는 "locked":true 로 덮어쓰기 방지.

### 렌더링
- 셀 다각형 바운딩박스 → 보드 영역에 스케일·센터링. 셀 크기 상수 금지.
- 터치 판정 point-in-polygon. 사각 히트박스 금지.
- 최소 터치 44pt 미달 시 경고.

## VERIFY
자동 실행 스크립트로 작성. 결과는 항목번호 + PASS/FAIL 만 출력.

V1  src/ 전체에 색 리터럴("#" hex, 색 이름)·스테이지 번호 리터럴 0건
V2  GameEngine에 x/y/width/height/column 참조 0건
V3  GameEngine에 topology 이름 분기(if name=="hex" 등) 0건
V4  3종 토폴로지 전부 fallTarget 그래프 순환 0건
V5  3종 전부 isSpawn 셀에서 도달 불가 셀 목록 산출(에러 아님, 출력만)
V6  각 토폴로지 모든 축 순회 결과가 직선(좌표 검증). triangle 포함
V7  동일 seed 100회 실행 → 보드·팔레트·조각순서·토폴로지 100% 동일
V8  paletteStream만 변경 → spawnStream 결과 불변
V9  생성 팔레트 전 쌍 ΔE >= minDeltaE, 배경 대비 >= minContrast
V10 팔레트 재생성 루프 최대 시도 내 종료(무한루프 없음)
V11 colorWeights 길이 == palette.size, 모든 colorIndex < size
V12 초기 보드에 minGroupSize 이상 그룹 0건
V13 stages.json 30개 전부 스키마 통과, 달성 불가 목표 0건
V14 스테이지 1~5 topology == square
V15 스테이지당 변화 축 <= 2, paletteSize/minGroupSize 동시 상향 0건
V16 5개 effect × 3개 토폴로지 = 15조합 전부 예외 없이 실행
V17 프리뷰 셀 집합 == 실제 소거 셀 집합 (1000회 랜덤 시행)
V18 line 평균 소거 칸수 토폴로지 간 편차 <= 30% (보정 적용 후)
V19 line이 locked 관통 0건
V20 분단 보드에서 아이템이 타 영역 침범 0건
V21 stages.json 값 변경 후 재시작만으로 반영(빌드 미실행)
V22 모든 조각 모양이 해당 토폴로지에서 배치 가능
V23 매 턴 조각 배치 가능 여부 검사 존재, 불가 시 처리 경로 존재
V24 렌더 다각형 겹침·틈 0건, 터치 히트박스 겹침 0건