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

        // 한 프레임에 여러 개가 쌓여 있어도 최신 것만 반영하면 되므로, 큐를 비우면서
        // 마지막으로 파싱에 성공한 것만 적용한다 (오래된 프레임을 굳이 순서대로 처리할
        // 필요 없음 - 위치 스트림은 최신 값이 항상 이전 값을 덮어써도 무방).
        string latest = null;
        while (_incoming.TryDequeue(out string json))
            latest = json;

        if (latest == null) return;

        FramePayload frame = null;
        try
        {
            frame = JsonUtility.FromJson<FramePayload>(latest);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PoseStream] JSON 파싱 실패: {e.Message}");
        }

        if (frame != null)
            hub.ApplyFrame(frame);
    }

    private void OnDestroy()
    {
        _running = false;
        _udp?.Close();
        _recvThread?.Join(200);
    }
}
