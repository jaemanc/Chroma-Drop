# 결제 기록과 지급 검증

대상은 iOS App Store와 Android Google Play 모두다. 현재 Unity 설정의 앱 식별자는
두 플랫폼 모두 `com.jaemanc.chromadrop`이며 실제 스토어 등록값과 일치하는지 확인이 필요하다.

현재 구현은 **서버 내부 지급 모듈**이다. Unity IAP·스토어 영수증 검증기·인증 HTTP
진입점은 아직 연결되지 않았다. 이 코드를 추가한 것만으로 실제 결제가 동작하지 않는다.
현재 앱은 오프라인 더미 구매를 로컬 테스트 지갑에 기록한다([확인 절차](../../docs/dummy-preview.md)).
실제 스토어 SDK는 스텁이고 지갑은 로컬 저장이다. `pay`와 서버 지갑이 생성돼도
현재 앱이 그 잔액을 자동으로 읽거나 소비하는 것은 아니다.

## 저장 모델

- `pay/{결제 해시}`: 인증 사용자, 스토어, 운영/샌드박스 구분, 상품, 수량, 검증된
  실제 금액(`amountMicros`, 통화 1단위의 백만분의 일), 통화, 결제 시각, 지급 코인,
  지갑 경로, 지급 시각, `status: granted`.
- `user/{uid}`: 사용자 문서에 운영 코인 잔액과 누적 구매 코인을 병합한다.
  샌드박스 잔액은 `user/{uid}/test/wallet`에 분리해 최상위 컬렉션을
  `pay`, `user`로 유지한다.
- 영수증 원문·구매 토큰·인증 토큰은 기록하거나 로그에 출력하지 않는다.

`fulfillPurchase({ db, uid, receipt, verifyReceipt })`는 Firebase Admin Firestore
인스턴스를 받는다. `uid`는 요청 본문이 아니라 서버가 검증한 Firebase ID 토큰에서
가져와야 한다. `verifyReceipt`는 스토어 서명/서버 API, 앱 식별자, 상품, 결제 완료,
취소/철회 여부, 구매자 연결을 검증한 뒤 다음 결과를 반환해야 한다.

```js
{
  state: 'paid', revoked: false, ownerUid: '인증한 사용자 UID',
  store: 'apple', environment: 'production',
  transactionId: '스토어가 검증한 고유 구매 식별자',
  productId: 'chromadrop.coins.300', quantity: 1,
  amountMicros: 2000000000, currency: 'KRW', purchasedAtMs: 1790906400000
}
```

위 값은 검증기 반환 계약의 예시이며 실제 영수증이 아니다. 클라이언트가 이 객체를
제출하도록 만들면 안 된다. Google은 구매 토큰 등 스토어의 재전송에도 동일한
고유 구매 식별자를 사용해야 한다. Apple 구매자 연결값도 서버에서 사용자와 대조한다.
검증된 실제 금액을 얻지 못했다면 클라이언트 표시 가격으로 대신 쓰지 않는다.

한 Firestore 트랜잭션 안에서 전역 결제 키와 지갑을 읽고, 신규 결제 기록과 지급량을
함께 저장한다. 같은 영수증 재전송은 추가 지급하지 않는다. 커밋 후 응답이 유실돼도
같은 구매 식별자로 재시도한다. 스토어 주문 확정/소비는 서버 지급 성공 이후에 하며,
실패한 주문은 미완료 상태로 남겨 재시도한다.

## 검사

```bash
node --test Backend/payments/fulfill-purchase.test.mjs
cd Backend/payments
npm install firebase-admin --ignore-scripts
npm install --save-dev firebase-tools --ignore-scripts
npm run test:emulator
```

첫 명령은 원자적 커밋 계약을 흉내낸 메모리 DB 단위 검사다. 실제 Firestore 검증으로
보고하지 않는다. 에뮬레이터 검사는 로컬 주소와 `demo-chromadrop-payments` 프로젝트만
사용하며 운영 자격증명을 요구하지 않는다. 커밋 실패 주입·콜백 재시도 주입은 단위
검사로만 수행하고, 동시 요청 검사는 에뮬레이터에서도 수행한다.

## 실제 서비스 연결 전 남은 항목

- App Store/Google Play 상품 및 앱 ID, 영수증 검증 설정, 검증기 구현.
- Firebase 사용자 인증을 검증하는 서버 진입점과 배포 대상.
- 서버 지갑으로 앱 지급·소비를 연결하고, PlayerPrefs 잔액 이전 방식을 확정한다.
  서버 잔액을 읽을 때마다 `Wallet.AddCoins`를 호출하면 중복 지급되므로 사용하지 않는다.
- 운영 규칙에서 클라이언트의 `pay`·서버 지갑 쓰기를 차단한다. 다른 경로의 넓은 허용
  규칙도 함께 확인해야 한다. 이 폴더의 규칙은 에뮬레이터 전용이며 운영에 배포하지 않는다.
- 실제 샌드박스 구매로 영수증 검증·Firebase 기록·앱 잔액·주문 확정까지 검사한다.
- 지급 후 환불/철회 처리와 계정 이전은 별도 구현이 필요하다.

트랜잭션의 원자성과 재시도 규칙은 [Firebase 공식 문서](https://firebase.google.com/docs/firestore/manage-data/transactions),
서버 지급 후 주문 확정은 [Unity IAP 구매 문서](https://docs.unity.com/en-us/iap/purchases)를 기준으로 한다.

## 미지급 방지 보완 (2026-10-20)

- 프로필만 있는 사용자 문서의 첫 구매는 누락된 지갑 필드를 0부터 생성한다.
  이미 지급 기록이 있는데 지갑 필드가 누락되거나 값이 null·음수이면 덮어쓰지 않고 오류로 남긴다.
- 로컬 상점은 코인 차감과 아이템/스킨 지급을 하나의 JSON 스냅샷으로 저장한다.
  기존 보유량은 최초 저장할 때 복사하며 기존 PlayerPrefs 키는 삭제하지 않는다.
- 더미 주문 복구는 해당 거래만 대기 목록에서 제거하고, 다른 미완료 주문은 남긴다.
  기록 손상·저장 예외는 구매 성공으로 알리지 않는다.
- PlayerPrefs는 서버 트랜잭션이나 스토어 영수증 보장이 아니다. 실제 청구는
  서버 지급과 스토어 주문 확정을 연결하기 전까지 활성화하지 않는다.
