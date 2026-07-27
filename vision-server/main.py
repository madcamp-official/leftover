"""
웹캠 비전 서버 (Python + MediaPipe) -> Unity(PC 게임)로 UDP 연속 스트리밍.
shared/PROTOCOL.md 참고.

저능아게임(미니게임 6종)은 게임마다 관절 좌표를 다르게 해석해야 해서, 이 서버는 동작을
분류하지 않는다. 매 프레임 Pose Landmarker가 잡은 두 사람의 상체+하체 관절 13개와,
Face Landmarker로 계산한 입 벌림/눈 감김 정도(비율값) 두 개만 얹어서 그대로 Unity에
스트리밍한다 - 분류는 전부 Unity(pc-game/Assets/Scripts/Common/) 쪽 책임.

주의: 이 머신의 mediapipe 빌드는 레거시 mp.solutions.pose/face_mesh API가 빠져있고
Tasks API(mediapipe.tasks.python.vision.PoseLandmarker/FaceLandmarker)만 들어있다 -
구 버전(main.py 이전 버전) 코드가 이미 이 문제를 Tasks API로 우회한 것을 그대로 따른다.

Face Landmarker 모델(face_landmarker.task)이 models/ 밑에 없으면 얼굴 인식(입 벌림/눈
감김)만 건너뛰고 포즈 스트리밍은 계속한다 - 모델 파일 하나 없다고 서버 전체가 죽을 필요는
없음. 받는 법은 vision-server/README.md 참고.

실행: python main.py --pc-ip 127.0.0.1
"""

import argparse
import json
import math
import pathlib
import socket
import time

import cv2
import mediapipe as mp
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

POSE_MODEL_PATH = pathlib.Path(__file__).parent / "models" / "pose_landmarker_lite.task"
FACE_MODEL_PATH = pathlib.Path(__file__).parent / "models" / "face_landmarker.task"

# PoseLandmark 인덱스 (BlazePose 33 keypoints 중 이번 게임에 쓰는 13개만)
_NOSE = 0
_L_SHOULDER, _R_SHOULDER = 11, 12
_L_ELBOW, _R_ELBOW = 13, 14
_L_WRIST, _R_WRIST = 15, 16
_L_HIP, _R_HIP = 23, 24
_L_KNEE, _R_KNEE = 25, 26
_L_ANKLE, _R_ANKLE = 27, 28

# 구 버전(검투 게임)에서 실측으로 확인한 대로, cv2.flip으로 거울모드를 만들면 MediaPipe가
# 리턴하는 raw L/R 라벨이 사용자 실제 좌우와 반대가 된다. PHYS_*가 "사용자 실제 기준"
# 좌/우를 가리키도록 여기서 한 번에 뒤집어 보정 - 이 파일 안에서는 전부 PHYS_*만 쓴다.
PHYS_R_SHOULDER, PHYS_L_SHOULDER = _L_SHOULDER, _R_SHOULDER
PHYS_R_ELBOW, PHYS_L_ELBOW = _L_ELBOW, _R_ELBOW
PHYS_R_WRIST, PHYS_L_WRIST = _L_WRIST, _R_WRIST
PHYS_R_HIP, PHYS_L_HIP = _L_HIP, _R_HIP
PHYS_R_KNEE, PHYS_L_KNEE = _L_KNEE, _R_KNEE
PHYS_R_ANKLE, PHYS_L_ANKLE = _L_ANKLE, _R_ANKLE

POSE_KEYS = {
    "nose": _NOSE,
    "leftShoulder": PHYS_L_SHOULDER, "rightShoulder": PHYS_R_SHOULDER,
    "leftElbow": PHYS_L_ELBOW, "rightElbow": PHYS_R_ELBOW,
    "leftWrist": PHYS_L_WRIST, "rightWrist": PHYS_R_WRIST,
    "leftHip": PHYS_L_HIP, "rightHip": PHYS_R_HIP,
    "leftKnee": PHYS_L_KNEE, "rightKnee": PHYS_R_KNEE,
    "leftAnkle": PHYS_L_ANKLE, "rightAnkle": PHYS_R_ANKLE,
}

# FaceMesh(468/478점) 랜드마크 인덱스. EAR(Eye Aspect Ratio) 6점 공식의 표준적으로 쓰이는
# 근사 인덱스 - 사람마다 눈매가 달라 절대값 임계값은 Unity 쪽에서 캘리브레이션으로 보정한다.
_FACE_RIGHT_EYE = (33, 160, 158, 133, 153, 144)   # (외곽, 위, 위, 내곽, 아래, 아래)
_FACE_LEFT_EYE = (362, 385, 387, 263, 373, 380)
_FACE_MOUTH_TOP, _FACE_MOUTH_BOTTOM = 13, 14        # 윗입술/아랫입술 안쪽 중앙
_FACE_TOP, _FACE_CHIN = 10, 152                     # 이마/턱 - 입 벌림 정규화용 얼굴 길이 기준


def dist(a, b):
    return math.hypot(a.x - b.x, a.y - b.y)


def _eye_aspect_ratio(lm, eye_indices):
    p1, p2, p3, p4, p5, p6 = (lm[i] for i in eye_indices)
    vertical = dist(p2, p6) + dist(p3, p5)
    horizontal = dist(p1, p4)
    if horizontal == 0:
        return 0.3  # 뜬 눈 기본값 근처로 안전하게
    return vertical / (2.0 * horizontal)


def _mouth_open_ratio(lm):
    face_len = dist(lm[_FACE_TOP], lm[_FACE_CHIN])
    if face_len == 0:
        return 0.0
    return dist(lm[_FACE_MOUTH_TOP], lm[_FACE_MOUTH_BOTTOM]) / face_len


def _hip_center_x(pose_landmarks):
    return (pose_landmarks[PHYS_L_HIP].x + pose_landmarks[PHYS_R_HIP].x) / 2.0


def _face_center_x(face_landmarks):
    return face_landmarks[_FACE_TOP].x


def build_players_payload(pose_result, face_result):
    """이번 프레임의 포즈/얼굴 감지 결과를 hip 중심 x좌표(작은 쪽=p1, 큰 쪽=p2) 기준으로
    정렬해서 최대 2명분의 PROTOCOL.md 포맷 리스트로 조립한다. 얼굴은 별도 모델 detection이라
    포즈와 짝을 맞춰야 하는데, 같은 프레임 안에서 사람이 최대 2명이라는 전제 하에 얼굴도
    x좌표로 정렬해서 포즈와 위치 순서대로 짝짓는다(왼쪽 얼굴 <-> 왼쪽 포즈)."""
    poses = list(pose_result.pose_landmarks) if pose_result.pose_landmarks else []
    poses.sort(key=_hip_center_x)

    faces = list(face_result.face_landmarks) if face_result and face_result.face_landmarks else []
    faces.sort(key=_face_center_x)

    players = []
    for i, pose_lm in enumerate(poses[:2]):
        player = {
            "id": f"p{i + 1}",
            # {"x":..,"y":..} 객체 형태 - Unity JsonUtility가 [x,y] 배열은 커스텀 타입
            # 필드로 못 받아서(배열<->객체 불일치) 애초에 객체로 보낸다.
            "pose": {key: {"x": pose_lm[idx].x, "y": pose_lm[idx].y} for key, idx in POSE_KEYS.items()},
            "face": {"mouthOpenRatio": 0.0, "eyeAspectRatio": 0.3},
        }
        if i < len(faces):
            face_lm = faces[i]
            player["face"] = {
                "mouthOpenRatio": _mouth_open_ratio(face_lm),
                "eyeAspectRatio": (
                    _eye_aspect_ratio(face_lm, _FACE_LEFT_EYE)
                    + _eye_aspect_ratio(face_lm, _FACE_RIGHT_EYE)
                ) / 2.0,
            }
        players.append(player)
    return players


def load_pose_landmarker() -> vision.PoseLandmarker:
    if not POSE_MODEL_PATH.exists():
        raise FileNotFoundError(
            f"모델 파일이 없습니다: {POSE_MODEL_PATH}\n"
            "python -m pip install mediapipe 후 pose_landmarker_lite.task를 받아주세요."
        )
    # model_asset_path 대신 파일을 직접 바이트로 읽어 model_asset_buffer로 넘긴다 - 한글이
    # 섞인 Windows 경로에서 네이티브 레이어가 FileNotFoundError를 내는 문제 우회 (구 버전에서
    # 실측으로 확인된 우회법 그대로 유지).
    options = vision.PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_buffer=POSE_MODEL_PATH.read_bytes()),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=2,
    )
    return vision.PoseLandmarker.create_from_options(options)


def load_face_landmarker():
    """얼굴 모델이 없으면 None을 리턴 - 호출부는 포즈만으로 계속 동작해야 한다."""
    if not FACE_MODEL_PATH.exists():
        print(
            f"[warn] 얼굴 모델이 없습니다: {FACE_MODEL_PATH}\n"
            "  -> 입 벌림/눈 감김 값은 계속 기본값(0.0 / 0.3)으로 전송됩니다.\n"
            "  받는 법은 vision-server/README.md 참고."
        )
        return None
    options = vision.FaceLandmarkerOptions(
        base_options=BaseOptions(model_asset_buffer=FACE_MODEL_PATH.read_bytes()),
        running_mode=vision.RunningMode.VIDEO,
        num_faces=2,
    )
    return vision.FaceLandmarker.create_from_options(options)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pc-ip", default="127.0.0.1", help="Unity가 돌아가는 PC의 IP (같은 PC면 127.0.0.1)")
    parser.add_argument("--port", type=int, default=9100)
    parser.add_argument("--camera-index", type=int, default=0)
    parser.add_argument("--show", action="store_true", default=True, help="디버그용 카메라 창 표시")
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (args.pc_ip, args.port)

    pose_landmarker = load_pose_landmarker()
    face_landmarker = load_face_landmarker()

    cap = cv2.VideoCapture(args.camera_index)
    if not cap.isOpened():
        raise RuntimeError(f"카메라 {args.camera_index}번을 열 수 없습니다.")

    start_time = time.time()
    print(f"[udp] {dest[0]}:{dest[1]} 로 연속 포즈 스트림 전송 (매 프레임)")
    print("화면 왼쪽에 선 사람 = p1, 오른쪽 = p2. 종료는 'q'.")

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break

            frame = cv2.flip(frame, 1)  # 거울 모드 (직관적인 좌우)
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            now_ms = int((time.time() - start_time) * 1000)

            pose_result = pose_landmarker.detect_for_video(mp_image, now_ms)
            face_result = face_landmarker.detect_for_video(mp_image, now_ms) if face_landmarker else None

            players = build_players_payload(pose_result, face_result)
            sock.sendto(
                json.dumps({"t": time.time(), "players": players}).encode("utf-8"),
                dest,
            )

            if args.show:
                h, w = frame.shape[:2]
                for player in players:
                    color = (0, 120, 255) if player["id"] == "p1" else (255, 120, 0)
                    for point in player["pose"].values():
                        cv2.circle(frame, (int(point["x"] * w), int(point["y"] * h)), 4, color, -1)
                    label = (
                        f"{player['id']}  mouth={player['face']['mouthOpenRatio']:.2f}"
                        f"  EAR={player['face']['eyeAspectRatio']:.2f}"
                    )
                    anchor_x = int(player["pose"]["nose"]["x"] * w)
                    anchor_y = max(20, int(player["pose"]["nose"]["y"] * h) - 20)
                    cv2.putText(frame, label, (anchor_x, anchor_y),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)
                if not players:
                    cv2.putText(frame, "NO POSE", (20, 40),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.75, (0, 0, 255), 2)
                cv2.putText(frame, "[q]=quit", (20, frame.shape[0] - 20),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
                cv2.imshow("vision-server", frame)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break
    finally:
        cap.release()
        cv2.destroyAllWindows()
        sock.close()
        pose_landmarker.close()
        if face_landmarker:
            face_landmarker.close()


if __name__ == "__main__":
    main()
