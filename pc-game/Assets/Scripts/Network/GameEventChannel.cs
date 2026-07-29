// TCP 기반 게임 이벤트 채널 (설계 문서 5장) - 원시 pose UDP 스트림과 완전히 분리된 별도
// 소켓으로, 이산 게임 이벤트를 JSON 한 줄 + '\n' 구분으로 주고받는다.
// 수신은 백그라운드 스레드에서 큐에만 쌓고, Unity API를 만지는 콜백은 반드시 메인 스레드
// (Poll 호출자)에서 실행한다 - Unity 객체는 메인 스레드 밖에서 건드리면 안 되기 때문.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public sealed class GameEventChannel
{
    public const int DefaultPort = 9200;

    private TcpListener _listener;
    private TcpClient _client;
    private NetworkStream _stream;
    private Thread _bgThread;
    private readonly object _writeLock = new object();
    private readonly ConcurrentQueue<NetworkEvent> _incoming = new ConcurrentQueue<NetworkEvent>();
    private volatile bool _connected;
    private volatile bool _stopping;

    public bool IsConnected => _connected;
    public string RemoteAddress { get; private set; } = "";

    public event Action OnConnected;
    public event Action OnDisconnected;
    // 접속 실패 사유를 UI로 올린다. 이게 없으면 클라이언트는 "접속 시도 중..."에서 영원히
    // 멈춘 것처럼 보여서 방화벽 문제인지 IP 오타인지 구분할 수 없다.
    public event Action<string> OnError;

    // TcpClient.Connect는 방화벽이 패킷을 조용히 버리면 OS 기본 타임아웃까지(20초 이상)
    // 블록된다. 그만큼 "접속 중" 상태로 방치되면 사용자가 뭐가 잘못됐는지 알 수 없으므로
    // 명시적으로 짧게 끊고 사유를 표시한다.
    private const int ConnectTimeoutMs = 5000;

    public void StartHost(int port = DefaultPort)
    {
        Stop();
        _stopping = false;
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
        }
        catch (Exception e)
        {
            // 포트를 이미 다른 프로세스가 쓰고 있는 경우 등 - 조용히 실패하면 호스트가
            // 대기 중인 줄 알고 계속 기다리게 된다.
            _incoming.Enqueue(new NetworkEvent { type = "__error", json = $"호스트 시작 실패: {e.Message}" });
            return;
        }
        _bgThread = new Thread(AcceptLoop) { IsBackground = true };
        _bgThread.Start();
    }

    public void StartClient(string hostAddress, int port = DefaultPort)
    {
        Stop();
        _stopping = false;
        _bgThread = new Thread(() => ConnectLoop(hostAddress, port)) { IsBackground = true };
        _bgThread.Start();
    }

    private void AcceptLoop()
    {
        // 한 번만 accept하면 클라이언트가 한 번 끊긴 뒤로는 재접속을 영원히 못 받는다
        // (AttachClient가 ReadLoop에서 끊길 때까지 블록하므로, 그 뒤 다시 accept로 돌아와야 한다).
        while (!_stopping)
        {
            try
            {
                TcpClient client = _listener.AcceptTcpClient();
                AttachClient(client); // 끊길 때까지 여기서 블록된다
            }
            catch (Exception)
            {
                // Stop()이 리스너를 닫으면 AcceptTcpClient가 예외로 빠져나온다 - 정상 종료 경로.
                break;
            }
        }
    }

    private void ConnectLoop(string hostAddress, int port)
    {
        try
        {
            var client = new TcpClient();
            if (!client.ConnectAsync(hostAddress, port).Wait(ConnectTimeoutMs))
            {
                client.Close();
                _incoming.Enqueue(new NetworkEvent
                {
                    type = "__error",
                    json = $"{hostAddress}:{port} 접속 시간 초과 - 호스트가 '호스트로 시작'을 눌렀는지, " +
                           "호스트 PC 방화벽이 TCP 포트를 막고 있지 않은지 확인하세요.",
                });
                return;
            }
            AttachClient(client);
        }
        catch (Exception e)
        {
            _incoming.Enqueue(new NetworkEvent { type = "__error", json = $"접속 실패: {e.Message}" });
        }
    }

    private void AttachClient(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = _client.GetStream();
        RemoteAddress = client.Client.RemoteEndPoint is IPEndPoint ep ? ep.Address.ToString() : "";
        _connected = true;
        _incoming.Enqueue(new NetworkEvent { type = "__connected" });
        ReadLoop();
    }

    private void ReadLoop()
    {
        try
        {
            var reader = new StreamReader(_stream, Encoding.UTF8);
            string line;
            while (!_stopping && (line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                NetworkEvent evt;
                try { evt = JsonUtility.FromJson<NetworkEvent>(line); }
                catch { continue; }
                if (evt != null) _incoming.Enqueue(evt);
            }
        }
        catch (Exception)
        {
            // 소켓 종료/네트워크 끊김 - finally에서 끊김 이벤트를 큐에 넣는다.
        }
        finally
        {
            if (_connected)
            {
                _connected = false;
                _incoming.Enqueue(new NetworkEvent { type = "__disconnected" });
            }
        }
    }

    public void Send(NetworkEvent evt)
    {
        if (!_connected || _stream == null) return;
        try
        {
            string line = JsonUtility.ToJson(evt) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            lock (_writeLock)
            {
                _stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameEventChannel] 전송 실패: {e.Message}");
        }
    }

    // 메인 스레드에서 매 프레임(NetworkSession.Update) 호출 - 큐에 쌓인 이벤트를 흘려보낸다.
    public void Poll(Action<NetworkEvent> onEvent)
    {
        while (_incoming.TryDequeue(out NetworkEvent evt))
        {
            if (evt.type == "__connected") { OnConnected?.Invoke(); continue; }
            if (evt.type == "__disconnected") { OnDisconnected?.Invoke(); continue; }
            if (evt.type == "__error") { OnError?.Invoke(evt.json); continue; }
            onEvent?.Invoke(evt);
        }
    }

    public void Stop()
    {
        _stopping = true;
        _connected = false;
        try { _listener?.Stop(); } catch { }
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _listener = null;
        _client = null;
        _stream = null;
    }
}
