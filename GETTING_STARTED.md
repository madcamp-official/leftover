# 시작 가이드

기획서([보스전_게임_기획서.md](보스전_게임_기획서.md)) 5장 일정의 **Day 1**부터 시작한다:
"폰 센서 스트리머 ↔ PC UDP 통신 검증, 시간 동기화 프로토콜 구현".

## 역할 분담 (기획서 6장)

- **A: 인식/판정 엔진** — `phone-sensor/`, `vision-server/`, 시간 동기화, 모션 분류 로직
- **B: 게임/비주얼** — `pc-game/` (3D 캐릭터/보스, 이펙트, UI), 웹캠 비전 연동 지점

두 사람 다 `shared/PROTOCOL.md`를 먼저 읽고 시작 — 이게 두 프로젝트를 잇는 계약(contract)이다.

## 지금 레포 상태

- `pc-game/`, `phone-sensor/` 폴더는 만들어져 있지만 **Unity 프로젝트 자체는 아직 없음**
  (Unity Editor가 이 머신에 설치 안 돼 있어서 Hub GUI로 직접 생성해야 함 — 각 폴더 README 참고)
- `shared/PROTOCOL.md`: 폰↔PC, Python↔Unity 간 UDP 패킷 포맷 정의 완료
- `shared/unity-reference/*.cs`: 시간 동기화 + 센서 스트리밍 레퍼런스 구현체 (Unity 프로젝트
  생성 후 `Assets/Scripts/Network/`로 복사해서 바로 사용 가능)
- `vision-server/`: Python + MediaPipe 스캐폴드, 웹캠 캡처와 UDP 송신 배관까지 완료
  (패링/쭈그리기 판별 로직은 Day 2에 채움)

## Day 1 체크리스트

### A (인식/판정 엔진)

1. `phone-sensor/README.md` 따라 Unity Editor 설치(Android Build Support 포함) + 프로젝트 생성
2. `shared/unity-reference/TimeSyncServer.cs`, `SensorStreamer.cs`를 `phone-sensor/Assets/Scripts/Network/`에 복사
3. `pc-game/README.md` 따라 PC용 Unity 프로젝트도 생성 (B와 병행 가능하면 B가 만들어도 됨)
4. `shared/unity-reference/TimeSyncClient.cs`, `SensorReceiver.cs`를 `pc-game/Assets/Scripts/Network/`에 복사
5. 폰을 실기기로 빌드해서 PC와 같은 네트워크(가능하면 폰 핫스팟)에 연결
6. PC 콘솔에 `[TimeSync] offset=... samples=20 rtt(last)=...` 로그가 찍히는지 확인
7. `SensorReceiver`가 폰을 흔들 때마다 값을 받는지 확인 (`OnSample`에 임시로 `Debug.Log` 켜보기)

### B (게임/비주얼)

1. `pc-game/README.md` 따라 Unity 프로젝트 생성 (A와 병행 가능, 하나만 만들면 됨 — 같이 상의)
2. 3D 캐릭터/보스 에셋 후보 조사 및 기본 씬 세팅 (바닥, 조명, 카메라)
3. `vision-server/README.md` 따라 Python venv 세팅해보고 웹캠 프리뷰가 뜨는지 확인
   (Day 2부터 본격 연동하지만, Day 1에 환경만 미리 검증해두면 Day 2가 빨라짐)
4. 보스 상태머신 설계 초안 잡기 (기획서 4장 아키텍처, Day 4 작업 대비)

## 다음 단계 (Day 2 이후)

기획서 5장 일정표 그대로 따라가면 된다. Day 1이 끝나면:
- Day 2: 웹캠 비전(패링/쭈그리기) 연동, `vision-server/main.py`의 `detect_guard_up`/`detect_crouch` 구현
- Day 3: 두 센서 신호(폰 IMU + 웹캠) 통합 판정 시스템 — `SensorReceiver.OnSample`에서 Tier 1
  임계값 기반 스윙/찌르기 분류 로직 채우기 (기획서 3-4 참고)

## 막히면

- 시간 동기화 offset이 이상하게 크거나(수백 ms 이상) 계속 바뀌면: 같은 네트워크인지,
  방화벽이 UDP 9000/9001을 막고 있진 않은지 먼저 확인
- Android에서 센서 값이 안 들어오면: 에디터가 아니라 **실기기 빌드**로 테스트하고 있는지 확인
  (에디터의 센서 시뮬레이터는 신뢰할 수 없음)
