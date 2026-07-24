"""
웹캠 비전 서버 (Python + MediaPipe) -> Unity(PC 게임)로 UDP 전송.
PROTOCOL.md "3. 웹캠 비전 결과" 참고.

Day 1 목표: 웹캠 캡처 + 포즈 랜드마크 오버레이가 뜨는지 확인.
Day 2 목표: guard_up(방패 패링), crouch(쭈그리기) 판별 로직 채워서 UDP 전송. (완료)

주의: 이 머신의 mediapipe 빌드(0.10.35, Python 3.14 win_amd64)는 예전
mp.solutions.pose API가 빠져있고 Tasks API(mediapipe.tasks.python.vision.PoseLandmarker)만
들어있다. 레거시 mp.solutions.pose.Pose(...)를 쓰면 AttributeError가 나서, 아래는
Tasks API 기준으로 작성했다 (다른 머신에서 mp.solutions.pose가 된다면 그쪽이 더 간단하니
필요하면 되돌려도 된다).

인식 로직 (Tier 1: 임계값 기반, 기획서 4-3-4 참고):
  - 쭈그리기(crouch): 어깨-엉덩이 중간 y좌표가 '서 있을 때' 기준보다 일정 비율 이상 내려가면 감지.
    카메라 거리/신장에 따라 절대값이 아니라 "어깨-엉덩이 거리(torso length)" 대비 상대값을 씀.
  - 방패 자세(guard_up): 양 손목이 모두 어깨 높이 근처에서 몸 앞쪽 중앙에 모여있으면 감지.
    depth(z)는 카메라 기준 상대값이라 오차가 크므로 1차는 x,y만 사용.

's' 키: 서 있는 기준 자세 캘리브레이션 (crouch 판정 기준선). 캘리브레이션 전에는
crouch가 항상 False로 나가니, --show로 카메라 창을 보면서 한 번 눌러줘야 한다.

실행: python main.py --pc-ip 127.0.0.1
"""

import argparse
import collections
import json
import pathlib
import socket
import time

import cv2
import mediapipe as mp
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

MODEL_PATH = pathlib.Path(__file__).parent / "models" / "pose_landmarker_lite.task"

# PoseLandmark 인덱스 (BlazePose 33 keypoints)
L_SHOULDER, R_SHOULDER = 11, 12
L_HIP, R_HIP = 23, 24
L_WRIST, R_WRIST = 15, 16

SQUAT_DROP_RATIO = 0.35  # torso 길이 대비 이만큼 더 내려가면 쭈그림으로 판정
SMOOTH_WINDOW = 5


def now_ms() -> float:
    return time.time() * 1000.0


def mid(a, b):
    return ((a.x + b.x) / 2, (a.y + b.y) / 2)


def dist(p1, p2):
    return ((p1[0] - p2[0]) ** 2 + (p1[1] - p2[1]) ** 2) ** 0.5


class MotionClassifier:
    """crouch/guard_up 판정. calibrate()를 한 번 호출해야 crouch가 동작한다."""

    def __init__(self):
        self.baseline_torso_mid_y = None
        self.baseline_torso_len = None
        self.torso_mid_y_hist = collections.deque(maxlen=SMOOTH_WINDOW)

    def calibrate(self, landmarks):
        sh_mid = mid(landmarks[L_SHOULDER], landmarks[R_SHOULDER])
        hip_mid = mid(landmarks[L_HIP], landmarks[R_HIP])
        self.baseline_torso_mid_y = (sh_mid[1] + hip_mid[1]) / 2
        self.baseline_torso_len = dist(sh_mid, hip_mid)
        print(
            f"[calibrate] baseline_y={self.baseline_torso_mid_y:.3f} "
            f"torso_len={self.baseline_torso_len:.3f}"
        )

    def detect_crouch(self, landmarks) -> bool:
        sh_mid = mid(landmarks[L_SHOULDER], landmarks[R_SHOULDER])
        hip_mid = mid(landmarks[L_HIP], landmarks[R_HIP])
        torso_mid_y = (sh_mid[1] + hip_mid[1]) / 2
        self.torso_mid_y_hist.append(torso_mid_y)
        smoothed_y = sum(self.torso_mid_y_hist) / len(self.torso_mid_y_hist)

        if self.baseline_torso_mid_y is None or not self.baseline_torso_len:
            return False
        drop = smoothed_y - self.baseline_torso_mid_y  # y는 아래로 갈수록 증가
        return drop > SQUAT_DROP_RATIO * self.baseline_torso_len

    def detect_guard_up(self, landmarks) -> bool:
        sh_mid = mid(landmarks[L_SHOULDER], landmarks[R_SHOULDER])
        hip_mid = mid(landmarks[L_HIP], landmarks[R_HIP])
        l_wrist, r_wrist = landmarks[L_WRIST], landmarks[R_WRIST]
        shoulder_width = dist(
            (landmarks[L_SHOULDER].x, landmarks[L_SHOULDER].y),
            (landmarks[R_SHOULDER].x, landmarks[R_SHOULDER].y),
        )
        torso_len = self.baseline_torso_len or dist(sh_mid, hip_mid)

        # 양 손목이 어깨 높이(±torso_len*0.5) 안에 있고, 엉덩이보다 위(=들어 올린 상태)
        wrists_near_shoulder_height = (
            abs(l_wrist.y - sh_mid[1]) < torso_len * 0.5
            and abs(r_wrist.y - sh_mid[1]) < torso_len * 0.5
        )
        wrists_up = l_wrist.y < hip_mid[1] and r_wrist.y < hip_mid[1]
        return wrists_near_shoulder_height and wrists_up and shoulder_width > 0


def load_landmarker() -> vision.PoseLandmarker:
    if not MODEL_PATH.exists():
        raise FileNotFoundError(
            f"모델 파일이 없습니다: {MODEL_PATH}\n"
            "python -m pip install mediapipe 후 pose_landmarker_lite.task를 받아주세요."
        )
    # 주의: model_asset_path를 쓰면 mediapipe 네이티브(C) 레이어가 경로를 열다가
    # 한글이 섞인 Windows 경로(예: "몰입캠프4주차")에서 FileNotFoundError를 낸다
    # (한글->CP949 등 인코딩 문제로 추정). Python에서 직접 바이트로 읽어
    # model_asset_buffer로 넘기면 경로 문제를 우회할 수 있다.
    model_bytes = MODEL_PATH.read_bytes()
    options = vision.PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_buffer=model_bytes),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=1,
    )
    return vision.PoseLandmarker.create_from_options(options)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pc-ip", default="127.0.0.1", help="Unity가 돌아가는 PC의 IP (같은 PC면 127.0.0.1)")
    parser.add_argument("--port", type=int, default=9002)
    parser.add_argument("--camera-index", type=int, default=0)
    parser.add_argument("--show", action="store_true", default=True, help="디버그용 카메라 창 표시")
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (args.pc_ip, args.port)

    cap = cv2.VideoCapture(args.camera_index)
    if not cap.isOpened():
        raise RuntimeError(f"카메라 {args.camera_index}번을 열 수 없습니다.")

    landmarker = load_landmarker()
    classifier = MotionClassifier()
    start_time = time.time()
    print("준비되면 화면 보고 똑바로 선 뒤 's' 키로 캘리브레이션 하세요 (crouch 판정 기준선). 종료는 'q'.")

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break

            frame = cv2.flip(frame, 1)  # 거울 모드 (직관적인 좌우)
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            ts_ms = int((time.time() - start_time) * 1000)

            result = landmarker.detect_for_video(mp_image, ts_ms)

            guard_up = False
            crouch = False
            confidence = 0.0

            if result.pose_landmarks:
                landmarks = result.pose_landmarks[0]
                visibilities = [lm.visibility for lm in landmarks]
                confidence = sum(visibilities) / len(visibilities) if visibilities else 0.0

                crouch = classifier.detect_crouch(landmarks)
                guard_up = classifier.detect_guard_up(landmarks)

                if args.show:
                    h, w = frame.shape[:2]
                    for lm in landmarks:
                        cv2.circle(frame, (int(lm.x * w), int(lm.y * h)), 3, (0, 255, 0), -1)

            payload = {
                "t": now_ms(),
                "guard_up": guard_up,
                "crouch": crouch,
                "pose_confidence": round(confidence, 3),
            }
            sock.sendto(json.dumps(payload).encode("utf-8"), dest)

            if args.show:
                cv2.putText(
                    frame,
                    f"guard_up={guard_up} crouch={crouch} conf={confidence:.2f}",
                    (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.6,
                    (0, 255, 0),
                    2,
                )
                cv2.putText(
                    frame, "[s]=calibrate  [q]=quit", (10, 60),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1,
                )
                cv2.imshow("vision-server", frame)
                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    break
                elif key == ord("s") and result.pose_landmarks:
                    classifier.calibrate(result.pose_landmarks[0])
    finally:
        cap.release()
        cv2.destroyAllWindows()
        sock.close()
        landmarker.close()


if __name__ == "__main__":
    main()
