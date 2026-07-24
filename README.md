# leftover

몰입캠프 26s-w4-c2-07 프로젝트 repository

듀얼 센서 모션 인식 보스전 게임 — 기획서: [보스전_게임_기획서.md](보스전_게임_기획서.md)

시작 가이드: [GETTING_STARTED.md](GETTING_STARTED.md)

## 폴더 구조

| 폴더 | 내용 | 담당 |
|---|---|---|
| [`pc-game/`](pc-game/) | 메인 게임 (Unity, Standalone) | B: 게임/비주얼 |
| [`phone-sensor/`](phone-sensor/) | 폰 센서 스트리머 (Unity, Android) | A: 인식/판정 엔진 |
| [`vision-server/`](vision-server/) | 웹캠 비전 처리 (Python, MediaPipe) | A: 인식/판정 엔진 |
| [`shared/`](shared/) | 통신 프로토콜 스펙 + Unity 레퍼런스 스크립트 | 공용 |
