# phone-sensor

폰에서 돌아가는 Unity 빌드. 가속도/자이로 센서를 읽어 UDP로 PC에 스트리밍하고,
시간 동기화 ping에 응답한다. 담당: **A (인식/판정 엔진)** — 기획서 6장 역할 분담 참고.

빌드 타겟: **Android**.

## 프로젝트 생성 (아직 안 만들어짐 — Unity Editor 필요)

1. Unity Hub → **Installs** 에서 pc-game과 **같은 Editor 버전**에 Android Build Support 모듈
   (+ OpenJDK, Android SDK & NDK Tools) 추가 설치 — Unity Hub 설치 화면에서 체크박스로 선택 가능
2. Unity Hub → **New Project** → 템플릿: **3D (Mobile)** 또는 **3D (URP)**
   (센서 스트리밍만 할 거라 그래픽 퀄리티는 중요하지 않음, 가벼운 템플릿이면 충분)
3. 프로젝트 이름: `phone-sensor`, 위치: 이 폴더(`leftover/phone-sensor`) 바로 안에 생성
4. **File > Build Settings > Android > Switch Platform**
5. 폰을 USB로 연결하고 개발자 모드 + USB 디버깅 켠 뒤 **Build And Run**으로 실제 기기 테스트
   (에디터의 센서 시뮬레이션은 정확하지 않으므로 실기기 필수)

## 프로젝트 생성 후 할 일

1. `.gitignore` 추가 (pc-game README와 동일한 이유)
2. `Assets/Scripts/Network/` 폴더 만들고 [../shared/unity-reference/TimeSyncServer.cs](../shared/unity-reference/TimeSyncServer.cs),
   [SensorStreamer.cs](../shared/unity-reference/SensorStreamer.cs) 복사
3. 빈 GameObject에 두 스크립트 붙이고 `SensorStreamer.pcIp`를 PC의 로컬 IP로 설정
4. **Player Settings**에서 센서 관련 권한 확인 (Android는 대부분 가속도/자이로가 별도 권한
   없이 되지만, 빌드 후 실제로 값이 들어오는지 꼭 확인)
5. 가능하면 폰 핫스팟을 켜고 PC를 그 핫스팟에 직결해서 테스트 (기획서 7 리스크: 발표장 와이파이 혼잡 대비)

## 참고 문서

- 통신 프로토콜: [../shared/PROTOCOL.md](../shared/PROTOCOL.md)
- 전체 가이드: [../GETTING_STARTED.md](../GETTING_STARTED.md)
