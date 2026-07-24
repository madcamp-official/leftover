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

카메라 프리뷰 창이 뜨고 landmark가 그려지면 성공. `q` 로 종료.

## 현재 상태 (Day 1)

- [x] 웹캠 캡처 + MediaPipe Pose landmark 오버레이
- [x] UDP로 PROTOCOL.md 포맷에 맞춰 전송하는 배관(plumbing) 완료
- [ ] `detect_guard_up`, `detect_crouch` 실제 판별 로직 (Day 2, `main.py` 안의 TODO 참고)

## 참고

- 전송 포맷: [../shared/PROTOCOL.md](../shared/PROTOCOL.md) 3번 항목
- 발표 환경 조명에 따라 인식률이 크게 달라지므로 (기획서 7 리스크) 실제 사용할 조명에서
  `pose_confidence` 값을 미리 확인해볼 것.
