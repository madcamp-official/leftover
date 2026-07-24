# prototype — Day 1 브라우저 기반 스파이크

`pc-game`/`phone-sensor`는 계획대로 Unity + UDP로 만들 거지만, Unity 프로젝트를 세팅하기
전에 "폰 센서 스트리밍 + 시간 동기화"와 "웹캠 모션 인식"이 컨셉적으로 되는지부터 빠르게
검증해본 스파이크다. 폰 쪽은 Unity 앱 대신 **그냥 브라우저**(devicemotion + WebSocket)를
썼다 — 앱 빌드 없이 바로 폰에서 열어볼 수 있어서 반복 속도가 빠르다.

- `pc_server/` — 폰 브라우저 ↔ PC WebSocket, NTP 스타일 시간 동기화, 센서 스트림 수신
  (+ 실험용으로 자이로 각속도로 실제 OS 마우스 커서를 움직여보는 기능 포함)
- `phone_client/` — 폰 브라우저에서 여는 정적 페이지 (devicemotion 스트리머)
- MediaPipe 웹캠 인식 쪽은 `vision-server/`로 병합했다 (아래 "vision-server와의 관계" 참고)

**중요**: 여기서 쓰는 통신 방식은 WebSocket(TCP, wss://)이고, 실제 `PROTOCOL.md`는 UDP
기준이다. 브라우저 JS는 raw UDP 소켓을 열 수 없어서, 진짜 UDP로 가려면 결국 phone-sensor를
Unity 앱으로 만들어야 한다 (기획서에서 애초에 그렇게 정한 이유). 이 프로토타입은 "개념
검증용"이지 최종 통신 계층이 아니다.

## 실행 방법

### 인증서 준비 (최초 1회, 로컬에서 각자 생성)

`pc_server/certs/`는 `.gitignore`에 들어있어서 각자 로컬에서 만들어야 한다:

```bash
mkdir -p prototype/pc_server/certs
openssl req -x509 -newkey rsa:2048 -nodes \
  -keyout prototype/pc_server/certs/key.pem \
  -out prototype/pc_server/certs/cert.pem \
  -days 365 -subj "/CN=localhost"
```

PC와 폰을 **같은 와이파이**(가능하면 폰 핫스팟에 PC 직결)에 연결한 뒤:

```bash
# 터미널 1: 폰이 열 정적 페이지 서빙 (HTTPS)
python prototype/pc_server/https_static_server.py

# 터미널 2: 센서 WebSocket 서버
python prototype/pc_server/sensor_ws_server.py
```

폰 브라우저에서:
1. `https://<PC-IP>:8443` 접속 → 자체서명 인증서 경고가 뜨면 "고급 → 계속 진행"
2. `https://<PC-IP>:8765` 도 새 탭에서 한 번 열어서 인증서를 수락해야 한다
   (WS 포트가 달라서 브라우저가 인증서를 별도로 확인함 — 안 하면 WS 연결이 조용히 실패한다.
   이때 뜨는 "failed to open a websocket connection: invalid Connection header" 텍스트는
   정상이다 — TLS는 이미 수락된 것이고, 그냥 일반 GET이라 handshake가 거부된 것뿐)
3. 원래 탭으로 돌아와 "연결 + 센서 권한 요청" 버튼 클릭
   - iOS는 이 시점에 센서 권한 팝업이 뜬다 (반드시 버튼 클릭 안에서 요청해야 함)
4. 접속 직후 ~0.7초는 폰을 평평한 곳에 가만히 둔다 (자이로 바이어스 자동 캘리브레이션)
5. PC 터미널 2에 접속 로그, 시간동기화 offset/RTT, 이후 Hz/가속도/자이로 요약 로그가 찍힌다

PC의 로컬 IP는 `ipconfig`로 확인 (와이파이 바뀌면 매번 바뀐다. 핫스팟 연결 시에도 재확인 필요).

## 검증된 것들

- **시간 동기화**: NTP 핑퐁 20라운드, 순수 Wi-Fi 접속 시 offset -12.5ms / RTT 4.2ms 수준으로
  정상 동작 확인. 폰 핫스팟 경유 시엔 offset/RTT가 눈에 띄게 나빠짐(수십ms) — 핫스팟이
  AP 역할까지 겸하면서 생기는 스케줄링 지연으로 추정.
- **센서 스트림**: devicemotion 기준 55~65Hz 안정적 수신. latency는 보통 0~90ms인데
  드물게 300ms~3초대 스파이크 발생 — TCP 재전송으로 인한 head-of-line blocking이거나
  폰 화면 꺼짐/백그라운드 전환 시 브라우저의 JS 콜백 스로틀링으로 추정 (둘 다 UDP로
  바꿔도 후자는 안 없어짐 — 전송 프로토콜과 무관한 OS/JS 스케줄링 이슈라서).
- **자이로 기반 실시간 포인터 제어**: 절대각을 적분하지 않고 "각속도(deg/s) → 커서
  속도(px/s)"로 직접 매핑하는 방식으로 실제 OS 마우스 커서를 폰 틸트로 움직이는 실험 완료.
  드리프트 누적이 없어서 폰을 멈추면 커서도 바로 멈춘다. 접속 직후 자이로 바이어스를
  샘플링해 0점을 잡고, 프레임 간 지터는 EMA 스무딩(α=0.35)으로 완화.
  - 폰을 평평하게 눕히고 화면이 하늘을 보는 자세 기준, 실측으로 확정한 축 매핑:
    `gx`(alpha/yaw) → 커서 Y, `gz`(gamma/roll) → 커서 X(부호 반전). 이론상 예상(beta=상하,
    gamma=좌우)과는 실측이 달랐다 — 축 이름만 보고 매핑을 추정하지 말고 반드시 실측할 것.
  - 자세한 파라미터는 `pc_server/sensor_ws_server.py`의 `SIGN_X`/`SIGN_Y`/`CURSOR_SENSITIVITY`/
    `SMOOTH_ALPHA` 참고.
- **웹캠 모션 인식**: 쭈그리기(회피)/방패 자세(패링) Tier 1 임계값 분류 로직을 검증하고
  `vision-server/main.py`에 병합함 (아래 참고).

## vision-server와의 관계

MediaPipe 웹캠 인식은 원래 계획대로 `vision-server/`가 담당한다(Unity `pc-game`에 UDP로
쏘는 배관까지 이미 있었음). 여기서 검증한 쭈그리기/방패 판정 로직(Tier 1 임계값 기반)은
`vision-server/main.py`의 `detect_guard_up`/`detect_crouch` TODO에 그대로 이식해뒀다.
이 mediapipe 빌드가 레거시 `mp.solutions.pose` API를 지원하지 않아서(Tasks API로 전환
필요) `vision-server/README.md`의 "이 머신에서만 해당하는 이슈" 섹션에 원인과 우회법을
적어뒀다.
