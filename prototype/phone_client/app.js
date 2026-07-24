// 폰 브라우저에서 돌아가는 센서 스트리머.
// - PC의 sensor_ws_server.py에 wss:// 로 접속
// - 서버가 보내는 {"type":"ping"} 에 즉시 {"type":"pong"} 으로 응답 (시간 동기화)
// - devicemotion 이벤트를 그대로 {"type":"sensor"} 로 스트리밍

// 같은 PC가 https://<ip>:8443 로 이 페이지를 서빙하므로, WS 포트만 8765로 바꿔서 접속.
const WS_PORT = 8765;
const wsUrl = `wss://${location.hostname}:${WS_PORT}`;

const statusEl = document.getElementById("status");
const wsStateEl = document.getElementById("wsState");
const offsetEl = document.getElementById("offsetVal");
const hzEl = document.getElementById("hzVal");
const accEl = document.getElementById("accVal");
const gyroEl = document.getElementById("gyroVal");
const btn = document.getElementById("connectBtn");

let ws = null;
let sentCount = 0;
let lastHzLog = performance.now();
let latestOffset = null;

function setStatus(text) {
  statusEl.textContent = text;
}

async function requestMotionPermission() {
  // iOS 13+ : 반드시 사용자 제스처(버튼 클릭) 안에서 호출해야 permission 프롬프트가 뜬다.
  if (typeof DeviceMotionEvent !== "undefined" && typeof DeviceMotionEvent.requestPermission === "function") {
    const res = await DeviceMotionEvent.requestPermission();
    if (res !== "granted") {
      throw new Error("motion permission denied");
    }
  }
  // Android Chrome 등은 requestPermission 자체가 없고 바로 이벤트가 온다.
}

function connectWs() {
  ws = new WebSocket(wsUrl);
  wsStateEl.textContent = "connecting...";

  ws.onopen = () => {
    wsStateEl.textContent = "connected";
    setStatus("연결됨. 시간 동기화 진행 중...");
  };

  ws.onclose = () => {
    wsStateEl.textContent = "closed";
    setStatus("연결 끊김. 새로고침 후 다시 시도하세요.");
  };

  ws.onerror = () => {
    wsStateEl.textContent = "error";
    setStatus(
      "WS 연결 실패.\n" +
      "1) https 경고 페이지에서 인증서를 먼저 신뢰했는지 확인\n" +
      "   (같은 포트가 아니라 8765 포트라 브라우저가 별도로 경고를 띄울 수 있음.\n" +
      "    새 탭에서 https://" + location.hostname + ":" + WS_PORT + " 를 직접 열어 인증서를 수락한 뒤 다시 시도)\n" +
      "2) PC와 같은 와이파이인지 확인"
    );
  };

  ws.onmessage = (evt) => {
    const msg = JSON.parse(evt.data);
    if (msg.type === "ping") {
      const t2 = Date.now();
      const t3 = Date.now();
      ws.send(JSON.stringify({ type: "pong", t1: msg.t1, t2, t3 }));
    }
  };
}

function onMotion(e) {
  if (!ws || ws.readyState !== WebSocket.OPEN) return;

  const acc = e.acceleration || { x: 0, y: 0, z: 0 };
  const rot = e.rotationRate || { alpha: 0, beta: 0, gamma: 0 };

  const payload = {
    type: "sensor",
    t: Date.now(),
    ax: acc.x || 0,
    ay: acc.y || 0,
    az: acc.z || 0,
    gx: rot.alpha || 0,
    gy: rot.beta || 0,
    gz: rot.gamma || 0,
  };
  ws.send(JSON.stringify(payload));

  sentCount++;
  const now = performance.now();
  if (now - lastHzLog > 500) {
    const hz = (sentCount / ((now - lastHzLog) / 1000)).toFixed(1);
    hzEl.textContent = `${hz} Hz`;
    accEl.textContent = `${payload.ax.toFixed(2)}, ${payload.ay.toFixed(2)}, ${payload.az.toFixed(2)}`;
    gyroEl.textContent = `${payload.gx.toFixed(1)}, ${payload.gy.toFixed(1)}, ${payload.gz.toFixed(1)}`;
    sentCount = 0;
    lastHzLog = now;
  }
}

btn.addEventListener("click", async () => {
  btn.disabled = true;
  try {
    await requestMotionPermission();
    connectWs();
    window.addEventListener("devicemotion", onMotion);
    setStatus("센서 스트리밍 중...");
  } catch (err) {
    setStatus("에러: " + err.message);
    btn.disabled = false;
  }
});
