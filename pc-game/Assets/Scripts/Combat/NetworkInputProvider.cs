// MediaPipe(vision-server, Python)가 UDP:9002로 보내는 인식 이벤트를 받아
// CombatInputHub에 그대로 꽂아준다. 와이어 포맷은 shared/PROTOCOL.md "Phase 1 이벤트
// 프로토콜" 참고.
//
// BossDuelPrototype.ConnectInput()이 Play 시작 시 KeyboardInputProvider와 함께 자동으로
// 붙여준다 — vision-server(main.py --pc-ip 127.0.0.1)를 켜두면 별도 씬 설정 없이 바로
// 웹캠 모션으로 플레이할 수 있고, vision-server를 안 켜면 그냥 키보드로만 동작한다.

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
        try
        {
            _udp = new UdpClient(listenPort);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkInput] UDP {listenPort} 바인딩 실패, MediaPipe 입력 비활성화(키보드는 계속 동작): {e.Message}");
            enabled = false;
            return;
        }

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
