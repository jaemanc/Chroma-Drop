import { test, after } from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { fulfillPurchase } from './fulfill-purchase.mjs';

// 단위 검사에서는 원자적 커밋 계약을 흉내낸다. 실제 DB 검사는 emulator.test.mjs로 구분한다.
class MemoryDb {
  docs = new Map();
  queue = Promise.resolve();
  retryOnce = false;
  failCommitOnce = false;

  doc(path) { return { path }; }
  snapshot(ref) {
    const data = this.docs.get(ref.path);
    return { exists: data !== undefined, data: () => structuredClone(data) };
  }
  async runTransaction(callback) {
    const previous = this.queue;
    let release;
    this.queue = new Promise((resolve) => { release = resolve; });
    await previous;
    try {
      const writes = new Map();
      const tx = {
        get: async (ref) => this.snapshot(ref),
        create: (ref, data) => {
          assert(!this.docs.has(ref.path));
          writes.set(ref.path, structuredClone(data));
        },
        set: (ref, data) => writes.set(ref.path, { ...this.docs.get(ref.path), ...structuredClone(data) }),
      };
      if (this.retryOnce) {
        this.retryOnce = false;
        await callback(tx);
        writes.clear();
      }
      const result = await callback(tx);
      if (this.failCommitOnce) {
        this.failCommitOnce = false;
        throw new Error('주입한 커밋 실패');
      }
      for (const [path, data] of writes) this.docs.set(path, data);
      return result;
    } finally { release(); }
  }
}

let firestore;
if (process.env.FIRESTORE_EMULATOR_HOST) {
  assert.match(process.env.FIRESTORE_EMULATOR_HOST, /^(127\.0\.0\.1|localhost):\d+$/, '로컬 에뮬레이터만 허용한다');
  const { initializeApp } = await import('firebase-admin/app');
  const { getFirestore } = await import('firebase-admin/firestore');
  firestore = getFirestore(initializeApp({ projectId: 'demo-chromadrop-payments' }));
  after(() => firestore.terminate());
}

function fixture(changes = {}) {
  const db = firestore || new MemoryDb();
  const uid = randomUUID();
  const purchase = {
    state: 'paid', ownerUid: uid, store: 'apple', environment: 'production',
    transactionId: randomUUID(), productId: 'chromadrop.coins.300', quantity: 1,
    amountMicros: 2000000000, currency: 'KRW', purchasedAtMs: 1790906400000,
    ...changes,
  };
  let verifications = 0;
  const call = (overrides = {}) => fulfillPurchase({
    db, uid, receipt: '테스트 전용 영수증',
    verifyReceipt: async () => { verifications++; return { ...purchase }; }, ...overrides,
  });
  const read = async (path) => firestore ? (await db.doc(path).get()) : db.snapshot(db.doc(path));
  return { db, uid, purchase, call, read, verifications: () => verifications };
}

test('결제 기록과 서버 지급량을 함께 저장한다', async () => {
  const f = fixture();
  const result = await f.call();
  const pay = (await f.read(`pay/${result.paymentId}`)).data();
  const wallet = (await f.read(`user/${f.uid}`)).data();
  assert.equal(pay.status, 'granted');
  assert.equal(pay.uid, f.uid);
  assert.equal(pay.amountMicros, 2000000000);
  assert.equal(pay.currency, 'KRW');
  assert.equal(pay.grantedCoins, 300);
  assert.equal(wallet.coins, 300);
  assert.equal(wallet.totalPurchasedCoins, 300);
  assert.equal(result.alreadyGranted, false);
});

test('지급은 기존 사용자 프로필을 보존하고 pay와 user에만 저장한다', { skip: !!firestore }, async () => {
  const f = fixture();
  f.db.docs.set(`user/${f.uid}`, { name: '기존 사용자', coins: 100, totalPurchasedCoins: 0 });
  await f.call();
  const user = (await f.read(`user/${f.uid}`)).data();
  assert.equal(user.name, '기존 사용자');
  assert.equal(user.coins, 400);
  assert.deepEqual([...new Set([...f.db.docs.keys()].map(path => path.split('/')[0]))].sort(), ['pay', 'user']);
});

test('지갑 필드 없는 기존 프로필의 첫 구매를 지급한다', async () => {
  const f = fixture();
  const path = `user/${f.uid}`;
  if (firestore) await f.db.doc(path).set({ name: '기존 프로필' });
  else f.db.docs.set(path, { name: '기존 프로필' });
  const result = await f.call();
  const user = (await f.read(path)).data();
  assert.equal(result.balance, 300);
  assert.equal(user.name, '기존 프로필');
  assert.equal(user.totalPurchasedCoins, 300);
  assert.equal((await f.call()).alreadyGranted, true);
});

test('손상된 지갑은 첫 구매 초기화로 덮어쓰지 않는다', async () => {
  for (const coins of [null, -1, '100']) {
    const f = fixture();
    const path = `user/${f.uid}`;
    if (firestore) await f.db.doc(path).set({ coins });
    else f.db.docs.set(path, { coins });
    await assert.rejects(f.call(), /서버 지갑 데이터 오류/);
    assert.equal((await f.read(path)).data().coins, coins);
  }
});

test('응답 유실 뒤 같은 영수증을 재전송해도 한 번만 지급한다', async () => {
  const f = fixture();
  const first = await f.call();
  const retry = await f.call();
  assert.equal(retry.paymentId, first.paymentId);
  assert.equal(retry.alreadyGranted, true);
  assert.equal((await f.read(`user/${f.uid}`)).data().coins, 300);
});

test('같은 결제의 동시 요청 10개는 한 번만 지급한다', async () => {
  const f = fixture();
  const results = await Promise.all(Array.from({ length: 10 }, () => f.call()));
  assert.equal(results.filter((r) => !r.alreadyGranted).length, 1);
  assert.equal((await f.read(`user/${f.uid}`)).data().coins, 300);
});

test('다른 결제의 동시 지급에서 잔액 증가가 유실되지 않는다', async () => {
  const f = fixture();
  await Promise.all(Array.from({ length: 5 }, () => f.call({
    verifyReceipt: async () => ({ ...f.purchase, transactionId: randomUUID() }),
  })));
  assert.equal((await f.read(`user/${f.uid}`)).data().coins, 1500);
});

test('샌드박스 구매는 운영 지갑에 지급하지 않는다', async () => {
  const f = fixture({ environment: 'sandbox' });
  await f.call();
  assert.equal((await f.read(`user/${f.uid}`)).exists, false);
  assert.equal((await f.read(`user/${f.uid}/test/wallet`)).data().coins, 300);
});

test('다른 사용자에게 같은 구매 식별자를 재사용할 수 없다', async () => {
  const f = fixture();
  await f.call();
  const otherUid = randomUUID();
  await assert.rejects(f.call({
    uid: otherUid, verifyReceipt: async () => ({ ...f.purchase, ownerUid: otherUid }),
  }), /기존 결제 기록 불일치/);
  assert.equal((await f.read(`user/${otherUid}`)).exists, false);
});

test('Google 구매도 수량대로 한 번만 지급하며 Apple의 거래 식별자와 충돌하지 않는다', async () => {
  const f = fixture({ store: 'google', quantity: 2, amountMicros: 4000000000 });
  const google = await f.call();
  const retry = await f.call();
  assert.equal(google.grantedCoins, 600);
  assert.equal(retry.alreadyGranted, true);
  const apple = await f.call({ verifyReceipt: async () => ({ ...f.purchase, store: 'apple' }) });
  assert.notEqual(apple.paymentId, google.paymentId);
  assert.equal((await f.read(`user/${f.uid}`)).data().coins, 1200);
});

test('같은 거래의 상품이나 금액이 바뀌면 재지급하지 않는다', async () => {
  const f = fixture();
  await f.call();
  for (const changes of [{ productId: 'chromadrop.coins.850' }, { amountMicros: 5000000000 }]) {
    await assert.rejects(f.call({ verifyReceipt: async () => ({ ...f.purchase, ...changes }) }), /기존 결제 기록 불일치/);
  }
  assert.equal((await f.read(`user/${f.uid}`)).data().coins, 300);
});

test('미결제·취소·미등록 상품·잘못된 금액·구매자 불일치는 지급하지 않는다', async () => {
  for (const changes of [
    { state: 'pending' }, { revoked: true }, { productId: 'unknown' },
    { quantity: 0 }, { amountMicros: -1 }, { currency: 'wrong' },
    { ownerUid: 'other' }, { purchasedAtMs: -1 }, { transactionId: '' },
  ]) {
    const f = fixture(changes);
    await assert.rejects(f.call());
    assert.equal((await f.read(`user/${f.uid}`)).exists, false);
  }
});

test('영수증 검증 실패는 DB 기록과 지급을 시작하지 않는다', async () => {
  const f = fixture();
  await assert.rejects(f.call({ verifyReceipt: async () => { throw new Error('검증 실패'); } }), /검증 실패/);
  assert.equal((await f.read(`user/${f.uid}`)).exists, false);
});

if (!firestore) {
  test('트랜잭션 콜백 재시도 중 영수증 검증과 지급 부작용이 반복되지 않는다', async () => {
    const f = fixture();
    f.db.retryOnce = true;
    await f.call();
    assert.equal(f.verifications(), 1);
    assert.equal((await f.read(`user/${f.uid}`)).data().coins, 300);
  });

  test('커밋 실패에는 부분 기록이 없고 재시도로 한 번 지급된다', async () => {
    const f = fixture();
    f.db.failCommitOnce = true;
    await assert.rejects(f.call(), /커밋 실패/);
    assert.equal(f.db.docs.size, 0);
    await f.call();
    assert.equal(f.db.docs.size, 2);
    assert.equal((await f.read(`user/${f.uid}`)).data().coins, 300);
  });
}
