"""
웹캠 비전 서버 (Python + MediaPipe) -> Unity(PC 게임)로 UDP 전송.
PROTOCOL.md "3. 웹캠 비전 결과" 참고.

Day 1 목표: 웹캠 캡처 + 포즈 랜드마크 오버레이가 뜨는지 확인.
Day 2 목표: guard_up(방패 패링), crouch(쭈그리기) 판별 로직 채워서 UDP 전송.

실행: python main.py --pc-ip 127.0.0.1
"""

import argparse
import json
import socket
import time

import cv2
import mediapipe as mp

mp_pose = mp.solutions.pose
mp_drawing = mp.solutions.drawing_utils


def now_ms() -> float:
    return time.time() * 1000.0


def detect_guard_up(landmarks) -> bool:
    """TODO(Day 2): 손목이 어깨 높이 근처, 카메라 정면으로 들려 있는지 판별."""
    return False


def detect_crouch(landmarks) -> bool:
    """TODO(Day 2): 어깨-엉덩이 높이(hip.y - shoulder.y 등)로 웅크림 판별."""
    return False


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

    with mp_pose.Pose(
        model_complexity=1,
        min_detection_confidence=0.5,
        min_tracking_confidence=0.5,
    ) as pose:
        try:
            while True:
                ok, frame = cap.read()
                if not ok:
                    break

                frame = cv2.flip(frame, 1)
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                result = pose.process(rgb)

                guard_up = False
                crouch = False
                confidence = 0.0

                if result.pose_landmarks:
                    landmarks = result.pose_landmarks.landmark
                    visibilities = [lm.visibility for lm in landmarks]
                    confidence = sum(visibilities) / len(visibilities)

                    guard_up = detect_guard_up(landmarks)
                    crouch = detect_crouch(landmarks)

                    if args.show:
                        mp_drawing.draw_landmarks(frame, result.pose_landmarks, mp_pose.POSE_CONNECTIONS)

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
                    cv2.imshow("vision-server", frame)
                    if cv2.waitKey(1) & 0xFF == ord("q"):
                        break
        finally:
            cap.release()
            cv2.destroyAllWindows()
            sock.close()


if __name__ == "__main__":
    main()
