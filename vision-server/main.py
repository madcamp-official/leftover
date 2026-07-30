"""
웹캠 비전 서버 (Python + MediaPipe) -> Unity(PC 게임)로 UDP 연속 스트리밍.
shared/PROTOCOL.md 참고.

우가우가게임(미니게임 6종)은 게임마다 관절 좌표를 다르게 해석해야 해서, 이 서버는 동작을
분류하지 않는다. 매 프레임 Pose Landmarker가 잡은 사람의 상체+하체 관절 13개와,
Face Landmarker로 계산한 입 벌림/눈 감김 정도(비율값) 두 개만 얹어서 그대로 Unity에
스트리밍한다 - 분류는 전부 Unity(pc-game/Assets/Scripts/Common/) 쪽 책임.

두 가지 실행 모드가 있다:
- 온라인 모드(권장, --player-id 지정): 플레이어 1명당 카메라 1대 - 노트북 두 대를 같은 LAN에
  놓고 각자 자기 카메라로 자기 자신만 잡아서 보낸다. 카메라 겹침 인식 문제(사람 2명이 붙어
  있으면 감지가 하나로 억제되는 NMS 문제)가 애초에 생기지 않아 가장 안정적 - 실측 결과 이
  방식으로 전환하기로 결정함. Unity는 한 PC에서만 실행되고 둘 다 그 화면을 같이 본다.
- 구모드(--player-id 생략): 카메라 1대 앞에 두 명이 같이 서서 좌/우로 구분. 별도 장비 없이
  빠르게 테스트할 땐 여전히 쓸 수 있지만, 사람이 붙어 있으면 한쪽이 인식 안 되는 문제가
  실측으로 확인돼 데모용 기본 모드로는 온라인 모드를 쓴다.

주의: 이 머신의 mediapipe 빌드는 레거시 mp.solutions.pose/face_mesh API가 빠져있고
Tasks API(mediapipe.tasks.python.vision.PoseLandmarker/FaceLandmarker)만 들어있다 -
구 버전(main.py 이전 버전) 코드가 이미 이 문제를 Tasks API로 우회한 것을 그대로 따른다.

Face Landmarker 모델(face_landmarker.task)이 models/ 밑에 없으면 얼굴 인식(입 벌림/눈
감김)만 건너뛰고 포즈 스트리밍은 계속한다 - 모델 파일 하나 없다고 서버 전체가 죽을 필요는
없음. 받는 법은 vision-server/README.md 참고.

실행(온라인 모드, 노트북 A/B에서 각각):
  python main.py --pc-ip <Unity가 돌아가는 PC의 LAN IP> --player-id p1
  python main.py --pc-ip <Unity가 돌아가는 PC의 LAN IP> --player-id p2
실행(구모드, 카메라 1대에 두 명):
  python main.py --pc-ip 127.0.0.1

소리지르기(ScreamDuel)에서만 필요한 마이크 음량(voice.level)은 기본적으로 캡처하지 않는다 -
--voice 플래그를 추가하면 이 PC의 시스템 기본 마이크로 RMS 음량을 재서 매 프레임에 얹어
보낸다(shared/PROTOCOL.md "계획 중: 마이크 음량(voice) 필드" 참고). sounddevice/numpy가
설치돼 있어야 한다: python -m pip install sounddevice numpy
  python main.py --pc-ip 127.0.0.1 --player-id p1 --voice
"""

import argparse
import json
import math
import pathlib
import socket
import sys
import time

import cv2
import mediapipe as mp
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

try:
    import numpy as np
    import sounddevice as sd
except ImportError:
    np = None
    sd = None

def _resource_dir() -> pathlib.Path:
    # PyInstaller로 얼려서 배포_아키텍처_설계.md 방식대로 번들링하면 __file__ 기준 상대
    # 경로가 더 이상 소스 트리를 가리키지 않는다 - sys._MEIPASS(onedir 빌드에서는 실행
    # 파일 옆 _internal 폴더)가 번들된 데이터 파일의 실제 위치.
    if getattr(sys, "frozen", False):
        return pathlib.Path(sys._MEIPASS)
    return pathlib.Path(__file__).parent


POSE_MODEL_PATH = _resource_dir() / "models" / "pose_landmarker_lite.task"
FACE_MODEL_PATH = _resource_dir() / "models" / "face_landmarker.task"

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


# 아래 점프 높이 계산은 pc-game/Assets/Scripts/Common/JumpHeightCalibrator.cs +
# PoseInputHub.cs의 TorsoLength/HipMid 정의를 그대로 옮긴 것 - 과일따기(FruitJump)의
# tierHeightThresholds 값을 실측으로 정하기 위해, 카메라 화면에 실시간 점프 높이 비율을
# 바로 찍어서 Unity를 켜지 않고도 확인할 수 있게 하는 디버그 전용 기능이다. build_players_payload가
# 이미 만들어 둔 player["pose"]({"leftHip": {"x","y"}, ...} 형태) 값을 그대로 받아서 쓴다.
def _hip_mid_from_payload(pose):
    return (
        (pose["leftHip"]["x"] + pose["rightHip"]["x"]) / 2.0,
        (pose["leftHip"]["y"] + pose["rightHip"]["y"]) / 2.0,
    )


def _torso_length_from_payload(pose):
    hx, hy = _hip_mid_from_payload(pose)
    sx = (pose["leftShoulder"]["x"] + pose["rightShoulder"]["x"]) / 2.0
    sy = (pose["leftShoulder"]["y"] + pose["rightShoulder"]["y"]) / 2.0
    return math.hypot(sx - hx, sy - hy)


class JumpHeightTracker:
    """플레이어 한 명의 점프 높이 상태(기준선 캘리브레이션 + 세션 최고 높이). Unity의
    JumpHeightCalibrator와 동일하게, 처음 calibration_seconds 동안 엉덩이 중점 y좌표를
    지수이동평균으로 모아 기준선을 잡고, 이후 (기준선 - 현재y)/몸통길이 비율을 점프 높이로
    본다(이미지 y는 아래로 증가하므로 값이 작아질수록 위로 뜬 것)."""

    def __init__(self, calibration_seconds: float = 1.5):
        self.calibration_seconds = calibration_seconds
        self._baseline_y = None
        self._calib_start = None
        self.is_calibrated = False
        self.current_height = 0.0
        self.max_height_seen = 0.0

    def update(self, pose, now: float):
        hip_x, hip_y = _hip_mid_from_payload(pose)
        torso = _torso_length_from_payload(pose)

        if not self.is_calibrated:
            if self._calib_start is None:
                self._calib_start = now
                self._baseline_y = hip_y
            else:
                self._baseline_y = self._baseline_y * 0.85 + hip_y * 0.15
            if now - self._calib_start >= self.calibration_seconds:
                self.is_calibrated = True
            self.current_height = 0.0
            return

        if torso <= 0:
            self.current_height = 0.0
            return
        self.current_height = max(0.0, (self._baseline_y - hip_y) / torso)
        self.max_height_seen = max(self.max_height_seen, self.current_height)


class VoiceLevelMeter:
    """마이크 RMS 음량을 실시간으로 0(무음)~1(설계상 최대 음량)로 정규화해서 들고 있는
    백그라운드 캡처기. 소리지르기(ScreamDuel) 전용 - shared/PROTOCOL.md "계획 중: 마이크
    음량(voice) 필드"에 정의한 그대로: RMS를 dB로 바꾼 뒤 [min_db, max_db] 구간을 0~1로
    클램프한다. sounddevice의 콜백은 별도 오디오 스레드에서 돌기 때문에 카메라/포즈 추론
    루프(메인 스레드)를 전혀 막지 않는다 - 마지막으로 계산된 값을 읽기만 하면 된다."""

    def __init__(self, min_db: float = -35.0, max_db: float = 0.0,
                 samplerate=None, blocksize: int = 0, device=None, channels=None):
        if sd is None or np is None:
            raise RuntimeError(
                "--voice 옵션을 쓰려면 sounddevice/numpy가 필요합니다.\n"
                "  -> python -m pip install sounddevice numpy"
            )
        if max_db <= min_db:
            raise ValueError("--voice-max-db는 --voice-min-db보다 커야 합니다.")

        # 16 kHz 고정값은 일부 Windows MME/노트북 마이크 드라이버에서 장치 열기 자체가
        # 실패한다. 선택된 입력 장치가 광고하는 기본 샘플레이트를 사용하면 같은 마이크를
        # DirectSound/WASAPI로 바꿔도 안전하게 시작할 수 있다.
        self.device = sd.default.device[0] if device is None else device
        device_info = sd.query_devices(self.device, "input")
        self.device_name = device_info["name"]
        self.samplerate = float(samplerate or device_info["default_samplerate"])
        # 노트북의 Microphone Array는 2~4채널 중 실제 음성이 특정 채널에만 들어오는 경우가
        # 있다. 1채널만 요청하면 무음 채널을 집을 수 있으므로 기본값은 장치의 입력 채널을
        # 전부 열고 콜백에서 전체 RMS를 계산한다.
        max_channels = int(device_info["max_input_channels"])
        self.channels = int(channels or max_channels)
        if self.channels < 1 or self.channels > max_channels:
            raise ValueError(
                f"마이크 채널 수는 1~{max_channels} 사이여야 합니다: {self.channels}"
            )
        self.min_db = min_db
        self.max_db = max_db
        self._level = 0.0
        self._reported_status = False
        self._stream = sd.InputStream(
            samplerate=self.samplerate, blocksize=blocksize, channels=self.channels,
            dtype="float32", device=self.device, callback=self._on_audio,
        )

    def _on_audio(self, indata, frames, time_info, status):
        if status and not self._reported_status:
            print(f"[voice] 오디오 입력 경고: {status}")
            self._reported_status = True
        rms = float(np.sqrt(np.mean(np.square(indata))))
        db = 20.0 * math.log10(rms) if rms > 1e-9 else self.min_db
        normalized = (db - self.min_db) / (self.max_db - self.min_db)
        self._level = max(0.0, min(1.0, normalized))

    @property
    def level(self) -> float:
        return self._level

    def start(self):
        self._stream.start()

    def stop(self):
        self._stream.stop()
        self._stream.close()


def _face_center_x(face_landmarks):
    return face_landmarks[_FACE_TOP].x


def _assign_player_ids(poses, prev_hip_x):
    """단순 x좌표 정렬 대신, 직전 프레임에 각 id(p1/p2)가 있던 hip x좌표에 더 가까운 쪽으로
    매칭한다 - 두 사람이 순간적으로 교차하거나 겹쳐서 hip x좌표 대소관계가 잠깐 역전돼도
    라벨이 프레임 단위로 뒤바뀌지 않게 하기 위함(실측 결과 실제로 발생하는 문제였음,
    PROTOCOL.md "결정 필요" 항목). prev_hip_x는 {"p1": float|None, "p2": float|None}이고
    이 함수가 in-place로 갱신한다. 최초 프레임이나 이력이 없으면 기존처럼 왼쪽=p1로 정렬."""
    hip_xs = [_hip_center_x(p) for p in poses]

    if len(poses) == 0:
        return []

    if len(poses) == 1:
        have_p1, have_p2 = prev_hip_x["p1"] is not None, prev_hip_x["p2"] is not None
        if have_p1 and have_p2:
            pid = "p1" if abs(hip_xs[0] - prev_hip_x["p1"]) <= abs(hip_xs[0] - prev_hip_x["p2"]) else "p2"
        elif have_p2 and not have_p1:
            pid = "p2"
        else:
            pid = "p1"
        prev_hip_x[pid] = hip_xs[0]
        return [(pid, poses[0])]

    # len(poses) == 2 (num_poses=2로 제한돼 있어 그 이상은 안 옴)
    if prev_hip_x["p1"] is None or prev_hip_x["p2"] is None:
        order = sorted(range(2), key=lambda i: hip_xs[i])
        assignment = [("p1", order[0]), ("p2", order[1])]
    else:
        cost_direct = abs(hip_xs[0] - prev_hip_x["p1"]) + abs(hip_xs[1] - prev_hip_x["p2"])
        cost_swap = abs(hip_xs[0] - prev_hip_x["p2"]) + abs(hip_xs[1] - prev_hip_x["p1"])
        assignment = [("p1", 0), ("p2", 1)] if cost_direct <= cost_swap else [("p2", 0), ("p1", 1)]

    result = [(pid, poses[idx]) for pid, idx in assignment]
    for pid, pose_lm in result:
        prev_hip_x[pid] = _hip_center_x(pose_lm)
    return result


def _match_faces_to_players(matched_poses, faces):
    """faces를 x좌표로 단순 정렬해서 포즈와 짝짓지 않고, 각 플레이어 포즈의 코 x좌표와 가장
    가까운 얼굴을 골라 짝짓는다(포즈 id 배정이 이력 기반으로 바뀌었으니 얼굴 매칭도 같은
    원칙을 따라야 라벨 안정성이 유지됨). 얼굴 하나는 최대 한 플레이어에게만 배정."""
    result = {}
    remaining = list(range(len(faces)))
    for pid, pose_lm in matched_poses:
        if not remaining:
            break
        ref_x = pose_lm[_NOSE].x
        best_idx = min(remaining, key=lambda i: abs(_face_center_x(faces[i]) - ref_x))
        result[pid] = faces[best_idx]
        remaining.remove(best_idx)
    return result


def build_players_payload(pose_result, face_result, prev_hip_x, fixed_player_id=None):
    """이번 프레임의 포즈/얼굴 감지 결과를 최대 2명분의 PROTOCOL.md 포맷 리스트로 조립한다.

    fixed_player_id가 None이면 기존처럼 한 카메라에 두 명이 잡히는 걸 전제로
    _assign_player_ids(이력 기반 최근접 매칭)를 쓴다. fixed_player_id가 주어지면(카메라
    1대=플레이어 1명 모드 - 온라인/각자 컴퓨터 구성) 좌우 정렬이 의미가 없으므로 감지된
    첫 사람을 그 id로 그대로 보낸다."""
    poses = list(pose_result.pose_landmarks) if pose_result.pose_landmarks else []
    faces = list(face_result.face_landmarks) if face_result and face_result.face_landmarks else []

    if fixed_player_id is not None:
        matched_poses = [(fixed_player_id, poses[0])] if poses else []
    else:
        matched_poses = _assign_player_ids(poses[:2], prev_hip_x)
    matched_faces = _match_faces_to_players(matched_poses, faces)

    players = []
    for pid, pose_lm in matched_poses:
        player = {
            "id": pid,
            # {"x":..,"y":..} 객체 형태 - Unity JsonUtility가 [x,y] 배열은 커스텀 타입
            # 필드로 못 받아서(배열<->객체 불일치) 애초에 객체로 보낸다.
            "pose": {key: {"x": pose_lm[idx].x, "y": pose_lm[idx].y} for key, idx in POSE_KEYS.items()},
            "face": {"mouthOpenRatio": 0.0, "eyeAspectRatio": 0.3},
        }
        face_lm = matched_faces.get(pid)
        if face_lm is not None:
            player["face"] = {
                "mouthOpenRatio": _mouth_open_ratio(face_lm),
                "eyeAspectRatio": (
                    _eye_aspect_ratio(face_lm, _FACE_LEFT_EYE)
                    + _eye_aspect_ratio(face_lm, _FACE_RIGHT_EYE)
                ) / 2.0,
            }
        players.append(player)
    # id 순서(p1 먼저)로 전송 - 필수는 아니지만 로그/디버깅 시 보기 편하게.
    players.sort(key=lambda p: p["id"])
    return players


def load_pose_landmarker(num_poses: int = 2) -> vision.PoseLandmarker:
    if not POSE_MODEL_PATH.exists():
        raise FileNotFoundError(
            f"모델 파일이 없습니다: {POSE_MODEL_PATH}\n"
            "python -m pip install mediapipe 후 pose_landmarker_lite.task를 받아주세요."
        )
    # model_asset_path 대신 파일을 직접 바이트로 읽어 model_asset_buffer로 넘긴다 - 한글이
    # 섞인 Windows 경로에서 네이티브 레이어가 FileNotFoundError를 내는 문제 우회 (구 버전에서
    # 실측으로 확인된 우회법 그대로 유지).
    # 기본 신뢰도 임계값(0.5)에서는 두 사람이 가까이 붙어 있을 때 사람 감지 단계의 NMS가
    # 겹친 바운딩박스를 하나로 억제해버려서 한쪽이 아예 안 잡히는 경우가 실측으로 확인됨
    # (한 카메라에 두 명을 같이 잡는 구모드에서 특히 자주 발생) - 임계값을 낮춰서 더
    # 적극적으로 잡도록 함. --player-id 모드(카메라 1대=1명)에서는 애초에 겹칠 사람이 없어
    # 덜 중요하지만 낮은 임계값을 유지해도 무해함.
    options = vision.PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_buffer=POSE_MODEL_PATH.read_bytes()),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=num_poses,
        min_pose_detection_confidence=0.3,
        min_pose_presence_confidence=0.3,
        min_tracking_confidence=0.3,
    )
    return vision.PoseLandmarker.create_from_options(options)


def load_face_landmarker(num_faces: int = 2):
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
        num_faces=num_faces,
    )
    return vision.FaceLandmarker.create_from_options(options)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pc-ip", default="127.0.0.1", help="Unity가 돌아가는 PC의 IP (같은 PC면 127.0.0.1)")
    parser.add_argument("--port", type=int, default=9100)
    parser.add_argument("--camera-index", type=int, default=0)
    parser.add_argument("--show", action="store_true", default=True, help="디버그용 카메라 창 표시")
    parser.add_argument(
        "--no-show", action="store_true",
        help="디버그용 카메라 창을 띄우지 않는다 - Unity가 게임 실행파일에 번들링해서 자동으로"
             " 이 프로세스를 백그라운드로 켤 때 쓴다(배포_아키텍처_설계.md). 이 경우 cv2 창의"
             " 'q' 키 종료도 같이 꺼지므로, 부모 프로세스(Unity)가 직접 이 프로세스를 종료시켜야"
             " 한다 - 수동 터미널 실행 중에는 계속 --show 기본값(창 표시)을 쓰면 된다.",
    )
    parser.add_argument(
        "--no-preview", action="store_true",
        help="Unity 로딩 화면용 저해상도 카메라 미리보기 전송을 끕니다.",
    )
    parser.add_argument("--preview-port", type=int, default=9101)
    parser.add_argument("--preview-fps", type=float, default=5.0)
    parser.add_argument("--preview-width", type=int, default=320)
    parser.add_argument("--preview-quality", type=int, default=50)
    parser.add_argument(
        "--player-id", choices=["p1", "p2"], default=None,
        help="지정하면 이 카메라는 '1명만 인식' 모드로 동작 - 감지된 첫 사람을 항상 이 id로"
             " 보낸다. 카메라 2대(플레이어 1명당 1대)로 나눠서 인식할 때 씀: 노트북 A는"
             " --player-id p1, 노트북 B는 --player-id p2로 각각 실행하고 둘 다 같은 --pc-ip를"
             " 바라보게 하면 됨(Unity 쪽 PoseInputHub는 id별로 값을 받으므로 별도 수정 불필요)."
             " 생략하면 기존처럼 카메라 1대에 두 명이 같이 잡히는 모드로 동작."
    )
    parser.add_argument(
        "--voice", action="store_true",
        help="마이크 음량(voice.level)도 같이 캡처해서 보낸다 - 소리지르기(ScreamDuel) 전용,"
             " 다른 미니게임에는 필요 없다(기본 꺼짐). 이 PC의 시스템 기본 마이크를 그대로 쓴다."
    )
    parser.add_argument(
        "--voice-device", type=int, default=None,
        help="사용할 sounddevice 입력 장치 번호. 생략하면 시스템 기본 마이크를 사용한다.",
    )
    parser.add_argument(
        "--voice-channels", type=int, default=None,
        help="읽을 마이크 채널 수. 생략하면 마이크 배열의 모든 입력 채널을 읽는다.",
    )
    parser.add_argument(
        "--list-audio-devices", action="store_true",
        help="사용 가능한 마이크 번호를 출력하고 종료한다.",
    )
    parser.add_argument(
        "--check-models", action="store_true",
        help="카메라/소켓 없이 포즈/얼굴 모델만 로드해보고 성공 여부를 출력한 뒤 종료한다 -"
             " PyInstaller 번들에 models/*.task가 제대로 포함됐는지 확인하는 용도.",
    )
    parser.add_argument("--voice-min-db", type=float, default=-35.0, help="이 dB 이하는 voice.level=0으로 클램프")
    parser.add_argument("--voice-max-db", type=float, default=0.0, help="이 dB 이상은 voice.level=1(100%%)로 클램프")
    args = parser.parse_args()
    show_window = args.show and not args.no_show

    if args.list_audio_devices:
        if sd is None:
            raise RuntimeError("sounddevice가 설치되어 있지 않습니다.")
        print(sd.query_devices())
        return

    if args.check_models:
        print(f"[check-models] resource_dir = {_resource_dir()}")
        load_pose_landmarker()
        print(f"[check-models] pose OK: {POSE_MODEL_PATH}")
        face = load_face_landmarker()
        print(f"[check-models] face {'OK' if face else 'MISSING (경고만, 계속 동작 가능)'}: {FACE_MODEL_PATH}")
        return

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (args.pc_ip, args.port)
    preview_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    preview_dest = (args.pc_ip, args.preview_port)

    voice_meter = None
    if args.voice:
        voice_meter = VoiceLevelMeter(
            min_db=args.voice_min_db,
            max_db=args.voice_max_db,
            device=args.voice_device,
            channels=args.voice_channels,
        )
        voice_meter.start()
        print(
            f"[voice] 마이크 캡처 시작: #{voice_meter.device} {voice_meter.device_name} "
            f"({voice_meter.samplerate:.0f}Hz, {voice_meter.channels}ch, "
            f"min={args.voice_min_db}dB, "
            f"max={args.voice_max_db}dB)"
        )
    else:
        print("[voice] 마이크 전송 꺼짐 - ScreamDuel을 하려면 --voice를 추가하세요.")

    num_people = 1 if args.player_id else 2
    pose_landmarker = load_pose_landmarker(num_people)
    face_landmarker = load_face_landmarker(num_people)

    cap = cv2.VideoCapture(args.camera_index)
    if not cap.isOpened():
        raise RuntimeError(f"카메라 {args.camera_index}번을 열 수 없습니다.")
    # 테스트 시 화면이 너무 작아 잘 안 보인다는 피드백 - 캡처 해상도 자체를 높여서 요청.
    # 카메라가 이 해상도를 지원 안 하면 드라이버가 가장 가까운 값으로 알아서 맞춘다.
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)

    if show_window:
        cv2.namedWindow("vision-server", cv2.WINDOW_NORMAL)
        cv2.resizeWindow("vision-server", 1280, 720)

    start_time = time.time()
    last_preview_sent = 0.0
    voice_display_level = 0.0
    voice_peak_level = 0.0
    prev_hip_x = {"p1": None, "p2": None}  # 라벨 안정화용 이력 - _assign_player_ids 참고.
    # 과일따기 tierHeightThresholds 실측용 - 플레이어별 점프 높이 캘리브레이션/세션 최고치.
    jump_trackers = {"p1": JumpHeightTracker(), "p2": JumpHeightTracker()}
    print(f"[udp] {dest[0]}:{dest[1]} 로 연속 포즈 스트림 전송 (매 프레임)")
    if args.player_id:
        print(f"1인 모드: 이 카메라에 감지된 사람은 전부 {args.player_id}로 전송됩니다. 종료는 'q'.")
    else:
        print("화면 왼쪽에 선 사람 = p1, 오른쪽 = p2. 종료는 'q'.")
    print("점프 높이 캘리브레이션: 가만히 서서 1.5초 기다리세요. 'r'=세션 최고 높이 초기화.")

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break

            frame = cv2.flip(frame, 1)  # 거울 모드 (직관적인 좌우)
            now = time.time()
            preview_interval = 1.0 / max(1.0, args.preview_fps)
            if not args.no_preview and now - last_preview_sent >= preview_interval:
                source_h, source_w = frame.shape[:2]
                preview_w = max(160, min(640, args.preview_width))
                preview_h = max(90, int(preview_w * source_h / max(1, source_w)))
                preview_frame = cv2.resize(
                    frame, (preview_w, preview_h), interpolation=cv2.INTER_AREA
                )
                quality = max(20, min(85, args.preview_quality))
                encoded, jpeg = cv2.imencode(
                    ".jpg",
                    preview_frame,
                    [int(cv2.IMWRITE_JPEG_QUALITY), quality],
                )
                if encoded:
                    preview_player = args.player_id or "all"
                    header = f"UGAPREV1|{preview_player}|".encode("ascii")
                    packet = header + jpeg.tobytes()
                    if len(packet) <= 60_000:
                        preview_sock.sendto(packet, preview_dest)
                        last_preview_sent = now
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            now_ms = int((time.time() - start_time) * 1000)

            pose_result = pose_landmarker.detect_for_video(mp_image, now_ms)
            face_result = face_landmarker.detect_for_video(mp_image, now_ms) if face_landmarker else None

            players = build_players_payload(pose_result, face_result, prev_hip_x, fixed_player_id=args.player_id)
            if voice_meter is not None:
                raw_voice_level = voice_meter.level
                # 독립 test_scream_mic.py와 같은 빠른 상승/느린 하강 표시. UDP에는 판정 지연이
                # 생기지 않도록 raw 값을 보내고, 카메라 창의 게이지만 눈에 잘 보이게 완화한다.
                smoothing = 0.55 if raw_voice_level >= voice_display_level else 0.12
                voice_display_level += (raw_voice_level - voice_display_level) * smoothing
                voice_peak_level = max(voice_peak_level, raw_voice_level)
                # 마이크 하나로 이 프로세스가 담당하는 플레이어(들) 전체에 같은 순간 음량을
                # 얹는다 - 소리지르기는 턴제라 한 마이크를 공유해도 문제없다(PROTOCOL.md 참고).
                for player in players:
                    player["voice"] = {"level": raw_voice_level}
            sock.sendto(
                json.dumps({"t": time.time(), "players": players}).encode("utf-8"),
                dest,
            )

            now = time.time()
            for player in players:
                jump_trackers[player["id"]].update(player["pose"], now)

            if show_window:
                h, w = frame.shape[:2]
                for player in players:
                    color = (0, 120, 255) if player["id"] == "p1" else (255, 120, 0)
                    for point in player["pose"].values():
                        cv2.circle(frame, (int(point["x"] * w), int(point["y"] * h)), 4, color, -1)
                    tracker = jump_trackers[player["id"]]
                    if tracker.is_calibrated:
                        jump_label = f"height={tracker.current_height:.2f} max={tracker.max_height_seen:.2f}"
                    else:
                        jump_label = "캘리브레이션 중..."
                    voice_label = f"  voice={voice_meter.level:.2f}" if voice_meter is not None else ""
                    label = (
                        f"{player['id']}  mouth={player['face']['mouthOpenRatio']:.2f}"
                        f"  EAR={player['face']['eyeAspectRatio']:.2f}  {jump_label}{voice_label}"
                    )
                    anchor_x = int(player["pose"]["nose"]["x"] * w)
                    anchor_y = max(20, int(player["pose"]["nose"]["y"] * h) - 20)
                    cv2.putText(frame, label, (anchor_x, anchor_y),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)
                if not players:
                    cv2.putText(frame, "NO POSE", (20, 40),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.75, (0, 0, 255), 2)
                if voice_meter is not None:
                    meter_left = 20
                    meter_right = w - 20
                    meter_top = h - 76
                    meter_bottom = h - 42
                    percent = int(round(voice_display_level * 100))
                    peak_percent = int(round(voice_peak_level * 100))
                    if percent < 45:
                        meter_color = (255, 150, 49)
                    elif percent < 75:
                        meter_color = (56, 186, 255)
                    else:
                        meter_color = (54, 75, 255)
                    cv2.rectangle(
                        frame,
                        (meter_left, meter_top),
                        (meter_right, meter_bottom),
                        (65, 55, 45),
                        -1,
                    )
                    fill_right = meter_left + int(
                        (meter_right - meter_left) * voice_display_level
                    )
                    cv2.rectangle(
                        frame,
                        (meter_left, meter_top),
                        (fill_right, meter_bottom),
                        meter_color,
                        -1,
                    )
                    cv2.rectangle(
                        frame,
                        (meter_left, meter_top),
                        (meter_right, meter_bottom),
                        (190, 165, 120),
                        2,
                    )
                    cv2.putText(
                        frame,
                        f"MIC {percent:3d} / PEAK {peak_percent:3d}",
                        (meter_left + 10, meter_top + 25),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.65,
                        (255, 255, 255),
                        2,
                    )
                cv2.putText(frame, "[q]=quit  [r]=reset peak", (20, frame.shape[0] - 16),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
                cv2.imshow("vision-server", frame)
                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    break
                if key == ord("r"):
                    for tracker in jump_trackers.values():
                        tracker.max_height_seen = 0.0
                    voice_peak_level = 0.0
    finally:
        cap.release()
        cv2.destroyAllWindows()
        sock.close()
        preview_sock.close()
        pose_landmarker.close()
        if face_landmarker:
            face_landmarker.close()
        if voice_meter is not None:
            voice_meter.stop()


if __name__ == "__main__":
    main()
