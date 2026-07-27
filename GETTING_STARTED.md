# 시작 가이드

기획: [우가우가게임_기획_프롬프트.md](우가우가게임_기획_프롬프트.md). 두 사람이 각자 카메라
앞에서(P1/P2) 6개 미니게임을 순서대로 1판씩 겨루고, 더 많이 이긴 사람이 최종 승자.

> 원래는 웹캠 한 대 앞에 두 사람이 같이 서서 화면 좌/우로 구분하는 구조였는데, 실측 결과
> 두 사람이 붙어 있으면 인식이 한쪽만 되는 문제가 확인돼 **플레이어 1명당 카메라 1대(온라인
> 모드)** 로 바꿨다. 자세한 배경/실행법은 [vision-server/README.md](vision-server/README.md)
> "실행 모드" 참고. 카메라 1대짜리 구모드도 빠른 테스트용으로는 남아있다.

## 역할 분담

- **입력팀** — `vision-server/`(Python, MediaPipe) + `pc-game/Assets/Scripts/Common/`(Unity):
  각자 카메라에서 관절 13개 + 입 벌림/눈 감김 비율을 매 프레임 그대로 뽑아서
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

- `vision-server/main.py`: MediaPipe Pose Landmarker + Face Landmarker로 매 프레임 포즈+표정을
  UDP 9100으로 스트리밍. `--player-id` 온라인 모드(카메라 1대=1명, 기본 권장)와 구모드(카메라
  1대에 두 명, 이력 기반 좌우 매칭)를 둘 다 지원. 완료.
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
    씬 뼈대까지만 채워진 스캐폴드. 각 파일 상단 주석에 우가우가게임_기획_프롬프트.md 스펙 요약과
    `TODO` 위치가 명시돼 있음.
- 이전 3D 검투 게임 관련 코드/에셋/기획서는 전부 삭제했다.

## 실행 방법

1. Unity를 실행할 PC 한 대를 정하고, 두 사람이 각자 노트북(또는 Unity PC 자체 + 노트북
   한 대)에서 vision-server를 켠다 — 모델 파일 확인 등 자세한 건
   [vision-server/README.md](vision-server/README.md) "실행 모드" 참고:
   ```bash
   # 플레이어 1 쪽 PC
   python main.py --pc-ip <Unity PC의 LAN IP> --player-id p1
   # 플레이어 2 쪽 PC
   python main.py --pc-ip <Unity PC의 LAN IP> --player-id p2
   ```
2. Unity에서 `Assets/Scenes/Hub.unity`를 열고 Play — "시작" 버튼을 누르면 6판이 순서대로
   진행된다. 두 사람은 이 화면을 같이 보면서(같은 방에서, 노트북 화면을 나란히 두거나
   외부 모니터 하나를 같이 보는 식으로) 플레이한다 — Unity 자체가 두 화면으로 나뉘어
   따로 렌더링되는 건 아님.
3. 미니게임 하나만 따로 테스트하고 싶으면 해당 씬(`Assets/Scenes/<게임이름>.unity`)을 직접
   열고 Play해도 된다 (`GameBootstrap`이 필요한 싱글턴을 자동으로 만들어줌).
4. 노트북이 한 대뿐이라 빠르게 혼자 테스트하고 싶으면 `--player-id` 없이
   `python main.py --pc-ip 127.0.0.1`로 카메라 1대에 두 명이 같이 서는 구모드도 여전히
   쓸 수 있다(단, 데모 때는 온라인 모드 권장 — 위 "시작 가이드" 상단 참고).

vision-server 없이도 Unity는 켜지지만, 이번 게임은 전부 모션 기반이라 실질적으로는
vision-server가 있어야 플레이할 수 있다 (구 버전의 키보드 폴백 같은 건 없음 - 필요하면
`PoseInputHub.Instance.ApplyFrame(...)`을 호출하는 테스트용 디버그 스크립트를 임시로 추가할 것).

## 막히면

- `PoseInputHub.Instance`가 `null`이면: 씬에 `GameBootstrap.EnsureInputSystems()`를 호출하는
  코드가 있는지 확인 (각 미니게임 `Start()`에 이미 들어있음).
- UDP 9100으로 아무것도 안 오면: vision-server 콘솔에
  `[udp] <IP>:9100 로 연속 포즈 스트림 전송` 로그가 있는지, Unity Console에
  `[PoseStream] UDP 9100 바인딩 실패` 경고가 없는지 확인. 온라인 모드(다른 PC에서 전송)라면
  Unity PC 방화벽이 UDP 9100 인바운드를 막고 있지 않은지도 확인
  (`vision-server/README.md` "네트워크 주의사항" 참고).
- 눈 감김/입 벌림 값이 계속 기본값(0.3 / 0.0)이면: `vision-server/models/face_landmarker.task`가
  없어서 얼굴 인식을 건너뛰고 있는 것 - `vision-server/README.md` "모델 파일" 참고해서 받을 것.
