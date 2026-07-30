# vision-server

한 사람의 상체 포즈(관절 13개)와 얼굴 상태(입 벌림/눈 감김 비율)를 매 프레임 그대로 UDP로
Unity(`pc-game`)에 스트리밍하는 Python 프로세스. 동작 분류는 하지 않는다 — 미니게임마다
필요한 판정이 달라서 Unity 쪽(`pc-game/Assets/Scripts/Common/`)에서 게임별로 해석한다.
자세한 포맷은 [../shared/PROTOCOL.md](../shared/PROTOCOL.md) 참고.

## 환경 설정

```bash
cd vision-server
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

## 모델 파일

두 모델이 `models/` 밑에 있어야 한다:

- `pose_landmarker_lite.task` — 이미 레포에 커밋되어 있음.
- `face_landmarker.task` — 없으면 아래로 받는다 (없어도 서버는 죽지 않고 포즈 스트리밍은
  계속되지만, 입 벌림/눈 감김 값이 계속 기본값으로 나가서 눈빛싸움/점프따기/돌바나나가
  제대로 안 됨):

```bash
curl -L -o models/face_landmarker.task \
  "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task"
```

## 실행 모드

실측 결과 카메라 1대 앞에 두 사람을 같이 세우면 두 사람이 붙어 있을 때 사람 감지 단계의
NMS가 겹친 바운딩박스를 하나로 억제해버려서 한쪽이 인식되지 않는 문제가 확인됐다. 그래서
**온라인 모드(카메라 1대 = 플레이어 1명)를 기본으로 쓴다** — 같은 LAN에 노트북을 두 대
놓고 각자 자기 카메라로 자기 자신만 인식한다. 호스트/클라이언트 Unity를 두 대에서 실행해도
두 vision-server의 `--pc-ip`는 모두 **게임 판정을 담당하는 호스트 Unity PC**로 지정한다.

```bash
# 노트북 A (플레이어 1) - <Unity PC의 LAN IP>는 같은 와이파이/공유기에서 Unity를 실행할
# PC의 IP (예: 192.168.0.12). ipconfig(Windows)/ifconfig(Mac/Linux)로 확인.
python main.py --pc-ip <Unity PC의 LAN IP> --player-id p1

# 노트북 B (플레이어 2)
python main.py --pc-ip <Unity PC의 LAN IP> --player-id p2
```

호스트 Unity PC 자체에서 vision-server 하나를 같이 돌려도 된다(`--pc-ip 127.0.0.1
--player-id p1`), 그러면 노트북은 한 대만 더 있으면 된다. 오프라인 단일 Unity 모드에서도
두 vision-server가 그 Unity PC를 향하게 하는 원칙은 같다.

두 명령 모두 MediaPipe가 이미 읽은 카메라 프레임을 로딩 화면 미리보기용으로도 자동
전송한다. 별도 카메라 프로세스는 필요 없다. 미리보기만 끄려면 `--no-preview`를 추가한다.

### 팀 테스트용 간편 실행

옵션을 외우지 않고 아래 런처를 실행해도 된다. 각 PC에서 Unity PC의 LAN IP와 담당 플레이어
번호만 입력하면 기존 `main.py`를 올바른 옵션으로 실행한다.

```bash
python run_team_test.py
```

또는 입력 과정 없이 바로 실행:

```bash
# 플레이어 1 PC
python run_team_test.py --pc-ip 192.168.0.12 --player-id p1

# 플레이어 2 PC
python run_team_test.py --pc-ip 192.168.0.12 --player-id p2
```

`--player-id`를 생략하면 예전처럼 카메라 1대 앞에 두 사람이 같이 서서 좌/우로 자동 구분하는
구모드로 동작한다(장비가 노트북 한 대뿐일 때 빠르게 테스트하는 용도로는 여전히 쓸 수 있지만,
위 문제 때문에 데모용 기본값으로는 권장하지 않음):

```bash
python main.py --pc-ip 127.0.0.1
```

카메라 프리뷰 창이 뜨고 랜드마크가 그려지면 성공. 캘리브레이션이 필요 없다 — 원시 좌표를
그대로 보내고, 사람마다 편차가 있는 임계값(눈 감김 EAR, 점프 높이 기준선 등)은 Unity 쪽에서
게임 시작 시 짧게 캘리브레이션한다. 종료는 `q`.

## 네트워크 주의사항 (온라인 모드)

- 두 노트북과 호스트/클라이언트 Unity PC가 같은 LAN(같은 공유기/핫스팟)에 있어야 한다.
  현재 배포 범위는 LAN 전용이며 공인 IP·방 코드·릴레이 서버 방식은 지원하지 않는다.
- 호스트 Unity PC의 방화벽이 UDP 9100(포즈/얼굴/음성) 또는 9101(로딩 카메라 미리보기)
  인바운드를 막고 있으면 다른 노트북에서 보낸 패킷이 안 들어올 수 있다 — Windows는
  "Windows Defender 방화벽" > "고급 설정"에서 인바운드 규칙 추가 필요.
- 호스트의 `PoseStreamReceiver.cs`는 모든 인터페이스(`IPAddress.Any`)에서 9100을 수신한다.
  두 노트북이 각자
  `id: "p1"` 또는 `id: "p2"`만 담긴 프레임을 보내도 `PoseInputHub.ApplyFrame()`이 프레임마다
  들어온 id만 갱신하므로 자연스럽게 합쳐진다. 클라이언트 Unity의 화면 상태와 로딩 프리뷰는
  호스트가 TCP 9200 게임 이벤트 채널로 중계한다.

## 현재 상태

- [x] 웹캠 캡처 + MediaPipe Pose Landmarker + Face Landmarker landmark 오버레이
- [x] UDP 9100으로 `PROTOCOL.md` v1.0 포맷(`{"t":..., "players":[...]}`)에 맞춰 매 프레임
      전송 — `pc-game/Assets/Scripts/Common/PoseStreamReceiver.cs`가 그대로 파싱함
- [x] `--player-id` 온라인 모드: 카메라 1대=플레이어 1명, 감지된 첫 사람을 고정 id로 전송
- [x] UDP 9101로 로딩 화면용 320px JPEG 미리보기 전송(기본 5fps, `--no-preview`로 비활성화)
- [x] 구모드(카메라 1대에 두 명) p1/p2 구분: 이력 기반 최근접 매칭으로 라벨 안정화(단순 x좌표
      정렬은 두 사람이 순간적으로 겹치면 라벨이 뒤바뀌는 문제가 있어 개선함), 얼굴도 같은
      원칙으로 짝지음
- [x] 사람 감지 신뢰도 임계값을 낮춰(0.5→0.3) 붙어 서 있을 때 인식 누락을 줄임 — 다만 실측
      결과 완전히 해결되진 않아 온라인 모드 도입의 직접적 계기가 됨

## 이 머신에서만 해당하는 이슈

- `mp.solutions.pose`/`mp.solutions.face_mesh`(레거시 API)가 이 mediapipe 빌드(0.10.35,
  Python 3.14 win_amd64)엔 없어서 Tasks API(`PoseLandmarker`/`FaceLandmarker`)로 작성했다.
  다른 머신에서 레거시 API가 된다면 그쪽이 더 간단하니 되돌려도 무방.
- 두 모델 파일을 `model_asset_path`로 로드하면 경로에 한글이 섞여 있을 때 네이티브
  레이어가 못 찾는 문제가 있었다. `model_asset_buffer`로 바이트를 직접 넘기는 방식으로
  우회했다 (`main.py`의 `load_pose_landmarker()`/`load_face_landmarker()` 참고).

## 참고

- 전송 포맷: [../shared/PROTOCOL.md](../shared/PROTOCOL.md)
- 발표 환경 조명/카메라 거리에 따라 인식률이 크게 달라지므로, 실제 사용할 환경에서 미리
  `--show` 창의 landmark가 잘 잡히는지 확인해볼 것.

## 마이크 입력

소리지르기까지 함께 테스트할 때만 기존 명령 끝에 `--voice`를 붙인다. 같은 Python 프로세스가
카메라와 시스템 기본 마이크를 함께 처리하므로 별도 음성 프로세스를 켤 필요가 없다.
