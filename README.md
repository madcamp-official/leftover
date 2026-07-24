# leftover

몰입캠프 26s-w4-c2-07 프로젝트 repository

웹캠 모션 인식 보스전 게임 (검 베기/찌르기, 방패 방어/패링, 회피, 발차기 등 8종
동작을 웹캠 하나로 인식) — 기획서: [보스전_게임_기획서.md](보스전_게임_기획서.md)

시작 가이드: [GETTING_STARTED.md](GETTING_STARTED.md)

폰 2대(IMU) 방식은 폐기하고 MediaPipe(웹캠) 단독 인식으로 확정했다 — 기획서 1장 참고.

## 폴더 구조

| 폴더 | 내용 | 담당 |
|---|---|---|
| [`pc-game/`](pc-game/) | 메인 게임 (Unity, Standalone) | 게임/비주얼 |
| [`vision-server/`](vision-server/) | 웹캠 비전 처리 (Python, MediaPipe) | 인식/판정 엔진 |
| [`shared/`](shared/) | 통신 프로토콜 스펙 | 공용 |
| [`prototype/`](prototype/) | Day 1 실험 스파이크 (MediaPipe-only MVP, 폰 IMU 실험 기록) | 참고용 |
