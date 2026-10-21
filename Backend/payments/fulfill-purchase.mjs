import { createHash } from 'node:crypto';

// 지급량은 서버가 결정한다. 클라이언트의 코인 수·표시 가격은 신뢰하지 않는다.
const packs = new Map([
  ['chromadrop.coins.300', 300],
  ['chromadrop.coins.850', 850],
  ['chromadrop.coins.1800', 1800],
]);

function check(condition, message) {
  if (!condition) throw new Error(message);
}

function validBalance(value) {
  return Number.isSafeInteger(value) && value >= 0;
}

/**
 * 서버 전용: uid는 인증한 Firebase 사용자이고 verifyReceipt는 스토어 서버 검증 함수다.
 * 외부 HTTP 진입점이나 클라이언트의 verified 플래그로 이 함수를 호출하면 안 된다.
 */
export async function fulfillPurchase({ db, uid, receipt, verifyReceipt }) {
  check(typeof uid === 'string' && uid.length > 0 && uid.length <= 128 && !uid.includes('/'), '인증 사용자 없음');
  check(typeof verifyReceipt === 'function', '스토어 영수증 검증기 없음');

  // 검증은 트랜잭션 밖에서 한 번만 한다. 재시도 콜백에는 DB 작업만 들어간다.
  const purchase = await verifyReceipt(receipt, uid);
  check(purchase && purchase.state === 'paid' && !purchase.revoked, '결제 완료 영수증이 아님');
  check(purchase.ownerUid === uid, '영수증 구매자 불일치');
  check(['apple', 'google'].includes(purchase.store), '지원하지 않는 스토어');
  check(purchase.environment === 'production' || purchase.environment === 'sandbox', '영수증 환경 없음');
  check(typeof purchase.transactionId === 'string' && purchase.transactionId.length > 0 && purchase.transactionId.length <= 4096, '스토어 거래 식별자 없음');
  const packCoins = packs.get(purchase.productId);
  check(packCoins !== undefined, '등록되지 않은 상품');
  check(Number.isSafeInteger(purchase.quantity) && purchase.quantity > 0, '상품 수량 오류');
  const coins = packCoins * purchase.quantity;
  check(validBalance(coins), '지급량 범위 오류');
  check(Number.isSafeInteger(purchase.amountMicros) && purchase.amountMicros > 0, '검증된 결제 금액 없음');
  check(typeof purchase.currency === 'string' && /^[A-Z]{3}$/.test(purchase.currency), '검증된 통화 없음');
  check(Number.isSafeInteger(purchase.purchasedAtMs) && purchase.purchasedAtMs > 0 && !Number.isNaN(new Date(purchase.purchasedAtMs).getTime()), '결제 시각 오류');

  // 사용자와 무관한 전역 키로 같은 영수증을 다른 계정에 재사용하는 것도 막는다.
  const paymentId = createHash('sha256')
    .update(JSON.stringify([purchase.store, purchase.environment, purchase.transactionId]))
    .digest('hex');
  const pay = db.doc(`pay/${paymentId}`);
  // 샌드박스 구매는 운영 잔액에 합산하지 않는다.
  const wallet = db.doc(purchase.environment === 'sandbox' ? `user/${uid}/test/wallet` : `user/${uid}`);
  const details = {
    uid,
    store: purchase.store,
    environment: purchase.environment,
    productId: purchase.productId,
    quantity: purchase.quantity,
    amountMicros: purchase.amountMicros,
    currency: purchase.currency,
    purchasedAtMs: purchase.purchasedAtMs,
    grantedCoins: coins,
    walletPath: wallet.path,
  };

  return db.runTransaction(async (tx) => {
    const previous = await tx.get(pay);
    const walletSnapshot = await tx.get(wallet);
    if (previous.exists) {
      const saved = previous.data();
      check(saved.status === 'granted' && Object.keys(details).every((key) => saved[key] === details[key]), '기존 결제 기록 불일치');
    }
    const data = walletSnapshot.exists ? walletSnapshot.data() : {};
    // 프로필만 있는 기존 사용자의 첫 구매도 지갑을 생성한다. null·음수는 손상으로 거부한다.
    const balance = data.coins === undefined && !previous.exists ? 0 : data.coins;
    const purchasedCoins = data.totalPurchasedCoins === undefined && !previous.exists ? 0 : data.totalPurchasedCoins;
    check(validBalance(balance) && validBalance(purchasedCoins), '서버 지갑 데이터 오류');

    if (previous.exists) {
      check(walletSnapshot.exists && purchasedCoins >= coins, '지급 기록과 지갑 불일치');
      return { paymentId, alreadyGranted: true, grantedCoins: coins, balance };
    }

    check(validBalance(balance + coins) && validBalance(purchasedCoins + coins), '서버 잔액 범위 오류');
    const grantedAt = new Date();
    // 기록과 지급은 함께 커밋된다. 실패하면 둘 다 남지 않는다.
    tx.create(pay, { ...details, status: 'granted', grantedAt });
    tx.set(wallet, {
      coins: balance + coins,
      totalPurchasedCoins: purchasedCoins + coins,
      updatedAt: grantedAt,
    }, { merge: true });
    return { paymentId, alreadyGranted: false, grantedCoins: coins, balance: balance + coins };
  });
}
