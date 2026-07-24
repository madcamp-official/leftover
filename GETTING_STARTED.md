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
- **인식 (MediaPipe)** — `vision-server/`: 웹캠으로 7동작(가로/세로 베기, 찌르기, 방어,
  패링, 앉기, 좌우 움직이기)을 인식해서 `shared/PROTOCOL.md` Phase 1 포맷으로 UDP 전송.

두 역할이 공유하는 건 `shared/PROTOCOL.md`의 이벤트 계약 하나뿐이라, 이것만 먼저 맞춰두면
이후엔 완전히 독립적으로 개발 가능하다.

## 지금 레포 상태

- `pc-game/`: Unity 프로젝트 생성 완료 (URP). `Assets/Scripts/Combat/`에 이미 있는 것:
  - `CombatInputHub.cs` — 게임 로직이 참조하는 단일 입력 창구 (트리거 이벤트 4종 + 상태 3종)
  - `KeyboardInputProvider.cs` — 임시 입력 소스. J/K/L=가로·세로 베기·찌르기, Space=방어,
    F=패링, S=앉기, A/D=좌우 이동. 씬에 붙이면 바로 키보드로 테스트 가능
  - `NetworkInputProvider.cs` — MediaPipe 연동용. UDP 9002로 오는 이벤트를 Hub에 꽂아줌
    (아직 vision-server가 이 포맷을 안 보내므로 지금은 안 써도 됨)
- `vision-server/`: Python + MediaPipe 웹캠 캡처까지는 되는데, 7동작 분류 로직과
  `shared/PROTOCOL.md` Phase 1 포맷으로의 UDP 전송은 아직 구현 안 됨 — 이번 단계의 핵심 작업
- `shared/PROTOCOL.md`: Phase 1 이벤트 계약(포맷 확정), Phase 2(폰 2대) 레퍼런스도 같이 있음
- `phone-sensor/`: Phase 2에서 쓸 폰 프로젝트, 아직 손 안 댐 (지금은 무시해도 됨)

## 지금 할 일

### 게임 (Unity) 담당

1. `pc-game`을 Unity 에디터로 열기
2. 빈 씬에 GameObject 하나 만들어서 `CombatInputHub`, `KeyboardInputProvider` 두 컴포넌트를 붙이기
3. Play 모드에서 J/K/L/Space/F/S/A/D 눌러보면서 Console에 `[CombatInput] ...` 로그가 찍히는지 확인
4. 확인되면 이제 `CombatInputHub.Instance`의 이벤트/상태를 구독하는 `CombatController` 작성 시작
   — 기획서 2장의 게이지 시스템, 판정(누가 뭘 맞았는지), 회피 성공 시 반격 찬스 로직
5. 3D 캐릭터는 없어도 됨 — 콘솔 로그나 임시 UI 텍스트로 "가로베기 성공! 데미지 10" 같은
   식으로 먼저 로직만 검증하고, 애니메이션/비주얼은 그 다음 (기획서 5장 Day 4 이후)

### 인식 (MediaPipe) 담당

1. `vision-server/README.md` 따라 venv 세팅, `python main.py`로 웹캠 프리뷰 확인
2. `shared/PROTOCOL.md`의 "Phase 1" 표를 보고 `main.py`에 7동작 분류 로직 채우기
   (오른쪽/왼쪽 손목 landmark 속도·위치, 어깨/골반 landmark로 앉기·좌우)
3. 분류 결과를 프로토콜 포맷 그대로 UDP 9002로 전송 (지금 `main.py`는 이전 버전 포맷
   `guard_up`/`crouch`를 보내고 있어서 갱신 필요)
4. **찌르기부터 먼저 실측**: 카메라 앞에서 실제로 찔러보고 안정적으로 잡히는지 확인. 잘
   안 되면 일단 6동작으로 넘기고 찌르기는 뒤로 미뤄도 됨 (PROTOCOL.md "결정 필요" 참고)

## 두 역할 합치기

인식 쪽 UDP 전송이 준비되면, `pc-game` 씬에서 `KeyboardInputProvider`를 끄고
`NetworkInputProvider`를 켜기만 하면 된다. 게임 로직 코드는 전혀 안 건드림 — 이게 이번
아키텍처(이벤트 계약 분리)의 요점.

## 다음 단계

기획서 5장 일정표 참고. Phase 1(Day 1~5)로 웹캠만으로 완결된 데모까지 만들고 나서,
시간이 남으면 Phase 2(Day 6~)로 폰 2대를 붙인다. Phase 1만으로도 발표 가능한 상태를
유지하는 게 이 순서의 안전장치.

## 막히면

- `CombatInputHub.Instance`가 `null`이면: 씬에 Hub 컴포넌트를 가진 GameObject가 있는지,
  Provider보다 먼저 `Awake()`가 실행되는지 확인 (스크립트 실행 순서 문제일 수 있음)
- 키보드 입력이 두 번씩 찍히면: `KeyboardInputProvider`가 씬에 중복으로 붙어있지 않은지 확인
- UDP 9002로 아무것도 안 오면: `vision-server`가 실제로 그 포트로 보내고 있는지, 방화벽이
  로컬 UDP를 막고 있진 않은지 확인
