# UI 폰트

| 역할 | 파일 | 실제 굵기 |
| --- | --- | --- |
| 게임 화면 로고·타이틀 | Fredoka/Fredoka-Bold.ttf | 700 |
| 메뉴·버튼 | Nunito/Nunito-ExtraBold.ttf | 800 |
| 설명·설정창 | Pretendard/Pretendard-Regular.otf | 400 |
| 점수·남은 이동 횟수 | Fredoka/Fredoka-SemiBold.ttf | 600 |
| 콤보·블록 파괴 효과 | Baloo2/Baloo2-ExtraBold.ttf | 800 |
| 한국어 강조 문구 | Jua/Jua-Regular.ttf | 400 |

메인 화면은 사용자의 요청에 따라 기존 로고·버튼 이미지를 유지한다.
`UiTypography`가 uGUI와 TextMeshPro에서 같은 역할을 적용하며 가짜 Bold/Italic은 더하지 않는다.
라틴 전용 강조 폰트의 한글은 Jua, 본문은 Pretendard로 표시한다.
`Assets/Resources/ui-theme.asset`는 폰트 및 공통 아트의 인스펙터 참조 설정이다.
`Chroma Drop/Apply UI Fonts` 메뉴로 참조를 다시 연결할 수 있다.

Fredoka·Nunito·Baloo 2·Jua는 [Google Fonts](https://github.com/google/fonts)의 정적 TTF,
Pretendard는 [공식 저장소](https://github.com/orioncactus/pretendard)의 Regular OTF를 사용한다.
각 디렉터리에 배포 라이선스를 함께 둔다. 기존 폰트 파일은 다른 참조를 보존하기 위해 남겨 둔다.
