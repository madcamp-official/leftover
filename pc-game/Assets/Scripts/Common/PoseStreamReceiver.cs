// vision-server(Python, MediaPipe)가 UDP:9100으로 보내는 연속 포즈 프레임을 받아
// PoseInputHub에 그대로 꽂아준다. 와이어 포맷은 shared/PROTOCOL.md 참고.
//
// 백그라운드 스레드에서 소켓을 읽고 큐에 쌓기만 하고, 실제 PoseInputHub.ApplyFrame() 호출은
// Update()에서 메인 스레드로 한 번에 처리한다 (Unity API는 메인 스레드에서만 호출 가능).

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class PoseStreamReceiver : MonoBehaviour
{
    public int listenPort = 9100;

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
            Debug.LogWarning($"[PoseStream] UDP {listenPort} 바인딩 실패, vision-server 입력 비활성화: {e.Message}");
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
            catch (SocketException) { /* 종료 중 */ }
            catch (Exception e) { Debug.LogWarning($"[PoseStream] recv error: {e.Message}"); }
        }
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        if (hub == null) return;

        // 쌓인 패킷을 전부 순서대로 적용한다. 같은 플레이어의 나중 프레임이 앞 프레임을
        // 덮어쓰므로 결과적으로는 각 플레이어의 최신 값만 남는다.
        //
        // 마지막 패킷 하나만 적용하는 최적화를 쓰면 안 된다: 온라인 모드(플레이어 1명당
        // vision-server 1개)에서는 p1 패킷과 p2 패킷이 서로 다른 소스에서 번갈아 도착하기
        // 때문에, 마지막 하나만 취하면 다른 플레이어의 프레임이 통째로 버려져서 그쪽이
        // stale timeout에 걸려 계속 추적 끊김 상태가 된다.
        while (_incoming.TryDequeue(out string json))
        {
            FramePayload frame = null;
            try
            {
                frame = JsonUtility.FromJson<FramePayload>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PoseStream] JSON 파싱 실패: {e.Message}");
            }

            if (frame != null)
                hub.ApplyFrame(frame);
        }
    }

    private void OnDestroy()
    {
        _running = false;
        _udp?.Close();
        _recvThread?.Join(200);
    }
}
