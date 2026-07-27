# 시작 가이드

기획: [저능아게임_기획_프롬프트.md](저능아게임_기획_프롬프트.md). 웹캠 한 대 앞에 두 사람이
서서(화면 왼쪽=P1, 오른쪽=P2) 6개 미니게임을 순서대로 1판씩 겨루고, 더 많이 이긴 사람이
최종 승자.

## 역할 분담

- **입력팀** — `vision-server/`(Python, MediaPipe) + `pc-game/Assets/Scripts/Common/`(Unity):
  웹캠에서 두 사람의 관절 13개 + 입 벌림/눈 감김 비율을 매 프레임 그대로 뽑아서
  `PoseInputHub`라는 단일 창구에 채워 넣는다. 동작 분류는 하지 않는다 - 게임마다 필요한
  임계값이 달라서, 원시 데이터를 게임팀이 직접 해석하는 게 더 유연하다.
- **게임팀** — `pc-game/Assets/Scripts/Minigames/`: 6개 미니게임 각각의 규칙/판정/연출.
  `PoseInputHub.Instance.Get(PlayerId.P1)`처럼 정리된 API만 보고 개발하면 되고, UDP/JSON을
  몰라도 된다.

두 팀이 공유하는 계약은 `shared/PROTOCOL.md`(와이어 포맷) + `PoseInputHub.cs`의 public API
뿐이다. vision-server가 안 켜져 있어도 `PoseInputHub.Instance.ApplyFrame(...)`을 직접 호출해서
가짜 데이터를 넣어보면 미니게임 로직을 독립적으로 테스트할 수 있으므로, 서로 기다릴 필요 없이
동시에 작업 가능하다.

## 지금 레포 상태

- `vision-server/main.py`: MediaPipe Pose Landmarker(최대 2명) + Face Landmarker(최대 2명)로
  매 프레임 포즈+표정을 UDP 9100으로 스트리밍. 완료.
- `pc-game/Assets/Scripts/Common/`:
  - `PoseInputHub.cs` — 게임팀이 참조할 단일 API (`PlayerPoseState` 및 `IsHandRaised`/
    `HeadTiltRatio`/`IsMouthOpen`/`IsEyeClosedNow`/`HandToHeadDistance` 등 공용 제스처 판정)
  - `PoseStreamReceiver.cs` — UDP 9100 수신 후 `PoseInputHub`에 반영
  - `EyeCloseTimer.cs` / `JumpHeightCalibrator.cs` — 지속시간·캘리브레이션이 필요한 판정용 유틸
  - `RuntimeSpriteFactory.cs` / `CavemanSilhouette.cs` — 아트 리소스 없이 도형으로 원시인 캐릭터
    표현 (원/캡슐 프로시저럴 스프라이트)
  - `GameBootstrap.cs` — 각 미니게임 씬을 단독으로 열어도 필요한 싱글턴을 자동 생성
- `pc-game/Assets/Scripts/GameFlow/`: `MatchController.cs`(6판 진행/점수), `HubController.cs`
  (시작/최종결과 화면)
- `pc-game/Assets/Scripts/Minigames/`:
  - `StaringContest/` — **완전히 구현됨**. 다른 5개를 맡을 사람은 이 파일을 참고용 예시로 볼 것.
  - `StoneThrow/`, `PoseCopy/`, `FruitJump/`, `CoconutCrack/`, `StoneOrBanana/` — 부트스트랩과
    씬 뼈대까지만 채워진 스캐폴드. 각 파일 상단 주석에 저능아게임_기획_프롬프트.md 스펙 요약과
    `TODO` 위치가 명시돼 있음.
- 이전 3D 검투 게임 관련 코드/에셋/기획서는 전부 삭제했다.

## 실행 방법

1. `vision-server`: [vision-server/README.md](vision-server/README.md)대로 모델 파일 확인 후
   `python main.py --pc-ip 127.0.0.1`
2. Unity에서 `Assets/Scenes/Hub.unity`를 열고 Play — "시작" 버튼을 누르면 6판이 순서대로
   진행된다.
3. 미니게임 하나만 따로 테스트하고 싶으면 해당 씬(`Assets/Scenes/<게임이름>.unity`)을 직접
   열고 Play해도 된다 (`GameBootstrap`이 필요한 싱글턴을 자동으로 만들어줌).

vision-server 없이도 Unity는 켜지지만, 이번 게임은 전부 모션 기반이라 실질적으로는
vision-server가 있어야 플레이할 수 있다 (구 버전의 키보드 폴백 같은 건 없음 - 필요하면
`PoseInputHub.Instance.ApplyFrame(...)`을 호출하는 테스트용 디버그 스크립트를 임시로 추가할 것).

## 막히면

- `PoseInputHub.Instance`가 `null`이면: 씬에 `GameBootstrap.EnsureInputSystems()`를 호출하는
  코드가 있는지 확인 (각 미니게임 `Start()`에 이미 들어있음).
- UDP 9100으로 아무것도 안 오면: vision-server 콘솔에
  `[udp] 127.0.0.1:9100 로 연속 포즈 스트림 전송` 로그가 있는지, Unity Console에
  `[PoseStream] UDP 9100 바인딩 실패` 경고가 없는지 확인.
- 눈 감김/입 벌림 값이 계속 기본값(0.3 / 0.0)이면: `vision-server/models/face_landmarker.task`가
  없어서 얼굴 인식을 건너뛰고 있는 것 - `vision-server/README.md` "모델 파일" 참고해서 받을 것.
