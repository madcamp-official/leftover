"""
검(찌르기) / 방패(패링) 를 웹캠 대신 폰 IMU로 인식해보는 실험.

mediapipe_only_mvp에서 찌르기/패링 인식이 잘 안 됐다 (손목 궤적만으로는 구분이 애매함).
기획서 원안대로 이 두 동작은 폰 센서 쪽이 맞는 선택인지 검증한다 (3-4 참고: "타이밍이
예민한 방어/패링은 응답이 빠른 폰 IMU에").

이벤트 이름은 shared/PROTOCOL.md "4. 모션 분류 결과 이벤트 이름" 표준을 따른다.

분류 로직 (Tier 1: 임계값 기반):
  - 찌르기(thrust): 가속도 크기(중력 제외한 순수 가속도, |a| = sqrt(ax^2+ay^2+az^2))가
    짧은 시간 안에 크게 튀는데, 그 순간 자이로 크기(|g|)는 낮다 ("회전 없이 직선으로만
    가속" — 기획서 2장 표 그대로).
  - 패링(parry): 자이로+가속도가 둘 다 낮은(=거의 정지) 상태가 일정 시간 유지되다가
    (방어 자세로 들고 있는 상태), 그 직후 자이로 크기가 급격히 튀면 패링으로 판정
    ("가만히 뒀다가 바깥으로 스윙").

지금은 폰 한 대로 두 로직을 다 테스트한다 (실제로는 검 폰=찌르기, 방패 폰=패링으로
다른 물리 폰이 됨 — PROTOCOL.md 참고). 폰을 손에 쥐고 직접 찌르기/패링 동작을 해보면서
아래 임계값을 실측 조정하면 된다.

실행: python sword_shield_classifier.py (phone_client에서 접속하는 방법은
prototype/README.md 참고 — https_static_server.py도 같이 띄워야 함)
"""

import asyncio
import json
import math
import pathlib
import ssl
import statistics
import time

import websockets

PORT = 8765
CERT_DIR = pathlib.Path(__file__).parent / "certs"
SYNC_ROUNDS = 20
LOG_INTERVAL_SEC = 0.5

# --- 찌르기(thrust) ---
THRUST_ACCEL_THRESHOLD = 8.0   # m/s^2. 이 이상으로 |가속도|가 튀면 후보 (실측 후 조정)
THRUST_GYRO_CAP = 120.0        # deg/s. 그 순간 |각속도|가 이 이하여야 "회전 없음"으로 인정
THRUST_COOLDOWN_SEC = 0.4

# --- 패링(parry) ---
STILL_ACCEL_MAX = 2.0          # m/s^2. 이 이하면 "정지"로 간주
STILL_GYRO_MAX = 40.0          # deg/s. 이 이하면 "정지"로 간주
PARRY_HOLD_SEC = 0.25          # 이 시간 이상 연속으로 정지 상태여야 "방어 중"으로 인정(armed)
PARRY_GYRO_THRESHOLD = 200.0   # deg/s. armed 상태에서 |각속도|가 이 이상 튀면 패링
PARRY_COOLDOWN_SEC = 0.5


def now_ms() -> float:
    return time.time() * 1000.0


def vec_mag(x, y, z) -> float:
    return math.sqrt(x * x + y * y + z * z)


async def run_time_sync(ws) -> float:
    """접속 직후 20회 핑퐁으로 offset(ms)의 중앙값을 구한다 (sensor_ws_server.py와 동일)."""
    offsets, rtts = [], []
    for _ in range(SYNC_ROUNDS):
        t1 = now_ms()
        await ws.send(json.dumps({"type": "ping", "t1": t1}))
        raw = await ws.recv()
        t4 = now_ms()
        msg = json.loads(raw)
        if msg.get("type") != "pong":
            continue
        t2, t3 = msg["t2"], msg["t3"]
        rtts.append((t4 - t1) - (t3 - t2))
        offsets.append(((t2 - t1) + (t3 - t4)) / 2)

    offset_med = statistics.median(offsets) if offsets else 0.0
    rtt_med = statistics.median(rtts) if rtts else float("nan")
    print(f"[sync] rounds={len(offsets)}/{SYNC_ROUNDS} offset_median={offset_med:.1f}ms rtt_median={rtt_med:.1f}ms")
    return offset_med


class SwordShieldClassifier:
    def __init__(self):
        self.last_thrust_t = -999.0
        self.still_since = None
        self.armed = False
        self.last_parry_t = -999.0

    def update(self, t: float, accel_mag: float, gyro_mag: float):
        """한 샘플 처리. 발생한 이벤트 이름(또는 None)을 리턴."""
        # 찌르기: 가속도 스파이크 + 그 순간 회전은 낮음
        if (
            accel_mag > THRUST_ACCEL_THRESHOLD
            and gyro_mag < THRUST_GYRO_CAP
            and t - self.last_thrust_t > THRUST_COOLDOWN_SEC
        ):
            self.last_thrust_t = t
            self.armed = False  # 찌르기 도중엔 패링 armed 상태 리셋
            self.still_since = None
            return "thrust"

        # 패링: 정지 유지 -> armed -> 급격한 회전
        is_still = accel_mag < STILL_ACCEL_MAX and gyro_mag < STILL_GYRO_MAX
        if is_still:
            if self.still_since is None:
                self.still_since = t
            elif not self.armed and t - self.still_since >= PARRY_HOLD_SEC:
                self.armed = True
        else:
            if self.armed and gyro_mag > PARRY_GYRO_THRESHOLD and t - self.last_parry_t > PARRY_COOLDOWN_SEC:
                self.last_parry_t = t
                self.armed = False
                self.still_since = None
                return "parry"
            # 정지가 깨졌는데 패링 임계값엔 못 미치면 다시 정지부터 재시작
            self.still_since = None
            self.armed = False

        return None


async def handle_client(ws):
    peer = ws.remote_address
    print(f"[conn] phone connected from {peer}")

    offset = await run_time_sync(ws)
    classifier = SwordShieldClassifier()

    count = 0
    last_log = time.monotonic()
    start = time.monotonic()

    async for raw in ws:
        msg = json.loads(raw)
        if msg.get("type") != "sensor":
            continue

        t_phone = msg["t"]
        t_pc_equiv = (t_phone - offset) / 1000.0  # 초 단위로 통일
        accel_mag = vec_mag(msg.get("ax", 0.0), msg.get("ay", 0.0), msg.get("az", 0.0))
        gyro_mag = vec_mag(msg.get("gx", 0.0), msg.get("gy", 0.0), msg.get("gz", 0.0))
        count += 1

        event = classifier.update(t_pc_equiv, accel_mag, gyro_mag)
        if event == "thrust":
            print(f"[event] 찌르기(thrust)  |a|={accel_mag:.1f} |g|={gyro_mag:.1f}")
        elif event == "parry":
            print(f"[event] 패링(parry)    |a|={accel_mag:.1f} |g|={gyro_mag:.1f}")

        if time.monotonic() - last_log >= LOG_INTERVAL_SEC:
            hz = count / (time.monotonic() - last_log)
            print(f"[sensor] {hz:5.1f}Hz  |a|={accel_mag:5.2f}  |g|={gyro_mag:6.1f}  armed={classifier.armed}")
            count = 0
            last_log = time.monotonic()

    print(f"[conn] phone disconnected {peer}")


async def main():
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(certfile=str(CERT_DIR / "cert.pem"), keyfile=str(CERT_DIR / "key.pem"))
    async with websockets.serve(handle_client, "0.0.0.0", PORT, ssl=ctx):
        print(f"[wss] listening on wss://0.0.0.0:{PORT}")
        print("찌르기: 폰을 앞으로 쭉 내지르기. 패링: 폰을 잠깐 가만히 뒀다가 바깥으로 휙 스윙.")
        await asyncio.Future()


if __name__ == "__main__":
    asyncio.run(main())
