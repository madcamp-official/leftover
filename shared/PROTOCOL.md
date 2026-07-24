# 통신 프로토콜 스펙 (v0.3 — Phase 1/2 분리)

> **v0.3 변경점**: 개발 순서를 두 단계로 나눴다.
> - **Phase 1 (지금 구현 대상)**: 폰 없이 MediaPipe(웹캠)만으로 7개 동작 전부 인식. 게임
>   로직을 먼저 완성하고 검증하는 게 목표. 시간 동기화 자체가 필요 없음(PC 프로세스 하나로
>   완결).
> - **Phase 2 (나중, 인식 정확도/반응속도 개선용)**: 검/방패 인식을 폰 2대 IMU로 옮김.
>   Phase 1에서 Unity 쪽에 만들어둔 이벤트 계약은 그대로 두고 이벤트를 "누가 보내느냐"만
>   바뀐다.
>
> Unity 게임 로직은 **Phase 1 이벤트 계약 하나만** 알면 된다 — 아래 "Phase 1" 섹션이
> 지금 당장 구현해야 하는 것이고, "Phase 2" 섹션(구 v0.2 내용)은 나중에 폰을 붙일 때
> 참고할 레퍼런스로 남겨둔다.

---

## Phase 1: MediaPipe 이벤트 프로토콜 (지금 구현 대상)

vision-server(Python, MediaPipe)가 7개 동작을 전부 자체 분류해서 **인식된 동작 이벤트**만
Unity로 보낸다. 원시 센서 데이터를 Unity로 보내고 Unity가 분류하는 게 아니라, 분류는
Python 쪽에서 끝내고 Unity는 "무슨 동작이 인식됐다"만 받는다 — 이게 Phase 2(원시 센서
스트리밍 + PC 측 분류)와 가장 큰 구조적 차이다.

### 포트

| 용도 | 포트 | 방향 |
|---|---|---|
| 동작 인식 이벤트 | UDP 9002 | vision-server(Python) → PC(Unity) |

시간 동기화용 포트(9000/9001)는 Phase 1에서는 쓰지 않는다. 같은 PC 안에서 프로세스 두 개가
로컬호스트로 통신하는 것뿐이라 서로 다른 시계를 맞출 필요가 없다.

### 이벤트 종류

검/방패의 순간 동작(스윙류)은 **트리거 이벤트**로, 방어/앉기/좌우이동처럼 계속 유지되는
자세는 **상태 이벤트**로 보낸다. Unity 쪽 `CombatInputHub`(`pc-game/Assets/Scripts/Combat/`)가
이 그대로의 이름으로 메서드를 갖고 있으니 필드명을 임의로 바꾸지 말 것.

**트리거 이벤트** (동작이 인식된 그 순간 1회 전송)
```json
{"action": "swing_horizontal"}
{"action": "swing_vertical"}
{"action": "thrust"}
{"action": "parry"}
```

**상태 이벤트** (상태가 바뀔 때마다 전송 — 매 프레임 보낼 필요 없이 변화 시점에만 보내도 됨)
```json
{"action": "guard", "active": true}
{"action": "crouch", "active": true}
{"action": "lateral", "position": "left"}
```
- `guard.active` / `crouch.active`: 자세 유지 중이면 `true`, 풀면 `false`
- `lateral.position`: `"left"` / `"right"` / `"center"`

| 동작(기획서 2장) | `action` 값 | 인식 소스 (MediaPipe Pose 랜드마크) |
|---|---|---|
| 가로 베기 | `swing_horizontal` | 오른쪽 손목 속도벡터, 수평 성분 우세 |
| 세로 베기 | `swing_vertical` | 오른쪽 손목 속도벡터, 수직 성분 우세 |
| 찌르기 | `thrust` | 팔꿈치-손목 거리 축소 / 손 크기 증가(카메라 방향 접근) — 정확도 낮을 수 있는 축, 아래 참고 |
| 기본 방어 | `guard` (active) | 왼쪽 손목이 가슴 근처에서 정지 |
| 패링 | `parry` | 왼쪽 손목이 가슴 근처 → 몸 바깥으로 급가속 |
| 앉기 | `crouch` (active) | 어깨/골반 랜드마크 y좌표 하강 |
| 좌우 움직이기 | `lateral` | 어깨 랜드마크 x좌표 이동 (발 고정, 상체만) |

**찌르기 인식은 리스크가 가장 큰 항목이다.** 단안 웹캠은 카메라를 향해 다가오는 깊이(Z)
방향 움직임을 X/Y만큼 정확히 못 잡는다. Day 1~2에 실측해보고, 신뢰도가 너무 낮으면
**Phase 1에서는 찌르기를 잠깐 빼고 6동작으로 먼저 완성 → Phase 2에서 폰 가속도계로 복귀**
하는 것도 백업 플랜으로 남겨둔다 (가속도계 전방축 감지가 이 동작엔 원래 더 적합, 기획서 2장).

### Unity 쪽 수신부

`pc-game/Assets/Scripts/Combat/NetworkInputProvider.cs`가 이 포맷을 그대로 파싱해서
`CombatInputHub`에 꽂아준다. 게임 로직 개발 중에는 `NetworkInputProvider` 대신
`KeyboardInputProvider`를 씬에 켜두면 같은 이벤트를 키보드로 흉내 낼 수 있다
(J/K/L=베기·찌르기, Space=방어, F=패링, S=앉기, A/D=좌우). 두 Provider는 동시에 켜두지
말 것 — MediaPipe 연동 시점에 Provider 컴포넌트만 교체하면 게임 로직은 그대로 재사용된다.

---

## Phase 2: 폰 2대 원시 센서 스트리밍 (나중, 인식 정확도/반응속도 개선용)

Phase 1으로 게임 로직/밸런스가 검증되고, MediaPipe만으로는 인식 정확도나 반응속도가
아쉬운 동작(특히 찌르기, 방어/패링 타이밍)이 확인되면 그 동작들을 폰 IMU로 옮긴다.
이때도 Unity 게임 로직은 그대로 두고, `CombatInputHub`에 이벤트를 꽂아주는 새 Provider
(`PhoneInputProvider` 같은 이름)만 하나 추가하면 된다 — 굳이 UDP 자체 루프를 안 거치고
`SensorReceiver`가 분류한 결과를 Hub에 바로 호출해도 됨 (같은 Unity 프로세스 안이므로).

같은 로컬 네트워크(검 폰이 핫스팟을 켜고 방패 폰+PC가 거기 접속)에서 동작한다고 가정.
Tier 1(임계값 분류)을 우선 구현한다. 페이로드 최적화(바이너리 패킹)는 필요해지면 진행.

### 포트

| 용도 | 포트 | 방향 |
|---|---|---|
| 시간 동기화 (ping/pong) | UDP 9001 | PC → 각 폰 (ping), 폰 → PC (pong) |
| 센서 스트리밍 (가속도/자이로) | UDP 9000 | 각 폰 → PC |

포트는 두 폰이 공용으로 쓴다 (서로 다른 IP의 기기라 충돌 없음). PC 쪽은 검 폰 IP,
방패 폰 IP를 각각 알고 있어야 하며, 두 IP 모두에 대해 시간 동기화를 독립적으로 수행한다.

### 1. 시간 동기화 (NTP 핑퐁)

기획서 3-1의 알고리즘을 그대로 구현한다. **PC가 클라이언트, 폰이 서버** 역할
(PC 기준 시각으로 모든 걸 정렬해야 하므로). **검 폰, 방패 폰 각각에 대해 독립적으로
수행** — 즉 PC는 두 폰의 IP를 각각 알고 있어야 하고, offset도 폰별로 따로 보관한다.

#### PING (PC → 폰, port 9001)
```json
{"type": "ping", "device": "sword", "seq": 3, "t1": 1721234567890.123}
```
- `device`: 이 ping이 향하는 폰 (`"sword"` 또는 `"shield"`) — 로깅/디버깅용. 실제 라우팅은
  목적지 IP로 이뤄지므로 필수는 아니지만 있으면 로그 읽기 편함
- `t1`: PC가 송신하는 순간의 PC 시각 (ms, `double`)

#### PONG (폰 → PC, port 9001, ping 수신 즉시 응답)
```json
{"type": "pong", "device": "sword", "seq": 3, "t1": 1721234567890.123, "t2": 1721234567895.500, "t3": 1721234567895.600}
```
- `device`: 응답하는 폰이 자기 자신의 역할을 echo (PC가 어느 폰의 pong인지 발신 IP로도
  구분 가능하지만 필드로도 한 번 더 명시)
- `t1`: 받은 ping의 t1을 그대로 echo
- `t2`: 폰이 ping을 수신한 순간의 폰 시각
- `t3`: 폰이 pong을 송신하는 순간의 폰 시각 (t2와 t3 사이 처리 시간은 최소화)

#### PC 측 계산 (pong 수신 시각 t4 기준)
```
RTT          = (t4 - t1) - (t3 - t2)
Clock Offset = ((t2 - t1) + (t3 - t4)) / 2      # phone_clock = pc_clock + offset
```
- 연결 시 폰마다 20회 반복 → offset은 **중앙값(median)** 사용 (평균은 아웃라이어에 취약)
- 폰별로 offset을 따로 저장 (`offsetSword`, `offsetShield`)
- 이후 센서 패킷의 폰 타임스탬프를 PC 기준으로 바꿀 때: `pc_time = phone_time - offset[device]`
- 재연결/네트워크 변화 감지 시 재동기화 (예: 30초마다 5회 정도 재측정해 offset 드리프트 보정)

### 2. 센서 스트리밍 (각 폰 → PC, port 9000)

60~100Hz로 전송. 페이로드는 최소한으로.

```json
{"device": "sword", "seq": 10432, "t": 1721234567900.250, "ax": 0.12, "ay": 9.81, "az": -0.05, "gx": 0.0, "gy": 1.23, "gz": -0.4}
```
```json
{"device": "shield", "seq": 8821, "t": 1721234567901.010, "ax": -0.03, "ay": 9.79, "az": 0.11, "gx": 0.02, "gy": -0.01, "gz": 0.03}
```

| 필드 | 의미 |
|---|---|
| `device` | `"sword"`(폰1) 또는 `"shield"`(폰2) — 어느 폰에서 온 샘플인지, offset 보정과 모션 분류기 라우팅에 사용 |
| `seq` | 증가하는 패킷 시퀀스 번호 (손실 감지용, **폰별로 별도 카운터**) |
| `t` | 샘플링 시점의 **폰** 시각 (ms) — PC에서 해당 폰의 offset으로 보정해서 사용 |
| `ax, ay, az` | 가속도계 3축 (m/s²) |
| `gx, gy, gz` | 자이로 3축 각속도 (deg/s 또는 rad/s — 팀에서 단위 통일, 아래 "결정 필요" 참고) |

PC 수신 측은 `device`별로 `seq` 역전/누락을 따로 감지하고, 오래된 패킷은 버린다.
분류기도 `device`로 분기: `sword` 스트림은 가로/세로/찌르기 3분류, `shield` 스트림은
방어/패링 상태머신으로 보낸다 (기획서 3-4). 분류 결과는 Phase 1과 동일한 이벤트 이름
(`swing_horizontal` 등)으로 `CombatInputHub`에 넘겨서 게임 로직과의 계약을 유지한다.

앉기/좌우 움직이기는 Phase 2에서도 계속 웹캠(MediaPipe) 담당— 폰으로 옮기지 않는다.

## 4. 모션 분류 결과 이벤트 이름 (표준화)

기획서 2장 "동작 목록(8종)"에 대응하는 코드/로그/UI 상의 이름을 통일한다. Unity
분류기(최종), `prototype/mediapipe_only_mvp`, `prototype/pc_server`의 폰 센서 분류
실험이 전부 이 이름을 따른다 — 실험마다 이름이 달라서 로그/문서를 서로 못 알아보는
일이 없도록.

| 이름 (코드/JSON) | 한국어 | 종류 | 담당 (최종 아키텍처 기준) |
|---|---|---|---|
| `swing_horizontal` | 가로 베기 | event (순간) | 검 폰(폰1) IMU |
| `swing_vertical` | 세로 베기 | event (순간) | 검 폰(폰1) IMU |
| `thrust` | 찌르기 | event (순간) | 검 폰(폰1) IMU |
| `guard_up` | 기본 방어 | level (지속 상태) | 방패 폰(폰2) IMU |
| `parry` | 패링 | event (순간) | 방패 폰(폰2) IMU |
| `crouch` | 앉기 | level (지속 상태) | 웹캠(vision-server) |
| `side_step` | 좌우 움직이기 | level (지속 상태, `"left"`/`"right"`/`"none"`) | 웹캠(vision-server) |
| `kick` | 발차기 | event (순간) | 웹캠(vision-server) — 좌/우발 구분 없이 하나로 판정 |

- **event(순간)**: 한 번의 동작을 트리거처럼 발생시키는 것. 재판정을 막기 위한 쿨다운을
  둔다 (같은 스윙 하나가 여러 프레임에 걸쳐 중복 판정되는 것 방지).
- **level(지속 상태)**: 조건이 유지되는 동안 계속 True/값을 유지하는 것. 콜다운 대신
  "일정 시간 이상 유지돼야 인정"하는 홀드 타임을 둬서 순간적인 오탐을 거른다.
- 웹캠 쪽 `crouch`/`side_step`은 이미 `vision-server`가 이 이름 그대로 쓰고 있음 (3장 참고).
- 검/방패 IMU 쪽 5개는 아직 Unity 구현 전이라 `prototype/pc_server`의 Python 실험에서
  먼저 이 이름으로 검증한다 (아래 "결정 필요" 참고 — 폰 IMU 분류는 아직 실험 단계).
- `kick`은 기획서 원안(7종)엔 없던 추가 동작. 게임 내 역할(공격/카운터 등)이 아직
  미정이라 3번 섹션의 JSON 페이로드 필드에는 아직 안 넣었음 — 역할이 정해지면
  `vision-server`의 `{"t":..,"crouch":..,"side_step":..}` 페이로드에 `"kick": true/false`
  필드로 추가하면 된다.

## 결정 필요 (팀 회의에서 확정할 것)

- [ ] (Phase 1) 찌르기 인식이 실측에서 충분히 안정적인지 — 불안정하면 6동작으로 축소할지
- [ ] (Phase 1) 판정 윈도우 값 (기획서 추천 ±250~300ms을 시작점으로, 실측 후 조정)
- [ ] (Phase 2) 자이로 단위: deg/s vs rad/s (Unity `Gyroscope.rotationRate`는 rad/s 기준)
- [ ] (Phase 2) 센서 전송 주기: 60Hz vs 100Hz (폰 발열/배터리 트레이드오프)
- [ ] (Phase 2) 검 폰/방패 폰 IP를 어떻게 설정할지 (MVP는 수동 입력으로 충분)
- [ ] (Phase 2) 접속 시 캘리브레이션 모드 절차 (샘플 스윙 몇 회, 어떤 값을 보정할지)
