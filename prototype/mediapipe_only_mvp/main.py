"""
1차 MVP: 폰 없이 웹캠(MediaPipe Pose)만으로 기획서 7개 동작을 전부 흉내내서 인식.

목적: 검/방패를 폰 IMU로 인식하는 최종 아키텍처(vision-server/ + phone-sensor/ + pc-game/)를
만들기 전에, "게임 루프 전체가 말이 되는지"를 폰 없이 빠르게 검증해보는 스파이크다.
공식 vision-server/main.py(회피 2종만 담당)는 건드리지 않고 별도로 둔다.

동작 목록 (기획서 2장, 7종) -> 웹캠 단독 근사 인식 방법:

  [검 (오른손으로 근사)]
  - 가로 베기 / 세로 베기: 손목이 몸통 기준 상대적으로 좌우/상하로 크게, 빠르게 움직임
  - 찌르기: 팔꿈치 각도(어깨-팔꿈치-손목)가 짧은 시간에 급격히 펴지고, 옆으로는 별로
    안 움직임 (스윙과 구분) — 실제로는 스윙 동작 중간에도 팔이 펴지는 순간이 있어서
    자꾸 같이 잡혔다. 스윙 발동 시 찌르기도 잠깐 쉬게(상호 쿨다운) 하는 것까지는 했는데
    여전히 헷갈려서 `update()`에서 호출 자체를 꺼뒀다 (`_thrust` 메서드는 남겨둠 — 재활성화는
    update() 안의 주석 처리된 한 줄만 살리면 됨).

  [방패 (왼손으로 근사)]
  - 기본 방어: 손목이 가슴 높이 근처에서 거의 안 움직이고 일정 시간 이상 유지됨
  - 패링: `_sword_swing`과 완전히 같은 방식(몸통 기준 상대좌표, 고정 임계값)을 왼팔에
    적용. 방향 구분 없이 "팔이 몸통 기준으로 크게 뻗어나가면" 패링으로 인정 —
    캘리브레이션도, 직전 방어 상태 요구도 없음 (처음엔 "방어 상태였다가 바깥으로"로
    설계했었는데 실측 캘리브레이션까지 붙여도 잘 안 잡혀서, 이미 잘 동작하던 스윙
    로직을 그대로 재사용하는 쪽으로 단순화했다).

  [회피 (전신 Pose)]
  - 앉기 / 좌우로 움직이기: 몸통 중심의 y/x가 기준 자세 대비 일정 비율 이상 벗어남

찌르기 히스토리: 손목 궤적만으로(v1), 발+손 lunge로(v2), 팔꿈치 각도+실측 캘리브레이션으로(v3)
시도했는데 스윙 동작과 자꾸 섞여 잡혀서 결국 비활성화했다 (`_thrust` 메서드/캘리브레이션
코드는 남겨둠 — 재활성화는 `update()` 안의 주석 처리된 한 줄만 살리면 됨).

주의 (이 머신 한정):
  - mp.solutions.pose가 이 mediapipe 빌드(0.10.35, Py3.14 win_amd64)엔 없어서 Tasks API 사용.
  - 모델 파일은 vision-server/models/ 에 있는 걸 그대로 재사용한다 (5.6MB 바이너리를
    레포에 중복으로 넣지 않으려고).
  - cv2.flip(frame, 1)로 거울 모드를 쓰는데, 실측 결과 MediaPipe가 리턴하는 "왼쪽/오른쪽"
    라벨이 실제 사용자의 좌/우와 반대로 나왔다. 아래 PHYS_* 상수에서 미리 뒤집어서
    보정해뒀다 — 만약 실측이 또 바뀌면(카메라/버전 차이 등) 이 블록만 다시 뒤집으면 된다.

's' 키: 서 있는 기준 자세 캘리브레이션 (앉기/좌우이동 판정 기준선). 'q' 키: 종료.
"""

import collections
import math
import pathlib
import time

import cv2
import mediapipe as mp
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

MODEL_PATH = (
    pathlib.Path(__file__).parent.parent.parent / "vision-server" / "models" / "pose_landmarker_lite.task"
)

# PoseLandmark 인덱스 (BlazePose 33 keypoints, MediaPipe가 실제로 리턴하는 raw 라벨)
_L_SHOULDER, _R_SHOULDER = 11, 12
_L_ELBOW, _R_ELBOW = 13, 14
_L_WRIST, _R_WRIST = 15, 16
_L_HIP, _R_HIP = 23, 24

# 실측 결과 미러(flip) 때문에 raw 라벨이 실제 좌우와 반대였다. PHYS_*가 "사용자 실제 기준"
# 좌/우를 가리키도록 여기서 한 번에 뒤집어 보정 — 아래부터는 전부 PHYS_*만 쓴다.
PHYS_R_SHOULDER, PHYS_L_SHOULDER = _L_SHOULDER, _R_SHOULDER
PHYS_R_ELBOW, PHYS_L_ELBOW = _L_ELBOW, _R_ELBOW
PHYS_R_WRIST, PHYS_L_WRIST = _L_WRIST, _R_WRIST
PHYS_R_HIP, PHYS_L_HIP = _L_HIP, _R_HIP

SWORD_WRIST = PHYS_R_WRIST    # 검 = 오른손 (기획서: "오른손에 스마트폰(검)")
SHIELD_WRIST = PHYS_L_WRIST   # 방패 = 왼손

SMOOTH_WINDOW = 5

# --- 회피 ---
SQUAT_DROP_RATIO = 0.35       # torso 길이 대비 이만큼 더 내려가면 앉기
LATERAL_MOVE_RATIO = 1.1      # 어깨너비 대비 이만큼 벗어나면 좌우 이동 (실측 후 조정, 원래 0.6은 과민했음)

# --- 검: 스윙 (짧은 시간창 안에서의, 몸통 기준 상대적인 손목 이동을 분석) ---
SWING_WINDOW_SEC = 0.25       # 이 시간 안의 이동을 하나의 "스윙 후보"로 봄
SWING_DIST_RATIO = 0.55       # torso 길이 대비 이만큼 이상 움직여야 스윙 후보
SWING_AXIS_RATIO = 1.3        # 우세 축이 다른 축보다 이 배 이상 커야 가로/세로 판정
SWORD_COOLDOWN_SEC = 0.45     # 한 번 판정되면 이 시간 동안 재판정 안 함 (같은 스윙 중복 방지)

# --- 방패: 기본 방어 ---
GUARD_Y_BAND_RATIO = 0.5      # 어깨 높이 기준 ±이 비율(torso 길이) 안이면 "가슴 높이"
GUARD_STATIONARY_SPEED = 1.2  # torso길이/초. 이보다 느리면 "정지"로 간주
GUARD_HOLD_SEC = 0.20         # 이 시간 이상 연속으로 정지+가슴높이여야 기본 방어로 인정

# --- 찌르기: 팔꿈치 각도가 급격히 펴짐(오른팔), 옆 이동은 적어야 함 ---
THRUST_WINDOW_SEC = 0.30
THRUST_LATERAL_CAP = 0.35     # torso 길이 비. 이 이하로 옆으로 덜 움직여야 스윙과 구분됨
THRUST_COOLDOWN_SEC = 0.5

# --- 패링: 스윙(_sword_swing)과 완전히 같은 방식, 왼팔에 적용. 캘리브레이션 없이 고정 임계값. ---
PARRY_WINDOW_SEC = 0.25
PARRY_DIST_RATIO = 0.5        # torso 길이 대비 이만큼 이상 움직여야 패링 (실측 후 조정)
PARRY_COOLDOWN_SEC = 0.5

# --- 찌르기 전용: 실측 샘플 기반 캘리브레이션 (현재 _thrust 자체가 비활성화라 이 값들도 미사용) ---
CALIB_CAPTURE_SEC = 1.2       # 't' 누른 뒤 이 시간 동안의 최대값을 샘플 하나로 기록
CALIB_SAMPLES_NEEDED = 3
CALIB_MARGIN = 0.55           # 실측 평균의 이 비율을 최종 임계값으로 사용 (실측치보다 낮게 잡아 여유를 둠)

EVENT_DISPLAY_SEC = 0.4       # 순간 이벤트를 화면에 이만큼 켜둠


def mid(a, b):
    return ((a.x + b.x) / 2, (a.y + b.y) / 2)


def dist(p1, p2):
    return math.hypot(p1[0] - p2[0], p1[1] - p2[1])


def angle_deg(a, b, c):
    """b를 꼭짓점으로 하는 a-b-c 각도(도). 팔이 완전히 펴지면 180에 가까워진다."""
    v1 = (a.x - b.x, a.y - b.y)
    v2 = (c.x - b.x, c.y - b.y)
    m1, m2 = math.hypot(*v1), math.hypot(*v2)
    if m1 * m2 == 0:
        return 180.0
    cos_a = max(-1.0, min(1.0, (v1[0] * v2[0] + v1[1] * v2[1]) / (m1 * m2)))
    return math.degrees(math.acos(cos_a))


class MotionRecognizer:
    def __init__(self):
        self.baseline_torso_mid_y = None
        self.baseline_torso_len = None
        self.baseline_center_x = None
        self.torso_len_ema = None  # 캘리브레이션 전에도 분모(torso_len)가 프레임마다 안 흔들리게

        self.torso_mid_y_hist = collections.deque(maxlen=SMOOTH_WINDOW)
        self.torso_mid_x_hist = collections.deque(maxlen=SMOOTH_WINDOW)

        self.sword_hist = collections.deque()        # (t, rel_x, rel_y)
        self.shield_hist = collections.deque()        # (t, x, y)
        self.thrust_hist = collections.deque()        # (t, elbow_angle, rel_x, rel_y)
        self.parry_swing_hist = collections.deque()   # (t, rel_x, rel_y) - _sword_swing과 동일 방식

        self.guard_held_since = None
        self.last_sword_event_t = -999.0
        self.last_thrust_event_t = -999.0
        self.last_parry_event_t = -999.0

        # 찌르기 전용 캘리브레이션 상태 (현재 _thrust 비활성화라 미사용, 재활성화 대비 보존)
        self.thrust_samples = []
        self.thrust_threshold = None
        self.thrust_capture_until = None
        self.thrust_capture_peak = 0.0

        # 화면에 잠깐 표시할 순간 이벤트: {"name": 표시종료시각}
        self.active_events = {}

    def calibrate(self, landmarks):
        """서 있는 기준 자세 (앉기/좌우이동 기준선). 찌르기/패링 캘리브레이션과는 별개."""
        sh_mid = mid(landmarks[PHYS_L_SHOULDER], landmarks[PHYS_R_SHOULDER])
        hip_mid = mid(landmarks[PHYS_L_HIP], landmarks[PHYS_R_HIP])
        self.baseline_torso_mid_y = (sh_mid[1] + hip_mid[1]) / 2
        self.baseline_torso_len = dist(sh_mid, hip_mid)
        self.baseline_center_x = (sh_mid[0] + hip_mid[0]) / 2
        print(
            f"[calibrate] baseline_y={self.baseline_torso_mid_y:.3f} "
            f"torso_len={self.baseline_torso_len:.3f} center_x={self.baseline_center_x:.3f}"
        )

    def start_thrust_capture(self, t):
        if len(self.thrust_samples) >= CALIB_SAMPLES_NEEDED:
            self.thrust_samples = []
            self.thrust_threshold = None
            print("[calib] 찌르기 재보정 시작")
        self.thrust_capture_until = t + CALIB_CAPTURE_SEC
        self.thrust_capture_peak = 0.0
        print(f"[calib] 찌르기 캡처 {len(self.thrust_samples) + 1}/{CALIB_SAMPLES_NEEDED} - 지금 찌르세요!")

    def _fire(self, name, t):
        self.active_events[name] = t + EVENT_DISPLAY_SEC
        print(f"[event] {name}  t={t:.2f}")

    def _prune(self, hist, t, window_sec):
        while hist and t - hist[0][0] > window_sec:
            hist.popleft()

    def _torso(self, landmarks):
        sh_mid = mid(landmarks[PHYS_L_SHOULDER], landmarks[PHYS_R_SHOULDER])
        hip_mid = mid(landmarks[PHYS_L_HIP], landmarks[PHYS_R_HIP])

        raw_torso_len = dist(sh_mid, hip_mid)
        # 캘리브레이션 전에는 매 프레임 새로 재는 값이라 landmark 노이즈가 그대로 분모에
        # 실려서, 손목이 가만히 있어도 상대좌표가 흔들려 보이는 문제가 있었다. EMA로 완화.
        self.torso_len_ema = raw_torso_len if self.torso_len_ema is None else (
            0.2 * raw_torso_len + 0.8 * self.torso_len_ema
        )
        torso_len = self.baseline_torso_len or self.torso_len_ema
        return sh_mid, hip_mid, torso_len

    def _dodge(self, landmarks, t):
        """앉기 / 좌우 이동 (레벨 상태)."""
        sh_mid, hip_mid, torso_len = self._torso(landmarks)
        shoulder_width = dist(
            (landmarks[PHYS_L_SHOULDER].x, landmarks[PHYS_L_SHOULDER].y),
            (landmarks[PHYS_R_SHOULDER].x, landmarks[PHYS_R_SHOULDER].y),
        )

        torso_mid_y = (sh_mid[1] + hip_mid[1]) / 2
        torso_mid_x = (sh_mid[0] + hip_mid[0]) / 2
        self.torso_mid_y_hist.append(torso_mid_y)
        self.torso_mid_x_hist.append(torso_mid_x)
        smoothed_y = sum(self.torso_mid_y_hist) / len(self.torso_mid_y_hist)
        smoothed_x = sum(self.torso_mid_x_hist) / len(self.torso_mid_x_hist)

        squat = False
        lateral = None  # None | "LEFT" | "RIGHT"
        if self.baseline_torso_len:
            drop = smoothed_y - self.baseline_torso_mid_y
            squat = drop > SQUAT_DROP_RATIO * self.baseline_torso_len

            dx = smoothed_x - self.baseline_center_x
            if shoulder_width and abs(dx) > LATERAL_MOVE_RATIO * shoulder_width:
                # 이미지가 거울모드라 화면상 왼쪽(-x)이 사용자 입장에서도 왼쪽으로 보임
                lateral = "LEFT" if dx < 0 else "RIGHT"

        return squat, lateral

    def _sword_swing(self, landmarks, t):
        """가로베기 / 세로베기 (순간 이벤트). 손목을 몸통 중심 기준 상대좌표로 추적한다."""
        sh_mid, hip_mid, torso_len = self._torso(landmarks)
        if not torso_len:
            return
        center_x = (sh_mid[0] + hip_mid[0]) / 2
        center_y = (sh_mid[1] + hip_mid[1]) / 2

        wrist = landmarks[SWORD_WRIST]
        rel_x = (wrist.x - center_x) / torso_len
        rel_y = (wrist.y - center_y) / torso_len

        self.sword_hist.append((t, rel_x, rel_y))
        self._prune(self.sword_hist, t, SWING_WINDOW_SEC)

        if t - self.last_sword_event_t < SWORD_COOLDOWN_SEC or len(self.sword_hist) < 3:
            return

        t0, rel_x0, rel_y0 = self.sword_hist[0]
        dx = rel_x - rel_x0
        dy = rel_y - rel_y0
        total_dist = math.hypot(dx, dy)

        if total_dist < SWING_DIST_RATIO:
            return  # 별로 안 움직임

        self.last_sword_event_t = t
        if abs(dx) > abs(dy) * SWING_AXIS_RATIO:
            self._fire("가로 베기", t)
        elif abs(dy) > abs(dx) * SWING_AXIS_RATIO:
            self._fire("세로 베기", t)
        else:
            return  # 어느 쪽도 우세하지 않으면(대각선) 판정 안 함 -> 다시 스윙해달라고 유도

        # 스윙 중간에 팔이 펴지는 순간을 찌르기가 별개로 잡아버리는 걸 막기 위해
        # 스윙이 발동하면 잠깐 찌르기 판정도 같이 쉬게 한다 (반대 방향도 _thrust에서 동일하게 처리).
        self.last_thrust_event_t = t

    def _shield_guard(self, landmarks, t):
        """기본 방어 (레벨 상태)."""
        sh_mid, hip_mid, torso_len = self._torso(landmarks)
        if not torso_len:
            return False

        wrist = landmarks[SHIELD_WRIST]
        self.shield_hist.append((t, wrist.x, wrist.y))
        self._prune(self.shield_hist, t, GUARD_HOLD_SEC + 0.1)

        in_band = abs(wrist.y - sh_mid[1]) < torso_len * GUARD_Y_BAND_RATIO and wrist.y < hip_mid[1]

        recent = [s for s in self.shield_hist if t - s[0] <= GUARD_HOLD_SEC]
        stationary = True
        if len(recent) >= 2:
            (t0, x0, y0), (t1, x1, y1) = recent[0], recent[-1]
            dt = max(t1 - t0, 1e-3)
            speed = math.hypot(x1 - x0, y1 - y0) / torso_len / dt
            stationary = speed < GUARD_STATIONARY_SPEED

        guard_up = False
        if in_band and stationary:
            if self.guard_held_since is None:
                self.guard_held_since = t
            elif t - self.guard_held_since >= GUARD_HOLD_SEC:
                guard_up = True
        else:
            self.guard_held_since = None

        return guard_up

    def _thrust(self, landmarks, t):
        """찌르기: 팔꿈치 각도가 급격히 펴짐 + 옆 이동은 적음 (스윙과 구분)."""
        sh_mid, hip_mid, torso_len = self._torso(landmarks)
        if not torso_len:
            return
        center_x = (sh_mid[0] + hip_mid[0]) / 2
        center_y = (sh_mid[1] + hip_mid[1]) / 2

        wrist = landmarks[PHYS_R_WRIST]
        elbow = landmarks[PHYS_R_ELBOW]
        shoulder = landmarks[PHYS_R_SHOULDER]
        angle = angle_deg(shoulder, elbow, wrist)
        rel_x = (wrist.x - center_x) / torso_len
        rel_y = (wrist.y - center_y) / torso_len

        self.thrust_hist.append((t, angle, rel_x, rel_y))
        self._prune(self.thrust_hist, t, THRUST_WINDOW_SEC)
        if len(self.thrust_hist) < 3:
            return

        t0, angle0, rx0, ry0 = self.thrust_hist[0]
        d_angle = angle - angle0
        lateral = math.hypot(rel_x - rx0, rel_y - ry0)
        metric = d_angle if lateral < THRUST_LATERAL_CAP else 0.0

        if self.thrust_capture_until is not None:
            self.thrust_capture_peak = max(self.thrust_capture_peak, metric)
            if t >= self.thrust_capture_until:
                self.thrust_samples.append(self.thrust_capture_peak)
                print(f"[calib] 찌르기 샘플 {len(self.thrust_samples)}/{CALIB_SAMPLES_NEEDED}: peak={self.thrust_capture_peak:.1f}deg")
                self.thrust_capture_until = None
                if len(self.thrust_samples) >= CALIB_SAMPLES_NEEDED:
                    self.thrust_threshold = (sum(self.thrust_samples) / len(self.thrust_samples)) * CALIB_MARGIN
                    print(f"[calib] 찌르기 임계값 확정: {self.thrust_threshold:.1f}deg")
            return

        if self.thrust_threshold is None:
            return  # 't'로 캘리브레이션하기 전까지는 판정 안 함

        if metric > self.thrust_threshold and t - self.last_thrust_event_t > THRUST_COOLDOWN_SEC:
            self.last_thrust_event_t = t
            self.last_sword_event_t = t  # 이후 잠깐 스윙 판정도 같이 쉬게 함 (반대는 _sword_swing에서 처리)
            self._fire("찌르기", t)

    def _shield_parry(self, landmarks, t):
        """패링 (순간 이벤트). _sword_swing과 완전히 같은 방식(몸통 기준 상대좌표, 고정
        임계값, 캘리브레이션 없음)을 왼팔(SHIELD_WRIST)에 적용한 것 — 방향 구분 없이
        "팔이 몸통 기준으로 크게 뻗어나가는 동작"이면 패링으로 인정한다."""
        sh_mid, hip_mid, torso_len = self._torso(landmarks)
        if not torso_len:
            return
        center_x = (sh_mid[0] + hip_mid[0]) / 2
        center_y = (sh_mid[1] + hip_mid[1]) / 2

        wrist = landmarks[SHIELD_WRIST]
        rel_x = (wrist.x - center_x) / torso_len
        rel_y = (wrist.y - center_y) / torso_len

        self.parry_swing_hist.append((t, rel_x, rel_y))
        self._prune(self.parry_swing_hist, t, PARRY_WINDOW_SEC)

        if t - self.last_parry_event_t < PARRY_COOLDOWN_SEC or len(self.parry_swing_hist) < 3:
            return

        t0, rel_x0, rel_y0 = self.parry_swing_hist[0]
        total_dist = math.hypot(rel_x - rel_x0, rel_y - rel_y0)

        if total_dist < PARRY_DIST_RATIO:
            return  # 별로 안 움직임

        self.last_parry_event_t = t
        self._fire("패링", t)

    def update(self, landmarks, t):
        """한 프레임 처리. 화면에 그릴 상태 dict를 리턴."""
        squat, lateral = self._dodge(landmarks, t)
        self._sword_swing(landmarks, t)
        guard_up = self._shield_guard(landmarks, t)
        # 찌르기(_thrust)는 스윙과 자꾸 섞여 잡혀서 일단 비활성화. 재활성화하려면 아래 한 줄만 살리면 됨.
        # self._thrust(landmarks, t)
        self._shield_parry(landmarks, t)

        # 만료된 순간 이벤트 정리
        self.active_events = {name: until for name, until in self.active_events.items() if until > t}

        return {
            "squat": squat,
            "lateral": lateral,
            "guard_up": guard_up,
            "events": list(self.active_events.keys()),
        }


def load_landmarker() -> vision.PoseLandmarker:
    if not MODEL_PATH.exists():
        raise FileNotFoundError(
            f"모델 파일이 없습니다: {MODEL_PATH}\n"
            "vision-server/models/pose_landmarker_lite.task 가 있는지 확인하세요."
        )
    # 한글이 섞인 Windows 경로에서 model_asset_path가 깨지는 문제 우회 (vision-server와 동일)
    model_bytes = MODEL_PATH.read_bytes()
    options = vision.PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_buffer=model_bytes),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=1,
    )
    return vision.PoseLandmarker.create_from_options(options)


def _calib_label(name, samples, threshold, capture_until):
    if capture_until is not None:
        return f"{name}:캡처중"
    if threshold is not None:
        return f"{name}:완료({threshold:.1f})"
    return f"{name}:{len(samples)}/{CALIB_SAMPLES_NEEDED}"


def main():
    landmarker = load_landmarker()
    recognizer = MotionRecognizer()

    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        raise RuntimeError("웹캠을 열 수 없습니다 (다른 프로그램이 점유 중이거나 인덱스가 다를 수 있음)")

    start_time = time.time()
    print("화면 보고 똑바로 선 뒤 's' 키로 캘리브레이션 하세요. 종료는 'q'.")
    print("패링은 캘리브레이션 없이 스윙과 같은 방식으로 자동 판정됩니다. (찌르기는 일단 비활성화)")
    print("검=오른손(빨강)  방패=왼손(파랑)")

    while True:
        ok, frame = cap.read()
        if not ok:
            break

        frame = cv2.flip(frame, 1)
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        now = time.time() - start_time
        result = landmarker.detect_for_video(mp_image, int(now * 1000))

        lines = ["NO POSE"]
        if result.pose_landmarks:
            landmarks = result.pose_landmarks[0]
            state = recognizer.update(landmarks, now)

            level_parts = []
            if state["squat"]:
                level_parts.append("SQUAT")
            if state["lateral"]:
                level_parts.append(f"MOVE-{state['lateral']}")
            if state["guard_up"]:
                level_parts.append("GUARD")
            lines = [
                "level: " + (" + ".join(level_parts) if level_parts else "IDLE"),
                "event: " + (" / ".join(state["events"]) if state["events"] else "-"),
            ]

            h, w = frame.shape[:2]
            marker_color = {
                SWORD_WRIST: (0, 0, 255),      # 검 손목: 빨강
                SHIELD_WRIST: (255, 0, 0),     # 방패 손목: 파랑
            }
            for i, lm in enumerate(landmarks):
                color = marker_color.get(i, (0, 255, 0))
                cv2.circle(frame, (int(lm.x * w), int(lm.y * h)), 4, color, -1)

        for i, line in enumerate(lines):
            cv2.putText(
                frame, line, (20, 40 + i * 30),
                cv2.FONT_HERSHEY_SIMPLEX, 0.75, (0, 0, 255), 2,
            )
        cv2.putText(
            frame, "[s]=stand-calib [q]=quit", (20, frame.shape[0] - 20),
            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1,
        )
        cv2.imshow("mediapipe_only_mvp", frame)

        key = cv2.waitKey(1) & 0xFF
        if key == ord("q"):
            break
        elif key == ord("s") and result.pose_landmarks:
            recognizer.calibrate(result.pose_landmarks[0])

    cap.release()
    cv2.destroyAllWindows()
    landmarker.close()


if __name__ == "__main__":
    main()
