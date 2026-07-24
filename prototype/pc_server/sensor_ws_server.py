"""
Day1 프로토타입 핵심: 폰 <-> PC 센서 스트리밍 + NTP 스타일 시간 동기화.

프로토콜 (기획서 3-1과 동일한 핑퐁 방식):
    1. PC가 폰에 {"type":"ping","t1": PC시각} 전송
    2. 폰이 수신 시각 t2 기록, 응답 직전 시각 t3 기록 후
       {"type":"pong","t1":..,"t2":..,"t3":..} 응답
    3. PC가 수신 시각 t4 기록
    4. RTT   = (t4 - t1) - (t3 - t2)
       offset = ((t2 - t1) + (t3 - t4)) / 2   (phone_clock - pc_clock, ms)
    -> 이후 폰이 보내는 센서 timestamp(t)에서 offset을 빼면 PC 기준 시각으로 환산 가능:
       pc_equivalent = t - offset

    이 라운드를 접속 시 20회 반복해서 offset의 중앙값을 채택한다 (기획서 권장대로).

센서 스트림:
    폰이 devicemotion 이벤트마다 {"type":"sensor","t":.., "ax":,"ay":,"az":,
    "gx":,"gy":,"gz":} 를 보낸다. 여기서는 우선 "제대로 도착하는지, 지연이 얼마나
    되는지" 검증이 목적이라 콘솔에 주기적으로 요약 로그만 찍는다. 실제 스윙/찌르기
    분류(Tier 1 임계값 로직)는 다음 단계에서 이 스트림 위에 얹으면 된다.

실행: python sensor_ws_server.py
"""

import asyncio
import ctypes
import json
import pathlib
import ssl
import statistics
import time

import websockets

PORT = 8765
CERT_DIR = pathlib.Path(__file__).parent / "certs"
SYNC_ROUNDS = 20

# 콘솔이 100Hz 로그로 도배되지 않도록 요약 출력 주기
LOG_INTERVAL_SEC = 0.5

# --- 실험: 자이로 각속도로 실제 OS 마우스 커서 움직이기 ---
# 절대 각도를 적분하지 않고 "각속도(deg/s) -> 커서 속도(px/s)"로 바로 매핑한다.
# (절대 적분은 드리프트가 누적되지만, 이 방식은 폰을 멈추면 커서도 바로 멈춘다.)
#
# 축 기준: 실측으로 확정한 매핑. gx(alpha) -> 커서 Y(그대로), gz(gamma) -> 커서 X(반대).
# app.js 매핑: gx=alpha, gy=beta, gz=gamma
user32 = ctypes.windll.user32
SCREEN_W = user32.GetSystemMetrics(0)
SCREEN_H = user32.GetSystemMetrics(1)
CURSOR_SENSITIVITY = 12.0  # px per (deg/s * s) = px per degree rotated
MAX_DT = 0.05  # 화면꺼짐 등으로 큰 gap 생겼을 때 순간이동 방지

SIGN_X = -1
SIGN_Y = -1

# 정지 상태에서도 자이로가 완전히 0이 아닌 바이어스(드리프트 원인)가 있다.
# 접속 직후 짧게 이 값을 샘플링해서 0점으로 잡아준다 -> "보정".
CALIB_SAMPLES = 40  # ~60Hz 기준 약 0.7초

# 폰이 보내는 매 프레임 값을 그대로 쓰면 네트워크 지터 때문에 커서가 끊겨 보인다.
# 지수이동평균(EMA)으로 살짝 스무딩. 값이 클수록(1에 가까울수록) 원값에 가깝고 반응 빠름/더 끊김.
SMOOTH_ALPHA = 0.35


def now_ms() -> float:
    return time.time() * 1000.0


async def run_time_sync(ws) -> float:
    """접속 직후 20회 핑퐁으로 offset(ms)의 중앙값을 구한다."""
    offsets = []
    rtts = []
    for i in range(SYNC_ROUNDS):
        t1 = now_ms()
        await ws.send(json.dumps({"type": "ping", "t1": t1}))
        raw = await ws.recv()
        t4 = now_ms()
        msg = json.loads(raw)
        if msg.get("type") != "pong":
            # 센서 데이터가 핑퐁 사이에 끼어들어올 수 있으니 무시하고 다음 메시지 기다림
            continue
        t2 = msg["t2"]
        t3 = msg["t3"]
        rtt = (t4 - t1) - (t3 - t2)
        offset = ((t2 - t1) + (t3 - t4)) / 2
        offsets.append(offset)
        rtts.append(rtt)

    offset_med = statistics.median(offsets) if offsets else 0.0
    rtt_med = statistics.median(rtts) if rtts else float("nan")
    print(
        f"[sync] rounds={len(offsets)}/{SYNC_ROUNDS} "
        f"offset_median={offset_med:.1f}ms rtt_median={rtt_med:.1f}ms"
    )
    return offset_med


async def handle_client(ws):
    peer = ws.remote_address
    print(f"[conn] phone connected from {peer}")

    offset = await run_time_sync(ws)

    count = 0
    last_log = time.monotonic()
    latencies = []

    cursor_x, cursor_y = SCREEN_W / 2, SCREEN_H / 2
    user32.SetCursorPos(int(cursor_x), int(cursor_y))
    last_motion_t = time.monotonic()

    calib_samples = []
    bias_x, bias_z = 0.0, 0.0
    calibrated = False
    smooth_gx, smooth_gz = 0.0, 0.0
    print(f"[calib] 폰을 평평한 곳에 가만히 두세요 ({CALIB_SAMPLES}샘플 수집 중)...")

    async for raw in ws:
        msg = json.loads(raw)
        if msg.get("type") != "sensor":
            continue

        t_phone = msg["t"]
        t_pc_equiv = t_phone - offset
        one_way_latency = now_ms() - t_pc_equiv  # 대략치 (네트워크 지연 + 처리 지연)
        latencies.append(one_way_latency)
        count += 1

        now_mono = time.monotonic()
        dt = min(now_mono - last_motion_t, MAX_DT)
        last_motion_t = now_mono

        gx = msg.get("gx", 0.0)  # yaw rate  (rotationRate.alpha, deg/s) -> 커서 Y (실측)
        gz = msg.get("gz", 0.0)  # roll rate (rotationRate.gamma, deg/s) -> 커서 X (실측, 반대)

        if not calibrated:
            calib_samples.append((gx, gz))
            if len(calib_samples) >= CALIB_SAMPLES:
                bias_x = sum(s[0] for s in calib_samples) / len(calib_samples)
                bias_z = sum(s[1] for s in calib_samples) / len(calib_samples)
                calibrated = True
                print(f"[calib] 완료. bias_yaw={bias_x:+.2f}deg/s bias_roll={bias_z:+.2f}deg/s")
            continue  # 캘리브레이션 중에는 커서를 움직이지 않음

        smooth_gx = SMOOTH_ALPHA * (gx - bias_x) + (1 - SMOOTH_ALPHA) * smooth_gx
        smooth_gz = SMOOTH_ALPHA * (gz - bias_z) + (1 - SMOOTH_ALPHA) * smooth_gz

        cursor_x = max(0, min(SCREEN_W - 1, cursor_x + SIGN_X * smooth_gz * dt * CURSOR_SENSITIVITY))
        cursor_y = max(0, min(SCREEN_H - 1, cursor_y + SIGN_Y * smooth_gx * dt * CURSOR_SENSITIVITY))
        user32.SetCursorPos(int(cursor_x), int(cursor_y))

        if time.monotonic() - last_log >= LOG_INTERVAL_SEC:
            hz = count / LOG_INTERVAL_SEC
            avg_lat = sum(latencies) / len(latencies) if latencies else 0.0
            print(
                f"[sensor] {hz:5.1f}Hz  "
                f"acc=({msg.get('ax', 0):+.2f},{msg.get('ay', 0):+.2f},{msg.get('az', 0):+.2f})  "
                f"gyro=({msg.get('gx', 0):+.1f},{msg.get('gy', 0):+.1f},{msg.get('gz', 0):+.1f})  "
                f"latency~{avg_lat:.1f}ms"
            )
            count = 0
            latencies.clear()
            last_log = time.monotonic()

    print(f"[conn] phone disconnected {peer}")


async def main():
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(
        certfile=str(CERT_DIR / "cert.pem"),
        keyfile=str(CERT_DIR / "key.pem"),
    )
    async with websockets.serve(handle_client, "0.0.0.0", PORT, ssl=ctx):
        print(f"[wss] listening on wss://0.0.0.0:{PORT}")
        await asyncio.Future()  # run forever


if __name__ == "__main__":
    asyncio.run(main())
