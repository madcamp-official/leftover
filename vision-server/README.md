# vision-server

웹캠으로 자세(패링/쭈그리기)를 인식해 UDP로 Unity(pc-game)에 전송하는 Python 프로세스.
기획서 4장 "웹캠 비전 처리 방식" 중 **Python(MediaPipe) 분리 프로세스** 방식 (Day 2 목표,
레이턴시 문제 생기면 Unity Sentis로 전환 검토).

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
캘리브레이션(쭈그리기 판정 기준선) 한 번 해줘야 `crouch`가 정상 동작한다. 종료는 `q`.

## 현재 상태 (Day 2 완료)

- [x] 웹캠 캡처 + MediaPipe Pose landmark 오버레이
- [x] UDP로 PROTOCOL.md 포맷에 맞춰 전송하는 배관(plumbing) 완료
- [x] `detect_guard_up`, `detect_crouch` 판별 로직 (임계값 기반, Tier 1)

## 이 머신에서만 해당하는 이슈

- `mp.solutions.pose`(레거시 API)가 이 mediapipe 빌드(0.10.35, Python 3.14 win_amd64)엔
  없어서 Tasks API(`mediapipe.tasks.python.vision.PoseLandmarker`)로 작성했다. 다른
  머신에서 레거시 API가 된다면 그쪽이 더 간단하니 되돌려도 무방.
- `models/pose_landmarker_lite.task`를 `model_asset_path`로 로드하면 경로에 한글이
  섞여 있을 때(`몰입캠프4주차` 등) 네이티브 레이어가 못 찾는다. `model_asset_buffer`로
  바이트를 직접 넘기는 방식으로 우회했다 (`main.py`의 `load_landmarker()` 참고).

## 참고

- 전송 포맷: [../shared/PROTOCOL.md](../shared/PROTOCOL.md) 3번 항목
- 발표 환경 조명에 따라 인식률이 크게 달라지므로 (기획서 7 리스크) 실제 사용할 조명에서
  `pose_confidence` 값을 미리 확인해볼 것.
