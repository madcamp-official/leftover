# vision-server

웹캠 한 대 앞에 선 두 사람의 상체 포즈(관절 13개)와 얼굴 상태(입 벌림/눈 감김 비율)를 매
프레임 그대로 UDP로 Unity(`pc-game`)에 스트리밍하는 Python 프로세스. 동작 분류는 하지 않는다 —
미니게임마다 필요한 판정이 달라서 Unity 쪽(`pc-game/Assets/Scripts/Common/`)에서 게임별로
해석한다. 자세한 포맷은 [../shared/PROTOCOL.md](../shared/PROTOCOL.md) 참고.

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

## 실행

```bash
# pc-game과 같은 PC에서 돌릴 경우
python main.py --pc-ip 127.0.0.1

# vision-server를 다른 머신에서 돌릴 경우 (보통은 안 씀, 레이턴시 손해)
python main.py --pc-ip <PC의 로컬 IP>
```

카메라 프리뷰 창이 뜨고 랜드마크가 그려지면 성공. 화면 왼쪽에 선 사람 = `p1`, 오른쪽 = `p2`.
캘리브레이션이 필요 없다 — 원시 좌표를 그대로 보내고, 사람마다 편차가 있는 임계값(눈 감김
EAR, 점프 높이 기준선 등)은 Unity 쪽에서 게임 시작 시 짧게 캘리브레이션한다. 종료는 `q`.

## 현재 상태

- [x] 웹캠 캡처 + MediaPipe Pose Landmarker(최대 2명) + Face Landmarker(최대 2명) landmark
      오버레이
- [x] UDP 9100으로 `PROTOCOL.md` v1.0 포맷(`{"t":..., "players":[...]}`)에 맞춰 매 프레임
      전송 — `pc-game/Assets/Scripts/Common/PoseStreamReceiver.cs`가 그대로 파싱함
- [x] p1/p2 구분: hip 중심 x좌표로 좌/우 정렬 (얼굴도 같은 방식으로 정렬해서 포즈와 짝지음)

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
