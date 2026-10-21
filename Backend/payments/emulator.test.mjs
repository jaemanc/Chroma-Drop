// 에뮬레이터 검사를 단위 검사나 운영 DB 검사로 잘못 실행하지 않는다.
if (!process.env.FIRESTORE_EMULATOR_HOST) {
  throw new Error('FIRESTORE_EMULATOR_HOST를 로컬 에뮬레이터 주소로 지정해야 한다');
}
await import('./fulfill-purchase.test.mjs');
