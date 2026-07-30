# 우가우가게임 (Uga Uga Game)

두 사람이 각자 카메라 앞에서 몸을 움직여 겨루는 1:1 카툰 파티 게임입니다. Python
`vision-server`가 MediaPipe로 포즈·표정과 마이크 음량을 읽어 Unity 2D 게임에 전달하고,
Unity가 7개 미니게임의 판정·연출·점수·화면 전환을 담당합니다.

현재 구현 기준일은 **2026-07-30**입니다. 프로젝트가 바뀐 과정과 주요 파일의 추가·삭제 내역은
[DEVELOPMENT_LOG.md](DEVELOPMENT_LOG.md)에 날짜와 커밋 시간별로 정리했습니다.

## 게임 구성

`MatchController.RoundScenes`에 등록된 실제 진행 순서입니다.

| 순서 | 씬 | 게임 | 주 입력 |
|---:|---|---|---|
| 1 | `StoneThrow` | 돌던지기 | 손 들기 |
| 2 | `FruitJump` | 점프해서 과일 따기 | 몸통 높이·입 벌림 |
| 3 | `CoconutCrack` | 머리로 코코넛 깨기 | 손-머리 거리와 왕복 동작 |
| 4 | `StoneOrBanana` | 돌 or 바나나 | 손 들기·머리 기울기·입 벌림 |
| 5 | `StaringContest` | 눈빛싸움 | 눈 감김 비율 |
| 6 | `ScreamDuel` | 소리지르기 | 마이크 음량 |
| 7 | `FeatherFlight` | 깃털날기 | 양손 들기의 상승 에지 |

초기 기획의 `PoseCopy`(자세 따라하기)와 그 이전의 3D 검투 프로토타입은 폐기되어 현재
코드·씬·에셋에 포함되지 않습니다.

## 실행 구조

```text
P1 카메라/마이크 ─ vision-server --player-id p1 ─┐
                                                   ├─ UDP 9100/9101 ─> 호스트 Unity
P2 카메라/마이크 ─ vision-server --player-id p2 ─┘                         │
                                                                            └─ TCP 9200 ─> 클라이언트 Unity
```

- 포즈·얼굴·음량은 UDP `9100`, 로딩 화면의 카메라 JPEG 미리보기는 UDP `9101`을 사용합니다.
- LAN 2인 모드에서는 양쪽 `vision-server`가 모두 **호스트 Unity의 IP**로 전송합니다.
- 호스트 Unity가 판정과 라운드 진행을 담당하고 TCP `9200`으로 클라이언트 Unity에 게임
  이벤트와 로딩 상태를 전달합니다.
- `StoneThrow`, `FruitJump`, `CoconutCrack`, `StoneOrBanana`, `StaringContest`,
  `ScreamDuel`에는 호스트/클라이언트 이벤트 처리가 연결되어 있습니다.
- `FeatherFlight`는 현재 로컬 판정만 구현되어 있어 2대 Unity 실기 동기화가 남아 있습니다.
- Hub에서 네트워크 연결 없이 실행하거나 `혼자하기`를 선택하는 개발·솔로 모드도 지원합니다.

프로토콜 세부 내용은 [shared/PROTOCOL.md](shared/PROTOCOL.md), Unity 네트워크 설계와 남은
검증 항목은 [docs/멀티플레이_분산_아키텍처_설계.md](docs/멀티플레이_분산_아키텍처_설계.md)를
참고하세요.

## 빠른 시작

### 1. vision-server 준비

Windows PowerShell 기준입니다. P1·P2 PC에서 각각 한 번씩 준비합니다.

```powershell
cd vision-server
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

`vision-server/models/pose_landmarker_lite.task`와 `face_landmarker.task`가 필요합니다.
얼굴 모델이 없으면 포즈는 동작하지만 눈빛싸움·입 동작 판정이 정상 작동하지 않습니다.

### 2. 카메라와 마이크 실행

전체 매치에는 소리지르기가 포함되므로 `--voice`를 붙입니다.

```powershell
# P1 PC
.\.venv\Scripts\python.exe main.py --pc-ip <호스트_Unity_IP> --player-id p1 --voice

# P2 PC
.\.venv\Scripts\python.exe main.py --pc-ip <호스트_Unity_IP> --player-id p2 --voice
```

호스트 PC에서 P1을 함께 실행한다면 `<호스트_Unity_IP>` 대신 `127.0.0.1`도 사용할 수
있습니다. 옵션 입력을 안내받으려면 `python run_team_test.py`를 실행합니다.

### 3. Unity 실행

1. Unity `6000.5.5f1`로 [`pc-game`](pc-game/)을 엽니다.
2. 두 PC에서 플레이한다면 양쪽 모두 `Assets/Scenes/Hub.unity`를 실행합니다.
3. 한쪽에서 `호스트로 시작`, 다른 쪽에서 호스트 IP를 입력해 `접속`합니다.
4. 호스트에서 게임을 시작합니다. 씬 사이 로딩 화면에서 랜덤 배경·팁·카메라 미리보기와
   신체 캘리브레이션이 진행됩니다.

방화벽에서 호스트 PC의 UDP `9100`, UDP `9101`, TCP `9200`을 허용해야 할 수 있습니다.

## 저장소 구조

| 경로 | 역할 |
|---|---|
| [`pc-game/`](pc-game/) | Unity 2D 본게임, 7개 씬, 게임 흐름, 네트워크, UI, SFX/BGM |
| [`vision-server/`](vision-server/) | MediaPipe 포즈·얼굴 인식과 선택적 마이크 캡처 |
| [`image/`](image/) | 캐릭터·게임·화면별 원본 PNG 제작 보관소 |
| [`audio/`](audio/) | 효과음과 BGM 원본 |
| [`docs/`](docs/) | 기획, 미니게임 규칙, UI·배포·멀티플레이 설계 |
| [`shared/PROTOCOL.md`](shared/PROTOCOL.md) | Python → Unity UDP 데이터 계약 |

Unity에서 실제로 읽는 런타임 에셋은 주로 `pc-game/Assets/Resources/`에 있습니다. `image/`와
`audio/`는 제작 원본이므로 원본을 바꾼 뒤 Unity용 파일도 함께 반영해야 합니다.

## 핵심 코드

| 코드 | 책임 |
|---|---|
| `PoseInputHub.cs` | P1/P2 포즈·얼굴·음량 상태의 공용 API |
| `PoseStreamReceiver.cs` | UDP 9100 JSON 수신 |
| `CameraPreviewReceiver.cs` | UDP 9101 프리뷰 수신·중계 프레임 반영 |
| `MatchController.cs` | 7라운드 순서, 승패 누적, 씬 전환 |
| `NetworkSession.cs` / `GameEventChannel.cs` | LAN 호스트·클라이언트와 TCP 9200 이벤트 |
| `LoadingScreenController.cs` | 랜덤 배경·팁·준비 상태·캘리브레이션 |
| `GameSfx.cs` / `GameBgm.cs` | 미니게임 효과음과 씬별 루프 BGM |

Unity 구조와 편집 위치는 [pc-game/README.md](pc-game/README.md), Python 옵션과 모델 설치는
[vision-server/README.md](vision-server/README.md), 원본 이미지 규칙은
[image/README.md](image/README.md)를 참고하세요.

팀원이 그대로 복사할 P1/P2 명령은
[docs/멀티플레이_실행_명령어.md](docs/멀티플레이_실행_명령어.md)에 별도로 정리되어 있습니다.

배포용 실행파일(macOS dmg / Windows 폴더)을 만드는 방법은
[docs/빌드_가이드.md](docs/빌드_가이드.md)를 참고하세요.

## 현재 확인할 사항

- 장시간 TCP/UDP 운용 시 지연·재접속·프레임 중계 부하 측정
- Windows에서 배포용 실행파일 빌드 실측(구조는 준비됨, [docs/빌드_가이드.md](docs/빌드_가이드.md)
  2장 참고) — macOS는 dmg까지 실측 완료
- 발표 환경의 카메라 거리·조명·마이크에 맞춘 임계값 최종 튜닝
