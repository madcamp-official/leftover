# leftover

몰입캠프 26s-w4-c2-07 프로젝트 repository

**우가우가게임** — 두 사람이 각자 카메라 앞에서 MediaPipe로 몸짓을 인식하고, 원시인
캐릭터로 6개 미니게임(돌던지기, 자세따라하기, 점프해서 과일따기, 머리로 코코넛 깨기, 돌 or
바나나, 눈빛싸움)에서 1:1로 겨루는 파티 게임. 기획: [docs/우가우가게임_기획_프롬프트.md](docs/우가우가게임_기획_프롬프트.md)

> 처음엔 웹캠 한 대 앞에 두 사람이 같이 서는 구조였는데, 실측 결과 두 사람이 붙어 있으면
> 인식이 한쪽만 되는 문제가 확인돼 **플레이어 1명당 카메라 1대(온라인 모드)** 로 바꿨다.
> Unity 자체는 한 PC에서만 실행되고 두 사람이 그 화면을 같이 본다 — 자세한 배경/실행법은
> [vision-server/README.md](vision-server/README.md) "실행 모드" 참고.

시작 가이드: [docs/게임_구동방식_정리.md](docs/게임_구동방식_정리.md)

Unity 화면과 캐릭터를 직접 다듬는 법: [docs/UNITY_EDITOR_편집_가이드.md](docs/UNITY_EDITOR_편집_가이드.md)

> 이전에 있던 3D 검투 게임 컨셉과 관련 코드/에셋/기획서는 전부 정리했다 — 그 작업은
> `feature/duel-polish-round3` 등 기존 브랜치 히스토리에 남아있지만, 이 방향으로는 더 이상
> 진행하지 않는다.

## 폴더 구조 및 분업

| 폴더 | 내용 | 담당 |
|---|---|---|
| [`vision-server/`](vision-server/) | 카메라로 포즈+표정을 인식해서 UDP로 연속 스트리밍하는 Python 프로세스(플레이어별로 1대씩 실행) | **입력팀** |
| [`pc-game/Assets/Scripts/Common/`](pc-game/Assets/Scripts/Common/) | UDP 스트림을 받아 정리된 API로 노출하는 공용 계층(`PoseInputHub` 등) - 게임팀이 참조하는 계약 | **입력팀** |
| [`pc-game/Assets/Scripts/GameFlow/`](pc-game/Assets/Scripts/GameFlow/) | 6판 진행/점수 관리 (`MatchController`, `HubController`) | 공용 |
| [`pc-game/Assets/Scripts/Minigames/`](pc-game/Assets/Scripts/Minigames/) | 판정·연출·HUD가 구현된 미니게임 6종. 정적 화면 오브젝트는 씬/프리팹에서 직접 편집 가능 | **게임팀** |
| [`shared/PROTOCOL.md`](shared/PROTOCOL.md) | 두 팀이 합의하는 통신 규약 - 이거 하나만 지키면 서로 독립적으로 작업 가능 | 공용 |

**분업 원칙**: 입력팀은 `PoseInputHub`의 public API(어떤 값을 어떻게 노출할지)까지만 책임지고,
게임팀은 그 API를 소비하는 쪽만 작업한다. vision-server가 아직 안 켜져 있어도
`PoseInputHub.Instance.ApplyFrame(...)`에 직접 가짜 데이터를 넣어보면 미니게임을 독립적으로
테스트할 수 있으므로, 두 팀이 서로를 기다릴 필요 없이 동시에 진행 가능하다.

## 실행

1. `vision-server/README.md`대로, 두 사람이 각자 PC에서 Python 서버 실행
   (`python main.py --pc-ip <Unity PC의 LAN IP> --player-id p1` / `p2`)
2. Unity PC에서 `Hub` 씬을 열고 Play — 두 사람이 그 화면을 같이 보면서 플레이
