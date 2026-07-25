# 시작 가이드

기획서([보스전_게임_기획서.md](보스전_게임_기획서.md)) 2장에 정리된 순서대로:
**Phase 1 — 폰 없이 MediaPipe(웹캠)만으로 7동작 전부 인식해서 게임을 먼저 완성** →
**Phase 2 — 검/방패 인식을 폰 2대 IMU로 옮겨서 정확도·반응속도 개선**.

지금 이 가이드는 **Phase 1 기준**이다. Phase 2(폰 시간 동기화/센서 스트리밍)는
`shared/PROTOCOL.md`의 "Phase 2" 섹션에 레퍼런스로 남겨뒀고, 나중에 붙일 때 참고한다.

## 역할 분담

- **게임 (Unity)** — `pc-game/`: 게이지/판정/데미지 로직, 3D 캐릭터·보스 AI, UI, 이펙트.
  `CombatInputHub`(`pc-game/Assets/Scripts/Combat/`)만 보고 개발하면 되고, 입력이
  키보드에서 오는지 MediaPipe에서 오는지는 몰라도 된다.
- **인식 (MediaPipe)** — `vision-server/`: 웹캠으로 7동작(가로/세로 베기, 발차기, 방어,
  패링, 앉기, 좌우 움직이기)을 인식해서 `shared/PROTOCOL.md` Phase 1 포맷으로 UDP 전송.

두 역할이 공유하는 건 `shared/PROTOCOL.md`의 이벤트 계약 하나뿐이라, 이것만 먼저 맞춰두면
이후엔 완전히 독립적으로 개발 가능하다.

## 지금 레포 상태 (Phase 1 완료)

- `pc-game/`: SF 우주전사 보스전 프로토타입이 `Assets/Scripts/Prototype/BossDuelPrototype.cs`에
  전부 구현돼 있다 (씬 자동 구성, 3D 캐릭터, VFX/사운드, 게이지·판정 로직). `Assets/Scripts/Combat/`:
  - `CombatInputHub.cs` — 게임 로직이 참조하는 단일 입력 창구 (트리거 이벤트 4종 + 상태 3종)
  - `KeyboardInputProvider.cs` — 카메라 없이 테스트할 폴백 입력. J/K/L=가로·세로 베기·발차기,
    Space=방어, F=패링, S=앉기, A/D=좌우 이동
  - `NetworkInputProvider.cs` — vision-server가 UDP 9002로 보내는 MediaPipe 인식 이벤트를 Hub에 꽂아줌
  - `BossDuelPrototype.ConnectInput()`이 Play 시작 시 위 두 Provider를 **자동으로 둘 다** 붙이므로
    씬을 손댈 필요 없이 키보드/웹캠 둘 다(또는 둘 다 동시에) 바로 동작한다
- `vision-server/`: Python + MediaPipe로 7동작 중 6개(가로/세로 베기, 발차기, 기본 방어, 패링,
  앉기, 좌우 회피)를 인식해서 `shared/PROTOCOL.md` Phase 1 포맷으로 UDP 9002 전송까지 완료.
  찌르기(`thrust`)만 스윙과 자꾸 섞여 잡혀서 비활성화 상태
- `shared/PROTOCOL.md`: Phase 1 이벤트 계약(포맷 확정), Phase 2(폰 2대) 레퍼런스도 같이 있음
- `phone-sensor/`: Phase 2에서 쓸 폰 프로젝트, 아직 손 안 댐 (지금은 무시해도 됨)

## 실행 방법

두 프로세스를 같이 켜면 웹캠 모션 인식으로 플레이할 수 있다. 자세한 단계는 각 폴더 README 참고:

1. `vision-server`: `python main.py --pc-ip 127.0.0.1` ([vision-server/README.md](vision-server/README.md))
2. `pc-game`: Unity에서 `Assets/Scenes/SampleScene.unity`를 열고 Play ([pc-game/README.md](pc-game/README.md)
   "실행 방법" 참고)

vision-server를 안 켜도 키보드로 바로 플레이 가능 — 두 입력은 동시에 켜둬도 서로 안 꼬인다.

## 다음 단계

기획서 5장 일정표 참고. Phase 1(웹캠만으로 완결된 데모)은 끝났고, 시간이 남으면 Phase 2로
폰 2대(검/방패 IMU)를 보조 입력으로 붙여 인식 정확도·반응속도를 개선한다 — 이때도
`CombatInputHub`에 새 Provider 하나만 추가하면 되고 게임 로직은 그대로 둔다
(`shared/PROTOCOL.md` "Phase 2" 섹션 참고).

## 막히면

- `CombatInputHub.Instance`가 `null`이면: `BossDuelPrototype`이 `Start()`에서
  `CombatInput` 게임오브젝트를 만드는데, 씬 이름이 `SampleScene`이 아니면 자동 설치가
  안 되니 씬 이름 확인
- 키보드 입력이 두 번씩 찍히면: `KeyboardInputProvider`가 씬에 수동으로 중복 배치돼있지 않은지 확인
- UDP 9002로 아무것도 안 오면: `vision-server`가 실제로 그 포트로 보내고 있는지(콘솔에
  `[udp] 127.0.0.1:9002 로 동작 이벤트 전송` 로그), 방화벽이 로컬 UDP를 막고 있진 않은지,
  Unity Console에 `[NetworkInput] UDP 9002 바인딩 실패` 경고가 없는지 확인
