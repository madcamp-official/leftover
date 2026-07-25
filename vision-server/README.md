# vision-server

웹캠으로 기획서 2장의 7동작(가로/세로 베기, 기본 방어, 패링, 앉기, 좌우 움직이기, 발차기)을
전부 인식해서 UDP로 Unity(pc-game)에 전송하는 Python 프로세스. 폰 2대(IMU) 방식은 폐기하고
이 프로세스 하나가 최종 인식 담당이다 — 기획서 1장 참고.

## 환경 설정

```bash
cd vision-server
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

## 실행

```bash
# pc-game과 같은 PC에서 돌릴 경우
python main.py --pc-ip 127.0.0.1

# vision-server를 다른 머신에서 돌릴 경우 (보통은 안 씀, 레이턴시 손해)
python main.py --pc-ip <PC의 로컬 IP>
```

카메라 프리뷰 창이 뜨고 landmark가 그려지면 성공. 화면 보고 똑바로 선 뒤 `s`로
캘리브레이션(앉기 판정 기준선) 한 번 해줘야 `crouch`가 정상 동작한다. 좌우 회피/방어/패링/
발차기는 캘리브레이션 없이 바로 동작한다. 종료는 `q`.

## 현재 상태

- [x] 웹캠 캡처 + MediaPipe Pose landmark 오버레이
- [x] UDP로 `PROTOCOL.md` "Phase 1" 포맷(`{"action": "swing_horizontal"}` 등)에 맞춰
      전송 — 포트 9002, `NetworkInputProvider.cs`가 그대로 파싱함
- [x] 가로/세로 베기, 기본 방어, 패링, 앉기, 좌우 회피, 발차기 — 6동작 활성 상태
- [ ] 찌르기 — 스윙과 자꾸 섞여 잡혀서 비활성화 (`main.py`의 `_thrust`/`update()` 주석 참고)

## 이 머신에서만 해당하는 이슈

- `mp.solutions.pose`(레거시 API)가 이 mediapipe 빌드(0.10.35, Python 3.14 win_amd64)엔
  없어서 Tasks API(`mediapipe.tasks.python.vision.PoseLandmarker`)로 작성했다. 다른
  머신에서 레거시 API가 된다면 그쪽이 더 간단하니 되돌려도 무방.
- `models/pose_landmarker_lite.task`를 `model_asset_path`로 로드하면 경로에 한글이
  섞여 있을 때(`몰입캠프4주차` 등) 네이티브 레이어가 못 찾는다. `model_asset_buffer`로
  바이트를 직접 넘기는 방식으로 우회했다 (`main.py`의 `load_landmarker()` 참고).

## 참고

- 전송 포맷: [../shared/PROTOCOL.md](../shared/PROTOCOL.md) "Phase 1" 섹션
- 인식 로직 상세(왜 이 지표를 골랐는지, 뭘 시도했다가 버렸는지)는
  [../prototype/mediapipe_only_mvp/main.py](../prototype/mediapipe_only_mvp/main.py)의
  docstring 참고 — 이 파일은 그 검증된 로직을 UDP 전송과 합쳐서 옮겨온 것.
- 발표 환경 조명에 따라 인식률이 크게 달라지므로 (기획서 7 리스크) 실제 사용할 조명에서
  화면에 뜨는 `conf` 값을 미리 확인해볼 것.
