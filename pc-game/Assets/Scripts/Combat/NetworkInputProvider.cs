// Phase 2 입력 소스 — MediaPipe(vision-server, Python)가 UDP:9002로 보내는 인식 이벤트를
// 받아 CombatInputHub에 그대로 꽂아준다. 와이어 포맷은 shared/PROTOCOL.md "Phase 1 이벤트
// 프로토콜" 참고 (문서상 이름은 Phase 1이지만, Unity 입력 소스 기준으로는 두 번째로 붙이는
// 것이라 파일명은 NetworkInputProvider).
//
// KeyboardInputProvider 대신 이 컴포넌트를 활성화하면 게임 로직은 한 줄도 안 건드리고
// 입력 소스만 교체된다.

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class NetworkInputProvider : MonoBehaviour
{
    public int listenPort = 9002;

    [Serializable]
    private class ActionMessage
    {
        public string action;
        public bool active;
        public string position; // "left" | "right" | "center"
    }

    private UdpClient _udp;
    private Thread _recvThread;
    private volatile bool _running;
    private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();

    private void Start()
    {
        _udp = new UdpClient(listenPort);
        _running = true;
        _recvThread = new Thread(ReceiveLoop) { IsBackground = true };
        _recvThread.Start();
    }

    private void ReceiveLoop()
    {
        var endpoint = new IPEndPoint(IPAddress.Any, listenPort);
        while (_running)
        {
            try
            {
                byte[] data = _udp.Receive(ref endpoint);
                _incoming.Enqueue(System.Text.Encoding.UTF8.GetString(data));
            }
            catch (SocketException) { /* shutting down */ }
            catch (Exception e) { Debug.LogWarning($"[NetworkInput] recv error: {e.Message}"); }
        }
    }

    private void Update()
    {
        var hub = CombatInputHub.Instance;
        if (hub == null) return;

        while (_incoming.TryDequeue(out var json))
        {
            var msg = JsonUtility.FromJson<ActionMessage>(json);
            if (msg == null) continue;

            switch (msg.action)
            {
                case "swing_horizontal": hub.RaiseSwingHorizontal(); break;
                case "swing_vertical": hub.RaiseSwingVertical(); break;
                case "kick": hub.RaiseKick(); break;
                case "parry": hub.RaiseParry(); break;
                case "guard": hub.SetGuarding(msg.active); break;
                case "crouch": hub.SetCrouching(msg.active); break;
                case "lateral":
                    LateralPosition pos = msg.position == "left" ? LateralPosition.Left
                                         : msg.position == "right" ? LateralPosition.Right
                                         : LateralPosition.Center;
                    hub.SetLateralPosition(pos);
                    break;
                default:
                    Debug.LogWarning($"[NetworkInput] unknown action: {msg.action}");
                    break;
            }
        }
    }

    private void OnDestroy()
    {
        _running = false;
        _udp?.Close();
        _recvThread?.Join(200);
    }
}
