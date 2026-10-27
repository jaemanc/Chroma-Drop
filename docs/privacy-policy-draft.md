# Chroma Drop 개인정보처리방침 초안 — 공개 전 검토 필요

이 문서는 아직 공개용 개인정보처리방침이 아니다. 운영자명, 문의 이메일,
공개 URL, 시행일과 실제 광고 설정을 확정한 뒤 공개한다.

## 현재 구현을 기준으로 확인한 처리

- 점수, 스테이지 진행, 코인·아이템·스킨, 하트 충전 시간, 사운드 설정은 기기에 저장된다.
- 게스트 이름과 국가 선택도 로컬에 보관된다. 현재 원격 랭킹은 중지되어 있다.
- 현재 실제 인앱결제는 제공하지 않는다. 출시 빌드에서는 더미 구매가 차단된다.
- Google Mobile Ads와 User Messaging Platform SDK를 사용한다. 테스트 광고 상태에서도
  SDK의 데이터 처리 여부를 확인해야 하며, 개인정보 미수집으로 단정해서 신고하지 않는다.
- 광고 동의가 필요한 지역에서는 UMP 동의 절차를 진행하며, 필요한 경우 앱 설정의
  AD PRIVACY에서 동의를 변경할 수 있다. 이는 Apple ATT 동의를 대체하지 않는다.

## 공개 전에 확정할 내용

1. 운영자명, 문의 이메일, 시행일
2. 광고 SDK가 실제 수집·공유하는 항목, 목적, 수신자, 보유 기간과 국외 처리
3. 추적 사용 여부와 ATT 적용 여부, 대상 연령 및 아동 대상 여부
4. 기기 내 기록 삭제 방법과 광고 관련 정보 삭제 요청 절차
5. App Store Connect의 개인정보 응답과 이 문서의 일치 여부

공개한 HTTPS URL은 `Assets/Resources/app-info.json`의 `privacyPolicyUrl`에 넣는다.
문의 이메일은 같은 파일의 `supportEmail` 및 공개 문서에 기재한다.
앱 설정에는 유효한 HTTPS URL이 있을 때만 PRIVACY POLICY 버튼이 표시된다.

참조: https://developers.google.com/admob/ios/privacy/data-disclosure
